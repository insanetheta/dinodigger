using System.Collections.Generic;
using UnityEngine;
using DinoDigger.Core;

namespace DinoDigger.Overworld
{
    /// <summary>
    /// DOODLE the music-box bot (DinoDigger-ldp). Lives in the town plaza, beside the
    /// Fossil Fountain, and turns the one-time milestone parade into an ON-DEMAND party.
    ///
    /// Awake, a tap CRANKS him: the crank handle spins, music notes puff out, and up to
    /// <c>GameConfig.DoodleMaxDancers</c> nearby residents trot over and
    /// keep playing their own species <c>DanceType</c> animation for
    /// <c>GameConfig.DoodlePartySeconds</c> before drifting home. That is
    /// the whole point of him: the nine per-species dance animations already shipped but
    /// were only ever seen ONE TAP AT A TIME — Doodle finally exhibits them as a chorus.
    ///
    /// CONSTRUCTION ALWAYS WINS, and it wins structurally rather than by policing:
    ///   * Dancers are drawn from <see cref="GameManager.MachineAcquireDancers"/>, which is
    ///     the visit system's eligibility rule verbatim (non-buddy, non-busy, non-seller,
    ///     not the ceremony baby) plus a proximity test. A dino ALREADY working or commuting
    ///     to a build site is <see cref="DinoController.IsBusy"/>, so it is never even offered.
    ///   * A dancer already at the party can still be drafted mid-song:
    ///     <see cref="DinoController.GoWork"/> refuses nothing and simply takes the dino, and
    ///     the party's per-dino beat below no-ops on anyone who has since started working.
    ///     Nothing here can block, delay, or un-draft a builder.
    ///   * The party claims no dino permanently and holds no lock. Worst case for any
    ///     participant is "it walked to the plaza and wiggled".
    /// </summary>
    public class DoodleMachine : MachineFriend
    {
        // How often a dancer re-triggers its species dance during the party. Comfortably
        // above DinoController's own 0.8s dance window so a beat is never swallowed by
        // the _busyDancing guard, and slow enough to read as dancing rather than jitter.
        private const float BeatSeconds = 1.15f;

        // Speed multiplier for the stroll to the plaza — an unhurried "ooh, music" amble.
        private const float GatherSpeed = 0.9f;

        public override MachineKind Kind => MachineKind.Doodle;

        [SerializeField] private Transform _crank;   // child handle sprite; spins on a crank

        private readonly List<DinoController> _dancers = new List<DinoController>();
        private float _partyRemaining;
        private float _beatTimer;
        private int _partiesThrown;
        private int _danceBeats;

        // Doodle's tank is a single crank's worth of wind-up, so the gauge under him reads
        // straight off the cooldown: empty right after a party, full when he can go again.
        protected override float RechargeSeconds =>
            Config != null ? Mathf.Max(0.1f, Config.DoodleCooldownSeconds) : 20f;

        /// <summary>Wire the crank handle child (created by <see cref="MachineFriendController"/>).</summary>
        internal void AttachCrank(Transform crank)
        {
            _crank = crank;
        }

        protected override void OnWoke()
        {
            FillTank(); // wound up and ready: the wake tap can be followed straight by a crank
        }

        protected override void Activate(Vector2 worldPoint)
        {
            _partiesThrown++;

            // --- the crank turns ---
            // Three full turns over the first beat. Purely visual; a null crank (art not
            // imported) simply skips it and the party still happens.
            if (_crank != null)
            {
                Transform c = _crank;
                Tween.Run(1.1f, t =>
                {
                    if (c != null)
                    {
                        c.localRotation = Quaternion.Euler(0f, 0f, -1080f * t);
                    }
                }, () =>
                {
                    if (c != null)
                    {
                        c.localRotation = Quaternion.identity;
                    }
                });
            }

            // --- the tune ---
            // A crank sting, then the music-box vamp starts looping UNDER the main track (which
            // ducks rather than stopping) and runs until EndParty. Starting a party while one is
            // already going re-cranks it without restarting the loop.
            GameManager.Instance?.Audio?.Chime();
            GameManager.Instance?.Audio?.StartDanceLoop();
            Sparkle(12);
            EmitNotes();

            // --- the chorus ---
            GatherDancers();

            _partyRemaining = Config != null ? Mathf.Max(0.5f, Config.DoodlePartySeconds) : 6f;
            _beatTimer = 0f; // first beat fires on the next tick, while the crank is still turning
        }

        protected override void TickAwake(float dt)
        {
            if (_partyRemaining <= 0f)
            {
                return;
            }

            _partyRemaining -= dt;

            _beatTimer -= dt;
            if (_beatTimer <= 0f)
            {
                _beatTimer = BeatSeconds;
                PlayBeat();
            }

            if (_partyRemaining <= 0f)
            {
                EndParty();
            }
        }

        /// <summary>One beat of the party: every still-eligible dancer plays its species
        /// dance, and a fresh puff of notes rises off the box.</summary>
        private void PlayBeat()
        {
            EmitNotes();

            for (int i = _dancers.Count - 1; i >= 0; i--)
            {
                DinoController d = _dancers[i];

                // Dropped from the party the moment anything else claims the dino — a build
                // draft, a buddy promotion, a meal, the parade. We never take it back.
                if (d == null || d.IsBuddy || d.IsWorking || d.IsOnVisit)
                {
                    _dancers.RemoveAt(i);
                    continue;
                }

                d.Dance();
                _danceBeats++;
            }
        }

        /// <summary>The party is over: stop counting. Every dancer is left in a perfectly
        /// ordinary resident state (Dance resolves to <c>ResumeRole</c> on its own, which
        /// trots a resident home), so there is no pose to restore and nothing to unwind —
        /// the disperse is the dinos' normal behaviour resuming, not a scripted exit.</summary>
        private void EndParty()
        {
            _partyRemaining = 0f;
            _dancers.Clear();
            GameManager.Instance?.Audio?.StopDanceLoop();   // and un-duck the main track
        }

        /// <summary>Safety net for the one thing a looping sound must never do: outlive the
        /// thing that started it. EndParty is the normal exit and TestResetMachine the test one,
        /// but a Doodle switched off mid-party (scene teardown, a future pooling pass) would
        /// otherwise leave the vamp looping under a plaza with no machine in it.</summary>
        private void OnDisable()
        {
            if (_partyRemaining > 0f)
            {
                GameManager.Instance?.Audio?.StopDanceLoop();
            }
        }

        private void GatherDancers()
        {
            _dancers.Clear();

            GameManager gm = GameManager.Instance;
            if (gm == null)
            {
                return;
            }

            int max = Config != null ? Mathf.Max(1, Config.DoodleMaxDancers) : 4;
            float radius = Config != null ? Mathf.Max(1f, Config.DoodleGatherRadius) : 6f;

            List<DinoController> found = gm.MachineAcquireDancers(transform.position, radius, max);
            if (found == null)
            {
                return;
            }

            for (int i = 0; i < found.Count; i++)
            {
                DinoController d = found[i];
                if (d == null)
                {
                    continue;
                }

                // Ring Doodle at a comfortable radius so the dancers face him instead of
                // stacking on him. WalkTo (not GoVisit): a dance changes mode, which would
                // immediately terminate a visit anyway — this way the dinos are plain
                // residents throughout and every existing claim on them still wins.
                d.WalkTo(RingPoint(i, found.Count), GatherSpeed, null);
                _dancers.Add(d);
            }
        }

        private Vector3 RingPoint(int slot, int count)
        {
            float ang = (Mathf.PI * 2f / Mathf.Max(1, count)) * slot + 0.35f;
            Vector3 p = transform.position +
                        new Vector3(Mathf.Cos(ang) * 1.25f, Mathf.Sin(ang) * 0.75f - 0.15f, 0f);
            p.z = transform.position.z;

            OverworldMap map = GameManager.Instance != null ? GameManager.Instance.TestMap : null;
            if (map != null)
            {
                Vector3 w = map.NearestWalkable(p, out bool found);
                if (found)
                {
                    return w;
                }
            }

            return p;
        }

        /// <summary>Music notes: a warm puff of the existing star particle rising off the
        /// box. No new art (there is no note sprite yet) and null-tolerant — with no star
        /// sprite the burst tints the default particle instead.</summary>
        private void EmitNotes()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null)
            {
                return;
            }

            gm.MachineSpawnFx(transform.position + new Vector3(0f, 0.75f, 0f),
                gm.TestLibrary != null ? gm.TestLibrary.StarParticle : null,
                new Color(1f, 0.82f, 0.35f), 0.3f, 8);
        }

        // ----------------------------------------------------------- TEST HOOKS

        /// <summary>TEST HOOK. Parties thrown since the last reset.</summary>
        internal int TestParties => _partiesThrown;

        /// <summary>TEST HOOK. Total per-dino dance beats played since the last reset —
        /// the direct evidence that residents actually danced, not just walked over.</summary>
        internal int TestDanceBeats => _danceBeats;

        /// <summary>TEST HOOK. Dinos currently signed up to the party.</summary>
        internal int TestDancerCount => _dancers.Count;

        /// <summary>TEST HOOK. Is a party running right now?</summary>
        internal bool TestPartyActive => _partyRemaining > 0f;

        /// <summary>TEST HOOK. The dinos currently dancing (live list, do not mutate).</summary>
        internal IReadOnlyList<DinoController> TestDancers => _dancers;

        internal override void TestResetMachine()
        {
            EndParty();
            _partiesThrown = 0;
            _danceBeats = 0;
            if (_crank != null)
            {
                _crank.localRotation = Quaternion.identity;
            }

            base.TestResetMachine();
        }
    }
}
