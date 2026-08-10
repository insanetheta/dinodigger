using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;

namespace DinoDigger.Dig
{
    /// <summary>
    /// THE LADDER DOWN (DinoDigger-dv1). A big friendly prop that appears at the bottom of the
    /// pit once the child has cleared enough of the layer, and takes them one stratum deeper
    /// when tapped.
    ///
    /// IT SPEAKS THE GAME'S ONE "COME LOOK" LANGUAGE, deliberately copied rather than invented:
    /// a gentle forever-sway plus a repeating sparkle glint — the same beacon the dig's surprise
    /// pocket uses for its mystery tile and the mossy sleepers use when a machine lands. A child
    /// who has learned "the wiggling sparkly thing is worth touching" already knows what this is,
    /// with no popup, no arrow and no words.
    ///
    /// It is NOT a DirtTile: the cell it stands in is EMPTY (that is the whole point — it needs
    /// somewhere clear to stand), so making it a tile kind would have meant inventing a tile that
    /// gravity must ignore. As a plain prop it is invisible to the cascade, to the settle loop
    /// and to every clear path, and it can never be dug, cracked or landed on.
    /// </summary>
    public class DigLadder : MonoBehaviour, ITappable
    {
        // Same beacon numbers as MachineFriend's arrival glint, on purpose: one visual language.
        private const float SwayRate = 2.1f;      // rad/s
        private const float SwayDegrees = 5f;
        private const float GlintSeconds = 1.6f;  // twice as eager as a sleeping machine's

        // The chevron's bob: slow enough to read as "pointing", fast enough to catch an eye.
        private const float ArrowBobRate = 3.0f;      // rad/s
        private const float ArrowBobUnits = 0.13f;    // world units of travel
        private const float ArrowDropUnits = 0.95f;   // world units below the ladder's centre
        private const float ArrowHeightUnits = 0.34f; // world height of the chevron

        private DigModeController _owner;
        private PlaceholderLibrary _lib;
        private ParticleSystem _sparkle;
        private float _swayPhase;
        private float _glintTimer;
        private int _glints;
        private int _taps;
        private bool _consumed;
        private Vector3 _restScale = Vector3.one;

        // The "down" affordance (DinoDigger-n05). A ladder says CLIMB; it does not say which
        // WAY, and a child who has never used a ladder has no reason to assume down. The
        // chevron is the half of the message the prop cannot carry on its own.
        private Transform _arrow;
        private SpriteRenderer _arrowRenderer;
        private Vector3 _arrowRest;

        /// <summary>TEST HOOK. Glints emitted (proof the beacon repeats rather than firing once).</summary>
        internal int TestGlints => _glints;

        /// <summary>TEST HOOK. Taps this ladder has answered.</summary>
        internal int TestTaps => _taps;

        public void Build(DigModeController owner, PlaceholderLibrary lib)
        {
            _owner = owner;
            _lib = lib;
            _restScale = transform.localScale;   // the ONE authoritative scale (see OnTapped)
            _swayPhase = Random.value * Mathf.PI * 2f;

            GameManager gm = GameManager.Instance;
            if (gm != null && _lib != null)
            {
                _sparkle = gm.MachineCreateParticles(transform, _lib.StarParticle,
                    new Color(1f, 0.92f, 0.55f), 0.32f);
            }

            BuildArrow();

            // Arrive with a pop AND a ding, so the moment it earns its place lands even if the
            // child happens to be looking at the other side of the pit — the sound is what
            // turns a head the pop alone would miss.
            transform.localScale = _restScale * 0.2f;
            Tween.ScaleTo(transform, _restScale, 0.35f);
            gm?.Audio?.LadderDing();
            Glint();
        }

        /// <summary>Hang the down-chevron under the ladder.
        ///
        /// IT IS A CHILD OF THE LADDER, so it inherits the sway (the pair leans together, which
        /// reads as one object rather than two) and goes away with it — a stray arrow left
        /// pointing at nothing would be a second unexplained thing in the pit, which is the
        /// whole bug this ticket exists for. The ladder's transform carries the art's fit scale,
        /// so every offset below is divided back out to stay in WORLD units.</summary>
        private void BuildArrow()
        {
            Sprite art = _lib != null ? _lib.ArrowDown : null;
            if (art == null || art.bounds.size.y <= 0.001f)
            {
                return; // no chevron art: the ladder still sways, glints and works
            }

            float inv = Mathf.Abs(_restScale.y) > 0.0001f ? 1f / _restScale.y : 1f;

            var go = new GameObject("LadderArrow");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, -ArrowDropUnits * inv, 0f);
            go.transform.localScale = Vector3.one * (ArrowHeightUnits / art.bounds.size.y * inv);

            _arrowRenderer = go.AddComponent<SpriteRenderer>();
            _arrowRenderer.sprite = art;
            _arrowRenderer.sortingOrder = 13; // above the ladder (12), below the critters (14)

            _arrow = go.transform;
            _arrowRest = go.transform.localPosition;
        }

        /// <summary>THIS LADDER HAS BEEN USED. One-way and idempotent: the collider goes off
        /// immediately (a <c>Destroy</c> does not take effect until the end of the frame, and a
        /// live collider in those milliseconds can still answer a tap), the beacon stops, and
        /// every later tap is nothing at all.
        ///
        /// A ladder is a one-shot object — it exists to be used exactly once — so this is the
        /// state that says so, rather than a timing window that hopes nothing arrives late.</summary>
        internal void Consume()
        {
            if (_consumed)
            {
                return;
            }

            _consumed = true;

            var col = GetComponent<Collider2D>();
            if (col != null)
            {
                col.enabled = false;
            }
        }

        /// <summary>TEST HOOK. Has this ladder already been taken?</summary>
        internal bool TestConsumed => _consumed;

        private void Update()
        {
            if (_consumed)
            {
                return; // a taken ladder stops calling attention to itself
            }

            float dt = Time.deltaTime;

            _swayPhase += dt * SwayRate;
            transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(_swayPhase) * SwayDegrees);

            // The chevron nods DOWNWARD on its own faster beat: it dips away from the ladder and
            // eases back, so the motion itself points the way rather than just wobbling.
            if (_arrow != null)
            {
                float inv = Mathf.Abs(_restScale.y) > 0.0001f ? 1f / _restScale.y : 1f;
                float bob = (Mathf.Sin(_swayPhase * (ArrowBobRate / SwayRate)) - 1f) * 0.5f;
                _arrow.localPosition = _arrowRest + new Vector3(0f, bob * ArrowBobUnits * inv, 0f);

                if (_arrowRenderer != null)
                {
                    _arrowRenderer.color = new Color(1f, 1f, 1f, 0.72f - bob * 0.28f);
                }
            }

            _glintTimer -= dt;
            if (_glintTimer <= 0f)
            {
                _glintTimer = GlintSeconds;
                Glint();
            }
        }

        private void Glint()
        {
            _glints++;
            if (_sparkle != null)
            {
                _sparkle.Play();
                _sparkle.Emit(6);
            }
        }

        /// <summary>Down we go. RESTING-SCALE SAFE: the acknowledging punch hands the transform
        /// over from any in-flight punch and re-bases first, so a toddler hammering the ladder
        /// while the camera dips cannot inflate it.</summary>
        public void OnTapped(Vector2 worldPoint)
        {
            if (_consumed)
            {
                return; // already taken: a second tap on the way down is simply nothing
            }

            // CONSUMED BY THE TAP ITSELF, before anything downstream runs. The owner guards the
            // descent too (one per layer), but a one-shot object should be the one that knows it
            // is spent — that way no future caller has to remember.
            Consume();

            _taps++;
            Tween.CancelPunch(transform);
            transform.localScale = _restScale;
            Tween.PunchScale(transform, 0.3f, 0.3f);

            if (_sparkle != null)
            {
                _sparkle.Play();
                _sparkle.Emit(14);
            }

            // The owner owns the guard: a second tap while a descent is already in flight is a
            // no-op there, so the ladder never has to know whether it is too late.
            _owner?.DescendLayer();
        }
    }
}
