using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using DinoDigger.Config;
using DinoDigger.Core;

namespace DinoDigger.EditorTools
{
    /// <summary>
    /// Imports the final AI-generated sprites + Kenney audio over the placeholder
    /// config assets. Idempotent — safe to re-run. Menu: DinoDigger/Import Generated Art.
    ///
    /// Art direction ("toy-box island"): the outlined chunky AI actors read over a
    /// simple flat environment. The tilemap tiles (grass/path/water/tree/rock/mound)
    /// intentionally stay on the procedural placeholders — this tool only swaps the
    /// actors, items, dirt, particles, and audio.
    ///
    /// PPU is computed per-category from each PNG's actual source height so sprites
    /// read at a consistent in-game world size regardless of the raw AI resolution:
    ///   backhoe + dinos  -> ~1.30 world units tall  (placeholder backhoe was ~1.2)
    ///   eggs/fruit/treasure -> ~0.70 world units tall
    ///   dirt tiles        -> ~1.00 world units (fills the 1x1 dig-grid cell)
    ///   particles         -> ~0.35 world units tall
    /// Directional character sets share one PPU (from the tallest facing) so an actor
    /// does not change size as it turns; single items compute PPU per file.
    /// </summary>
    public static class GeneratedArtImporter
    {
        private const string GenRoot = "Assets/Art/Generated";
        private const string ConfigDir = "Assets/Art/Placeholder/Config";

        private const string DigitalAudioDir = "Assets/Audio/Kenney/DigitalAudio";
        private const string InterfaceDir = "Assets/Audio/Kenney/InterfaceSounds";
        private const string ImpactDir = "Assets/Audio/Kenney/ImpactSounds";
        private const string JinglesDir = "Assets/Audio/Kenney/MusicJingles";
        private const string MusicPath = "Assets/Audio/Music/Bluebonnet_looped.ogg";

        // Target world-space heights per category.
        private const float CharTargetH = 1.30f;
        private const float ItemTargetH = 0.70f;
        private const float DirtTargetH = 1.00f;
        private const float ParticleTargetH = 0.35f;

        // Dig excavator arm segments render 1:1 (Simple draw mode, UNIFORM scale —
        // zero stretching, so the pin bosses stay perfect circles). PPU maps the
        // art's measured pin-to-pin distance to the rig's bone length, and the
        // sprite pivot sits ON the base pin boss centroid, so the joints rotate
        // about the drawn circles. All pin numbers below were MEASURED from the
        // generated art (dark pin-hole centroids, Tools-side numpy; re-measure if
        // the art is regenerated) — keep in sync with DigModeController's pin
        // constants.
        private const float BoomLenWorld = 3.4f;    // == DigModeController.BoomLen
        private const float StickLenWorld = 3.1f;    // == DigModeController.StickLen
        private const float BoomPinDistPx = 681.2f;  // base->tip pin distance, art px
        private const float StickPinDistPx = 737.1f;
        private static readonly Vector2 BoomBasePin = new Vector2(0.1393f, 0.3525f);
        private static readonly Vector2 StickBasePin = new Vector2(0.1162f, 0.5026f);
        private const float BucketTargetH = 0.72f;   // toothed bucket height

        // Dig arm V2 (DinoDigger-rrn): the proportionate slim art set in digarm2/.
        // Same import conventions as V1 (pin-boss pivots, pin-to-pin PPU); numbers
        // measured by Tools/generate_digarm2.py `measure` — keep in sync with
        // digarm2/pins.json and DigArmV2.cs when the art is regenerated.
        private const float Boom2PinDistPx = 716.9f;
        private const float Stick2PinDistPx = 703.0f;
        private static readonly Vector2 Boom2BasePin = new Vector2(0.1258f, 0.2487f);
        private static readonly Vector2 Stick2BasePin = new Vector2(0.1166f, 0.5069f);
        private static readonly Vector2 Bucket2Pivot = new Vector2(0.8981f, 0.8249f);
        private const float Bucket2TargetH = 1.0f;   // V2 bucket is the arm-end star

        // Town buildings are sized by WIDTH so each footprint reads at a consistent
        // ~2.2 world units across regardless of raw resolution: PPU = sourceWidth / 2.2.
        // All construction states share the width target + a bottom-center pivot so they
        // sit on one ground line and grow upward as the silhouette gets taller.
        private const float BuildingTargetW = 2.2f;

        // Construction-worker props (DinoDigger-771). Sized to read at a fixed WORLD
        // size next to the chunky dinos: the hat is measured by WIDTH so it caps a
        // head, the mallet by HEIGHT so it reads as a hand tool, and the ground sign
        // by WIDTH. Hat + mallet keep the default CENTER pivot (ConfigureSprite) so the
        // runtime overlay can pin them by bounds; the sign takes a BOTTOM-CENTER pivot
        // (ConfigureBuilding) so it plants on the ground like a building.
        private const float HardHatTargetW = 0.5f;
        private const float ToolHammerTargetH = 0.45f;
        private const float ConstructionSignTargetW = 0.8f;

        // Town building construction-state file suffixes, ASCENDING completeness: s0
        // (ground-breaking dirt) .. s3 (nearly done) then the finished building. Matches
        // BuildingController's state indices (0..3 building, 4 == finished). Sliced by
        // Tools/slice_sprites.py to Generated/town/<slug>_<suffix>.png.
        private static readonly string[] BuildingStateSuffix = { "s0", "s1", "s2", "s3", "done" };

        // The nine curated town buildings in BUILD ORDER (== plot order == the order of
        // GameConfig.TownBuildingPrices and PlaceholderLibrary.TownBuildings). Slugs are the
        // generated-art folder names under Generated/town/.
        //
        // Art arrives in batches (DinoDigger-mnn), so this table is deliberately tolerant of
        // gaps: a slug with no PNGs at all (fossil_fountain, whose batch stopped on the
        // OpenRouter monthly cap) or only some states (gronks_grocer: done + s3) imports what
        // exists, leaves the rest null, and the runtime falls back to the generic
        // PlaceholderLibrary.BuildingStates placeholder for the states it is missing.
        private static readonly string[] TownBuildingSlugs =
        {
            "pebble_playground", // 0  Pebble Playground    10
            "boulder_brew",      // 1  Boulder Brew         25
            "slate_library",     // 2  Slate Library        50
            "bedrock_bijou",     // 3  Bedrock Bijou        90
            "boneanza_bowling",  // 4  Bone-anza Bowling   150
            "dino_daycare",      // 5  Dino Daycare        240
            "tarpit_springs",    // 6  Tar-Pit Springs     380
            "gronks_grocer",     // 7  Gronk's Grocer      490
            "fossil_fountain",   // 8  Fossil Fountain     600 (finale)
        };

        // The generic placeholder set (PlaceholderLibrary.BuildingStates) stays pointed at the
        // FIRST building's art: it is the fallback every plot falls back to for any state whose
        // per-building art is missing, so it must always be a complete five-state set.
        private const int GenericBuildingStatesSlug = 0;

        // MACHINE FRIENDS (epic DinoDigger-b48), transparent PNGs in Generated/machines/.
        //
        // These are, for now, the CONCEPT paintings copied straight over from
        // Assets/Art/Concepts/machines/ — they are clean, single-subject and already
        // transparent, so they read fine in-game and unblock the whole feature while the art
        // agent is busy elsewhere. Dedicated dormant/awake art is a follow-up; the dormant
        // state is currently a colour multiply on this same sprite (see MachineFriend), which
        // is why one sprite per machine is enough.
        //
        // Sized by HEIGHT to a shared overworld-prop target so the three read as one family
        // whatever their raw resolution, with a BOTTOM-CENTER pivot so they stand on the
        // ground line like a building instead of floating half-sunk. Roster order matches
        // MachineKind / PlaceholderLibrary.Machine(int): Doodle, Sprinkles, Tuggy.
        private const float MachineTargetH = 1.1f;
        private static readonly string[] MachineRels =
        {
            "machines/doodle",     // 0  wind-up music box on wheels (plaza)
            "machines/sprinkles",  // 1  squat watering bot (berry garden)
            "machines/tuggy",      // 2  palm-sized tugboat (streams)
        };

        // Construction-worker props (DinoDigger-771), transparent PNGs in Generated/town/.
        private const string HardHatRel = "town/prop_hardhat";
        private const string ToolHammerRel = "town/prop_tool_hammer";
        private const string ConstructionSignRel = "town/prop_sign_construction";

        // Dig toys (DinoDigger-z4d), sliced to Generated/dig/. Crystals, the boom geode and the
        // pinata pot all occupy ONE grid cell, exactly like a dirt tile — but their raw art is
        // not square (a crystal is tall, a geode is wide), so sizing them by height the way the
        // dirt states are sized would push a wide one out over its neighbours. They are sized by
        // their LARGER dimension instead: PPU = max(w,h) / DirtTargetH, which fits every toy
        // inside the same 1x1 cell footprint whatever its aspect. The three crystal colours are
        // pixel-identical silhouettes, so they all land on the same PPU anyway and a colour swap
        // can never change a tile's footprint.
        private static readonly string[] DigCrystalRels =
            { "dig/crystal_teal", "dig/crystal_coral", "dig/crystal_gold" };
        private const string BoomGeodeRel = "dig/boom_geode";
        private const string PinataPotRel = "dig/pinata_pot";
        private const string PinataPotCrackedRel = "dig/pinata_pot_cracked";

        // The landing/geode dust puff is a PARTICLE sprite (the emitter sets its world size), so
        // it just needs a sane import scale like the other particles.
        private const string DustPuffRel = "dig/dust_thump";

        // FOSSIL BONES (DinoDigger-0z5), sliced to Generated/dig/bones/. Indexed by BoneType
        // (0 SmallBone, 1 Femur, 2 Rib, 3 Skull) — the enum is a stable contract the save keys
        // off, so this table's ORDER matters and must never be shuffled. The generated set has
        // more bones than the game models (claw, horn, jaw, pelvis, toe, tooth); those are
        // spare art for a future skeleton, and picking the vertebra as the "small bone" keeps
        // the 1x2 stub reading as a piece of spine rather than a mystery lump.
        //
        // Sized like a dig toy — LARGER dimension fitted to one grid cell — because the pop
        // prop scales a bone up from its own aspect (see DigModeController.SpawnBoneProp) and
        // the skeleton board draws them into fixed UI rects with preserveAspect, so a
        // consistent one-cell base is what makes every bone read at a comparable size.
        private static readonly string[] BoneRels =
        {
            "dig/bones/bone_vertebra", // 0 SmallBone — the 1x2 stub
            "dig/bones/bone_femur",    // 1 Femur
            "dig/bones/bone_rib",      // 2 Rib
            "dig/bones/bone_skull",    // 3 Skull
        };

        // SKELETON BOARD SILHOUETTES (DinoDigger-5ve), one per fossil species in
        // Config.SkeletonPlan.Species order. Drawn in the HUD (where PPU does not matter) and
        // ALSO as the ghost that floats into the Dino-Matic during a revival (where it does),
        // so they import by HEIGHT to the character target — the ghost is meant to read as a
        // whole dinosaur skeleton standing next to the machine.
        private static readonly string[] SkeletonBoardRels =
        {
            "dig/boards/board_pteranodon",
            "dig/boards/board_ankylosaurus",
            "dig/boards/board_spinosaurus",
            "dig/boards/board_parasaurolophus",
            "dig/boards/board_velociraptor",
        };

        // THE DINO-MATIC (DinoDigger-3rz): an excavation, not a construction, but mechanically
        // the same five-state ladder as a town building (s0 = the buried mound with the dome
        // glint, s1..s3 = the crew digging it out, done = the working machine), so it imports
        // on exactly the town's terms — width-normalised with a bottom-center pivot — and
        // BuildingController drives it unchanged.
        private const string DinoMaticSlug = "dinomatic";

        // Dig-mode background is sized by WIDTH so it covers the whole camera view.
        // During dig the camera uses GameConfig.DigOrthoSize (3.2) => visible width at
        // 16:10 is 2 * 3.2 * 1.6 = 10.24 world units; the dig grid is 7 columns wide.
        // Target the backdrop at 14 world units wide so it covers the view with margin
        // no matter the raw resolution: PPU = sourceWidthPx / 14.
        private const float DigBgTargetW = 14.0f;

        // Generated directional filenames, indexed by Dir8 (N,NE,E,SE,S,SW,W,NW).
        private static readonly string[] Dir8Suffix = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        // Growth-stage art sets. The adult (Big) stage keeps the <folder>_<DIR>.png
        // names; baby/kid live at <folder>/<stage>_<DIR>.png (see Tools/generate_sprites.py).
        private static readonly string[] StageNames = { "baby", "kid" };

        // Walk-stride frames (y85.1). Adult strides: <folder>/walkA_<DIR>.png and
        // walkB_<DIR>.png (unprefixed, like the adult idles); stage strides:
        // <folder>/<stage>_walkA_<DIR>.png. Optional — only piloted dinos have them.
        private static readonly string[] StridePoses = { "walkA", "walkB" };

        // Backhoe roll frames (DinoDigger-682): <folder>/rollA_<DIR>.png and
        // rollB_<DIR>.png (unprefixed, like the adult idles). Backhoe-only; same
        // path shape as an adult stride so StridePath(folder, null, pose, i) reaches
        // them and LoadStrideSet loads them.
        private static readonly string[] RollPoses = { "rollA", "rollB" };

        // Actor folders that carry an 8-direction set.
        private static readonly string[] CharacterFolders =
            { "backhoe", "trex", "triceratops", "brachiosaurus", "stegosaurus",
              "pteranodon", "ankylosaurus", "spinosaurus", "parasaurolophus", "velociraptor" };

        // Dino roster wiring: definition asset name, generated folder, egg color file.
        private struct DinoWire
        {
            public string AssetName;   // Dino_<AssetName>.asset
            public string Folder;      // Assets/Art/Generated/<Folder>/
            public string EggFile;     // eggs/<EggFile>.png
            public DinoWire(string a, string f, string e) { AssetName = a; Folder = f; EggFile = e; }
        }

        private static readonly DinoWire[] Dinos =
        {
            new DinoWire("TRex",          "trex",          "egg_green"),
            new DinoWire("Triceratops",   "triceratops",   "egg_orange"),
            new DinoWire("Brachiosaurus", "brachiosaurus", "egg_blue"),
            new DinoWire("Stegosaurus",   "stegosaurus",   "egg_purple"),
            // Shard-exclusive species (bl6.2). Egg files land in eggs/egg_<color>.png
            // like the original four; each shell's color/pattern telegraphs the dino.
            new DinoWire("Pteranodon",      "pteranodon",      "egg_teal"),
            new DinoWire("Ankylosaurus",    "ankylosaurus",    "egg_red"),
            new DinoWire("Spinosaurus",     "spinosaurus",     "egg_olive"),
            new DinoWire("Parasaurolophus", "parasaurolophus", "egg_pink"),
            new DinoWire("Velociraptor",    "velociraptor",    "egg_grey"),
        };

        // ===================== ENV: Jurassic-earth environment set (DinoDigger-y1g)
        //
        // WORLD SIZE IS THE CONTRACT, NOT PPU. Every env sprite replaces a placeholder
        // whose world footprint (px / PPU) it must match EXACTLY — that is what lets this
        // be an art-only swap with no collider, cell pitch or spawn rect retuned. So the
        // importer stores the TARGET WORLD WIDTH per asset and derives
        //     PPU = sourceWidthPx / targetWorldWidth
        // rather than hardcoding the PPU. Re-baking the art at a different resolution then
        // keeps the same footprint automatically instead of silently resizing the island.
        // With the shipped 2026-08 bake the derivation reproduces the mapping in
        // Tools/generate_env.py's docstring exactly:
        //   ground tile / bridge / mound  256 px wide, 1.00 u  -> PPU 256   (was 128 @ 128)
        //   tree / rock                   256 px wide, 1.00 u  -> PPU 256   (was 128 @ 128)
        //   nest                          256 px wide, 1.28 u  -> PPU 200   (was 128 @ 100)
        //   fence_x / fence_y             256 px wide, 2.56 u  -> PPU 100   (== Kenney)
        //
        // PIVOTS ARE CENTER, and that is deliberate — the ticket's "bottom-center like the
        // current props" does not describe this project. Every prop being replaced here is
        // either a TILEMAP TILE (tree/rock/mound/bridge: the tilemap plants the sprite on
        // the cell anchor, so a bottom pivot would shift the whole island up half a cell)
        // or a center-pivoted SpriteRenderer prop (the DigMound sprite sits centered on its
        // CircleCollider2D; the nest bowl sits at its parent's local origin; the Kenney
        // fence pieces are center-pivoted and SceneBuilder offsets them by half their
        // height). Same canvas + same pivot + same world size = pixel-identical placement
        // and untouched colliders. ConfigureBuilding's BOTTOM-CENTER treatment is for
        // buildings/machines, which stand on a ground line — none of these do.
        //
        // NOT IMPORTED, ON PURPOSE: env/ground/plate_*.png and env/decor/plate_stone.png
        // are 1024^2 PIPELINE MASTERS (re-slice sources), and contact_sheet.png /
        // verify_*.png are review artifacts. None is referenced by the game, so none ever
        // enters the player build; leaving their import settings alone keeps the AssetDB
        // churn (and the 3260x3804 contact sheet) out of every import run.
        private const string EnvDir = GenRoot + "/env";
        private const string EnvTilesDir = EnvDir + "/Tiles";

        // Ground plates slice 4x4 (the bed plate only yields four usable squares).
        private const int EnvTileVariants = 16;
        private const int EnvBedVariants = 4;
        // The baked transition families cover every non-empty 4-bit neighbour mask, 1..15.
        private const int EnvEdgeMasks = 16;

        // One iso ground cell is 1.0 x 0.5 world units (Grid cellSize), so every ground
        // tile, bridge deck and mound targets 1.0 world units WIDE.
        private const float EnvCellWorldW = 1.0f;
        private const float EnvPropWorldW = 1.0f;   // tree / rock: 1x1, as the flat tiles were
        private const float EnvNestWorldW = 1.28f;  // == nest_base.png 128px @ PPU 100
        private const float EnvFenceWorldW = 2.56f; // == Kenney fenceLow_* 256px @ PPU 100

        // Decal world WIDTHS, copied from Tools/generate_env.py's DECAL_WORLD_W — decals
        // ship TRIMMED, so height follows from the art's own aspect. Keep in sync with the
        // generator if the set is re-baked.
        private struct EnvDecalArt
        {
            public string File;
            public float WorldW;
            public EnvDecalArt(string f, float w) { File = f; WorldW = w; }
        }

        // Rule 4 of the style contract ("life in clusters, not carpets") is a GRAMMAR, and
        // these buckets are it: grass takes only things that cannot read as pickable, path
        // takes ground marks, water takes lilies, and the warm stone accent is its own rare
        // bucket. SceneBuilder never scatters a decal outside its bucket's biome.
        private static readonly EnvDecalArt[] EnvGrassDecalArt =
        {
            new EnvDecalArt("decal_fern", 0.42f),
            new EnvDecalArt("decal_moss", 0.34f),
            new EnvDecalArt("decal_clover", 0.20f),
        };

        private static readonly EnvDecalArt[] EnvPathDecalArt =
        {
            new EnvDecalArt("decal_footprints", 0.30f),
            new EnvDecalArt("decal_pebbles", 0.26f),
        };

        private static readonly EnvDecalArt[] EnvWaterDecalArt =
        {
            new EnvDecalArt("decal_lily", 0.22f),
            new EnvDecalArt("decal_lily_blossom", 0.26f),
        };

        private static readonly EnvDecalArt[] EnvAccentDecalArt =
        {
            new EnvDecalArt("decal_stones", 0.30f),
        };

        // Outlined tappable props. [0] of each list is the one whose sprite is written into
        // the existing Tile asset (see WireEnvLibrary) — the rest ride along in the library
        // for the shake beat and any future variant pass.
        private static readonly string[] EnvTreeArt =
            { "tree_cycad", "tree_gingko", "tree_conifer" };
        private const string EnvTreeShakeArt = "tree_gingko_shake";
        private static readonly string[] EnvRockArt = { "rock_boulder", "rock_mossy" };
        private static readonly string[] EnvBridgeArt = { "bridge_a", "bridge_b" };
        private const string EnvMoundArt = "mound";
        private const string EnvNestArt = "nest";
        private const string EnvFenceXArt = "fence_x";
        private const string EnvFenceYArt = "fence_y";

        [MenuItem("DinoDigger/Import Generated Art")]
        public static void Import()
        {
            AssetDatabase.Refresh();

            var missing = new List<string>();
            var wired = new List<string>();

            // ------------------------------------------------ 1) texture importers
            // Directional character sets: one shared PPU per actor (tallest facing).
            float backhoePpu = 0f;
            foreach (string folder in CharacterFolders)
            {
                var paths = new List<string>(8);
                int maxH = 0;
                for (int i = 0; i < 8; i++)
                {
                    string p = CharPath(folder, i);
                    paths.Add(p);
                    maxH = Mathf.Max(maxH, SourceHeight(p));
                }

                if (maxH <= 0)
                {
                    missing.Add($"{folder}/* (no readable source textures)");
                    continue;
                }

                float ppu = maxH / CharTargetH;
                foreach (string p in paths)
                {
                    ConfigureSprite(p, ppu, missing);
                }

                // Per-stage art (baby/kid) shares the SAME per-actor PPU as the adult
                // set so a dino never changes world size just from swapping sets on
                // growth — the subtle size delta is carried by GameConfig.StageScales,
                // the SHAPE delta by the art itself. Silently skipped for folders with
                // no stage files (e.g. backhoe); dino wiring below tracks any misses.
                foreach (string stage in StageNames)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        string sp = StagePath(folder, stage, i);
                        if (SourceHeight(sp) > 0)
                        {
                            ConfigureSprite(sp, ppu, missing);
                        }
                    }
                }

                // Walk-stride frames (y85.1): same per-actor PPU as the idle set —
                // the slicer aligns each stride frame to its idle frame's canvas, so
                // equal PPU keeps the body pixel-stationary when frames swap. Adult
                // strides are walkA/walkB_<DIR>.png (no stage prefix, matching the
                // unprefixed adult idles); stage strides are <stage>_walkA_<DIR>.png.
                // Silently skipped where absent (only piloted dinos have them).
                foreach (string pose in StridePoses)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        string wp = StridePath(folder, null, pose, i);
                        if (SourceHeight(wp) > 0)
                        {
                            ConfigureSprite(wp, ppu, missing);
                        }

                        foreach (string stage in StageNames)
                        {
                            string wsp = StridePath(folder, stage, pose, i);
                            if (SourceHeight(wsp) > 0)
                            {
                                ConfigureSprite(wsp, ppu, missing);
                            }
                        }
                    }
                }

                if (folder == "backhoe")
                {
                    backhoePpu = ppu; // reuse for the armless dig body so it matches scale

                    // Wheel-roll frames (DinoDigger-682): same per-actor PPU as the
                    // idle set — the slicer aligns each roll frame to the idle
                    // facing's canvas, so equal PPU keeps the body pixel-stationary
                    // when frames swap. Silently skipped when absent (roll art is
                    // optional; the drive cycle stays inert without it).
                    foreach (string pose in RollPoses)
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            string rp = StridePath(folder, null, pose, i);
                            if (SourceHeight(rp) > 0)
                            {
                                ConfigureSprite(rp, ppu, missing);
                            }
                        }
                    }
                }

                wired.Add($"{folder}: 8-dir @ PPU {ppu:F1} (tallest {maxH}px -> {CharTargetH} units)");
            }

            // Single items: PPU per file so each reads at its category target height.
            string[] eggs = { "eggs/egg_green", "eggs/egg_orange", "eggs/egg_blue", "eggs/egg_purple",
                              // shard-exclusive species eggs (bl6.2)
                              "eggs/egg_teal", "eggs/egg_red", "eggs/egg_olive", "eggs/egg_pink", "eggs/egg_grey" };
            string[] fruit = { "fruit/fruit_apple", "fruit/fruit_banana", "fruit/fruit_berries", "fruit/fruit_watermelon" };
            string[] treasure = { "treasure/treasure_coin", "treasure/treasure_gem", "treasure/treasure_boot", "treasure/treasure_bone" };
            string[] dirt = { "dirt/dirt_crack_0", "dirt/dirt_crack_1", "dirt/dirt_crack_2" };
            string[] particles = { "particles/particle_star", "particles/particle_heart", "particles/particle_crumb" };

            ConfigureEach(eggs, ItemTargetH, missing);
            ConfigureEach(fruit, ItemTargetH, missing);
            ConfigureEach(treasure, ItemTargetH, missing);
            ConfigureEach(dirt, DirtTargetH, missing);
            ConfigureEach(particles, ParticleTargetH, missing);

            // Dig toys: fit each one inside the 1x1 grid cell by its LARGER dimension, so a wide
            // geode and a tall crystal both sit inside the same footprint the dirt states use.
            foreach (string rel in DigCrystalRels)
            {
                ConfigureCellFit(rel, missing);
            }

            ConfigureCellFit(BoomGeodeRel, missing);
            ConfigureCellFit(PinataPotRel, missing);
            ConfigureCellFit(PinataPotCrackedRel, missing);
            ConfigureEach(new[] { DustPuffRel }, ParticleTargetH, missing);

            // Fossil bones + skeleton-board silhouettes (DinoDigger-0z5 / -5ve).
            foreach (string rel in BoneRels)
            {
                ConfigureCellFit(rel, missing);
            }

            ConfigureEach(SkeletonBoardRels, CharTargetH, missing);

            // The Dino-Matic's five excavation states, on the town's building terms.
            foreach (string rel in BuildingStatePaths(DinoMaticSlug))
            {
                string dp = GenPath(rel);
                int dw = SourceWidth(dp);
                if (dw > 0)
                {
                    ConfigureBuilding(dp, dw / BuildingTargetW, missing);
                }
                else
                {
                    missing.Add(dp + " (no readable source texture)");
                }
            }

            // Full-bleed dig backdrop: PPU from WIDTH so it covers the camera view.
            const string digBgRel = "digbg/dig_background";
            string digBgPath = GenPath(digBgRel);
            int digBgW = SourceWidth(digBgPath);
            if (digBgW > 0)
            {
                ConfigureSprite(digBgPath, digBgW / DigBgTargetW, missing);
            }
            else
            {
                missing.Add(digBgPath + " (no readable source texture)");
            }

            // Dig excavator rig: armless body (backhoe PPU) + arm pieces (base pivots).
            const string digBodyRel = "digbody/digbody";
            string digBodyPath = GenPath(digBodyRel);
            int digBodyH = SourceHeight(digBodyPath);
            if (digBodyH > 0)
            {
                // Match the backhoe set's PPU so the armless body reads at the same
                // size the old side-view body did; fall back to the char target height.
                float bodyPpu = backhoePpu > 0f ? backhoePpu : digBodyH / CharTargetH;
                ConfigureSprite(digBodyPath, bodyPpu, missing);
            }
            else
            {
                missing.Add(digBodyPath + " (no readable source texture)");
            }

            // Arm segments (anatomical side profiles): pivot ON the measured base
            // pin boss; PPU = measured pin-to-pin px / bone length so the drawn
            // pin spacing equals the rig's bone length at scale 1 (rendered 1:1,
            // no slicing, no stretching — pins stay perfect circles).
            // Bucket: custom pivot ON its drawn hinge bolt (measured at 0.60, 0.88)
            // so the curl rotates about the bolt and the bucket sockets rigidly onto
            // the wrist; PPU from height (uniform scale, no distortion).
            ConfigureArmPiece("digarm/digarm_boom",
                BoomPinDistPx / BoomLenWorld, BoomBasePin, missing);
            ConfigureArmPiece("digarm/digarm_stick",
                StickPinDistPx / StickLenWorld, StickBasePin, missing);
            int bucketH = SourceHeight(GenPath("digarm/digarm_bucket"));
            if (bucketH > 0)
            {
                ConfigureArmPiece("digarm/digarm_bucket",
                    bucketH / BucketTargetH, new Vector2(0.60f, 0.88f), missing);
            }
            else
            {
                missing.Add(GenPath("digarm/digarm_bucket") + " (no readable source texture)");
            }

            // Dig arm V2 (DinoDigger-rrn): same conventions as the V1 block above.
            // Optional on purpose — a checkout without digarm2/ art just tracks the
            // misses and the rig stays on V1 whatever the config switch says.
            ConfigureArmPiece("digarm2/digarm2_boom",
                Boom2PinDistPx / BoomLenWorld, Boom2BasePin, missing);
            ConfigureArmPiece("digarm2/digarm2_stick",
                Stick2PinDistPx / StickLenWorld, Stick2BasePin, missing);
            int bucket2H = SourceHeight(GenPath("digarm2/digarm2_bucket"));
            if (bucket2H > 0)
            {
                ConfigureArmPiece("digarm2/digarm2_bucket",
                    bucket2H / Bucket2TargetH, Bucket2Pivot, missing);
            }
            else
            {
                missing.Add(GenPath("digarm2/digarm2_bucket") + " (no readable source texture)");
            }

            // Town buildings (DinoDigger-5li.3 + DinoDigger-ggy): PPU from WIDTH so each reads
            // ~BuildingTargetW wide; bottom-center pivot so states share a ground line. All
            // nine curated buildings import the same way; a state whose PNG has not been
            // generated yet is tracked and skipped (never an import error).
            foreach (string slug in TownBuildingSlugs)
            {
                foreach (string rel in BuildingStatePaths(slug))
                {
                    string bp = GenPath(rel);
                    int bw = SourceWidth(bp);
                    if (bw > 0)
                    {
                        ConfigureBuilding(bp, bw / BuildingTargetW, missing);
                    }
                    else
                    {
                        missing.Add(bp + " (no readable source texture)");
                    }
                }
            }

            // Construction-worker props (DinoDigger-771): hat/mallet import like the
            // single items (plain Simple sprite, CENTER pivot) at a per-file PPU; the
            // sign plants on the ground (bottom-center) like a building. Null-safe — a
            // missing PNG is tracked and the runtime feature stays silently absent.
            string hatPath = GenPath(HardHatRel);
            int hatW = SourceWidth(hatPath);
            if (hatW > 0)
            {
                ConfigureSprite(hatPath, hatW / HardHatTargetW, missing);
            }
            else
            {
                missing.Add(hatPath + " (no readable source texture)");
            }

            string hammerPath = GenPath(ToolHammerRel);
            int hammerH = SourceHeight(hammerPath);
            if (hammerH > 0)
            {
                ConfigureSprite(hammerPath, hammerH / ToolHammerTargetH, missing);
            }
            else
            {
                missing.Add(hammerPath + " (no readable source texture)");
            }

            string signPath = GenPath(ConstructionSignRel);
            int signW = SourceWidth(signPath);
            if (signW > 0)
            {
                ConfigureBuilding(signPath, signW / ConstructionSignTargetW, missing);
            }
            else
            {
                missing.Add(signPath + " (no readable source texture)");
            }

            // Machine Friends (DinoDigger-b48): PPU from HEIGHT so each machine stands
            // ~MachineTargetH tall, and ConfigureBuilding's BOTTOM-CENTER pivot so they plant
            // on the ground line like the buildings do. Same tolerant shape as everything
            // above — a machine whose PNG is absent is tracked, skipped, and falls back at
            // runtime to a tinted mound blob rather than blanking out.
            foreach (string rel in MachineRels)
            {
                string mp = GenPath(rel);
                int mh = SourceHeight(mp);
                if (mh > 0)
                {
                    ConfigureBuilding(mp, mh / MachineTargetH, missing);
                }
                else
                {
                    missing.Add(mp + " (no readable source texture)");
                }
            }

            // Jurassic-earth environment set (DinoDigger-y1g): ground/edge tiles, decals,
            // outlined props and decor, every one sized by TARGET WORLD WIDTH so it lands
            // on the exact footprint of the placeholder it replaces.
            ImportEnvTextures(missing, wired);

            AssetDatabase.Refresh();

            // ------------------------------------------------ 2) DinoDefinitions
            int sIndex = (int)Dir8.S;
            foreach (DinoWire d in Dinos)
            {
                string defPath = $"{ConfigDir}/Dino_{d.AssetName}.asset";
                var def = AssetDatabase.LoadAssetAtPath<DinoDefinition>(defPath);
                if (def == null)
                {
                    missing.Add(defPath);
                    continue;
                }

                var walk = new Sprite[8];
                int found = 0;
                for (int i = 0; i < 8; i++)
                {
                    walk[i] = LoadSprite(CharPath(d.Folder, i));
                    if (walk[i] != null)
                    {
                        found++;
                    }
                    else
                    {
                        missing.Add(CharPath(d.Folder, i));
                    }
                }

                def.WalkSprites = walk;
                def.IdleSprite = walk[sIndex]; // S-facing front view as the idle pose

                // Per-stage sets: baby/kid 8-dir arrays. Left empty (null) if the art
                // isn't present; DinoDefinition.StageSprites then falls back to adult.
                def.BabySprites = LoadStageSet(d.Folder, "baby", out int babyFound);
                def.KidSprites = LoadStageSet(d.Folder, "kid", out int kidFound);

                // Walk-stride sets (y85.1): null when absent -> DinoController's
                // cycler stays inert and the dino keeps the static walk behavior.
                def.WalkASprites = LoadStrideSet(d.Folder, null, "walkA", out int strideFound);
                def.WalkBSprites = LoadStrideSet(d.Folder, null, "walkB", out int walkBFound);
                strideFound += walkBFound;
                def.BabyWalkASprites = LoadStrideSet(d.Folder, "baby", "walkA", out int n1);
                def.BabyWalkBSprites = LoadStrideSet(d.Folder, "baby", "walkB", out int n2);
                def.KidWalkASprites = LoadStrideSet(d.Folder, "kid", "walkA", out int n3);
                def.KidWalkBSprites = LoadStrideSet(d.Folder, "kid", "walkB", out int n4);
                strideFound += n1 + n2 + n3 + n4;

                Sprite egg = LoadSprite(GenPath("eggs/" + d.EggFile));
                if (egg != null)
                {
                    def.EggSprite = egg;
                }
                else
                {
                    missing.Add(GenPath(d.EggFile));
                }

                EditorUtility.SetDirty(def);
                wired.Add($"Dino_{d.AssetName}: {found}/8 walk (adult), {babyFound}/8 baby, " +
                          $"{kidFound}/8 kid, {strideFound}/48 strides, " +
                          $"egg={(egg != null ? d.EggFile : "MISSING")}");
            }

            // ------------------------------------------------ 3) PlaceholderLibrary
            string libPath = $"{ConfigDir}/PlaceholderLibrary.asset";
            var lib = AssetDatabase.LoadAssetAtPath<PlaceholderLibrary>(libPath);
            if (lib == null)
            {
                missing.Add(libPath);
            }
            else
            {
                var backhoe = new Sprite[8];
                for (int i = 0; i < 8; i++)
                {
                    backhoe[i] = LoadSprite(CharPath("backhoe", i));
                    if (backhoe[i] == null)
                    {
                        missing.Add(CharPath("backhoe", i));
                    }
                }

                lib.BackhoeDir = backhoe;

                // Wheel-roll frames (DinoDigger-682): null when absent -> the
                // BackhoeController's drive cycler stays inert and it keeps the
                // static facing behavior. Same rollA/rollB_<DIR>.png path as an
                // adult stride, so LoadStrideSet(..., null, "rollA"/"rollB") loads them.
                lib.BackhoeRollA = LoadStrideSet("backhoe", null, "rollA", out int rollAFound);
                lib.BackhoeRollB = LoadStrideSet("backhoe", null, "rollB", out int rollBFound);
                wired.Add($"Library: backhoe roll {rollAFound}/8 A + {rollBFound}/8 B " +
                          (rollAFound + rollBFound > 0 ? "(drive cycle wired)"
                                                       : "(none: static drive)"));

                // Dig-mode side-view body: the scoop rests to the RIGHT of the body
                // (PlaceBackhoe offsets +0.6 x), so the body should face east.
                Sprite bodyE = LoadSprite(CharPath("backhoe", (int)Dir8.E));
                if (bodyE != null)
                {
                    lib.BackhoeBody = bodyE;
                    wired.Add("Library.BackhoeBody = backhoe_E (faces the scoop)");
                }
                else
                {
                    missing.Add(CharPath("backhoe", (int)Dir8.E));
                }

                // ScoopArm: no generated equivalent exists -> keep the placeholder.
                wired.Add("Library.ScoopArm left on placeholder (no generated scoop art)");

                // Dig excavator rig: armless body + two-bone arm + toothed bucket.
                Sprite digBody = LoadSprite(GenPath(digBodyRel));
                if (digBody != null)
                {
                    lib.DigBodySprite = digBody;
                    wired.Add("Library.DigBodySprite = digbody (armless side body)");
                }
                else
                {
                    missing.Add(GenPath(digBodyRel));
                }

                lib.BoomSprite = LoadSpriteTracked("digarm/digarm_boom", missing);
                lib.StickSprite = LoadSpriteTracked("digarm/digarm_stick", missing);
                lib.BucketSprite = LoadSpriteTracked("digarm/digarm_bucket", missing);
                wired.Add("Library: dig rig boom+stick+bucket (base pivots)");

                // Dig arm V2 (DinoDigger-rrn): loaded WITHOUT tracking — a checkout
                // without the V2 art is an expected gap (the importer pass above
                // already reported it) and the rig then stays on V1.
                lib.Boom2Sprite = LoadSprite(GenPath("digarm2/digarm2_boom"));
                lib.Stick2Sprite = LoadSprite(GenPath("digarm2/digarm2_stick"));
                lib.Bucket2Sprite = LoadSprite(GenPath("digarm2/digarm2_bucket"));
                wired.Add("Library: dig rig V2 boom+stick+bucket " +
                          (lib.Boom2Sprite != null && lib.Stick2Sprite != null &&
                           lib.Bucket2Sprite != null
                              ? "(slim set wired; switch = GameConfig.DigArmVersion)"
                              : "(absent: rig stays on V1)"));

                lib.FruitSprites = LoadArray(fruit, missing);
                lib.TreasureSprites = LoadArray(treasure, missing);
                lib.DirtStates = LoadArray(dirt, missing);

                // Dig toys (DinoDigger-z4d). Direct typed assignment, no reflection — same
                // convention as the town block above. Any sprite left null (art not generated /
                // a stale library asset) is handled at runtime: a crystal falls back to a tinted
                // dirt sprite, the dust falls back to the crumb particle. Nothing throws and no
                // cell ever blanks out.
                lib.CrystalSprites = LoadArray(DigCrystalRels, missing);
                lib.BoomGeode = LoadSpriteTracked(BoomGeodeRel, missing);
                lib.PinataPot = LoadSpriteTracked(PinataPotRel, missing);
                lib.PinataPotCracked = LoadSpriteTracked(PinataPotCrackedRel, missing);
                lib.DustPuff = LoadSpriteTracked(DustPuffRel, missing);
                wired.Add($"Library: dig toys crystal x{DigCrystalRels.Length} + geode + pot " +
                          $"(whole/cracked) + dust puff (each fits a {DirtTargetH}-unit grid cell)");

                // Fossil bones, indexed by BoneType (see BoneRels). A null slot falls back at
                // runtime to the treasure bone and then a white silhouette, so a bone ALWAYS
                // pops something visible and the board still fills.
                lib.BoneSprites = LoadArray(BoneRels, missing);

                // Skeleton-board silhouettes, indexed by SkeletonPlan.Species order. Loaded
                // WITHOUT tracking — the pass above already reported anything missing, and the
                // board degrades to a plain dark card rather than blanking out.
                var boards = new Sprite[SkeletonBoardRels.Length];
                int boardsFound = 0;
                for (int i = 0; i < SkeletonBoardRels.Length; i++)
                {
                    boards[i] = LoadSprite(GenPath(SkeletonBoardRels[i]));
                    if (boards[i] != null)
                    {
                        boardsFound++;
                    }
                }

                lib.SkeletonBoards = boards;
                // The HUD bone button reuses the SKULL: it is the most immediately
                // "dinosaur bones" shape in the set and needs no art of its own.
                lib.BoneButtonIcon = lib.Bone((int)DinoDigger.Config.BoneType.Skull);
                wired.Add($"Library: fossil bones x{BoneRels.Length} + skeleton boards " +
                          $"{boardsFound}/{SkeletonBoardRels.Length} (bone button = skull)");

                // The Dino-Matic's excavation states, same shape as a town building's set.
                if (lib.DinoMaticArt == null)
                {
                    lib.DinoMaticArt = new BuildingArt();
                }

                string[] dinoMaticRels = BuildingStatePaths(DinoMaticSlug);
                var dinoMaticStates = new Sprite[dinoMaticRels.Length];
                int dinoMaticFound = 0;
                for (int i = 0; i < dinoMaticRels.Length; i++)
                {
                    dinoMaticStates[i] = LoadSprite(GenPath(dinoMaticRels[i]));
                    if (dinoMaticStates[i] != null)
                    {
                        dinoMaticFound++;
                    }
                }

                lib.DinoMaticArt.States = dinoMaticStates;
                wired.Add($"Library.DinoMaticArt: {dinoMaticFound}/{dinoMaticRels.Length} " +
                          "excavation states (missing ones fall back to the generic placeholder)");

                lib.StarParticle = LoadSpriteTracked(particles[0], missing);
                lib.HeartParticle = LoadSpriteTracked(particles[1], missing);
                lib.CrumbParticle = LoadSpriteTracked(particles[2], missing);

                Sprite digBg = LoadSprite(GenPath(digBgRel));
                if (digBg != null)
                {
                    lib.DigBackground = digBg;
                    wired.Add($"Library.DigBackground = {digBgRel} (PPU {(digBgW > 0 ? digBgW / DigBgTargetW : 0f):F1}, ~{DigBgTargetW} units wide)");
                }
                else
                {
                    missing.Add(GenPath(digBgRel));
                }

                // Town building states (DinoDigger-5li.3 / -ggy). The generic BuildingStates
                // keeps the first building's five states — it is the placeholder every plot
                // falls back to — and each of the nine curated buildings gets its own set in
                // PlaceholderLibrary.TownBuildings, indexed by BUILD ORDER. Direct typed
                // assignment, no reflection; a building with no (or partial) art leaves those
                // slots null and BuildingController falls back to the generic set state by state.
                lib.BuildingStates = LoadArray(
                    BuildingStatePaths(TownBuildingSlugs[GenericBuildingStatesSlug]), missing);
                wired.Add($"Library: generic BuildingStates = {TownBuildingSlugs[GenericBuildingStatesSlug]} " +
                          $"s0..s3+done (~{BuildingTargetW}u wide, bottom pivot)");

                if (lib.TownBuildings == null || lib.TownBuildings.Length != TownBuildingSlugs.Length)
                {
                    // Older library asset (saved before the per-building table existed) or a
                    // roster resize: rebuild the array so every curated building has a slot.
                    lib.TownBuildings = new BuildingArt[TownBuildingSlugs.Length];
                }

                for (int b = 0; b < TownBuildingSlugs.Length; b++)
                {
                    if (lib.TownBuildings[b] == null)
                    {
                        lib.TownBuildings[b] = new BuildingArt();
                    }

                    // Load WITHOUT tracking: the missing state PNGs were already reported by the
                    // importer pass above, and a not-yet-generated building is an expected gap
                    // (same convention as the optional stage/stride sets).
                    string[] rels = BuildingStatePaths(TownBuildingSlugs[b]);
                    var states = new Sprite[rels.Length];
                    int found = 0;
                    for (int s = 0; s < rels.Length; s++)
                    {
                        states[s] = LoadSprite(GenPath(rels[s]));
                        if (states[s] != null)
                        {
                            found++;
                        }
                    }

                    lib.TownBuildings[b].States = states;
                    wired.Add($"Library.TownBuildings[{b}] {TownBuildingSlugs[b]}: {found}/{rels.Length} states" +
                              (found == rels.Length
                                  ? ""
                                  : " (missing states fall back to the generic placeholder)"));
                }

                // Construction-worker props (DinoDigger-771). Direct assignment (no
                // reflection); each stays null when its PNG is absent so the builder
                // hat/mallet + build-site sign features silently no-op.
                lib.HardHat = LoadSpriteTracked(HardHatRel, missing);
                lib.ToolHammer = LoadSpriteTracked(ToolHammerRel, missing);
                lib.ConstructionSign = LoadSpriteTracked(ConstructionSignRel, missing);
                wired.Add("Library: builder props hat/hammer/sign (DinoDigger-771)");

                // Machine Friends (DinoDigger-b48). Direct typed assignment, no reflection.
                // Loaded WITHOUT tracking: the importer pass above already reported anything
                // missing, and a machine with no art is an expected, handled state (the
                // runtime draws a tinted mound blob so the friend is still visible + tappable).
                lib.MachineDoodle = LoadSprite(GenPath(MachineRels[0]));
                lib.MachineSprinkles = LoadSprite(GenPath(MachineRels[1]));
                lib.MachineTuggy = LoadSprite(GenPath(MachineRels[2]));
                int machineCount = (lib.MachineDoodle != null ? 1 : 0) +
                                   (lib.MachineSprinkles != null ? 1 : 0) +
                                   (lib.MachineTuggy != null ? 1 : 0);
                wired.Add($"Library: machine friends {machineCount}/3 " +
                          $"(~{MachineTargetH}u tall, bottom pivot" +
                          (machineCount == 3 ? ")" : "; missing ones fall back to a tinted blob)"));

                // Jurassic-earth environment set (DinoDigger-y1g). Builds/refreshes the env
                // Tile assets, fills the typed EnvTileSet/EnvEdgeSet slots, and re-points
                // the SPRITE inside the existing tree/rock/mound/bridge Tile assets (never
                // the Tile references themselves — GameManager routes tree/rock taps by
                // comparing against lib.TreeTile / lib.RockTile). Every slot is null-tolerant:
                // whatever the env set is missing simply stays on the flat placeholder.
                WireEnvLibrary(lib, missing, wired);

                // Icons intentionally left on placeholders.
                EditorUtility.SetDirty(lib);
                wired.Add("Library: backhoe 8-dir, fruit x4, treasure x4, dirt x3, particles x3");
                wired.Add("Library: icons kept on placeholders");
            }

            // ------------------------------------------------ 4) AudioConfig
            string audioPath = $"{ConfigDir}/AudioConfig.asset";
            var audio = AssetDatabase.LoadAssetAtPath<AudioConfig>(audioPath);
            if (audio == null)
            {
                missing.Add(audioPath);
            }
            else
            {
                // Music (streaming); everything else short SFX (decompress on load).
                audio.Music = LoadClip(MusicPath, true, missing);
                audio.Tap = LoadClip(Iface("click_002"), false, missing);
                audio.Move = LoadClip(Iface("switch_004"), false, missing);
                audio.Dig = LoadClip(Iface("drop_002"), false, missing);
                audio.Crumble = LoadClip(Iface("scratch_003"), false, missing);
                audio.ItemPop = LoadClip(Iface("pluck_002"), false, missing);
                audio.Chime = LoadClip(Digital("threeTone1"), false, missing);
                audio.Hatch = LoadClip(Digital("powerUp1"), false, missing);
                audio.Roar = LoadClip(Digital("lowThreeTone"), false, missing);
                audio.Eat = LoadClip(Digital("pepSound2"), false, missing);
                audio.Grow = LoadClip(Digital("phaserUp1"), false, missing);
                audio.TreasureCollect = LoadClip(Digital("highUp"), false, missing);
                audio.Honk = LoadClip(Digital("twoTone1"), false, missing);
                audio.Heart = LoadClip(Iface("glass_001"), false, missing);

                // ---- dig audio pass (DinoDigger-7c4) ----
                // The dig loop's own vocabulary. Impact Sounds carries the physical events
                // (crack / thump / pop / knock), Digital Audio the expressive ones (fizz,
                // gurgle, giggle), Music Jingles the two pizzicato phrases. Per-clip loudness
                // trims are NOT here — they live beside the hooks in AudioManager, because the
                // files ship byte-identical to the CC0 packs. See Tools/ASSET_SOURCES.md.
                // NOTE: these come from the CURATED pack folders — if you swap a filename,
                // add it to the whitelist in Tools/download_assets.sh too, or a fresh asset
                // download will leave it missing here.
                audio.TileCrackA = LoadClip(Impact("impactMining_000"), false, missing);
                audio.TileCrackB = LoadClip(Impact("impactMining_001"), false, missing);
                audio.TileCrackC = LoadClip(Impact("impactMining_002"), false, missing);
                audio.Crumble = LoadClip(Iface("scratch_004"), false, missing);
                audio.LandingThump = LoadClip(Impact("impactSoft_heavy_000"), false, missing);
                audio.Whumph = LoadClip(Impact("impactSoft_heavy_001"), false, missing);
                audio.FuseSizzle = LoadClip(Digital("lowRandom"), false, missing);
                audio.CrystalPop = LoadClip(Impact("impactGlass_light_000"), false, missing);
                audio.CrystalPopBig = LoadClip(Impact("impactGlass_medium_000"), false, missing);
                audio.PotCrack = LoadClip(Impact("impactTin_medium_000"), false, missing);
                audio.CoinSpray = LoadClip(Jingle("jingles_PIZZI00"), false, missing);
                audio.BoneRattle = LoadClip(Impact("impactWood_light_000"), false, missing);
                audio.BonePop = LoadClip(Digital("powerUp7"), false, missing);
                audio.CeremonyPoof = LoadClip(Impact("impactSoft_medium_000"), false, missing);
                audio.MachineWake = LoadClip(Impact("impactBell_heavy_002"), false, missing);
                audio.Gurgle = LoadClip(Digital("lowDown"), false, missing);
                audio.Toot = LoadClip(Digital("twoTone2"), false, missing);
                audio.Giggle = LoadClip(Digital("pepSound3"), false, missing);
                audio.WaterGush = LoadClip(Iface("scroll_003"), false, missing);
                audio.DanceLoop = LoadClip(Jingle("jingles_PIZZI03"), true, missing);
                audio.LadderDing = LoadClip(Digital("threeTone2"), false, missing);
                audio.SparkZap = LoadClip(Digital("zap1"), false, missing);
                audio.Boing = LoadClip(Digital("phaseJump1"), false, missing);

                EditorUtility.SetDirty(audio);
                wired.Add("AudioConfig: Music=Bluebonnet_looped, Tap=click_002, Move=switch_004, " +
                          "Dig=drop_002, Crumble=scratch_004, ItemPop=pluck_002, Chime=threeTone1, " +
                          "Hatch=powerUp1, Roar=lowThreeTone, Eat=pepSound2, Grow=phaserUp1, " +
                          "TreasureCollect=highUp, Honk=twoTone1, Heart=glass_001");
                wired.Add("AudioConfig dig pass: TileCrackA/B/C=impactMining_000/001/002, " +
                          "LandingThump=impactSoft_heavy_000, Whumph=impactSoft_heavy_001, " +
                          "FuseSizzle=lowRandom, CrystalPop=impactGlass_light_000, " +
                          "CrystalPopBig=impactGlass_medium_000, PotCrack=impactTin_medium_000, " +
                          "CoinSpray=jingles_PIZZI00, BoneRattle=impactWood_light_000, " +
                          "BonePop=powerUp7, CeremonyPoof=impactSoft_medium_000, " +
                          "MachineWake=impactBell_heavy_002, Gurgle=lowDown, Toot=twoTone2, " +
                          "Giggle=pepSound3, WaterGush=scroll_003, DanceLoop=jingles_PIZZI03, " +
                          "LadderDing=threeTone2, SparkZap=zap1, Boing=phaseJump1");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // ------------------------------------------------ 5) summary
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[GeneratedArtImporter] Import complete. Wired {wired.Count} groups:");
            foreach (string w in wired)
            {
                sb.AppendLine("  + " + w);
            }

            if (missing.Count > 0)
            {
                sb.AppendLine($"[GeneratedArtImporter] {missing.Count} item(s) NOT FOUND / skipped:");
                foreach (string m in missing)
                {
                    sb.AppendLine("  ! " + m);
                }

                Debug.LogWarning(sb.ToString());
            }
            else
            {
                sb.AppendLine("[GeneratedArtImporter] All assets found and wired.");
                Debug.Log(sb.ToString());
            }
        }

        // ------------------------------------------------------------- helpers

        /// <summary>Import a dig-grid sprite so its LARGER dimension is exactly one cell
        /// (<see cref="DirtTargetH"/> world units): PPU = max(sourceW, sourceH) / target. That is
        /// what keeps a tall crystal and a wide geode inside the same footprint a dirt tile
        /// occupies, instead of one of them spilling over its neighbours.</summary>
        // =========================================== ENV import (DinoDigger-y1g)

        private static string EnvGroundPath(string name) => $"{EnvDir}/ground/{name}.png";
        private static string EnvDecalPath(string name) => $"{EnvDir}/decal/{name}.png";
        private static string EnvPropPath(string name) => $"{EnvDir}/prop/{name}.png";
        private static string EnvDecorPath(string name) => $"{EnvDir}/decor/{name}.png";

        private static string EnvTileName(string biome, int variant) => $"tile_{biome}_{variant:00}";
        private static string EnvEdgeName(string other, int mask) => $"edge_grass_{other}_{mask}";

        /// <summary>
        /// Configure every env texture that exists on disk. Nothing here is required: a
        /// checkout without the art (or with only part of it) reports ONE summary line and
        /// leaves the island on the flat placeholder tiles.
        /// </summary>
        private static void ImportEnvTextures(List<string> missing, List<string> wired)
        {
            // Probe one file rather than tracking 117 individual misses when the whole set
            // is simply not in this checkout.
            if (SourceWidth(EnvGroundPath(EnvTileName("grass", 0))) <= 0)
            {
                wired.Add($"ENV: no environment set under {EnvDir} — the island keeps the " +
                          "flat placeholder tiles (no regression, nothing to import)");
                return;
            }

            int ground = 0;
            ground += ConfigureEnvBiome("grass", EnvTileVariants);
            ground += ConfigureEnvBiome("path", EnvTileVariants);
            ground += ConfigureEnvBiome("water", EnvTileVariants);
            ground += ConfigureEnvBiome("bed", EnvBedVariants);

            int edges = 0;
            edges += ConfigureEnvEdges("path");
            edges += ConfigureEnvEdges("water");
            edges += ConfigureEnvEdges("bed");

            int blobs = 0;
            blobs += ConfigureEnvBlobs("water");
            blobs += ConfigureEnvBlobs("path");
            blobs += ConfigureEnvBlobs("bed");

            int decals = ConfigureEnvDecals(EnvGrassDecalArt) +
                         ConfigureEnvDecals(EnvPathDecalArt) +
                         ConfigureEnvDecals(EnvWaterDecalArt) +
                         ConfigureEnvDecals(EnvAccentDecalArt);

            int props = 0;
            foreach (string t in EnvTreeArt)
            {
                props += ConfigureEnv(EnvPropPath(t), EnvPropWorldW) ? 1 : 0;
            }

            props += ConfigureEnv(EnvPropPath(EnvTreeShakeArt), EnvPropWorldW) ? 1 : 0;
            foreach (string r in EnvRockArt)
            {
                props += ConfigureEnv(EnvPropPath(r), EnvPropWorldW) ? 1 : 0;
            }

            props += ConfigureEnv(EnvPropPath(EnvMoundArt), EnvCellWorldW) ? 1 : 0;

            int decor = 0;
            foreach (string b in EnvBridgeArt)
            {
                decor += ConfigureEnv(EnvDecorPath(b), EnvCellWorldW) ? 1 : 0;
            }

            decor += ConfigureEnv(EnvDecorPath(EnvFenceXArt), EnvFenceWorldW) ? 1 : 0;
            decor += ConfigureEnv(EnvDecorPath(EnvFenceYArt), EnvFenceWorldW) ? 1 : 0;
            decor += ConfigureEnv(EnvDecorPath(EnvNestArt), EnvNestWorldW) ? 1 : 0;

            wired.Add($"ENV textures: {ground} ground variants + {edges} transitions + " +
                      $"{blobs} connected pieces + {decals} decals + {props} props + " +
                      $"{decor} decor (PPU = sourceWidth / target world width, CENTER " +
                      "pivots — every footprint identical to the placeholder it replaces)");

            int expected = EnvTileVariants * 3 + EnvBedVariants + (EnvEdgeMasks - 1) * 3 +
                           EnvDressing.BlobPieceCount * 3 +
                           EnvGrassDecalArt.Length + EnvPathDecalArt.Length +
                           EnvWaterDecalArt.Length + EnvAccentDecalArt.Length +
                           EnvTreeArt.Length + 1 + EnvRockArt.Length + 1 +
                           EnvBridgeArt.Length + 3;
            int got = ground + edges + blobs + decals + props + decor;
            if (got != expected)
            {
                missing.Add($"{EnvDir}: imported {got}/{expected} env sprites — the gaps stay " +
                            "on their flat placeholders (re-run Tools/generate_env.py bake)");
            }
        }

        private static int ConfigureEnvBiome(string biome, int variants)
        {
            int n = 0;
            for (int i = 0; i < variants; i++)
            {
                n += ConfigureEnv(EnvGroundPath(EnvTileName(biome, i)), EnvCellWorldW) ? 1 : 0;
            }

            return n;
        }

        // Connected (topology-keyed) pieces, DinoDigger-l9g. File name carries the
        // CANONICAL KEY, not a slot index, so the art and the engine agree even if the
        // canonical ordering is ever regenerated: <biome>_b<key:000>.png.
        private static string EnvBlobName(string biome, int key) => $"{biome}_b{key:000}";

        private static int ConfigureEnvBlobs(string biome)
        {
            int n = 0;
            for (int slot = 0; slot < EnvDressing.BlobPieceCount; slot++)
            {
                string name = EnvBlobName(biome, EnvDressing.BlobKeyAt(slot));
                n += ConfigureEnv(EnvGroundPath(name), EnvCellWorldW) ? 1 : 0;
            }

            return n;
        }

        /// <summary>Fill one biome's connected set, in canonical slot order.</summary>
        private static int WireEnvBlobs(EnvBlobSet set, string biome)
        {
            var tiles = new TileBase[EnvDressing.BlobPieceCount];
            int found = 0;
            for (int slot = 0; slot < tiles.Length; slot++)
            {
                string name = EnvBlobName(biome, EnvDressing.BlobKeyAt(slot));
                tiles[slot] = EnsureEnvTile(name, EnvGroundPath(name));
                if (tiles[slot] != null)
                {
                    found++;
                }
            }

            set.Pieces = tiles;
            return found;
        }

        private static int ConfigureEnvEdges(string other)
        {
            int n = 0;
            for (int mask = 1; mask < EnvEdgeMasks; mask++)
            {
                n += ConfigureEnv(EnvGroundPath(EnvEdgeName(other, mask)), EnvCellWorldW) ? 1 : 0;
            }

            return n;
        }

        private static int ConfigureEnvDecals(EnvDecalArt[] set)
        {
            int n = 0;
            foreach (EnvDecalArt d in set)
            {
                n += ConfigureEnv(EnvDecalPath(d.File), d.WorldW) ? 1 : 0;
            }

            return n;
        }

        /// <summary>
        /// Import one env sprite at PPU = sourceWidth / <paramref name="worldWidth"/> with a
        /// CENTER pivot — the two things that make an env asset a drop-in for the placeholder
        /// it replaces. Returns false (and configures nothing) when the PNG is absent, which
        /// is always a legal state: the caller's slot then stays on its flat fallback.
        ///
        /// Forces spriteMode = Single through BOTH the property and the settings block for
        /// the same reason ConfigureBuilding does: freshly dropped PNGs can arrive carrying a
        /// stale auto-slice rect from a previous canvas, and a Single-mode sprite never reads
        /// those rects.
        /// </summary>
        private static bool ConfigureEnv(string assetPath, float worldWidth)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null || worldWidth <= 0.0001f)
            {
                return false;
            }

            importer.GetSourceTextureWidthAndHeight(out int w, out int _);
            if (w <= 0)
            {
                return false;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = w / worldWidth;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            // CLAMP, not Repeat: the ground tiles carry a 3px dilated alpha skirt so
            // neighbours overlap opaquely (see generate_env.py's _diamond_alpha). Wrapping
            // would sample the opposite edge into that skirt and re-draw the dark lattice
            // the whole art pass exists to delete.
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.maxTextureSize = 1024;
            importer.spriteBorder = Vector4.zero;

            var s = new TextureImporterSettings();
            importer.ReadTextureSettings(s);
            s.spriteAlignment = (int)SpriteAlignment.Center;
            s.spriteMode = (int)SpriteImportMode.Single;
            // FULL RECT, not Tight (DinoDigger-ajm). A Tight sprite mesh clips the drawn
            // geometry to the alpha silhouette, which is exactly the 3px dilated skirt the
            // ground tiles rely on to overlap their neighbours: whatever the mesh
            // generator trims off that skirt becomes a gap the camera's clear colour
            // shines through, one hairline per diamond edge, worst at the four vertices
            // where the skirt is already clipped by the canvas. FullRect draws the whole
            // 256x128 quad, so the overlap is guaranteed to be rasterised and the only
            // thing deciding coverage is the alpha the baker authored. It also costs the
            // env set nothing to sort/batch — these are one-sprite Single tiles.
            s.spriteMeshType = SpriteMeshType.FullRect;
            s.spriteExtrude = 0;   // meaningless for FullRect; keep it off the meta churn
            importer.SetTextureSettings(s);

            importer.SaveAndReimport();
            return true;
        }

        /// <summary>
        /// Create (or refresh) the <see cref="Tile"/> asset that lets an env sprite be
        /// painted on a tilemap, and return it. colliderType is None on every one of them —
        /// the ground/decal layers must never introduce physics, and walkability stays
        /// exactly what OverworldMap already computes from tile PRESENCE. Returns null when
        /// the sprite is absent, so the caller leaves that slot empty.
        /// </summary>
        private static TileBase EnsureEnvTile(string tileName, string spriteAssetPath)
        {
            Sprite sprite = LoadSprite(spriteAssetPath);
            if (sprite == null)
            {
                return null;
            }

            EnsureFolder(EnvTilesDir);
            string path = $"{EnvTilesDir}/{tileName}.asset";
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, path);
            }

            if (tile.sprite != sprite || tile.colliderType != Tile.ColliderType.None)
            {
                tile.sprite = sprite;
                tile.colliderType = Tile.ColliderType.None;
                EditorUtility.SetDirty(tile);
            }

            return tile;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        /// <summary>Fill one biome's variant set from tile_&lt;biome&gt;_NN.png.</summary>
        private static int WireEnvBiome(EnvTileSet set, string biome, int variants)
        {
            var tiles = new TileBase[variants];
            int found = 0;
            for (int i = 0; i < variants; i++)
            {
                string name = EnvTileName(biome, i);
                tiles[i] = EnsureEnvTile(name, EnvGroundPath(name));
                if (tiles[i] != null)
                {
                    found++;
                }
            }

            set.Variants = tiles;
            return found;
        }

        /// <summary>Fill one grass-to-X transition family, indexed BY MASK (slot 0 unused).</summary>
        private static int WireEnvEdges(EnvEdgeSet set, string other)
        {
            var tiles = new TileBase[EnvEdgeSet.MaskCount];
            int found = 0;
            for (int mask = 1; mask < EnvEdgeSet.MaskCount; mask++)
            {
                string name = EnvEdgeName(other, mask);
                tiles[mask] = EnsureEnvTile(name, EnvGroundPath(name));
                if (tiles[mask] != null)
                {
                    found++;
                }
            }

            set.ByMask = tiles;
            return found;
        }

        private static int WireEnvDecals(EnvTileSet set, EnvDecalArt[] art)
        {
            var tiles = new TileBase[art.Length];
            int found = 0;
            for (int i = 0; i < art.Length; i++)
            {
                tiles[i] = EnsureEnvTile(art[i].File, EnvDecalPath(art[i].File));
                if (tiles[i] != null)
                {
                    found++;
                }
            }

            set.Variants = tiles;
            return found;
        }

        private static Sprite[] LoadEnvProps(string[] names, System.Func<string, string> path)
        {
            var arr = new Sprite[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                arr[i] = LoadSprite(path(names[i]));
            }

            return arr;
        }

        /// <summary>The first non-null sprite in a variant list, or null.</summary>
        private static Sprite FirstOf(Sprite[] arr)
        {
            if (arr == null)
            {
                return null;
            }

            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i] != null)
                {
                    return arr[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Re-point an EXISTING Tile asset's sprite (tree/rock/mound/bridge). This is the
        /// whole prop swap: the Tile REFERENCE is untouched, so GameManager's
        /// <c>ObstacleAt(cell) == lib.TreeTile</c> tap routing, the integration cases that
        /// find trees/rocks the same way, and every already-painted scene keep working —
        /// only the pixels change. Null sprite (art absent) leaves the placeholder alone.
        ///
        /// FOOTGUN, same as every other override in this file: re-running
        /// PlaceholderArtGenerator rewrites these Tile assets back to the procedural
        /// placeholders. The order has always been Generate -> Import -> Build Main Scene,
        /// and SceneBuilder only auto-Generates when the config assets are missing outright.
        /// </summary>
        private static bool RestyleTile(TileBase tileBase, Sprite sprite)
        {
            var tile = tileBase as Tile;
            if (tile == null || sprite == null)
            {
                return false;
            }

            if (tile.sprite != sprite)
            {
                tile.sprite = sprite;
                EditorUtility.SetDirty(tile);
            }

            return true;
        }

        /// <summary>
        /// Fill every ENV slot on the library. Entirely null-tolerant, slot by slot: an
        /// absent variant, transition, decal or prop leaves its slot empty and SceneBuilder
        /// falls back to exactly what it painted before the env set existed.
        /// </summary>
        private static void WireEnvLibrary(PlaceholderLibrary lib, List<string> missing,
            List<string> wired)
        {
            if (lib.GrassTiles == null) { lib.GrassTiles = new EnvTileSet(); }
            if (lib.PathTiles == null) { lib.PathTiles = new EnvTileSet(); }
            if (lib.WaterTiles == null) { lib.WaterTiles = new EnvTileSet(); }
            if (lib.BedTiles == null) { lib.BedTiles = new EnvTileSet(); }
            if (lib.GrassPathEdges == null) { lib.GrassPathEdges = new EnvEdgeSet(); }
            if (lib.GrassWaterEdges == null) { lib.GrassWaterEdges = new EnvEdgeSet(); }
            if (lib.GrassBedEdges == null) { lib.GrassBedEdges = new EnvEdgeSet(); }
            if (lib.GrassDecals == null) { lib.GrassDecals = new EnvTileSet(); }
            if (lib.PathDecals == null) { lib.PathDecals = new EnvTileSet(); }
            if (lib.WaterDecals == null) { lib.WaterDecals = new EnvTileSet(); }
            if (lib.AccentDecals == null) { lib.AccentDecals = new EnvTileSet(); }

            int g = WireEnvBiome(lib.GrassTiles, "grass", EnvTileVariants);
            int p = WireEnvBiome(lib.PathTiles, "path", EnvTileVariants);
            int w = WireEnvBiome(lib.WaterTiles, "water", EnvTileVariants);
            int b = WireEnvBiome(lib.BedTiles, "bed", EnvBedVariants);
            wired.Add($"Library ENV ground: grass {g}/{EnvTileVariants}, path {p}/{EnvTileVariants}, " +
                      $"water {w}/{EnvTileVariants}, bed {b}/{EnvBedVariants} variants " +
                      "(empty sets fall back to the flat tiles)");

            // Connected sets first — when they are COMPLETE the painter uses them and the
            // grass-side transitions below become the documented fallback (a connected
            // piece already carries its own bank; melting the biome into the grass next
            // door as well would contradict it at the seam).
            if (lib.WaterBlobs == null) { lib.WaterBlobs = new EnvBlobSet(); }
            if (lib.PathBlobs == null) { lib.PathBlobs = new EnvBlobSet(); }
            if (lib.BedBlobs == null) { lib.BedBlobs = new EnvBlobSet(); }
            int bw = WireEnvBlobs(lib.WaterBlobs, "water");
            int bp = WireEnvBlobs(lib.PathBlobs, "path");
            int bb = WireEnvBlobs(lib.BedBlobs, "bed");
            int all = EnvDressing.BlobPieceCount;
            wired.Add($"Library ENV connected: water {bw}/{all}, path {bp}/{all}, " +
                      $"bed {bb}/{all} pieces — " +
                      (lib.UsesBlobs(EnvBiome.Water) && lib.UsesBlobs(EnvBiome.Path) &&
                       lib.UsesBlobs(EnvBiome.Bed)
                          ? "topology-keyed painting ACTIVE (DinoDigger-l9g)"
                          : "INCOMPLETE, painter falls back to flat variants + edges"));

            int ep = WireEnvEdges(lib.GrassPathEdges, "path");
            int ew = WireEnvEdges(lib.GrassWaterEdges, "water");
            int eb = WireEnvEdges(lib.GrassBedEdges, "bed");
            wired.Add($"Library ENV transitions: grass->path {ep}/15, grass->water {ew}/15, " +
                      "grass->bed " + eb + "/15 (indexed by neighbour mask 1..15)");

            int dg = WireEnvDecals(lib.GrassDecals, EnvGrassDecalArt);
            int dp = WireEnvDecals(lib.PathDecals, EnvPathDecalArt);
            int dw = WireEnvDecals(lib.WaterDecals, EnvWaterDecalArt);
            int da = WireEnvDecals(lib.AccentDecals, EnvAccentDecalArt);
            wired.Add($"Library ENV decals: grass {dg}, path {dp}, water {dw}, accent {da} " +
                      "(rule-4 buckets — SceneBuilder never scatters one outside its biome)");

            // Outlined tappable props. Every one of these keeps the world footprint of the
            // sprite it replaces, so no collider is touched (verified in the mapping table
            // at the top of this file).
            lib.TreeSprites = LoadEnvProps(EnvTreeArt, EnvPropPath);
            lib.TreeShakeSprite = LoadSprite(EnvPropPath(EnvTreeShakeArt));
            lib.RockSprites = LoadEnvProps(EnvRockArt, EnvPropPath);
            lib.BridgeSprites = LoadEnvProps(EnvBridgeArt, EnvDecorPath);
            if (lib.BridgeTiles == null) { lib.BridgeTiles = new EnvTileSet(); }
            var bridgeTiles = new TileBase[EnvBridgeArt.Length];
            for (int i = 0; i < EnvBridgeArt.Length; i++)
            {
                bridgeTiles[i] = EnsureEnvTile(EnvBridgeArt[i], EnvDecorPath(EnvBridgeArt[i]));
            }

            lib.BridgeTiles.Variants = bridgeTiles;
            lib.FenceAlongX = LoadSprite(EnvDecorPath(EnvFenceXArt));
            lib.FenceAlongY = LoadSprite(EnvDecorPath(EnvFenceYArt));

            Sprite moundSprite = LoadSprite(EnvPropPath(EnvMoundArt));
            Sprite nestSprite = LoadSprite(EnvDecorPath(EnvNestArt));

            // The RESTYLE: same Tile assets, new pixels. Identity is the tap contract.
            bool tree = RestyleTile(lib.TreeTile, FirstOf(lib.TreeSprites));
            bool rock = RestyleTile(lib.RockTile, FirstOf(lib.RockSprites));
            bool bridge = RestyleTile(lib.BridgeTile, FirstOf(lib.BridgeSprites));
            bool moundTile = RestyleTile(lib.MoundTile, moundSprite);

            // Overworld mound prop + nest bowl are plain SpriteRenderer sprites: swap the
            // library slot and every consumer (DigMound, BerrySprout's tinted base, the
            // machine-friend blob fallback, NestController) picks the new art up unchanged.
            if (moundSprite != null)
            {
                lib.MoundSprite = moundSprite;
            }

            if (nestSprite != null)
            {
                lib.NestSprite = nestSprite;
            }

            int propCount = (tree ? 1 : 0) + (rock ? 1 : 0) + (bridge ? 1 : 0) +
                            (moundTile ? 1 : 0) + (moundSprite != null ? 1 : 0) +
                            (nestSprite != null ? 1 : 0);
            wired.Add($"Library ENV props: {propCount}/6 restyled in place " +
                      $"(tree={tree}, rock={rock}, bridge={bridge}, moundTile={moundTile}, " +
                      $"moundSprite={moundSprite != null}, nest={nestSprite != null}) — " +
                      "Tile REFERENCES unchanged, so tree/rock tap routing is untouched");
            wired.Add($"Library ENV decor: fence {(lib.FenceAlongX != null ? "X" : "-")}" +
                      $"{(lib.FenceAlongY != null ? "Y" : "-")} " +
                      "(canvas-compatible Kenney drop-ins), " +
                      $"tree variants {lib.TreeSprites.Length}, shake pose " +
                      (lib.TreeShakeSprite != null ? "present" : "absent") +
                      $", rock variants {lib.RockSprites.Length}, bridge decks {lib.BridgeSprites.Length}");

            if (!lib.HasEnvGround)
            {
                missing.Add($"{EnvDir}/ground (no env ground tiles wired — island stays flat)");
            }
        }

        private static void ConfigureCellFit(string rel, List<string> missing)
        {
            string p = GenPath(rel);
            int w = SourceWidth(p);
            int h = SourceHeight(p);
            int longest = Mathf.Max(w, h);
            if (longest <= 0)
            {
                missing.Add(p + " (no readable source texture)");
                return;
            }

            ConfigureSprite(p, longest / DirtTargetH, missing);
        }

        private static void ConfigureEach(string[] relPaths, float targetHeight, List<string> missing)
        {
            foreach (string rel in relPaths)
            {
                string p = GenPath(rel);
                int h = SourceHeight(p);
                if (h <= 0)
                {
                    missing.Add(p + " (no readable source texture)");
                    continue;
                }

                ConfigureSprite(p, h / targetHeight, missing);
            }
        }

        private static void ConfigureSprite(string assetPath, float ppu, List<string> missing)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                missing.Add(assetPath);
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = ppu;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            // "Automatic" compression = the platform-chosen compressed format.
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
        }

        // Import an arm piece at an explicit PPU with a custom pivot (the joint
        // pin / hinge boss the piece rotates about). Plain Simple-sprite import:
        // no 9-slice borders, no draw-mode tricks — the rig renders these 1:1.
        private static void ConfigureArmPiece(string rel, float ppu, Vector2 pivot,
            List<string> missing)
        {
            string path = GenPath(rel);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                missing.Add(path);
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = ppu;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.maxTextureSize = 1024;
            importer.spriteBorder = Vector4.zero;

            var s = new TextureImporterSettings();
            importer.ReadTextureSettings(s);
            s.spriteAlignment = (int)SpriteAlignment.Custom;
            s.spritePivot = pivot;
            importer.SetTextureSettings(s);

            importer.SaveAndReimport();
        }

        // Import a town building state: plain Simple sprite at an explicit PPU, with a
        // BOTTOM-CENTER pivot so every construction state sits on the same ground line
        // and the taller finished silhouette grows upward from that base.
        //
        // ROBUSTNESS (DinoDigger-ggy): several freshly-dropped town PNGs arrive with
        // spriteMode = Multiple plus a STALE auto-slice rect left over from the pre-crop
        // canvas they were sliced out of — e.g. boulder_brew_s1 is 726x569 but its .meta
        // still carries a 969x564 rect at (7,7), and every boneanza_bowling state carries a
        // full 1024-wide rect. Unity validates those rects against the real texture and logs
        // "rect lies (partially) outside of texture". Forcing SINGLE mode (below) both fixes
        // the import mode we actually want and retires the stale sheet rects, so the warning
        // clears WITHOUT re-slicing the art (Tools/ is owned elsewhere) and without touching
        // the PNGs. The stale rect data stays inert in the .meta until the next re-slice
        // rewrites it — harmless, because a Single-mode sprite never reads it.
        private static void ConfigureBuilding(string assetPath, float ppu, List<string> missing)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                missing.Add(assetPath);
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = ppu;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.maxTextureSize = 1024;
            importer.spriteBorder = Vector4.zero;

            var s = new TextureImporterSettings();
            importer.ReadTextureSettings(s);
            s.spriteAlignment = (int)SpriteAlignment.BottomCenter;
            // Belt-and-braces against the stale-slice metas described above: write the mode
            // through the settings block too, so the sheet rects can never be re-read even if
            // a preset flipped the file to Multiple after the property assignment.
            s.spriteMode = (int)SpriteImportMode.Single;
            importer.SetTextureSettings(s);

            importer.SaveAndReimport();
        }

        private static int SourceHeight(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return 0;
            }

            importer.GetSourceTextureWidthAndHeight(out int _, out int height);
            return height;
        }

        private static int SourceWidth(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return 0;
            }

            importer.GetSourceTextureWidthAndHeight(out int width, out int _);
            return width;
        }

        private static Sprite[] LoadArray(string[] relPaths, List<string> missing)
        {
            var arr = new Sprite[relPaths.Length];
            for (int i = 0; i < relPaths.Length; i++)
            {
                arr[i] = LoadSpriteTracked(relPaths[i], missing);
            }

            return arr;
        }

        private static Sprite LoadSpriteTracked(string rel, List<string> missing)
        {
            Sprite s = LoadSprite(GenPath(rel));
            if (s == null)
            {
                missing.Add(GenPath(rel));
            }

            return s;
        }

        private static AudioClip LoadClip(string assetPath, bool streaming, List<string> missing)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;
            if (importer == null)
            {
                missing.Add(assetPath);
                return null;
            }

            AudioImporterSampleSettings s = importer.defaultSampleSettings;
            s.loadType = streaming ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            importer.defaultSampleSettings = s;
            importer.SaveAndReimport();

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
            if (clip == null)
            {
                missing.Add(assetPath);
            }

            return clip;
        }

        // Load a growth-stage 8-dir set. Returns null (not a partial array) when the
        // stage art isn't present at all, so DinoDefinition falls back to the adult
        // set cleanly; a partially-present set keeps the found sprites and reports the
        // gaps. Missing files are NOT tracked as errors — stage art is optional.
        private static Sprite[] LoadStageSet(string folder, string stage, out int found)
        {
            var set = new Sprite[8];
            found = 0;
            for (int i = 0; i < 8; i++)
            {
                set[i] = LoadSprite(StagePath(folder, stage, i));
                if (set[i] != null)
                {
                    found++;
                }
            }

            return found > 0 ? set : null;
        }

        // Load a walk-stride 8-dir set (stage == null for the adult). Returns null
        // when the art isn't present at all so DinoDefinition.StrideSprites reports
        // "no walk animation" and DinoController keeps the static behavior. Missing
        // files are NOT tracked as errors — stride art is optional (pilot: trex only).
        private static Sprite[] LoadStrideSet(string folder, string stage, string pose, out int found)
        {
            var set = new Sprite[8];
            found = 0;
            for (int i = 0; i < 8; i++)
            {
                set[i] = LoadSprite(StridePath(folder, stage, pose, i));
                if (set[i] != null)
                {
                    found++;
                }
            }

            return found > 0 ? set : null;
        }

        // ---- DinoDigger-bw4: generated-art diagonal-facing correction ----------
        // ROOT CAUSE (art, not code): Tools/generate_sprites.py rotates the front (S)
        // reference into every other facing with an AMBIGUOUS, character-relative
        // instruction ("...so we see its front AND its RIGHT side"). The image model
        // interprets "its right side" inconsistently — frequently as the character's
        // ANATOMICAL right, which lands on SCREEN-LEFT — so a number of generated
        // facings came out horizontally MIRRORED relative to the compass name in their
        // filename. Because each facing (and each growth stage) is an INDEPENDENT
        // img2img call, the error is per-(actor,facing), NOT a single uniform swap:
        // e.g. trex_SE faces down-right (correct) while stegosaurus_SE faces down-left,
        // and triceratops_NE is correct while backhoe_NE is mirrored.
        //
        // Direction8's sector math is correct for diagonals ((1,-1)->SE, (-1,1)->NW…),
        // and the slicer's E->W / SE->SW / NE->NW step is geometrically correct — the
        // left-side PNGs are EXACT pixel-mirrors of the right-side ones (verified). So
        // the correctly-oriented sprite for any mis-generated facing ALREADY EXISTS as
        // its mirror partner, and we can fix a flipped facing with NO regeneration by
        // loading its partner file into the slot (a per-actor pair-swap of the compass
        // horizontal component). The integration FacingCorrectness test cannot detect
        // this class of bug — it only checks Dir8-index<->array-slot consistency, which
        // was always correct — which is why diagonals slipped through.
        //
        // The table below lists only HIGH-CONFIDENCE flips found by visual audit of the
        // ADULT idle art (unambiguous landmarks: the backhoe's loader/cab-face, dino
        // snouts/beaks/frills). Listing only certain cases means the correction can
        // never REGRESS an already-correct actor; any un-audited/ambiguous facing keeps
        // its raw filename. Adult strides + backhoe rolls share each facing's handedness
        // (they are img2img-edited FROM that facing) and are corrected via the same
        // suffix. Baby/kid stage sets are SEPARATE generations, not yet audited, and are
        // left on their raw names. The permanent fix is to REGENERATE with the corrected
        // screen-relative prompt now in generate_sprites.py, after which this table
        // should be emptied. Keyed by the right-side member (E/SE/NE) of each flipped pair.
        private static readonly Dictionary<string, HashSet<Dir8>> FlippedFacingPairs =
            new Dictionary<string, HashSet<Dir8>>
            {
                { "backhoe",      new HashSet<Dir8> { Dir8.SE, Dir8.NE } },
                { "triceratops",  new HashSet<Dir8> { Dir8.SE } },
                { "stegosaurus",  new HashSet<Dir8> { Dir8.SE } },
                { "ankylosaurus", new HashSet<Dir8> { Dir8.E, Dir8.SE, Dir8.NE } },
            };

        // Horizontal mirror of a Dir8 (flip the E/W component; N/S unchanged).
        private static Dir8 MirrorDir(Dir8 d) => d switch
        {
            Dir8.E => Dir8.W, Dir8.W => Dir8.E,
            Dir8.NE => Dir8.NW, Dir8.NW => Dir8.NE,
            Dir8.SE => Dir8.SW, Dir8.SW => Dir8.SE,
            _ => d,
        };

        // The right-side representative (E/SE/NE) identifying a facing's mirror pair.
        private static Dir8 PairKey(Dir8 d) => d switch
        {
            Dir8.W => Dir8.E, Dir8.SW => Dir8.SE, Dir8.NW => Dir8.NE,
            _ => d,
        };

        // Adult-set filename suffix with the bw4 facing correction applied: when this
        // actor's pair is flagged flipped, resolve to the mirror partner's file so the
        // slot renders the correct on-screen facing.
        private static string AdultSuffix(string folder, int dir8)
        {
            var d = (Dir8)dir8;
            if (FlippedFacingPairs.TryGetValue(folder, out HashSet<Dir8> pairs) && pairs.Contains(PairKey(d)))
            {
                d = MirrorDir(d);
            }

            return Dir8Suffix[(int)d];
        }

        // The five generated-art relative paths for one town building slug, in construction
        // order (s0..s3 then done) — the same order BuildingController indexes its states in.
        private static string[] BuildingStatePaths(string slug)
        {
            var rels = new string[BuildingStateSuffix.Length];
            for (int i = 0; i < rels.Length; i++)
            {
                rels[i] = $"town/{slug}_{BuildingStateSuffix[i]}";
            }

            return rels;
        }

        private static Sprite LoadSprite(string assetPath) => AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        private static string GenPath(string rel) => $"{GenRoot}/{rel}.png";
        private static string CharPath(string folder, int dir8) => $"{GenRoot}/{folder}/{folder}_{AdultSuffix(folder, dir8)}.png";
        private static string StagePath(string folder, string stage, int dir8) => $"{GenRoot}/{folder}/{stage}_{Dir8Suffix[dir8]}.png";
        private static string StridePath(string folder, string stage, string pose, int dir8) =>
            stage == null
                ? $"{GenRoot}/{folder}/{pose}_{AdultSuffix(folder, dir8)}.png"
                : $"{GenRoot}/{folder}/{stage}_{pose}_{Dir8Suffix[dir8]}.png";
        private static string Digital(string name) => $"{DigitalAudioDir}/{name}.ogg";
        private static string Iface(string name) => $"{InterfaceDir}/{name}.ogg";
        private static string Impact(string name) => $"{ImpactDir}/{name}.ogg";
        private static string Jingle(string name) => $"{JinglesDir}/{name}.ogg";
    }
}
