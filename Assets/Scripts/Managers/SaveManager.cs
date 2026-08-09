using System.Collections.Generic;
using UnityEngine;
using DinoDigger.Config;

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

            Migrate();
        }

        /// <summary>Bring a just-loaded payload up to <see cref="SaveData.CurrentVersion"/>.
        /// Runs exactly once per load, before anything else reads the data, and is a no-op for
        /// a save already at the current version (including a brand-new one).</summary>
        private void Migrate()
        {
            if (Data == null)
            {
                Data = new SaveData();
            }

            // Absent lists (an old save, or a hand-written JSON that omits them) come back
            // null from JsonUtility, so normalise before anything indexes them.
            if (Data.Bones == null) { Data.Bones = new List<BoneSave>(); }
            if (Data.RevivedSpecies == null) { Data.RevivedSpecies = new List<DinoType>(); }
            if (Data.NestSpeciesQueue == null) { Data.NestSpeciesQueue = new List<DinoType>(); }
            if (Data.Dinos == null) { Data.Dinos = new List<DinoSave>(); }

            if (Data.Version < SaveData.BoneFieldVersion)
            {
                MigrateToV5();
            }
        }

        /// <summary>
        /// v4 -> v5: THE NEST RETIRES, THE SKELETON BOARD TAKES OVER (DinoDigger-5ve).
        ///
        /// Nothing a v4 player earned may be lost, so three things are carried across:
        ///
        ///   1. HATCHED STAYS HATCHED. Every fossil species that already exists in
        ///      <see cref="SaveData.Dinos"/> came out of the old nest, so it is marked
        ///      REVIVED — its skeleton shows complete and coloured on the board and the
        ///      Dino-Matic never offers it again. (GameManager re-derives this from the live
        ///      dinos on every load too, so the two can never disagree.)
        ///   2. THE NEST QUEUE DRAINS. A species sitting in the old NestSpeciesQueue is
        ///      revived if it is genuinely owned, and otherwise simply dropped — it was
        ///      only ever an intent, and the shards behind it are converted below. The
        ///      queue is then cleared for good.
        ///   3. IN-PROGRESS SHARDS BECOME IN-PROGRESS BONES, AT FLOOR VALUE. The formula:
        ///
        ///          req    = LegacyShardsPerHatch[clamp(revivedCount)]   // 5 / 8 / 15 / 20
        ///          target = the first species in SkeletonPlan.FocusOrder not yet revived
        ///          bones  = floor(ShardCount * SkeletonPlan.SlotCount(target) / req)
        ///
        ///      i.e. "you were THIS FRACTION of the way to your next dino; here is that same
        ///      fraction of its skeleton", rounded DOWN so the conversion can never hand out
        ///      a bone that was not earned, and clamped to one whole skeleton so a save that
        ///      somehow banked past its requirement cannot cascade into several revivals.
        ///      Those bones fill the target's slots in board order (skull first), the same
        ///      order the dig fills them in, and ShardCount is then zeroed — the shard
        ///      economy is over and must never be converted twice.
        /// </summary>
        private void MigrateToV5()
        {
            // ---- 1) hatched fossil species are already-revived skeletons ----
            for (int i = 0; i < Data.Dinos.Count; i++)
            {
                DinoSave d = Data.Dinos[i];
                if (d != null && SkeletonPlan.IsFossilSpecies(d.Type) && !Data.RevivedSpecies.Contains(d.Type))
                {
                    Data.RevivedSpecies.Add(d.Type);
                }
            }

            // ---- 2) drain the nest queue: owned -> revived, everything else dropped ----
            for (int i = 0; i < Data.NestSpeciesQueue.Count; i++)
            {
                DinoType q = Data.NestSpeciesQueue[i];
                if (SkeletonPlan.IsFossilSpecies(q) && OwnsSpecies(q) && !Data.RevivedSpecies.Contains(q))
                {
                    Data.RevivedSpecies.Add(q);
                }
            }

            Data.NestSpeciesQueue.Clear();

            // ---- 3) leftover shards become banked bones on the next unrevived skeleton ----
            int shards = Mathf.Max(0, Data.ShardCount);
            Data.ShardCount = 0;
            if (shards > 0 && TryNextUnrevived(out DinoType target))
            {
                int[] curve = SaveData.LegacyShardsPerHatch;
                int req = Mathf.Max(1, curve[Mathf.Clamp(Data.RevivedSpecies.Count, 0, curve.Length - 1)]);
                int slots = SkeletonPlan.SlotCount(target);
                int bones = Mathf.Clamp(shards * slots / req, 0, slots); // integer divide == floor

                for (int slot = 0; slot < bones; slot++)
                {
                    AddBone(target, SkeletonPlan.SlotBone(target, slot));
                }
            }

            Data.Version = SaveData.CurrentVersion;
        }

        /// <summary>The first species in the board's fill order whose skeleton has not been
        /// revived — where converted shard progress lands.</summary>
        private bool TryNextUnrevived(out DinoType species)
        {
            for (int i = 0; i < SkeletonPlan.FocusOrder.Length; i++)
            {
                if (!Data.RevivedSpecies.Contains(SkeletonPlan.FocusOrder[i]))
                {
                    species = SkeletonPlan.FocusOrder[i];
                    return true;
                }
            }

            species = default;
            return false;
        }

        private bool OwnsSpecies(DinoType type)
        {
            for (int i = 0; i < Data.Dinos.Count; i++)
            {
                if (Data.Dinos[i] != null && Data.Dinos[i].Type == type)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Add one banked bone row (or bump an existing one) in the payload.</summary>
        private void AddBone(DinoType species, int boneIndex)
        {
            if (boneIndex < 0)
            {
                return;
            }

            for (int i = 0; i < Data.Bones.Count; i++)
            {
                BoneSave row = Data.Bones[i];
                if (row != null && row.Species == species && row.BoneIndex == boneIndex)
                {
                    row.Count++;
                    return;
                }
            }

            Data.Bones.Add(new BoneSave { Species = species, BoneIndex = boneIndex, Count = 1 });
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
