using System.Collections.Generic;
using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;

namespace DinoDigger.Overworld
{
    /// <summary>
    /// Ambient pond life. On a relaxed timer (every 20-40s, at most two alive) this
    /// spawner hatches a <see cref="Duck"/> at a random stream head; the duck drifts
    /// slowly along that stream course and despawns at the mouth. Tapping a duck
    /// catches it (see <see cref="Duck.OnTapped"/>): it quacks, flaps away offscreen,
    /// and leaves a fruit-or-treasure reward behind.
    ///
    /// Built + wired by SceneBuilder; reads its routes from <see cref="StreamNetwork"/>.
    /// Fully null-tolerant: no streams / no art -> it simply never spawns.
    /// </summary>
    public class DuckController : MonoBehaviour
    {
        [SerializeField] private StreamNetwork _streams;
        [SerializeField] private OverworldMap _map; // to spot bridge ('B') cells on a course
        [SerializeField] private Sprite _sideSprite; // side view, faces EAST (mirror for W)
        [SerializeField] private Sprite _flySprite;  // airborne flap frame (catch pose)
        [SerializeField] private Transform _overworldRoot;

        private const float SpawnMin = 20f;
        private const float SpawnMax = 40f;
        private const int MaxAlive = 2;

        // Above the water tile (order 1) but below the backhoe (12) and dinos (15).
        private const int DuckSorting = 10;

        private float _timer;
        private readonly List<Duck> _alive = new List<Duck>();

        /// <summary>The side-view duck sprite, borrowed by the Duck! dig surprise so it can
        /// fly the same duck art without its own wiring. Null when no duck art is present.</summary>
        internal Sprite SurpriseSprite => _sideSprite;

        public void Configure(StreamNetwork streams, OverworldMap map, Sprite side, Sprite fly,
            Transform overworldRoot)
        {
            _streams = streams;
            _map = map;
            _sideSprite = side;
            _flySprite = fly;
            _overworldRoot = overworldRoot;
        }

        private void Start()
        {
            _timer = Random.Range(SpawnMin, SpawnMax);
        }

        private void Update()
        {
            PruneDead();

            if (_streams == null || _streams.Count == 0)
            {
                return;
            }

            // Only bring ducks out on the calm overworld, never over the dig site.
            GameManager gm = GameManager.Instance;
            if (gm != null && gm.State != null && !gm.State.Is(GameState.Roam))
            {
                return;
            }

            _timer -= Time.deltaTime;
            if (_timer > 0f)
            {
                return;
            }

            _timer = Random.Range(SpawnMin, SpawnMax);
            if (_alive.Count >= MaxAlive)
            {
                return;
            }

            SpawnDuck(Random.Range(0, _streams.Count));
        }

        private void PruneDead()
        {
            for (int i = _alive.Count - 1; i >= 0; i--)
            {
                if (_alive[i] == null)
                {
                    _alive.RemoveAt(i);
                }
            }
        }

        private Duck SpawnDuck(int courseIndex)
        {
            IReadOnlyList<Vector3Int> cells = _streams != null ? _streams.CourseCells(courseIndex) : null;
            if (cells == null || cells.Count == 0)
            {
                return null;
            }

            // Build the world route from the course's cell centers (so the duck floats
            // dead-center in the channel), plus a parallel flag marking which cells are
            // walkable bridge decks ('B') — the duck slips UNDER those.
            var route = new List<Vector3>(cells.Count);
            var underBridge = new List<bool>(cells.Count);
            for (int i = 0; i < cells.Count; i++)
            {
                route.Add(_streams.CellCenter(cells[i]));
                underBridge.Add(_map != null && _map.IsWalkableCell(cells[i]));
            }

            var go = new GameObject("Duck");
            go.transform.SetParent(_overworldRoot, false);
            go.transform.position = route[0]; // the COAST end of the course

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = DuckSorting;
            sr.sprite = _sideSprite;

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.3f; // generous toddler touch target, ~0.5-unit duck
            col.isTrigger = true;

            var duck = go.AddComponent<Duck>();
            duck.Init(route, underBridge, _sideSprite, _flySprite, sr, col, DuckSorting);
            _alive.Add(duck);
            return duck;
        }

        // ------------------------------------------------------------- TEST HOOKS

        /// <summary>TEST HOOK. Spawn a duck immediately at the first stream head,
        /// bypassing the timer and the alive cap. Returns the live duck (or null if
        /// there are no streams / no art wired).</summary>
        internal Duck TestForceSpawnDuck()
        {
            return _streams != null && _streams.Count > 0 ? SpawnDuck(0) : null;
        }

        internal int TestAliveCount
        {
            get
            {
                PruneDead();
                return _alive.Count;
            }
        }
    }

    /// <summary>
    /// One drifting duck. Floats cell-to-cell along a stream course with a gentle
    /// bob (facing its travel direction), and despawns at the mouth. A tap catches
    /// it: quack SFX, flap-away arc (rising + accelerating offscreen) using the
    /// flying frame, and a fruit-or-treasure pickup dropped where it sat.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class Duck : MonoBehaviour, ITappable
    {
        private enum Mode { Drift, FlyAway }

        private const float DriftSpeed = 0.5f;   // world units / sec
        private const float ArriveEps = 0.05f;
        private const float BobAmp = 0.05f;
        private const float BobRate = 3.2f;
        private const float FlyMaxSeconds = 3f;

        private SpriteRenderer _sr;
        private CircleCollider2D _col;
        private Sprite _side;
        private Sprite _fly;
        private int _baseSorting;

        private const float FadeRate = 3f; // alpha units / sec when ducking under a bridge

        private List<Vector3> _route;
        private List<bool> _underBridge; // per-route-cell: true where a bridge decks the water
        private int _index;
        private Vector3 _basePos;   // un-bobbed drift position
        private float _bobPhase;
        private bool _faceRight = true;
        private float _fade = 1f;   // sprite alpha (dips toward 0 while passing under a bridge)

        private Mode _mode = Mode.Drift;
        private bool _caught;
        private float _flyElapsed;
        private Vector3 _flyVel;

        internal bool TestCaught => _caught;

        public void Init(List<Vector3> route, List<bool> underBridge, Sprite side, Sprite fly,
            SpriteRenderer sr, CircleCollider2D col, int baseSorting)
        {
            _route = route;
            _underBridge = underBridge;
            _side = side;
            _fly = fly;
            _sr = sr != null ? sr : GetComponent<SpriteRenderer>();
            _col = col != null ? col : GetComponent<CircleCollider2D>();
            _baseSorting = baseSorting;

            _bobPhase = Random.value * Mathf.PI * 2f;
            _index = _route != null && _route.Count > 1 ? 1 : 0;
            _basePos = _route != null && _route.Count > 0 ? _route[0] : transform.position;
            transform.position = _basePos;
            _fade = 1f;

            if (_sr != null && _side != null)
            {
                _sr.sprite = _side;
            }
        }

        // True if the route cell at the given index is a walkable bridge deck.
        private bool IsUnderBridge(int i) =>
            _underBridge != null && i >= 0 && i < _underBridge.Count && _underBridge[i];

        private void Update()
        {
            switch (_mode)
            {
                case Mode.Drift:
                    TickDrift();
                    break;
                case Mode.FlyAway:
                    TickFlyAway();
                    break;
            }
        }

        private void TickDrift()
        {
            if (_route == null || _index >= _route.Count)
            {
                Destroy(gameObject); // reached the mouth (or no route) — gone
                return;
            }

            Vector3 target = _route[_index];
            Vector3 delta = target - _basePos;
            delta.z = 0f;
            float d = delta.magnitude;

            if (d <= ArriveEps)
            {
                _basePos = new Vector3(target.x, target.y, _basePos.z);
                _index++;
            }
            else
            {
                Vector3 step = delta / d * Mathf.Min(DriftSpeed * Time.deltaTime, d);
                _basePos += step;
                if (Mathf.Abs(step.x) > 0.0001f)
                {
                    _faceRight = step.x >= 0f;
                    if (_sr != null)
                    {
                        _sr.flipX = !_faceRight; // art faces EAST; flip for west drift
                    }
                }
            }

            // Gentle vertical bob for a "floating" read (collider follows).
            _bobPhase += Time.deltaTime * BobRate;
            float bob = Mathf.Sin(_bobPhase) * BobAmp;
            transform.position = _basePos + new Vector3(0f, bob, 0f);

            // Pass UNDER bridges: the deck (a grey stone/path tile) draws on the ground
            // layer below every sprite, so a duck drawn on top would look wrong. While the
            // duck is crossing a bridged cell (either the one it's heading to or the one it
            // just left) fade it out so it reads as slipping beneath the deck, then fade
            // back in on the far side.
            bool crossing = IsUnderBridge(_index) || IsUnderBridge(_index - 1);
            _fade = Mathf.MoveTowards(_fade, crossing ? 0f : 1f, Time.deltaTime * FadeRate);
            if (_sr != null)
            {
                Color col = _sr.color;
                col.a = _fade;
                _sr.color = col;
            }
        }

        private void TickFlyAway()
        {
            _flyElapsed += Time.deltaTime;

            // Rise + accelerate: gravity in reverse, so it lifts off then races up.
            _flyVel.y += 6f * Time.deltaTime;
            transform.position += _flyVel * Time.deltaTime;

            // Cheerful wing-flap wobble on the way up.
            transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(_flyElapsed * 22f) * 10f);

            if (_flyElapsed >= FlyMaxSeconds || IsAboveView())
            {
                Destroy(gameObject);
            }
        }

        private bool IsAboveView()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return false;
            }

            float top = cam.transform.position.y + cam.orthographicSize + 1.5f;
            return transform.position.y > top;
        }

        // ----- Tap-to-catch -----

        public void OnTapped(Vector2 worldPoint)
        {
            if (_caught)
            {
                return;
            }

            _caught = true;
            _mode = Mode.FlyAway;
            _flyElapsed = 0f;

            if (_col != null)
            {
                _col.enabled = false;
            }

            if (_sr != null)
            {
                if (_fly != null)
                {
                    _sr.sprite = _fly;
                }

                _sr.sortingOrder = _baseSorting + 30; // flap over everything on the way out
                _fade = 1f;
                Color col = _sr.color; // undo any under-bridge fade so the catch is fully visible
                col.a = 1f;
                _sr.color = col;
            }

            // Reward drops where the duck sat (GameManager clamps it to walkable land).
            Vector3 spot = _basePos;
            GameManager gm = GameManager.Instance;
            if (gm != null)
            {
                // Reuse the wired Honk clip as the quack (a honk-tone fits a duck and
                // is already mapped to a Kenney sound; avoids a silent new slot).
                gm.Audio?.Honk();

                ItemType type = Random.value < 0.5f ? ItemType.Treasure : ItemType.Fruit;
                int variant = Random.Range(0, 4);
                gm.SpawnRewardPickup(type, DinoType.TRex, variant, spot);
            }

            // Launch: continue in the facing direction, angled up and to the side.
            float sx = _faceRight ? 1f : -1f;
            _flyVel = new Vector3(sx * 0.9f, 2.4f, 0f);
        }
    }
}
