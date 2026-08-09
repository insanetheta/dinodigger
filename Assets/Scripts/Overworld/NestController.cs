using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;

namespace DinoDigger.Overworld
{
    /// <summary>
    /// The meadow's nest prop — RETIRED AS A MECHANIC, KEPT AS A PLACE (DinoDigger-5ve).
    ///
    /// It used to be the egg-shard machine: shards flew to it, its egg assembled through five
    /// states, and a full nest ran the hatch ceremony. Save v5 retires that whole path — bones
    /// and the skeleton board replace shards, and the Dino-Matic replaces the hatch — so the
    /// nest keeps only what was always good about it: it is the spot in the meadow where new
    /// dinosaurs come from.
    ///
    /// WHY KEEP IT AT ALL, rather than deleting the prop with its system? Three reasons, and
    /// the third is the deciding one:
    ///   - it is already placed, wired and loved in the shipped scene, and tearing it out would
    ///     need a scene rebuild to gain literally nothing;
    ///   - the whole roster of shard dinos visibly came out of it, so an empty meadow corner
    ///     would read as something LOST rather than something finished; and
    ///   - it becomes the board's WORLD ANCHOR: the collection lives in a HUD panel, which is
    ///     nowhere, so every banked bone also pops the nest — a punch and a sparkle out in the
    ///     world — and the meadow keeps a physical place where progress is visible.
    ///
    /// It therefore shows its FINAL, fully-assembled egg forever (the nest did its job) and
    /// listens to <see cref="GameEvents.BoneBanked"/> instead of the retired shard event. It
    /// still registers <see cref="GameEvents.NestTargetProvider"/> so camera framing has a
    /// meadow focal point. Persists nothing; fully null-tolerant.
    /// </summary>
    public class NestController : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _base;    // twig-ring nest bowl
        [SerializeField] private SpriteRenderer _egg;     // the finished egg it hatched
        [SerializeField] private ParticleSystem _sparkle; // bone-banked echo
        [SerializeField] private PlaceholderLibrary _library;

        private int _bonePops;   // test-observable

        // TEST HOOKS (integration runner; no reflection).
        internal Sprite TestEggSprite => _egg != null ? _egg.sprite : null;
        internal Vector3 TestEggWorld => EggWorld;
        internal int TestBonePops => _bonePops;

        /// <summary>The nest's focal point (the egg, else the bowl, else this transform).</summary>
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
            GameEvents.BoneBanked += OnBoneBanked;
        }

        private void OnDisable()
        {
            GameEvents.BoneBanked -= OnBoneBanked;
            // Only surrender the provider if it is still ours.
            if (GameEvents.NestTargetProvider != null)
            {
                GameEvents.NestTargetProvider = null;
            }
        }

        private void Start()
        {
            ShowFinishedEgg();
        }

        /// <summary>A bone landed on the board: the nest pops and sparkles, so the collection
        /// filling has a place in the WORLD and not only in a HUD panel.</summary>
        private void OnBoneBanked(DinoType species, int boneIndex)
        {
            _bonePops++;

            if (_egg != null)
            {
                Tween.PunchScale(_egg.transform, 0.3f, 0.35f);
            }

            if (_sparkle != null)
            {
                _sparkle.Emit(10);
            }
        }

        /// <summary>Park the nest on its LAST assembly sprite — the whole, hatched egg — and
        /// leave it there. State-derived and idempotent: there is exactly one look a retired
        /// nest can have, so no code path can strand it mid-assembly.</summary>
        public void ShowFinishedEgg()
        {
            Sprite[] set = _library != null ? _library.EggAssemblySprites : null;
            if (_egg != null && set != null && set.Length > 0)
            {
                _egg.sprite = set[set.Length - 1];
            }
        }
    }
}
