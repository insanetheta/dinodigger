using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;

namespace DinoDigger.Overworld
{
    /// <summary>
    /// One town building under construction. A pure VIEW + progress state machine:
    /// it steps through construction states 0..3 (ground-break, foundation, frame,
    /// walls) then FINISHED, swapping its sprite from
    /// <see cref="PlaceholderLibrary.BuildingStates"/> at each step. It knows nothing
    /// about coins, builders, or events — <see cref="TownController"/> banks builder
    /// work into it via <see cref="AddWork"/> and translates state changes into
    /// <see cref="Core.GameEvents"/>. Fully null-tolerant so a scene missing building
    /// art still advances (the state number is what matters to tests).
    /// </summary>
    public class BuildingController : MonoBehaviour
    {
        /// <summary>Number of under-construction states (0..3); reaching this index means FINISHED.</summary>
        public const int ConstructionStates = 4;

        // "Under construction" barrier sign (DinoDigger-771): a child prop planted in
        // front of the site while it builds, popped away when it finishes. Null-tolerant:
        // absent art (or a stale library) simply means no sign object is ever created.
        private const int SignSortOffset = 1;              // just above the building
        private static readonly Vector3 SignOffset = new Vector3(0.55f, -0.05f, 0f); // toward screen-south/front

        private SpriteRenderer _renderer;
        private ParticleSystem _workFx;
        private PlaceholderLibrary _library;
        private GameObject _sign;
        private bool _signDismissed;

        private int _state;        // 0..3 building; == ConstructionStates (4) means finished
        private float _worked;     // seconds of builder work banked toward the next state
        private float _perState = 8f;

        /// <summary>Current construction-state index (0..3), or <see cref="ConstructionStates"/> when done.</summary>
        public int State => _state;

        /// <summary>True once every construction state has been worked through.</summary>
        public bool IsFinished => _state >= ConstructionStates;

        // TEST HOOKS (integration runner; no reflection).
        internal int TestState => _state;
        internal Sprite TestSprite => _renderer != null ? _renderer.sprite : null;
        internal bool TestSignActive => _sign != null && _sign.activeSelf;

        /// <summary>Wire the site's renderer, work particles, and per-state timing.</summary>
        public void Init(PlaceholderLibrary library, GameConfig config, SpriteRenderer renderer,
            ParticleSystem workFx)
        {
            _library = library;
            _renderer = renderer;
            _workFx = workFx;
            _perState = config != null ? Mathf.Max(0.05f, config.TownSecondsPerBuildState) : 8f;
            _state = 0;
            _worked = 0f;
            _signDismissed = false;
            EnsureSign();
            ApplyVisual();
        }

        /// <summary>Bank <paramref name="seconds"/> of builder work; advances one state per
        /// <c>TownSecondsPerBuildState</c> worked (may cross several in one big step).
        /// Returns the number of states advanced this call.</summary>
        public int AddWork(float seconds)
        {
            if (seconds <= 0f || IsFinished)
            {
                return 0;
            }

            _worked += seconds;
            int advanced = 0;
            while (!IsFinished && _worked >= _perState)
            {
                _worked -= _perState;
                _state++;
                advanced++;
                ApplyVisual();
            }

            return advanced;
        }

        /// <summary>Puff of dust/crumbs while the crew hammers (called by TownController).</summary>
        public void EmitWorkPuff()
        {
            if (_workFx != null)
            {
                _workFx.Emit(4);
            }
        }

        private void ApplyVisual()
        {
            if (_renderer == null || _library == null)
            {
                return;
            }

            Sprite[] set = _library.BuildingStates;
            if (set == null || set.Length == 0)
            {
                return;
            }

            // States 0..3 index directly; FINISHED uses the last slot (index 4 == finished art).
            int idx = Mathf.Clamp(_state, 0, set.Length - 1);
            Sprite s = set[idx];
            if (s != null)
            {
                _renderer.sprite = s;
            }

            if (IsFinished)
            {
                DismissSign();
            }
        }

        /// <summary>Plant the "under construction" barrier sign in front of the site (bottom-center
        /// pivot on the ground, one sort step above the building, nudged screen-south so it never
        /// covers the structure). No-op when the sign art is absent — feature silently off.</summary>
        private void EnsureSign()
        {
            if (_sign != null || _renderer == null || _library == null || _library.ConstructionSign == null)
            {
                return;
            }

            _sign = new GameObject("ConstructionSign");
            _sign.transform.SetParent(transform, false);
            _sign.transform.localPosition = SignOffset;

            var sr = _sign.AddComponent<SpriteRenderer>();
            sr.sprite = _library.ConstructionSign;
            sr.sortingOrder = _renderer.sortingOrder + SignSortOffset;
        }

        /// <summary>Retire the sign with a happy little pop then scale-out (once). Guarded by
        /// <see cref="_signDismissed"/> so the finish-state repaint can't retrigger it.</summary>
        private void DismissSign()
        {
            if (_sign == null || _signDismissed)
            {
                return;
            }

            _signDismissed = true;
            Transform t = _sign.transform;
            Tween.ScaleTo(t, Vector3.one * 1.25f, 0.12f, () =>
                Tween.ScaleTo(t, Vector3.zero, 0.2f, () =>
                {
                    if (_sign != null)
                    {
                        _sign.SetActive(false);
                    }
                }));
        }
    }
}
