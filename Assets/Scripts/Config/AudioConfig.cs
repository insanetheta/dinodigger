using UnityEngine;

namespace DinoDigger.Config
{
    /// <summary>
    /// Named audio clip slots. Every field may be left null; the AudioManager
    /// silently skips missing clips so audio can land after code.
    /// </summary>
    [CreateAssetMenu(menuName = "DinoDigger/Audio Config", fileName = "AudioConfig")]
    public class AudioConfig : ScriptableObject
    {
        [Header("Music")]
        public AudioClip Music;

        [Header("SFX")]
        public AudioClip Tap;
        public AudioClip Move;
        public AudioClip Dig;
        public AudioClip Crumble;
        public AudioClip ItemPop;
        public AudioClip Chime;
        public AudioClip Hatch;
        public AudioClip Roar;
        public AudioClip Eat;
        public AudioClip Grow;
        public AudioClip TreasureCollect;
        public AudioClip Honk;
        public AudioClip Heart;

        [Header("Mix")]
        [Range(0f, 1f)] public float MusicVolume = 0.5f;
        [Range(0f, 1f)] public float SfxVolume = 0.9f;
    }
}
