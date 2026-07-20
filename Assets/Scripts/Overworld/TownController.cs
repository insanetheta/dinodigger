using System.Collections.Generic;
using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;
using DinoDigger.Managers;

namespace DinoDigger.Overworld
{
    /// <summary>
    /// The Dino Town build-queue service. Watches the treasure wallet and, with ZERO
    /// player input, auto-starts the next building the moment the wallet clears its
    /// curated price: it deducts the coins (save written), breaks ground on the next
    /// free <see cref="TownArea"/> plot, then drafts up to
    /// <see cref="GameConfig.TownMaxBuilders"/> NON-BUDDY meadow residents to commute
    /// in, work the site through its states, celebrate, and trot home.
    ///
    /// THE PLAYER IS NEVER TOUCHED. This controller holds no reference to the backhoe
    /// and cannot move it; its only labor source is <see cref="GameManager.TownAcquireBuilders"/>,
    /// which returns non-buddy residents only. Buddies and the player backhoe are
    /// structurally excluded from town construction — the hard rule is enforced by what
    /// this class simply cannot reach, not by a runtime check.
    ///
    /// Wired by SceneBuilder (the town-district ticket) via <see cref="Configure"/>;
    /// ticked by <see cref="GameManager"/>. Town state PERSISTS across restarts (save
    /// schema v4): the queue index + every building's progress are written via
    /// <see cref="WriteSave"/> (captured by GameManager.SaveNow and pushed on each build
    /// event through <see cref="GameManager.TownPersist"/>) and rebuilt on load via
    /// <see cref="RestoreFromSave"/> — finished buildings return finished (no crew, no
    /// confetti), a partial site resumes accepting crew, and the queue continues from the
    /// saved index. Also resets cleanly for the integration runner.
    /// </summary>
    public class TownController : MonoBehaviour
    {
        [SerializeField] private TownArea _area;
        [SerializeField] private PlaceholderLibrary _library;
        [SerializeField] private GameConfig _config;

        // Curated order: _nextIndex is both the next building AND its plot slot.
        private int _nextIndex;
        private BuildingController _activeSite;
        private int _activeIndex = -1;
        private readonly List<DinoController> _builders = new List<DinoController>();
        private float _workPuffTimer;

        /// <summary>True once the building at <paramref name="index"/> in build order has
        /// FINISHED. Finished buildings occupy plots 0.._nextIndex-1, so this is derived
        /// straight from the queue index (no per-building lookup). Used by the Fruit Stand
        /// sell flow to ask "is the stand open for business?".</summary>
        public bool IsBuildingFinished(int index) => index >= 0 && index < _nextIndex;

        /// <summary>World position of the plot for the building at <paramref name="index"/>
        /// (the drop-off point the Fruit Stand sell flow walks fruit to). Null-tolerant.</summary>
        public Vector3 BuildingWorld(int index) =>
            _area != null ? _area.PlotWorld(index) : transform.position;

        // TEST HOOKS (integration runner; no reflection).
        internal TownArea TestArea => _area;
        internal BuildingController TestActiveSite => _activeSite;
        internal int TestNextIndex => _nextIndex;
        internal int TestBuilderCount => _builders.Count;
        internal IReadOnlyList<DinoController> TestBuilders => _builders;

        private void OnEnable()
        {
            // Self-register: a banked coin should break ground immediately, not only on
            // the next poll tick. (Tick() also polls, covering direct wallet writes.)
            GameEvents.TreasureCollected += OnTreasureCollected;
        }

        private void OnDisable()
        {
            GameEvents.TreasureCollected -= OnTreasureCollected;
        }

        /// <summary>Wire the district, art library, and tuning. Null-tolerant.</summary>
        public void Configure(TownArea area, PlaceholderLibrary library, GameConfig config)
        {
            _area = area;
            _library = library;
            _config = config;
        }

        private void OnTreasureCollected(int total)
        {
            TryStartBuild();
        }

        /// <summary>Driven by <see cref="GameManager"/> every frame. Starts the next build
        /// when affordable and a plot is free, then advances the active site via its crew.</summary>
        public void Tick(float dt)
        {
            if (_config == null || _area == null)
            {
                return;
            }

            if (_activeSite == null)
            {
                TryStartBuild();
                return;
            }

            TickActiveSite(dt);
        }

        // ----------------------------------------------------------- build queue

        private void TryStartBuild()
        {
            if (_activeSite != null || _area == null || _config == null)
            {
                return;
            }

            GameManager gm = GameManager.Instance;
            if (gm == null)
            {
                return;
            }

            if (_nextIndex >= _area.PlotCount)
            {
                return; // no free plot: the whole curated queue is built out
            }

            int price = _config.TownBuildingPrice(_nextIndex);
            if (gm.TownWallet < price)
            {
                return; // can't afford the next building yet
            }

            if (!gm.TownTrySpend(price))
            {
                return; // deduction failed (save-written spend is the single money gate)
            }

            _activeSite = CreateBuildingObject(_nextIndex, 0, 0f);
            _activeIndex = _nextIndex;
            _workPuffTimer = 0f;
            GameEvents.RaiseTownBuildStarted(_activeIndex);
            gm.TownPersist(); // capture the freshly broken-ground site (state 0) in the save
            // The crew joins over the next few ticks (TickActiveSite drafts them).
        }

        /// <summary>Spawn one building GameObject at the plot for <paramref name="index"/>,
        /// wired to its renderer + crumb particles and initialised to the given construction
        /// state / banked partial. Shared by a fresh break-ground (state 0) and a reload
        /// (<see cref="RestoreFromSave"/>), so both paths build identical sites.</summary>
        private BuildingController CreateBuildingObject(int index, int initialState, float initialWorked)
        {
            GameManager gm = GameManager.Instance;
            Vector3 plot = _area.PlotWorld(index);

            var go = new GameObject($"Building_{index}");
            go.transform.SetParent(transform, false);
            go.transform.position = plot;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 12; // sits among overworld props

            var building = go.AddComponent<BuildingController>();
            ParticleSystem crumbs = gm != null
                ? gm.TownCreateParticles(go.transform,
                    _library != null ? _library.CrumbParticle : null,
                    new Color(0.78f, 0.62f, 0.42f), 0.3f)
                : null;
            building.Init(_library, _config, sr, crumbs, initialState, initialWorked);

            // Fruit Stand identity: the stand plot gets a warm tint + a bobbing fruit sign
            // once it finishes (deferred inside BuildingController until IsFinished). Reuses
            // an existing fruit sprite — zero new hand-made art. Null-tolerant.
            if (index == GameConfig.FruitStandIndex && _library != null)
            {
                building.MarkFruitStand(_library.Fruit(0));
            }

            return building;
        }

        // ---------------------------------------------------------- active site

        private void TickActiveSite(float dt)
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || _activeSite == null)
            {
                return;
            }

            ManageBuilders(gm);

            int working = 0;
            for (int i = 0; i < _builders.Count; i++)
            {
                if (_builders[i] != null && _builders[i].IsWorking)
                {
                    working++;
                }
            }

            if (working <= 0)
            {
                return; // no crew on site: construction WAITS (never drafts buddies/player)
            }

            int before = _activeSite.State;
            _activeSite.AddWork(dt * working);

            // Puff dust/crumbs at the site while the crew hammers.
            _workPuffTimer -= dt;
            if (_workPuffTimer <= 0f)
            {
                _workPuffTimer = 0.5f;
                _activeSite.EmitWorkPuff();
            }

            // Announce every state boundary crossed this tick (finish included), so a
            // single big step still fires the full 1->2->3->finished sequence.
            for (int st = before + 1; st <= _activeSite.State; st++)
            {
                if (st >= BuildingController.ConstructionStates)
                {
                    FinishSite(gm);
                    break;
                }

                GameEvents.RaiseBuildingStateAdvanced(st);
            }

            // Persist the new construction state whenever a boundary was crossed (the
            // finished case already persisted inside FinishSite). Only on boundaries, so
            // the mid-state partial isn't written to disk every frame.
            if (_activeSite != null && _activeSite.State != before)
            {
                gm.TownPersist();
            }
        }

        private void ManageBuilders(GameManager gm)
        {
            // Drop builders that vanished or got promoted to buddy (a player tap-to-swap
            // pulls a resident onto the walk — the town lets it go and re-drafts).
            for (int i = _builders.Count - 1; i >= 0; i--)
            {
                DinoController d = _builders[i];
                if (d == null || d.IsBuddy)
                {
                    _builders.RemoveAt(i);
                }
            }

            // Re-issue the commute for any assigned builder that settled without arriving
            // (e.g. bumped out of its walk); a working or still-commuting builder is left alone.
            for (int i = 0; i < _builders.Count; i++)
            {
                DinoController d = _builders[i];
                if (d != null && !d.IsWorking && !d.IsBusy)
                {
                    SendToWork(d);
                }
            }

            // Draft more residents up to the cap. TownAcquireBuilders returns NON-BUDDY
            // residents only — the backhoe/player and walk buddies can never appear here.
            // Over-request (a commuting builder is not "working" yet, so the pool can
            // still contain one already on our list); we skip those and take fresh ones.
            int max = Mathf.Max(0, _config != null ? _config.TownMaxBuilders : 2);
            if (_builders.Count < max)
            {
                List<DinoController> pool = gm.TownAcquireBuilders(max + _builders.Count);
                for (int i = 0; i < pool.Count && _builders.Count < max; i++)
                {
                    DinoController d = pool[i];
                    if (d == null || _builders.Contains(d))
                    {
                        continue;
                    }

                    _builders.Add(d);
                    SendToWork(d);
                }
            }
        }

        private void SendToWork(DinoController d)
        {
            if (d == null || _area == null)
            {
                return;
            }

            float speed = _config != null ? _config.TownBuilderCommuteSpeed : 1.1f;
            int slot = _builders.IndexOf(d);
            Vector3 stand = _area.StandWorld(_activeIndex, Mathf.Max(0, slot));
            // Pass the plot center (so the builder holds its mallet toward the structure)
            // and the art library (so it can "put on" the hard hat). Both null-tolerant.
            Vector3 building = _area.PlotWorld(_activeIndex);
            d.GoWork(stand, building, speed, null, _library);
        }

        private void FinishSite(GameManager gm)
        {
            int finishedIndex = _activeIndex;

            if (_activeSite != null)
            {
                gm.TownSpawnConfetti(_activeSite.transform.position + new Vector3(0f, 0.5f, 0f));
            }

            gm.Audio?.Grow(); // completion sting
            GameEvents.RaiseBuildingFinished(finishedIndex);

            // Crew celebrates (dance) then trots home; the finished building stays put
            // showing its finished state. Buddies/player were never involved.
            for (int i = 0; i < _builders.Count; i++)
            {
                _builders[i]?.StopWork(celebrate: true);
            }

            _builders.Clear();
            _activeSite = null;
            _activeIndex = -1;
            _nextIndex++; // curated order advances to the next building/plot
            gm.TownPersist(); // the finished building + advanced queue index land in the save
        }

        // -------------------------------------------------------------- persistence

        /// <summary>Write the town's build state into <paramref name="data"/> (save schema
        /// v4): the queue index plus one <see cref="TownBuildingSave"/> per building in
        /// order — the first <see cref="_nextIndex"/> finished, then the in-progress site
        /// (if any). Called by GameManager.SaveNow so every save captures the live town.</summary>
        public void WriteSave(SaveData data)
        {
            if (data == null)
            {
                return;
            }

            data.TownNextIndex = _nextIndex;
            if (data.TownBuildings == null)
            {
                data.TownBuildings = new List<TownBuildingSave>();
            }

            data.TownBuildings.Clear();

            // Finished buildings occupy plots 0.._nextIndex-1.
            for (int i = 0; i < _nextIndex; i++)
            {
                data.TownBuildings.Add(new TownBuildingSave
                {
                    Finished = true,
                    State = BuildingController.ConstructionStates,
                    Worked = 0f,
                });
            }

            // The one site still under construction (if any) sits at plot _nextIndex.
            if (_activeSite != null)
            {
                data.TownBuildings.Add(new TownBuildingSave
                {
                    Finished = false,
                    State = _activeSite.State,
                    Worked = _activeSite.WorkedPartial,
                });
            }
        }

        /// <summary>Rebuild the town from <paramref name="data"/> on load: finished
        /// buildings reappear finished (no crew, no confetti), a partially-built site is
        /// restored to its construction state + banked work and resumes as the active site
        /// (the crew clocks back in on the next tick), and the queue continues from the
        /// saved index. A v3 (or earlier) save has no town fields, so the town stays empty.</summary>
        public void RestoreFromSave(SaveData data)
        {
            if (_area == null || _config == null || data == null)
            {
                return;
            }

            ClearAllSites(); // defensive: Start runs on a fresh town, but never double-place

            int plots = _area.PlotCount;
            _nextIndex = Mathf.Clamp(data.TownNextIndex, 0, plots);

            List<TownBuildingSave> list = data.TownBuildings;
            if (list == null)
            {
                return;
            }

            for (int i = 0; i < list.Count && i < plots; i++)
            {
                TownBuildingSave b = list[i];
                if (b == null)
                {
                    continue;
                }

                if (b.Finished)
                {
                    CreateBuildingObject(i, BuildingController.ConstructionStates, 0f);
                }
                else
                {
                    // Resume the in-progress site: restored state + banked partial, made
                    // active so TickActiveSite re-drafts a crew and finishes it off.
                    _activeSite = CreateBuildingObject(i,
                        Mathf.Clamp(b.State, 0, BuildingController.ConstructionStates - 1),
                        Mathf.Max(0f, b.Worked));
                    _activeIndex = i;
                    _workPuffTimer = 0f;
                }
            }
        }

        /// <summary>Destroy every placed building (in-progress + finished) and clear the
        /// active-site pointers. Shared by <see cref="RestoreFromSave"/> and the test reset.</summary>
        private void ClearAllSites()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform c = transform.GetChild(i);
                if (c != null && c.GetComponent<BuildingController>() != null)
                {
                    Destroy(c.gameObject);
                }
            }

            _activeSite = null;
            _activeIndex = -1;
        }

        // ------------------------------------------------------------ test reset

        /// <summary>TEST HOOK. Clear all town state between integration cases: send any
        /// crew home, destroy every site (in-progress and finished), and rewind the queue.
        /// Called from <see cref="GameManager.TestReset"/> so a reset wipes the town cleanly.</summary>
        internal void TestResetTown()
        {
            for (int i = 0; i < _builders.Count; i++)
            {
                _builders[i]?.StopWork(celebrate: false);
            }

            _builders.Clear();

            ClearAllSites();
            _nextIndex = 0;
            _workPuffTimer = 0f;
        }
    }
}
