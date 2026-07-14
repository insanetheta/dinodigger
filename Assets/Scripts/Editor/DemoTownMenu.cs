using UnityEditor;
using UnityEngine;

namespace DinoDigger.EditorTools
{
    /// <summary>
    /// Play-mode-only helpers that let the orchestrator drive a live Dino Town build for a
    /// visual capture. Everything here pokes only PUBLIC members of Assembly-CSharp — editor
    /// scripts live in a separate assembly and cannot reach internal Test* hooks.
    ///
    /// All items no-op with a warning outside play mode: they mutate live runtime state and
    /// raise gameplay events, which is meaningless without a running player loop.
    /// </summary>
    public static class DemoTownMenu
    {
        [MenuItem("DinoDigger/Demo/Grant 12 Coins")]
        public static void Grant12Coins()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[Demo] Grant 12 Coins only works in play mode — enter play mode first.");
                return;
            }

            // Keep the player loop alive while the editor is unfocused (e.g. during capture),
            // matching the integration runner's behavior.
            Application.runInBackground = true;

            var gm = DinoDigger.Core.GameManager.Instance;
            if (gm == null)
            {
                Debug.LogWarning("[Demo] GameManager.Instance is null — is the Main scene loaded and initialized?");
                return;
            }

            gm.Save.Data.TreasureCount += 12;
            DinoDigger.Core.GameEvents.RaiseTreasureCollected(gm.Save.Data.TreasureCount);
            Debug.Log($"[Demo] Granted 12 coins. Wallet is now {gm.Save.Data.TreasureCount}.");
        }

        [MenuItem("DinoDigger/Demo/Log Town Status")]
        public static void LogTownStatus()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[Demo] Log Town Status only works in play mode — enter play mode first.");
                return;
            }

            var gm = DinoDigger.Core.GameManager.Instance;
            int wallet = gm != null ? gm.Save.Data.TreasureCount : -1;

            bool townObjectExists = GameObject.Find("Town") != null;
            var townController = Object.FindFirstObjectByType<DinoDigger.Overworld.TownController>();
            var dinos = Object.FindObjectsByType<DinoDigger.Overworld.DinoController>(FindObjectsSortMode.None);

            Debug.Log(
                $"[Demo] Town status — wallet: {(gm != null ? wallet.ToString() : "n/a (GameManager null)")}, " +
                $"GameObject named 'Town' exists: {townObjectExists}, " +
                $"TownController present: {(townController != null)}, " +
                $"DinoController count: {dinos.Length}.");
        }

        [MenuItem("DinoDigger/Demo/Ensure Resident Dinos")]
        public static void EnsureResidentDinos()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[Demo] Ensure Resident Dinos only works in play mode — enter play mode first.");
                return;
            }

            var dinos = Object.FindObjectsByType<DinoDigger.Overworld.DinoController>(FindObjectsSortMode.None);
            if (dinos.Length < 4)
            {
                Debug.LogWarning(
                    $"[Demo] Only {dinos.Length} DinoController(s) present — builders need at least 4 non-buddy " +
                    "resident dinos for a good Town capture. Not spawning any (orchestrator's call); reporting count only.");
                return;
            }

            Debug.Log($"[Demo] {dinos.Length} DinoController(s) present — enough residents for a Town capture.");
        }
    }
}
