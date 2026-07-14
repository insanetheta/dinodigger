using System;
using System.Collections.Generic;
using DinoDigger.Config;

namespace DinoDigger.Managers
{
    /// <summary>Serializable snapshot of one dino's progress.</summary>
    [Serializable]
    public class DinoSave
    {
        public DinoType Type;
        public GrowthStage Stage;
        public int FruitEaten;

        // Companion redesign: is this dino one of the (max 2) backhoe buddies?
        // Old saves (SaveData.Version < 2) lack this field; the loader falls back
        // to "the first 2 loaded dinos are buddies" so those saves still work.
        public bool IsBuddy;
    }

    /// <summary>Root save payload. Kept flat and JsonUtility-friendly.</summary>
    [Serializable]
    public class SaveData
    {
        // v1: original. v2: adds DinoSave.IsBuddy + ParadeDone. v3: adds ShardCount
        // + NestSpeciesQueue for the egg-shard nest progression (bl6). Older saves
        // migrate cleanly: JsonUtility leaves absent fields at their defaults
        // (ShardCount = 0, NestSpeciesQueue = empty), so nothing is lost.
        public const int CurrentVersion = 3;

        // Saves at or above this version carry the real DinoSave.IsBuddy flag; below
        // it (v1) the loader falls back to "first two loaded dinos are buddies".
        public const int BuddyFieldVersion = 2;

        public int Version = 1;
        public int TreasureCount;
        public List<DinoSave> Dinos = new List<DinoSave>();

        // Milestone parade (all four egg species Big) plays exactly once, ever.
        public bool ParadeDone;

        // ---- v3: egg-shard nest progression ----
        // Banked egg shards dug up once every egg species is owned.
        public int ShardCount;

        // Shard-exclusive species queued for / assembled at the nest. Populated by
        // the nest system (bl6.4); persisted here so nest state survives a restart.
        public List<DinoType> NestSpeciesQueue = new List<DinoType>();
    }
}
