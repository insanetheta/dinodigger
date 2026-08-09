using System.Collections.Generic;
using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;
using DinoDigger.Managers;

namespace DinoDigger.Overworld
{
    /// <summary>
    /// THE DINO-MATIC SERVICE (DinoDigger-3rz): discovery, placement, hand-off to the town
    /// crew, and persistence for the one machine that brings skeletons back. The machine
    /// itself (<see cref="DinoMatic"/>) owns only its own beats.
    ///
    /// EARNED, NOT PRE-PLACED — the Machine Friends rule, applied to the finale. On day zero
    /// there is nothing there. The FIRST BONE the child banks is the discovery gate: the
    /// island coughs up a suspicious mound with a glinting dome, and from then on it is real.
    /// The gate is the honest signal, because a bone is the only thing the machine is for.
    ///
    /// IT RESPECTS THE DISCOVERY QUEUE. The Dino-Matic counts as a machine friend for pacing:
    /// while some OTHER machine is still standing in the world undiscovered and glinting for
    /// attention, the Dino-Matic's arrival waits. Two glinting strangers competing for one
    /// pair of eyes is exactly what that guard exists to prevent, and a finale beat is the
    /// last thing that should be shouted over.
    ///
    /// THE CREW DIGS IT OUT, THE PLAYER NEVER DOES. Once placed, the machine is handed to
    /// <see cref="TownController"/> as a ZERO-COST FREE SITE. That was the cleanest of the two
    /// options: a "small treasure price" would have made the finale's arrival contingent on the
    /// wallet — a child who has dug 24 bones but spent every coin on the town would be told to
    /// come back later, which is the opposite of a reward — while a free site reuses the crew
    /// machinery verbatim and can never charge for a thing the child already earned. Because
    /// the only labour source in the game is <c>GameManager.TownAcquireBuilders</c> (non-buddy
    /// residents only), "the player is never drafted" stays structural rather than policed.
    ///
    /// Wholly null-tolerant: with no map, no mounds and no town it simply never finds a spot,
    /// stays queued, and nothing throws.
    /// </summary>
    public class DinoMaticController : MonoBehaviour
    {
        // Placement scan. Rings of candidate spots around the centre of the dig-mound belt,
        // sampled on an isometric-squashed ellipse (a circle in cell space is an ellipse on
        // screen, and the child sees the screen).
        private const int Rings = 5;
        private const int Spokes = 16;
        private const float MinRadius = 1.6f;
        private const float MaxRadius = 5.5f;

        // How far from the nearest dig mound the machine must stand. PREFERRED keeps its tap
        // target fully clear of a mound's; MINIMUM is the "place it anyway" floor, because a
        // cramped island must still get its finale.
        private const float PreferredMoundClearance = 2.2f;
        private const float MinimumMoundClearance = 1.2f;

        [SerializeField] private OverworldMap _map;
        [SerializeField] private PlaceholderLibrary _library;
        [SerializeField] private GameConfig _config;
        [SerializeField] private TownController _town;
        [SerializeField] private TownArea _townArea;
        [SerializeField] private MachineFriendController _machines;
        [SerializeField] private MeadowArea _meadow;
        [SerializeField] private GardenArea _garden;
        [SerializeField] private Transform _root;

        private readonly List<DigMound> _mounds = new List<DigMound>();

        private DinoMatic _site;
        private bool _found;              // the first bone has been banked
        private bool _wasExcavated;       // edge-detect so a finished excavation persists once
        private int _restoreState;        // construction state a restored site resumes at
        private float _restoreWorked;
        private int _arrivals;            // test-observable

        /// <summary>The live machine, or null while it has not arrived.</summary>
        public DinoMatic Site => _site;

        /// <summary>True once the first bone has been banked (the site exists or is queued).</summary>
        public bool IsFound => _found;

        /// <summary>True once the crew has dug it all the way out.</summary>
        public bool IsExcavated => _site != null && _site.IsExcavated;

        // ------------------------------------------------------------ TEST HOOKS
        internal DinoMatic TestSite => _site;
        internal bool TestFound => _found;
        internal bool TestPresent => _site != null;
        internal bool TestExcavated => IsExcavated;
        internal int TestArrivals => _arrivals;

        /// <summary>Wire the service. Every reference is optional.</summary>
        public void Configure(OverworldMap map, PlaceholderLibrary library, GameConfig config,
            TownController town, TownArea townArea, MachineFriendController machines,
            MeadowArea meadow, GardenArea garden, IList<DigMound> mounds, Transform root)
        {
            _map = map;
            _library = library;
            _config = config;
            _town = town;
            _townArea = townArea;
            _machines = machines;
            _meadow = meadow;
            _garden = garden;
            _root = root != null ? root : transform;

            _mounds.Clear();
            if (mounds != null)
            {
                for (int i = 0; i < mounds.Count; i++)
                {
                    if (mounds[i] != null)
                    {
                        _mounds.Add(mounds[i]);
                    }
                }
            }
        }

        // ------------------------------------------------------------ gate + tick

        /// <summary>GATE: a fossil bone was banked. The island has heard there is a digger
        /// worth waking up for. Idempotent — every later bone is a no-op.</summary>
        public void NotifyBoneBanked()
        {
            if (_found)
            {
                return;
            }

            _found = true;
            GameManager.Instance?.DinoMaticPersist();
        }

        /// <summary>Driven every frame by GameManager: releases the arrival once the pacing
        /// guard allows it, and edge-detects the excavation finishing so it persists once.</summary>
        public void Tick(float dt)
        {
            if (!_found)
            {
                return;
            }

            if (_site == null)
            {
                // PACING: never land on top of another undiscovered machine's moment.
                bool oneAtATime = _config == null || _config.MachineOneDiscoveryAtATime;
                if (oneAtATime && _machines != null && _machines.HasUndiscoveredMachine())
                {
                    return;
                }

                TrySpawn();
                return;
            }

            // The excavation state lives in the save, but the town's crew is what advances it,
            // so watch for the transition rather than asking the town to tell us. One less
            // callback to keep alive across a reset.
            bool excavated = _site.IsExcavated;
            if (excavated != _wasExcavated)
            {
                _wasExcavated = excavated;
                GameManager.Instance?.DinoMaticPersist();
            }
        }

        // ----------------------------------------------------------------- spawn

        /// <summary>Build the machine at its scanned home and hand it to the town crew.
        /// Returns false when no spot can be found yet (no map, no mounds) — the arrival then
        /// simply stays pending and is retried next frame.</summary>
        private bool TrySpawn()
        {
            if (!TryFindSpot(out Vector3 pos))
            {
                return false;
            }

            var go = new GameObject("DinoMatic");
            go.transform.SetParent(_root != null ? _root : transform, false);
            go.transform.position = pos;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 12; // among the overworld props, like a town building

            var machine = go.AddComponent<DinoMatic>();

            GameManager gm = GameManager.Instance;
            ParticleSystem crumbs = gm != null
                ? gm.TownCreateParticles(go.transform,
                    _library != null ? _library.CrumbParticle : null,
                    new Color(0.78f, 0.62f, 0.42f), 0.3f)
                : null;
            ParticleSystem sparkle = gm != null
                ? gm.MachineCreateParticles(go.transform,
                    _library != null ? _library.StarParticle : null,
                    new Color(1f, 0.94f, 0.65f), 0.3f)
                : null;
            if (sparkle != null)
            {
                sparkle.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            }

            // Its own five-state art if the batch has landed; missing states fall back to the
            // generic building placeholder exactly like a half-generated town building.
            BuildingArt art = _library != null && _library.DinoMaticArt != null &&
                              _library.DinoMaticArt.HasAny
                ? _library.DinoMaticArt
                : null;

            machine.Init(_library, _config, sr, crumbs,
                Mathf.Clamp(_restoreState, 0, BuildingController.ConstructionStates),
                Mathf.Max(0f, _restoreWorked), art);
            machine.Configure(_library, sparkle);
            machine.WireTown(_town, -1);

            _site = machine;
            _wasExcavated = machine.IsExcavated;
            _arrivals++;
            _restoreState = 0;
            _restoreWorked = 0f;

            // Hand it to the crew as a zero-cost site. A machine restored already excavated is
            // NOT offered — there is nothing left to dig.
            if (!machine.IsExcavated)
            {
                _town?.SetFreeSite(machine, pos);
            }

            return true;
        }

        /// <summary>
        /// THE SPOT. A deterministic scan (no randomness — the machine lands in the same place
        /// for everyone, which is what makes it a landmark rather than clutter) for a clear,
        /// walkable spot NEAR THE DIG-MOUND BELT: the machine belongs where the bones come
        /// from, not in the plaza with the town's buildings.
        ///
        /// The scan mirrors the one the machine friends use for their homes — rings of
        /// candidates around a centre, scored by clearance, nearest-in wins — with the centre
        /// being the mound belt's centroid and the exclusions being everything the machine
        /// must not sit on: water/obstacles (walkability), the meadow, the town district and
        /// the berry patch (all reserved rects that a prop would clutter or that mound
        /// respawns already avoid), and every dig mound's tap target.
        /// </summary>
        private bool TryFindSpot(out Vector3 pos)
        {
            pos = Vector3.zero;
            if (!TryMoundCentroid(out Vector3 center))
            {
                return false;
            }

            Vector3 bestPreferred = Vector3.zero;
            float bestPreferredDist = float.MaxValue;
            bool havePreferred = false;

            Vector3 bestFallback = Vector3.zero;
            float bestFallbackClearance = -1f;

            for (int r = 0; r < Rings; r++)
            {
                float radius = Mathf.Lerp(MinRadius, MaxRadius, Rings == 1 ? 0f : r / (float)(Rings - 1));
                for (int a = 0; a < Spokes; a++)
                {
                    float ang = a * (Mathf.PI * 2f / Spokes);
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

                    if (IsReserved(p))
                    {
                        continue;
                    }

                    float clearance = NearestMoundDistance(p);
                    if (clearance >= PreferredMoundClearance)
                    {
                        float d = (p - center).magnitude;
                        if (!havePreferred || d < bestPreferredDist)
                        {
                            havePreferred = true;
                            bestPreferredDist = d;
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

            if (bestFallbackClearance >= MinimumMoundClearance)
            {
                pos = bestFallback;
                return true;
            }

            return false;
        }

        /// <summary>Centre of the dig-mound belt: the mean of every mound's position (active or
        /// consumed — a consumed mound respawns, so the belt is where they ALL are). Falls back
        /// to the backhoe so a scene with no mounds still places something sensible.</summary>
        private bool TryMoundCentroid(out Vector3 center)
        {
            center = Vector3.zero;
            int n = 0;
            for (int i = 0; i < _mounds.Count; i++)
            {
                if (_mounds[i] != null)
                {
                    center += _mounds[i].transform.position;
                    n++;
                }
            }

            if (n > 0)
            {
                center /= n;
                center.z = 0f;
                return true;
            }

            GameManager gm = GameManager.Instance;
            if (gm != null)
            {
                center = gm.RewardSpawnPoint;
                center.z = 0f;
                return true;
            }

            return false;
        }

        /// <summary>True for a point inside a district that already belongs to something else.</summary>
        private bool IsReserved(Vector3 p)
        {
            if (_meadow != null && _meadow.ContainsOuter(p))
            {
                return true;
            }

            if (_map != null && _map.InTownDistrict(p))
            {
                return true;
            }

            if (_townArea != null && _townArea.ContainsWorld(p))
            {
                return true;
            }

            if (_garden != null && _garden.ContainsWorldExpanded(p, 1))
            {
                return true;
            }

            return false;
        }

        private float NearestMoundDistance(Vector3 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < _mounds.Count; i++)
            {
                if (_mounds[i] == null)
                {
                    continue;
                }

                Vector3 m = _mounds[i].transform.position;
                m.z = p.z;
                best = Mathf.Min(best, (m - p).magnitude);
            }

            return best;
        }

        // ------------------------------------------------------------ persistence

        /// <summary>Restore the machine's discovery + excavation state (save v5). The machine
        /// itself is NOT built here — the tick builds it, so "the Dino-Matic appears" has
        /// exactly ONE code path whether it is arriving for the first time or coming back from
        /// a save. That is the machine-friends rule, and the reason a restored half-dug site
        /// resumes with a crew instead of a special case.</summary>
        public void RestoreFromSave(SaveData data)
        {
            _found = false;
            _restoreState = 0;
            _restoreWorked = 0f;
            _wasExcavated = false;

            if (data == null)
            {
                return;
            }

            _found = data.DinoMaticFound;
            _restoreState = Mathf.Clamp(data.DinoMaticState, 0, BuildingController.ConstructionStates);
            _restoreWorked = Mathf.Max(0f, data.DinoMaticWorked);
        }

        /// <summary>Write discovery + excavation state into the payload, alongside the town's.</summary>
        public void WriteSave(SaveData data)
        {
            if (data == null)
            {
                return;
            }

            data.DinoMaticFound = _found;
            if (_site != null)
            {
                data.DinoMaticState = _site.State;
                data.DinoMaticWorked = _site.WorkedPartial;
            }
            else
            {
                // Not built yet this session: keep whatever we were restored with, so a save
                // written before the arrival lands cannot rewind a half-dug excavation.
                data.DinoMaticState = _restoreState;
                data.DinoMaticWorked = _restoreWorked;
            }
        }

        // ------------------------------------------------------------- test reset

        /// <summary>TEST HOOK. Tear the machine back out of the world and forget the gate, so
        /// each case starts from day zero. Does NOT touch the save — the caller owns Save.Data,
        /// exactly as it does for the town and the machine friends.
        ///
        /// THE TEARDOWN IS IMMEDIATE, NOT END-OF-FRAME. <c>Destroy</c> is deferred, so a
        /// destroyed-but-not-yet-collected machine keeps answering <c>Physics2D.OverlapPointAll</c>
        /// for the rest of the frame — and a case that calls <c>GameManager.TestReset</c> itself
        /// and then immediately taps (most of them do) would have its tap swallowed by a machine
        /// that is supposed to be gone, with nothing on screen to explain it. Deactivating first
        /// makes the collider inert the instant this returns; the Destroy then cleans up.
        /// Handing the free-site offer back to the town is order-independent belt and braces:
        /// GameManager.TestReset already resets the town first, but the service should not
        /// depend on that to avoid leaving the crew pointed at a corpse.</summary>
        internal void TestResetDinoMatic()
        {
            if (_site != null)
            {
                _town?.SetFreeSite(null, Vector3.zero);
                _site.gameObject.SetActive(false);
                Destroy(_site.gameObject);
            }

            _site = null;
            _found = false;
            _wasExcavated = false;
            _restoreState = 0;
            _restoreWorked = 0f;
            _arrivals = 0;
        }

        /// <summary>TEST HOOK. Force the arrival to be attempted RIGHT NOW instead of waiting
        /// for the next tick, so a case can assert the site on the very next frame.</summary>
        internal bool TestForceArrival()
        {
            if (_site != null)
            {
                return true;
            }

            _found = true;
            return TrySpawn();
        }
    }
}
