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
    public class BuildingController : MonoBehaviour, ITappable
    {
        /// <summary>Number of under-construction states (0..3); reaching this index means FINISHED.</summary>
        public const int ConstructionStates = 4;

        // "Under construction" barrier sign (DinoDigger-771): a child prop planted in
        // front of the site while it builds, popped away when it finishes. Null-tolerant:
        // absent art (or a stale library) simply means no sign object is ever created.
        private const int SignSortOffset = 1;              // just above the building
        private static readonly Vector3 SignOffset = new Vector3(0.55f, -0.05f, 0f); // toward screen-south/front

        // Fruit Stand identity (DinoDigger-pu3): the stand building gets a warm produce-stall
        // tint plus a fruit sprite that bobs above it like a shop sign, both applied only once
        // the building FINISHES. Reuses an existing fruit sprite — no new hand-made art.
        private static readonly Color FruitStandTint = new Color(1f, 0.82f, 0.55f); // warm produce-stall wash
        private static readonly Vector3 StandSignOffset = new Vector3(0f, 0.9f, 0f);  // riding above the roof
        private const int StandSignSortOffset = 2;

        private SpriteRenderer _renderer;
        private ParticleSystem _workFx;
        private PlaceholderLibrary _library;
        private GameObject _sign;
        private bool _signDismissed;

        private bool _isFruitStand;
        private Sprite _standSignSprite;
        private GameObject _standSign;
        private float _standBobPhase;

        private int _state;        // 0..3 building; == ConstructionStates (4) means finished
        private float _worked;     // seconds of builder work banked toward the next state
        private float _perState = 8f;

        // Recess Time (DinoDigger-x07): once FINISHED, the building becomes a tap target — a
        // BoxCollider2D sized to its sprite plus ITappable routing, so GameManager.FindTappable
        // picks it up and a tap throws a 15s dino party. Wired to its owning TownController +
        // build-order index so the tap can reach the recess service. Under-construction sites
        // stay non-tappable (no collider until IsFinished).
        private TownController _town;
        private int _index = -1;
        private BoxCollider2D _tapCollider;

        /// <summary>Current construction-state index (0..3), or <see cref="ConstructionStates"/> when done.</summary>
        public int State => _state;

        /// <summary>True once every construction state has been worked through.</summary>
        public bool IsFinished => _state >= ConstructionStates;

        /// <summary>Seconds of builder work banked toward the next state (the mid-state
        /// partial). Persisted so a reloaded site resumes exactly where it left off.</summary>
        public float WorkedPartial => _worked;

        // TEST HOOKS (integration runner; no reflection).
        internal int TestState => _state;
        internal Sprite TestSprite => _renderer != null ? _renderer.sprite : null;
        internal bool TestSignActive => _sign != null && _sign.activeSelf;
        internal bool TestFruitStandDressed => _isFruitStand && IsFinished &&
                                               _standSign != null && _standSign.activeSelf;

        /// <summary>Wire the site's renderer, work particles, and per-state timing. Starts
        /// fresh at construction state 0 by default; a reloaded site passes its saved
        /// <paramref name="initialState"/> (0..3, or <see cref="ConstructionStates"/> for a
        /// finished building) and <paramref name="initialWorked"/> banked partial so it
        /// resumes in place. A restored FINISHED building shows its finished art with no
        /// construction sign (nothing to dismiss, so no pop replay).</summary>
        public void Init(PlaceholderLibrary library, GameConfig config, SpriteRenderer renderer,
            ParticleSystem workFx, int initialState = 0, float initialWorked = 0f)
        {
            _library = library;
            _renderer = renderer;
            _workFx = workFx;
            _perState = config != null ? Mathf.Max(0.05f, config.TownSecondsPerBuildState) : 8f;
            _state = Mathf.Clamp(initialState, 0, ConstructionStates);
            _worked = Mathf.Max(0f, initialWorked);
            _signDismissed = false;
            if (!IsFinished)
            {
                // Only an in-progress site carries the "under construction" barrier sign;
                // a restored finished building never creates one (no dismiss animation).
                EnsureSign();
            }

            ApplyVisual();
            RefreshTappable(); // a restored-finished building is immediately a tap target
        }

        // TEST HOOK (integration runner; no reflection).
        internal bool TestIsTappable => _tapCollider != null;

        /// <summary>Wire the building to its owning town service + build-order index so a tap
        /// can reach the recess flow, then (re)evaluate its tap collider. Called by
        /// <see cref="TownController"/> right after <see cref="Init"/>, on both the fresh
        /// break-ground and the reload paths.</summary>
        internal void WireRecess(TownController town, int index)
        {
            _town = town;
            _index = index;
            RefreshTappable();
        }

        /// <summary>A finished building is a tap target: give it a BoxCollider2D sized to its
        /// sprite (a sensible 1x1 default in placeholder-only, no-art runs) so
        /// <see cref="GameManager.FindTappable"/> resolves the tap to this building. No-op while
        /// under construction (never tappable) and idempotent once the collider exists.</summary>
        private void RefreshTappable()
        {
            if (!IsFinished || _tapCollider != null)
            {
                return;
            }

            _tapCollider = gameObject.GetComponent<BoxCollider2D>();
            if (_tapCollider == null)
            {
                _tapCollider = gameObject.AddComponent<BoxCollider2D>();
            }

            _tapCollider.isTrigger = true;

            if (_renderer != null && _renderer.sprite != null)
            {
                Bounds b = _renderer.sprite.bounds; // local sprite space (building GO is unscaled)
                _tapCollider.size = b.size;
                _tapCollider.offset = b.center;
            }
            else
            {
                _tapCollider.size = Vector2.one;
                _tapCollider.offset = Vector2.zero;
            }
        }

        /// <summary>Tap on a FINISHED building: route to the town's recess service (bounce +
        /// chime + confetti, and a party if any residents are free). The collider only exists
        /// once finished, so the IsFinished guard is just belt-and-braces.</summary>
        public void OnTapped(Vector2 worldPoint)
        {
            if (!IsFinished)
            {
                return;
            }

            _town?.OnBuildingTapped(this, _index);
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

            if (IsFinished)
            {
                RefreshTappable(); // just finished: becomes a tap target for a recess party
            }

            return advanced;
        }

        /// <summary>Flag this building as the Fruit Stand and hand it the fruit sprite to
        /// fly as its shop sign. The dressing (warm tint + bobbing fruit) only shows once the
        /// building is FINISHED, so a mid-build stand looks like any other site; re-applied
        /// through <see cref="ApplyVisual"/> so a restored-finished stand dresses immediately.</summary>
        public void MarkFruitStand(Sprite signSprite)
        {
            _isFruitStand = true;
            _standSignSprite = signSprite;
            ApplyVisual();
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

            if (_isFruitStand)
            {
                ApplyFruitStandDressing();
            }
        }

        /// <summary>Dress the FINISHED stand: warm the building sprite and raise the bobbing
        /// fruit sign. No-op while still building (the stand looks like any site until done),
        /// and null-tolerant so a stand with no fruit sprite simply keeps the tint.</summary>
        private void ApplyFruitStandDressing()
        {
            if (_renderer == null || !IsFinished)
            {
                return;
            }

            _renderer.color = FruitStandTint;

            if (_standSign == null && _standSignSprite != null)
            {
                _standSign = new GameObject("FruitStandSign");
                _standSign.transform.SetParent(transform, false);
                _standSign.transform.localPosition = StandSignOffset;

                var sr = _standSign.AddComponent<SpriteRenderer>();
                sr.sprite = _standSignSprite;
                sr.sortingOrder = _renderer.sortingOrder + StandSignSortOffset;
            }

            if (_standSign != null)
            {
                _standSign.SetActive(true);
            }
        }

        /// <summary>Gentle idle bob for the fruit shop-sign so the finished stand reads as
        /// "open for business". Only runs once the sign exists (finished stand).</summary>
        private void Update()
        {
            if (_standSign == null || !_standSign.activeSelf)
            {
                return;
            }

            _standBobPhase += Time.deltaTime * 2.5f;
            float y = Mathf.Sin(_standBobPhase) * 0.08f;
            _standSign.transform.localPosition = StandSignOffset + new Vector3(0f, y, 0f);
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
