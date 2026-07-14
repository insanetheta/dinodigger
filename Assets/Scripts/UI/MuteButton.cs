using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DinoDigger.Config;
using DinoDigger.Managers;

namespace DinoDigger.UI
{
    /// <summary>
    /// Parent-gated mute toggle: the child must HOLD the button for ~3 seconds
    /// (a toddler tap does nothing) before mute flips. A radial fill shows hold
    /// progress. Wired to the AudioManager by GameManager.
    /// </summary>
    public class MuteButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private Image _iconImage;
        [SerializeField] private Image _holdFill;   // radial fill, optional
        [SerializeField] private Sprite _soundSprite;
        [SerializeField] private Sprite _muteSprite;

        private AudioManager _audio;
        private GameConfig _config;
        private bool _holding;
        private float _held;

        public void Bind(AudioManager audio, GameConfig config)
        {
            _audio = audio;
            _config = config;
            RefreshIcon();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _holding = true;
            _held = 0f;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _holding = false;
            _held = 0f;
            if (_holdFill != null)
            {
                _holdFill.fillAmount = 0f;
            }
        }

        private void Update()
        {
            if (!_holding)
            {
                return;
            }

            float need = _config != null ? _config.ParentGateHoldSeconds : 3f;
            _held += Time.unscaledDeltaTime;

            if (_holdFill != null)
            {
                _holdFill.fillAmount = Mathf.Clamp01(_held / need);
            }

            if (_held >= need)
            {
                _holding = false;
                _held = 0f;
                if (_holdFill != null)
                {
                    _holdFill.fillAmount = 0f;
                }

                Toggle();
            }
        }

        private void Toggle()
        {
            if (_audio != null)
            {
                _audio.ToggleMute();
            }

            RefreshIcon();
            Core.Tween.PunchScale(transform, 0.3f, 0.3f);
        }

        private void RefreshIcon()
        {
            if (_iconImage == null)
            {
                return;
            }

            bool muted = _audio != null && _audio.Muted;
            Sprite s = muted ? _muteSprite : _soundSprite;
            if (s != null)
            {
                _iconImage.sprite = s;
            }
        }
    }
}
