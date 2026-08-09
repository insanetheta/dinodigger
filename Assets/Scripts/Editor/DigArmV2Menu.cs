using UnityEditor;
using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Dig;

namespace DinoDigger.EditorTools
{
    /// <summary>
    /// Demo toggle for the dig-arm V2 art set (DinoDigger-rrn): flips
    /// GameConfig.DigArmVersion on the shared config asset and, when the editor is
    /// playing, remounts the live rig on the spot — so Greg can A/B the two arm
    /// versions by eye mid-dig in seconds. The menu item shows a checkmark while V2
    /// is selected. V1 remains the serialized default; outside play mode the toggle
    /// simply persists on the asset for the next play session.
    /// </summary>
    public static class DigArmV2Menu
    {
        private const string MenuPath = "DinoDigger/Demo/Dig Arm V2 On|Off";
        private const string ConfigPath = "Assets/Art/Placeholder/Config/GameConfig.asset";

        [MenuItem(MenuPath)]
        public static void Toggle()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config == null)
            {
                Debug.LogWarning($"[DigArmV2Menu] no GameConfig at {ConfigPath}");
                return;
            }

            config.DigArmVersion = config.DigArmVersion == DigArmVersion.V2
                ? DigArmVersion.V1
                : DigArmVersion.V2;
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            Menu.SetChecked(MenuPath, config.DigArmVersion == DigArmVersion.V2);

            // Live switch: remount the running rig immediately (a no-op when the dig
            // site isn't open — the next PlaceBackhoe picks the new selection up).
            if (Application.isPlaying)
            {
                var dig = Object.FindFirstObjectByType<DigModeController>();
                if (dig != null)
                {
                    dig.RefreshDigArmVersion();
                }
            }

            Debug.Log($"[DigArmV2Menu] Dig arm art -> {config.DigArmVersion}");
        }

        [MenuItem(MenuPath, true)]
        public static bool ToggleValidate()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            Menu.SetChecked(MenuPath,
                config != null && config.DigArmVersion == DigArmVersion.V2);
            return config != null;
        }
    }
}
