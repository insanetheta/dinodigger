using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;

namespace DinoDigger.Overworld
{
    /// <summary>
    /// The three helper machines, in roster order. The STRING form (<see cref="MachineFriend.IdOf"/>)
    /// is what the save file stores, so this enum may be re-ordered or extended freely.
    /// </summary>
    public enum MachineKind
    {
        Doodle = 0,     // wind-up music box, town plaza
        Sprinkles = 1,  // watering bot, berry garden
        Tuggy = 2,      // tugboat, streams
    }

    /// <summary>
    /// THE MOSSY SLEEPER (epic DinoDigger-b48). Shared behaviour for every left-behind
    /// helper machine, built once here and worn thin by three subclasses.
    ///
    /// The pattern, straight out of docs/backstory.md ("machines are found asleep and wake
    /// up happy when the island needs their job again"):
    ///
    ///   DORMANT — the machine is dark-grey, its eye-light is off and one tuft of moss has
    ///             grown on it. It does nothing at all. This is a machine's WORST state:
    ///             never broken, never scary, just napping under a bed like a toy.
    ///   FIRST TAP — the eye-light blinks on, sparkles pop, the whole body does a happy
    ///             jiggle, colour floods back, the moss falls off, and the machine's ONE
    ///             JOB switches on permanently. Persisted (<see cref="SaveData.MachinesWoken"/>)
    ///             so a woken friend is never re-buried by a restart.
    ///   AWAKE   — every further tap runs the job when the gauge is full, and gives a
    ///             wordless "not yet" wobble when it is not. A tap ALWAYS does something:
    ///             that is the toddler rule and it has no exceptions here.
    ///
    /// FOUR DISCIPLINES THIS BASE ENFORCES so the subclasses cannot get them wrong:
    ///
    ///   1. STATE-DERIVED VISIBILITY. Nothing toggles a renderer at a call site. Every
    ///      renderer's enabled flag and colour is (re)computed from the CURRENT state in
    ///      <see cref="ApplyStateVisuals"/>, which runs after any state change AND every
    ///      frame in LateUpdate. That is the hard-won builder hat/mallet lesson: the bug is
    ///      never "we forgot to show it", it is always "one exit path forgot to hide it".
    ///   2. RESTING-SCALE-SAFE TWEENS. The body has ONE authoritative scale
    ///      (<see cref="RestingScale"/>). Every wobble cancels any in-flight punch and puts
    ///      the transform back on that scale before starting, so a re-tap mid-jiggle can
    ///      never capture an inflated scale as its new base (the giant-blueberry bug).
    ///   3. NULL-TOLERANT ART. Every sprite lookup may come back null. A machine with no
    ///      imported art falls back to the mound sprite under its signature tint, so it is
    ///      always visible and always tappable; the gauge/moss/eye overlays just vanish.
    ///   4. NO REFLECTION, TEST HOOKS ONLY. Everything a test needs to see is an explicit
    ///      internal member at the bottom of this file.
    /// </summary>
    public abstract class MachineFriend : MonoBehaviour, ITappable
    {
        // Sorting: machines are props that stand on the ground among the dinos. Above the
        // buildings (12) and ducks (10), below a dino (15) so a dino walking in front of a
        // machine still draws in front of it.
        internal const int MachineSorting = 13;

        // Dormant colour multiplier applied on top of the machine's awake tint: a mossy,
        // shaded, unlit read WITHOUT a shader (plain SpriteRenderer.color multiply, which
        // is the simplest thing that is unambiguously readable at toddler distance).
        private static readonly Color DormantMultiply = new Color(0.42f, 0.46f, 0.44f, 1f);

        // Moss tuft: the mound sprite, shrunk and tinted, perched on the machine's shoulder.
        private static readonly Color MossTint = new Color(0.38f, 0.62f, 0.30f, 1f);

        // Gauge colours (the belly tank / cooldown dial). Back = the empty well, fill = the
        // charge in it. Deliberately high-contrast: this is the only "UI" a 2-year-old gets.
        private static readonly Color GaugeBackTint = new Color(0.16f, 0.18f, 0.20f, 0.85f);
        private static readonly Color GaugeFillTint = new Color(1f, 0.86f, 0.30f, 1f);

        private const float GaugeWidth = 0.62f;
        private const float GaugeHeight = 0.11f;

        [SerializeField] private SpriteRenderer _body;
        [SerializeField] private SpriteRenderer _moss;      // dormant only
        [SerializeField] private SpriteRenderer _eye;       // awake only ("the lights are on")
        [SerializeField] private SpriteRenderer _gaugeBack; // awake only
        [SerializeField] private SpriteRenderer _gaugeFill; // awake only, x-scaled by charge
        [SerializeField] private ParticleSystem _sparkle;
        [SerializeField] private Collider2D _collider;

        protected GameConfig Config { get; private set; }
        protected PlaceholderLibrary Library { get; private set; }

        /// <summary>The body renderer, for subclasses that need to mirror the art (Tuggy
        /// chugging west). NEVER assign its colour directly — <see cref="ApplyStateVisuals"/>
        /// owns that and would stomp the write on the same frame; use
        /// <see cref="BodyAlpha"/> for transparency instead.</summary>
        protected SpriteRenderer BodyRenderer => _body;

        /// <summary>Extra transparency a subclass wants on the body, folded into the
        /// state-derived colour so the two can never fight (Tuggy fades to slip under a
        /// bridge deck, exactly like a duck does). 1 = fully opaque.</summary>
        protected float BodyAlpha { get; set; } = 1f;

        private MachineFriendController _owner;
        private Color _awakeTint = Color.white;
        private Vector3 _restingScale = Vector3.one;
        private bool _awake;
        private float _eyePhase;

        // Test-observable tallies (never used by gameplay).
        private int _wakeTaps;
        private int _activations;
        private int _deniedTaps;
        private int _busyTaps;

        /// <summary>Which machine this is. Fixed per subclass.</summary>
        public abstract MachineKind Kind { get; }

        /// <summary>The stable save id for a machine kind. Strings (not enum ordinals) so the
        /// roster can be re-ordered without ever waking the wrong friend from an old save.</summary>
        public static string IdOf(MachineKind kind)
        {
            switch (kind)
            {
                case MachineKind.Doodle: return "doodle";
                case MachineKind.Sprinkles: return "sprinkles";
                case MachineKind.Tuggy: return "tuggy";
                default: return kind.ToString().ToLowerInvariant();
            }
        }

        /// <summary>This machine's stable save id.</summary>
        public string SaveId => IdOf(Kind);

        /// <summary>True once the child has woken it. Dormant machines do NOTHING.</summary>
        public bool IsAwake => _awake;

        /// <summary>The one authoritative body scale. Every wobble departs from and settles
        /// back onto this, so nothing can compound.</summary>
        public Vector3 RestingScale => _restingScale;

        // ------------------------------------------------------------------ setup

        /// <summary>Wire the machine. <paramref name="awakeTint"/> is the body colour when
        /// awake — white for real imported art, the machine's signature colour when it is
        /// running on the mound-sprite fallback. <paramref name="startAwake"/> restores a
        /// machine the child already woke in an earlier session.</summary>
        public void Configure(MachineFriendController owner, GameConfig config,
            PlaceholderLibrary library, Color awakeTint, bool startAwake)
        {
            _owner = owner;
            Config = config;
            Library = library;
            _awakeTint = awakeTint;
            _restingScale = transform.localScale;
            _awake = startAwake;

            OnConfigured();
            ApplyStateVisuals();
        }

        /// <summary>Subclass wiring hook, called once from <see cref="Configure"/> AFTER the
        /// base fields are live and BEFORE the first visual pass.</summary>
        protected virtual void OnConfigured()
        {
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            // A dormant machine does no JOB — but it is not invisible furniture either. It
            // has just arrived because the child earned it, so it sways and glints "come
            // look" until the first tap (see TickBeacon).
            if (!_awake)
            {
                TickBeacon(dt);
                return;
            }

            TickCooldown(dt);
            TickAwake(dt);
            _eyePhase += dt;
        }

        // ---------------------------------------------------------- arrival beacon

        // The "come look" language, deliberately the SAME as the dig's Surprise Pocket: a
        // gentle forever-sway plus a periodic sparkle glint. No popup, no camera takeover,
        // no sound — the child has to spot it and hunt it down, which is the whole delight
        // of a machine that landed while they were away.
        private const float SwayRate = 2.1f;      // rad/s
        private const float SwayDegrees = 4.5f;
        private const float GlintSeconds = 3.2f;  // one soft sparkle this often while asleep

        private float _swayPhase;
        private float _glintTimer;
        private int _glints;

        /// <summary>True while this machine is doing its "come look" arrival beacon —
        /// i.e. it is present, dormant, and has never been tapped.</summary>
        public bool IsGlinting => !_awake;

        private void TickBeacon(float dt)
        {
            _swayPhase += dt * SwayRate;
            transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(_swayPhase) * SwayDegrees);

            _glintTimer -= dt;
            if (_glintTimer > 0f)
            {
                return;
            }

            _glintTimer = GlintSeconds;
            Glint();
        }

        /// <summary>One soft sparkle off a sleeping machine. Small on its own; the point is
        /// that it repeats, so a wandering eye eventually catches it.</summary>
        private void Glint()
        {
            _glints++;
            Sparkle(4);
        }

        /// <summary>IDLE-ATTRACT. When the child has gone quiet, a still-undiscovered machine
        /// is one of the things the game points at — a bigger glint plus a bounce, exactly
        /// like a ripe berry or a waiting mound. An already-woken machine ignores this: it has
        /// been found, and nagging about it would be noise.</summary>
        public void AttractPulse()
        {
            if (_awake)
            {
                return;
            }

            Jiggle(0.24f, 0.5f);
            _glintTimer = GlintSeconds;
            _glints++;
            Sparkle(10);
        }

        /// <summary>Runs every frame while awake, after the gauge has been advanced.</summary>
        protected virtual void TickAwake(float dt)
        {
        }

        // Visuals are re-derived every frame (not just on transitions) so no code path can
        // leave an overlay stranded in the wrong state — see discipline 1 in the class doc.
        private void LateUpdate()
        {
            ApplyStateVisuals();
        }

        // ------------------------------------------------------------------- taps

        /// <summary>
        /// EVERY TAP IS ANSWERED, in every state — the toddler rule, with no exceptions. There
        /// are exactly four outcomes and all four are visible, so no state can fall through
        /// silently and read as a broken machine:
        ///
        ///   asleep      -> WAKE UP (the big one: eyes on, sparkles, jiggle, job on for good)
        ///   busy on job -> "I'M ON IT" — a bright acknowledging wiggle. Crucially this comes
        ///                  BEFORE the readiness test, so tapping a machine that has already
        ///                  walked off to do the job neither restarts the errand nor spends a
        ///                  second charge; it just cheers it along.
        ///   ready       -> DO THE JOB (spends a charge)
        ///   not ready   -> the machine's own wordless "not yet" (base: wobble + shake + gurgle)
        ///
        /// The two refusal paths both route through helpers that ALWAYS jiggle, so even a
        /// subclass that overrides them and forgets to call base still moves the body.
        /// </summary>
        public void OnTapped(Vector2 worldPoint)
        {
            if (!_awake)
            {
                WakeUp();
                return;
            }

            if (IsBusyOnJob)
            {
                _busyTaps++;
                BusyResponse();
                return;
            }

            if (IsReady)
            {
                _activations++;
                SpendCharge();
                Jiggle(0.16f, 0.28f);
                Activate(worldPoint);
                return;
            }

            // NEVER NOTHING. An empty tank / cooling dial still answers with a sad-cute
            // wobble so the tap is always rewarded and never reads as a broken machine.
            _deniedTaps++;
            NotReadyResponse();
        }

        /// <summary>True while the machine is physically away doing its job and a tap must not
        /// restart it. Only Sprinkles has one (it walks off to a sprout); Doodle's spring
        /// rewinding and Tuggy's tow-line pose do NOT occupy the machine — those are ordinary
        /// cooldowns, and a tap during them is correctly a "not yet".</summary>
        protected virtual bool IsBusyOnJob => false;

        /// <summary>The wordless "I'm on it!": a bright little wiggle acknowledging a tap that
        /// arrived while the machine is already off doing the job. Always moves the body.</summary>
        protected virtual void BusyResponse()
        {
            Jiggle(0.12f, 0.25f);
            Sparkle(3);
        }

        /// <summary>The first tap, ever: eyes blink on, sparkles pop, a happy jiggle, and the
        /// job switches on FOR GOOD. Idempotent — a double-tap in one frame cannot wake twice.</summary>
        public void WakeUp()
        {
            if (_awake)
            {
                return;
            }

            _awake = true;
            _wakeTaps++;

            // The beacon is over: put the body back upright before the happy jiggle, so the
            // sway can never leave a woken machine standing at a tilt (the exit-path rule —
            // whatever a transient pose changes, the transition out puts back).
            transform.localRotation = Quaternion.identity;

            if (_sparkle != null)
            {
                _sparkle.Play();
                _sparkle.Emit(14);
            }

            GameManager.Instance?.Audio?.Chime();
            Jiggle(0.30f, 0.55f);

            // Start the job "ready", so the very first wake tap can be followed straight
            // away by a first real use — the child gets cause and effect back-to-back.
            OnWoke();
            ApplyStateVisuals();

            // Persist immediately: a woken friend must never be re-buried by a restart.
            _owner?.NotifyWoken(this);
        }

        /// <summary>Subclass hook: the machine just woke. Put the job in its ready state.</summary>
        protected virtual void OnWoke()
        {
        }

        /// <summary>The job. Only ever called on an AWAKE machine with a charge already spent.</summary>
        protected abstract void Activate(Vector2 worldPoint);

        /// <summary>The wordless "not yet": a small sad-cute wobble plus a low gurgle. Subclasses
        /// may extend it (Sprinkles adds an empty-tank slosh) but must always call base.</summary>
        protected virtual void NotReadyResponse()
        {
            Jiggle(0.10f, 0.35f);
            Tween.ShakeRotation(transform, 5f, 0.4f, 2);
            GameManager.Instance?.Audio?.Honk();
        }

        // -------------------------------------------------------------- the gauge

        /// <summary>Charges in the tank right now (Doodle/Tuggy run a 1-charge tank, so this
        /// is 0 or 1 for them; Sprinkles carries up to <c>SprinklesTankCharges</c>).</summary>
        public int Charges { get; private set; }

        /// <summary>Tank capacity. One charge unless a subclass says otherwise.</summary>
        protected virtual int MaxCharges => 1;

        /// <summary>Seconds to refill ONE charge.</summary>
        protected abstract float RechargeSeconds { get; }

        private float _recharge; // seconds elapsed toward the next charge

        /// <summary>True when a tap will run the job. Virtual because a machine may need more
        /// than water in the tank: Sprinkles also needs a thirsty sprout to spray, and burning
        /// a charge on an empty garden would read to a child as the machine breaking. Anything
        /// that makes this false routes the tap to <see cref="NotReadyResponse"/> instead — so
        /// the tap is still answered, whichever reason it was.</summary>
        public virtual bool IsReady => Charges > 0;

        /// <summary>0..1 gauge fill: whole charges plus the fraction of the one refilling.
        /// This is the number the belly/dial draws, so "how full it looks" and "will a tap
        /// work" can never disagree.</summary>
        public float GaugeFill
        {
            get
            {
                int cap = Mathf.Max(1, MaxCharges);
                float partial = Charges >= cap || RechargeSeconds <= 0f
                    ? 0f
                    : Mathf.Clamp01(_recharge / RechargeSeconds);
                return Mathf.Clamp01((Charges + partial) / cap);
            }
        }

        /// <summary>Fill the tank to the brim (used on wake, and by the reset hook).</summary>
        protected void FillTank()
        {
            Charges = Mathf.Max(1, MaxCharges);
            _recharge = 0f;
        }

        private void SpendCharge()
        {
            Charges = Mathf.Max(0, Charges - 1);
        }

        /// <summary>Put ONE charge back (never above capacity), and reset the partial refill so
        /// the gauge does not jump. For a job that was started and then could not run.</summary>
        protected void AddCharge()
        {
            Charges = Mathf.Min(Mathf.Max(1, MaxCharges), Charges + 1);
            _recharge = 0f;
        }

        private void TickCooldown(float dt)
        {
            int cap = Mathf.Max(1, MaxCharges);
            if (Charges >= cap)
            {
                _recharge = 0f;
                return;
            }

            float need = RechargeSeconds;
            if (need <= 0f)
            {
                Charges = cap;
                return;
            }

            _recharge += dt;
            while (_recharge >= need && Charges < cap)
            {
                _recharge -= need;
                Charges++;
            }

            if (Charges >= cap)
            {
                _recharge = 0f;
            }
        }

        // ---------------------------------------------------------------- visuals

        /// <summary>THE single source of truth for what this machine looks like. Called after
        /// every state change and again every LateUpdate, so there is exactly one place that
        /// can be wrong — and it is driven purely by state, never by "who called what".</summary>
        private void ApplyStateVisuals()
        {
            if (_body != null)
            {
                Color c = _awake ? _awakeTint : _awakeTint * DormantMultiply;
                c.a = Mathf.Clamp01(_awakeTint.a * BodyAlpha);
                _body.color = c;
            }

            if (_moss != null)
            {
                // One tuft, and only while sleeping. Waking shakes it off.
                _moss.enabled = !_awake;
                _moss.color = MossTint;
            }

            if (_eye != null)
            {
                _eye.enabled = _awake;
                if (_awake)
                {
                    // A slow warm pulse so an awake machine reads as ALIVE even standing still.
                    float pulse = 0.78f + 0.22f * Mathf.Sin(_eyePhase * 2.2f);
                    _eye.color = new Color(1f, 0.95f, 0.72f, pulse);
                }
            }

            bool showGauge = _awake;
            if (_gaugeBack != null)
            {
                _gaugeBack.enabled = showGauge;
                _gaugeBack.color = GaugeBackTint;
                _gaugeBack.transform.localScale = GaugeScale(1f);
            }

            if (_gaugeFill != null)
            {
                _gaugeFill.enabled = showGauge;
                _gaugeFill.color = GaugeFillTint;

                // The fill grows from its LEFT edge: scale x by the fill and slide the bar
                // right by half the shortfall, so an empty tank collapses to the left rather
                // than shrinking toward the middle (which reads as "small", not "empty").
                float fill = Mathf.Clamp01(GaugeFill);
                _gaugeFill.transform.localScale = GaugeScale(fill);
                Vector3 p = _gaugeFill.transform.localPosition;
                p.x = -GaugeWidth * 0.5f * (1f - fill);
                _gaugeFill.transform.localPosition = p;
            }
        }

        // Turn a 0..1 fill into a local scale for a gauge bar built from a unit-ish sprite.
        private Vector3 GaugeScale(float fill)
        {
            float unit = _gaugeBack != null && _gaugeBack.sprite != null
                ? Mathf.Max(0.01f, _gaugeBack.sprite.bounds.size.x)
                : 1f;
            float unitY = _gaugeBack != null && _gaugeBack.sprite != null
                ? Mathf.Max(0.01f, _gaugeBack.sprite.bounds.size.y)
                : 1f;
            return new Vector3(GaugeWidth * fill / unit, GaugeHeight / unitY, 1f);
        }

        /// <summary>A happy body wobble that ALWAYS departs from and settles onto
        /// <see cref="RestingScale"/> — see discipline 2 in the class doc.</summary>
        protected void Jiggle(float amount, float duration)
        {
            Tween.CancelPunch(transform);          // hand the scale over from any in-flight punch
            transform.localScale = _restingScale;  // ...and re-base it before punching again
            Tween.PunchScale(transform, amount, duration);
        }

        /// <summary>Emit the machine's own sparkle burst (no-op with no particle wired).</summary>
        protected void Sparkle(int count = 8)
        {
            if (_sparkle == null)
            {
                return;
            }

            _sparkle.Play();
            _sparkle.Emit(Mathf.Clamp(count, 1, 40));
        }

        // ----------------------------------------------------------- build helper

        /// <summary>Attach the shared overlay children (moss tuft, eye light, gauge bars,
        /// sparkle) and the tap collider to a freshly created machine object. Called by
        /// <see cref="MachineFriendController"/> so the scene build and the integration rig
        /// produce IDENTICAL machines. Every piece is null-tolerant.
        ///
        /// The machine ROOT sits on the ground line and always has scale 1 (which is what
        /// makes <see cref="RestingScale"/> trivially safe and keeps every overlay's world
        /// size honest); <paramref name="body"/> is a CHILD renderer carrying the art and its
        /// own size normalisation. All the local positions below are therefore measured up
        /// from the ground, in world units.</summary>
        internal void BuildOverlays(PlaceholderLibrary lib, SpriteRenderer body,
            float bodyHeight, ParticleSystem sparkle)
        {
            _body = body;
            _sparkle = sparkle;

            Sprite blob = lib != null ? lib.MoundSprite : null;
            Sprite star = lib != null ? lib.StarParticle : null;

            _moss = MakeOverlay("Moss", blob, MachineSorting + 1,
                new Vector3(0.20f, bodyHeight * 0.86f, 0f), 0.26f);
            _eye = MakeOverlay("EyeLight", star, MachineSorting + 2,
                new Vector3(0f, bodyHeight * 0.62f, 0f), 0.34f);
            _gaugeBack = MakeOverlay("GaugeBack", blob, MachineSorting + 1,
                new Vector3(0f, -0.12f, 0f), 1f);
            _gaugeFill = MakeOverlay("GaugeFill", blob, MachineSorting + 2,
                new Vector3(0f, -0.12f, 0f), 1f);

            var col = gameObject.GetComponent<CircleCollider2D>();
            if (col == null)
            {
                col = gameObject.AddComponent<CircleCollider2D>();
            }

            // Generous toddler touch target, sized off the body so a big boat and a squat
            // sprinkler both get a fair one.
            //
            // THE RADIUS MUST COMFORTABLY CLEAR THE OFFSET. The machine's own transform sits on
            // the GROUND LINE while the collider is lifted to the middle of the body, so the
            // root point is (radius - offset) inside the circle's bottom rim. Sizing these at
            // 0.5/0.45 of the height left that margin at 0.055u — a knife-edge, and a tap aimed
            // at the machine's own position missed the moment anything nudged it: a bob, a
            // trundle step, or simply a collider not yet re-synced after a transform write
            // (Physics2D.autoSyncTransforms is false, so colliders only catch up on the
            // FixedUpdate tick). At 0.62/0.40 the margin is ~0.24u, which no single frame of
            // machine movement can eat, and the circle still spans the whole body.
            col.radius = Mathf.Clamp(bodyHeight * 0.62f, 0.45f, 0.8f);
            col.offset = new Vector2(0f, bodyHeight * 0.40f);
            col.isTrigger = true;
            _collider = col;
        }

        private SpriteRenderer MakeOverlay(string name, Sprite sprite, int sorting,
            Vector3 localPos, float worldHeight)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = sorting;
            sr.enabled = false; // ApplyStateVisuals owns this from here on

            if (sprite != null && worldHeight > 0f && sprite.bounds.size.y > 0.001f)
            {
                float k = worldHeight / sprite.bounds.size.y;
                go.transform.localScale = new Vector3(k, k, 1f);
            }

            return sr;
        }

        // ----------------------------------------------------------- TEST HOOKS

        internal bool TestAwake => _awake;
        internal int TestCharges => Charges;
        internal float TestGaugeFill => GaugeFill;
        internal bool TestReady => IsReady;
        internal int TestWakeTaps => _wakeTaps;
        internal int TestActivations => _activations;
        internal int TestDeniedTaps => _deniedTaps;

        /// <summary>TEST HOOK. Taps that arrived while the machine was already off doing its
        /// job and were answered with the "I'm on it" wiggle.</summary>
        internal int TestBusyTaps => _busyTaps;

        /// <summary>TEST HOOK. EVERY tap this machine has answered, whichever of the four
        /// outcomes it took. A case can compare this against the number of taps it made to
        /// prove no tap was ever silently swallowed — the toddler rule, asserted directly.</summary>
        internal int TestAnsweredTaps => _wakeTaps + _activations + _busyTaps + _deniedTaps;
        internal bool TestMossVisible => _moss != null && _moss.enabled;
        internal bool TestEyeVisible => _eye != null && _eye.enabled;
        internal bool TestGaugeVisible => _gaugeFill != null && _gaugeFill.enabled;
        internal Color TestBodyColor => _body != null ? _body.color : Color.white;
        internal Collider2D TestCollider => _collider;

        /// <summary>TEST HOOK. Is the arrival beacon running (present, dormant, never tapped)?</summary>
        internal bool TestGlinting => IsGlinting;

        /// <summary>TEST HOOK. Glints emitted since the last reset — proof the beacon is
        /// actually repeating rather than firing once and going quiet.</summary>
        internal int TestGlints => _glints;

        /// <summary>TEST HOOK. Refill the tank right now instead of waiting out the recharge.</summary>
        internal void TestRefill() => FillTank();

        /// <summary>TEST HOOK. Empty the tank right now (to prove the cooldown holds).</summary>
        internal void TestDrain()
        {
            Charges = 0;
            _recharge = 0f;
        }

        /// <summary>TEST HOOK. Advance the recharge clock without waiting in realtime.</summary>
        internal void TestAdvanceRecharge(float seconds) => TickCooldown(Mathf.Max(0f, seconds));

        /// <summary>TEST HOOK. Put the machine back to sleep with a full tank, so cases can
        /// replay the whole wake-up beat from scratch. Also used by TestReset.</summary>
        internal virtual void TestResetMachine()
        {
            _awake = false;
            _wakeTaps = 0;
            _activations = 0;
            _deniedTaps = 0;
            _busyTaps = 0;
            _glints = 0;
            _glintTimer = 0f;
            _swayPhase = 0f;
            FillTank();
            Tween.CancelPunch(transform);
            transform.localScale = _restingScale;
            transform.localRotation = Quaternion.identity;
            ApplyStateVisuals();
        }
    }
}
