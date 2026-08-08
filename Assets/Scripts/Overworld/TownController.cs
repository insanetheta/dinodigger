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
    /// in, work the site through its states, celebrate, and trot home. Each builder's
    /// contribution is scaled by how GROWN it is (DinoDigger-s90 — see
    /// <see cref="CrewWorkRate"/>), so feeding the meadow visibly raises the town faster.
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
    ///
    /// CONSTRUCTION ONLY. What a FINISHED building does — the townsfolk who drop by to slide,
    /// sip, soak and splash — belongs to the sibling <see cref="TownLifeController"/> this
    /// class ensures and ticks but never reaches into (DinoDigger-3pz).
    ///
    /// THE JOY PASS (Phase 3) hangs two player-facing beats off that spine, both additive and
    /// both incapable of taking control of anything: tapping the ACTIVE site cheers the crew on
    /// for a short, NON-STACKING speed burst (<see cref="OnSiteCheered"/>, DinoDigger-5y9), and
    /// topping a building out plays a choreographed celebration — confetti, a shared arms-up
    /// cheer, hard hats in the air, then the building's DEBUT interaction
    /// (<see cref="PlayCompletionChoreography"/>, DinoDigger-0gd).
    /// </summary>
    public class TownController : MonoBehaviour
    {
        // Completion choreography timing (DinoDigger-0gd), in seconds of game time from the
        // moment the last state is worked through:
        //   0.00  confetti over the site; the crew AND every dino within CelebrationCheerRadius
        //         throw an arms-up hop (0.45s, RestingScale-safe — see DinoController.CheerHop)
        //   0.00  each crew hard hat pops off and arcs up-and-out, fading over HatTossSeconds
        //   0.00  the crew's own StopWork(celebrate:true) dance starts (0.8s) and then walks it
        //         home — untouched, because the stranded-builder guard depends on it
        //   1.50  the finished building's DEBUT interaction plays once, handed to town life
        // The debut is deliberately LAST and independent: the cheer reads as "we did it", the
        // debut as "and look, someone's already using it".
        private const float CelebrationCheerRadius = 3f;
        private const float HatTossSeconds = 1f;
        private const float DebutVisitDelay = 1.5f;

        [SerializeField] private TownArea _area;
        [SerializeField] private PlaceholderLibrary _library;
        [SerializeField] private GameConfig _config;

        // Ambient town LIFE (DinoDigger-3pz), a separate concern living on the same root: once
        // a building is finished, residents drop by to play its little scene. This controller
        // owns nothing about those visits beyond the component's lifetime + tick — see
        // TownLifeController. Ensured in Configure, so the built scene and the test rig alike
        // always have one.
        [SerializeField] private TownLifeController _life;

        // Curated order: _nextIndex is both the next building AND its plot slot.
        private int _nextIndex;
        private BuildingController _activeSite;
        private int _activeIndex = -1;
        private readonly List<DinoController> _builders = new List<DinoController>();
        private float _workPuffTimer;
        // Test-observable build-work accrual (DinoDigger-s90). Both counters advance in the
        // SAME tick, so _workBanked / _workElapsed is an exact crew work-rate with no clock.
        private float _workBanked;  // crew work-seconds banked into the active site
        private float _workElapsed; // real seconds ticked while a crew was on that site

        // Tap-to-cheer (DinoDigger-5y9): tapping the ACTIVE construction site cheers the crew on
        // and they work faster for a few seconds. NON-STACKING BY CONSTRUCTION — a re-tap
        // ASSIGNS this timer (never adds to it) and the multiplier is applied once, so a toddler
        // hammering the site gets the same generous 2x, just for longer. Never saved: a reload
        // comes back to a calm site.
        private float _cheerTimer;
        private int _cheerTaps;             // test-observable: cheers accepted

        // Completion choreography (DinoDigger-0gd): tallies for the integration case plus the
        // throwaway hard-hat props currently arcing through the air (dropped on a town reset so
        // no case ever inherits the previous one's celebration).
        private int _hatsTossed;
        private int _celebrationCheers;
        private readonly List<GameObject> _tossedHats = new List<GameObject>();

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

        /// <summary>How many buildings are FINISHED (they occupy plots 0..count-1, in curated
        /// build order). The ambient <see cref="TownLifeController"/> picks its visit targets
        /// from this range; zero means the plaza is still an empty lot and nothing is alive
        /// yet.</summary>
        public int FinishedBuildingCount => _nextIndex;

        /// <summary>True when the town has something worth showing off — a finished building or
        /// a site currently going up. The idle-attract camera tour (DinoDigger-sbc) asks this
        /// before framing the district, so an empty lot keeps the plain honk + mound pulse.</summary>
        public bool HasVisibleTown => _nextIndex > 0 || _activeSite != null;

        /// <summary>Where the attract tour points the camera: the ACTIVE construction site when
        /// one exists (the liveliest thing in town — a crew is hammering there), else the plot of
        /// the most recently FINISHED building (its townsfolk scene may well be playing), else
        /// the district centre. Always inside <see cref="TownArea.ContainsWorld"/>, so the tour
        /// frames the plaza wherever the roster has got to.</summary>
        public Vector3 AttractFocusPoint
        {
            get
            {
                if (_area == null)
                {
                    return transform.position;
                }

                if (_activeIndex >= 0)
                {
                    return _area.PlotWorld(_activeIndex);
                }

                return _nextIndex > 0 ? _area.PlotWorld(_nextIndex - 1) : _area.Center;
            }
        }

        /// <summary>World position of the plot for the building at <paramref name="index"/>
        /// (the drop-off point the Fruit Stand sell flow walks fruit to). Null-tolerant.</summary>
        public Vector3 BuildingWorld(int index) =>
            _area != null ? _area.PlotWorld(index) : transform.position;

        /// <summary>True when a world point sits on — or within <paramref name="clearance"/>
        /// world units of — a plot that already carries a building (finished, or the site
        /// currently under construction). Mound respawns steer clear of these (DinoDigger-lie):
        /// buildings import ~2.2 units wide while the cleared district is measured in CELLS,
        /// so a mound one cell outside the district could still clip a building's tap collider
        /// and make a tap in the overlap ambiguous. Null-tolerant (no area = nothing built).</summary>
        public bool NearBuiltPlot(Vector3 world, float clearance)
        {
            if (_area == null)
            {
                return false;
            }

            float clearSq = clearance * clearance;
            for (int i = 0; i < _area.PlotCount; i++)
            {
                if (i >= _nextIndex && i != _activeIndex)
                {
                    continue; // empty lot: nothing to clip
                }

                Vector3 plot = _area.PlotWorld(i);
                plot.z = world.z;
                if ((plot - world).sqrMagnitude < clearSq)
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------ builder snack
        // Snack-powered building (DinoDigger-4yu): feeding a fruit to a builder standing on an
        // active site banks a chunk of build work so the building visibly jumps ahead. The feed
        // path (GameManager.TrySnackBuilder) aims the fruit at FirstWorkingBuilder and, on arrival,
        // calls BankBuilderSnack; the glut guard uses HasWorkingBuilderOnSite as a fruit-demand sink.

        /// <summary>The first drafted builder currently WORKING on the active site — physically on
        /// site (its <see cref="DinoController.IsWorking"/> is true), NOT merely commuting — or null
        /// when no site is active or nobody has clocked in yet. The builder-snack feed path aims the
        /// tapped fruit at this worker.</summary>
        public DinoController FirstWorkingBuilder()
        {
            if (_activeSite == null)
            {
                return null;
            }

            for (int i = 0; i < _builders.Count; i++)
            {
                if (_builders[i] != null && _builders[i].IsWorking)
                {
                    return _builders[i];
                }
            }

            return null;
        }

        /// <summary>True when a building is under construction AND at least one builder is physically
        /// on site working — the guard for both the builder-snack feed path and the fruit glut-guard's
        /// third demand sink.</summary>
        public bool HasWorkingBuilderOnSite() => FirstWorkingBuilder() != null;

        /// <summary>Snack payoff: bank a chunk of BONUS build work at the active site —
        /// <see cref="GameConfig.SnackWorkSeconds"/>, one construction state by default — when a fruit
        /// is fed to a builder on site. The building jumps ahead immediately: any state boundaries
        /// crossed fire the SAME 1->2->3->finished events + save as normal crew accrual (AddWork
        /// carries any remainder forward), and a crumb + confetti pop makes the jump read. No-op unless
        /// a crew member is actually working (the feed path guards this too).</summary>
        public void BankBuilderSnack()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || _activeSite == null || !HasWorkingBuilderOnSite())
            {
                return;
            }

            BuildingController site = _activeSite;
            Vector3 sitePos = site.transform.position;

            float seconds = _config != null ? Mathf.Max(0f, _config.SnackWorkSeconds) : 8f;
            int before = site.State;
            site.AddWork(seconds); // carries the remainder forward exactly like per-frame accrual

            // Announce every state boundary crossed (finish included), mirroring TickActiveSite so a
            // snack that lands mid-state still fires the full 1->2->3->finished sequence.
            for (int st = before + 1; st <= site.State; st++)
            {
                if (st >= BuildingController.ConstructionStates)
                {
                    FinishSite(gm);
                    break;
                }

                GameEvents.RaiseBuildingStateAdvanced(st);
            }

            // Persist the new state whenever a boundary was crossed (the finished case already
            // persisted inside FinishSite, which cleared _activeSite — so this won't double-write).
            if (_activeSite != null && site.State != before)
            {
                gm.TownPersist();
            }

            // Payoff FX at the site so the jump reads even when the snack stayed within one state.
            site.EmitWorkPuff();
            gm.TownSpawnConfetti(sitePos + new Vector3(0f, 0.5f, 0f));
        }

        // TEST HOOKS (integration runner; no reflection).

        /// <summary>TEST HOOK. While true the queue never breaks ground, so the wallet is
        /// FROZEN for the caller. Count-exact treasure cases pin this: the builder spends the
        /// instant it can afford the next plot — inside the very frame a coin banks — which
        /// makes an "exact wallet value" wait miss its target and hang. Default false = the
        /// always-on builder of normal play. Always restore it in a finally.</summary>
        internal static bool TestSuspendBuilds;

        internal TownArea TestArea => _area;
        internal BuildingController TestActiveSite => _activeSite;
        internal int TestNextIndex => _nextIndex;
        internal int TestBuilderCount => _builders.Count;
        internal IReadOnlyList<DinoController> TestBuilders => _builders;
        internal TownLifeController TestLife => _life;
        internal int TestRecessCount => _recesses.Count;
        internal int TestRecessTapFeedback => _recessTapFeedback;

        /// <summary>Tap-to-cheer state (DinoDigger-5y9): how many cheers have been accepted and
        /// how much of the current burst is left. The remaining time can never exceed
        /// <see cref="GameConfig.TownCheerSeconds"/> — that IS the non-stacking rule, observable.</summary>
        internal int TestCheerTaps => _cheerTaps;
        internal float TestCheerSecondsLeft => _cheerTimer;
        internal bool TestCheerActive => _cheerTimer > 0f;

        /// <summary>The multiplier the crew is banking work at RIGHT NOW (1 when no cheer is
        /// running). Lets a case assert the burst without inferring it from a rate.</summary>
        internal float TestCheerMultiplier => CheerMultiplier;

        /// <summary>Completion choreography tallies (DinoDigger-0gd): hard hats tossed and
        /// celebration hops played, cumulative since the last town reset.</summary>
        internal int TestHatsTossed => _hatsTossed;
        internal int TestCelebrationCheers => _celebrationCheers;
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

        /// <summary>Cumulative crew work-seconds banked into the active site by
        /// <see cref="TickActiveSite"/> since the last town reset (DinoDigger-s90).</summary>
        internal float TestWorkBanked => _workBanked;

        /// <summary>Cumulative REAL seconds ticked while a crew was on site, over the same window
        /// as <see cref="TestWorkBanked"/>. Both advance inside one tick, so their delta ratio is
        /// the crew's exact work rate — BigDinoBuildsFaster compares growth-stage crews that way
        /// instead of racing two builds against the wall clock.</summary>
        internal float TestWorkElapsed => _workElapsed;

        private void Awake()
        {
            // Self-heal: a scene SAVED before the town-life service existed has no
            // TownLifeController serialized on this root, and nothing calls Configure at
            // runtime. Ensuring it here means ambient life comes up from the serialized
            // wiring alone — no scene rebuild required.
            EnsureLife();
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

        /// <summary>Wire the district, art library, and tuning. Null-tolerant. Also ensures the
        /// sibling <see cref="TownLifeController"/> (ambient townsfolk visits) exists and is
        /// wired with the same district/art/tuning — construction and life stay separate
        /// classes, but one call site wires both.</summary>
        public void Configure(TownArea area, PlaceholderLibrary library, GameConfig config)
        {
            _area = area;
            _library = library;
            _config = config;
            EnsureLife();
        }

        /// <summary>Ensure the sibling ambient-life service exists on this root and is wired to
        /// the same district/art/tuning. Idempotent — safe from both <see cref="Configure"/>
        /// (SceneBuilder, the test rig) and <see cref="Awake"/> (a scene serialized before the
        /// service existed).</summary>
        private void EnsureLife()
        {
            if (_life == null)
            {
                _life = GetComponent<TownLifeController>();
            }

            if (_life == null)
            {
                _life = gameObject.AddComponent<TownLifeController>();
            }

            _life.Configure(_area, this, _library, _config);
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

            // The cheer burst counts down every frame, whatever the site is doing — so a cheer
            // whose building finishes (or gets torn down) mid-burst still expires on schedule
            // instead of leaking into the next site.
            if (_cheerTimer > 0f)
            {
                _cheerTimer = Mathf.Max(0f, _cheerTimer - dt);
            }

            // Recess parties run independently of the build queue (they use free residents,
            // never builders), so advance them every frame regardless of build state.
            TickRecesses(dt);

            // Ambient life does too: townsfolk visit FINISHED buildings while the next one
            // goes up. A visitor is freely draftable, so this can never starve a build.
            _life?.Tick(dt);

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
            if (_activeSite != null || _area == null || _config == null || TestSuspendBuilds)
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
            // Per-building art (DinoDigger-ggy), picked by BUILD-ORDER INDEX: each plot raises
            // its OWN structure. Null (art not generated yet) or a partial set degrades to the
            // generic BuildingStates placeholder inside BuildingController, state by state.
            BuildingArt art = _library != null ? _library.TownBuilding(index) : null;
            building.Init(_library, _config, sr, crumbs, initialState, initialWorked, art);

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
            building.WireTown(this, index);

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

            float rate = CrewWorkRate();
            if (rate <= 0f)
            {
                return; // no crew on site: construction WAITS (never drafts buddies/player)
            }

            float banked = dt * rate;
            _workBanked += banked;
            _workElapsed += dt;

            int before = _activeSite.State;
            _activeSite.AddWork(banked);

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

        /// <summary>Build-work SECONDS this crew banks per real second (DinoDigger-s90). Only
        /// builders physically ON SITE count, and each contributes its growth-stage multiplier
        /// (<see cref="GameConfig.BuildSpeedFor"/>: Baby x1.0, Kid x1.6, Big x2.5) instead of a
        /// flat 1 — so feeding the meadow visibly speeds the town up. Zero means the site waits.
        ///
        /// The whole crew total is then scaled by <see cref="CheerMultiplier"/> (DinoDigger-5y9),
        /// which is 1 unless the player has just cheered the site on. Applying the cheer HERE, to
        /// the summed rate, is what makes it non-stacking: it is a property of the moment, not
        /// something banked per tap.</summary>
        private float CrewWorkRate()
        {
            float rate = 0f;
            for (int i = 0; i < _builders.Count; i++)
            {
                DinoController d = _builders[i];
                if (d != null && d.IsWorking)
                {
                    rate += _config != null ? _config.BuildSpeedFor(d.Stage) : 1f;
                }
            }

            return rate * CheerMultiplier;
        }

        /// <summary>How much faster the crew is working right now: the configured cheer
        /// multiplier while a burst is running, else 1. Clamped to >= 1 so a mis-set config can
        /// only ever fail to help, never SLOW the town down.</summary>
        private float CheerMultiplier =>
            _cheerTimer > 0f && _config != null ? Mathf.Max(1f, _config.TownCheerMultiplier) : 1f;

        // -------------------------------------------------------------- tap to cheer

        /// <summary>The ACTIVE construction site was tapped (routed here by
        /// <see cref="BuildingController"/>): the player is cheering the crew on (DinoDigger-5y9).
        ///
        /// EVERY tap responds — the site bounces, confetti pops, dust puffs off the scaffolding,
        /// a chime plays and every builder throws a happy hop — even when the burst is already
        /// running and even with nobody on site yet. What a tap does NOT do is stack: the burst
        /// timer is ASSIGNED (never added to), so ten taps in a row buy the same
        /// <see cref="GameConfig.TownCheerMultiplier"/>, refreshed to a full
        /// <see cref="GameConfig.TownCheerSeconds"/>, and never a compounding one.
        ///
        /// The player is not drafted by any of this: the burst only scales what the RESIDENT
        /// crew is already banking (see <see cref="CrewWorkRate"/>), so a cheer with an empty
        /// site is pure fireworks and moves the build not at all.
        ///
        /// <paramref name="index"/> is the tapped plot, carried for symmetry with
        /// <see cref="OnBuildingTapped"/> (and for a future per-building cheer); the burst itself
        /// belongs to whatever site is active, of which there is only ever one.</summary>
        internal void OnSiteCheered(BuildingController site, int index)
        {
            _cheerTaps++;

            GameManager gm = GameManager.Instance;
            if (site != null)
            {
                Tween.PunchScale(site.transform, 0.16f, 0.3f); // re-bounces on every tap
                site.EmitWorkPuff();                           // dust off the scaffolding
                gm?.TownSpawnConfetti(site.transform.position + new Vector3(0f, 0.5f, 0f));
            }

            gm?.Audio?.Chime();

            // Refresh, never accumulate — this single assignment IS the non-stacking rule.
            _cheerTimer = _config != null ? Mathf.Max(0f, _config.TownCheerSeconds) : 3f;

            // The crew bounces back. CheerHop claims nothing and changes no mode, so a builder
            // hops WHILE it keeps hammering (and a still-commuting one hops mid-walk).
            for (int i = 0; i < _builders.Count; i++)
            {
                _builders[i]?.CheerHop(0.26f, 0.35f);
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
            Vector3 sitePos = _activeSite != null
                ? _activeSite.transform.position
                : (_area != null ? _area.PlotWorld(Mathf.Max(0, finishedIndex)) : transform.position);

            gm.Audio?.Grow(); // completion sting
            GameEvents.RaiseBuildingFinished(finishedIndex);

            // The party (DinoDigger-0gd) runs BEFORE the crew is stood down, because it needs
            // the crew: it tosses their hats and it needs to know where they are standing.
            PlayCompletionChoreography(gm, sitePos);

            // Crew celebrates (dance) then trots home; the finished building stays put
            // showing its finished state. Buddies/player were never involved. UNCHANGED by the
            // choreography on purpose — every builder is stood down in the same frame the
            // building tops out, which is what keeps a commuting builder from being stranded.
            for (int i = 0; i < _builders.Count; i++)
            {
                _builders[i]?.StopWork(celebrate: true);
            }

            _builders.Clear();
            _activeSite = null;
            _activeIndex = -1;
            _cheerTimer = 0f; // a cheer never carries over to the NEXT building
            _nextIndex++;     // curated order advances to the next building/plot
            gm.TownPersist(); // the finished building + advanced queue index land in the save

            // ...and a beat later the building opens for business: its townsfolk scene plays
            // once as a debut. Independent of the crew's walk home — the two overlap, which is
            // the point (the builders wander off while the first customer arrives).
            Tween.After(DebutVisitDelay, () => PlayDebut(finishedIndex));
        }

        // ------------------------------------------------- completion choreography

        /// <summary>The "we built it!" beat (DinoDigger-0gd): confetti over the site, an arms-up
        /// hop from the crew AND from every dino close enough to have watched it go up, and the
        /// crew's hard hats popping off in little arcs.
        ///
        /// All decoration, no claims: <see cref="DinoController.CheerHop"/> changes no mode and
        /// the tossed hats are throwaway props, so nothing here can delay the walk home, poach a
        /// dino from town life, or strand a pose. Every piece is null-tolerant, so a
        /// placeholder-only run simply celebrates with fewer sprites.</summary>
        private void PlayCompletionChoreography(GameManager gm, Vector3 sitePos)
        {
            if (gm == null)
            {
                return;
            }

            gm.TownSpawnConfetti(sitePos + new Vector3(0f, 0.5f, 0f));

            // The crew's hats fly off. The REAL overlay hats are not touched — they hide by
            // themselves the moment StopWork drops the assignment (gear visibility is derived
            // from mode), so the throwaway props read as exactly those hats leaving.
            for (int i = 0; i < _builders.Count; i++)
            {
                TossHardHat(_builders[i], i);
            }

            // Everyone in earshot cheers — the crew plus any resident/buddy standing nearby
            // (an ambient VISITOR is skipped inside CheerHop; its pose belongs to town life).
            List<DinoController> nearby = gm.TownDinosNear(sitePos, CelebrationCheerRadius);
            for (int i = 0; i < nearby.Count; i++)
            {
                if (nearby[i] == null)
                {
                    continue;
                }

                nearby[i].CheerHop(0.32f, 0.45f);
                _celebrationCheers++;
            }
        }

        /// <summary>Pop ONE throwaway hard hat off a builder's head: it arcs up and away from the
        /// site, tumbling and fading, and destroys itself after <see cref="HatTossSeconds"/>.
        /// Sized from the sprite's own bounds so the art's PPU doesn't matter, and parented to
        /// the town root (never the dino — the hat is leaving, it must not ride the walk home).
        /// No-op without hat art.</summary>
        private void TossHardHat(DinoController builder, int slot)
        {
            Sprite hat = _library != null ? _library.HardHat : null;
            if (builder == null || hat == null)
            {
                return;
            }

            float h = hat.bounds.size.y;
            var go = new GameObject("TossedHardHat");
            go.transform.SetParent(transform, true);
            Vector3 from = builder.transform.position + new Vector3(0f, 0.55f, 0f);
            go.transform.position = from;
            go.transform.localScale = Vector3.one * (h > 0.0001f ? 0.4f / h : 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = hat;
            sr.sortingOrder = 40; // above the buildings, below the particle bursts

            _tossedHats.Add(go);
            _hatsTossed++;

            // Alternate the throw side so two hats never trace the same arc.
            float side = (slot % 2 == 0) ? 1f : -1f;
            Vector3 to = from + new Vector3(side * 0.9f, 0.25f, 0f);

            Tween.MoveArc(go.transform, from, to, 0.9f, HatTossSeconds, () =>
            {
                _tossedHats.Remove(go);
                if (go != null)
                {
                    Destroy(go);
                }
            });
            Tween.ShakeRotation(go.transform, 180f, HatTossSeconds, 2); // tumbling

            // Fade out over the same window so it vanishes rather than blinking away.
            Tween.Run(HatTossSeconds, t =>
            {
                if (sr != null)
                {
                    Color c = sr.color;
                    c.a = 1f - t;
                    sr.color = c;
                }
            });
        }

        /// <summary>The finished building's DEBUT: its townsfolk interaction plays once, right
        /// after the completion beat, so the very first thing the new building does is be USED.
        /// Routed through the ambient service's own entry point
        /// (<see cref="TownLifeController.PlayDebutVisit"/>), so a debut is an ordinary visit in
        /// every respect — same eligibility, same yielding, same cleanup.
        ///
        /// Best-effort and single-shot ON PURPOSE: with everybody still dancing or walking home
        /// there may be nobody free, and the right answer then is "no debut", not "chase the
        /// crew". Retrying would risk pulling a builder back out of the meadow it was walking to,
        /// which is exactly what the stranded-builder guard watches for.</summary>
        private void PlayDebut(int index)
        {
            if (_life == null || !IsBuildingFinished(index))
            {
                return; // torn down / reset between the finish and this callback
            }

            _life.PlayDebutVisit(index);
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

            // Tap-to-cheer + the completion party are transient too: end any running burst and
            // destroy the hats still in the air, so the next case starts on a quiet plaza.
            _cheerTimer = 0f;
            _cheerTaps = 0;
            _hatsTossed = 0;
            _celebrationCheers = 0;
            for (int i = 0; i < _tossedHats.Count; i++)
            {
                if (_tossedHats[i] != null)
                {
                    Destroy(_tossedHats[i]);
                }
            }

            _tossedHats.Clear();

            // Ambient visits are transient too (never saved): send every visitor home, destroy
            // its props, and rewind the life tallies so the next case starts on a quiet plaza.
            _life?.TestResetLife();

            ClearAllSites();
            _nextIndex = 0;
            _workPuffTimer = 0f;
            _workBanked = 0f;
            _workElapsed = 0f;
        }
    }
}
