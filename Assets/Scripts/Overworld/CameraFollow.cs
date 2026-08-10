using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;

namespace DinoDigger.Overworld
{
    /// <summary>
    /// Gently follows the backhoe with a rectangular deadzone during Roam, and
    /// eases to the dig-view center (with a zoom) during Dig. A single camera is
    /// moved between the two areas — see SceneBuilder notes.
    ///
    /// FRAMING IS FIT-TO-CONTENT, NOT A NUMBER (DinoDigger-kgm). This component never stores an
    /// ortho size; it stores a <see cref="CameraFit"/> — the world rect the current view has to
    /// show — and resolves it against the LIVE aspect every time it needs a size. Two things
    /// fall out of that for free:
    ///
    ///   PORTRAIT WORKS. Ortho is half the VERTICAL extent, so a fixed size crops horizontally
    ///     the moment the screen gets narrow (the dig grid was clipped at 9:19.5). Deriving the
    ///     size from content makes every aspect correct by construction.
    ///   ROTATING IS FREE. The size is re-derived, not remembered — including from inside a
    ///     transition tween, which reads the fit on every step and therefore LANDS on the right
    ///     size even if the device turned halfway through the glide.
    ///
    /// The config's ortho values survive as LANDSCAPE BASELINES (minimums), so a desktop or a
    /// phone held sideways frames exactly what it always did.
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        [SerializeField] private Camera _camera;
        [SerializeField] private Transform _target;   // backhoe
        [SerializeField] private GameConfig _config;

        private Vector3 _digCenter;
        private bool _digMode;
        private bool _focusMode;   // parked on a focus point (nest ceremony, town tour) — no roam follow
        private bool _transitioning;

        // The framing the camera is AT, or (mid-transition) the one it is easing TOWARD. This
        // is the single source of truth for size: nothing else may write orthographicSize.
        private CameraFit _fit;

        // Aspect the current size was derived from. A change here — a phone rotating, a WebGL
        // canvas resizing under a browser rotate — is the whole reframe trigger.
        private float _appliedAspect = -1f;
        private const float AspectEpsilon = 0.0005f;

        // The camera move currently in flight. Every transition below stops the previous one
        // before starting its own: two live tweens would both write transform.position each
        // frame and the loser would still be fighting for it. This is what lets a glide be
        // CANCELLED mid-flight (the idle-attract town tour, DinoDigger-sbc, turns straight
        // round on a player tap) instead of finishing on top of the reversal.
        private Coroutine _move;

        // TEST HOOKS (integration runner; no reflection).
        internal bool TestFocused => _focusMode;
        internal bool TestTransitioning => _transitioning;
        internal Transform TestTarget => _target;
        internal float TestOrthoSize => _camera != null ? _camera.orthographicSize : 0f;
        internal CameraFit TestFit => _fit;
        internal float TestAppliedAspect => _appliedAspect;

        private void Awake()
        {
            if (_camera == null)
            {
                _camera = GetComponent<Camera>();
            }
        }

        public void Configure(Camera cam, Transform target, GameConfig config)
        {
            _camera = cam;
            _target = target;
            _config = config;
            if (_camera != null)
            {
                _camera.orthographic = true;
                SetFraming(RoamFit());

                // COVERAGE (DinoDigger-5k8.1). Framing the camera correctly at every aspect is
                // only half the promise: what it can see has to be PAINTED. The backstop quad
                // rides the camera and guarantees there is never nothing there, and the clear
                // colour matches it so the very first frame agrees with every frame after.
                Color sea = _config != null ? _config.SeaColor : new Color(0.459f, 0.698f, 0.882f);
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = sea;
                CameraBackdrop.Ensure(_camera, sea);
            }
        }

        public void SetTarget(Transform target) => _target = target;

        /// <summary>The overworld framing: a target visible world WIDTH floored at the landscape
        /// baseline, so landscape is untouched and portrait zooms out instead of showing a
        /// letterbox slot of island.</summary>
        private CameraFit RoamFit()
        {
            return _config != null ? _config.RoamFit() : CameraFit.Fixed(5.5f);
        }

        /// <summary>The ceremony / attract-tour push-in framing.</summary>
        private CameraFit FocusFit()
        {
            return _config != null ? _config.CeremonyFit() : CameraFit.Fixed(4f);
        }

        /// <summary>Adopt a framing and apply it NOW. Used at boot and by the snap hooks; the
        /// transitions below adopt the framing first and let their tween ease into it.</summary>
        private void SetFraming(CameraFit fit)
        {
            _fit = fit;
            if (_camera != null)
            {
                _appliedAspect = CameraFraming.ScreenAspect;
                _camera.orthographicSize = _fit.Ortho(_appliedAspect);
            }
        }

        /// <summary>The size the ACTIVE framing wants on the screen as it is right now. Every
        /// transition tween lerps toward this rather than toward a value captured when it
        /// started, which is what makes a rotate mid-glide land on the correct final size
        /// instead of on the size the old orientation asked for.</summary>
        private float TargetOrtho()
        {
            _appliedAspect = CameraFraming.ScreenAspect;
            return _fit.Ortho(_appliedAspect);
        }

        /// <summary>
        /// LIVE REFRAMING. Polls the aspect once a frame — a float compare, cheaper than the
        /// event plumbing it would replace — and re-derives the size whenever the screen
        /// changes shape. This is the whole answer to "the browser canvas resizes on rotate":
        /// there is nothing to subscribe to in WebGL that beats noticing.
        ///
        /// Mid-transition it only records the new aspect and gets out of the way: the tween is
        /// already reading <see cref="TargetOrtho"/> every step, and a second writer would fight
        /// it for the same frame.
        /// </summary>
        private void ApplyFramingIfAspectChanged()
        {
            if (_camera == null)
            {
                return;
            }

            float aspect = CameraFraming.ScreenAspect;
            if (Mathf.Abs(aspect - _appliedAspect) < AspectEpsilon)
            {
                return;
            }

            _appliedAspect = aspect;
            if (_transitioning)
            {
                return;
            }

            if (!_fit.IsValid)
            {
                _fit = RoamFit();
            }

            _camera.orthographicSize = _fit.Ortho(aspect);
        }

        private void LateUpdate()
        {
            // Reframing runs BEFORE the follow guard: a rotate has to be answered while parked
            // in the dig view or on a ceremony too, not only while roaming.
            ApplyFramingIfAspectChanged();

            if (_digMode || _focusMode || _transitioning || _target == null || _camera == null || _config == null)
            {
                return;
            }

            Vector3 cam = transform.position;
            Vector3 tgt = _target.position;
            Vector2 dz = _config.CameraDeadzone;

            float dx = tgt.x - cam.x;
            float dy = tgt.y - cam.y;
            Vector3 desired = cam;

            if (Mathf.Abs(dx) > dz.x)
            {
                desired.x = tgt.x - Mathf.Sign(dx) * dz.x;
            }

            if (Mathf.Abs(dy) > dz.y)
            {
                desired.y = tgt.y - Mathf.Sign(dy) * dz.y;
            }

            float k = 1f - Mathf.Exp(-_config.CameraFollowLerp * Time.deltaTime);
            Vector3 next = Vector3.Lerp(cam, desired, k);
            next.z = cam.z;
            transform.position = next;
        }

        /// <summary>Ease into the dig view at a framing the SITE chose. A mega-fossil dig
        /// (DinoDigger-84f) opens a much bigger pit and needs a wider frame, and the site is the
        /// only thing that knows how big its own board is — so the FIT travels in with the
        /// centre rather than being read from config here. A fit rather than a size, so the
        /// same request frames the pit correctly on any screen and keeps doing so if the screen
        /// changes shape while the camera is still flying in.</summary>
        public void EnterDig(Vector3 digCenter, CameraFit fit, System.Action onArrived)
        {
            _digCenter = digCenter;
            _transitioning = true;
            _fit = fit.IsValid ? fit : CameraFit.Fixed(_config != null ? _config.DigOrthoSize : 3.2f);
            Vector3 from = transform.position;
            Vector3 to = new Vector3(digCenter.x, digCenter.y, from.z);
            float dur = _config != null ? _config.TransitionSeconds : 0.5f;
            float fromSize = _camera != null ? _camera.orthographicSize : 5.5f;

            Tween.Stop(_move);
            _move = Tween.Run(dur, t =>
            {
                if (_camera == null)
                {
                    return;
                }

                transform.position = Vector3.Lerp(from, to, t);
                _camera.orthographicSize = Mathf.Lerp(fromSize, TargetOrtho(), t);
            }, () =>
            {
                _move = null;
                _transitioning = false;
                _digMode = true;
                onArrived?.Invoke();
            }, Tween.EaseInOutCubic);
        }

        /// <summary>Ease to focus on a world point (the nest ceremony; the idle-attract town
        /// tour, DinoDigger-sbc), pushing in to the ceremony ortho size. Reuses the same
        /// EaseInOutCubic as the dig transition and stays parked there (no roam follow) until
        /// <see cref="ExitFocus"/>. Calling ExitFocus mid-glide is well defined: it stops this
        /// tween and reverses from wherever the camera has got to.</summary>
        public void EnterFocus(Vector3 worldPoint, System.Action onArrived)
        {
            _focusMode = true;
            _transitioning = true;
            _fit = FocusFit();
            Vector3 from = transform.position;
            Vector3 to = new Vector3(worldPoint.x, worldPoint.y, from.z);
            float dur = _config != null ? _config.TransitionSeconds : 0.5f;
            float fromSize = _camera != null ? _camera.orthographicSize : 5.5f;

            Tween.Stop(_move);
            _move = Tween.Run(dur, t =>
            {
                if (_camera == null)
                {
                    return;
                }

                transform.position = Vector3.Lerp(from, to, t);
                _camera.orthographicSize = Mathf.Lerp(fromSize, TargetOrtho(), t);
            }, () =>
            {
                _move = null;
                _transitioning = false;
                onArrived?.Invoke();
            }, Tween.EaseInOutCubic);
        }

        /// <summary>Ease back out from a focus point to following the backhoe.</summary>
        public void ExitFocus(System.Action onArrived)
        {
            _focusMode = false;
            _transitioning = true;
            _fit = RoamFit();
            Vector3 from = transform.position;
            Vector3 to = _target != null
                ? new Vector3(_target.position.x, _target.position.y, from.z)
                : from;
            float dur = _config != null ? _config.TransitionSeconds : 0.5f;
            float fromSize = _camera != null ? _camera.orthographicSize : 4f;

            Tween.Stop(_move);
            _move = Tween.Run(dur, t =>
            {
                if (_camera == null)
                {
                    return;
                }

                transform.position = Vector3.Lerp(from, to, t);
                _camera.orthographicSize = Mathf.Lerp(fromSize, TargetOrtho(), t);
            }, () =>
            {
                _move = null;
                _transitioning = false;
                onArrived?.Invoke();
            }, Tween.EaseInOutCubic);
        }

        /// <summary>TEST HOOK. Instantly cancel any dig/focus transition and snap to the roam
        /// view. Stops the in-flight move first — a snap that leaves a tween running is undone
        /// on the very next frame.</summary>
        internal void TestForceRoam()
        {
            Tween.Stop(_move);
            _move = null;
            _transitioning = false;
            _digMode = false;
            _focusMode = false;

            SetFraming(RoamFit());

            if (_target != null)
            {
                Vector3 p = transform.position;
                transform.position = new Vector3(_target.position.x, _target.position.y, p.z);
            }
        }

        /// <summary>Tiny camera nudge for a dig-site whumph (the boom geode, DinoDigger-z4d).
        ///
        /// Shakes around the dig framing this component ALREADY OWNS (<see cref="_digCenter"/>),
        /// never around a snapshot of wherever the transform happens to be: a snapshot taken
        /// while another tween is writing the position would settle the camera on a stale spot
        /// when the shake ends. Only runs while parked in the dig view — during a transition the
        /// move tween owns the transform, and a second writer would fight it — so a geode that
        /// goes off as the round ends simply shakes nothing.</summary>
        public void ShakeDig(float amplitude, float seconds)
        {
            if (_camera == null || !_digMode || _transitioning)
            {
                return;
            }

            float amp = Mathf.Clamp(amplitude, 0f, 0.5f);   // a nudge, never a jolt (toddler rule)
            float dur = Mathf.Clamp(seconds, 0.05f, 1f);
            if (amp <= 0.0001f)
            {
                return;
            }

            float z = transform.position.z;
            Vector3 basePos = new Vector3(_digCenter.x, _digCenter.y, z);
            float seed = Random.value * 10f;

            Tween.Run(dur, t =>
            {
                if (_camera == null)
                {
                    return;
                }

                // Decaying wobble on both axes, from two out-of-phase sines so it reads as a
                // thump rather than a rattle.
                float decay = 1f - Mathf.Clamp01(t);
                float dx = Mathf.Sin((seed + t) * 46f) * amp * decay;
                float dy = Mathf.Cos((seed + t) * 37f) * amp * decay * 0.7f;
                transform.position = basePos + new Vector3(dx, dy, 0f);
            }, () =>
            {
                if (_camera != null && _digMode && !_transitioning)
                {
                    transform.position = basePos; // always land back on the exact framing
                }
            });
        }

        /// <summary>THE DESCENT DIP (DinoDigger-dv1). Ease the dig view DOWN by
        /// <paramref name="units"/>, run <paramref name="onBottom"/> there, then ease back to the
        /// framing it came from. The new stratum is built at the bottom of the dip, so the child
        /// watches the world go down instead of being teleported into a different one.
        ///
        /// Guarded exactly like <see cref="ShakeDig"/>: it only runs while the camera is parked
        /// in the dig view (during a transition the move tween owns the transform and a second
        /// writer would fight it), and it works off the framing this component ALREADY OWNS
        /// (<see cref="_digCenter"/>) rather than a snapshot of the live position, so it always
        /// lands back on the exact frame. If it cannot run, the callback still fires immediately —
        /// the descent must never be conditional on the flourish.</summary>
        public void DipDig(float units, float seconds, System.Action onBottom)
        {
            float drop = Mathf.Clamp(units, 0f, 6f);
            float dur = Mathf.Clamp(seconds, 0f, 3f);
            if (_camera == null || !_digMode || _transitioning || drop <= 0.0001f || dur <= 0.01f)
            {
                onBottom?.Invoke();
                return;
            }

            float z = transform.position.z;
            Vector3 basePos = new Vector3(_digCenter.x, _digCenter.y, z);
            Vector3 bottom = basePos + new Vector3(0f, -drop, 0f);
            bool fired = false;

            Tween.Stop(_move);
            _move = Tween.Run(dur * 0.5f, t =>
            {
                if (_camera != null)
                {
                    transform.position = Vector3.Lerp(basePos, bottom, t);
                }
            }, () =>
            {
                if (!fired)
                {
                    fired = true;
                    onBottom?.Invoke();
                }

                _move = Tween.Run(dur * 0.5f, t =>
                {
                    if (_camera != null)
                    {
                        transform.position = Vector3.Lerp(bottom, basePos, t);
                    }
                }, () =>
                {
                    _move = null;
                    if (_camera != null && _digMode && !_transitioning)
                    {
                        transform.position = basePos; // always land back on the exact framing
                    }
                }, Tween.EaseInOutCubic);
            }, Tween.EaseInOutCubic);
        }

        /// <summary>Ease back out to following the backhoe.</summary>
        public void ExitDig(System.Action onArrived)
        {
            _digMode = false;
            _transitioning = true;
            _fit = RoamFit();
            Vector3 from = transform.position;
            Vector3 to = _target != null
                ? new Vector3(_target.position.x, _target.position.y, from.z)
                : from;
            float dur = _config != null ? _config.TransitionSeconds : 0.5f;
            float fromSize = _camera != null ? _camera.orthographicSize : 3.2f;

            Tween.Stop(_move);
            _move = Tween.Run(dur, t =>
            {
                if (_camera == null)
                {
                    return;
                }

                transform.position = Vector3.Lerp(from, to, t);
                _camera.orthographicSize = Mathf.Lerp(fromSize, TargetOrtho(), t);
            }, () =>
            {
                _move = null;
                _transitioning = false;
                onArrived?.Invoke();
            }, Tween.EaseInOutCubic);
        }
    }
}
