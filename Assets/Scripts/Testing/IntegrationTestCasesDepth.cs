using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;
using DinoDigger.Dig;
using DinoDigger.Overworld;

namespace DinoDigger.Testing
{
    /// <summary>
    /// DIG LOOP 2.0, PHASE D3 — the integration cases for depth layers (DinoDigger-dv1), the
    /// mega-fossil site (DinoDigger-84f), the wave-2 toys (DinoDigger-u47) and Glow the lantern
    /// bot (DinoDigger-6tc).
    ///
    /// Every case here drives site generation directly through the off-screen build hooks
    /// (<c>TestBuildThemedSite</c> / <c>TestBuildMegaSite</c>) rather than walking the island to
    /// a mound: a board costs a frame instead of a drive, which is what lets these assert exact
    /// cell configurations instead of hoping a rolled site happens to contain one.
    ///
    /// NOT ONE OF THEM WAITS ON A WALL CLOCK for a decision. Where a beat is genuinely timed (a
    /// critter's scurry, its ten seconds of life) the case waits on the COUNTER the beat
    /// increments, with a named budget — so a slow frame makes a case slower, never redder.
    ///
    /// Registered from IntegrationTestCases.BuildCases; see IntegrationTestRunner.cs for the
    /// driver and its between-case pin backstop (which clears the two pins added by this wave,
    /// TestSuppressLadder and TestSuppressGlow).
    /// </summary>
    public partial class IntegrationTestRunner
    {
        // ==================================================== DEPTH LAYERS (dv1)

        /// <summary>
        /// THE LADDER DOWN, end to end: it is not there at the start, it appears exactly when
        /// the layer has been dug out past the configured threshold, tapping it takes the child
        /// one stratum deeper on a REBUILT board, and the bottom of the ladder is the bottom —
        /// a second layer offers no third one.
        ///
        /// The board is dug from the TOP of each column down, which is deliberate: clearing a
        /// cell with nothing above it moves no tiles at all, so the cleared fraction this case
        /// asserts against is exactly the number of clears it made, with no cascade in the
        /// middle quietly clearing more.
        /// </summary>
        private IEnumerator Case_LadderDescends(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            DigModeController dm = gm.TestDigMode;
            ctx.Assert(dm != null, "no dig controller");

            float cfgFraction = gm.TestConfig != null ? gm.TestConfig.DigLadderRevealFraction : 0.6f;
            int cfgLayers = gm.TestConfig != null ? gm.TestConfig.DigDepthLayers : 2;

            try
            {
                gm.TestReset();
                DigModeController.TestSuppressCrew = true;   // no superpower clearing tiles for us
                // Pin the surprise pocket to the harmless GIGGLE: a rolled Rainbow Geode
                // pocket detonates a 3x3 the moment this case's digging happens to crack
                // it, which would clear tiles (and uncover items) behind the assertions.
                DigModeController.TestForceSurpriseKind = 0;
                DigModeController.TestSuppressToys = true;   // no geode/blob clearing them either
                DigModeController.TestSuppressBones = true;
                DigModeController.TestSuppressGlow = true;   // Glow's beam is GlowRevealsAdjacent's

                // A lower threshold than the shipped 0.6 so the case never has to dig so far that
                // it uncovers the site's last buried item (which would end the round before the
                // ladder could be earned). The RULE is what is under test, not the number — and
                // the number it asserts against is read back from config, not hard-coded.
                // ...and the shipped TWO-layer ladder, pinned, so "the bottom is the bottom"
                // below is asserting the rule rather than whatever the config asset happens to
                // hold. (Both knobs are restored in the finally.)
                if (gm.TestConfig != null)
                {
                    gm.TestConfig.DigLadderRevealFraction = 0.3f;
                    gm.TestConfig.DigDepthLayers = 2;
                }

                float threshold = gm.TestConfig != null ? gm.TestConfig.DigLadderRevealFraction : 0.3f;
                ctx.Assert(dm.TestMaxLayers == 2,
                    $"the site reports {dm.TestMaxLayers} strata, this case pinned 2");

                dm.TestBuildThemedSite(null);
                yield return ctx.WaitFrames(1);

                ctx.Assert(dm.TestLayer == 0, $"a fresh site opened on layer {dm.TestLayer}");
                ctx.Assert(!dm.TestLadderShown, "the ladder is already offered on an untouched board");
                ctx.Assert(dm.TestClearedFraction < 0.001f, "a fresh board reports cleared tiles");
                ctx.Assert(dm.TestBuriedCount >= 2,
                    $"site buried only {dm.TestBuriedCount} item(s) — this case needs a spare so " +
                    "the round cannot end while it digs");

                int tiles = dm.TestTileCount;
                int guard = 0;
                while (dm.IsOpen && !dm.TestLadderShown && guard++ < tiles + 4)
                {
                    // Below the threshold the ladder must stay away: a ladder that turned up
                    // early would be a way out of a dig the child has barely started.
                    if (dm.TestClearedFraction < threshold)
                    {
                        ctx.Assert(!dm.TestLadderShown,
                            $"the ladder appeared at {dm.TestClearedFraction:F2} cleared, under the " +
                            $"{threshold:F2} threshold");
                    }

                    ctx.Assert(ClearTopmostPlainTile(dm),
                        $"ran out of item-free tiles to clear at {dm.TestClearedFraction:F2} cleared");
                    yield return ctx.WaitFrames(1);
                }

                ctx.Assert(dm.IsOpen, "the round ended before the ladder could be earned");
                ctx.Assert(dm.TestLadderShown,
                    $"no ladder after clearing {dm.TestClearedFraction:F2} of the layer " +
                    $"(threshold {threshold:F2})");
                ctx.Assert(dm.TestClearedFraction >= threshold,
                    $"the ladder appeared at {dm.TestClearedFraction:F2} cleared, under the threshold");

                float clearedAtReveal = dm.TestClearedFraction;
                int buriedBefore = dm.TestBuriedCount;

                // ---- Down we go ----
                dm.TestDescend();
                yield return ctx.WaitFrames(2);

                ctx.Assert(dm.TestLayer == 1, $"tapping the ladder left the site on layer {dm.TestLayer}");
                ctx.Assert(dm.TestDescents == 1, $"{dm.TestDescents} descents recorded for one ladder");

                // ONE LADDER, ONE DESCENT — asserted directly rather than inferred, because the
                // first gate run produced two. A second request from the same layer (a re-tap
                // during the camera dip, a stray tap on a collider that outlived its Destroy, a
                // hook called twice) must be nothing at all.
                dm.TestDescend();
                dm.TestDescend();
                yield return ctx.WaitFrames(2);
                ctx.Assert(dm.TestDescents == 1,
                    $"repeat descend requests took the site down {dm.TestDescents} times — a " +
                    "descent must be a one-way door out of a layer");
                ctx.Assert(dm.TestLayer == 1,
                    $"repeat descend requests pushed the site to layer {dm.TestLayer}");
                ctx.Assert(dm.IsOpen, "the site closed on the way down");
                ctx.Assert(!dm.TestLadderShown, "the ladder is still standing after it was taken");
                ctx.Assert(dm.TestTileCount == tiles,
                    $"the deeper layer built {dm.TestTileCount} tiles, the layer above had {tiles}");
                ctx.Assert(dm.TestClearedFraction < 0.001f,
                    "the deeper layer opened partly dug — it must be a whole fresh board");
                ctx.Assert(dm.TestBuriedCount >= 1,
                    "the deeper layer buried nothing at all — descending must never be a dead end");

                // ---- The bottom is the bottom: dig the deep layer out, no third ladder ----
                guard = 0;
                while (dm.IsOpen && guard++ < tiles + 4 && dm.TestClearedFraction < threshold + 0.15f)
                {
                    if (!ClearTopmostPlainTile(dm))
                    {
                        break;
                    }

                    yield return ctx.WaitFrames(1);
                }

                ctx.Assert(!dm.TestLadderShown,
                    "a ladder was offered on the DEEPEST layer — two strata is the whole ladder");

                // ---- Exiting from a deep layer works exactly as it always has ----
                gm.TestForceRoam();
                yield return ctx.WaitFrames(1);
                ctx.Assert(!dm.IsOpen, "leaving the dig from the deep layer did not close the site");
                ctx.Assert(dm.TestLayer == 0, "a closed site did not reset to the surface layer");

                ctx.Log($"ladder appeared at {clearedAtReveal:F2} cleared (threshold {threshold:F2}) " +
                        $"with {buriedBefore} items still buried; the descent rebuilt a full " +
                        $"{tiles}-tile board on layer 1, offered no third ladder, and exiting " +
                        "from the deep layer closed the site cleanly");
            }
            finally
            {
                if (gm.TestConfig != null)
                {
                    gm.TestConfig.DigLadderRevealFraction = cfgFraction;
                    gm.TestConfig.DigDepthLayers = cfgLayers;
                }

                DigModeController.TestSuppressCrew = false;
                DigModeController.TestForceSurpriseKind = -1;
                DigModeController.TestSuppressToys = false;
                DigModeController.TestSuppressBones = false;
                DigModeController.TestSuppressGlow = false;
                gm.TestForceRoam();
            }
        }

        /// <summary>
        /// DEEPER IS RICHER, and every claim is checked against the CONFIG that makes it rather
        /// than against a number typed twice:
        ///   hardness  — every DIRT tile carries exactly DigDeepHardnessBonus more taps;
        ///   look      — the dirt AND the backdrop are darker by the configured multiply;
        ///   payouts   — the toy coin multiplier and the loot table's treasure weight scale by
        ///               their configured factors;
        ///   toys      — the deep layer rolls more crystal clusters; and
        ///   variety   — the featured-toy guarantee applies PER LAYER, and the deep layer
        ///               refuses to lead with the treat the layer above led with.
        ///
        /// TWO PASSES, and the split is the point. HARDNESS IS A DIRT RULE: a toy seats its own
        /// hardness in DirtTile.SetKind (a crystal pops in one hit however deep it is buried —
        /// depth must never make a toy harder to enjoy), so a whole-board sum moves with the
        /// board's COMPOSITION as well as with the bonus, and a flat "sum + bonus x tiles"
        /// expectation is simply wrong on a board with toys on it (that is what failed the first
        /// gate run: 108 actual vs a naive 102, because the deep layer's extra crystal cluster
        /// re-seated several tiles at 1). So:
        ///
        ///   PASS A — toys SUPPRESSED. Every tile is dirt, so the arithmetic is exact and the
        ///            depth rule is isolated from everything that could confound it.
        ///   PASS B — toys LIVE. The rule is asserted PER KIND instead: dirt carries the bonus,
        ///            every toy still seats its own value. This is also where the per-layer
        ///            featured-toy guarantee is checked, which needs the roller running.
        ///
        /// Counter- and config-driven throughout: nothing here waits on a clock.
        /// </summary>
        private IEnumerator Case_DeeperLayerRicher(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            DigModeController dm = gm.TestDigMode;
            ctx.Assert(dm != null, "no dig controller");

            try
            {
                gm.TestReset();
                DigModeController.TestSuppressCrew = true;
                DigModeController.TestForceSurpriseKind = 0;   // giggle, never a geode (see above)
                DigModeController.TestSuppressBones = true;
                DigModeController.TestSuppressGlow = true;
                DigModeController.TestResetPrimaryToy(); // a known "no history" for the roller

                GameConfig cfg = gm.TestConfig;
                int hardnessBonus = cfg != null ? Mathf.Max(0, cfg.DigDeepHardnessBonus) : 1;
                float coinFactor = cfg != null ? Mathf.Max(1f, cfg.DigDeepCoinMultiplier) : 2f;
                float treasureFactor = cfg != null ? Mathf.Max(1f, cfg.DigDeepTreasureWeightMultiplier) : 2f;
                int clusterBonus = cfg != null ? Mathf.Max(0, cfg.DigDeepCrystalClusterBonus) : 1;

                // ================= PASS A: toys off, the depth rule in isolation =========
                DigModeController.TestSuppressToys = true;

                dm.TestBuildThemedSite(null);
                yield return ctx.WaitFrames(1);

                ctx.Assert(dm.TestLayer == 0, "the baseline site is not on the surface layer");
                ctx.Assert(Mathf.Approximately(dm.TestLayerCoinMultiplier, 1f),
                    $"the surface layer multiplies toy coins by {dm.TestLayerCoinMultiplier}");
                ctx.Assert(dm.TestLayerHardnessBonus == 0,
                    $"the surface layer adds {dm.TestLayerHardnessBonus} hardness");

                int tiles = dm.TestTileCount;
                ctx.Assert(dm.TestDirtTileCount == tiles,
                    $"{tiles - dm.TestDirtTileCount} non-dirt cells on a toy-suppressed board");

                int shallowHardness = dm.TestDirtHardnessSum;
                int baseHardness = cfg != null ? cfg.DirtHealth : 3;
                ctx.Assert(shallowHardness == baseHardness * tiles,
                    $"an unthemed surface board sums {shallowHardness} hardness across {tiles} " +
                    $"tiles; every tile should roll the flat DirtHealth {baseHardness}");

                Color shallowBg = dm.TestBackgroundColor;
                Color shallowDirt = FirstPlainDirtColor(dm);

                // ---- One stratum down ----
                dm.TestDescend();
                yield return ctx.WaitFrames(2);
                ctx.Assert(dm.TestLayer == 1, "the descent did not reach layer 1");
                ctx.Assert(dm.TestTileCount == tiles, "the deep layer built a different-sized board");
                ctx.Assert(dm.TestDirtTileCount == tiles, "the deep toy-suppressed board grew a toy");

                // HARDNESS: exactly one bonus per tile, with nothing else on the board to move
                // the number. The site is unthemed, so this is arithmetic, not a range.
                int deepHardness = dm.TestDirtHardnessSum;
                ctx.Assert(dm.TestLayerHardnessBonus == hardnessBonus,
                    $"layer 1 reports a hardness bonus of {dm.TestLayerHardnessBonus}, config says {hardnessBonus}");
                ctx.Assert(deepHardness == shallowHardness + hardnessBonus * tiles,
                    $"deep board sums {deepHardness} hardness; expected {shallowHardness} + " +
                    $"{hardnessBonus}x{tiles} = {shallowHardness + hardnessBonus * tiles}");
                ctx.Assert(AllDirtTilesAt(dm, baseHardness + hardnessBonus, out string offender),
                    $"a deep dirt tile does not carry exactly the layer bonus: {offender}");

                // LOOK: both the dirt and the backdrop darken.
                Color deepBg = dm.TestBackgroundColor;
                Color deepDirt = FirstPlainDirtColor(dm);
                ctx.Assert(deepBg.grayscale < shallowBg.grayscale - 0.02f,
                    $"the deep backdrop ({deepBg.grayscale:F2}) is not darker than the surface " +
                    $"one ({shallowBg.grayscale:F2})");
                ctx.Assert(deepDirt.grayscale < shallowDirt.grayscale - 0.02f,
                    $"deep dirt ({deepDirt.grayscale:F2}) is not darker than surface dirt " +
                    $"({shallowDirt.grayscale:F2})");

                // PAYOUTS + GENERATION: the multipliers the site actually reads.
                ctx.Assert(Mathf.Abs(dm.TestLayerCoinMultiplier - coinFactor) < 0.01f,
                    $"layer 1 multiplies toy coins by {dm.TestLayerCoinMultiplier}, config says {coinFactor}");
                ctx.Assert(Mathf.Abs(dm.TestLayerTreasureWeight - treasureFactor) < 0.01f,
                    $"layer 1 multiplies the treasure weight by {dm.TestLayerTreasureWeight}, " +
                    $"config says {treasureFactor}");
                ctx.Assert(dm.TestLayerCrystalBonus == clusterBonus,
                    $"layer 1 adds {dm.TestLayerCrystalBonus} crystal clusters, config says {clusterBonus}");
                ctx.Assert(dm.TestLayerBoneChanceBonus > 0f,
                    "the deep layer adds nothing to the bone chance — bones are its headline");

                gm.TestForceRoam();
                yield return ctx.WaitFrames(1);

                // ================= PASS B: toys live, the rule PER KIND =================
                DigModeController.TestSuppressToys = false;
                DigModeController.TestResetPrimaryToy();

                dm.TestBuildThemedSite(null);
                yield return ctx.WaitFrames(1);

                int shallowFeature = dm.TestPrimaryToy;
                ctx.Assert(shallowFeature >= 0, "the surface layer featured no toy at all");
                ctx.Assert(KindHardnessHolds(dm, baseHardness, 0, out string shallowBad),
                    $"surface board: {shallowBad}");

                dm.TestDescend();
                yield return ctx.WaitFrames(2);
                ctx.Assert(dm.TestLayer == 1, "the second descent did not reach layer 1");

                // A DIRT tile carries the bonus; a TOY still seats its own hardness, because a
                // crystal that got harder to pop the deeper you went would be the game taking a
                // toy away rather than giving one.
                ctx.Assert(KindHardnessHolds(dm, baseHardness, hardnessBonus, out string deepBad),
                    $"deep board: {deepBad}");

                // VARIETY: the guarantee holds per LAYER, and the deep draw refuses to repeat.
                int deepFeature = dm.TestPrimaryToy;
                ctx.Assert(deepFeature >= 0, "the deep layer featured no toy at all");
                ctx.Assert(deepFeature != shallowFeature,
                    $"the deep layer led with feature {deepFeature} again — a descent must " +
                    "re-roll the feature AND refuse the layer above's");

                ctx.Log($"toy-free pass: dirt hardness {shallowHardness}->{deepHardness} " +
                        $"(+{hardnessBonus}/tile across {tiles}); backdrop " +
                        $"{shallowBg.grayscale:F2}->{deepBg.grayscale:F2}; coins x{coinFactor}, " +
                        $"treasure weight x{treasureFactor}, +{clusterBonus} clusters. " +
                        $"Toys-live pass: dirt carried the bonus and every toy kept its own " +
                        $"hardness; feature {shallowFeature}->{deepFeature}");
            }
            finally
            {
                DigModeController.TestSuppressCrew = false;
                DigModeController.TestForceSurpriseKind = -1;
                DigModeController.TestSuppressToys = false;
                DigModeController.TestSuppressBones = false;
                DigModeController.TestSuppressGlow = false;
                DigModeController.TestResetPrimaryToy();
                gm.TestForceRoam();
            }
        }

        // ================================================== WAVE 2 TOYS (u47)

        /// <summary>
        /// THE WATER POCKET: cracking it washes the remaining hardness off the column below and
        /// floats buried loot one row up. Both halves are asserted on the CELLS themselves —
        /// hits-remaining before and after, and the identity of the tile the item is riding —
        /// rather than on the counters alone, because a counter that ticks while nothing moved
        /// would be a passing test of nothing.
        /// </summary>
        private IEnumerator Case_WaterPocketWashes(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            DigModeController dm = gm.TestDigMode;
            ctx.Assert(dm != null, "no dig controller");

            try
            {
                gm.TestReset();
                DigModeController.TestSuppressCrew = true;
                DigModeController.TestForceSurpriseKind = 0;   // giggle, never a geode (see above)
                DigModeController.TestSuppressToys = true;   // the pocket under test is hand-placed
                DigModeController.TestSuppressBones = true;
                DigModeController.TestSuppressLadder = true;
                DigModeController.TestSuppressGlow = true;

                dm.TestBuildThemedSite(null);
                yield return ctx.WaitFrames(1);

                // THE WHOLE COLUMN, not just the three cells this phase plants in. A gush lifts
                // EVERY item in the column, so a site-generated item sitting below the planted
                // one would legitimately rise into the cell the planted one vacated — which is
                // the chain working, but it makes "the origin is empty afterwards" an untrue
                // assertion. Phase 2 below tests that chain on purpose; phase 1 excludes it so
                // its own assertions stay exact.
                ctx.Assert(FindCleanColumn(dm, 1, dm.TestRows - 1, out int col),
                    "no column clean from row 1 to the pit floor to run a gush down");

                DirtTile below = dm.TestTileAt(2, col);
                DirtTile bottom = dm.TestTileAt(3, col);
                ctx.Assert(below != null && bottom != null, "the clean column lost its tiles");

                int belowHitsBefore = dm.TestHitsRemaining(2, col);
                ctx.Assert(belowHitsBefore > 1,
                    $"the tile below the pocket only needs {belowHitsBefore} hit — nothing to wash off");

                ctx.Assert(dm.TestBuryItemAt(3, col, ItemType.Treasure, 0),
                    $"could not bury a test item at r3c{col}");
                ctx.Assert(bottom.HasItem, "the buried item did not land on the bottom tile");
                ctx.Assert(!below.HasItem, "the tile the item must float onto already hides one");

                ctx.Assert(dm.TestSetWater(1, col), $"could not place a water pocket at r1c{col}");
                ctx.Assert(dm.TestKindAt(1, col) == DigTileKind.Water, "the water pocket did not take");

                int buriedCountBefore = dm.TestBuriedCount;

                // ---- Crack it ----
                dm.TestClearCell(1, col);
                yield return ctx.WaitFrames(1);

                ctx.Assert(dm.TestWaterGushes == 1, $"{dm.TestWaterGushes} gushes fired for one pocket");
                ctx.Assert(dm.TestTilesWashed >= 1,
                    "the gush washed no tiles at all — the column below it is untouched");

                // WATER SOFTENS, IT NEVER DIGS — and the trap here is not the wash itself (which
                // floors at one remaining hit) but the gush's OWN collapse: the column above
                // falls into the hole the pocket left and lands on the tile that was just
                // softened. Until the landing crack learned to skip a washed tile, that finished
                // it, and the water dug for the child.
                ctx.Assert(!below.IsDestroyed, "the gush DESTROYED a tile — water softens, it never digs");
                ctx.Assert(below.TestHitsRemaining == 1,
                    $"the washed tile still needs {below.TestHitsRemaining} hits (expected 1, " +
                    $"it needed {belowHitsBefore} before the gush)");
                ctx.Assert(below.IsWashed,
                    "the softened tile is not flagged as washed, so the cascade may still finish it");

                ctx.Assert(dm.TestItemsFloated >= 1, "the gush floated no buried item");
                List<DirtTile> buriedNow = dm.TestBuriedTiles();
                ctx.Assert(buriedNow.Contains(below),
                    "the buried item did not float onto the tile above it");

                // A MOVE, NOT A COPY. With the column clean below the planted item there is
                // nothing that could legitimately rise into the cell it left, so the origin must
                // be empty on BOTH books — the map and the tile's own flag.
                ctx.Assert(!buriedNow.Contains(bottom) && !bottom.HasItem,
                    $"the item is still ALSO on the tile it floated off — the bookkeeping split " +
                    $"(origin r{bottom.Row}c{bottom.Col}: map={buriedNow.Contains(bottom)} " +
                    $"HasItem={bottom.HasItem}; destination r{below.Row}c{below.Col}: " +
                    $"map={buriedNow.Contains(below)} HasItem={below.HasItem})");
                ctx.Assert(buriedCountBefore == dm.TestBuriedCount,
                    $"the gush changed the buried-item count ({buriedCountBefore} -> " +
                    $"{dm.TestBuriedCount}) — a float relocates loot, it never creates or loses it");
                ctx.Assert(below.TestPeekEnabled,
                    "the floated item's peek did not travel with it (the hint and the map disagree)");

                ctx.Assert(dm.TestFloaterReport() == "",
                    $"board not settled after the gush: {dm.TestFloaterReport()}");

                // ...and THE CHILD GETS THE LAST TAP. One bite finishes what the water started,
                // which is the other half of the same promise: softening is a gift, not a theft.
                yield return TapTileUntilDestroyed(ctx, dm, below);
                yield return ctx.WaitFrames(2);
                ctx.Assert(below.IsDestroyed,
                    "the washed tile survived the bite the wash left it needing");

                int phase1Washed = dm.TestTilesWashed;
                int phase1Floated = dm.TestItemsFloated;

                // ================== PHASE 2: A WHOLE COLUMN OF LOOT RISES ==================
                // The branch that failed a gate run: a second item BELOW the first rises into the
                // cell the first just left. Nothing is duplicated — each item moves exactly one
                // row, exactly once — and this proves it by IDENTITY (distinct treasure variants)
                // rather than by presence, plus the conservation invariant on the map's size.
                gm.TestForceRoam();
                yield return ctx.WaitFrames(1);
                dm.TestBuildThemedSite(null);
                yield return ctx.WaitFrames(1);

                ctx.Assert(FindCleanColumn(dm, 1, dm.TestRows - 1, out int chainCol),
                    "no clean column for the chain phase");

                DirtTile top = dm.TestTileAt(2, chainCol);
                DirtTile mid = dm.TestTileAt(3, chainCol);
                DirtTile low = dm.TestTileAt(4, chainCol);
                ctx.Assert(top != null && mid != null && low != null,
                    "the chain column is not four cells deep");

                const int upperVariant = 0; // coin
                const int lowerVariant = 1; // gem — a different face, so identity is provable
                ctx.Assert(dm.TestBuryItemAt(3, chainCol, ItemType.Treasure, upperVariant),
                    $"could not bury the upper chain item at r3c{chainCol}");
                ctx.Assert(dm.TestBuryItemAt(4, chainCol, ItemType.Treasure, lowerVariant),
                    $"could not bury the lower chain item at r4c{chainCol}");
                ctx.Assert(dm.TestSetWater(1, chainCol),
                    $"could not place the chain phase's water pocket at r1c{chainCol}");

                int chainBuriedBefore = dm.TestBuriedCount;
                int floatedBefore = dm.TestItemsFloated;

                dm.TestClearCell(1, chainCol);
                yield return ctx.WaitFrames(1);

                ctx.Assert(dm.TestItemsFloated - floatedBefore == 2,
                    $"the gush floated {dm.TestItemsFloated - floatedBefore} items up a column " +
                    "holding two — every item in the column rises, exactly once");
                ctx.Assert(dm.TestBuriedCount == chainBuriedBefore,
                    $"the chain changed the buried-item count ({chainBuriedBefore} -> " +
                    $"{dm.TestBuriedCount}) — moving a column of loot must conserve it exactly");

                List<DirtTile> chainNow = dm.TestBuriedTiles();
                ctx.Assert(chainNow.Contains(top) && dm.TestBuriedVariant(top) == upperVariant,
                    $"the upper item did not land on r{top.Row}c{top.Col} " +
                    $"(it carries variant {dm.TestBuriedVariant(top)})");

                // THE BRANCH ITSELF: the cell the upper item vacated is refilled from below, and
                // by the LOWER item specifically — not by a ghost of the one that left.
                ctx.Assert(chainNow.Contains(mid) && dm.TestBuriedVariant(mid) == lowerVariant,
                    $"the lower item did not rise into the cell the upper one left " +
                    $"(r{mid.Row}c{mid.Col} carries variant {dm.TestBuriedVariant(mid)})");
                ctx.Assert(!chainNow.Contains(low) && !low.HasItem,
                    $"the bottom cell r{low.Row}c{low.Col} still carries an item after its loot " +
                    "floated away — the bookkeeping split");
                ctx.Assert(top.TestPeekEnabled && mid.TestPeekEnabled,
                    "a risen item's peek did not travel with it");

                ctx.Log($"water pocket at r1c{col}: washed {phase1Washed} tiles " +
                        $"({belowHitsBefore}->1 hits on the one below), floated " +
                        $"{phase1Floated} item(s) up a row, board settled. Chain at c{chainCol}: " +
                        "two stacked items each rose exactly one row (coin then gem, in that " +
                        "order), the vacated cell refilled from below, and the buried count " +
                        $"held at {chainBuriedBefore}");
            }
            finally
            {
                DigModeController.TestSuppressCrew = false;
                DigModeController.TestForceSurpriseKind = -1;
                DigModeController.TestSuppressToys = false;
                DigModeController.TestSuppressBones = false;
                DigModeController.TestSuppressLadder = false;
                DigModeController.TestSuppressGlow = false;
                gm.TestForceRoam();
            }
        }

        /// <summary>
        /// THE DIG CRITTER: it scurries, a tap catches it for coins, and an uncaught one burrows
        /// away on its own. Threaded through it all, the property that makes a moving object safe
        /// inside a modal pit — IT BLOCKS NOTHING: the tile it is standing on is still there, the
        /// board is still settled, and the round is not waiting on it.
        /// </summary>
        private IEnumerator Case_CritterCatchable(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            DigModeController dm = gm.TestDigMode;
            ctx.Assert(dm != null, "no dig controller");

            try
            {
                gm.TestReset();
                DigModeController.TestSuppressCrew = true;
                DigModeController.TestForceSurpriseKind = 0;   // giggle, never a geode (see above)
                DigModeController.TestSuppressToys = true;   // no rolled critters competing
                DigModeController.TestSuppressBones = true;
                DigModeController.TestSuppressLadder = true;
                DigModeController.TestSuppressGlow = true;

                dm.TestBuildThemedSite(null);
                yield return ctx.WaitFrames(1);
                ctx.Assert(dm.TestCritterCount == 0, "a critter is loose before one was spawned");

                int tilesBefore = dm.TestTileCount;
                int coinsBefore = dm.TestToyCoins;
                int expectCoins = gm.TestConfig != null ? Mathf.Max(1, gm.TestConfig.DigCritterCoins) : 2;

                DigCritter critter = dm.TestSpawnCritter(1, dm.TestCols / 2);
                ctx.Assert(critter != null && dm.TestCritterCount == 1, "the critter did not spawn");

                // IT SCURRIES. Waited on the HOP COUNTER, not on a clock: a slow frame makes this
                // take longer, it never makes it fail.
                yield return ctx.WaitUntil(() => dm.TestCritterHops >= 1 || dm.TestCritterCount == 0, 20f,
                    () => $"the critter never scurried (hops {dm.TestCritterHops})");
                ctx.Assert(dm.TestCritterCount == 1, "the critter vanished before it could be caught");

                // IT BLOCKS NOTHING: the board is untouched by its presence.
                ctx.Assert(dm.TestTileCount == tilesBefore,
                    "the pit gained or lost tiles while a critter was loose");
                ctx.Assert(dm.TestFloaterReport() == "",
                    $"the board is unsettled with a critter in it: {dm.TestFloaterReport()}");

                // ---- Catch it ----
                Physics2D.SyncTransforms();
                gm.TestTapWorldRouted(critter.transform.position);
                yield return ctx.WaitFrames(2);

                ctx.Assert(dm.TestCrittersCaught == 1,
                    $"tapping the critter caught {dm.TestCrittersCaught} of them");
                ctx.Assert(dm.TestCritterCount == 0, "the caught critter is still in the pit");
                ctx.Assert(dm.TestToyCoins >= coinsBefore + expectCoins,
                    $"a caught critter paid {dm.TestToyCoins - coinsBefore} coins (expected {expectCoins})");

                // ---- Uncaught: it burrows away by itself, and that costs nothing ----
                int caught = dm.TestCrittersCaught;
                DigCritter second = dm.TestSpawnCritter(1, dm.TestCols / 2);
                ctx.Assert(second != null && dm.TestCritterCount == 1, "the second critter did not spawn");

                float life = gm.TestConfig != null ? gm.TestConfig.DigCritterLifeSeconds : 10f;
                yield return ctx.WaitUntil(() => dm.TestCritterCount == 0, life * 3f + 15f,
                    () => $"the uncaught critter never burrowed away ({dm.TestCritterCount} still out)");
                ctx.Assert(dm.TestCrittersCaught == caught,
                    "a critter that burrowed away was counted as caught");
                ctx.Assert(dm.IsOpen,
                    "the round ended when a critter despawned — a critter must gate nothing");

                ctx.Log($"critter scurried {dm.TestCritterHops} times, a tap caught it for " +
                        $"{expectCoins} coins, and an uncaught one burrowed away on its own " +
                        "without touching the board or the round");
            }
            finally
            {
                DigModeController.TestSuppressCrew = false;
                DigModeController.TestForceSurpriseKind = -1;
                DigModeController.TestSuppressToys = false;
                DigModeController.TestSuppressBones = false;
                DigModeController.TestSuppressLadder = false;
                DigModeController.TestSuppressGlow = false;
                gm.TestForceRoam();
            }
        }

        /// <summary>
        /// THE GEM VEIN: a hand-built four-cell run, hit at one END, must pop every segment and
        /// pay per segment. Asserted on the cells (all four gone, none left standing) and on the
        /// board (settled afterwards) as well as on the counters, so a chain that stopped halfway
        /// cannot pass.
        /// </summary>
        private IEnumerator Case_GemVeinChains(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            DigModeController dm = gm.TestDigMode;
            ctx.Assert(dm != null, "no dig controller");

            try
            {
                gm.TestReset();
                DigModeController.TestSuppressCrew = true;
                DigModeController.TestForceSurpriseKind = 0;   // giggle, never a geode (see above)
                DigModeController.TestSuppressToys = true;
                DigModeController.TestSuppressBones = true;
                DigModeController.TestSuppressLadder = true;
                DigModeController.TestSuppressGlow = true;

                dm.TestBuildThemedSite(null);
                yield return ctx.WaitFrames(1);

                const int veinCells = 4;
                ctx.Assert(FindCleanRow(dm, 2, veinCells, out int row, out int col),
                    $"no clean {veinCells}-cell run to lay a gem vein along");

                for (int i = 0; i < veinCells; i++)
                {
                    ctx.Assert(dm.TestSetVein(row, col + i),
                        $"could not place a vein segment at r{row}c{col + i}");
                }

                ctx.Assert(dm.TestVeinSizeAt(row, col) == veinCells,
                    $"the vein walk sees {dm.TestVeinSizeAt(row, col)} connected cells, " +
                    $"{veinCells} were placed");
                ctx.Assert(dm.TestKindCount(DigTileKind.Vein) == veinCells,
                    "the board does not hold the vein that was just placed");

                int coinsBefore = dm.TestToyCoins;
                int perSegment = gm.TestConfig != null
                    ? Mathf.Max(0, gm.TestConfig.DigGemVeinCoinsPerSegment)
                    : 1;

                // Hit ONE END of the run, through the ordinary clear chokepoint.
                dm.TestClearCell(row, col);
                yield return ctx.WaitFrames(2);

                ctx.Assert(dm.TestVeinChains == 1, $"{dm.TestVeinChains} chains fired for one hit");
                ctx.Assert(dm.TestVeinSegments == veinCells,
                    $"the spark popped {dm.TestVeinSegments} of {veinCells} segments");
                ctx.Assert(dm.TestKindCount(DigTileKind.Vein) == 0,
                    $"{dm.TestKindCount(DigTileKind.Vein)} vein cells are still standing after the chain");
                ctx.Assert(dm.TestToyCoins >= coinsBefore + perSegment * veinCells,
                    $"the vein paid {dm.TestToyCoins - coinsBefore} coins for {veinCells} segments " +
                    $"(expected {perSegment * veinCells})");

                yield return ctx.WaitUntil(() => !dm.IsOpen || dm.TestFloaterReport() == "", 10f,
                    () => $"board never settled after the vein popped: {dm.TestFloaterReport()}");

                ctx.Log($"a {veinCells}-cell vein hit at one end popped every segment and paid " +
                        $"{dm.TestToyCoins - coinsBefore} coins; board settled");
            }
            finally
            {
                DigModeController.TestSuppressCrew = false;
                DigModeController.TestForceSurpriseKind = -1;
                DigModeController.TestSuppressToys = false;
                DigModeController.TestSuppressBones = false;
                DigModeController.TestSuppressLadder = false;
                DigModeController.TestSuppressGlow = false;
                gm.TestForceRoam();
            }
        }

        /// <summary>
        /// THE BOUNCY MUSHROOM: the first bite BOINGS off it — no damage at all — and flings dirt
        /// instead, clearing neighbours; the second bite pops the mushroom. The "no damage" half
        /// is the one that matters most: a bite that appeared to do nothing would be the one beat
        /// in this game that punishes a tap, and the fling is what makes it a gift instead.
        ///
        /// FIRST, THOUGH, THE WORLD BOUNCES OFF IT TOO, and that sub-test is here because its
        /// absence is what made this case go red on the second gate run. A boing flings the
        /// mushroom's neighbours — including, on some rolls, the tile directly above it — so the
        /// column then drops ONTO the mushroom, and while a landing crack could pop it the toy
        /// destroyed itself as a direct consequence of its own gag: one bite, a funny bounce, and
        /// the promised second bite never came. Falling dirt now bounces off a mushroom exactly
        /// like the bucket does (no damage, no bounce spent), which is both the fix and the
        /// obviously right reading of "bouncy". Asserted deterministically by dropping a tile on
        /// one on purpose.
        /// </summary>
        private IEnumerator Case_MushroomBoings(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            DigModeController dm = gm.TestDigMode;
            ctx.Assert(dm != null, "no dig controller");

            try
            {
                gm.TestReset();
                DigModeController.TestSuppressCrew = true;
                DigModeController.TestForceSurpriseKind = 0;   // giggle, never a geode (see above)
                DigModeController.TestSuppressToys = true;
                DigModeController.TestSuppressBones = true;
                DigModeController.TestSuppressLadder = true;
                DigModeController.TestSuppressGlow = true;

                dm.TestBuildThemedSite(null);
                yield return ctx.WaitFrames(1);

                // A cell with two live tiles stacked above it: the sub-test below needs to be
                // able to drop one ON the mushroom, which means clearing the tile between them.
                ctx.Assert(FindMushroomCellWithRoofAbove(dm, out int row, out int col),
                    "no clean cell with two tiles above it to grow a mushroom under");
                ctx.Assert(dm.TestSetMushroom(row, col), $"could not place a mushroom at r{row}c{col}");

                DirtTile shroom = dm.TestTileAt(row, col);
                ctx.Assert(shroom != null && shroom.Kind == DigTileKind.Mushroom,
                    "the mushroom did not take");
                ctx.Assert(shroom.TestHitsRemaining == 2,
                    $"a fresh mushroom needs {shroom.TestHitsRemaining} bites (expected 2: boing, then pop)");

                // ---- THE WORLD BOUNCES OFF IT: drop the tile above onto the mushroom ----
                // Clearing r-1 lets the tile at r-2 fall onto the mushroom, which is precisely
                // the landing that used to pop it.
                dm.TestClearCell(row - 1, col);
                yield return ctx.WaitFrames(2);

                ctx.Assert(dm.TestMushroomBounceOffs >= 1,
                    "a tile landed on the mushroom and did not bounce off it");
                ctx.Assert(!shroom.IsDestroyed,
                    "falling dirt DESTROYED the mushroom — the world bounces off it, only a bite pops it");
                ctx.Assert(shroom.TestDamage == 0,
                    $"a landing dealt the mushroom {shroom.TestDamage} damage");
                ctx.Assert(!shroom.TestBounced,
                    "a falling tile spent the mushroom's bounce — that bounce belongs to the bite");
                ctx.Assert(shroom.TestHitsRemaining == 2,
                    $"after being landed on, the mushroom needs {shroom.TestHitsRemaining} bites " +
                    "(it must still owe the child both)");
                ctx.Assert(dm.TestMushroomBoings == 0,
                    $"{dm.TestMushroomBoings} BITE-boings recorded for a landing — the two must " +
                    "never be counted as the same beat");

                int tilesAlive = AliveTileCount(dm);

                // ---- Bite one: BOING ----
                yield return ctx.WaitUntil(() => dm.TestArmReady && !shroom.IsFalling, 15f,
                    "the arm never came ready for the first bite");
                ctx.TapWorld(shroom.transform.position);
                yield return ctx.WaitUntil(() => dm.TestMushroomBoings >= 1 || !dm.IsOpen, 20f,
                    () => $"the bucket's bite did not boing off the mushroom " +
                          $"(boings {dm.TestMushroomBoings})");
                yield return ctx.WaitFrames(2);

                ctx.Assert(!shroom.IsDestroyed, "the first bite DESTROYED the mushroom — it must bounce");
                ctx.Assert(shroom.TestDamage == 0,
                    $"the boing dealt {shroom.TestDamage} damage — a bounce is not a hit");
                ctx.Assert(shroom.TestBounced, "the mushroom did not record its bounce");
                ctx.Assert(dm.TestFlungTiles >= 1,
                    "the boing flung no dirt at all — a bite that does nothing is the one thing " +
                    "this beat may not be");

                int flungMin = 1;
                int flungMax = 2;
                gm.TestConfig?.GetMushroomFlingRange(out flungMin, out flungMax);
                ctx.Assert(dm.TestFlungTiles <= flungMax,
                    $"one boing flung {dm.TestFlungTiles} tiles, config caps it at {flungMax}");
                ctx.Assert(AliveTileCount(dm) < tilesAlive,
                    "the board has just as many tiles after a boing flung some loose");

                // ---- Bite two: pop ----
                yield return TapTileUntilDestroyed(ctx, dm, shroom);
                yield return ctx.WaitFrames(2);
                ctx.Assert(shroom.IsDestroyed, "the second bite did not pop the mushroom");
                ctx.Assert(dm.TestMushroomBoings == 1,
                    $"{dm.TestMushroomBoings} boings for one mushroom — the second bite must not bounce");

                yield return ctx.WaitUntil(() => !dm.IsOpen || dm.TestFloaterReport() == "", 10f,
                    () => $"board never settled after the mushroom: {dm.TestFloaterReport()}");

                ctx.Log($"mushroom at r{row}c{col}: {dm.TestMushroomBounceOffs} falling tile(s) " +
                        $"bounced off it with no damage and no bounce spent; bite 1 boinged " +
                        $"(0 damage) and flung {dm.TestFlungTiles} neighbour(s); bite 2 popped " +
                        "it; board settled");
            }
            finally
            {
                DigModeController.TestSuppressCrew = false;
                DigModeController.TestForceSurpriseKind = -1;
                DigModeController.TestSuppressToys = false;
                DigModeController.TestSuppressBones = false;
                DigModeController.TestSuppressLadder = false;
                DigModeController.TestSuppressGlow = false;
                gm.TestForceRoam();
            }
        }

        // ======================================================== GLOW (6tc)

        /// <summary>
        /// GLOW, gate to beam:
        ///   1. THE GATE. No lantern anywhere on the bright surface layer of a game where it has
        ///      never been found, and its discovery gate untripped.
        ///   2. THE DESCENT trips the gate and hides the bot behind a tile of the dark stratum —
        ///      dormant, covered, and lighting nothing.
        ///   3. THE WAKE persists (the machine service records it exactly as it does for the
        ///      three overworld sleepers) and turns the beam on.
        ///   4. THE BEAM raises buried-outline alphas INSIDE its zone and leaves outlines outside
        ///      it alone — the assertion that separates "a lamp" from "a board-wide reveal".
        ///   5. BACK ON THE BRIGHT LAYER it dims to a night-light and lights nothing.
        /// </summary>
        private IEnumerator Case_GlowRevealsAdjacent(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            DigModeController dm = gm.TestDigMode;
            ctx.Assert(dm != null, "no dig controller");

            gm.TestReset();
            MachineFriendController mf = EnsureMachines(ctx);

            try
            {
                DigModeController.TestSuppressCrew = true;
                DigModeController.TestForceSurpriseKind = 0;   // giggle, never a geode (see above)
                DigModeController.TestSuppressToys = true;   // toys claim cells this case needs
                DigModeController.TestSuppressBones = true;
                DigModeController.TestSuppressLadder = true;

                // ---- (1) The gate: nothing in the daylight ----
                dm.TestBuildThemedSite(null);
                yield return ctx.WaitFrames(1);
                ctx.Assert(dm.TestLayer == 0, "the baseline site is not on the surface layer");
                ctx.Assert(!mf.TestGateTripped(MachineKind.Glow),
                    "Glow's discovery gate is tripped before the child has ever gone deep");
                ctx.Assert(dm.TestGlow == null,
                    "a lantern bot is standing in a surface dig it has never been found in");

                // ---- (2) Down: the gate trips and a sleeper is waiting ----
                dm.TestDescend();
                yield return ctx.WaitFrames(2);

                ctx.Assert(dm.TestLayer == 1, "the descent did not reach the dark stratum");
                ctx.Assert(mf.TestGateTripped(MachineKind.Glow),
                    "reaching the dark did not trip Glow's discovery gate");
                ctx.Assert(dm.TestGlow != null, "no lantern bot waiting in the first dark layer");
                ctx.Assert(!dm.TestGlowAwake, "Glow arrived awake — it must be FOUND first");
                ctx.Assert(dm.TestGlow.TestCovered && dm.TestGlowRow >= 0,
                    "the dormant lantern is not hidden behind a tile");
                ctx.Assert(dm.TestGlowLitCells == 0, "a sleeping lantern is lighting the pit");

                // ---- (3) Bury two outlines: one where the beam will be, one far away ----
                int beamCell = dm.TestGlowBeamCell;
                ctx.Assert(beamCell >= 0, "the board has no uncleared cell for the beam to aim at");
                int beamRow = beamCell / 1000;
                int beamCol = beamCell % 1000;

                ctx.Assert(FindBuriableNear(dm, beamRow, beamCol, out int litRow, out int litCol),
                    $"no free cell beside the beam cell r{beamRow}c{beamCol} to bury an outline in");
                ctx.Assert(FindBuriableFar(dm, beamRow, beamCol, out int darkRow, out int darkCol),
                    "no free cell far from the beam to bury a control outline in");

                ctx.Assert(dm.TestBuryItemAt(litRow, litCol, ItemType.Treasure, 0),
                    $"could not bury the lit-zone outline at r{litRow}c{litCol}");
                ctx.Assert(dm.TestBuryItemAt(darkRow, darkCol, ItemType.Treasure, 0),
                    $"could not bury the control outline at r{darkRow}c{darkCol}");

                DirtTile lit = dm.TestTileAt(litRow, litCol);
                DirtTile dark = dm.TestTileAt(darkRow, darkCol);
                ctx.Assert(lit != null && dark != null, "the buried cells lost their tiles");

                float restingAlpha = dark.TestPeekAlpha;
                ctx.Assert(restingAlpha > 0.01f, "a buried outline shows no hint at all before the lamp");

                // ---- (4) Wake it: the beam comes on ----
                dm.TestWakeGlow();
                yield return ctx.WaitFrames(2);

                ctx.Assert(dm.TestGlowAwake, "the wake did not wake Glow");
                ctx.Assert(mf.IsWoken(MachineKind.Glow),
                    "waking Glow was not recorded by the machine service — a found friend must " +
                    "never be re-buried by a restart");
                ctx.Assert(!dm.TestGlow.TestCovered, "a woken lantern is still buried");
                ctx.Assert(dm.TestGlowLitCells > 0, "an awake lantern in the dark is lighting nothing");
                ctx.Assert(dm.TestGlowSweeps >= 1, "the beam never swept");

                float beamAlpha = gm.TestConfig != null ? gm.TestConfig.DigGlowPeekAlpha : 0.85f;
                ctx.Assert(dm.TestGlowLits(litRow, litCol),
                    $"the cell beside the beam (r{litRow}c{litCol}) is not lit");
                ctx.Assert(lit.TestPeekAlpha >= beamAlpha - 0.01f,
                    $"the lit outline sits at alpha {lit.TestPeekAlpha:F2}, the beam should raise " +
                    $"it to {beamAlpha:F2}");
                ctx.Assert(lit.TestPeekAlpha > dark.TestPeekAlpha + 0.05f,
                    $"the lit outline ({lit.TestPeekAlpha:F2}) is no brighter than the one outside " +
                    $"the beam ({dark.TestPeekAlpha:F2}) — this is a lamp, not a board-wide reveal");
                ctx.Assert(!dm.TestGlowLits(darkRow, darkCol),
                    $"the control cell r{darkRow}c{darkCol} is inside the beam zone after all");

                // THE LAMP SELLS INFORMATION, NEVER PROGRESS: a lit tile is exactly as much
                // digging as an unlit one.
                ctx.Assert(lit.TestHitsRemaining == dark.TestHitsRemaining,
                    $"the lit tile needs {lit.TestHitsRemaining} hits and the unlit one " +
                    $"{dark.TestHitsRemaining} — the beam must never soften a tile");

                // ---- (5) Back in the daylight: present, awake, and deliberately idle ----
                dm.TestBuildThemedSite(null);
                yield return ctx.WaitFrames(2);
                ctx.Assert(dm.TestLayer == 0, "the fresh site is not on the surface layer");
                ctx.Assert(dm.TestGlow != null && dm.TestGlowAwake,
                    "a woken Glow did not come along to the next dig");
                ctx.Assert(!dm.GlowShouldBeam, "the surface layer thinks it needs a lantern");
                ctx.Assert(dm.TestGlowLitCells == 0,
                    "Glow is beaming in broad daylight instead of idling as a night-light");

                ctx.Log($"gate: no lantern on the surface; the descent tripped it and hid a sleeper " +
                        $"behind r{dm.TestGlowRow}c{dm.TestGlowCol}; waking it persisted and lit " +
                        $"{dm.TestGlowLitCells} cells; the outline in the beam read " +
                        $"{lit.TestPeekAlpha:F2} vs {dark.TestPeekAlpha:F2} outside it, with " +
                        "identical hardness; back on the surface the beam is off");
            }
            finally
            {
                DigModeController.TestSuppressCrew = false;
                DigModeController.TestForceSurpriseKind = -1;
                DigModeController.TestSuppressToys = false;
                DigModeController.TestSuppressBones = false;
                DigModeController.TestSuppressLadder = false;
                gm.TestForceRoam();
                gm.TestReset();
            }
        }

        // ================================================ MEGA-FOSSIL SITE (84f)

        /// <summary>
        /// THE MEGA-FOSSIL SITE, end to end: a skull-marked mound, a much bigger pit, and EVERY
        /// bone the board's current skeleton still needs buried in it — so digging it out
        /// completes that species in one sitting. Also asserts the two rules that keep it from
        /// becoming a way to LOSE a skeleton: no ladder offers a rebuild out from under it, and
        /// the round does not end while bones are still in the ground.
        /// </summary>
        private IEnumerator Case_MegaFossilCompletes(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            DigModeController dm = gm.TestDigMode;
            ctx.Assert(dm != null, "no dig controller");

            try
            {
                gm.TestReset();
                DigModeController.TestSuppressCrew = true;
                DigModeController.TestForceSurpriseKind = 0;   // giggle, never a geode (see above)
                DigModeController.TestSuppressToys = true;
                DigModeController.TestSuppressGlow = true;

                // Bones ride the all-egg-species gate, and so does the mega-fossil roll.
                gm.TestSpawnDino(DinoType.TRex, GrowthStage.Baby);
                gm.TestSpawnDino(DinoType.Triceratops, GrowthStage.Baby);
                gm.TestSpawnDino(DinoType.Brachiosaurus, GrowthStage.Baby);
                gm.TestSpawnDino(DinoType.Stegosaurus, GrowthStage.Baby);
                yield return ctx.WaitFrames(1);
                ctx.Assert(gm.TestEggSpeciesAllOwned, "need all egg species owned to unlock bones");

                // ---- The promise, made in the overworld ----
                DigMound mound = FirstActiveMound(gm);
                ctx.Assert(mound != null, "no active mound to mark");
                ctx.Assert(!mound.IsMegaFossil, "a mound is already skull-marked after a reset");
                gm.TestForceMegaFossil(mound);
                ctx.Assert(mound.IsMegaFossil, "the mound did not take the mega-fossil mark");

                // ---- What the board still wants ----
                var wanted = new List<int>();
                ctx.Assert(gm.TryRemainingBones(out DinoType species, wanted),
                    "the skeleton board wants no bones at all on a fresh game");
                int need = wanted.Count;
                ctx.Assert(need >= SkeletonPlan.SmallSlots,
                    $"the focus skeleton ({species}) reports only {need} missing bones");
                ctx.Assert(!gm.TestSkeletonComplete(species), $"{species} is already complete");

                // ---- The pit ----
                dm.TestBuildMegaSite(null);
                yield return ctx.WaitFrames(1);

                ctx.Assert(dm.TestMega, "TestBuildMegaSite did not open a mega-fossil site");
                if (gm.TestConfig != null)
                {
                    gm.TestConfig.GetDigGridSize(true, out int megaRows, out int megaCols);
                    ctx.Assert(dm.TestRows == megaRows && dm.TestCols == megaCols,
                        $"the mega pit is {dm.TestRows}x{dm.TestCols}, config says {megaRows}x{megaCols}");
                    ctx.Assert(dm.TestRows * dm.TestCols >
                               gm.TestConfig.DigRows * gm.TestConfig.DigColumns,
                        "the mega pit is no bigger than an ordinary dig site");
                }
                ctx.Assert(dm.TestMegaSpecies == species,
                    $"the mega site buried {dm.TestMegaSpecies}'s skeleton, the board is filling {species}");
                ctx.Assert(dm.TestMegaBonesPlanned == need,
                    $"the mega site buried {dm.TestMegaBonesPlanned} of the {need} bones {species} " +
                    "still needs — it must bury the whole remaining skeleton");
                ctx.Assert(dm.TestBoneCount == need,
                    $"{dm.TestBoneCount} bones are actually on the board (expected {need})");
                ctx.Assert(!dm.TestLadderShown, "a mega-fossil site offered a ladder out from under itself");

                // ---- Dig the whole skeleton out ----
                int banked = gm.TestBonesBanked;
                int cleared = dm.TestUncoverAllBones();
                ctx.Assert(cleared > 0, "no bone cells were cleared");
                yield return ctx.WaitFrames(2);

                ctx.Assert(dm.TestBonesPopped == need,
                    $"{dm.TestBonesPopped} of {need} bones popped after every cell was uncovered");
                ctx.Assert(gm.TestBonesBanked == banked + need,
                    $"the bank holds {gm.TestBonesBanked - banked} new bones, {need} were dug");
                ctx.Assert(gm.TestSkeletonComplete(species),
                    $"{species}'s skeleton is still incomplete after digging out the whole mega site");
                ctx.Assert(gm.TestRevivalPending,
                    "a completed skeleton did not register as waiting for the Dino-Matic");

                ctx.Log($"mega-fossil site: skull-marked mound, {dm.TestRows}x{dm.TestCols} pit, all " +
                        $"{need} remaining {species} bones buried and dug out in one dig " +
                        $"({cleared} cells cleared) — the skeleton completed and is waiting for revival");
            }
            finally
            {
                DigModeController.TestSuppressCrew = false;
                DigModeController.TestForceSurpriseKind = -1;
                DigModeController.TestSuppressToys = false;
                DigModeController.TestSuppressGlow = false;
                gm.TestForceRoam();
                gm.TestReset();
            }
        }

        // ================================================================ helpers

        /// <summary>Clear the TOPMOST alive tile of some column that is not hiding an item.
        /// Clearing a cell with nothing above it moves no tiles at all, so a case built on this
        /// controls the cleared fraction exactly — and skipping item tiles keeps the round from
        /// ending underneath the assertions. False when no such tile is left.</summary>
        private bool ClearTopmostPlainTile(DigModeController dm)
        {
            for (int c = 0; c < dm.TestCols; c++)
            {
                int height = dm.TestColumnCount(c);
                if (height <= 0)
                {
                    continue;
                }

                int row = dm.TestRows - height;
                DirtTile t = dm.TestTileAt(row, c);
                if (t == null || t.IsDestroyed || t.HasItem)
                {
                    continue;
                }

                dm.TestClearCell(row, c);
                return true;
            }

            return false;
        }

        /// <summary>Every alive DIRT tile carries exactly <paramref name="expected"/> break-taps.
        /// Names the first offender rather than reporting a bare false — a sum that is off by
        /// three tells you nothing about which tile is wrong.</summary>
        private bool AllDirtTilesAt(DigModeController dm, int expected, out string offender)
        {
            IReadOnlyList<DirtTile> tiles = dm.TestTiles;
            for (int i = 0; i < tiles.Count; i++)
            {
                DirtTile t = tiles[i];
                if (t == null || t.IsDestroyed || t.Kind != DigTileKind.Dirt)
                {
                    continue;
                }

                if (t.TestMaxHealth != expected)
                {
                    offender = $"r{t.Row}c{t.Col} needs {t.TestMaxHealth} taps, expected {expected}";
                    return false;
                }
            }

            offender = "";
            return true;
        }

        /// <summary>THE DEPTH RULE, PER KIND, on a board with toys on it: a DIRT tile carries
        /// <paramref name="baseHardness"/> plus <paramref name="layerBonus"/>, and every TOY
        /// still seats the hardness its kind has always had — 1 for the things that pop in a
        /// single hit, 2 for a pinata pot. Depth makes the dirt older; it must never make a toy
        /// harder to enjoy.</summary>
        private bool KindHardnessHolds(DigModeController dm, int baseHardness, int layerBonus,
            out string offender)
        {
            int expectedDirt = Mathf.Clamp(baseHardness + layerBonus, 1, 6);
            IReadOnlyList<DirtTile> tiles = dm.TestTiles;
            for (int i = 0; i < tiles.Count; i++)
            {
                DirtTile t = tiles[i];
                if (t == null || t.IsDestroyed)
                {
                    continue;
                }

                int expected = t.Kind == DigTileKind.Dirt
                    ? expectedDirt
                    : (t.Kind == DigTileKind.Pot ? 2 : 1);

                if (t.TestMaxHealth != expected)
                {
                    offender = $"{t.Kind} at r{t.Row}c{t.Col} needs {t.TestMaxHealth} taps, " +
                               $"expected {expected}";
                    return false;
                }
            }

            offender = "";
            return true;
        }

        private int AliveTileCount(DigModeController dm)
        {
            int n = 0;
            IReadOnlyList<DirtTile> tiles = dm.TestTiles;
            for (int i = 0; i < tiles.Count; i++)
            {
                if (tiles[i] != null && !tiles[i].IsDestroyed)
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>The dirt tint a plain DIRT tile is currently drawn with — the layer's own
        /// darkening, read off the board rather than recomputed. (A toy cell is skipped: its
        /// colour is its identity, not the stratum's.)</summary>
        private Color FirstPlainDirtColor(DigModeController dm)
        {
            IReadOnlyList<DirtTile> tiles = dm.TestTiles;
            for (int i = 0; i < tiles.Count; i++)
            {
                DirtTile t = tiles[i];
                if (t != null && !t.IsDestroyed && t.Kind == DigTileKind.Dirt)
                {
                    return t.TestDirtColor;
                }
            }

            return Color.white;
        }

        /// <summary>Find a column whose cells from <paramref name="fromRow"/> down for
        /// <paramref name="count"/> rows are all plain, item-free, un-claimed dirt.</summary>
        private bool FindCleanColumn(DigModeController dm, int fromRow, int count, out int col)
        {
            for (int c = 0; c < dm.TestCols; c++)
            {
                bool ok = true;
                for (int r = fromRow; r < fromRow + count && ok; r++)
                {
                    ok = IsCleanCell(dm, r, c);
                }

                if (ok)
                {
                    col = c;
                    return true;
                }
            }

            col = -1;
            return false;
        }

        /// <summary>Find a horizontal run of <paramref name="count"/> clean cells, preferring the
        /// requested row and falling back to any other row below the surface.</summary>
        private bool FindCleanRow(DigModeController dm, int preferRow, int count, out int row, out int col)
        {
            for (int attempt = 0; attempt < dm.TestRows; attempt++)
            {
                int r = attempt == 0 ? preferRow : attempt - 1;
                if (r < 0 || r >= dm.TestRows)
                {
                    continue;
                }

                for (int c = 0; c + count <= dm.TestCols; c++)
                {
                    bool ok = true;
                    for (int i = 0; i < count && ok; i++)
                    {
                        ok = IsCleanCell(dm, r, c + i);
                    }

                    if (ok)
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

        /// <summary>A clean cell with TWO live, item-free dirt tiles stacked directly above it —
        /// so a case can clear the middle one and drop the top one onto whatever it plants in the
        /// bottom. Deepest rows first, because a cell near the floor has the most roof over it
        /// and the least chance of the board rearranging around the test.</summary>
        private bool FindMushroomCellWithRoofAbove(DigModeController dm, out int row, out int col)
        {
            for (int r = dm.TestRows - 1; r >= 2; r--)
            {
                for (int c = 0; c < dm.TestCols; c++)
                {
                    if (!IsCleanCell(dm, r, c) || !IsCleanCell(dm, r - 1, c) || !IsCleanCell(dm, r - 2, c))
                    {
                        continue;
                    }

                    row = r;
                    col = c;
                    return true;
                }
            }

            row = -1;
            col = -1;
            return false;
        }

        /// <summary>A cell any hand-placement hook would accept: alive, plain dirt, hiding
        /// nothing, not the pocket, not a bone cell.</summary>
        private bool IsCleanCell(DigModeController dm, int r, int c)
        {
            DirtTile t = dm.TestTileAt(r, c);
            return t != null && !t.IsDestroyed && !t.HasItem && !t.IsSurprise && !t.CoversBone &&
                   t.Kind == DigTileKind.Dirt;
        }

        /// <summary>A clean cell inside Glow's 3x3 beam zone (the beam cell itself included).</summary>
        private bool FindBuriableNear(DigModeController dm, int beamRow, int beamCol,
            out int row, out int col)
        {
            for (int dr = -1; dr <= 1; dr++)
            {
                for (int dc = -1; dc <= 1; dc++)
                {
                    int r = beamRow + dr;
                    int c = beamCol + dc;
                    if (IsCleanCell(dm, r, c))
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

        /// <summary>A clean cell well outside the beam zone AND outside the 3-cell cones the
        /// beam throws ahead of cracked tiles — the control the lit cell is compared against.
        /// Requires a Chebyshev distance of at least 2 from the beam, and (belt and braces) that
        /// the cell is not currently lit.</summary>
        private bool FindBuriableFar(DigModeController dm, int beamRow, int beamCol,
            out int row, out int col)
        {
            for (int r = 0; r < dm.TestRows; r++)
            {
                for (int c = 0; c < dm.TestCols; c++)
                {
                    if (Mathf.Max(Mathf.Abs(r - beamRow), Mathf.Abs(c - beamCol)) < 2)
                    {
                        continue;
                    }

                    if (IsCleanCell(dm, r, c) && !dm.TestGlowLits(r, c))
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
    }
}
