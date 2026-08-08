using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;
using DinoDigger.Managers;
using DinoDigger.Overworld;

namespace DinoDigger.Testing
{
    /// <summary>
    /// Dino Town integration cases: the economy/build-queue and the builder NPC loop +
    /// celebration, plus the HARD-RULE case that proves town construction never
    /// commandeers the player backhoe or a walk buddy. Phase 2 adds the nine-plot
    /// curated price curve (<see cref="Case_PriceCurveOrdersBuilds"/>) and the
    /// growth-stage build dividend (<see cref="Case_BigDinoBuildsFaster"/>).
    ///
    /// SceneBuilder ships a live, wired town (TownController + a 9-plot TownArea on the
    /// "Town" root, wired into GameManager._town) — <see cref="Case_TownWiredInScene"/>
    /// proves that directly. The behavioural cases below stay robust either way:
    /// <see cref="EnsureTown"/> prefers the scene's town when present, and only falls back
    /// to building a small TownArea near the meadow + injecting a TownController when the
    /// district has not been placed. See IntegrationTestRunner.cs for the driver.
    /// </summary>
    public partial class IntegrationTestRunner
    {
        // =============================================== scene wiring (regression)

        // The BUILT scene must ship a live, wired Dino Town: SceneBuilder attaches a
        // TownController (with its 9-plot TownArea) to the "Town" root and strict-wires it
        // into GameManager._town. This asserts that BEFORE any test-side EnsureTown /
        // TestInstallTown runs — so it proves a real player's banked treasure would find a
        // town to build in, not just the self-installed test rig that once masked this gap.
        private IEnumerator Case_TownWiredInScene(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();

            TownController town = gm.TestTown;
            ctx.Assert(town != null,
                "scene ships no wired TownController (GameManager._town is null) — " +
                "SceneBuilder must build + wire the town");
            ctx.Assert(town.TestArea != null, "scene town has no TownArea wired");
            ctx.Assert(town.TestArea.PlotCount == 9,
                $"scene town has {town.TestArea.PlotCount} plots (expected 9)");

            // (DinoDigger-6or) The plaza layout, asserted structurally so a future re-layout
            // can't quietly re-crowd the town: (1) the FINALE plot — Fossil Fountain, the last
            // and dearest entry in the curated roster — crowns the district centre, the same
            // point the Town root and TownArea.Center sit on; (2) every other plot rings it at
            // a real distance; and (3) no two plots sit closer than a building is wide
            // (~2.2 world units, PlaceholderLibrary BuildingTargetW), so each keeps room for
            // its builder stand-points and a toddler-sized tap target.
            TownArea area = town.TestArea;
            int finale = area.PlotCount - 1;
            ctx.Assert((area.PlotWorld(finale) - area.Center).sqrMagnitude < 0.01f,
                $"the finale plot {finale} is not the district centre — Fossil Fountain must " +
                "crown the plaza");

            float closest = float.MaxValue;
            for (int i = 0; i < area.PlotCount; i++)
            {
                if (i != finale)
                {
                    float toCenter = (area.PlotWorld(i) - area.Center).magnitude;
                    ctx.Assert(toCenter > 1.5f,
                        $"plot {i} sits {toCenter:0.##}u from the centre finale plot (too close)");
                }

                for (int j = i + 1; j < area.PlotCount; j++)
                {
                    closest = Mathf.Min(closest, (area.PlotWorld(i) - area.PlotWorld(j)).magnitude);
                }
            }

            ctx.Assert(closest > 1.9f,
                $"the two closest plots are only {closest:0.##}u apart — buildings import ~2.2u " +
                "wide, so the town has no breathing room for stand-points or tap targets");

            ctx.Log("built scene ships a live TownController wired into GameManager._town with a " +
                    $"9-plot TownArea: eight plots ring the centre finale plot, closest pair {closest:0.##}u apart");
            yield break;
        }

        // ============================================== town state persistence (v4)

        // A saved town rebuilds on load (TownController.RestoreFromSave): finished
        // buildings reappear FINISHED with no crew and no confetti replay, a partially-
        // built site is restored to its construction state as the active site and resumes
        // accepting crew, and the queue index continues from where it left off. Old saves
        // with no town fields restore an empty town.
        private IEnumerator Case_TownStatePersists(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset(); // clears all dinos, so nothing gets drafted mid-check
            TownController town = EnsureTown(ctx);
            ctx.Assert(town.TestArea != null && town.TestArea.PlotCount >= 3,
                $"need >=3 plots for the persistence test (have {(town.TestArea != null ? town.TestArea.PlotCount : 0)})");

            int finished = 0;
            Action<int> onFin = _ => finished++;
            int started = 0;
            Action<int> onStart = _ => started++;
            GameEvents.BuildingFinished += onFin;
            GameEvents.TownBuildStarted += onStart;

            int savedNext = gm.Save.Data.TownNextIndex;
            List<TownBuildingSave> savedList = gm.Save.Data.TownBuildings;
            try
            {
                // Author a save: plots 0 and 1 finished, plot 2 mid-build at state 2.
                gm.Save.Data.TownNextIndex = 2;
                gm.Save.Data.TownBuildings = new List<TownBuildingSave>
                {
                    new TownBuildingSave { Finished = true, State = BuildingController.ConstructionStates },
                    new TownBuildingSave { Finished = true, State = BuildingController.ConstructionStates },
                    new TownBuildingSave { Finished = false, State = 2, Worked = 0f },
                };

                town.RestoreFromSave(gm.Save.Data);
                yield return ctx.WaitFrames(2);

                // Restoring must not replay build-start/finish events (no confetti).
                ctx.Assert(finished == 0, $"restore replayed BuildingFinished ({finished}x)");
                ctx.Assert(started == 0, $"restore fired TownBuildStarted ({started}x)");

                // The queue continues from the saved index...
                ctx.Assert(town.TestNextIndex == 2,
                    $"restored queue index {town.TestNextIndex} != 2");

                // ...the partial site is restored, active, at its saved construction state...
                ctx.Assert(town.TestActiveSite != null, "partial site not restored as the active site");
                ctx.Assert(town.TestActiveSite.State == 2,
                    $"restored active site at state {town.TestActiveSite.State} != 2");
                ctx.Assert(!town.TestActiveSite.IsFinished, "restored partial site is finished (should be mid-build)");

                // ...and three building objects exist (2 finished + 1 active).
                int buildings = town.transform.GetComponentsInChildren<BuildingController>(true).Length;
                ctx.Assert(buildings == 3, $"restored {buildings} building objects (expected 3)");

                // With no residents in the scene, no crew is drafted, so the site holds
                // its restored state (proving it resumes WAITING for crew, not auto-finishing).
                ctx.Assert(town.TestBuilderCount == 0, "a crew was drafted with no residents present");

                // A v3-style save (no town fields) restores an empty town.
                gm.Save.Data.TownNextIndex = 0;
                gm.Save.Data.TownBuildings = new List<TownBuildingSave>();
                town.RestoreFromSave(gm.Save.Data);
                yield return ctx.WaitFrames(2);
                ctx.Assert(town.TestActiveSite == null && town.TestNextIndex == 0,
                    "empty-town save did not restore an empty town");
                int after = town.transform.GetComponentsInChildren<BuildingController>(true).Length;
                ctx.Assert(after == 0, $"empty-town restore left {after} building objects");

                ctx.Log("town persistence: 2 finished + 1 state-2 site restored (no crew/confetti), " +
                        "queue continued at index 2; empty save restored an empty town");
            }
            finally
            {
                GameEvents.BuildingFinished -= onFin;
                GameEvents.TownBuildStarted -= onStart;
                gm.Save.Data.TownNextIndex = savedNext;
                gm.Save.Data.TownBuildings = savedList ?? new List<TownBuildingSave>();
                gm.TestReset();
            }
        }

        // ===================================================== 5li.1 economy/queue

        // Granting coins that clear the next building's price auto-starts a build with
        // ZERO player input: a site appears and the price is deducted from the wallet.
        private IEnumerator Case_CoinsAutoSpendStartsBuild(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            TownController town = EnsureTown(ctx);

            int price = gm.TestConfig.TownBuildingPrice(0);
            ctx.Assert(price > 0, $"town price[0] not positive ({price})");

            int started = 0;
            Action<int> onStart = _ => started++;
            GameEvents.TownBuildStarted += onStart;
            try
            {
                // Bank exactly the first building's price. No taps, no menus.
                gm.Save.Data.TreasureCount = price;

                yield return ctx.WaitUntil(() => town.TestActiveSite != null);

                ctx.Assert(started >= 1, "TownBuildStarted event never fired");
                ctx.Assert(gm.Save.Data.TreasureCount == 0,
                    $"price not deducted: wallet {gm.Save.Data.TreasureCount} (expected 0)");
                ctx.Assert(town.TestActiveSite.State == 0, "new site not at construction state 0");
                ctx.Log($"granting {price} coins auto-started a build; wallet drained to 0, site at state 0");
            }
            finally
            {
                GameEvents.TownBuildStarted -= onStart;
                gm.TestReset();
            }
        }

        // ============================================ 5li.2 states + builder loop

        // With a resident crew on site the building steps through construction states
        // 0 -> 1 -> 2 -> 3 -> finished (accelerated per-state timing).
        private IEnumerator Case_BuildAdvancesThroughStates(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            TownController town = EnsureTown(ctx);
            GameConfig cfg = gm.TestConfig;
            float saved = cfg.TownSecondsPerBuildState;

            var advanced = new List<int>();
            Action<int> onAdv = st => advanced.Add(st);
            GameEvents.BuildingStateAdvanced += onAdv;
            try
            {
                cfg.TownSecondsPerBuildState = 0.3f; // accelerate worked-time per state

                DinoController b1 = gm.TestSpawnDino(DinoType.TRex, GrowthStage.Big);
                DinoController b2 = gm.TestSpawnDino(DinoType.Stegosaurus, GrowthStage.Big);
                gm.TestMakeResident(b1, teleportIntoMeadow: true);
                gm.TestMakeResident(b2, teleportIntoMeadow: true);
                yield return ctx.WaitFrames(2);

                gm.Save.Data.TreasureCount = cfg.TownBuildingPrice(0);
                yield return ctx.WaitUntil(() => town.TestActiveSite != null);
                BuildingController site = town.TestActiveSite;

                // (DinoDigger-771) The "under construction" barrier sign is up while the site
                // builds. Only meaningful once the art is imported (null sprite = no sign).
                bool signOn = gm.TestLibrary != null && gm.TestLibrary.ConstructionSign != null;
                if (signOn)
                {
                    ctx.Assert(site.TestSignActive, "construction sign not shown while the site is building");
                }

                // The crew commutes then works; the state climbs to finished.
                yield return ctx.WaitUntil(() => site != null && site.IsFinished);

                ctx.Assert(advanced.Contains(1) && advanced.Contains(2) && advanced.Contains(3),
                    $"did not step through states 1..3 (saw: {Join(advanced)})");
                ctx.Assert(site.State == BuildingController.ConstructionStates,
                    $"final state {site.State} != finished ({BuildingController.ConstructionStates})");

                // ...and it pops away once the build finishes.
                if (signOn)
                {
                    yield return ctx.WaitUntil(() => !site.TestSignActive);
                    ctx.Assert(!site.TestSignActive, "construction sign persisted after the build finished");
                }

                ctx.Log($"crew advanced the build through states {Join(advanced)} to finished");
            }
            finally
            {
                GameEvents.BuildingStateAdvanced -= onAdv;
                cfg.TownSecondsPerBuildState = saved;
                gm.TestReset();
            }
        }

        // The drafted builders are NON-BUDDY residents that start in the meadow, leave
        // it, and reach the site to work (never a buddy or the backhoe).
        private IEnumerator Case_BuilderCommutesFromMeadow(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            MeadowArea meadow = gm.TestMeadow;
            ctx.Assert(meadow != null, "no MeadowArea in the scene");
            TownController town = EnsureTown(ctx);

            DinoController r1 = gm.TestSpawnDino(DinoType.TRex, GrowthStage.Big);
            DinoController r2 = gm.TestSpawnDino(DinoType.Triceratops, GrowthStage.Big);
            gm.TestMakeResident(r1, teleportIntoMeadow: true);
            gm.TestMakeResident(r2, teleportIntoMeadow: true);
            yield return ctx.WaitFrames(2);
            ctx.Assert(!r1.IsBuddy && !r2.IsBuddy, "test builders are not residents");
            ctx.Assert(meadow.ContainsInterior(r1.transform.position) &&
                       meadow.ContainsInterior(r2.transform.position),
                "builders did not start inside the meadow");

            gm.Save.Data.TreasureCount = gm.TestConfig.TownBuildingPrice(0);
            yield return ctx.WaitUntil(() => town.TestActiveSite != null);

            // A crew is drafted, and it is residents only.
            yield return ctx.WaitUntil(() => town.TestBuilderCount > 0);
            IReadOnlyList<DinoController> crew = town.TestBuilders;
            for (int i = 0; i < crew.Count; i++)
            {
                ctx.Assert(crew[i] != null && !crew[i].IsBuddy,
                    "a drafted builder is a buddy (town must use non-buddy residents only)");
            }

            // (DinoDigger-771) The hard-hat overlay is a construction-worker tell that must be
            // on from the moment a builder is dispatched. Only meaningful when the art is
            // imported — placeholder-only runs leave the sprite null and the feature absent.
            bool hats = gm.TestLibrary != null && gm.TestLibrary.HardHat != null;
            if (hats)
            {
                for (int i = 0; i < crew.Count; i++)
                {
                    ctx.Assert(crew[i] != null && crew[i].TestHatActive,
                        "a freshly-drafted builder is not wearing its hard hat while commuting");
                }
            }

            // They commute out of the meadow and clock in at the site.
            yield return ctx.WaitUntil(() => AnyBuilderWorking(town));
            DinoController worker = FirstWorkingBuilder(town);
            ctx.Assert(worker != null, "no builder reached the site to work");
            ctx.Assert(!meadow.ContainsInterior(worker.transform.position),
                "working builder is still inside the meadow (never commuted)");
            ctx.Assert((worker.transform.position - town.TestArea.PlotWorld(0)).magnitude < 3f,
                "working builder did not arrive near the build plot");
            if (hats)
            {
                ctx.Assert(worker.TestHatActive, "working builder is not wearing its hard hat");
            }

            ctx.Log("2 residents left the meadow, commuted to the site, and clocked in (no buddy/backhoe drafted)");

            // ...and the hat comes off the instant the builder leaves the assignment. Recall the
            // crew (StopWork via the town reset — dinos survive, unlike GameManager.TestReset),
            // then confirm the still-alive worker's hat is gone: proving the exit-path removal.
            if (hats)
            {
                town.TestResetTown();
                yield return ctx.WaitFrames(2); // LateUpdate derives the hidden state from mode
                ctx.Assert(worker != null && !worker.TestHatActive,
                    "hard hat persisted after the builder was recalled off the build");
            }

            gm.TestReset();
        }

        // On completion: BuildingFinished fires, the site shows its finished state, and
        // the crew celebrates then trots back to the meadow (staying residents).
        private IEnumerator Case_BuildingFinishesAndCelebrates(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            MeadowArea meadow = gm.TestMeadow;
            ctx.Assert(meadow != null, "no MeadowArea in the scene");
            TownController town = EnsureTown(ctx);
            GameConfig cfg = gm.TestConfig;
            float saved = cfg.TownSecondsPerBuildState;

            int finished = 0;
            Action<int> onFin = _ => finished++;
            GameEvents.BuildingFinished += onFin;
            try
            {
                cfg.TownSecondsPerBuildState = 0.25f;

                DinoController b1 = gm.TestSpawnDino(DinoType.TRex, GrowthStage.Big);
                DinoController b2 = gm.TestSpawnDino(DinoType.Stegosaurus, GrowthStage.Big);
                gm.TestMakeResident(b1, teleportIntoMeadow: true);
                gm.TestMakeResident(b2, teleportIntoMeadow: true);
                yield return ctx.WaitFrames(2);

                gm.Save.Data.TreasureCount = cfg.TownBuildingPrice(0);
                yield return ctx.WaitUntil(() => town.TestActiveSite != null);
                BuildingController site = town.TestActiveSite;

                yield return ctx.WaitUntil(() => finished >= 1);
                ctx.Assert(site != null && site.IsFinished,
                    "site not marked finished on the BuildingFinished event");
                ctx.Assert(town.TestActiveSite == null,
                    "town still holds an active site after finishing");

                // Crew celebrates then heads home: both end up back inside the meadow.
                yield return ctx.WaitUntil(() =>
                    b1 != null && b2 != null &&
                    meadow.ContainsInterior(b1.transform.position) &&
                    meadow.ContainsInterior(b2.transform.position));
                ctx.Assert(!b1.IsBuddy && !b2.IsBuddy,
                    "a builder was promoted off the crew (builders stay residents)");
                ctx.Log("build finished (event fired), site shows finished state, crew celebrated and returned home");
            }
            finally
            {
                GameEvents.BuildingFinished -= onFin;
                cfg.TownSecondsPerBuildState = saved;
                gm.TestReset();
            }
        }

        // ============================================== HARD RULE (Greg's caveat)

        // While a build is actively underway: the backhoe never auto-moves toward the
        // site, tap-to-move still works normally, buddies keep following the player, and
        // dig entry still works. The player character is never taken over for building.
        private IEnumerator Case_PlayerControlUnaffectedByBuild(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            BackhoeController bh = gm.TestBackhoe;
            OverworldMap map = gm.TestMap;
            ctx.Assert(bh != null && map != null, "missing backhoe/map");
            TownController town = EnsureTown(ctx);
            GameConfig cfg = gm.TestConfig;

            float savedPerState = cfg.TownSecondsPerBuildState;
            try
            {
                // Park the per-state pacing so the site CANNOT finish while the case is still
                // checking (BigDinoBuildsFaster's 1000s pattern). This case verifies player
                // control during ACTIVE construction, and every check below re-asserts the
                // site is still there — but the crew keeps working the whole time, and with
                // the waits now budgeted generously enough to absorb a load hitch, two Big
                // builders could legitimately complete the building mid-verification ("the
                // build vanished mid-check"). Parking the pacing does not weaken anything:
                // ground is still broken from the wallet, the crew still commutes and works
                // on site for real, the site just never reaches its last state. Build
                // PROGRESSION is BuildAdvancesThroughStates' and BuildingFinishesAndCelebrates'
                // job, not this one's.
                cfg.TownSecondsPerBuildState = 1000f;

                // A buddy that must keep following the PLAYER, plus two residents so the town
                // has a genuine crew and construction is really active.
                DinoController buddy = gm.TestSpawnDino(DinoType.TRex, GrowthStage.Kid);
                DinoController res1 = gm.TestSpawnDino(DinoType.Triceratops, GrowthStage.Big);
                DinoController res2 = gm.TestSpawnDino(DinoType.Stegosaurus, GrowthStage.Big);
                gm.TestMakeResident(res1, teleportIntoMeadow: true);
                gm.TestMakeResident(res2, teleportIntoMeadow: true);
                yield return ctx.WaitFrames(2);
                ctx.Assert(buddy.IsBuddy, "buddy is not a buddy");

                gm.Save.Data.TreasureCount = cfg.TownBuildingPrice(0);
                yield return ctx.WaitUntil(() => town.TestActiveSite != null, 15f,
                    "town never broke ground on a fully funded plot");

                // The crew has to WALK from the meadow to the plot, so this is the case's one
                // genuinely long wait — budget it for a cross-island commute plus load hitches,
                // and name it so a wedged commute doesn't read as "the whole case timed out".
                yield return ctx.WaitUntil(() => AnyBuilderWorking(town), 40f,
                    "no builder ever clocked in at the site (commute wedged?)");

                // (1) The buddy is never drafted onto the crew.
                IReadOnlyList<DinoController> crew = town.TestBuilders;
                for (int i = 0; i < crew.Count; i++)
                {
                    ctx.Assert(crew[i] != buddy, "the buddy was drafted to build (forbidden)");
                }

                // (2) Parked backhoe holds position for 2s of active construction — nothing
                // commandeers it or nudges it toward the site.
                Vector3 park = FindMoveTarget(map, bh.transform.position, 1.5f);
                ctx.Assert((park - bh.transform.position).sqrMagnitude > 0.25f,
                    "no distinct parking spot near the backhoe");
                Vector3 preParkPos = bh.transform.position;
                bh.MoveTo(park);
                yield return ctx.WaitUntil(() => !bh.IsMoving, LegBudget(preParkPos, park),
                    "backhoe never reached its parking spot");
                Vector3 held = bh.transform.position;
                float t = 0f;
                while (t < 2f)
                {
                    ctx.Assert((bh.transform.position - held).sqrMagnitude < 0.0004f,
                        "backhoe auto-moved during construction (player character was commandeered)");
                    ctx.Assert(town.TestActiveSite != null, "the build vanished mid-check");
                    t += Time.deltaTime;
                    yield return null;
                }

                // (3) Player tap-to-move still works normally, mid-construction. The target is
                // picked with a real minimum SEPARATION (not merely a different cell): on this
                // isometric grid the neighbouring cell centre can sit well under the 0.5-unit
                // movement threshold this then asserts, which made the old helper's target a
                // coin flip against its own assertion.
                Vector3 moveTarget = FindMoveTarget(map, bh.transform.position, 1.5f);
                ctx.Assert((moveTarget - bh.transform.position).sqrMagnitude > 0.25f, "no distinct move target");
                gm.TestTapWorldRouted(moveTarget);
                yield return ctx.WaitUntil(() => !bh.IsMoving, LegBudget(held, moveTarget),
                    "tap-to-move never completed during construction");
                ctx.Assert((bh.transform.position - held).sqrMagnitude > 0.25f,
                    "tap-to-move did not move the backhoe during construction");
                ctx.Assert(map.IsWalkableWorld(bh.transform.position), "backhoe ended off a walkable cell");
                ctx.Assert(town.TestActiveSite != null,
                    "the build vanished during the tap-move check (pacing pin failed?)");

                // (4) The buddy stays a follower (not pulled to the town) and follows the player.
                ctx.Assert(buddy.IsBuddy, "buddy stopped being a buddy during construction");
                yield return ctx.WaitUntil(
                    () => buddy != null && (buddy.transform.position - bh.transform.position).magnitude < 3.5f,
                    20f, () => "buddy never caught up to the player during construction " +
                               $"(gap {(buddy != null ? (buddy.transform.position - bh.transform.position).magnitude : -1f):F1})");

                // (5) Dig entry still works while the town builds.
                DigMound m = FirstActiveMound(gm);
                ctx.Assert(m != null, "no active mound to dig");
                Vector3 preDigPos = bh.transform.position;
                bh.DriveToMound(m);
                yield return ctx.WaitUntil(() => gm.State.Is(GameState.Dig),
                    LegBudget(preDigPos, m.transform.position) + 5f, // + the dig-zoom transition
                    "never entered dig during construction");
                ctx.Assert(town.TestActiveSite != null,
                    "the build vanished during the dig-entry check (pacing pin failed?)");

                ctx.Log("during active construction: backhoe held then moved on tap, buddy kept following, dig entry worked");
            }
            finally
            {
                cfg.TownSecondsPerBuildState = savedPerState;
                gm.TestForceRoam();
                gm.TestReset();
            }
        }

        // ============================================= DinoDigger-pu3 Fruit Stand

        // Once the Fruit Stand (building index GameConfig.FruitStandIndex) is finished,
        // tapping a loose fruit that no dino wants SELLS it: (a) dug fruit stops downgrading
        // to treasure (the glut guard widened — surplus fruit is sellable gameplay now), and
        // (b) each sale banks a coin, with every 5th sale paying a jackpot gem instead. With
        // no residents in the scene every sale takes the deterministic self-serve FALLBACK
        // path (the fruit flies to the stand and sells itself), so no walking is timed.
        private IEnumerator Case_FruitStandSellsSurplus(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset(); // clears all dinos -> nobody is hungry
            TownController town = EnsureTown(ctx);
            ctx.Assert(town.TestArea != null && town.TestArea.PlotCount > GameConfig.FruitStandIndex,
                $"need > {GameConfig.FruitStandIndex} plots for the Fruit Stand test " +
                $"(have {(town.TestArea != null ? town.TestArea.PlotCount : 0)})");

            const int gemEverySale = 5; // mirrors GameManager.FruitStandGemEverySale

            int savedNext = gm.Save.Data.TownNextIndex;
            List<TownBuildingSave> savedList = gm.Save.Data.TownBuildings;
            int savedWallet = gm.Save.Data.TreasureCount;
            try
            {
                // Author every building up to and including the stand as FINISHED.
                gm.Save.Data.TreasureCount = 0; // a clean wallet so no build auto-starts mid-test
                gm.Save.Data.TownNextIndex = GameConfig.FruitStandIndex + 1;
                gm.Save.Data.TownBuildings = new List<TownBuildingSave>();
                for (int i = 0; i <= GameConfig.FruitStandIndex; i++)
                {
                    gm.Save.Data.TownBuildings.Add(new TownBuildingSave
                    {
                        Finished = true,
                        State = BuildingController.ConstructionStates,
                    });
                }

                town.RestoreFromSave(gm.Save.Data);
                yield return ctx.WaitFrames(2);

                ctx.Assert(town.IsBuildingFinished(GameConfig.FruitStandIndex),
                    "Fruit Stand not reported finished after restore");
                ctx.Assert(gm.TestFruitStandFinished,
                    "GameManager does not see the Fruit Stand as open");

                // Visual identity (guarded by art presence, like the sign/hat cases): the
                // finished stand carries its warm tint + bobbing fruit sign.
                Transform standT = town.transform.Find("Building_" + GameConfig.FruitStandIndex);
                BuildingController standObj = standT != null ? standT.GetComponent<BuildingController>() : null;
                ctx.Assert(standObj != null, "no Fruit Stand building object after restore");
                bool fruitArt = gm.TestLibrary != null && gm.TestLibrary.Fruit(0) != null;
                if (fruitArt)
                {
                    ctx.Assert(standObj.TestFruitStandDressed,
                        "finished Fruit Stand is not dressed (warm tint + bobbing fruit sign)");
                }

                // (1) Glut-guard widened: with the stand open and nobody hungry, dug fruit no
                //     longer downgrades to treasure — every sample stays fruit.
                int stayedFruit = 0;
                for (int i = 0; i < 40; i++)
                {
                    DugItemInfo r = gm.TestResolveItem(
                        new DugItemInfo(ItemType.Fruit, DinoType.TRex, 0, Vector3.zero));
                    if (r.Type == ItemType.Fruit)
                    {
                        stayedFruit++;
                    }
                }

                ctx.Assert(stayedFruit == 40,
                    $"dug fruit still downgraded with the stand open ({stayedFruit}/40 stayed fruit)");

                // (2) Selling pays out: coins (value 1) for sales 1..4, a gem (value 3) on the
                //     5th. No dino exists, so each sale runs the self-serve fallback.
                Vector3 stand = town.BuildingWorld(GameConfig.FruitStandIndex);
                int coinVal = gm.TestConfig.TreasureValue(0);
                int gemVal = gm.TestConfig.TreasureValue(1);

                for (int sale = 1; sale <= gemEverySale; sale++)
                {
                    int before = gm.Save.Data.TreasureCount;
                    ItemPickup fruit = gm.TestSpawnItem(ItemType.Fruit, DinoType.TRex, 0,
                        stand + new Vector3(1.2f, 0f, 0f));
                    yield return ctx.WaitUntil(() => fruit == null || fruit.IsCarryableFruit);
                    ctx.Assert(fruit != null, $"sale #{sale}: fruit vanished before it could be sold");

                    gm.RequestFeed(fruit); // nobody hungry + stand open -> sell
                    yield return ctx.WaitUntil(() => gm.Save.Data.TreasureCount > before);

                    int delta = gm.Save.Data.TreasureCount - before;
                    int expected = (sale % gemEverySale == 0) ? gemVal : coinVal;
                    ctx.Assert(delta == expected,
                        $"sale #{sale} banked {delta} (expected {expected})");
                    ctx.Assert(gm.TestFruitSalesCount == sale,
                        $"sale counter {gm.TestFruitSalesCount} != {sale}");
                }

                ctx.Log($"Fruit Stand open: dug fruit stopped downgrading (40/40 stayed fruit); " +
                        $"5 surplus fruit sold self-serve banking {coinVal},{coinVal},{coinVal},{coinVal},{gemVal} " +
                        "(jackpot gem on the 5th)");
            }
            finally
            {
                gm.Save.Data.TownNextIndex = savedNext;
                gm.Save.Data.TownBuildings = savedList ?? new List<TownBuildingSave>();
                gm.Save.Data.TreasureCount = savedWallet;
                gm.TestReset();
            }
        }

        // ============================================= DinoDigger-4yu Snack Builders

        // Snack-powered building: feeding a fruit to a builder standing on an active site banks a
        // chunk of build work so the building visibly jumps a construction state. Proves: (a) the
        // glut guard widened — dug fruit stays fruit while a crewed site is active even with nobody
        // hungry and the stand unfinished; (b) a fruit tap with an on-site working builder banks
        // SnackWorkSeconds and advances the state exactly one step; (c) a hungry dino still wins the
        // fruit (the snack path never fires ahead of a baby); and (d) with NO active site the fruit
        // falls through to the Fruit Stand sale.
        private IEnumerator Case_SnackBuilders(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            MeadowArea meadow = gm.TestMeadow;
            ctx.Assert(meadow != null, "no MeadowArea in the scene");
            TownController town = EnsureTown(ctx);
            ctx.Assert(town.TestArea != null && town.TestArea.PlotCount > GameConfig.FruitStandIndex,
                $"need > {GameConfig.FruitStandIndex} plots for the snack test " +
                $"(have {(town.TestArea != null ? town.TestArea.PlotCount : 0)})");

            GameConfig cfg = gm.TestConfig;
            float savedPerState = cfg.TownSecondsPerBuildState;
            float savedSnack = cfg.SnackWorkSeconds;
            int savedNext = gm.Save.Data.TownNextIndex;
            List<TownBuildingSave> savedList = gm.Save.Data.TownBuildings;
            int savedWallet = gm.Save.Data.TreasureCount;

            var advanced = new List<int>();
            Action<int> onAdv = st => advanced.Add(st);
            GameEvents.BuildingStateAdvanced += onAdv;
            try
            {
                // Slow per-state timing so the site never advances on its own during the case, and one
                // snack banks exactly one construction state (SnackWorkSeconds == per-state).
                cfg.TownSecondsPerBuildState = 100f;
                cfg.SnackWorkSeconds = cfg.TownSecondsPerBuildState;

                // Two Big residents become the crew (Big => never hungry, so AnyDinoHungry stays false).
                DinoController b1 = gm.TestSpawnDino(DinoType.TRex, GrowthStage.Big);
                DinoController b2 = gm.TestSpawnDino(DinoType.Stegosaurus, GrowthStage.Big);
                gm.TestMakeResident(b1, teleportIntoMeadow: true);
                gm.TestMakeResident(b2, teleportIntoMeadow: true);
                yield return ctx.WaitFrames(2);

                // Break ground on building 0 and wait for a builder to physically clock in.
                gm.Save.Data.TreasureCount = cfg.TownBuildingPrice(0);
                yield return ctx.WaitUntil(() => town.TestActiveSite != null);
                BuildingController site = town.TestActiveSite;
                yield return ctx.WaitUntil(() => AnyBuilderWorking(town));
                ctx.Assert(town.HasWorkingBuilderOnSite(), "no builder reported working on the active site");

                // (a) Glut guard widened: crewed site active, nobody hungry, stand UNfinished -> dug
                //     fruit never downgrades. Every sample stays fruit.
                ctx.Assert(!gm.TestFruitStandFinished, "fruit stand unexpectedly finished for the glut check");
                int stayedFruit = 0;
                for (int i = 0; i < 40; i++)
                {
                    DugItemInfo r = gm.TestResolveItem(
                        new DugItemInfo(ItemType.Fruit, DinoType.TRex, 0, Vector3.zero));
                    if (r.Type == ItemType.Fruit)
                    {
                        stayedFruit++;
                    }
                }

                ctx.Assert(stayedFruit == 40,
                    $"dug fruit downgraded with a crewed build site active ({stayedFruit}/40 stayed fruit)");

                // (b) A fruit fed with an on-site builder banks a snack: the site jumps exactly one state.
                int before = site.State;
                ctx.Assert(before < BuildingController.ConstructionStates - 1,
                    $"site started too far along for the snack check (state {before})");
                advanced.Clear();
                ItemPickup snack = gm.TestSpawnItem(ItemType.Fruit, DinoType.TRex, 0,
                    town.BuildingWorld(0) + new Vector3(1.4f, 0f, 0f));
                yield return ctx.WaitUntil(() => snack == null || snack.IsCarryableFruit);
                ctx.Assert(snack != null, "snack fruit vanished before it could be fed");

                gm.RequestFeed(snack); // nobody hungry + crewed site -> builder snack
                yield return ctx.WaitUntil(() => site.State > before);
                ctx.Assert(site.State == before + 1,
                    $"snack advanced {site.State - before} states (expected exactly 1)");
                ctx.Assert(advanced.Contains(before + 1),
                    $"snack did not fire BuildingStateAdvanced({before + 1}) (saw: {Join(advanced)})");

                // (c) A hungry dino still wins the fruit: the snack path never fires ahead of a baby.
                //     Spawn a hungry Baby BUDDY (buddies are never drafted to build) and drop the fruit
                //     on it so it eats at once; the site must NOT advance from a snack.
                int stateHeld = site.State;
                DinoController baby = gm.TestSpawnDino(DinoType.Triceratops, GrowthStage.Baby);
                ctx.Assert(baby.IsBuddy, "test baby is not a buddy (would risk being drafted as a builder)");
                ctx.Assert(baby.IsHungry, "test baby is not hungry");
                yield return ctx.WaitFrames(2);

                int babyAteBefore = baby.FruitEaten;
                ItemPickup babyFruit = gm.TestSpawnItem(ItemType.Fruit, DinoType.TRex, 0,
                    baby.transform.position);
                yield return ctx.WaitUntil(() => babyFruit == null || babyFruit.IsCarryableFruit);
                ctx.Assert(babyFruit != null, "baby's fruit vanished before it could be eaten");

                gm.RequestFeed(babyFruit); // hungry baby present -> the baby eats, not a builder
                yield return ctx.WaitUntil(() => baby.FruitEaten > babyAteBefore);
                ctx.Assert(site.State == stateHeld,
                    $"site advanced ({stateHeld} -> {site.State}) while a hungry dino should have won the fruit");

                // (d) With NO active site the fruit falls through to the Fruit Stand sale. Reset, author
                //     a finished stand + no active site + no residents, tap a surplus fruit -> it self-sells.
                gm.TestReset();
                gm.Save.Data.TreasureCount = 0;
                gm.Save.Data.TownNextIndex = GameConfig.FruitStandIndex + 1;
                gm.Save.Data.TownBuildings = new List<TownBuildingSave>();
                for (int i = 0; i <= GameConfig.FruitStandIndex; i++)
                {
                    gm.Save.Data.TownBuildings.Add(new TownBuildingSave
                    {
                        Finished = true,
                        State = BuildingController.ConstructionStates,
                    });
                }

                town.RestoreFromSave(gm.Save.Data);
                yield return ctx.WaitFrames(2);
                ctx.Assert(gm.TestFruitStandFinished, "Fruit Stand not open after restore");
                ctx.Assert(!town.HasWorkingBuilderOnSite(),
                    "unexpected active crewed site for the fall-through check");

                int walletBefore = gm.Save.Data.TreasureCount;
                Vector3 stand = town.BuildingWorld(GameConfig.FruitStandIndex);
                ItemPickup sale = gm.TestSpawnItem(ItemType.Fruit, DinoType.TRex, 0,
                    stand + new Vector3(1.2f, 0f, 0f));
                yield return ctx.WaitUntil(() => sale == null || sale.IsCarryableFruit);
                ctx.Assert(sale != null, "fall-through fruit vanished before it could sell");

                gm.RequestFeed(sale); // no active site + stand open -> stand sale
                yield return ctx.WaitUntil(() => gm.Save.Data.TreasureCount > walletBefore);
                ctx.Assert(gm.Save.Data.TreasureCount > walletBefore,
                    "surplus fruit did not sell at the stand when no build site was active");

                ctx.Log("snack builders: crewed-site glut guard kept 40/40 fruit; a snack advanced the " +
                        "build one state (event fired); a hungry baby still won the fruit (no snack); with " +
                        "no active site the fruit fell through to a stand sale");
            }
            finally
            {
                GameEvents.BuildingStateAdvanced -= onAdv;
                cfg.TownSecondsPerBuildState = savedPerState;
                cfg.SnackWorkSeconds = savedSnack;
                gm.Save.Data.TownNextIndex = savedNext;
                gm.Save.Data.TownBuildings = savedList ?? new List<TownBuildingSave>();
                gm.Save.Data.TreasureCount = savedWallet;
                gm.TestReset();
            }
        }

        // ============================================= DinoDigger-x07 Recess Time

        // Tapping a FINISHED building throws a 15s dino party. Proves: (a) a finished building
        // is tappable and bounces (instant feedback fires); (b) an under-construction building
        // is NOT tappable; (c) a recess recruits free residents but never poaches a busy
        // builder off an active site; (d) a repeat tap during a running recess re-bounces but
        // does not re-recruit; (e) the party runs then ends, its residents heading home; and
        // (f) a tap with zero free dinos still responds (feedback only, never an error).
        private IEnumerator Case_RecessTime(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            MeadowArea meadow = gm.TestMeadow;
            ctx.Assert(meadow != null, "no MeadowArea in the scene");
            TownController town = EnsureTown(ctx);
            ctx.Assert(town.TestArea != null && town.TestArea.PlotCount >= 2,
                $"need >=2 plots for recess (have {(town.TestArea != null ? town.TestArea.PlotCount : 0)})");

            GameConfig cfg = gm.TestConfig;
            float savedRecess = cfg.RecessSeconds;
            float savedPerState = cfg.TownSecondsPerBuildState;
            int savedNext = gm.Save.Data.TownNextIndex;
            List<TownBuildingSave> savedList = gm.Save.Data.TownBuildings;
            int savedWallet = gm.Save.Data.TreasureCount;
            try
            {
                cfg.RecessSeconds = 1.0f;             // short party so the case finishes fast
                cfg.TownSecondsPerBuildState = 100f; // building 1 stays UNDER CONSTRUCTION all case

                // Author building 0 FINISHED; the queue continues at plot 1.
                gm.Save.Data.TreasureCount = 0;
                gm.Save.Data.TownNextIndex = 1;
                gm.Save.Data.TownBuildings = new List<TownBuildingSave>
                {
                    new TownBuildingSave { Finished = true, State = BuildingController.ConstructionStates },
                };
                town.RestoreFromSave(gm.Save.Data);
                yield return ctx.WaitFrames(2);

                Transform b0t = town.transform.Find("Building_0");
                BuildingController b0 = b0t != null ? b0t.GetComponent<BuildingController>() : null;
                ctx.Assert(b0 != null && b0.IsFinished, "building 0 not restored finished");

                // (a) A finished building is TAPPABLE: it carries a tap collider.
                ctx.Assert(b0.TestIsTappable && b0.GetComponent<Collider2D>() != null,
                    "finished building is not tappable (no collider)");

                // Four meadow residents: two get drafted as builders (busy), two stay free.
                var residents = new List<DinoController>();
                DinoType[] types =
                {
                    DinoType.TRex, DinoType.Stegosaurus, DinoType.Triceratops, DinoType.Brachiosaurus
                };
                for (int i = 0; i < types.Length; i++)
                {
                    DinoController d = gm.TestSpawnDino(types[i], GrowthStage.Big);
                    gm.TestMakeResident(d, teleportIntoMeadow: true);
                    residents.Add(d);
                }

                yield return ctx.WaitFrames(2);

                // Break ground on plot 1 so a crew is drafted and genuinely busy.
                gm.Save.Data.TreasureCount = cfg.TownBuildingPrice(1);
                yield return ctx.WaitUntil(() => town.TestActiveSite != null);
                BuildingController site = town.TestActiveSite;
                ctx.Assert(!site.IsFinished, "active site finished too fast (per-state timing)");

                // (b) An UNDER-CONSTRUCTION building is NOT tappable.
                ctx.Assert(!site.TestIsTappable && site.GetComponent<Collider2D>() == null,
                    "under-construction building is tappable (should not be)");

                yield return ctx.WaitUntil(() => AnyBuilderWorking(town));
                DinoController worker = FirstWorkingBuilder(town);
                ctx.Assert(worker != null, "no builder reached the site");
                int builderCount = town.TestBuilderCount;

                // (a cont.) A REAL routed tap on the finished building fires instant feedback and
                // starts a recess. Let a physics step register the collider, then tap a point on
                // it where the building is the FIRST ITappable (mirrors FindTappable) — robust
                // against a respawned mound whose footprint occasionally clips the building.
                yield return new WaitForFixedUpdate();
                Physics2D.SyncTransforms();
                int fbBefore = town.TestRecessTapFeedback;
                bool routed = RoutedTapOnBuilding(gm, b0, 0);
                ctx.Assert(routed, "routed tap did not resolve to the finished building");
                ctx.Assert(town.TestRecessTapFeedback == fbBefore + 1,
                    "tap on finished building gave no instant feedback");

                yield return ctx.WaitUntil(() => town.TestIsRecessRunning(0));
                ctx.Assert(town.TestRecessDinoTotal >= 1,
                    "recess recruited nobody though free residents existed");

                // (c) The busy builder was NOT poached: the crew is intact and still working.
                ctx.Assert(worker.IsWorking, "a working builder was pulled off the site by the recess");
                ctx.Assert(town.TestBuilderCount == builderCount,
                    $"builder crew changed during recess ({town.TestBuilderCount} != {builderCount})");

                // (d) A repeat tap during the running recess re-bounces but does NOT re-recruit or
                // start a second recess on the same building. Call the handler directly here so
                // the assertion can't be confused by a party-goer now standing over the plot.
                int fb2 = town.TestRecessTapFeedback;
                int recCount = town.TestRecessCount;
                town.OnBuildingTapped(b0, 0);
                ctx.Assert(town.TestRecessTapFeedback == fb2 + 1, "repeat tap gave no feedback");
                ctx.Assert(town.TestRecessCount == recCount,
                    "repeat tap started a second recess on the same building");

                // (e) The party runs then ends: the recess clears and its residents head home.
                yield return ctx.WaitUntil(() => !town.TestIsRecessRunning(0) && town.TestRecessCount == 0);
                yield return ctx.WaitUntil(() =>
                {
                    for (int i = 0; i < residents.Count; i++)
                    {
                        DinoController d = residents[i];
                        if (d == null || d.IsWorking)
                        {
                            continue; // builders stay on their site
                        }

                        if (!meadow.ContainsInterior(d.transform.position))
                        {
                            return false;
                        }
                    }

                    return true;
                });
                ctx.Assert(worker.IsWorking, "builder stopped working after the party ended");

                // (f) Zero free dinos: reset the town, re-finish building 0 with NO residents,
                // and tap — the tap still responds (feedback), no recess, no error.
                gm.TestReset();
                town.RestoreFromSave(gm.Save.Data); // TownNextIndex=1, building 0 finished
                yield return ctx.WaitFrames(2);
                Transform b0t2 = town.transform.Find("Building_0");
                BuildingController b0b = b0t2 != null ? b0t2.GetComponent<BuildingController>() : null;
                ctx.Assert(b0b != null && b0b.TestIsTappable, "re-restored building 0 not tappable");

                yield return new WaitForFixedUpdate();
                Physics2D.SyncTransforms();
                int fb3 = town.TestRecessTapFeedback;
                bool routed2 = RoutedTapOnBuilding(gm, b0b, 0);
                ctx.Assert(routed2, "zero-free routed tap did not resolve to the building");
                yield return ctx.WaitFrames(2);
                ctx.Assert(town.TestRecessTapFeedback == fb3 + 1,
                    "tap with zero free dinos gave no feedback");
                ctx.Assert(town.TestRecessCount == 0,
                    "a recess started with zero free dinos (should be feedback-only)");

                ctx.Log("recess: finished building tappable+bounces, under-construction not tappable; " +
                        "party recruited free residents (busy builder not poached), repeat tap re-bounced " +
                        "without re-recruiting, party ended and residents went home; zero-free tap still responded");
            }
            finally
            {
                cfg.RecessSeconds = savedRecess;
                cfg.TownSecondsPerBuildState = savedPerState;
                gm.Save.Data.TownNextIndex = savedNext;
                gm.Save.Data.TownBuildings = savedList ?? new List<TownBuildingSave>();
                gm.Save.Data.TreasureCount = savedWallet;
                gm.TestReset();
            }
        }

        // ============================ DinoDigger-lie overlapping-tap determinism

        // Two tappables can share a world point — a respawned dig mound landing on a finished
        // building's footprint is the case that bit us — and Physics2D.OverlapPointAll hands
        // them back in NO defined order, so the same tap used to open a dig on one frame and
        // throw a party on the next. This case pins BOTH halves of the fix:
        //   (1) FindTappable resolves an overlap by explicit PRIORITY (the transient mound
        //       above the permanent building) with nearest-collider-centre as the tiebreak,
        //       so the answer is identical every time it is asked;
        //   (2) SpawnManager never places a respawn on a built plot in the first place, so
        //       the overlap does not arise in real play at all.
        // The overlap is FORCED here through test hooks (a mound parked on the plot), never
        // waited for — the bug was rare precisely because the placement is random.
        private IEnumerator Case_TapPriorityOverlap(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            TownController town = EnsureTown(ctx);
            OverworldMap map = gm.TestMap;
            ctx.Assert(town.TestArea != null && town.TestArea.PlotCount >= 1, "no town plots");

            int savedNext = gm.Save.Data.TownNextIndex;
            List<TownBuildingSave> savedList = gm.Save.Data.TownBuildings;
            int savedWallet = gm.Save.Data.TreasureCount;
            RectInt savedDistrict = map.TestTownDistrict;
            bool hadDistrict = map.TestHasTownDistrict;
            DigMound parked = null;
            Vector3 moundHome = Vector3.zero;
            TownController.TestSuspendBuilds = true; // no build may start mid-case
            try
            {
                // Author building 0 FINISHED (the only tappable building in the plaza).
                gm.Save.Data.TreasureCount = 0;
                gm.Save.Data.TownNextIndex = 1;
                gm.Save.Data.TownBuildings = new List<TownBuildingSave>
                {
                    new TownBuildingSave { Finished = true, State = BuildingController.ConstructionStates },
                };
                town.RestoreFromSave(gm.Save.Data);
                yield return ctx.WaitFrames(2);

                Transform b0t = town.transform.Find("Building_0");
                BuildingController b0 = b0t != null ? b0t.GetComponent<BuildingController>() : null;
                ctx.Assert(b0 != null && b0.IsFinished, "building 0 not restored finished");
                Collider2D bCol = b0.GetComponent<Collider2D>();
                ctx.Assert(bCol != null, "finished building has no tap collider");

                // ---- (a) Building alone: a tap there resolves to the building. ----
                // Pick a point where the building is the ONLY tappable, so the overlap below
                // is exactly two objects and the expected answer is unambiguous.
                yield return new WaitForFixedUpdate();
                Physics2D.SyncTransforms();
                ctx.Assert(FindBuildingOnlyPoint(b0, out Vector3 overlapPoint),
                    "no point on the finished building is free of other tappables");
                ctx.Assert(gm.TestFindTappable(overlapPoint) == (Component)b0,
                    "a tap on an unobstructed finished building did not resolve to it");

                // ---- (b) Force the overlap: park an active mound on the building. ----
                DigMound mound = FirstActiveMound(gm);
                ctx.Assert(mound != null, "no active mound to park on the plot");
                parked = mound;
                moundHome = mound.transform.position; // put it back when the case ends
                mound.Respawn(overlapPoint); // respawn pops in from scale 0, so wait for it

                Collider2D mCol = mound.GetComponent<Collider2D>();
                ctx.Assert(mCol != null, "mound has no collider");
                yield return ctx.WaitUntil(() =>
                {
                    Physics2D.SyncTransforms();
                    return mCol.OverlapPoint(overlapPoint) && bCol.OverlapPoint(overlapPoint);
                }, 10f, "could not force a mound/building collider overlap");

                // The whole bug: ask the same question repeatedly and get one answer. Ten
                // asks (physics may reorder its results between queries) must all agree, and
                // agree with the documented priority — the mound, the transient thing the
                // toddler is aiming at, not the building underneath it.
                Component first = gm.TestFindTappable(overlapPoint);
                ctx.Assert(first == (Component)mound,
                    $"overlap resolved to {(first != null ? first.GetType().Name : "nothing")} " +
                    "(expected the mound: dino > pickup > mound > building)");
                for (int i = 0; i < 10; i++)
                {
                    ctx.Assert(gm.TestFindTappable(overlapPoint) == first,
                        $"overlap resolution changed between identical taps (ask {i + 1})");
                    yield return null;
                }

                // And the routed tap really takes the mound's branch: no party starts.
                int fbBefore = town.TestRecessTapFeedback;
                gm.TestTapWorldRouted(overlapPoint);
                yield return ctx.WaitFrames(2);
                ctx.Assert(town.TestRecessTapFeedback == fbBefore,
                    "the overlapping tap reached the building (should have gone to the mound)");
                ctx.Assert(town.TestRecessCount == 0, "a recess started from the overlapping tap");

                // ---- (c) Placement: a respawn may never land on a built plot. ----
                // Test the rule in ISOLATION: drop the district rect (a CELL-measured guard
                // that already covers the plaza and would mask the new one) and park the
                // player well away from the plot (its 4-unit clearance would mask it too).
                // With both out of the way, only the built-plot rule can refuse the plot —
                // so this also proves the town actually reached SpawnManager.
                // Teleporting the backhoe doubles as cancelling the dig drive the tap above
                // started, so the next case begins from a parked player.
                Vector3 plot0 = town.BuildingWorld(0);
                BackhoeController bh = gm.TestBackhoe;
                if (bh != null)
                {
                    Vector3 far = FindMoveTarget(map, plot0, 8f);
                    ctx.Assert((far - plot0).sqrMagnitude > 25f, "nowhere to park the player clear of the plot");
                    bh.TestTeleport(far, bh.Facing);
                }

                ctx.Assert(town.NearBuiltPlot(plot0, 1.2f), "built plot 0 not recognised as built");
                ctx.Assert(!town.NearBuiltPlot(plot0 + new Vector3(4f, 0f, 0f), 1.2f),
                    "a point 4 units off the plot counted as built");

                map.SetTownDistrict(new RectInt(0, 0, 0, 0)); // district guard off
                ctx.Assert(!gm.Spawn.TestCanPlace(plot0, mound),
                    "a mound respawn was allowed onto a finished building's plot");
                ctx.Assert(!gm.Spawn.TestCanPlace(plot0 + new Vector3(0.8f, 0f, 0f), mound),
                    "a mound respawn was allowed to clip a finished building's footprint");

                // Sanity: the filter is not simply refusing everything.
                bool anyAllowed = false;
                for (int i = 0; i < 200 && !anyAllowed; i++)
                {
                    if (map.TryRandomWalkableCell(out Vector3Int c))
                    {
                        anyAllowed = gm.Spawn.TestCanPlace(map.CellCenter(c), mound);
                    }
                }

                ctx.Assert(anyAllowed, "the respawn filter rejected every walkable cell on the island");

                ctx.Log("overlapping mound+building tap resolves to the mound, identically across " +
                        "11 asks; respawn placement refuses built plots (and their 1.2u ring)");
            }
            finally
            {
                if (hadDistrict)
                {
                    map.SetTownDistrict(savedDistrict);
                }

                if (parked != null)
                {
                    parked.Respawn(moundHome); // never leave a mound sitting on the plaza
                }

                TownController.TestSuspendBuilds = false;
                gm.Save.Data.TownNextIndex = savedNext;
                gm.Save.Data.TownBuildings = savedList ?? new List<TownBuildingSave>();
                gm.Save.Data.TreasureCount = savedWallet;
                gm.TestReset();
            }
        }

        // ================================= DinoDigger-3pz townsfolk interaction loops

        // The representative buildings whose scenes this case drives end to end. Chosen for
        // coverage rather than count: 1 Boulder Brew is the plain one-guest loop; 3 Bedrock
        // Bijou is the RISKIEST one (its guests scale away at the door, so it proves an
        // interrupted or finished scene always restores the puppet's pose); 5 Dino Daycare
        // runs on its FALLBACK path here (every resident in this case is Big, so "any dino
        // peeks"); 8 Fossil Fountain is the multi-guest finale with the longer loop.
        private static readonly int[] InteractionBuildingsChecked = { 1, 3, 5, 8 };

        // A finished building is ALIVE: residents stroll over and play its little scene, then
        // wander home. Proves, for several representative buildings: (a) a forced visit really
        // walks a NON-buddy resident to the building and puts it in the visiting puppet state;
        // (b) the scene runs to completion and everyone exits cleanly with their pose restored
        // (no shrunken cinema-goers, no squashed bathers); (c) the daycare's stage FALLBACK
        // path is exercised when no baby is around; (d) a visitor DRAFTED to build abandons its
        // visit instantly and the build proceeds to finish — town life can never deadlock
        // construction; and (e) a walk BUDDY is never ambient life, refused both by the
        // recruiter and by DinoController.GoVisit itself.
        //
        // Deterministic by construction: ambient visits are parked (interval 10000s) and every
        // visit in the case is forced through the Test hook, beats are accelerated via config,
        // and each wait is a state poll — no wall-clock races anywhere.
        private IEnumerator Case_EachBuildingPlaysInteraction(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            MeadowArea meadow = gm.TestMeadow;
            ctx.Assert(meadow != null, "no MeadowArea in the scene");

            TownController town = EnsureTown(ctx);
            TownLifeController life = town.TestLife;
            ctx.Assert(life != null,
                "the town has no TownLifeController — TownController.Configure must ensure the " +
                "ambient-life service on the town root");

            TownArea area = town.TestArea;
            int plots = area != null ? area.PlotCount : 0;
            ctx.Assert(plots >= 9,
                $"need the full nine-plot roster for the interaction test (have {plots})");

            GameConfig cfg = gm.TestConfig;
            float savedInterval = cfg.TownVisitIntervalSeconds;
            float savedBeat = cfg.TownVisitBeatSeconds;
            int savedMaxVisits = cfg.TownMaxVisits;
            float savedPerState = cfg.TownSecondsPerBuildState;
            int savedNext = gm.Save.Data.TownNextIndex;
            List<TownBuildingSave> savedList = gm.Save.Data.TownBuildings;
            int savedWallet = gm.Save.Data.TreasureCount;
            try
            {
                cfg.TownVisitIntervalSeconds = 10000f; // no ambient visits: the case forces each one
                cfg.TownVisitBeatSeconds = 0.2f;       // a whole scene plays in about a second
                cfg.TownMaxVisits = 3;
                cfg.TownSecondsPerBuildState = 100f;   // part (d) decides when a build may finish

                // Re-arm the ambient countdown AFTER raising the interval — otherwise the
                // service is still holding the short countdown it captured on the reset above,
                // and a stray ambient visit could gate-crash the forced ones we are measuring.
                life.TestResetLife();

                // ---- (a)(b)(c) the plaza is finished; each representative scene plays out ----
                gm.Save.Data.TreasureCount = 0; // nothing can auto-start while we watch
                gm.Save.Data.TownNextIndex = plots;
                gm.Save.Data.TownBuildings = new List<TownBuildingSave>();
                for (int i = 0; i < plots; i++)
                {
                    gm.Save.Data.TownBuildings.Add(new TownBuildingSave
                    {
                        Finished = true,
                        State = BuildingController.ConstructionStates,
                    });
                }

                town.RestoreFromSave(gm.Save.Data);
                yield return ctx.WaitFrames(2);
                ctx.Assert(town.FinishedBuildingCount == plots,
                    $"restored {town.FinishedBuildingCount} finished buildings (expected {plots})");

                // Three Big residents — Big so nothing pulls them off to eat mid-scene, and Big
                // is also what puts the daycare on its no-baby fallback path.
                var residents = new List<DinoController>();
                DinoType[] types = { DinoType.TRex, DinoType.Stegosaurus, DinoType.Triceratops };
                for (int i = 0; i < types.Length; i++)
                {
                    DinoController d = gm.TestSpawnDino(types[i], GrowthStage.Big);
                    gm.TestMakeResident(d, teleportIntoMeadow: true);
                    residents.Add(d);
                }

                yield return ctx.WaitFrames(2);

                for (int k = 0; k < InteractionBuildingsChecked.Length; k++)
                {
                    int index = InteractionBuildingsChecked[k];
                    int arrivedBefore = life.TestVisitsArrived;
                    int completedBefore = life.TestVisitsCompleted;
                    int abortedBefore = life.TestVisitsAborted;

                    // Poll the force hook until someone is actually free (the previous scene's
                    // guests are still walking home) — a state poll, never a sleep.
                    yield return ctx.WaitUntil(() => life.TestForceVisit(index));
                    ctx.Assert(life.TestIsVisiting(index),
                        $"forced visit on building {index} did not register as running");

                    // (a) A guest walks over and clocks in AT the building. The distance is
                    //     captured by the service at the moment of arrival, so a short scene
                    //     that finishes quickly can't race this assertion.
                    yield return ctx.WaitUntil(() => life.TestVisitsArrived > arrivedBefore);
                    ctx.Assert(life.TestLastArrivalIndex == index,
                        $"arrival recorded for building {life.TestLastArrivalIndex}, expected {index}");
                    ctx.Assert(life.TestLastArrivalDistance >= 0f && life.TestLastArrivalDistance < 3f,
                        $"visitor clocked in {life.TestLastArrivalDistance:0.##}u from building " +
                        $"{index}'s plot — it never really walked there");

                    // The interaction is live: at least one guest is in the visiting puppet
                    // state, and not one of them is a buddy.
                    List<DinoController> guests = life.TestVisitDinos(index);
                    ctx.Assert(guests.Count > 0, $"building {index}'s visit has no guests");
                    bool attending = false;
                    for (int i = 0; i < guests.Count; i++)
                    {
                        ctx.Assert(guests[i] != null && !guests[i].IsBuddy,
                            $"a walk BUDDY joined building {index}'s visit (forbidden)");
                        attending |= guests[i].IsVisiting;
                    }

                    ctx.Assert(attending,
                        $"nobody is attending building {index} although the visit reported an arrival");

                    // (c) With only Big residents around, the daycare plays its fallback scene.
                    if (index == 5)
                    {
                        ctx.Assert(life.TestVisitFallback(index),
                            "the daycare did not fall back to 'any dino peeks' with no baby around");
                    }

                    // (b) The scene finishes on its own and is retired as COMPLETED (not aborted).
                    yield return ctx.WaitUntil(() => !life.TestIsVisiting(index));
                    ctx.Assert(life.TestVisitsCompleted == completedBefore + 1,
                        $"building {index}'s scene did not complete cleanly " +
                        $"(completed {life.TestVisitsCompleted - completedBefore}, " +
                        $"aborted {life.TestVisitsAborted - abortedBefore})");
                    ctx.Assert(life.TestVisitsAborted == abortedBefore,
                        $"building {index}'s scene aborted instead of finishing");

                    // ...and every guest is off the visit with its pose restored (the cinema
                    // scales its guests away at the door — nobody may be left shrunk). Waited
                    // out first so the little exit bounce has settled.
                    yield return ctx.WaitSecondsScaled(0.5f);
                    for (int i = 0; i < guests.Count; i++)
                    {
                        DinoController d = guests[i];
                        ctx.Assert(d != null && !d.IsOnVisit && !d.IsVisiting,
                            $"a guest is still flagged as visiting building {index} after the scene");
                        float rest = cfg.StageScale(d.Stage);
                        Vector3 s = d.transform.localScale;
                        ctx.Assert(Mathf.Abs(s.x - rest) < 0.05f && Mathf.Abs(s.y - rest) < 0.05f,
                            $"a guest left building {index} at scale {s.x:0.##}x{s.y:0.##} " +
                            $"(expected {rest:0.##}) — the visit pose was not restored");
                    }
                }

                // Part A's tally, kept for the log line (the reset below rewinds the counters).
                int scenesPlayed = life.TestVisitsCompleted;

                // ---- (d) a DRAFTED visitor abandons its visit, and the build still finishes ----
                gm.TestReset();                        // clean world: no dinos, no sites
                cfg.TownSecondsPerBuildState = 0.3f;   // the ex-visitor can now finish a build
                cfg.TownVisitBeatSeconds = 2f;         // a long scene, so the draft lands mid-visit
                gm.Save.Data.TreasureCount = 0;
                gm.Save.Data.TownNextIndex = 1;
                gm.Save.Data.TownBuildings = new List<TownBuildingSave>
                {
                    new TownBuildingSave { Finished = true, State = BuildingController.ConstructionStates },
                };
                town.RestoreFromSave(gm.Save.Data);
                yield return ctx.WaitFrames(2);

                // EXACTLY ONE resident, so the crew draft can only possibly reach the visitor.
                DinoController lone = gm.TestSpawnDino(DinoType.TRex, GrowthStage.Big);
                gm.TestMakeResident(lone, teleportIntoMeadow: true);
                yield return ctx.WaitFrames(2);

                yield return ctx.WaitUntil(() => life.TestForceVisit(0));
                yield return ctx.WaitUntil(() => lone.IsVisiting);
                ctx.Assert(life.TestIsVisiting(0), "the lone resident's visit is not running");
                int abortedBeforeDraft = life.TestVisitsAborted;

                // Fund the next building: the town drafts the only resident it can see.
                gm.Save.Data.TreasureCount = cfg.TownBuildingPrice(1);
                yield return ctx.WaitUntil(() => town.TestActiveSite != null);
                BuildingController site = town.TestActiveSite;
                yield return ctx.WaitUntil(() => town.TestBuilderCount > 0);
                ctx.Assert(town.TestBuilders[0] == lone,
                    "the town drafted somebody other than the lone resident (test setup broke)");

                // The visit yielded the instant the draft landed — no grace period, no tug of war.
                ctx.Assert(!lone.IsOnVisit && !lone.IsVisiting,
                    "a drafted builder is still flagged as visiting (construction must always win)");
                yield return ctx.WaitFrames(2);
                ctx.Assert(!life.TestIsVisiting(0), "the visit outlived its drafted guest");
                ctx.Assert(life.TestVisitsAborted == abortedBeforeDraft + 1,
                    "the abandoned visit was not retired");
                ctx.Assert(life.TestVisitCount == 0, "a stale visit is still running after the draft");

                // The abandoned scene left NO pose behind either: a beat tween killed mid-flight
                // must still resolve to the builder's stage scale. (Checked here, while the
                // ex-visitor is still commuting, so the on-site work bob can't colour the read.)
                float loneRest = cfg.StageScale(lone.Stage);
                Vector3 loneScale = lone.transform.localScale;
                ctx.Assert(Mathf.Abs(loneScale.x - loneRest) < 0.05f &&
                           Mathf.Abs(loneScale.y - loneRest) < 0.05f,
                    $"the drafted ex-visitor is at scale {loneScale.x:0.##}x{loneScale.y:0.##} " +
                    $"(expected {loneRest:0.##}) — an aborted scene stranded its pose");

                // ...and construction is not deadlocked: the ex-visitor clocks in and finishes.
                yield return ctx.WaitUntil(() => lone.IsWorking);
                yield return ctx.WaitUntil(() => site != null && site.IsFinished);

                // ---- (e) a walk buddy is never ambient town life ----
                gm.TestReset();
                cfg.TownSecondsPerBuildState = 100f;
                gm.Save.Data.TreasureCount = 0;
                gm.Save.Data.TownNextIndex = 1;
                gm.Save.Data.TownBuildings = new List<TownBuildingSave>
                {
                    new TownBuildingSave { Finished = true, State = BuildingController.ConstructionStates },
                };
                town.RestoreFromSave(gm.Save.Data);
                yield return ctx.WaitFrames(2);

                DinoController buddy = gm.TestSpawnDino(DinoType.Brachiosaurus, GrowthStage.Big);
                yield return ctx.WaitFrames(2);
                ctx.Assert(buddy.IsBuddy, "the dino under test is not a walk buddy");

                // The recruiter cannot see a buddy: with only a buddy alive, nothing starts.
                ctx.Assert(!life.TestForceVisit(0),
                    "a visit was recruited from a world containing only a walk buddy");
                ctx.Assert(life.TestVisitCount == 0, "a visit started with nobody eligible");

                // ...and the dino itself refuses, so no future caller can sneak one in.
                ctx.Assert(!buddy.GoVisit(town.BuildingWorld(0), 1f),
                    "DinoController.GoVisit accepted a walk buddy");
                ctx.Assert(!buddy.IsOnVisit && buddy.IsBuddy,
                    "the refused GoVisit still mutated the buddy's state");

                // With a resident alongside it, the visit takes the resident — never the buddy.
                DinoController res = gm.TestSpawnDino(DinoType.Stegosaurus, GrowthStage.Big);
                gm.TestMakeResident(res, teleportIntoMeadow: true);
                yield return ctx.WaitFrames(2);
                yield return ctx.WaitUntil(() => life.TestForceVisit(0));
                List<DinoController> picked = life.TestVisitDinos(0);
                ctx.Assert(picked.Count > 0, "the visit recruited nobody though a resident was free");
                for (int i = 0; i < picked.Count; i++)
                {
                    ctx.Assert(picked[i] != buddy && !picked[i].IsBuddy,
                        "the buddy was recruited alongside the resident");
                }

                ctx.Assert(!buddy.IsOnVisit, "the buddy was pulled onto the visit");

                ctx.Log($"town life: {scenesPlayed} building scenes played to completion " +
                        $"(buildings {Join(new List<int>(InteractionBuildingsChecked))}), visitors arrived " +
                        "at their plots and left with poses restored; the daycare fell back to 'any dino " +
                        "peeks'; a drafted visitor abandoned its scene instantly and finished the build; " +
                        "a walk buddy was refused by both the recruiter and GoVisit");
            }
            finally
            {
                cfg.TownVisitIntervalSeconds = savedInterval;
                cfg.TownVisitBeatSeconds = savedBeat;
                cfg.TownMaxVisits = savedMaxVisits;
                cfg.TownSecondsPerBuildState = savedPerState;
                gm.Save.Data.TownNextIndex = savedNext;
                gm.Save.Data.TownBuildings = savedList ?? new List<TownBuildingSave>();
                gm.Save.Data.TreasureCount = savedWallet;
                gm.TestReset();
            }
        }

        // ====================================== DinoDigger-6or price curve / build order

        // How many buildings the price-curve case drives end to end. Three is enough to prove
        // the queue walks the CURATED order (0 -> 1 -> 2, never skipping or repeating) while
        // keeping the case's runtime sane — the remaining six plots are covered by the order
        // + plot-distinctness assertions that run over the whole roster.
        private const int PriceCurveBuildsChecked = 3;

        // The curated roster builds strictly in price order, one plot at a time, and NOTHING
        // breaks ground until the wallet clears the NEXT price exactly. Proves: (a) the town
        // ships one plot per price (nine of each, no cap at four); (b) the curve ascends, so
        // curated order == price order; (c) one coin short of price[i] leaves the site closed,
        // however long the town polls; (d) the coin that clears it breaks ground on plot i —
        // and only plot i — draining the wallet by exactly that price; and (e) after building
        // i finishes, the queue advances one slot and then WAITS on price[i+1].
        private IEnumerator Case_PriceCurveOrdersBuilds(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            TownController town = EnsureTown(ctx);
            GameConfig cfg = gm.TestConfig;
            ctx.Assert(town.TestArea != null && town.TestArea.PlotCount >= PriceCurveBuildsChecked,
                $"need >={PriceCurveBuildsChecked} plots for the price-curve test " +
                $"(have {(town.TestArea != null ? town.TestArea.PlotCount : 0)})");

            float savedPerState = cfg.TownSecondsPerBuildState;
            int savedWallet = gm.Save.Data.TreasureCount;

            var started = new List<int>();
            Action<int> onStart = idx => started.Add(idx);
            var finished = new List<int>();
            Action<int> onFin = idx => finished.Add(idx);
            GameEvents.TownBuildStarted += onStart;
            GameEvents.BuildingFinished += onFin;
            try
            {
                cfg.TownSecondsPerBuildState = 0.15f; // accelerate worked-time per state
                gm.Save.Data.TreasureCount = 0;       // start broke so nothing auto-starts early

                // (a) One plot per curated price — the whole nine-entry curve is reachable.
                int prices = cfg.TownBuildingPrices != null ? cfg.TownBuildingPrices.Length : 0;
                ctx.Assert(prices == 9, $"curated price curve has {prices} entries (expected 9)");
                ctx.Assert(town.TestArea.PlotCount >= prices,
                    $"town has {town.TestArea.PlotCount} plots for {prices} prices — the tail of the " +
                    "curated roster can never break ground");

                // (b) Curated order IS price order, and every plot is a distinct spot on the map
                //     (a duplicated plot would let two buildings stack invisibly).
                for (int i = 1; i < prices; i++)
                {
                    ctx.Assert(cfg.TownBuildingPrice(i) > cfg.TownBuildingPrice(i - 1),
                        $"price[{i}] ({cfg.TownBuildingPrice(i)}) does not exceed price[{i - 1}] " +
                        $"({cfg.TownBuildingPrice(i - 1)}) — curated order is not price order");
                }

                for (int i = 0; i < town.TestArea.PlotCount; i++)
                {
                    for (int j = i + 1; j < town.TestArea.PlotCount; j++)
                    {
                        ctx.Assert((town.TestArea.PlotWorld(i) - town.TestArea.PlotWorld(j)).sqrMagnitude > 0.25f,
                            $"plots {i} and {j} sit on top of each other");
                    }
                }

                // A Big crew so each site finishes promptly and the queue keeps moving.
                DinoController b1 = gm.TestSpawnDino(DinoType.TRex, GrowthStage.Big);
                DinoController b2 = gm.TestSpawnDino(DinoType.Stegosaurus, GrowthStage.Big);
                gm.TestMakeResident(b1, teleportIntoMeadow: true);
                gm.TestMakeResident(b2, teleportIntoMeadow: true);
                yield return ctx.WaitFrames(2);

                for (int i = 0; i < PriceCurveBuildsChecked; i++)
                {
                    int price = cfg.TownBuildingPrice(i);
                    ctx.Assert(town.TestNextIndex == i,
                        $"queue index {town.TestNextIndex} != {i} before building {i}");
                    ctx.Assert(town.TestActiveSite == null,
                        $"a site was already active before building {i} was funded");

                    // (c) One coin SHORT: the town polls every frame and still refuses to start.
                    gm.Save.Data.TreasureCount = price - 1;
                    yield return ctx.WaitFrames(20);
                    ctx.Assert(town.TestActiveSite == null,
                        $"building {i} broke ground with {price - 1} coins in hand (price {price})");
                    ctx.Assert(gm.Save.Data.TreasureCount == price - 1,
                        $"wallet moved ({gm.Save.Data.TreasureCount}) while under building {i}'s price");
                    ctx.Assert(started.Count == i,
                        $"{started.Count} builds had started before building {i} was affordable (expected {i})");

                    // (d) The coin that clears the price breaks ground on plot i, and the wallet
                    //     drains by exactly that price (we funded it to the coin, so it hits 0).
                    gm.Save.Data.TreasureCount = price;
                    yield return ctx.WaitUntil(() => town.TestActiveSite != null);
                    ctx.Assert(gm.Save.Data.TreasureCount == 0,
                        $"building {i} left {gm.Save.Data.TreasureCount} coins behind (price {price} " +
                        "should have drained the wallet exactly)");
                    ctx.Assert(started.Count == i + 1 && started[i] == i,
                        $"TownBuildStarted fired for {Join(started)} (expected the curated order up to {i})");
                    ctx.Assert((town.TestActiveSite.transform.position - town.TestArea.PlotWorld(i)).sqrMagnitude < 0.01f,
                        $"building {i} broke ground away from plot {i}");

                    // (e) The crew finishes it and the queue advances exactly one slot.
                    yield return ctx.WaitUntil(() => town.TestNextIndex == i + 1);
                    ctx.Assert(finished.Count == i + 1 && finished[i] == i,
                        $"BuildingFinished fired for {Join(finished)} (expected the curated order up to {i})");
                    ctx.Assert(town.TestActiveSite == null,
                        $"a new site broke ground on an empty wallet right after building {i}");
                    ctx.Assert(gm.Save.Data.TreasureCount == 0,
                        $"wallet is {gm.Save.Data.TreasureCount} after building {i} (expected 0)");
                }

                // ...and with the wallet parked one coin under the NEXT price, the town holds.
                int nextPrice = cfg.TownBuildingPrice(PriceCurveBuildsChecked);
                gm.Save.Data.TreasureCount = nextPrice - 1;
                yield return ctx.WaitFrames(20);
                ctx.Assert(town.TestActiveSite == null && town.TestNextIndex == PriceCurveBuildsChecked,
                    $"building {PriceCurveBuildsChecked} broke ground {nextPrice - 1} coins in " +
                    $"(price {nextPrice})");

                ctx.Log($"price curve ({string.Join("/", cfg.TownBuildingPrices)}) drove {PriceCurveBuildsChecked} " +
                        $"builds in curated order {Join(finished)}: each waited one coin short, broke ground on " +
                        "its own plot, drained the wallet exactly, then handed off to the next price");
            }
            finally
            {
                GameEvents.TownBuildStarted -= onStart;
                GameEvents.BuildingFinished -= onFin;
                cfg.TownSecondsPerBuildState = savedPerState;
                gm.Save.Data.TreasureCount = savedWallet;
                gm.TestReset();
            }
        }

        // ==================================== DinoDigger-s90 growth-stage build speed

        // Frames per measurement window. Long enough that the accrual delta dwarfs float
        // noise, short enough that three windows cost well under a second.
        private const int BuildRateWindowFrames = 40;

        // Growth pays a build dividend: a builder contributes work scaled by its stage
        // (Baby x1.0, Kid x1.6, Big x2.5), so a grown-up crew raises a building measurably
        // faster than a baby crew. NO WALL-CLOCK RACE and no second build: the SAME crew on
        // the SAME site is re-staged in place and its accrual measured as banked-work per
        // ticked-second (TownController advances both counters inside one tick, so the ratio
        // is exact). Per-state time is parked at 1000s so the site never advances during the
        // measurement — nothing here depends on how fast frames happen to run.
        private IEnumerator Case_BigDinoBuildsFaster(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            TownController town = EnsureTown(ctx);
            GameConfig cfg = gm.TestConfig;

            float savedPerState = cfg.TownSecondsPerBuildState;
            int savedWallet = gm.Save.Data.TreasureCount;
            try
            {
                // The design-doc curve itself, read through the sanitising accessor (so an old
                // GameConfig asset that deserialized the fields to 0 fails HERE, loudly).
                float mBaby = cfg.BuildSpeedFor(GrowthStage.Baby);
                float mKid = cfg.BuildSpeedFor(GrowthStage.Kid);
                float mBig = cfg.BuildSpeedFor(GrowthStage.Big);
                ctx.Assert(Mathf.Abs(mBaby - 1.0f) < 0.001f, $"Baby build speed {mBaby} != 1.0");
                ctx.Assert(Mathf.Abs(mKid - 1.6f) < 0.001f, $"Kid build speed {mKid} != 1.6");
                ctx.Assert(Mathf.Abs(mBig - 2.5f) < 0.001f, $"Big build speed {mBig} != 2.5");

                cfg.TownSecondsPerBuildState = 1000f; // the site cannot advance mid-measurement
                gm.Save.Data.TreasureCount = 0;

                // Two residents, spawned Big (a Big dino is never hungry, so nothing pulls it
                // off site) and re-staged in place once they are working.
                DinoController b1 = gm.TestSpawnDino(DinoType.TRex, GrowthStage.Big);
                DinoController b2 = gm.TestSpawnDino(DinoType.Stegosaurus, GrowthStage.Big);
                gm.TestMakeResident(b1, teleportIntoMeadow: true);
                gm.TestMakeResident(b2, teleportIntoMeadow: true);
                yield return ctx.WaitFrames(2);

                gm.Save.Data.TreasureCount = cfg.TownBuildingPrice(0);
                yield return ctx.WaitUntil(() => town.TestActiveSite != null);
                BuildingController site = town.TestActiveSite;

                // Wait for the WHOLE crew to clock in, so the crew size is stable across all
                // three windows and the rates are directly comparable.
                yield return ctx.WaitUntil(() => town.TestBuilderCount >= 2 && AllBuildersWorking(town));
                int crew = town.TestBuilderCount;
                ctx.Assert(crew == 2, $"crew is {crew} builders (expected 2 for the comparison)");

                var rate = new float[1];

                // --- adult crew ---
                yield return MeasureBuildRate(ctx, town, rate);
                float bigRate = rate[0];
                ctx.Assert(Mathf.Abs(bigRate - crew * mBig) < 0.01f,
                    $"adult crew banked {bigRate:0.###} work/s (expected {crew} x {mBig} = {crew * mBig:0.###})");

                // --- same crew, re-staged to Kid ---
                b1.ForceStage(GrowthStage.Kid);
                b2.ForceStage(GrowthStage.Kid);
                yield return ctx.WaitFrames(2);
                ctx.Assert(AllBuildersWorking(town), "a builder left the site when it was re-staged to Kid");
                yield return MeasureBuildRate(ctx, town, rate);
                float kidRate = rate[0];
                ctx.Assert(Mathf.Abs(kidRate - crew * mKid) < 0.01f,
                    $"kid crew banked {kidRate:0.###} work/s (expected {crew} x {mKid} = {crew * mKid:0.###})");

                // --- same crew, re-staged to Baby ---
                b1.ForceStage(GrowthStage.Baby);
                b2.ForceStage(GrowthStage.Baby);
                yield return ctx.WaitFrames(2);
                ctx.Assert(AllBuildersWorking(town), "a builder left the site when it was re-staged to Baby");
                yield return MeasureBuildRate(ctx, town, rate);
                float babyRate = rate[0];
                ctx.Assert(Mathf.Abs(babyRate - crew * mBaby) < 0.01f,
                    $"baby crew banked {babyRate:0.###} work/s (expected {crew} x {mBaby} = {crew * mBaby:0.###})");

                // The payoff, stated the way a player feels it: strictly ordered, and an adult
                // crew raises the SAME building in well under half a baby crew's time (time to
                // finish = total work / rate, so the time ratio is the inverse of the rates).
                ctx.Assert(bigRate > kidRate && kidRate > babyRate,
                    $"build rates are not ordered by growth stage (baby {babyRate:0.###}, " +
                    $"kid {kidRate:0.###}, big {bigRate:0.###})");
                float timeRatio = bigRate > 0f ? babyRate / bigRate : 1f;
                ctx.Assert(timeRatio < 0.45f,
                    $"an adult crew only finishes {1f / Mathf.Max(0.0001f, timeRatio):0.##}x faster than a " +
                    "baby crew (expected ~2.5x)");

                // ...and the banked work really landed IN the building, not just in a counter:
                // with no state boundary crossed, the site's partial IS the total banked work.
                ctx.Assert(Mathf.Abs(site.WorkedPartial - town.TestWorkBanked) < 0.05f,
                    $"site banked {site.WorkedPartial:0.###}s but the crew contributed " +
                    $"{town.TestWorkBanked:0.###}s — the stage-scaled work is not reaching the building");
                ctx.Assert(site.State == 0,
                    $"site advanced to state {site.State} mid-measurement (per-state time was not parked)");

                ctx.Log($"growth-stage build speed: the same {crew}-dino crew banked {babyRate:0.##} work/s as " +
                        $"babies, {kidRate:0.##} as kids and {bigRate:0.##} as adults " +
                        $"(x1.0 / x1.6 / x2.5) — an adult crew finishes the same building " +
                        $"{1f / timeRatio:0.##}x faster");
            }
            finally
            {
                cfg.TownSecondsPerBuildState = savedPerState;
                gm.Save.Data.TreasureCount = savedWallet;
                gm.TestReset();
            }
        }

        /// <summary>Measure the active site's build-work accrual as banked work-seconds per
        /// TICKED second, over a fixed frame window. Both counters are advanced inside the same
        /// <c>TickActiveSite</c> call, so the ratio is exact regardless of frame rate, editor
        /// hitches or how the coroutine interleaves with Update — there is no wall clock in it.</summary>
        private IEnumerator MeasureBuildRate(TestContext ctx, TownController town, float[] outRate)
        {
            float w0 = town.TestWorkBanked;
            float e0 = town.TestWorkElapsed;
            yield return ctx.WaitFrames(BuildRateWindowFrames);

            float dw = town.TestWorkBanked - w0;
            float de = town.TestWorkElapsed - e0;
            ctx.Assert(de > 0.01f,
                $"no crew time accrued over {BuildRateWindowFrames} frames (the site stalled)");
            outRate[0] = dw / de;
        }

        // ================================================================= HELPERS

        /// <summary>Return the scene's town (real or previously-injected), building a small
        /// TownArea near the meadow + injecting a TownController when the district has not
        /// been placed yet. Idempotent across cases: reuses the injected town/area.</summary>
        private TownController EnsureTown(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            TownController town = gm.TestTown;
            if (town == null)
            {
                town = UnityEngine.Object.FindFirstObjectByType<TownController>();
            }

            TownArea area = town != null ? town.TestArea : null;
            if (area == null || area.PlotCount == 0)
            {
                area = UnityEngine.Object.FindFirstObjectByType<TownArea>();
            }

            if (area == null || area.PlotCount == 0)
            {
                area = BuildTestTownArea(gm);
            }

            if (town == null)
            {
                var go = new GameObject("~TestTownController");
                town = go.AddComponent<TownController>();
            }

            town.Configure(area, gm.TestLibrary, gm.TestConfig);
            gm.TestInstallTown(town);
            town.TestResetTown();
            return town;
        }

        /// <summary>Build a 3-plot TownArea on walkable ground a short walk from the
        /// backhoe (the island is fully connected, so residents can always path here).</summary>
        private TownArea BuildTestTownArea(GameManager gm)
        {
            OverworldMap map = gm.TestMap;
            var go = new GameObject("~TestTownArea");
            var area = go.AddComponent<TownArea>();

            Vector3 anchor = gm.TestBackhoe != null ? gm.TestBackhoe.transform.position : Vector3.zero;
            Vector3[] offs =
            {
                new Vector3(2f, 1.5f, 0f), new Vector3(3.2f, 0.3f, 0f), new Vector3(2f, -1.2f, 0f)
            };

            var plots = new List<Vector3>();
            for (int i = 0; i < offs.Length; i++)
            {
                Vector3 w = map != null ? map.NearestWalkable(anchor + offs[i], out _) : anchor + offs[i];
                plots.Add(w);
            }

            area.Configure(map, plots[0], plots, 4f);
            return area;
        }

        private bool AnyBuilderWorking(TownController town)
        {
            IReadOnlyList<DinoController> crew = town.TestBuilders;
            for (int i = 0; i < crew.Count; i++)
            {
                if (crew[i] != null && crew[i].IsWorking)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when the crew is non-empty and EVERY drafted builder has clocked in
        /// (nobody still commuting) — the point at which the site's work rate is stable.</summary>
        private bool AllBuildersWorking(TownController town)
        {
            IReadOnlyList<DinoController> crew = town.TestBuilders;
            if (crew.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < crew.Count; i++)
            {
                if (crew[i] == null || !crew[i].IsWorking)
                {
                    return false;
                }
            }

            return true;
        }

        private DinoController FirstWorkingBuilder(TownController town)
        {
            IReadOnlyList<DinoController> crew = town.TestBuilders;
            for (int i = 0; i < crew.Count; i++)
            {
                if (crew[i] != null && crew[i].IsWorking)
                {
                    return crew[i];
                }
            }

            return null;
        }

        /// <summary>Route a REAL world tap (through GameManager.FindTappable) onto a finished
        /// building at a point where the building is the ONLY ITappable, so the case asserts the
        /// building's own behaviour rather than the tap-priority order. Returns false if no clear
        /// point exists (the whole footprint is covered by another tappable — effectively never).</summary>
        private bool RoutedTapOnBuilding(GameManager gm, BuildingController b, int index)
        {
            if (!FindBuildingOnlyPoint(b, out Vector3 p))
            {
                return false;
            }

            gm.TestTapWorldRouted(p);
            return true;
        }

        /// <summary>A point on this building's collider where NOTHING else tappable overlaps.
        /// Tries spots high on the sprite first (least likely to share ground with a mound).</summary>
        private bool FindBuildingOnlyPoint(BuildingController b, out Vector3 point)
        {
            point = Vector3.zero;
            Collider2D col = b != null ? b.GetComponent<Collider2D>() : null;
            if (col == null)
            {
                return false;
            }

            Bounds bb = col.bounds;
            Vector3[] cands =
            {
                bb.center + new Vector3(0f, bb.extents.y * 0.6f, 0f),
                bb.center,
                bb.center + new Vector3(bb.extents.x * 0.5f, bb.extents.y * 0.3f, 0f),
                bb.center + new Vector3(-bb.extents.x * 0.5f, bb.extents.y * 0.3f, 0f),
                bb.center + new Vector3(0f, -bb.extents.y * 0.4f, 0f),
            };

            for (int c = 0; c < cands.Length; c++)
            {
                if (OnlyBuildingTappable(cands[c], b))
                {
                    point = cands[c];
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when the ONLY ITappable overlapping <paramref name="p"/> is
        /// <paramref name="b"/> — so GameManager.FindTappable resolves a tap there to this
        /// building whatever the tap-priority order says.</summary>
        private bool OnlyBuildingTappable(Vector3 p, BuildingController b)
        {
            Collider2D[] hits = Physics2D.OverlapPointAll(p);
            bool foundBuilding = false;
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i] == null)
                {
                    continue;
                }

                var t = hits[i].GetComponent<ITappable>() ?? hits[i].GetComponentInParent<ITappable>();
                if (t == null)
                {
                    continue; // non-tappable collider (ground/stream): FindTappable skips it
                }

                bool isBuilding = hits[i].GetComponent<BuildingController>() == b ||
                                  hits[i].GetComponentInParent<BuildingController>() == b;
                if (isBuilding)
                {
                    foundBuilding = true;
                }
                else
                {
                    return false; // another tappable overlaps -> ambiguous, skip this point
                }
            }

            return foundBuilding;
        }

        private static string Join(List<int> xs) => string.Join(",", xs);
    }
}
