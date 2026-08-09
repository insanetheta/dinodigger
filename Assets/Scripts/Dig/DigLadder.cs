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

        private DigModeController _owner;
        private PlaceholderLibrary _lib;
        private ParticleSystem _sparkle;
        private float _swayPhase;
        private float _glintTimer;
        private int _glints;
        private int _taps;
        private bool _consumed;
        private Vector3 _restScale = Vector3.one;

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

            // Arrive with a pop AND a ding, so the moment it earns its place lands even if the
            // child happens to be looking at the other side of the pit — the sound is what
            // turns a head the pop alone would miss.
            transform.localScale = _restScale * 0.2f;
            Tween.ScaleTo(transform, _restScale, 0.35f);
            gm?.Audio?.LadderDing();
            Glint();
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
