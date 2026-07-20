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
    /// Dino Town (Phase 1) integration cases: the economy/build-queue and the builder
    /// NPC loop + celebration, plus the HARD-RULE case that proves town construction
    /// never commandeers the player backhoe or a walk buddy.
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
            ctx.Log("built scene ships a live TownController wired into GameManager._town " +
                    "with a 9-plot TownArea");
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

            // A buddy that must keep following the PLAYER, plus two residents so the town
            // has a genuine crew and construction is really active.
            DinoController buddy = gm.TestSpawnDino(DinoType.TRex, GrowthStage.Kid);
            DinoController res1 = gm.TestSpawnDino(DinoType.Triceratops, GrowthStage.Big);
            DinoController res2 = gm.TestSpawnDino(DinoType.Stegosaurus, GrowthStage.Big);
            gm.TestMakeResident(res1, teleportIntoMeadow: true);
            gm.TestMakeResident(res2, teleportIntoMeadow: true);
            yield return ctx.WaitFrames(2);
            ctx.Assert(buddy.IsBuddy, "buddy is not a buddy");

            gm.Save.Data.TreasureCount = gm.TestConfig.TownBuildingPrice(0);
            yield return ctx.WaitUntil(() => town.TestActiveSite != null);
            yield return ctx.WaitUntil(() => AnyBuilderWorking(town)); // build genuinely underway

            // (1) The buddy is never drafted onto the crew.
            IReadOnlyList<DinoController> crew = town.TestBuilders;
            for (int i = 0; i < crew.Count; i++)
            {
                ctx.Assert(crew[i] != buddy, "the buddy was drafted to build (forbidden)");
            }

            // (2) Parked backhoe holds position for 2s of active construction — nothing
            // commandeers it or nudges it toward the site.
            Vector3 park = FindDistinctWalkable(map, bh.transform.position);
            bh.MoveTo(park);
            yield return ctx.WaitUntil(() => !bh.IsMoving);
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

            // (3) Player tap-to-move still works normally, mid-construction.
            Vector3 moveTarget = FindDistinctWalkable(map, bh.transform.position);
            ctx.Assert((moveTarget - bh.transform.position).sqrMagnitude > 0.25f, "no distinct move target");
            gm.TestTapWorldRouted(moveTarget);
            yield return ctx.WaitUntil(() => !bh.IsMoving);
            ctx.Assert((bh.transform.position - held).sqrMagnitude > 0.25f,
                "tap-to-move did not move the backhoe during construction");
            ctx.Assert(map.IsWalkableWorld(bh.transform.position), "backhoe ended off a walkable cell");

            // (4) The buddy stays a follower (not pulled to the town) and follows the player.
            ctx.Assert(buddy.IsBuddy, "buddy stopped being a buddy during construction");
            yield return ctx.WaitUntil(() =>
                buddy != null && (buddy.transform.position - bh.transform.position).magnitude < 3.5f);

            // (5) Dig entry still works while the town builds.
            DigMound m = FirstActiveMound(gm);
            ctx.Assert(m != null, "no active mound to dig");
            bh.DriveToMound(m);
            yield return ctx.WaitUntil(() => gm.State.Is(GameState.Dig));
            ctx.Assert(gm.State.Is(GameState.Dig), "could not enter dig during construction");

            ctx.Log("during active construction: backhoe held then moved on tap, buddy kept following, dig entry worked");
            gm.TestForceRoam();
            gm.TestReset();
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
        /// building, choosing a point on its collider where the building is the ONLY ITappable so
        /// the routing is deterministic regardless of OverlapPointAll ordering (and immune to a
        /// respawned mound clipping part of the footprint). Returns false if no clear point exists
        /// (the whole footprint is covered by another tappable — effectively never).</summary>
        private bool RoutedTapOnBuilding(GameManager gm, BuildingController b, int index)
        {
            Collider2D col = b != null ? b.GetComponent<Collider2D>() : null;
            if (col == null)
            {
                return false;
            }

            Bounds bb = col.bounds;
            // Candidates high on the sprite first (least likely to collide with a ground mound).
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
                    gm.TestTapWorldRouted(cands[c]);
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when the ONLY ITappable overlapping <paramref name="p"/> is
        /// <paramref name="b"/> — so GameManager.FindTappable (which returns the first ITappable
        /// hit) is guaranteed to resolve a tap there to this building.</summary>
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
