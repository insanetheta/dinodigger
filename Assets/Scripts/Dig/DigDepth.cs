using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;

namespace DinoDigger.Dig
{
    /// <summary>
    /// DEPTH LAYERS (DinoDigger-dv1) — the "depth is time" beat from docs/backstory.md, made
    /// mechanical.
    ///
    /// THE LOOP. Clear enough of the first stratum and a big friendly LADDER appears at the
    /// bottom of the pit, wiggling and glinting in exactly the language every other "come look"
    /// object in this game uses (the surprise pocket's sway, the mossy sleeper's glint). Tapping
    /// it dips the camera and the SAME dig site rebuilds one layer deeper: cooler and dimmer,
    /// harder dirt, and visibly richer — more crystals, more toys, bigger coin payouts, better
    /// odds of a bone. Two layers is the whole ladder, on purpose: a toddler's dig should end
    /// while it is still fun, and an endless descent is a grind wearing an adventure's clothes.
    ///
    /// WHY THIS IS A REBUILD AND NOT A SECOND BOARD. Descending calls the site's own
    /// <c>BuildGrid</c>, which is the single path every dig site has ever been built through.
    /// That buys, for free and with no special cases anywhere:
    ///
    ///   * the featured-toy guarantee applies PER LAYER (a fresh draw, and because the roller's
    ///     no-repeat history is what it is, the deep layer refuses to lead with the treat the
    ///     layer above led with — which is exactly the spec's "re-rolls, still no-repeat");
    ///   * bones, items, the pocket and the toys all re-generate through their existing rules,
    ///     each simply reading the deeper layer's multipliers;
    ///   * the site generation counter bumps, so every cascade, fuse and ring still in flight
    ///     from the layer above retires itself instead of reaching into the new board;
    ///   * <c>_found</c> is NOT cleared, so everything dug on the way down still rides home in
    ///     the same spill when the round ends. Descending never costs the child a thing.
    ///
    /// EXITING IS UNCHANGED FROM ANY LAYER: the round still ends when the last buried item of
    /// the CURRENT layer is uncovered, and FinishDig still spills the whole accumulated batch.
    /// </summary>
    public partial class DigModeController
    {
        // Which stratum this site is showing: 0 = the surface layer every dig has always been,
        // 1 = the first dark layer, and so on up to the configured maximum.
        private int _layer;

        // The ladder prop currently offered (null = not yet earned, or already taken).
        private DigLadder _ladder;

        // A descent in flight. The camera dip is a real (short) window during which the board is
        // still the OLD one, so a second tap on a ladder that is already on its way down must do
        // nothing at all rather than queue a second rebuild.
        private bool _descending;

        // THE LAYER THIS SITE HAS ALREADY COMMITTED TO LEAVING (-1 = none). The `_descending`
        // flag alone was not enough: it is only true for the length of the camera dip, and with
        // no camera parked on the pit (an off-screen build, a bare rig) the dip resolves
        // SYNCHRONOUSLY — so it opens and closes inside the same call and a second request
        // arriving a moment later sailed straight through it. That is exactly how one ladder
        // produced two descents in the gate run.
        //
        // This is the state-derived version of the same idea and it does not depend on timing at
        // all: a descent is a one-way door out of a LAYER, so once a layer has asked to be left,
        // it can never ask again — whatever asked (a re-tap during the dip, a stray tap on a
        // collider that outlived its Destroy, a test hook called twice, a second callback).
        private int _descendFromLayer = -1;

        // Test-observable: descents made at THIS site (reset with the site, like every other
        // per-site tally — a counter that quietly spans sites is a counter that lies).
        private int _descents;

        /// <summary>How many strata this site may have (1 = the ladder is disabled outright).</summary>
        private int MaxLayers =>
            _config != null ? Mathf.Clamp(_config.DigDepthLayers, 1, 4) : 2;

        /// <summary>Fraction of this layer's tiles that must be gone before the ladder shows.</summary>
        private float LadderRevealFraction =>
            _config != null ? Mathf.Clamp01(_config.DigLadderRevealFraction) : 0.6f;

        /// <summary>Fraction of this layer's tiles that have been cleared. The ladder's trigger,
        /// and deliberately derived from the live board rather than from a counter: a counter
        /// would have to be maintained by every clear path in the file, and one that forgot would
        /// silently withhold the ladder.</summary>
        private float ClearedFraction()
        {
            if (_tiles.Count == 0)
            {
                return 0f;
            }

            int alive = 0;
            for (int i = 0; i < _tiles.Count; i++)
            {
                if (_tiles[i] != null && !_tiles[i].IsDestroyed)
                {
                    alive++;
                }
            }

            return 1f - alive / (float)_tiles.Count;
        }

        // ------------------------------------------------------------ layer maths
        // EVERY ONE of these is read at the moment it is USED (never cached at build time), so
        // dragging a depth slider in play mode retunes the very next layer — the same live-tuning
        // discipline the cascade knobs are held to (DinoDigger-73a).

        /// <summary>Compound multiply for <paramref name="perLayer"/> applied once per layer
        /// below the surface. Alpha is deliberately left at 1: these are tints, not fades.</summary>
        private Color LayerMultiply(Color perLayer)
        {
            Color m = Color.white;
            for (int i = 0; i < _layer; i++)
            {
                m = new Color(m.r * perLayer.r, m.g * perLayer.g, m.b * perLayer.b, 1f);
            }

            return m;
        }

        /// <summary>The dirt tint for this layer: the theme's own multiply, then the depth's.</summary>
        private Color LayerDirtTint(Color themeTint)
        {
            Color deep = _config != null ? _config.DigDeepDirtMultiply : new Color(0.62f, 0.63f, 0.78f, 1f);
            Color m = LayerMultiply(deep);
            return new Color(themeTint.r * m.r, themeTint.g * m.g, themeTint.b * m.b, themeTint.a);
        }

        /// <summary>The backdrop tint for this layer (same composition as the dirt).</summary>
        private Color LayerBackgroundTint(Color themeTint)
        {
            Color deep = _config != null ? _config.DigDeepBackgroundMultiply : new Color(0.52f, 0.55f, 0.72f, 1f);
            Color m = LayerMultiply(deep);
            return new Color(themeTint.r * m.r, themeTint.g * m.g, themeTint.b * m.b, themeTint.a);
        }

        /// <summary>Extra break-taps every tile of this layer carries.</summary>
        private int LayerHardnessBonus()
        {
            int per = _config != null ? _config.DigDeepHardnessBonus : 1;
            return Mathf.Max(0, per) * _layer;
        }

        /// <summary>Multiplier on every coin a TOY pays at this depth.</summary>
        private float LayerCoinMultiplier()
        {
            float per = _config != null ? Mathf.Max(1f, _config.DigDeepCoinMultiplier) : 2f;
            return Mathf.Pow(per, _layer);
        }

        /// <summary>Multiplier on the buried loot table's TREASURE weight at this depth.</summary>
        private float LayerTreasureWeightMultiplier()
        {
            float per = _config != null ? Mathf.Max(1f, _config.DigDeepTreasureWeightMultiplier) : 2f;
            return Mathf.Pow(per, _layer);
        }

        /// <summary>Extra crystal clusters this layer's generation rolls.</summary>
        private int LayerCrystalClusterBonus()
        {
            int per = _config != null ? Mathf.Max(0, _config.DigDeepCrystalClusterBonus) : 1;
            return per * _layer;
        }

        /// <summary>Added to every SECONDARY toy chance at this depth (the result is clamped to
        /// 1 by the caller, so a deep layer can be busy but never certain of everything).</summary>
        private float LayerToyChanceBonus()
        {
            float per = _config != null ? Mathf.Max(0f, _config.DigDeepToyChanceBonus) : 0.2f;
            return per * _layer;
        }

        /// <summary>Added to the chance this layer buries a bone.</summary>
        private float LayerBoneChanceBonus()
        {
            float per = _config != null ? Mathf.Max(0f, _config.DigDeepBoneChanceBonus) : 0.5f;
            return per * _layer;
        }

        // --------------------------------------------------------------- the ladder

        /// <summary>Offer the ladder down, if this layer has been dug out enough to earn it.
        /// Called from the settle tail, so it is re-checked after every single clearing beat —
        /// a bite, a cascade, a crystal chain, a mushroom's fling — and can never be missed by a
        /// path that forgot to ask.</summary>
        private void MaybeRevealLadder()
        {
            if (_ladder != null || _descending || !_open || _finished || _grid == null)
            {
                return;
            }

            if (_layer + 1 >= MaxLayers || TestSuppressLadder)
            {
                return;
            }

            // NEVER ON A MEGA-FOSSIL SITE (DinoDigger-84f). Descending REBUILDS the board, and a
            // rebuild would take an un-dug skeleton with it — the one thing a mega site exists to
            // give the child. The two features are deliberately exclusive: mega is wide, not deep.
            if (_mega)
            {
                return;
            }

            // TWO WAYS TO EARN IT, and the second one is what makes the feature real rather than
            // theoretical. The headline rule is the clear threshold. But a dig ENDS when its last
            // buried item is uncovered, and on a 35-tile board with three items that very often
            // happens before 60% of the tiles are gone — so a threshold alone would mean a child
            // could play for a week without ever seeing a ladder.
            //
            // So the ladder is ALSO offered once the layer is down to its last buried item (and
            // the child has actually dug something). The way down is then always on the table
            // before the round can close, and which of the two the child takes — one more hint,
            // or the dark — is a choice rather than a coin toss.
            bool byThreshold = ClearedFraction() >= LadderRevealFraction;
            bool byLastItem = _buried.Count <= 1 && ClearedFraction() > 0f;
            if (!byThreshold && !byLastItem)
            {
                return;
            }

            if (!TryLadderCell(out int r, out int c))
            {
                return;
            }

            SpawnLadder(r, c);
        }

        /// <summary>Find the cell the ladder stands in: an EMPTY one on the pit floor, nearest
        /// the middle so it reads as the way down rather than as a thing tucked in a corner.
        /// Falls back to the lowest empty cell anywhere on the board — with 60% of the tiles
        /// gone there is always one, and a ladder halfway up is still obviously a ladder.</summary>
        private bool TryLadderCell(out int row, out int col)
        {
            row = -1;
            col = -1;

            for (int r = _rows - 1; r >= 0; r--)
            {
                int bestCol = -1;
                float bestDist = float.MaxValue;
                float mid = (_cols - 1) * 0.5f;
                for (int c = 0; c < _cols; c++)
                {
                    if (TileAt(r, c) != null)
                    {
                        continue;
                    }

                    float d = Mathf.Abs(c - mid);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestCol = c;
                    }
                }

                if (bestCol >= 0)
                {
                    row = r;
                    col = bestCol;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Build the ladder prop. Every lookup falls through to something visible
        /// rather than leaving an invisible tap target (see <see cref="LadderSprite"/>).</summary>
        private void SpawnLadder(int row, int col)
        {
            var go = new GameObject("DigLadder");
            go.transform.SetParent(_root != null ? _root : transform, false);
            go.transform.position = CellPosition(row, col);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = LadderSprite();
            sr.sortingOrder = 12; // above the tiles (10) and their peeks (11)

            // REAL ART IS SHOWN AS PAINTED; only a fallback gets tinted. The old flat gold
            // multiply existed to make the striped barrier sign read as timber, and left on the
            // real ladder it would flatten the wood grain into one yellow slab.
            sr.color = _lib != null && _lib.LadderDown != null
                ? Color.white
                : new Color(1f, 0.86f, 0.45f);

            // Sized to a cell and a half: a ladder is the biggest, friendliest thing in the pit
            // for the moment it is on screen, because it is the only thing the child must find.
            if (sr.sprite != null && sr.sprite.bounds.size.y > 0.001f)
            {
                float k = 1.4f / sr.sprite.bounds.size.y;
                go.transform.localScale = new Vector3(k, k, 1f);
            }

            // The tap target stays INSIDE the ladder's own (empty) cell, even though the art
            // deliberately overhangs it: the ladder outranks a dirt tile for taps, so a collider
            // that spilled into a neighbouring cell would be a prop that can eat a bite aimed at
            // dirt. A full cell is a generous toddler target on its own.
            var box = go.AddComponent<BoxCollider2D>();
            box.size = new Vector2(1f, 1f);
            box.isTrigger = true;

            _ladder = go.AddComponent<DigLadder>();
            _ladder.Build(this, _lib);

            GameManager.Instance?.Audio?.Chime();
        }

        /// <summary>Art for the ladder: the real wooden ladder prop, else the striped barrier
        /// sign, else the dirt sprite. Never null on a library that has any art at all.
        ///
        /// THE FALLBACK CHAIN IS DELIBERATELY LOOT-FREE (DinoDigger-n05). The old chain ended
        /// at the MOUND sprite, which is the sprite this game uses for a dig site and for every
        /// machine's charge gauge — i.e. it made the way down look like a thing to collect. A
        /// toddler learns what a tap means from what the thing LOOKS like, so the rule here is
        /// that every rung of this chain must read as built scenery: a ladder, a barrier, or
        /// plain dirt. Nothing that glitters, nothing that could be picked up.</summary>
        private Sprite LadderSprite()
        {
            if (_lib == null)
            {
                return null;
            }

            if (_lib.LadderDown != null)
            {
                return _lib.LadderDown;
            }

            return _lib.ConstructionSign != null ? _lib.ConstructionSign : _lib.Dirt(0);
        }

        /// <summary>Take the ladder down (the tap handler, and the test hook's entry point).
        ///
        /// The camera DIPS and the new layer is built at the BOTTOM of the dip, so the child
        /// watches the world go down rather than being teleported into a different one. With no
        /// camera wired (a bare rig, an off-screen test build) the dip degrades to an immediate
        /// rebuild — the descent is never conditional on the flourish.</summary>
        internal void DescendLayer()
        {
            if (!_open || _finished || _descending || _layer + 1 >= MaxLayers)
            {
                return;
            }

            // ONE DESCENT PER LAYER, EVER (see _descendFromLayer). Checked and claimed in the
            // same breath, before anything else can run, so no ordering of taps, callbacks or
            // hooks can slip a second rebuild through.
            if (_descendFromLayer == _layer)
            {
                return;
            }

            _descendFromLayer = _layer;
            _descending = true;

            // Where the way down WAS, captured before the prop goes: every descent cue is
            // staged around that point, and RemoveLadder is about to null it.
            Vector3 ladderAt = _ladder != null ? _ladder.transform.position : CellPosition(_rows - 1, _cols / 2);
            RemoveLadder();

            GameManager gm = GameManager.Instance;
            gm?.Audio?.Crumble();

            int gen = _siteGeneration;
            float seconds = _config != null ? Mathf.Clamp(_config.DigLadderDipSeconds, 0f, 3f) : 0.6f;
            float units = _config != null ? Mathf.Clamp(_config.DigLadderDipUnits, 0f, 6f) : 1.6f;

            PlayDescentCues(ladderAt, units, seconds);

            if (gm != null)
            {
                gm.DigDipCamera(units, seconds, () => BuildDeeperLayer(gen));
            }
            else
            {
                BuildDeeperLayer(gen);
            }
        }

        // ---------------------------------------------------- the descent, made legible
        // THE CAMERA DIP ALONE WAS NOT ENOUGH (DinoDigger-n05). A dip moves the frame, but if
        // everything inside the frame moves with it there is no parallax and therefore no
        // evidence of travel — the child just saw the picture get darker and read it as NIGHT
        // FALLING, not as going down. Depth has to be shown by things passing the other way, so
        // three cues run against the dip, none of which the descent depends on:
        //
        //   THE LADDER STAYS PUT while the world drops — a ghost copy of the prop that rises out
        //     of frame. It is the one object the child was looking at, so it is the one whose
        //     motion they will read, and a ladder receding UPWARD can only mean "we went down".
        //   STRATA SLIDE UP past the frame: bands of earth streaming the opposite way to the
        //     camera, the same trick a lift shaft uses.
        //   A PUFF OF DIRT at the ladder's foot, so the descent has a physical cause.
        //
        // Every one of them is a plain prop with no collider, on a generation guard, destroyed
        // when the dip ends. Nothing here can outlive its layer or eat a tap.
        private int _descentCues;

        /// <summary>TEST HOOK. Descent-cue props staged (the flourish is observable, so a case
        /// can prove the descent is SHOWN rather than only counted).</summary>
        internal int TestDescentCues => _descentCues;

        private void PlayDescentCues(Vector3 ladderAt, float dipUnits, float dipSeconds)
        {
            if (_root == null && transform == null)
            {
                return;
            }

            Transform parent = _root != null ? _root : transform;
            float travel = Mathf.Max(1.5f, dipUnits * 2f);
            float seconds = Mathf.Max(0.15f, dipSeconds);
            int gen = _siteGeneration;

            // (1) The ladder climbs away above us.
            Sprite ladderArt = LadderSprite();
            if (ladderArt != null)
            {
                var ghost = new GameObject("LadderGhost");
                ghost.transform.SetParent(parent, false);
                ghost.transform.position = ladderAt;

                var gs = ghost.AddComponent<SpriteRenderer>();
                gs.sprite = ladderArt;
                gs.sortingOrder = 13;
                gs.color = _lib != null && _lib.LadderDown != null
                    ? Color.white
                    : new Color(1f, 0.86f, 0.45f);
                if (ladderArt.bounds.size.y > 0.001f)
                {
                    float k = 1.4f / ladderArt.bounds.size.y;
                    ghost.transform.localScale = new Vector3(k, k, 1f);
                }

                RiseAndVanish(ghost.transform, gs, ladderAt, travel * 1.15f, seconds, gen);
                _descentCues++;
            }

            // (2) Bands of earth streaming up past the frame. Drawn from the dirt sprite so they
            //     are the same material the pit is made of, stretched wide and squashed flat.
            Sprite band = _lib != null ? _lib.Dirt(0) : null;
            if (band != null && band.bounds.size.x > 0.001f && band.bounds.size.y > 0.001f)
            {
                for (int i = 0; i < 3; i++)
                {
                    var strata = new GameObject("DescentStrata");
                    strata.transform.SetParent(parent, false);
                    Vector3 from = new Vector3(_origin.x, ladderAt.y - 0.9f - i * 1.35f, 0.02f);
                    strata.transform.position = from;

                    var ss = strata.AddComponent<SpriteRenderer>();
                    ss.sprite = band;
                    ss.sortingOrder = 3; // behind every tile (10), in front of the backdrop (2)
                    // Alternating warm earth tones so the bands read as separate strata rather
                    // than as one moving slab.
                    ss.color = i % 2 == 0
                        ? new Color(0.44f, 0.30f, 0.19f, 0.9f)
                        : new Color(0.55f, 0.38f, 0.24f, 0.9f);
                    strata.transform.localScale = new Vector3(
                        (_cols + 3f) / band.bounds.size.x, 0.5f / band.bounds.size.y, 1f);

                    RiseAndVanish(strata.transform, ss, from, travel, seconds, gen);
                    _descentCues++;
                }
            }

            // (3) The dirt the way down kicks up. Crumbs, never stars: a star burst here would
            //     say "you won something" at the exact moment nothing was won.
            SpawnDescentDust(ladderAt);
        }

        /// <summary>Carry a cue prop straight up and fade it out, then destroy it. Guarded on the
        /// site generation, so a cue caught by a site teardown mid-flight simply stops.</summary>
        private void RiseAndVanish(Transform t, SpriteRenderer sr, Vector3 from, float rise,
            float seconds, int gen)
        {
            Color start = sr.color;
            Tween.Run(seconds,
                u =>
                {
                    if (t == null || sr == null)
                    {
                        return;
                    }

                    t.position = from + new Vector3(0f, rise * u, 0f);
                    sr.color = new Color(start.r, start.g, start.b, start.a * (1f - u * u));
                },
                () =>
                {
                    if (t != null)
                    {
                        Destroy(t.gameObject);
                    }
                },
                Tween.EaseOutCubic);

            // Backstop: a site torn down mid-dip destroys the root anyway, but a cue whose
            // tween was cancelled must not be left hanging on the next layer's board.
            Tween.After(seconds + 0.2f, () =>
            {
                if (t != null && (gen != _siteGeneration || !_open))
                {
                    Destroy(t.gameObject);
                }
            });
        }

        /// <summary>The dirt puff at the ladder's foot. Deliberately the CRUMB particle (the
        /// game's "something broke" material), never the star — see SpawnPitBurst, which is the
        /// reward-flavoured twin of this and is the wrong voice for a descent.</summary>
        private void SpawnDescentDust(Vector3 at)
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || _lib == null)
            {
                return;
            }

            Sprite dust = _lib.DustPuff != null ? _lib.DustPuff : _lib.CrumbParticle;
            if (dust == null)
            {
                return;
            }

            ParticleSystem ps = gm.TownCreateParticles(_root != null ? _root : transform,
                dust, new Color(0.62f, 0.47f, 0.33f), 0.45f);
            if (ps == null)
            {
                return;
            }

            ps.transform.position = at + new Vector3(0f, -0.35f, 0f);
            ps.Emit(16);
            _descentCues++;
            Tween.After(2f, () =>
            {
                if (ps != null)
                {
                    Destroy(ps.gameObject);
                }
            });
        }

        /// <summary>The rebuild itself, one stratum down. Fires from the camera dip's midpoint,
        /// which outlives nothing but is still a deferred callback, so it proves its site is the
        /// one that asked for it (see <c>_siteGeneration</c>) before touching anything.</summary>
        private void BuildDeeperLayer(int gen)
        {
            _descending = false;

            if (!_open || _finished || gen != _siteGeneration)
            {
                return; // the site closed (or finished) inside the dip: nothing to descend into
            }

            _layer = Mathf.Min(_layer + 1, MaxLayers - 1);
            _descents++;

            // GLOW'S GATE (DinoDigger-6tc) is "the child reached the dark". Tripped BEFORE the
            // rebuild so the new layer's generation can hide the lantern bot behind one of its
            // tiles on this very first descent.
            GameManager.Instance?.NotifyDeepDigLayer(_layer);

            BuildGrid();
        }

        /// <summary>Destroy the ladder prop (taken, or the layer went away under it).
        ///
        /// The prop is CONSUMED before it is destroyed, and that ordering is load-bearing:
        /// <c>Destroy</c> does not take effect until the end of the frame, so a ladder that has
        /// been "removed" still has a live collider for the rest of it. Consuming turns the tap
        /// target off immediately (and stops the beacon), so those last few milliseconds cannot
        /// answer a tap that no longer means anything.</summary>
        private void RemoveLadder()
        {
            if (_ladder != null)
            {
                _ladder.Consume();
                Destroy(_ladder.gameObject);
                _ladder = null;
            }
        }

        /// <summary>Per-SITE depth bookkeeping, reset alongside every other per-site tally when a
        /// site (not a layer) begins. A layer rebuild deliberately does NOT run this — the whole
        /// point of the descent counter is that it counts across the layers of one dig.</summary>
        private void ResetDepthForNewSite()
        {
            _layer = 0;
            _descents = 0;
            _descending = false;
            _descendFromLayer = -1;
        }

        // ------------------------------------------------------------ DEMO SURFACE
        // PUBLIC on purpose (see DemoDigMenu): editor scripts live in another assembly and
        // cannot reach the internal Test* hooks, and the descent is the one beat in this file
        // that can only be judged by eye — "does this read as going DOWN, or as night falling?"
        // was the question that produced DinoDigger-n05, and it cannot be answered from a
        // counter. Neither of these bypasses a RULE: the ladder still refuses a mega site and
        // the deepest layer, and the descent still runs through the one-way door in
        // DescendLayer. All they skip is the digging.

        /// <summary>DEMO. Offer the way down right now, whatever fraction has been cleared.
        /// Returns false when the layer cannot have one (the deepest stratum, a mega site, a
        /// ladder already standing) or when the board has no empty cell for it to stand in yet —
        /// collapse a column first, then ask again.</summary>
        public bool DemoOfferLadder()
        {
            if (!_open || _finished || _grid == null || _ladder != null || _descending)
            {
                return false;
            }

            if (_layer + 1 >= MaxLayers || _mega)
            {
                return false;
            }

            if (!TryLadderCell(out int r, out int c))
            {
                return false;
            }

            SpawnLadder(r, c);
            return true;
        }

        /// <summary>DEMO. Take the way down, with or without a ladder standing.</summary>
        public bool DemoDescend()
        {
            int before = _layer;
            DescendLayer();
            return _descending || _layer != before;
        }

        /// <summary>DEMO. Which stratum the site is showing (0 = the surface).</summary>
        public int DemoLayer => _layer;

        // ------------------------------------------------------------ TEST HOOKS

        /// <summary>TEST HOOK. Never offer the ladder at the next site. The twin of
        /// TestSuppressToys/TestSuppressBones and kept separate for the same reason: a case that
        /// digs a board to rubble must not have a ladder appear in the middle of its assertions.
        /// Cleared by the runner's between-case backstop.</summary>
        internal static bool TestSuppressLadder;

        /// <summary>TEST HOOK. Which stratum the site is showing (0 = surface).</summary>
        internal int TestLayer => _layer;

        /// <summary>TEST HOOK. Descents made at this site.</summary>
        internal int TestDescents => _descents;

        /// <summary>TEST HOOK. Is the ladder standing in the pit right now?</summary>
        internal bool TestLadderShown => _ladder != null;

        /// <summary>TEST HOOK. World position of the offered ladder (Vector3.zero when none) —
        /// so a case can tap it through the REAL input pipeline rather than calling the
        /// descent directly.</summary>
        internal Vector3 TestLadderPosition =>
            _ladder != null ? _ladder.transform.position : Vector3.zero;

        /// <summary>TEST HOOK. The sprite the offered ladder is actually DRAWING (null when no
        /// ladder is standing).
        ///
        /// Asserted rather than eyeballed because this bug shipped twice over: the prop looked
        /// wired in the .asset while the runtime was drawing something else entirely, and no
        /// test could tell the difference — the only evidence was a screenshot of a child's
        /// screen. A case that reads the live renderer closes that gap.</summary>
        internal Sprite TestLadderSprite
        {
            get
            {
                if (_ladder == null)
                {
                    return null;
                }

                var sr = _ladder.GetComponent<SpriteRenderer>();
                return sr != null ? sr.sprite : null;
            }
        }

        /// <summary>TEST HOOK. The x of the grid's leftmost and rightmost cell centres, so a case
        /// can prove a prop stands ON the board rather than beside it.</summary>
        internal float TestGridMinX => CellPosition(0, 0).x;

        internal float TestGridMaxX => CellPosition(0, Mathf.Max(0, _cols - 1)).x;

        /// <summary>TEST HOOK. Fraction of this layer's tiles that have been cleared — the
        /// number the ladder's threshold is compared against.</summary>
        internal float TestClearedFraction => ClearedFraction();

        /// <summary>TEST HOOK. The configured reveal threshold, so a case can drive the board
        /// to exactly it instead of hard-coding 0.6.</summary>
        internal float TestLadderThreshold => LadderRevealFraction;

        /// <summary>TEST HOOK. Take the ladder without a tap (for cases that are asserting what
        /// the DEEP LAYER is, not how it was reached).</summary>
        internal void TestDescend() => DescendLayer();

        /// <summary>TEST HOOK. Summed break-tap hardness of every tile alive on this layer. The
        /// direct, counter-based evidence that deep dirt is older dirt — no wall clock, no
        /// eyeballing a tint.</summary>
        internal int TestHardnessSum
        {
            get
            {
                int sum = 0;
                for (int i = 0; i < _tiles.Count; i++)
                {
                    DirtTile t = _tiles[i];
                    if (t != null && !t.IsDestroyed)
                    {
                        sum += t.TestMaxHealth;
                    }
                }

                return sum;
            }
        }

        /// <summary>TEST HOOK. Summed hardness of the alive DIRT tiles only, and how many there
        /// are.
        ///
        /// The depth bonus is a DIRT rule: a toy seats its own hardness in
        /// <see cref="DirtTile.SetKind"/> (a crystal pops in one hit, a pot takes two) and depth
        /// deliberately does not touch that — a crystal that got harder to pop the deeper you
        /// went would be the game taking a toy away. So the whole-board sum moves with the
        /// board's COMPOSITION as well as with the bonus, and only these two numbers make the
        /// depth rule assertable as arithmetic.</summary>
        internal int TestDirtHardnessSum
        {
            get
            {
                int sum = 0;
                for (int i = 0; i < _tiles.Count; i++)
                {
                    DirtTile t = _tiles[i];
                    if (t != null && !t.IsDestroyed && t.Kind == DigTileKind.Dirt)
                    {
                        sum += t.TestMaxHealth;
                    }
                }

                return sum;
            }
        }

        internal int TestDirtTileCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _tiles.Count; i++)
                {
                    DirtTile t = _tiles[i];
                    if (t != null && !t.IsDestroyed && t.Kind == DigTileKind.Dirt)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        /// <summary>TEST HOOK. Strata this site may have, as the ladder reads it.</summary>
        internal int TestMaxLayers => MaxLayers;

        /// <summary>TEST HOOK. The per-layer multipliers, exactly as the generation reads them —
        /// so a case asserts the CONFIG relationship (deep is richer by the configured factor)
        /// rather than a number someone typed twice.</summary>
        internal int TestLayerHardnessBonus => LayerHardnessBonus();
        internal float TestLayerCoinMultiplier => LayerCoinMultiplier();
        internal float TestLayerTreasureWeight => LayerTreasureWeightMultiplier();
        internal int TestLayerCrystalBonus => LayerCrystalClusterBonus();
        internal float TestLayerToyChanceBonus => LayerToyChanceBonus();
        internal float TestLayerBoneChanceBonus => LayerBoneChanceBonus();
    }
}
