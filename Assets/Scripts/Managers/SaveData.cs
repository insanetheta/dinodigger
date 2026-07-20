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

    /// <summary>Serializable snapshot of one town building's construction progress.
    /// Entries are stored in build order (index i describes the plot at slot i): the
    /// first <see cref="SaveData.TownNextIndex"/> entries are <see cref="Finished"/>
    /// buildings; the one after them (if any) is the site still under construction,
    /// carrying the construction state it had reached plus its banked partial work.</summary>
    [Serializable]
    public class TownBuildingSave
    {
        // Construction state reached: 0..3 while building, == BuildingController.ConstructionStates
        // (4) once finished. Restored verbatim so a partial site resumes at its state.
        public int State;

        // Seconds of builder work banked toward the NEXT state (the mid-state partial).
        public float Worked;

        // True once every construction state has been worked through. A finished
        // building is rebuilt showing its finished art with no crew and no confetti.
        public bool Finished;
    }

    /// <summary>Root save payload. Kept flat and JsonUtility-friendly.</summary>
    [Serializable]
    public class SaveData
    {
        // v1: original. v2: adds DinoSave.IsBuddy + ParadeDone. v3: adds ShardCount
        // + NestSpeciesQueue for the egg-shard nest progression (bl6). v4: adds
        // TownNextIndex + TownBuildings for Dino Town persistence. Older saves migrate
        // cleanly: JsonUtility leaves absent fields at their defaults (ShardCount = 0,
        // NestSpeciesQueue = empty, TownNextIndex = 0, TownBuildings = empty), so a v3
        // (or earlier) save loads with an empty town and nothing else is lost.
        public const int CurrentVersion = 4;

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

        // ---- v4: Dino Town persistence ----
        // Index of the next building to break ground on in the curated build order,
        // which also equals the number of FINISHED buildings. On load the queue
        // continues from here.
        public int TownNextIndex;

        // Per-building construction progress in build order (see TownBuildingSave).
        // The first TownNextIndex entries are finished; a trailing non-finished entry
        // is the site that was mid-construction. Empty on a fresh (or migrated) save.
        public List<TownBuildingSave> TownBuildings = new List<TownBuildingSave>();
    }
}
