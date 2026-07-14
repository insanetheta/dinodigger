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
        private List<TestCase> BuildCases()
        {
            return new List<TestCase>
            {
                new TestCase("RoamTapToMove",        20f, Case_RoamTapToMove),
                new TestCase("PathfindingAnywhere", 200f, Case_PathfindingAnywhere),
                new TestCase("EightDirFacing",       25f, Case_EightDirFacing),
                new TestCase("FacingCorrectness",    30f, Case_FacingCorrectness),
                new TestCase("FacingStability",      30f, Case_FacingStability),
                new TestCase("MoundToDig",           20f, Case_MoundToDig),
                new TestCase("DirtTileDamage",       20f, Case_DirtTileDamage),
                new TestCase("PeekVisible",          20f, Case_PeekVisible),
                new TestCase("MultiItemCollection",  30f, Case_MultiItemCollection),
                new TestCase("EggHatch",             20f, Case_EggHatch),
                new TestCase("UniqueDinoNoDupes",    20f, Case_UniqueDinoNoDupes),
                new TestCase("ShardDropRate",        20f, Case_ShardDropRate),
                new TestCase("NestAssembly",         30f, Case_NestAssembly),
                new TestCase("ShardHatchCeremony",   40f, Case_ShardHatchCeremony),
                new TestCase("FruitPunchNoCompound", 20f, Case_FruitPunchNoCompound),
                new TestCase("FeedAndGrow",          25f, Case_FeedAndGrow),
                new TestCase("GrowthStageArt",       15f, Case_GrowthStageArt),
                new TestCase("DinoDance",            15f, Case_DinoDance),
                new TestCase("BigDinoHelps",         20f, Case_BigDinoHelps),
                new TestCase("TreasureCounter",      15f, Case_TreasureCounter),
                new TestCase("MoundRespawn",         20f, Case_MoundRespawn),
                new TestCase("IdleAttract",          10f, Case_IdleAttract),
                new TestCase("SaveRoundtrip",        10f, Case_SaveRoundtrip),
                new TestCase("ParentGateMute",       10f, Case_ParentGateMute),
                new TestCase("DinoIdleStable",       25f, Case_DinoIdleStable),
                new TestCase("WalkAnimCycles",       30f, Case_WalkAnimCycles),
                new TestCase("BuddyCapTwo",          35f, Case_BuddyCapTwo),
                new TestCase("BuddySwapOnTap",       25f, Case_BuddySwapOnTap),
                new TestCase("MeadowContainsResidents", 25f, Case_MeadowContainsResidents),
                new TestCase("MoundsAvoidMeadow",    20f, Case_MoundsAvoidMeadow),
                new TestCase("BrachioTreeShake",     30f, Case_BrachioTreeShake),
                new TestCase("StegoSniff",           25f, Case_StegoSniff),
                new TestCase("TrikeCarry",           35f, Case_TrikeCarry),
                new TestCase("ParadeOnce",           30f, Case_ParadeOnce),
                new TestCase("StreamsConnectivity",  15f, Case_StreamsConnectivity),
                new TestCase("DuckCatch",            20f, Case_DuckCatch),
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
            yield return ctx.WaitUntil(() => !bh.IsMoving);

            Vector3 arrived = bh.transform.position;
            ctx.Assert(map.IsWalkableWorld(arrived), "backhoe ended on a non-walkable cell");
            ctx.Assert((arrived - start).sqrMagnitude > 0.25f, "backhoe did not move on a walkable tap");

            // Pond water: tap into the pond; the target must be clamped to land.
            ctx.Assert(FindBlockedPondCell(map, out Vector3Int waterCell), "could not locate a pond/water cell");
            Vector3 waterWorld = map.CellCenter(waterCell);
            ctx.TapWorld(waterWorld);
            yield return ctx.WaitUntil(() => !bh.IsMoving);

            Vector3 after = bh.transform.position;
            ctx.Assert(map.IsWalkableWorld(after), "backhoe entered a water cell");
            ctx.Assert(map.WorldToCell(after) != waterCell, "backhoe reached the water cell (not clamped)");

            ctx.Log($"moved {map.WorldToCell(start)}->{map.WorldToCell(arrived)}; water tap {waterCell} rejected");
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

        /// <summary>An interior pond cell whose 4 neighbours are all non-walkable
        /// (deep water); falls back to any pond-border water cell for a small pond.</summary>
        private bool FindDeepWaterCell(OverworldMap map, out Vector3Int cell)
        {
            int[] dx = { -1, 1, 0, 0 };
            int[] dy = { 0, 0, -1, 1 };
            Vector3Int fallback = Vector3Int.zero;
            bool haveFallback = false;

            for (int x = 3; x <= 12; x++)
            {
                for (int y = 12; y <= 20; y++)
                {
                    var c = new Vector3Int(x, y, 0);
                    if (map.IsWalkableCell(c))
                    {
                        continue;
                    }

                    // Must be genuine water (ground painted but flooded), not empty space:
                    // a cell that borders at least one walkable land cell is on/near the pond.
                    bool bordersLand = false;
                    bool allWaterAround = true;
                    for (int i = 0; i < 4; i++)
                    {
                        var nb = new Vector3Int(x + dx[i], y + dy[i], 0);
                        if (map.IsWalkableCell(nb))
                        {
                            bordersLand = true;
                            allWaterAround = false;
                        }
                    }

                    if (bordersLand && !haveFallback)
                    {
                        fallback = c;
                        haveFallback = true;
                    }

                    if (allWaterAround)
                    {
                        // Deep only counts if it's part of the pond (reachable water region);
                        // require a walkable land cell within 2 rings so we skip void cells.
                        bool nearLand = false;
                        for (int rx = -2; rx <= 2 && !nearLand; rx++)
                        {
                            for (int ry = -2; ry <= 2; ry++)
                            {
                                if (map.IsWalkableCell(new Vector3Int(x + rx, y + ry, 0)))
                                {
                                    nearLand = true;
                                    break;
                                }
                            }
                        }

                        if (nearLand)
                        {
                            cell = c;
                            return true;
                        }
                    }
                }
            }

            cell = fallback;
            return haveFallback;
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

            Vector2[] dirs =
            {
                new Vector2(1f, 0f), new Vector2(0f, 1f),
                new Vector2(-1f, 0f), new Vector2(0f, -1f),
            };

            // The spawn area on the 48x48 island can be cluttered (trees/pond) —
            // relocate the backhoe to an open cell with >=3 clear cardinals first.
            RelocateToOpenGround(gm, map, bh, dirs);
            Vector3 anchor = bh.transform.position;

            int tested = 0;
            bool xAxisTested = false;
            bool yAxisTested = false;
            for (int i = 0; i < dirs.Length; i++)
            {
                // Each driven leg moves the backhoe off the vetted open cell, which
                // invalidates the remaining directions' clearances — snap back first.
                bh.transform.position = anchor;
                Vector3 start = anchor;
                if (!FindClearCardinalTarget(map, gm, start, dirs[i], out Vector3 target))
                {
                    continue; // no clear straight-line target this way from here
                }

                Dir8 expected = Direction8.FromVector(dirs[i]);
                bh.MoveTo(target);

                // Drive long enough for the smoothed facing to settle (>=0.4s of motion).
                float t = 0f;
                while (bh.IsMoving && t < 2.0f)
                {
                    t += Time.deltaTime;
                    yield return null;
                }

                if (t < 0.4f)
                {
                    yield return ctx.WaitSecondsScaled(0.4f - t);
                }

                ctx.Assert(bh.Facing == expected,
                    $"drove {dirs[i]} but faced {bh.Facing} (expected {expected}) — compass flip?");
                ctx.Assert(bh.TestSprite == bh.TestDirSprite(expected),
                    $"rendered sprite != wired array[{(int)expected}] ({expected}) after driving {dirs[i]}");
                tested++;
                if (Mathf.Abs(dirs[i].x) > 0.5f) { xAxisTested = true; } else { yAxisTested = true; }
            }

            // Streams/trees can make 3 clear lanes from one spot rare; two tested
            // cardinals are sufficient IF they span both axes (catches X/Y swaps
            // and the tested axes' sign flips — the jiggle/opposite-facing bugs).
            ctx.Assert(tested >= 2 && xAxisTested && yAxisTested,
                $"insufficient cardinal coverage: {tested} tested (xAxis={xAxisTested}, yAxis={yAxisTested})");
            ctx.Log($"facing correct for {tested}/4 cardinals (+X=E, +Y=N, -X=W, -Y=S), sprite matches array index");
            gm.TestReset();
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

            // ---- Backhoe: one straight leg, count facing changes across the drive. ----
            Vector3 start = bh.transform.position;
            ctx.Assert(FindAnyClearCardinalTarget(map, gm, start, out Vector3 target),
                "no clear straight leg available for the backhoe");

            bh.MoveTo(target);
            yield return ctx.WaitFrames(1);
            Dir8 last = bh.Facing;
            int changes = 0;
            int guard = 0;
            while (bh.IsMoving && guard++ < 1200)
            {
                if (bh.Facing != last)
                {
                    changes++;
                    last = bh.Facing;
                }

                yield return null;
            }

            ctx.Assert(changes <= 3, $"backhoe facing changed {changes}x on ONE straight leg (jiggle regression)");

            // ---- Dino: follow the moving backhoe ~3s, measure facing changes/sec. ----
            gm.TestReset();
            DinoController dino = gm.TestSpawnDino(DinoType.TRex, GrowthStage.Baby);
            ctx.Assert(dino != null, "dino spawn failed");
            yield return ctx.WaitFrames(2);

            Dir8 dlast = dino.TestFacing;
            int dchanges = 0;
            float elapsed = 0f;
            while (elapsed < 3f)
            {
                if (!bh.IsMoving &&
                    FindAnyClearCardinalTarget(map, gm, bh.transform.position, out Vector3 next))
                {
                    bh.MoveTo(next); // keep it moving so the dino actively follows
                }

                if (dino.TestFacing != dlast)
                {
                    dchanges++;
                    dlast = dino.TestFacing;
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

            int max = tile.TestMaxHealth;
            Sprite prev = tile.TestDirtSprite;
            int crumbPeak = 0;

            for (int hit = 1; hit <= max; hit++)
            {
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
            yield return EnterDig(ctx);
            DigModeController dm = gm.TestDigMode;

            List<DirtTile> buried = dm.TestBuriedTiles();
            ctx.Assert(buried.Count > 0, "no buried items to collect");

            int eggs = 0, fruit = 0, treasure = 0;
            for (int i = 0; i < buried.Count; i++)
            {
                switch (dm.TestBuriedType(buried[i]))
                {
                    case ItemType.Egg: eggs++; break;
                    case ItemType.Fruit: fruit++; break;
                    default: treasure++; break;
                }
            }

            int treasureBefore = gm.Save.Data.TreasureCount;
            int expectedPickups = eggs + fruit;

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
            yield return ctx.WaitUntil(() => gm.State.Is(GameState.Roam));

            // Non-treasure items become pickups; treasure auto-flies to the counter.
            // This window is after items spawn + treasures fly, but before eggs hatch.
            yield return ctx.WaitUntil(() =>
                CountOverworldPickups(gm, true) == expectedPickups &&
                gm.Save.Data.TreasureCount == treasureBefore + treasure);

            ctx.Log($"eggs={eggs} fruit={fruit} treasure={treasure}: {expectedPickups} pickups spawned, treasure+={treasure}");
            gm.TestReset();
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

            // ---- Full ownership: zero owned-species eggs, shards appear. ----
            gm.TestSpawnDino(DinoType.Brachiosaurus, GrowthStage.Baby);
            gm.TestSpawnDino(DinoType.Stegosaurus, GrowthStage.Baby);
            yield return ctx.WaitFrames(1);
            ctx.Assert(gm.TestEggSpeciesAllOwned, "all 4 egg species not owned after 4 spawns");

            int eggs = 0, shards = 0, total = 0;
            for (int round = 0; round < 50; round++) // renamed: `batch` list above shadows it
            {
                for (int i = 0; i < 4; i++) // ~a dig site's batch worth of items
                {
                    DugItemInfo info = gm.TestRollDugItem();
                    total++;
                    if (info.Type == ItemType.Egg) eggs++;
                    else if (info.Type == ItemType.Shard) shards++;
                }
            }

            ctx.Assert(eggs == 0, $"{eggs} owned-species eggs rolled after owning all 4 (must convert to shards)");
            ctx.Assert(shards > 0, "no egg shards appeared after owning every species");
            ctx.Log($"partial: {partialEggs} eggs (all unowned), 0 shards; full: 0 eggs, {shards}/{total} shards");
            gm.TestReset();
        }

        // Egg-shard nerf: once every egg species is owned, egg items collapse to at
        // most ~25% of their pre-nerf rate and the freed weight becomes egg shards.
        private IEnumerator Case_ShardDropRate(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();

            gm.TestSpawnDino(DinoType.TRex, GrowthStage.Baby);
            gm.TestSpawnDino(DinoType.Triceratops, GrowthStage.Baby);
            gm.TestSpawnDino(DinoType.Brachiosaurus, GrowthStage.Baby);
            gm.TestSpawnDino(DinoType.Stegosaurus, GrowthStage.Baby);
            yield return ctx.WaitFrames(1);
            ctx.Assert(gm.TestEggSpeciesAllOwned, "need all egg species owned for the shard nerf");

            GameConfig cfg = gm.TestConfig;
            float cfgTotal = Mathf.Max(0.0001f, cfg.EggWeight + cfg.FruitWeight + cfg.TreasureWeight);
            float preNerfEggFrac = cfg.EggWeight / cfgTotal;

            const int N = 3000;
            int eggs = 0, shards = 0;
            for (int i = 0; i < N; i++)
            {
                DugItemInfo info = gm.TestRollDugItem();
                if (info.Type == ItemType.Egg) eggs++;
                else if (info.Type == ItemType.Shard) shards++;
            }

            float eggFrac = eggs / (float)N;
            float shardFrac = shards / (float)N;

            ctx.Assert(eggFrac <= 0.25f * preNerfEggFrac + 0.001f,
                $"egg rate {eggFrac:F3} > 25% of pre-nerf {preNerfEggFrac:F3} after the shard nerf");
            ctx.Assert(shardFrac >= 0.5f * preNerfEggFrac,
                $"shard rate {shardFrac:F3} too low (expected ~{preNerfEggFrac:F3} of the freed egg weight)");
            ctx.Log($"all owned: eggFrac={eggFrac:F3} (<=25% of {preNerfEggFrac:F3}), shardFrac={shardFrac:F3}");
            gm.TestReset();
        }

        // Nest egg-assembly: banking shards advances the egg's assembly sprite index at
        // the 0/5/10/15/20 thresholds (per/4 step). Drives the REAL collect path via the
        // shard pickup hook; stays below ShardsPerHatch so the hatch ceremony never fires.
        private IEnumerator Case_NestAssembly(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            NestController nest = gm.TestNest;
            ctx.Assert(nest != null, "no NestController in the scene (rebuild via DinoDigger/Build Main Scene)");

            int per = gm.TestConfig.ShardsPerHatch;
            ctx.Assert(per > 0, "ShardsPerHatch must be positive");
            int step = Mathf.Max(1, per / 4); // 5 shards per assembly state

            // Baseline: 0 banked, collect one -> total 1 -> assembly index 0.
            gm.Save.Data.ShardCount = 0;
            yield return CollectOneShardTo(ctx, gm, 1);
            ctx.Assert(nest.TestAssemblyIndex == 0, $"assembly idx {nest.TestAssemblyIndex} != 0 at 1 shard");
            Sprite idx0Sprite = nest.TestEggSprite;

            // Advance to each threshold (5/10/15...) and confirm the index steps up 1/2/3.
            int lastIdx = 0;
            for (int i = 1; i <= 3; i++)
            {
                int target = step * i;
                if (target >= per)
                {
                    break; // never cross into ceremony territory
                }

                gm.Save.Data.ShardCount = target - 1;
                yield return CollectOneShardTo(ctx, gm, target);
                ctx.Assert(nest.TestAssemblyIndex == i,
                    $"assembly idx {nest.TestAssemblyIndex} != {i} at {target} shards");
                lastIdx = i;
            }

            ctx.Assert(lastIdx >= 1, "assembly index never advanced past 0");
            ctx.Assert(nest.TestEggSprite != idx0Sprite, "egg sprite did not change as shards assembled");
            ctx.Log($"nest assembly stepped 0..{lastIdx} across shard thresholds (step {step} of {per})");

            gm.TestReset();
            gm.Save.Data.ShardCount = 0;
            gm.TestNest?.RefreshAssembly(0);
        }

        // Full nest -> hatch ceremony: reaching ShardsPerHatch with a shard species still
        // unowned zooms to the nest, hatches a NEW shard-exclusive baby that waits there,
        // and tapping it promotes it to a buddy + ends the ceremony. With all 9 owned:
        // no ceremony and shards stop dropping.
        private IEnumerator Case_ShardHatchCeremony(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            ctx.Assert(gm.TestNest != null, "no NestController (rebuild via DinoDigger/Build Main Scene)");

            int per = gm.TestConfig.ShardsPerHatch;

            // ---- Run 1: a shard species is unowned -> ceremony fires. ----
            gm.Save.Data.ShardCount = per - 1;
            gm.TestNest.RefreshAssembly(gm.Save.Data.ShardCount);
            ctx.Assert(gm.TestAnyShardSpeciesUnowned, "no shard species unowned at test start");

            // Collect the final shard -> reaches `per` -> ceremony begins.
            Vector3 pos = WalkableNear(gm.TestMap, gm.TestBackhoe.transform.position);
            gm.TestSpawnItem(ItemType.Shard, DinoType.TRex, 0, pos);

            yield return ctx.WaitUntil(() => gm.State.Is(GameState.Ceremony));
            ctx.Assert(gm.TestCeremonyActive, "ceremony flag not set");

            // A new dino appears at the nest: shard-exclusive species, resident (not buddy).
            yield return ctx.WaitUntil(() => gm.TestCeremonyDino != null);
            DinoController baby = gm.TestCeremonyDino;
            ctx.Assert(baby != null, "no ceremony dino spawned");
            ctx.Assert(!DinoSpecies.IsEggHatchable(baby.Type),
                $"ceremony hatched a non-shard species {baby.Type}");
            ctx.Assert(!baby.IsBuddy, "ceremony dino should wait as a resident, not a buddy");

            // Shards consumed, remainder kept: (per-1) + 1 - per = 0.
            ctx.Assert(gm.Save.Data.ShardCount == 0,
                $"shard count {gm.Save.Data.ShardCount} not reduced to remainder 0 after the hatch");

            // Tap the baby: it joins the team, and the ceremony ends (camera back, Roam).
            DinoType hatched = baby.Type;
            Physics2D.SyncTransforms();
            gm.TestTapWorldRouted(baby.transform.position);
            yield return ctx.WaitUntil(() => baby.IsBuddy);
            yield return ctx.WaitUntil(() => gm.State.Is(GameState.Roam));
            ctx.Assert(!gm.TestCeremonyActive, "ceremony did not end after tap-to-join");

            // ---- Run 2: own all 9 species -> no ceremony, no shard drops. ----
            gm.TestReset();
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
            ctx.Assert(!gm.TestAnyShardSpeciesUnowned, "still reports a shard species unowned with all 9 owned");

            // Shards stop dropping: no rolled item resolves to a shard.
            int shardRolls = 0;
            for (int i = 0; i < 400; i++)
            {
                if (gm.TestRollDugItem().Type == ItemType.Shard)
                {
                    shardRolls++;
                }
            }

            ctx.Assert(shardRolls == 0, $"{shardRolls} shards still rolled after owning every species");

            // Even a forced shard collection to `per` must NOT start a ceremony.
            gm.Save.Data.ShardCount = per - 1;
            Vector3 pos2 = WalkableNear(gm.TestMap, gm.TestBackhoe.transform.position);
            gm.TestSpawnItem(ItemType.Shard, DinoType.TRex, 0, pos2);
            yield return ctx.WaitUntil(() => gm.Save.Data.ShardCount >= per);
            yield return ctx.WaitFrames(3);
            ctx.Assert(!gm.TestCeremonyActive && !gm.State.Is(GameState.Ceremony),
                "ceremony fired even though every shard species is owned");

            ctx.Log($"run1: {hatched} hatched at the nest + tap-joined; " +
                    $"run2 (all 9 owned): {shardRolls} shard rolls, no ceremony");
            gm.TestReset();
            gm.Save.Data.ShardCount = 0;
            gm.TestNest?.RefreshAssembly(0);
        }

        /// <summary>Spawn a shard pickup near the backhoe and wait until it flies to the
        /// nest and banks the count up to <paramref name="expectedTotal"/>.</summary>
        private IEnumerator CollectOneShardTo(TestContext ctx, GameManager gm, int expectedTotal)
        {
            Vector3 pos = WalkableNear(gm.TestMap, gm.TestBackhoe.transform.position);
            gm.TestSpawnItem(ItemType.Shard, DinoType.TRex, 0, pos);
            yield return ctx.WaitUntil(() => gm.Save.Data.ShardCount >= expectedTotal);
        }

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
            float baby = gm.TestConfig.StageScale(GrowthStage.Baby);
            ctx.Assert(Mathf.Abs(dino.transform.localScale.x - baby) < 0.05f,
                $"baby scale {dino.transform.localScale.x:F2} != config {baby:F2}");

            float kid = gm.TestConfig.StageScale(GrowthStage.Kid);
            dino.Feed();
            yield return ctx.WaitUntil(() => Mathf.Abs(dino.transform.localScale.x - kid) < 0.05f);
            ctx.Assert(dino.Stage == GrowthStage.Kid, $"stage {dino.Stage} not Kid after 2 fruit");

            float big = gm.TestConfig.StageScale(GrowthStage.Big);
            dino.Feed();
            yield return ctx.WaitFrames(1);
            dino.Feed();
            yield return ctx.WaitFrames(1);
            dino.Feed();
            yield return ctx.WaitUntil(() => Mathf.Abs(dino.transform.localScale.x - big) < 0.05f);
            ctx.Assert(dino.Stage == GrowthStage.Big, $"stage {dino.Stage} not Big after 5 fruit");

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

            int tileBefore = tile.TestDamage;
            int neighborBefore = NeighborDamageSum(dm, tile);
            ctx.TapWorld(tile.transform.position);
            yield return ctx.WaitUntil(() => tile.TestDamage > tileBefore);

            int neighborAfter = NeighborDamageSum(dm, tile);
            ctx.Assert(tile.TestDamage >= tileBefore + 1, "tapped tile not damaged");
            ctx.Assert(neighborAfter >= neighborBefore + 1, "helper did not also damage an adjacent tile");
            ctx.Log($"helper enabled; tap damaged tile + adjacent (neighborSum {neighborBefore}->{neighborAfter})");
            gm.TestForceRoam();
        }

        // ============================================================ TREASURE / UI

        private IEnumerator Case_TreasureCounter(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();

            int before = gm.Save.Data.TreasureCount;
            Vector3 pos = WalkableNear(gm.TestMap, gm.TestBackhoe.transform.position + new Vector3(0.6f, 0.6f, 0f));
            gm.TestSpawnItem(ItemType.Treasure, DinoType.TRex, 0, pos);

            yield return ctx.WaitUntil(() => gm.Save.Data.TreasureCount == before + 1);

            var counter = gm.TestTreasureCounter;
            ctx.Assert(counter != null, "no treasure counter");
            ctx.Assert(counter.TestCount == gm.Save.Data.TreasureCount,
                $"counter {counter.TestCount} != save {gm.Save.Data.TreasureCount}");
            ctx.Assert(counter.TestCountText == gm.Save.Data.TreasureCount.ToString(),
                $"counter text '{counter.TestCountText}' != {gm.Save.Data.TreasureCount}");
            ctx.Log($"treasure {before}->{gm.Save.Data.TreasureCount}, UI text '{counter.TestCountText}'");
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
                sm.Data.ShardCount = 77;
                sm.Data.Dinos.Clear();
                sm.Data.Dinos.Add(new DinoSave { Type = DinoType.Stegosaurus, Stage = GrowthStage.Kid, FruitEaten = 3 });
                sm.Save();

                // Mutate in memory, then reload from disk.
                sm.Data.TreasureCount = 0;
                sm.Data.ShardCount = 0;
                sm.Data.Dinos.Clear();
                sm.Load();

                ctx.Assert(sm.Data.TreasureCount == 4242, $"treasure not restored ({sm.Data.TreasureCount})");
                ctx.Assert(sm.Data.ShardCount == 77, $"shard count not restored ({sm.Data.ShardCount})");
                ctx.Assert(sm.Data.Version == SaveData.CurrentVersion, $"save version {sm.Data.Version} != {SaveData.CurrentVersion}");
                ctx.Assert(sm.Data.Dinos.Count == 1, $"dino count {sm.Data.Dinos.Count} != 1");
                DinoSave d = sm.Data.Dinos[0];
                ctx.Assert(d.Type == DinoType.Stegosaurus && d.Stage == GrowthStage.Kid && d.FruitEaten == 3,
                    $"dino fields not restored ({d.Type}/{d.Stage}/{d.FruitEaten})");

                // v2 -> v3 migration: a save written before ShardCount/NestSpeciesQueue
                // existed must load cleanly with those fields at their defaults.
                File.WriteAllText(path,
                    "{\"Version\":2,\"TreasureCount\":11,\"Dinos\":[],\"ParadeDone\":true}");
                sm.Load();
                ctx.Assert(sm.Data.TreasureCount == 11 && sm.Data.ParadeDone,
                    "v2 fields lost on migration");
                ctx.Assert(sm.Data.ShardCount == 0, $"migrated v2 save should default ShardCount=0 (got {sm.Data.ShardCount})");
                ctx.Assert(sm.Data.NestSpeciesQueue != null, "migrated v2 save left NestSpeciesQueue null");
                ctx.Log("save roundtrip (treasure=4242, shards=77, v3) + v2 migrates with ShardCount defaulting to 0");
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
                    FindAnyClearCardinalTarget(map, gm, bh.transform.position, out Vector3 next))
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

            // Stand the Brachio near the tree (within the ~3-unit power range) but
            // far enough that its scaled tap collider can't swallow the tree tap.
            Vector3 beside = WalkableNear(gm.TestMap, treeWorld + new Vector3(1.6f, -0.8f, 0f));
            if ((beside - treeWorld).magnitude > 2.9f)
            {
                beside = WalkableNear(gm.TestMap, treeWorld + new Vector3(0f, -1.3f, 0f));
            }

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

            gm.TestTapWorldRouted(duck.transform.position);
            ctx.Assert(duck.TestCaught, "tapping the duck did not catch it");

            // A reward appears: a lingering fruit pickup, or a treasure that flew to
            // the counter (auto-collect bumps the treasure count).
            yield return ctx.WaitUntil(() =>
                CountOverworldPickups(gm, false) > pickupsBefore ||
                gm.Save.Data.TreasureCount > treasureBefore);

            // The caught duck flaps away and despawns.
            yield return ctx.WaitUntil(() => duck == null);

            bool rewarded = CountOverworldPickups(gm, false) > pickupsBefore ||
                            gm.Save.Data.TreasureCount > treasureBefore;
            ctx.Assert(rewarded, "no fruit/treasure reward left where the duck was caught");

            ctx.Log("tapped a duck: it quacked, flapped away (despawned), and left a reward");
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

        private IEnumerator EnterDig(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            DigMound m = FirstActiveMound(gm);
            ctx.Assert(m != null, "no active mound to dig");
            // On the 48x48 island the nearest mound is usually OFF-SCREEN, and
            // TapWorld's world->screen conversion drops off-screen taps. Use the
            // same code path a mound tap invokes; the tap->collider routing itself
            // is covered by MoundToDig (which walks into view first).
            gm.TestBackhoe.DriveToMound(m);
            yield return ctx.WaitUntil(() => gm.State.Is(GameState.Dig));
            yield return ctx.WaitUntil(() => gm.TestDigMode.TestTileCount > 0);
        }

        private IEnumerator TapTileUntilDestroyed(TestContext ctx, DigModeController dm, DirtTile tile)
        {
            int guard = 0;
            while (tile != null && !tile.IsDestroyed && dm.IsOpen && guard++ < 12)
            {
                int before = tile.TestDamage;
                ctx.TapWorld(tile.transform.position);
                yield return ctx.WaitUntil(() => tile == null || tile.IsDestroyed || tile.TestDamage > before || !dm.IsOpen);
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

        private bool FindBlockedPondCell(OverworldMap map, out Vector3Int cell)
        {
            // Scan the handcrafted pond region and return an interior water cell
            // (a blocked cell that borders at least one walkable cell).
            int[] dx = { -1, 1, 0, 0 };
            int[] dy = { 0, 0, -1, 1 };

            for (int x = 4; x <= 11; x++)
            {
                for (int y = 13; y <= 19; y++)
                {
                    var c = new Vector3Int(x, y, 0);
                    if (map.IsWalkableCell(c))
                    {
                        continue;
                    }

                    for (int i = 0; i < 4; i++)
                    {
                        if (map.IsWalkableCell(new Vector3Int(x + dx[i], y + dy[i], 0)))
                        {
                            cell = c;
                            return true;
                        }
                    }
                }
            }

            cell = Vector3Int.zero;
            return false;
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

        private DirtTile FindPlainTile(DigModeController dm)
        {
            DirtTile mid = dm.TestTileAt(0, dm.TestCols / 2);
            if (mid != null && !mid.HasItem && !mid.IsDestroyed)
            {
                return mid;
            }

            IReadOnlyList<DirtTile> tiles = dm.TestTiles;
            for (int i = 0; i < tiles.Count; i++)
            {
                DirtTile t = tiles[i];
                if (t != null && !t.HasItem && !t.IsDestroyed)
                {
                    return t;
                }
            }

            return null;
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

        private int NeighborDamageSum(DigModeController dm, DirtTile tile)
        {
            int[] dr = { -1, 1, 0, 0 };
            int[] dc = { 0, 0, -1, 1 };
            int sum = 0;
            for (int i = 0; i < 4; i++)
            {
                DirtTile t = dm.TestTileAt(tile.Row + dr[i], tile.Col + dc[i]);
                if (t != null)
                {
                    sum += t.TestDamage;
                }
            }

            return sum;
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
