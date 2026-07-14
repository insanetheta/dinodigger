using UnityEngine;
using UnityEngine.Tilemaps;

namespace DinoDigger.Config
{
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

        [Header("Items")]
        public Sprite[] FruitSprites = new Sprite[4];
        public Sprite[] TreasureSprites = new Sprite[4];
        [Tooltip("Sparkly egg-shell piece dug once every egg species is owned (flies to the nest).")]
        public Sprite ShardSprite;

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
