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

        public int Row { get; private set; }
        public int Col { get; private set; }
        public bool HasItem { get; private set; }
        public bool IsSurprise => _isSurprise;
        public bool IsDestroyed => _destroyed;

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
            _wigglePhase = Random.value * Mathf.PI * 2f;
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
