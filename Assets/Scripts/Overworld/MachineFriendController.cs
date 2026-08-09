using System.Collections.Generic;
using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;
using DinoDigger.Managers;

namespace DinoDigger.Overworld
{
    /// <summary>
    /// THE MACHINE FRIENDS SERVICE (epic DinoDigger-b48). Owns discovery, arrival, placement
    /// and persistence for Doodle, Sprinkles and Tuggy; the machines themselves own only
    /// their own one job.
    ///
    /// MACHINES ARE EARNED, NOT PRE-PLACED. Nothing is standing in the world on day zero.
    /// Each machine has a DISCOVERY GATE — a thing the child does in the loop that machine
    /// serves — and it only arrives once that gate trips. In-world the story is "it heard
    /// about you and came to help":
    ///
    ///   Sprinkles  <- the first berry HARVESTED. The garden's only player verb is the
    ///                 harvest tap (a sprout ripens on a timer with nobody involved), so a
    ///                 harvest is the honest signal that the child has actually engaged the
    ///                 garden rather than merely walked past it on day zero.
    ///   Tuggy      <- the first duck CAUGHT. The child must know and love the duck-catch
    ///                 before the duck-amplifier turns up; arriving earlier would introduce
    ///                 the ducks' machine before the ducks.
    ///   Doodle     <- the first town BUILDING FINISHED. A plaza needs a plaza first.
    ///
    /// ARRIVAL IS AN EVENT, NOT A PROP. A machine that has just landed is DORMANT and runs
    /// the "come look" beacon (a gentle forever-sway plus a repeating sparkle glint — the
    /// same language as the dig's Surprise Pocket), and it joins the idle-attract pulse
    /// rotation so a quiet moment can point at it. There is no popup, no camera seizure and
    /// no sound: the child has to spot the glint and hunt it down, and then the first tap
    /// wakes it forever.
    ///
    /// ONE DISCOVERY AT A TIME. At most ONE undiscovered machine may stand in the world.
    /// If a second gate trips while a sleeper is still waiting to be found, the second
    /// machine QUEUES and arrives the moment the first is woken. That is the bible's arc —
    /// "the island wakes up, one friend at a time" — made mechanical, and it stops two
    /// glinting strangers from competing for the same pair of eyes. Config-disableable
    /// (<see cref="GameConfig.MachineOneDiscoveryAtATime"/>) so a test can set up several
    /// machines at once.
    ///
    /// Built + wired by SceneBuilder (sibling of the duck spawner), ticked by GameManager,
    /// and fully null-tolerant: with no garden / no streams / no town the corresponding
    /// machine simply never arrives, and nothing throws.
    /// </summary>
    public class MachineFriendController : MonoBehaviour
    {
        // Roster order == MachineKind order. Used for the arrival queue's tie-break, so two
        // gates tripping on the same frame always resolve the same way.
        private static readonly MachineKind[] Roster =
        {
            MachineKind.Doodle, MachineKind.Sprinkles, MachineKind.Tuggy
        };

        // Fallback body tints when a machine's real art has not been imported. The mound
        // sprite under one of these is never mistaken for a mound, and — crucially — the
        // machine stays VISIBLE and tappable, so a sleeping friend can never become an
        // invisible hole in the world that a child taps by accident.
        private static readonly Color DoodleFallbackTint = new Color(0.86f, 0.55f, 0.28f);
        private static readonly Color SprinklesFallbackTint = new Color(0.45f, 0.75f, 0.92f);
        private static readonly Color TuggyFallbackTint = new Color(0.90f, 0.36f, 0.32f);

        // Machine world heights, matching the importer's overworld-prop target (~1.1 units).
        private const float MachineHeight = 1.1f;

        [SerializeField] private OverworldMap _map;
        [SerializeField] private PlaceholderLibrary _library;
        [SerializeField] private GameConfig _config;
        [SerializeField] private TownArea _townArea;
        [SerializeField] private TownController _town;
        [SerializeField] private GardenArea _garden;
        [SerializeField] private List<BerrySprout> _sprouts = new List<BerrySprout>();
        [SerializeField] private StreamNetwork _streams;
        [SerializeField] private DuckController _ducks;
        [SerializeField] private Transform _root;

        // Live machines, by kind. A kind absent from here has not arrived yet.
        private readonly Dictionary<MachineKind, MachineFriend> _live =
            new Dictionary<MachineKind, MachineFriend>();

        // Gates that have tripped (the child did the thing) and machines already woken.
        private readonly HashSet<MachineKind> _gated = new HashSet<MachineKind>();
        private readonly HashSet<MachineKind> _woken = new HashSet<MachineKind>();

        // Gated machines waiting their turn behind an undiscovered sleeper (pacing guard).
        private readonly List<MachineKind> _queue = new List<MachineKind>();

        private int _arrivals;   // test-observable

        /// <summary>Wire the service. Every reference is optional: a null garden means
        /// Sprinkles never arrives, a null stream network means Tuggy never does, and so on.</summary>
        public void Configure(OverworldMap map, PlaceholderLibrary library, GameConfig config,
            TownArea townArea, TownController town, GardenArea garden, IList<BerrySprout> sprouts,
            StreamNetwork streams, DuckController ducks, Transform root)
        {
            _map = map;
            _library = library;
            _config = config;
            _townArea = townArea;
            _town = town;
            _garden = garden;
            _streams = streams;
            _ducks = ducks;
            _root = root != null ? root : transform;

            _sprouts.Clear();
            if (sprouts != null)
            {
                for (int i = 0; i < sprouts.Count; i++)
                {
                    if (sprouts[i] != null)
                    {
                        _sprouts.Add(sprouts[i]);
                    }
                }
            }
        }

        // -------------------------------------------------------------- gate hooks

        /// <summary>GATE: a ripe berry was harvested. Sprinkles has heard the garden is in
        /// business. Idempotent — every later harvest is a no-op.</summary>
        public void NotifyBerryHarvested() => TripGate(MachineKind.Sprinkles);

        /// <summary>GATE: a duck was caught. Tuggy has heard there is a duck fan on the
        /// island. Idempotent.</summary>
        public void NotifyDuckCaught() => TripGate(MachineKind.Tuggy);

        /// <summary>GATE: a town building was finished. Doodle has heard there is a plaza.
        /// Also polled in <see cref="Tick"/>, so a town restored from a save trips it on the
        /// first frame without needing an event to have fired this session.</summary>
        public void NotifyBuildingFinished() => TripGate(MachineKind.Doodle);

        private void TripGate(MachineKind kind)
        {
            if (!_gated.Add(kind))
            {
                return; // already tripped: gates are one-way and fire exactly once
            }

            Enqueue(kind);
            GameManager.Instance?.MachinePersist();
        }

        // ------------------------------------------------------------------- tick

        /// <summary>Driven every frame by GameManager. Polls the town gate (so a save-restored
        /// town counts) and lets the arrival queue release its next machine.</summary>
        public void Tick(float dt)
        {
            if (_town != null && _town.FinishedBuildingCount > 0)
            {
                TripGate(MachineKind.Doodle);
            }

            PumpQueue();
        }

        /// <summary>Release queued machines into the world, subject to the pacing guard.</summary>
        private void PumpQueue()
        {
            if (_queue.Count == 0)
            {
                return;
            }

            bool oneAtATime = _config == null || _config.MachineOneDiscoveryAtATime;

            for (int i = 0; i < _queue.Count; i++)
            {
                MachineKind kind = _queue[i];

                if (_live.ContainsKey(kind))
                {
                    _queue.RemoveAt(i);
                    return; // already here; re-enter next frame with a clean list
                }

                // THE PACING GUARD. One undiscovered sleeper at a time, so each arrival gets
                // the child's whole attention. A machine that is already woken no longer
                // counts — it has been found, and the island may offer the next friend.
                if (oneAtATime && HasUndiscoveredMachine())
                {
                    return;
                }

                if (!Spawn(kind))
                {
                    continue; // can't place it yet (no garden/stream/plaza wired) — try later
                }

                _queue.RemoveAt(i);
                return; // exactly one arrival per frame, always
            }
        }

        /// <summary>True while a machine is standing in the world dormant and un-tapped.</summary>
        public bool HasUndiscoveredMachine()
        {
            foreach (KeyValuePair<MachineKind, MachineFriend> kv in _live)
            {
                if (kv.Value != null && !kv.Value.IsAwake)
                {
                    return true;
                }
            }

            return false;
        }

        private void Enqueue(MachineKind kind)
        {
            if (_live.ContainsKey(kind) || _queue.Contains(kind))
            {
                return;
            }

            _queue.Add(kind);

            // Keep the queue in roster order so simultaneous gates always release in the same
            // sequence — a deterministic world beats a marginally more "interesting" one.
            _queue.Sort((a, b) => System.Array.IndexOf(Roster, a).CompareTo(System.Array.IndexOf(Roster, b)));
        }

        // ------------------------------------------------------------------ spawn

        /// <summary>Build one machine at its home spot. Returns false when the world does not
        /// (yet) have what that machine needs — no plaza, no garden, no water — in which case
        /// the arrival simply stays queued.</summary>
        private bool Spawn(MachineKind kind)
        {
            Vector3 pos;
            switch (kind)
            {
                case MachineKind.Doodle:
                    if (!TryPlazaSpot(out pos))
                    {
                        return false;
                    }

                    break;

                case MachineKind.Sprinkles:
                    if (!TryGardenEdgeSpot(out pos))
                    {
                        return false;
                    }

                    break;

                case MachineKind.Tuggy:
                    if (!TryStreamMooring(out pos, out int course))
                    {
                        return false;
                    }

                    _live[kind] = BuildTuggy(pos, course);
                    _arrivals++;
                    return true;

                default:
                    return false;
            }

            MachineFriend m = kind == MachineKind.Doodle ? BuildDoodle(pos) : BuildSprinkles(pos);
            if (m == null)
            {
                return false;
            }

            _live[kind] = m;
            _arrivals++;
            return true;
        }

        private GameObject NewMachineObject(string name, Vector3 pos, MachineKind kind,
            out SpriteRenderer body, out Color tint)
        {
            // The ROOT stands on the ground at scale 1 — that is what makes the machine's
            // RestingScale trivially safe and keeps every overlay's world size honest. The
            // art lives on a CHILD that carries its own size normalisation and, for the
            // center-pivot fallback sprite, its own half-height lift.
            var go = new GameObject(name);
            go.transform.SetParent(_root != null ? _root : transform, false);
            go.transform.position = pos;

            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(go.transform, false);

            body = bodyGo.AddComponent<SpriteRenderer>();
            body.sortingOrder = MachineFriend.MachineSorting;

            Sprite art = _library != null ? _library.Machine((int)kind) : null;
            bool bottomPivot = art != null; // machines import bottom-center; the blob is centred
            if (art != null)
            {
                body.sprite = art;
                tint = Color.white;
            }
            else
            {
                // Fallback: the mound blob under the machine's signature colour. Always
                // visible, always tappable, obviously a placeholder to an adult eye.
                body.sprite = _library != null ? _library.MoundSprite : null;
                tint = FallbackTint(kind);
            }

            // Normalise to the machine world height whatever the source art measures, so the
            // three read as one family and the tap collider is always the right size.
            if (body.sprite != null && body.sprite.bounds.size.y > 0.001f)
            {
                float k = MachineHeight / body.sprite.bounds.size.y;
                bodyGo.transform.localScale = new Vector3(k, k, 1f);
            }

            if (!bottomPivot)
            {
                bodyGo.transform.localPosition = new Vector3(0f, MachineHeight * 0.5f, 0f);
            }

            return go;
        }

        private static Color FallbackTint(MachineKind kind)
        {
            switch (kind)
            {
                case MachineKind.Doodle: return DoodleFallbackTint;
                case MachineKind.Sprinkles: return SprinklesFallbackTint;
                default: return TuggyFallbackTint;
            }
        }

        private DoodleMachine BuildDoodle(Vector3 pos)
        {
            GameObject go = NewMachineObject("Doodle", pos, MachineKind.Doodle,
                out SpriteRenderer body, out Color tint);

            // The crank handle: the builder mallet sprite reads as a handle on a stick, which
            // is exactly what a music-box crank is. Null-tolerant (no mallet art, no crank).
            Transform crank = null;
            Sprite handle = _library != null ? _library.ToolHammer : null;
            if (handle != null)
            {
                var crankGo = new GameObject("Crank");
                crankGo.transform.SetParent(go.transform, false);
                crankGo.transform.localPosition = new Vector3(0.42f, MachineHeight * 0.55f, 0f);
                var sr = crankGo.AddComponent<SpriteRenderer>();
                sr.sprite = handle;
                sr.sortingOrder = MachineFriend.MachineSorting + 1;
                crank = crankGo.transform;
            }

            var machine = go.AddComponent<DoodleMachine>();
            machine.BuildOverlays(_library, body, MachineHeight, MakeSparkle(go.transform));
            machine.AttachCrank(crank);
            machine.Configure(this, _config, _library, tint, _woken.Contains(MachineKind.Doodle));
            return machine;
        }

        private SprinklesMachine BuildSprinkles(Vector3 pos)
        {
            GameObject go = NewMachineObject("Sprinkles", pos, MachineKind.Sprinkles,
                out SpriteRenderer body, out Color tint);

            var machine = go.AddComponent<SprinklesMachine>();
            machine.BuildOverlays(_library, body, MachineHeight, MakeSparkle(go.transform));
            machine.SetGarden(_sprouts, pos);
            machine.Configure(this, _config, _library, tint, _woken.Contains(MachineKind.Sprinkles));
            return machine;
        }

        private TuggyMachine BuildTuggy(Vector3 pos, int courseIndex)
        {
            GameObject go = NewMachineObject("Tuggy", pos, MachineKind.Tuggy,
                out SpriteRenderer body, out Color tint);

            var machine = go.AddComponent<TuggyMachine>();
            machine.BuildOverlays(_library, body, MachineHeight, MakeSparkle(go.transform));
            machine.SetStream(_streams, _map, _ducks, courseIndex,
                _library != null ? _library.MoundSprite : null);
            machine.Configure(this, _config, _library, tint, _woken.Contains(MachineKind.Tuggy));
            return machine;
        }

        private ParticleSystem MakeSparkle(Transform parent)
        {
            GameManager gm = GameManager.Instance;
            if (gm == null)
            {
                return null;
            }

            // Reuses the shared particle factory (same one the town's ambient FX use), so a
            // machine's sparkle is the same visual vocabulary as every other reward puff.
            ParticleSystem ps = gm.MachineCreateParticles(parent,
                _library != null ? _library.StarParticle : null,
                new Color(1f, 0.94f, 0.65f), 0.3f);
            if (ps != null)
            {
                ps.transform.localPosition = new Vector3(0f, MachineHeight * 0.55f, 0f);
            }

            return ps;
        }

        // ------------------------------------------------------------- placements

        /// <summary>Doodle's spot: in the plaza, near the Fossil Fountain plot (the finale
        /// plot, which crowns the district centre) but CLEAR OF EVERY PLOT so he never sits
        /// on a building or steals a build site's tap target. Sampled rather than hardcoded:
        /// several rings of candidate angles are scored by their distance to the nearest plot,
        /// and the roomiest walkable one wins — so a future re-layout of the town cannot
        /// silently bury him under a building.</summary>
        private bool TryPlazaSpot(out Vector3 pos)
        {
            pos = Vector3.zero;
            if (_townArea == null || _townArea.PlotCount == 0)
            {
                return false;
            }

            // A finished building's tap collider is its sprite box — about 2.2 world units
            // wide, so ~1.1u of half-width. PREFERRED clearance puts Doodle's own (0.55u)
            // touch target fully outside that box, which is what stops a machine from ever
            // swallowing a tap meant for a building. MINIMUM is the "place him anyway"
            // floor: a cramped plaza should still get its music box.
            return TryClearSpot(_townArea.PlotWorld(_townArea.PlotCount - 1), 1.2f, 3.4f,
                preferredClearance: 1.7f, minimumClearance: 0.85f, insideTest: null,
                clearanceOf: NearestPlotDistance, pos: out pos);
        }

        private float NearestPlotDistance(Vector3 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < _townArea.PlotCount; i++)
            {
                float d = (_townArea.PlotWorld(i) - p).magnitude;
                if (d < best)
                {
                    best = d;
                }
            }

            return best;
        }

        /// <summary>Ring-sample walkable candidates around <paramref name="center"/> and pick a
        /// home for a machine: the CLOSEST-IN spot that still clears everything it must not sit
        /// on top of. Sampled rather than hardcoded so a future re-layout of the town or the
        /// garden cannot silently bury a machine under a building or on a sprout.
        ///
        /// Two bars, on purpose. A candidate meeting <paramref name="preferredClearance"/> wins
        /// outright (and among those the nearest to the centre wins, so the machine still reads
        /// as belonging to its place). If nothing does, the roomiest candidate above
        /// <paramref name="minimumClearance"/> is taken instead — a cramped world still gets its
        /// friend. Below even that, the arrival stays queued rather than landing badly.</summary>
        private bool TryClearSpot(Vector3 center, float minRadius, float maxRadius,
            float preferredClearance, float minimumClearance,
            System.Func<Vector3, bool> insideTest, System.Func<Vector3, float> clearanceOf,
            out Vector3 pos)
        {
            pos = Vector3.zero;

            Vector3 bestPreferred = Vector3.zero;
            float bestPreferredCenterDist = float.MaxValue;
            bool havePreferred = false;

            Vector3 bestFallback = Vector3.zero;
            float bestFallbackClearance = -1f;

            const int Rings = 6;
            const int Spokes = 16;
            for (int r = 0; r < Rings; r++)
            {
                float radius = Mathf.Lerp(minRadius, maxRadius, Rings == 1 ? 0f : r / (float)(Rings - 1));
                for (int a = 0; a < Spokes; a++)
                {
                    float ang = a * (Mathf.PI * 2f / Spokes);

                    // The y term is squashed because the world is isometric: a circle in cell
                    // space is an ellipse on screen, and sampling the ellipse keeps the ring
                    // an even distance away in the space the child actually sees.
                    Vector3 p = center + new Vector3(Mathf.Cos(ang) * radius,
                        Mathf.Sin(ang) * radius * 0.6f, 0f);
                    p.z = center.z;

                    if (_map != null)
                    {
                        Vector3 w = _map.NearestWalkable(p, out bool found);
                        if (!found)
                        {
                            continue;
                        }

                        p = w;
                    }

                    if (insideTest != null && !insideTest(p))
                    {
                        continue;
                    }

                    float clearance = clearanceOf(p);
                    if (clearance >= preferredClearance)
                    {
                        float centerDist = (p - center).magnitude;
                        if (!havePreferred || centerDist < bestPreferredCenterDist)
                        {
                            havePreferred = true;
                            bestPreferredCenterDist = centerDist;
                            bestPreferred = p;
                        }
                    }
                    else if (!havePreferred && clearance > bestFallbackClearance)
                    {
                        bestFallbackClearance = clearance;
                        bestFallback = p;
                    }
                }
            }

            if (havePreferred)
            {
                pos = bestPreferred;
                return true;
            }

            if (bestFallbackClearance >= minimumClearance)
            {
                pos = bestFallback;
                return true;
            }

            return false;
        }

        /// <summary>Sprinkles' spot: the EDGE of the berry patch — inside the reserved garden
        /// rect (so no dig mound can ever respawn on top of him) but as far from the sprouts as
        /// that rect allows, so he never covers a sprout's tap target.</summary>
        private bool TryGardenEdgeSpot(out Vector3 pos)
        {
            pos = Vector3.zero;
            if (_garden == null || _garden.SproutCount == 0)
            {
                return false;
            }

            // Stay INSIDE the reserved patch (or at most one cell out of it) so a dig mound
            // can never respawn on top of the bot — the rect is exactly what SpawnManager
            // keeps mounds out of — while sitting as far from the sprouts as the patch allows.
            return TryClearSpot(_garden.Center, 0.9f, 2.0f,
                preferredClearance: 0.95f, minimumClearance: 0.55f,
                insideTest: p => _garden.ContainsWorldExpanded(p, 1),
                clearanceOf: NearestSproutDistance, pos: out pos);
        }

        private float NearestSproutDistance(Vector3 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < _garden.SproutCount; i++)
            {
                float d = (_garden.SproutWorld(i) - p).magnitude;
                if (d < best)
                {
                    best = d;
                }
            }

            return best;
        }

        /// <summary>Tuggy's mooring: the head of the LONGEST stream course, so he has the most
        /// water to chug and the most room to trail a duckling line behind him.</summary>
        private bool TryStreamMooring(out Vector3 pos, out int courseIndex)
        {
            pos = Vector3.zero;
            courseIndex = 0;

            if (_streams == null || _streams.Count == 0)
            {
                return false;
            }

            int bestLen = 0;
            for (int i = 0; i < _streams.Count; i++)
            {
                IReadOnlyList<Vector3Int> cells = _streams.CourseCells(i);
                int len = cells != null ? cells.Count : 0;
                if (len > bestLen)
                {
                    bestLen = len;
                    courseIndex = i;
                }
            }

            if (bestLen < 2)
            {
                return false;
            }

            IReadOnlyList<Vector3Int> best = _streams.CourseCells(courseIndex);
            pos = _streams.CellCenter(best[0]);
            return true;
        }

        // ------------------------------------------------------------ wake + save

        /// <summary>A machine was woken by its first tap: remember it and write the save. Also
        /// unblocks the arrival queue — the next friend may now come looking.</summary>
        internal void NotifyWoken(MachineFriend machine)
        {
            if (machine == null)
            {
                return;
            }

            _woken.Add(machine.Kind);
            _gated.Add(machine.Kind); // a woken machine's gate is open by definition
            GameManager.Instance?.MachinePersist();
        }

        /// <summary>Restore gates + woken flags from the save. Machines themselves are NOT
        /// built here — the queue builds them on the next tick, which keeps one code path for
        /// "a machine appears" whether it is arriving for the first time or coming back from a
        /// save.</summary>
        public void RestoreFromSave(SaveData data)
        {
            _gated.Clear();
            _woken.Clear();
            _queue.Clear();

            if (data == null)
            {
                return;
            }

            ReadIds(data.MachineGatesTripped, _gated);
            ReadIds(data.MachinesWoken, _woken);

            // A woken machine's gate is open whatever the gate list says. Belt and braces
            // against a hand-edited or partially-written save: the child can never lose a
            // friend they already found.
            foreach (MachineKind k in _woken)
            {
                _gated.Add(k);
            }

            foreach (MachineKind k in Roster)
            {
                if (_gated.Contains(k))
                {
                    Enqueue(k);
                }
            }
        }

        /// <summary>Write gates + woken flags into the save payload. Called from
        /// GameManager.SaveNow alongside the town's own snapshot.</summary>
        public void WriteSave(SaveData data)
        {
            if (data == null)
            {
                return;
            }

            data.MachinesWoken = WriteIds(_woken);
            data.MachineGatesTripped = WriteIds(_gated);
        }

        private static void ReadIds(List<string> ids, HashSet<MachineKind> into)
        {
            if (ids == null)
            {
                return;
            }

            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                for (int k = 0; k < Roster.Length; k++)
                {
                    if (MachineFriend.IdOf(Roster[k]) == id)
                    {
                        into.Add(Roster[k]);
                        break;
                    }
                }

                // Unknown id (a save from a build with a machine this one doesn't have):
                // silently ignored, exactly like an absent field.
            }
        }

        private static List<string> WriteIds(HashSet<MachineKind> set)
        {
            var list = new List<string>();
            for (int i = 0; i < Roster.Length; i++)
            {
                if (set.Contains(Roster[i]))
                {
                    list.Add(MachineFriend.IdOf(Roster[i]));
                }
            }

            return list;
        }

        // ---------------------------------------------------------- idle attract

        /// <summary>IDLE-ATTRACT candidate: the nearest machine that is present but still
        /// undiscovered, so a quiet moment can point the child at the thing that just arrived.
        /// Null once every present machine has been woken — a found friend is never nagged
        /// about again.</summary>
        public MachineFriend NearestUndiscovered(Vector3 from)
        {
            MachineFriend best = null;
            float bestSq = float.MaxValue;

            foreach (KeyValuePair<MachineKind, MachineFriend> kv in _live)
            {
                MachineFriend m = kv.Value;
                if (m == null || m.IsAwake)
                {
                    continue;
                }

                float sq = (m.transform.position - from).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = m;
                }
            }

            return best;
        }

        // ------------------------------------------------------------ TEST HOOKS

        /// <summary>TEST HOOK. The live machine of a kind, or null when it has not arrived.</summary>
        internal MachineFriend TestMachine(MachineKind kind) =>
            _live.TryGetValue(kind, out MachineFriend m) ? m : null;

        internal DoodleMachine TestDoodle => TestMachine(MachineKind.Doodle) as DoodleMachine;
        internal SprinklesMachine TestSprinkles => TestMachine(MachineKind.Sprinkles) as SprinklesMachine;
        internal TuggyMachine TestTuggy => TestMachine(MachineKind.Tuggy) as TuggyMachine;

        /// <summary>TEST HOOK. Has this machine's discovery gate tripped?</summary>
        internal bool TestGateTripped(MachineKind kind) => _gated.Contains(kind);

        /// <summary>TEST HOOK. Is this machine standing in the world right now?</summary>
        internal bool TestPresent(MachineKind kind) =>
            _live.TryGetValue(kind, out MachineFriend m) && m != null;

        /// <summary>TEST HOOK. Machines gated but held back by the pacing guard.</summary>
        internal int TestQueuedCount => _queue.Count;

        /// <summary>TEST HOOK. Total arrivals since the last reset.</summary>
        internal int TestArrivals => _arrivals;

        /// <summary>TEST HOOK. How many present machines are still undiscovered — the pacing
        /// guard's invariant is that this is never more than 1.</summary>
        internal int TestUndiscoveredCount
        {
            get
            {
                int n = 0;
                foreach (KeyValuePair<MachineKind, MachineFriend> kv in _live)
                {
                    if (kv.Value != null && !kv.Value.IsAwake)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        /// <summary>TEST HOOK. Trip a gate directly, without playing out the loop that earns
        /// it (harvesting a berry, catching a duck, finishing a building).</summary>
        internal void TestTripGate(MachineKind kind) => TripGate(kind);

        /// <summary>TEST HOOK. Force the arrival queue to release right now instead of waiting
        /// for the next Tick — so a case can assert the arrival on the very next frame.</summary>
        internal void TestPumpQueue() => PumpQueue();

        /// <summary>TEST HOOK. Tear the whole service back down to day zero: destroy every live
        /// machine, forget every gate, empty the queue. Cases that want a specific machine then
        /// trip exactly the gate they care about. Does NOT touch the save — the caller owns
        /// Save.Data, exactly as it does for the town.</summary>
        internal void TestResetMachines()
        {
            foreach (KeyValuePair<MachineKind, MachineFriend> kv in _live)
            {
                if (kv.Value != null)
                {
                    Destroy(kv.Value.gameObject);
                }
            }

            _live.Clear();
            _gated.Clear();
            _woken.Clear();
            _queue.Clear();
            _arrivals = 0;
        }
    }
}
