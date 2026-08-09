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

        /// <summary>Short pizzicato vamp looped UNDER the main music while Doodle's party runs.</summary>
        public AudioClip DanceLoop;

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

        // ---------------------------------------------------------------- dig loop
        // Added by the dig audio pass (DinoDigger-7c4). Before it, the whole dig ran on
        // Crumble + Chime + ItemPop, so a crystal pop, a geode fuse and a machine waking up
        // were all literally the same sound. These slots split those apart.
        // Clip -> file mapping and the offline loudness maths: Tools/ASSET_SOURCES.md.

        /// <summary>Three cracks for the same event; AudioManager rotates them so repeated
        /// bites on one tile never machine-gun the identical sample.</summary>
        [Header("SFX — digging")]
        public AudioClip TileCrackA;
        public AudioClip TileCrackB;
        public AudioClip TileCrackC;
        /// <summary>Low soft thud for a tile finishing its gravity fall (throttled).</summary>
        public AudioClip LandingThump;

        [Header("SFX — dig treasures")]
        public AudioClip CrystalPop;
        /// <summary>Fatter pop reserved for a multi-tile crystal blob.</summary>
        public AudioClip CrystalPopBig;
        /// <summary>Fizz while an armed geode counts down.</summary>
        public AudioClip FuseSizzle;
        /// <summary>The geode going off — soft, not a bang.</summary>
        public AudioClip Whumph;
        public AudioClip PotCrack;
        public AudioClip CoinSpray;
        public AudioClip BoneRattle;
        public AudioClip BonePop;

        [Header("SFX — machines & creatures")]
        public AudioClip CeremonyPoof;
        public AudioClip MachineWake;
        /// <summary>Sad-cute wobble for a machine that is not ready yet.</summary>
        public AudioClip Gurgle;
        public AudioClip Toot;
        public AudioClip Giggle;
        public AudioClip WaterGush;

        // Dig Loop 2.0 tiles (Vein / Mushroom) and the depth ladder landed alongside this audio
        // pass. The clips and hooks are wired and ready; the two tile ones are not called yet
        // because the controller callbacks they belong to (OnMushroomBounced, the vein spark
        // walk) were still being written. Wiring each is a one-line Audio?.X() at that callback.
        [Header("SFX — depth & Dig Loop 2.0 tiles")]
        /// <summary>The depth ladder arriving in the pit.</summary>
        public AudioClip LadderDing;
        /// <summary>One segment of a gem vein sparking; fired per segment along the run.</summary>
        public AudioClip SparkZap;
        /// <summary>A bite bouncing off a mushroom tile.</summary>
        public AudioClip Boing;

        [Header("Mix")]
        [Range(0f, 1f)] public float MusicVolume = 0.5f;
        [Range(0f, 1f)] public float SfxVolume = 0.9f;

        // Per-category trims sit UNDER SfxVolume (they multiply it), so a parent can still
        // pull everything down with one knob while the mix keeps its shape. Digging is the
        // sound the child hears hundreds of times a session, so it is deliberately not the
        // loudest thing in the game — rewards are.
        [Header("Mix — SFX categories")]
        [Range(0f, 1f)] public float DigVolume = 0.75f;
        [Range(0f, 1f)] public float RewardVolume = 0.9f;
        [Range(0f, 1f)] public float MachineVolume = 0.8f;
        [Range(0f, 1f)] public float CreatureVolume = 0.85f;
        [Range(0f, 1f)] public float UiVolume = 0.7f;
        /// <summary>The party vamp is background texture, never the main event.</summary>
        [Range(0f, 1f)] public float DanceLoopVolume = 0.35f;

        /// <summary>Random pitch spread applied to every one-shot (+/- this fraction), so
        /// repeated taps sound like repeated events rather than one looping sample.</summary>
        [Range(0f, 0.25f)] public float PitchJitter = 0.05f;
    }
}
