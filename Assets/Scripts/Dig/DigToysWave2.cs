using System.Collections.Generic;
using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;

namespace DinoDigger.Dig
{
    /// <summary>
    /// DIG TOYS, WAVE 2 (DinoDigger-u47): the water pocket, the dig critter, the gem vein and
    /// the bouncy mushroom. Four new things to find, joining the featured-toy roster and the
    /// secondary rolls alongside the crystals, geodes and pinata pots of wave 1.
    ///
    /// ONE RULE, INHERITED UNCHANGED: a tap ALWAYS wins and every outcome is a bonus. Three of
    /// the four are still just <see cref="DirtTile"/>s with a <see cref="DigTileKind"/> — same
    /// collider, same gravity, same clear chokepoint — so the cascade engine never learns they
    /// exist. The fourth (the critter) is not part of the board at all.
    ///
    /// WHAT EACH ONE ADDS, and why it is not just another way to pop a tile:
    ///
    ///   WATER POCKET — the only toy that makes the REST of the board easier instead of paying
    ///     out. Cracking it washes the remaining hardness off everything below it in its column
    ///     and floats buried loot one row up toward the surface. A child who finds one has been
    ///     handed a shortcut down a whole column.
    ///   DIG CRITTER — the only MOVING thing in the pit. It is not a tile, it is not in the
    ///     grid, it blocks nothing and gates nothing; it scurries, and if the child can land a
    ///     tap on it, it giggles and pays. Missing it costs exactly nothing.
    ///   GEM VEIN — the domino. Hit either end and a spark runs the length of the run, popping
    ///     each segment in turn and paying per segment. Crystals reward MATCHING; a vein rewards
    ///     finding the end of a line.
    ///   BOUNCY MUSHROOM — the joke. The bucket bounces off it: no damage, an enormous squash,
    ///     and the dirt it flings clears one or two random neighbours instead. The second bite
    ///     pops the mushroom. A bite that "fails" and still helps is the toddler rule in one
    ///     beat.
    ///
    /// ART IS PLACEHOLDER BY DESIGN. This wave ships no new generations (the art budget belongs
    /// to another ticket — see the follow-up bead): each toy borrows an existing sprite under a
    /// signature tint (see <see cref="DirtTile"/>) and every particle is the shared star/dust
    /// emitter. The mechanics are complete; only the pixels are pending.
    /// </summary>
    public partial class DigModeController
    {
        // ---- Water pocket ----
        private int _waterGushes;    // test-observable: pockets burst this site
        private int _tilesWashed;    // test-observable: tiles softened by a gush
        private int _itemsFloated;   // test-observable: buried items lifted a row

        // ---- Gem vein ----
        private readonly List<DirtTile> _vein = new List<DirtTile>();
        private readonly List<int> _veinRing = new List<int>();
        private readonly HashSet<DirtTile> _veinSeen = new HashSet<DirtTile>();
        private int _veinChains;     // test-observable: veins sparked this site
        private int _veinSegments;   // test-observable: segments popped this site

        // ---- Bouncy mushroom ----
        private int _mushroomBoings;    // test-observable: BITES bounced this site
        private int _flungTiles;        // test-observable: neighbours a boing cleared
        private int _mushroomBounceOffs; // test-observable: falling tiles bounced off a mushroom

        // ---- Dig critter ----
        private readonly List<DigCritter> _critters = new List<DigCritter>();
        private int _crittersSpawned;
        private int _crittersCaught;
        private int _critterHops;

        /// <summary>Reset the wave-2 bookkeeping. Called from BuildGrid alongside the wave-1
        /// counters, so a fresh site (or a fresh LAYER) starts every tally at zero.</summary>
        private void ResetWave2Counters()
        {
            _waterGushes = 0;
            _tilesWashed = 0;
            _itemsFloated = 0;
            _veinChains = 0;
            _veinSegments = 0;
            _mushroomBoings = 0;
            _flungTiles = 0;
            _mushroomBounceOffs = 0;
            _crittersSpawned = 0;
            _crittersCaught = 0;
            _critterHops = 0;
            _vein.Clear();
            _veinRing.Clear();
            _veinSeen.Clear();
        }

        // ==================================================== SITE GENERATION

        /// <summary>Roll the wave-2 SECONDARY toys onto whatever plain dirt the earlier layers
        /// left. Each chance takes the depth bonus (a deep layer is a busier board) and is
        /// clamped to 1, so a deep stratum can be generous but never fully deterministic.</summary>
        private void PlaceWave2SecondaryToys()
        {
            float bonus = LayerToyChanceBonus();

            float water = _config != null ? Mathf.Clamp01(_config.DigWaterPocketChance) : 0.3f;
            if (Random.value < Mathf.Clamp01(water + bonus))
            {
                PlaceWaterPocket();
            }

            float vein = _config != null ? Mathf.Clamp01(_config.DigGemVeinChance) : 0.3f;
            if (Random.value < Mathf.Clamp01(vein + bonus))
            {
                GrowGemVein(RollVeinLength());
            }

            float shroom = _config != null ? Mathf.Clamp01(_config.DigMushroomChance) : 0.3f;
            if (Random.value < Mathf.Clamp01(shroom + bonus))
            {
                PlaceMushroom();
            }
        }

        /// <summary>Place a featured wave-2 toy for the roller. Returns false when the board has
        /// no room, which is the roller's cue to walk on to the next roster entry.</summary>
        private bool TryPlacePrimaryWave2(PrimaryToy toy)
        {
            switch (toy)
            {
                case PrimaryToy.Water: return PlaceWaterPocket() != null;
                case PrimaryToy.Vein: return GrowGemVein(RollVeinLength()) > 1;
                case PrimaryToy.Mushroom: return PlaceMushroom() != null;
                case PrimaryToy.Critter: return SpawnCritterOnBoard();
                default: return false;
            }
        }

        /// <summary>A random unclaimed plain cell no LOWER than <paramref name="maxRow"/> — the
        /// row-bounded twin of <see cref="RandomPlainTile()"/>, used by the water pocket so it
        /// lands with a column underneath it to gush down. Returns null (rather than reaching
        /// deeper) when the upper board has no room, and the caller falls back.</summary>
        private DirtTile RandomPlainTile(int maxRow)
        {
            var pool = new List<DirtTile>();
            for (int i = 0; i < _tiles.Count; i++)
            {
                DirtTile t = _tiles[i];
                if (t != null && !t.IsDestroyed && !t.HasItem && !t.IsSurprise && !t.CoversBone &&
                    t.Kind == DigTileKind.Dirt && t.Row <= maxRow)
                {
                    pool.Add(t);
                }
            }

            return pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : null;
        }

        private int RollVeinLength()
        {
            int min = 3;
            int max = 5;
            _config?.GetGemVeinRange(out min, out max);
            return Random.Range(min, max + 1);
        }

        /// <summary>Put one water pocket on the board, PREFERRING a cell with column below it to
        /// gush down — a pocket on the pit floor is a toy that cannot do its trick. Falls back to
        /// any plain cell rather than to nothing.</summary>
        private DirtTile PlaceWaterPocket()
        {
            DirtTile t = RandomPlainTile(maxRow: _rows - 2);
            if (t == null)
            {
                t = RandomPlainTile();
            }

            t?.SetKind(DigTileKind.Water, 0);
            return t;
        }

        /// <summary>Grow one connected gem vein of up to <paramref name="length"/> cells: a
        /// random WALK that extends from the tip (not from a random member, which is how the
        /// crystal cluster deliberately grows a blob) so the result snakes through the dirt like
        /// a seam. Returns the cells actually grown, so the roller can tell a placed feature from
        /// a refused one.</summary>
        private int GrowGemVein(int length)
        {
            DirtTile seed = RandomPlainTile();
            if (seed == null)
            {
                return 0;
            }

            seed.SetKind(DigTileKind.Vein, 0);
            int grown = 1;
            DirtTile tip = seed;

            int[] dr = { -1, 1, 0, 0 };
            int[] dc = { 0, 0, -1, 1 };
            int guard = 0;
            while (grown < length && guard++ < length * 8)
            {
                int d = Random.Range(0, 4);
                DirtTile next = TileAt(tip.Row + dr[d], tip.Col + dc[d]);
                if (next == null || next.IsDestroyed || next.HasItem || next.IsSurprise ||
                    next.CoversBone || next.Kind != DigTileKind.Dirt)
                {
                    continue; // the same claimed-cell bar every other layer holds itself to
                }

                next.SetKind(DigTileKind.Vein, 0);
                tip = next;
                grown++;
            }

            return grown;
        }

        /// <summary>Put one bouncy mushroom on the board. Prefers a cell with at least one live
        /// neighbour so the first boing has dirt to fling; any plain cell will do otherwise (the
        /// board fills in around it as the cascade runs anyway).</summary>
        private DirtTile PlaceMushroom()
        {
            DirtTile t = RandomPlainTile();
            t?.SetKind(DigTileKind.Mushroom, 0);
            return t;
        }

        // ======================================================= WATER POCKET

        /// <summary>A water pocket has burst: the column below it is washed soft and everything
        /// buried in it floats a row closer to the surface.
        ///
        /// LOGIC IS SYNCHRONOUS, THE SPLASH IS STAGGERED — the same split the crystal blob pop
        /// and the cascade engine use, and for the same reason: the whole column's hardness and
        /// the whole column's buried bookkeeping are resolved on the bursting frame (so a test
        /// can assert it, and so nothing can be left half-washed if the site closes a frame
        /// later), while the splash visibly RUNS down the column over
        /// <c>GameConfig.DigWaterGushSeconds</c>.
        ///
        /// THE FLOAT IS RELATIVE, AND THAT IS THE POINT. The gush happens before the board falls
        /// into the hole the pocket left, so an item that floats up one row is one row higher
        /// than the collapse would otherwise have left it. Against gravity, not on top of it.</summary>
        private void GushWaterColumn(DirtTile pocket, string cause)
        {
            if (pocket == null || _grid == null)
            {
                return;
            }

            int col = pocket.Col;
            int fromRow = pocket.Row;
            _waterGushes++;

            GameManager.Instance?.Audio?.WaterGush();

            // (1) Wash: every DIRT tile below drops to one remaining hit. Toys are skipped by
            //     DirtTile.WashSoft itself — each of them has its own promise to the child and
            //     water running past must not quietly rewrite it.
            for (int r = fromRow + 1; r < _rows; r++)
            {
                DirtTile t = TileAt(r, col);
                if (t != null && t.WashSoft())
                {
                    _tilesWashed++;
                }
            }

            // (2) Float: buried loot rises one row. Walked TOP-DOWN so an item that has just
            //     moved up is never picked up again by the same pass and carried two rows — and
            //     so a whole COLUMN of loot rises together, each item stepping into the cell the
            //     one above it has just left. An origin cell being refilled from below is the
            //     chain working, not a duplicate: every move goes through MoveBuriedItem, which
            //     conserves the map's size or rolls itself back.
            for (int r = fromRow + 1; r < _rows; r++)
            {
                if (FloatBuriedUp(r, col))
                {
                    _itemsFloated++;
                }
            }

            // (3) The splash itself: one burst per row, running down the column. Deferred, so it
            //     proves its site is still the current one before drawing anything.
            float total = _config != null ? Mathf.Clamp(_config.DigWaterGushSeconds, 0f, 3f) : 0.6f;
            int splash = _config != null ? Mathf.Clamp(_config.DigWaterSplashCount, 0, 40) : 10;
            int rowsBelow = Mathf.Max(1, _rows - fromRow);
            int gen = _siteGeneration;
            Color tint = DirtTile.KindTint(DigTileKind.Water);

            for (int r = fromRow; r < _rows; r++)
            {
                Vector3 at = CellPosition(r, col);
                float delay = total * (r - fromRow) / rowsBelow;
                Tween.After(delay, () =>
                {
                    if (!_open || gen != _siteGeneration)
                    {
                        return;
                    }

                    SpawnPitBurst(at, tint, splash);
                });
            }
        }

        /// <summary>Move the buried item at r,c onto the tile one row above.
        ///
        /// THE COLUMN RISES AS A CHAIN, and that is the whole reason this walks TOP-DOWN. The
        /// item nearest the surface moves first, into a cell that has already been processed, so
        /// the one below it can then move into the cell that just emptied. Every item in the
        /// column ends up exactly one row higher, each having moved exactly once — a cell an item
        /// floated OFF is expected to be refilled from below, and an empty origin is not the
        /// invariant. (The invariant is conservation: see <see cref="MoveBuriedItem"/>.)</summary>
        private bool FloatBuriedUp(int row, int col)
        {
            return MoveBuriedItem(TileAt(row, col), TileAt(row - 1, col));
        }

        /// <summary>THE ONE CHOKEPOINT for relocating a buried item, and the only place in the
        /// dig that writes the buried bookkeeping to two tiles at once.
        ///
        /// A buried item is TWO facts kept in step — an entry in the <c>_buried</c> map and the
        /// tile's own peek/HasItem — and moving it means four writes across two tiles. Done
        /// inline, any early return between them leaves the item half-moved: showing on one tile
        /// and banked against another, or worse, counted twice. So the move is a transaction:
        /// BOTH ends are validated before anything is written, the writes then happen together,
        /// and a post-check on the map's own SIZE (a move must conserve it exactly) rolls the
        /// whole thing back if the two ends ever disagree. A refused move changes nothing at all.
        ///
        /// The destination bar is exactly the bar site generation holds itself to — alive, plain
        /// dirt, hiding nothing, not the pocket, not a bone cell — so a moved item can never come
        /// to rest somewhere the generator could not have buried it in the first place.</summary>
        private bool MoveBuriedItem(DirtTile from, DirtTile to)
        {
            if (from == null || to == null || from == to || from.IsDestroyed)
            {
                return false;
            }

            if (!_buried.TryGetValue(from, out Buried b) || !CanHostBuriedItem(to))
            {
                return false;
            }

            int before = _buried.Count;

            _buried.Remove(from);
            from.ClearItem();
            _buried[to] = b;
            Sprite peek = PeekSprite(b, out Color tint);
            to.SetPeek(peek, tint);

            // The transaction's own audit. A move relocates one item: the map must hold exactly
            // as many entries as it did, the destination must now be showing the item, and the
            // origin must have stopped. If any of that is untrue the move is undone in full
            // rather than left half-applied — a wrong board the child can still finish beats a
            // stranded item that makes the round unfinishable.
            if (_buried.Count == before && to.HasItem && !from.HasItem)
            {
                return true;
            }

            _buried.Remove(to);
            to.ClearItem();
            _buried[from] = b;
            Sprite backPeek = PeekSprite(b, out Color backTint);
            from.SetPeek(backPeek, backTint);
            Debug.LogError($"[Dig] buried-item move r{from.Row}c{from.Col} -> r{to.Row}c{to.Col} " +
                           "did not conserve the bookkeeping; rolled back");
            return false;
        }

        /// <summary>A tile that may take over a buried item: alive, plain dirt, hiding nothing of
        /// its own, not the surprise pocket, not standing on a bone cell, and not already banked
        /// in the map. The same bar every other layer of site generation holds itself to.</summary>
        private bool CanHostBuriedItem(DirtTile t)
        {
            return t != null && !t.IsDestroyed && !t.HasItem && !t.IsSurprise && !t.CoversBone &&
                   t.Kind == DigTileKind.Dirt && !_buried.ContainsKey(t);
        }

        // ========================================================== GEM VEIN

        /// <summary>Spark the whole vein containing <paramref name="start"/> and let the board
        /// fall into it. The entry point for a tap; every other path uses the logical half so
        /// several clears can share ONE settle.</summary>
        private void PopGemVein(DirtTile start, string cause)
        {
            if (PopGemVeinLogical(start, cause) > 0)
            {
                SettleGrid(cause);
            }
        }

        /// <summary>The chain itself: walk the connected vein from the hit segment, clear every
        /// cell of it, and pay per segment.
        ///
        /// Built as the crystal blob's twin on purpose — flood fill, copy out of the scratch,
        /// ForceBreak with a per-RING delay, clear through the normal chokepoint — because that
        /// shape is already proven against every hard case in this file (a site closing mid-pop,
        /// a chain started from inside a settle, a pop that finishes the round). The differences
        /// are exactly two: connectivity ignores colour, and the stagger is slow enough
        /// (0.12s a segment) to read as a spark TRAVELLING rather than a blob vanishing.</summary>
        private int PopGemVeinLogical(DirtTile start, string cause)
        {
            if (start == null || start.IsDestroyed || start.Kind != DigTileKind.Vein)
            {
                return 0;
            }

            CollectVein(start);
            int count = _vein.Count;
            if (count == 0)
            {
                return 0;
            }

            var cells = _vein.ToArray();
            var rings = _veinRing.ToArray();

            float stagger = _config != null
                ? Mathf.Clamp(_config.DigGemVeinStaggerSeconds, 0f, 0.5f)
                : 0.12f;
            int sparkles = _config != null ? Mathf.Clamp(_config.DigCrystalSparkleCount, 0, 40) : 12;
            int gen = _siteGeneration;
            Color tint = DirtTile.KindTint(DigTileKind.Vein);

            for (int i = 0; i < count; i++)
            {
                DirtTile t = cells[i];
                if (t == null || t.IsDestroyed)
                {
                    continue;
                }

                float delay = rings[i] * stagger;
                Vector3 at = t.transform.position;

                // Copied per iteration on purpose: `i` is the for-loop's own variable and is
                // SHARED by every closure below (unlike a foreach variable), so capturing it
                // directly would hand every segment the final index and flatten the zip.
                int step = i;

                t.ForceBreak(delay);   // cell vacated + collider off NOW, pixels linger
                ClearTile(t, cause);
                _veinSegments++;

                Tween.After(delay, () =>
                {
                    if (!_open || gen != _siteGeneration)
                    {
                        return;
                    }

                    SpawnPitBurst(at, tint, sparkles);

                    // The vein's own sound, pitched up along the run so a five-segment chain
                    // reads as one rising zip instead of five identical crystal pops.
                    GameManager.Instance?.Audio?.SparkZap(step, count);
                });
            }

            _veinChains++;

            int perSegment = _config != null ? Mathf.Max(0, _config.DigGemVeinCoinsPerSegment) : 1;
            PayToyCoins(perSegment * count);
            return count;
        }

        /// <summary>Breadth-first walk of the connected vein from <paramref name="start"/> into
        /// <see cref="_vein"/>, with each segment's distance from the hit cell in
        /// <see cref="_veinRing"/> — which is exactly the order the spark travels in.
        ///
        /// Its own scratch lists rather than the crystal blob's: a vein segment can be cleared
        /// from inside a settle pass that is itself iterating the crystal scratch, and two
        /// flood fills sharing one buffer is a bug waiting for a busy board.</summary>
        private void CollectVein(DirtTile start)
        {
            _vein.Clear();
            _veinRing.Clear();
            _veinSeen.Clear();

            if (start == null || start.IsDestroyed || start.Kind != DigTileKind.Vein)
            {
                return;
            }

            _vein.Add(start);
            _veinRing.Add(0);
            _veinSeen.Add(start);

            int[] dr = { -1, 1, 0, 0 };
            int[] dc = { 0, 0, -1, 1 };
            for (int head = 0; head < _vein.Count; head++)
            {
                DirtTile cur = _vein[head];
                int ring = _veinRing[head];
                for (int i = 0; i < 4; i++)
                {
                    DirtTile n = TileAt(cur.Row + dr[i], cur.Col + dc[i]);
                    if (n == null || n.IsDestroyed || n.Kind != DigTileKind.Vein || _veinSeen.Contains(n))
                    {
                        continue;
                    }

                    _veinSeen.Add(n);
                    _vein.Add(n);
                    _veinRing.Add(ring + 1);
                }
            }
        }

        // ==================================================== BOUNCY MUSHROOM

        /// <summary>BOING. The mushroom has eaten a hit (see <see cref="DirtTile.Damage"/>) and
        /// now pays for it in dirt: a huge squash, a puff, and one or two random NEIGHBOURS
        /// cleared instead.
        ///
        /// The fling routes through the ordinary no-settle clear and then settles ONCE, so a
        /// boing behaves exactly like any other multi-cell clear in this file: buried loot in a
        /// flung tile is collected, a crystal there takes its blob with it, a geode there lights
        /// its fuse. Nothing about the mushroom is a special case downstream of this method.</summary>
        internal void OnMushroomBounced(DirtTile mushroom)
        {
            if (mushroom == null || !_open || _finished || _grid == null)
            {
                return;
            }

            _mushroomBoings++;

            float squash = _config != null ? Mathf.Clamp(_config.DigMushroomSquash, 0.05f, 0.9f) : 0.5f;
            float squashTime = _config != null
                ? Mathf.Clamp(_config.DigMushroomSquashSeconds, 0.05f, 2f)
                : 0.4f;
            mushroom.Boing(squash, squashTime);

            // The mushroom's own springy note, not the geode's muffled thud — the tile the child
            // bounces off must not sound like the tile that explodes.
            GameManager.Instance?.Audio?.Boing();
            SpawnDust(mushroom.transform.position, 8);
            SpawnPitBurst(mushroom.transform.position, DirtTile.KindTint(DigTileKind.Mushroom), 10);

            int min = 1;
            int max = 2;
            _config?.GetMushroomFlingRange(out min, out max);
            int want = Random.Range(min, max + 1);

            // 8-neighbours, shuffled: the fling is meant to read as dirt spraying off a springy
            // cap, so which tiles go is deliberately unpredictable.
            var neighbours = new List<DirtTile>();
            for (int dr = -1; dr <= 1; dr++)
            {
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0)
                    {
                        continue;
                    }

                    DirtTile n = TileAt(mushroom.Row + dr, mushroom.Col + dc);
                    if (n != null && !n.IsDestroyed && n != mushroom)
                    {
                        neighbours.Add(n);
                    }
                }
            }

            Shuffle(neighbours);
            int flung = 0;
            for (int i = 0; i < neighbours.Count && flung < want; i++)
            {
                DirtTile n = neighbours[i];
                if (n == null || n.IsDestroyed)
                {
                    continue;
                }

                SpawnDust(n.transform.position, 3);
                ClearTileNoSettle(n, "mushroom boing");
                flung++;
                _flungTiles++;
            }

            SettleGrid("mushroom boing");
        }

        /// <summary>A falling tile landed on a mushroom: it BOUNCES OFF. A small squash, a puff
        /// of dirt, and nothing else — no damage, no fling, and above all no spending of the
        /// mushroom's one bounce, which belongs to the child's bite.
        ///
        /// Deliberately NOT the full <see cref="OnMushroomBounced"/> beat. Two reasons, both
        /// structural: the fling is the payoff for a BITE (a toy that dug for you unprompted is a
        /// toy that took a turn away), and firing a multi-cell clear from inside the settle loop
        /// that is calling this would let one mushroom re-enter the cascade on every landing.
        /// A bounce off the world is scenery; a bounce off the bucket is a gift.</summary>
        private void BounceOffMushroom(DirtTile mushroom)
        {
            if (mushroom == null)
            {
                return;
            }

            _mushroomBounceOffs++;

            float squash = _config != null ? Mathf.Clamp(_config.DigMushroomSquash, 0.05f, 0.9f) : 0.5f;
            float squashTime = _config != null
                ? Mathf.Clamp(_config.DigMushroomSquashSeconds, 0.05f, 2f)
                : 0.4f;

            // Softer than a bite's boing: the world bumping into it, not the bucket hitting it.
            mushroom.Boing(squash * 0.6f, squashTime);
            SpawnDust(mushroom.transform.position, 3);
        }

        // ======================================================== DIG CRITTER

        /// <summary>A tile just cleared: maybe a glowbug was living under it. Rolled on the
        /// clear chokepoint so ANY way of clearing a tile can release one, and capped so the pit
        /// never fills with things competing with the digging itself.</summary>
        private void MaybeSpawnCritter(DirtTile from)
        {
            if (from == null || !_open || _finished || TestSuppressToys || TestSuppressCritters)
            {
                return;
            }

            int cap = _config != null ? Mathf.Clamp(_config.DigCritterMax, 0, 6) : 2;
            if (LiveCritterCount() >= cap)
            {
                return;
            }

            float chance = _config != null ? Mathf.Clamp01(_config.DigCritterChance) : 0.12f;
            if (Random.value >= chance)
            {
                return;
            }

            SpawnCritter(from.transform.position);
        }

        /// <summary>Put a critter somewhere on the board right now (the roller's featured-toy
        /// placement). False only on a site with no tiles at all.</summary>
        private bool SpawnCritterOnBoard()
        {
            if (_tiles.Count == 0 || TestSuppressCritters)
            {
                return false; // the roller walks on to the next roster entry
            }

            DirtTile t = _tiles[Random.Range(0, _tiles.Count)];
            if (t == null)
            {
                return false;
            }

            return SpawnCritter(t.transform.position) != null;
        }

        /// <summary>Build one critter. It is NOT a tile: no grid cell, no gravity, no hardness,
        /// no bearing on when the round ends. That is the whole safety argument for a moving
        /// thing in a modal pit — the worst case for a critter nobody catches is that it
        /// scurries about for ten seconds and burrows away.</summary>
        private DigCritter SpawnCritter(Vector3 at)
        {
            var go = new GameObject("DigCritter");
            go.transform.SetParent(_root != null ? _root : transform, false);
            go.transform.position = at + new Vector3(0f, 0.15f, 0f);

            var sr = go.AddComponent<SpriteRenderer>();

            // A THING THAT PAYS A COIN MUST NOT LOOK LIKE THE COIN (DinoDigger-n05). This drew
            // on the STAR PARTICLE, so the one creature in the pit read as loot — and a toddler
            // learns what a tap means from what the thing looks like, so a star that has to be
            // chased teaches "chase the treasure" instead of "catch the bug". The real art is a
            // round green glowbug with legs; the star stays only as the never-invisible
            // fallback, and it is the only rung of this chain that can lie.
            bool realArt = _lib != null && _lib.CritterGlowbug != null;
            if (realArt)
            {
                sr.sprite = _lib.CritterGlowbug;
            }
            else if (_lib != null)
            {
                sr.sprite = _lib.StarParticle != null ? _lib.StarParticle : _lib.Treasure(0);
            }

            // Real art is shown as painted; only the fallback blob needs the glowbug's colour
            // pushed onto it.
            sr.color = realArt ? Color.white : new Color(0.85f, 1f, 0.45f);
            sr.sortingOrder = 14; // above tiles, peeks and the ladder

            if (sr.sprite != null && sr.sprite.bounds.size.y > 0.001f)
            {
                float k = 0.45f / sr.sprite.bounds.size.y;
                go.transform.localScale = new Vector3(k, k, 1f);
            }

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.34f / Mathf.Max(0.01f, go.transform.localScale.x);
            col.isTrigger = true;

            var critter = go.AddComponent<DigCritter>();
            critter.Build(this, _config);
            _critters.Add(critter);
            _crittersSpawned++;
            return critter;
        }

        /// <summary>Where a critter scurries next: a random cell on the board that is not the one
        /// it is standing on. Cells, not tiles — a critter is as happy in a dug-out hole as on
        /// top of the dirt, which is what keeps it from ever looking like part of the puzzle.</summary>
        internal Vector3 CritterHopTarget(Vector3 from)
        {
            if (_grid == null || _rows == 0 || _cols == 0)
            {
                return from;
            }

            for (int attempt = 0; attempt < 8; attempt++)
            {
                Vector3 p = CellPosition(Random.Range(0, _rows), Random.Range(0, _cols)) +
                            new Vector3(0f, 0.15f, 0f);
                if ((p - from).sqrMagnitude > 0.25f)
                {
                    return p;
                }
            }

            return from;
        }

        /// <summary>Caught! A giggle, a sparkle, the coins, and the critter is gone. Coins ride
        /// the normal guarded reward path (and take the depth multiplier with them, like every
        /// other toy payout), so a critter caught on the deep layer is worth more.</summary>
        internal void OnCritterCaught(DigCritter critter)
        {
            if (critter == null)
            {
                return;
            }

            _crittersCaught++;
            GameManager.Instance?.Audio?.Giggle();
            SpawnPitBurst(critter.transform.position, new Color(0.85f, 1f, 0.45f), 16);

            int coins = _config != null ? Mathf.Max(1, _config.DigCritterCoins) : 2;
            PayToyCoins(coins);

            DespawnCritter(critter);
        }

        /// <summary>A critter's ten seconds are up (or the layer went away under it).</summary>
        internal void DespawnCritter(DigCritter critter)
        {
            if (critter == null)
            {
                return;
            }

            _critters.Remove(critter);
            if (critter.gameObject != null)
            {
                Destroy(critter.gameObject);
            }
        }

        /// <summary>Test-observable: one more scurry happened (reported by the critter).</summary>
        internal void NoteCritterHop() => _critterHops++;

        private int LiveCritterCount()
        {
            int n = 0;
            for (int i = 0; i < _critters.Count; i++)
            {
                if (_critters[i] != null)
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>Clear every critter off the board. Called when the layer is rebuilt and when
        /// the site closes — a critter is a creature of ONE board, exactly like a tile.</summary>
        private void ClearCritters()
        {
            for (int i = 0; i < _critters.Count; i++)
            {
                if (_critters[i] != null && _critters[i].gameObject != null)
                {
                    Destroy(_critters[i].gameObject);
                }
            }

            _critters.Clear();
        }

        // ------------------------------------------------------------ TEST HOOKS

        /// <summary>TEST HOOK. Turn the cell at r,c into a water pocket / vein segment /
        /// mushroom. Same refusals as every other hand-placement hook (an item, the pocket, a
        /// bone cell, an existing toy, a dead cell), so a hand-built board is exactly as legal as
        /// a rolled one.</summary>
        /// <summary>TEST HOOK. Loose NO dig critters at the next site — the ambient half of the
        /// wave-2 toys, pinned on its own.
        ///
        /// Kept separate from TestSuppressToys on purpose, because a critter is not a tile and
        /// the two pins mean genuinely different things. TestSuppressToys says "build me an exact
        /// BOARD"; this says "put nothing LOOSE in the pit". A critter is the only thing this
        /// wave added that MOVES, PAYS COINS and — because catching one has to be possible at all
        /// — OUTRANKS A DIRT TILE FOR TAPS. Any case that certifies an exact spawn count or aims
        /// taps at exact tiles wants to be able to say so without also flattening the board.
        /// (TestSuppressToys still implies it, so every case already pinned stays pinned.)
        /// Cleared by the runner's between-case backstop.</summary>
        internal static bool TestSuppressCritters;

        internal bool TestSetWater(int r, int c) => TestSetToy(r, c, DigTileKind.Water, 0);
        internal bool TestSetVein(int r, int c) => TestSetToy(r, c, DigTileKind.Vein, 0);
        internal bool TestSetMushroom(int r, int c) => TestSetToy(r, c, DigTileKind.Mushroom, 0);

        internal int TestWaterGushes => _waterGushes;
        internal int TestTilesWashed => _tilesWashed;
        internal int TestItemsFloated => _itemsFloated;
        internal int TestVeinChains => _veinChains;
        internal int TestVeinSegments => _veinSegments;
        internal int TestMushroomBoings => _mushroomBoings;
        internal int TestFlungTiles => _flungTiles;

        /// <summary>TEST HOOK. Falling tiles that BOUNCED OFF a mushroom instead of cracking it —
        /// the direct evidence that the world cannot pop one.</summary>
        internal int TestMushroomBounceOffs => _mushroomBounceOffs;
        internal int TestCrittersSpawned => _crittersSpawned;
        internal int TestCrittersCaught => _crittersCaught;
        internal int TestCritterHops => _critterHops;
        internal int TestCritterCount => LiveCritterCount();

        /// <summary>TEST HOOK. Hits the tile at r,c still needs (a mushroom's pending BOUNCE
        /// counts as one), so a case can prove a gush really softened a column.</summary>
        internal int TestHitsRemaining(int r, int c)
        {
            DirtTile t = TileAt(r, c);
            return t != null ? t.TestHitsRemaining : 0;
        }

        /// <summary>TEST HOOK. Spawn a critter at a known cell, so a case can chase one without
        /// waiting for the per-clear roll to hand it one.</summary>
        internal DigCritter TestSpawnCritter(int r, int c)
        {
            DirtTile t = TileAt(r, c);
            return SpawnCritter(t != null ? t.transform.position : CellPosition(r, c));
        }

        /// <summary>TEST HOOK. The live critter (the first one), so a case can tap it through
        /// the real input pipeline.</summary>
        internal DigCritter TestCritter => _critters.Count > 0 ? _critters[0] : null;

        /// <summary>TEST HOOK. Cells the vein containing r,c spans right now, without popping
        /// it — proof the walk sees exactly the run the site generated.</summary>
        internal int TestVeinSizeAt(int r, int c)
        {
            CollectVein(TileAt(r, c));
            return _vein.Count;
        }
    }
}
