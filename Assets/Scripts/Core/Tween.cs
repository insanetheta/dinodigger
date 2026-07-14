using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DinoDigger.Core
{
    /// <summary>
    /// Tiny coroutine-based tween helper. No external dependencies (no DOTween).
    /// All tweens are driven by a single hidden <see cref="TweenRunner"/> that is
    /// created on demand. Every tween null-checks its target each frame so
    /// destroying an object mid-tween is safe.
    /// </summary>
    public static class Tween
    {
        private static TweenRunner _runner;

        private static TweenRunner Runner
        {
            get
            {
                if (_runner == null)
                {
                    var go = new GameObject("~TweenRunner")
                    {
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    _runner = go.AddComponent<TweenRunner>();
                }

                return _runner;
            }
        }

        // ----- Easing -----

        public static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);

        public static float EaseInOutCubic(float t) =>
            t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;

        public static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        // ----- Public API -----

        /// <summary>Run a normalized 0..1 tween. Returns the running coroutine.</summary>
        public static Coroutine Run(float duration, Action<float> onUpdate,
            Action onComplete = null, Func<float, float> ease = null)
        {
            if (duration <= 0f)
            {
                onUpdate?.Invoke(1f);
                onComplete?.Invoke();
                return null;
            }

            return Runner.StartCoroutine(RunRoutine(duration, onUpdate, onComplete, ease));
        }

        /// <summary>Delay an action by <paramref name="seconds"/> (unscaled-safe via realtime? no, uses scaled).</summary>
        public static Coroutine After(float seconds, Action action)
        {
            return Runner.StartCoroutine(AfterRoutine(seconds, action));
        }

        // Active punches by transform. A re-punch mid-swell must reuse the ORIGINAL
        // base scale — capturing localScale while inflated compounds forever
        // (the giant-blueberry bug).
        private static readonly Dictionary<Transform, (Coroutine co, Vector3 baseScale)> _punches =
            new Dictionary<Transform, (Coroutine, Vector3)>();

        /// <summary>Bouncy punch on local scale then settle back to the pre-punch scale.
        /// Re-punching a transform mid-punch restores and reuses its original base scale.</summary>
        public static Coroutine PunchScale(Transform target, float amount = 0.25f, float duration = 0.3f)
        {
            if (target == null)
            {
                return null;
            }

            Vector3 baseScale;
            if (_punches.TryGetValue(target, out var active))
            {
                Stop(active.co);
                baseScale = active.baseScale;
                target.localScale = baseScale;
            }
            else
            {
                baseScale = target.localScale;
            }

            Coroutine co = Run(duration, t =>
            {
                if (target == null)
                {
                    return;
                }

                // sin envelope that decays: overshoot then settle
                float envelope = Mathf.Sin(t * Mathf.PI) * (1f - t);
                target.localScale = baseScale * (1f + amount * envelope);
            }, () =>
            {
                if (target != null)
                {
                    target.localScale = baseScale;
                }

                _punches.Remove(target); // remove even for destroyed targets so the map can't leak
            });

            if (co != null)
            {
                _punches[target] = (co, baseScale);
            }

            return co;
        }

        /// <summary>Smooth scale from current to <paramref name="to"/>.</summary>
        public static Coroutine ScaleTo(Transform target, Vector3 to, float duration = 0.35f,
            Action onComplete = null)
        {
            if (target == null)
            {
                return null;
            }

            Vector3 from = target.localScale;
            return Run(duration, t =>
            {
                if (target != null)
                {
                    target.localScale = Vector3.LerpUnclamped(from, to, EaseOutBack(t));
                }
            }, onComplete);
        }

        /// <summary>Parabolic hop from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static Coroutine MoveArc(Transform target, Vector3 from, Vector3 to, float height,
            float duration = 0.5f, Action onComplete = null, Func<float, float> ease = null)
        {
            if (target == null)
            {
                return null;
            }

            ease = ease ?? EaseOutCubic;
            return Run(duration, t =>
            {
                if (target == null)
                {
                    return;
                }

                float e = ease(t);
                Vector3 pos = Vector3.LerpUnclamped(from, to, e);
                pos.y += height * 4f * t * (1f - t); // parabola peaking at t=0.5
                target.position = pos;
            }, onComplete);
        }

        /// <summary>Linear/eased move to a world position.</summary>
        public static Coroutine MoveTo(Transform target, Vector3 to, float duration = 0.4f,
            Action onComplete = null, Func<float, float> ease = null)
        {
            if (target == null)
            {
                return null;
            }

            ease = ease ?? EaseInOutCubic;
            Vector3 from = target.position;
            return Run(duration, t =>
            {
                if (target != null)
                {
                    target.position = Vector3.LerpUnclamped(from, to, ease(t));
                }
            }, onComplete);
        }

        /// <summary>Wobble rotation around Z (degrees), decaying to zero.</summary>
        public static Coroutine ShakeRotation(Transform target, float degrees = 15f,
            float duration = 0.5f, int shakes = 3, Action onComplete = null)
        {
            if (target == null)
            {
                return null;
            }

            Quaternion baseRot = target.localRotation;
            return Run(duration, t =>
            {
                if (target == null)
                {
                    return;
                }

                float decay = 1f - t;
                float angle = Mathf.Sin(t * Mathf.PI * shakes * 2f) * degrees * decay;
                target.localRotation = baseRot * Quaternion.Euler(0f, 0f, angle);
            }, () =>
            {
                if (target != null)
                {
                    target.localRotation = baseRot;
                }

                onComplete?.Invoke(); // must fire even if the target died — egg hatch depends on it
            });
        }

        public static void Stop(Coroutine c)
        {
            if (c != null && _runner != null)
            {
                _runner.StopCoroutine(c);
            }
        }

        // ----- Coroutine bodies -----

        private static IEnumerator RunRoutine(float duration, Action<float> onUpdate,
            Action onComplete, Func<float, float> ease)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                onUpdate?.Invoke(ease != null ? ease(t) : t);
                yield return null;
            }

            onUpdate?.Invoke(ease != null ? ease(1f) : 1f);
            onComplete?.Invoke();
        }

        private static IEnumerator AfterRoutine(float seconds, Action action)
        {
            if (seconds > 0f)
            {
                yield return new WaitForSeconds(seconds);
            }

            action?.Invoke();
        }
    }

    /// <summary>Hidden MonoBehaviour that hosts tween coroutines.</summary>
    public class TweenRunner : MonoBehaviour
    {
    }
}
