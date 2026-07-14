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
        private bool _focusMode;   // parked on a focus point (nest ceremony) — no roam follow
        private bool _transitioning;

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

        /// <summary>Ease into the dig view centered on <paramref name="digCenter"/>.</summary>
        public void EnterDig(Vector3 digCenter, System.Action onArrived)
        {
            _digCenter = digCenter;
            _transitioning = true;
            Vector3 from = transform.position;
            Vector3 to = new Vector3(digCenter.x, digCenter.y, from.z);
            float dur = _config != null ? _config.TransitionSeconds : 0.5f;
            float fromSize = _camera != null ? _camera.orthographicSize : 5.5f;
            float toSize = _config != null ? _config.DigOrthoSize : 3.2f;

            Tween.Run(dur, t =>
            {
                if (_camera == null)
                {
                    return;
                }

                transform.position = Vector3.Lerp(from, to, t);
                _camera.orthographicSize = Mathf.Lerp(fromSize, toSize, t);
            }, () =>
            {
                _transitioning = false;
                _digMode = true;
                onArrived?.Invoke();
            }, Tween.EaseInOutCubic);
        }

        /// <summary>Ease to focus on a world point (nest ceremony), pushing in to the
        /// ceremony ortho size. Reuses the same EaseInOutCubic as the dig transition and
        /// stays parked there (no roam follow) until <see cref="ExitFocus"/>.</summary>
        public void EnterFocus(Vector3 worldPoint, System.Action onArrived)
        {
            _focusMode = true;
            _transitioning = true;
            Vector3 from = transform.position;
            Vector3 to = new Vector3(worldPoint.x, worldPoint.y, from.z);
            float dur = _config != null ? _config.TransitionSeconds : 0.5f;
            float fromSize = _camera != null ? _camera.orthographicSize : 5.5f;
            float toSize = _config != null ? _config.CeremonyOrthoSize : 4f;

            Tween.Run(dur, t =>
            {
                if (_camera == null)
                {
                    return;
                }

                transform.position = Vector3.Lerp(from, to, t);
                _camera.orthographicSize = Mathf.Lerp(fromSize, toSize, t);
            }, () =>
            {
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

            Tween.Run(dur, t =>
            {
                if (_camera == null)
                {
                    return;
                }

                transform.position = Vector3.Lerp(from, to, t);
                _camera.orthographicSize = Mathf.Lerp(fromSize, toSize, t);
            }, () =>
            {
                _transitioning = false;
                onArrived?.Invoke();
            }, Tween.EaseInOutCubic);
        }

        /// <summary>TEST HOOK. Instantly cancel any dig transition and snap to the roam view.</summary>
        internal void TestForceRoam()
        {
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

            Tween.Run(dur, t =>
            {
                if (_camera == null)
                {
                    return;
                }

                transform.position = Vector3.Lerp(from, to, t);
                _camera.orthographicSize = Mathf.Lerp(fromSize, toSize, t);
            }, () =>
            {
                _transitioning = false;
                onArrived?.Invoke();
            }, Tween.EaseInOutCubic);
        }
    }
}
