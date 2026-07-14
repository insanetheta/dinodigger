using UnityEngine;

namespace DinoDigger.Managers
{
    /// <summary>
    /// Local JSON save. Uses <see cref="Application.persistentDataPath"/> on
    /// native platforms and PlayerPrefs on WebGL (no reliable filesystem there).
    /// Fully offline; no network.
    /// </summary>
    public class SaveManager
    {
        private const string PrefsKey = "DinoDigger.Save";
        private const string FileName = "dinodigger_save.json";

        public SaveData Data { get; private set; } = new SaveData();

        // TEST HOOK: the on-disk save path, so the integration runner can back it up
        // and restore it around a save-roundtrip test (native platforms only).
        internal static string TestFilePath =>
            System.IO.Path.Combine(Application.persistentDataPath, FileName);

        public void Load()
        {
            string json = null;

#if UNITY_WEBGL && !UNITY_EDITOR
            if (PlayerPrefs.HasKey(PrefsKey))
            {
                json = PlayerPrefs.GetString(PrefsKey);
            }
#else
            try
            {
                string path = System.IO.Path.Combine(Application.persistentDataPath, FileName);
                if (System.IO.File.Exists(path))
                {
                    json = System.IO.File.ReadAllText(path);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SaveManager] Load failed: {e.Message}");
            }
#endif

            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    Data = JsonUtility.FromJson<SaveData>(json) ?? new SaveData();
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[SaveManager] Parse failed, starting fresh: {e.Message}");
                    Data = new SaveData();
                }
            }
            else
            {
                Data = new SaveData();
            }
        }

        public void Save()
        {
            if (Data == null)
            {
                Data = new SaveData();
            }

            // Always write at the current schema version — the field's default (1)
            // only describes freshly constructed data, not what we persist.
            Data.Version = SaveData.CurrentVersion;
            string json = JsonUtility.ToJson(Data);

#if UNITY_WEBGL && !UNITY_EDITOR
            PlayerPrefs.SetString(PrefsKey, json);
            PlayerPrefs.Save();
#else
            try
            {
                string path = System.IO.Path.Combine(Application.persistentDataPath, FileName);
                System.IO.File.WriteAllText(path, json);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SaveManager] Save failed: {e.Message}");
            }
#endif
        }

        public void ResetAll()
        {
            Data = new SaveData();
            Save();
        }
    }
}
