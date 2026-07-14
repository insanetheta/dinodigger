using System;
using System.Collections.Generic;
using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;

namespace DinoDigger.Overworld
{
    /// <summary>
    /// A hatched dino. Two roles:
    ///   BUDDY    — one of (at most) two walk companions that loosely follow the
    ///              backhoe at an assigned offset slot.
    ///   RESIDENT — lives in the fenced meadow: strolls between points inside it,
    ///              naps occasionally, and dances when tapped (which promotes it
    ///              to buddy via GameManager).
    /// Movement is a small state machine with an explicit IDLE state that writes
    /// NO positions at all — every walking state clamps its step to the remaining
    /// distance and snaps onto its target on arrival, so a settled dino can never
    /// oscillate back and forth across its goal (the idle-jitter bug).
    /// Also provides generic primitives (WalkTo, StartParade, carry attach) used
    /// by GameManager for the species superpowers and the milestone parade.
    /// </summary>
    public class DinoController : MonoBehaviour, ITappable
    {
        private enum Mode { Idle, Follow, Stroll, Travel, Eat, Dance, Nap, Parade, Work }

        private const float ArriveEps = 0.09f;       // world units: snap + stop here
        private const float WaypointEps = 0.18f;     // advance to next route point
        private const float FidgetMin = 4f;          // idle "sniff" wiggle cadence
        private const float FidgetMax = 7f;
        private const float NapChance = 0.3f;        // resident idle -> nap odds
        private const float NapMin = 4f;
        private const float NapMax = 7f;
        private const float NapAngle = -22f;         // lie-down lean (deg)
        private const float WalkAnimFps = 6f;        // walk-cycle frames/sec at base follow speed

        // Builder construction-worker gear (DinoDigger-771).
        private const float HatInsetFrac = 0.15f;    // hat sits this far below the head-bounds top
        private const float HatTilt = -8f;           // jaunty tilt (deg)
        private const float MalletHeightFrac = 0.6f; // mallet rides this far up the body bounds
        private const float MalletSideFrac = 0.45f;  // ...and this far out toward the build site

        [SerializeField] private SpriteRenderer _renderer;
        [SerializeField] private ParticleSystem _hearts;
        [SerializeField] private ParticleSystem _poof;

        private DinoDefinition _def;
        private GameConfig _config;
        private Transform _follow;
        private Vector2 _slot;
        private OverworldMap _map;
        private MeadowArea _meadow;

        private Mode _mode = Mode.Idle;
        private Dir8 _facing = Dir8.S;
        private FacingSmoother _facingSmoother;

        // Active 8-dir sprite set for the CURRENT growth stage. Swapped whenever the
        // stage changes so the facing indexer draws from the matching stage art
        // (baby/kid/adult) rather than only scaling one set. _spriteStage tracks which
        // stage _activeSprites was built for so ApplySprite can lazily re-swap.
        private Sprite[] _activeSprites;
        private GrowthStage _spriteStage = (GrowthStage)(-1);

        // Walk-cycle strides for the CURRENT stage (refreshed with _activeSprites).
        // Both null for dinos/stages without stride art -> the cycler is inert and
        // the sprite behaves exactly as before (static facing frame).
        private Sprite[] _strideA;
        private Sprite[] _strideB;
        private float _walkClock;                    // wraps 0..4 across the cycle
        private int _walkFrame;                      // 0 idle, 1 A, 2 idle, 3 B

        // Idle / resident stroll state.
        private float _idleTimer;                    // counts down to fidget/stroll/nap
        private Vector3 _strollTarget;

        // Travel (BFS waypoint walk) state.
        private readonly List<Vector3> _route = new List<Vector3>();
        private int _routeIndex;
        private float _travelSpeedMul = 1f;
        private Action _onTravelArrived;

        // Eat state.
        private Vector3 _eatTarget;
        private Action _onReachedFood;

        // Dance / nap.
        private bool _busyDancing;
        private bool _napping;
        private float _napTimer;
        private float _napPuffTimer;

        // Builder work (town construction): a resident stays put at the site and bobs.
        private float _workBobTimer;
        // True from the moment a builder is dispatched (GoWork) until it either arrives
        // and starts working or is recalled — so a still-COMMUTING builder can be turned
        // around, not just one that has already clocked in.
        private bool _headingToWork;

        // Construction-worker gear (DinoDigger-771). Created lazily on the first GoWork
        // from the art library (null-tolerant: no sprite -> no child object -> feature
        // silently absent). Visibility is DERIVED from state every LateUpdate rather than
        // toggled per exit-path: the hat shows while (_headingToWork || Work), the mallet
        // only while Work. So EVERY path that leaves the build assignment — StopWork,
        // BecomeBuddy, BecomeResident, ResumeRole, Dance, a recall mid-commute — hides the
        // gear automatically the next frame simply by clearing _mode / _headingToWork; the
        // gear can never strand on a non-builder.
        private PlaceholderLibrary _library;
        private GameObject _hat;
        private SpriteRenderer _hatRenderer;
        private GameObject _mallet;
        private SpriteRenderer _malletRenderer;
        private float _buildingX;                    // world-x of the site being built (mallet side)

        // Parade.
        private Vector3 _paradeCenter;
        private float _paradePhase;
        private float _paradeElapsed;
        private float _paradeDuration;

        // Carry (Triceratops fruit courier): the carried pickup rides here.
        private Transform _carried;

        public DinoType Type => _def != null ? _def.Type : DinoType.TRex;
        public GrowthStage Stage { get; private set; } = GrowthStage.Baby;
        public int FruitEaten { get; private set; }
        public bool IsHungry => Stage != GrowthStage.Big && _mode != Mode.Eat;
        public bool IsBig => Stage == GrowthStage.Big;

        /// <summary>True for the (max 2) walk companions; false for meadow residents.</summary>
        public bool IsBuddy { get; private set; } = true;

        /// <summary>Busy with a directed activity — the superpower scans skip these.</summary>
        public bool IsBusy => _mode == Mode.Eat || _mode == Mode.Dance ||
                              _mode == Mode.Travel || _mode == Mode.Parade ||
                              _mode == Mode.Work || _carried != null;

        /// <summary>Working a town construction site (a NON-buddy resident builder).</summary>
        public bool IsWorking => _mode == Mode.Work;

        /// <summary>Currently hauling a pickup on its head (Trike courier run).</summary>
        public bool IsCarrying => _carried != null;

        /// <summary>Mid scripted walk (WalkTo). Used by the courier watchdog.</summary>
        public bool IsTraveling => _mode == Mode.Travel;

        // TEST HOOKS for the integration runner.
        internal bool TestBusyDancing => _busyDancing;
        internal ParticleSystem TestHearts => _hearts;
        internal Dir8 TestFacing => _facing;
        internal bool TestIsSettled => _mode == Mode.Idle || _mode == Mode.Nap;
        internal bool TestNapping => _napping;
        internal bool TestCarrying => _carried != null;
        internal Sprite TestSprite => _renderer != null ? _renderer.sprite : null;
        internal Sprite[] TestActiveSprites => _activeSprites;
        internal Sprite TestStageDirSprite(GrowthStage stage, Dir8 dir) =>
            _def != null ? Direction8.Pick(_def.StageSprites(stage), dir, null) : null;
        internal Sprite TestStrideDirSprite(GrowthStage stage, int phase, Dir8 dir) =>
            _def != null ? Direction8.Pick(_def.StrideSprites(stage, phase), dir, null) : null;
        internal bool TestHasStrides => _strideA != null || _strideB != null;
        internal int TestWalkFrame => _walkFrame;
        internal bool TestWorking => _mode == Mode.Work;
        internal bool TestHatActive => _hat != null && _hat.activeSelf;
        internal bool TestMalletActive => _mallet != null && _mallet.activeSelf;

        private void Awake()
        {
            if (_renderer == null)
            {
                _renderer = GetComponent<SpriteRenderer>();
            }

            _facingSmoother.Reset(_facing);
        }

        /// <summary>Wire renderer + particles when this dino is built at runtime.</summary>
        public void AttachParticles(SpriteRenderer renderer, ParticleSystem hearts, ParticleSystem poof)
        {
            if (renderer != null)
            {
                _renderer = renderer;
            }

            _hearts = hearts;
            _poof = poof;
        }

        public void Init(DinoDefinition def, GameConfig config, Transform follow,
            Vector2 slot, GrowthStage stage, int fruitEaten)
        {
            _def = def;
            _config = config;
            _follow = follow;
            _slot = slot;
            Stage = stage;
            FruitEaten = fruitEaten;
            _mode = Mode.Idle;
            _idleTimer = UnityEngine.Random.Range(FidgetMin, FidgetMax);

            ApplyScale(false);
            ApplySprite();

            if (_renderer != null && _def != null)
            {
                _renderer.color = Color.white; // sprite already colored; keep tintable
            }
        }

        /// <summary>Late wiring for map/meadow (both optional, null-tolerant).</summary>
        public void ConfigureWorld(OverworldMap map, MeadowArea meadow)
        {
            _map = map;
            _meadow = meadow;
        }

        public void SetSlot(Vector2 slot) => _slot = slot;

        // ------------------------------------------------------------------ roles

        /// <summary>Become a walk buddy in the given follow slot.</summary>
        public void BecomeBuddy(Vector2 slot)
        {
            IsBuddy = true;
            _slot = slot;
            CancelNap();
            _onTravelArrived = null;
            _headingToWork = false; // dropped from any pending build commute
            if (_mode != Mode.Eat && _mode != Mode.Dance)
            {
                _mode = Mode.Idle; // the follow check re-engages on its own next frame
            }
        }

        /// <summary>Become a meadow resident: happily trot home (BFS path when
        /// possible, straight walk otherwise — dinos are decorative movers with no
        /// physics, so a straight walk always arrives). With no meadow in the
        /// scene the dino just wanders in place where it is.
        /// <paramref name="delayHomeWalk"/> lets the hatch celebration finish
        /// first — the resident idles briefly, then the idle tick (which sends any
        /// out-of-meadow resident home) starts the trot on its own.</summary>
        public void BecomeResident(bool delayHomeWalk = false)
        {
            IsBuddy = false;
            CancelNap();
            if (!delayHomeWalk && _meadow != null && !_meadow.ContainsInterior(transform.position))
            {
                WalkTo(_meadow.RandomInteriorPoint(), 1f, null);
            }
            else if (_mode != Mode.Eat && _mode != Mode.Dance)
            {
                EnterIdle();
            }
        }

        /// <summary>Back to the role's default behavior after a directed activity.</summary>
        private void ResumeRole()
        {
            if (!IsBuddy && _meadow != null && !_meadow.ContainsInterior(transform.position))
            {
                WalkTo(_meadow.RandomInteriorPoint(), 1f, null);
                return;
            }

            EnterIdle();
        }

        // ----------------------------------------------------------------- update

        private void Update()
        {
            if (_config == null)
            {
                return;
            }

            switch (_mode)
            {
                case Mode.Idle:
                    TickIdle();
                    break;
                case Mode.Follow:
                    TickFollowChase();
                    break;
                case Mode.Stroll:
                    TickStroll();
                    break;
                case Mode.Travel:
                    TickTravel();
                    break;
                case Mode.Eat:
                    TickEat();
                    break;
                case Mode.Nap:
                    TickNap();
                    break;
                case Mode.Parade:
                    TickParade();
                    break;
                case Mode.Work:
                    TickWork();
                    break;
                case Mode.Dance:
                    break; // driven by tweens
            }
        }

        private Vector3 FollowHome()
        {
            return _follow != null
                ? _follow.position + new Vector3(_slot.x, _slot.y, 0f)
                : transform.position;
        }

        /// <summary>IDLE writes no positions. Buddies watch their follow slot and
        /// re-chase only once it drifts past the full slack; residents count down
        /// to a nap or a stroll. Everyone does a tiny rotation "sniff" now and
        /// then (rotation only — position stays bit-identical while settled).</summary>
        private void TickIdle()
        {
            if (IsBuddy)
            {
                if (_follow != null &&
                    Vector2.Distance(transform.position, FollowHome()) > _config.DinoFollowSlack)
                {
                    _mode = Mode.Follow;
                    return;
                }

                _idleTimer -= Time.deltaTime;
                if (_idleTimer <= 0f)
                {
                    _idleTimer = UnityEngine.Random.Range(FidgetMin, FidgetMax);
                    if (!_busyDancing)
                    {
                        Tween.ShakeRotation(transform, 4f, 0.5f, 1);
                    }
                }

                return;
            }

            // Resident: nap or stroll on a relaxed timer.
            _idleTimer -= Time.deltaTime;
            if (_idleTimer > 0f)
            {
                return;
            }

            _idleTimer = UnityEngine.Random.Range(2.5f, 5f);

            // A resident that ended up outside the meadow (post-hatch celebration,
            // finished eating near the backhoe, parade over...) always heads home
            // first; naps and strolls only happen inside the fence.
            if (_meadow != null && !_meadow.ContainsInterior(transform.position))
            {
                WalkTo(_meadow.RandomInteriorPoint(), 1f, null);
                return;
            }

            if (UnityEngine.Random.value < NapChance)
            {
                BeginNap();
            }
            else
            {
                _strollTarget = PickResidentStrollPoint();
                _mode = Mode.Stroll;
            }
        }

        /// <summary>Buddy chasing its follow slot. Runs until it lands ON the slot
        /// (step clamped to remaining distance), then settles to Idle — no
        /// half-slack limbo where it could flip between chase and rest.</summary>
        private void TickFollowChase()
        {
            if (_follow == null)
            {
                EnterIdle();
                return;
            }

            if (MoveTowards(FollowHome(), _config.DinoFollowSpeed))
            {
                EnterIdle();
            }
        }

        private void TickStroll()
        {
            if (MoveTowards(_strollTarget, _config.DinoFollowSpeed * 0.4f))
            {
                EnterIdle();
            }
        }

        private Vector3 PickResidentStrollPoint()
        {
            if (_meadow != null && _meadow.ContainsInterior(transform.position))
            {
                return _meadow.RandomInteriorPoint();
            }

            // No meadow (legacy scene) or failed to get inside: wander in place.
            Vector2 rnd = UnityEngine.Random.insideUnitCircle *
                          (_config != null ? _config.DinoWanderRadius : 1f);
            return transform.position + new Vector3(rnd.x, rnd.y * 0.7f, 0f);
        }

        private void EnterIdle()
        {
            _mode = Mode.Idle;
            _idleTimer = IsBuddy
                ? UnityEngine.Random.Range(FidgetMin, FidgetMax)
                : UnityEngine.Random.Range(2.5f, 5f);
            ResetWalkAnim(); // settled: always show the idle frame, never a stride
        }

        // ----------------------------------------------------------------- travel

        /// <summary>Walk to a world point along a BFS route when the map can give
        /// one (looks right around the pond), else straight there. Fires
        /// <paramref name="onArrived"/> once, then resumes the role default.</summary>
        public void WalkTo(Vector3 target, float speedMul, Action onArrived)
        {
            target.z = transform.position.z;
            _route.Clear();
            _routeIndex = 0;

            if (_map == null || !_map.FindPath(transform.position, target, _route) || _route.Count == 0)
            {
                _route.Clear();
                _route.Add(target);
            }

            _travelSpeedMul = Mathf.Max(0.1f, speedMul);
            _onTravelArrived = onArrived;
            CancelNap();
            _mode = Mode.Travel;
        }

        private void TickTravel()
        {
            if (_routeIndex >= _route.Count)
            {
                FinishTravel();
                return;
            }

            Vector3 wp = _route[_routeIndex];
            bool last = _routeIndex == _route.Count - 1;
            if (MoveTowards(wp, _config.DinoFollowSpeed * _travelSpeedMul,
                    last ? ArriveEps : WaypointEps))
            {
                _routeIndex++;
                if (_routeIndex >= _route.Count)
                {
                    FinishTravel();
                }
            }
        }

        private void FinishTravel()
        {
            Action cb = _onTravelArrived;
            _onTravelArrived = null;
            EnterIdle();
            cb?.Invoke(); // callback may immediately set a new mode (carry chain etc.)
        }

        // ------------------------------------------------------------------- eat

        public void GoEat(Vector3 foodWorld, Action onReached)
        {
            _eatTarget = new Vector3(foodWorld.x, foodWorld.y, transform.position.z);
            _onReachedFood = onReached;
            CancelNap();
            _onTravelArrived = null;
            _mode = Mode.Eat;
        }

        private void TickEat()
        {
            if (MoveTowards(_eatTarget, _config.DinoEatSpeed, 0.25f))
            {
                Action cb = _onReachedFood;
                _onReachedFood = null;
                ResumeRole();
                cb?.Invoke();
            }
        }

        /// <summary>Register one eaten fruit; returns the new stage if it grew.</summary>
        public GrowthStage? Feed()
        {
            FruitEaten++;
            Tween.PunchScale(transform, 0.3f, 0.3f);
            EmitHearts();

            GrowthStage target = Stage;
            if (Stage == GrowthStage.Baby && FruitEaten >= _config.FruitThreshold(GrowthStage.Kid))
            {
                target = GrowthStage.Kid;
            }

            if (FruitEaten >= _config.FruitThreshold(GrowthStage.Big))
            {
                target = GrowthStage.Big;
            }

            if (target != Stage)
            {
                Stage = target;
                GrowPoof();
                return Stage;
            }

            return null;
        }

        public void ForceStage(GrowthStage stage)
        {
            Stage = stage;
            ApplyScale(false);
            ApplySprite(); // swap to the new stage's art immediately
        }

        private void GrowPoof()
        {
            ApplyScale(true);
            ApplySprite(); // swap to the grown stage's art set right away
            if (_poof != null)
            {
                _poof.Emit(20);
            }
        }

        private void ApplyScale(bool animated)
        {
            float s = _config != null ? _config.StageScale(Stage) : 1f;
            Vector3 target = Vector3.one * s;
            if (animated)
            {
                Tween.ScaleTo(transform, target, 0.4f);
            }
            else
            {
                transform.localScale = target;
            }
        }

        // ------------------------------------------------------------------- nap

        private void BeginNap()
        {
            _napping = true;
            _napTimer = UnityEngine.Random.Range(NapMin, NapMax);
            _napPuffTimer = 0.8f;
            _mode = Mode.Nap;

            // Gentle lie-down lean (rotation only — position never moves).
            Tween.Run(0.45f, t =>
            {
                if (this != null && _napping)
                {
                    transform.localRotation = Quaternion.Euler(0f, 0f, NapAngle * t);
                }
            });
        }

        private void TickNap()
        {
            _napTimer -= Time.deltaTime;

            // Sleepy "Zzz": a slow star puff drifting up every ~1.2s.
            _napPuffTimer -= Time.deltaTime;
            if (_napPuffTimer <= 0f && _poof != null)
            {
                _napPuffTimer = 1.2f;
                var ep = new ParticleSystem.EmitParams
                {
                    position = transform.position + new Vector3(0.15f, 0.45f, 0f),
                    velocity = new Vector3(0.15f, 0.7f, 0f),
                    startLifetime = 1.4f,
                    startSize = 0.22f
                };
                _poof.Emit(ep, 1);
            }

            if (_napTimer <= 0f)
            {
                CancelNap();
                EnterIdle();
            }
        }

        private void CancelNap()
        {
            if (!_napping)
            {
                return;
            }

            _napping = false;
            transform.localRotation = Quaternion.identity;
        }

        // ---------------------------------------------------------------- parade

        /// <summary>March in the milestone parade: chase a point orbiting
        /// <paramref name="center"/> for <paramref name="duration"/> seconds
        /// (phase spreads the line out), then resume the normal role.</summary>
        public void StartParade(Vector3 center, float phase, float duration)
        {
            CancelNap();
            _onTravelArrived = null;
            _onReachedFood = null;
            _paradeCenter = center;
            _paradePhase = phase;
            _paradeElapsed = 0f;
            _paradeDuration = Mathf.Max(1f, duration);
            _mode = Mode.Parade;
        }

        private void TickParade()
        {
            _paradeElapsed += Time.deltaTime;
            if (_paradeElapsed >= _paradeDuration)
            {
                ResumeRole();
                return;
            }

            // Flattened ring (iso ground plane), one revolution per ~6s.
            float ang = _paradePhase + _paradeElapsed * (Mathf.PI * 2f / 6f);
            Vector3 target = _paradeCenter + new Vector3(Mathf.Cos(ang) * 2.4f, Mathf.Sin(ang) * 1.5f, 0f);
            MoveTowards(target, _config.DinoFollowSpeed * 1.15f);
        }

        // --------------------------------------------------------- builder work

        /// <summary>Commute to a town build site, then WORK it: on arrival the dino enters
        /// a stay-put work loop (bobbing in place, counted as busy) that — unlike Idle —
        /// never sends a resident home. Reuses the existing WalkTo travel + BFS movement.
        /// Only ever called for NON-buddy residents by <see cref="TownController"/>; the
        /// player backhoe and walk buddies are never routed here.</summary>
        public void GoWork(Vector3 site, Vector3 buildingCenter, float speedMul, Action onWorking,
            PlaceholderLibrary library)
        {
            _library = library;
            _buildingX = buildingCenter.x; // which side the structure is on (mallet flip)
            _headingToWork = true;
            EquipBuilderGear();            // "puts on" the hard hat for the commute + shift
            WalkTo(site, speedMul, () =>
            {
                EnterWork();
                onWorking?.Invoke();
            });
        }

        /// <summary>Lazily build the hard-hat + mallet child overlays from the art library.
        /// Null-tolerant: a missing sprite means no child object is created, so the feature
        /// is silently absent (placeholder-only / stale-library runs). Idempotent — reused
        /// across repeated draftings. Visibility is handled in <see cref="LateUpdate"/>.</summary>
        private void EquipBuilderGear()
        {
            if (_library == null)
            {
                return;
            }

            if (_hat == null && _library.HardHat != null)
            {
                _hat = new GameObject("BuilderHat");
                _hat.transform.SetParent(transform, false);
                _hatRenderer = _hat.AddComponent<SpriteRenderer>();
                _hatRenderer.sprite = _library.HardHat;
            }

            if (_mallet == null && _library.ToolHammer != null)
            {
                _mallet = new GameObject("BuilderMallet");
                _mallet.transform.SetParent(transform, false);
                _malletRenderer = _mallet.AddComponent<SpriteRenderer>();
                _malletRenderer.sprite = _library.ToolHammer;
            }
        }

        /// <summary>Track the builder gear onto the body AFTER movement/tweens have run this
        /// frame (bounds-following picks up the walk-cycle bounce and work bob for free), and
        /// derive its visibility from the current mode — the single source of truth that keeps
        /// the hat/mallet off any non-builder. Early-outs entirely for a dino that has never
        /// been drafted (no gear objects), so ordinary dinos pay nothing.</summary>
        private void LateUpdate()
        {
            if (_hat == null && _mallet == null)
            {
                return;
            }

            UpdateHat();
            UpdateMallet();
        }

        // The hat is worn from dispatch (commute) through the whole shift; pinned to the
        // top-center of the body bounds, inset down a little onto the head, one sort step
        // above the dino, at a jaunty tilt.
        private void UpdateHat()
        {
            if (_hat == null)
            {
                return;
            }

            bool want = (_headingToWork || _mode == Mode.Work) &&
                        _hatRenderer != null && _hatRenderer.sprite != null;
            if (_hat.activeSelf != want)
            {
                _hat.SetActive(want);
            }

            if (!want || _renderer == null)
            {
                return;
            }

            Bounds b = _renderer.bounds;
            _hat.transform.position = new Vector3(
                b.center.x, b.max.y - b.size.y * HatInsetFrac, transform.position.z);
            _hat.transform.rotation = Quaternion.Euler(0f, 0f, HatTilt);
            _hatRenderer.sortingOrder = _renderer.sortingOrder + 1;
        }

        // The mallet is held only while actually working; parked at the side of the body
        // toward the structure (flipped by which side that is) and rocked by TickWork.
        private void UpdateMallet()
        {
            if (_mallet == null)
            {
                return;
            }

            bool want = _mode == Mode.Work && _malletRenderer != null && _malletRenderer.sprite != null;
            if (_mallet.activeSelf != want)
            {
                _mallet.SetActive(want);
            }

            if (!want || _renderer == null)
            {
                return;
            }

            Bounds b = _renderer.bounds;
            float side = _buildingX >= transform.position.x ? 1f : -1f;
            _mallet.transform.position = new Vector3(
                b.center.x + side * b.size.x * MalletSideFrac,
                b.min.y + b.size.y * MalletHeightFrac,
                transform.position.z);
            // Flip toward the site (parent scale is uniform-positive, so a -1 x mirrors it);
            // rotation is left to the TickWork swing so we don't stomp it here.
            _mallet.transform.localScale = new Vector3(side, 1f, 1f);
            _malletRenderer.sortingOrder = _renderer.sortingOrder + 1;
        }

        private void EnterWork()
        {
            CancelNap();
            _onReachedFood = null;
            _onTravelArrived = null;
            _headingToWork = false; // arrived: Mode.Work now represents the assignment
            _mode = Mode.Work;
            _workBobTimer = UnityEngine.Random.Range(0.2f, 0.6f);
            ResetWalkAnim();
        }

        /// <summary>Working: hold position (writes NO position — a builder never drifts
        /// home or toward anything), bobbing every so often to sell the hammering loop.</summary>
        private void TickWork()
        {
            _workBobTimer -= Time.deltaTime;
            if (_workBobTimer <= 0f)
            {
                _workBobTimer = UnityEngine.Random.Range(0.55f, 0.95f);
                Tween.PunchScale(transform, 0.14f, 0.4f);

                // Rock the mallet in time with the work bob (rotation only — LateUpdate
                // owns its position/scale, so the two never fight). No-op without a mallet.
                if (_mallet != null && _mallet.activeSelf)
                {
                    Tween.ShakeRotation(_mallet.transform, 18f, 0.4f, 1);
                }
            }
        }

        /// <summary>Clock out of a build assignment. Recalls a builder that has ARRIVED and
        /// is working (<see cref="Mode.Work"/>) AND one that is still COMMUTING to the site
        /// (<see cref="_headingToWork"/>) — the latter is critical: if a commute is left
        /// running when the build finishes, its pending arrival callback clocks the dino in
        /// at an abandoned plot and it bobs there forever, never coming home.
        /// <paramref name="celebrate"/> plays the tap dance first (only meaningful for a
        /// builder actually on-site) which then resumes the resident role and trots home;
        /// otherwise the dino heads straight home. A no-op if not on a build assignment.</summary>
        public void StopWork(bool celebrate)
        {
            bool working = _mode == Mode.Work;
            if (!working && !_headingToWork)
            {
                return;
            }

            _headingToWork = false; // recalled: cancel any in-flight commute intent

            if (celebrate && working)
            {
                Dance(); // Dance -> ResumeRole -> walk back to the meadow
            }
            else
            {
                // A still-commuting builder can't dance on a site it never reached; send it
                // straight home (ResumeRole overrides the pending commute + arrival callback).
                ResumeRole();
            }
        }

        // ----------------------------------------------------------------- carry

        /// <summary>Perch a carried pickup above this dino's head (Trike courier).</summary>
        public void AttachCarried(Transform pickup)
        {
            _carried = pickup;
            if (pickup != null)
            {
                pickup.SetParent(transform, true);
                pickup.localPosition = new Vector3(0f, 0.75f, 0f);
                pickup.localScale = Vector3.one * 0.8f; // counter the dino's growth scale a bit
            }
        }

        /// <summary>Release the carried pickup (repositioning is the caller's job).</summary>
        public Transform DetachCarried()
        {
            Transform t = _carried;
            _carried = null;
            if (t != null)
            {
                t.SetParent(null, true);
            }

            return t;
        }

        // ----------------------------------------------------------------- moving

        /// <summary>One movement step toward <paramref name="target"/>, clamped to
        /// the remaining distance so it can NEVER overshoot and oscillate. Returns
        /// true on arrival (position snapped exactly onto the target).</summary>
        private bool MoveTowards(Vector3 target, float speed, float arriveEps = ArriveEps)
        {
            Vector3 pos = transform.position;
            Vector3 delta = target - pos;
            delta.z = 0f;
            float d = delta.magnitude;
            if (d <= arriveEps)
            {
                if (d > 0.0001f)
                {
                    // Snap the final sliver so the next frame has nothing left to chase.
                    transform.position = new Vector3(target.x, target.y, pos.z);
                }

                return true;
            }

            Vector3 move = (delta / d) * Mathf.Min(speed * Time.deltaTime, d);
            Vector3 next = pos + move;
            next.z = pos.z;
            transform.position = next;

            // Feed the ACTUAL step (not the raw target vector) through the shared
            // smoother so micro-steps near a goal never flip the facing.
            _facing = _facingSmoother.Tick(new Vector2(move.x, move.y), Time.deltaTime);
            TickWalkAnim(speed);
            ApplySprite();
            return false;
        }

        // ------------------------------------------------------------- walk cycle

        /// <summary>Advance the walk cycle (idle -> strideA -> idle -> strideB at
        /// ~<see cref="WalkAnimFps"/> frames/sec, scaled by how fast the dino is
        /// actually moving relative to its base follow speed). Runs only from a
        /// movement step; a no-stride-art dino exits immediately, so every other
        /// dino/stage keeps the pre-pilot static behavior bit for bit.</summary>
        private void TickWalkAnim(float speed)
        {
            if (_activeSprites == null || _spriteStage != Stage)
            {
                RefreshStageSprites();
            }

            if (_strideA == null && _strideB == null)
            {
                return; // no stride art for this dino/stage: stay on the facing frame
            }

            float baseSpeed = _config != null ? _config.DinoFollowSpeed : 3f;
            float mul = baseSpeed > 0.01f ? Mathf.Clamp(speed / baseSpeed, 0.5f, 2f) : 1f;
            _walkClock = Mathf.Repeat(_walkClock + Time.deltaTime * WalkAnimFps * mul, 4f);
            _walkFrame = (int)_walkClock;
        }

        /// <summary>Snap the walk cycle back to the idle frame (called on settle).</summary>
        private void ResetWalkAnim()
        {
            _walkClock = 0f;
            if (_walkFrame != 0)
            {
                _walkFrame = 0;
                ApplySprite();
            }
        }

        // ----- Tap reaction -----

        public void OnTapped(Vector2 worldPoint)
        {
            Dance();
            // Buddy promotion (tap-to-swap) is coordinated by the GameManager.
            GameManager.Instance?.NotifyDinoTapped(this);
        }

        public void Dance()
        {
            if (_busyDancing)
            {
                return;
            }

            _busyDancing = true;
            CancelNap();
            _mode = Mode.Dance;
            EmitHearts();
            GameEvents.RaiseDinoTapped();
            GameManager.Instance?.Audio?.Roar();

            DanceType dt = _def != null ? _def.Dance : DanceType.StompRoar;
            switch (dt)
            {
                case DanceType.HeadShake:
                    Tween.ShakeRotation(transform, 18f, 0.7f, 4);
                    break;
                case DanceType.NeckSway:
                    Tween.ShakeRotation(transform, 10f, 0.9f, 2);
                    break;
                case DanceType.TailWag:
                    Tween.ShakeRotation(transform, 22f, 0.6f, 5);
                    break;
                default: // StompRoar
                    Tween.PunchScale(transform, 0.35f, 0.6f);
                    Tween.ShakeRotation(transform, 8f, 0.6f, 2);
                    break;
            }

            Tween.After(0.8f, () =>
            {
                _busyDancing = false;
                if (this != null && _mode == Mode.Dance)
                {
                    ResumeRole(); // something else (eat/parade) may have taken over meanwhile
                }
            });
        }

        private void EmitHearts()
        {
            if (_hearts != null)
            {
                _hearts.Emit(6);
            }

            GameManager.Instance?.Audio?.Heart();
        }

        /// <summary>Directional star puff aimed the given way (Stego sniffer trail).</summary>
        public void EmitDirectedSparkles(Vector3 dir)
        {
            if (_poof == null)
            {
                return;
            }

            dir.z = 0f;
            if (dir.sqrMagnitude < 0.0001f)
            {
                dir = Vector3.right;
            }

            dir.Normalize();
            for (int i = 0; i < 4; i++)
            {
                var ep = new ParticleSystem.EmitParams
                {
                    position = transform.position + new Vector3(0f, 0.35f, 0f) + dir * (0.3f + i * 0.28f),
                    velocity = dir * (1.6f + i * 0.3f) + new Vector3(0f, 0.9f - i * 0.2f, 0f),
                    startLifetime = 0.8f,
                    startSize = 0.26f
                };
                _poof.Emit(ep, 1);
            }
        }

        /// <summary>Point <see cref="_activeSprites"/> (and the stride sets) at the
        /// current stage's art.</summary>
        private void RefreshStageSprites()
        {
            if (_def == null)
            {
                return;
            }

            _activeSprites = _def.StageSprites(Stage);
            _strideA = _def.StrideSprites(Stage, 0);
            _strideB = _def.StrideSprites(Stage, 1);
            _spriteStage = Stage;
        }

        private void ApplySprite()
        {
            if (_renderer == null || _def == null)
            {
                return;
            }

            if (_activeSprites == null || _spriteStage != Stage)
            {
                RefreshStageSprites(); // swap the whole set on a growth-stage change
            }

            Sprite s = Direction8.Pick(_activeSprites, _facing, _def.GetIdle());

            // Mid-stride walk frames (1 = A, 3 = B); the same frame slot is used in
            // whatever direction the dino currently faces, so a facing change stays
            // in phase. Missing stride art falls back to the idle facing frame.
            if (_walkFrame == 1 || _walkFrame == 3)
            {
                Sprite stride = Direction8.Pick(_walkFrame == 1 ? _strideA : _strideB,
                    _facing, null);
                if (stride != null)
                {
                    s = stride;
                }
            }

            if (s != null)
            {
                _renderer.sprite = s;
            }
        }
    }
}
