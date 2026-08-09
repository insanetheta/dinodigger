using System;
using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;

namespace DinoDigger.Dig
{
    /// <summary>
    /// One chunky dirt tile in the dig grid. Takes damage across 3 crack states
    /// and crumbles away. May hide a buried item that "peeks" through once cracked.
    /// </summary>
    public class DirtTile : MonoBehaviour, ITappable
    {
        private SpriteRenderer _dirt;
        private SpriteRenderer _peek;
        private ParticleSystem _crumbs;
        private DigModeController _owner;
        private PlaceholderLibrary _lib;
        private GameConfig _config;

        private int _maxHealth = 3;
        private int _damage;
        private bool _destroyed;
        private Color _peekTint = Color.white;
        private Color _dirtTint = Color.white; // theme multiply (Dig Postcards)
        // Resting buried-peek alpha (raised by the Stegosaurus "treasure map" power so
        // the hint reads brighter all round). Default 0.55 = the baseline buried hint.
        private float _restPeekAlpha = 0.55f;

        // Surprise Pocket: the site's one mystery tile wiggles gently FOREVER (a small
        // looping sway on top of the crack sprite) so a toddler can spot it. It never
        // shows a buried peek; the surprise fires when it is fully cleared (owner side).
        private bool _isSurprise;
        private bool _wiggling;
        private float _wigglePhase;
        private const float WiggleRate = 3.2f;    // sway speed (rad/s)
        private const float WiggleDegrees = 7f;   // sway amplitude

        // ---- Gravity cascade (Dig Loop 2.0) ---------------------------------
        // A falling tile is LOGICALLY already at its new cell — DigModeController's
        // settle loop resolves the whole board synchronously and then hands each mover
        // a travel tween. So this is pure travel: the peek/pocket/collider all ride the
        // same transform, and the owner drops taps aimed at a tile while IsFalling is
        // true (it lands within ~0.3s and is tappable again — a tap is never stolen,
        // just ignored). RESTING SCALE: the landing squash and the Damage punch both
        // write scale, so both go through the base captured ONCE at Build — reading the
        // live localScale mid-squash is what compounds a tile into a giant blueberry.
        private Coroutine _fall;
        private Coroutine _squash;
        private bool _falling;
        private Vector3 _restScale = Vector3.one;

        // MOTION IS CONFIG, NOT CONSTANTS (DinoDigger-73a). Squash depth/recovery and the fall
        // curve are read from GameConfig at the moment they are USED, never cached into a field
        // at Build time, so dragging a slider in play mode retunes the very next landing. The
        // fallbacks below are only for a tile built with no config (a legacy/bare scene).
        private const float DefaultSquashTime = 0.22f;
        private const float DefaultSquashAmount = 0.18f;

        // ---- Dig toys (Dig Loop 2.0) -----------------------------------------
        // A toy is still a DirtTile: same collider, same gravity, same clear chokepoint. Only
        // the art, the hardness and what happens when it breaks differ, which is what keeps the
        // cascade engine completely unaware that crystals or geodes exist.
        private DigTileKind _kind = DigTileKind.Dirt;
        private int _crystalColor;
        private bool _geodeArmed;   // a geode takes no damage: its FIRST hit lights the fuse

        // BOUNCY MUSHROOM (DinoDigger-u47). Modelled on the geode's interception, for the same
        // reason: the FIRST thing that hits a mushroom is answered by the mushroom rather than
        // by the damage counter, so "the bite boings off" is one branch instead of four
        // (tap / crew clear / landing crack / fling all arrive through Damage). The SECOND hit
        // falls through and pops it — the mushroom is 1-hardness, so the bounce flag is
        // effectively its first hit point.
        private bool _bounced;

        // WASHED BY A WATER POCKET (DinoDigger-u47). Set when a gush softens this tile down to
        // its last remaining hit, cleared the moment it takes a real one. It exists for one
        // reason: a washed tile is a 1-hit tile, and the gush's OWN cascade lands on it a
        // heartbeat later — so without this the water would routinely finish the very tiles it
        // softened, which is the one thing a water pocket must never do (it softens, it never
        // digs). The cascade skips a washed victim exactly as it already skips a crystal and the
        // surprise pocket, and for the same reason: those are the child's tap to take.
        private bool _washed;

        // GLOW'S BEAM (DinoDigger-6tc). A floor under the buried-peek alpha, raised while this
        // cell is lit by the lantern bot and dropped when the beam sweeps away. It is a FLOOR,
        // never an assignment: a tile the child has already cracked open is showing a brighter
        // hint of its own and the lamp must not dim it.
        private float _glowFloor;

        // Fallback tints for the three crystal colours, used only when the generated crystal art
        // has not been imported (the dirt sprite is tinted instead). Deliberately vivid: the
        // colour is the ONLY thing a child has to match, so it must read at a glance either way.
        private static readonly Color[] CrystalTints =
        {
            new Color(0.35f, 0.85f, 0.85f),  // teal
            new Color(1.00f, 0.55f, 0.45f),  // coral
            new Color(1.00f, 0.82f, 0.30f),  // gold
        };

        // Signature tints for the wave-2 toys (DinoDigger-u47). Vivid and unmistakable for the
        // same reason the crystal fallbacks are: while these run on borrowed art, the COLOUR is
        // the only thing telling a toddler this cell is not ordinary dirt.
        private static readonly Color WaterTint = new Color(0.38f, 0.66f, 1.00f);
        private static readonly Color VeinTint = new Color(0.80f, 0.55f, 1.00f);
        private static readonly Color MushroomTint = new Color(1.00f, 0.42f, 0.45f);

        /// <summary>The splash/spark colour a wave-2 toy throws, so the dig's particle bursts
        /// and the tile itself can never drift apart.</summary>
        public static Color KindTint(DigTileKind kind)
        {
            switch (kind)
            {
                case DigTileKind.Water: return WaterTint;
                case DigTileKind.Vein: return VeinTint;
                case DigTileKind.Mushroom: return MushroomTint;
                default: return Color.white;
            }
        }

        public int Row { get; private set; }
        public int Col { get; private set; }
        public bool HasItem { get; private set; }
        public bool IsSurprise => _isSurprise;
        public bool IsDestroyed => _destroyed;

        /// <summary>True while this tile sits ON one cell of a buried fossil bone
        /// (DinoDigger-0z5) and is showing that cell's bone-ish peek.
        ///
        /// A bone is NOT a buried item: it belongs to the CELL, not to the tile, so this flag
        /// travels with whatever tile currently occupies a bone cell — the cascade re-seats it
        /// after every settle. That is why it is its own flag instead of <see cref="HasItem"/>:
        /// the buried bookkeeping keys off the TILE and must never gain a phantom entry, and a
        /// bone cell must never also be pickable loot (site generation keeps the two layers
        /// apart, exactly as it keeps toys and items apart).</summary>
        public bool CoversBone { get; private set; }

        /// <summary>What this cell is: plain dirt, a crystal, a boom geode or a pinata pot.</summary>
        public DigTileKind Kind => _kind;

        /// <summary>Crystal colour index (0 teal / 1 coral / 2 gold). Meaningless for other
        /// kinds; the flood fill only ever compares it between two crystals.</summary>
        public int CrystalColor => _crystalColor;

        /// <summary>True once a geode has been hit and its fuse is burning — a second hit must
        /// not re-light it (that would double the boom).</summary>
        public bool IsGeodeArmed => _geodeArmed;

        /// <summary>The colour a crystal of <paramref name="color"/> reads as, for its sparkle
        /// burst and for the art-less fallback tint.</summary>
        public static Color CrystalTint(int color)
        {
            return CrystalTints[Mathf.Clamp(color, 0, CrystalTints.Length - 1)];
        }

        /// <summary>True while this tile is travelling to a cell the cascade already moved
        /// it to. Taps on it are ignored (it lands first) and tests pace to it.</summary>
        public bool IsFalling => _falling;

        /// <summary>True once this tile has taken at least one hit and is showing cracks. Read by
        /// Glow's beam (DinoDigger-6tc), which throws its preview cone one tile AHEAD of a crack —
        /// a cracked tile is where the child is working, so it is where a preview is worth
        /// having.</summary>
        public bool IsCracked => _damage > 0;

        /// <summary>True while this tile is sitting on the floor a water pocket washed it down to
        /// (one hit left) and has not been hit since. The gravity cascade does not damage a
        /// washed tile — see <see cref="_washed"/>.</summary>
        public bool IsWashed => _washed && !_destroyed;

        // TEST HOOKS for the integration runner (damage progression + peek visibility).
        internal int TestDamage => _damage;
        internal int TestMaxHealth => _maxHealth;
        internal Sprite TestDirtSprite => _dirt != null ? _dirt.sprite : null;
        internal Color TestDirtColor => _dirt != null ? _dirt.color : Color.white;
        internal bool TestPeekEnabled => _peek != null && _peek.enabled;
        internal float TestPeekAlpha => _peek != null ? _peek.color.a : 0f;
        internal bool TestIsSurprise => _isSurprise;
        internal DigTileKind TestKind => _kind;
        internal bool TestCoversBone => CoversBone;

        /// <summary>TEST HOOK. Re-seat this tile's max health (clamped >= 1) and reset its
        /// damage, refreshing the crack sprite, so a test can verify the proportional
        /// crack-state mapping at maxHealth != 3.</summary>
        internal void TestSetMaxHealth(int maxHealth)
        {
            _maxHealth = Mathf.Max(1, maxHealth);
            _damage = 0;
            _destroyed = false;
            RefreshSprite();
        }

        public void Build(DigModeController owner, PlaceholderLibrary lib, GameConfig config,
            int row, int col, int maxHealth, ParticleSystem crumbs)
        {
            _owner = owner;
            _lib = lib;
            _config = config;
            Row = row;
            Col = col;
            _maxHealth = Mathf.Max(1, maxHealth);
            _crumbs = crumbs;
            _restScale = transform.localScale; // build-time pose = the one true resting scale

            _dirt = gameObject.GetComponent<SpriteRenderer>();
            if (_dirt == null)
            {
                _dirt = gameObject.AddComponent<SpriteRenderer>();
            }

            _dirt.sortingOrder = 10;
            RefreshSprite();

            // Peek child renders just IN FRONT of the dirt (higher sorting order) so
            // a faint hint of the buried item shows through it. Sitting behind the
            // opaque dirt (the old order 8 < 10) meant it never rendered at all.
            var peekGo = new GameObject("Peek");
            peekGo.transform.SetParent(transform, false);
            _peek = peekGo.AddComponent<SpriteRenderer>();
            _peek.sortingOrder = 11;
            _peek.enabled = false;
        }

        /// <summary>Apply the dig theme's dirt tint (a MULTIPLY over the crack sprites).
        /// Called by DigModeController.BuildGrid; re-applied on every RefreshSprite so a
        /// fresh crack state keeps the tint.</summary>
        public void SetDirtTint(Color tint)
        {
            _dirtTint = tint;
            ApplyTint();
        }

        /// <summary>Turn this cell into a dig toy (or back into plain dirt): art, hardness and
        /// break behaviour in one call. Site generation and the demo/test hooks both go through
        /// here, so a hand-placed crystal is indistinguishable from a rolled one.
        ///
        /// A toy NEVER also hides a buried item — the caller (site gen) picks from item-free
        /// tiles — so this clears any peek defensively rather than leaving a hint glowing under
        /// art that will never be dug out of.
        ///
        /// Hardness is the toy's whole difficulty story: a crystal is 1 (it pops), a geode is 1
        /// (its first crack lights the fuse, and it never actually takes the hit), a pot is 2
        /// (crack, then break). Dirt keeps whatever the theme rolled.</summary>
        public void SetKind(DigTileKind kind, int crystalColor)
        {
            _kind = kind;
            _crystalColor = Mathf.Clamp(crystalColor, 0, CrystalTints.Length - 1);
            _geodeArmed = false;
            _bounced = false;
            _washed = false;
            _damage = 0;

            switch (kind)
            {
                case DigTileKind.Crystal:
                case DigTileKind.Geode:
                    _maxHealth = 1;
                    break;
                case DigTileKind.Pot:
                    _maxHealth = 2;
                    break;

                // WAVE 2 (DinoDigger-u47). All three are one-hit tiles, and each spends that
                // hit differently: the water pocket bursts, a vein segment sparks its whole run,
                // and the mushroom's first bite is eaten by the BOUNCE (see _bounced) so it
                // takes two bites in practice without ever taking two points of damage.
                case DigTileKind.Water:
                case DigTileKind.Vein:
                case DigTileKind.Mushroom:
                    _maxHealth = 1;
                    break;
            }

            if (kind != DigTileKind.Dirt)
            {
                HasItem = false;
                CoversBone = false; // a toy cell is never also a bone cell (see site generation)
                if (_peek != null)
                {
                    _peek.enabled = false;
                }
            }

            RefreshSprite();
        }

        public void SetPeek(Sprite itemSprite, Color tint)
        {
            HasItem = true;
            _peekTint = tint;
            if (_peek != null)
            {
                _peek.sprite = itemSprite;
                _peek.transform.localScale = Vector3.one * 0.7f;
                _peek.enabled = true;
                // Clear color hint visible from the start (2x boosted per playtest
                // feedback); strengthens further as the dirt cracks — and rides whatever
                // floor Glow's beam has this cell on (see ApplyPeekAlpha).
                ApplyPeekAlpha();
            }
        }

        /// <summary>This tile is standing on one cell of a buried bone: show the same peek the
        /// buried-item layer uses, in a bone-ish tint, so the child reads "something is under
        /// here" through the cracks exactly as they do for loot.
        ///
        /// Deliberately REUSES the single peek renderer rather than adding a second one: a bone
        /// cell never also hides an item (site generation places bones before items and items
        /// skip bone cells), so the two can never want the renderer at the same time — and the
        /// owner re-seats bone peeks after every settle, which is what keeps the hint on
        /// whichever tile has fallen into the cell. No-op on a tile that hides an item or is a
        /// toy: those have their own art and it wins.</summary>
        public void SetBonePeek(Sprite boneSprite, Color tint)
        {
            if (HasItem || _isSurprise || _kind != DigTileKind.Dirt)
            {
                return;
            }

            CoversBone = true;
            _peekTint = tint;
            if (_peek != null)
            {
                _peek.sprite = boneSprite;
                _peek.transform.localScale = Vector3.one * 0.7f;
                _peek.enabled = boneSprite != null;
                ApplyPeekAlpha();
            }
        }

        /// <summary>WATER POCKET (DinoDigger-u47). This tile's buried item has floated up to the
        /// tile above: drop the secret and the hint together. The owner moves the
        /// <c>_buried</c> entry in the same breath, so "the map says this tile hides something"
        /// and "this tile is glowing" can never disagree for even one frame.
        ///
        /// Leaves a BONE cell's hint alone — that hint belongs to the cell, not to the tile, and
        /// the owner re-seats it after every settle.</summary>
        public void ClearItem()
        {
            if (!HasItem)
            {
                return;
            }

            HasItem = false;
            if (_peek != null && !CoversBone)
            {
                _peek.sprite = null;
                _peek.enabled = false;
            }
        }

        /// <summary>This tile has moved OFF the bone cell it was covering (or the bone popped):
        /// drop the hint. Never touches a buried-item peek — only a tile that is actually
        /// flagged as bone-covering can be cleared here.</summary>
        public void ClearBonePeek()
        {
            if (!CoversBone)
            {
                return;
            }

            CoversBone = false;
            if (_peek != null && !HasItem)
            {
                _peek.enabled = false;
            }
        }

        /// <summary>Stegosaurus "treasure map": briefly flash the buried-item peek up to
        /// <paramref name="flashAlpha"/>, then settle it at <paramref name="settleAlpha"/>
        /// (brighter than the default buried hint) so it reads clearly for the rest of the
        /// round. No-op on a plain (unburied) tile.</summary>
        public void FlashPeek(float flashAlpha, float settleAlpha)
        {
            if (!HasItem || _peek == null)
            {
                return;
            }

            _restPeekAlpha = settleAlpha;
            Tween.Run(0.6f, t =>
            {
                if (_peek == null)
                {
                    return;
                }

                float a = Mathf.Lerp(flashAlpha, settleAlpha, t);
                _peek.color = new Color(_peekTint.r, _peekTint.g, _peekTint.b, a);
            });
        }

        /// <summary>Mark this tile as the site's Surprise Pocket: it wiggles gently forever
        /// so a toddler can spot it. Marking never adds a buried peek (a surprise tile is
        /// always chosen from the non-item tiles), so the wiggle is the only hint.</summary>
        public void MarkSurprise()
        {
            _isSurprise = true;
            _wiggling = true;
            _wigglePhase = UnityEngine.Random.value * Mathf.PI * 2f;
        }

        // ---- Gravity cascade -------------------------------------------------

        /// <summary>The cascade moved this tile to a new grid cell. Bookkeeping only — the
        /// visual travel is <see cref="FallTo"/>, which may still be in the air behind it.</summary>
        internal void SetCell(int row, int col)
        {
            Row = row;
            Col = col;
        }

        /// <summary>Travel to the world position of the cell the cascade just moved this tile
        /// into: hold for <paramref name="delay"/> (the per-row stagger that makes a column
        /// read as a tumble rather than a lift), then accelerate down over
        /// <paramref name="duration"/> and land with a squash-bounce.
        /// <paramref name="onLanded"/> is the owner's landing flourish (dust + thump) and MUST
        /// prove its site is still current — this fires from a tween that outlives a close.
        /// Re-falling mid-flight (a chained cascade) simply retargets from wherever the tile
        /// is right now, so a tile never teleports.</summary>
        internal void FallTo(Vector3 target, float delay, float duration, Action onLanded)
        {
            if (this == null || _destroyed)
            {
                return;
            }

            if (_fall != null)
            {
                Tween.Stop(_fall);
                _fall = null;
            }

            _falling = true;
            Vector3 from = transform.position;
            float total = Mathf.Max(0.01f, delay + duration);
            float hold = Mathf.Clamp01(delay / total);
            _fall = Tween.Run(total, t =>
            {
                if (this == null)
                {
                    return;
                }

                float u = hold >= 1f ? 1f : Mathf.Clamp01((t - hold) / (1f - hold));
                // Shape read from config EVERY FRAME (DinoDigger-73a): switching FallEase in the
                // inspector re-curves even the falls already in the air.
                float e = _config != null ? _config.DigFallCurve(u) : u * u;
                transform.position = Vector3.LerpUnclamped(from, target, e);
            }, () =>
            {
                if (this == null)
                {
                    return;
                }

                _fall = null;
                _falling = false;
                transform.position = target;
                Squash();
                onLanded?.Invoke();
            });
        }

        /// <summary>Chunky landing: widen and flatten off the resting scale, then spring back
        /// to it exactly. Hands the scale over from any in-flight Damage punch first so the two
        /// never interleave (and so no punch can ever capture a squashed scale as its base).</summary>
        private void Squash()
        {
            Tween.CancelPunch(transform);
            EndSquash();

            // Read at IMPACT TIME, not at build time (DinoDigger-73a): a tweak to the squash
            // knobs shows up on the very next tile that lands.
            float time = _config != null
                ? Mathf.Clamp(_config.DigSquashRecoverSeconds, 0.01f, 2f)
                : DefaultSquashTime;
            float amount = _config != null
                ? Mathf.Clamp(_config.DigSquashAmplitude, 0f, 0.9f)
                : DefaultSquashAmount;

            _squash = Tween.Run(time, t =>
            {
                if (this == null)
                {
                    return;
                }

                float e = Mathf.Sin(t * Mathf.PI) * (1f - t); // impact spike, decaying to rest
                transform.localScale = new Vector3(
                    _restScale.x * (1f + amount * e),
                    _restScale.y * (1f - amount * e),
                    _restScale.z);
            }, () =>
            {
                if (this != null)
                {
                    _squash = null;
                    transform.localScale = _restScale;
                }
            });
        }

        /// <summary>Stop any running squash and put the tile back on its resting scale, so
        /// whatever writes the scale next starts from a clean pose.</summary>
        private void EndSquash()
        {
            if (_squash != null)
            {
                Tween.Stop(_squash);
                _squash = null;
                transform.localScale = _restScale;
            }
        }

        private void Update()
        {
            if (!_wiggling || _destroyed)
            {
                return;
            }

            // Gentle looping sway (rotation only — independent of the Damage punch-scale).
            _wigglePhase += Time.deltaTime * WiggleRate;
            float ang = Mathf.Sin(_wigglePhase) * WiggleDegrees;
            transform.localRotation = Quaternion.Euler(0f, 0f, ang);
        }

        public void OnTapped(Vector2 worldPoint)
        {
            _owner?.OnTileTapped(this);
        }

        /// <summary>Apply one hit. Returns true when this hit destroys the tile.
        ///
        /// A BOOM GEODE never takes a hit at all: the first thing that touches it — a tap, a
        /// tile landing on it, a crew clear — lights its fuse instead and reports "not
        /// destroyed", so the geode is still standing (and still sparkling) right up until it
        /// goes off. That single interception is what makes "tapping OR uncovering-by-crack
        /// triggers it" one code path rather than four.</summary>
        public bool Damage()
        {
            if (_destroyed)
            {
                return false;
            }

            if (_kind == DigTileKind.Geode)
            {
                if (!_geodeArmed)
                {
                    _geodeArmed = true;
                    _owner?.OnGeodeArmed(this);
                }

                return false;
            }

            // A BOUNCY MUSHROOM eats its first hit exactly the way a geode does, and for the
            // same architectural reason: whatever hit it — the bucket, a crew clear, a tile
            // landing on it — the answer is the BOING, decided here once. It reports "still
            // standing" (it is), the owner flings the dirt, and the NEXT hit falls through to
            // the ordinary damage path below and pops it.
            if (_kind == DigTileKind.Mushroom && !_bounced)
            {
                _bounced = true;
                _owner?.OnMushroomBounced(this);
                return false;
            }

            // A real hit spends the wash: whatever softened this tile, it has now been dug, and
            // from here it is an ordinary tile again (a fresh gush may soften it once more).
            _washed = false;

            _damage++;
            if (_crumbs != null)
            {
                _crumbs.transform.position = transform.position;
                _crumbs.Emit(10);
            }

            EndSquash(); // never let a punch capture a mid-squash scale as its base
            Tween.PunchScale(transform, 0.18f, 0.18f);

            if (_damage >= _maxHealth)
            {
                Crumble();
                return true;
            }

            RefreshSprite();
            RevealPeek();
            return false;
        }

        private void RevealPeek()
        {
            // The hint is visible from the start; brighten it as the dirt cracks. A BONE cell's
            // peek brightens on exactly the same curve — from the child's side "there is
            // something under this tile" is one idea, whether it turns out to be loot or a
            // piece of a skeleton.
            ApplyPeekAlpha();
        }

        /// <summary>THE one place the buried hint's alpha is decided (DinoDigger-6tc). Three
        /// inputs, combined by MAX rather than by "whoever wrote last":
        ///
        ///   the resting hint  — 0.55 by default, raised for good by the Stegosaurus map;
        ///   the crack curve   — a cracked tile shows more of what it hides;
        ///   Glow's beam floor — the lantern lifting an outline it is shining at.
        ///
        /// Taking the max is what lets the lamp sweep away without ever DIMMING a hint the
        /// child has already earned by cracking the tile.</summary>
        private void ApplyPeekAlpha()
        {
            if (_peek == null || (!HasItem && !CoversBone))
            {
                return;
            }

            float baseAlpha = _damage > 0
                ? Mathf.Lerp(0.7f, 1f, (float)_damage / Mathf.Max(1, _maxHealth))
                : _restPeekAlpha;
            float a = Mathf.Max(baseAlpha, _glowFloor);

            // Only ever turns the hint ON (a renderer with no sprite draws nothing either way):
            // hiding a peek belongs to Crumble / ClearBonePeek, which own the exit paths.
            if (_peek.sprite != null)
            {
                _peek.enabled = true;
            }

            _peek.color = new Color(_peekTint.r, _peekTint.g, _peekTint.b, a);
        }

        /// <summary>GLOW'S BEAM (DinoDigger-6tc). Raise (or release) the alpha floor under this
        /// cell's buried outline. Pure rendering: it never touches the tile's damage, its
        /// hardness or the buried bookkeeping, so a lit tile is still exactly as much work to
        /// dig as an unlit one — the lamp sells INFORMATION, never progress.</summary>
        public void SetGlowFloor(float alpha)
        {
            float next = Mathf.Clamp01(alpha);
            if (Mathf.Approximately(next, _glowFloor))
            {
                return;
            }

            _glowFloor = next;
            ApplyPeekAlpha();
        }

        /// <summary>WATER POCKET (DinoDigger-u47). Wash this tile down to ONE remaining hit,
        /// whatever hardness it rolled, and return true if that actually changed anything.
        ///
        /// Deliberately never destroys: the gush makes the column soft, it does not dig it for
        /// the child. And deliberately DIRT ONLY — a toy has its own promise (a crystal is the
        /// child's to pop, a geode's fuse is its own event, a pot pays on ITS break) and water
        /// running over it must not quietly rewrite that.</summary>
        public bool WashSoft()
        {
            if (_destroyed || _kind != DigTileKind.Dirt || _maxHealth <= 1 || _damage >= _maxHealth - 1)
            {
                return false;
            }

            // A FLOOR OF ONE REMAINING HIT, never zero. Softening is not digging: the child
            // always gets the last tap on a tile the water ran over.
            _damage = _maxHealth - 1;
            _washed = true;
            RefreshSprite();
            ApplyPeekAlpha();
            return true;
        }

        /// <summary>BOUNCY MUSHROOM (DinoDigger-u47). The big springy squash a boing throws.
        /// RESTING-SCALE SAFE by construction: it hands the transform over from any in-flight
        /// punch and re-bases on <see cref="_restScale"/> first, exactly as the landing squash
        /// does, so a child hammering the mushroom can never inflate it (the giant-blueberry
        /// bug). All of its motion is read from config at the moment of the bounce.</summary>
        public void Boing(float amount, float seconds)
        {
            Tween.CancelPunch(transform);
            EndSquash();
            transform.localScale = _restScale;
            Tween.PunchScale(transform, Mathf.Clamp(amount, 0.05f, 0.9f),
                Mathf.Clamp(seconds, 0.05f, 2f));
        }

        /// <summary>TEST HOOK. Has this mushroom already spent its one bounce?</summary>
        internal bool TestBounced => _bounced;

        /// <summary>TEST HOOK. Hits this tile still needs before it crumbles (a mushroom's
        /// pending BOUNCE counts as one, because to the child it is one more bite).</summary>
        internal int TestHitsRemaining =>
            _destroyed ? 0 : Mathf.Max(0, _maxHealth - _damage) +
                             (_kind == DigTileKind.Mushroom && !_bounced ? 1 : 0);

        /// <summary>Break this tile RIGHT NOW whatever its hardness, optionally holding the art
        /// on screen for <paramref name="visualDelay"/> seconds first and then sparkle-shrinking
        /// it away. Used by the crystal blob pop (whose LOGIC is synchronous — the whole blob is
        /// cleared and the board settled on the tapped frame — while the VISUAL ripples outward
        /// ring by ring) and by a geode detonating itself.
        ///
        /// The collider and the grid cell go immediately in every case: a corpse must never be
        /// tappable and gravity must never see a hole that is not there yet. Only the pixels
        /// linger.</summary>
        internal void ForceBreak(float visualDelay)
        {
            if (_destroyed)
            {
                return;
            }

            _damage = _maxHealth;
            Crumble(visualDelay);
        }

        private void Crumble(float visualDelay = 0f)
        {
            _destroyed = true;
            _wiggling = false;
            CoversBone = false; // the cell it was covering is now UNCOVERED (owner-side bookkeeping)

            // A crumbling tile stops travelling: kill any fall/squash in flight so a
            // corpse can never keep sliding toward a cell it no longer occupies.
            if (_fall != null)
            {
                Tween.Stop(_fall);
                _fall = null;
            }

            _falling = false;
            EndSquash();
            transform.localRotation = Quaternion.identity; // undo any surprise sway

            var col = GetComponent<Collider2D>();
            if (col != null)
            {
                col.enabled = false;
            }

            if (_peek != null)
            {
                _peek.enabled = false; // item is now uncovered / about to pop
            }

            if (_dirt == null)
            {
                return;
            }

            if (visualDelay <= 0f)
            {
                _dirt.enabled = false;
                return;
            }

            // Held-then-shrunk exit (a crystal waiting for its ring). The shrink writes scale,
            // so it hands the transform over from any in-flight punch/squash first and runs off
            // the RESTING scale — never off whatever pose it happens to be in mid-punch.
            Tween.CancelPunch(transform);
            transform.localScale = _restScale;
            float fade = _config != null
                ? Mathf.Clamp(_config.DigCrystalPopFadeSeconds, 0.02f, 1f)
                : 0.14f;
            Tween.After(visualDelay, () =>
            {
                if (this == null || _dirt == null)
                {
                    return;
                }

                Tween.Run(fade, t =>
                {
                    if (this == null || _dirt == null)
                    {
                        return;
                    }

                    // A quick bulge-then-nothing: it reads as a pop rather than a fade-out.
                    float k = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI * 0.5f);
                    transform.localScale = _restScale * (1f + 0.25f * k) * (1f - k);
                }, () =>
                {
                    if (this == null)
                    {
                        return;
                    }

                    if (_dirt != null)
                    {
                        _dirt.enabled = false;
                    }

                    transform.localScale = _restScale;
                });
            });
        }

        private void RefreshSprite()
        {
            if (_dirt == null || _lib == null)
            {
                return;
            }

            switch (_kind)
            {
                case DigTileKind.Crystal:
                {
                    Sprite crystal = _lib.Crystal(_crystalColor);
                    if (crystal != null)
                    {
                        _dirt.sprite = crystal;
                    }

                    break;
                }

                case DigTileKind.Geode:
                    if (_lib.BoomGeode != null)
                    {
                        _dirt.sprite = _lib.BoomGeode;
                    }

                    break;

                case DigTileKind.Pot:
                {
                    // One crack state: whole until it has been hit, cracked after (and the
                    // NEXT hit breaks it). Falls back to the whole pot if the cracked art is
                    // missing rather than blanking the cell.
                    Sprite pot = _damage > 0 && _lib.PinataPotCracked != null
                        ? _lib.PinataPotCracked
                        : _lib.PinataPot;
                    if (pot != null)
                    {
                        _dirt.sprite = pot;
                    }

                    break;
                }

                // ---- Wave 2 toys: PLACEHOLDER ART ON PURPOSE (DinoDigger-u47) ----
                // The art budget for this wave belongs to another ticket (see the follow-up
                // bead), so each of these borrows an existing sprite under a signature tint
                // rather than shipping nothing. Every lookup is null-tolerant and falls all the
                // way back to plain dirt under the tint, so a toy is ALWAYS visible and always
                // reads as "not ordinary dirt" — which is the only thing the mechanic needs
                // from the art until the real sprites land.
                case DigTileKind.Water:
                    // Deliberately the dirt silhouette: a water pocket is a POCKET — a patch of
                    // wet dark ground, not an object sitting in the wall. The tint carries it.
                    if (_lib.Dirt(0) != null)
                    {
                        _dirt.sprite = _lib.Dirt(0);
                    }

                    break;

                case DigTileKind.Vein:
                    // The gem treasure sprite: a gem embedded in the wall is exactly what a
                    // vein segment is, and it can never be confused with a crystal TILE (which
                    // is a whole chunky cell of one flat colour).
                    if (_lib.Treasure(1) != null)
                    {
                        _dirt.sprite = _lib.Treasure(1);
                    }

                    break;

                case DigTileKind.Mushroom:
                    // The mound blob reads as a fat round cap; under the mushroom tint it is
                    // unmistakably a toadstool and unmistakably not a mound (which is never in
                    // the pit in the first place).
                    if (_lib.MoundSprite != null)
                    {
                        _dirt.sprite = _lib.MoundSprite;
                    }

                    break;

                default:
                {
                    // Map damage 0..max-1 across the 3 crack-state sprites.
                    int stateCount = 3;
                    int state = _maxHealth <= 1 ? 0
                        : Mathf.Clamp(Mathf.FloorToInt((float)_damage / _maxHealth * stateCount), 0, stateCount - 1);
                    Sprite s = _lib.Dirt(state);
                    if (s != null)
                    {
                        _dirt.sprite = s;
                    }

                    break;
                }
            }

            ApplyTint();
        }

        /// <summary>Re-apply the renderer tint for this tile's kind. The theme's dirt multiply
        /// belongs to DIRT only — a crystal's whole job is to read as its own colour, and a
        /// muddy-brown "gold" crystal would break the one matching rule the game has. A toy with
        /// no imported art keeps the dirt sprite under its toy tint so it is still readable.</summary>
        private void ApplyTint()
        {
            if (_dirt == null)
            {
                return;
            }

            switch (_kind)
            {
                case DigTileKind.Crystal:
                    _dirt.color = _lib != null && _lib.Crystal(_crystalColor) != null
                        ? Color.white
                        : CrystalTint(_crystalColor);
                    break;

                case DigTileKind.Geode:
                    _dirt.color = _lib != null && _lib.BoomGeode != null
                        ? Color.white
                        : new Color(0.72f, 0.62f, 0.95f);
                    break;

                case DigTileKind.Pot:
                    _dirt.color = _lib != null && _lib.PinataPot != null
                        ? Color.white
                        : new Color(0.95f, 0.55f, 0.75f);
                    break;

                // The wave-2 toys are ALWAYS tinted, whatever art they found: the tint IS their
                // identity until the real sprites land (see RefreshSprite), so unlike the three
                // toys above they never fall through to white.
                case DigTileKind.Water:
                    _dirt.color = WaterTint;
                    break;

                case DigTileKind.Vein:
                    _dirt.color = VeinTint;
                    break;

                case DigTileKind.Mushroom:
                    _dirt.color = MushroomTint;
                    break;

                default:
                    _dirt.color = _dirtTint; // keep the theme tint across crack-state swaps
                    break;
            }
        }
    }
}
