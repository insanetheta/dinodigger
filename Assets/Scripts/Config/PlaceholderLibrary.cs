using UnityEngine;
using UnityEngine.Tilemaps;

namespace DinoDigger.Config
{
    /// <summary>
    /// One town building's five construction-state sprites, in ascending completeness:
    /// index 0..3 = ground-break / foundation / frame / walls, index 4 = FINISHED.
    /// Every slot is optional — a building whose art has not been generated yet (or
    /// whose batch only produced some states) simply leaves them null and the runtime
    /// falls back to the generic <see cref="PlaceholderLibrary.BuildingStates"/> for
    /// exactly the states it is missing. Direct typed access, no reflection.
    /// </summary>
    [System.Serializable]
    public class BuildingArt
    {
        [Tooltip("s0..s3 then the finished building, matching BuildingController's state indices.")]
        public Sprite[] States = new Sprite[5];

        /// <summary>The sprite for construction <paramref name="state"/>, or null when this
        /// building has no art for it (the caller then falls back to the generic set).</summary>
        public Sprite State(int state)
        {
            if (States == null || States.Length == 0)
            {
                return null;
            }

            state = Mathf.Clamp(state, 0, States.Length - 1);
            return States[state];
        }

        /// <summary>True when at least one state sprite is present — i.e. this building has
        /// real art worth handing to its <c>BuildingController</c>.</summary>
        public bool HasAny
        {
            get
            {
                if (States == null)
                {
                    return false;
                }

                for (int i = 0; i < States.Length; i++)
                {
                    if (States[i] != null)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }

    /// <summary>
    /// Central registry of all placeholder art (tiles + sprites), authored by the
    /// PlaceholderArtGenerator editor tool and wired into the scene by SceneBuilder.
    /// Runtime code references art only through this asset (or serialized fields) —
    /// never via hardcoded paths.
    /// </summary>
    [CreateAssetMenu(menuName = "DinoDigger/Placeholder Library", fileName = "PlaceholderLibrary")]
    public class PlaceholderLibrary : ScriptableObject
    {
        [Header("Isometric tiles")]
        public TileBase GrassTile;
        public TileBase PathTile;
        public TileBase WaterTile;
        [Tooltip("Stone-grey planks/cobbles over blue water: the walkable bridge deck " +
                 "painted where a path (or a connectivity heal) crosses a stream.")]
        public TileBase BridgeTile;
        public TileBase MoundTile;
        public TileBase TreeTile;
        public TileBase RockTile;

        [Header("Backhoe")]
        [Tooltip("8-directional backhoe sprites indexed by Dir8.")]
        public Sprite[] BackhoeDir = new Sprite[8];
        [Tooltip("8-dir wheel-roll frame A (spokes turned + suspension dip) for the " +
                 "drive cycle. Empty = the backhoe drives on the static facing frame.")]
        public Sprite[] BackhoeRollA = new Sprite[8];
        [Tooltip("8-dir wheel-roll frame B (opposite spoke angle + suspension bob).")]
        public Sprite[] BackhoeRollB = new Sprite[8];
        public Sprite BackhoeBody;   // side-view body used in dig mode (WITH rear arm)
        public Sprite ScoopArm;      // legacy placeholder square (fallback + compat)

        [Header("Dig excavator rig")]
        [Tooltip("Armless side-view tractor body (rear boom removed) for the IK dig rig.")]
        public Sprite DigBodySprite; // side-view body WITHOUT the rear arm
        [Tooltip("Two-bone excavator arm pieces + toothed bucket, drawn pointing +x with a base (left-edge) pivot.")]
        public Sprite BoomSprite;    // first (proximal) arm segment
        public Sprite StickSprite;   // second (distal) arm segment
        public Sprite BucketSprite;  // toothed digging bucket, opening leftward

        [Header("Dig excavator rig V2 (DinoDigger-rrn)")]
        [Tooltip("Proportionate slim arm rebuild (Assets/Art/Generated/digarm2). Same rig " +
                 "skeleton as V1; selected by GameConfig.DigArmVersion. Null slots = V2 " +
                 "unavailable and the rig stays on V1 regardless of the switch.")]
        public Sprite Boom2Sprite;   // V2 proximal segment (slim, small matched pin bosses)
        public Sprite Stick2Sprite;  // V2 distal segment
        public Sprite Bucket2Sprite; // V2 bucket, opening leftward, hinge lug top-right

        [Header("Overworld object sprites")]
        public Sprite MoundSprite;   // SpriteRenderer sprite for dig mounds

        [Header("Egg-shard nest")]
        [Tooltip("Brown twig-ring nest base prop that sits in the meadow.")]
        public Sprite NestSprite;
        [Tooltip("Egg-assembly build states, 0..4 = 0/5/10/15/20 shards: cracked-shell " +
                 "fragments piecing into a whole egg. Real generated art can replace these " +
                 "in place with no code change.")]
        public Sprite[] EggAssemblySprites = new Sprite[5];

        [Header("Dig grid")]
        [Tooltip("Dirt tile crack states: 0 = full, 1 = cracked, 2 = crumbling.")]
        public Sprite[] DirtStates = new Sprite[3];
        [Tooltip("Full-bleed side-view backdrop behind the dig grid (sky + grass lip + soil).")]
        public Sprite DigBackground;

        [Header("Dig toys (Dig Loop 2.0)")]
        [Tooltip("Crystal tiles by colour index: 0 teal, 1 coral, 2 gold. The three sprites are " +
                 "PIXEL-IDENTICAL silhouettes, so the colour is the only thing a child has to " +
                 "match and a swap can never change a tile's footprint. Left null (no generated " +
                 "art yet) a crystal falls back to the dirt sprite under a strong colour tint, " +
                 "so the blob is still readable and every test still means something.")]
        public Sprite[] CrystalSprites = new Sprite[3];

        [Tooltip("Rare boom geode: a tap (or a crack from a landing tile) lights its fuse and " +
                 "it clears a 3x3.")]
        public Sprite BoomGeode;

        [Tooltip("Pinata pot, whole and cracked (shown after its first hit). Breaking it sprays " +
                 "a fountain of coins.")]
        public Sprite PinataPot;
        public Sprite PinataPotCracked;

        [Tooltip("Soft dust puff used for cascade landings and the geode's ring. Null = the " +
                 "landings quietly fall back to the crumb particle.")]
        public Sprite DustPuff;

        [Header("Items")]
        public Sprite[] FruitSprites = new Sprite[4];
        public Sprite[] TreasureSprites = new Sprite[4];
        [Tooltip("Sparkly egg-shell piece dug once every egg species is owned (flies to the nest).")]
        public Sprite ShardSprite;

        [Header("Dino Town")]
        [Tooltip("Building construction states, index 0..3 = ground-break/foundation/frame/walls, " +
                 "index 4 = finished. The Pebble Playground placeholder for phase 1; real generated " +
                 "art can replace these in place with no code change.")]
        public Sprite[] BuildingStates = new Sprite[5];

        [Tooltip("PER-BUILDING construction art indexed by CURATED BUILD ORDER (0 Pebble " +
                 "Playground, 1 Boulder Brew, 2 Slate Library, 3 Bedrock Bijou, 4 Bone-anza " +
                 "Bowling, 5 Dino Daycare, 6 Tar-Pit Springs, 7 Gronk's Grocer, 8 Fossil " +
                 "Fountain), five states each. Filled by GeneratedArtImporter. Any building " +
                 "(or single state) whose art has not been generated yet stays null and falls " +
                 "back to the generic BuildingStates above — a plot never throws or blanks out.")]
        public BuildingArt[] TownBuildings = NewTownBuildings();

        [Tooltip("Builder construction-worker props (DinoDigger-771): a yellow hard hat worn by a " +
                 "drafted builder (center pivot), a stone mallet it holds on-site (center pivot), " +
                 "and a striped barrier sign shown at an active build (bottom-center pivot). Any of " +
                 "these left null = that prop is silently absent (placeholder-only / stale-library runs).")]
        public Sprite HardHat;
        public Sprite ToolHammer;
        public Sprite ConstructionSign;

        [Header("Particles")]
        public Sprite StarParticle;
        public Sprite HeartParticle;
        public Sprite CrumbParticle;

        [Header("Icons")]
        public Sprite TreasureIcon;   // for the corner counter
        public Sprite MuteIcon;
        public Sprite SoundIcon;

        public Sprite Fruit(int variant)
        {
            return Pick(FruitSprites, variant);
        }

        public Sprite Treasure(int variant)
        {
            return Pick(TreasureSprites, variant);
        }

        public Sprite Dirt(int state)
        {
            return Pick(DirtStates, state);
        }

        /// <summary>Crystal art for <paramref name="color"/> (0 teal, 1 coral, 2 gold), or null
        /// when the dig art has not been imported — the tile then tints the dirt sprite instead.</summary>
        public Sprite Crystal(int color)
        {
            return Pick(CrystalSprites, color);
        }

        /// <summary>How many crystal colours this library actually carries art for (at least 1,
        /// so site generation always has a colour to roll even on a stale asset).</summary>
        public int CrystalColorCount =>
            CrystalSprites != null && CrystalSprites.Length > 0 ? CrystalSprites.Length : 3;

        public Sprite BuildingState(int state)
        {
            return Pick(BuildingStates, state);
        }

        /// <summary>Number of curated town buildings this library can carry art for.</summary>
        public const int TownBuildingCount = 9;

        /// <summary>The per-building art set for <paramref name="buildingIndex"/> in curated
        /// build order, or NULL when that building has no generated art yet (art still queued,
        /// or an older library asset saved before this field existed). Callers treat null — and
        /// individual null states inside a partial set — as "use the generic
        /// <see cref="BuildingStates"/> placeholder", so a missing building degrades to the
        /// placeholder rather than blanking a plot.</summary>
        public BuildingArt TownBuilding(int buildingIndex)
        {
            if (TownBuildings == null || buildingIndex < 0 || buildingIndex >= TownBuildings.Length)
            {
                return null;
            }

            BuildingArt art = TownBuildings[buildingIndex];
            return art != null && art.HasAny ? art : null;
        }

        /// <summary>A fresh, fully-populated per-building art table (nine empty sets). Doubles as
        /// the field initializer so a newly created asset has every slot ready for the importer.</summary>
        private static BuildingArt[] NewTownBuildings()
        {
            var arr = new BuildingArt[TownBuildingCount];
            for (int i = 0; i < arr.Length; i++)
            {
                arr[i] = new BuildingArt();
            }

            return arr;
        }

        private static Sprite Pick(Sprite[] arr, int i)
        {
            if (arr == null || arr.Length == 0)
            {
                return null;
            }

            i = Mathf.Clamp(i, 0, arr.Length - 1);
            return arr[i];
        }
    }
}
