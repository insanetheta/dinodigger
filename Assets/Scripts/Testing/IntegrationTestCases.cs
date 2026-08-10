using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using DinoDigger.Config;
using DinoDigger.Core;
using DinoDigger.Dig;
using DinoDigger.Managers;
using DinoDigger.Overworld;

namespace DinoDigger.Testing
{
    /// <summary>The concrete play-through test cases. See IntegrationTestRunner.cs for the driver.</summary>
    public partial class IntegrationTestRunner
    {
        /// <summary>Island size in cells, matching SceneBuilder's N — the bound for the
        /// whole-map scans below (walkable sweep, water flood-fill).</summary>
        private const int MapCells = 48;

        private List<TestCase> BuildCases()
        {
            return new List<TestCase>
            {
                // Realtime budgets. Two rules of thumb, both learned the hard way: size a
                // budget from the WORK a case does (legs driven, tiles dug) rather than a
                // round number, and leave enough slack that a load hitch on a machine running
                // two editors can never fail a case that is making progress. A case that
                // needs its full budget is broken, not slow.
                new TestCase("RoamTapToMove",        40f, Case_RoamTapToMove),
                new TestCase("PathfindingAnywhere", 200f, Case_PathfindingAnywhere),
                new TestCase("EightDirFacing",       25f, Case_EightDirFacing),
                // 8 legs x up to 3 re-drives, each with its own distance-proportional budget.
                new TestCase("FacingCorrectness",    90f, Case_FacingCorrectness),
                new TestCase("FacingStability",      30f, Case_FacingStability),
                new TestCase("MoundToDig",           20f, Case_MoundToDig),
                new TestCase("DirtTileDamage",       20f, Case_DirtTileDamage),
                new TestCase("PeekVisible",          20f, Case_PeekVisible),
                new TestCase("MultiItemCollection",  60f, Case_MultiItemCollection),
                new TestCase("DigThemes",            25f, Case_DigThemes),
                new TestCase("TileHardness",         25f, Case_TileHardness),
                new TestCase("EggHatch",             20f, Case_EggHatch),
                new TestCase("UniqueDinoNoDupes",    20f, Case_UniqueDinoNoDupes),
                // Replaces the retired ShardDropRate: the egg nerf still collapses egg drops
                // once every species is owned, but the freed weight is treasure now and the
                // late-game COLLECTION is the buried fossil bone. NestAssembly and
                // ShardHatchCeremony retired WITH their systems (the egg-shard nest and its
                // hatch); their behaviour lives on as SkeletonBoardFills and
                // ReviveCeremonyJoins below.
                new TestCase("BoneDropRate",         25f, Case_BoneDropRate),
                new TestCase("FruitPunchNoCompound", 20f, Case_FruitPunchNoCompound),
                new TestCase("FeedAndGrow",          25f, Case_FeedAndGrow),
                new TestCase("GrowthStageArt",       15f, Case_GrowthStageArt),
                new TestCase("DinoDance",            15f, Case_DinoDance),
                new TestCase("BigDinoHelps",         20f, Case_BigDinoHelps),
                new TestCase("TreasureCounter",      30f, Case_TreasureCounter),
                new TestCase("MoundRespawn",         20f, Case_MoundRespawn),
                new TestCase("IdleAttract",          10f, Case_IdleAttract),
                new TestCase("SaveRoundtrip",        10f, Case_SaveRoundtrip),
                new TestCase("ParentGateMute",       10f, Case_ParentGateMute),
                new TestCase("DinoIdleStable",       25f, Case_DinoIdleStable),
                new TestCase("WalkAnimCycles",       30f, Case_WalkAnimCycles),
                new TestCase("BackhoeRollCycles",    30f, Case_BackhoeRollCycles),
                new TestCase("BuddyCapTwo",          35f, Case_BuddyCapTwo),
                new TestCase("BuddySwapOnTap",       25f, Case_BuddySwapOnTap),
                new TestCase("MeadowContainsResidents", 25f, Case_MeadowContainsResidents),
                new TestCase("MoundsAvoidMeadow",    20f, Case_MoundsAvoidMeadow),
                new TestCase("BrachioTreeShake",     30f, Case_BrachioTreeShake),
                new TestCase("AnkyRockSmash",        40f, Case_AnkyRockSmash),
                new TestCase("StegoSniff",           25f, Case_StegoSniff),
                new TestCase("TrikeCarry",           35f, Case_TrikeCarry),
                new TestCase("ParadeOnce",           30f, Case_ParadeOnce),
                new TestCase("StreamsConnectivity",  15f, Case_StreamsConnectivity),
                new TestCase("EnvDressingApplied",   20f, Case_EnvDressingApplied), // body: IntegrationTestCasesEnv.cs (DinoDigger-y1g)
                new TestCase("DuckCatch",            40f, Case_DuckCatch),
                new TestCase("TownAvoidsMoundAndStream", 25f, Case_TownAvoidsMoundAndStream),
                new TestCase("TownWiredInScene",         10f, Case_TownWiredInScene),
                new TestCase("TownStatePersists",        15f, Case_TownStatePersists),
                new TestCase("CoinsAutoSpendStartsBuild",    25f, Case_CoinsAutoSpendStartsBuild),
                new TestCase("BuildAdvancesThroughStates",   45f, Case_BuildAdvancesThroughStates),
                new TestCase("BuilderCommutesFromMeadow",    45f, Case_BuilderCommutesFromMeadow),
                // Headroom for the completion choreography's debut visit (DinoDigger-0gd): if a
                // builder happens to be home and free when the debut fires, it may stroll back
                // to the new building before finally settling in the meadow.
                new TestCase("BuildingFinishesAndCelebrates", 70f, Case_BuildingFinishesAndCelebrates),
                // Includes a cross-island builder commute (40s of its own budget).
                new TestCase("PlayerControlUnaffectedByBuild", 100f, Case_PlayerControlUnaffectedByBuild),
                new TestCase("TapPriorityOverlap",    40f, Case_TapPriorityOverlap),
                new TestCase("PriceCurveOrdersBuilds", 90f, Case_PriceCurveOrdersBuilds),
                new TestCase("BigDinoBuildsFaster",    45f, Case_BigDinoBuildsFaster),
                // Re-measures live dino sprites against the baked hard-hat/mallet anchors,
                // so a dino-art re-slice can never again leave the crew's gear floating
                // (DinoDigger-rip). Body: IntegrationTestCasesTown.cs.
                new TestCase("BuilderAnchorsMatchArt", 40f, Case_BuilderAnchorsMatchArt),
                new TestCase("FruitStandSellsSurplus", 40f, Case_FruitStandSellsSurplus),
                new TestCase("SnackBuilders",         45f, Case_SnackBuilders),
                new TestCase("RecessTime",            45f, Case_RecessTime),
                new TestCase("EachBuildingPlaysInteraction", 150f, Case_EachBuildingPlaysInteraction),
                // Four 40-frame measurement windows plus a meadow-to-plot commute.
                new TestCase("TapToCheerSpeedsBuild",  70f, Case_TapToCheerSpeedsBuild),
                // A whole build, the celebration beat, the debut interaction, and the walk home.
                new TestCase("CelebrationNoConsoleErrors", 120f, Case_CelebrationNoConsoleErrors),
                new TestCase("AttractShowsTownGrowth",  90f, Case_AttractShowsTownGrowth),
                new TestCase("BerryPatch",            40f, Case_BerryPatch),
                // Runs late (after the count-exact TreasureCounter and the town cases): a
                // buddy dig can finish a round and bank a random amount of treasure, which
                // would inflate the persistent wallet over the town's build threshold and
                // let the always-on town builder spend during a count-exact case.
                new TestCase("BuddyDigCrew",         80f, Case_BuddyDigCrew),
                // Runs late alongside BuddyDigCrew: a fired Giggle Pocket banks coins, which
                // would inflate the wallet ahead of the count-exact treasure/town cases.
                new TestCase("SurprisePocket",       90f, Case_SurprisePocket),
                // Gravity cascade cases run late for the same reason as the two above: a
                // cascade can uncover the last buried item and finish the round, which banks a
                // random amount of treasure into the persistent wallet.
                new TestCase("TilesFallAndSettle",   60f, Case_TilesFallAndSettle),
                new TestCase("CascadeNeverWedges",   90f, Case_CascadeNeverWedges),
                // Dig toys (DinoDigger-z4d) run late for the same wallet reason as everything
                // above: every one of them BANKS COINS on purpose, which would inflate the
                // persistent wallet ahead of a count-exact treasure/town case.
                new TestCase("CrystalPopFloodFill",  60f, Case_CrystalPopFloodFill),
                new TestCase("BoomChainsResolve",    70f, Case_BoomChainsResolve),
                new TestCase("PinataPotPays",        60f, Case_PinataPotPays),
                // The "every dig has a toy" guarantee (DinoDigger-qhy): builds several sites
                // back to back off-screen, so it is quick, but it runs with the toy roller LIVE
                // and can therefore bank coins — late, like every case above it.
                new TestCase("EveryDigHasAToy",      45f, Case_EveryDigHasAToy),
                // Multi-cell fossil bones (DinoDigger-0z5). Late for two reasons: it owns all
                // four egg species (which changes the loot table for anything after it until the
                // next reset) and a bone pop is a reward beat.
                new TestCase("BoneSpansCells",       60f, Case_BoneSpansCells),
                // The fossil finale (DinoDigger-5ve / -3rz). Late for the same reasons as
                // BoneSpansCells — they own every egg species and bank coins — and in
                // dependency order: the board fills, the machine is dug out, the ceremony
                // runs, and only then can a bone be a duplicate. Bodies live in
                // IntegrationTestCasesFossil.cs.
                new TestCase("SkeletonBoardFills",   60f, Case_SkeletonBoardFills),
                new TestCase("MachineExcavates",     90f, Case_MachineExcavates),
                new TestCase("ReviveCeremonyJoins",  90f, Case_ReviveCeremonyJoins),
                new TestCase("DuplicateBonePaysOut", 45f, Case_DuplicateBonePaysOut),
                // Dig-arm V2 live swap (DinoDigger-rrn): digs full tiles, so it can
                // finish a round and bank treasure — late, like the cases above. Body
                // lives in IntegrationTestCasesDigArm.cs.
                new TestCase("DigArmV2Swaps",        60f, Case_DigArmV2Swaps),
                // Machine Friends (DinoDigger-b48). Bodies live in IntegrationTestCasesMachines.cs.
                new TestCase("DoodleDanceParty",     70f, Case_DoodleDanceParty),
                new TestCase("SprinklesRipensOnTap", 70f, Case_SprinklesRipensOnTap),
                new TestCase("TuggyTowsDucklings",   70f, Case_TuggyTowsDucklings),
                new TestCase("MachineDiscoveryQueue", 45f, Case_MachineDiscoveryQueue),
                // DIG LOOP 2.0 D3 (DinoDigger-dv1 / -84f / -u47 / -6tc). Bodies live in
                // IntegrationTestCasesDepth.cs. Late, like every dig case above them: all of
                // them bank coins (toys pay), and the mega-fossil case owns every egg species
                // and completes a whole skeleton — the loudest state change in the suite.
                new TestCase("LadderDescends",       60f, Case_LadderDescends),
                new TestCase("DeeperLayerRicher",    60f, Case_DeeperLayerRicher),
                new TestCase("WaterPocketWashes",    45f, Case_WaterPocketWashes),
                new TestCase("CritterCatchable",     45f, Case_CritterCatchable),
                new TestCase("GemVeinChains",        45f, Case_GemVeinChains),
                new TestCase("MushroomBoings",       45f, Case_MushroomBoings),
                new TestCase("GlowRevealsAdjacent",  60f, Case_GlowRevealsAdjacent),
                // Rarity before payoff: the landmark case rolls the whole island on a PRISTINE
                // board (no skeleton finished yet), so it runs ahead of the case that completes
                // one. Both own every egg species; both reset after themselves.
                new TestCase("MegaFossilOneAtATime", 60f, Case_MegaFossilOneAtATime),
                new TestCase("MegaFossilCompletes",  75f, Case_MegaFossilCompletes),
                // Dig audio (DinoDigger-7c4). Runs last before NoConsoleErrors because it
                // TOGGLES MUTE, which is persisted in PlayerPrefs: it restores the previous
                // value in a finally, but keeping it late means a missed restore cannot silence
                // the cases above it. Body lives in IntegrationTestCasesAudio.cs.
                new TestCase("AudioHooksFire",       45f, Case_AudioHooksFire),
                // PORTRAIT-FIRST FRAMING (DinoDigger-kgm / -avw). Bodies live in
                // IntegrationTestCasesFraming.cs. Late, and in this order, because the two live
                // ones SUBSTITUTE A PHONE-SHAPED SCREEN for a few frames: the camera and the HUD
                // canvas both reframe while they run, and anything measuring screen positions
                // during that would be measuring a different device. Each hands the real screen
                // back in a finally, and the runner clears the override again between cases.
                new TestCase("DigFitsPortrait",       10f, Case_DigFitsPortrait),
                new TestCase("MegaDigFitsPortrait",   10f, Case_MegaDigFitsPortrait),
                new TestCase("RoamZoomsOutInPortrait", 10f, Case_RoamZoomsOutInPortrait),
                new TestCase("AspectChangeReframesLive", 30f, Case_AspectChangeReframesLive),
                new TestCase("PortraitHudOnScreen",   20f, Case_PortraitHudOnScreen),
                // COVERAGE, not framing (DinoDigger-5k8.1): the camera can now see further, and
                // these two assert that something is PAINTED everywhere it looks. The dig one
                // drives a real dig (hence the fatter budget) so it checks live renderers rather
                // than only the geometry.
                new TestCase("DigBackdropCoversView", 60f, Case_DigBackdropCoversView),
                new TestCase("SeaCoversBeyondTheMap", 20f, Case_SeaCoversBeyondTheMap),
                new TestCase("NoConsoleErrors",       5f, Case_NoConsoleErrors),
            };
        }

        // ============================================================= ROAM / MOVE

        private IEnumerator Case_RoamTapToMove(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            BackhoeController bh = gm.TestBackhoe;
            OverworldMap map = gm.TestMap;
            ctx.Assert(bh != null && map != null, "missing backhoe/map");

            Vector3 start = bh.transform.position;
            Vector3 target = FindDistinctWalkable(map, start);
            ctx.Assert((target - start).sqrMagnitude > 0.25f, "no distinct walkable target found");

            ctx.TapWorld(target);
            yield return ctx.WaitUntil(() => !bh.IsMoving, LegBudget(start, target),
                "backhoe never arrived at the tapped walkable cell");

            Vector3 arrived = bh.transform.position;
            ctx.Assert(map.IsWalkableWorld(arrived), "backhoe ended on a non-walkable cell");
            ctx.Assert((arrived - start).sqrMagnitude > 0.25f, "backhoe did not move on a walkable tap");

            // Pond water: tap into the pond; the target must be clamped to land. The pond cell
            // is found from the map (DinoDigger-8e1) and is normally well off screen, so route
            // the tap through the same OnTap path without the world->screen conversion that
            // would silently DROP it — a dropped tap makes "the backhoe never reached the
            // water" true for the wrong reason, which is how this stopped testing the pond.
            ctx.Assert(FindBlockedPondCell(map, out Vector3Int waterCell), "could not locate a pond/water cell");
            Vector3 waterWorld = map.CellCenter(waterCell);
            float distBefore = (arrived - waterWorld).magnitude;
            gm.TestTapWorldRouted(waterWorld);
            yield return ctx.WaitUntil(() => !bh.IsMoving, LegBudget(arrived, waterWorld),
                "backhoe never settled after the water tap");

            Vector3 after = bh.transform.position;
            ctx.Assert(map.IsWalkableWorld(after), "backhoe entered a water cell");
            ctx.Assert(map.WorldToCell(after) != waterCell, "backhoe reached the water cell (not clamped)");
            if (distBefore > 2f)
            {
                // Only meaningful from a distance: parked on the shore already, the clamp
                // target IS where it stands and holding still is the correct outcome.
                ctx.Assert((after - waterWorld).magnitude < distBefore - 0.25f,
                    "water tap did not drive the backhoe to the near shore (silently ignored?)");
            }

            ctx.Log($"moved {map.WorldToCell(start)}->{map.WorldToCell(arrived)}; pond tap {waterCell} " +
                    $"clamped to shore {map.WorldToCell(after)}");
        }

        // Robustness guarantee (DinoDigger-e47): ONE tap = ONE guaranteed arrival.
        // Drive to a spread of seeded-random walkable targets across the whole island
        // (forcing routes around the pond and across the 1-cell stream bridges) and
        // assert every one arrives with ZERO honk-give-ups — the stall watchdog must
        // replan, never quit, while a route exists. Also taps DEEP water and asserts the
        // backhoe clamps to the near shore instead of no-opping.
        private IEnumerator Case_PathfindingAnywhere(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            BackhoeController bh = gm.TestBackhoe;
            OverworldMap map = gm.TestMap;
            ctx.Assert(bh != null && map != null, "missing backhoe/map");

            Vector3 start = bh.transform.position;
            Vector3Int startCell = map.WorldToCell(start);

            // Collect all walkable cells, then pick a seeded, well-spread subset so the
            // targets scatter across the island (deterministic across runs).
            var walkable = new List<Vector3Int>();
            for (int x = 0; x < 48; x++)
            {
                for (int y = 0; y < 48; y++)
                {
                    var c = new Vector3Int(x, y, 0);
                    if (map.IsWalkableCell(c))
                    {
                        walkable.Add(c);
                    }
                }
            }

            ctx.Assert(walkable.Count > 50, $"only {walkable.Count} walkable cells — map not built?");

            // Fisher-Yates with a fixed seed, then greedily keep cells that are far from
            // the start and from each other so the set spans the island (pond + streams).
            var rng = new System.Random(0xD1D0);
            for (int i = walkable.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (walkable[i], walkable[j]) = (walkable[j], walkable[i]);
            }

            const int wantTargets = 18;
            const int minSpread = 6; // Chebyshev cells between chosen targets
            var targets = new List<Vector3Int>();
            for (int i = 0; i < walkable.Count && targets.Count < wantTargets; i++)
            {
                Vector3Int c = walkable[i];
                if (Cheb(c, startCell) < minSpread)
                {
                    continue;
                }

                bool farEnough = true;
                for (int k = 0; k < targets.Count; k++)
                {
                    if (Cheb(c, targets[k]) < minSpread)
                    {
                        farEnough = false;
                        break;
                    }
                }

                if (farEnough)
                {
                    targets.Add(c);
                }
            }

            ctx.Assert(targets.Count >= 15, $"only found {targets.Count} well-spread targets (expected >= 15)");

            // Prove the set really spans the island (guards against a clustered sample).
            int minX = 48, minY = 48, maxX = 0, maxY = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                minX = Mathf.Min(minX, targets[i].x); maxX = Mathf.Max(maxX, targets[i].x);
                minY = Mathf.Min(minY, targets[i].y); maxY = Mathf.Max(maxY, targets[i].y);
            }

            ctx.Assert((maxX - minX) >= 24 && (maxY - minY) >= 24,
                $"targets not spread across the island (span {maxX - minX}x{maxY - minY})");

            int giveUpsBefore = bh.TestGiveUpCount;

            // Drive to each target; each must arrive on its cell with no give-up.
            for (int i = 0; i < targets.Count; i++)
            {
                Vector3 tgt = map.CellCenter(targets[i]);
                Vector3 from = bh.transform.position;
                bh.MoveTo(tgt);

                // Per-leg budget proportional to crow-flies distance (realtime; the
                // runner drives at 3x game speed, speed 3.5 u/s → ~0.1 s/unit, so
                // 0.5 s/unit is 5x slack for detours/replans), floor 6s, cap 20s.
                float crowFlies = (tgt - from).magnitude;
                float budget = Mathf.Clamp(6f + crowFlies * 0.5f, 6f, 20f);
                float deadline = Time.realtimeSinceStartup + budget;
                while (bh.IsMoving && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                Vector3 arrived = bh.transform.position;
                ctx.Assert(!bh.IsMoving,
                    $"target {i} {targets[i]} (from {map.WorldToCell(from)}) never arrived (stuck en route)");
                ctx.Assert(map.IsWalkableWorld(arrived),
                    $"target {i} ended on a non-walkable cell {map.WorldToCell(arrived)}");
                ctx.Assert(map.WorldToCell(arrived) == targets[i],
                    $"target {i} arrived at {map.WorldToCell(arrived)} != {targets[i]}");
                ctx.Assert(bh.TestGiveUpCount == giveUpsBefore,
                    $"backhoe honk-gave-up reaching target {i} {targets[i]} (give-ups now {bh.TestGiveUpCount})");
            }

            // ---- Deep-water tap: must clamp to the near shore, never a silent no-op. ----
            ctx.Assert(FindDeepWaterCell(map, out Vector3Int deep), "no interior pond water cell found");
            Vector3 deepWorld = map.CellCenter(deep);

            // Park the backhoe well away from the pond first so "moved toward the shore"
            // is an unambiguous signal (not already sitting on the near bank).
            Vector3Int anchor = targets[0];
            for (int i = 1; i < targets.Count; i++)
            {
                if (Cheb(targets[i], deep) > Cheb(anchor, deep))
                {
                    anchor = targets[i];
                }
            }

            bh.MoveTo(map.CellCenter(anchor));
            yield return ctx.WaitUntil(() => !bh.IsMoving);

            Vector3 preTap = bh.transform.position;
            float preDist = (preTap - deepWorld).magnitude;

            // Route a move straight AT the deep water: FindPath must clamp it to the near
            // shore (NearestWalkable + the toward-the-mover fallback) and drive there,
            // never honk. (The tap->reject pipeline itself is covered by RoamTapToMove;
            // this parks the backhoe far off, where an on-screen tap can't reach the pond.)
            bh.MoveTo(deepWorld);
            yield return ctx.WaitUntil(() => !bh.IsMoving);

            Vector3 afterTap = bh.transform.position;
            ctx.Assert(map.IsWalkableWorld(afterTap), "deep-water target put the backhoe in the water");
            ctx.Assert(map.WorldToCell(afterTap) != deep, "backhoe reached the deep water cell (not clamped)");
            ctx.Assert((afterTap - deepWorld).magnitude < preDist - 0.25f,
                "deep-water target did not move the backhoe toward the near shore");
            ctx.Assert(bh.TestGiveUpCount == giveUpsBefore, "deep-water target honk-gave-up instead of clamping");

            ctx.Log($"{targets.Count} spread targets all arrived (0 give-ups); " +
                    $"deep-water tap {deep} clamped to shore {map.WorldToCell(afterTap)}");
            gm.TestReset();
        }

        private static int Cheb(Vector3Int a, Vector3Int b)
        {
            return Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
        }

        /// <summary>An INTERIOR pond cell — island water (ground painted, then flooded, so
        /// never open ocean) with water on all four sides, which is what forces the
        /// clamp-to-near-shore fallback rather than a one-ring NearestWalkable hop. Falls
        /// back to a pond shore cell on a pond too small to have an interior. Located from
        /// the map data, not a hardcoded rect — the pond has moved before (DinoDigger-8e1).</summary>
        private bool FindDeepWaterCell(OverworldMap map, out Vector3Int cell)
        {
            return FindIslandWaterCell(map, minWaterNeighbors: 4, requireLandNeighbor: false, out cell) ||
                   FindIslandWaterCell(map, minWaterNeighbors: 0, requireLandNeighbor: true, out cell);
        }

        private IEnumerator Case_EightDirFacing(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            BackhoeController bh = gm.TestBackhoe;
            OverworldMap map = gm.TestMap;
            ctx.Assert(bh != null && map != null, "missing backhoe/map");

            var sprites = new HashSet<Sprite>();
            var facings = new HashSet<Dir8>();
            Vector3[] offsets =
            {
                new Vector3(3f, 0f, 0f), new Vector3(0f, 3f, 0f),
                new Vector3(-3f, 0f, 0f), new Vector3(0f, -3f, 0f),
                new Vector3(3f, 2f, 0f), new Vector3(-3f, -2f, 0f),
            };

            for (int i = 0; i < offsets.Length; i++)
            {
                Vector3 tgt = FindDistinctWalkable(map, bh.transform.position, offsets[i]);
                ctx.TapWorld(tgt);

                int guard = 0;
                while (bh.IsMoving && guard++ < 200)
                {
                    if (bh.TestSprite != null)
                    {
                        sprites.Add(bh.TestSprite);
                    }

                    facings.Add(bh.Facing);
                    yield return null;
                }

                if (sprites.Count >= 3 && facings.Count >= 3)
                {
                    break;
                }
            }

            ctx.Assert(facings.Count >= 3, $"only {facings.Count} distinct headings observed");
            ctx.Assert(sprites.Count >= 3, $"only {sprites.Count} distinct facing sprites observed");
            ctx.Log($"observed {facings.Count} headings / {sprites.Count} distinct sprites");
        }

        // Drive the backhoe straight in each cardinal SCREEN direction and assert the
        // resolved facing (and rendered sprite) is the expected Dir8. This guards the
        // compass math against a sign/axis flip: world +X=E, +Y=N (back view),
        // -X=W, -Y=S (front view / faces the camera).
        private IEnumerator Case_FacingCorrectness(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            BackhoeController bh = gm.TestBackhoe;
            OverworldMap map = gm.TestMap;
            ctx.Assert(bh != null && map != null, "missing backhoe/map");

            // All 8 compass headings — DIAGONALS INCLUDED (DinoDigger-bw4). The expected
            // facing comes from the SAME Direction8.FromVector the runtime uses, so this
            // validates (a) the vector->Dir8 sector math for every point and (b) that the
            // rendered sprite is the one wired at that Dir8 array index.
            //
            // IMPORTANT: this can only validate the Dir8 index<->array-slot mapping and the
            // sector math — it has NO way to see which direction the ART actually points.
            // The bw4 bug was mirrored/mislabeled PNGs (fixed in GeneratedArtImporter), which
            // is invisible here; that layer needs visual QA, not this test.
            Vector2[] dirs =
            {
                new Vector2(1f, 0f), new Vector2(0f, 1f),
                new Vector2(-1f, 0f), new Vector2(0f, -1f),
                new Vector2(1f, 1f), new Vector2(-1f, 1f),
                new Vector2(-1f, -1f), new Vector2(1f, -1f),
            };

            // Diagonal legs on the iso grid STAIRCASE around streams/bridges unless the
            // whole leg has corridor line-of-sight; a staircase's every step is a
            // legitimate facing change that would make the expected-facing assertion
            // ambiguous. So relocate to open ground that offers the most CORRIDOR-straight
            // legs across all 8 headings (preferring both axes AND both diagonal hands).
            RelocateForEightWay(gm, map, bh, dirs);
            Vector3 anchor = bh.transform.position;

            int tested = 0, diagTested = 0;
            bool xAxisTested = false, yAxisTested = false;
            bool eastDiag = false, westDiag = false;
            int giveUpsBefore = bh.TestGiveUpCount;
            for (int i = 0; i < dirs.Length; i++)
            {
                if (!FindClearStraightTarget(map, gm, anchor, dirs[i], out Vector3 target))
                {
                    continue; // no corridor-straight target this way from here
                }

                Dir8 expected = Direction8.FromVector(dirs[i]);
                Vector2 leg = new Vector2(target.x - anchor.x, target.y - anchor.y);

                // Start every leg from the SAME known, non-adjacent facing (two sectors
                // clockwise of the expected one). The smoother is velocity-smoothed with
                // hysteresis, so without this the leg inherits the PREVIOUS leg's heading
                // across the teleport and a neighbouring sector can legitimately be held
                // part-way through the drive — the "drove (0,1) but faced NE" flake. Two
                // sectors away also keeps the test honest: the leg must genuinely rotate
                // the smoother, and it never starts antiparallel (no zero-crossing hold).
                Dir8 start = (Dir8)(((int)expected + 2) % 8);

                // Convergence is driven by MOTION, never by wall clock — the smoother's
                // deadband freezes the facing while the backhoe is parked, so topping a
                // short leg up with a stationary wait (what this used to do) could never
                // finish a convergence that a load hitch cut short. Under load
                // Time.deltaTime is clamped to maximumDeltaTime and then scaled x3, so one
                // hitchy frame can step a whole leg at once and a collision slide on that
                // giant step feeds the EMA an off-axis delta. So: drive the leg again from
                // the anchor (more motion) instead of waiting longer, up to 3 times.
                Dir8 got = expected;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    // Each driven leg leaves the backhoe off the vetted open cell, which
                    // invalidates the remaining directions' clearances — snap back (and
                    // re-seat the smoother) before every drive.
                    bh.TestTeleport(anchor, start);
                    bh.MoveTo(target);
                    yield return ctx.WaitUntil(() => !bh.IsMoving, LegBudget(anchor, target),
                        $"leg {dirs[i]} (attempt {attempt + 1}) never arrived");
                    got = bh.Facing;
                    if (FacingAcceptable(got, expected, leg))
                    {
                        break;
                    }
                }

                ctx.Assert(bh.TestGiveUpCount == giveUpsBefore,
                    $"leg {dirs[i]} honked instead of driving — the vetted leg was not drivable, " +
                    "so the facing below never got its motion");
                ctx.Assert(FacingAcceptable(got, expected, leg),
                    $"drove {dirs[i]} but faced {got} (expected {expected}) — compass flip?");

                // The wheel-roll cycler (DinoDigger-682) may still have a ROLL frame up
                // if the backhoe is sampled while it is mid-drive; accept the idle frame
                // OR either roll phase for the settled facing (mirrors GrowthStageArt's
                // stride tolerance). All three are direction-indexed, so a compass flip
                // still fails loudly.
                Sprite rendered = bh.TestSprite;
                bool spriteForFacing = rendered == bh.TestDirSprite(got) ||
                    rendered == bh.TestRollDirSprite(0, got) ||
                    rendered == bh.TestRollDirSprite(1, got);
                ctx.Assert(spriteForFacing,
                    $"rendered sprite != wired array[{(int)got}] ({got}) after driving {dirs[i]}");
                tested++;
                bool diag = Mathf.Abs(dirs[i].x) > 0.5f && Mathf.Abs(dirs[i].y) > 0.5f;
                if (diag)
                {
                    diagTested++;
                    if (dirs[i].x > 0f) { eastDiag = true; } else { westDiag = true; }
                }
                else if (Mathf.Abs(dirs[i].x) > 0.5f) { xAxisTested = true; }
                else { yAxisTested = true; }
            }

            // Coverage: both cardinal axes (catches X/Y swaps + sign flips) AND at least two
            // diagonals — the bw4 regression surface. Prefer opposite diagonal hands so a
            // SE<->SW / NE<->NW sector-math mirror fails loudly; the relocation biases toward
            // that, and >=2 diagonals guarantees at least one east and one west OR two on the
            // same side (still exercises the diagonal sectors).
            ctx.Assert(tested >= 4 && xAxisTested && yAxisTested && diagTested >= 2,
                $"insufficient facing coverage: tested={tested} xAxis={xAxisTested} " +
                $"yAxis={yAxisTested} diagonals={diagTested} (E={eastDiag}, W={westDiag})");
            ctx.Log($"facing correct for {tested}/8 headings ({diagTested} diagonal, " +
                    $"E-diag={eastDiag}, W-diag={westDiag}); sprite matches array index");

            // A follower dino must index a diagonal facing to the right slot too (bw4).
            yield return DinoDiagonalSpotCheck(ctx, gm, map, bh, anchor, dirs);

            gm.TestReset();
        }

        /// <summary>True when <paramref name="got"/> is a legitimate settled facing for a leg
        /// driven along <paramref name="legDir"/> whose ideal heading is <paramref name="expected"/>.
        ///
        /// An exact match always passes. An ADJACENT sector passes only while the leg's ACTUAL
        /// heading still sits inside that neighbour's hysteresis band: FacingSmoother holds the
        /// current facing until the smoothed heading swings more than 22.5+11 degrees off that
        /// sector's centre, so for a leg that close to the boundary EITHER sector is a correct
        /// answer and asserting one of them is asserting a coin flip. (Today's vetting keeps
        /// targets within ~10 degrees of the ideal axis, so this band is a guard rather than a
        /// routine pass; the re-drive loop above is what actually settles the facing.) Anything
        /// two or more sectors out is a genuine compass flip and still fails loudly — never
        /// widen this past adjacent.</summary>
        private static bool FacingAcceptable(Dir8 got, Dir8 expected, Vector2 legDir)
        {
            if (got == expected)
            {
                return true;
            }

            int delta = ((((int)got - (int)expected) % 8) + 8) % 8;
            if ((delta != 1 && delta != 7) || legDir.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            float legDeg = Mathf.Atan2(legDir.x, legDir.y) * Mathf.Rad2Deg;
            float gotDeg = (int)got * 45f;
            return Mathf.Abs(Mathf.DeltaAngle(legDeg, gotDeg)) <=
                   22.5f + FacingSmoother.DefaultHysteresisDeg;
        }

        // DinoDigger-bw4: spawn a dino, drive the backhoe along one corridor-straight
        // DIAGONAL leg so the follower orients diagonally, and verify the dino renders the
        // sprite wired at that diagonal's Dir8 index (idle OR either stride phase). Skips
        // gracefully — never flakes — if the island offers no diagonal leg or the dino
        // never settles on the exact diagonal within the observation window.
        private IEnumerator DinoDiagonalSpotCheck(TestContext ctx, GameManager gm,
            OverworldMap map, BackhoeController bh, Vector3 anchor, Vector2[] dirs)
        {
            Vector2 diag = Vector2.zero;
            Vector3 target = anchor;
            for (int i = 0; i < dirs.Length; i++)
            {
                bool isDiag = Mathf.Abs(dirs[i].x) > 0.5f && Mathf.Abs(dirs[i].y) > 0.5f;
                if (isDiag && FindClearStraightTarget(map, gm, anchor, dirs[i], out target))
                {
                    diag = dirs[i];
                    break;
                }
            }

            if (diag == Vector2.zero)
            {
                ctx.Log("dino diagonal spot-check skipped (no corridor-straight diagonal leg)");
                yield break;
            }

            bh.transform.position = anchor;
            Physics2D.SyncTransforms();
            DinoController dino = gm.TestSpawnDino(DinoType.TRex, GrowthStage.Baby);
            ctx.Assert(dino != null, "dino spawn failed");
            yield return ctx.WaitFrames(2);

            Dir8 expected = Direction8.FromVector(diag);
            bh.MoveTo(target);

            float t = 0f;
            bool sawDiag = false;
            while (t < 3f)
            {
                // Keep the backhoe moving so the dino keeps following (re-issue on arrival).
                if (!bh.IsMoving &&
                    FindClearStraightTarget(map, gm, bh.transform.position, diag, out Vector3 next))
                {
                    bh.MoveTo(next);
                }

                if (dino.TestFacing == expected)
                {
                    sawDiag = true;
                    Sprite r = dino.TestSprite;
                    bool ok = r == dino.TestStageDirSprite(GrowthStage.Baby, expected) ||
                        r == dino.TestStrideDirSprite(GrowthStage.Baby, 0, expected) ||
                        r == dino.TestStrideDirSprite(GrowthStage.Baby, 1, expected);
                    ctx.Assert(ok,
                        $"dino rendered sprite != array[{(int)expected}] ({expected}) while facing it");
                    break;
                }

                t += Time.deltaTime;
                yield return null;
            }

            ctx.Log(sawDiag
                ? $"dino diagonal facing {expected}: sprite matches array index"
                : $"dino diagonal spot-check: dino never settled on {expected} within 3s (skipped)");
        }

        // A long straight leg must hold ONE facing (the seizure-jiggle regression),
        // and a dino following the moving backhoe must not flap its facing either.
        private IEnumerator Case_FacingStability(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            BackhoeController bh = gm.TestBackhoe;
            OverworldMap map = gm.TestMap;
            ctx.Assert(bh != null && map != null, "missing backhoe/map");

            // ---- Backhoe: one GENUINELY straight leg, count facing changes. ----
            // The leg must have CORRIDOR line-of-sight the whole way, not merely a clear
            // center ray: the backhoe drives via FindPath -> SmoothWaypoints, which
            // collapses a route with corridor LOS into a SINGLE straight segment. A leg
            // that only passes the single-ray test (FindAnyClearCardinalTarget) can still
            // smooth into a grid STAIRCASE around a stream/bridge, whose every step is a
            // legitimate facing change -> the "changed 4x" flake this case kept hitting.
            // Relocate to vetted open ground that offers a corridor-straight leg first.
            ctx.Assert(RelocateForStraightLeg(gm, map, bh, out Vector3 target),
                "no corridor-straight leg available anywhere for the backhoe");
            Vector3 start = bh.transform.position;
            ctx.Assert(map.HasCorridorLineOfSight(start, target),
                "chosen backhoe leg is not corridor-straight (would smooth into a staircase)");

            bh.MoveTo(target);
            yield return ctx.WaitFrames(1);

            // A straight leg legitimately SWEEPS the facing at drive start: the
            // FacingSmoother EMAs the heading from the pre-drive facing through the
            // intermediate sectors up to the leg's cardinal (e.g. S -> SE -> E = 2 changes),
            // and may sweep once more on arrival deceleration. That is correct smoothing,
            // NOT the per-frame seizure-jiggle this case guards (dozens of flips/sec). So
            // ignore the start sweep: begin counting only once the facing first settles on
            // the leg's expected cardinal, or after a 0.6s grace window (comfortably past
            // the ~0.15s EMA time constant + 11° hysteresis), whichever comes first. Over
            // the steady-state remainder a genuinely straight drive must hold ONE facing
            // (allow a single change for arrival deceleration).
            Dir8 expected = Direction8.FromVector((Vector2)(target - start));
            const float graceSec = 0.6f;
            float grace = 0f;
            bool counting = false;
            Dir8 last = bh.Facing;
            int changes = 0;
            int guard = 0;
            while (bh.IsMoving && guard++ < 1200)
            {
                if (!counting)
                {
                    grace += Time.deltaTime;
                    if (bh.Facing == expected || grace >= graceSec)
                    {
                        counting = true;
                        last = bh.Facing; // baseline the steady-state facing
                    }
                }
                else if (bh.Facing != last)
                {
                    changes++;
                    last = bh.Facing;
                }

                yield return null;
            }

            ctx.Assert(changes <= 1,
                $"backhoe facing changed {changes}x on the steady-state part of ONE straight leg (jiggle regression)");

            // ---- Dino: follow the moving backhoe ~3s, measure facing changes/sec. ----
            // Same determinism guard: put the backhoe where corridor-straight legs
            // exist so it leads the dino along straight paths (a staircasing backhoe
            // makes the follower flap its facing legitimately), then spawn the dino
            // beside the relocated backhoe.
            gm.TestReset();
            RelocateForStraightLeg(gm, map, bh, out _);
            DinoController dino = gm.TestSpawnDino(DinoType.TRex, GrowthStage.Baby);
            ctx.Assert(dino != null, "dino spawn failed");
            yield return ctx.WaitFrames(2);

            // Same start-sweep exclusion as the backhoe half: when the dino first orients
            // toward the moving backhoe its facing legitimately sweeps through intermediate
            // sectors. Hold off counting for a 0.6s grace window (> the ~0.15s EMA + 11°
            // hysteresis) so the initial orientation sweep is not scored as flapping. The
            // rate is still averaged over the full 3s observation window (NOT the shortened
            // post-grace window) so the threshold keeps the meaning it was calibrated with —
            // this can only lower the measured rate versus counting from frame zero.
            Dir8 dlast = dino.TestFacing;
            int dchanges = 0;
            float elapsed = 0f;
            const float dinoGraceSec = 0.6f;
            while (elapsed < 3f)
            {
                if (!bh.IsMoving &&
                    FindStraightCorridorTarget(map, gm, bh.transform.position, out Vector3 next))
                {
                    bh.MoveTo(next); // keep it moving over a straight leg so the dino follows a straight path
                }

                if (elapsed >= dinoGraceSec)
                {
                    if (dino.TestFacing != dlast)
                    {
                        dchanges++;
                        dlast = dino.TestFacing;
                    }
                }
                else
                {
                    dlast = dino.TestFacing; // keep the baseline current through the start sweep
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            float perSec = dchanges / Mathf.Max(elapsed, 0.001f);
            ctx.Assert(perSec < 4f, $"dino facing flapped {perSec:F1}x/s while following (expected < 4/s)");
            ctx.Log($"straight leg held facing ({changes} changes); dino followed at {perSec:F1} facing-changes/s");
            gm.TestReset();
        }

        // ================================================================== DIG

        private IEnumerator Case_MoundToDig(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            DigModeController dm = gm.TestDigMode;
            DigMound m = FirstActiveMound(gm);
            ctx.Assert(m != null && dm != null, "no active mound / dig controller");

            // Walk into tapping range first: on the 48x48 island the mound may be
            // off-screen, and TapWorld's world->screen conversion needs it in view.
            gm.TestBackhoe.MoveTo(m.transform.position);
            yield return ctx.WaitUntil(() => !gm.TestBackhoe.IsMoving);

            ctx.TapWorld(m.transform.position);
            yield return ctx.WaitUntil(() => gm.State.Is(GameState.Dig));
            // The state flips to Dig at the START of the camera transition; the dirt
            // grid is built when the camera lands (~0.5s later) — wait for tiles first.
            yield return ctx.WaitUntil(() => dm.TestTileCount > 0);

            int rows = Mathf.Clamp(gm.TestConfig.DigRows, 4, 6);
            int cols = Mathf.Max(3, gm.TestConfig.DigColumns);
            ctx.Assert(dm.TestTileCount == rows * cols, $"dig grid {dm.TestTileCount} != {rows}x{cols}");

            ctx.Assert(dm.DigCenter.x > 500f, $"dig center not at far dig root (x={dm.DigCenter.x:F0})");
            float camX = gm.TestCamera.transform.position.x;
            ctx.Assert(Mathf.Abs(camX - dm.DigCenter.x) < 0.75f, $"camera x {camX:F1} not moved to dig root {dm.DigCenter.x:F1}");

            ctx.Log($"entered dig: {rows}x{cols}={dm.TestTileCount} tiles, camera@{camX:F0}");
            gm.TestForceRoam();
        }

        private IEnumerator Case_DirtTileDamage(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            yield return EnterDig(ctx);
            DigModeController dm = gm.TestDigMode;

            DirtTile tile = FindPlainTile(dm);
            ctx.Assert(tile != null, "no plain (unburied) dirt tile found");

            // Pin the tile at the canonical 3 taps — one crack state per non-final hit.
            // Per-theme hardness rolls up to 4 taps (Sparkle is a 3-4 theme) and the crack
            // art has only 3 states, so on a 4-tap tile damage 1 still maps to state 0 and
            // the sprite legitimately does NOT change on hit 1. That proportional mapping
            // is TileHardness's business; this case is about the crack/crumb/destroy
            // progression, so it must not ride on a random hardness roll.
            tile.TestSetMaxHealth(3);

            int max = tile.TestMaxHealth;
            Sprite prev = tile.TestDirtSprite;
            int crumbPeak = 0;

            for (int hit = 1; hit <= max; hit++)
            {
                // The excavator arm bites one tile at a time and drops a same-tile
                // re-tap while that bite is still in flight — wait until it is parked
                // before each tap so every tap lands as a fresh bite. A tile that is
                // mid-FALL also drops taps (gravity, DinoDigger-7fw); this tile is the
                // top-row one so nothing can land on it, but pacing to both keeps the
                // case honest if the pick ever changes.
                yield return ctx.WaitUntil(() => (dm.TestArmReady && !tile.IsFalling) || tile.IsDestroyed);
                if (tile.IsDestroyed)
                {
                    break;
                }

                int before = tile.TestDamage;
                ctx.TapWorld(tile.transform.position);
                yield return ctx.WaitUntil(() => tile.TestDamage > before || tile.IsDestroyed);

                if (dm.TestCrumbs != null)
                {
                    crumbPeak = Mathf.Max(crumbPeak, dm.TestCrumbs.particleCount);
                }

                if (hit < max)
                {
                    Sprite now = tile.TestDirtSprite;
                    ctx.Assert(now != prev, $"crack sprite did not change on hit {hit}");
                    prev = now;
                }
            }

            ctx.Assert(tile.IsDestroyed, "tile not destroyed after 3 hits");
            ctx.Assert(crumbPeak > 0, "no crumb particles emitted while digging");
            ctx.Log($"3 hits crumbled tile (crumb peak={crumbPeak})");
            gm.TestForceRoam();
        }

        private IEnumerator Case_PeekVisible(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            yield return EnterDig(ctx);
            DigModeController dm = gm.TestDigMode;

            List<DirtTile> buried = dm.TestBuriedTiles();
            ctx.Assert(buried.Count > 0, "no buried item tiles at the site");

            for (int i = 0; i < buried.Count; i++)
            {
                DirtTile t = buried[i];
                ctx.Assert(t.TestPeekEnabled, $"peek renderer disabled at ({t.Row},{t.Col})");
                ctx.Assert(t.TestPeekAlpha > 0.01f, $"peek alpha {t.TestPeekAlpha:F2} not >0 at ({t.Row},{t.Col})");
            }

            ctx.Log($"{buried.Count} buried tiles all show a visible peek from the start");
            gm.TestForceRoam();
        }

        private IEnumerator Case_MultiItemCollection(TestContext ctx)
        {
            GameManager gm = ctx.GM;

            // Keep a hungry dino present so dug fruit keeps its identity: the
            // fruit->treasure downgrade only fires when NOTHING is hungry, and this
            // case asserts exact per-type collection outcomes.
            gm.TestSpawnDino(DinoType.TRex, GrowthStage.Baby);

            // Dig toys off (DinoDigger-z4d) — pinned BEFORE the site is built, since the roll
            // happens in BuildGrid. This case asserts an EXACT wallet delta from the BURIED loot,
            // and every toy is a second, deliberate source of coins: a pot cracked open by a
            // falling tile pays 5-8 on its own. Their own cases cover them.
            DigModeController.TestSuppressToys = true;

            // Nothing LOOSE in the pit either (Dig Loop 2.0 D3). Two objects this wave puts in
            // every dig would otherwise perturb a case that certifies exact spawn counts and aims
            // taps at exact tiles:
            //   critters — ambient, coin-paying, and (so a catch is possible at all) they outrank
            //              a dirt tile for taps, so one sitting on a target tile eats the bite;
            //   the ladder — a second tappable prop standing in the pit while the last item is
            //              being dug.
            // Neither is what this case is about, and both have their own cases.
            DigModeController.TestSuppressCritters = true;
            DigModeController.TestSuppressLadder = true;
            try
            {
                yield return EnterDig(ctx);
                DigModeController dm = gm.TestDigMode;

                List<DirtTile> buried = dm.TestBuriedTiles();
                ctx.Assert(buried.Count > 0, "no buried items to collect");

                int eggs = 0, fruit = 0, treasure = 0;
                int expectedTreasureGain = 0; // denominations: each treasure banks its variant value
                for (int i = 0; i < buried.Count; i++)
                {
                    switch (dm.TestBuriedType(buried[i]))
                    {
                        case ItemType.Egg: eggs++; break;
                        case ItemType.Fruit: fruit++; break;
                        default:
                            treasure++;
                            expectedTreasureGain += gm.TestConfig.TreasureValue(dm.TestBuriedVariant(buried[i]));
                            break;
                    }
                }

                int treasureBefore = gm.Save.Data.TreasureCount;
                int expectedPickups = eggs + fruit;

                // The town builder must not spend out of the wallet this case counts to the coin
                // (see Case_TreasureCounter for the full story) — freeze the queue while we dig.
                TownController.TestSuspendBuilds = true;
                try
                {
                    // Dig every buried tile. State must remain Dig until the last is uncovered.
                    int guard = 0;
                    while (gm.State.Is(GameState.Dig) && dm.TestBuriedCount > 0 && guard++ < 60)
                    {
                        List<DirtTile> remaining = dm.TestBuriedTiles();
                        if (remaining.Count == 0)
                        {
                            break;
                        }

                        if (dm.TestBuriedCount > 1)
                        {
                            ctx.Assert(gm.State.Is(GameState.Dig), "left dig before all items were uncovered");
                        }

                        yield return TapTileUntilDestroyed(ctx, dm, remaining[0]);
                    }

                    ctx.Assert(!gm.State.Is(GameState.Dig), "still in dig after clearing every item");
                    yield return ctx.WaitUntil(() => gm.State.Is(GameState.Roam), 20f,
                        "never returned to roam after the last item was uncovered");

                    // Non-treasure items become pickups; treasure auto-flies to the counter and
                    // banks its per-variant denomination (coin=1, gem=3, boot=1, bone=2).
                    //
                    // DEFLAKE (DinoDigger-dzs): the alive-pickup count is only equal to the
                    // expected total inside a WINDOW — after every item lands, before the first
                    // egg wobbles open (~1.2s of scaled time). Under editor load one frame can
                    // carry a full second of scaled time, so a poll for an exact instantaneous
                    // count could step straight over that window and then wait forever. Track the
                    // PEAK instead: it is reached as soon as the last item lands and it never
                    // decays, so no amount of frame-hitching can hide it. The wallet side waits
                    // on >= (monotone) and is asserted exact afterwards, same as TreasureCounter.
                    int peakPickups = 0;
                    yield return ctx.WaitUntil(() =>
                    {
                        peakPickups = Mathf.Max(peakPickups, CountOverworldPickups(gm, true));
                        return peakPickups >= expectedPickups &&
                               gm.Save.Data.TreasureCount >= treasureBefore + expectedTreasureGain;
                    }, 25f, () => $"dug batch never fully surfaced (pickups peaked at {peakPickups}/{expectedPickups}, " +
                                  $"treasure +{gm.Save.Data.TreasureCount - treasureBefore}/{expectedTreasureGain})");

                    // NAME THE STRAY. An over-count used to report only a number, which left the
                    // next reader guessing which system had spawned a fruit or an egg into the
                    // world mid-window (a dug batch is not the only source: a caught duck drops
                    // fruit half the time, a shaken tree drops several, a sprout harvest drops
                    // one, and a previous case's staggered spill can still be in the air). The
                    // breadcrumb lists what is actually lying there, so a recurrence identifies
                    // its own cause instead of being pinned on the newest feature.
                    ctx.Assert(peakPickups == expectedPickups,
                        $"{peakPickups} pickups spawned (expected {expectedPickups}) — live " +
                        $"non-treasure pickups now: {DescribeOverworldPickups(gm)}");
                    ctx.Assert(gm.Save.Data.TreasureCount == treasureBefore + expectedTreasureGain,
                        $"treasure +{gm.Save.Data.TreasureCount - treasureBefore} (expected +{expectedTreasureGain})");

                    ctx.Log($"eggs={eggs} fruit={fruit} treasure={treasure}: {expectedPickups} pickups spawned, treasure+={expectedTreasureGain}");
                }
                finally
                {
                    TownController.TestSuspendBuilds = false;
                }

                gm.TestReset();
            }
            finally
            {
                DigModeController.TestSuppressToys = false;
                DigModeController.TestSuppressCritters = false;
                DigModeController.TestSuppressLadder = false;
            }
        }

        /// <summary>Every live non-treasure pickup in the world, as "Type(variant)" — the
        /// breadcrumb an exact-count failure prints so the stray names itself instead of leaving
        /// the next reader to guess which system spawned it.</summary>
        private string DescribeOverworldPickups(GameManager gm)
        {
            Transform root = gm.TestOverworldRoot;
            if (root == null)
            {
                return "(no overworld root)";
            }

            var parts = new List<string>();
            ItemPickup[] arr = root.GetComponentsInChildren<ItemPickup>(true);
            for (int i = 0; i < arr.Length; i++)
            {
                ItemPickup p = arr[i];
                if (p == null || p.IsConsumed || p.Type == ItemType.Treasure)
                {
                    continue;
                }

                parts.Add($"{p.Type}({p.Variant})");
            }

            return parts.Count == 0 ? "(none)" : string.Join(", ", parts);
        }

        // Dig Postcards: themed dig sites. Mounds roll a WEIGHTED theme and tint themselves;
        // the site reads the theme for its tints, loot skew and buried-item count. Golden
        // Mound is the rare all-treasure jackpot (always 4 items) and still passes cleanly
        // through ResolveDugItem; Berry Bog's raw loot skews to fruit. All tint-only, no art.
        private IEnumerator Case_DigThemes(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            GameConfig cfg = gm.TestConfig;
            ctx.Assert(cfg != null, "no config");

            int themeCount = cfg.DigThemeCount;
            ctx.Assert(themeCount >= 4, $"expected >=4 dig themes, got {themeCount}");

            int golden = FindThemeIndex(cfg, "Golden Mound");
            int meadow = FindThemeIndex(cfg, "Meadow Classic");
            int berry = FindThemeIndex(cfg, "Berry Bog");
            ctx.Assert(golden >= 0 && meadow >= 0 && berry >= 0,
                "Golden Mound / Meadow Classic / Berry Bog themes not all found by name");

            // ---- Mounds carry a valid theme index and tint themselves to match it. ----
            IReadOnlyList<DigMound> mounds = gm.TestMounds;
            ctx.Assert(mounds != null && mounds.Count > 0, "no mounds in scene");
            int checkedMounds = 0;
            for (int i = 0; i < mounds.Count; i++)
            {
                DigMound m = mounds[i];
                if (m == null)
                {
                    continue;
                }

                ctx.Assert(m.ThemeIndex >= 0 && m.ThemeIndex < themeCount,
                    $"mound {i} theme index {m.ThemeIndex} out of range [0,{themeCount})");
                Color expectedTint = cfg.GetTheme(m.ThemeIndex).MoundTint;
                ctx.Assert(ColorsClose(m.TestTint, expectedTint),
                    $"mound {i} tint {m.TestTint} != theme {m.ThemeIndex} MoundTint {expectedTint}");
                checkedMounds++;
            }

            ctx.Assert(checkedMounds > 0, "no non-null mounds to check");

            // ---- Weighted pick: every theme appears; the rare Golden < the common Meadow. ----
            var counts = new int[themeCount];
            const int samples = 4000;
            for (int i = 0; i < samples; i++)
            {
                counts[cfg.PickThemeIndex()]++;
            }

            for (int t = 0; t < themeCount; t++)
            {
                ctx.Assert(counts[t] > 0, $"theme {t} ({cfg.GetTheme(t).Name}) never picked in {samples} samples");
            }

            ctx.Assert(counts[golden] < counts[meadow],
                $"Golden picks ({counts[golden]}) not rarer than Meadow ({counts[meadow]})");

            // ---- Golden dig: exactly 4 items, ALL treasure, and still treasure after
            //      ResolveDugItem (treasure passes through the uniqueness/glut resolution). ----
            gm.TestBuildThemedDigSite(golden);
            yield return ctx.WaitFrames(1);
            DigModeController dm = gm.TestDigMode;

            List<DirtTile> buried = dm.TestBuriedTiles();
            ctx.Assert(buried.Count == 4, $"golden site buried {buried.Count} items (expected exactly 4)");
            for (int i = 0; i < buried.Count; i++)
            {
                ItemType t = dm.TestBuriedType(buried[i]);
                ctx.Assert(t == ItemType.Treasure, $"golden site buried a {t} (expected all Treasure)");
                DugItemInfo resolved = gm.TestResolveItem(
                    new DugItemInfo(t, DinoType.TRex, dm.TestBuriedVariant(buried[i]), Vector3.zero));
                ctx.Assert(resolved.Type == ItemType.Treasure,
                    $"golden treasure resolved to {resolved.Type} (must pass through unchanged)");
            }

            // ---- Golden tints landed on the dirt tiles + the backdrop. ----
            // Sampled from a DIRT tile specifically (DinoDigger-z4d): a site now also rolls dig
            // toys, and a crystal/geode/pot deliberately does NOT take the theme's dirt multiply —
            // its whole job is to read as its own colour, and a muddy-brown "gold" crystal would
            // break the one matching rule the game has. This case is about the tint landing on
            // dirt, so it samples dirt; the toys coexisting on a themed site is left as real
            // coverage rather than suppressed.
            DigTheme goldenTheme = cfg.GetTheme(golden);
            var tiles = new List<DirtTile>(dm.TestTiles);
            ctx.Assert(tiles.Count > 0, "golden site built no tiles");

            DirtTile dirtSample = null;
            for (int i = 0; i < tiles.Count && dirtSample == null; i++)
            {
                if (tiles[i] != null && tiles[i].Kind == DigTileKind.Dirt)
                {
                    dirtSample = tiles[i];
                }
            }

            ctx.Assert(dirtSample != null, "golden site built no plain dirt tile to check the tint on");
            ctx.Assert(ColorsClose(dirtSample.TestDirtColor, goldenTheme.DirtTint),
                $"tile dirt tint {dirtSample.TestDirtColor} != golden DirtTint {goldenTheme.DirtTint}");
            ctx.Assert(ColorsClose(dm.TestBackgroundColor, goldenTheme.BackgroundTint),
                $"backdrop tint {dm.TestBackgroundColor} != golden BackgroundTint {goldenTheme.BackgroundTint}");

            gm.TestForceRoam();
            yield return ctx.WaitFrames(1);

            // ---- Berry Bog RAW loot (no glut downgrade) skews to fruit. ----
            gm.TestBuildThemedDigSite(berry);
            yield return ctx.WaitFrames(1);
            int eggs = 0, fruit = 0, treasure = 0, shards = 0;
            const int rolls = 2000;
            for (int i = 0; i < rolls; i++)
            {
                switch (gm.TestRollDugItemRaw().Type)
                {
                    case ItemType.Egg: eggs++; break;
                    case ItemType.Fruit: fruit++; break;
                    case ItemType.Shard: shards++; break;
                    default: treasure++; break;
                }
            }

            float fruitFrac = fruit / (float)rolls;
            ctx.Assert(fruitFrac > 0.45f, $"Berry Bog fruit fraction {fruitFrac:F2} not >0.45 (should be fruit-heavy)");
            ctx.Assert(fruit > treasure && fruit > eggs,
                $"Berry Bog not fruit-dominant (egg={eggs} fruit={fruit} treasure={treasure} shard={shards})");

            gm.TestForceRoam();
            ctx.Log($"themes={themeCount}; {checkedMounds} mounds tinted; golden=4 all-treasure; " +
                    $"berry fruitFrac={fruitFrac:F2}; picks golden={counts[golden]} < meadow={counts[meadow]}");
            gm.TestReset();
        }

        // Per-tile break-tap hardness: tiles vary by theme with capped, LOW-biased jitter
        // (roll twice, keep the smaller) instead of a uniform 3 taps. Verifies the range is
        // honoured, never exceeds [1,4], skews soft, Sparkle Cave is harder than Berry Bog,
        // and the DirtTile crack sprite still maps correctly at maxHealth != 3.
        private IEnumerator Case_TileHardness(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            GameConfig cfg = gm.TestConfig;
            ctx.Assert(cfg != null, "no config");

            // Dig toys (DinoDigger-z4d) are OFF for this case. A crystal/geode/pot seats its own
            // hardness (1/1/2) AFTER the theme roll, so a toy tile is simply not evidence about
            // the roll this case certifies — sampling one would fail the range assertion for a
            // reason that has nothing to do with hardness. Suppressing them at BuildGrid time
            // keeps the sample a complete census of RollTileHardness (and keeps the crack-sprite
            // probe at the end on real dirt: a geode arms instead of taking a hit, so it would
            // never crumble). Cleared in the finally so a failed assertion cannot leak the pin
            // into the rest of the run.
            DigModeController.TestSuppressToys = true;
            try
            {
                int berry = FindThemeIndex(cfg, "Berry Bog");
                int sparkle = FindThemeIndex(cfg, "Sparkle Cave");
                ctx.Assert(berry >= 0 && sparkle >= 0, "Berry Bog / Sparkle Cave themes not found by name");

                cfg.GetTheme(berry).GetTapRange(out int bMin, out int bMax);
                cfg.GetTheme(sparkle).GetTapRange(out int sMin, out int sMax);
                ctx.Assert(bMin == 1 && bMax == 2, $"Berry Bog tap range {bMin}-{bMax} (expected 1-2)");
                ctx.Assert(sMin == 3 && sMax == 4, $"Sparkle Cave tap range {sMin}-{sMax} (expected 3-4)");

                DigModeController dm = gm.TestDigMode;

                // ---- Sample many tiles across several rebuilds per theme. ----
                double berrySum = 0; int berryTiles = 0, berryAtMin = 0, berryAtMax = 0;
                double sparkleSum = 0; int sparkleTiles = 0, sparkleAtMin = 0, sparkleAtMax = 0;
                const int rebuilds = 6;

                for (int build = 0; build < rebuilds; build++)
                {
                    gm.TestBuildThemedDigSite(berry);
                    yield return ctx.WaitFrames(1);
                    foreach (DirtTile t in dm.TestTiles)
                    {
                        int h = t.TestMaxHealth;
                        ctx.Assert(h >= 1 && h <= 4, $"berry tile health {h} outside the hard cap [1,4]");
                        ctx.Assert(h >= bMin && h <= bMax, $"berry tile health {h} outside theme range [{bMin},{bMax}]");
                        berrySum += h; berryTiles++;
                        if (h == bMin) berryAtMin++;
                        if (h == bMax) berryAtMax++;
                    }

                    gm.TestForceRoam();
                    yield return ctx.WaitFrames(1);

                    gm.TestBuildThemedDigSite(sparkle);
                    yield return ctx.WaitFrames(1);
                    foreach (DirtTile t in dm.TestTiles)
                    {
                        int h = t.TestMaxHealth;
                        ctx.Assert(h >= 1 && h <= 4, $"sparkle tile health {h} outside the hard cap [1,4]");
                        ctx.Assert(h >= sMin && h <= sMax, $"sparkle tile health {h} outside theme range [{sMin},{sMax}]");
                        sparkleSum += h; sparkleTiles++;
                        if (h == sMin) sparkleAtMin++;
                        if (h == sMax) sparkleAtMax++;
                    }

                    gm.TestForceRoam();
                    yield return ctx.WaitFrames(1);
                }

                ctx.Assert(berryTiles > 100 && sparkleTiles > 100,
                    $"too few tiles sampled (berry={berryTiles}, sparkle={sparkleTiles})");

                // ---- LOW bias: a healthy share sit at MinTaps and few at MaxTaps. ----
                float berryMinFrac = berryAtMin / (float)berryTiles;
                float berryMaxFrac = berryAtMax / (float)berryTiles;
                ctx.Assert(berryMinFrac > 0.5f, $"Berry MinTaps share {berryMinFrac:F2} not >0.5 (should skew soft)");
                ctx.Assert(berryMinFrac > berryMaxFrac,
                    $"Berry not low-biased (min share {berryMinFrac:F2} <= max share {berryMaxFrac:F2})");

                float sparkleMinFrac = sparkleAtMin / (float)sparkleTiles;
                float sparkleMaxFrac = sparkleAtMax / (float)sparkleTiles;
                ctx.Assert(sparkleMinFrac > sparkleMaxFrac,
                    $"Sparkle not low-biased (min share {sparkleMinFrac:F2} <= max share {sparkleMaxFrac:F2})");

                // ---- Sparkle Cave is harder on average than Berry Bog. ----
                float berryAvg = (float)(berrySum / berryTiles);
                float sparkleAvg = (float)(sparkleSum / sparkleTiles);
                ctx.Assert(sparkleAvg > berryAvg,
                    $"Sparkle avg {sparkleAvg:F2} not > Berry avg {berryAvg:F2}");

                // ---- Crack-sprite state maps correctly at maxHealth != 3 (Damage() alone crumbles
                //      a tile; it never runs the controller's collect/finish path, so no side effects). ----
                gm.TestBuildThemedDigSite(sparkle);
                yield return ctx.WaitFrames(1);
                var tiles = new List<DirtTile>(dm.TestTiles);
                ctx.Assert(tiles.Count >= 2, "built site has too few tiles to probe crack states");

                // maxHealth 1 -> one hit crumbles it.
                DirtTile one = tiles[0];
                one.TestSetMaxHealth(1);
                bool crumbled = one.Damage();
                ctx.Assert(crumbled && one.IsDestroyed, "maxHealth-1 tile did not crumble in a single hit");

                // maxHealth 4 -> intermediate crack states across the first 3 hits, crumbles on the 4th.
                DirtTile four = tiles[1];
                four.TestSetMaxHealth(4);
                var states = new HashSet<Sprite> { four.TestDirtSprite };
                for (int hit = 1; hit <= 4; hit++)
                {
                    bool d = four.Damage();
                    if (hit < 4)
                    {
                        ctx.Assert(!d && !four.IsDestroyed, $"maxHealth-4 tile crumbled early on hit {hit}");
                        states.Add(four.TestDirtSprite);
                    }
                    else
                    {
                        ctx.Assert(d && four.IsDestroyed, "maxHealth-4 tile did not crumble on the 4th hit");
                    }
                }

                ctx.Assert(states.Count >= 2, "maxHealth-4 tile never showed intermediate crack states");

                gm.TestForceRoam();
                ctx.Log($"berry avg={berryAvg:F2} (minFrac={berryMinFrac:F2}, maxFrac={berryMaxFrac:F2}); " +
                        $"sparkle avg={sparkleAvg:F2} (minFrac={sparkleMinFrac:F2}, maxFrac={sparkleMaxFrac:F2}); " +
                        $"crack states at max4={states.Count}");
                gm.TestReset();
            }
            finally
            {
                DigModeController.TestSuppressToys = false;
            }
        }

        /// <summary>Index of the dig theme with the given name, or -1.</summary>
        private int FindThemeIndex(GameConfig cfg, string name)
        {
            for (int i = 0; i < cfg.DigThemeCount; i++)
            {
                if (cfg.GetTheme(i).Name == name)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>RGBA equality within a small tolerance (tint round-trips exactly, but
        /// stay lenient against float drift).</summary>
        private static bool ColorsClose(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f &&
                   Mathf.Abs(a.b - b.b) < 0.02f && Mathf.Abs(a.a - b.a) < 0.02f;
        }

        // ============================================================ REGRESSIONS

        private IEnumerator Case_EggHatch(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();

            int dinosBefore = gm.TestDinos.Count;
            Vector3 pos = WalkableNear(gm.TestMap, gm.TestBackhoe.transform.position);
            gm.TestSpawnItem(ItemType.Egg, DinoType.Triceratops, 0, pos);

            // Lands (~0.55s), wobbles (~1.2s via ShakeRotation), then HatchEgg spawns a dino.
            yield return ctx.WaitUntil(() => gm.TestDinos.Count > dinosBefore);

            DinoController hatched = null;
            IReadOnlyList<DinoController> dinos = gm.TestDinos;
            for (int i = 0; i < dinos.Count; i++)
            {
                if (dinos[i] != null)
                {
                    hatched = dinos[i];
                }
            }

            ctx.Assert(hatched != null, "no DinoController after hatch");
            ctx.Log($"egg wobbled then hatched into a {hatched.Type} (ShakeRotation onComplete fired)");
            gm.TestReset();
        }

        // Uniqueness: dig eggs never roll an OWNED species. While species remain
        // unowned, every egg is one of the unowned egg species; once all four are
        // owned there are no unique eggs left, so eggs convert to egg shards (zero
        // owned-species eggs, shards appear). Uses direct roll hooks, not dig loops.
        private IEnumerator Case_UniqueDinoNoDupes(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();

            // ---- Same-batch uniqueness: a batch of eggs dug at once (none hatched)
            // must resolve to DISTINCT species. With nothing owned, all four egg
            // species are available; a 6-egg batch yields the 4 distinct species then
            // falls back to FRUIT (never a duplicate, and never an early shard because
            // the shard nerf is not active while egg species remain unowned). ----
            List<DugItemInfo> batch = gm.TestResolveDugBatch(6);
            var batchSpecies = new HashSet<DinoType>();
            int batchEggs = 0, batchFruit = 0, batchOther = 0;
            for (int i = 0; i < batch.Count; i++)
            {
                DugItemInfo it = batch[i];
                if (it.Type == ItemType.Egg)
                {
                    batchEggs++;
                    ctx.Assert(DinoSpecies.IsEggHatchable(it.DinoType),
                        $"batch egg rolled non-egg species {it.DinoType}");
                    ctx.Assert(batchSpecies.Add(it.DinoType),
                        $"DUPLICATE egg species {it.DinoType} in one dig batch (the 2-T-Rex bug)");
                }
                else if (it.Type == ItemType.Fruit)
                {
                    batchFruit++;
                }
                else
                {
                    batchOther++;
                }
            }

            ctx.Assert(batchEggs == DinoSpecies.EggHatchableCount,
                $"batch produced {batchEggs} unique eggs (expected {DinoSpecies.EggHatchableCount})");
            ctx.Assert(batchOther == 0,
                $"{batchOther} shard/treasure items leaked in a batch while egg species were unowned (expected fruit fallback)");
            ctx.Assert(batchFruit == 6 - DinoSpecies.EggHatchableCount,
                $"expected {6 - DinoSpecies.EggHatchableCount} fruit fallbacks, got {batchFruit}");
            ctx.Assert(gm.TestReservedEggSpeciesCount == DinoSpecies.EggHatchableCount,
                $"reserved {gm.TestReservedEggSpeciesCount} species after batch (expected {DinoSpecies.EggHatchableCount})");

            // Nothing hatched: a reset must clear every reservation so the next case
            // (and the ownership checks below) start from a clean slate.
            gm.TestReset();
            ctx.Assert(gm.TestReservedEggSpeciesCount == 0,
                $"reservations not cleared by reset ({gm.TestReservedEggSpeciesCount} left)");

            // ---- Partial ownership: eggs may only be UNOWNED egg species. ----
            gm.TestSpawnDino(DinoType.TRex, GrowthStage.Baby);
            gm.TestSpawnDino(DinoType.Triceratops, GrowthStage.Baby);
            yield return ctx.WaitFrames(1);
            ctx.Assert(!gm.TestEggSpeciesAllOwned, "reported all-owned with only 2 species");

            int partialEggs = 0, partialShards = 0;
            for (int i = 0; i < 300; i++)
            {
                DugItemInfo info = gm.TestRollDugItem();
                if (info.Type == ItemType.Egg)
                {
                    partialEggs++;
                    ctx.Assert(
                        info.DinoType == DinoType.Brachiosaurus || info.DinoType == DinoType.Stegosaurus,
                        $"egg rolled owned/invalid species {info.DinoType} (only unowned egg species allowed)");
                }
                else if (info.Type == ItemType.Shard)
                {
                    partialShards++;
                }
            }

            ctx.Assert(partialEggs > 0, "no eggs rolled while unowned species remained");
            ctx.Assert(partialShards == 0, $"{partialShards} shards rolled before all species owned (expected 0)");

            // ---- Full ownership: zero owned-species eggs, and NOT an egg shard in sight.
            // Shards retired with the nest (save v5) — the freed egg weight is treasure now
            // and the late-game collectible is the fossil bone the SITE buries. ----
            gm.TestSpawnDino(DinoType.Brachiosaurus, GrowthStage.Baby);
            gm.TestSpawnDino(DinoType.Stegosaurus, GrowthStage.Baby);
            yield return ctx.WaitFrames(1);
            ctx.Assert(gm.TestEggSpeciesAllOwned, "all 4 egg species not owned after 4 spawns");

            int eggs = 0, shards = 0, treasure = 0, total = 0;
            for (int round = 0; round < 50; round++) // renamed: `batch` list above shadows it
            {
                for (int i = 0; i < 4; i++) // ~a dig site's batch worth of items
                {
                    DugItemInfo info = gm.TestRollDugItem();
                    total++;
                    if (info.Type == ItemType.Egg) eggs++;
                    else if (info.Type == ItemType.Shard) shards++;
                    else if (info.Type == ItemType.Treasure) treasure++;
                }
            }

            ctx.Assert(eggs == 0, $"{eggs} owned-species eggs rolled after owning all 4 (must convert to treasure)");
            ctx.Assert(shards == 0, $"{shards} egg shards rolled — the shard economy is retired (save v5)");
            ctx.Assert(treasure > 0, "no treasure appeared after owning every species (the freed egg weight)");
            ctx.Log($"partial: {partialEggs} eggs (all unowned), 0 shards; " +
                    $"full: 0 eggs, 0 shards, {treasure}/{total} treasure");
            gm.TestReset();
        }

        // THE LATE-GAME REWARD SWAP (replaces the retired ShardDropRate, DinoDigger-5ve).
        // Once every egg species is owned:
        //   - egg items still collapse to at most ~25% of their pre-nerf rate (unchanged), but
        //   - the freed weight is TREASURE, not egg shards: the shard economy is retired,
        //   - and the thing that actually carries the late game is the multi-cell FOSSIL BONE
        //     the site buries in its own layer, aimed at the skeleton the board is filling.
        private IEnumerator Case_BoneDropRate(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();

            gm.TestSpawnDino(DinoType.TRex, GrowthStage.Baby);
            gm.TestSpawnDino(DinoType.Triceratops, GrowthStage.Baby);
            gm.TestSpawnDino(DinoType.Brachiosaurus, GrowthStage.Baby);
            gm.TestSpawnDino(DinoType.Stegosaurus, GrowthStage.Baby);
            yield return ctx.WaitFrames(1);
            ctx.Assert(gm.TestEggSpeciesAllOwned, "need all egg species owned for the egg nerf");

            GameConfig cfg = gm.TestConfig;
            float cfgTotal = Mathf.Max(0.0001f, cfg.EggWeight + cfg.FruitWeight + cfg.TreasureWeight);
            float preNerfEggFrac = cfg.EggWeight / cfgTotal;
            float preNerfTreasureFrac = cfg.TreasureWeight / cfgTotal;

            const int N = 3000;
            int eggs = 0, shards = 0, treasure = 0;
            for (int i = 0; i < N; i++)
            {
                DugItemInfo info = gm.TestRollDugItem();
                if (info.Type == ItemType.Egg) eggs++;
                else if (info.Type == ItemType.Shard) shards++;
                else if (info.Type == ItemType.Treasure) treasure++;
            }

            float eggFrac = eggs / (float)N;
            float treasureFrac = treasure / (float)N;

            ctx.Assert(eggFrac <= 0.25f * preNerfEggFrac + 0.001f,
                $"egg rate {eggFrac:F3} > 25% of pre-nerf {preNerfEggFrac:F3} after the egg nerf");
            ctx.Assert(shards == 0, $"{shards} egg shards rolled — shards retired with the nest (save v5)");
            ctx.Assert(treasureFrac >= preNerfTreasureFrac + 0.5f * preNerfEggFrac,
                $"treasure rate {treasureFrac:F3} did not absorb the freed egg weight " +
                $"(expected >= {preNerfTreasureFrac + 0.5f * preNerfEggFrac:F3})");

            // ---- ...and the site buries a BONE, aimed at the skeleton the board wants. ----
            try
            {
                DigModeController.TestSuppressCrew = true;
                DigModeController.TestSuppressToys = true;

                DigModeController dm = gm.TestDigMode;
                dm.TestBuildThemedSite(null);
                yield return ctx.WaitFrames(1);

                ctx.Assert(dm.TestBoneCount >= 1, "no bone buried at a site with every egg species owned");
                DinoType buried = dm.TestBoneSpecies(0);
                ctx.Assert(SkeletonPlan.IsFossilSpecies(buried),
                    $"the site buried a bone for {buried}, which has no skeleton on the board");
                ctx.Assert(!gm.TestSkeletonComplete(buried),
                    $"the site is digging toward {buried}, whose skeleton is already complete");
                ctx.Assert(!gm.TestSpeciesRevived(buried),
                    $"the site is digging toward {buried}, which has already been revived — a " +
                    "revived skeleton must never be a bone target again (a save migrated from " +
                    "the v4 nest revives species that have no banked bones at all)");

                int bone = dm.TestBoneIndex(0);
                ctx.Assert(gm.TestBoneCount(buried, bone) < SkeletonPlan.NeedOf(buried, bone),
                    $"the site buried a {(BoneType)bone} the {buried} skeleton does not still need");

                ctx.Log($"all owned: eggFrac={eggFrac:F3} (<=25% of {preNerfEggFrac:F3}), 0 shards, " +
                        $"treasureFrac={treasureFrac:F3}; site buried a {buried} {(BoneType)bone}");
            }
            finally
            {
                DigModeController.TestSuppressCrew = false;
                DigModeController.TestSuppressToys = false;
            }

            gm.TestForceRoam();
            gm.TestReset();
        }

        // NOTE. Two cases retired here WITH their systems (DinoDigger-5ve):
        //
        //   NestAssembly       certified the nest egg's five assembly sprites advancing as
        //                      shards banked. The nest no longer assembles anything — it is
        //                      scenery that echoes a banked bone — and the progress display
        //                      it stood for is now the skeleton board, certified by
        //                      SkeletonBoardFills (slots fill, species completes, the drawn
        //                      picture matches the bank, and it survives a save roundtrip).
        //   ShardHatchCeremony certified a full nest zooming the camera, hatching a new
        //                      shard-exclusive baby, and that baby tap-joining the team. Every
        //                      one of those behaviours still exists, at the Dino-Matic instead
        //                      of the nest, and is certified by ReviveCeremonyJoins — which
        //                      drives the SAME ceremony/join code paths this case did.
        //
        // Nothing that still exists lost coverage; only the shard bookkeeping the two cases
        // also asserted (requirement curves, remainder carry-over) went away, because the
        // shard economy did.

        private IEnumerator Case_FruitPunchNoCompound(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();

            Vector3 pos = WalkableNear(gm.TestMap, gm.TestBackhoe.transform.position);
            ItemPickup fruit = gm.TestSpawnItem(ItemType.Fruit, DinoType.TRex, 0, pos);
            ctx.Assert(fruit != null, "fruit spawn failed");

            // Wait for the landing arc + its landing punch to settle.
            yield return ctx.WaitSecondsScaled(1.1f);
            Vector3 baseScale = fruit.transform.localScale;
            ctx.Assert(baseScale.x > 0.5f, $"unexpected base scale {baseScale.x:F2}");

            // 8 rapid taps in the same frame — the punch registry must NOT compound.
            for (int i = 0; i < 8; i++)
            {
                ctx.TapWorld(fruit.transform.position);
            }

            yield return ctx.WaitSecondsScaled(1.0f);

            float ratio = fruit.transform.localScale.x / baseScale.x;
            ctx.Assert(Mathf.Abs(ratio - 1f) <= 0.05f, $"scale compounded: ratio {ratio:F2} (giant-blueberry regression)");
            ctx.Log($"8 rapid taps left scale at {ratio:F3}x base (no compounding)");
            gm.TestReset();
        }

        private IEnumerator Case_FeedAndGrow(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();

            DinoController dino = gm.TestSpawnDino(DinoType.TRex, GrowthStage.Baby);
            ctx.Assert(dino != null, "dino spawn failed");
            yield return ctx.WaitFrames(2);

            // First feed goes through the real tap -> walk -> eat path.
            Vector3 fpos = WalkableNear(gm.TestMap, dino.transform.position + new Vector3(1f, 0f, 0f));
            ItemPickup fruit = gm.TestSpawnItem(ItemType.Fruit, DinoType.TRex, 0, fpos);
            yield return ctx.WaitSecondsScaled(0.9f); // let it land + become edible

            bool heartsSeen = false;
            ctx.TapWorld(fruit.transform.position);
            yield return ctx.WaitUntil(() =>
            {
                if (dino.TestHearts != null && dino.TestHearts.particleCount > 0) heartsSeen = true;
                return fruit == null || fruit.IsConsumed;
            });
            for (int i = 0; i < 4; i++)
            {
                if (dino.TestHearts != null && dino.TestHearts.particleCount > 0) heartsSeen = true;
                yield return null;
            }

            ctx.Assert(fruit == null || fruit.IsConsumed, "fruit not consumed by dino");
            ctx.Assert(dino.FruitEaten == 1, $"FruitEaten={dino.FruitEaten} after one tap-feed");
            ctx.Assert(heartsSeen, "no hearts FX on feed");
            ctx.Assert(dino.Stage == GrowthStage.Baby, "dino grew before threshold");

            // Feed to Kid (2 total) then Big (5 total). Growth mechanic itself, not tapping.
            // Scales are now SUBTLE (baby 1.0 / kid ~1.15 / big ~1.3) since per-stage ART
            // carries most of the growth; read the expected scale from config rather than
            // hardcoding, and confirm each ~0.15 step lands within tolerance.
            // The tap-feed just fired an eat/grow punch-scale; let it decay so we read
            // the RESTING stage scale, not a mid-tween overshoot (the 1.10-vs-1.00 flake).
            yield return WaitForStableScale(ctx, dino);
            float baby = gm.TestConfig.StageScale(GrowthStage.Baby);
            ctx.Assert(Mathf.Abs(dino.transform.localScale.x - baby) < 0.05f,
                $"baby scale {dino.transform.localScale.x:F2} != config {baby:F2}");

            float kid = gm.TestConfig.StageScale(GrowthStage.Kid);
            dino.Feed();
            yield return ctx.WaitUntil(() => dino.Stage == GrowthStage.Kid);
            yield return WaitForStableScale(ctx, dino); // let the grow tween + punch settle
            ctx.Assert(dino.Stage == GrowthStage.Kid, $"stage {dino.Stage} not Kid after 2 fruit");
            ctx.Assert(Mathf.Abs(dino.transform.localScale.x - kid) < 0.05f,
                $"kid scale {dino.transform.localScale.x:F2} != config {kid:F2}");

            float big = gm.TestConfig.StageScale(GrowthStage.Big);
            dino.Feed();
            yield return ctx.WaitFrames(1);
            dino.Feed();
            yield return ctx.WaitFrames(1);
            dino.Feed();
            yield return ctx.WaitUntil(() => dino.Stage == GrowthStage.Big);
            yield return WaitForStableScale(ctx, dino);
            ctx.Assert(dino.Stage == GrowthStage.Big, $"stage {dino.Stage} not Big after 5 fruit");
            ctx.Assert(Mathf.Abs(dino.transform.localScale.x - big) < 0.05f,
                $"big scale {dino.transform.localScale.x:F2} != config {big:F2}");

            ctx.Log($"tap-fed then grew Baby->Kid(~{kid})->Big(~{big})");
            gm.TestReset();
        }

        // Per-stage ART: as a dino grows, the RENDERED sprite must come from the
        // matching stage's 8-dir array (baby/kid/adult), not just a rescaled single
        // set. Drives ForceStage through each stage and asserts the active sprite
        // belongs to that stage's set for the dino's current facing.
        private IEnumerator Case_GrowthStageArt(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();

            DinoController dino = gm.TestSpawnDino(DinoType.TRex, GrowthStage.Baby);
            ctx.Assert(dino != null, "dino spawn failed");
            yield return ctx.WaitFrames(2);

            GrowthStage[] stages = { GrowthStage.Baby, GrowthStage.Kid, GrowthStage.Big };
            foreach (GrowthStage stage in stages)
            {
                dino.ForceStage(stage);
                yield return ctx.WaitFrames(1);

                Dir8 f = dino.TestFacing;
                Sprite rendered = dino.TestSprite;
                Sprite expected = dino.TestStageDirSprite(stage, f);
                ctx.Assert(rendered != null, $"{stage}: no sprite rendered");

                // The walk cycler may have a STRIDE frame up at sample time (stages
                // with stride art, e.g. trex baby) — any frame of the stage's set
                // (idle or either stride) proves the stage array is active.
                DinoDefinition def = gm.TestConfig != null ? gm.TestConfig.GetDino(DinoType.TRex) : null;
                Sprite[] strideA = def != null ? def.StrideSprites(stage, 0) : null;
                Sprite[] strideB = def != null ? def.StrideSprites(stage, 1) : null;
                bool fromStage = rendered == expected ||
                    (strideA != null && System.Array.IndexOf(strideA, rendered) >= 0) ||
                    (strideB != null && System.Array.IndexOf(strideB, rendered) >= 0);
                ctx.Assert(fromStage,
                    $"{stage}: rendered sprite is not from the {stage} stage set (facing {f})");
            }

            // Per-stage art really differs: with baby/kid art generated + imported,
            // the baby front sprite must not be the same asset as the adult front.
            Sprite babyS = dino.TestStageDirSprite(GrowthStage.Baby, Dir8.S);
            Sprite bigS = dino.TestStageDirSprite(GrowthStage.Big, Dir8.S);
            ctx.Assert(babyS != null && bigS != null, "missing baby/adult front sprite");
            ctx.Assert(babyS != bigS,
                "baby and adult share the same front sprite (per-stage art not wired?)");

            ctx.Log("rendered sprite tracked the stage array across Baby->Kid->Big; baby != adult art");
            gm.TestReset();
        }

        private IEnumerator Case_DinoDance(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();

            DinoController dino = gm.TestSpawnDino(DinoType.TRex, GrowthStage.Kid);
            ctx.Assert(dino != null, "dino spawn failed");
            yield return ctx.WaitFrames(2);

            bool tapped = false;
            Action onTapped = () => tapped = true;
            GameEvents.DinoTapped += onTapped;

            try
            {
                bool heartsSeen = false;
                ctx.TapWorld(dino.transform.position);
                yield return ctx.WaitUntil(() =>
                {
                    if (dino.TestHearts != null && dino.TestHearts.particleCount > 0) heartsSeen = true;
                    return dino.TestBusyDancing;
                });

                ctx.Assert(tapped, "DinoTapped event did not fire");
                ctx.Assert(dino.TestBusyDancing, "dino did not enter dance");

                for (int i = 0; i < 5; i++)
                {
                    if (dino.TestHearts != null && dino.TestHearts.particleCount > 0) heartsSeen = true;
                    yield return null;
                }

                ctx.Assert(heartsSeen, "no hearts emitted during dance");
                yield return ctx.WaitUntil(() => !dino.TestBusyDancing);
                ctx.Log("dance triggered (event + hearts + tween) and completed cleanly");
            }
            finally
            {
                GameEvents.DinoTapped -= onTapped;
            }
        }

        private IEnumerator Case_BigDinoHelps(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();

            // Dig toys off (DinoDigger-z4d): this case picks an arbitrary interior tile and its
            // neighbour to prove the Big T-Rex bite also damages an adjacent cell, and a toy
            // answers a tap differently by design — a crystal pops its blob (and takes the whole
            // bite with it), a geode ARMS instead of taking damage at all, so a wait on "damage
            // went up" would never finish. The power itself is what is under test here.
            DigModeController.TestSuppressToys = true;
            try
            {
                // The dig helper is the T-REX superpower now: a BIG T-Rex that is a
                // walk BUDDY (fresh reset -> first spawn takes a free buddy slot).
                DinoController big = gm.TestSpawnDino(DinoType.TRex, GrowthStage.Big);
                ctx.Assert(big != null && big.IsBig, "big dino spawn failed");
                ctx.Assert(big.IsBuddy, "big T-Rex did not take a free buddy slot");
                yield return ctx.WaitFrames(2);

                yield return EnterDig(ctx);
                DigModeController dm = gm.TestDigMode;
                ctx.Assert(dm.TestHelperEnabled, "big-dino helper renderer not enabled in dig");

                DirtTile tile = null;
                for (int r = 1; r < dm.TestRows && tile == null; r++)
                {
                    for (int c = 0; c < dm.TestCols; c++)
                    {
                        DirtTile t = dm.TestTileAt(r, c);
                        if (t != null && !t.HasItem && !t.IsDestroyed && NeighborsIntactCount(dm, t) > 0)
                        {
                            tile = t;
                            break;
                        }
                    }
                }

                ctx.Assert(tile != null, "no suitable plain interior tile with a neighbor");

                // Hold the neighbours as REFERENCES, not coordinates: if this bite crumbles the
                // tile, gravity drops the column into its cell and a coordinate-addressed sum would
                // be comparing different tiles before and after (see NeighborTilesOf).
                int tileBefore = tile.TestDamage;
                List<DirtTile> neighbors = NeighborTilesOf(dm, tile);
                int neighborBefore = DamageSumOf(neighbors);
                ctx.TapWorld(tile.transform.position);
                yield return ctx.WaitUntil(() => tile.TestDamage > tileBefore);

                int neighborAfter = DamageSumOf(neighbors);
                ctx.Assert(tile.TestDamage >= tileBefore + 1, "tapped tile not damaged");
                ctx.Assert(neighborAfter >= neighborBefore + 1, "helper did not also damage an adjacent tile");
                ctx.Log($"helper enabled; tap damaged tile + adjacent (neighborSum {neighborBefore}->{neighborAfter})");
                gm.TestForceRoam();
            }
            finally
            {
                DigModeController.TestSuppressToys = false;
            }
        }

        // Buddy Dig Crew: every buddy species gets an automatic dig superpower, fired on
        // the child's own bites (never by the child). Covers helper display, the Trike
        // headbutt column-clear cadence, the Stego treasure-map start, the Brachio one-shot
        // bonus fruit (routed through ResolveDugItem), the Big-T-Rex adjacent clear, and the
        // no-buddy baseline.
        private IEnumerator Case_BuddyDigCrew(TestContext ctx)
        {
            GameManager gm = ctx.GM;

            // Dig toys off for this case (DinoDigger-z4d): it certifies the CREW superpowers,
            // and a toy in the wrong cell changes what a power is allowed to leave behind — a
            // boom geode caught by the Trike headbutt (or the T-Rex adjacent bite) ARMS instead
            // of crumbling, so the column is cleared by its whumph a beat later rather than by
            // the power itself, and the "power left nothing standing" assertions below would
            // read that correct behaviour as a failure. The toys have their own cases.
            DigModeController.TestSuppressToys = true;
            try
            {

                // ---- Two-helper crew + Stego treasure-map + Trike headbutt cadence ----
                gm.TestReset();
                gm.TestSpawnDino(DinoType.Triceratops, GrowthStage.Big); // Big -> headbutt every 4th bite
                gm.TestSpawnDino(DinoType.Stegosaurus, GrowthStage.Kid);
                yield return ctx.WaitFrames(2);

                yield return EnterDig(ctx);
                DigModeController dm = gm.TestDigMode;
                ctx.Assert(dm.TestCrewCount == 2, $"crew shows {dm.TestCrewCount} helpers (expected 2 buddies)");
                ctx.Assert(dm.TestCrewHas(DinoType.Triceratops) && dm.TestCrewHas(DinoType.Stegosaurus),
                    "crew missing the Triceratops/Stegosaurus helpers");
                ctx.Assert(dm.TestHelperEnabled, "slot-0 helper renderer not shown for a staffed crew");

                // Stego treasure-map: the buried peeks flash and settle brighter than the 0.55 default.
                List<DirtTile> buried = dm.TestBuriedTiles();
                ctx.Assert(buried.Count > 0, "no buried tiles to brighten");
                yield return ctx.WaitSecondsScaled(1f); // let the flash tween settle (~0.6s)
                bool anyBright = false;
                for (int i = 0; i < buried.Count; i++)
                {
                    if (buried[i] != null && buried[i].TestPeekAlpha > 0.7f)
                    {
                        anyBright = true;
                        break;
                    }
                }

                ctx.Assert(anyBright, "Stego treasure-map did not brighten any buried peek at round start");

                // Trike headbutt: every 4th bite (Big) clears the last-tapped tile's whole column.
                int budget = 0;
                while (dm.TestHeadbuttCount == 0 && dm.IsOpen && budget++ < 30)
                {
                    DirtTile plain = FindPlainTile(dm);
                    if (plain == null)
                    {
                        break;
                    }

                    // Pace to the arm and to gravity alike (a falling tile drops taps).
                    yield return ctx.WaitUntil(() => (dm.TestArmReady && !plain.IsFalling) || !dm.IsOpen);
                    if (!dm.IsOpen)
                    {
                        break;
                    }

                    int before = plain.TestDamage;
                    ctx.TapWorld(plain.transform.position);
                    yield return ctx.WaitUntil(() => plain.TestDamage > before || plain.IsDestroyed || !dm.IsOpen);
                }

                ctx.Assert(dm.TestHeadbuttCount >= 1, "Trike headbutt never fired on cadence");
                int col = dm.TestHeadbuttColumn;
                ctx.Assert(col >= 0, "headbutt column not recorded");
                yield return ctx.WaitSecondsScaled(0.7f); // let the top-to-bottom cascade finish
                if (dm.IsOpen)
                {
                    for (int r = 0; r < dm.TestRows; r++)
                    {
                        DirtTile t = dm.TestTileAt(r, col);
                        ctx.Assert(t == null || t.IsDestroyed, $"headbutt left tile ({r},{col}) intact");
                    }
                }

                gm.TestForceRoam();

                // ---- Brachiosaurus one-shot bonus fruit, routed through ResolveDugItem ----
                gm.TestReset();
                gm.TestSpawnDino(DinoType.Brachiosaurus, GrowthStage.Big); // Big -> bonus after the 6th bite
                yield return ctx.WaitFrames(2);
                yield return EnterDig(ctx);
                dm = gm.TestDigMode;

                int foundBefore = dm.TestFoundCount;
                budget = 0;
                while (dm.TestBonusFruitDropped == 0 && dm.IsOpen && budget++ < 40)
                {
                    DirtTile plain = FindPlainTile(dm);
                    if (plain == null)
                    {
                        break;
                    }

                    yield return ctx.WaitUntil(() => (dm.TestArmReady && !plain.IsFalling) || !dm.IsOpen);
                    if (!dm.IsOpen)
                    {
                        break;
                    }

                    int before = plain.TestDamage;
                    ctx.TapWorld(plain.transform.position);
                    yield return ctx.WaitUntil(() => plain.TestDamage > before || plain.IsDestroyed || !dm.IsOpen);
                }

                ctx.Assert(dm.TestBonusFruitDropped == 1,
                    $"Brachio bonus fruit dropped {dm.TestBonusFruitDropped}x (expected exactly 1)");
                ctx.Assert(dm.TestFoundCount > foundBefore, "bonus fruit not banked into the dug batch");

                // More bites must NOT drop a second bonus (strictly one-shot per round).
                DirtTile more = FindPlainTile(dm);
                if (more != null && dm.IsOpen)
                {
                    yield return TapTileUntilDestroyed(ctx, dm, more);
                }

                ctx.Assert(dm.TestBonusFruitDropped == 1, "Brachio bonus fruit dropped more than once");

                // ResolveDugItem coverage: the bonus rides the normal dug-item batch (_found),
                // which FinishDig runs through ResolveDugItem exactly like any dug fruit. Prove
                // that a bonus-fruit DugItemInfo passes cleanly through the REAL resolution (the
                // glut guard may downgrade it to treasure — that IS the guard applying), so it
                // can never wedge or throw. We deliberately do NOT finish the round here: a
                // dig-out spills+banks a random amount of treasure, which would inflate the
                // persistent wallet over the town's build threshold and let the always-on town
                // builder spend during a later count-exact case (TreasureCounter).
                DugItemInfo bonusResolved = gm.TestResolveItem(
                    new DugItemInfo(ItemType.Fruit, DinoType.TRex, 0, Vector3.zero));
                ctx.Assert(bonusResolved.Type == ItemType.Fruit || bonusResolved.Type == ItemType.Treasure,
                    $"bonus fruit resolved to an unexpected {bonusResolved.Type}");
                gm.TestForceRoam();

                // ---- No-buddy dig: no helpers, plain digging still works ----
                gm.TestReset();
                yield return EnterDig(ctx);
                dm = gm.TestDigMode;
                ctx.Assert(dm.TestCrewCount == 0, $"no-buddy dig shows {dm.TestCrewCount} helpers (expected 0)");
                ctx.Assert(!dm.TestHelperEnabled, "no-buddy dig still shows a helper renderer");

                DirtTile plainSolo = FindPlainTile(dm);
                ctx.Assert(plainSolo != null, "no plain tile in the no-buddy dig");
                yield return TapTileUntilDestroyed(ctx, dm, plainSolo);
                ctx.Assert(plainSolo.IsDestroyed, "plain tile did not crumble in the no-buddy dig");
                gm.TestForceRoam();

                // ---- Big T-Rex still clears an adjacent tile ----
                gm.TestReset();
                gm.TestSpawnDino(DinoType.TRex, GrowthStage.Big);
                yield return ctx.WaitFrames(2);
                yield return EnterDig(ctx);
                dm = gm.TestDigMode;
                ctx.Assert(dm.TestCrewHas(DinoType.TRex) && dm.TestHelperEnabled, "Big T-Rex helper not shown");

                DirtTile target = null;
                for (int r = 1; r < dm.TestRows && target == null; r++)
                {
                    for (int c = 0; c < dm.TestCols; c++)
                    {
                        DirtTile t = dm.TestTileAt(r, c);
                        if (t != null && !t.HasItem && !t.IsDestroyed && NeighborsIntactCount(dm, t) > 0)
                        {
                            target = t;
                            break;
                        }
                    }
                }

                ctx.Assert(target != null, "no interior plain tile with an intact neighbor");
                List<DirtTile> targetNeighbors = NeighborTilesOf(dm, target); // references: gravity moves cells
                int nBefore = DamageSumOf(targetNeighbors);
                int tBefore = target.TestDamage;
                ctx.TapWorld(target.transform.position);
                yield return ctx.WaitUntil(() => target.TestDamage > tBefore || target.IsDestroyed);
                int nAfter = DamageSumOf(targetNeighbors);
                ctx.Assert(nAfter >= nBefore + 1, "Big T-Rex did not also clear an adjacent tile");

                ctx.Log("crew: 2 helpers + Stego map + Trike headbutt; Brachio bonus x1 through ResolveDugItem; " +
                        "no-buddy clean; Big T-Rex adjacent clear ok");
                gm.TestForceRoam();
            }
            finally
            {
                DigModeController.TestSuppressToys = false;
            }
        }

        // Surprise Pockets: one wiggling non-item tile per site fires a delightful one-shot
        // when cracked (from a last-seen-excluded pool). Covers placement (exactly one, no
        // peek), Giggle banking 3 coins through the guarded collect path, exactly-once firing
        // across the crew-clear + tap paths, the round still finishing with the pocket
        // uncracked, and the last-seen exclusion rotating the pool.
        private IEnumerator Case_SurprisePocket(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            DigModeController dm = gm.TestDigMode;
            ctx.Assert(dm != null, "no dig controller");

            try
            {
                // DEFLAKE (DinoDigger-38r). The real cause was NOT a crew superpower: the
                // GEODE this case fires in section 3 crumbles its 8-neighbour ring through
                // DELAYED callbacks addressed by ROW/COL (0.42s of scaled stagger), and this
                // case re-enters a dig almost instantly afterwards — TestForceRoam leaves the
                // backhoe parked on the very mound it just dug, so the next EnterDig opens a
                // brand-new grid within a frame or two. The tail of the old ring then landed
                // on the NEW site and crumbled whatever sat at those coordinates, sometimes
                // its untouched pocket. Fixed site-side with a site-generation guard on both
                // staggered cascades. The crew pin below stays as belt-and-braces (it removes
                // the OTHER non-tap paths — T-Rex adjacent clear, Trike column — from this
                // case entirely; BuddyDigCrew still covers them), and the case now prints
                // DigModeController's firing breadcrumb if the pocket ever fires anyway.
                DigModeController.TestSuppressCrew = true;

                // Dig toys off too (DinoDigger-z4d), for the same "no other path" reason the crew
                // pin exists: this case banks an EXACT 3 coins for the Giggle Pocket, and a
                // crystal auto-pop or a pot cracked open by a falling tile would quietly add coins
                // to that count. The toys have their own cases.
                DigModeController.TestSuppressToys = true;

                // ---- Placement: exactly one surprise tile, on a non-item tile, no peek ----
                gm.TestReset();
                DigModeController.TestForceSurpriseKind = -1;
                dm.TestBuildThemedSite(null);
                yield return ctx.WaitFrames(1);

                DirtTile surprise = dm.TestSurpriseTile;
                ctx.Assert(surprise != null, "no surprise tile placed at the site");
                ctx.Assert(!surprise.HasItem, "surprise tile sits on a buried-item tile");
                ctx.Assert(!surprise.TestPeekEnabled, "surprise tile shows a buried peek");
                ctx.Assert(surprise.TestIsSurprise, "surprise tile not marked wiggling");

                int surpriseCount = 0;
                IReadOnlyList<DirtTile> tiles = dm.TestTiles;
                for (int i = 0; i < tiles.Count; i++)
                {
                    if (tiles[i] != null && tiles[i].TestIsSurprise)
                    {
                        surpriseCount++;
                    }
                }

                ctx.Assert(surpriseCount == 1, $"expected exactly 1 surprise tile, found {surpriseCount}");
                gm.TestForceRoam();

                // ---- Last-seen exclusion rotates the pool (no two sites in a row alike) ----
                int prevKind = -1;
                for (int s = 0; s < 6; s++)
                {
                    dm.TestBuildThemedSite(null);
                    yield return ctx.WaitFrames(1);
                    int kind = dm.TestSurpriseKind;
                    if (s > 0)
                    {
                        ctx.Assert(kind != prevKind, $"surprise kind {kind} repeated the last-seen kind");
                    }

                    prevKind = kind;
                    gm.TestForceRoam();
                }

                // ---- Giggle Pocket fires ONCE on a tap and banks 3 coins ----
                // Count TreasureCollected events: the always-on town auto-spend uses SetCount
                // (no event), so this stays exact even with a fat late-suite wallet.
                int bankEvents = 0;
                Action<int> onBank = _ => bankEvents++;
                GameEvents.TreasureCollected += onBank;
                try
                {
                    gm.TestReset();
                    DigModeController.TestForceSurpriseKind = 0; // Giggle
                    yield return EnterDig(ctx);
                    dm = gm.TestDigMode;
                    DirtTile pocket = dm.TestSurpriseTile;
                    ctx.Assert(pocket != null && !pocket.HasItem, "Giggle site has no clean surprise tile");

                    // GRAVITY (DinoDigger-7fw): clearing the pocket drops its column, and each
                    // landing cracks the tile under it. On a soft theme a 1-tap tile is COMPLETED
                    // by that crack, which could uncover buried items and — in the corner case
                    // where they were the last ones — end the round, breaking the "cracking the
                    // pocket wrongly ended the round" assertion below for a reason that has
                    // nothing to do with the pocket. Pinning this one column at 3 taps makes the
                    // cascade purely cosmetic here; the chain itself is CascadeNeverWedges's job.
                    PinColumnHardness(dm, pocket.Col);

                    bankEvents = 0;
                    yield return TapTileUntilDestroyed(ctx, dm, pocket);
                    ctx.Assert(dm.TestSurpriseFired, "Giggle pocket did not fire when cracked");
                    ctx.Assert(dm.TestSurpriseFireCount == 1,
                        $"Giggle fired {dm.TestSurpriseFireCount}x on one crack");

                    // The firing breadcrumb must actually be recorded and name the tap path —
                    // otherwise the "never cracked" check further down would report an empty
                    // string on the day it matters.
                    ctx.Assert(dm.TestSurpriseFiredBy.Contains("player bite"),
                        $"tap-cracked pocket recorded cause '{dm.TestSurpriseFiredBy}' (expected a player bite)");

                    yield return ctx.WaitUntil(() => bankEvents >= 3, 25f,
                        () => $"Giggle banked only {bankEvents}/3 coins");
                    yield return ctx.WaitSecondsScaled(0.4f); // let any stray extra bank surface
                    ctx.Assert(bankEvents == 3, $"Giggle banked {bankEvents} coins (expected 3)");
                    ctx.Assert(!gm.State.Is(GameState.Roam), "cracking the pocket wrongly ended the round");
                    gm.TestForceRoam();
                }
                finally
                {
                    GameEvents.TreasureCollected -= onBank;
                }

                // ---- Never fires twice across a crew-clear + a follow-up tap ----
                gm.TestReset();
                DigModeController.TestForceSurpriseKind = 2; // Geode (a chain-clear path)
                yield return EnterDig(ctx);
                dm = gm.TestDigMode;
                DirtTile pocket2 = dm.TestSurpriseTile;
                ctx.Assert(pocket2 != null, "no surprise tile for the double-fire check");
                Vector3 pocketPos = pocket2.transform.position; // capture before it is destroyed

                dm.TestClearSurpriseTile(); // crew-clear chokepoint fires it once
                yield return ctx.WaitFrames(1);
                ctx.Assert(dm.TestSurpriseFired && dm.TestSurpriseFireCount == 1,
                    $"crew-clear fired {dm.TestSurpriseFireCount}x (expected 1)");

                // Re-tapping where the pocket USED to be must not re-fire it. Under gravity
                // (DinoDigger-7fw) that spot is no longer empty — the column above dropped into
                // it — so this now taps a perfectly ordinary tile, which is if anything a
                // stronger check: the fire count must hold at 1 through a real bite there.
                ctx.TapWorld(pocketPos);
                yield return ctx.WaitFrames(3);
                ctx.Assert(dm.TestSurpriseFireCount == 1,
                    $"pocket fired again on re-tap ({dm.TestSurpriseFireCount})");
                gm.TestForceRoam();

                // ---- Round still finishes with the pocket left uncracked ----
                gm.TestReset();
                DigModeController.TestForceSurpriseKind = 0; // (kind irrelevant; never cracked)
                yield return EnterDig(ctx);
                dm = gm.TestDigMode;
                ctx.Assert(dm.TestSurpriseTile != null, "no surprise tile for the finish check");
                ctx.Assert(dm.TestCrewCount == 0,
                    $"{dm.TestCrewCount} helper(s) staffed despite the crew pin — a superpower " +
                    "could crack the pocket without a tap");

                // The gravity cascade adds a clearing path this check has to survive: tiles
                // dropped onto the pocket. The engine exempts the pocket from landing cracks
                // (ApplyLandingCracks) precisely so a mystery tile is always DISCOVERED and
                // never squashed — digging out the whole site below it must still leave it
                // uncracked, which is what the assertion after this loop proves.

                int guard = 0;
                while (gm.State.Is(GameState.Dig) && dm.TestBuriedCount > 0 && guard++ < 60)
                {
                    List<DirtTile> remaining = dm.TestBuriedTiles();
                    if (remaining.Count == 0)
                    {
                        break;
                    }

                    yield return TapTileUntilDestroyed(ctx, dm, remaining[0]);
                }

                yield return ctx.WaitUntil(() => gm.State.Is(GameState.Roam), 25f,
                    "round never returned to roam after every buried item was uncovered");
                ctx.Assert(!dm.TestSurpriseFired,
                    "surprise fired even though it was never cracked — fired by: " +
                    (string.IsNullOrEmpty(dm.TestSurpriseFiredBy) ? "(no breadcrumb)" : dm.TestSurpriseFiredBy));

                ctx.Log("1 wiggling non-item pocket/site (no peek); Giggle banks 3 coins; fires exactly once " +
                        "across crew-clear + tap; round finishes with an uncracked pocket; pool rotates");
            }
            finally
            {
                DigModeController.TestForceSurpriseKind = -1;
                DigModeController.TestSuppressCrew = false;
                DigModeController.TestSuppressToys = false;
            }
        }

        // ========================================================= GRAVITY CASCADE

        // Tiles fall. Clearing a tile drops every tile above it in the column onto the next
        // occupied cell (or the pit floor), each landing deals one hardness tick to what it
        // lands on, and the whole board is resolved SYNCHRONOUSLY (only the travel is a tween),
        // so this case can assert the settled state on the same frame it clears a tile.
        // Covers: the column shift, the landing crack, a buried peek riding its own tile down
        // (items fall WITH their tile), the board resting exactly on its cells, settle
        // idempotence, and taps staying live through a cascade.
        private IEnumerator Case_TilesFallAndSettle(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();

            try
            {
                // No crew: every superpower is a clearing path of its own, and this case is
                // about what exactly ONE clear does. (BuddyDigCrew covers the powers;
                // CascadeNeverWedges covers them all cascading together.)
                DigModeController.TestSuppressCrew = true;

                // No toys either (DinoDigger-z4d), for exactly the same reason: a crystal is
                // exempt from landing cracks, a geode arms instead of taking one, and a pot pays
                // coins — each of them a different answer to "what does ONE clear do". They also
                // eat the columns this case needs a clean full one of. BoomChainsResolve covers
                // toys cascading; the real-site smoke cases (DirtTileDamage, PeekVisible,
                // MoundToDig...) still dig sites with the toy roller live.
                DigModeController.TestSuppressToys = true;

                // No fossil bones either (DinoDigger-0z5/-5ve), for the third time for the same
                // reason: a bone spans 2-4 cells that then refuse to hold an item, so a bone
                // layer silently shrinks the pool of clean vertical pairs this case builds its
                // fall out of — and a bone cell clearing is its own beat (BoneSpansCells owns
                // it), not "what does ONE clear do". Bones are gated on owning every egg species
                // so a reset board would not roll one anyway; pinning it says so out loud and
                // keeps that true if the gate ever moves.
                DigModeController.TestSuppressBones = true;

                yield return EnterDig(ctx);
                DigModeController dm = gm.TestDigMode;
                ctx.Assert(dm.TestRows >= 3, $"grid is only {dm.TestRows} rows — too shallow to drop a tile");

                // ---- A mid-column clear drops the column by exactly one row ----
                int col = FindDropColumn(dm);
                ctx.Assert(col >= 0, "no full pocket-free column to drop");
                int mid = dm.TestRows - 2;                       // one above the deepest row
                DirtTile above = dm.TestTileAt(mid - 1, col);    // must end up at `mid`
                DirtTile below = dm.TestTileAt(mid + 1, col);    // must take the landing crack
                ctx.Assert(above != null && below != null, "column too shallow for a fall onto a tile");

                // Pin the whole column at 3 taps so no landing crack can COMPLETE a tile: this
                // section is about the crack being dealt and the column shifting by one, not
                // about the chain a completed tile starts (CascadeNeverWedges drives that).
                PinColumnHardness(dm, col);
                int heightBefore = dm.TestColumnCount(col);
                int cracksBefore = dm.TestLandingCracks;
                List<DirtTile> buriedBefore = dm.TestBuriedTiles();
                ctx.Assert(buriedBefore.Count > 0, "site buried nothing — no peeks to check");

                dm.TestClearCell(mid, col); // engine chokepoint: clear + collect + cascade, all now

                ctx.Assert(dm.TestColumnCount(col) == heightBefore - 1,
                    $"column {col} holds {dm.TestColumnCount(col)} tiles after one clear (expected {heightBefore - 1})");
                ctx.Assert(above.Row == mid && above.Col == col,
                    $"tile above the hole sits at r{above.Row}c{above.Col} (expected r{mid}c{col})");
                ctx.Assert(dm.TestTileAt(mid, col) == above, "grid does not report the fallen tile in the hole");
                ctx.Assert(dm.TestTileAt(0, col) == null, "top cell not vacated after the column dropped");
                ctx.Assert(below.TestDamage == 1,
                    $"landing dealt {below.TestDamage} ticks to the tile below (expected exactly 1)");
                ctx.Assert(dm.TestLandingCracks > cracksBefore, "engine recorded no landing crack at all");
                ctx.Assert(dm.TestFloaterReport() == "", $"board not settled: {dm.TestFloaterReport()}");
                ctx.Assert(dm.TestSettleImmediately() == 1,
                    "re-settling a settled board moved something (the settle is not idempotent)");

                // ---- Items fall WITH their tile: a peek is never orphaned ----
                //
                // BUILT, NOT FOUND. This used to scan the generated site for a buried tile that
                // happened to be sitting on a clearable plain one, which made the case a bet on
                // the layout the RNG dealt: every site-generation change upstream (toys, then the
                // fossil bone layer, then simply a different point in the random stream because
                // an earlier case rolled more) re-rolls that bet, and it eventually comes up
                // empty — "no buried tile sitting on a clearable plain tile" is the case failing
                // to SET ITSELF UP, not the cascade being broken. So the configuration is now
                // constructed: find a clean vertical pair of plain tiles and bury an item on the
                // upper one through the same bookkeeping generation uses (TestBuryItemAt refuses
                // any cell generation would refuse, so the board stays one a real site could
                // produce). The behaviour under test — an item riding its tile down — is
                // unchanged; only the setup stopped being a lottery.
                ctx.Assert(dm.IsOpen,
                    "the first clear collected the site's last buried item and finished the " +
                    "round, so there is no board left to drop a buried tile through");

                DirtTile buriedFaller = null;
                for (int r = dm.TestRows - 2; r >= 1 && buriedFaller == null; r--)
                {
                    for (int c = 0; c < dm.TestCols && buriedFaller == null; c++)
                    {
                        DirtTile upper = dm.TestTileAt(r, c);
                        DirtTile under = dm.TestTileAt(r + 1, c);
                        if (upper == null || under == null || upper.IsDestroyed || under.IsDestroyed)
                        {
                            continue;
                        }

                        // The tile BELOW must be an ordinary clearable one: an item/pocket/bone
                        // cell below would make the clear mean something other than "make a hole".
                        if (under.HasItem || under.IsSurprise || under.CoversBone ||
                            under.Kind != DigTileKind.Dirt || upper.IsSurprise)
                        {
                            continue;
                        }

                        // Reuse a naturally buried upper tile when there is one, else bury our
                        // own. Either way the assertions below are about the SAME mechanism.
                        if (upper.HasItem || dm.TestBuryItemAt(r, c, ItemType.Treasure, 0))
                        {
                            buriedFaller = upper;
                        }
                    }
                }

                ctx.Assert(buriedFaller != null,
                    "could not build a buried tile sitting on a clearable plain tile (no clean " +
                    "vertical pair of plain dirt cells left on the board)");
                ctx.Assert(dm.TestBuriedTiles().Contains(buriedFaller),
                    "the tile chosen to fall is not registered as buried");
                int buriedRow = buriedFaller.Row;
                int buriedCol = buriedFaller.Col;
                ItemType buriedType = dm.TestBuriedType(buriedFaller);
                PinColumnHardness(dm, buriedCol); // no chain: exactly one row of drop to assert
                dm.TestClearCell(buriedRow + 1, buriedCol);

                ctx.Assert(buriedFaller.Row == buriedRow + 1 && buriedFaller.Col == buriedCol,
                    $"buried tile did not ride its column down (r{buriedFaller.Row}c{buriedFaller.Col})");
                ctx.Assert(dm.TestTileAt(buriedRow + 1, buriedCol) == buriedFaller,
                    "grid lost track of the fallen buried tile");
                ctx.Assert(buriedFaller.TestPeekEnabled && buriedFaller.TestPeekAlpha > 0.01f,
                    "buried peek went dark after the fall");
                ctx.Assert(dm.TestBuriedTiles().Contains(buriedFaller),
                    "buried bookkeeping lost the item when its tile fell");
                ctx.Assert(dm.TestBuriedType(buriedFaller) == buriedType,
                    "buried item changed identity across the fall");

                // ---- Every tile comes to rest exactly on its cell (travel tween lands) ----
                yield return ctx.WaitSecondsScaled(1f);
                if (dm.IsOpen)
                {
                    int checkedTiles = 0;
                    for (int r = 0; r < dm.TestRows; r++)
                    {
                        for (int c = 0; c < dm.TestCols; c++)
                        {
                            DirtTile t = dm.TestTileAt(r, c);
                            if (t == null)
                            {
                                continue;
                            }

                            float off = (t.transform.position - dm.TestCellPosition(r, c)).magnitude;
                            ctx.Assert(off < 0.05f, $"tile r{r}c{c} rests {off:F2}u off its cell");
                            ctx.Assert(!t.IsFalling, $"tile r{r}c{c} still falling a second after the cascade");
                            checkedTiles++;
                        }
                    }

                    ctx.Assert(checkedTiles > 0, "no tiles left to check for rest positions");

                    // Every peek still readable after the board moved under it.
                    List<DirtTile> buriedNow = dm.TestBuriedTiles();
                    for (int i = 0; i < buriedNow.Count; i++)
                    {
                        DirtTile b = buriedNow[i];
                        ctx.Assert(b != null && b.TestPeekEnabled && b.TestPeekAlpha > 0.01f,
                            "a buried tile lost its peek during the cascade");
                    }

                    // ---- Taps stay live: the child can dig straight through a cascade ----
                    DirtTile plain = FindPlainTile(dm);
                    ctx.Assert(plain != null, "no plain tile left to prove taps still work");
                    int dmgBefore = plain.TestDamage;
                    yield return ctx.WaitUntil(() => dm.TestArmReady && !plain.IsFalling, 10f,
                        "arm never parked after the cascade");
                    ctx.TapWorld(plain.transform.position);
                    yield return ctx.WaitUntil(() => plain.TestDamage > dmgBefore || plain.IsDestroyed, 15f,
                        "tap after a cascade never landed — falling stole the input");

                    ctx.Log($"clear dropped column {col} by 1 (landing crack dealt, {dm.TestLandingCracks} total); " +
                            $"buried {buriedType} rode its tile r{buriedRow}->r{buriedFaller.Row}; " +
                            "board rests on its cells; taps still live");
                }
                else
                {
                    ctx.Log($"clear dropped column {col} by 1 (landing crack dealt); " +
                            "cascade finished the round early — collection path clean");
                }
            }
            finally
            {
                DigModeController.TestSuppressCrew = false;
                DigModeController.TestSuppressToys = false;
                DigModeController.TestSuppressBones = false;
            }

            gm.TestForceRoam();
        }

        // Worst-case chaining: grind a whole column out from under the board one cell at a time
        // (every clear drops the rest of it and cracks it again), then fire a geode ring into
        // the middle — a radial clear whose staggered steps land while the board is still
        // moving. The settle must resolve every time, well inside its iteration cap, leaving no
        // floating tile, no console error, and a site that is either still playable or finished
        // cleanly. This is the case that would catch an infinite settle or a wedged board.
        private IEnumerator Case_CascadeNeverWedges(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            int errorsBefore = _errors.Count;

            try
            {
                DigModeController.TestSuppressCrew = true;

                // Toys off here too (DinoDigger-z4d): this case grinds a column out cell by cell
                // and then asserts it is EMPTY, which a boom geode legitimately breaks (it arms
                // and clears itself a beat later instead of crumbling under the clear). It also
                // needs a full, clean column to exist at all, and a site that rolled two crystal
                // clusters plus a geode and a pot can leave none. BoomChainsResolve is the toy
                // chaos case; this one is the engine's.
                DigModeController.TestSuppressToys = true;
                yield return EnterDig(ctx);
                DigModeController dm = gm.TestDigMode;

                int col = FindDropColumn(dm);
                ctx.Assert(col >= 0, "no full pocket-free column to grind");
                int worstPasses = 0;

                // Clear the DEEPEST cell over and over: each clear drops the entire remaining
                // column one row onto the floor and cracks it on the way, so the column is fed
                // through the engine tile by tile — the longest chain a single column can make.
                for (int i = 0; i < dm.TestRows && dm.IsOpen; i++)
                {
                    dm.TestClearCell(dm.TestRows - 1, col);
                    worstPasses = Mathf.Max(worstPasses, dm.TestSettlePasses);
                    ctx.Assert(dm.TestSettlePasses < dm.TestSettleCap,
                        $"settle needed {dm.TestSettlePasses} passes (cap {dm.TestSettleCap}) on column grind {i}");
                    ctx.Assert(dm.TestFloaterReport() == "",
                        $"column grind {i} left the board unsettled: {dm.TestFloaterReport()}");
                }

                ctx.Assert(dm.TestColumnCount(col) == 0 || !dm.IsOpen,
                    $"column {col} still holds {dm.TestColumnCount(col)} tiles after being ground out");

                // Geode on top of that: a radial 8-neighbour clear, staggered across ~0.5s, each
                // step landing on whatever gravity has dropped into those coordinates by then.
                if (dm.IsOpen)
                {
                    DirtTile center = FindAliveTile(dm);
                    if (center != null)
                    {
                        dm.TestFireGeode(center.Row, center.Col);
                    }
                }

                yield return ctx.WaitSecondsScaled(1.5f); // let the whole ring + its falls play out

                if (dm.IsOpen)
                {
                    int passes = dm.TestSettleImmediately();
                    worstPasses = Mathf.Max(worstPasses, passes);
                    ctx.Assert(passes >= 1 && passes < dm.TestSettleCap,
                        $"final settle took {passes} passes (cap {dm.TestSettleCap})");
                    ctx.Assert(dm.TestFloaterReport() == "",
                        $"board never settled after the geode: {dm.TestFloaterReport()}");

                    // Still playable: a tap must still dig, with the site in one piece.
                    DirtTile plain = FindPlainTile(dm);
                    if (plain != null)
                    {
                        int before = plain.TestDamage;
                        yield return ctx.WaitUntil(() => dm.TestArmReady && !plain.IsFalling, 10f,
                            "arm never parked after the worst-case cascade");
                        ctx.TapWorld(plain.transform.position);
                        yield return ctx.WaitUntil(() => plain.TestDamage > before || plain.IsDestroyed || !dm.IsOpen,
                            15f, "site stopped accepting taps after the worst-case cascade");
                    }
                }
                else
                {
                    // The cascade uncovered every buried item: the round must have ended the
                    // normal way rather than stranding the player in an empty pit.
                    yield return ctx.WaitUntil(() => gm.State.Is(GameState.Roam), 25f,
                        "cascade finished the dig but never returned to roam");
                }

                int newErrors = _errors.Count - errorsBefore;
                ctx.Assert(newErrors == 0,
                    $"{newErrors} console error(s) during the cascade: " +
                    (newErrors > 0 ? _errors[_errors.Count - 1] : ""));

                ctx.Log($"ground column {col} + geode chain: worst settle {worstPasses}/{dm.TestSettleCap} passes, " +
                        $"{dm.TestLandingCracks} landing cracks, board settled, zero errors");
            }
            finally
            {
                DigModeController.TestSuppressCrew = false;
                DigModeController.TestSuppressToys = false;
            }

            gm.TestForceRoam();
        }

        // ================================================================ DIG TOYS
        // Crystals, boom geodes and pinata pots (DinoDigger-z4d). All three are DirtTiles with a
        // Kind, so they fall and clear through the cascade engine above; these cases prove the
        // toy behaviour ON TOP of it — a tap taking a whole colour blob, a geode's fuse chaining
        // into that blob without wedging the settle, and a pot paying every coin it promised.
        //
        // Every one of them suppresses BOTH the crew and the random toy roll, so the board under
        // test is exactly the board the case built: a superpower or a rolled cluster wandering
        // into frame would decide the outcome instead of the feature.

        // Tap ONE crystal, get the whole connected same-colour blob: the flood fill takes every
        // crystal of that colour reachable 4-way, leaves a touching crystal of a DIFFERENT colour
        // alone, pays a coin per crystal, and leaves the board fully settled with the cascade
        // having run through the normal chokepoint.
        private IEnumerator Case_CrystalPopFloodFill(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();

            int bankEvents = 0;
            Action<int> onBank = _ => bankEvents++;

            try
            {
                DigModeController.TestSuppressCrew = true;
                DigModeController.TestSuppressToys = true;

                yield return EnterDig(ctx);
                DigModeController dm = gm.TestDigMode;

                // A 2x2 of clean cells one row down, so the tiles ABOVE the blob really do fall
                // into it (a blob in row 0 would pop with nothing over it to cascade).
                ctx.Assert(FindCleanSquare(dm, out int r0, out int c0),
                    "no 2x2 patch of plain, item-free tiles to build a crystal blob in");

                // Pin both columns at 3 taps BEFORE converting: a landing crack must not be able
                // to complete a tile and start a chain that decides this case's coin count. (The
                // pin resets max health, so it has to happen before SetCrystal seats hardness 1.)
                PinColumnHardness(dm, c0);
                PinColumnHardness(dm, c0 + 1);

                ctx.Assert(dm.TestSetCrystal(r0, c0, 0) && dm.TestSetCrystal(r0, c0 + 1, 0) &&
                           dm.TestSetCrystal(r0 + 1, c0, 0) && dm.TestSetCrystal(r0 + 1, c0 + 1, 0),
                    "could not seat the 4-crystal blob (a cell refused the conversion)");

                // A different colour touching the blob: it must NOT be swept up by the fill.
                DirtTile oddColor = null;
                if (dm.TestSetCrystal(r0 + 2, c0, 1))
                {
                    oddColor = dm.TestTileAt(r0 + 2, c0);
                }

                var blob = new List<DirtTile>
                {
                    dm.TestTileAt(r0, c0), dm.TestTileAt(r0, c0 + 1),
                    dm.TestTileAt(r0 + 1, c0), dm.TestTileAt(r0 + 1, c0 + 1),
                };

                for (int i = 0; i < blob.Count; i++)
                {
                    ctx.Assert(blob[i] != null && blob[i].Kind == DigTileKind.Crystal,
                        $"blob cell {i} is not a crystal after conversion");
                }

                ctx.Assert(dm.TestBlobSizeAt(r0, c0) == 4,
                    $"flood fill sees {dm.TestBlobSizeAt(r0, c0)} crystals (expected exactly the 4 " +
                    "same-colour ones — a different colour must not join the blob)");

                // ---- One tap pops all four ----
                GameEvents.TreasureCollected += onBank;
                bankEvents = 0;
                int expectedCoins = gm.TestConfig != null ? gm.TestConfig.DigCrystalCoins(4) : 4;

                DirtTile tapped = blob[0];
                yield return ctx.WaitUntil(() => dm.TestArmReady && !tapped.IsFalling, 10f,
                    "arm never parked before the crystal tap");
                ctx.TapWorld(tapped.transform.position);

                yield return ctx.WaitUntil(() => tapped == null || tapped.IsDestroyed || !dm.IsOpen, 20f,
                    "the tapped crystal never popped");

                // Null-tolerant throughout: if the cascade happened to uncover the last buried
                // item, the round ends and the site tears its tiles down — a destroyed tile is
                // still a popped one, and reading a property off it would throw.
                for (int i = 0; i < blob.Count; i++)
                {
                    ctx.Assert(blob[i] == null || blob[i].IsDestroyed,
                        $"blob crystal {i} survived the tap — the flood fill did not reach it");
                }

                ctx.Assert(dm.TestLastBlobSize == 4,
                    $"engine popped a blob of {dm.TestLastBlobSize} (expected 4)");
                ctx.Assert(dm.TestCrystalsPopped >= 4,
                    $"only {dm.TestCrystalsPopped} crystal(s) recorded as popped");
                if (oddColor != null && dm.IsOpen)
                {
                    ctx.Assert(!oddColor.IsDestroyed,
                        "the touching crystal of a DIFFERENT colour popped too — the fill ignored colour");
                }

                // ---- The cascade ran, and the board is settled ----
                if (dm.IsOpen)
                {
                    ctx.Assert(dm.TestFloaterReport() == "",
                        $"board not settled after the pop: {dm.TestFloaterReport()}");
                    ctx.Assert(dm.TestSettlePasses >= 1 && dm.TestSettlePasses < dm.TestSettleCap,
                        $"pop settled in {dm.TestSettlePasses} passes (cap {dm.TestSettleCap})");
                    ctx.Assert(dm.TestSettleImmediately() == 1,
                        "re-settling after a crystal pop moved something (the pop left the board unstable)");
                }

                // ---- Coins: one per crystal, banked through the normal reward path ----
                yield return ctx.WaitUntil(() => bankEvents >= expectedCoins, 30f,
                    () => $"crystal blob banked only {bankEvents}/{expectedCoins} coins");
                yield return ctx.WaitSecondsScaled(0.5f); // let any stray extra bank surface
                ctx.Assert(bankEvents == expectedCoins,
                    $"crystal blob banked {bankEvents} coins (expected {expectedCoins})");

                ctx.Log($"one tap popped all 4 same-colour crystals (a touching odd colour left " +
                        $"standing), banked {bankEvents} coins, board settled in " +
                        $"{dm.TestSettlePasses}/{dm.TestSettleCap} passes");
            }
            finally
            {
                GameEvents.TreasureCollected -= onBank;
                DigModeController.TestSuppressCrew = false;
                DigModeController.TestSuppressToys = false;
            }

            gm.TestForceRoam();
        }

        // A boom geode next to a crystal blob: the worst chain the toys can make. Tapping the
        // geode lights its fuse, the whumph clears a 3x3, a crystal caught in that 3x3 takes its
        // WHOLE blob with it, and every one of those clears feeds the same cascade. The chain
        // must resolve well inside the settle cap, leave no floating tile and log zero errors.
        private IEnumerator Case_BoomChainsResolve(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            int errorsBefore = _errors.Count;

            try
            {
                DigModeController.TestSuppressCrew = true;
                DigModeController.TestSuppressToys = true;

                yield return EnterDig(ctx);
                DigModeController dm = gm.TestDigMode;

                ctx.Assert(FindCleanSquare(dm, out int r0, out int c0),
                    "no 2x2 patch of plain, item-free tiles to build the chain in");

                ctx.Assert(dm.TestSetCrystal(r0, c0, 0) && dm.TestSetCrystal(r0 + 1, c0, 0) &&
                           dm.TestSetCrystal(r0 + 1, c0 + 1, 0),
                    "could not seat the crystal blob next to the geode");

                var blob = new List<DirtTile>
                {
                    dm.TestTileAt(r0, c0), dm.TestTileAt(r0 + 1, c0), dm.TestTileAt(r0 + 1, c0 + 1),
                };

                // The geode goes DIAGONALLY off the blob's corner, so its 3x3 covers part of the
                // blob but its own cell is not one of them — the chain has to travel.
                ctx.Assert(dm.TestSetGeode(r0, c0 + 1),
                    "could not seat the boom geode beside the blob");
                DirtTile geode = dm.TestTileAt(r0, c0 + 1);
                ctx.Assert(geode != null && geode.Kind == DigTileKind.Geode, "geode cell is not a geode");

                yield return ctx.WaitUntil(() => dm.TestArmReady && !geode.IsFalling, 10f,
                    "arm never parked before the geode tap");
                ctx.TapWorld(geode.transform.position);

                // The fuse is an anticipation beat, not an instant clear: the geode must still be
                // standing right after the hit, and go off shortly after.
                yield return ctx.WaitUntil(
                    () => geode == null || geode.IsGeodeArmed || geode.IsDestroyed || !dm.IsOpen, 20f,
                    "the tapped geode never lit its fuse");
                yield return ctx.WaitUntil(() => dm.TestGeodeBooms >= 1 || !dm.IsOpen, 20f,
                    () => $"geode fuse never went off ({dm.TestGeodeBooms} boom(s) recorded)");

                yield return ctx.WaitSecondsScaled(1.5f); // let the whole chain + its falls play

                ctx.Assert(dm.TestGeodeBooms == 1,
                    $"{dm.TestGeodeBooms} geode boom(s) recorded (expected exactly 1 — a re-armed " +
                    "fuse would double the blast)");
                // Null-tolerant: a chain that finished the round tears the tiles down, and a
                // destroyed tile is still a detonated one.
                ctx.Assert(geode == null || geode.IsDestroyed, "the geode survived its own boom");

                for (int i = 0; i < blob.Count; i++)
                {
                    ctx.Assert(blob[i] == null || blob[i].IsDestroyed,
                        $"crystal {i} of the blob survived — the boom did not chain into it");
                }

                ctx.Assert(dm.TestCrystalsPopped >= blob.Count,
                    $"boom popped only {dm.TestCrystalsPopped} crystal(s) (expected at least {blob.Count})");

                if (dm.IsOpen)
                {
                    int passes = dm.TestSettleImmediately();
                    ctx.Assert(passes >= 1 && passes < dm.TestSettleCap,
                        $"final settle took {passes} passes (cap {dm.TestSettleCap})");
                    ctx.Assert(dm.TestFloaterReport() == "",
                        $"board never settled after the chain: {dm.TestFloaterReport()}");

                    // Still playable: the site must accept a tap with the chain behind it.
                    DirtTile plain = FindPlainTile(dm);
                    if (plain != null)
                    {
                        int before = plain.TestDamage;
                        yield return ctx.WaitUntil(() => dm.TestArmReady && !plain.IsFalling, 10f,
                            "arm never parked after the boom chain");
                        ctx.TapWorld(plain.transform.position);
                        yield return ctx.WaitUntil(
                            () => plain.TestDamage > before || plain.IsDestroyed || !dm.IsOpen, 15f,
                            "site stopped accepting taps after the boom chain");
                    }
                }
                else
                {
                    // The chain uncovered every buried item: the round must have ended the normal
                    // way rather than stranding the player in an empty pit.
                    yield return ctx.WaitUntil(() => gm.State.Is(GameState.Roam), 25f,
                        "boom chain finished the dig but never returned to roam");
                }

                int newErrors = _errors.Count - errorsBefore;
                ctx.Assert(newErrors == 0,
                    $"{newErrors} console error(s) during the boom chain: " +
                    (newErrors > 0 ? _errors[_errors.Count - 1] : ""));

                ctx.Log($"geode fused then cleared its 3x3, chaining into the blob " +
                        $"({dm.TestCrystalsPopped} crystals popped, {dm.TestAutoPops} auto-pop pass(es)); " +
                        $"settle {dm.TestSettlePasses}/{dm.TestSettleCap} passes, board clean, zero errors");
            }
            finally
            {
                DigModeController.TestSuppressCrew = false;
                DigModeController.TestSuppressToys = false;
            }

            gm.TestForceRoam();
        }

        // A pinata pot takes two taps — crack, then break — and pays every coin it sprayed. The
        // fountain is decorative; the money is banked coin by coin through the normal reward path,
        // so this counts BANK EVENTS (the town's auto-spend uses SetCount and raises none).
        private IEnumerator Case_PinataPotPays(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();

            int bankEvents = 0;
            Action<int> onBank = _ => bankEvents++;

            try
            {
                DigModeController.TestSuppressCrew = true;
                DigModeController.TestSuppressToys = true;

                yield return EnterDig(ctx);
                DigModeController dm = gm.TestDigMode;

                ctx.Assert(FindCleanSquare(dm, out int r0, out int c0),
                    "no plain, item-free cell to put a pinata pot in");

                // Same pin as the crystal case: keep the pot's own column from chain-completing
                // tiles, so the only coins banked in the window are the pot's.
                PinColumnHardness(dm, c0);
                ctx.Assert(dm.TestSetPot(r0, c0), "could not seat the pinata pot");
                DirtTile pot = dm.TestTileAt(r0, c0);
                ctx.Assert(pot != null && pot.Kind == DigTileKind.Pot, "pot cell is not a pot");
                ctx.Assert(pot.TestMaxHealth == 2,
                    $"pot has {pot.TestMaxHealth} hit points (expected 2: crack, then break)");

                // ---- First tap CRACKS it (and must not pay) ----
                GameEvents.TreasureCollected += onBank;
                bankEvents = 0;

                yield return ctx.WaitUntil(() => dm.TestArmReady && !pot.IsFalling, 10f,
                    "arm never parked before the first pot tap");
                ctx.TapWorld(pot.transform.position);
                yield return ctx.WaitUntil(
                    () => pot == null || pot.TestDamage >= 1 || pot.IsDestroyed || !dm.IsOpen, 20f,
                    "the first tap never landed on the pot");
                ctx.Assert(pot != null && !pot.IsDestroyed,
                    "the pot broke on the FIRST tap (expected a crack first)");
                ctx.Assert(dm.TestPotsBroken == 0,
                    "the pot paid out on its crack — the fountain must wait for the break");

                // ---- Second tap BREAKS it: the fountain, and every coin banked ----
                yield return TapTileUntilDestroyed(ctx, dm, pot);
                yield return ctx.WaitUntil(() => dm.TestPotsBroken >= 1 || !dm.IsOpen, 20f,
                    "the pot never broke on the second tap");

                int coins = dm.TestLastPotCoins;
                ctx.Assert(dm.TestPotsBroken == 1, $"{dm.TestPotsBroken} pot(s) broke (expected 1)");
                ctx.Assert(coins >= 5 && coins <= 8,
                    $"pot sprayed {coins} coins (expected the configured 5-8)");

                yield return ctx.WaitUntil(() => bankEvents >= coins, 30f,
                    () => $"pot banked only {bankEvents}/{coins} sprayed coins");
                yield return ctx.WaitSecondsScaled(0.6f); // let any stray extra bank surface
                ctx.Assert(bankEvents == coins,
                    $"pot banked {bankEvents} coins but sprayed {coins} — the fountain and the " +
                    "wallet disagree");

                if (dm.IsOpen)
                {
                    ctx.Assert(dm.TestFloaterReport() == "",
                        $"board not settled after the pot broke: {dm.TestFloaterReport()}");
                }

                ctx.Log($"pot cracked on tap 1, broke on tap 2, sprayed and banked {coins} coins " +
                        "(crack paid nothing), board settled");
            }
            finally
            {
                GameEvents.TreasureCollected -= onBank;
                DigModeController.TestSuppressCrew = false;
                DigModeController.TestSuppressToys = false;
            }

            gm.TestForceRoam();
        }

        // ==================================================== THE TOY ROLLER (qhy)

        // THE ANTI-DULL GUARANTEE. Site generation used to roll each toy on its own independent
        // chance, which meant a site could legitimately come up with nothing on it — and two of
        // those in a row teach a toddler that digging is sometimes boring. Now every site picks
        // one FEATURED toy from the roster (crystal cluster / boom geode / pinata pot / surprise
        // pocket) and places it unconditionally, and never leads with the same one twice running.
        //
        // Drives site generation directly through TestBuildThemedSite (the same off-screen build
        // the surprise-pool rotation check uses), so N sites cost a frame each instead of N drives
        // across the island. Asserts three things per site: a feature was chosen, it is REALLY ON
        // THE BOARD (not merely recorded), and it is not the one the previous site led with.
        private IEnumerator Case_EveryDigHasAToy(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            DigModeController dm = gm.TestDigMode;
            ctx.Assert(dm != null, "no dig controller");

            try
            {
                // No crew: a superpower can clear tiles the moment a site opens, and this case is
                // about what site GENERATION produces, not what survives the first bite.
                DigModeController.TestSuppressCrew = true;

                // Bones off. They are the reward layer, not a toy — TestSuppressToys deliberately
                // leaves them alone — and a bone claims cells the roller would otherwise have to
                // place its feature around. (BoneSpansCells owns that interaction.)
                DigModeController.TestSuppressBones = true;

                gm.TestReset();
                DigModeController.TestResetPrimaryToy(); // start from a known "no history"
                ctx.Assert(DigModeController.TestLastPrimaryToy == -1,
                    "roller history did not clear — the first site's roll would be steered");

                // TEN sites, not the five the spec asks for. The no-repeat rule alone would be
                // satisfied forever by two toys ping-ponging, so this case also asserts the
                // roster actually rotates — and that assertion has to be one a healthy roller
                // cannot fail. Over 6 sites a legitimate A/B/A/B/A/B run comes up about 1 time in
                // 60; over 10 it is about 1 in 1500, which is the difference between a gate and a
                // coin flip. Each site is one off-screen build, so the extra four are free.
                const int sites = 10;
                int prev = -1;
                var seen = new List<int>();
                for (int s = 0; s < sites; s++)
                {
                    dm.TestBuildThemedSite(null);
                    yield return ctx.WaitFrames(1);

                    int primary = dm.TestPrimaryToy;
                    ctx.Assert(primary >= 0,
                        $"site {s} came up with no featured toy at all — the guarantee is broken");
                    ctx.Assert(primary != prev,
                        $"site {s} led with feature {primary} again (previous site led with {prev})");
                    ctx.Assert(DigModeController.TestLastPrimaryToy == primary,
                        $"site {s} featured {primary} but the roller remembered " +
                        $"{DigModeController.TestLastPrimaryToy}");

                    // The feature has to be ON THE BOARD. A recorded-but-absent feature would
                    // pass every assertion above and still be a site with nothing in it.
                    switch (primary)
                    {
                        case 0:
                            ctx.Assert(dm.TestKindCount(DigTileKind.Crystal) > 0,
                                $"site {s} featured a crystal cluster but has no crystal cells");
                            break;
                        case 1:
                            ctx.Assert(dm.TestKindCount(DigTileKind.Geode) > 0,
                                $"site {s} featured a boom geode but has none");
                            break;
                        case 2:
                            ctx.Assert(dm.TestKindCount(DigTileKind.Pot) > 0,
                                $"site {s} featured a pinata pot but has none");
                            break;
                        case 3:
                            ctx.Assert(dm.TestSurpriseTile != null,
                                $"site {s} featured the surprise pocket but none was placed");
                            break;

                        // WAVE 2 (DinoDigger-u47). The roster widened from four to eight, and
                        // this case is the guarantee's only witness — an entry it did not know
                        // about would pass silently as "some feature was chosen" while putting
                        // nothing on the board at all.
                        case 4:
                            ctx.Assert(dm.TestKindCount(DigTileKind.Water) > 0,
                                $"site {s} featured a water pocket but has none");
                            break;
                        case 5:
                            ctx.Assert(dm.TestKindCount(DigTileKind.Vein) > 1,
                                $"site {s} featured a gem vein but has {dm.TestKindCount(DigTileKind.Vein)} " +
                                "vein cells (a vein needs at least two to chain)");
                            break;
                        case 6:
                            ctx.Assert(dm.TestKindCount(DigTileKind.Mushroom) > 0,
                                $"site {s} featured a bouncy mushroom but has none");
                            break;
                        default:
                            ctx.Assert(dm.TestCritterCount > 0,
                                $"site {s} featured a dig critter but none is loose in the pit");
                            break;
                    }

                    // Whatever the feature was, the pocket is part of every site — the roster
                    // member that costs the board nothing.
                    ctx.Assert(dm.TestSurpriseTile != null, $"site {s} has no surprise pocket");

                    seen.Add(primary);
                    prev = primary;
                    gm.TestForceRoam();
                    yield return ctx.WaitFrames(1);
                }

                // The no-repeat rule alone would be satisfied by ping-ponging between two toys
                // forever; the weights are there to keep the roster in play.
                var distinct = new List<int>();
                for (int i = 0; i < seen.Count; i++)
                {
                    if (!distinct.Contains(seen[i]))
                    {
                        distinct.Add(seen[i]);
                    }
                }

                ctx.Assert(distinct.Count >= 3,
                    $"{sites} sites only ever featured {distinct.Count} different toys — the " +
                    "roster is not rotating");

                // Suppression still wins: a case that pins the toys off must get a bare board.
                DigModeController.TestSuppressToys = true;
                dm.TestBuildThemedSite(null);
                yield return ctx.WaitFrames(1);
                ctx.Assert(dm.TestPrimaryToy == -1,
                    "the guarantee overrode TestSuppressToys — every hand-built board is now dirty");
                ctx.Assert(dm.TestKindCount(DigTileKind.Crystal) == 0 &&
                           dm.TestKindCount(DigTileKind.Geode) == 0 &&
                           dm.TestKindCount(DigTileKind.Pot) == 0 &&
                           dm.TestKindCount(DigTileKind.Water) == 0 &&
                           dm.TestKindCount(DigTileKind.Vein) == 0 &&
                           dm.TestKindCount(DigTileKind.Mushroom) == 0 &&
                           dm.TestCritterCount == 0,
                    "toys placed at a site with the toy roller suppressed");
                gm.TestForceRoam();

                ctx.Log($"{sites} consecutive sites, every one featured a toy, no repeats, " +
                        $"{distinct.Count} different features used; suppression still bare");
            }
            finally
            {
                DigModeController.TestSuppressCrew = false;
                DigModeController.TestSuppressToys = false;
                DigModeController.TestSuppressBones = false;
                DigModeController.TestResetPrimaryToy();
            }
        }

        // ======================================================= FOSSIL BONES (0z5)

        // A bone spans several CELLS and lives under the tiles. Uncovering one cell is progress;
        // uncovering the last one pops the whole bone and banks it. The hard part is gravity: a
        // cleared cell is immediately refilled by the column above it, so this case clears a known
        // 1x3 femur cell by cell WITH the cascade running and proves the rule the engine is built
        // on — bone cells are fixed to the grid, and an uncovered cell STAYS uncovered even when a
        // falling tile buries it again. Progress toward a bone never regresses.
        private IEnumerator Case_BoneSpansCells(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();

            try
            {
                DigModeController.TestSuppressCrew = true;
                DigModeController.TestSuppressToys = true;

                DigModeController dm = gm.TestDigMode;
                ctx.Assert(dm != null, "no dig controller");

                // ---- The gate: no bones until every egg species is owned ----
                dm.TestBuildThemedSite(null);
                yield return ctx.WaitFrames(1);
                ctx.Assert(dm.TestBoneCount == 0,
                    $"{dm.TestBoneCount} bone(s) buried before any egg species was owned — bones " +
                    "must ride the same gate egg shards do");
                gm.TestForceRoam();

                gm.TestSpawnDino(DinoType.TRex, GrowthStage.Baby);
                gm.TestSpawnDino(DinoType.Triceratops, GrowthStage.Baby);
                gm.TestSpawnDino(DinoType.Brachiosaurus, GrowthStage.Baby);
                gm.TestSpawnDino(DinoType.Stegosaurus, GrowthStage.Baby);
                yield return ctx.WaitFrames(1);
                ctx.Assert(gm.TestEggSpeciesAllOwned, "need all egg species owned to unlock bones");

                // ---- Owned: a site buries a whole bone, every cell of it covered + peeking ----
                dm.TestBuildThemedSite(null);
                yield return ctx.WaitFrames(1);
                ctx.Assert(dm.TestBoneCount >= 1, "no bone buried at a site with every species owned");
                int cells = dm.TestBoneCells(0);
                ctx.Assert(cells >= 2 && cells <= 4, $"rolled bone spans {cells} cells (expected 2-4)");
                ctx.Assert(dm.TestBoneUncovered(0) == 0, "a freshly buried bone starts partly uncovered");

                int peeking = 0;
                IReadOnlyList<DirtTile> allTiles = dm.TestTiles;
                for (int i = 0; i < allTiles.Count; i++)
                {
                    DirtTile t = allTiles[i];
                    if (t != null && t.CoversBone)
                    {
                        ctx.Assert(t.TestPeekEnabled && t.TestPeekAlpha > 0.01f,
                            $"bone cell r{t.Row}c{t.Col} shows no peek — nothing telegraphs the bone");
                        ctx.Assert(!t.HasItem, $"bone cell r{t.Row}c{t.Col} also hides a buried item");
                        ctx.Assert(!t.IsSurprise, $"bone cell r{t.Row}c{t.Col} is also the surprise pocket");
                        ctx.Assert(t.Kind == DigTileKind.Dirt, $"bone cell r{t.Row}c{t.Col} is also a toy");
                        peeking++;
                    }
                }

                ctx.Assert(peeking == cells,
                    $"{peeking} tiles carry the bone peek but the bone spans {cells} cells");
                gm.TestForceRoam();

                // ---- A KNOWN 1x3 femur, cleared cell by cell, with the cascade running ----
                DigModeController.TestSuppressBones = true; // no rolled bone competing for cells
                dm.TestBuildThemedSite(null);
                yield return ctx.WaitFrames(1);
                ctx.Assert(dm.TestBoneCount == 0, "TestSuppressBones did not suppress the rolled bone");

                ctx.Assert(FindBoneRow(dm, out int row, out int col),
                    "no clean 1x3 run of cells with a full column above it to bury a femur in");
                ctx.Assert(dm.TestPlaceBone(row, col, DigModeController.BoneTemplateFemurH, DinoType.TRex),
                    $"could not bury the 1x3 femur at r{row}c{col}");
                ctx.Assert(dm.TestBoneCount == 1 && dm.TestBoneCells(0) == 3,
                    $"placed bone spans {dm.TestBoneCells(0)} cells (expected 3)");

                // Pin all three columns at 3 taps: a landing crack on a 1-tap tile would COMPLETE
                // it and chain into another bone cell, so "one cell per clear" would stop being
                // the thing under test. (CascadeNeverWedges is where chains are the point.)
                for (int i = 0; i < 3; i++)
                {
                    PinColumnHardness(dm, col + i);
                }

                int bankedBefore = gm.TestBonesBanked;
                int femurBefore = gm.TestBoneCount(DinoType.TRex, (int)BoneType.Femur);

                for (int i = 0; i < 3; i++)
                {
                    ctx.Assert(dm.IsOpen, $"site closed before bone cell {i} could be uncovered");
                    dm.TestClearCell(row, col + i);
                    yield return ctx.WaitFrames(1);

                    ctx.Assert(dm.TestBoneUncovered(0) == i + 1,
                        $"{dm.TestBoneUncovered(0)} bone cells uncovered after clearing {i + 1}");
                    ctx.Assert(dm.TestBoneCellUncovered(0, i),
                        $"cell {i} did not register as uncovered when its covering tile cleared");

                    // NO REGRESSION. The column above dropped straight back into the cell we just
                    // cleared — so the bone is visually buried again — and every cell uncovered so
                    // far must still read as uncovered.
                    for (int k = 0; k <= i; k++)
                    {
                        ctx.Assert(dm.TestBoneCellUncovered(0, k),
                            $"bone cell {k} went BACK to covered after cell {i} was cleared — " +
                            "progress toward a bone must never regress");
                    }

                    if (i < 2)
                    {
                        ctx.Assert(dm.TestBonesPopped == 0,
                            $"bone popped after only {i + 1} of its 3 cells were uncovered");
                    }
                }

                // ---- The last cell pops the WHOLE bone, once, into the bank ----
                ctx.Assert(dm.TestBonesPopped == 1,
                    $"{dm.TestBonesPopped} whole-bone pops after uncovering all three cells (expected 1)");
                ctx.Assert(gm.TestBonesBanked == bankedBefore + 1,
                    $"bank went {bankedBefore} -> {gm.TestBonesBanked} (expected exactly one bone)");
                ctx.Assert(gm.TestBoneCount(DinoType.TRex, (int)BoneType.Femur) == femurBefore + 1,
                    "the banked bone did not land in the T-Rex femur slot");

                // The bone is spent: re-clearing a cell it used to sit under never re-pops it.
                if (dm.IsOpen && dm.TestTileAt(row, col) != null)
                {
                    dm.TestClearCell(row, col);
                    yield return ctx.WaitFrames(1);
                }

                ctx.Assert(dm.TestBonesPopped == 1, "a popped bone popped again");
                ctx.Assert(gm.TestBonesBanked == bankedBefore + 1, "a popped bone banked twice");

                if (dm.IsOpen)
                {
                    ctx.Assert(dm.TestFloaterReport() == "",
                        $"board not settled after the bone came out: {dm.TestFloaterReport()}");
                }

                ctx.Log($"gate holds until all 4 species owned; rolled bone spans {cells} cells, all " +
                        "peeking; hand-placed 1x3 femur uncovered cell by cell through a cascade " +
                        "(no regression), popped whole once and banked to T-Rex/femur");
            }
            finally
            {
                DigModeController.TestSuppressCrew = false;
                DigModeController.TestSuppressToys = false;
                DigModeController.TestSuppressBones = false;
            }

            gm.TestForceRoam();
            gm.TestReset();
        }

        /// <summary>A clean 1x3 horizontal run to bury a femur in: three side-by-side cells that
        /// site generation would accept (alive, plain dirt, item-free, not the pocket), each with
        /// at least one tile ABOVE it so clearing the cell really does drop a tile back into it —
        /// which is what makes the no-regression assertion mean something. Returns the LEFT cell.</summary>
        private bool FindBoneRow(DigModeController dm, out int row, out int col)
        {
            for (int r = dm.TestRows - 1; r >= 1; r--)
            {
                for (int c = 0; c + 2 < dm.TestCols; c++)
                {
                    if (IsToyCandidate(dm, r, c) && IsToyCandidate(dm, r, c + 1) &&
                        IsToyCandidate(dm, r, c + 2) &&
                        dm.TestTileAt(r - 1, c) != null && dm.TestTileAt(r - 1, c + 1) != null &&
                        dm.TestTileAt(r - 1, c + 2) != null)
                    {
                        row = r;
                        col = c;
                        return true;
                    }
                }
            }

            row = -1;
            col = -1;
            return false;
        }

        // ============================================================ TREASURE / UI

        private IEnumerator Case_TreasureCounter(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();

            // ROOT CAUSE of this case's timeouts (DinoDigger-9w5): the always-on town
            // builder spends the wallet the instant it can afford the next plot — inside the
            // very frame a coin banks, before this coroutine polls again. Every wait below is
            // an EXACT wallet value, so once the wallet was fat enough to break ground the
            // target number was gone before it could be observed and the case sat out its
            // whole budget waiting for it (nothing was slow; the wait was unsatisfiable).
            // Whether that happened depended on the wallet the run inherited, which is why it
            // read as load-flakiness. Freeze the queue: for this case the wallet is ours.
            TownController.TestSuspendBuilds = true;
            try
            {
                int before = gm.Save.Data.TreasureCount;
                Vector3 pos = WalkableNear(gm.TestMap, gm.TestBackhoe.transform.position + new Vector3(0.6f, 0.6f, 0f));

                // A coin (variant 0) banks its face value of 1. Wait on >= (monotone with the
                // queue frozen) and assert the exact value after: a poll that can only ever be
                // true on one specific frame is a race, a threshold is not.
                int coinValue = gm.TestConfig.TreasureValue(0);
                gm.TestSpawnItem(ItemType.Treasure, DinoType.TRex, 0, pos);
                yield return ctx.WaitUntil(() => gm.Save.Data.TreasureCount >= before + coinValue,
                    20f, "spawned coin never banked (arc -> counter flight -> wallet)");
                ctx.Assert(gm.Save.Data.TreasureCount == before + coinValue,
                    $"coin banked {gm.Save.Data.TreasureCount - before} (expected {coinValue})");

                var counter = gm.TestTreasureCounter;
                ctx.Assert(counter != null, "no treasure counter");
                ctx.Assert(counter.TestCount == gm.Save.Data.TreasureCount,
                    $"counter {counter.TestCount} != save {gm.Save.Data.TreasureCount}");
                ctx.Assert(counter.TestCountText == gm.Save.Data.TreasureCount.ToString(),
                    $"counter text '{counter.TestCountText}' != {gm.Save.Data.TreasureCount}");

                // Denominations: a gem (variant 1) banks its higher value in one collect.
                int afterCoin = gm.Save.Data.TreasureCount;
                int gemValue = gm.TestConfig.TreasureValue(1);
                Vector3 pos2 = WalkableNear(gm.TestMap, gm.TestBackhoe.transform.position + new Vector3(-0.6f, 0.6f, 0f));
                gm.TestSpawnItem(ItemType.Treasure, DinoType.TRex, 1, pos2);
                yield return ctx.WaitUntil(() => gm.Save.Data.TreasureCount >= afterCoin + gemValue,
                    20f, "spawned gem never banked");
                ctx.Assert(gm.Save.Data.TreasureCount == afterCoin + gemValue,
                    $"gem banked {gm.Save.Data.TreasureCount - afterCoin} (expected {gemValue})");
                ctx.Assert(counter.TestCount == gm.Save.Data.TreasureCount,
                    $"counter {counter.TestCount} != save {gm.Save.Data.TreasureCount} after gem");

                ctx.Log($"treasure {before}->{gm.Save.Data.TreasureCount} (coin+{coinValue}, gem+{gemValue}), UI text '{counter.TestCountText}'");
            }
            finally
            {
                TownController.TestSuspendBuilds = false;
            }
        }

        // ================================================================ SPAWNS

        private IEnumerator Case_MoundRespawn(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestConfig.MoundRespawnSeconds = 3f; // restored by the runner after the case

            DigMound m = FarthestActiveMound(gm);
            ctx.Assert(m != null, "no active mound");

            gm.Spawn.ScheduleRespawn(m);
            ctx.Assert(!m.IsActive, "mound not consumed when respawn scheduled");

            yield return ctx.WaitUntil(() => m.IsActive);

            ctx.Assert(gm.TestMap.IsWalkableWorld(m.transform.position), "respawned mound on a non-walkable cell");
            float sq = (m.transform.position - gm.TestBackhoe.transform.position).sqrMagnitude;
            ctx.Assert(sq >= 4f - 0.01f, $"respawned mound within backhoe clearance (sq={sq:F2})");
            ctx.Log($"mound respawned (~3s) at walkable cell, clearance sq={sq:F1}");
        }

        private IEnumerator Case_IdleAttract(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();

            bool fired = false;
            Action onIdle = () => fired = true;
            GameEvents.IdleAttract += onIdle;

            try
            {
                gm.ForceIdleAttract();
                yield return ctx.WaitFrames(3);
                ctx.Assert(fired, "IdleAttract did not fire (honk + nearest-mound pulse path)");
                ctx.Log("idle-attract fired: honk requested + nearest mound pulse");
            }
            finally
            {
                GameEvents.IdleAttract -= onIdle;
            }
        }

        // ================================================================== SAVE

        private IEnumerator Case_SaveRoundtrip(TestContext ctx)
        {
            yield return null;

            string path = SaveManager.TestFilePath;
            bool existed = false;
            byte[] backup = null;
            try
            {
                if (File.Exists(path))
                {
                    existed = true;
                    backup = File.ReadAllBytes(path);
                }
            }
            catch (Exception ex)
            {
                Debug.Log($"[IntegrationTest] save backup warning: {ex.Message}");
            }

            try
            {
                var sm = new SaveManager();
                sm.Data.TreasureCount = 4242;
                sm.Data.Dinos.Clear();
                sm.Data.Dinos.Add(new DinoSave { Type = DinoType.Stegosaurus, Stage = GrowthStage.Kid, FruitEaten = 3 });
                // v4 Dino Town: 1 finished building + 1 mid-build site at state 1.
                sm.Data.TownNextIndex = 1;
                sm.Data.TownBuildings.Clear();
                sm.Data.TownBuildings.Add(new TownBuildingSave { Finished = true, State = 4 });
                sm.Data.TownBuildings.Add(new TownBuildingSave { Finished = false, State = 1, Worked = 2.5f });
                // v5 fossil finale: a part-filled skeleton, a revived one, a half-dug machine.
                sm.Data.Bones.Clear();
                sm.Data.Bones.Add(new BoneSave
                {
                    Species = DinoType.Velociraptor, BoneIndex = (int)BoneType.Rib, Count = 2,
                });
                sm.Data.RevivedSpecies.Clear();
                sm.Data.RevivedSpecies.Add(DinoType.Pteranodon);
                sm.Data.DinoMaticFound = true;
                sm.Data.DinoMaticState = 2;
                sm.Data.DinoMaticWorked = 3.25f;
                sm.Save();

                // Mutate in memory, then reload from disk.
                sm.Data.TreasureCount = 0;
                sm.Data.Dinos.Clear();
                sm.Data.TownNextIndex = 0;
                sm.Data.TownBuildings.Clear();
                sm.Data.Bones.Clear();
                sm.Data.RevivedSpecies.Clear();
                sm.Data.DinoMaticFound = false;
                sm.Data.DinoMaticState = 0;
                sm.Data.DinoMaticWorked = 0f;
                sm.Load();

                ctx.Assert(sm.Data.TreasureCount == 4242, $"treasure not restored ({sm.Data.TreasureCount})");
                ctx.Assert(sm.Data.Version == SaveData.CurrentVersion, $"save version {sm.Data.Version} != {SaveData.CurrentVersion}");
                ctx.Assert(SaveData.CurrentVersion == 5, $"CurrentVersion is {SaveData.CurrentVersion}, expected the v5 bump");
                ctx.Assert(sm.Data.Dinos.Count == 1, $"dino count {sm.Data.Dinos.Count} != 1");
                DinoSave d = sm.Data.Dinos[0];
                ctx.Assert(d.Type == DinoType.Stegosaurus && d.Stage == GrowthStage.Kid && d.FruitEaten == 3,
                    $"dino fields not restored ({d.Type}/{d.Stage}/{d.FruitEaten})");

                // v4 town fields survive the roundtrip verbatim.
                ctx.Assert(sm.Data.TownNextIndex == 1, $"town next index not restored ({sm.Data.TownNextIndex})");
                ctx.Assert(sm.Data.TownBuildings.Count == 2, $"town building count {sm.Data.TownBuildings.Count} != 2");
                ctx.Assert(sm.Data.TownBuildings[0].Finished && sm.Data.TownBuildings[0].State == 4,
                    "finished town building not restored");
                ctx.Assert(!sm.Data.TownBuildings[1].Finished && sm.Data.TownBuildings[1].State == 1 &&
                           Mathf.Approximately(sm.Data.TownBuildings[1].Worked, 2.5f),
                    "in-progress town building fields not restored");

                // v5 fossil fields survive it too — and a v5 save must NOT be re-migrated
                // (a second conversion would double-count leftover shards).
                ctx.Assert(sm.Data.Bones.Count == 1, $"bone row count {sm.Data.Bones.Count} != 1");
                ctx.Assert(sm.Data.Bones[0].Species == DinoType.Velociraptor &&
                           sm.Data.Bones[0].BoneIndex == (int)BoneType.Rib &&
                           sm.Data.Bones[0].Count == 2,
                    "banked bone row not restored");
                ctx.Assert(sm.Data.RevivedSpecies.Count == 1 &&
                           sm.Data.RevivedSpecies[0] == DinoType.Pteranodon,
                    "revived species not restored");
                ctx.Assert(sm.Data.DinoMaticFound && sm.Data.DinoMaticState == 2 &&
                           Mathf.Approximately(sm.Data.DinoMaticWorked, 3.25f),
                    "Dino-Matic excavation state not restored");

                // v2 -> v5 migration: a save written before ShardCount/NestSpeciesQueue, before
                // the town fields and before the fossil fields must load cleanly with all of
                // them at their defaults, and be stamped at the current version.
                File.WriteAllText(path,
                    "{\"Version\":2,\"TreasureCount\":11,\"Dinos\":[],\"ParadeDone\":true}");
                sm.Load();
                ctx.Assert(sm.Data.TreasureCount == 11 && sm.Data.ParadeDone,
                    "v2 fields lost on migration");
                ctx.Assert(sm.Data.Version == SaveData.CurrentVersion,
                    $"migrated v2 save left at version {sm.Data.Version}");
                ctx.Assert(sm.Data.ShardCount == 0, $"migrated v2 save should default ShardCount=0 (got {sm.Data.ShardCount})");
                ctx.Assert(sm.Data.NestSpeciesQueue != null, "migrated v2 save left NestSpeciesQueue null");
                ctx.Assert(sm.Data.TownNextIndex == 0, $"migrated save should default TownNextIndex=0 (got {sm.Data.TownNextIndex})");
                ctx.Assert(sm.Data.TownBuildings != null && sm.Data.TownBuildings.Count == 0,
                    "migrated save should default TownBuildings to empty (an empty town)");
                ctx.Assert(sm.Data.Bones != null && sm.Data.Bones.Count == 0,
                    "migrated save should default the bone bank to empty");
                ctx.Assert(sm.Data.RevivedSpecies != null && sm.Data.RevivedSpecies.Count == 0,
                    "migrated save should default the revived set to empty");
                ctx.Assert(!sm.Data.DinoMaticFound,
                    "migrated save should not have found the Dino-Matic (no bone has been banked)");

                // ---- v4 -> v5, THE REAL ONE: shards become bones, nothing owed is lost. ----
                //
                // A returning player who had hatched Pteranodon from the nest and was partway
                // to their SECOND shard egg. The formula (SaveManager.MigrateToV5):
                //   revivedCount = 1 (Pteranodon is in Dinos)   -> req = LegacyShardsPerHatch[1] = 8
                //   target       = Velociraptor (first unrevived in SkeletonPlan.FocusOrder)
                //   slots        = 3 (a small skeleton)
                //   bones        = floor(6 * 3 / 8) = 2
                // ...filling the target's first two slots in board order (skull, then rib).
                File.WriteAllText(path,
                    "{\"Version\":4,\"TreasureCount\":50,\"ShardCount\":6," +
                    "\"Dinos\":[{\"Type\":4,\"Stage\":0,\"FruitEaten\":0,\"IsBuddy\":false}]," +
                    "\"NestSpeciesQueue\":[4]}");
                sm.Load();

                ctx.Assert(sm.Data.Version == SaveData.CurrentVersion,
                    $"v4 save not migrated to v{SaveData.CurrentVersion} (got {sm.Data.Version})");
                ctx.Assert(sm.Data.TreasureCount == 50, "v4 treasure lost in the v5 migration");
                ctx.Assert(sm.Data.Dinos.Count == 1 && sm.Data.Dinos[0].Type == DinoType.Pteranodon,
                    "HATCHED STAYS HATCHED: the v4 Pteranodon vanished in the migration");
                ctx.Assert(sm.Data.RevivedSpecies.Contains(DinoType.Pteranodon),
                    "an already-hatched fossil species must migrate as REVIVED (its skeleton is done)");
                ctx.Assert(sm.Data.ShardCount == 0,
                    $"shards not consumed by the migration ({sm.Data.ShardCount} left — it would convert twice)");
                ctx.Assert(sm.Data.NestSpeciesQueue.Count == 0, "the nest queue must drain to empty");

                int migratedBones = 0;
                for (int i = 0; i < sm.Data.Bones.Count; i++)
                {
                    ctx.Assert(sm.Data.Bones[i].Species == DinoType.Velociraptor,
                        $"converted shards landed on {sm.Data.Bones[i].Species}, not the next unrevived skeleton");
                    migratedBones += sm.Data.Bones[i].Count;
                }

                int expectBones = 6 * SkeletonPlan.SlotCount(DinoType.Velociraptor) /
                                  SaveData.LegacyShardsPerHatch[1];
                ctx.Assert(migratedBones == expectBones,
                    $"6 shards converted to {migratedBones} bones (floor formula expects {expectBones})");

                // Idempotent: reloading the now-v5 file must not convert anything a second time.
                sm.Save();
                sm.Load();
                int reloadedBones = 0;
                for (int i = 0; i < sm.Data.Bones.Count; i++)
                {
                    reloadedBones += sm.Data.Bones[i].Count;
                }

                ctx.Assert(reloadedBones == migratedBones && sm.Data.ShardCount == 0,
                    $"a second load re-ran the migration ({migratedBones} -> {reloadedBones} bones)");

                ctx.Log($"v5 roundtrip (treasure=4242, 1 bone row, 1 revived, machine s2+3.25s); " +
                        $"v2 migrates to defaults; v4 (6 shards, hatched Pteranodon) -> " +
                        $"{migratedBones} Velociraptor bones, shards zeroed, nest queue drained, " +
                        "and re-loading converts nothing twice");
            }
            finally
            {
                // Restore the player's real save file.
                try
                {
                    if (existed && backup != null)
                    {
                        File.WriteAllBytes(path, backup);
                    }
                    else if (!existed && File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch (Exception ex)
                {
                    Debug.Log($"[IntegrationTest] save restore warning: {ex.Message}");
                }
            }
        }

        // ================================================================== INPUT / UI

        private IEnumerator Case_ParentGateMute(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestConfig.ParentGateHoldSeconds = 0.4f; // restored by the runner after the case

            var btn = gm.TestMuteButton;
            ctx.Assert(btn != null, "no mute button");
            ctx.Assert(gm.Audio != null, "no audio manager");

            bool before = gm.Audio.Muted;
            var ped = new PointerEventData(EventSystem.current);

            // Short tap (< hold): must NOT toggle.
            btn.OnPointerDown(ped);
            yield return ctx.WaitSecondsRealtime(0.15f);
            btn.OnPointerUp(ped);
            yield return ctx.WaitFrames(3);
            ctx.Assert(gm.Audio.Muted == before, "a short tap toggled mute (parent gate failed)");

            // Long hold (>= hold): must toggle.
            btn.OnPointerDown(ped);
            yield return ctx.WaitSecondsRealtime(0.7f);
            btn.OnPointerUp(ped);
            ctx.Assert(gm.Audio.Muted != before, "a full hold did not toggle mute");

            gm.Audio.SetMuted(before); // restore original mute state
            ctx.Log($"short tap = no-op; full hold toggled mute {before}->{!before}");
        }

        // ========================================================= DINO COMPANIONS

        // A settled dino must be genuinely STILL: no forward/back position jitter
        // around its follow slot (the idle-jitter regression). Complements
        // FacingStability, which covers sprite/facing flapping while moving.
        private IEnumerator Case_DinoIdleStable(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();

            DinoController dino = gm.TestSpawnDino(DinoType.TRex, GrowthStage.Kid);
            ctx.Assert(dino != null, "dino spawn failed");

            // Backhoe stays parked; wait for the dino to reach its slot and settle,
            // then confirm the settle held through a short grace window.
            yield return ctx.WaitUntil(() => dino.TestIsSettled);
            yield return ctx.WaitSecondsScaled(0.5f);
            yield return ctx.WaitUntil(() => dino.TestIsSettled);

            Vector3 anchor = dino.transform.position;
            Vector3 lastPos = anchor;
            Vector3 lastStep = Vector3.zero;
            float maxDisp = 0f;
            int reversals = 0;
            float elapsed = 0f;

            while (elapsed < 3f)
            {
                Vector3 p = dino.transform.position;
                maxDisp = Mathf.Max(maxDisp, (p - anchor).magnitude);

                Vector3 step = p - lastPos;
                if (step.magnitude > 0.004f)
                {
                    if (lastStep != Vector3.zero && Vector3.Dot(step, lastStep) < 0f)
                    {
                        reversals++;
                    }

                    lastStep = step;
                }

                lastPos = p;
                elapsed += Time.deltaTime;
                yield return null;
            }

            ctx.Assert(maxDisp < 0.15f,
                $"settled dino drifted {maxDisp:F3} units over 3s (idle jitter regression)");
            ctx.Assert(reversals <= 2,
                $"settled dino reversed direction {reversals}x over 3s (oscillation)");
            ctx.Log($"idle held for 3s: maxDisp={maxDisp:F3}, reversals={reversals}");
            gm.TestReset();
        }

        // Walk-cycle pilot (y85.1/y85.3): a trex buddy must alternate through >= 2
        // distinct mid-stride sprites while it follows the moving backhoe, and return
        // to the plain idle facing frame once settled. Deliberately PINNED to the
        // TREX ADULT set — the pilot dino that ships with generated walkA/walkB art —
        // so a broken import fails loudly instead of skip-passing; all other
        // dinos/stages have no stride art and keep the static behavior (covered by
        // the stride-art assert being trex-only).
        private IEnumerator Case_WalkAnimCycles(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            BackhoeController bh = gm.TestBackhoe;
            OverworldMap map = gm.TestMap;
            ctx.Assert(bh != null && map != null, "missing backhoe/map");

            // Seat the backhoe on open ground that offers a corridor-straight leg BEFORE
            // spawning the follower. The walk-cycle cadence is movement-driven (TickWalkAnim
            // advances with the step), so the buddy only shows both stride phases (idle->A->
            // idle->B) over a SUSTAINED straight lead. TestReset never repositions the
            // backhoe, so without this the drive can start from a cluttered carried-over
            // cell where only jerky one-cell hops are possible and the cycle shows <2 frames.
            RelocateForStraightLeg(gm, map, bh, out _);

            DinoController dino = gm.TestSpawnDino(DinoType.TRex, GrowthStage.Big);
            ctx.Assert(dino != null, "dino spawn failed");
            yield return ctx.WaitFrames(2);

            ctx.Assert(dino.TestStrideDirSprite(GrowthStage.Big, 0, Dir8.S) != null,
                "trex adult stride art missing (run the Tools pipeline, then DinoDigger/Import Generated Art)");

            // Every stride sprite (both phases x 8 dirs) for identifying walk frames.
            var strideSprites = new HashSet<Sprite>();
            for (int phase = 0; phase < 2; phase++)
            {
                for (int i = 0; i < 8; i++)
                {
                    Sprite s = dino.TestStrideDirSprite(GrowthStage.Big, phase, (Dir8)i);
                    if (s != null)
                    {
                        strideSprites.Add(s);
                    }
                }
            }

            // Keep the backhoe driving ~3s so the buddy actively follows; sample the
            // rendered sprite every frame it is in motion.
            var seenStrides = new HashSet<Sprite>();
            bool idleBeatSeen = false;
            float elapsed = 0f;
            while (elapsed < 3f)
            {
                if (!bh.IsMoving &&
                    FindStraightCorridorTarget(map, gm, bh.transform.position, out Vector3 next))
                {
                    bh.MoveTo(next);
                }

                if (!dino.TestIsSettled)
                {
                    Sprite cur = dino.TestSprite;
                    if (cur != null && strideSprites.Contains(cur))
                    {
                        seenStrides.Add(cur);
                    }
                    else if (cur != null)
                    {
                        idleBeatSeen = true; // the idle beats of idle->A->idle->B
                    }
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            ctx.Assert(seenStrides.Count >= 2,
                $"only {seenStrides.Count} distinct stride sprites rendered while walking (expected >= 2)");
            ctx.Assert(idleBeatSeen,
                "idle frame never appeared mid-walk (cycle should be idle->A->idle->B)");

            // Park: the dino reaches its slot, settles, and must be back on the plain
            // idle facing frame (not frozen mid-stride).
            yield return ctx.WaitUntil(() => !bh.IsMoving);
            yield return ctx.WaitUntil(() => dino.TestIsSettled);
            yield return ctx.WaitFrames(2);

            Sprite idleExpected = dino.TestStageDirSprite(GrowthStage.Big, dino.TestFacing);
            ctx.Assert(dino.TestSprite == idleExpected,
                $"settled dino not on the idle facing frame (facing {dino.TestFacing})");

            ctx.Log($"walk cycled through {seenStrides.Count} distinct stride frames " +
                    "with idle beats between, then settled back on the idle frame");

            // NEW-species coverage (y85.2): prove the remaining-7 batch art is wired
            // end-to-end by walking a velociraptor buddy through its own stride cycle.
            gm.TestReset();
            RelocateForStraightLeg(gm, map, bh, out _); // sustained lead so the cycle completes
            DinoController raptor = gm.TestSpawnDino(DinoType.Velociraptor, GrowthStage.Big);
            ctx.Assert(raptor != null, "velociraptor spawn failed");
            yield return ctx.WaitFrames(2);

            ctx.Assert(raptor.TestStrideDirSprite(GrowthStage.Big, 0, Dir8.S) != null,
                "velociraptor adult stride art missing (y85.2 batch not generated/imported)");

            var raptorStrides = new HashSet<Sprite>();
            for (int phase = 0; phase < 2; phase++)
            {
                for (int i = 0; i < 8; i++)
                {
                    Sprite s = raptor.TestStrideDirSprite(GrowthStage.Big, phase, (Dir8)i);
                    if (s != null)
                    {
                        raptorStrides.Add(s);
                    }
                }
            }

            var raptorSeen = new HashSet<Sprite>();
            float rElapsed = 0f;
            while (rElapsed < 3f)
            {
                if (!bh.IsMoving &&
                    FindStraightCorridorTarget(map, gm, bh.transform.position, out Vector3 rnext))
                {
                    bh.MoveTo(rnext);
                }

                if (!raptor.TestIsSettled)
                {
                    Sprite cur = raptor.TestSprite;
                    if (cur != null && raptorStrides.Contains(cur))
                    {
                        raptorSeen.Add(cur);
                    }
                }

                rElapsed += Time.deltaTime;
                yield return null;
            }

            ctx.Assert(raptorSeen.Count >= 2,
                $"velociraptor rendered only {raptorSeen.Count} distinct stride frames " +
                "while walking (expected >= 2; y85.2 batch art)");
            ctx.Log($"velociraptor buddy walk-cycled through {raptorSeen.Count} distinct " +
                    "stride frames (y85.2 batch art wired)");
            gm.TestReset();
        }

        // Drive the backhoe and assert its wheel-roll drive cycle alternates through
        // the roll frames (idle->A->idle->B) and settles back to the idle facing
        // frame when it stops (DinoDigger-682). Mirrors WalkAnimCycles' structure.
        private IEnumerator Case_BackhoeRollCycles(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            BackhoeController bh = gm.TestBackhoe;
            OverworldMap map = gm.TestMap;
            ctx.Assert(bh != null && map != null, "missing backhoe/map");

            ctx.Assert(bh.TestRollDirSprite(0, Dir8.S) != null,
                "backhoe roll art missing (run the Tools pipeline, then DinoDigger/Import Generated Art)");

            // Seat the backhoe on open ground with a corridor-straight leg first. The
            // wheel-roll cadence is movement-driven (TickRoll advances with the step) and
            // ResetRoll snaps back to the idle frame on every Arrive, so only a SUSTAINED
            // straight drive cycles through both roll phases. TestReset never repositions
            // the backhoe, so a cluttered carried-over start cell yields only one-cell hops
            // that re-idle before a roll frame is ever reached (the "0 distinct" failure).
            RelocateForStraightLeg(gm, map, bh, out _);

            // Every roll sprite (both phases x 8 dirs) for identifying drive frames.
            var rollSprites = new HashSet<Sprite>();
            for (int phase = 0; phase < 2; phase++)
            {
                for (int i = 0; i < 8; i++)
                {
                    Sprite s = bh.TestRollDirSprite(phase, (Dir8)i);
                    if (s != null)
                    {
                        rollSprites.Add(s);
                    }
                }
            }

            // Keep the backhoe driving ~3s (re-targeting clear legs) and sample the
            // rendered sprite every frame it is in motion.
            var seenRolls = new HashSet<Sprite>();
            bool idleBeatSeen = false;
            float elapsed = 0f;
            while (elapsed < 3f)
            {
                if (!bh.IsMoving &&
                    FindStraightCorridorTarget(map, gm, bh.transform.position, out Vector3 next))
                {
                    bh.MoveTo(next);
                }

                if (bh.IsMoving)
                {
                    Sprite cur = bh.TestSprite;
                    if (cur != null && rollSprites.Contains(cur))
                    {
                        seenRolls.Add(cur);
                    }
                    else if (cur != null)
                    {
                        idleBeatSeen = true; // the idle beats of idle->A->idle->B
                    }
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            ctx.Assert(seenRolls.Count >= 2,
                $"only {seenRolls.Count} distinct roll frames rendered while driving (expected >= 2)");
            ctx.Assert(idleBeatSeen,
                "idle frame never appeared mid-drive (cycle should be idle->A->idle->B)");

            // Park: the backhoe stops and must be back on the plain idle facing frame
            // (not frozen mid-roll).
            yield return ctx.WaitUntil(() => !bh.IsMoving);
            yield return ctx.WaitFrames(2);

            ctx.Assert(bh.TestRollFrame == 0, "roll cycle did not settle to the idle frame");
            Sprite idleExpected = bh.TestDirSprite(bh.Facing);
            ctx.Assert(idleExpected == null || bh.TestSprite == idleExpected,
                $"settled backhoe not on the idle facing frame (facing {bh.Facing})");

            ctx.Log($"backhoe drive cycled through {seenRolls.Count} distinct roll frames " +
                    "with idle beats between, then settled back on the idle frame");
            gm.TestReset();
        }

        // Hatch 4 dinos: exactly 2 become walk buddies, the other 2 head home to
        // the meadow (buddy cap).
        private IEnumerator Case_BuddyCapTwo(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            MeadowArea meadow = gm.TestMeadow;
            ctx.Assert(meadow != null, "no MeadowArea in the scene (rebuild via DinoDigger/Build Main Scene)");

            OverworldMap map = gm.TestMap;
            Vector3 bp = gm.TestBackhoe.transform.position;
            DinoType[] types = { DinoType.TRex, DinoType.Triceratops, DinoType.Brachiosaurus, DinoType.Stegosaurus };
            for (int i = 0; i < types.Length; i++)
            {
                Vector3 pos = WalkableNear(map, bp + new Vector3(0.8f + 0.4f * i, 0.4f - 0.3f * i, 0f));
                gm.HatchEgg(types[i], pos);
                yield return ctx.WaitFrames(1);
            }

            yield return ctx.WaitFrames(2);
            ctx.Assert(gm.TestDinos.Count == 4, $"expected 4 dinos, got {gm.TestDinos.Count}");
            ctx.Assert(gm.TestBuddies.Count == 2, $"buddy cap broken: {gm.TestBuddies.Count} buddies");

            int buddyFlags = 0;
            var residents = new List<DinoController>();
            for (int i = 0; i < gm.TestDinos.Count; i++)
            {
                DinoController d = gm.TestDinos[i];
                if (d == null)
                {
                    continue;
                }

                if (d.IsBuddy)
                {
                    buddyFlags++;
                }
                else
                {
                    residents.Add(d);
                }
            }

            ctx.Assert(buddyFlags == 2 && residents.Count == 2,
                $"expected 2 buddies + 2 residents, got {buddyFlags}/{residents.Count}");

            // Save must carry the assignment (v2 IsBuddy field).
            int savedBuddies = 0;
            foreach (DinoSave ds in gm.Save.Data.Dinos)
            {
                if (ds.IsBuddy)
                {
                    savedBuddies++;
                }
            }

            ctx.Assert(savedBuddies == 2, $"save has {savedBuddies} IsBuddy entries (expected 2)");

            // The two residents trot home: both end up inside the meadow.
            yield return ctx.WaitUntil(() =>
                residents[0] != null && residents[1] != null &&
                meadow.ContainsInterior(residents[0].transform.position) &&
                meadow.ContainsInterior(residents[1].transform.position));

            ctx.Log("4 hatches -> 2 buddies followed, 2 residents walked into the meadow; save has 2 IsBuddy");
            gm.TestReset();
        }

        // Tapping a resident promotes it to buddy; the LONGEST-SERVING buddy
        // departs for the meadow.
        private IEnumerator Case_BuddySwapOnTap(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            ctx.Assert(gm.TestMeadow != null, "no MeadowArea in the scene (rebuild via DinoDigger/Build Main Scene)");

            DinoController a = gm.TestSpawnDino(DinoType.TRex, GrowthStage.Kid); // oldest buddy
            yield return ctx.WaitFrames(1);
            DinoController b = gm.TestSpawnDino(DinoType.Triceratops, GrowthStage.Kid);
            yield return ctx.WaitFrames(1);
            DinoController c = gm.TestSpawnDino(DinoType.Stegosaurus, GrowthStage.Kid); // cap full -> resident
            yield return ctx.WaitFrames(2);

            ctx.Assert(a.IsBuddy && b.IsBuddy, "first two spawns are not buddies");
            ctx.Assert(!c.IsBuddy, "third spawn should be a resident (cap 2)");

            // Let the resident separate a little so the tap can't hit a buddy's collider.
            yield return ctx.WaitUntil(() =>
                (c.transform.position - a.transform.position).sqrMagnitude > 2.25f &&
                (c.transform.position - b.transform.position).sqrMagnitude > 2.25f);

            ctx.TapWorld(c.transform.position);
            yield return ctx.WaitUntil(() => c.IsBuddy);

            ctx.Assert(!a.IsBuddy, "longest-serving buddy was not demoted on swap");
            ctx.Assert(b.IsBuddy, "wrong buddy was demoted (should keep the newer one)");
            ctx.Assert(gm.TestBuddies.Count == 2, $"buddy count {gm.TestBuddies.Count} != 2 after swap");
            ctx.Log("tapped resident joined the walk; oldest buddy trotted off to the meadow");
            gm.TestReset();
        }

        // Residents stay inside the meadow bounds while strolling/napping.
        private IEnumerator Case_MeadowContainsResidents(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            MeadowArea meadow = gm.TestMeadow;
            ctx.Assert(meadow != null, "no MeadowArea in the scene");

            DinoController d1 = gm.TestSpawnDino(DinoType.TRex, GrowthStage.Baby);
            DinoController d2 = gm.TestSpawnDino(DinoType.Stegosaurus, GrowthStage.Kid);
            gm.TestMakeResident(d1, teleportIntoMeadow: true);
            gm.TestMakeResident(d2, teleportIntoMeadow: true);
            yield return ctx.WaitFrames(2);

            float elapsed = 0f;
            while (elapsed < 4f)
            {
                ctx.Assert(meadow.ContainsOuter(d1.transform.position),
                    $"resident 1 escaped the meadow at {d1.transform.position}");
                ctx.Assert(meadow.ContainsOuter(d2.transform.position),
                    $"resident 2 escaped the meadow at {d2.transform.position}");
                elapsed += Time.deltaTime;
                yield return null;
            }

            ctx.Log("2 residents stayed inside the meadow bounds for 4s of strolling/napping");
            gm.TestReset();
        }

        // No mound may sit inside the meadow — at build time or after a respawn.
        private IEnumerator Case_MoundsAvoidMeadow(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            MeadowArea meadow = gm.TestMeadow;
            ctx.Assert(meadow != null, "no MeadowArea in the scene");

            IReadOnlyList<DigMound> mounds = gm.TestMounds;
            ctx.Assert(mounds != null && mounds.Count > 0, "no mounds in the scene");
            for (int i = 0; i < mounds.Count; i++)
            {
                if (mounds[i] != null)
                {
                    ctx.Assert(!meadow.ContainsOuter(mounds[i].transform.position),
                        $"build-time mound {i} sits inside the meadow");
                }
            }

            // Forced respawn must respect the exclusion too.
            gm.TestConfig.MoundRespawnSeconds = 1f; // restored by the runner
            DigMound m = FarthestActiveMound(gm);
            ctx.Assert(m != null, "no active mound to respawn");
            gm.Spawn.ScheduleRespawn(m);
            yield return ctx.WaitUntil(() => m.IsActive);

            ctx.Assert(!meadow.ContainsOuter(m.transform.position),
                "respawned mound landed inside the meadow");
            ctx.Log($"{mounds.Count} build mounds + 1 forced respawn all outside the meadow");
        }

        // The Berry Patch: SceneBuilder bakes a GardenArea holding three BerrySprouts, all
        // inside the garden rect, on walkable ground, and mound-excluded. A BUDDING tap
        // wiggles without fruit; a RIPE tap pops exactly one fruit through the standard
        // pickup path and resets the sprout to budding; and that fruit rides the normal
        // feed chain (a hungry dino eats it). Force-ripen is used so the case never waits
        // out the 25s ripen timer.
        private IEnumerator Case_BerryPatch(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset(); // re-buds all sprouts (staggered) + clears dinos/pickups

            GardenArea garden = gm.TestGarden;
            ctx.Assert(garden != null,
                "scene ships no wired GardenArea (GameManager._garden is null) — " +
                "rebuild via DinoDigger/Build Main Scene");

            IReadOnlyList<BerrySprout> sprouts = gm.TestSprouts;
            ctx.Assert(sprouts != null && sprouts.Count == 3,
                $"expected 3 sprouts (have {(sprouts != null ? sprouts.Count : 0)})");
            ctx.Assert(garden.SproutCount == 3,
                $"GardenArea has {garden.SproutCount} sprouts (expected 3)");

            OverworldMap map = gm.TestMap;

            // (1) Every sprout sits inside the garden rect on walkable ground.
            for (int i = 0; i < sprouts.Count; i++)
            {
                BerrySprout s = sprouts[i];
                ctx.Assert(s != null, $"sprout {i} is null");
                ctx.Assert(garden.ContainsWorld(s.transform.position),
                    $"sprout {i} is outside the garden rect");
                ctx.Assert(map.IsWalkableWorld(s.transform.position),
                    $"sprout {i} is on a non-walkable cell");
            }

            // ...and no dig mound sits inside the garden.
            IReadOnlyList<DigMound> mounds = gm.TestMounds;
            for (int i = 0; i < mounds.Count; i++)
            {
                if (mounds[i] != null)
                {
                    ctx.Assert(!garden.ContainsWorldExpanded(mounds[i].transform.position, 0),
                        $"build-time mound {i} sits inside the garden");
                }
            }

            // Pick a sprout whose center is the ONLY ITappable, so routed taps resolve to
            // it deterministically (avoids the overlapping-collider trap).
            Physics2D.SyncTransforms();
            BerrySprout sprout = null;
            for (int i = 0; i < sprouts.Count; i++)
            {
                if (sprouts[i] != null && OnlySproutTappable(sprouts[i].transform.position, sprouts[i]))
                {
                    sprout = sprouts[i];
                    break;
                }
            }

            ctx.Assert(sprout != null, "no sprout has a clean (sole-ITappable) tap point");

            // (2) A BUDDING tap wiggles but spawns NO fruit.
            ctx.Assert(!sprout.IsRipe, "sprout should start budding after reset");
            int fruitBefore = CountFruitPickups(gm);
            gm.TestTapWorldRouted(sprout.transform.position);
            yield return ctx.WaitFrames(3);
            ctx.Assert(!sprout.IsRipe, "budding tap ripened the sprout (should only wiggle)");
            ctx.Assert(CountFruitPickups(gm) == fruitBefore,
                "budding tap spawned fruit (should only wiggle + rustle)");

            // (3) Force-ripen, then a RIPE tap pops exactly one fruit and resets to budding.
            sprout.TestForceRipen();
            ctx.Assert(sprout.IsRipe, "TestForceRipen did not ripen the sprout");
            int before = CountFruitPickups(gm);
            gm.TestTapWorldRouted(sprout.transform.position);
            yield return ctx.WaitUntil(() => CountFruitPickups(gm) > before);
            int after = CountFruitPickups(gm);
            ctx.Assert(after == before + 1,
                $"ripe tap spawned {after - before} fruit (expected exactly 1)");
            ctx.Assert(!sprout.IsRipe, "sprout did not reset to budding after harvest");

            // (4) The harvested fruit rides the normal feed chain: a hungry dino eats it.
            ItemPickup harvested = FirstFruitPickup(gm);
            ctx.Assert(harvested != null, "no harvested fruit pickup found");
            yield return ctx.WaitUntil(() => harvested == null || harvested.IsCarryableFruit);
            ctx.Assert(harvested != null, "harvested fruit vanished before it could be eaten");

            DinoController dino = gm.TestSpawnDino(DinoType.TRex, GrowthStage.Baby);
            ctx.Assert(dino != null && dino.IsHungry, "test dino is not hungry");
            int ate = dino.FruitEaten;
            gm.RequestFeed(harvested);
            yield return ctx.WaitUntil(() =>
                dino.FruitEaten > ate || harvested == null || harvested.IsConsumed);
            ctx.Assert(dino.FruitEaten > ate,
                "hungry dino did not eat the harvested berry (feed chain broken)");

            ctx.Log("berry patch: 3 sprouts in-rect + mound-excluded; budding tap wiggled (no fruit); " +
                    "ripe tap popped exactly 1 fruit + reset to bud; a hungry dino ate the harvested berry");
            gm.TestReset();
        }

        /// <summary>Count the live (unconsumed) fruit pickups under the overworld root.</summary>
        private int CountFruitPickups(GameManager gm)
        {
            Transform root = gm.TestOverworldRoot;
            if (root == null)
            {
                return 0;
            }

            ItemPickup[] ps = root.GetComponentsInChildren<ItemPickup>(true);
            int n = 0;
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i] != null && ps[i].Type == ItemType.Fruit && !ps[i].IsConsumed)
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>The first live (unconsumed) fruit pickup under the overworld root.</summary>
        private ItemPickup FirstFruitPickup(GameManager gm)
        {
            Transform root = gm.TestOverworldRoot;
            if (root == null)
            {
                return null;
            }

            ItemPickup[] ps = root.GetComponentsInChildren<ItemPickup>(true);
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i] != null && ps[i].Type == ItemType.Fruit && !ps[i].IsConsumed)
                {
                    return ps[i];
                }
            }

            return null;
        }

        /// <summary>True when the ONLY ITappable overlapping <paramref name="p"/> is
        /// <paramref name="s"/> — so GameManager.FindTappable (first ITappable hit) is
        /// guaranteed to resolve a tap there to this sprout (mirrors OnlyBuildingTappable).</summary>
        private bool OnlySproutTappable(Vector3 p, BerrySprout s)
        {
            Collider2D[] hits = Physics2D.OverlapPointAll(p);
            bool found = false;
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

                bool isSprout = hits[i].GetComponent<BerrySprout>() == s ||
                                hits[i].GetComponentInParent<BerrySprout>() == s;
                if (isSprout)
                {
                    found = true;
                }
                else
                {
                    return false; // another tappable overlaps -> ambiguous, skip this point
                }
            }

            return found;
        }

        // The cleared town district must contain no mound, stream/water, or tree/rock
        // at build time, and mound respawns must never land inside it (it is walkable
        // grass, so the guard is by district rect, not walkability). Mirrors
        // MoundsAvoidMeadow's build-time + forced-respawn structure.
        private IEnumerator Case_TownAvoidsMoundAndStream(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            OverworldMap map = gm.TestMap;
            ctx.Assert(map != null, "no overworld map");
            ctx.Assert(map.TestHasTownDistrict,
                "no town district on the map (rebuild via DinoDigger/Build Main Scene)");

            RectInt d = map.TestTownDistrict;

            // 1) Every district cell is clear, walkable grass: has ground, no
            //    water (pond/stream), no obstacle (tree/rock).
            for (int x = d.xMin; x < d.xMax; x++)
            {
                for (int y = d.yMin; y < d.yMax; y++)
                {
                    var cell = new Vector3Int(x, y, 0);
                    ctx.Assert(map.TestHasGround(cell),
                        $"district cell {cell} has no ground tile (should be grass)");
                    ctx.Assert(!map.TestHasWater(cell),
                        $"district cell {cell} carries water (a stream/pond cut into the district)");
                    ctx.Assert(map.ObstacleAt(cell) == null,
                        $"district cell {cell} has a tree/rock obstacle");
                }
            }

            // 2) No build-time mound sits inside the district.
            IReadOnlyList<DigMound> mounds = gm.TestMounds;
            ctx.Assert(mounds != null && mounds.Count > 0, "no mounds in the scene");
            for (int i = 0; i < mounds.Count; i++)
            {
                if (mounds[i] != null)
                {
                    ctx.Assert(!map.InTownDistrict(mounds[i].transform.position),
                        $"build-time mound {i} sits inside the town district");
                }
            }

            // 3) Forced respawns must respect the exclusion too. Cycle several so the
            //    random cell draw is genuinely exercised against the small district.
            gm.TestConfig.MoundRespawnSeconds = 1f; // restored by the runner
            for (int r = 0; r < 8; r++)
            {
                DigMound m = FarthestActiveMound(gm);
                ctx.Assert(m != null, "no active mound to respawn");
                gm.Spawn.ScheduleRespawn(m);
                yield return ctx.WaitUntil(() => m.IsActive);
                ctx.Assert(!map.InTownDistrict(m.transform.position),
                    $"respawn {r} landed inside the town district at {m.transform.position}");
            }

            ctx.Log($"town district {d.width}x{d.height} clear of mounds/streams/trees at " +
                    "build + 8 forced respawns all outside");
        }

        // Buddy Brachiosaurus near a tapped tree: walks over, neck-sways, and
        // fruit pops out of the canopy.
        private IEnumerator Case_BrachioTreeShake(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();

            DinoController brachio = gm.TestSpawnDino(DinoType.Brachiosaurus, GrowthStage.Kid);
            ctx.Assert(brachio != null && brachio.IsBuddy, "buddy Brachio spawn failed");
            yield return ctx.WaitFrames(2);

            ctx.Assert(FindTreeCell(gm, out Vector3Int treeCell, out Vector3 treeWorld),
                "no tree tile found on the island");

            // Stand the Brachio NEAR the tree but far enough that the player's tap on the
            // tree center is NOT swallowed by the dino's own tap collider. THE TIMEOUT BUG:
            // this Grid is IsometricZAsY with cellSize (1, 0.5), so a cardinal-NEIGHBOUR
            // cell center is only ~0.56 world units from the tree center — INSIDE the
            // dino's 0.6-unit (× Kid stage scale ~1.15 ≈ 0.69) touch collider. Placing the
            // Brachio right beside the tree made FindTappable(treeWorld) return the DINO,
            // so the routed tree tap hit dino.OnTapped (it just danced) and OnTreeTapped
            // NEVER ran — no fruit, deterministic timeout every run. Pick a walkable cell
            // ~1.0–2.7 world units out: clear of the ~0.7 collider, still inside the
            // 3-unit shake range. Scan by ACTUAL world distance (isometric steps vary per
            // grid direction), nearest ring first.
            Vector3 beside = treeWorld;
            bool placed = false;
            for (int ring = 1; ring <= 6 && !placed; ring++)
            {
                for (int ox = -ring; ox <= ring && !placed; ox++)
                {
                    for (int oy = -ring; oy <= ring && !placed; oy++)
                    {
                        if (Mathf.Max(Mathf.Abs(ox), Mathf.Abs(oy)) != ring)
                        {
                            continue; // ring perimeter only (nearest cells first)
                        }

                        var c = new Vector3Int(treeCell.x + ox, treeCell.y + oy, 0);
                        if (!gm.TestMap.IsWalkableCell(c))
                        {
                            continue;
                        }

                        Vector3 w = gm.TestMap.CellCenter(c);
                        float dm = (w - treeWorld).magnitude;
                        if (dm >= 1.0f && dm <= 2.7f)
                        {
                            beside = w;
                            placed = true;
                        }
                    }
                }
            }

            ctx.Assert(placed,
                "no walkable cell 1.0-2.7 units from the tree (clear of the dino tap collider, inside shake range)");

            // Park the backhoe on the NEAREST walkable cell to the tree BEFORE
            // dropping the buddy in. A single buddy's follow slot sits ~1.4 units
            // off the backhoe (SlotOffset(0) == (-1.4, 0)), so anchoring the backhoe
            // at the tree keeps that slot inside the 3-unit shake range. With the
            // backhoe parked far away (wherever an earlier case left it), the
            // buddy-follow FSM immediately trots the Brachio back toward it and the
            // shake silently no-ops (leaf rustle only) -> timeout.
            gm.TestBackhoe.transform.position = WalkableNear(gm.TestMap, treeWorld);
            Physics2D.SyncTransforms();
            // Let the follow slot resolve next to the tree before we place the dino.
            yield return ctx.WaitSecondsScaled(0.5f);

            brachio.transform.position = beside;
            Physics2D.SyncTransforms();

            int before = CountOverworldPickups(gm, true);

            // Route the tap like OnTap does (world-routed hook: the tree may be
            // far outside the camera frame during tests).
            gm.TestTapWorldRouted(treeWorld);

            yield return ctx.WaitUntil(() => CountOverworldPickups(gm, true) > before);
            int gained = CountOverworldPickups(gm, true) - before;
            ctx.Assert(gained >= 1 && gained <= 2, $"tree dropped {gained} fruit (expected 1-2)");
            ctx.Log($"tapped tree at {treeCell}: Brachio shook out {gained} fruit");
            gm.TestReset();
        }

        // Ankylosaurus rock smash: a rock tapped with NO Anky buddy only wiggles
        // (no payout); a buddy Anky in range walks over, tail-clubs it and loot pops
        // out; a second tap while the rock is on cooldown does NOT pay out again; and
        // the treasure-vs-shard payout is gated on unhatched shard species.
        private IEnumerator Case_AnkyRockSmash(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();

            ctx.Assert(FindRockCell(gm, out Vector3Int rockCell, out Vector3 rockWorld),
                "no rock tile found on the island");

            // 1) Wiggle-only: no Anky buddy present -> the rock must NOT pay out.
            gm.TestTapWorldRouted(rockWorld);
            yield return ctx.WaitSecondsScaled(0.5f);
            ctx.Assert(gm.TestRockSmashPayouts == 0, "rock paid out with no Ankylosaurus buddy present");

            // 2) Smash: a buddy Anky in range breaks the rock open.
            DinoController anky = gm.TestSpawnDino(DinoType.Ankylosaurus, GrowthStage.Kid);
            ctx.Assert(anky != null && anky.IsBuddy, "buddy Anky spawn failed");
            yield return ctx.WaitFrames(2);

            // Park the backhoe beside the rock so the buddy's follow slot stays inside
            // the smash range (same framing as the tree-shake case), let it settle, then
            // drop the Anky ~1.0-2.7 world units off the rock: clear of its own tap
            // collider but well inside RockSmashRange.
            gm.TestBackhoe.transform.position = WalkableNear(gm.TestMap, rockWorld);
            Physics2D.SyncTransforms();
            yield return ctx.WaitSecondsScaled(0.5f);

            Vector3 beside = rockWorld;
            bool placed = false;
            for (int ring = 1; ring <= 6 && !placed; ring++)
            {
                for (int ox = -ring; ox <= ring && !placed; ox++)
                {
                    for (int oy = -ring; oy <= ring && !placed; oy++)
                    {
                        if (Mathf.Max(Mathf.Abs(ox), Mathf.Abs(oy)) != ring)
                        {
                            continue; // ring perimeter only (nearest cells first)
                        }

                        var c = new Vector3Int(rockCell.x + ox, rockCell.y + oy, 0);
                        if (!gm.TestMap.IsWalkableCell(c))
                        {
                            continue;
                        }

                        Vector3 w = gm.TestMap.CellCenter(c);
                        float dm = (w - rockWorld).magnitude;
                        if (dm >= 1.0f && dm <= 2.7f)
                        {
                            beside = w;
                            placed = true;
                        }
                    }
                }
            }

            ctx.Assert(placed,
                "no walkable cell 1.0-2.7 units from the rock (clear of the dino tap collider, inside smash range)");

            anky.transform.position = beside;
            Physics2D.SyncTransforms();

            gm.TestTapWorldRouted(rockWorld);
            yield return ctx.WaitUntil(() => gm.TestRockSmashPayouts >= 1);
            ctx.Assert(gm.TestRockSmashPayouts == 1, $"rock produced {gm.TestRockSmashPayouts} payouts (expected 1)");

            // 3) Cooldown: an immediate second tap on the SAME rock must not pay out
            // again (it still wiggles for feedback).
            gm.TestTapWorldRouted(rockWorld);
            yield return ctx.WaitSecondsScaled(0.8f);
            ctx.Assert(gm.TestRockSmashPayouts == 1, "rock paid out a second time while on cooldown");

            // 4) A rock is ALWAYS coins. It used to roll an egg shard some of the time to keep
            // the nest ticking over; the nest retired with save v5 (DinoDigger-5ve) and the
            // fossil species come out of dig sites as bones now, so every payout — with
            // nothing owned OR with the whole roster owned — must be treasure and never a
            // shard. Asserted at BOTH ends of the game because the old behaviour was gated on
            // ownership, and a leftover gate would only show up at one of them.
            gm.TestReset();

            int shardRolls = 0;
            int treasureRolls = 0;
            for (int i = 0; i < 400; i++)
            {
                ItemType t = gm.TestRollRockPayout().Type;
                if (t == ItemType.Shard)
                {
                    shardRolls++;
                }
                else if (t == ItemType.Treasure)
                {
                    treasureRolls++;
                }
            }

            ctx.Assert(shardRolls == 0, $"{shardRolls} rock rolls produced an egg shard (shards are retired)");
            ctx.Assert(treasureRolls == 400, $"only {treasureRolls}/400 rock rolls were treasure");

            DinoType[] all =
            {
                DinoType.TRex, DinoType.Triceratops, DinoType.Brachiosaurus, DinoType.Stegosaurus,
                DinoType.Pteranodon, DinoType.Ankylosaurus, DinoType.Spinosaurus,
                DinoType.Parasaurolophus, DinoType.Velociraptor
            };
            for (int i = 0; i < all.Length; i++)
            {
                gm.TestSpawnDino(all[i], GrowthStage.Baby);
            }

            yield return ctx.WaitFrames(1);

            int shardsWhenOwned = 0;
            for (int i = 0; i < 400; i++)
            {
                if (gm.TestRollRockPayout().Type == ItemType.Shard)
                {
                    shardsWhenOwned++;
                }
            }

            ctx.Assert(shardsWhenOwned == 0, $"{shardsWhenOwned} shard rolls leaked with every species owned");

            ctx.Log($"smashed rock at {rockCell}: payout fired once, cooldown held; " +
                    $"payouts always treasure ({treasureRolls}/400 fresh, 0 shards either end)");
            gm.TestReset();
        }

        // Buddy Stegosaurus + an active mound: the sniffer sparkle fires within
        // one interval (~6s game time).
        private IEnumerator Case_StegoSniff(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset(); // also resets the sniffer timer + pulse counter

            ctx.Assert(FirstActiveMound(gm) != null, "no active mound for the sniffer");
            DinoController stego = gm.TestSpawnDino(DinoType.Stegosaurus, GrowthStage.Kid);
            ctx.Assert(stego != null && stego.IsBuddy, "buddy Stego spawn failed");

            int before = gm.TestSnifferPulses;
            yield return ctx.WaitUntil(() => gm.TestSnifferPulses > before);

            ctx.Log($"sniffer pulsed {gm.TestSnifferPulses - before}x toward the nearest mound");
            gm.TestReset();
        }

        // Buddy Triceratops ferries a far-away fruit back to the backhoe.
        private IEnumerator Case_TrikeCarry(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();

            DinoController trike = gm.TestSpawnDino(DinoType.Triceratops, GrowthStage.Kid);
            ctx.Assert(trike != null && trike.IsBuddy, "buddy Trike spawn failed");
            yield return ctx.WaitFrames(2);

            Transform bh = gm.TestBackhoe.transform;
            // The map has streams/trees near spawn now — probe several directions and
            // distances for a walkable drop >2.5 units out instead of assuming due east.
            Vector3 far = bh.position;
            Vector2[] probeDirs =
            {
                new Vector2(1f, 0f), new Vector2(-1f, 0f), new Vector2(0f, 1f), new Vector2(0f, -1f),
                new Vector2(0.7f, 0.7f), new Vector2(-0.7f, 0.7f), new Vector2(0.7f, -0.7f), new Vector2(-0.7f, -0.7f),
            };
            for (int pd = 0; pd < probeDirs.Length && (far - bh.position).magnitude <= 2.5f; pd++)
            {
                for (float pdist = 5f; pdist <= 8f && (far - bh.position).magnitude <= 2.5f; pdist += 1.5f)
                {
                    Vector3 probe = bh.position + (Vector3)(probeDirs[pd] * pdist);
                    Vector3 clamped = WalkableNear(gm.TestMap, probe);
                    if ((clamped - bh.position).magnitude > 2.5f)
                    {
                        far = clamped;
                    }
                }
            }

            ctx.Assert((far - bh.position).magnitude > 2.5f, "could not place fruit far enough away");
            ItemPickup fruit = gm.TestSpawnItem(ItemType.Fruit, DinoType.TRex, 0, far);
            ctx.Assert(fruit != null, "fruit spawn failed");

            // Wait for the full run: land -> scan -> fetch -> carry -> set down near the backhoe.
            yield return ctx.WaitUntil(() =>
                fruit != null && !fruit.IsCarried && !fruit.IsConsumed &&
                (fruit.transform.position - bh.position).magnitude <= 1.5f);

            ctx.Assert(fruit != null && !fruit.IsConsumed, "fruit was lost during the carry");
            float dist = (fruit.transform.position - bh.position).magnitude;
            ctx.Assert(dist <= 1.5f, $"fruit set down {dist:F2} from the backhoe (expected <= 1.5)");
            ctx.Log($"Trike fetched a fruit from 5 units out and set it down {dist:F2} from the backhoe");
            gm.TestReset();
        }

        // The all-four-species-Big parade fires exactly once, sets the save flag,
        // and never repeats.
        private IEnumerator Case_ParadeOnce(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();

            bool savedFlag = gm.Save.Data.ParadeDone;
            gm.Save.Data.ParadeDone = false;

            int paradeEvents = 0;
            Action onParade = () => paradeEvents++;
            GameEvents.ParadeStarted += onParade;

            try
            {
                gm.TestSpawnDino(DinoType.TRex, GrowthStage.Big);
                gm.TestSpawnDino(DinoType.Triceratops, GrowthStage.Big);
                gm.TestSpawnDino(DinoType.Brachiosaurus, GrowthStage.Big);
                DinoController last = gm.TestSpawnDino(DinoType.Stegosaurus, GrowthStage.Big);
                yield return ctx.WaitFrames(2);

                // The real trigger path: a grow event reaching the GameManager.
                GameEvents.RaiseDinoGrew(last.Type, GrowthStage.Big);
                yield return ctx.WaitFrames(3);

                ctx.Assert(paradeEvents == 1, $"parade fired {paradeEvents}x (expected 1)");
                ctx.Assert(gm.Save.Data.ParadeDone, "ParadeDone flag not set in the save");
                ctx.Assert(gm.TestParadeActive, "parade did not start marching");

                // Second trigger: both the event path and the direct check are no-ops.
                GameEvents.RaiseDinoGrew(last.Type, GrowthStage.Big);
                gm.TestTryStartParade();
                yield return ctx.WaitFrames(3);
                ctx.Assert(paradeEvents == 1, $"parade repeated ({paradeEvents}x) despite ParadeDone");

                ctx.Log("parade fired once, flag saved, repeat triggers ignored");
            }
            finally
            {
                GameEvents.ParadeStarted -= onParade;
                gm.Save.Data.ParadeDone = savedFlag; // restore the player's real flag
                gm.Save.Save();
                gm.TestReset();
            }
        }

        // ============================================================ STREAMS / DUCKS

        // The carved streams are CONTINUOUS ribbons: >= 2 courses, >= 8 cells each, each
        // course's cells 4-adjacent-consecutive (no gaps/jumps) with a coastal source and
        // a pond mouth — and the mandatory connectivity guarantee still holds (every
        // walkable cell reachable from start, bridges healing any stream-cut regions).
        private IEnumerator Case_StreamsConnectivity(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            OverworldMap map = gm.TestMap;
            ctx.Assert(map != null, "no overworld map");

            var streams = UnityEngine.Object.FindFirstObjectByType<StreamNetwork>();
            ctx.Assert(streams != null,
                "no StreamNetwork in the scene (rebuild via DinoDigger/Build Main Scene)");

            // Endpoint classifiers mirror the deterministic map generation (island ellipse
            // centered at 23.5 radius 23*0.95; pond ellipse at (15,31) radii 5.6/4.2).
            bool OnIsland(int x, int y)
            {
                float nx = (x - 23.5f) / 23f, ny = (y - 23.5f) / 23f;
                return Mathf.Sqrt(nx * nx + ny * ny) < 0.95f;
            }
            bool InPond(int x, int y)
            {
                float px = (x - 15f) / 5.6f, py = (y - 31f) / 4.2f;
                return px * px + py * py < 1f;
            }
            // Coastal = within a 2-cell band of open ocean, classified from the
            // PAINTED map (ocean = no ground tile and no water tile) rather than a
            // re-derived ellipse — the formula copy drifts at the rim (e.g. (18,2)).
            bool IsOcean(Vector3Int c) => !map.TestHasGround(c) && !map.TestHasWater(c);
            bool IsCoast(Vector3Int c)
            {
                if (IsOcean(c))
                {
                    return false;
                }

                for (int dx = -2; dx <= 2; dx++)
                {
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        if (IsOcean(new Vector3Int(c.x + dx, c.y + dy, 0)))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
            bool IsPondEnd(Vector3Int c) => InPond(c.x, c.y) ||
                InPond(c.x + 1, c.y) || InPond(c.x - 1, c.y) ||
                InPond(c.x, c.y + 1) || InPond(c.x, c.y - 1);

            int longCourses = 0;
            int totalStreamCells = 0;
            for (int i = 0; i < streams.Count; i++)
            {
                IReadOnlyList<Vector3Int> course = streams.CourseCells(i);
                int cells = course != null ? course.Count : 0;
                totalStreamCells += cells;
                if (cells >= 8)
                {
                    longCourses++;
                }

                if (course == null || cells == 0)
                {
                    continue;
                }

                // Continuity: every consecutive pair is exactly one cardinal step apart.
                for (int k = 1; k < cells; k++)
                {
                    int man = Mathf.Abs(course[k].x - course[k - 1].x) +
                              Mathf.Abs(course[k].y - course[k - 1].y);
                    ctx.Assert(man == 1,
                        $"course {i} breaks continuity between {course[k - 1]} and {course[k]} (step {man})");
                }

                // Endpoints: a continuous ribbon from the coast to the pond.
                Vector3Int head = course[0];
                Vector3Int mouth = course[cells - 1];
                ctx.Assert(IsCoast(head) || IsPondEnd(head),
                    $"course {i} head {head} is neither coast- nor pond-adjacent");
                ctx.Assert(IsCoast(mouth) || IsPondEnd(mouth),
                    $"course {i} mouth {mouth} is neither coast- nor pond-adjacent");
                ctx.Assert(IsCoast(head) || IsCoast(mouth),
                    $"course {i} has no coastal end (ducks must spawn at the coast)");
            }

            ctx.Assert(streams.Count >= 2, $"only {streams.Count} stream course(s) (expected >= 2)");
            ctx.Assert(longCourses >= 2,
                $"only {longCourses} stream course(s) with >= 8 cells (expected >= 2)");

            // Flood every walkable cell from the backhoe's start cell.
            const int n = 48;
            Vector3Int start = map.WorldToCell(gm.TestBackhoe.transform.position);
            ctx.Assert(map.IsWalkableCell(start), "backhoe start cell is not walkable");

            var reached = new HashSet<Vector3Int>();
            var frontier = new Queue<Vector3Int>();
            reached.Add(start);
            frontier.Enqueue(start);
            Vector3Int[] step =
            {
                new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0),
                new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0),
            };
            while (frontier.Count > 0)
            {
                Vector3Int c = frontier.Dequeue();
                for (int i = 0; i < step.Length; i++)
                {
                    Vector3Int nb = c + step[i];
                    if (!reached.Contains(nb) && map.IsWalkableCell(nb))
                    {
                        reached.Add(nb);
                        frontier.Enqueue(nb);
                    }
                }
            }

            int totalWalkable = 0;
            Vector3Int firstUnreached = new Vector3Int(-1, -1, 0);
            for (int x = 0; x < n; x++)
            {
                for (int y = 0; y < n; y++)
                {
                    var c = new Vector3Int(x, y, 0);
                    if (map.IsWalkableCell(c))
                    {
                        totalWalkable++;
                        if (!reached.Contains(c) && firstUnreached.x < 0)
                        {
                            firstUnreached = c;
                        }
                    }
                }
            }

            ctx.Assert(firstUnreached.x < 0,
                $"walkable cell {firstUnreached} unreachable from start (island not fully connected)");

            ctx.Log($"{streams.Count} streams ({longCourses} with >= 8 cells, {totalStreamCells} cells total); " +
                    $"all {totalWalkable} walkable cells reachable from start");
            yield break;
        }

        // Force-spawn a duck, tap it: it must catch (quack + flap-away despawn) and
        // leave a fruit-or-treasure reward where it sat.
        private IEnumerator Case_DuckCatch(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();

            var spawner = UnityEngine.Object.FindFirstObjectByType<DuckController>();
            ctx.Assert(spawner != null,
                "no DuckController in the scene (rebuild via DinoDigger/Build Main Scene)");

            Duck duck = spawner.TestForceSpawnDuck();
            ctx.Assert(duck != null, "duck force-spawn failed (no streams / no duck art wired)");
            yield return ctx.WaitFrames(2); // collider live

            // Sit the duck next to the backhoe so the routed tap + reward land on the
            // reachable center of the island, then tap it through the collider router.
            Vector3 spot = WalkableNear(gm.TestMap, gm.TestBackhoe.transform.position + new Vector3(1.5f, 0f, 0f));
            duck.transform.position = spot;
            Physics2D.SyncTransforms();

            int pickupsBefore = CountOverworldPickups(gm, false);
            int treasureBefore = gm.Save.Data.TreasureCount;

            // The reward is a coin flip between fruit and TREASURE, and treasure banks into
            // the same wallet the town builder spends from. A spend in the banking frame can
            // leave the count at or below where it started, so a plain "wallet went up" poll
            // could wait for a rise that already happened and been spent (see
            // Case_TreasureCounter). Freeze the queue so the signal stays truthful.
            TownController.TestSuspendBuilds = true;
            try
            {
                gm.TestTapWorldRouted(duck.transform.position);
                ctx.Assert(duck.TestCaught, "tapping the duck did not catch it");

                // A reward appears: a lingering fruit pickup, or a treasure that flew to
                // the counter (auto-collect bumps the treasure count). Both are one arc plus
                // a short flight; 20s of wall clock is ~50x that even at 1x speed.
                bool rewarded = false;
                yield return ctx.WaitUntil(() =>
                {
                    rewarded |= CountOverworldPickups(gm, false) > pickupsBefore ||
                                gm.Save.Data.TreasureCount > treasureBefore;
                    return rewarded;
                }, 20f, "no fruit/treasure reward left where the duck was caught");

                // The caught duck flaps away and despawns. Latch the reward above rather than
                // re-testing it here: a fruit reward can be eaten (or a treasure spent) while
                // the duck is still flying out, and "the reward existed" is what this asserts.
                yield return ctx.WaitUntil(() => duck == null, 20f,
                    "caught duck never flapped away (no despawn)");
                ctx.Assert(rewarded, "no fruit/treasure reward left where the duck was caught");

                ctx.Log("tapped a duck: it quacked, flapped away (despawned), and left a reward");
            }
            finally
            {
                TownController.TestSuspendBuilds = false;
            }

            gm.TestReset();
        }

        // ============================================================ CONSOLE HYGIENE

        private IEnumerator Case_NoConsoleErrors(TestContext ctx)
        {
            yield return ctx.WaitFrames(1);
            string detail = _errors.Count == 0
                ? "zero Error/Exception log entries across the whole run"
                : $"{_errors.Count} console error(s): " +
                  string.Join(" | ", _errors.GetRange(0, Mathf.Min(3, _errors.Count)));
            ctx.Assert(_errors.Count == 0, detail);
            ctx.Log(detail);
        }

        // ================================================================= HELPERS

        /// <summary>Realtime budget for one driven leg, using the model PathfindingAnywhere
        /// settled on: a 6s floor plus 0.5s per world unit of crow-flies distance, capped at
        /// 20s. The runner drives at 3x game speed and the backhoe moves 3.5 u/s, so a leg
        /// really costs ~0.1s/unit — 0.5s/unit is 5x slack for detours, replans, and the load
        /// hitches this machine produces when two editors and a pile of agents share it.
        /// Distance-proportional on purpose: a flat budget is either too tight for the long
        /// legs or too slack to ever catch a wedge on the short ones.</summary>
        private static float LegBudget(Vector3 from, Vector3 to)
        {
            return Mathf.Clamp(6f + (to - from).magnitude * 0.5f, 6f, 20f);
        }

        private IEnumerator EnterDig(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            DigMound m = FirstActiveMound(gm);
            ctx.Assert(m != null, "no active mound to dig");
            // On the 48x48 island the nearest mound is usually OFF-SCREEN, and
            // TapWorld's world->screen conversion drops off-screen taps. Use the
            // same code path a mound tap invokes; the tap->collider routing itself
            // is covered by MoundToDig (which walks into view first).
            Vector3 from = gm.TestBackhoe.transform.position;
            gm.TestBackhoe.DriveToMound(m);

            // Budget the drive by DISTANCE (plus the dig zoom), so a mound on the far side of
            // the island is not a race against a flat number, and name both waits: a wedged
            // approach used to surface as an anonymous case-level timeout.
            yield return ctx.WaitUntil(() => gm.State.Is(GameState.Dig),
                LegBudget(from, m.transform.position) + 5f,
                "backhoe never reached the mound / dig never opened");
            yield return ctx.WaitUntil(() => gm.TestDigMode.TestTileCount > 0, 10f,
                "dig opened but built no tiles");
        }

        private IEnumerator TapTileUntilDestroyed(TestContext ctx, DigModeController dm, DirtTile tile)
        {
            int guard = 0;
            while (tile != null && !tile.IsDestroyed && dm.IsOpen && guard++ < 12)
            {
                // Pace to the arm AND to gravity: a same-tile re-tap issued mid-bite is dropped
                // by the dig queue, and a tap aimed at a tile that is still FALLING into the
                // cell the cascade moved it to is dropped by the controller (it lands first).
                // Waiting on both is what keeps every tap in this helper a real bite — and the
                // tap position is re-read below, so a tile that fell is still tapped where it
                // now sits, not where it used to be.
                yield return ctx.WaitUntil(() =>
                    tile == null || tile.IsDestroyed || !dm.IsOpen ||
                    (dm.TestArmReady && !tile.IsFalling));
                if (tile == null || tile.IsDestroyed || !dm.IsOpen)
                {
                    break;
                }

                int before = tile.TestDamage;
                ctx.TapWorld(tile.transform.position);
                yield return ctx.WaitUntil(() => tile == null || tile.IsDestroyed || tile.TestDamage > before || !dm.IsOpen);
            }
        }

        /// <summary>Wait until the dino's uniform scale holds steady for ~0.3s of
        /// scaled time, so a scale assertion reads the resting stage scale rather than
        /// a mid-flight eat/grow punch-scale overshoot.</summary>
        private IEnumerator WaitForStableScale(TestContext ctx, DinoController dino)
        {
            float last = dino.transform.localScale.x;
            float stableFor = 0f;
            while (stableFor < 0.3f)
            {
                yield return null;
                float now = dino.transform.localScale.x;
                if (Mathf.Abs(now - last) < 0.002f)
                {
                    stableFor += Time.deltaTime;
                }
                else
                {
                    stableFor = 0f;
                }

                last = now;
            }
        }

        private DigMound FirstActiveMound(GameManager gm)
        {
            // Nearest active mound — the straight-line steering gives up on far
            // targets blocked by the pond, so always exercise a reachable one.
            IReadOnlyList<DigMound> list = gm.TestMounds;
            if (list == null)
            {
                return null;
            }

            Vector3 bp = gm.TestBackhoe != null ? gm.TestBackhoe.transform.position : Vector3.zero;
            DigMound best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < list.Count; i++)
            {
                DigMound m = list[i];
                if (m == null || !m.IsActive)
                {
                    continue;
                }

                float sq = (m.transform.position - bp).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = m;
                }
            }

            return best;
        }

        /// <summary>True if the point sits within tapping distance of an active mound
        /// (roam-move test taps must not accidentally start a dig).</summary>
        private bool NearActiveMound(GameManager gm, Vector3 p, float radius)
        {
            IReadOnlyList<DigMound> list = gm.TestMounds;
            if (list == null)
            {
                return false;
            }

            float sqr = radius * radius;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].IsActive &&
                    (list[i].transform.position - p).sqrMagnitude <= sqr)
                {
                    return true;
                }
            }

            return false;
        }

        private DigMound FarthestActiveMound(GameManager gm)
        {
            IReadOnlyList<DigMound> list = gm.TestMounds;
            if (list == null)
            {
                return null;
            }

            Vector3 bp = gm.TestBackhoe != null ? gm.TestBackhoe.transform.position : Vector3.zero;
            DigMound best = null;
            float bestSq = -1f;
            for (int i = 0; i < list.Count; i++)
            {
                DigMound m = list[i];
                if (m == null || !m.IsActive)
                {
                    continue;
                }

                float sq = (m.transform.position - bp).sqrMagnitude;
                if (sq > bestSq)
                {
                    bestSq = sq;
                    best = m;
                }
            }

            return best;
        }

        private Vector3 WalkableNear(OverworldMap map, Vector3 desired)
        {
            if (map == null)
            {
                return desired;
            }

            Vector3 w = map.NearestWalkable(desired, out bool found);
            return found ? w : desired;
        }

        /// <summary>A walkable point at least <paramref name="minDist"/> world units from
        /// <paramref name="start"/>, clear of active mounds. Unlike
        /// <see cref="FindDistinctWalkable"/> this guarantees the SEPARATION, not just a
        /// different cell: NearestWalkable can clamp a 2-unit probe back to a neighbouring
        /// cell whose centre is a fraction of a unit away on this isometric grid, and a case
        /// that then asserts "the backhoe moved > 0.5 units" is asserting against its own
        /// setup. Sweeps 8 headings at growing radii and takes the first that qualifies;
        /// returns <paramref name="start"/> only when the backhoe is genuinely boxed in (the
        /// caller asserts on that).</summary>
        /// <summary>A walkable spot at least <paramref name="minDist"/> from <paramref name="start"/>
        /// that a tap-to-move can actually be AIMED at.
        ///
        /// THE TARGET MUST BE EMPTY GROUND, and that is the whole subtlety. GameManager routes
        /// every tap to a collider FIRST (FindTappable) and only drives the backhoe when nothing
        /// answered — so a target with anything tappable standing on it produces a tap that does
        /// something else entirely and a backhoe that never moves. The old version only avoided
        /// active dig MOUNDS, which left every other tappable in the game free to sit on the
        /// answer: a dino (collider radius 0.6, and it OUTRANKS everything a move-tap could have
        /// meant), a duck drifting past, a machine, a pickup, a building. That is exactly the
        /// shape of the "tap-to-move did not move the backhoe" failure — the case's own buddy
        /// parks in its follow slot ~1.4u from the backhoe, and the first candidate ring is at
        /// 2.0u — and it was only ever a matter of which layout the random stream dealt.
        ///
        /// So candidates are now REJECTED WHEN A TAP THERE WOULD BE SWALLOWED, probed through
        /// the same resolution the tap itself will use. The candidate set is widened at the same
        /// time so the stricter filter cannot exhaust it on a busy island.</summary>
        private Vector3 FindMoveTarget(OverworldMap map, Vector3 start, float minDist)
        {
            GameManager gm = GameManager.Instance;
            float minSq = minDist * minDist;
            float[] radii = { 2f, 2.5f, 3f, 4f, 5f, 6f, 7f, 9f, 12f }; // grows past minDist for far parks
            Vector2[] dirs =
            {
                new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(-1f, 0f), new Vector2(0f, -1f),
                new Vector2(1f, 1f), new Vector2(-1f, 1f), new Vector2(-1f, -1f), new Vector2(1f, -1f),
                new Vector2(2f, 1f), new Vector2(-2f, 1f), new Vector2(2f, -1f), new Vector2(-2f, -1f),
                new Vector2(1f, 2f), new Vector2(-1f, 2f), new Vector2(1f, -2f), new Vector2(-1f, -2f),
            };

            // Colliders only catch up with transforms on the physics tick
            // (Physics2D.autoSyncTransforms is false), so sync ONCE up front — otherwise this
            // probes a stale world and clears a spot the tap then finds occupied.
            Physics2D.SyncTransforms();

            for (int r = 0; r < radii.Length; r++)
            {
                for (int d = 0; d < dirs.Length; d++)
                {
                    Vector2 u = dirs[d].normalized * radii[r];
                    Vector3 w = map.NearestWalkable(start + new Vector3(u.x, u.y, 0f), out bool found);
                    if (found && (w - start).sqrMagnitude >= minSq &&
                        (gm == null || (!NearActiveMound(gm, w, 1.2f) && !TapWouldBeSwallowed(gm, w))))
                    {
                        return w;
                    }
                }
            }

            return start;
        }

        /// <summary>True when a tap at <paramref name="world"/> would resolve to something
        /// tappable instead of driving the backhoe. Uses the game's OWN tap resolution
        /// (GameManager.TestFindTappable -> FindTappable), so a helper can never disagree with
        /// the routing the tap will actually take.</summary>
        private bool TapWouldBeSwallowed(GameManager gm, Vector3 world)
        {
            return gm != null && gm.TestFindTappable(world) != null;
        }

        private Vector3 FindDistinctWalkable(OverworldMap map, Vector3 start)
        {
            return FindDistinctWalkable(map, start, Vector3.zero);
        }

        private Vector3 FindDistinctWalkable(OverworldMap map, Vector3 start, Vector3 preferred)
        {
            Vector3Int startCell = map.WorldToCell(start);
            GameManager gm = GameManager.Instance;

            var offsets = new List<Vector3>();
            if (preferred.sqrMagnitude > 0.01f)
            {
                offsets.Add(preferred);
            }

            offsets.Add(new Vector3(2f, 0f, 0f));
            offsets.Add(new Vector3(0f, 2f, 0f));
            offsets.Add(new Vector3(-2f, 0f, 0f));
            offsets.Add(new Vector3(0f, -2f, 0f));
            offsets.Add(new Vector3(3f, 0f, 0f));
            offsets.Add(new Vector3(0f, 3f, 0f));

            for (int i = 0; i < offsets.Count; i++)
            {
                Vector3 w = map.NearestWalkable(start + offsets[i], out bool found);
                if (found && map.WorldToCell(w) != startCell &&
                    (gm == null || !NearActiveMound(gm, w, 1.2f)))
                {
                    return w;
                }
            }

            return start;
        }

        /// <summary>Find a walkable target roughly <paramref name="worldDir"/> of
        /// <paramref name="start"/> with a clear straight walkable line (single path
        /// <summary>Teleport the backhoe to a walkable cell from which at least 3 of the
        /// given cardinal directions have clear straight-line targets. Test-only helper:
        /// the 48x48 island's spawn area can be too cluttered for the facing sweep.</summary>
        private void RelocateToOpenGround(GameManager gm, OverworldMap map,
            BackhoeController bh, Vector2[] dirs)
        {
            // Try the current spot first, then probe candidate cells around the island
            // center outward until one is open enough.
            if (CountClearCardinals(gm, map, bh.transform.position, dirs) >= 3)
            {
                return;
            }

            // Streams + trees + meadow leave fewer 4-way plazas on the island now —
            // sample generously, and remember the best 2-cardinal spot as a fallback
            // so the case can still validate two orthogonal axes if no 3+ spot exists.
            Vector3 best2 = bh.transform.position;
            bool have2 = false;
            for (int attempt = 0; attempt < 200; attempt++)
            {
                if (!map.TryRandomWalkableCell(out Vector3Int cell))
                {
                    return;
                }

                Vector3 pos = map.CellCenter(cell);
                int clear = CountClearCardinals(gm, map, pos, dirs);
                if (clear >= 3)
                {
                    bh.transform.position = pos;
                    return;
                }

                // Fallback spots must span BOTH axes (one clear horizontal + one
                // clear vertical) or the case can't rule out an axis swap.
                if (!have2 && clear >= 2 &&
                    (FindClearCardinalTarget(map, gm, pos, dirs[0], out _) ||
                     FindClearCardinalTarget(map, gm, pos, dirs[2], out _)) &&
                    (FindClearCardinalTarget(map, gm, pos, dirs[1], out _) ||
                     FindClearCardinalTarget(map, gm, pos, dirs[3], out _)))
                {
                    best2 = pos;
                    have2 = true;
                }
            }

            if (have2)
            {
                bh.transform.position = best2;
            }
        }

        private int CountClearCardinals(GameManager gm, OverworldMap map, Vector3 from, Vector2[] dirs)
        {
            int clear = 0;
            for (int i = 0; i < dirs.Length; i++)
            {
                if (FindClearCardinalTarget(map, gm, from, dirs[i], out _))
                {
                    clear++;
                }
            }

            return clear;
        }

        /// segment), far enough that the drive lasts long enough to settle the facing,
        /// and clamped closely enough to the axis that the expected Dir8 is unambiguous.</summary>
        private bool FindClearCardinalTarget(OverworldMap map, GameManager gm, Vector3 start,
            Vector2 worldDir, out Vector3 target)
        {
            Vector3Int startCell = map.WorldToCell(start);
            float[] dists = { 4f, 3.5f, 3f, 2.5f };
            for (int i = 0; i < dists.Length; i++)
            {
                Vector3 probe = start + new Vector3(worldDir.x, worldDir.y, 0f) * dists[i];
                Vector3 w = map.NearestWalkable(probe, out bool found);
                if (!found || map.WorldToCell(w) == startCell)
                {
                    continue;
                }

                if (NearActiveMound(gm, w, 1.2f) || !map.HasLineOfSight(start, w))
                {
                    continue;
                }

                // Clamped cell center must stay within the cardinal's 22.5° sector so
                // the expected facing is unambiguous (cos 22.5° ~= 0.924).
                Vector2 to = new Vector2(w.x - start.x, w.y - start.y);
                if (to.sqrMagnitude < 0.01f || Vector2.Dot(to.normalized, worldDir) < 0.93f)
                {
                    continue;
                }

                target = w;
                return true;
            }

            target = start;
            return false;
        }

        /// <summary>First clear straight-line target in any cardinal direction.</summary>
        private bool FindAnyClearCardinalTarget(OverworldMap map, GameManager gm, Vector3 start, out Vector3 target)
        {
            Vector2[] dirs =
            {
                new Vector2(1f, 0f), new Vector2(0f, 1f),
                new Vector2(-1f, 0f), new Vector2(0f, -1f),
            };
            for (int i = 0; i < dirs.Length; i++)
            {
                if (FindClearCardinalTarget(map, gm, start, dirs[i], out target))
                {
                    return true;
                }
            }

            target = start;
            return false;
        }

        /// <summary>Like <see cref="FindClearCardinalTarget"/>, but requires CORRIDOR
        /// line-of-sight for the WHOLE leg (map.HasCorridorLineOfSight), not just a single
        /// center ray. That is exactly the test the backhoe's FindPath/string-pull uses to
        /// collapse a route into one straight segment, so a leg that passes here is
        /// guaranteed to drive straight (no grid staircase around a stream/bridge) and the
        /// facing legitimately holds for the whole leg — the signal FacingStability guards.</summary>
        private bool FindStraightCorridorTarget(OverworldMap map, GameManager gm, Vector3 start, out Vector3 target)
        {
            Vector2[] dirs =
            {
                new Vector2(1f, 0f), new Vector2(0f, 1f),
                new Vector2(-1f, 0f), new Vector2(0f, -1f),
            };
            float[] dists = { 4f, 3.5f, 3f, 2.5f };
            Vector3Int startCell = map.WorldToCell(start);
            for (int d = 0; d < dirs.Length; d++)
            {
                for (int i = 0; i < dists.Length; i++)
                {
                    Vector3 probe = start + new Vector3(dirs[d].x, dirs[d].y, 0f) * dists[i];
                    Vector3 w = map.NearestWalkable(probe, out bool found);
                    if (!found || map.WorldToCell(w) == startCell)
                    {
                        continue;
                    }

                    if (NearActiveMound(gm, w, 1.2f))
                    {
                        continue;
                    }

                    // Clamped target must stay inside the cardinal's 22.5° sector so the
                    // straight leg maps to one unambiguous screen-cardinal facing.
                    Vector2 to = new Vector2(w.x - start.x, w.y - start.y);
                    if (to.sqrMagnitude < 0.01f || Vector2.Dot(to.normalized, dirs[d]) < 0.93f)
                    {
                        continue;
                    }

                    if (!map.HasCorridorLineOfSight(start, w))
                    {
                        continue; // would smooth into a staircase — not a genuinely straight drive
                    }

                    target = w;
                    return true;
                }
            }

            target = start;
            return false;
        }

        /// <summary>DinoDigger-bw4: like <see cref="FindClearCardinalTarget"/> but for an
        /// ARBITRARY unit heading (cardinal OR diagonal) and requiring CORRIDOR
        /// line-of-sight for the whole leg (<see cref="OverworldMap.HasCorridorLineOfSight"/>),
        /// so a diagonal leg drives in a genuinely straight line rather than a grid
        /// staircase — reusing the exact vetting the backhoe's string-pull uses.
        ///
        /// The clamped target must sit WELL inside the heading's Dir8 sector (dot >= 0.985,
        /// i.e. within ~10° of the ideal axis), NOT merely inside the raw 22.5° sector.
        /// Reason: the assertion compares bh.Facing (the FacingSmoother's hysteresis-stable
        /// value) against Direction8.FromVector(idealDir). The smoother HOLDS the prior
        /// leg's facing until the smoothed heading moves >22.5°+11° = 33.5° from that
        /// neighbour's centre, so a target 22° off due-south, driven right after a west
        /// leg, legitimately sticks at SW instead of S. On open ground the clean axial/
        /// diagonal cell lands exactly on the ideal (dot == 1); only blocked fallbacks are
        /// off-axis, and rejecting those keeps the expected facing unambiguous whatever the
        /// previous facing was.</summary>
        private bool FindClearStraightTarget(OverworldMap map, GameManager gm, Vector3 start,
            Vector2 worldDir, out Vector3 target)
        {
            Vector2 dir = worldDir.normalized;
            Vector3Int startCell = map.WorldToCell(start);
            float[] dists = { 4f, 3.5f, 3f, 2.5f };
            for (int i = 0; i < dists.Length; i++)
            {
                Vector3 probe = start + new Vector3(dir.x, dir.y, 0f) * dists[i];
                Vector3 w = map.NearestWalkable(probe, out bool found);
                if (!found || map.WorldToCell(w) == startCell)
                {
                    continue;
                }

                if (NearActiveMound(gm, w, 1.2f))
                {
                    continue;
                }

                Vector2 to = new Vector2(w.x - start.x, w.y - start.y);
                if (to.sqrMagnitude < 0.01f || Vector2.Dot(to.normalized, dir) < 0.985f)
                {
                    continue; // must be within ~10° of the ideal axis (see summary: hysteresis)
                }

                if (!map.HasCorridorLineOfSight(start, w))
                {
                    continue; // would smooth into a staircase — not a genuinely straight drive
                }

                target = w;
                return true;
            }

            target = start;
            return false;
        }

        /// <summary>DinoDigger-bw4: teleport the backhoe to the open cell offering the most
        /// corridor-straight legs across all 8 headings, biased toward covering both
        /// cardinal axes AND both diagonal hands (so the FacingCorrectness case can exercise
        /// the diagonal sectors that regressed). Best-effort — keeps the best spot found.</summary>
        private void RelocateForEightWay(GameManager gm, OverworldMap map,
            BackhoeController bh, Vector2[] dirs)
        {
            Vector3 bestPos = bh.transform.position;
            int bestRank = RankEightWay(gm, map, bestPos, dirs);
            for (int attempt = 0; attempt < 300 && bestRank < 1000; attempt++)
            {
                if (!map.TryRandomWalkableCell(out Vector3Int cell))
                {
                    break;
                }

                Vector3 pos = map.CellCenter(cell);
                int rank = RankEightWay(gm, map, pos, dirs);
                if (rank > bestRank)
                {
                    bestRank = rank;
                    bestPos = pos;
                }
            }

            bh.transform.position = bestPos;
            Physics2D.SyncTransforms();
        }

        /// <summary>Higher = better spot. Rewards (in priority order) covering both diagonal
        /// hands, then both cardinal axes, then raw count of corridor-straight legs.</summary>
        private int RankEightWay(GameManager gm, OverworldMap map, Vector3 from, Vector2[] dirs)
        {
            int legs = 0;
            bool x = false, y = false, de = false, dw = false;
            for (int i = 0; i < dirs.Length; i++)
            {
                if (!FindClearStraightTarget(map, gm, from, dirs[i], out _))
                {
                    continue;
                }

                legs++;
                bool diag = Mathf.Abs(dirs[i].x) > 0.5f && Mathf.Abs(dirs[i].y) > 0.5f;
                if (diag)
                {
                    if (dirs[i].x > 0f) { de = true; } else { dw = true; }
                }
                else if (Mathf.Abs(dirs[i].x) > 0.5f) { x = true; } else { y = true; }
            }

            int rank = legs;
            if (x && y) { rank += 100; }
            if (de && dw) { rank += 500; }
            if (x && y && de && dw && legs >= 6) { rank += 1000; }
            return rank;
        }

        /// <summary>Teleport the backhoe to vetted open ground from which a
        /// corridor-straight cardinal leg exists, and hand back that leg's target. Tries
        /// the current spot first, then samples random walkable cells. Test-only: the
        /// 48x48 island's spawn area (and wherever a prior case parked the backhoe) can be
        /// too cluttered with streams/trees to offer a genuinely straight drive.</summary>
        private bool RelocateForStraightLeg(GameManager gm, OverworldMap map, BackhoeController bh, out Vector3 target)
        {
            if (FindStraightCorridorTarget(map, gm, bh.transform.position, out target))
            {
                return true;
            }

            for (int attempt = 0; attempt < 300; attempt++)
            {
                if (!map.TryRandomWalkableCell(out Vector3Int cell))
                {
                    break;
                }

                Vector3 pos = map.CellCenter(cell);
                if (FindStraightCorridorTarget(map, gm, pos, out target))
                {
                    bh.transform.position = pos;
                    Physics2D.SyncTransforms();
                    return true;
                }
            }

            target = bh.transform.position;
            return false;
        }

        /// <summary>A POND SHORE cell: painted island water (never open ocean) that sits in a
        /// real body of water and touches land, nearest the backhoe so the clamp-to-shore
        /// drive is short. Found from the map data, not a hardcoded rect (DinoDigger-8e1):
        /// the old scan swept x4-11,y13-19, which the pond moved out of, so it kept returning
        /// an OCEAN cell and the case silently stopped testing the pond-tap rejection its
        /// comment claims.</summary>
        private bool FindBlockedPondCell(OverworldMap map, out Vector3Int cell)
        {
            return FindIslandWaterCell(map, minWaterNeighbors: 3, requireLandNeighbor: true, out cell) ||
                   FindIslandWaterCell(map, minWaterNeighbors: 2, requireLandNeighbor: true, out cell) ||
                   FindIslandWaterCell(map, minWaterNeighbors: 0, requireLandNeighbor: true, out cell);
        }

        /// <summary>Scan the whole island for an ENCLOSED water cell and hand back the one
        /// NEAREST the backhoe that satisfies the shape asked for.
        ///
        /// How the painted map actually distinguishes water from ocean (SceneBuilder.PaintMap,
        /// verified against the built scene): pond ('W') and stream ('S') cells get a WATER
        /// tile and NO ground tile; open ocean ('~') gets NO tile on any layer; land gets
        /// ground and no water. So a water tile already means "not ocean" — an earlier version
        /// of this helper additionally demanded a ground tile underneath and therefore matched
        /// ZERO cells in the real scene. <see cref="InlandWaterMask"/> adds the enclosure test
        /// the ticket asks for on top (flood-fill; a body touching the map border is ocean),
        /// so this keeps working even if a future scene does paint its sea.
        ///
        /// <paramref name="minWaterNeighbors"/> (of the 4 orthogonal neighbours) filters out
        /// the 1-cell-wide streams when a real pond BODY is wanted; <paramref name="requireLandNeighbor"/>
        /// keeps the result on the shoreline, where the clamp-to-land target is a short drive away.</summary>
        private bool FindIslandWaterCell(OverworldMap map, int minWaterNeighbors,
            bool requireLandNeighbor, out Vector3Int cell)
        {
            int[] dx = { -1, 1, 0, 0 };
            int[] dy = { 0, 0, -1, 1 };

            GameManager gm = GameManager.Instance;
            Vector3 from = gm != null && gm.TestBackhoe != null
                ? gm.TestBackhoe.transform.position
                : Vector3.zero;

            bool[,] inland = InlandWaterMask(map);

            cell = Vector3Int.zero;
            bool found = false;
            float bestSq = float.MaxValue;

            for (int x = 0; x < MapCells; x++)
            {
                for (int y = 0; y < MapCells; y++)
                {
                    var c = new Vector3Int(x, y, 0);
                    if (!inland[x, y] || map.IsWalkableCell(c))
                    {
                        continue; // ocean, dry land, or a walkable cell — not pond water
                    }

                    int water = 0;
                    bool land = false;
                    for (int i = 0; i < 4; i++)
                    {
                        var nb = new Vector3Int(x + dx[i], y + dy[i], 0);
                        if (map.TestHasWater(nb))
                        {
                            water++;
                        }

                        if (map.IsWalkableCell(nb))
                        {
                            land = true;
                        }
                    }

                    if (water < minWaterNeighbors || (requireLandNeighbor && !land))
                    {
                        continue;
                    }

                    float sq = (map.CellCenter(c) - from).sqrMagnitude;
                    if (sq < bestSq)
                    {
                        bestSq = sq;
                        cell = c;
                        found = true;
                    }
                }
            }

            return found;
        }

        /// <summary>Which painted water cells belong to an ENCLOSED body — a pond or a stream
        /// on the island — rather than to the open sea. Flood-fills the water cells 4-way and
        /// discards any body that reaches the map border, which is the definition of "open
        /// ocean" that survives a re-themed map: no rect, no ellipse, no assumption about
        /// which tilemap layers a water cell happens to carry.</summary>
        private bool[,] InlandWaterMask(OverworldMap map)
        {
            var water = new bool[MapCells, MapCells];
            for (int x = 0; x < MapCells; x++)
            {
                for (int y = 0; y < MapCells; y++)
                {
                    water[x, y] = map.TestHasWater(new Vector3Int(x, y, 0));
                }
            }

            var visited = new bool[MapCells, MapCells];
            var inland = new bool[MapCells, MapCells];
            var body = new List<Vector3Int>();
            var stack = new List<Vector3Int>();
            int[] dx = { -1, 1, 0, 0 };
            int[] dy = { 0, 0, -1, 1 };

            for (int sx = 0; sx < MapCells; sx++)
            {
                for (int sy = 0; sy < MapCells; sy++)
                {
                    if (!water[sx, sy] || visited[sx, sy])
                    {
                        continue;
                    }

                    body.Clear();
                    stack.Clear();
                    stack.Add(new Vector3Int(sx, sy, 0));
                    visited[sx, sy] = true;
                    bool touchesBorder = false;

                    while (stack.Count > 0)
                    {
                        Vector3Int p = stack[stack.Count - 1];
                        stack.RemoveAt(stack.Count - 1);
                        body.Add(p);
                        if (p.x == 0 || p.y == 0 || p.x == MapCells - 1 || p.y == MapCells - 1)
                        {
                            touchesBorder = true; // reaches the edge of the world: open sea
                        }

                        for (int i = 0; i < 4; i++)
                        {
                            int nx = p.x + dx[i], ny = p.y + dy[i];
                            if (nx < 0 || ny < 0 || nx >= MapCells || ny >= MapCells ||
                                !water[nx, ny] || visited[nx, ny])
                            {
                                continue;
                            }

                            visited[nx, ny] = true;
                            stack.Add(new Vector3Int(nx, ny, 0));
                        }
                    }

                    if (!touchesBorder)
                    {
                        for (int i = 0; i < body.Count; i++)
                        {
                            inland[body[i].x, body[i].y] = true;
                        }
                    }
                }
            }

            return inland;
        }

        /// <summary>Locate a tree tile on the Obstacles tilemap that has a walkable
        /// cell right next to it (so a dino can stand beside it). Nearest-to-backhoe
        /// first, purely for nicer test framing.</summary>
        private bool FindTreeCell(GameManager gm, out Vector3Int cell, out Vector3 world)
        {
            cell = Vector3Int.zero;
            world = Vector3.zero;

            OverworldMap map = gm.TestMap;
            var lib = gm.TestLibrary;
            if (map == null || lib == null || lib.TreeTile == null)
            {
                return false;
            }

            Vector3 bp = gm.TestBackhoe != null ? gm.TestBackhoe.transform.position : Vector3.zero;
            int[] dx = { -1, 1, 0, 0 };
            int[] dy = { 0, 0, -1, 1 };
            float bestSq = float.MaxValue;
            bool found = false;

            for (int x = 0; x < 48; x++)
            {
                for (int y = 0; y < 48; y++)
                {
                    var c = new Vector3Int(x, y, 0);
                    if (map.ObstacleAt(c) != lib.TreeTile)
                    {
                        continue;
                    }

                    bool hasNeighbor = false;
                    for (int i = 0; i < 4; i++)
                    {
                        if (map.IsWalkableCell(new Vector3Int(x + dx[i], y + dy[i], 0)))
                        {
                            hasNeighbor = true;
                            break;
                        }
                    }

                    if (!hasNeighbor)
                    {
                        continue;
                    }

                    Vector3 w = map.CellCenter(c);

                    // An active mound (ITappable, ~0.7 collider) sitting within ~0.56
                    // units of the tree on this isometric grid would swallow the routed
                    // tree tap. Skip trees whose center a mound collider could cover so
                    // the tap always reaches OnTreeTapped.
                    if (NearActiveMound(gm, w, 0.8f))
                    {
                        continue;
                    }

                    float sq = (w - bp).sqrMagnitude;
                    if (sq < bestSq)
                    {
                        bestSq = sq;
                        cell = c;
                        world = w;
                        found = true;
                    }
                }
            }

            return found;
        }

        /// <summary>Locate a rock tile on the Obstacles tilemap that has a walkable
        /// cell right next to it (so an Anky can stand beside it) and whose center no
        /// active mound collider could cover (which would swallow the routed rock tap).
        /// Nearest-to-backhoe first, purely for nicer test framing.</summary>
        private bool FindRockCell(GameManager gm, out Vector3Int cell, out Vector3 world)
        {
            cell = Vector3Int.zero;
            world = Vector3.zero;

            OverworldMap map = gm.TestMap;
            var lib = gm.TestLibrary;
            if (map == null || lib == null || lib.RockTile == null)
            {
                return false;
            }

            Vector3 bp = gm.TestBackhoe != null ? gm.TestBackhoe.transform.position : Vector3.zero;
            int[] dx = { -1, 1, 0, 0 };
            int[] dy = { 0, 0, -1, 1 };
            float bestSq = float.MaxValue;
            bool found = false;

            for (int x = 0; x < 48; x++)
            {
                for (int y = 0; y < 48; y++)
                {
                    var c = new Vector3Int(x, y, 0);
                    if (map.ObstacleAt(c) != lib.RockTile)
                    {
                        continue;
                    }

                    bool hasNeighbor = false;
                    for (int i = 0; i < 4; i++)
                    {
                        if (map.IsWalkableCell(new Vector3Int(x + dx[i], y + dy[i], 0)))
                        {
                            hasNeighbor = true;
                            break;
                        }
                    }

                    if (!hasNeighbor)
                    {
                        continue;
                    }

                    Vector3 w = map.CellCenter(c);
                    if (NearActiveMound(gm, w, 0.8f))
                    {
                        continue;
                    }

                    float sq = (w - bp).sqrMagnitude;
                    if (sq < bestSq)
                    {
                        bestSq = sq;
                        cell = c;
                        world = w;
                        found = true;
                    }
                }
            }

            return found;
        }

        /// <summary>A tappable plain (unburied) DIRT tile. Never returns a tile that is mid-FALL:
        /// the controller drops taps aimed at a travelling tile, so handing one back would spend a
        /// case's tap budget on bites that can never land.
        ///
        /// "Plain" now also means Kind == Dirt (DinoDigger-z4d). Every caller taps this tile and
        /// then expects ORDINARY dirt behaviour — a damage tick per bite, a crack sprite per
        /// state, a crumble at max health — and no toy does any of that: a crystal pops its whole
        /// blob on the first bite with no sprite change, a pot has only one crack state, and a
        /// geode ARMS instead of taking damage at all (a wait on "damage went up" would simply
        /// never finish). Filtering here fixes every caller at once rather than making each one
        /// re-learn what a toy is.</summary>
        private DirtTile FindPlainTile(DigModeController dm)
        {
            DirtTile mid = dm.TestTileAt(0, dm.TestCols / 2);
            if (IsPlainDirt(mid))
            {
                return mid;
            }

            IReadOnlyList<DirtTile> tiles = dm.TestTiles;
            for (int i = 0; i < tiles.Count; i++)
            {
                if (IsPlainDirt(tiles[i]))
                {
                    return tiles[i];
                }
            }

            return null;
        }

        private bool IsPlainDirt(DirtTile t)
        {
            return t != null && !t.HasItem && !t.IsDestroyed && !t.IsFalling &&
                   t.Kind == DigTileKind.Dirt;
        }

        private int NeighborsIntactCount(DigModeController dm, DirtTile tile)
        {
            int[] dr = { -1, 1, 0, 0 };
            int[] dc = { 0, 0, -1, 1 };
            int n = 0;
            for (int i = 0; i < 4; i++)
            {
                DirtTile t = dm.TestTileAt(tile.Row + dr[i], tile.Col + dc[i]);
                if (t != null && !t.IsDestroyed)
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>The tile's four neighbours captured AS REFERENCES.
        ///
        /// GRAVITY (DinoDigger-7fw): a grid coordinate no longer names the same tile from one
        /// moment to the next — clearing a tile drops its whole column by a row — so a
        /// before/after damage comparison addressed by row/col can read a different tile at the
        /// end than it did at the start (or nothing at all, since a cleared cell is vacated).
        /// Holding the tiles themselves is what keeps such a comparison meaningful.</summary>
        private List<DirtTile> NeighborTilesOf(DigModeController dm, DirtTile tile)
        {
            var list = new List<DirtTile>(4);
            int[] dr = { -1, 1, 0, 0 };
            int[] dc = { 0, 0, -1, 1 };
            for (int i = 0; i < 4; i++)
            {
                DirtTile t = dm.TestTileAt(tile.Row + dr[i], tile.Col + dc[i]);
                if (t != null)
                {
                    list.Add(t);
                }
            }

            return list;
        }

        /// <summary>Total damage across a captured tile list. Damage never decreases and a
        /// crumbled tile keeps its final value, so this only ever grows — which is exactly what
        /// makes it safe to compare across a cascade.</summary>
        private int DamageSumOf(List<DirtTile> tiles)
        {
            int sum = 0;
            for (int i = 0; i < tiles.Count; i++)
            {
                if (tiles[i] != null)
                {
                    sum += tiles[i].TestDamage;
                }
            }

            return sum;
        }

        /// <summary>A clean stage for a gravity assertion: a column with every cell still
        /// filled, no surprise pocket in it (the pocket is exempt from landing cracks, which
        /// would make an expected crack count conditional), NO DIG TOY in it, and a plain tile
        /// one row above the floor to clear (clearing a BURIED tile would collect an item, and
        /// collecting the last one ends the round mid-assertion).
        ///
        /// The toy exclusion (DinoDigger-z4d) is the same kind of exclusion as the pocket's: a
        /// crystal is exempt from landing cracks so it would swallow an expected crack, a geode
        /// ARMS instead of taking one (so a column ground out around it is not empty until its
        /// whumph fires a beat later), and a pot breaking mid-assertion sprays coins. All of that
        /// is correct toy behaviour with its own cases; it just is not what a column of dirt
        /// falling is supposed to be measuring.</summary>
        private int FindDropColumn(DigModeController dm)
        {
            DirtTile pocket = dm.TestSurpriseTile;
            for (int c = 0; c < dm.TestCols; c++)
            {
                if (pocket != null && pocket.Col == c)
                {
                    continue;
                }

                if (dm.TestColumnCount(c) != dm.TestRows)
                {
                    continue;
                }

                if (ColumnHasToy(dm, c))
                {
                    continue;
                }

                DirtTile target = dm.TestTileAt(dm.TestRows - 2, c);
                if (target != null && !target.HasItem)
                {
                    return c;
                }
            }

            return -1;
        }

        private bool ColumnHasToy(DigModeController dm, int c)
        {
            for (int r = 0; r < dm.TestRows; r++)
            {
                DirtTile t = dm.TestTileAt(r, c);
                if (t != null && t.Kind != DigTileKind.Dirt)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Pin every tile in a column at the canonical 3 taps.
        ///
        /// Per-theme hardness rolls as low as ONE tap, and a 1-tap tile is COMPLETED by a single
        /// landing crack — which turns a tidy one-row drop into a chain. That chain is real
        /// behaviour (CascadeNeverWedges drives it on purpose), but a case asserting what one
        /// clear does must not have its outcome decided by a hardness roll.</summary>
        private void PinColumnHardness(DigModeController dm, int col)
        {
            for (int r = 0; r < dm.TestRows; r++)
            {
                DirtTile t = dm.TestTileAt(r, col);
                if (t != null && !t.IsDestroyed)
                {
                    t.TestSetMaxHealth(3);
                }
            }
        }

        /// <summary>A 2x2 block of cells a dig TOY may be planted in: all four alive, plain
        /// dirt, item-free and not the surprise pocket — exactly the bar site generation holds
        /// itself to, so a hand-built board is one the game could really have produced.
        ///
        /// Starts at row 1, never row 0: a toy in the top row has nothing above it, so popping
        /// it would drop no tiles at all and the case would assert a cascade that never ran.
        /// Returns the TOP-LEFT cell.</summary>
        private bool FindCleanSquare(DigModeController dm, out int row, out int col)
        {
            for (int r = 1; r + 1 < dm.TestRows; r++)
            {
                for (int c = 0; c + 1 < dm.TestCols; c++)
                {
                    if (IsToyCandidate(dm, r, c) && IsToyCandidate(dm, r, c + 1) &&
                        IsToyCandidate(dm, r + 1, c) && IsToyCandidate(dm, r + 1, c + 1))
                    {
                        row = r;
                        col = c;
                        return true;
                    }
                }
            }

            row = -1;
            col = -1;
            return false;
        }

        private bool IsToyCandidate(DigModeController dm, int r, int c)
        {
            // CoversBone (DinoDigger-0z5) joins the refusals for the same reason HasItem is
            // there: the bone layer has already claimed that cell, and the controller's own
            // Test/Demo placement hooks refuse it too — a hand-built board must stay a board
            // the game could really have produced.
            DirtTile t = dm.TestTileAt(r, c);
            return t != null && !t.IsDestroyed && !t.HasItem && !t.IsSurprise && !t.CoversBone &&
                   t.Kind == DigTileKind.Dirt;
        }

        /// <summary>Any tile still standing (for driving a cascade into a board that several
        /// clears have already chewed through).</summary>
        private DirtTile FindAliveTile(DigModeController dm)
        {
            for (int r = 0; r < dm.TestRows; r++)
            {
                for (int c = 0; c < dm.TestCols; c++)
                {
                    DirtTile t = dm.TestTileAt(r, c);
                    if (t != null && !t.IsDestroyed)
                    {
                        return t;
                    }
                }
            }

            return null;
        }

        private int CountOverworldPickups(GameManager gm, bool nonTreasureOnly)
        {
            Transform root = gm.TestOverworldRoot;
            if (root == null)
            {
                return 0;
            }

            ItemPickup[] arr = root.GetComponentsInChildren<ItemPickup>(true);
            int n = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                ItemPickup p = arr[i];
                if (p == null || p.IsConsumed)
                {
                    continue;
                }

                if (nonTreasureOnly && p.Type == ItemType.Treasure)
                {
                    continue;
                }

                n++;
            }

            return n;
        }
    }
}
