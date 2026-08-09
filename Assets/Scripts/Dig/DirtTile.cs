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
        private const float SquashTime = 0.22f;   // impact squash + spring back
        private const float SquashAmount = 0.18f; // widen x / flatten y at peak impact

        public int Row { get; private set; }
        public int Col { get; private set; }
        public bool HasItem { get; private set; }
        public bool IsSurprise => _isSurprise;
        public bool IsDestroyed => _destroyed;

        /// <summary>True while this tile is travelling to a cell the cascade already moved
        /// it to. Taps on it are ignored (it lands first) and tests pace to it.</summary>
        public bool IsFalling => _falling;

        // TEST HOOKS for the integration runner (damage progression + peek visibility).
        internal int TestDamage => _damage;
        internal int TestMaxHealth => _maxHealth;
        internal Sprite TestDirtSprite => _dirt != null ? _dirt.sprite : null;
        internal Color TestDirtColor => _dirt != null ? _dirt.color : Color.white;
        internal bool TestPeekEnabled => _peek != null && _peek.enabled;
        internal float TestPeekAlpha => _peek != null ? _peek.color.a : 0f;
        internal bool TestIsSurprise => _isSurprise;

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

        public void Build(DigModeController owner, PlaceholderLibrary lib, int row, int col,
            int maxHealth, ParticleSystem crumbs)
        {
            _owner = owner;
            _lib = lib;
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
            if (_dirt != null)
            {
                _dirt.color = tint;
            }
        }

        public void SetPeek(Sprite itemSprite, Color tint)
        {
            HasItem = true;
            _peekTint = tint;
            if (_peek != null)
            {
                _peek.sprite = itemSprite;
                // Clear color hint visible from the start (2x boosted per playtest
                // feedback); strengthens further as the dirt cracks.
                _peek.color = new Color(tint.r, tint.g, tint.b, _restPeekAlpha);
                _peek.transform.localScale = Vector3.one * 0.7f;
                _peek.enabled = true;
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
                transform.position = Vector3.LerpUnclamped(from, target, u * u); // accelerate: heavy, not floaty
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
            _squash = Tween.Run(SquashTime, t =>
            {
                if (this == null)
                {
                    return;
                }

                float e = Mathf.Sin(t * Mathf.PI) * (1f - t); // impact spike, decaying to rest
                transform.localScale = new Vector3(
                    _restScale.x * (1f + SquashAmount * e),
                    _restScale.y * (1f - SquashAmount * e),
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

        /// <summary>Apply one hit. Returns true when this hit destroys the tile.</summary>
        public bool Damage()
        {
            if (_destroyed)
            {
                return false;
            }

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
            // The hint is visible from the start; brighten it as the dirt cracks.
            if (HasItem && _peek != null)
            {
                _peek.enabled = true;
                float a = Mathf.Lerp(0.7f, 1f, (float)_damage / Mathf.Max(1, _maxHealth));
                _peek.color = new Color(_peekTint.r, _peekTint.g, _peekTint.b, a);
            }
        }

        private void Crumble()
        {
            _destroyed = true;
            _wiggling = false;

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
            if (_dirt != null)
            {
                _dirt.enabled = false;
            }

            var col = GetComponent<Collider2D>();
            if (col != null)
            {
                col.enabled = false;
            }

            if (_peek != null)
            {
                _peek.enabled = false; // item is now uncovered / about to pop
            }
        }

        private void RefreshSprite()
        {
            if (_dirt == null || _lib == null)
            {
                return;
            }

            // Map damage 0..max-1 across the 3 crack-state sprites.
            int stateCount = 3;
            int state = _maxHealth <= 1 ? 0
                : Mathf.Clamp(Mathf.FloorToInt((float)_damage / _maxHealth * stateCount), 0, stateCount - 1);
            Sprite s = _lib.Dirt(state);
            if (s != null)
            {
                _dirt.sprite = s;
            }

            _dirt.color = _dirtTint; // keep the theme tint across crack-state swaps
        }
    }
}
