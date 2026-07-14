using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
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

                lib.FruitSprites = LoadArray(fruit, missing);
                lib.TreasureSprites = LoadArray(treasure, missing);
                lib.DirtStates = LoadArray(dirt, missing);

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

                // Tilemap tiles + MoundSprite + icons intentionally left on placeholders.
                EditorUtility.SetDirty(lib);
                wired.Add("Library: backhoe 8-dir, fruit x4, treasure x4, dirt x3, particles x3");
                wired.Add("Library: tiles/mound/icons kept on placeholders (flat env per art direction)");
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

                EditorUtility.SetDirty(audio);
                wired.Add("AudioConfig: Music=Bluebonnet_looped, Tap=click_002, Move=switch_004, " +
                          "Dig=drop_002, Crumble=scratch_003, ItemPop=pluck_002, Chime=threeTone1, " +
                          "Hatch=powerUp1, Roar=lowThreeTone, Eat=pepSound2, Grow=phaserUp1, " +
                          "TreasureCollect=highUp, Honk=twoTone1, Heart=glass_001");
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
    }
}
