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

        // Recess Time (DinoDigger-x07): transient dino parties thrown by tapping a FINISHED
        // building. One recess per building at a time; multiple different buildings CAN party
        // simultaneously (recruitment naturally de-dupes, since a party-goer is IsBusy while
        // commuting/orbiting and so is never re-recruited). NEVER saved — a reload comes back
        // to a calm town.
        private readonly List<Recess> _recesses = new List<Recess>();
        private int _recessTapFeedback; // test-observable: taps that fired instant feedback

        /// <summary>One running recess: the host building + its recruited party-goers, a run
        /// timer, and a spacing timer for the periodic star/confetti pops.</summary>
        private class Recess
        {
            public int Index;
            public BuildingController Building;
            public readonly List<DinoController> Dinos = new List<DinoController>();
            public float Elapsed;
            public float PopTimer;
        }

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
        internal int TestRecessCount => _recesses.Count;
        internal int TestRecessTapFeedback => _recessTapFeedback;
        internal bool TestIsRecessRunning(int index) => IsRecessRunning(index);
        internal int TestRecessDinoTotal
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _recesses.Count; i++)
                {
                    n += _recesses[i] != null ? _recesses[i].Dinos.Count : 0;
                }

                return n;
            }
        }

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

            // Recess parties run independently of the build queue (they use free residents,
            // never builders), so advance them every frame regardless of build state.
            TickRecesses(dt);

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

            // Recess Time (DinoDigger-x07): hand the building its owning town + build-order
            // index so a tap on the FINISHED building can reach the recess flow (the building
            // installs its own tap collider only once IsFinished).
            building.WireRecess(this, index);

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

        // ------------------------------------------------------------ recess time

        /// <summary>A FINISHED building was tapped (routed here by <see cref="BuildingController"/>).
        /// EVERY tap gives instant feedback — a squash-and-stretch bounce, a cheerful chime, and
        /// a small confetti pop — even if no dinos are free and even mid-party (the toddler rule:
        /// every tap responds). Then, if no recess is already running on THIS building, recruit
        /// 2..RecessMaxDinos free residents to trot over and throw a ~RecessSeconds party.</summary>
        internal void OnBuildingTapped(BuildingController building, int index)
        {
            _recessTapFeedback++;

            GameManager gm = GameManager.Instance;
            if (building != null)
            {
                Tween.PunchScale(building.transform, 0.18f, 0.35f); // re-bounces on every tap
                gm?.TownSpawnConfetti(building.transform.position + new Vector3(0f, 0.5f, 0f));
            }

            gm?.Audio?.Chime();

            // One recess per building at a time: a tap during a running party is just feedback.
            if (IsRecessRunning(index))
            {
                return;
            }

            StartRecess(building, index);
        }

        private bool IsRecessRunning(int index)
        {
            for (int i = 0; i < _recesses.Count; i++)
            {
                if (_recesses[i] != null && _recesses[i].Index == index)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Recruit up to <see cref="GameConfig.RecessMaxDinos"/> eligible residents (same
        /// pool the builder draft/seller pick uses — non-buddy, not the ceremony dino, not busy,
        /// not a seller, and NOT a builder on an active site, since a commuting/working builder
        /// reads as busy), trot them to the building, then orbit-party it with staggered phases.
        /// With zero free dinos the tap already gave full feedback — nothing else happens.</summary>
        private void StartRecess(BuildingController building, int index)
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || _area == null || building == null)
            {
                return;
            }

            int max = Mathf.Max(1, _config != null ? _config.RecessMaxDinos : 4);
            List<DinoController> goers = gm.TownAcquireRecessGoers(max);
            if (goers.Count == 0)
            {
                return; // nobody free: the bounce/chime/confetti was the whole reaction
            }

            var recess = new Recess { Index = index, Building = building, Elapsed = 0f, PopTimer = 0f };
            Vector3 center = _area.PlotWorld(index);
            float duration = _config != null ? Mathf.Max(1f, _config.RecessSeconds) : 15f;
            float speed = _config != null ? _config.TownBuilderCommuteSpeed : 1.1f;

            for (int i = 0; i < goers.Count; i++)
            {
                DinoController d = goers[i];
                if (d == null)
                {
                    continue;
                }

                recess.Dinos.Add(d);
                float phase = (i / (float)goers.Count) * Mathf.PI * 2f; // spread the ring out
                Vector3 stand = _area.StandWorld(index, i);
                // Trot over (builder commute speed), then orbit-party the plot for the recess.
                d.WalkTo(stand, speed, () =>
                {
                    if (d != null)
                    {
                        d.StartParade(center, phase, duration);
                    }
                });
            }

            _recesses.Add(recess);
            gm.Audio?.Grow(); // a little party-start sting
        }

        /// <summary>Advance every running recess: drop any party-goer that left (tapped into a
        /// buddy mid-party, or destroyed — mirrors the seller watchdog), pop the occasional
        /// star/confetti burst, and once the run timer elapses (or everyone left) end it with a
        /// final dance so the residents trot home and resume their meadow role on their own.</summary>
        private void TickRecesses(float dt)
        {
            if (_recesses.Count == 0)
            {
                return;
            }

            GameManager gm = GameManager.Instance;

            for (int r = _recesses.Count - 1; r >= 0; r--)
            {
                Recess rec = _recesses[r];
                if (rec == null || rec.Building == null)
                {
                    _recesses.RemoveAt(r);
                    continue;
                }

                // Watchdog: a party-goer promoted to buddy (tap-to-swap) or destroyed cleanly
                // leaves the party — it's no longer ours to orbit.
                for (int i = rec.Dinos.Count - 1; i >= 0; i--)
                {
                    DinoController d = rec.Dinos[i];
                    if (d == null || d.IsBuddy)
                    {
                        rec.Dinos.RemoveAt(i);
                    }
                }

                rec.Elapsed += dt;

                // Occasional star/confetti pops (with a soft chime) while the party runs.
                rec.PopTimer -= dt;
                if (rec.PopTimer <= 0f && gm != null)
                {
                    rec.PopTimer = 2f;
                    gm.TownSpawnConfetti(rec.Building.transform.position + new Vector3(0f, 0.6f, 0f));
                    gm.Audio?.Chime();
                }

                float duration = _config != null ? Mathf.Max(1f, _config.RecessSeconds) : 15f;
                if (rec.Elapsed >= duration || rec.Dinos.Count == 0)
                {
                    EndRecess(rec);
                    _recesses.RemoveAt(r);
                }
            }
        }

        /// <summary>End a recess: everyone does a final <see cref="DinoController.Dance"/> (which
        /// then resumes the resident role and trots home), plus one last confetti pop.</summary>
        private void EndRecess(Recess rec)
        {
            if (rec == null)
            {
                return;
            }

            for (int i = 0; i < rec.Dinos.Count; i++)
            {
                rec.Dinos[i]?.Dance(); // Dance -> ResumeRole -> walk back to the meadow
            }

            if (rec.Building != null)
            {
                GameManager.Instance?.TownSpawnConfetti(
                    rec.Building.transform.position + new Vector3(0f, 0.5f, 0f));
            }
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

            // Recess is transient (never saved): end any running party so its dinos stop
            // orbiting and resume their role, then forget them. GameManager.TestReset destroys
            // the dinos anyway; EndRecess keeps a stand-alone TestResetTown tidy too.
            for (int i = 0; i < _recesses.Count; i++)
            {
                EndRecess(_recesses[i]);
            }

            _recesses.Clear();
            _recessTapFeedback = 0;

            ClearAllSites();
            _nextIndex = 0;
            _workPuffTimer = 0f;
        }
    }
}
