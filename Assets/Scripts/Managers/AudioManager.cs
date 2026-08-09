using UnityEngine;
using DinoDigger.Config;

namespace DinoDigger.Managers
{
    /// <summary>Mix buses. Every one-shot names one; the category's trim in
    /// <see cref="AudioConfig"/> multiplies the master SFX volume, so the shape of the mix
    /// survives a parent turning everything down.</summary>
    public enum SfxCategory
    {
        /// <summary>Buttons, taps, board clicks — the quietest bus.</summary>
        Ui = 0,
        /// <summary>Cracks, crumbles, cascade thumps. Heard hundreds of times a session,
        /// so deliberately mixed under the rewards.</summary>
        Dig = 1,
        /// <summary>Pops, coins, bones, chimes — the payoff sounds, mixed loudest.</summary>
        Reward = 2,
        /// <summary>Dino-Matic, Doodle, Sprinkles, Tuggy.</summary>
        Machine = 3,
        /// <summary>Dinos, ducks, giggles.</summary>
        Creature = 4,
    }

    /// <summary>
    /// SFX pool + one looping music source + a party-loop source. Null clips are skipped
    /// silently, so code may call any hook before its clip is wired.
    ///
    /// Every one-shot goes through <see cref="PlaySfx(AudioClip, SfxCategory, float, float)"/>,
    /// which applies three things the hooks must never do themselves:
    ///   * the per-clip GAIN, a fixed trim that levels the CC0 packs against each other. The
    ///     packs peak near -1 dBFS but their MEAN level spans ~15 dB (Kenney's digital blips
    ///     scream next to its impacts), so each wrapper below carries the trim derived offline
    ///     in Tools/ASSET_SOURCES.md. That is what makes the set toddler-soft.
    ///   * the per-category trim, and
    ///   * pitch jitter, so a child bashing one tile hears repeated EVENTS rather than one
    ///     sample stuttering.
    ///
    /// Mute is persisted in PlayerPrefs and gated behind the parent-hold button. It is
    /// enforced here, at the single choke point, rather than at the ~60 call sites — and it
    /// suppresses the play outright instead of relying on AudioSource.mute, so a muted game
    /// starts no voices at all.
    /// </summary>
    public class AudioManager
    {
        private const string MuteKey = "DinoDigger.Muted";

        // How far the main music ducks while the dance party vamp is playing.
        private const float PartyDuckFactor = 0.55f;

        private AudioConfig _config;
        private AudioSource[] _sfxPool;
        private AudioSource _music;
        private AudioSource _danceLoop;
        private int _next;
        private int _crackVariant;

        public bool Muted { get; private set; }

        public void Init(AudioConfig config, AudioSource[] sfxPool, AudioSource music, AudioSource danceLoop = null)
        {
            _config = config;
            _sfxPool = sfxPool;
            _music = music;
            _danceLoop = danceLoop;
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

            if (_danceLoop != null)
            {
                _danceLoop.loop = true;
                _danceLoop.playOnAwake = false;
                _danceLoop.spatialBlend = 0f;
            }
        }

        // ------------------------------------------------------------------ playback

        /// <summary>
        /// Play a one-shot on the next pooled voice. <paramref name="gain"/> is the clip's
        /// fixed loudness trim (see class doc); <paramref name="pitchCenter"/> lets a caller
        /// bias a sound up or down (a BIG version of a pop sits lower) while still getting
        /// the configured jitter around that centre.
        /// </summary>
        public void PlaySfx(AudioClip clip, SfxCategory category, float gain = 1f, float pitchCenter = 1f)
        {
            // Counted BEFORE the null-clip test on purpose: the integration case asserts that
            // hooks reach the service and that mute stops them, and it must not depend on
            // whether the art/audio import has run in that particular editor session.
            if (Muted)
            {
                TestSuppressedCount++;
                return;
            }

            TestPlayCount++;
            TestCategoryCounts[(int)category]++;

            if (clip == null || _sfxPool == null || _sfxPool.Length == 0)
            {
                return;
            }

            AudioSource src = _sfxPool[_next];
            _next = (_next + 1) % _sfxPool.Length;

            if (src == null)
            {
                return;
            }

            float jitter = _config != null ? _config.PitchJitter : 0.05f;
            src.pitch = pitchCenter * Random.Range(1f - jitter, 1f + jitter);
            src.volume = 1f;
            src.PlayOneShot(clip, Mathf.Clamp01(MasterSfx * CategoryVolume(category) * gain));
        }

        /// <summary>Legacy overload: fixed pitch, UI bus, no trim. Kept so the pre-audio-pass
        /// call sites keep compiling; new hooks should name a category.</summary>
        public void PlaySfx(AudioClip clip, float pitch = 1f)
        {
            PlaySfx(clip, SfxCategory.Ui, 1f, pitch);
        }

        private float MasterSfx => _config != null ? _config.SfxVolume : 1f;

        private float CategoryVolume(SfxCategory category)
        {
            if (_config == null)
            {
                return 1f;
            }

            switch (category)
            {
                case SfxCategory.Dig: return _config.DigVolume;
                case SfxCategory.Reward: return _config.RewardVolume;
                case SfxCategory.Machine: return _config.MachineVolume;
                case SfxCategory.Creature: return _config.CreatureVolume;
                default: return _config.UiVolume;
            }
        }

        // ------------------------------------------------------- named hooks (pre-existing)
        // Gains below are the offline-derived trims; see Tools/ASSET_SOURCES.md.

        public void Tap() => PlaySfx(_config?.Tap, SfxCategory.Ui, 1f);
        public void Move() => PlaySfx(_config?.Move, SfxCategory.Ui, 1f);
        public void Dig() => PlaySfx(_config?.Dig, SfxCategory.Dig, 1f);
        public void Crumble() => PlaySfx(_config?.Crumble, SfxCategory.Dig, 1f);
        public void ItemPop() => PlaySfx(_config?.ItemPop, SfxCategory.Reward, 1f);
        public void Chime() => PlaySfx(_config?.Chime, SfxCategory.Reward, 0.50f);
        public void Hatch() => PlaySfx(_config?.Hatch, SfxCategory.Reward, 0.51f);
        public void Roar() => PlaySfx(_config?.Roar, SfxCategory.Creature, 0.50f);
        public void Eat() => PlaySfx(_config?.Eat, SfxCategory.Creature, 0.35f);
        public void Grow() => PlaySfx(_config?.Grow, SfxCategory.Reward, 0.51f);
        public void Treasure() => PlaySfx(_config?.TreasureCollect, SfxCategory.Reward, 0.35f);
        public void Honk() => PlaySfx(_config?.Honk, SfxCategory.Creature, 0.56f);
        public void Heart() => PlaySfx(_config?.Heart, SfxCategory.Creature, 1f);

        // ------------------------------------------------------------ named hooks (dig pass)

        /// <summary>A tile took a hit and survived. Rotates three samples rather than picking
        /// at random: random repeats itself, and a child bashing one tile hears that.</summary>
        public void TileCrack()
        {
            AudioClip clip;
            switch (_crackVariant)
            {
                case 0: clip = _config?.TileCrackA; break;
                case 1: clip = _config?.TileCrackB; break;
                default: clip = _config?.TileCrackC; break;
            }

            _crackVariant = (_crackVariant + 1) % 3;
            PlaySfx(clip, SfxCategory.Dig, 1f);
        }

        /// <summary>A tile finished falling. Throttled by the caller, not here.</summary>
        public void LandingThump() => PlaySfx(_config?.LandingThump, SfxCategory.Dig, 0.69f);

        /// <summary>Crystal pop. A multi-tile blob gets the fatter sample pitched slightly
        /// down, so bigger really does sound bigger.</summary>
        public void CrystalPop(bool big = false)
        {
            if (big)
            {
                PlaySfx(_config?.CrystalPopBig, SfxCategory.Reward, 1f, 0.92f);
            }
            else
            {
                PlaySfx(_config?.CrystalPop, SfxCategory.Reward, 1f);
            }
        }

        public void FuseSizzle() => PlaySfx(_config?.FuseSizzle, SfxCategory.Dig, 0.35f);
        public void Whumph() => PlaySfx(_config?.Whumph, SfxCategory.Dig, 0.69f);
        public void PotCrack() => PlaySfx(_config?.PotCrack, SfxCategory.Dig, 1f);
        public void CoinSpray() => PlaySfx(_config?.CoinSpray, SfxCategory.Reward, 0.71f);
        public void BoneRattle() => PlaySfx(_config?.BoneRattle, SfxCategory.Dig, 1f);
        public void BonePop() => PlaySfx(_config?.BonePop, SfxCategory.Reward, 0.51f);
        public void CeremonyPoof() => PlaySfx(_config?.CeremonyPoof, SfxCategory.Machine, 0.48f);
        public void MachineWake() => PlaySfx(_config?.MachineWake, SfxCategory.Machine, 1f);
        public void Gurgle() => PlaySfx(_config?.Gurgle, SfxCategory.Machine, 0.35f);
        public void Toot() => PlaySfx(_config?.Toot, SfxCategory.Machine, 0.56f);
        public void Giggle() => PlaySfx(_config?.Giggle, SfxCategory.Creature, 0.35f);
        public void WaterGush() => PlaySfx(_config?.WaterGush, SfxCategory.Machine, 1f);

        /// <summary>The depth ladder arriving — a small "you earned a way down" ding.</summary>
        public void LadderDing() => PlaySfx(_config?.LadderDing, SfxCategory.Reward, 0.50f);

        /// <summary>One vein segment sparking. Pitched UP a little per step along the run so a
        /// five-segment vein reads as a rising zip rather than five identical zaps; pass the
        /// segment index and the run length.</summary>
        public void SparkZap(int index = 0, int total = 1)
        {
            float t = total > 1 ? Mathf.Clamp01(index / (float)(total - 1)) : 0f;
            PlaySfx(_config?.SparkZap, SfxCategory.Reward, 0.39f, Mathf.Lerp(0.94f, 1.18f, t));
        }

        /// <summary>A bite bouncing off a mushroom tile.</summary>
        public void Boing() => PlaySfx(_config?.Boing, SfxCategory.Dig, 0.58f);

        // ------------------------------------------------------------------ dance party loop

        /// <summary>True while the party vamp is looping.</summary>
        public bool DanceLoopPlaying => _danceLoop != null && _danceLoop.isPlaying;

        /// <summary>Start the music-box vamp under the main track and duck the track to make
        /// room. Idempotent: a second party starting while one runs does not restart it.</summary>
        public void StartDanceLoop()
        {
            if (Muted || _danceLoop == null || _config == null || _config.DanceLoop == null)
            {
                return;
            }

            if (_danceLoop.isPlaying)
            {
                return;
            }

            _danceLoop.clip = _config.DanceLoop;
            _danceLoop.volume = Mathf.Clamp01(_config.DanceLoopVolume);
            _danceLoop.Play();

            if (_music != null)
            {
                _music.volume = _config.MusicVolume * PartyDuckFactor;
            }
        }

        /// <summary>Stop the vamp and restore the main track. Safe to call when not playing.</summary>
        public void StopDanceLoop()
        {
            if (_danceLoop != null && _danceLoop.isPlaying)
            {
                _danceLoop.Stop();
            }

            if (_music != null && _config != null)
            {
                _music.volume = _config.MusicVolume;
            }
        }

        // ------------------------------------------------------------------------ mute

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

            if (_danceLoop != null)
            {
                _danceLoop.mute = Muted;

                // A party that started before the mute must not keep spinning silently and
                // then burst back in when sound returns.
                if (Muted && _danceLoop.isPlaying)
                {
                    _danceLoop.Stop();
                }
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

        // ------------------------------------------------------------------ test hooks
        // Counters, not audio assertions: the integration suite proves hooks REACH the
        // service and that mute stops them there. Nothing here reads the audio output.

        /// <summary>One-shots that passed the mute gate since the last reset.</summary>
        public int TestPlayCount { get; private set; }

        /// <summary>One-shots refused because the game is muted.</summary>
        public int TestSuppressedCount { get; private set; }

        /// <summary>Per-<see cref="SfxCategory"/> tally, indexed by the enum value.</summary>
        public readonly int[] TestCategoryCounts = new int[5];

        public void TestResetCounters()
        {
            TestPlayCount = 0;
            TestSuppressedCount = 0;
            for (int i = 0; i < TestCategoryCounts.Length; i++)
            {
                TestCategoryCounts[i] = 0;
            }
        }
    }
}
