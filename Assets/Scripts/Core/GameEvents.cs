using System;
using UnityEngine;
using DinoDigger.Config;

namespace DinoDigger.Core
{
    /// <summary>Payload describing an item that was just dug up in dig mode.</summary>
    public struct DugItemInfo
    {
        public ItemType Type;
        public DinoType DinoType;   // valid when Type == Egg
        public int Variant;         // fruit/treasure sub-kind for sprite selection
        public Vector3 OriginWorld; // where in the world it should pop out from

        public DugItemInfo(ItemType type, DinoType dinoType, int variant, Vector3 originWorld)
        {
            Type = type;
            DinoType = dinoType;
            Variant = variant;
            OriginWorld = originWorld;
        }
    }

    /// <summary>
    /// Lightweight static event bus. Systems stay decoupled: publishers call the
    /// Raise* helpers, subscribers add to the events. All invocation is
    /// null-guarded so partially-wired scenes never throw.
    /// </summary>
    public static class GameEvents
    {
        public static event Action<GameState> StateChanged;
        public static event Action<DugItemInfo> ItemDug;
        public static event Action<DinoType> EggHatched;
        public static event Action<DinoType, GrowthStage> DinoGrew;
        public static event Action<int> TreasureCollected;   // new running total
        public static event Action<int> ShardCollected;      // new running shard total (flies to nest)
        public static event Action DigModeEntered;
        public static event Action DigModeExited;
        public static event Action DinoTapped;
        public static event Action FruitEaten;
        public static event Action BackhoeMoved;
        public static event Action IdleAttract;
        public static event Action<Vector3Int> TreeTapped;   // obstacle cell of the tapped tree
        public static event Action ParadeStarted;            // all-four-species-Big celebration

        /// <summary>
        /// Where a dug egg shard should fly to. Set by the nest system (bl6.4) once
        /// a nest exists in the meadow; when null, shard collection falls back to a
        /// safe target (meadow center, else the treasure counter corner). Kept as a
        /// provider rather than an event so the shard can retarget every flight.
        /// </summary>
        public static Func<Vector3?> NestTargetProvider;

        public static void RaiseStateChanged(GameState s) => StateChanged?.Invoke(s);
        public static void RaiseItemDug(DugItemInfo info) => ItemDug?.Invoke(info);
        public static void RaiseEggHatched(DinoType t) => EggHatched?.Invoke(t);
        public static void RaiseDinoGrew(DinoType t, GrowthStage s) => DinoGrew?.Invoke(t, s);
        public static void RaiseTreasureCollected(int total) => TreasureCollected?.Invoke(total);
        public static void RaiseShardCollected(int total) => ShardCollected?.Invoke(total);
        public static void RaiseDigModeEntered() => DigModeEntered?.Invoke();
        public static void RaiseDigModeExited() => DigModeExited?.Invoke();
        public static void RaiseDinoTapped() => DinoTapped?.Invoke();
        public static void RaiseFruitEaten() => FruitEaten?.Invoke();
        public static void RaiseBackhoeMoved() => BackhoeMoved?.Invoke();
        public static void RaiseIdleAttract() => IdleAttract?.Invoke();
        public static void RaiseTreeTapped(Vector3Int cell) => TreeTapped?.Invoke(cell);
        public static void RaiseParadeStarted() => ParadeStarted?.Invoke();

        /// <summary>
        /// Clear every subscription. Called on GameManager teardown so stale
        /// delegates from a previous play session (domain-reload disabled) don't
        /// leak into the next one.
        /// </summary>
        public static void ClearAll()
        {
            StateChanged = null;
            ItemDug = null;
            EggHatched = null;
            DinoGrew = null;
            TreasureCollected = null;
            ShardCollected = null;
            NestTargetProvider = null;
            DigModeEntered = null;
            DigModeExited = null;
            DinoTapped = null;
            FruitEaten = null;
            BackhoeMoved = null;
            IdleAttract = null;
            TreeTapped = null;
            ParadeStarted = null;
        }
    }
}
