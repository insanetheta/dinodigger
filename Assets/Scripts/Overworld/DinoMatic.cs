using System;
using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;

namespace DinoDigger.Overworld
{
    /// <summary>
    /// THE DINO-MATIC (DinoDigger-3rz): the left-behind human machine that turns a completed
    /// skeleton back into a living baby dinosaur. Per docs/backstory.md it is not built, it is
    /// FOUND — the child's first banked bone makes a suspicious mound with a glinting dome
    /// appear near the dig belt, and the town's NPC crew digs it out over four states while the
    /// child gets on with their day.
    ///
    /// IT IS A BUILD SITE, LITERALLY. This subclasses <see cref="BuildingController"/> rather
    /// than reimplementing it, so the excavation IS the town's construction state machine: the
    /// same five-sprite state ladder (s0 buried mound -> s1..s3 dug out -> the finished
    /// machine), the same <c>AddWork</c> accrual, the same crew, the same tap-to-cheer. That is
    /// what makes "the player is never drafted" structural: the only labour source in the game
    /// is <c>GameManager.TownAcquireBuilders</c>, which cannot return a buddy or the backhoe.
    ///
    /// WHAT IT ADDS on top of a building is three beats, all state-derived (nothing toggles a
    /// renderer at a call site — every frame recomputes the whole look from the current state):
    ///   BURIED   — the machine-friend "come look" language, verbatim: a gentle forever-sway
    ///              plus a repeating sparkle glint. It glints HARDER once a skeleton is waiting,
    ///              which is what stops "skeleton finished before the machine arrived" from
    ///              being a dead end.
    ///   READY    — excavated, with a completed skeleton waiting: the dome button glows and the
    ///              whole machine bounces on a slow pulse. Tap it.
    ///   CEREMONY — the skeleton floats in, the dome fogs, the lights chase, the machine
    ///              jiggles, POOF. Skip-tolerant: a tap during any of it jumps straight to the
    ///              poof, because a tap must always DO something and never block.
    ///
    /// Every sprite lookup is null-tolerant; with no imported art the machine is the generic
    /// building placeholder under a tint and every beat still plays.
    /// </summary>
    public class DinoMatic : BuildingController
    {
        // Beacon feel, deliberately the SAME numbers as MachineFriend's arrival beacon so a
        // buried Dino-Matic and a sleeping Doodle speak one language.
        private const float SwayRate = 2.1f;
        private const float SwayDegrees = 4.5f;
        private const float GlintSeconds = 3.2f;

        // ...except when a skeleton is already waiting, and the machine is the only thing
        // standing between the child and a new dinosaur. Then it nags.
        private const float EagerGlintSeconds = 1.4f;

        private const float ReadyPulseRate = 2.6f;
        private const float RevivalSeconds = 1.8f;

        private static readonly Color FogTint = new Color(0.86f, 0.95f, 1f, 0.75f);
        private static readonly Color GhostTint = new Color(0.92f, 0.96f, 1f, 0.9f);
        private static readonly Color ButtonTint = new Color(1f, 0.93f, 0.55f, 1f);
        private static readonly Color BuriedTint = new Color(0.72f, 0.74f, 0.70f, 1f);

        [SerializeField] private SpriteRenderer _button;   // the dome light: awake + ready only
        [SerializeField] private SpriteRenderer _fog;      // dome fog, ceremony only
        [SerializeField] private SpriteRenderer _ghost;    // floating skeleton, ceremony only
        [SerializeField] private ParticleSystem _sparkle;

        private PlaceholderLibrary _library;
        private Vector3 _restingScale = Vector3.one;
        private Vector3 _buttonRestScale = Vector3.one;
        private float _swayPhase;
        private float _glintTimer;
        private float _readyPhase;
        private int _glints;

        private Coroutine _revival;
        private Action _revivalDone;
        private bool _revivalRunning;

        /// <summary>True once the crew has dug it all the way out and it can be used.</summary>
        public bool IsExcavated => IsFinished;

        /// <summary>Where the revived baby lands: just in front of the machine.</summary>
        public Vector3 PadWorld => transform.position + new Vector3(0f, 0.25f, 0f);

        /// <summary>True while the revival choreography is playing.</summary>
        public bool IsCeremonyPlaying => _revivalRunning;

        // ------------------------------------------------------------ TEST HOOKS
        internal bool TestExcavated => IsExcavated;
        internal int TestGlints => _glints;
        internal bool TestGlinting => !IsExcavated;
        internal bool TestButtonLit => _button != null && _button.enabled;
        internal bool TestCeremonyPlaying => _revivalRunning;

        // ---------------------------------------------------------------- setup

        /// <summary>Attach the machine's own overlays (dome light, fog, skeleton ghost, sparkle)
        /// and remember the authoritative resting scale. Called by
        /// <see cref="DinoMaticController"/> right after <c>Init</c>, so the built world and any
        /// test rig produce IDENTICAL machines — the same discipline the machine friends use.</summary>
        internal void Configure(PlaceholderLibrary library, ParticleSystem sparkle)
        {
            _library = library;
            _sparkle = sparkle;
            _restingScale = transform.localScale;

            Sprite blob = library != null ? library.MoundSprite : null;
            Sprite star = library != null ? library.StarParticle : null;

            _button = MakeOverlay("DomeLight", star, 3, new Vector3(0f, 1.05f, 0f), 0.42f);
            _fog = MakeOverlay("DomeFog", blob, 2, new Vector3(0f, 0.75f, 0f), 1.25f);
            _ghost = MakeOverlay("SkeletonGhost", null, 4, new Vector3(0f, 1.0f, 0f), 1.1f);

            // The button's own authoritative scale, captured AFTER MakeOverlay normalised it —
            // its ready-pulse departs from and returns to exactly this.
            _buttonRestScale = _button != null ? _button.transform.localScale : Vector3.one;

            ApplyStateVisuals();
        }

        private SpriteRenderer MakeOverlay(string name, Sprite sprite, int sortOffset,
            Vector3 localPos, float worldHeight)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = (Renderer != null ? Renderer.sortingOrder : 12) + sortOffset;
            sr.enabled = false; // ApplyStateVisuals owns this from here on

            if (sprite != null && worldHeight > 0f && sprite.bounds.size.y > 0.001f)
            {
                float k = worldHeight / sprite.bounds.size.y;
                go.transform.localScale = new Vector3(k, k, 1f);
            }

            return sr;
        }

        // ----------------------------------------------------------------- tick

        /// <summary>Per-frame beats. NOTE the base class dispatches this from its own Update —
        /// declaring an Update here would silently switch the base's off (see
        /// <see cref="BuildingController.Tick"/>).</summary>
        protected override void Tick(float dt)
        {
            if (!IsExcavated)
            {
                TickBeacon(dt);
                return;
            }

            // Excavated: the beacon is over, so put the body back upright — whatever a
            // transient pose changes, the transition out puts back.
            if (transform.localRotation != Quaternion.identity)
            {
                transform.localRotation = Quaternion.identity;
            }

            _readyPhase += dt;
        }

        /// <summary>The buried machine's "come look": a gentle sway plus a repeating glint,
        /// faster when a finished skeleton is already waiting for it.</summary>
        private void TickBeacon(float dt)
        {
            // Only the un-dug mound sways. Once the crew has broken into it (state 1+) it is a
            // building site and holds still, so the sway never fights the construction art.
            if (State == 0)
            {
                _swayPhase += dt * SwayRate;
                transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(_swayPhase) * SwayDegrees);
            }
            else if (transform.localRotation != Quaternion.identity)
            {
                transform.localRotation = Quaternion.identity;
            }

            _glintTimer -= dt;
            if (_glintTimer > 0f)
            {
                return;
            }

            bool eager = GameManager.Instance != null && GameManager.Instance.RevivalPending;
            _glintTimer = eager ? EagerGlintSeconds : GlintSeconds;
            _glints++;
            Sparkle(eager ? 8 : 4);
        }

        /// <summary>IDLE-ATTRACT candidate, exactly like an undiscovered machine friend: a
        /// bigger glint plus a bounce when the child has gone quiet. A machine with nothing to
        /// offer (dug out, no skeleton waiting) ignores it — nagging about it would be noise.</summary>
        public void AttractPulse()
        {
            if (IsExcavated && (GameManager.Instance == null || !GameManager.Instance.RevivalPending))
            {
                return;
            }

            Jiggle(0.22f, 0.5f);
            _glintTimer = GlintSeconds;
            _glints++;
            Sparkle(10);
        }

        // ------------------------------------------------------------- visuals

        /// <summary>THE single source of truth for the machine's own overlays, re-derived every
        /// LateUpdate so no code path can strand one on. (The BODY sprite belongs to the base
        /// class's construction-state ladder and is never touched here.)</summary>
        private void LateUpdate()
        {
            ApplyStateVisuals();
        }

        private void ApplyStateVisuals()
        {
            bool ready = IsExcavated && GameManager.Instance != null &&
                         GameManager.Instance.RevivalPending && !_revivalRunning;

            if (Renderer != null)
            {
                // A buried machine is a dull mound; a dug-out one is its true colour.
                Renderer.color = IsExcavated ? Color.white : BuriedTint;
            }

            if (_button != null)
            {
                _button.enabled = ready;
                if (ready)
                {
                    // THE BUTTON glows and bounces — not the whole machine. Deliberate: the body
                    // is the one transform every wobble (a "not yet" tap, the ceremony jiggle,
                    // an attract pulse) punches, and a per-frame pose written onto the same
                    // transform would flatten every one of them. Pulsing a CHILD keeps the
                    // invitation loud and keeps the body's resting scale sacred.
                    float wave = Mathf.Sin(_readyPhase * ReadyPulseRate);
                    Color c = ButtonTint;
                    c.a = 0.7f + 0.3f * wave;
                    _button.color = c;
                    _button.transform.localScale = _buttonRestScale * (1f + 0.14f * wave);
                }
                else
                {
                    _button.transform.localScale = _buttonRestScale;
                }
            }

            if (_fog != null)
            {
                _fog.enabled = _revivalRunning;
            }

            if (_ghost != null)
            {
                _ghost.enabled = _revivalRunning && _ghost.sprite != null;
            }
        }

        // ----------------------------------------------------------------- taps

        /// <summary>THE TAP TARGET NEVER EXCEEDS WHAT THE CHILD CAN SEE. A town building sits on
        /// a reserved plot where nothing else stands, so an approximate box costs it nothing.
        /// This machine stands OUT IN THE WORLD, deliberately among the dig mounds, sharing
        /// ground with wandering dinos and the empty grass a tap-to-move is aimed at — and it
        /// outranks most of them (TappableRank 3). A box larger than the art would quietly eat
        /// taps meant for the ground beside it, and an invisible target is the worst kind to
        /// lose a tap to because nothing on screen explains it.
        ///
        /// So: with art, the drawn silhouette exactly (the base behaviour). WITHOUT art, the
        /// base fallback is a 1x1 box centred on the ROOT — and the root sits on the GROUND
        /// LINE, so half that box hangs below ground where nothing is drawn at all. This
        /// replaces it with a modest box that sits ABOVE the ground line, where a placeholder
        /// machine actually reads.</summary>
        protected override void ShapeTapCollider(BoxCollider2D col, Sprite sprite)
        {
            if (sprite != null)
            {
                base.ShapeTapCollider(col, sprite);
                return;
            }

            col.size = new Vector2(0.9f, 1.1f);
            col.offset = new Vector2(0f, 0.55f); // lifted off the ground line, like the art
        }

        /// <summary>
        /// EVERY TAP IS ANSWERED, in every state:
        ///   buried / being dug -> the town's tap-to-cheer (confetti, the crew hops, they work
        ///                         faster) — the base class's behaviour, unchanged.
        ///   ceremony playing   -> SKIP AHEAD. A toddler hammering the machine speeds the show
        ///                         up; it can never stall or restart it.
        ///   excavated          -> ask for a revival. GameManager decides whether a skeleton is
        ///                         ready and answers either way, so a "not yet" still wobbles.
        /// </summary>
        public override void OnTapped(Vector2 worldPoint)
        {
            if (_revivalRunning)
            {
                SkipRevival();
                return;
            }

            if (!IsExcavated)
            {
                base.OnTapped(worldPoint); // cheer the excavation crew on
                return;
            }

            GameManager.Instance?.RequestRevival(this);
        }

        /// <summary>The wordless "not yet": the machine wobbles and gurgles. Used when the
        /// machine is dug out but no skeleton is finished — a tap still does something.</summary>
        internal void NotReadyWobble()
        {
            Jiggle(0.12f, 0.35f);
            Tween.ShakeRotation(transform, 5f, 0.4f, 2);
            GameManager.Instance?.Audio?.Honk();
        }

        // ------------------------------------------------------------- ceremony

        /// <summary>Play the revival: the skeleton floats in over the pad, the dome fogs up,
        /// the lights chase round it and the machine jiggles, then POOF. Runs as ONE tween so
        /// there is exactly one thing to stop when the child skips it — a chain of delayed
        /// callbacks would leave beats in flight after the skip and double-fire the finish.
        /// <paramref name="onDone"/> fires exactly once, however it ends.</summary>
        internal void PlayRevival(Sprite skeletonArt, Action onDone)
        {
            _revivalDone = onDone;
            _revivalRunning = true;

            if (_ghost != null)
            {
                _ghost.sprite = skeletonArt;
                _ghost.color = GhostTint;
                _ghost.transform.localPosition = new Vector3(0f, 0.75f, 0f);
                if (skeletonArt != null && skeletonArt.bounds.size.y > 0.001f)
                {
                    float k = 1.1f / skeletonArt.bounds.size.y;
                    _ghost.transform.localScale = new Vector3(k, k, 1f);
                }
            }

            GameManager.Instance?.Audio?.Hatch();
            Sparkle(12);

            int lightsFired = 0;
            Tween.Stop(_revival);
            _revival = Tween.Run(RevivalSeconds, t =>
            {
                // Dome fog swells in and then blows out with the poof.
                if (_fog != null)
                {
                    Color c = FogTint;
                    c.a = FogTint.a * Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
                    _fog.color = c;
                    _fog.transform.localScale = Vector3.one * (0.9f + 0.35f * t) * FogScale();
                }

                // The skeleton rises and turns solid as the machine works on it.
                if (_ghost != null)
                {
                    _ghost.transform.localPosition = new Vector3(0f, 0.75f + 0.45f * t, 0f);
                    Color g = GhostTint;
                    g.a = Mathf.Lerp(0.35f, 1f, t);
                    _ghost.color = g;
                }

                // Lights chase: three sparkle bursts spread across the run.
                int want = Mathf.Clamp(Mathf.FloorToInt(t * 4f), 0, 3);
                while (lightsFired < want)
                {
                    lightsFired++;
                    Sparkle(6);
                }
            }, FinishRevival);

            // Whole-body jiggle while it works. RestingScale-safe (Jiggle re-bases first), and
            // harmless if the child skips: a punch that outlives the skip still settles onto
            // the same resting scale.
            Jiggle(0.18f, RevivalSeconds * 0.5f);
        }

        private float FogScale()
        {
            Sprite s = _fog != null ? _fog.sprite : null;
            if (s == null || s.bounds.size.y <= 0.001f)
            {
                return 1f;
            }

            return 1.25f / s.bounds.size.y;
        }

        /// <summary>A tap during the ceremony: jump straight to the poof. Idempotent, so
        /// hammering the machine cannot fire the finish twice.</summary>
        internal void SkipRevival()
        {
            if (!_revivalRunning)
            {
                return;
            }

            Tween.Stop(_revival);
            _revival = null;
            FinishRevival();
        }

        /// <summary>POOF: the ghost pops, sparkles and confetti fly, the machine jiggles, and
        /// the caller's continuation (spawn the baby) runs. Guarded so the tween completing and
        /// a skip landing in the same frame still finish exactly once.</summary>
        private void FinishRevival()
        {
            if (!_revivalRunning)
            {
                return;
            }

            _revivalRunning = false;
            _revival = null;

            if (_ghost != null)
            {
                _ghost.sprite = null;
                _ghost.enabled = false;
            }

            Sparkle(22);
            Jiggle(0.3f, 0.4f);
            GameManager.Instance?.TownSpawnConfetti(PadWorld + new Vector3(0f, 0.4f, 0f));
            GameManager.Instance?.Audio?.Roar();

            Action done = _revivalDone;
            _revivalDone = null;
            done?.Invoke();
        }

        // ------------------------------------------------------------- helpers

        /// <summary>A body wobble that ALWAYS departs from and settles onto the resting scale,
        /// so a re-tap mid-jiggle can never capture an inflated scale as its new base.</summary>
        private void Jiggle(float amount, float duration)
        {
            Tween.CancelPunch(transform);
            transform.localScale = _restingScale;
            Tween.PunchScale(transform, amount, duration);
        }

        private void Sparkle(int count)
        {
            if (_sparkle == null)
            {
                return;
            }

            _sparkle.Play();
            _sparkle.Emit(Mathf.Clamp(count, 1, 40));
        }

        /// <summary>TEST HOOK. Dig the machine straight out (skip the crew), so a case about
        /// the REVIVAL does not have to play out an excavation it is not testing.</summary>
        internal void TestForceExcavated()
        {
            AddWork(ConstructionStates * 1000f);
        }
    }
}
