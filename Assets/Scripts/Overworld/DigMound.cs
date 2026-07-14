using UnityEngine;
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

        private Vector3 _baseScale;

        private void Awake()
        {
            if (_renderer == null)
            {
                _renderer = GetComponent<SpriteRenderer>();
            }

            _baseScale = transform.localScale;
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

        /// <summary>Move this mound to a fresh spot and re-enable it.</summary>
        public void Respawn(Vector3 worldPos)
        {
            transform.position = worldPos;
            SetActiveMound(true);
            transform.localScale = Vector3.zero;
            Tween.ScaleTo(transform, _baseScale, 0.4f);
        }

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
