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

        // ===================== ENV: Jurassic-earth environment set (DinoDigger-y1g)
        // The flat tiles above stay as the FALLBACK for every slot below: an un-imported
        // (or partially imported) env set leaves these empty and SceneBuilder paints
        // exactly what it painted before, so the island can never regress below today.
        //
        // Note what is NOT here: TreeTile / RockTile / MoundTile / BridgeTile keep their
        // identity. GameManager routes a tree/rock tap by comparing the obstacle tile
        // against those very references, so the env swap re-points the SPRITE INSIDE each
        // of those Tile assets rather than introducing new ones. Trees and rocks are
        // therefore an art-only swap with byte-identical placement, and the tree/rock
        // variant sprites below are carried for the props that need them (and for a
        // future variant pass that would have to change the tap contract first).

        [Header("ENV ground (DinoDigger-y1g)")]
        [Tooltip("Mottle variants for plain grass — 16 slices of one continuous plate. " +
                 "Empty = paint the flat GrassTile everywhere, exactly as before.")]
        public EnvTileSet GrassTiles = new EnvTileSet();
        [Tooltip("Mottle variants for the dirt path. Empty = flat PathTile.")]
        public EnvTileSet PathTiles = new EnvTileSet();
        [Tooltip("Mottle variants for pond/stream water. Empty = flat WaterTile.")]
        public EnvTileSet WaterTiles = new EnvTileSet();
        [Tooltip("Tilled berry-garden bed variants (4). Empty = the garden plot stays " +
                 "plain grass, exactly as before.")]
        public EnvTileSet BedTiles = new EnvTileSet();

        [Header("ENV connected sets (DinoDigger-l9g)")]
        [Tooltip("Water's 47 topology-keyed pieces. These SUPERSEDE WaterTiles + " +
                 "GrassWaterEdges: a connected piece carries its own bank, so the grass " +
                 "next door stays plain grass. Incomplete = the painter falls back to the " +
                 "flat variants and grass-side transitions.")]
        public EnvBlobSet WaterBlobs = new EnvBlobSet();
        [Tooltip("The dirt path's 47 topology-keyed pieces (supersede PathTiles + " +
                 "GrassPathEdges).")]
        public EnvBlobSet PathBlobs = new EnvBlobSet();
        [Tooltip("The garden bed's 47 topology-keyed pieces (supersede BedTiles + " +
                 "GrassBedEdges).")]
        public EnvBlobSet BedBlobs = new EnvBlobSet();

        [Tooltip("Grass->path transition tiles keyed by the 4-bit neighbour mask.")]
        public EnvEdgeSet GrassPathEdges = new EnvEdgeSet();
        [Tooltip("Grass->water shoreline tiles keyed by the 4-bit neighbour mask.")]
        public EnvEdgeSet GrassWaterEdges = new EnvEdgeSet();
        [Tooltip("Grass->garden-bed transition tiles keyed by the 4-bit neighbour mask.")]
        public EnvEdgeSet GrassBedEdges = new EnvEdgeSet();

        [Header("ENV decals (scatter layer)")]
        [Tooltip("Grass-legal decals (fern/moss/clover). Rule 4: nothing here may read as " +
                 "pickable. Empty = no scatter on grass.")]
        public EnvTileSet GrassDecals = new EnvTileSet();
        [Tooltip("Path-legal decals (footprints/pebbles).")]
        public EnvTileSet PathDecals = new EnvTileSet();
        [Tooltip("Water-legal decals (lily pads/blossoms).")]
        public EnvTileSet WaterDecals = new EnvTileSet();
        [Tooltip("The WARM ACCENT decal (stones) — path-side only and deliberately rare, " +
                 "so it stays a treat rather than a carpet.")]
        public EnvTileSet AccentDecals = new EnvTileSet();

        [Header("ENV props + decor")]
        [Tooltip("Outlined tree variants (cycad/gingko/conifer) at 1x1 world units — the " +
                 "same footprint the flat tree tile had. TreeTile's own sprite is set from " +
                 "[0]; the rest are carried for future variant painting.")]
        public Sprite[] TreeSprites = new Sprite[0];
        [Tooltip("Gingko mid-shake pose, reserved for the Brachio tree-shake beat (trees " +
                 "are tilemap tiles today, so nothing swaps to it yet).")]
        public Sprite TreeShakeSprite;
        [Tooltip("Outlined rock variants (boulder/mossy) at 1x1 world units. RockTile's " +
                 "sprite is set from [0].")]
        public Sprite[] RockSprites = new Sprite[0];
        [Tooltip("Stone bridge decks (a/b) at 1 x 0.5 world units. BridgeTile's sprite is " +
                 "set from [0].")]
        public Sprite[] BridgeSprites = new Sprite[0];
        [Tooltip("The same two bridge decks as paintable tiles, so a multi-cell crossing " +
                 "alternates slab patterns instead of stamping one deck. Empty = the single " +
                 "BridgeTile is painted everywhere, exactly as before. Nothing compares " +
                 "bridge tile identity, so extra tiles here are safe.")]
        public EnvTileSet BridgeTiles = new EnvTileSet();
        [Tooltip("Meadow fence piece for edges running along cell X (screen NE), a " +
                 "canvas-compatible drop-in for Kenney fenceLow_E. Null = SceneBuilder " +
                 "keeps using the Kenney sprite.")]
        public Sprite FenceAlongX;
        [Tooltip("Meadow fence piece for edges running along cell Y (screen NW), a " +
                 "canvas-compatible drop-in for Kenney fenceLow_N.")]
        public Sprite FenceAlongY;

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

        [Tooltip("The backdrop's own TOP EDGE colour, re-sampled by GeneratedArtImporter every " +
                 "time the art is imported. The dig extends the sprite upward with a flat band " +
                 "in this colour to fill a portrait camera rect (DinoDigger-5k8.1); sampling it " +
                 "rather than hard-coding it is what stops a regenerated sky from leaving a " +
                 "seam. Alpha 0 means 'never sampled' and the code falls back to the measured " +
                 "value. Measured: (103, 205, 249) — the top 34 rows vary by 5/255 in total.")]
        public Color DigSkyColor = new Color(103f / 255f, 205f / 255f, 249f / 255f, 1f);

        [Tooltip("The backdrop's own BOTTOM EDGE colour, on the same terms as DigSkyColor — the " +
                 "flat band that carries the soil down past the deepest row. Measured: " +
                 "(163, 105, 53), the bottom 46 rows varying by 6/255.")]
        public Color DigSoilColor = new Color(163f / 255f, 105f / 255f, 53f / 255f, 1f);

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

        [Header("Fossil bones (DinoDigger-0z5)")]
        [Tooltip("Assembled bone props indexed by BoneType (0 small bone, 1 femur, 2 rib, " +
                 "3 skull) — the whole bone that rises out of the pit once every cell of it " +
                 "has been uncovered. Left null (the D2 art ticket has not landed yet) the " +
                 "dig falls back to the treasure bone sprite, and then to a plain white " +
                 "silhouette sized to the bone's footprint, so a bone ALWAYS pops visibly.")]
        public Sprite[] BoneSprites = new Sprite[4];

        [Header("Skeleton board (DinoDigger-5ve)")]
        [Tooltip("Dark skeleton SILHOUETTES for the five fossil species, indexed by " +
                 "Config.SkeletonPlan.Species order (Pteranodon, Ankylosaurus, Spinosaurus, " +
                 "Parasaurolophus, Velociraptor). Drawn dark on the collection board and " +
                 "filled in bone by bone; a completed species swaps to its REAL sprite in full " +
                 "colour. Any slot left null (art not imported) falls back to a plain dark card " +
                 "at runtime, so the board still fills and every slot is still tappable.")]
        public Sprite[] SkeletonBoards = new Sprite[5];

        [Tooltip("The board's own HUD button icon. Falls back to the skull bone sprite, then " +
                 "the treasure icon, so the button is never invisible.")]
        public Sprite BoneButtonIcon;

        [Header("The Dino-Matic (DinoDigger-3rz)")]
        [Tooltip("Excavation states of the left-behind revival machine, in the same order a " +
                 "town building uses them: 0 = the buried mound with the dome glint the child " +
                 "first spots, 1..3 = the NPC crew digging it out, 4 = the finished machine. " +
                 "Missing states fall back to the generic BuildingStates placeholder exactly " +
                 "as a half-generated town building does.")]
        public BuildingArt DinoMaticArt = new BuildingArt();

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

        [Header("Machine Friends (DinoDigger-b48)")]
        [Tooltip("Overworld sprites for the three helper machines, imported ~1.1 world units " +
                 "tall with a bottom-center pivot so they stand on the ground like a prop. " +
                 "Direct typed fields, no reflection. Any slot left null (art not imported, " +
                 "stale library asset) falls back at runtime to the mound sprite under the " +
                 "machine's signature tint, so a machine is ALWAYS visible and tappable — a " +
                 "sleeping friend never turns into an invisible hole in the world.")]
        public Sprite MachineDoodle;      // wind-up music box on wheels (plaza)
        public Sprite MachineSprinkles;   // squat watering bot (berry garden)
        public Sprite MachineTuggy;       // palm-sized tugboat (streams)

        /// <summary>Overworld sprite for <paramref name="machine"/> in roster order
        /// (0 Doodle, 1 Sprinkles, 2 Tuggy), or null when that art is not imported yet.</summary>
        public Sprite Machine(int machine)
        {
            switch (machine)
            {
                case 0: return MachineDoodle;
                case 1: return MachineSprinkles;
                case 2: return MachineTuggy;
                default: return null;
            }
        }

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

        /// <summary>Assembled bone art for <paramref name="boneIndex"/> ((int)BoneType), or null
        /// when the fossil art has not been generated yet — the dig site then falls back to the
        /// treasure bone / a white silhouette rather than popping nothing.</summary>
        public Sprite Bone(int boneIndex)
        {
            return Pick(BoneSprites, boneIndex);
        }

        /// <summary>Skeleton silhouette for a fossil species by its board index (0-4, see
        /// <c>Config.SkeletonPlan.Species</c>), or null when that art is not imported — the
        /// board then draws a plain dark card in its place.</summary>
        public Sprite SkeletonBoard(int boardIndex)
        {
            if (SkeletonBoards == null || boardIndex < 0 || boardIndex >= SkeletonBoards.Length)
            {
                return null;
            }

            return SkeletonBoards[boardIndex];
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

        // ------------------------------------------------- ENV lookups (DinoDigger-y1g)
        // One lookup shared by the painter (SceneBuilder) and the verifier
        // (EnvDressingApplied), so the two can never drift apart.

        /// <summary>The base-variant set for a biome (never null; may be empty).</summary>
        public EnvTileSet GroundSet(EnvBiome biome)
        {
            switch (biome)
            {
                case EnvBiome.Path: return PathTiles ?? (PathTiles = new EnvTileSet());
                case EnvBiome.Water: return WaterTiles ?? (WaterTiles = new EnvTileSet());
                case EnvBiome.Bed: return BedTiles ?? (BedTiles = new EnvTileSet());
                default: return GrassTiles ?? (GrassTiles = new EnvTileSet());
            }
        }

        /// <summary>The FLAT placeholder tile a biome falls back to when its env art is
        /// absent — the exact tile SceneBuilder painted before the env set existed. The
        /// garden bed has no flat predecessor, so it falls back to grass.</summary>
        public TileBase FlatTile(EnvBiome biome)
        {
            switch (biome)
            {
                case EnvBiome.Path: return PathTile;
                case EnvBiome.Water: return WaterTile;
                case EnvBiome.Bed: return GrassTile;
                default: return GrassTile;
            }
        }

        /// <summary>The grass-to-<paramref name="other"/> transition family, or null when
        /// that biome has no transition art (the caller then paints the plain variant).</summary>
        public EnvEdgeSet EdgeSet(EnvBiome other)
        {
            switch (other)
            {
                case EnvBiome.Path: return GrassPathEdges;
                case EnvBiome.Water: return GrassWaterEdges;
                case EnvBiome.Bed: return GrassBedEdges;
                default: return null;
            }
        }

        /// <summary>The decal bucket legal on a biome (rule 4 of the style contract), or
        /// null when that biome takes no scatter.</summary>
        public EnvTileSet DecalSet(EnvBiome biome)
        {
            switch (biome)
            {
                case EnvBiome.Grass: return GrassDecals;
                case EnvBiome.Path: return PathDecals;
                case EnvBiome.Water: return WaterDecals;
                default: return null;
            }
        }

        /// <summary>The connected (topology-keyed) set for a biome, or null for grass —
        /// grass is the universal background every other biome banks into, so it never
        /// needs pieces of its own.</summary>
        public EnvBlobSet BlobSet(EnvBiome biome)
        {
            switch (biome)
            {
                case EnvBiome.Water: return WaterBlobs;
                case EnvBiome.Path: return PathBlobs;
                case EnvBiome.Bed: return BedBlobs;
                default: return null;
            }
        }

        /// <summary>True when a biome paints CONNECTED pieces (and therefore owns its own
        /// transition, so the grass beside it must stay plain).</summary>
        public bool UsesBlobs(EnvBiome biome)
        {
            EnvBlobSet set = BlobSet(biome);
            return set != null && set.HasAll;
        }

        /// <summary>
        /// The ground tile this library would paint at <paramref name="cell"/>. PURE —
        /// same inputs always give the same tile, which is what makes the painted island
        /// reproducible across rebuilds AND lets a test predict every cell.
        ///
        /// Three tiers, in order:
        ///   1. CONNECTED piece, keyed by the 8-neighbourhood <paramref name="sameForBlob"/>
        ///      describes. This is what water/path/bed use now (DinoDigger-l9g).
        ///   2. Grass-side transition tile, for a grass cell bordering a biome that is NOT
        ///      painting connected pieces — the pre-l9g behaviour, kept as the fallback.
        ///      Deliberately NOT applied next to a connected biome: that tile already
        ///      paints its own grass bank, and melting water into the grass as well would
        ///      put blue on one side of a seam and sand on the other.
        ///   3. A hashed flat variant, then the flat placeholder.
        /// </summary>
        public TileBase GroundTileFor(Vector3Int cell, EnvBiome biome,
            System.Func<Vector3Int, EnvBiome> biomeAt,
            System.Func<Vector3Int, bool> sameForBlob)
        {
            if (sameForBlob != null && UsesBlobs(biome))
            {
                TileBase piece = BlobSet(biome).Piece(EnvDressing.BlobKey(cell, sameForBlob));
                if (piece != null)
                {
                    return piece;
                }
            }

            if (biome == EnvBiome.Grass && biomeAt != null)
            {
                int mask = EnvDressing.EdgeMask(cell, biomeAt, out EnvBiome other);
                if (!UsesBlobs(other))
                {
                    EnvEdgeSet edges = EdgeSet(other);
                    TileBase edge = edges != null ? edges.Edge(mask) : null;
                    if (edge != null)
                    {
                        return edge;
                    }
                }
            }

            TileBase variant = GroundSet(biome).Variant(cell, EnvDressing.SaltFor(biome));
            return variant != null ? variant : FlatTile(biome);
        }

        /// <summary>The decal tile this library would scatter at <paramref name="cell"/> on
        /// <paramref name="biome"/>, or null for "leave the cell bare". Also pure — the
        /// scatter is reproducible, not random. <paramref name="chance"/> is the per-cell
        /// density and <paramref name="accentShare"/> the odds a path decal is the rare
        /// warm-stone accent.</summary>
        public TileBase DecalTileFor(Vector3Int cell, EnvBiome biome, float chance,
            float accentShare)
        {
            EnvTileSet bucket = DecalSet(biome);
            if (bucket == null || !bucket.HasAny || chance <= 0f)
            {
                return null;
            }

            if (EnvDressing.Roll(cell, EnvDressing.SaltDecalPlace) >= chance)
            {
                return null;
            }

            if (biome == EnvBiome.Path && AccentDecals != null && AccentDecals.HasAny &&
                EnvDressing.Roll(cell, EnvDressing.SaltDecalPick) < accentShare)
            {
                TileBase accent = AccentDecals.Variant(cell, EnvDressing.SaltDecalPick);
                if (accent != null)
                {
                    return accent;
                }
            }

            return bucket.Variant(cell, EnvDressing.SaltDecalPick);
        }

        /// <summary>True when any part of the env ground set is imported — the switch that
        /// tells SceneBuilder there is something to dress with at all.</summary>
        public bool HasEnvGround =>
            GroundSet(EnvBiome.Grass).HasAny || GroundSet(EnvBiome.Path).HasAny ||
            GroundSet(EnvBiome.Water).HasAny || GroundSet(EnvBiome.Bed).HasAny ||
            UsesBlobs(EnvBiome.Water) || UsesBlobs(EnvBiome.Path) || UsesBlobs(EnvBiome.Bed);

        /// <summary>True when any decal bucket is imported.</summary>
        public bool HasEnvDecals =>
            (GrassDecals != null && GrassDecals.HasAny) ||
            (PathDecals != null && PathDecals.HasAny) ||
            (WaterDecals != null && WaterDecals.HasAny) ||
            (AccentDecals != null && AccentDecals.HasAny);

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
