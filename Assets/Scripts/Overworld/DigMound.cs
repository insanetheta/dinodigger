using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;

namespace DinoDigger.Overworld
{
    /// <summary>
    /// A tappable/driveable dig spot on the overworld. Tapping it (or arriving at
    /// it) tells the GameManager to send the backhoe over and enter dig mode.
    /// A gentle sparkle makes it discoverable; idle-attract can boost it.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class DigMound : MonoBehaviour, ITappable
    {
        [SerializeField] private SpriteRenderer _renderer;
        [SerializeField] private ParticleSystem _sparkle;

        public bool IsActive { get; private set; } = true;

        /// <summary>MEGA-FOSSIL SITE (DinoDigger-84f). This mound wears a skull marker and opens
        /// the big 7x9 pit with a whole remaining skeleton buried in it. Rolled by
        /// <see cref="GameManager.RollMegaFossilMound"/> whenever the mound (re)rolls its
        /// flavour, so the promise is made in the OVERWORLD — the child chooses to accept it —
        /// and the dig simply delivers on it.
        ///
        /// Session state on the live mound, deliberately not saved: a mound that has not been dug
        /// stays in the world exactly like any other, and a restart re-rolls flavours from
        /// scratch, which costs the child nothing (the bones a mega site would have buried are
        /// still the bones their board is missing).</summary>
        public bool IsMegaFossil { get; private set; }

        // The skull overlay: a child renderer built on demand, shown only while this mound is a
        // mega-fossil site. State-derived, never toggled at a call site — the same discipline
        // MachineFriend's overlays are held to, and for the same reason.
        private SpriteRenderer _skullMarker;

        /// <summary>Index into GameConfig.EffectiveThemes for this mound's rolled dig
        /// postcard. Drives the dig site's tint + loot skew, and this mound's own colour.</summary>
        public int ThemeIndex { get; private set; }

        private Vector3 _baseScale;
        private Color _baseSparkleColor = Color.white; // pre-tint sparkle colour
        private GameConfig _config;

        private void Awake()
        {
            if (_renderer == null)
            {
                _renderer = GetComponent<SpriteRenderer>();
            }

            _baseScale = transform.localScale;
            if (_sparkle != null)
            {
                _baseSparkleColor = _sparkle.main.startColor.color;
            }
        }

        /// <summary>Roll a fresh dig postcard theme (weighted) and tint the mound + sparkle
        /// to match, so the colour telegraphs the flavour. Called on (re)spawn. Null config
        /// leaves the mound at its default (Meadow Classic) look.</summary>
        public void RollTheme(GameConfig config)
        {
            _config = config;
            if (config == null)
            {
                return;
            }

            ThemeIndex = config.PickThemeIndex();
            ApplyThemeTint(config.GetTheme(ThemeIndex));

            // ...and, rarely, the skull. Asked here rather than decided here: the roll needs the
            // skeleton board, the session's pity counter and the bones gate, none of which a
            // mound has any business knowing about.
            GameManager.Instance?.RollMegaFossilMound(this);
        }

        /// <summary>Mark (or un-mark) this mound as a mega-fossil site and show its skull.
        /// <paramref name="marker"/> is the skull sprite the dig itself uses for a skull bone —
        /// passed in rather than looked up, because a mound has no art library of its own. A null
        /// marker still marks the mound (the dig is what matters); it just shows no overlay.
        ///
        /// ONLY <see cref="GameManager"/> MAY CALL THIS WITH true, and only from
        /// <c>MarkMegaFossilMound</c> — that is where the "at most one skull on the island"
        /// invariant lives (DinoDigger-tyf), and a mound marking itself would walk straight
        /// around it.</summary>
        public void SetMegaFossil(bool mega, Sprite marker)
        {
            IsMegaFossil = mega;

            if (mega && marker != null && _skullMarker == null)
            {
                var go = new GameObject("SkullMarker");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, 0.45f, 0f);

                _skullMarker = go.AddComponent<SpriteRenderer>();
                _skullMarker.sprite = marker;
                _skullMarker.color = new Color(0.98f, 0.96f, 0.88f);
                if (_renderer != null)
                {
                    _skullMarker.sortingLayerID = _renderer.sortingLayerID;
                    _skullMarker.sortingOrder = _renderer.sortingOrder + 1;
                }

                if (marker.bounds.size.y > 0.001f)
                {
                    float k = 0.55f / marker.bounds.size.y;
                    go.transform.localScale = new Vector3(k, k, 1f);
                }
            }

            if (_skullMarker != null)
            {
                _skullMarker.enabled = mega && IsActive;
            }
        }

        /// <summary>TEST HOOK. Is the skull actually being drawn (not merely recorded)?</summary>
        internal bool TestSkullVisible => _skullMarker != null && _skullMarker.enabled;

        private void ApplyThemeTint(DigTheme theme)
        {
            if (theme == null)
            {
                return;
            }

            if (_renderer != null)
            {
                _renderer.color = theme.MoundTint;
            }

            if (_sparkle != null)
            {
                // Multiply the base sparkle colour by the theme tint so a white-tinted
                // (Meadow) mound keeps its default gold sparkle, while a themed mound
                // shifts toward its colour.
                var main = _sparkle.main;
                main.startColor = _baseSparkleColor * theme.MoundTint;
            }
        }

        public void OnTapped(Vector2 worldPoint)
        {
            if (!IsActive)
            {
                return;
            }

            Tween.PunchScale(transform, 0.25f, 0.3f);
            GameManager.Instance?.RequestDig(this);
        }

        /// <summary>Move this mound to a fresh spot and re-enable it. A respawn also rolls
        /// a brand-new dig postcard theme, so a dug-out mound comes back a fresh flavour.</summary>
        public void Respawn(Vector3 worldPos)
        {
            transform.position = worldPos;
            RollTheme(_config); // fresh flavour each respawn (no-op if config not yet set)
            SetActiveMound(true);
            transform.localScale = Vector3.zero;
            Tween.ScaleTo(transform, _baseScale, 0.4f);
        }

        /// <summary>TEST HOOK. The mound sprite's current tint (its theme colour).</summary>
        internal Color TestTint => _renderer != null ? _renderer.color : Color.white;

        public void Consume()
        {
            SetActiveMound(false);
        }

        public void SetActiveMound(bool active)
        {
            IsActive = active;
            if (_renderer != null)
            {
                _renderer.enabled = active;
            }

            // The skull is part of the mound's body: a consumed mound takes its marker with it,
            // so a dug-out site can never leave a floating skull behind.
            if (_skullMarker != null)
            {
                _skullMarker.enabled = active && IsMegaFossil;
            }

            var col = GetComponent<Collider2D>();
            if (col != null)
            {
                col.enabled = active;
            }

            if (_sparkle != null)
            {
                if (active)
                {
                    _sparkle.Play();
                }
                else
                {
                    _sparkle.Stop();
                }
            }
        }

        /// <summary>Idle-attract: a gentle bounce + sparkle pulse to invite a tap.</summary>
        public void AttractPulse()
        {
            if (!IsActive)
            {
                return;
            }

            Tween.PunchScale(transform, 0.3f, 0.5f);
            if (_sparkle == null)
            {
                return;
            }

            // A small emission burst plus a soft size swell that eases back — the old
            // constant 2x star (~0.6+ units, held for a second) covered the backhoe.
            // Base startSize is ~0.3, so the ~1.8x peak tops out around 0.55 units.
            _sparkle.Emit(8);
            Tween.Run(0.7f, t =>
            {
                if (_sparkle == null)
                {
                    return;
                }

                var m = _sparkle.main;
                m.startSizeMultiplier = 1f + Mathf.Sin(t * Mathf.PI) * 0.85f;
            }, () =>
            {
                if (_sparkle != null)
                {
                    var m = _sparkle.main;
                    m.startSizeMultiplier = 1f;
                }
            });
        }
    }
}
