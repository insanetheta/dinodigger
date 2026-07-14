using System;
using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;

namespace DinoDigger.Overworld
{
    /// <summary>
    /// The egg-shard nest prop that lives in the meadow. It:
    ///   - registers itself as <see cref="GameEvents.NestTargetProvider"/> so dug egg
    ///     shards fly TO the egg sitting in the nest;
    ///   - listens to <see cref="GameEvents.ShardCollected"/> and advances the egg's
    ///     5-state assembly sprite, its thresholds SCALED onto the current shard
    ///     requirement (GameManager.ShardsPerHatch, which escalates 5/8/15/20 per egg):
    ///     state = floor(ShardCount / requirement * (states-1)), punch-scaling +
    ///     sparkling on each arrival;
    ///   - drives the ceremony egg wobble/crack visual on GameManager's cue.
    ///
    /// Persists nothing itself — ShardCount lives in the save (read via GameManager).
    /// Fully null-tolerant so a partially-wired scene never throws. Art comes from the
    /// <see cref="PlaceholderLibrary"/> (NestSprite + EggAssemblySprites); real generated
    /// art can replace those fields in place with no code change here.
    /// </summary>
    public class NestController : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _base;    // twig-ring nest bowl
        [SerializeField] private SpriteRenderer _egg;     // assembling egg
        [SerializeField] private ParticleSystem _sparkle; // shard-arrival + hatch fx
        [SerializeField] private PlaceholderLibrary _library;

        private int _assemblyIndex = -1;

        // TEST HOOKS (integration runner; no reflection).
        internal int TestAssemblyIndex => _assemblyIndex;
        internal Sprite TestEggSprite => _egg != null ? _egg.sprite : null;
        internal Vector3 TestEggWorld => EggWorld;
        internal int TestStateCount =>
            _library != null && _library.EggAssemblySprites != null &&
            _library.EggAssemblySprites.Length > 0 ? _library.EggAssemblySprites.Length : 5;

        /// <summary>Where a dug shard flies / the ceremony spawns the new dino: the egg's
        /// world position (falls back to the nest base, then this transform).</summary>
        public Vector3 EggWorld =>
            _egg != null ? _egg.transform.position :
            _base != null ? _base.transform.position : transform.position;

        private void Awake()
        {
            if (_base != null && _library != null && _library.NestSprite != null)
            {
                _base.sprite = _library.NestSprite;
            }
        }

        private void OnEnable()
        {
            GameEvents.NestTargetProvider = () => EggWorld;
            GameEvents.ShardCollected += OnShardCollected;
        }

        private void OnDisable()
        {
            GameEvents.ShardCollected -= OnShardCollected;
            // Only surrender the provider if it is still ours.
            if (GameEvents.NestTargetProvider != null)
            {
                GameEvents.NestTargetProvider = null;
            }
        }

        private void Start()
        {
            // Reflect the saved shard progress (RestoreFromSave also pushes this, but
            // self-refreshing covers scenes wired without a GameManager restore call).
            int count = GameManager.Instance != null ? GameManager.Instance.ShardCount : 0;
            RefreshAssembly(count);
        }

        private void OnShardCollected(int total)
        {
            int before = _assemblyIndex;
            RefreshAssembly(total);

            // A shard landed: always give a little pop + sparkle so the arrival reads,
            // and a bigger punch when the assembly state actually stepped forward.
            if (_egg != null)
            {
                Tween.PunchScale(_egg.transform, _assemblyIndex != before ? 0.4f : 0.22f, 0.35f);
            }

            if (_sparkle != null)
            {
                _sparkle.Emit(_assemblyIndex != before ? 16 : 8);
            }
        }

        /// <summary>Set the egg's assembly sprite for a given banked shard count.
        /// No fx — used for the initial restore and the post-ceremony reset.</summary>
        public void RefreshAssembly(int shardCount)
        {
            int idx = AssemblyIndex(shardCount);
            _assemblyIndex = idx;

            Sprite[] set = _library != null ? _library.EggAssemblySprites : null;
            if (_egg != null && set != null && set.Length > 0)
            {
                _egg.sprite = set[Mathf.Clamp(idx, 0, set.Length - 1)];
            }
        }

        /// <summary>Ceremony cue: show the fully-assembled egg, then wobble + crack it,
        /// firing <paramref name="onHatched"/> when the wobble completes (the caller
        /// spawns the new dino there). Uses ShakeRotation so its onComplete always fires.</summary>
        public void PlayHatch(Action onHatched)
        {
            Sprite[] set = _library != null ? _library.EggAssemblySprites : null;
            if (_egg != null && set != null && set.Length > 0)
            {
                _egg.sprite = set[set.Length - 1]; // whole egg
                _assemblyIndex = set.Length - 1;
            }

            if (_sparkle != null)
            {
                _sparkle.Emit(24);
            }

            Transform t = _egg != null ? _egg.transform : transform;
            Tween.ShakeRotation(t, 16f, 1.0f, 3, () => onHatched?.Invoke());
        }

        private int AssemblyIndex(int shardCount)
        {
            Sprite[] set = _library != null ? _library.EggAssemblySprites : null;
            int states = set != null && set.Length > 0 ? set.Length : 5;
            if (states <= 1)
            {
                return 0;
            }

            // Scale the (states-1) build steps across the CURRENT requirement so the egg
            // reads as "full" exactly as the ceremony fires, whatever the requirement is
            // (5/8/15/20). At requirement 20 this reproduces the old 0/5/10/15/20 steps.
            int per = GameManager.Instance != null ? GameManager.Instance.ShardsPerHatch : 20;
            per = Mathf.Max(1, per);
            int idx = Mathf.FloorToInt((float)shardCount * (states - 1) / per);
            return Mathf.Clamp(idx, 0, states - 1);
        }
    }
}
