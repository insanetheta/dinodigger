using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;

namespace DinoDigger.Overworld
{
    /// <summary>
    /// Gently follows the backhoe with a rectangular deadzone during Roam, and
    /// eases to the dig-view center (with a zoom) during Dig. A single camera is
    /// moved between the two areas — see SceneBuilder notes.
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
            if (_camera != null && _config != null)
            {
                _camera.orthographic = true;
                _camera.orthographicSize = _config.RoamOrthoSize;
            }
        }

        public void SetTarget(Transform target) => _target = target;

        private void LateUpdate()
        {
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

        /// <summary>Ease into the dig view centered on <paramref name="digCenter"/> at the
        /// config's standard dig framing.</summary>
        public void EnterDig(Vector3 digCenter, System.Action onArrived)
        {
            EnterDig(digCenter, _config != null ? _config.DigOrthoSize : 3.2f, onArrived);
        }

        /// <summary>Ease into the dig view at a framing the SITE chose. A mega-fossil dig
        /// (DinoDigger-84f) opens a much bigger pit and needs a wider frame, and the site is the
        /// only thing that knows how big its own board is — so the size travels in with the
        /// centre rather than being read from config here.</summary>
        public void EnterDig(Vector3 digCenter, float orthoSize, System.Action onArrived)
        {
            _digCenter = digCenter;
            _transitioning = true;
            Vector3 from = transform.position;
            Vector3 to = new Vector3(digCenter.x, digCenter.y, from.z);
            float dur = _config != null ? _config.TransitionSeconds : 0.5f;
            float fromSize = _camera != null ? _camera.orthographicSize : 5.5f;
            float toSize = orthoSize > 0.1f
                ? orthoSize
                : (_config != null ? _config.DigOrthoSize : 3.2f);

            Tween.Stop(_move);
            _move = Tween.Run(dur, t =>
            {
                if (_camera == null)
                {
                    return;
                }

                transform.position = Vector3.Lerp(from, to, t);
                _camera.orthographicSize = Mathf.Lerp(fromSize, toSize, t);
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
            Vector3 from = transform.position;
            Vector3 to = new Vector3(worldPoint.x, worldPoint.y, from.z);
            float dur = _config != null ? _config.TransitionSeconds : 0.5f;
            float fromSize = _camera != null ? _camera.orthographicSize : 5.5f;
            float toSize = _config != null ? _config.CeremonyOrthoSize : 4f;

            Tween.Stop(_move);
            _move = Tween.Run(dur, t =>
            {
                if (_camera == null)
                {
                    return;
                }

                transform.position = Vector3.Lerp(from, to, t);
                _camera.orthographicSize = Mathf.Lerp(fromSize, toSize, t);
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
            Vector3 from = transform.position;
            Vector3 to = _target != null
                ? new Vector3(_target.position.x, _target.position.y, from.z)
                : from;
            float dur = _config != null ? _config.TransitionSeconds : 0.5f;
            float fromSize = _camera != null ? _camera.orthographicSize : 4f;
            float toSize = _config != null ? _config.RoamOrthoSize : 5.5f;

            Tween.Stop(_move);
            _move = Tween.Run(dur, t =>
            {
                if (_camera == null)
                {
                    return;
                }

                transform.position = Vector3.Lerp(from, to, t);
                _camera.orthographicSize = Mathf.Lerp(fromSize, toSize, t);
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

            if (_camera != null && _config != null)
            {
                _camera.orthographicSize = _config.RoamOrthoSize;
            }

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
            Vector3 from = transform.position;
            Vector3 to = _target != null
                ? new Vector3(_target.position.x, _target.position.y, from.z)
                : from;
            float dur = _config != null ? _config.TransitionSeconds : 0.5f;
            float fromSize = _camera != null ? _camera.orthographicSize : 3.2f;
            float toSize = _config != null ? _config.RoamOrthoSize : 5.5f;

            Tween.Stop(_move);
            _move = Tween.Run(dur, t =>
            {
                if (_camera == null)
                {
                    return;
                }

                transform.position = Vector3.Lerp(from, to, t);
                _camera.orthographicSize = Mathf.Lerp(fromSize, toSize, t);
            }, () =>
            {
                _move = null;
                _transitioning = false;
                onArrived?.Invoke();
            }, Tween.EaseInOutCubic);
        }
    }
}
