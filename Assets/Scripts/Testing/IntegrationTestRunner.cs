using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DinoDigger.Core;

namespace DinoDigger.Testing
{
    /// <summary>
    /// Plays through the whole game in PLAY MODE with simulated taps and asserts
    /// every feature end to end. Created automatically by the editor menu
    /// (DinoDigger/Run Integration Tests) via a RuntimeInitializeOnLoadMethod guard
    /// that checks an EditorPrefs flag; it never runs during a normal play session.
    ///
    /// Each case runs on a flat coroutine driver that enforces a per-case realtime
    /// timeout and catches assertion failures, so one broken case can never hang or
    /// cascade into the others. State is reset between cases via GameManager test
    /// hooks (no reflection — reflection crashes the editor MCP bridge).
    ///
    /// On completion it logs a COMPLETE line, writes Logs/integration_report.json,
    /// and (in the editor) calls EditorApplication.ExitPlaymode itself — that proved
    /// more reliable than an external editor watcher.
    /// </summary>
    public partial class IntegrationTestRunner : MonoBehaviour
    {
        public const string RunFlagKey = "DinoDigger.RunIntegrationTests";
        private const string LogPrefix = "[IntegrationTest]";

        // Speeds up long in-game waits (tweens, camera transitions) without touching
        // unscaled-time behaviors (the parent-gate hold uses unscaledDeltaTime).
        private const float TestTimeScale = 3f;

        [Serializable]
        private class CaseResult
        {
            public string name;
            public bool pass;
            public string detail;
            public float seconds;
        }

        [Serializable]
        private class Report
        {
            public List<CaseResult> cases = new List<CaseResult>();
            public int passed;
            public int failed;
        }

        private readonly List<CaseResult> _results = new List<CaseResult>();
        private readonly List<string> _errors = new List<string>();
        private float _originalTimeScale = 1f;
        private float _cfgRespawn;
        private float _cfgParentGate;

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            // Second layer against the WebGL "pause when unfocused" editor emulation:
            // an unfocused editor freezes the player loop at frame ~1-2, so this must be
            // the very first thing the runtime bootstrap does — before any frame is needed.
            Application.runInBackground = true;

            if (!UnityEditor.EditorPrefs.GetBool(RunFlagKey, false))
            {
                return;
            }

            // Consume the flag immediately so a later manual Play doesn't re-trigger.
            UnityEditor.EditorPrefs.SetBool(RunFlagKey, false);

            var go = new GameObject("~IntegrationTestRunner");
            go.AddComponent<IntegrationTestRunner>();
        }
#endif

        private void Start()
        {
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            Application.logMessageReceived += OnLog;
            Application.runInBackground = true;

            // Wait for the game to boot (GameManager.Awake + Start) before touching it.
            float bootDeadline = Time.realtimeSinceStartup + 20f;
            while (GameManager.Instance == null && Time.realtimeSinceStartup < bootDeadline)
            {
                yield return null;
            }

            yield return new WaitForSeconds(0.25f); // let Start() finish (save load, restore)

            GameManager gm = GameManager.Instance;
            if (gm == null)
            {
                Debug.Log($"{LogPrefix} FAIL bootstrap — GameManager.Instance never appeared");
                WriteReport();
                Finish();
                yield break;
            }

            _originalTimeScale = Time.timeScale;
            Time.timeScale = TestTimeScale;

            // Snapshot config values that some cases override, so we can always restore
            // them between cases — even if a case times out before its own cleanup.
            if (gm.TestConfig != null)
            {
                _cfgRespawn = gm.TestConfig.MoundRespawnSeconds;
                _cfgParentGate = gm.TestConfig.ParentGateHoldSeconds;
            }

            var ctx = new TestContext(gm);
            List<TestCase> cases = BuildCases();

            for (int i = 0; i < cases.Count; i++)
            {
                // Clean slate before every case.
                SafeReset(gm);
                yield return ctx.WaitFrames(2);

                yield return RunCase(cases[i], ctx);

                // Restore any config a case may have changed.
                RestoreConfig(gm);
                yield return ctx.WaitFrames(1);
            }

            int passed = 0, failed = 0;
            for (int i = 0; i < _results.Count; i++)
            {
                if (_results[i].pass) passed++;
                else failed++;
            }

            float duration = 0f;
            for (int i = 0; i < _results.Count; i++)
            {
                duration += _results[i].seconds;
            }

            Debug.Log($"{LogPrefix} COMPLETE passed={passed} failed={failed} durationSec={duration:F1}");
            WriteReport();

            Time.timeScale = _originalTimeScale;
            Application.logMessageReceived -= OnLog;

            Finish();
        }

        /// <summary>Flat driver: steps the case enumerator, flattening nested waits and
        /// enforcing the timeout + catching assertion failures on every frame.</summary>
        private IEnumerator RunCase(TestCase c, TestContext ctx)
        {
            ctx.ResetDetail();
            float start = Time.realtimeSinceStartup;
            float deadline = start + c.Timeout;

            var stack = new Stack<IEnumerator>();
            stack.Push(c.Body(ctx));

            bool failed = false;
            string failDetail = "";

            while (stack.Count > 0)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    failed = true;
                    failDetail = $"timeout after {c.Timeout:F0}s";
                    break;
                }

                IEnumerator top = stack.Peek();
                bool moved = false;
                object current = null;

                try
                {
                    moved = top.MoveNext();
                    if (moved)
                    {
                        current = top.Current;
                    }
                }
                catch (TestFailure tf)
                {
                    failed = true;
                    failDetail = tf.Message;
                    break;
                }
                catch (Exception ex)
                {
                    failed = true;
                    failDetail = $"{ex.GetType().Name}: {ex.Message}";
                    break;
                }

                if (!moved)
                {
                    stack.Pop();
                    continue;
                }

                if (current is IEnumerator sub)
                {
                    stack.Push(sub);
                    continue;
                }

                yield return current; // null / WaitForSeconds etc.
            }

            // Dispose any abandoned enumerators so their finally blocks (cleanup) run.
            if (failed)
            {
                foreach (IEnumerator e in stack)
                {
                    (e as IDisposable)?.Dispose();
                }
            }

            float seconds = Time.realtimeSinceStartup - start;
            bool pass = !failed;
            string detail = failed ? failDetail : (string.IsNullOrEmpty(ctx.Detail) ? "ok" : ctx.Detail);

            _results.Add(new CaseResult { name = c.Name, pass = pass, detail = detail, seconds = seconds });
            Debug.Log($"{LogPrefix} {(pass ? "PASS" : "FAIL")} {c.Name} — {detail}");
        }

        private void SafeReset(GameManager gm)
        {
            try
            {
                gm.TestReset();
            }
            catch (Exception ex)
            {
                Debug.Log($"{LogPrefix} reset warning: {ex.Message}");
            }
        }

        private void RestoreConfig(GameManager gm)
        {
            if (gm != null && gm.TestConfig != null)
            {
                gm.TestConfig.MoundRespawnSeconds = _cfgRespawn;
                gm.TestConfig.ParentGateHoldSeconds = _cfgParentGate;
            }
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                // Ignore our own PASS/FAIL lines — those go through Debug.Log (LogType.Log).
                _errors.Add($"{type}: {condition}");
            }
        }

        private void WriteReport()
        {
            var report = new Report();
            report.cases = _results;
            foreach (CaseResult r in _results)
            {
                if (r.pass) report.passed++;
                else report.failed++;
            }

            try
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string logsDir = Path.Combine(projectRoot, "Logs");
                if (!Directory.Exists(logsDir))
                {
                    Directory.CreateDirectory(logsDir);
                }

                string path = Path.Combine(logsDir, "integration_report.json");
                File.WriteAllText(path, JsonUtility.ToJson(report, true));
                Debug.Log($"{LogPrefix} report written to {path}");
            }
            catch (Exception ex)
            {
                Debug.Log($"{LogPrefix} could not write report: {ex.Message}");
            }
        }

        private void Finish()
        {
#if UNITY_EDITOR
            // Give the console/report a moment to flush, then leave play mode.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (UnityEditor.EditorApplication.isPlaying)
                {
                    UnityEditor.EditorApplication.ExitPlaymode();
                }
            };
#endif
        }
    }
}
