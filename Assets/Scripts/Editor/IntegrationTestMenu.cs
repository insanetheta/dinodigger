using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using DinoDigger.Testing;

namespace DinoDigger.EditorTools
{
    /// <summary>
    /// Editor entry point for the runtime integration tests.
    ///
    /// "DinoDigger/Run Integration Tests" makes sure the Main scene is open, arms an
    /// EditorPrefs flag, and enters play mode. On play, IntegrationTestRunner's
    /// RuntimeInitializeOnLoadMethod sees the flag, spawns itself, plays through every
    /// case with simulated taps, writes Logs/integration_report.json, logs a COMPLETE
    /// line, and exits play mode on its own.
    /// </summary>
    public static class IntegrationTestMenu
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";

        // Mirrors SaveManager.FileName; kept literal because that constant lives in a
        // different assembly (Assembly-CSharp) and is not visible to editor scripts.
        private const string SaveFileName = "dinodigger_save.json";

        private static byte[] _saveBackup;
        private static bool _saveExisted;

        [MenuItem("DinoDigger/Run Integration Tests")]
        public static void RunIntegrationTests()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[IntegrationTest] Already in play mode — stop first, then run.");
                return;
            }

            if (!File.Exists(ScenePath))
            {
                Debug.LogError($"[IntegrationTest] Scene not found at {ScenePath}. Build it via DinoDigger/Build Main Scene first.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // Preserve the player's real save around the run — the game saves on exit,
            // so we restore AFTER play mode fully ends (EnteredEditMode).
            BackupSave();
            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            // Arm the runner; it disarms the flag the moment it starts.
            EditorPrefs.SetBool(IntegrationTestRunner.RunFlagKey, true);

            Debug.Log("[IntegrationTest] Entering play mode to run integration tests…");
            EditorApplication.EnterPlaymode();
        }

        private static string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

        private static void BackupSave()
        {
            try
            {
                string p = SavePath;
                _saveExisted = File.Exists(p);
                _saveBackup = _saveExisted ? File.ReadAllBytes(p) : null;
            }
            catch
            {
                _saveExisted = false;
                _saveBackup = null;
            }
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            // With WebGL as the active build target the editor emulates WebGL's
            // pause-when-hidden behavior: entering play mode UNFOCUSED freezes the player
            // loop at frame ~1-2, so the runner's own runInBackground assignment (which
            // happens in the player loop) never lands and the suite hangs. This editor
            // callback runs on the editor loop, which stays alive when unfocused, so we
            // set the flag here the moment play mode starts to unfreeze the player loop.
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                Application.runInBackground = true;
                return;
            }

            if (state != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorPrefs.SetBool(IntegrationTestRunner.RunFlagKey, false);

            try
            {
                string p = SavePath;
                if (_saveExisted && _saveBackup != null)
                {
                    File.WriteAllBytes(p, _saveBackup);
                }
                else if (!_saveExisted && File.Exists(p))
                {
                    File.Delete(p);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[IntegrationTest] could not restore save: {ex.Message}");
            }

            string report = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "integration_report.json");
            Debug.Log($"[IntegrationTest] Run finished. Report: {report}");
        }
    }
}
