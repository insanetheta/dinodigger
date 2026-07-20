using System.Collections.Generic;
using UnityEngine;

namespace DinoDigger.Config
{
    /// <summary>All designer-tunable numbers in one asset.</summary>
    [CreateAssetMenu(menuName = "DinoDigger/Game Config", fileName = "GameConfig")]
    public class GameConfig : ScriptableObject
    {
        [Header("Dino roster")]
        public List<DinoDefinition> Dinos = new List<DinoDefinition>();

        [Header("Growth")]
        [Tooltip("Uniform scale multiplier per growth stage: baby, kid, big. Kept subtle " +
                 "because per-stage ART (baby/kid/adult sprite sets) now carries most of " +
                 "the visible growth; this only adds a gentle size bump on top.")]
        public float[] StageScales = { 1.0f, 1.15f, 1.3f };

        [Tooltip("Total fruit eaten to reach Kid stage.")]
        public int FruitToKid = 2;

        [Tooltip("Additional fruit (beyond Kid total) to reach Big stage.")]
        public int FruitToBig = 3;

        [Header("Dig mounds")]
        [Tooltip("Seconds after a mound is dug out before it respawns elsewhere.")]
        public float MoundRespawnSeconds = 20f;

        [Header("Dig site contents")]
        public int MinItemsPerSite = 2;
        public int MaxItemsPerSite = 4;

        [Tooltip("Relative weights for Egg / Fruit / Treasure.")]
        public float EggWeight = 0.35f;
        public float FruitWeight = 0.40f;
        public float TreasureWeight = 0.25f;

        [Tooltip("Number of fruit sprite variants (apple, banana, berry, watermelon).")]
        public int FruitVariants = 4;

        [Tooltip("Number of treasure sprite variants (coin, gem, boot, bone).")]
        public int TreasureVariants = 4;

        [Tooltip("Coins banked per treasure variant when collected (coin, gem, boot, bone). " +
                 "Out-of-range variants safely bank 1 via TreasureValue().")]
        public int[] TreasureValues = { 1, 3, 1, 2 };

        [Tooltip("Chance a dug FRUIT downgrades to a random treasure when NOTHING is hungry, " +
                 "so uneaten fruit can't pile up; the rest stays fruit so the world keeps some.")]
        public float FruitDowngradeFraction = 0.75f;

        [Header("Dig grid")]
        public int DigRows = 5;      // 4-6 layers of dirt
        public int DigColumns = 7;
        [Tooltip("Taps to fully crumble one dirt tile (matches 3 crack states).")]
        public int DirtHealth = 3;

        [Header("Movement")]
        public float BackhoeSpeed = 3.5f;
        public float BackhoeArriveDistance = 0.15f;
        public float DinoFollowSpeed = 3.0f;
        public float DinoFollowSlack = 0.4f;   // deadzone before a dino chases
        public float DinoWanderRadius = 1.2f;
        public float DinoEatSpeed = 4.0f;

        [Header("Camera")]
        public float RoamOrthoSize = 5.5f;
        // Dig view frames the close-up 2.4-unit backhoe body ABOVE the surface
        // plus all grid rows below it (see DigModeController.DigCenter): rows=5
        // needs a half-height of ~4.2.
        public float DigOrthoSize = 4.2f;
        [Tooltip("Ortho size the camera zooms to for the shard-hatch nest ceremony " +
                 "(a gentle push-in, framing the nest + the new baby dino).")]
        public float CeremonyOrthoSize = 4.0f;
        public float CameraFollowLerp = 3.0f;
        public Vector2 CameraDeadzone = new Vector2(1.2f, 0.8f);
        public float TransitionSeconds = 0.5f;

        [Header("Egg-shard nest")]
        [Tooltip("Egg shards required to hatch each successive shard-built egg, indexed by " +
                 "the number of shard eggs ALREADY hatched (the 1st egg uses entry [0]). " +
                 "Escalates so the first shard dino comes quickly, then costs more; the last " +
                 "entry clamps for any further eggs. The nest's assembly sprites scale onto " +
                 "whichever requirement is current: state = floor(ShardCount / requirement * " +
                 "(states-1)), e.g. 0/5/10/15/20 at requirement 20, tighter at 5.")]
        public int[] ShardsPerHatchProgression = { 5, 8, 15, 20 };

        [Header("Dino Town (idle builder)")]
        /// <summary>Build-order index of the Fruit Stand — the second building (price 25),
        /// the first FUNCTIONAL one: once finished it turns surplus fruit into coins. A
        /// compile-time const (not a serialized field) so an existing GameConfig asset
        /// can't silently deserialize it to 0.</summary>
        public const int FruitStandIndex = 1;

        [Tooltip("Curated build-price curve for the town, buildings 1..N (indexed 0-based). " +
                 "Coins auto-spend on the next building the moment the wallet clears its price. " +
                 "All nine entries build in order, one per town plot. Values from the design " +
                 "doc's roster: 10/25/50/90/150/240/380/490/600 (~x1.6 step).")]
        public int[] TownBuildingPrices = { 10, 25, 50, 90, 150, 240, 380, 490, 600 };

        [Tooltip("Seconds of builder WORK time to advance one construction state (0->1->2->3->finished). " +
                 "Timing is driven by the crew, not a clock: worked time only accrues while builders are " +
                 "on site. A bigger crew banks it faster (dt * working-builder count).")]
        public float TownSecondsPerBuildState = 8f;

        [Tooltip("Max NON-BUDDY resident dinos drafted onto one construction site. If none are available " +
                 "the build simply waits — buddies and the player backhoe are NEVER drafted.")]
        public int TownMaxBuilders = 2;

        [Tooltip("Walk-speed multiplier for a resident commuting from the meadow to a build site.")]
        public float TownBuilderCommuteSpeed = 1.1f;

        [Header("Rock Smash (Ankylosaurus)")]
        [Tooltip("A buddy Ankylosaurus must be at least this close to a tapped rock to " +
                 "smash it open (same reach as the Brachio tree-shake).")]
        public float RockSmashRange = 3f;

        [Tooltip("Per-rock cooldown after a smash: the same rock can't pay out again " +
                 "until this many seconds pass (a tap still wiggles for feedback).")]
        public float RockCooldownSeconds = 15f;

        [Tooltip("Chance a smashed rock coughs up an egg SHARD instead of treasure — but " +
                 "only while shard species remain unhatched; once the nest is complete it " +
                 "is always treasure (mirrors the dig-loot shard gate).")]
        public float RockShardChance = 0.1f;

        [Header("Feel")]
        public float IdleAttractSeconds = 15f;
        public float ParentGateHoldSeconds = 3f;

        // ----- Derived helpers -----

        /// <summary>Egg shards needed to hatch the NEXT shard-built egg, given how many
        /// shard eggs have ALREADY hatched (0 -> the first egg's requirement). Clamps to
        /// the last progression entry, so the 5th+ egg reuses the final cost.</summary>
        public int GetShardRequirement(int eggsHatched)
        {
            if (ShardsPerHatchProgression == null || ShardsPerHatchProgression.Length == 0)
            {
                return 20;
            }

            int i = Mathf.Clamp(eggsHatched, 0, ShardsPerHatchProgression.Length - 1);
            return Mathf.Max(1, ShardsPerHatchProgression[i]);
        }

        /// <summary>Back-compat / editor convenience: the FIRST shard egg's requirement.
        /// Runtime code should prefer <see cref="GetShardRequirement"/> (via
        /// GameManager.ShardsPerHatch) for the escalating value.</summary>
        public int ShardsPerHatch => GetShardRequirement(0);

        /// <summary>Coins banked when the treasure <paramref name="variant"/> is collected
        /// (clamped). An out-of-range or unconfigured variant safely banks 1.</summary>
        public int TreasureValue(int variant)
        {
            if (TreasureValues == null || TreasureValues.Length == 0)
            {
                return 1;
            }

            variant = Mathf.Clamp(variant, 0, TreasureValues.Length - 1);
            return Mathf.Max(1, TreasureValues[variant]);
        }

        public float StageScale(GrowthStage stage)
        {
            int i = (int)stage;
            if (StageScales == null || StageScales.Length == 0)
            {
                return 1f;
            }

            i = Mathf.Clamp(i, 0, StageScales.Length - 1);
            return StageScales[i];
        }

        /// <summary>Total cumulative fruit required to be at <paramref name="stage"/>.</summary>
        public int FruitThreshold(GrowthStage stage)
        {
            switch (stage)
            {
                case GrowthStage.Kid: return FruitToKid;
                case GrowthStage.Big: return FruitToKid + FruitToBig;
                default: return 0;
            }
        }

        /// <summary>Curated price of the town building at <paramref name="index"/> in build
        /// order (clamped). Returns a huge value if the curve is empty so nothing ever
        /// auto-starts without a configured price.</summary>
        public int TownBuildingPrice(int index)
        {
            if (TownBuildingPrices == null || TownBuildingPrices.Length == 0)
            {
                return int.MaxValue;
            }

            index = Mathf.Clamp(index, 0, TownBuildingPrices.Length - 1);
            return Mathf.Max(0, TownBuildingPrices[index]);
        }

        public DinoDefinition GetDino(DinoType type)
        {
            if (Dinos == null)
            {
                return null;
            }

            for (int i = 0; i < Dinos.Count; i++)
            {
                if (Dinos[i] != null && Dinos[i].Type == type)
                {
                    return Dinos[i];
                }
            }

            return Dinos.Count > 0 ? Dinos[0] : null;
        }
    }
}
