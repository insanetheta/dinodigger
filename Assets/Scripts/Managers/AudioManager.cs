using UnityEngine;
using DinoDigger.Config;

namespace DinoDigger.Managers
{
    /// <summary>
    /// SFX pool + one looping music source. Null clips are skipped silently.
    /// Mute is persisted in PlayerPrefs and gated behind the parent-hold button.
    /// </summary>
    public class AudioManager
    {
        private const string MuteKey = "DinoDigger.Muted";

        private AudioConfig _config;
        private AudioSource[] _sfxPool;
        private AudioSource _music;
        private int _next;

        public bool Muted { get; private set; }

        public void Init(AudioConfig config, AudioSource[] sfxPool, AudioSource music)
        {
            _config = config;
            _sfxPool = sfxPool;
            _music = music;
            _next = 0;

            Muted = PlayerPrefs.GetInt(MuteKey, 0) == 1;
            ApplyMute();

            if (_music != null && _config != null && _config.Music != null)
            {
                _music.clip = _config.Music;
                _music.loop = true;
                _music.volume = _config.MusicVolume;
                _music.playOnAwake = false;
                _music.Play();
            }
        }

        public void PlaySfx(AudioClip clip, float pitch = 1f)
        {
            if (clip == null || _sfxPool == null || _sfxPool.Length == 0 || Muted)
            {
                return;
            }

            AudioSource src = _sfxPool[_next];
            _next = (_next + 1) % _sfxPool.Length;

            if (src == null)
            {
                return;
            }

            src.pitch = pitch;
            src.volume = _config != null ? _config.SfxVolume : 1f;
            src.PlayOneShot(clip);
        }

        // Convenience wrappers keyed off the config (all null-tolerant).
        public void Tap() => PlaySfx(_config?.Tap);
        public void Move() => PlaySfx(_config?.Move);
        public void Dig() => PlaySfx(_config?.Dig, Random.Range(0.95f, 1.1f));
        public void Crumble() => PlaySfx(_config?.Crumble, Random.Range(0.9f, 1.15f));
        public void ItemPop() => PlaySfx(_config?.ItemPop);
        public void Chime() => PlaySfx(_config?.Chime);
        public void Hatch() => PlaySfx(_config?.Hatch);
        public void Roar() => PlaySfx(_config?.Roar, Random.Range(0.95f, 1.05f));
        public void Eat() => PlaySfx(_config?.Eat);
        public void Grow() => PlaySfx(_config?.Grow);
        public void Treasure() => PlaySfx(_config?.TreasureCollect);
        public void Honk() => PlaySfx(_config?.Honk);
        public void Heart() => PlaySfx(_config?.Heart);

        public void SetMuted(bool muted)
        {
            Muted = muted;
            PlayerPrefs.SetInt(MuteKey, muted ? 1 : 0);
            PlayerPrefs.Save();
            ApplyMute();
        }

        public void ToggleMute() => SetMuted(!Muted);

        private void ApplyMute()
        {
            if (_music != null)
            {
                _music.mute = Muted;
            }

            if (_sfxPool != null)
            {
                for (int i = 0; i < _sfxPool.Length; i++)
                {
                    if (_sfxPool[i] != null)
                    {
                        _sfxPool[i].mute = Muted;
                    }
                }
            }
        }
    }
}
