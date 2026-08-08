using System;
using System.Collections;
using UnityEngine;
using DinoDigger.Core;

namespace DinoDigger.Testing
{
    /// <summary>Thrown by <see cref="TestContext.Assert"/> to fail the current case.</summary>
    public class TestFailure : Exception
    {
        public TestFailure(string message) : base(message) { }
    }

    /// <summary>One integration test: a name, a realtime timeout, and a coroutine body.</summary>
    public class TestCase
    {
        public readonly string Name;
        public readonly float Timeout;
        public readonly Func<TestContext, IEnumerator> Body;

        public TestCase(string name, float timeout, Func<TestContext, IEnumerator> body)
        {
            Name = name;
            Timeout = timeout;
            Body = body;
        }
    }

    /// <summary>
    /// Helpers handed to every test case: tap simulation (through the REAL input
    /// pipeline), frame/second waits, and assertions. All waits yield <c>null</c>
    /// so the runner's flat driver can enforce the per-case timeout every frame.
    /// </summary>
    public class TestContext
    {
        public GameManager GM { get; private set; }
        public string Detail { get; set; } = "";

        public TestContext(GameManager gm)
        {
            GM = gm;
        }

        public void ResetDetail()
        {
            Detail = "";
        }

        // ----- Tapping (real pipeline) -----

        public Vector2 WorldToScreen(Vector3 world)
        {
            Camera cam = GM != null ? GM.TestCamera : Camera.main;
            return cam != null ? (Vector2)cam.WorldToScreenPoint(world) : Vector2.zero;
        }

        /// <summary>Simulate a tap at a WORLD position via InputService.SimulateTap.</summary>
        public void TapWorld(Vector3 world)
        {
            // Make sure collider positions reflect any transforms we just moved so the
            // OverlapPoint raycast inside GameManager sees the current world.
            Physics2D.SyncTransforms();
            var input = GM != null ? GM.TestInput : null;
            if (input != null)
            {
                input.SimulateTap(WorldToScreen(world));
            }
        }

        // ----- Waits (all yield null; timeout enforced by the runner) -----

        public IEnumerator WaitFrames(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                yield return null;
            }
        }

        /// <summary>Wait in scaled game time (tracks Time.timeScale, same as tweens).</summary>
        public IEnumerator WaitSecondsScaled(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }

        /// <summary>Wait in wall-clock time (for unscaled-time behaviors like the parent gate).</summary>
        public IEnumerator WaitSecondsRealtime(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
            }
        }

        /// <summary>Poll until the condition holds. If it never does, the runner times the case out.</summary>
        public IEnumerator WaitUntil(Func<bool> condition)
        {
            while (!condition())
            {
                yield return null;
            }
        }

        /// <summary>
        /// Poll until the condition holds, failing with a NAMED message if it has not held
        /// within <paramref name="budgetSeconds"/> of wall clock. Prefer this over the
        /// unbounded overload for any wait that can legitimately never finish: the case then
        /// reports which step wedged instead of an opaque "timeout after Ns".
        ///
        /// Budgets are realtime (the runner's own timeout is) so keep them generous — this
        /// machine often runs two editors plus heavy agents, and a load hitch must never fail
        /// a case that is making progress. Where the wait scales with a distance or a step
        /// count, size the budget from that rather than picking a flat number.
        /// </summary>
        public IEnumerator WaitUntil(Func<bool> condition, float budgetSeconds, string what)
        {
            return WaitUntil(condition, budgetSeconds, () => what);
        }

        /// <summary>As above, but the message is built ON FAILURE — so it can report how far
        /// the wait actually got (counts seen, position reached) instead of the state the
        /// caller had before waiting.</summary>
        public IEnumerator WaitUntil(Func<bool> condition, float budgetSeconds, Func<string> what)
        {
            float deadline = Time.realtimeSinceStartup + budgetSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    throw new TestFailure($"{what()} (waited {budgetSeconds:F0}s)");
                }

                yield return null;
            }
        }

        // ----- Assertions -----

        public void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new TestFailure(message);
            }
        }

        public void Log(string detail)
        {
            Detail = detail;
        }
    }
}
