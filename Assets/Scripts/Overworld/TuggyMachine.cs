using System.Collections.Generic;
using UnityEngine;
using DinoDigger.Core;

namespace DinoDigger.Overworld
{
    /// <summary>
    /// TUGGY the tugboat (DinoDigger-xt3), REDESIGNED per Greg's ruling.
    ///
    /// The eval cut the bible's Tuggy for good reasons: a ferry solves a problem that does
    /// not exist (bridges shipped, nothing on this island is ever stuck), and — worse — a
    /// boat that carries ducks around AMBIGUATES the duck-catch, which is the stream's one
    /// scarce joy. Greg overrode the cut with a brief that inverts the harm:
    ///
    ///   Tuggy does not carry ducks. He MAKES them.
    ///
    /// He chugs a slow forever-loop along the island's longest stream — mechanically a
    /// bigger, slower duck (the same cell-to-cell course drift, the same under-bridge fade),
    /// which is why this class deliberately mirrors <see cref="Duck"/> rather than inventing
    /// a second way to follow water. Tap him and he TOOTS: two or three ducklings drop onto
    /// lily pads behind him and drift off down the same course. Every one of them is a real
    /// <see cref="Duck"/> — catchable with the same tap, paying the same reward — and they do
    /// NOT count against the ambient two-duck cap, so a toot leaves the stream with strictly
    /// MORE to catch than before. Amplifier, not competitor.
    ///
    /// NO DINO BOARDING, deliberately and permanently. That is what kept every new state out
    /// of <see cref="DinoController"/> — the most watchdog-laden system in the codebase — and
    /// left Tuggy a stream-bound spectacle-plus-spawner, exactly like DuckController.
    ///
    /// SHARING A ONE-CELL STREAM. Nothing collides: every water actor is a trigger collider
    /// and the project has no Rigidbody2D at all, so a boat and three ducklings simply
    /// overlap the way two ambient ducks already do today. The only real question an overlap
    /// raises is which one a TAP means, and GameManager.TappableRank settles it the same way
    /// every frame: a duck outranks a machine, so the fleeting catchable thing always wins
    /// the tap and the big steady boat underneath never steals it.
    /// </summary>
    public class TuggyMachine : MachineFriend
    {
        private const float ArriveEps = 0.05f;
        private const float BobAmp = 0.045f;
        private const float BobRate = 2.1f;   // slower, heavier bob than a duck's 3.2
        private const float FadeRate = 3f;    // under-bridge fade, matching Duck

        public override MachineKind Kind => MachineKind.Tuggy;

        private StreamNetwork _streams;
        private OverworldMap _map;
        private DuckController _ducks;
        private Sprite _lilyPad;
        private int _courseIndex;

        private readonly List<Vector3> _route = new List<Vector3>();
        private readonly List<bool> _underBridge = new List<bool>();
        private int _index;
        private int _step = 1;      // +1 downstream, -1 back up: Tuggy LOOPS, he never despawns
        private Vector3 _basePos;
        private float _bobPhase;
        private float _fade = 1f;

        private float _towRemaining;
        private int _toots;
        private int _ducklingsTowed;

        protected override float RechargeSeconds =>
            Config != null ? Mathf.Max(0.1f, Config.TuggyCooldownSeconds) : 40f;

        /// <summary>Moor Tuggy on a stream course. <paramref name="courseIndex"/> is chosen by
        /// <see cref="MachineFriendController"/> (the LONGEST course, so he has the most water
        /// to chug and the most room for a tow line). Null-tolerant throughout: with no stream
        /// network he simply sits still and his toot spawns nothing, which is what a scene with
        /// no water should do.</summary>
        public void SetStream(StreamNetwork streams, OverworldMap map, DuckController ducks,
            int courseIndex, Sprite lilyPad)
        {
            _streams = streams;
            _map = map;
            _ducks = ducks;
            _courseIndex = courseIndex;
            _lilyPad = lilyPad;

            BuildRoute();
        }

        private void BuildRoute()
        {
            _route.Clear();
            _underBridge.Clear();

            IReadOnlyList<Vector3Int> cells = _streams != null ? _streams.CourseCells(_courseIndex) : null;
            if (cells == null || cells.Count == 0)
            {
                return;
            }

            for (int i = 0; i < cells.Count; i++)
            {
                _route.Add(_streams.CellCenter(cells[i]));
                _underBridge.Add(_map != null && _map.IsWalkableCell(cells[i]));
            }

            // Moored at the head of the course, pointed downstream and ready to cast off the
            // moment the child wakes him.
            _basePos = _route[0];
            transform.position = _basePos;
            _index = _route.Count > 1 ? 1 : 0;
            _step = 1;
            _bobPhase = Random.value * Mathf.PI * 2f;
        }

        protected override void OnWoke()
        {
            FillTank();       // wound up and ready: the wake tap can be followed by a first toot
            GameManager.Instance?.Audio?.Honk();
        }

        protected override void Activate(Vector2 worldPoint)
        {
            _toots++;

            // The toot itself: a honk, a bob, a puff off the smokestack.
            GameManager.Instance?.Audio?.Honk();
            Jiggle(0.18f, 0.35f);

            GameManager gm = GameManager.Instance;
            Sprite star = gm != null && gm.TestLibrary != null ? gm.TestLibrary.StarParticle : null;
            gm?.MachineSpawnFx(transform.position + new Vector3(0f, 0.85f, 0f), star,
                new Color(1f, 0.95f, 0.9f), 0.3f, 9);

            // The tow line: 2-3 real ducks dropped onto the water BEHIND him (one cell back
            // per duckling), each riding a lily pad. They drift the course on their own from
            // there — Tuggy tows nothing mechanically, which is exactly why a duckling is
            // never ambiguous to tap.
            int min = Config != null ? Mathf.Max(1, Config.TuggyDucklingsMin) : 2;
            int max = Config != null ? Mathf.Max(min, Config.TuggyDucklingsMax) : 3;
            int count = Random.Range(min, max + 1);

            for (int i = 0; i < count; i++)
            {
                if (SpawnDuckling(i + 1) != null)
                {
                    _ducklingsTowed++;
                }
            }

            _towRemaining = Config != null ? Mathf.Max(0.5f, Config.TuggyTowSeconds) : 30f;
        }

        private Duck SpawnDuckling(int trailPosition)
        {
            if (_ducks == null || _route.Count == 0)
            {
                return null;
            }

            // ONE CELL APART, strung out along the course from Tuggy's own cell. Spacing them
            // is the whole point: ducks drift downstream from wherever they enter the water at
            // a fixed speed, so a line dropped on ONE cell would stay perfectly stacked forever
            // and read as a single duck. Stepping the entry cell turns them into a visible
            // parade of lily pads that streams away from the boat (ducks drift at 0.5 u/s and
            // Tuggy chugs at 0.32, so they pull ahead on their own).
            //
            // Clamped into range, so a toot near the end of the stream still puts every
            // duckling on the water rather than dropping the ones that fell off the course.
            int cell = Mathf.Clamp(_index + (trailPosition - 1), 0, _route.Count - 1);
            return _ducks.SpawnEscortDuck(_courseIndex, cell, _lilyPad);
        }

        protected override void TickAwake(float dt)
        {
            if (_towRemaining > 0f)
            {
                _towRemaining -= dt;
            }

            TickChug(dt);
        }

        /// <summary>The forever-loop. Same cell-to-cell walk a drifting <see cref="Duck"/> uses,
        /// with two differences that make him a BOAT rather than a duck: he is slower, and at
        /// the end of the course he turns around instead of despawning.</summary>
        private void TickChug(float dt)
        {
            if (_route.Count < 2)
            {
                return;
            }

            float speed = Config != null ? Mathf.Max(0.02f, Config.TuggyChugSpeed) : 0.32f;

            Vector3 target = _route[_index];
            Vector3 delta = target - _basePos;
            delta.z = 0f;
            float d = delta.magnitude;

            if (d <= ArriveEps)
            {
                _basePos = new Vector3(target.x, target.y, _basePos.z);
                _index += _step;

                // Reached an end of the course: about-turn and chug back. A loop, so the boat
                // is a permanent, findable feature of the stream rather than a passing event.
                if (_index >= _route.Count || _index < 0)
                {
                    _step = -_step;
                    _index = Mathf.Clamp(_index + _step * 2, 0, _route.Count - 1);
                }
            }
            else
            {
                Vector3 stepVec = delta / d * Mathf.Min(speed * dt, d);
                _basePos += stepVec;
                if (Mathf.Abs(stepVec.x) > 0.0001f && BodyRenderer != null)
                {
                    // Art faces EAST; mirror for a westward chug (no second sprite needed).
                    BodyRenderer.flipX = stepVec.x < 0f;
                }
            }

            _bobPhase += dt * BobRate;
            transform.position = _basePos + new Vector3(0f, Mathf.Sin(_bobPhase) * BobAmp, 0f);

            // Slip UNDER bridge decks exactly like a duck: the deck draws on the ground layer
            // below every sprite, so a boat drawn on top of one would look wrong. Routed
            // through BodyAlpha (not a direct colour write) so the state-derived visual pass
            // folds it in instead of overwriting it on the same frame.
            bool crossing = IsUnderBridge(_index) || IsUnderBridge(_index - _step);
            _fade = Mathf.MoveTowards(_fade, crossing ? 0f : 1f, dt * FadeRate);
            BodyAlpha = _fade;
        }

        private bool IsUnderBridge(int i) =>
            i >= 0 && i < _underBridge.Count && _underBridge[i];

        // ----------------------------------------------------------- TEST HOOKS

        /// <summary>TEST HOOK. Toots since the last reset.</summary>
        internal int TestToots => _toots;

        /// <summary>TEST HOOK. Ducklings actually put on the water since the last reset.</summary>
        internal int TestDucklingsTowed => _ducklingsTowed;

        /// <summary>TEST HOOK. True while a tow line is still "out" (pose only — the ducklings
        /// themselves keep drifting and stay catchable until they reach the mouth).</summary>
        internal bool TestTowing => _towRemaining > 0f;

        /// <summary>TEST HOOK. Which stream course Tuggy chugs.</summary>
        internal int TestCourseIndex => _courseIndex;

        /// <summary>TEST HOOK. Number of cells on that course (0 = no water wired).</summary>
        internal int TestRouteLength => _route.Count;

        internal override void TestResetMachine()
        {
            _toots = 0;
            _ducklingsTowed = 0;
            _towRemaining = 0f;
            _fade = 1f;
            BodyAlpha = 1f;
            if (BodyRenderer != null)
            {
                BodyRenderer.flipX = false;
            }

            BuildRoute();
            base.TestResetMachine();
        }
    }
}
