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
        }

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
