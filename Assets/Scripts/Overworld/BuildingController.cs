using UnityEngine;
using DinoDigger.Config;

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

        private SpriteRenderer _renderer;
        private ParticleSystem _workFx;
        private PlaceholderLibrary _library;

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
        }
    }
}
