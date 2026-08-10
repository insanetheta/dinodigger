using System.Collections.Generic;
using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;
using DinoDigger.Overworld;

namespace DinoDigger.Dig
{
    /// <summary>
    /// GLOW THE LANTERN BOT, INSIDE THE DIG (DinoDigger-6tc). This half is the SITE's side of
    /// the friendship: where Glow sleeps, where it perches, and what its belly beam actually
    /// lights. The machine's own behaviour — dormant/awake, the wake tap, the gauge, the
    /// persistence — is <see cref="GlowBot"/>, which is an ordinary <see cref="MachineFriend"/>
    /// and inherits every discipline the three overworld machines are held to.
    ///
    /// WHY GLOW IS THE PILOT MACHINE (docs/machine-roster-eval.md ranks it BUILD NOW): it is the
    /// only friend in the roster that changes the CORE VERB. A dig tap is a lottery pull until
    /// something tells the child what is behind the dirt; Glow tells them, one tile ahead, and
    /// the tap becomes a choice. It is also the answer to the problem depth layers create — a
    /// dark stratum could read as unresponsive or scary, and the fix is not to make the dark
    /// brighter but to put a friend in it holding a lamp.
    ///
    /// THE BEAM IS INFORMATION, NEVER PROGRESS. Everything below writes ONE thing: the alpha
    /// FLOOR under a buried outline (<see cref="DirtTile.SetGlowFloor"/>). It never damages a
    /// tile, never softens one, never collects anything and never touches the buried
    /// bookkeeping. A lit board is exactly as much digging as an unlit one — it is just a board
    /// the child can make decisions about.
    ///
    /// WHERE IT SHINES, precisely:
    ///   * the 3x3 around the DEEPEST UNCLEARED cell — where the child is heading, lit at
    ///     <c>GameConfig.DigGlowPeekAlpha</c>; and
    ///   * a 3-cell CONE one tile ahead of every CRACKED tile, pointing toward the beam, lit a
    ///     little softer. That is the "one tile ahead" promise: crack something, and the lamp
    ///     shows you what is on the far side of it.
    ///
    /// ON THE BRIGHT FIRST LAYER IT DOES NOTHING AT ALL (and dims to a night-light idle): there
    /// is nothing to reveal in daylight, and a machine that helps everywhere is a machine the
    /// child never notices helping.
    /// </summary>
    public partial class DigModeController
    {
        // The bot itself, alive for as long as the SITE is (its woken flag lives in the save).
        private GlowBot _glow;

        // While dormant, the grid cell Glow is sleeping behind. -1 = not hidden (already found,
        // or already awake and perched).
        private int _glowRow = -1;
        private int _glowCol = -1;

        // Cells currently carrying a raised peek floor, so a sweep can put them all back before
        // lighting the new ones. A set, not a scan: the beam has to be able to move without
        // leaving a trail of permanently bright tiles behind it.
        private readonly HashSet<DirtTile> _glowLit = new HashSet<DirtTile>();

        private int _glowSweeps;      // test-observable
        private int _glowRevealed;    // test-observable: dormant discoveries (0 or 1 per site)

        /// <summary>TEST HOOK. Keep Glow out of the next site entirely. The twin of
        /// TestSuppressToys/Bones/Ladder: a case asserting raw peek alphas must not have a lamp
        /// quietly raising them. Cleared by the runner's between-case backstop.</summary>
        internal static bool TestSuppressGlow;

        /// <summary>True on a layer where Glow has work to do (the dark strata). The surface
        /// layer is lit by the sky and the lamp stays a night-light.</summary>
        internal bool GlowShouldBeam => _layer >= 1;

        /// <summary>The machine service (which owns every machine's gate + woken flag, and the
        /// save that carries them). Null in a bare rig, and everything here tolerates that.</summary>
        private MachineFriendController Machines =>
            GameManager.Instance != null ? GameManager.Instance.Machines : null;

        /// <summary>Has the child already woken Glow in some earlier dig (or session)?</summary>
        private bool GlowAlreadyWoken()
        {
            MachineFriendController mf = Machines;
            return mf != null && mf.IsWoken(MachineKind.Glow);
        }

        // ------------------------------------------------------ build / rebuild

        /// <summary>Put Glow where this layer needs it. Called at the end of every grid build,
        /// which is what makes "Glow follows you between layers" true by construction: an awake
        /// lantern is simply re-perched on the new board rather than rebuilt, and a still-sleeping
        /// one is re-hidden behind one of the new layer's tiles.</summary>
        private void RefreshGlow()
        {
            // The lit set belongs to the board that has just been thrown away: drop it before
            // anything else, so no reference to a destroyed tile can outlive the layer.
            _glowLit.Clear();
            _glowSweeps = 0;
            _glowRevealed = 0;

            if (TestSuppressGlow)
            {
                DespawnGlow();
                return;
            }

            bool woken = GlowAlreadyWoken();

            if (_glow == null)
            {
                // DISCOVERY GATE: a lantern bot is found in the dark, and the gate that says so
                // is the machine service's — the same one that gates Sprinkles behind a harvest
                // and Tuggy behind a duck. Before the first descent there is no Glow anywhere:
                // not dimmed, not hidden, not present. (With no service wired — a bare rig — the
                // gate is treated as open, because the alternative is a friend that can never be
                // found at all.)
                bool gated = Machines == null || Machines.IsGated(MachineKind.Glow);
                if (!woken && (_layer < 1 || !gated))
                {
                    return;
                }

                SpawnGlow(woken);
                return;
            }

            if (_glow.IsAwake)
            {
                _glowRow = -1;
                _glowCol = -1;
                _glow.PerchAt(GlowPerchPoint());
            }
            else
            {
                HideGlowBehindTile();
            }

            ApplyGlowLight();
        }

        /// <summary>Build the bot. Awake (the child has met it before) it lands straight on its
        /// perch; asleep it is tucked behind a random tile of this layer, glinting through the
        /// dirt until the tile above it comes away.</summary>
        private void SpawnGlow(bool awake)
        {
            var go = new GameObject("Glow");
            go.transform.SetParent(_root != null ? _root : transform, false);

            // The ROOT sits at scale 1 with the art on a CHILD — the same shape every machine is
            // built in, which is what makes MachineFriend.RestingScale trivially safe.
            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(go.transform, false);
            var body = bodyGo.AddComponent<SpriteRenderer>();
            body.sortingOrder = MachineFriend.MachineSorting;

            // THIS IS THE BUG GREG FOUND (DinoDigger-n05). With no art wired this drew on the
            // STAR PARTICLE — so what a child actually saw, perched off the right-hand edge of
            // the pit next to the dig crew, was a giant gold smiley STAR with the (mound-sprite)
            // charge gauge stretched into a little dirt pile under it: an unexplained,
            // collectible-looking object sitting outside the board. Glow now loads its own
            // lantern-bot art through the same typed roster slot its three cousins use.
            //
            // The fallback chain is kept (a friend must never become an invisible hole in the
            // world) but it is now the LAST resort rather than the shipped look.
            Sprite art = _lib != null ? _lib.Machine((int)MachineKind.Glow) : null;
            if (art == null && _lib != null)
            {
                art = _lib.StarParticle != null ? _lib.StarParticle : _lib.MoundSprite;
            }

            body.sprite = art;
            if (art != null && art.bounds.size.y > 0.001f)
            {
                float k = GlowBot.BodyHeight / art.bounds.size.y;
                bodyGo.transform.localScale = new Vector3(k, k, 1f);
                bodyGo.transform.localPosition = new Vector3(0f, GlowBot.BodyHeight * 0.5f, 0f);
            }

            ParticleSystem sparkle = GameManager.Instance != null
                ? GameManager.Instance.MachineCreateParticles(go.transform,
                    _lib != null ? _lib.StarParticle : null, new Color(1f, 0.93f, 0.6f), 0.3f)
                : null;

            _glow = go.AddComponent<GlowBot>();
            _glow.BuildOverlays(_lib, body, GlowBot.BodyHeight, sparkle);
            _glow.Attach(this);
            // Real art is shown as painted (MachineFriend's own convention: white for imported
            // art, the machine's signature colour only when it is running on the blob fallback).
            _glow.Configure(Machines, _config, _lib,
                art != null && _lib != null && _lib.MachineGlow == art ? Color.white : GlowBot.LanternTint,
                awake);

            if (awake)
            {
                _glow.PerchAt(GlowPerchPoint());
            }
            else
            {
                HideGlowBehindTile();
            }

            ApplyGlowLight();
        }

        /// <summary>Tuck the sleeping bot behind a random tile of this layer: it stands at that
        /// cell, renders BEHIND the dirt and answers no taps, so all the child can see is a soft
        /// repeating glint coming out of the wall. That is the discovery beat from the bible —
        /// "a soft glow behind dirt; breaking its tile frees it".</summary>
        private void HideGlowBehindTile()
        {
            if (_glow == null || _glow.IsAwake)
            {
                return;
            }

            DirtTile host = RandomPlainTile();
            if (host == null)
            {
                // Nowhere to hide (a board with no plain dirt left): stand it on the pit floor
                // rather than leaving it nowhere — a found friend is never worse than no friend.
                _glowRow = -1;
                _glowCol = -1;
                _glow.PerchAt(GlowPerchPoint());
                _glow.SetCovered(false);
                return;
            }

            _glowRow = host.Row;
            _glowCol = host.Col;
            _glow.transform.position = host.transform.position;
            _glow.SetCovered(true);
        }

        /// <summary>The tile Glow sleeps behind has come away: it is visible and tappable now.
        /// Checked on the settle tail, so however the tile went — a bite, a cascade, a mushroom's
        /// fling, a geode — the discovery lands on the same beat.</summary>
        private void MaybeRevealGlow()
        {
            if (_glow == null || _glow.IsAwake || _glowRow < 0)
            {
                return;
            }

            if (TileAt(_glowRow, _glowCol) != null)
            {
                return; // still buried (a tile fell into the cell: it is hidden again, and that
                        // is fine — the glint keeps going and the child digs it out again)
            }

            _glowRevealed++;
            _glow.SetCovered(false);
            _glow.Stretch();
        }

        /// <summary>Where an awake Glow perches: on the pit's right-hand edge, low, clear of the
        /// excavator's traverse on the left and below the buddy crew on the surface. Deliberately
        /// OUT of the grid — Glow is a light source, not an actor in the puzzle
        /// (docs/machine-roster-eval.md's one warning about it), and nothing it does may ever
        /// compete with a tap meant for a tile.</summary>
        private Vector3 GlowPerchPoint()
        {
            return _origin + new Vector3(_gridHalfW + 1.3f, -1.6f, 0f);
        }

        /// <summary>The perch, for the bot itself (it hops here the moment it wakes).</summary>
        internal Vector3 GlowPerch => GlowPerchPoint();

        private void DespawnGlow()
        {
            if (_glow != null)
            {
                Destroy(_glow.gameObject);
                _glow = null;
            }

            _glowRow = -1;
            _glowCol = -1;
            _glowLit.Clear();
        }

        // ------------------------------------------------------------- the beam

        /// <summary>One sweep of the belly beam (driven by GlowBot's own timer, and again on
        /// every settle so the light never lags the board).</summary>
        internal void GlowSweep()
        {
            _glowSweeps++;
            ApplyGlowLight();
        }

        /// <summary>Re-light the board: put every previously lit cell back, then light the beam
        /// zone and the crack cones.
        ///
        /// RELEASE FIRST, ALWAYS. The floor is a FLOOR, so a cell the beam has moved off must be
        /// released or the pit would slowly turn into a fully-revealed board — which would end
        /// the game's only tension. Releasing never dims a hint the child earned by cracking the
        /// tile: <see cref="DirtTile.SetGlowFloor"/> only ever competes with the resting alpha,
        /// never with the crack curve.</summary>
        private void ApplyGlowLight()
        {
            foreach (DirtTile t in _glowLit)
            {
                if (t != null)
                {
                    t.SetGlowFloor(0f);
                }
            }

            _glowLit.Clear();

            if (_grid == null || _glow == null || !_glow.IsAwake || !GlowShouldBeam || !_open)
            {
                return;
            }

            if (!TryDeepestUnclearedCell(out int beamRow, out int beamCol))
            {
                return;
            }

            float beamAlpha = _config != null ? Mathf.Clamp01(_config.DigGlowPeekAlpha) : 0.85f;
            float coneAlpha = _config != null ? Mathf.Clamp01(_config.DigGlowConePeekAlpha) : 0.7f;

            // Cones FIRST, beam second: where the two overlap the brighter beam wins, and doing
            // it in this order means that is true without any per-cell bookkeeping.
            LightCracksToward(beamRow, beamCol, coneAlpha);

            for (int dr = -1; dr <= 1; dr++)
            {
                for (int dc = -1; dc <= 1; dc++)
                {
                    LightCell(beamRow + dr, beamCol + dc, beamAlpha);
                }
            }
        }

        /// <summary>The 3-cell cone one tile ahead of every cracked tile, pointing at the beam.
        /// This is the part that turns dig taps into informed choices: the child cracks a tile
        /// and the lamp shows them what is on the OTHER side of it, before they commit the next
        /// tap.</summary>
        private void LightCracksToward(int beamRow, int beamCol, float alpha)
        {
            for (int i = 0; i < _tiles.Count; i++)
            {
                DirtTile t = _tiles[i];
                if (t == null || t.IsDestroyed || !t.IsCracked)
                {
                    continue;
                }

                int dr = (int)Mathf.Sign(beamRow - t.Row);
                int dc = (int)Mathf.Sign(beamCol - t.Col);
                if (beamRow == t.Row)
                {
                    dr = 0;
                }

                if (beamCol == t.Col)
                {
                    dc = 0;
                }

                if (dr == 0 && dc == 0)
                {
                    continue; // the cracked tile IS the beam cell; the beam covers it anyway
                }

                if (dr != 0 && dc != 0)
                {
                    // Diagonal: the cell ahead plus the two that shoulder it.
                    LightCell(t.Row + dr, t.Col + dc, alpha);
                    LightCell(t.Row + dr, t.Col, alpha);
                    LightCell(t.Row, t.Col + dc, alpha);
                }
                else if (dr != 0)
                {
                    // Straight up/down: the row ahead, three wide.
                    LightCell(t.Row + dr, t.Col - 1, alpha);
                    LightCell(t.Row + dr, t.Col, alpha);
                    LightCell(t.Row + dr, t.Col + 1, alpha);
                }
                else
                {
                    // Straight across: the column ahead, three tall.
                    LightCell(t.Row - 1, t.Col + dc, alpha);
                    LightCell(t.Row, t.Col + dc, alpha);
                    LightCell(t.Row + 1, t.Col + dc, alpha);
                }
            }
        }

        private void LightCell(int r, int c, float alpha)
        {
            DirtTile t = TileAt(r, c);
            if (t == null || t.IsDestroyed)
            {
                return;
            }

            t.SetGlowFloor(alpha);
            _glowLit.Add(t);
        }

        /// <summary>The deepest cell that still has dirt in it — where the digging is heading,
        /// and therefore where a lamp is worth pointing. Ties break toward the middle of the
        /// board so the beam sits under the pit rather than in a corner.</summary>
        private bool TryDeepestUnclearedCell(out int row, out int col)
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
                    DirtTile t = TileAt(r, c);
                    if (t == null || t.IsDestroyed)
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

        // ------------------------------------------------------------ TEST HOOKS

        /// <summary>TEST HOOK. The lantern bot in this site (null = it has not been met, or the
        /// site is on the bright layer of a game where it was never found).</summary>
        internal GlowBot TestGlow => _glow;

        /// <summary>TEST HOOK. Is Glow present and awake right now?</summary>
        internal bool TestGlowAwake => _glow != null && _glow.IsAwake;

        /// <summary>TEST HOOK. The cell a dormant Glow is sleeping behind (-1 when none).</summary>
        internal int TestGlowRow => _glowRow;
        internal int TestGlowCol => _glowCol;

        /// <summary>TEST HOOK. Beam sweeps run, and dormant discoveries made, this site.</summary>
        internal int TestGlowSweeps => _glowSweeps;
        internal int TestGlowRevealed => _glowRevealed;

        /// <summary>TEST HOOK. Cells the beam is lighting right now — the direct measure of
        /// "the lamp is on and pointing somewhere".</summary>
        internal int TestGlowLitCells => _glowLit.Count;

        /// <summary>TEST HOOK. Is the cell at r,c inside the beam/cone right now?</summary>
        internal bool TestGlowLits(int r, int c)
        {
            DirtTile t = TileAt(r, c);
            return t != null && _glowLit.Contains(t);
        }

        /// <summary>TEST HOOK. Wake Glow without hunting for its tile and tapping it — for cases
        /// asserting what the BEAM does rather than how the bot was found. Goes through the real
        /// <see cref="MachineFriend.WakeUp"/>, so the save is written exactly as a tap would.</summary>
        internal void TestWakeGlow()
        {
            if (_glow == null)
            {
                return;
            }

            _glow.SetCovered(false);
            _glow.WakeUp();
            ApplyGlowLight();
        }

        /// <summary>TEST HOOK. The deepest uncleared cell, encoded as row * 1000 + col (-1 when
        /// the board is empty) — so a case can compute the beam zone the same way the beam does
        /// instead of hard-coding a cell the generator might not produce.</summary>
        internal int TestGlowBeamCell =>
            TryDeepestUnclearedCell(out int r, out int c) ? r * 1000 + c : -1;
    }
}
