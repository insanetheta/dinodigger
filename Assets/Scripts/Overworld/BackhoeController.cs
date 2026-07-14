using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;

namespace DinoDigger.Overworld
{
    /// <summary>
    /// Tap-to-move backhoe. Straight-line steering with a simple wall-slide when
    /// the direct step hits water/obstacle. Resolves 8-directional facing from the
    /// movement vector. Fires dig entry when it reaches a targeted mound.
    /// </summary>
    public class BackhoeController : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _renderer;
        [SerializeField] private OverworldMap _map;
        [SerializeField] private GameConfig _config;
        [SerializeField] private Sprite[] _dirSprites = new Sprite[8];

        // Wheel-roll drive frames (DinoDigger-682). Both empty/null -> the cycler is
        // inert and the backhoe behaves exactly as before (static facing frame).
        [SerializeField] private Sprite[] _rollA = new Sprite[8];
        [SerializeField] private Sprite[] _rollB = new Sprite[8];
        private float _rollClock;   // wraps 0..4 across the cycle
        private int _rollFrame;     // 0 idle, 1 A, 2 idle, 3 B
        private const float RollFps = 7f;

        private Vector3 _target;
        private bool _moving;
        private DigMound _pendingMound;
        private Dir8 _facing = Dir8.S;
        private FacingSmoother _facingSmoother;
        private Vector3 _baseScale;

        // BFS waypoint route (world-space cell centers, goal last).
        private readonly System.Collections.Generic.List<Vector3> _waypoints =
            new System.Collections.Generic.List<Vector3>();
        private int _waypointIndex;

        // Progress watchdog: safety net in case a route is invalidated mid-drive.
        // Track the best remaining distance; if it hasn't improved for a while, REPLAN
        // (re-run the BFS from where we actually are) rather than give up — a stall at a
        // narrow bridge/concave bank almost always still has a route, it just needs a
        // fresh string-pull from the current spot. Only a BFS that itself fails is a
        // genuine "can't get there".
        private float _bestDist;
        private float _stallTimer;
        private const float StallProgressEps = 0.05f;
        private const float StallGiveUpSeconds = 1.2f;
        private const float MoundDigRange = 1.5f;

        // Replan budget per journey: enough to thread several awkward passages, capped
        // so a genuinely impossible target can't loop forever before it honks.
        private const int MaxReplans = 5;
        private int _replans;

        // TEST HOOK: number of times a drive ended in a honk-give-up because the target
        // was unreachable (initial BFS failure, or replans exhausted). A robust route to
        // any reachable tap must keep this at zero.
        private int _giveUps;
        internal int TestGiveUpCount => _giveUps;

        public bool IsMoving => _moving;
        public Dir8 Facing => _facing;

        // TEST HOOK: current facing sprite, for the integration runner's facing check.
        internal Sprite TestSprite => _renderer != null ? _renderer.sprite : null;

        // TEST HOOK: the wired sprite for a given facing, so a case can assert the
        // rendered sprite equals the expected index of the directional array.
        internal Sprite TestDirSprite(Dir8 dir) => Direction8.Pick(_dirSprites, dir, null);

        // TEST HOOKS for the roll drive cycle.
        internal Sprite TestRollDirSprite(int phase, Dir8 dir) =>
            Direction8.Pick(phase == 0 ? _rollA : _rollB, dir, null);
        internal bool TestHasRoll => HasAny(_rollA) || HasAny(_rollB);
        internal int TestRollFrame => _rollFrame;

        private void Awake()
        {
            if (_renderer == null)
            {
                _renderer = GetComponent<SpriteRenderer>();
            }

            _facingSmoother.Reset(_facing);
            _target = transform.position;
            _baseScale = transform.localScale;
            ApplySprite();
        }

        public void Configure(OverworldMap map, GameConfig config, Sprite[] dirSprites,
            Sprite[] rollA = null, Sprite[] rollB = null)
        {
            _map = map;
            _config = config;
            if (dirSprites != null && dirSprites.Length > 0)
            {
                _dirSprites = dirSprites;
            }

            if (rollA != null && rollA.Length > 0)
            {
                _rollA = rollA;
            }

            if (rollB != null && rollB.Length > 0)
            {
                _rollB = rollB;
            }

            ApplySprite();
        }

        /// <summary>Drive to a walkable world point (clamped to nearest walkable).</summary>
        public void MoveTo(Vector3 world)
        {
            _pendingMound = null;
            SetDestination(world);
        }

        /// <summary>Drive to a mound; enter dig mode on arrival.</summary>
        public void DriveToMound(DigMound mound)
        {
            if (mound == null)
            {
                return;
            }

            _pendingMound = mound;
            SetDestination(mound.transform.position);
        }

        private void SetDestination(Vector3 world)
        {
            _waypoints.Clear();
            _waypointIndex = 0;

            if (_map != null)
            {
                // BFS route around the pond/trees; straight-line steering wedges on
                // concave obstacle edges. Unreachable target -> honk, don't move.
                if (!_map.FindPath(transform.position, world, _waypoints))
                {
                    _moving = false;
                    _pendingMound = null;
                    _giveUps++;
                    Honk();
                    return;
                }

                _target = _waypoints[_waypoints.Count - 1];
            }
            else
            {
                _target = world;
                _waypoints.Add(world);
            }

            _target.z = transform.position.z;
            _moving = true;
            _bestDist = float.MaxValue;
            _stallTimer = 0f;
            _replans = 0;
            Tween.PunchScale(transform, 0.12f, 0.2f);
            GameEvents.RaiseBackhoeMoved();
        }

        private void Update()
        {
            if (!_moving || _config == null)
            {
                return;
            }

            Vector3 pos = transform.position;

            // Advance the wheel-roll cycle while driving (inert without roll art).
            TickRoll();

            // Corner-cutting: as we move, skip ahead to the farthest waypoint we can
            // reach by a straight walkable line. Combined with the LOS smoothing in
            // FindPath this makes the backhoe drive long smooth diagonals toward the
            // click and cut corners naturally, instead of following the grid staircase.
            if (AdvanceByLineOfSight(pos))
            {
                _bestDist = float.MaxValue; // route step changed — don't misfire the stall watchdog
                _stallTimer = 0f;
            }

            // Advance along the waypoints; the final waypoint is the target.
            Vector3 step_goal = _waypointIndex < _waypoints.Count ? _waypoints[_waypointIndex] : _target;
            step_goal.z = pos.z;
            Vector3 toWaypoint = step_goal - pos;
            toWaypoint.z = 0f;
            // Normally we accept a waypoint from ~2x the arrive distance so long diagonals
            // stay smooth. Through a narrow corridor (1-cell bridge / slot) tighten that to
            // ~1x so the backhoe threads the cell center instead of clipping the corner and
            // wedging on the far bank.
            float advanceMul = NextWaypointsInCorridor() ? 1f : 2f;
            float advanceDist = _config.BackhoeArriveDistance * advanceMul;
            if (_waypointIndex < _waypoints.Count - 1 &&
                toWaypoint.sqrMagnitude <= advanceDist * advanceDist)
            {
                _waypointIndex++;
                _bestDist = float.MaxValue; // new segment — don't let the stall watchdog misfire
                _stallTimer = 0f;
                return;
            }

            Vector3 toTarget = step_goal - pos;
            toTarget.z = 0f;
            float dist = toTarget.magnitude;
            float remaining = (_target - pos).magnitude;

            if (remaining <= _config.BackhoeArriveDistance ||
                (_waypointIndex >= _waypoints.Count - 1 && dist <= _config.BackhoeArriveDistance))
            {
                Arrive();
                return;
            }

            if (dist < _bestDist - StallProgressEps)
            {
                _bestDist = dist;
                _stallTimer = 0f;
            }
            else
            {
                _stallTimer += Time.deltaTime;
                if (_stallTimer >= StallGiveUpSeconds)
                {
                    // Stalled against geometry: try a fresh route from here before quitting.
                    if (!TryReplan())
                    {
                        GiveUp();
                    }

                    return;
                }
            }

            float step = _config.BackhoeSpeed * Time.deltaTime;
            Vector3 dir = toTarget / Mathf.Max(dist, 0.0001f);
            Vector3 desired = pos + dir * Mathf.Min(step, dist);

            Vector3 moved = ResolveStep(pos, desired, dir, step);
            Vector3 delta = moved - pos;
            transform.position = moved;

            if (delta.sqrMagnitude > 0.0000001f)
            {
                // Smoothed + hysteresis facing: kills the per-frame sprite jiggle.
                _facing = _facingSmoother.Tick(new Vector2(delta.x, delta.y), Time.deltaTime);
                ApplySprite();
            }
            else
            {
                // Fully wedged this frame (direct step + both axis slides blocked).
                // Re-plan from here instead of quitting; only honk if no route remains.
                if (!TryReplan())
                {
                    GiveUp();
                }
            }
        }

        /// <summary>Re-run the BFS from the current position toward the same goal and
        /// adopt the fresh route. Returns false when no route exists (truly unreachable)
        /// or the per-journey replan budget is spent — the caller then honks.</summary>
        private bool TryReplan()
        {
            if (_map == null || _replans >= MaxReplans)
            {
                return false;
            }

            _replans++;
            Vector3 goal = _target; // FindPath clears _waypoints, so read the goal first
            if (!_map.FindPath(transform.position, goal, _waypoints))
            {
                return false;
            }

            _waypointIndex = 0;
            _target = _waypoints[_waypoints.Count - 1];
            _target.z = transform.position.z;
            _bestDist = float.MaxValue;
            _stallTimer = 0f;
            return true;
        }

        /// <summary>True when the current or next waypoint sits in a pinched corridor,
        /// so the follower should tighten its arrival threshold there.</summary>
        private bool NextWaypointsInCorridor()
        {
            if (_map == null)
            {
                return false;
            }

            if (_waypointIndex < _waypoints.Count &&
                _map.IsCorridorCell(_map.WorldToCell(_waypoints[_waypointIndex])))
            {
                return true;
            }

            if (_waypointIndex + 1 < _waypoints.Count &&
                _map.IsCorridorCell(_map.WorldToCell(_waypoints[_waypointIndex + 1])))
            {
                return true;
            }

            return false;
        }

        /// <summary>Skip the waypoint cursor forward to the farthest later waypoint
        /// still reachable by a straight walkable line from <paramref name="pos"/>.
        /// Returns true when the cursor advanced.</summary>
        private bool AdvanceByLineOfSight(Vector3 pos)
        {
            if (_map == null || _waypoints.Count == 0)
            {
                return false;
            }

            for (int j = _waypoints.Count - 1; j > _waypointIndex; j--)
            {
                // Corridor-width LOS: the shortcut must clear a body's worth of margin,
                // so it can't cut a corner into water. Through 1-cell bridges this fails
                // and we keep following the raw cell centers (hugging the bridge middle).
                if (_map.HasCorridorLineOfSight(pos, _waypoints[j]))
                {
                    _waypointIndex = j;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Stop without reaching the target. Only enters dig mode if the
        /// pending mound is genuinely close — a blocked drive must never
        /// teleport-dig a mound from across the island.</summary>
        private void GiveUp()
        {
            _moving = false;
            ResetRoll(); // settled: back to the static facing frame, never mid-roll
            if (_pendingMound != null && _pendingMound.IsActive &&
                (_pendingMound.transform.position - transform.position).sqrMagnitude
                    <= MoundDigRange * MoundDigRange)
            {
                DigMound m = _pendingMound;
                _pendingMound = null;
                GameManager.Instance?.EnterDig(m);
                return;
            }

            _pendingMound = null;
            _giveUps++;
            Honk(); // friendly "can't get there" wiggle
        }

        /// <summary>Try direct move, else slide along one axis (basic detour).</summary>
        private Vector3 ResolveStep(Vector3 pos, Vector3 desired, Vector3 dir, float step)
        {
            if (_map == null || _map.IsWalkableWorld(desired))
            {
                return desired;
            }

            Vector3 slideX = pos + new Vector3(dir.x, 0f, 0f) * step;
            if (Mathf.Abs(dir.x) > 0.01f && _map.IsWalkableWorld(slideX))
            {
                return slideX;
            }

            Vector3 slideY = pos + new Vector3(0f, dir.y, 0f) * step;
            if (Mathf.Abs(dir.y) > 0.01f && _map.IsWalkableWorld(slideY))
            {
                return slideY;
            }

            return pos; // blocked
        }

        private void Arrive()
        {
            _moving = false;
            ResetRoll(); // settled: back to the static facing frame, never mid-roll
            if (_pendingMound != null && _pendingMound.IsActive)
            {
                DigMound m = _pendingMound;
                _pendingMound = null;
                GameManager.Instance?.EnterDig(m);
            }
        }

        private void ApplySprite()
        {
            if (_renderer == null)
            {
                return;
            }

            Sprite s = Direction8.Pick(_dirSprites, _facing, _renderer.sprite);

            // Mid-roll drive frames (1 = A, 3 = B) in the current facing; keeps phase
            // across a facing change. Missing roll art falls back to the facing frame,
            // so a backhoe without roll art never changes behavior.
            if (_rollFrame == 1 || _rollFrame == 3)
            {
                Sprite roll = Direction8.Pick(_rollFrame == 1 ? _rollA : _rollB, _facing, null);
                if (roll != null)
                {
                    s = roll;
                }
            }

            if (s != null)
            {
                _renderer.sprite = s;
            }
        }

        /// <summary>Advance the drive cycle (idle -> rollA -> idle -> rollB at
        /// ~<see cref="RollFps"/> fps). A backhoe with no roll art exits immediately,
        /// keeping the pre-682 static behavior bit for bit.</summary>
        private void TickRoll()
        {
            if (!HasAny(_rollA) && !HasAny(_rollB))
            {
                return; // no roll art: stay on the facing frame
            }

            _rollClock = Mathf.Repeat(_rollClock + Time.deltaTime * RollFps, 4f);
            _rollFrame = (int)_rollClock;
        }

        /// <summary>Snap the drive cycle back to the idle frame (called on stop).</summary>
        private void ResetRoll()
        {
            _rollClock = 0f;
            if (_rollFrame != 0)
            {
                _rollFrame = 0;
                ApplySprite();
            }
        }

        private static bool HasAny(Sprite[] set)
        {
            if (set == null)
            {
                return false;
            }

            for (int i = 0; i < set.Length; i++)
            {
                if (set[i] != null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Idle-attract honk wiggle.</summary>
        public void Honk()
        {
            Tween.ShakeRotation(transform, 8f, 0.5f, 2);
            Tween.PunchScale(transform, 0.2f, 0.4f);
        }
    }
}
