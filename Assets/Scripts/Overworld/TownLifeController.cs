using System.Collections.Generic;
using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;

namespace DinoDigger.Overworld
{
    /// <summary>
    /// Dino Town AMBIENT LIFE (DinoDigger-3pz). A finished building is not a prop — it is
    /// ALIVE: every so often a free meadow resident strolls over to one and plays its tiny
    /// scene (whoosh down the playground slide, sip a coffee, soak in the tar-pit springs,
    /// toss a coin in the Fossil Fountain finale), then wanders home.
    ///
    /// SEPARATE CONCERN FROM CONSTRUCTION. <see cref="TownController"/> owns coins, plots,
    /// crews and progress; this service owns nothing but transient visits and never touches a
    /// building's state, the wallet, or the save (town life is pure decoration — Phase 3 owns
    /// persistence). It lives as a sibling component on the Town root, created + configured +
    /// ticked by TownController so both the built scene and the integration rig get one for
    /// free, and it reads the town through the small public surface
    /// (<see cref="TownController.FinishedBuildingCount"/> / <c>BuildingWorld</c>).
    ///
    /// HARD RULES, enforced structurally rather than by policing:
    ///   * Guests come ONLY from <see cref="GameManager.TownAcquireRecessGoers"/> — non-buddy,
    ///     non-busy, non-seller residents. The player backhoe is not a DinoController and a
    ///     walk buddy is filtered out twice (there, and again by
    ///     <see cref="DinoController.GoVisit"/> refusing a buddy outright).
    ///   * A visit YIELDS to everything. Construction drafts a visitor without asking
    ///     (<c>GoWork</c> drops the visit), a tap promotes it to buddy, a fruit sends it to
    ///     eat, the parade takes it — in every case <see cref="DinoController.IsOnVisit"/>
    ///     goes false and the next tick here retires the visit, restores its props and moves
    ///     on. Nothing here can ever block a build, a buddy swap, or the parade.
    ///
    /// The scenes are motion-only: existing walk animation, <see cref="Tween"/> helpers and
    /// EXISTING particle/prop sprites out of <see cref="PlaceholderLibrary"/>. No new art, and
    /// every sprite lookup is null-tolerant, so a placeholder-only run still reads as life
    /// (the dinos hop, sway, squash and bounce; only the little props go missing).
    /// </summary>
    public class TownLifeController : MonoBehaviour
    {
        // Guests wanted per building, indexed by CURATED BUILD ORDER. A visit runs with
        // whoever actually showed up (even one), so these are wishes, not requirements.
        //   0 Playground 2 (a queue for the slide)   1 Brew 1     2 Library 1
        //   3 Bijou 2 (a little crowd)               4 Bowling 1  5 Daycare 1
        //   6 Springs 1                              7 Grocer 1   8 Fountain 3 (finale)
        private static readonly int[] GuestsWanted = { 2, 1, 1, 2, 1, 1, 1, 1, 3 };

        // Beats in each building's loop. One beat == GameConfig.TownVisitBeatSeconds, so this
        // is also how LONG a visit lasts — the Fossil Fountain finale simply runs more beats.
        private static readonly int[] BeatsPerLoop = { 4, 3, 3, 4, 3, 4, 4, 4, 6 };

        // Curated build-order indices used by the scenes below (readability, not new data).
        private const int Playground = 0;
        private const int BoulderBrew = 1;
        private const int SlateLibrary = 2;
        private const int BedrockBijou = 3;
        private const int BoneanzaBowling = 4;
        private const int DinoDaycare = 5;
        private const int TarPitSprings = 6;
        private const int GronksGrocer = 7;
        private const int FossilFountain = 8;

        // A guest that never arrives (bumped out of its walk, pathing oddity) must not pin a
        // visit forever: give up on the commute after this long and retire the visit cleanly.
        private const float CommuteTimeoutSeconds = 30f;

        // How many eligible residents to look at when picking guests. Over-request so the
        // stage preference (little ones for the playground/daycare) has something to choose
        // from without needing a second, stage-aware acquire hook on GameManager.
        private const int MaxGuestCandidates = 8;

        // Transient scene props (a bowling boulder, a shopping fruit, a tossed coin) sit above
        // the buildings (sort 12) and below the particle bursts (sort 60).
        private const int PropSortOrder = 20;

        [SerializeField] private TownArea _area;
        [SerializeField] private TownController _town;
        [SerializeField] private PlaceholderLibrary _library;
        [SerializeField] private GameConfig _config;

        private readonly List<Visit> _visits = new List<Visit>();
        private float _attemptTimer;

        // Test-observable tallies (cumulative since the last reset; no reflection needed).
        private int _visitsStarted;
        private int _visitsArrived;
        private int _visitsCompleted;
        private int _visitsAborted;
        private int _lastArrivalIndex = -1;
        private float _lastArrivalDistance = -1f;

        /// <summary>One resident visiting one finished building: its guests, which beat of the
        /// building's loop it is on, and the transient tweens/props the scene is using (both
        /// torn down on EVERY exit, deliberate or aborted).</summary>
        private class Visit
        {
            public int Index;                   // build-order index of the host building
            public readonly List<Guest> Guests = new List<Guest>();
            public bool Fallback;               // no stage-preferred guest: play the simple version
            public bool Arrived;                // at least one guest has reached the building
            public float Waiting;               // seconds spent commuting (arrival watchdog)
            public float BeatTimer;             // counts down to the next beat
            public int Step;                    // beats played so far
            public readonly List<Coroutine> Fx = new List<Coroutine>();
            public readonly List<GameObject> Props = new List<GameObject>();
        }

        /// <summary>One visiting dino and the stand-point it belongs at.
        ///
        /// Deliberately NO cached "base scale": every scene tween reads
        /// <see cref="DinoController.RestingScale"/> live instead (see <see cref="Rest"/>).
        /// Snapshotting <c>localScale</c> at dispatch is the captured-inflated-base bug — the
        /// sample lands mid-flight of some OTHER tween (the 0.4s spawn pop in
        /// GameManager.SpawnDino is the easy one to hit: a visit forced a couple of frames
        /// after a dino spawns samples ~1.37 instead of 1.30) and the scene's own tweens, being
        /// the last writer, then settle the dino at that wrong size for good.</summary>
        private class Guest
        {
            public DinoController Dino;
            public Vector3 Stand;
        }

        // TEST HOOKS (integration runner; no reflection).
        internal int TestVisitCount => _visits.Count;
        internal int TestVisitsStarted => _visitsStarted;
        internal int TestVisitsArrived => _visitsArrived;
        internal int TestVisitsCompleted => _visitsCompleted;
        internal int TestVisitsAborted => _visitsAborted;

        /// <summary>Build-order index of the most recent arrival, and how far that guest stood
        /// from the building's plot when it clocked in. Recorded AT the moment of arrival so a
        /// case can assert "the visitor really walked to the building" without racing a short
        /// scene that may already have finished.</summary>
        internal int TestLastArrivalIndex => _lastArrivalIndex;
        internal float TestLastArrivalDistance => _lastArrivalDistance;

        internal bool TestIsVisiting(int index) => FindVisit(index) != null;
        internal bool TestVisitArrived(int index)
        {
            Visit v = FindVisit(index);
            return v != null && v.Arrived;
        }

        /// <summary>True when the running visit at <paramref name="index"/> found no
        /// stage-preferred guest and is playing its FALLBACK version (any dino bounces by the
        /// slide / peeks at the daycare window).</summary>
        internal bool TestVisitFallback(int index)
        {
            Visit v = FindVisit(index);
            return v != null && v.Fallback;
        }

        /// <summary>The dinos currently attending the visit at <paramref name="index"/> (empty
        /// when no visit is running there).</summary>
        internal List<DinoController> TestVisitDinos(int index)
        {
            var list = new List<DinoController>();
            Visit v = FindVisit(index);
            if (v == null)
            {
                return list;
            }

            for (int i = 0; i < v.Guests.Count; i++)
            {
                if (v.Guests[i] != null && v.Guests[i].Dino != null)
                {
                    list.Add(v.Guests[i].Dino);
                }
            }

            return list;
        }

        /// <summary>A building's DEBUT (DinoDigger-0gd): play its interaction ONCE, right now,
        /// because it has just been finished — the completion choreography's last beat. Bypasses
        /// the ambient countdown but not a single eligibility rule: same finished-building check,
        /// same guest pool, same yielding, same cleanup, so a debut is an ordinary visit that
        /// merely skipped the queue. Returns false, changing nothing, when the building is not
        /// finished, is already hosting a visit, or nobody is free to go — the caller treats that
        /// as "no debut this time" rather than retrying.</summary>
        internal bool PlayDebutVisit(int index) => StartVisit(index);

        /// <summary>TEST HOOK. Start a visit on <paramref name="index"/> RIGHT NOW, bypassing
        /// the ambient timer (but not a single eligibility rule — same finished-building check
        /// and same guest pool). Returns false, with no side effects, when the building is not
        /// finished, already hosting a visit, or nobody is free to go.</summary>
        internal bool TestForceVisit(int index) => StartVisit(index);

        /// <summary>TEST HOOK. Retire every running visit (guests head home, props destroyed)
        /// and rewind the tallies + ambient timer. Called from TownController.TestResetTown so
        /// a case never inherits the previous one's townsfolk.</summary>
        internal void TestResetLife()
        {
            for (int i = _visits.Count - 1; i >= 0; i--)
            {
                EndVisit(_visits[i], completed: false);
            }

            _visits.Clear();
            // A FULL interval, never zero: a case that restores finished buildings right after
            // a reset must not have an ambient visit fire on its very next frame.
            _attemptTimer = VisitInterval;
            _visitsStarted = 0;
            _visitsArrived = 0;
            _visitsCompleted = 0;
            _visitsAborted = 0;
            _lastArrivalIndex = -1;
            _lastArrivalDistance = -1f;
        }

        /// <summary>Last safety net: if the service itself goes away (component disabled, scene
        /// torn down) no dino may be left frozen in its visiting puppet state — retire every
        /// visit so each guest restores its pose and resumes being a meadow resident.</summary>
        private void OnDisable()
        {
            for (int i = _visits.Count - 1; i >= 0; i--)
            {
                EndVisit(_visits[i], completed: false);
            }

            _visits.Clear();
        }

        /// <summary>Wire the district, owning town, art library and tuning. Null-tolerant:
        /// with anything missing the service simply never starts a visit.</summary>
        public void Configure(TownArea area, TownController town, PlaceholderLibrary library,
            GameConfig config)
        {
            _area = area;
            _town = town;
            _library = library;
            _config = config;
            _attemptTimer = VisitInterval; // a loaded save full of finished buildings waits its turn
        }

        /// <summary>Driven every frame by <see cref="TownController.Tick"/>. Advances running
        /// visits, then counts down to the next ambient visit attempt.</summary>
        public void Tick(float dt)
        {
            if (_config == null || _area == null || _town == null)
            {
                return;
            }

            TickVisits(dt);

            // The ambient clock only runs once there is somewhere to go. A town with nothing
            // finished never accrues a "pending" visit that would fire the instant the first
            // building tops out (and the countdown restarts clean after every attempt).
            if (_town.FinishedBuildingCount <= 0)
            {
                _attemptTimer = VisitInterval;
                return;
            }

            _attemptTimer -= dt;
            if (_attemptTimer > 0f)
            {
                return;
            }

            _attemptTimer = VisitInterval;
            TryStartVisit();
        }

        private float VisitInterval =>
            _config != null ? Mathf.Max(0.05f, _config.TownVisitIntervalSeconds) : 18f;

        private float BeatSeconds =>
            _config != null ? Mathf.Max(0.02f, _config.TownVisitBeatSeconds) : 0.9f;

        private int MaxVisits => _config != null ? Mathf.Max(1, _config.TownMaxVisits) : 2;

        private float WalkSpeed => _config != null ? _config.TownBuilderCommuteSpeed : 1.1f;

        // ------------------------------------------------------------ starting a visit

        /// <summary>Pick a random FINISHED building that is not already hosting a visit and
        /// send someone over. Silently does nothing when the plaza is busy or nobody is free —
        /// ambient life is best-effort by design.</summary>
        private void TryStartVisit()
        {
            if (_visits.Count >= MaxVisits)
            {
                return;
            }

            int finished = _town.FinishedBuildingCount;
            if (finished <= 0)
            {
                return;
            }

            // Random start, then scan forward, so every finished building gets its turn
            // without keeping any per-building bookkeeping.
            int offset = Random.Range(0, finished);
            for (int i = 0; i < finished; i++)
            {
                int index = (offset + i) % finished;
                if (FindVisit(index) == null && StartVisit(index))
                {
                    return;
                }
            }
        }

        /// <summary>Recruit guests and dispatch them to the building at <paramref name="index"/>.
        /// Returns false (changing nothing) when the building is not finished, already hosts a
        /// visit, the concurrency cap is reached, or no eligible resident is free.</summary>
        private bool StartVisit(int index)
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || _town == null || _area == null || index < 0)
            {
                return false;
            }

            if (!_town.IsBuildingFinished(index) || FindVisit(index) != null ||
                _visits.Count >= MaxVisits)
            {
                return false;
            }

            // The SAME pool the recess party recruits from: non-buddy, not busy (so a builder,
            // a seller, a courier or another visitor is never poached), never the ceremony baby
            // and — structurally — never the player backhoe.
            List<DinoController> pool = gm.TownAcquireRecessGoers(MaxGuestCandidates);
            if (pool.Count == 0)
            {
                return false;
            }

            int want = Mathf.Max(1, GuestsFor(index));
            var chosen = new List<DinoController>();

            // Stage preference first (little ones on the playground slide, a baby at the
            // daycare window), then anyone — the design's stated fallback.
            bool preferred = false;
            for (int i = 0; i < pool.Count && chosen.Count < want; i++)
            {
                if (pool[i] != null && IsPreferredGuest(index, pool[i]))
                {
                    chosen.Add(pool[i]);
                    preferred = true;
                }
            }

            for (int i = 0; i < pool.Count && chosen.Count < want; i++)
            {
                if (pool[i] != null && !chosen.Contains(pool[i]))
                {
                    chosen.Add(pool[i]);
                }
            }

            var visit = new Visit { Index = index, Fallback = !preferred };
            for (int i = 0; i < chosen.Count; i++)
            {
                DinoController d = chosen[i];
                Vector3 stand = _area.StandWorld(index, i);
                if (!d.GoVisit(stand, WalkSpeed))
                {
                    continue; // refused (buddy / already building): just skip this candidate
                }

                visit.Guests.Add(new Guest { Dino = d, Stand = stand });
            }

            if (visit.Guests.Count == 0)
            {
                return false; // everyone refused: nothing started, nothing to clean up
            }

            _visits.Add(visit);
            _visitsStarted++;
            return true;
        }

        /// <summary>Guests this building's scene would like (clamped to the table).</summary>
        private static int GuestsFor(int index) =>
            index >= 0 && index < GuestsWanted.Length ? GuestsWanted[index] : 1;

        /// <summary>Beats in this building's loop (an unlisted building plays the generic
        /// three-beat "happy visitor" scene).</summary>
        private static int BeatsFor(int index) =>
            index >= 0 && index < BeatsPerLoop.Length ? BeatsPerLoop[index] : 3;

        /// <summary>Would this dino make the scene read better? The playground wants babies and
        /// kids on the slide; the daycare wants a baby at the window. Everywhere else anyone
        /// will do, and both preferences fall back to "any dino" when no little one is free.</summary>
        private static bool IsPreferredGuest(int index, DinoController d)
        {
            if (d == null)
            {
                return false;
            }

            switch (index)
            {
                case Playground:
                    return d.Stage == GrowthStage.Baby || d.Stage == GrowthStage.Kid;
                case DinoDaycare:
                    return d.Stage == GrowthStage.Baby;
                default:
                    return false;
            }
        }

        // ------------------------------------------------------------ running visits

        private void TickVisits(float dt)
        {
            for (int i = _visits.Count - 1; i >= 0; i--)
            {
                Visit v = _visits[i];
                if (v == null)
                {
                    _visits.RemoveAt(i);
                    continue;
                }

                // THE EXIT AUDIT, every frame: drop any guest that is no longer ours. IsOnVisit
                // goes false the instant ANYTHING else claims the dino — a build draft, a tap
                // promoting it to buddy, a fruit, a nap-inducing role change, destruction — so
                // this one check covers every exit path without construction, buddy swaps or
                // the parade ever having to know that town life exists.
                for (int g = v.Guests.Count - 1; g >= 0; g--)
                {
                    Guest guest = v.Guests[g];
                    if (guest != null && guest.Dino != null && !guest.Dino.IsBuddy &&
                        guest.Dino.IsOnVisit)
                    {
                        continue; // still ours
                    }

                    // This one belongs to something else now (drafted to build, tapped into a
                    // buddy, sent to eat...). Kill the scene's tweens BEFORE letting go, then
                    // restore the body. The dino cleared its own pose the instant it was
                    // claimed, but a beat tween still running could have re-posed it afterwards
                    // — script/tween update order is not fixed — and a tween stopped mid-flight
                    // writes nothing more. Stop-then-restore is the ordering with no window.
                    StopFx(v);
                    if (guest != null && guest.Dino != null)
                    {
                        guest.Dino.RestoreRestingPose();
                    }

                    v.Guests.RemoveAt(g);
                }

                if (v.Guests.Count == 0)
                {
                    EndVisit(v, completed: false); // everyone got claimed: yield immediately
                    _visits.RemoveAt(i);
                    continue;
                }

                if (!v.Arrived)
                {
                    v.Waiting += dt;
                    if (AnyAttending(v))
                    {
                        v.Arrived = true;
                        v.BeatTimer = 0f; // the first beat plays as soon as someone is there
                        RecordArrival(v);
                    }
                    else if (v.Waiting >= CommuteTimeoutSeconds)
                    {
                        EndVisit(v, completed: false); // nobody made it: give the guests back
                        _visits.RemoveAt(i);
                    }

                    continue;
                }

                v.BeatTimer -= dt;
                if (v.BeatTimer > 0f)
                {
                    continue;
                }

                v.BeatTimer = BeatSeconds;
                if (v.Step >= BeatsFor(v.Index))
                {
                    EndVisit(v, completed: true); // scene over: everyone wanders home
                    _visits.RemoveAt(i);
                    continue;
                }

                StopFx(v);            // the previous beat's tweens hand over cleanly
                PlayBeat(v, v.Step);
                v.Step++;
            }
        }

        /// <summary>True once at least one guest has stopped walking and is attending the
        /// building (its <see cref="DinoController.IsVisiting"/> puppet state).</summary>
        private static bool AnyAttending(Visit v)
        {
            for (int i = 0; i < v.Guests.Count; i++)
            {
                if (v.Guests[i] != null && v.Guests[i].Dino != null && v.Guests[i].Dino.IsVisiting)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Note where the first arriving guest ended up, for the integration case's
        /// "the visitor really walked to the building" assertion (recorded now, because a short
        /// scene may be over before a test coroutine gets to look).</summary>
        private void RecordArrival(Visit v)
        {
            _visitsArrived++;
            _lastArrivalIndex = v.Index;
            _lastArrivalDistance = -1f;

            Vector3 plot = Plot(v.Index);
            for (int i = 0; i < v.Guests.Count; i++)
            {
                Guest g = v.Guests[i];
                if (g != null && g.Dino != null && g.Dino.IsVisiting)
                {
                    _lastArrivalDistance = (g.Dino.transform.position - plot).magnitude;
                    return;
                }
            }
        }

        /// <summary>Retire a visit: stop its tweens, destroy its props, and send every guest
        /// still with us home (<see cref="DinoController.StopVisit"/> restores the pose and
        /// resumes the resident role). Runs for a finished scene AND for every abort, so a
        /// yielded visit leaves nothing behind — no frozen puppet, no orphan prop.</summary>
        private void EndVisit(Visit v, bool completed)
        {
            if (v == null)
            {
                return;
            }

            StopFx(v);

            for (int i = 0; i < v.Props.Count; i++)
            {
                if (v.Props[i] != null)
                {
                    Destroy(v.Props[i]);
                }
            }

            v.Props.Clear();

            for (int i = 0; i < v.Guests.Count; i++)
            {
                Guest g = v.Guests[i];
                if (g == null || g.Dino == null)
                {
                    continue;
                }

                // Tweens are already stopped (StopFx above), so put the body back to its
                // resting pose FIRST: a scene cut short mid-squash or mid-hide writes nothing
                // more by itself, and this is what guarantees the guest leaves at its stage
                // scale on EVERY exit — clean finish, abort, or teardown.
                g.Dino.RestoreRestingPose();

                if (completed)
                {
                    // The "that was nice" bounce outlives the visit by a quarter second and is
                    // deliberately not tracked; it resolves against the LIVE resting scale, so
                    // even if the dino is drafted a frame later its last write is still correct.
                    Pop(g, 0.16f, 0.25f);
                }

                g.Dino.StopVisit();
            }

            v.Guests.Clear();

            if (completed)
            {
                _visitsCompleted++;
            }
            else
            {
                _visitsAborted++;
            }
        }

        private Visit FindVisit(int index)
        {
            for (int i = 0; i < _visits.Count; i++)
            {
                if (_visits[i] != null && _visits[i].Index == index)
                {
                    return _visits[i];
                }
            }

            return null;
        }

        // ------------------------------------------------------------ the little scenes
        //
        // Each beat is one readable gesture at toddler zoom, built out of motion + EXISTING
        // sprites: no new art anywhere. Every guest lookup is index-guarded and every prop
        // sprite may be null (placeholder-only runs), in which case the motion carries it.

        private void PlayBeat(Visit v, int step)
        {
            switch (v.Index)
            {
                case Playground: BeatPlayground(v, step); break;
                case BoulderBrew: BeatBoulderBrew(v, step); break;
                case SlateLibrary: BeatSlateLibrary(v, step); break;
                case BedrockBijou: BeatBedrockBijou(v, step); break;
                case BoneanzaBowling: BeatBoneanzaBowling(v, step); break;
                case DinoDaycare: BeatDinoDaycare(v, step); break;
                case TarPitSprings: BeatTarPitSprings(v, step); break;
                case GronksGrocer: BeatGronksGrocer(v, step); break;
                case FossilFountain: BeatFossilFountain(v, step); break;
                default: BeatGeneric(v, step); break;
            }
        }

        // 0 — Pebble Playground: babies/kids waddle up, whoosh down the slide on a little arc,
        // then line up to go again. With no little ones around (fallback) any dino just has a
        // happy bounce by the slide instead of riding it.
        private void BeatPlayground(Visit v, int step)
        {
            Guest rider = MainGuest(v, step % Mathf.Max(1, v.Guests.Count));
            if (rider == null)
            {
                return;
            }

            Vector3 plot = Plot(v.Index);
            Vector3 top = plot + new Vector3(0.2f, 0.6f, 0f);
            Vector3 landing = plot + new Vector3(0.75f, -0.5f, 0f);

            if (v.Fallback)
            {
                Track(v, Pop(rider, 0.3f, 0.4f));           // happy bounce
                Sparkle(rider.Dino.transform.position, 3);
                return;
            }

            switch (step % 2)
            {
                case 0: // waddle up the steps
                    Track(v, Drift(rider, top, 0.45f));
                    break;
                default: // whoosh down
                    Track(v, Arc(rider, landing, 0.3f, 0.45f));
                    Track(v, Pop(rider, 0.2f, 0.4f));
                    Sparkle(landing, 4);
                    break;
            }
        }

        // 1 — Boulder Brew: shuffle up to the counter, pause, sip, and float a heart.
        private void BeatBoulderBrew(Visit v, int step)
        {
            Guest g = MainGuest(v, 0);
            if (g == null)
            {
                return;
            }

            Vector3 counter = Plot(v.Index) + new Vector3(0.3f, -0.4f, 0f);
            switch (step)
            {
                case 0:
                    Track(v, Drift(g, counter, 0.5f));       // shuffle up
                    break;
                case 1:
                    Track(v, Squash(g, 1.06f, 0.9f, 0.5f));  // the sip
                    Hearts(Above(g, 0.55f), 2);
                    break;
                default:
                    Track(v, Pop(g, 0.18f, 0.35f));          // ahh
                    Hearts(Above(g, 0.6f), 3);
                    break;
            }
        }

        // 2 — Slate Library: pause out front, "read" with a gentle sway, flick a page, toddle off.
        private void BeatSlateLibrary(Visit v, int step)
        {
            Guest g = MainGuest(v, 0);
            if (g == null)
            {
                return;
            }

            switch (step)
            {
                case 0:
                    Track(v, Drift(g, Plot(v.Index) + new Vector3(0f, -0.45f, 0f), 0.5f));
                    break;
                case 1:
                    Track(v, Tween.ShakeRotation(g.Dino.transform, 5f, 1.1f, 2)); // gentle sway
                    break;
                default:
                    Track(v, Tween.ShakeRotation(g.Dino.transform, 13f, 0.35f, 3)); // page flip
                    Track(v, Pop(g, 0.1f, 0.3f));
                    break;
            }
        }

        // 3 — Bedrock Bijou: the little crowd files in at the door (scaling away as they step
        // inside), a beat passes on the marquee, then they file out with a happy hop.
        private void BeatBedrockBijou(Visit v, int step)
        {
            Vector3 door = Plot(v.Index) + new Vector3(0f, -0.3f, 0f);
            switch (step)
            {
                case 0: // file in
                    for (int i = 0; i < v.Guests.Count; i++)
                    {
                        Track(v, Drift(GuestAt(v, i), door + new Vector3(i * 0.25f, 0f, 0f), 0.5f));
                    }

                    break;
                case 1: // step inside (scale away at the door)
                    for (int i = 0; i < v.Guests.Count; i++)
                    {
                        Track(v, ScaleFactor(GuestAt(v, i), 1f, 0.05f, 0.3f));
                    }

                    break;
                case 2: // ...the picture...
                    Sparkle(Plot(v.Index) + new Vector3(0f, 0.75f, 0f), 3); // marquee twinkle
                    break;
                default: // file out with a hop, back to their stand-points
                    for (int i = 0; i < v.Guests.Count; i++)
                    {
                        Guest g = GuestAt(v, i);
                        if (g == null)
                        {
                            continue;
                        }

                        Track(v, ScaleFactor(g, 0.05f, 1f, 0.25f));
                        Track(v, Arc(g, g.Stand, 0.3f, 0.45f));
                    }

                    break;
            }
        }

        // 4 — Bone-anza Bowling: crouch, roll a boulder at the lane, then arms-up cheer.
        private void BeatBoneanzaBowling(Visit v, int step)
        {
            Guest g = MainGuest(v, 0);
            if (g == null)
            {
                return;
            }

            Vector3 plot = Plot(v.Index);
            switch (step)
            {
                case 0:
                    Track(v, Squash(g, 1.15f, 0.8f, 0.55f)); // crouch into the delivery
                    break;
                case 1:
                    RollBoulder(v, g, plot + new Vector3(0f, -0.1f, 0f));
                    break;
                default:
                    Track(v, Pop(g, 0.4f, 0.5f));            // strike!
                    Sparkle(Above(g, 0.7f), 5);
                    break;
            }
        }

        // The boulder itself: an existing mound sprite shrunk down, rolled at the pins, and
        // puffed away in crumbs. Without the sprite the crumb puff alone still sells it.
        private void RollBoulder(Visit v, Guest g, Vector3 target)
        {
            Vector3 from = g.Dino.transform.position + new Vector3(0f, 0.12f, 0f);
            GameObject boulder = SpawnProp(_library != null ? _library.MoundSprite : null,
                from, 0.32f, null);
            if (boulder != null)
            {
                v.Props.Add(boulder);
                Transform t = boulder.transform;
                Track(v, Tween.MoveTo(t, target, 0.55f, () =>
                {
                    if (t != null)
                    {
                        Crumbs(t.position, new Color(0.78f, 0.62f, 0.42f), 5);
                        Destroy(t.gameObject);
                    }
                }));
                Track(v, Tween.ShakeRotation(t, 40f, 0.55f, 3)); // it rolls
            }
            else
            {
                Crumbs(target, new Color(0.78f, 0.62f, 0.42f), 5);
            }
        }

        // 5 — Dino Daycare: a baby pops in and out at the window. Fallback: any dino peeks.
        private void BeatDinoDaycare(Visit v, int step)
        {
            Guest g = MainGuest(v, 0);
            if (g == null)
            {
                return;
            }

            Vector3 plot = Plot(v.Index);
            Vector3 window = plot + new Vector3(0.15f, 0.55f, 0f);
            Vector3 below = plot + new Vector3(0.15f, -0.3f, 0f);

            switch (step)
            {
                case 0:
                    Track(v, Drift(g, below, 0.45f));         // toddle under the window
                    break;
                case 1:
                case 3:
                    Track(v, Arc(g, window, 0.12f, 0.3f));    // peekaboo!
                    Track(v, Pop(g, 0.25f, 0.35f));
                    if (step == 3)
                    {
                        Hearts(window + new Vector3(0f, 0.25f, 0f), 3);
                    }

                    break;
                default:
                    Track(v, Drift(g, below, 0.3f));          // ...and gone again
                    break;
            }
        }

        // 6 — Tar-Pit Springs: settle in, sink with a squash, bubble, then blissful hearts.
        private void BeatTarPitSprings(Visit v, int step)
        {
            Guest g = MainGuest(v, 0);
            if (g == null)
            {
                return;
            }

            Vector3 pool = Plot(v.Index) + new Vector3(0f, -0.25f, 0f);
            switch (step)
            {
                case 0:
                    Track(v, Drift(g, pool, 0.5f));           // step into the springs
                    break;
                case 1:
                    Track(v, Drift(g, pool + new Vector3(0f, -0.18f, 0f), 0.5f)); // sink
                    Track(v, Squash(g, 1.12f, 0.82f, 0.6f));
                    break;
                case 2:
                    Crumbs(Above(g, 0.3f), new Color(0.28f, 0.24f, 0.22f), 4); // tar bubbles
                    Track(v, Pop(g, 0.07f, 0.4f));
                    break;
                default:
                    Crumbs(Above(g, 0.3f), new Color(0.28f, 0.24f, 0.22f), 3);
                    Hearts(Above(g, 0.6f), 3);                // bliss
                    break;
            }
        }

        // 7 — Gronk's Grocer: amble stall to stall in two short hops, pick a fruit up, carry
        // it off. The fruit is an existing fruit sprite riding on the dino's head.
        private void BeatGronksGrocer(Visit v, int step)
        {
            Guest g = MainGuest(v, 0);
            if (g == null)
            {
                return;
            }

            Vector3 plot = Plot(v.Index);
            switch (step)
            {
                case 0:
                    Track(v, Arc(g, plot + new Vector3(-0.6f, -0.4f, 0f), 0.22f, 0.4f));
                    break;
                case 1:
                    Track(v, Arc(g, plot + new Vector3(0.6f, -0.4f, 0f), 0.22f, 0.4f));
                    break;
                case 2:
                    GiveFruit(v, g);
                    break;
                default:
                    Track(v, Pop(g, 0.16f, 0.35f));           // off home with the shopping
                    break;
            }
        }

        // Perch an existing fruit sprite on the shopper's head (a child of the dino, so it
        // rides along) with a little pop. Destroyed with the visit — town life saves nothing.
        private void GiveFruit(Visit v, Guest g)
        {
            // Fruit(variant) clamps internally, so a library with fewer variants (or none at
            // all) is safe — it just hands back the first sprite, or null.
            Sprite fruit = _library != null ? _library.Fruit(Random.Range(0, 4)) : null;

            GameObject prop = SpawnProp(fruit, Above(g, 0.62f), 0.32f, g.Dino.transform);
            if (prop != null)
            {
                v.Props.Add(prop);
                Tween.PunchScale(prop.transform, 0.4f, 0.3f);
            }

            Sparkle(Above(g, 0.62f), 3);
        }

        // 8 — Fossil Fountain (FINALE): two or three residents gather at the plaza centre,
        // splash, toss a coin for a glint, and finish on confetti. A longer loop than the rest.
        private void BeatFossilFountain(Visit v, int step)
        {
            Vector3 plot = Plot(v.Index);
            Vector3 rim = plot + new Vector3(0f, 0.3f, 0f);

            switch (step)
            {
                case 0: // gather round
                    for (int i = 0; i < v.Guests.Count; i++)
                    {
                        float ang = i * (Mathf.PI * 2f / Mathf.Max(1, v.Guests.Count)) + 0.4f;
                        Vector3 spot = plot + new Vector3(Mathf.Cos(ang) * 0.75f,
                            Mathf.Sin(ang) * 0.45f - 0.3f, 0f);
                        Track(v, Drift(GuestAt(v, i), spot, 0.5f));
                    }

                    break;
                case 1:
                case 3: // splash!
                    Sparkle(rim, 7);
                    for (int i = 0; i < v.Guests.Count; i++)
                    {
                        Track(v, Pop(GuestAt(v, i), 0.22f, 0.4f));
                    }

                    if (step == 3)
                    {
                        Hearts(rim + new Vector3(0f, 0.3f, 0f), 3);
                    }

                    break;
                case 2:
                case 4: // coin toss, one guest at a time
                    TossCoin(v, MainGuest(v, step == 2 ? 0 : 1), rim);
                    break;
                default: // the whole plaza cheers
                    for (int i = 0; i < v.Guests.Count; i++)
                    {
                        Track(v, Pop(GuestAt(v, i), 0.3f, 0.45f));
                    }

                    GameManager.Instance?.TownSpawnConfetti(rim);
                    break;
            }
        }

        // An existing coin sprite arcs from the guest into the fountain and glints away.
        private void TossCoin(Visit v, Guest g, Vector3 rim)
        {
            if (g == null || g.Dino == null)
            {
                Sparkle(rim, 4);
                return;
            }

            Track(v, Pop(g, 0.14f, 0.3f));

            GameObject coin = SpawnProp(_library != null ? _library.Treasure(0) : null,
                Above(g, 0.5f), 0.26f, null);
            if (coin == null)
            {
                Sparkle(rim, 4);
                return;
            }

            v.Props.Add(coin);
            Transform t = coin.transform;
            Track(v, Tween.MoveArc(t, t.position, rim, 0.55f, 0.5f, () =>
            {
                if (t != null)
                {
                    Sparkle(t.position, 5); // the glint as it lands
                    Destroy(t.gameObject);
                }
            }));
        }

        // Any building with no scripted scene (a roster longer than the table, or a plot whose
        // design is still being written): a friendly hop and a sparkle. Never nothing.
        private void BeatGeneric(Visit v, int step)
        {
            Guest g = MainGuest(v, step % Mathf.Max(1, v.Guests.Count));
            if (g == null)
            {
                return;
            }

            Track(v, Pop(g, 0.22f, 0.4f));
            if (step % 2 == 1)
            {
                Hearts(Above(g, 0.55f), 2);
            }
        }

        // ------------------------------------------------------------------- helpers

        private Vector3 Plot(int index) =>
            _area != null ? _area.PlotWorld(index) : transform.position;

        /// <summary>Guest <paramref name="i"/> of a visit — but only once it has ARRIVED and is
        /// holding still in the visiting puppet state. A guest still walking in owns its own
        /// movement, so a scene tween must not fight it; the beat simply skips that guest.</summary>
        private static Guest GuestAt(Visit v, int i)
        {
            Guest g = v != null && i >= 0 && i < v.Guests.Count ? v.Guests[i] : null;
            return Ready(g) ? g : null;
        }

        private static bool Ready(Guest g) => g != null && g.Dino != null && g.Dino.IsVisiting;

        /// <summary>The guest a one-dino scene should act on: the preferred slot when it has
        /// arrived, otherwise WHOEVER has (guests arrive in whatever order their walks finish,
        /// and a scene should never stall waiting for a particular one).</summary>
        private static Guest MainGuest(Visit v, int prefer)
        {
            Guest g = GuestAt(v, prefer);
            if (g != null)
            {
                return g;
            }

            for (int i = 0; v != null && i < v.Guests.Count; i++)
            {
                g = GuestAt(v, i);
                if (g != null)
                {
                    return g;
                }
            }

            return null;
        }

        private static Vector3 Above(Guest g, float dy) =>
            g != null && g.Dino != null
                ? g.Dino.transform.position + new Vector3(0f, dy, 0f)
                : Vector3.zero;

        // ---- motion (all null-safe; a destroyed dino just yields a null coroutine) ----

        /// <summary>Walk-speed slide to a point (the puppet's own movement — Visit mode writes
        /// no positions, so these tweens are the only thing moving the body).</summary>
        private static Coroutine Drift(Guest g, Vector3 to, float duration)
        {
            if (g == null || g.Dino == null)
            {
                return null;
            }

            to.z = g.Dino.transform.position.z;
            return Tween.MoveTo(g.Dino.transform, to, duration);
        }

        /// <summary>Hop/whoosh along a parabola (slide rides, cinema exits, stall hops).</summary>
        private static Coroutine Arc(Guest g, Vector3 to, float height, float duration)
        {
            if (g == null || g.Dino == null)
            {
                return null;
            }

            to.z = g.Dino.transform.position.z;
            return Tween.MoveArc(g.Dino.transform, g.Dino.transform.position, to, height, duration);
        }

        /// <summary>The guest's authoritative resting scale, read LIVE from its growth stage —
        /// never a value sampled off the transform. Every scene tween below multiplies against
        /// this each frame, so no scene can ever inherit (and then bake in) another tween's
        /// in-flight size, and a dino that somehow re-staged mid-scene still lands correctly.</summary>
        private static Vector3 Rest(Guest g) =>
            g != null && g.Dino != null ? g.Dino.RestingScale : Vector3.one;

        /// <summary>Bouncy scale punch that ALWAYS resolves back to the guest's resting scale.
        /// Deliberately not <see cref="Tween.PunchScale"/>: that helper captures a base scale
        /// from the transform (fine for its own callers, wrong here, where scenes also squash
        /// and hide the same dino and other systems may be mid-punch on it).</summary>
        private static Coroutine Pop(Guest g, float amount, float duration)
        {
            if (g == null || g.Dino == null)
            {
                return null;
            }

            Transform t = g.Dino.transform;
            return Tween.Run(duration, k =>
            {
                if (t != null)
                {
                    float env = Mathf.Sin(k * Mathf.PI) * (1f - k);
                    t.localScale = Rest(g) * (1f + amount * env);
                }
            }, () =>
            {
                if (t != null)
                {
                    t.localScale = Rest(g);
                }
            });
        }

        /// <summary>Squash-and-stretch that swells to (sx, sy) at the midpoint and returns to
        /// the resting scale (a sip, a crouch, sinking into the springs).</summary>
        private static Coroutine Squash(Guest g, float sx, float sy, float duration)
        {
            if (g == null || g.Dino == null)
            {
                return null;
            }

            Transform t = g.Dino.transform;
            return Tween.Run(duration, k =>
            {
                if (t != null)
                {
                    Vector3 rest = Rest(g);
                    float env = Mathf.Sin(k * Mathf.PI);
                    t.localScale = new Vector3(rest.x * (1f + (sx - 1f) * env),
                        rest.y * (1f + (sy - 1f) * env), rest.z);
                }
            }, () =>
            {
                if (t != null)
                {
                    t.localScale = Rest(g);
                }
            });
        }

        /// <summary>Scale from one FRACTION of the resting size to another (the cinema's
        /// "steps inside" / "comes back out"). Always expressed against the live resting scale,
        /// so even a hide interrupted at 5% is repaired the moment the visit lets go.</summary>
        private static Coroutine ScaleFactor(Guest g, float from, float to, float duration)
        {
            if (g == null || g.Dino == null)
            {
                return null;
            }

            Transform t = g.Dino.transform;
            return Tween.Run(duration, k =>
            {
                if (t != null)
                {
                    t.localScale = Rest(g) * Mathf.LerpUnclamped(from, to, k);
                }
            }, () =>
            {
                if (t != null)
                {
                    t.localScale = Rest(g) * to;
                }
            });
        }

        // ---- FX (existing particle sprites only) ----

        private void Sparkle(Vector3 pos, int count) =>
            GameManager.Instance?.TownSpawnFx(pos, _library != null ? _library.StarParticle : null,
                new Color(1f, 0.95f, 0.65f), 0.3f, count);

        private void Hearts(Vector3 pos, int count) =>
            GameManager.Instance?.TownSpawnFx(pos, _library != null ? _library.HeartParticle : null,
                new Color(1f, 0.55f, 0.65f), 0.3f, count);

        private void Crumbs(Vector3 pos, Color tint, int count) =>
            GameManager.Instance?.TownSpawnFx(pos, _library != null ? _library.CrumbParticle : null,
                tint, 0.26f, count);

        /// <summary>A transient scene prop from an EXISTING sprite, sized to
        /// <paramref name="worldHeight"/> world units regardless of the art's PPU. Returns null
        /// when the sprite is absent, and every caller degrades to motion + particles.</summary>
        private GameObject SpawnProp(Sprite sprite, Vector3 pos, float worldHeight, Transform parent)
        {
            if (sprite == null)
            {
                return null;
            }

            var go = new GameObject("TownLifeProp");
            go.transform.SetParent(parent != null ? parent : transform, true);
            go.transform.position = pos;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = PropSortOrder;

            // Uniform scale from the sprite's own height, divided out of any parent scale (a
            // prop riding a Big dino must not inherit its growth multiplier). For a dino host
            // that divisor comes from its RESTING scale, never its live localScale — the same
            // captured-inflated-base trap, which here would size the shopping fruit off a
            // mid-bounce body and leave it wrong for the rest of the scene.
            float h = sprite.bounds.size.y;
            float uni = h > 0.0001f ? worldHeight / h : 1f;
            float parentScale = 1f;
            if (parent != null)
            {
                var host = parent.GetComponent<DinoController>();
                parentScale = Mathf.Max(1e-4f, Mathf.Abs(
                    host != null ? host.RestingScale.x : parent.lossyScale.x));
            }

            go.transform.localScale = Vector3.one * (uni / parentScale);
            return go;
        }

        private static void Track(Visit v, Coroutine c)
        {
            if (v != null && c != null)
            {
                v.Fx.Add(c);
            }
        }

        private static void StopFx(Visit v)
        {
            if (v == null)
            {
                return;
            }

            for (int i = 0; i < v.Fx.Count; i++)
            {
                Tween.Stop(v.Fx[i]);
            }

            v.Fx.Clear();
        }
    }
}
