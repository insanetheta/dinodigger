namespace DinoDigger.Config
{
    /// <summary>
    /// All nine dinosaur species. The first FOUR (index 0-3) are the original
    /// EGG-HATCHABLE species — dig eggs only ever roll these. The next FIVE
    /// (index 4-8) are SHARD-EXCLUSIVE: they are never produced by egg rolls and
    /// are unlocked later from egg shards assembled at the nest (see bl6 epic).
    /// Ordering matters: code keys "egg species" off index &lt; 4, so never
    /// reorder or insert before Velociraptor.
    /// </summary>
    public enum DinoType
    {
        // ---- Egg-hatchable (original four) ----
        TRex = 0,
        Triceratops = 1,
        Brachiosaurus = 2,
        Stegosaurus = 3,

        // ---- Shard-exclusive (five new) ----
        Pteranodon = 4,
        Ankylosaurus = 5,
        Spinosaurus = 6,
        Parasaurolophus = 7,
        Velociraptor = 8
    }

    /// <summary>Number of original egg-hatchable species (DinoType index &lt; this).</summary>
    public static class DinoSpecies
    {
        public const int EggHatchableCount = 4;
        public const int TotalCount = 9;

        /// <summary>True for the original four species that dig eggs can roll.</summary>
        public static bool IsEggHatchable(DinoType type) => (int)type < EggHatchableCount;
    }

    /// <summary>Which happy dance a dino performs when tapped.</summary>
    public enum DanceType
    {
        StompRoar = 0,    // T-Rex
        HeadShake = 1,    // Triceratops
        NeckSway = 2,     // Brachiosaurus
        TailWag = 3,      // Stegosaurus
        WingFlap = 4,     // Pteranodon
        TailClub = 5,     // Ankylosaurus
        SailWiggle = 6,   // Spinosaurus
        CrestToot = 7,    // Parasaurolophus
        SpinHop = 8       // Velociraptor
    }

    /// <summary>Category of a diggable / droppable item.</summary>
    public enum ItemType
    {
        Egg = 0,
        Fruit = 1,
        Treasure = 2,
        Shard = 3   // sparkly egg-shell piece; flies to the nest, banked as ShardCount
    }

    /// <summary>Growth stage for a dino. Scale is driven from GameConfig.</summary>
    public enum GrowthStage
    {
        Baby = 0,
        Kid = 1,
        Big = 2
    }
}
