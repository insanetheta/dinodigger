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

    /// <summary>Serializable count of one banked fossil bone (DinoDigger-0z5): which
    /// skeleton it belongs to, which bone of that skeleton, and how many have been dug.
    ///
    /// NOT PERSISTED YET, ON PURPOSE. The dig site banks bones into a session dictionary on
    /// GameManager and this is the shape that dictionary snapshots into; D2b (the skeleton
    /// board) owns the save version bump that adds the actual <c>List&lt;BoneSave&gt;</c>
    /// field to <see cref="SaveData"/>. Defining the row here now means that bump is a
    /// one-line field addition rather than a data-model design.</summary>
    [Serializable]
    public class BoneSave
    {
        public DinoType Species;
        public int BoneIndex;   // (int)BoneType — a stable contract, never renumbered
        public int Count;
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
        //
        // MACHINE FRIENDS (DinoDigger-b48) DELIBERATELY DID NOT BUMP THE VERSION.
        // MachinesWoken below is a PURELY ADDITIVE field whose absent-value semantics are
        // already the correct migration: a save written before machines existed has no
        // woken machines, and JsonUtility leaves the list at its field initializer (empty)
        // for exactly that case. A version bump would only be needed if an OLD field had
        // to be reinterpreted (the BuddyFieldVersion situation, where absent != false).
        // Since nothing is reinterpreted, v4 stays v4 and every v1..v4 save loads with
        // three sleeping machines — which is also what a brand-new player sees.
        public const int CurrentVersion = 4;

        // Saves at or above this version carry the real DinoSave.IsBuddy flag; below
        // it (v1) the loader falls back to "first two loaded dinos are buddies".
        public const int BuddyFieldVersion = 2;

        // The version from which MachinesWoken is written. Kept as a named constant for
        // the same reason BuddyFieldVersion is — it documents WHICH schema introduced the
        // field — but no loader gates on it, because "absent" and "empty" mean the same
        // thing here (see the CurrentVersion note above). If a future machine field ever
        // needs "absent != default" semantics, THAT is the change that bumps the version.
        public const int MachinesWokenFieldVersion = 4;

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

        // ---- Machine Friends (additive on v4; see the CurrentVersion note) ----
        // STABLE STRING IDs of the machines the child has woken with a first tap
        // ("doodle" / "sprinkles" / "tuggy" — MachineFriend.MachineId). Strings, not enum
        // ordinals, so re-ordering or inserting a machine can never silently wake the
        // wrong one; unknown ids in a save are simply ignored. Absent field => empty list
        // => every machine is still asleep, which is the correct migration for v1..v4.
        public List<string> MachinesWoken = new List<string>();

        // STABLE STRING IDs of the machines whose DISCOVERY GATE has tripped — i.e. the
        // child has engaged the loop that machine serves (harvested a berry, caught a duck,
        // finished a building), so the machine has "heard about you and come to help" and
        // may now arrive in the world. Separate from MachinesWoken because the two are
        // genuinely different beats: gated = it has ARRIVED and is glinting for attention;
        // woken = the child found it and tapped it. Same additive-on-v4 rules as above —
        // absent field => no gates tripped => a returning player re-earns each arrival
        // exactly as they earned it the first time (and cannot lose a machine they woke,
        // because MachinesWoken independently forces its gate open on load).
        public List<string> MachineGatesTripped = new List<string>();

        // ---- Dig toy roller (additive on v4; DinoDigger-qhy) ----
        // The FEATURED toy of the last dig site, stored as index+1 so the absent-field
        // default (0) means "no history" rather than naming a real toy. That is the whole
        // migration: a save written before the roller existed simply lets the first site of
        // the session roll anything, which is also what a brand-new player gets — so, like
        // MachinesWoken above, this is purely additive and v4 stays v4.
        public int LastPrimaryToy;
    }
}
