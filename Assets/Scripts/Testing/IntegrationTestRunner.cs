using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DinoDigger.Core;
using DinoDigger.Dig;
using DinoDigger.Overworld;

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

        // TOWN TUNING BACKSTOP. GameConfig is a ScriptableObject ASSET: a value a case writes at
        // runtime sticks to the asset for the rest of the editor session — it survives leaving
        // play mode, so it leaks into every later case AND every later suite RUN. Each case does
        // restore its own knobs in a finally (and those finallys really do run: RunCase disposes
        // every abandoned enumerator on timeout, which executes their finally blocks), but a
        // single missed restore is invisible until some unrelated case starts failing on the
        // second run of the day. These are the town knobs cases park — accelerated or frozen
        // build pacing, ambient visits, cheer windows — re-asserted after EVERY case so no run
        // can inherit the previous one's tuning.
        private float _cfgPerBuildState;
        private float _cfgSnackSeconds;
        private float _cfgCheerMultiplier;
        private float _cfgCheerSeconds;
        private float _cfgRecessSeconds;
        private float _cfgVisitInterval;
        private float _cfgVisitBeat;
        private int _cfgMaxVisits;

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
                _cfgPerBuildState = gm.TestConfig.TownSecondsPerBuildState;
                _cfgSnackSeconds = gm.TestConfig.SnackWorkSeconds;
                _cfgCheerMultiplier = gm.TestConfig.TownCheerMultiplier;
                _cfgCheerSeconds = gm.TestConfig.TownCheerSeconds;
                _cfgRecessSeconds = gm.TestConfig.RecessSeconds;
                _cfgVisitInterval = gm.TestConfig.TownVisitIntervalSeconds;
                _cfgVisitBeat = gm.TestConfig.TownVisitBeatSeconds;
                _cfgMaxVisits = gm.TestConfig.TownMaxVisits;
            }

            // START EVERY RUN BROKE. The save file outlives play mode (GameManager.OnDestroy
            // writes on exit), so run N+1 of the suite boots holding whatever treasure run N
            // banked — and the town builder auto-spends the instant it can afford a plot. That
            // makes a second run of the same suite a DIFFERENT run: builds break ground during
            // cases that never funded one, on plots those cases did not choose. Zeroing the
            // in-memory wallet here is the one save field that changes behaviour on its own
            // (dinos and town progress are wiped per case by TestReset), and it is what makes a
            // suite run reproducible instead of a function of the day's history.
            if (gm.Save != null && gm.Save.Data != null)
            {
                gm.Save.Data.TreasureCount = 0;
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

                // Town tuning: see the field comments — a ScriptableObject asset mutation
                // outlives play mode, so this backstop is what keeps run 2 of the suite from
                // playing under run 1's leftover pacing.
                gm.TestConfig.TownSecondsPerBuildState = _cfgPerBuildState;
                gm.TestConfig.SnackWorkSeconds = _cfgSnackSeconds;
                gm.TestConfig.TownCheerMultiplier = _cfgCheerMultiplier;
                gm.TestConfig.TownCheerSeconds = _cfgCheerSeconds;
                gm.TestConfig.RecessSeconds = _cfgRecessSeconds;
                gm.TestConfig.TownVisitIntervalSeconds = _cfgVisitInterval;
                gm.TestConfig.TownVisitBeatSeconds = _cfgVisitBeat;
                gm.TestConfig.TownMaxVisits = _cfgMaxVisits;
            }

            // Static test pins are always cleared here as well as in each case's own finally:
            // a pin that leaked into a later case would change that case's gameplay silently
            // (a frozen build queue, an unstaffed dig crew), which is far worse to debug than
            // the flake it was pinning.
            TownController.TestSuspendBuilds = false;
            DigModeController.TestSuppressCrew = false;
            DigModeController.TestForceSurpriseKind = -1;
            DigModeController.TestSuppressToys = false;
            DigModeController.TestSuppressBones = false;

            // Dig Loop 2.0 D3 pins, cleared for exactly the same reason as every pin above them:
            // a leaked ladder suppression would silently withhold the way down from a case that
            // expects it, and a leaked Glow suppression would leave a case asserting raw peek
            // alphas passing for the wrong reason.
            DigModeController.TestSuppressLadder = false;
            DigModeController.TestSuppressGlow = false;

            // The toy roller's no-repeat history (DinoDigger-qhy) is static AND mirrored into the
            // save, so a feature rolled by one case would steer the next case's first site. Wiped
            // here for the same reason as every pin above: a case must never inherit state it did
            // not ask for.
            DigModeController.TestResetPrimaryToy();
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
