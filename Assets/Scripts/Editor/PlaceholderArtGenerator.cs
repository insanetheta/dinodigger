using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using DinoDigger.Config;
using DinoDigger.Core;

namespace DinoDigger.EditorTools
{
    /// <summary>
    /// Procedurally generates all placeholder art (PNGs + Tile assets) plus the
    /// config ScriptableObjects (dino definitions, GameConfig, AudioConfig,
    /// PlaceholderLibrary) so the game is fully playable before real art lands.
    /// Menu: DinoDigger/Generate Placeholder Art. Safe to re-run.
    /// </summary>
    public static class PlaceholderArtGenerator
    {
        private const string Root = "Assets/Art/Placeholder";
        private const string SpritesDir = Root + "/Sprites";
        private const string TilesDir = Root + "/Tiles";
        private const string ConfigDir = Root + "/Config";

        private const int TileW = 128;
        private const int TileH = 64;
        private const float PpuTile = 128f;
        private const int SpriteSize = 128;
        private const float PpuSprite = 100f;
        private const int ParticleSize = 32;

        [MenuItem("DinoDigger/Generate Placeholder Art")]
        public static void Generate()
        {
            EnsureFolders();

            // 1) Write all PNGs.
            var spritePaths = new List<string>();  // paths needing sprite import at PpuSprite
            var tilePaths = new List<string>();     // paths needing sprite import at PpuTile

            // --- Isometric ground/water/path/mound diamonds (tile PPU) ---
            WriteDiamond("tile_grass", new Color32(96, 190, 84, 255), new Color32(64, 140, 56, 255), tilePaths);
            WriteDiamond("tile_path", new Color32(196, 158, 104, 255), new Color32(150, 116, 70, 255), tilePaths);
            WriteDiamond("tile_water", new Color32(80, 150, 230, 255), new Color32(50, 110, 190, 255), tilePaths);
            WriteBridge("tile_bridge", tilePaths);
            WriteMound("tile_mound", tilePaths);
            WriteObstacle("tile_tree", new Color32(60, 150, 70, 255), new Color32(120, 80, 40, 255), true, tilePaths);
            WriteObstacle("tile_rock", new Color32(150, 150, 160, 255), new Color32(90, 90, 100, 255), false, tilePaths);

            // --- Backhoe 8 directions + body + scoop (sprite PPU) ---
            for (int i = 0; i < 8; i++)
            {
                WriteBackhoe($"backhoe_{i}", (Dir8)i, spritePaths);
            }

            WriteBackhoeBody("backhoe_body", spritePaths);
            WriteScoop("backhoe_scoop", spritePaths);

            // --- Dinos (blob per type) + eggs ---
            // Index 0-3: original egg-hatchable species. Index 4-8: shard-exclusive
            // species (silhouette placeholders share the blob; colors stay distinct
            // so code/tests read correctly until the real turnarounds land in bl6.2).
            Color[] dinoColors = DinoColors;

            for (int i = 0; i < dinoColors.Length; i++)
            {
                WriteDino($"dino_{i}", dinoColors[i], spritePaths);
                WriteEgg($"egg_{i}", dinoColors[i], spritePaths);
            }

            // --- Egg shard (sparkly shell piece) ---
            WriteShard("item_shard", new Color(0.75f, 0.92f, 1f), spritePaths);

            // --- Egg-shard nest: twig-ring base + 5 egg-assembly build states ---
            WriteNest("nest_base", spritePaths);
            for (int i = 0; i < 5; i++)
            {
                WriteEggAssembly($"egg_assembly_{i}", i / 4f, spritePaths);
            }

            // --- Fruit (apple/banana/berry/watermelon) ---
            WriteRoundItem("fruit_0", new Color(0.9f, 0.2f, 0.2f), spritePaths);   // apple
            WriteRoundItem("fruit_1", new Color(0.95f, 0.85f, 0.2f), spritePaths); // banana(ish)
            WriteRoundItem("fruit_2", new Color(0.5f, 0.2f, 0.7f), spritePaths);   // berry
            WriteRoundItem("fruit_3", new Color(0.3f, 0.8f, 0.4f), spritePaths);   // watermelon

            // --- Treasure (coin/gem/boot/bone) ---
            WriteRoundItem("treasure_0", new Color(1f, 0.84f, 0.2f), spritePaths);  // coin
            WriteRoundItem("treasure_1", new Color(0.3f, 0.9f, 0.9f), spritePaths); // gem
            WriteRoundItem("treasure_2", new Color(0.5f, 0.3f, 0.15f), spritePaths);// boot
            WriteRoundItem("treasure_3", new Color(0.95f, 0.95f, 0.9f), spritePaths);// bone

            // --- Dirt dig tiles: 3 crack states ---
            for (int i = 0; i < 3; i++)
            {
                WriteDirt($"dirt_{i}", i, spritePaths);
            }

            // --- Particles ---
            WriteStar("fx_star", new Color(1f, 0.9f, 0.3f), spritePaths);
            WriteHeart("fx_heart", new Color(1f, 0.45f, 0.6f), spritePaths);
            WriteCrumb("fx_crumb", new Color(0.55f, 0.4f, 0.25f), spritePaths);

            // --- Icons ---
            WriteRoundItem("icon_treasure", new Color(1f, 0.84f, 0.2f), spritePaths);
            WriteSpeaker("icon_sound", true, spritePaths);
            WriteSpeaker("icon_mute", false, spritePaths);

            AssetDatabase.Refresh();

            // 2) Configure importers.
            foreach (string p in tilePaths)
            {
                ConfigureSprite(p, PpuTile);
            }

            foreach (string p in spritePaths)
            {
                ConfigureSprite(p, PpuSprite);
            }

            AssetDatabase.Refresh();

            // 3) Build Tile assets.
            CreateTile("Grass", SpritePath("tile_grass"));
            CreateTile("Path", SpritePath("tile_path"));
            CreateTile("Water", SpritePath("tile_water"));
            CreateTile("Bridge", SpritePath("tile_bridge"));
            CreateTile("Mound", SpritePath("tile_mound"));
            CreateTile("Tree", SpritePath("tile_tree"));
            CreateTile("Rock", SpritePath("tile_rock"));

            // 4) Build config SOs.
            BuildConfigs(dinoColors);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[PlaceholderArtGenerator] Done. Art + config written to " + Root);
        }

        // ------------------------------------------------------------- configs

        // All nine species colors (0-3 egg-hatchable, 4-8 shard-exclusive). Kept in
        // one place so the PNG pass and the config pass never drift out of sync.
        private static readonly Color[] DinoColors =
        {
            new Color(0.36f, 0.72f, 0.36f), // 0 T-Rex           green
            new Color(0.95f, 0.55f, 0.20f), // 1 Triceratops     orange
            new Color(0.30f, 0.55f, 0.95f), // 2 Brachiosaurus   blue
            new Color(0.62f, 0.40f, 0.85f), // 3 Stegosaurus     purple
            new Color(0.20f, 0.75f, 0.72f), // 4 Pteranodon      teal
            new Color(0.85f, 0.25f, 0.22f), // 5 Ankylosaurus    red
            new Color(0.70f, 0.80f, 0.25f), // 6 Spinosaurus     yellow-green
            new Color(0.95f, 0.55f, 0.75f), // 7 Parasaurolophus pink
            new Color(0.62f, 0.72f, 0.80f)  // 8 Velociraptor    sky-grey
        };

        private static void BuildConfigs(Color[] dinoColors)
        {
            string[] names =
            {
                "TRex", "Triceratops", "Brachiosaurus", "Stegosaurus",
                "Pteranodon", "Ankylosaurus", "Spinosaurus", "Parasaurolophus", "Velociraptor"
            };
            DanceType[] dances =
            {
                DanceType.StompRoar, DanceType.HeadShake, DanceType.NeckSway, DanceType.TailWag,
                DanceType.WingFlap, DanceType.TailClub, DanceType.SailWiggle, DanceType.CrestToot,
                DanceType.SpinHop
            };

            var defs = new List<DinoDefinition>();
            for (int i = 0; i < names.Length; i++)
            {
                var def = LoadOrCreate<DinoDefinition>($"{ConfigDir}/Dino_{names[i]}.asset");
                def.Type = (DinoType)i;
                def.DisplayName = names[i];
                def.Dance = dances[i];
                def.BodyColor = dinoColors[i];
                def.EggColor = dinoColors[i];
                def.EggSprite = LoadSprite(SpritePath($"egg_{i}"));

                Sprite blob = LoadSprite(SpritePath($"dino_{i}"));
                def.WalkSprites = new Sprite[8];
                for (int d = 0; d < 8; d++)
                {
                    def.WalkSprites[d] = blob;
                }

                def.IdleSprite = blob;
                EditorUtility.SetDirty(def);
                defs.Add(def);
            }

            var gameConfig = LoadOrCreate<GameConfig>($"{ConfigDir}/GameConfig.asset");
            gameConfig.Dinos = defs;
            EditorUtility.SetDirty(gameConfig);

            var audioConfig = LoadOrCreate<AudioConfig>($"{ConfigDir}/AudioConfig.asset");
            EditorUtility.SetDirty(audioConfig);

            var lib = LoadOrCreate<PlaceholderLibrary>($"{ConfigDir}/PlaceholderLibrary.asset");
            lib.GrassTile = LoadTile("Grass");
            lib.PathTile = LoadTile("Path");
            lib.WaterTile = LoadTile("Water");
            lib.BridgeTile = LoadTile("Bridge");
            lib.MoundTile = LoadTile("Mound");
            lib.TreeTile = LoadTile("Tree");
            lib.RockTile = LoadTile("Rock");

            lib.BackhoeDir = new Sprite[8];
            for (int i = 0; i < 8; i++)
            {
                lib.BackhoeDir[i] = LoadSprite(SpritePath($"backhoe_{i}"));
            }

            lib.BackhoeBody = LoadSprite(SpritePath("backhoe_body"));
            lib.ScoopArm = LoadSprite(SpritePath("backhoe_scoop"));
            lib.MoundSprite = LoadSprite(SpritePath("tile_mound"));

            lib.DirtStates = new Sprite[3];
            for (int i = 0; i < 3; i++)
            {
                lib.DirtStates[i] = LoadSprite(SpritePath($"dirt_{i}"));
            }

            lib.FruitSprites = new Sprite[4];
            lib.TreasureSprites = new Sprite[4];
            for (int i = 0; i < 4; i++)
            {
                lib.FruitSprites[i] = LoadSprite(SpritePath($"fruit_{i}"));
                lib.TreasureSprites[i] = LoadSprite(SpritePath($"treasure_{i}"));
            }

            lib.ShardSprite = LoadSprite(SpritePath("item_shard"));

            lib.NestSprite = LoadSprite(SpritePath("nest_base"));
            lib.EggAssemblySprites = new Sprite[5];
            for (int i = 0; i < 5; i++)
            {
                lib.EggAssemblySprites[i] = LoadSprite(SpritePath($"egg_assembly_{i}"));
            }

            lib.StarParticle = LoadSprite(SpritePath("fx_star"));
            lib.HeartParticle = LoadSprite(SpritePath("fx_heart"));
            lib.CrumbParticle = LoadSprite(SpritePath("fx_crumb"));
            lib.TreasureIcon = LoadSprite(SpritePath("icon_treasure"));
            lib.SoundIcon = LoadSprite(SpritePath("icon_sound"));
            lib.MuteIcon = LoadSprite(SpritePath("icon_mute"));
            EditorUtility.SetDirty(lib);
        }

        // ---------------------------------------------------------- draw: tiles

        private static void WriteDiamond(string name, Color32 fill, Color32 outline, List<string> paths)
        {
            var px = NewCanvas(TileW, TileH);
            FillDiamond(px, TileW, TileH, fill, outline);
            SaveTile(name, px, TileW, TileH, paths);
        }

        private static void WriteMound(string name, List<string> paths)
        {
            var px = NewCanvas(TileW, TileH);
            FillDiamond(px, TileW, TileH, new Color32(120, 82, 48, 255), new Color32(80, 54, 30, 255));
            // A rounded hump of darker dirt in the middle.
            FillCircle(px, TileW, TileH, TileW / 2f, TileH / 2f + 6f, 22f, new Color32(96, 64, 36, 255));
            // sparkle dots
            SetPix(px, TileW, TileH, TileW / 2 - 12, TileH / 2 + 10, new Color32(255, 255, 210, 255));
            SetPix(px, TileW, TileH, TileW / 2 + 14, TileH / 2 + 4, new Color32(255, 255, 210, 255));
            SaveTile(name, px, TileW, TileH, paths);
        }

        /// <summary>Stone bridge deck: a blue water diamond (so it reads as sitting OVER
        /// the stream) overlaid with rows of stone-grey cobbles. Deliberately grey —
        /// distinct from the tan path tile and the plain blue water tile — so the player
        /// sees exactly where a walkway crosses the water. Any cobble overspill is masked
        /// back to the iso diamond so the tile keeps a clean silhouette.</summary>
        private static void WriteBridge(string name, List<string> paths)
        {
            var px = NewCanvas(TileW, TileH);
            FillDiamond(px, TileW, TileH, new Color32(80, 150, 230, 255), new Color32(50, 110, 190, 255));

            var stone = new Color32(150, 150, 162, 255);
            var stoneLite = new Color32(188, 188, 198, 255);
            var stoneDark = new Color32(96, 96, 110, 255);
            float cx = TileW / 2f, cy = TileH / 2f;

            // Three rows of rounded cobbles fanning along the diamond's wide (screen-
            // horizontal) axis; narrower rows are inset so they land inside the diamond.
            for (int row = -1; row <= 1; row++)
            {
                float ry = cy + row * 12f;
                int cobbles = 5 - Mathf.Abs(row);
                float inset = 26f - Mathf.Abs(row) * 6f;
                for (int i = 0; i < cobbles; i++)
                {
                    float t = cobbles > 1 ? i / (float)(cobbles - 1) : 0.5f;
                    float rx = Mathf.Lerp(cx - inset, cx + inset, t);
                    FillCircle(px, TileW, TileH, rx, ry, 8f, stone);
                    FillCircle(px, TileW, TileH, rx - 2f, ry + 2f, 3f, stoneLite);
                    FillCircle(px, TileW, TileH, rx + 3f, ry - 3f, 2f, stoneDark);
                }
            }

            // Mask overspill back to the diamond silhouette.
            float dcx = (TileW - 1) * 0.5f, dcy = (TileH - 1) * 0.5f;
            float hw = TileW * 0.5f - 1f, hh = TileH * 0.5f - 1f;
            var clear = new Color32(0, 0, 0, 0);
            for (int y = 0; y < TileH; y++)
            {
                for (int x = 0; x < TileW; x++)
                {
                    if (Mathf.Abs(x - dcx) / hw + Mathf.Abs(y - dcy) / hh > 1f)
                    {
                        px[y * TileW + x] = clear;
                    }
                }
            }

            SaveTile(name, px, TileW, TileH, paths);
        }

        private static void WriteObstacle(string name, Color32 body, Color32 trunk, bool tree, List<string> paths)
        {
            int w = SpriteSize, h = SpriteSize;
            var px = NewCanvas(w, h);
            if (tree)
            {
                FillRect(px, w, h, w / 2 - 8, 20, 16, 40, trunk);          // trunk
                FillCircle(px, w, h, w / 2f, 78f, 34f, body);             // canopy
                FillCircle(px, w, h, w / 2f - 20f, 66f, 24f, body);
                FillCircle(px, w, h, w / 2f + 20f, 66f, 24f, body);
            }
            else
            {
                FillCircle(px, w, h, w / 2f, 50f, 38f, body);             // rock
                FillCircle(px, w, h, w / 2f - 14f, 60f, 20f, Lighten(body, 0.15f));
            }

            SaveTile(name, px, w, h, paths);
        }

        // --------------------------------------------------------- draw: chars

        private static void WriteBackhoe(string name, Dir8 dir, List<string> paths)
        {
            int s = SpriteSize;
            var px = NewCanvas(s, s);
            var yellow = new Color32(245, 205, 60, 255);
            var dark = new Color32(60, 45, 20, 255);

            FillCircleOutline(px, s, s, s / 2f, s / 2f - 6f, 40f, yellow, dark, 4f); // body
            FillRect(px, s, s, s / 2 - 26, s / 2 + 8, 52, 26, yellow);               // cab
            // eyes
            FillCircle(px, s, s, s / 2f - 12f, s / 2f + 16f, 7f, Color.white);
            FillCircle(px, s, s, s / 2f + 12f, s / 2f + 16f, 7f, Color.white);
            FillCircle(px, s, s, s / 2f - 12f, s / 2f + 16f, 3f, Color.black);
            FillCircle(px, s, s, s / 2f + 12f, s / 2f + 16f, 3f, Color.black);

            // direction "nose" bump
            Vector2 d = Direction8.ToVector(dir);
            float nx = s / 2f + d.x * 34f;
            float ny = s / 2f - 6f + d.y * 34f;
            FillCircle(px, s, s, nx, ny, 10f, dark);

            SaveSprite(name, px, s, s, paths);
        }

        private static void WriteBackhoeBody(string name, List<string> paths)
        {
            int s = SpriteSize;
            var px = NewCanvas(s, s);
            var yellow = new Color32(245, 205, 60, 255);
            var dark = new Color32(60, 45, 20, 255);
            FillRect(px, s, s, 24, 30, 80, 44, yellow);                 // chassis
            FillRect(px, s, s, 40, 66, 44, 30, yellow);                 // cab
            FillCircleOutline(px, s, s, 44f, 30f, 16f, dark, Color.black, 3f); // wheel
            FillCircleOutline(px, s, s, 86f, 30f, 16f, dark, Color.black, 3f);
            FillCircle(px, s, s, 58f, 80f, 6f, Color.white);
            SaveSprite(name, px, s, s, paths);
        }

        private static void WriteScoop(string name, List<string> paths)
        {
            int s = SpriteSize;
            var px = NewCanvas(s, s);
            var metal = new Color32(200, 170, 60, 255);
            FillRect(px, s, s, s / 2 - 18, s / 2 - 22, 36, 26, metal);
            FillRect(px, s, s, s / 2 - 18, s / 2 - 24, 36, 6, new Color32(120, 100, 40, 255)); // teeth
            SaveSprite(name, px, s, s, paths);
        }

        private static void WriteDino(string name, Color color, List<string> paths)
        {
            int s = SpriteSize;
            var px = NewCanvas(s, s);
            Color32 body = color;
            Color32 dark = new Color32((byte)(color.r * 150), (byte)(color.g * 150), (byte)(color.b * 150), 255);

            FillCircle(px, s, s, s / 2f, s / 2f - 8f, 36f, body);        // body
            FillCircle(px, s, s, s / 2f + 4f, s / 2f + 28f, 22f, body);  // head
            FillRect(px, s, s, s / 2 - 22, 18, 12, 22, dark);            // legs
            FillRect(px, s, s, s / 2 + 10, 18, 12, 22, dark);
            // eyes
            FillCircle(px, s, s, s / 2f - 2f, s / 2f + 34f, 6f, Color.white);
            FillCircle(px, s, s, s / 2f + 14f, s / 2f + 34f, 6f, Color.white);
            FillCircle(px, s, s, s / 2f - 2f, s / 2f + 34f, 3f, Color.black);
            FillCircle(px, s, s, s / 2f + 14f, s / 2f + 34f, 3f, Color.black);
            SaveSprite(name, px, s, s, paths);
        }

        private static void WriteEgg(string name, Color color, List<string> paths)
        {
            int s = SpriteSize;
            var px = NewCanvas(s, s);
            Color32 shell = Lighten((Color32)color, 0.35f);
            FillEllipse(px, s, s, s / 2f, s / 2f, 30f, 40f, shell, new Color32(60, 50, 40, 255));
            // spots telegraphing the dino color
            Color32 spot = color;
            FillCircle(px, s, s, s / 2f - 10f, s / 2f + 6f, 6f, spot);
            FillCircle(px, s, s, s / 2f + 12f, s / 2f - 8f, 7f, spot);
            FillCircle(px, s, s, s / 2f, s / 2f - 20f, 5f, spot);
            SaveSprite(name, px, s, s, paths);
        }

        private static void WriteShard(string name, Color color, List<string> paths)
        {
            int s = SpriteSize;
            var px = NewCanvas(s, s);
            Color32 shell = (Color32)color;
            Color32 edge = new Color32(70, 95, 115, 255);
            var white = new Color32(255, 255, 255, 255);

            // A broken egg-shell fragment: a broad shell chunk tapering to a point,
            // with a couple of speckles (reads as "egg") and a bright sparkle.
            int cx = s / 2, top = s / 2 + 34, bot = s / 2 - 34;
            FillTriangle(px, s, s, cx - 34, top, cx + 34, top, cx, bot, shell);
            FillTriangle(px, s, s, cx - 34, top, cx - 6, top - 46, cx, bot, shell);
            FillCircle(px, s, s, cx - 8, top - 14, 5f, edge);
            FillCircle(px, s, s, cx + 10, top - 24, 4f, edge);

            // sparkle
            DrawLineThick(px, s, s, cx + 14, top - 6, cx + 14, top - 30, white, 3);
            DrawLineThick(px, s, s, cx + 2, top - 18, cx + 26, top - 18, white, 3);
            FillCircle(px, s, s, cx + 14, top - 18, 3f, white);

            SaveSprite(name, px, s, s, paths);
        }

        private static void WriteRoundItem(string name, Color color, List<string> paths)
        {
            int s = SpriteSize;
            var px = NewCanvas(s, s);
            FillCircleOutline(px, s, s, s / 2f, s / 2f, 34f, color, new Color32(40, 30, 20, 255), 4f);
            FillCircle(px, s, s, s / 2f - 10f, s / 2f + 10f, 7f, Lighten((Color32)color, 0.4f)); // shine
            SaveSprite(name, px, s, s, paths);
        }

        // ------------------------------------------------------ draw: nest + egg

        /// <summary>Brown twig-ring nest bowl (flattened for the iso ground plane),
        /// with a darker hollow where the assembling egg sits and scruffy twig strokes
        /// around the rim. Style-consistent with the other placeholder props.</summary>
        private static void WriteNest(string name, List<string> paths)
        {
            int s = SpriteSize;
            var px = NewCanvas(s, s);
            Color32 twig = new Color32(150, 104, 58, 255);
            Color32 twigDark = new Color32(98, 66, 36, 255);
            float cx = s / 2f, cy = s / 2f - 6f;

            FillEllipse(px, s, s, cx, cy, 48f, 27f, twig, twigDark);   // outer bowl
            FillEllipse(px, s, s, cx, cy + 4f, 31f, 15f, twigDark, twigDark); // hollow

            var rng = new System.Random(name.GetHashCode());
            for (int i = 0; i < 24; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r0 = 30f + (float)rng.NextDouble() * 15f;
                int x0 = (int)(cx + Mathf.Cos(a) * r0);
                int y0 = (int)(cy + Mathf.Sin(a) * r0 * 0.55f);
                int x1 = (int)(cx + Mathf.Cos(a + 0.32f) * (r0 + 8f));
                int y1 = (int)(cy + Mathf.Sin(a + 0.32f) * (r0 + 8f) * 0.55f);
                DrawLineThick(px, s, s, x0, y0, x1, y1, twigDark, 2);
            }

            SaveSprite(name, px, s, s, paths);
        }

        /// <summary>One egg-assembly build state. <paramref name="fraction"/> 0..1 fills
        /// the egg from the bottom up (shell fragments piecing together), with a jagged
        /// seam at the current assembly line and a sparkle glint once whole. Cream shell
        /// so it reads as a nascent egg regardless of eventual species.</summary>
        private static void WriteEggAssembly(string name, float fraction, List<string> paths)
        {
            int s = SpriteSize;
            var px = NewCanvas(s, s);
            Color32 shell = new Color32(240, 230, 205, 255);
            Color32 outline = new Color32(92, 78, 60, 255);
            Color32 crack = new Color32(120, 100, 70, 255);

            float cx = s / 2f, cy = s / 2f;
            float rx = 30f, ry = 40f;
            float fillTopY = (cy - ry) + (2f * ry) * fraction; // assemble bottom -> top

            int minX = Mathf.Max(0, (int)(cx - rx));
            int maxX = Mathf.Min(s - 1, (int)(cx + rx));
            int minY = Mathf.Max(0, (int)(cy - ry));
            int maxY = Mathf.Min(s - 1, (int)(cy + ry));
            for (int y = minY; y <= maxY; y++)
            {
                if (y > fillTopY)
                {
                    continue; // above the current assembly line: not built yet
                }

                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x - cx) / rx, dy = (y - cy) / ry;
                    float d = dx * dx + dy * dy;
                    if (d <= 1f)
                    {
                        SetPix(px, s, s, x, y, d >= 0.82f ? outline : shell);
                    }
                }
            }

            // Jagged assembly seam where the shell currently ends.
            if (fraction > 0.05f && fraction < 0.98f)
            {
                int seamY = Mathf.RoundToInt(fillTopY);
                for (int x = minX; x <= maxX; x += 6)
                {
                    int jitter = ((x / 6) % 2 == 0) ? 3 : -3;
                    DrawLineThick(px, s, s, x, seamY + jitter, x + 6, seamY - jitter, crack, 2);
                }
            }

            if (fraction > 0.25f)
            {
                FillCircle(px, s, s, cx - 9f, cy - ry * 0.45f, 4f, crack); // a shell speckle
            }

            if (fraction >= 0.98f)
            {
                FillCircle(px, s, s, cx + 11f, cy + 13f, 5f, new Color32(255, 255, 255, 235)); // glint
            }

            SaveSprite(name, px, s, s, paths);
        }

        // ----------------------------------------------------------- draw: dig

        private static void WriteDirt(string name, int state, List<string> paths)
        {
            int s = SpriteSize;
            var px = NewCanvas(s, s);
            var dirt = new Color32(150, 105, 62, 255);
            var edge = new Color32(110, 76, 44, 255);
            FillRect(px, s, s, 2, 2, s - 4, s - 4, dirt);
            // border
            DrawRectOutline(px, s, s, 2, 2, s - 4, s - 4, edge, 3);

            // speckles
            var rng = new System.Random(name.GetHashCode());
            for (int i = 0; i < 30; i++)
            {
                int x = rng.Next(6, s - 6);
                int y = rng.Next(6, s - 6);
                SetPix(px, s, s, x, y, edge);
            }

            // cracks grow with state
            if (state >= 1)
            {
                DrawLineThick(px, s, s, s / 2, s - 6, s / 2 - 14, s / 2, new Color32(70, 48, 28, 255), 3);
                DrawLineThick(px, s, s, s / 2, s / 2, s / 2 + 18, 8, new Color32(70, 48, 28, 255), 3);
            }

            if (state >= 2)
            {
                DrawLineThick(px, s, s, 8, s / 2 + 10, s - 8, s / 2 - 10, new Color32(70, 48, 28, 255), 3);
                DrawLineThick(px, s, s, s / 2 - 20, s - 8, s / 2 - 8, 8, new Color32(70, 48, 28, 255), 3);
            }

            SaveSprite(name, px, s, s, paths);
        }

        // ----------------------------------------------------- draw: particles

        private static void WriteStar(string name, Color color, List<string> paths)
        {
            int s = ParticleSize;
            var px = NewCanvas(s, s);
            FillCircle(px, s, s, s / 2f, s / 2f, 6f, color);
            // 4 spikes
            DrawLineThick(px, s, s, s / 2, 3, s / 2, s - 3, color, 3);
            DrawLineThick(px, s, s, 3, s / 2, s - 3, s / 2, color, 3);
            SaveSprite(name, px, s, s, paths);
        }

        private static void WriteHeart(string name, Color color, List<string> paths)
        {
            int s = ParticleSize;
            var px = NewCanvas(s, s);
            FillCircle(px, s, s, s / 2f - 5f, s / 2f + 4f, 7f, color);
            FillCircle(px, s, s, s / 2f + 5f, s / 2f + 4f, 7f, color);
            FillTriangle(px, s, s, s / 2 - 11, s / 2 + 5, s / 2 + 11, s / 2 + 5, s / 2, 4, color);
            SaveSprite(name, px, s, s, paths);
        }

        private static void WriteCrumb(string name, Color color, List<string> paths)
        {
            int s = ParticleSize;
            var px = NewCanvas(s, s);
            FillRect(px, s, s, s / 2 - 5, s / 2 - 5, 10, 10, color);
            SaveSprite(name, px, s, s, paths);
        }

        private static void WriteSpeaker(string name, bool on, List<string> paths)
        {
            int s = SpriteSize;
            var px = NewCanvas(s, s);
            var col = new Color32(60, 60, 70, 255);
            FillRect(px, s, s, 30, s / 2 - 14, 16, 28, col);            // base
            FillTriangle(px, s, s, 46, s / 2 - 22, 46, s / 2 + 22, 74, s / 2, col); // cone
            if (on)
            {
                FillCircleOutline(px, s, s, 86f, s / 2f, 16f, new Color32(0, 0, 0, 0), col, 3f);
                FillCircleOutline(px, s, s, 86f, s / 2f, 24f, new Color32(0, 0, 0, 0), col, 3f);
            }
            else
            {
                DrawLineThick(px, s, s, 80, s / 2 - 16, 104, s / 2 + 16, new Color32(210, 60, 60, 255), 4);
                DrawLineThick(px, s, s, 104, s / 2 - 16, 80, s / 2 + 16, new Color32(210, 60, 60, 255), 4);
            }

            SaveSprite(name, px, s, s, paths);
        }

        // ---------------------------------------------------- pixel primitives

        private static Color32[] NewCanvas(int w, int h)
        {
            var px = new Color32[w * h];
            var clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < px.Length; i++)
            {
                px[i] = clear;
            }

            return px;
        }

        private static void SetPix(Color32[] px, int w, int h, int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= w || y >= h)
            {
                return;
            }

            px[y * w + x] = c;
        }

        private static void FillRect(Color32[] px, int w, int h, int x0, int y0, int rw, int rh, Color32 c)
        {
            for (int y = y0; y < y0 + rh; y++)
            {
                for (int x = x0; x < x0 + rw; x++)
                {
                    SetPix(px, w, h, x, y, c);
                }
            }
        }

        private static void DrawRectOutline(Color32[] px, int w, int h, int x0, int y0, int rw, int rh, Color32 c, int t)
        {
            FillRect(px, w, h, x0, y0, rw, t, c);
            FillRect(px, w, h, x0, y0 + rh - t, rw, t, c);
            FillRect(px, w, h, x0, y0, t, rh, c);
            FillRect(px, w, h, x0 + rw - t, y0, t, rh, c);
        }

        private static void FillCircle(Color32[] px, int w, int h, float cx, float cy, float r, Color32 c)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(cx - r));
            int maxX = Mathf.Min(w - 1, Mathf.CeilToInt(cx + r));
            int minY = Mathf.Max(0, Mathf.FloorToInt(cy - r));
            int maxY = Mathf.Min(h - 1, Mathf.CeilToInt(cy + r));
            float r2 = r * r;
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy <= r2)
                    {
                        SetPix(px, w, h, x, y, c);
                    }
                }
            }
        }

        private static void FillCircleOutline(Color32[] px, int w, int h, float cx, float cy, float r,
            Color32 fill, Color32 outline, float t)
        {
            float r2 = r * r;
            float ri2 = (r - t) * (r - t);
            int minX = Mathf.Max(0, Mathf.FloorToInt(cx - r));
            int maxX = Mathf.Min(w - 1, Mathf.CeilToInt(cx + r));
            int minY = Mathf.Max(0, Mathf.FloorToInt(cy - r));
            int maxY = Mathf.Min(h - 1, Mathf.CeilToInt(cy + r));
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float d2 = dx * dx + dy * dy;
                    if (d2 > r2)
                    {
                        continue;
                    }

                    SetPix(px, w, h, x, y, d2 >= ri2 ? outline : fill);
                }
            }
        }

        private static void FillEllipse(Color32[] px, int w, int h, float cx, float cy, float rx, float ry,
            Color32 fill, Color32 outline)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(cx - rx));
            int maxX = Mathf.Min(w - 1, Mathf.CeilToInt(cx + rx));
            int minY = Mathf.Max(0, Mathf.FloorToInt(cy - ry));
            int maxY = Mathf.Min(h - 1, Mathf.CeilToInt(cy + ry));
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x - cx) / rx, dy = (y - cy) / ry;
                    float d = dx * dx + dy * dy;
                    if (d <= 1f)
                    {
                        SetPix(px, w, h, x, y, d >= 0.82f ? outline : fill);
                    }
                }
            }
        }

        private static void FillDiamond(Color32[] px, int w, int h, Color32 fill, Color32 outline)
        {
            float cx = (w - 1) * 0.5f;
            float cy = (h - 1) * 0.5f;
            float hw = w * 0.5f - 1f;
            float hh = h * 0.5f - 1f;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float d = Mathf.Abs(x - cx) / hw + Mathf.Abs(y - cy) / hh;
                    if (d <= 1f)
                    {
                        SetPix(px, w, h, x, y, d >= 0.88f ? outline : fill);
                    }
                }
            }
        }

        private static void FillTriangle(Color32[] px, int w, int h, int x0, int y0, int x1, int y1,
            int x2, int y2, Color32 c)
        {
            int minX = Mathf.Max(0, Mathf.Min(x0, Mathf.Min(x1, x2)));
            int maxX = Mathf.Min(w - 1, Mathf.Max(x0, Mathf.Max(x1, x2)));
            int minY = Mathf.Max(0, Mathf.Min(y0, Mathf.Min(y1, y2)));
            int maxY = Mathf.Min(h - 1, Mathf.Max(y0, Mathf.Max(y1, y2)));
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (PointInTri(x, y, x0, y0, x1, y1, x2, y2))
                    {
                        SetPix(px, w, h, x, y, c);
                    }
                }
            }
        }

        private static bool PointInTri(int px, int py, int ax, int ay, int bx, int by, int cx, int cy)
        {
            float d1 = Sign(px, py, ax, ay, bx, by);
            float d2 = Sign(px, py, bx, by, cx, cy);
            float d3 = Sign(px, py, cx, cy, ax, ay);
            bool neg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            bool pos = (d1 > 0) || (d2 > 0) || (d3 > 0);
            return !(neg && pos);
        }

        private static float Sign(int px, int py, int ax, int ay, int bx, int by)
        {
            return (px - bx) * (ay - by) - (ax - bx) * (py - by);
        }

        private static void DrawLineThick(Color32[] px, int w, int h, int x0, int y0, int x1, int y1, Color32 c, int t)
        {
            int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            int half = Mathf.Max(1, t / 2);
            while (true)
            {
                FillCircle(px, w, h, x0, y0, half, c);
                if (x0 == x1 && y0 == y1)
                {
                    break;
                }

                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }

        private static Color32 Lighten(Color32 c, float amt)
        {
            return new Color32(
                (byte)Mathf.Clamp(c.r + 255 * amt, 0, 255),
                (byte)Mathf.Clamp(c.g + 255 * amt, 0, 255),
                (byte)Mathf.Clamp(c.b + 255 * amt, 0, 255),
                c.a);
        }

        // ------------------------------------------------------- asset helpers

        private static void SaveSprite(string name, Color32[] px, int w, int h, List<string> spritePaths)
        {
            string path = SpritePath(name);
            WritePng(px, w, h, path);
            spritePaths.Add(path);
        }

        private static void SaveTile(string name, Color32[] px, int w, int h, List<string> tilePaths)
        {
            string path = SpritePath(name);
            WritePng(px, w, h, path);
            tilePaths.Add(path);
        }

        private static void WritePng(Color32[] px, int w, int h, string path)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            byte[] bytes = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            File.WriteAllBytes(ProjectPath(path), bytes);
        }

        private static void ConfigureSprite(string assetPath, float ppu)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = ppu;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private static void CreateTile(string tileName, string spriteAssetPath)
        {
            Sprite sprite = LoadSprite(spriteAssetPath);
            string path = $"{TilesDir}/{tileName}.asset";
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, path);
            }

            tile.sprite = sprite;
            tile.colliderType = Tile.ColliderType.None;
            EditorUtility.SetDirty(tile);
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }

            return asset;
        }

        private static Sprite LoadSprite(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(path);
        private static Tile LoadTile(string name) => AssetDatabase.LoadAssetAtPath<Tile>($"{TilesDir}/{name}.asset");
        private static string SpritePath(string name) => $"{SpritesDir}/{name}.png";
        private static string ProjectPath(string assetPath) =>
            Path.Combine(Directory.GetCurrentDirectory(), assetPath);

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/Art");
            EnsureFolder(Root);
            EnsureFolder(SpritesDir);
            EnsureFolder(TilesDir);
            EnsureFolder(ConfigDir);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
