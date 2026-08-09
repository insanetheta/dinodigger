using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;
using DinoDigger.Overworld;

namespace DinoDigger.Dig
{
    /// <summary>
    /// GLOW THE LANTERN BOT (DinoDigger-6tc) — the fourth mossy sleeper, and the first one that
    /// lives inside the dig instead of out in the world.
    ///
    /// It is an ordinary <see cref="MachineFriend"/> and inherits every discipline that base
    /// enforces, which is the whole reason it is one: state-derived visuals, resting-scale-safe
    /// wobbles, null-tolerant art, an answer to EVERY tap in EVERY state, and a woken flag that
    /// is persisted the instant it is set so a found friend is never re-buried by a restart.
    ///
    /// ITS ARC, straight out of docs/backstory.md ("Glow wakes in the deep"):
    ///
    ///   ASLEEP, BURIED — it sits behind a random tile of the first dark stratum, rendering
    ///     BEHIND the dirt with its collider off, sparkling on the base class's own arrival
    ///     beacon. All the child sees is a soft glint coming out of the wall.
    ///   FOUND — the tile above it comes away (however: a bite, a cascade, a mushroom's fling)
    ///     and it stretches. Now it is visible and tappable, still asleep.
    ///   WOKEN — one tap. Eyes on, sparkles, and it hops up to perch on the pit's edge, where
    ///     its belly beam lights the deep end of the dig for the rest of the round — and for
    ///     every round after this one, forever.
    ///
    /// IT IS A LIGHT SOURCE, NOT AN ACTOR. It never digs, never clears, never blocks, and never
    /// sits on the grid — the one design warning the roster evaluation raised about it. All it
    /// does is make what is already down there visible one tile earlier.
    /// </summary>
    public class GlowBot : MachineFriend
    {
        /// <summary>World height of the lantern's body. Smaller than an overworld machine's 1.1:
        /// the pit is a close-up view and Glow must read as a friend perched on the edge, never
        /// as a machine looming over the dig.</summary>
        internal const float BodyHeight = 0.9f;

        /// <summary>Warm lantern yellow — the body tint while awake, and the colour of every
        /// sparkle it throws.</summary>
        internal static readonly Color LanternTint = new Color(1f, 0.92f, 0.62f);

        // A "charge" here is a FLARE: a tap on an awake Glow brightens the beam and re-sweeps it
        // right away. Short cooldown, because the answer to a tap must be something the child can
        // cause again while they still remember causing it.
        private const float FlareRechargeSeconds = 6f;

        private DigModeController _dig;
        private bool _covered;
        private float _sweepTimer;
        private int _flares;
        private int _stretches;

        public override MachineKind Kind => MachineKind.Glow;

        protected override float RechargeSeconds => FlareRechargeSeconds;

        /// <summary>Wire the site this lantern belongs to (it is rebuilt with no site only in a
        /// bare rig, and everything below tolerates that).</summary>
        internal void Attach(DigModeController dig)
        {
            _dig = dig;
        }

        /// <summary>True while it is still asleep behind a tile: rendered behind the dirt and
        /// untappable, so a tap aimed at the tile above it always digs.</summary>
        internal bool IsCovered => _covered;

        /// <summary>Hide behind (or emerge from) the dirt. Rendering and the collider are the
        /// only two things this changes: the machine's STATE is untouched, so a Glow that is
        /// buried again by a falling tile is still exactly as asleep or awake as it was.</summary>
        internal void SetCovered(bool covered)
        {
            _covered = covered;

            if (BodyRenderer != null)
            {
                // Behind the tiles (10) while buried, in front of them once found.
                BodyRenderer.sortingOrder = covered ? 9 : MachineSorting;
            }

            Collider2D col = TestCollider;
            if (col != null)
            {
                col.enabled = !covered;
            }
        }

        /// <summary>The little stretch it does when the dirt in front of it comes away — the beat
        /// the bible describes ("it stretches, then hops up"). Resting-scale safe via
        /// <see cref="MachineFriend.Jiggle"/>.</summary>
        internal void Stretch()
        {
            _stretches++;
            Jiggle(0.35f, 0.5f);
            Sparkle(10);
        }

        /// <summary>Put the lantern on its perch. Hops if it is far away (the wake beat), snaps
        /// if it is already about there (a layer rebuild re-seating it).</summary>
        internal void PerchAt(Vector3 point)
        {
            SetCovered(false);

            Vector3 from = transform.position;
            if ((point - from).sqrMagnitude < 0.04f)
            {
                transform.position = point;
                return;
            }

            Tween.MoveArc(transform, from, point, 1.1f, 0.45f);
        }

        protected override void OnWoke()
        {
            FillTank();     // the wake tap can be followed straight away by a first flare
            SetCovered(false);

            if (_dig != null)
            {
                PerchAt(_dig.GlowPerch);
                _dig.GlowSweep();   // light the pit on the very frame it wakes
            }
        }

        /// <summary>The job: a FLARE. The beam brightens for a beat and re-sweeps immediately, so
        /// tapping the lantern does the one thing a lantern should do when you poke it. It costs
        /// a charge (the gauge under it shows the wait), and a tap with an empty gauge still gets
        /// the base class's wordless "not yet" — a tap is never nothing.</summary>
        protected override void Activate(Vector2 worldPoint)
        {
            _flares++;
            Sparkle(14);
            GameManager.Instance?.Audio?.Chime();
            _dig?.GlowSweep();
        }

        protected override void TickAwake(float dt)
        {
            // THE NIGHT-LIGHT IDLE. On the bright surface layer there is nothing to reveal, so
            // the lantern dims to a soft glow and simply keeps the child company. Handing this to
            // BodyAlpha (rather than writing the renderer colour) is what keeps it from fighting
            // MachineFriend's state-derived visuals, which re-derive the body colour every frame.
            bool beaming = _dig != null && _dig.GlowShouldBeam;
            float dim = Config != null ? Mathf.Clamp01(Config.DigGlowDimAlpha) : 0.55f;
            BodyAlpha = beaming ? 1f : dim;

            if (!beaming || _dig == null)
            {
                return;
            }

            float sweep = Config != null ? Mathf.Clamp(Config.DigGlowSweepSeconds, 0.05f, 5f) : 0.5f;
            _sweepTimer -= dt;
            if (_sweepTimer <= 0f)
            {
                _sweepTimer = sweep;
                _dig.GlowSweep();
            }
        }

        // ------------------------------------------------------------ TEST HOOKS

        /// <summary>TEST HOOK. Flares fired (taps answered with a beam pulse).</summary>
        internal int TestFlares => _flares;

        /// <summary>TEST HOOK. Stretches (dirt-came-away reveals) this site.</summary>
        internal int TestStretches => _stretches;

        /// <summary>TEST HOOK. Is it still hidden behind a tile?</summary>
        internal bool TestCovered => _covered;

        internal override void TestResetMachine()
        {
            _flares = 0;
            _stretches = 0;
            _sweepTimer = 0f;
            BodyAlpha = 1f;
            SetCovered(false);
            base.TestResetMachine();
        }
    }
}
