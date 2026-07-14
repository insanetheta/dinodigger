using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;
using DinoDigger.Overworld;

namespace DinoDigger.Testing
{
    /// <summary>
    /// Dino Town (Phase 1) integration cases: the economy/build-queue and the builder
    /// NPC loop + celebration, plus the HARD-RULE case that proves town construction
    /// never commandeers the player backhoe or a walk buddy.
    ///
    /// SceneBuilder ships a live, wired town (TownController + a 4-plot TownArea on the
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
        // TownController (with its 4-plot TownArea) to the "Town" root and strict-wires it
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
            ctx.Assert(town.TestArea.PlotCount == 4,
                $"scene town has {town.TestArea.PlotCount} plots (expected 4)");
            ctx.Log("built scene ships a live TownController wired into GameManager._town " +
                    "with a 4-plot TownArea");
            yield break;
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

        private static string Join(List<int> xs) => string.Join(",", xs);
    }
}
