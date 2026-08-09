using System.Collections.Generic;
using UnityEngine;
using DinoDigger.Core;

namespace DinoDigger.Overworld
{
    /// <summary>
    /// SPRINKLES the watering bot (DinoDigger-25j), built to the REDESIGN in
    /// docs/machine-roster-eval.md rather than the bible's original spec.
    ///
    /// The original spec was a growth-RATE buff on a 25s invisible timer — arithmetic no
    /// 2-year-old can perceive, and the eval called it a rounding error. This is the
    /// rescue: Sprinkles is a TAP-PAYOFF. Tap him and he trundles to the nearest sprout
    /// that isn't ripe yet, sprays a sparkling arc, and that sprout ripens RIGHT NOW
    /// (<see cref="BerrySprout.WaterNow"/> — the same swell + sparkle the timer would have
    /// produced). The child is the cause of every single spray.
    ///
    /// The cooldown is diegetic and wordless: a belly tank holding
    /// <c>GameConfig.SprinklesTankCharges</c> sprays, drawn as a fill bar,
    /// sipping one charge back every <c>GameConfig.SprinklesRechargeSeconds</c>.
    /// Tapping an EMPTY tank never does nothing — it gives a sad-cute empty-gurgle wobble
    /// (the base wobble plus a hollow sideways slosh), so the tap is always answered.
    /// </summary>
    public class SprinklesMachine : MachineFriend
    {
        private enum Mode { Parked, Trundling, Spraying, Returning }

        private const float SpraySeconds = 0.7f;
        private const float ArriveEps = 0.12f;

        // Trundling has to end even if a target is unreachable in a straight line (it never
        // is on this island, but a watchdog is cheaper than a wedged robot).
        private const float TrundleTimeoutSeconds = 12f;

        public override MachineKind Kind => MachineKind.Sprinkles;

        private readonly List<BerrySprout> _sprouts = new List<BerrySprout>();
        private Vector3 _home;
        private Mode _mode = Mode.Parked;
        private BerrySprout _target;
        private float _sprayTimer;
        private float _trundleElapsed;
        private int _spraysDelivered;
        private int _emptyGurgles;

        protected override int MaxCharges =>
            Config != null ? Mathf.Max(1, Config.SprinklesTankCharges) : 3;

        protected override float RechargeSeconds =>
            Config != null ? Mathf.Max(0.1f, Config.SprinklesRechargeSeconds) : 45f;

        private float TrundleSpeed =>
            Config != null ? Mathf.Max(0.1f, Config.SprinklesTrundleSpeed) : 1.1f;

        /// <summary>Wire the garden this bot tends. Copies the list so the caller may reuse
        /// its own; a null/empty list simply means every tap is a (still-answered) no-op
        /// wobble, which is the right degradation for a scene with no garden.</summary>
        public void SetGarden(IList<BerrySprout> sprouts, Vector3 home)
        {
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

            _home = home;
        }

        protected override void OnConfigured()
        {
            if (_home == Vector3.zero)
            {
                _home = transform.position;
            }
        }

        protected override void OnWoke()
        {
            FillTank(); // a full tank on waking: the very first tap gets a real spray
        }

        // A tap only counts as "ready" when there is somewhere to spray AND water to spray.
        // Without the target test an empty garden would silently burn a charge, which reads
        // to a child as the machine breaking. Either way the tap still gets an answer — see
        // NotReadyResponse.
        private bool HasThirstySprout => FindThirstiest() != null;

        public override bool IsReady => base.IsReady && HasThirstySprout;

        /// <summary>Sprinkles is the one machine that physically walks away to do its job, so
        /// it is the one machine with a real busy state. Tapping it mid-errand used to fall
        /// straight through to the readiness test and START THE JOB AGAIN — spending a second
        /// charge and re-targeting a sprout halfway to the first one. Now it gets the "I'm on
        /// it" wiggle instead: the tap is answered, the errand is undisturbed, and the tank is
        /// only ever spent on water that actually leaves it.
        ///
        /// Note RETURNING home is deliberately NOT busy — the job is done, the bot is just
        /// walking back, and a tap then should be free to start the next errand.</summary>
        protected override bool IsBusyOnJob => _mode == Mode.Trundling || _mode == Mode.Spraying;

        /// <summary>The busy answer: a wiggle plus a cheerful chirp, so "I'm already on my way"
        /// reads clearly differently from the empty tank's sad gurgle.</summary>
        protected override void BusyResponse()
        {
            base.BusyResponse();
            GameManager.Instance?.Audio?.Tap();
        }

        protected override void Activate(Vector2 worldPoint)
        {
            BerrySprout target = FindThirstiest();
            if (target == null)
            {
                // Unreachable in practice (IsReady above already requires a target), but a
                // charge must never vanish into nothing if that ever changes.
                RefundCharge();
                NotReadyResponse();
                return;
            }

            _target = target;
            _mode = Mode.Trundling;
            _trundleElapsed = 0f;
            GameManager.Instance?.Audio?.Move();
        }

        /// <summary>The tap is only "ready" when the tank has water AND a sprout wants it.
        /// Both failure modes fall through to <see cref="NotReadyResponse"/>, so the child
        /// always gets an answer either way.</summary>
        protected override void NotReadyResponse()
        {
            _emptyGurgles++;

            base.NotReadyResponse();

            // The empty-tank gurgle: a hollow sideways slosh on top of the base wobble, so
            // "I'd love to, but I'm out" reads differently from "here you go".
            Transform t = transform;
            Vector3 home = t.position;
            Tween.Run(0.45f, k =>
            {
                if (t != null)
                {
                    t.position = home + new Vector3(Mathf.Sin(k * Mathf.PI * 4f) * 0.06f * (1f - k), 0f, 0f);
                }
            }, () =>
            {
                if (t != null)
                {
                    t.position = home;
                }
            });
        }

        protected override void TickAwake(float dt)
        {
            switch (_mode)
            {
                case Mode.Trundling:
                    TickTrundle(dt, _target != null ? _target.transform.position : _home, Mode.Spraying);
                    break;
                case Mode.Spraying:
                    TickSpray(dt);
                    break;
                case Mode.Returning:
                    TickTrundle(dt, _home, Mode.Parked);
                    break;
            }
        }

        private void TickTrundle(float dt, Vector3 target, Mode onArrive)
        {
            _trundleElapsed += dt;

            // Stand BESIDE the sprout, not on top of it, so the spray arc is visible and the
            // bot never covers its own tap target.
            Vector3 stand = target + new Vector3(transform.position.x <= target.x ? -0.45f : 0.45f, -0.05f, 0f);
            if (onArrive == Mode.Parked)
            {
                stand = target;
            }

            stand.z = transform.position.z;
            Vector3 delta = stand - transform.position;
            float d = delta.magnitude;

            if (d <= ArriveEps || _trundleElapsed >= TrundleTimeoutSeconds)
            {
                transform.position = stand;
                EnterMode(onArrive);
                return;
            }

            transform.position += delta / d * Mathf.Min(TrundleSpeed * dt, d);
        }

        private void EnterMode(Mode mode)
        {
            _mode = mode;
            _trundleElapsed = 0f;

            if (mode == Mode.Spraying)
            {
                _sprayTimer = SpraySeconds;
                Spray();
            }
        }

        private void TickSpray(float dt)
        {
            _sprayTimer -= dt;
            if (_sprayTimer > 0f)
            {
                return;
            }

            _target = null;
            _mode = Mode.Returning;
            _trundleElapsed = 0f;
        }

        /// <summary>The payoff, all in one frame: a sparkling arc of droplets, the sprout
        /// swelling into a ripe berry, and a bright chime. Cause and effect with nothing
        /// in between — which is the entire redesign.</summary>
        private void Spray()
        {
            BerrySprout target = _target;
            if (target == null)
            {
                return;
            }

            GameManager gm = GameManager.Instance;
            Sprite star = gm != null && gm.TestLibrary != null ? gm.TestLibrary.StarParticle : null;

            // The arc: three quick puffs stepping from the spout toward the sprout, so the
            // water reads as travelling rather than teleporting.
            Vector3 from = transform.position + new Vector3(0f, 0.55f, 0f);
            Vector3 to = target.transform.position + new Vector3(0f, 0.25f, 0f);
            for (int i = 0; i < 3; i++)
            {
                float u = (i + 1) / 3f;
                Vector3 p = Vector3.Lerp(from, to, u) + new Vector3(0f, 0.35f * u * (1f - u) * 4f, 0f);
                gm?.MachineSpawnFx(p, star, new Color(0.55f, 0.85f, 1f), 0.26f, 6);
            }

            bool ripened = target.WaterNow();
            if (ripened)
            {
                _spraysDelivered++;
            }

            // Sparkle payoff on the berry itself, then the chime — the "ta-da".
            gm?.MachineSpawnFx(target.transform.position, star, new Color(0.75f, 1f, 0.55f), 0.32f, 10);
            gm?.Audio?.Chime();
            Sparkle(6);
            Jiggle(0.14f, 0.3f);
        }

        /// <summary>The nearest sprout that is NOT ripe yet — the thirstiest thing in the
        /// patch. Null when the whole garden is already ripe (or there is no garden).</summary>
        private BerrySprout FindThirstiest()
        {
            BerrySprout best = null;
            float bestSq = float.MaxValue;
            Vector3 p = transform.position;

            for (int i = 0; i < _sprouts.Count; i++)
            {
                BerrySprout s = _sprouts[i];
                if (s == null || s.IsRipe)
                {
                    continue;
                }

                float sq = (s.transform.position - p).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = s;
                }
            }

            return best;
        }

        /// <summary>Hand a spent charge back when the job could not run, so the gauge stays
        /// honest: it only ever goes down when water actually left the tank. Routed through the
        /// base class's own recharge accounting rather than poking the count, so the drawn
        /// tank, the readiness test and the real charge count can never drift apart.</summary>
        private void RefundCharge()
        {
            AddCharge();
        }

        // ----------------------------------------------------------- TEST HOOKS

        /// <summary>TEST HOOK. Sprouts actually ripened by a spray since the last reset.</summary>
        internal int TestSprays => _spraysDelivered;

        /// <summary>TEST HOOK. Empty/no-target taps that were answered with a gurgle wobble.</summary>
        internal int TestEmptyGurgles => _emptyGurgles;

        /// <summary>TEST HOOK. True while the bot is trundling to, or spraying, a sprout.</summary>
        internal bool TestOnErrand => _mode == Mode.Trundling || _mode == Mode.Spraying;

        /// <summary>TEST HOOK. True only when the bot is fully settled back home — not on an
        /// errand AND not still walking back. A case that wants to tap a STATIONARY Sprinkles
        /// should wait on this, so the tap is never aimed at a body that moved after the
        /// assertion was written.</summary>
        internal bool TestParked => _mode == Mode.Parked;

        /// <summary>TEST HOOK. The sprout currently being watered (null when parked).</summary>
        internal BerrySprout TestTarget => _target;

        /// <summary>TEST HOOK. True when there is a sprout worth spraying right now.</summary>
        internal bool TestHasThirstySprout => HasThirstySprout;

        internal override void TestResetMachine()
        {
            _mode = Mode.Parked;
            _target = null;
            _sprayTimer = 0f;
            _trundleElapsed = 0f;
            _spraysDelivered = 0;
            _emptyGurgles = 0;
            if (_home != Vector3.zero)
            {
                transform.position = _home;
            }

            base.TestResetMachine();
        }
    }
}
