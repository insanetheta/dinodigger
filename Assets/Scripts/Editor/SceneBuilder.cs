using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using DinoDigger.Config;
using DinoDigger.Core;
using DinoDigger.Dig;
using DinoDigger.Input;
using DinoDigger.Overworld;
using DinoDigger.UI;

namespace DinoDigger.EditorTools
{
    /// <summary>
    /// Builds Assets/Scenes/Main.unity from scratch (idempotent). Creates the
    /// isometric island, all managers wired together, the dig-mode root (parked at
    /// world x=1000 — a SINGLE camera is moved there by CameraFollow), the UI, and
    /// an Input-System EventSystem. Assigns placeholder art from the generator.
    ///
    /// Camera approach: one orthographic Main Camera. During Roam it follows the
    /// backhoe; EnterDig eases it to the dig root's grid center and zooms in; on
    /// item reveal it eases back. No second camera, no scene load.
    ///
    /// Menu: DinoDigger/Build Main Scene.
    /// </summary>
    public static class SceneBuilder
    {
        private const int N = 48;
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private const string ConfigDir = "Assets/Art/Placeholder/Config";
        private const string DuckDir = "Assets/Art/Generated/duck";
        private const float DuckWorldHeight = 0.5f;
        private static readonly Vector3 DigRootPos = new Vector3(1000f, 0f, 0f);

        [MenuItem("DinoDigger/Build Main Scene")]
        public static void BuildMainScene()
        {
            // Ensure art + config exist (existence check only — real loads happen after NewScene).
            if (AssetDatabase.LoadAssetAtPath<PlaceholderLibrary>($"{ConfigDir}/PlaceholderLibrary.asset") == null ||
                AssetDatabase.LoadAssetAtPath<GameConfig>($"{ConfigDir}/GameConfig.asset") == null)
            {
                Debug.Log("[SceneBuilder] Placeholder assets missing — generating first.");
                PlaceholderArtGenerator.Generate();
            }

            // NewScene(Single) runs an asset GC that unloads assets held only by locals,
            // leaving fake-null references — so create the scene FIRST, then load configs.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lib = AssetDatabase.LoadAssetAtPath<PlaceholderLibrary>($"{ConfigDir}/PlaceholderLibrary.asset");
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>($"{ConfigDir}/GameConfig.asset");
            var audioConfig = AssetDatabase.LoadAssetAtPath<AudioConfig>($"{ConfigDir}/AudioConfig.asset");

            // ---- Camera ----
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = config != null ? config.RoamOrthoSize : 5.5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.8f, 0.95f);
            cam.transparencySortMode = TransparencySortMode.CustomAxis;
            cam.transparencySortAxis = new Vector3(0f, 1f, 0f);
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            camGo.AddComponent<AudioListener>();
            var camFollow = camGo.AddComponent<CameraFollow>();

            // ---- Grid + tilemaps ----
            var gridGo = new GameObject("Grid");
            var grid = gridGo.AddComponent<Grid>();
            grid.cellLayout = GridLayout.CellLayout.IsometricZAsY;
            grid.cellSize = new Vector3(1f, 0.5f, 1f);

            Tilemap ground = CreateTilemap(gridGo, "Ground", 0);
            Tilemap water = CreateTilemap(gridGo, "Water", 1);
            Tilemap obstacles = CreateTilemap(gridGo, "Obstacles", 5);

            var overworldMap = gridGo.AddComponent<OverworldMap>();
            var streamNetwork = gridGo.AddComponent<StreamNetwork>();

            // ---- Paint island ----
            char[,] map = BuildMap(out RectInt meadowRect);
            Vector3Int startCell = FindStartCell(map); // resolved before mounds so we can keep them clear of it

            // Carve meandering 1-tile streams (north coast -> pond, pond -> south
            // coast, east coast -> pond), bridged where they cross a path band, then
            // run the MANDATORY post-gen connectivity check so no walkable region is
            // ever orphaned.
            var streamCourses = new List<List<Vector3Int>>();
            CarveStreams(map, meadowRect, startCell, streamCourses);
            EnsureStreamConnectivity(map, startCell, streamCourses);

            var moundCells = new List<Vector3Int>();
            PaintMap(map, ground, water, obstacles, lib, moundCells, startCell, meadowRect);
            streamNetwork.Configure(grid, streamCourses);

            // ---- Overworld root ----
            var overworldRoot = new GameObject("Overworld");

            // ---- Dino meadow (fenced home area, NE quadrant) + egg-shard nest ----
            MeadowArea meadowArea = CreateMeadow(grid, overworldMap, overworldRoot.transform,
                meadowRect, lib, out NestController nest);

            // ---- Backhoe ----
            var backhoeGo = new GameObject("Backhoe");
            backhoeGo.transform.position = grid.GetCellCenterWorld(startCell);
            var backhoeSr = backhoeGo.AddComponent<SpriteRenderer>();
            backhoeSr.sortingOrder = 12;
            if (lib != null)
            {
                backhoeSr.sprite = SafeSprite(lib.BackhoeDir, 4); // facing S
            }

            var backhoe = backhoeGo.AddComponent<BackhoeController>();

            // ---- Dig mounds ----
            var mounds = new List<DigMound>();
            foreach (Vector3Int cell in moundCells)
            {
                DigMound m = CreateMound(grid.GetCellCenterWorld(cell), overworldRoot.transform, lib);
                mounds.Add(m);
            }

            // ---- Ducks (ambient pond life that drifts along the streams) ----
            var duckSpawnerGo = new GameObject("DuckSpawner");
            var duckController = duckSpawnerGo.AddComponent<DuckController>();
            duckController.Configure(streamNetwork, overworldMap,
                LoadDuckSprite("duck_E", DuckWorldHeight),
                LoadDuckSprite("duck_fly", DuckWorldHeight),
                overworldRoot.transform);

            // ---- Dig root (parked far away) ----
            var digRootGo = new GameObject("DigRoot");
            digRootGo.transform.position = DigRootPos;
            var digMode = digRootGo.AddComponent<DigModeController>();

            // ---- Dig background (full-bleed backdrop, well behind the dirt tiles) ----
            // Align the image's grass lip with the backhoe's surface line so sky sits
            // above the surface and soil covers all grid rows below it.
            //   PlaceBackhoe puts the backhoe body at surface = DigRootPos + (0, 0.1).
            //   In the source PNG the grass lip is ~0.48 of the way down from the top
            //   (GrassLipFraction, measured). A horizontal line at fraction f from the
            //   top maps to local y = H*(0.5 - f) ABOVE the sprite center (H = sprite
            //   world height from its cover-PPU). To land that line on the surface, the
            //   sprite center must sit offsetY = H*(0.5 - f) below the surface.
            // Grid rows span y = -0.5 .. -(rows+0.5) below the origin; with H ~= 14 the
            // soil half (below the lip) is ~7 units tall and covers all 5 rows (-5.5).
            const float surfaceY = 0.1f;
            const float grassLipFraction = 0.48f;
            float bgHeight = (lib != null && lib.DigBackground != null)
                ? lib.DigBackground.bounds.size.y
                : 14f; // fallback if art not yet imported (matches ~14-unit cover width, square source)
            float bgOffsetY = bgHeight * (0.5f - grassLipFraction);
            MakeChildRenderer(digRootGo.transform, "Background",
                lib != null ? lib.DigBackground : null, 2,
                DigRootPos + new Vector3(0f, surfaceY - bgOffsetY, 0f));

            // Body: prefer the armless dig body; fall back to the old side-view body.
            // Dig-view staging: close-up body ~2.4 units tall, parked at the LEFT
            // end of the surface (wheels on the grass lip at y=0.1) and MIRRORED so
            // its rear arm-mount faces the grid. DigModeController re-applies all of
            // this at runtime (its DigBodyH/BodyRestX are authoritative).
            Sprite bodySprite = lib != null
                ? (lib.DigBodySprite != null ? lib.DigBodySprite : lib.BackhoeBody)
                : null;
            var bodySr = MakeChildRenderer(digRootGo.transform, "BackhoeBody",
                bodySprite, 20, DigRootPos + new Vector3(-3.0f, 1.3f, 0f));
            bodySr.flipX = true;
            PreviewScaleUniformByHeight(bodySr, 2.4f);

            // ---- Two-bone excavator rig ----------------------------------------
            // ArmPivot (shoulder) -> Boom -> Elbow -> Stick -> Wrist -> Bucket.
            // The ArmPivot deliberately lives under DigRoot, NOT under the scaled
            // body transform (scaling would distort the bone lengths); the
            // controller glues it to the body's rear mount every frame. Sprites
            // fall back to the placeholder square when generated art is absent;
            // DigModeController re-positions the joint nodes and re-sizes the
            // segment sprites at runtime (single source of the reach geometry —
            // keep these numbers matching its BoomLen 3.4 / StickLen 3.1 /
            // thickness ~0.3 / bucket height 0.72), then solves two-bone IK to
            // each tapped tile. The preview scaling below only makes the saved
            // scene look sane in the editor.
            Sprite armFallback = lib != null ? lib.ScoopArm : null;
            Sprite boomSprite = lib != null && lib.BoomSprite != null ? lib.BoomSprite : armFallback;
            Sprite stickSprite = lib != null && lib.StickSprite != null ? lib.StickSprite : armFallback;
            Sprite bucketSprite = lib != null && lib.BucketSprite != null ? lib.BucketSprite : armFallback;

            Transform armPivot = MakeChildNode(digRootGo.transform, "ArmPivot", new Vector3(-2.05f, 1.45f, 0f));
            var boomSr = MakeChildRendererLocal(armPivot, "Boom", boomSprite, 21);
            PreviewSegment(boomSr, lib != null && lib.BoomSprite != null, 3.4f, 0.34f,
                new Vector2(0.1393f, 0.3525f), new Vector2(0.8970f, 0.5515f));
            Transform elbow = MakeChildNode(armPivot, "Elbow", new Vector3(3.4f, 0f, 0f));
            var stickSr = MakeChildRendererLocal(elbow, "Stick", stickSprite, 22);
            PreviewSegment(stickSr, lib != null && lib.StickSprite != null, 3.1f, 0.30f,
                new Vector2(0.1162f, 0.5026f), new Vector2(0.8929f, 0.5107f));
            Transform wrist = MakeChildNode(elbow, "Wrist", new Vector3(3.1f, 0f, 0f));
            var bucketSr = MakeChildRendererLocal(wrist, "Bucket", bucketSprite, 23);
            PreviewScaleUniformByHeight(bucketSr, 0.72f);

            var helperSr = MakeChildRenderer(digRootGo.transform, "HelperDino",
                null, 24, DigRootPos + new Vector3(4.4f, 0.1f, 0f));
            helperSr.enabled = false;
            if (lib != null && config != null && config.Dinos.Count > 0 && config.Dinos[0] != null)
            {
                helperSr.sprite = config.Dinos[0].GetIdle();
            }

            ParticleSystem crumbs = CreateParticle(digRootGo.transform, "Crumbs",
                lib != null ? lib.CrumbParticle : null, new Color(0.55f, 0.4f, 0.25f), false);

            // ---- Managers object ----
            var managerGo = new GameObject("GameManager");
            var input = managerGo.AddComponent<InputService>();
            var gm = managerGo.AddComponent<GameManager>();

            // ---- UI ----
            BuildUI(lib, out TreasureCounter counter, out MuteButton mute);

            // ---- EventSystem ----
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();

            // ---- Wire everything ----
            Wire(overworldMap, "_grid", grid);
            Wire(overworldMap, "_ground", ground);
            Wire(overworldMap, "_water", water);
            Wire(overworldMap, "_obstacles", obstacles);

            Wire(camFollow, "_camera", cam);
            Wire(camFollow, "_target", backhoeGo.transform);
            Wire(camFollow, "_config", config);

            Wire(backhoe, "_renderer", backhoeSr);
            Wire(backhoe, "_map", overworldMap);
            Wire(backhoe, "_config", config);
            WireArray(backhoe, "_dirSprites", lib != null ? lib.BackhoeDir : null);
            WireArray(backhoe, "_rollA", lib != null ? lib.BackhoeRollA : null);
            WireArray(backhoe, "_rollB", lib != null ? lib.BackhoeRollB : null);

            Wire(digMode, "_root", digRootGo.transform);
            Wire(digMode, "_backhoeBody", bodySr);
            Wire(digMode, "_armPivot", armPivot);
            Wire(digMode, "_boom", boomSr);
            Wire(digMode, "_elbow", elbow);
            Wire(digMode, "_stick", stickSr);
            Wire(digMode, "_wrist", wrist);
            Wire(digMode, "_bucket", bucketSr);
            Wire(digMode, "_helperDino", helperSr);
            Wire(digMode, "_crumbs", crumbs);

            // (TreasureCounter and MuteButton fields are wired inside BuildUI.)

            // GameManager fields
            Wire(gm, "_config", config);
            Wire(gm, "_library", lib);
            Wire(gm, "_audioConfig", audioConfig);
            Wire(gm, "_mainCamera", cam);
            Wire(gm, "_input", input);
            Wire(gm, "_backhoe", backhoe);
            Wire(gm, "_map", overworldMap);
            Wire(gm, "_cameraFollow", camFollow);
            Wire(gm, "_digMode", digMode);
            Wire(gm, "_treasureCounter", counter);
            Wire(gm, "_muteButton", mute);
            Wire(gm, "_overworldRoot", overworldRoot.transform);
            Wire(gm, "_meadow", meadowArea);
            Wire(gm, "_nest", nest);
            WireMoundList(gm, "_mounds", mounds);

            // ---- Save + register scene ----
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);

            AssetDatabase.SaveAssets();
            Debug.Log("[SceneBuilder] Built " + ScenePath);
        }

        // ------------------------------------------------------------- map gen

        // Meadow patch size (cells, including the decorative fence ring).
        private const int MeadowSize = 7;

        private static char[,] BuildMap(out RectInt meadowRect)
        {
            // Deterministic "handcrafted" island: grass ellipse, carved pond,
            // a crossing path, scattered trees/rocks. Legend:
            //   ~ ocean  G grass  P path  W water  T tree  R rock
            //   M meadow grass (reserved: fenced dino home, no trees/rocks/mounds)
            var m = new char[N, N];
            var rng = new System.Random(1337);
            Vector2 c = new Vector2((N - 1) * 0.5f, (N - 1) * 0.5f);

            for (int x = 0; x < N; x++)
            {
                for (int y = 0; y < N; y++)
                {
                    float nx = (x - c.x) / 23f;
                    float ny = (y - c.y) / 23f;
                    float d = Mathf.Sqrt(nx * nx + ny * ny);
                    m[x, y] = d < 0.95f ? 'G' : '~';
                }
            }

            // Pond (unwalkable) — an ellipse offset toward one corner.
            Vector2 pond = new Vector2(15f, 31f);
            for (int x = 0; x < N; x++)
            {
                for (int y = 0; y < N; y++)
                {
                    if (m[x, y] != 'G')
                    {
                        continue;
                    }

                    float px = (x - pond.x) / 5.6f;
                    float py = (y - pond.y) / 4.2f;
                    if (px * px + py * py < 1f)
                    {
                        m[x, y] = 'W';
                    }
                }
            }

            // A path band crossing the middle horizontally.
            for (int x = 0; x < N; x++)
            {
                for (int y = 21; y <= 22; y++)
                {
                    if (m[x, y] == 'G')
                    {
                        m[x, y] = 'P';
                    }
                }
            }

            // Vertical path leg.
            for (int y = 0; y < N; y++)
            {
                for (int x = 30; x <= 31; x++)
                {
                    if (m[x, y] == 'G')
                    {
                        m[x, y] = 'P';
                    }
                }
            }

            // Second horizontal path so the far half of the bigger island has a
            // landmark to wander along too.
            for (int x = 8; x < N - 4; x++)
            {
                for (int y = 34; y <= 35; y++)
                {
                    if (m[x, y] == 'G')
                    {
                        m[x, y] = 'P';
                    }
                }
            }

            // Reserve the dino meadow BEFORE scattering obstacles so the patch is
            // guaranteed clear, walkable grass (away from the start cell, pond and
            // paths — the search only accepts all-'G' windows).
            meadowRect = ReserveMeadow(m);

            // Scatter trees and rocks on plain grass (not path/pond/meadow).
            for (int x = 0; x < N; x++)
            {
                for (int y = 0; y < N; y++)
                {
                    if (m[x, y] != 'G')
                    {
                        continue;
                    }

                    int roll = rng.Next(0, 100);
                    if (roll < 6)
                    {
                        m[x, y] = 'T';
                    }
                    else if (roll < 10)
                    {
                        m[x, y] = 'R';
                    }
                }
            }

            return m;
        }

        /// <summary>Pick a MeadowSize x MeadowSize all-grass window in the island's
        /// north-east quadrant (far from the center start cell and the SW pond),
        /// preferring the most north-easterly fit, and mark it 'M'. Falls back to
        /// scanning the whole island, then to a center-adjacent window, so the
        /// build never fails even if the map generator changes.</summary>
        private static RectInt ReserveMeadow(char[,] m)
        {
            // Preferred: NE quadrant, scanned from the far corner inward.
            for (int y = N - 1 - MeadowSize; y >= N / 2; y--)
            {
                for (int x = N - 1 - MeadowSize; x >= N / 2; x--)
                {
                    if (IsAllGrass(m, x, y))
                    {
                        return MarkMeadow(m, x, y);
                    }
                }
            }

            // Fallback: anywhere on the island.
            for (int y = N - 1 - MeadowSize; y >= 0; y--)
            {
                for (int x = N - 1 - MeadowSize; x >= 0; x--)
                {
                    if (IsAllGrass(m, x, y))
                    {
                        return MarkMeadow(m, x, y);
                    }
                }
            }

            // Last resort: stamp near the NE of center (converts whatever is there
            // to meadow grass — still walkable, still fenced).
            return MarkMeadow(m, Mathf.Clamp(N / 2 + 4, 0, N - MeadowSize),
                Mathf.Clamp(N / 2 + 4, 0, N - MeadowSize));
        }

        private static bool IsAllGrass(char[,] m, int x0, int y0)
        {
            for (int x = x0; x < x0 + MeadowSize; x++)
            {
                for (int y = y0; y < y0 + MeadowSize; y++)
                {
                    if (m[x, y] != 'G')
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static RectInt MarkMeadow(char[,] m, int x0, int y0)
        {
            for (int x = x0; x < x0 + MeadowSize; x++)
            {
                for (int y = y0; y < y0 + MeadowSize; y++)
                {
                    m[x, y] = 'M';
                }
            }

            return new RectInt(x0, y0, MeadowSize, MeadowSize);
        }

        private static void PaintMap(char[,] m, Tilemap ground, Tilemap water, Tilemap obstacles,
            PlaceholderLibrary lib, List<Vector3Int> moundCells, Vector3Int startCell,
            RectInt meadowRect)
        {
            var grassCandidates = new List<Vector3Int>();

            for (int x = 0; x < N; x++)
            {
                for (int y = 0; y < N; y++)
                {
                    var cell = new Vector3Int(x, y, 0);
                    char t = m[x, y];
                    switch (t)
                    {
                        case 'G':
                            ground.SetTile(cell, lib != null ? lib.GrassTile : null);
                            grassCandidates.Add(cell);
                            break;
                        case 'M':
                            // Meadow: plain walkable grass, but NEVER a mound candidate.
                            ground.SetTile(cell, lib != null ? lib.GrassTile : null);
                            break;
                        case 'P':
                            ground.SetTile(cell, lib != null ? lib.PathTile : null);
                            grassCandidates.Add(cell);
                            break;
                        case 'W':
                            water.SetTile(cell, lib != null ? lib.WaterTile : null);
                            break;
                        case 'S':
                            // Stream: a water tile with NO ground tile => unwalkable,
                            // exactly like the pond.
                            water.SetTile(cell, lib != null ? lib.WaterTile : null);
                            break;
                        case 'B':
                            // Bridge: a path (or a connectivity heal) crosses the stream
                            // here. Paint the stone-grey bridge deck on the GROUND layer
                            // (walkable, like a path) with NO water tile, so the crossing
                            // stays passable and reads as a bridge over the blue channel.
                            // NOT a mound candidate.
                            ground.SetTile(cell, lib != null
                                ? (lib.BridgeTile != null ? lib.BridgeTile : lib.PathTile)
                                : null);
                            break;
                        case 'T':
                            ground.SetTile(cell, lib != null ? lib.GrassTile : null);
                            obstacles.SetTile(cell, lib != null ? lib.TreeTile : null);
                            break;
                        case 'R':
                            ground.SetTile(cell, lib != null ? lib.GrassTile : null);
                            obstacles.SetTile(cell, lib != null ? lib.RockTile : null);
                            break;
                    }
                }
            }

            // Choose 9 spread-out mound cells from plain grass/path candidates.
            PickMounds(grassCandidates, 12, moundCells, startCell, meadowRect);

            Debug.Log($"[SceneBuilder] PaintMap done: lib={(lib == null ? "NULL" : "ok")} " +
                      $"grassTile={(lib != null && lib.GrassTile != null ? "ok" : "NULL")} " +
                      $"candidates={grassCandidates.Count} groundUsed={ground.GetUsedTilesCount()} " +
                      $"waterUsed={water.GetUsedTilesCount()} obstUsed={obstacles.GetUsedTilesCount()}");
        }

        private static void PickMounds(List<Vector3Int> candidates, int count, List<Vector3Int> outCells,
            Vector3Int startCell, RectInt meadowRect)
        {
            var rng = new System.Random(4242);
            int guard = 0;
            while (outCells.Count < count && candidates.Count > 0 && guard++ < 500)
            {
                Vector3Int pick = candidates[rng.Next(candidates.Count)];

                // Keep a 3-cell clear radius around the backhoe's start cell so no
                // mound spawns on top of (or right next to) the player at launch.
                if (Mathf.Max(Mathf.Abs(pick.x - startCell.x), Mathf.Abs(pick.y - startCell.y)) <= 3)
                {
                    continue;
                }

                // Never inside (or on the fence of) the dino meadow. Candidates
                // already exclude 'M' cells; this guards against future map edits.
                if (meadowRect.Contains(new Vector2Int(pick.x, pick.y)))
                {
                    continue;
                }

                bool tooClose = false;
                foreach (Vector3Int c in outCells)
                {
                    // Doubled separation on the 4x island: mounds spread out ~2x.
                    if (Mathf.Abs(c.x - pick.x) + Mathf.Abs(c.y - pick.y) < 6)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (!tooClose)
                {
                    outCells.Add(pick);
                }
            }
        }

        private static Vector3Int FindStartCell(char[,] m)
        {
            // Center-ish path/grass cell.
            for (int r = 0; r < N; r++)
            {
                for (int x = N / 2 - r; x <= N / 2 + r; x++)
                {
                    for (int y = N / 2 - r; y <= N / 2 + r; y++)
                    {
                        if (x < 0 || y < 0 || x >= N || y >= N)
                        {
                            continue;
                        }

                        char t = m[x, y];
                        if (t == 'G' || t == 'P')
                        {
                            return new Vector3Int(x, y, 0);
                        }
                    }
                }
            }

            return new Vector3Int(N / 2, N / 2, 0);
        }

        // --------------------------------------------------------------- streams

        private static readonly Vector2Int[] Step4 =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1),
        };

        // Pond ellipse center used by BuildMap (keep in sync with the pond carve).
        private static readonly Vector2Int PondCenter = new Vector2Int(15, 31);

        /// <summary>Carve three CONTINUOUS one-tile stream ribbons into the char map,
        /// each running unbroken from a coastal source down to the pond. Every ribbon is
        /// laid by A* (see <see cref="AStarStream"/>) so it always reaches the pond —
        /// routing AROUND trees/rocks/meadow/the start clearing rather than dead-ending,
        /// and NEVER erasing them. Grass on the route becomes 'S' (unwalkable stream
        /// water); a path band the route must cross becomes a 'B' bridge (walkable stone
        /// deck OVER the water) — the only crossing a stream is allowed to make. Each
        /// course is ordered coast -> pond and appended to <paramref name="courses"/>, so
        /// the duck spawner floats ducks from the coast down the full length of the stream
        /// into the pond.</summary>
        private static void CarveStreams(char[,] m, RectInt meadow, Vector3Int start,
            List<List<Vector3Int>> courses)
        {
            // North / east / south coasts all feed the single pond (15,31): three
            // continuous ribbons converging like a little delta.
            CarveOne(m, courses, CoastSource(m, 'N', PondCenter.x), PondCenter, meadow, start);
            CarveOne(m, courses, CoastSource(m, 'E', 20), PondCenter, meadow, start);
            CarveOne(m, courses, CoastSource(m, 'S', 20), PondCenter, meadow, start);
        }

        /// <summary>Pick a coastal SOURCE for a stream: scanning inward from the given
        /// edge, the first grass cell sitting right on the coastline (so the ribbon's
        /// mouth is ocean-adjacent and we never have to erase a coastal tree/rock to
        /// start it). Tries a spread of lateral offsets to find grass; falls back to the
        /// first non-ocean cell on the preferred line.</summary>
        private static Vector2Int CoastSource(char[,] m, char edge, int preferredLateral)
        {
            for (int off = 0; off <= 6; off++)
            {
                for (int sign = 0; sign < (off == 0 ? 1 : 2); sign++)
                {
                    int lateral = preferredLateral + (sign == 0 ? off : -off);
                    Vector2Int cell = FirstCoastLand(m, edge, lateral, out char c);
                    if (c == 'G')
                    {
                        return cell;
                    }
                }
            }

            return FirstCoastLand(m, edge, preferredLateral, out _);
        }

        /// <summary>Scan inward from an island edge along a fixed lateral line to the
        /// first land (non-ocean) cell, reporting its char.</summary>
        private static Vector2Int FirstCoastLand(char[,] m, char edge, int lateral, out char c)
        {
            lateral = Mathf.Clamp(lateral, 0, N - 1);
            switch (edge)
            {
                case 'N':
                    for (int y = N - 1; y >= 0; y--)
                    {
                        if (m[lateral, y] != '~') { c = m[lateral, y]; return new Vector2Int(lateral, y); }
                    }
                    break;
                case 'S':
                    for (int y = 0; y < N; y++)
                    {
                        if (m[lateral, y] != '~') { c = m[lateral, y]; return new Vector2Int(lateral, y); }
                    }
                    break;
                case 'E':
                    for (int x = N - 1; x >= 0; x--)
                    {
                        if (m[x, lateral] != '~') { c = m[x, lateral]; return new Vector2Int(x, lateral); }
                    }
                    break;
                case 'W':
                    for (int x = 0; x < N; x++)
                    {
                        if (m[x, lateral] != '~') { c = m[x, lateral]; return new Vector2Int(x, lateral); }
                    }
                    break;
            }

            c = 'G';
            return new Vector2Int(lateral, N / 2);
        }

        /// <summary>Carve one continuous ribbon along the A* route from <paramref name="src"/>
        /// (coast) to the pond. The route is guaranteed 4-connected and unbroken; each cell
        /// is written to the map (grass -> 'S' stream, path -> 'B' bridge; pond/existing
        /// stream cells left as-is) and appended IN ORDER to a new course. Because the
        /// course stores ALL its cells — including any that later become bridges — the
        /// duck's path stays continuous even where the stream is decked over.</summary>
        private static void CarveOne(char[,] m, List<List<Vector3Int>> courses,
            Vector2Int src, Vector2Int goal, RectInt meadow, Vector3Int start)
        {
            List<Vector2Int> route = AStarStream(m, src, goal, meadow, start);
            if (route == null || route.Count == 0)
            {
                return; // no route on the open island (shouldn't happen) — skip, never erase
            }

            var course = new List<Vector3Int>(route.Count);
            foreach (Vector2Int cell in route)
            {
                char c = m[cell.x, cell.y];
                if (c == 'P')
                {
                    m[cell.x, cell.y] = 'B'; // path over water -> walkable stone bridge deck
                }
                else if (c == 'G')
                {
                    m[cell.x, cell.y] = 'S'; // grass -> stream water (unwalkable)
                }
                // 'W' pond mouth, '~', or an existing 'S'/'B' shared with another stream:
                // part of the route, left unchanged.
                course.Add(Cell(cell));
            }

            // Trim unpainted cells (e.g. an ocean-rim source that stayed '~') off both
            // ends so the stored course exactly matches the painted channel — duck
            // drift and the stream tests rely on every course cell being real water.
            bool IsWet(Vector3Int cc)
            {
                char ch = m[cc.x, cc.y];
                return ch == 'S' || ch == 'B' || ch == 'W';
            }

            int first = 0;
            int last = course.Count - 1;
            while (first <= last && !IsWet(course[first]))
            {
                first++;
            }

            while (last >= first && !IsWet(course[last]))
            {
                last--;
            }

            if (last - first + 1 >= 2)
            {
                courses.Add(course.GetRange(first, last - first + 1));
            }
        }

        /// <summary>A* over the char map from a coastal <paramref name="src"/> to the pond
        /// (first 'W' cell reached). Returns the ordered cell route (src .. pond mouth), or
        /// null if unreachable. Steps only into stream-routable cells — grass, path (bridged,
        /// at a stiff cost so crossings stay perpendicular and rare), existing stream/bridge
        /// cells, and pond water — so it flows AROUND trees, rocks, the meadow and the start
        /// clearing instead of dead-ending on them. A tiny per-cell deterministic jitter gives
        /// a natural wobble without ever breaking continuity.</summary>
        private static List<Vector2Int> AStarStream(char[,] m, Vector2Int src, Vector2Int goal,
            RectInt meadow, Vector3Int start)
        {
            var came = new Dictionary<Vector2Int, Vector2Int>();
            var g = new Dictionary<Vector2Int, float> { [src] = 0f };
            var open = new List<Vector2Int> { src };
            var openSet = new HashSet<Vector2Int> { src };

            Vector2Int end = src;
            bool found = false;
            int guard = 0;

            while (open.Count > 0 && guard++ < N * N * 4)
            {
                int bi = 0;
                float bf = float.PositiveInfinity;
                for (int i = 0; i < open.Count; i++)
                {
                    float f = g[open[i]] + Manhattan(open[i], goal);
                    if (f < bf) { bf = f; bi = i; }
                }

                Vector2Int cur = open[bi];
                open.RemoveAt(bi);
                openSet.Remove(cur);

                if (m[cur.x, cur.y] == 'W') // reached the pond — this is the ribbon's mouth
                {
                    end = cur;
                    found = true;
                    break;
                }

                for (int i = 0; i < Step4.Length; i++)
                {
                    Vector2Int nb = cur + Step4[i];
                    if (!RoutableForStream(m, nb, meadow, start))
                    {
                        continue;
                    }

                    float tentative = g[cur] + StreamStepCost(m[nb.x, nb.y], nb);
                    if (!g.TryGetValue(nb, out float gn) || tentative < gn)
                    {
                        g[nb] = tentative;
                        came[nb] = cur;
                        if (openSet.Add(nb))
                        {
                            open.Add(nb);
                        }
                    }
                }
            }

            if (!found)
            {
                return null;
            }

            var route = new List<Vector2Int> { end };
            Vector2Int p = end;
            while (p != src)
            {
                p = came[p];
                route.Add(p);
            }

            route.Reverse();
            return route;
        }

        /// <summary>A cell a stream ribbon may flow into: in bounds, not forbidden (meadow /
        /// start clearing), and grass / path / existing stream-or-bridge / pond water. Trees,
        /// rocks and open ocean are excluded, so the ribbon routes AROUND them.</summary>
        private static bool RoutableForStream(char[,] m, Vector2Int c, RectInt meadow, Vector3Int start)
        {
            if (!InBounds(c) || ForbiddenForStream(c, meadow, start))
            {
                return false;
            }

            char ch = m[c.x, c.y];
            return ch == 'G' || ch == 'P' || ch == 'S' || ch == 'B' || ch == 'W';
        }

        /// <summary>Per-step routing cost: ~1 per grass cell, plus a heavy surcharge for
        /// crossing a path band (keeps bridges few and perpendicular) and a small stable
        /// per-cell jitter so ribbons wander a little instead of running dead straight.</summary>
        private static float StreamStepCost(char ch, Vector2Int c)
        {
            float cost = 1f;
            if (ch == 'P')
            {
                cost += 8f; // discourage path crossings -> minimal bridge count
            }

            uint hsh = (uint)((c.x * 73856093) ^ (c.y * 19349663));
            cost += (hsh % 100) / 111f; // 0 .. ~0.89 deterministic jitter
            return cost;
        }

        private static int Manhattan(Vector2Int a, Vector2Int b) =>
            Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

        private static bool ForbiddenForStream(Vector2Int c, RectInt meadow, Vector3Int start)
        {
            if (meadow.Contains(new Vector2Int(c.x, c.y)))
            {
                return true; // never cut through the dino meadow
            }

            // 3-cell clear ring around the backhoe start so no stream can box it in.
            return Mathf.Max(Mathf.Abs(c.x - start.x), Mathf.Abs(c.y - start.y)) <= 3;
        }

        /// <summary>MANDATORY post-generation connectivity guarantee: BFS-flood walkable
        /// cells from the start; while any walkable cell is unreachable, bridge the
        /// stream cell that separates it (carve an 'S' into a walkable 'B') and re-check.
        /// A pocket walled off by pre-existing pond/obstacles (no stream to bridge over)
        /// is dissolved to rock so the island is guaranteed fully connected.</summary>
        private static void EnsureStreamConnectivity(char[,] m, Vector3Int start,
            List<List<Vector3Int>> courses)
        {
            for (int guard = 0; guard < 512; guard++)
            {
                bool[,] reach = FloodWalkable(m, new Vector2Int(start.x, start.y));

                if (!FindOrphan(m, reach, out Vector2Int orphan))
                {
                    return; // every walkable cell is reachable from the start
                }

                if (FindBridgeableStream(m, reach, out Vector2Int bridge))
                {
                    m[bridge.x, bridge.y] = 'B'; // walkable bridge reconnects the region
                }
                else
                {
                    DissolvePocket(m, orphan); // pre-existing island -> rock (never walkable)
                }
            }
        }

        private static bool[,] FloodWalkable(char[,] m, Vector2Int start)
        {
            var reach = new bool[N, N];
            if (!InBounds(start) || !CharWalkable(m[start.x, start.y]))
            {
                return reach;
            }

            var q = new Queue<Vector2Int>();
            reach[start.x, start.y] = true;
            q.Enqueue(start);
            while (q.Count > 0)
            {
                Vector2Int c = q.Dequeue();
                for (int i = 0; i < Step4.Length; i++)
                {
                    Vector2Int nb = c + Step4[i];
                    if (InBounds(nb) && !reach[nb.x, nb.y] && CharWalkable(m[nb.x, nb.y]))
                    {
                        reach[nb.x, nb.y] = true;
                        q.Enqueue(nb);
                    }
                }
            }

            return reach;
        }

        private static bool FindOrphan(char[,] m, bool[,] reach, out Vector2Int orphan)
        {
            for (int x = 0; x < N; x++)
            {
                for (int y = 0; y < N; y++)
                {
                    if (CharWalkable(m[x, y]) && !reach[x, y])
                    {
                        orphan = new Vector2Int(x, y);
                        return true;
                    }
                }
            }

            orphan = default;
            return false;
        }

        private static bool FindBridgeableStream(char[,] m, bool[,] reach, out Vector2Int bridge)
        {
            for (int x = 0; x < N; x++)
            {
                for (int y = 0; y < N; y++)
                {
                    if (m[x, y] != 'S')
                    {
                        continue;
                    }

                    bool touchReached = false;
                    bool touchOrphan = false;
                    for (int i = 0; i < Step4.Length; i++)
                    {
                        Vector2Int nb = new Vector2Int(x, y) + Step4[i];
                        if (!InBounds(nb) || !CharWalkable(m[nb.x, nb.y]))
                        {
                            continue;
                        }

                        if (reach[nb.x, nb.y])
                        {
                            touchReached = true;
                        }
                        else
                        {
                            touchOrphan = true;
                        }
                    }

                    if (touchReached && touchOrphan)
                    {
                        bridge = new Vector2Int(x, y);
                        return true;
                    }
                }
            }

            bridge = default;
            return false;
        }

        private static void DissolvePocket(char[,] m, Vector2Int seed)
        {
            var q = new Queue<Vector2Int>();
            m[seed.x, seed.y] = 'R';
            q.Enqueue(seed);
            while (q.Count > 0)
            {
                Vector2Int c = q.Dequeue();
                for (int i = 0; i < Step4.Length; i++)
                {
                    Vector2Int nb = c + Step4[i];
                    if (InBounds(nb) && CharWalkable(m[nb.x, nb.y]))
                    {
                        m[nb.x, nb.y] = 'R';
                        q.Enqueue(nb);
                    }
                }
            }
        }

        private static bool CharWalkable(char c) => c == 'G' || c == 'P' || c == 'M' || c == 'B';
        private static bool InBounds(Vector2Int c) => c.x >= 0 && c.x < N && c.y >= 0 && c.y < N;

        private static Vector3Int Cell(Vector2Int c) => new Vector3Int(c.x, c.y, 0);

        // --------------------------------------------------------------- pieces

        private static Tilemap CreateTilemap(GameObject gridGo, string name, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(gridGo.transform, false);
            var tm = go.AddComponent<Tilemap>();
            var tr = go.AddComponent<TilemapRenderer>();
            tr.sortingOrder = sortingOrder;
            tr.mode = TilemapRenderer.Mode.Individual; // sort tiles with sprites for iso depth
            return tm;
        }

        private static DigMound CreateMound(Vector3 pos, Transform parent, PlaceholderLibrary lib)
        {
            var go = new GameObject("DigMound");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 8;
            if (lib != null)
            {
                sr.sprite = lib.MoundSprite;
            }

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.7f;   // generous touch target
            col.isTrigger = true;

            ParticleSystem sparkle = CreateParticle(go.transform, "Sparkle",
                lib != null ? lib.StarParticle : null, new Color(1f, 0.95f, 0.5f), true);

            var mound = go.AddComponent<DigMound>();
            Wire(mound, "_renderer", sr);
            Wire(mound, "_sparkle", sparkle);
            return mound;
        }

        // --------------------------------------------------------------- meadow

        private const string FenceSpriteDir = "Assets/Art/Kenney/IsometricMiniatureFarm/Isometric";

        /// <summary>Build the fenced dino meadow: a MeadowArea component holding
        /// the cell rect, plus a decorative ring of Kenney farm fence sprites with
        /// a 2-cell gate opening on the SOUTH edge. No colliders — residents are
        /// kept inside by their own wander logic. Fully null-tolerant: a missing
        /// fence PNG just skips that visual, the area still works.</summary>
        private static MeadowArea CreateMeadow(Grid grid, OverworldMap map, Transform parent,
            RectInt rect, PlaceholderLibrary lib, out NestController nest)
        {
            var go = new GameObject("Meadow");
            go.transform.SetParent(parent, false);

            int minX = rect.xMin;
            int minY = rect.yMin;
            int maxX = rect.xMax - 1;
            int maxY = rect.yMax - 1;

            go.transform.position = grid.GetCellCenterWorld(
                new Vector3Int((minX + maxX) / 2, (minY + maxY) / 2, 0));

            // Gate: 2 adjacent openings centered on the south edge.
            int gateA = minX + MeadowSize / 2 - 1;
            int gateB = gateA + 1;
            Vector3 gateWorld = (grid.GetCellCenterWorld(new Vector3Int(gateA, minY, 0)) +
                                 grid.GetCellCenterWorld(new Vector3Int(gateB, minY, 0))) * 0.5f;

            var area = go.AddComponent<MeadowArea>();
            area.Configure(map, minX, minY, maxX, maxY, gateWorld);

            // Fence orientation: grid +x runs screen NE, grid +y runs screen NW,
            // so edges along X and along Y need the two different Kenney rotations.
            Sprite alongX = LoadFenceSprite("fenceLow_E");
            Sprite alongY = LoadFenceSprite("fenceLow_N");
            if (alongX == null)
            {
                alongX = alongY;
            }

            if (alongY == null)
            {
                alongY = alongX;
            }

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    bool onEdge = x == minX || x == maxX || y == minY || y == maxY;
                    if (!onEdge)
                    {
                        continue;
                    }

                    if (y == minY && (x == gateA || x == gateB))
                    {
                        continue; // the south gate opening
                    }

                    bool xEdge = y == minY || y == maxY;
                    Sprite s = xEdge ? alongX : alongY;
                    if (s != null)
                    {
                        CreateFencePiece(grid, go.transform, new Vector3Int(x, y, 0), s);
                    }
                }
            }

            // Egg-shard nest: a unique prop at the NE corner cell of the interior (one
            // cell inside the fence ring). Matches MeadowArea.NestWorld exactly so the
            // shard fly-to target and the runtime nest position agree.
            var nestCell = new Vector3Int(maxX - 1, maxY - 1, 0);
            nest = CreateNest(grid, go.transform, nestCell, lib);

            return area;
        }

        /// <summary>Build the egg-shard nest prop: a twig-ring base with the assembling
        /// egg sitting in it, plus an arrival/hatch sparkle. Null-tolerant (a missing
        /// sprite just leaves that renderer blank; NestController still works).</summary>
        private static NestController CreateNest(Grid grid, Transform parent, Vector3Int cell,
            PlaceholderLibrary lib)
        {
            var go = new GameObject("Nest");
            go.transform.SetParent(parent, false);
            go.transform.position = grid.GetCellCenterWorld(cell);
            go.transform.localScale = Vector3.one * 0.85f; // tuck it into the cell

            // Base bowl: above ground + fence (0/9), below the backhoe/dinos (12/15) so
            // actors and the ceremony baby always render in front of it.
            var baseSr = MakeChildRendererLocalAt(go.transform, "NestBase",
                lib != null ? lib.NestSprite : null, 10, Vector3.zero);

            // Egg nestled in the bowl (slightly up so it reads as sitting inside).
            var eggSr = MakeChildRendererLocalAt(go.transform, "NestEgg",
                lib != null ? SafeSprite(lib.EggAssemblySprites, 0) : null, 11,
                new Vector3(0f, 0.2f, 0f));

            ParticleSystem sparkle = CreateParticle(go.transform, "Sparkle",
                lib != null ? lib.StarParticle : null, new Color(1f, 0.95f, 0.6f), false);

            var nest = go.AddComponent<NestController>();
            Wire(nest, "_base", baseSr);
            Wire(nest, "_egg", eggSr);
            Wire(nest, "_sparkle", sparkle);
            Wire(nest, "_library", lib);
            return nest;
        }

        // SpriteRenderer child at a given LOCAL position (props whose parent carries the
        // world placement + scale, e.g. the nest bowl + egg).
        private static SpriteRenderer MakeChildRendererLocalAt(Transform parent, string name,
            Sprite sprite, int order, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = order;
            sr.sprite = sprite;
            return sr;
        }

        /// <summary>Load a generated duck frame, configuring its TextureImporter (Sprite
        /// type + a PPU that renders it at <paramref name="targetH"/> world units) the
        /// first time. GeneratedArtImporter owns every other actor's import; the ducks
        /// were added after it, so SceneBuilder configures them here. Null-tolerant.</summary>
        private static Sprite LoadDuckSprite(string name, float targetH)
        {
            string path = $"{DuckDir}/{name}.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[SceneBuilder] Duck sprite missing at {path} — ducks will be invisible " +
                                 "(run Tools/generate_ducks.py).");
                return null;
            }

            importer.GetSourceTextureWidthAndHeight(out int _, out int h);
            float ppu = h > 0 ? h / targetH : 100f;

            bool changed = false;
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                changed = true;
            }

            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                changed = true;
            }

            if (!Mathf.Approximately(importer.spritePixelsPerUnit, ppu))
            {
                importer.spritePixelsPerUnit = ppu;
                changed = true;
            }

            if (!importer.alphaIsTransparency)
            {
                importer.alphaIsTransparency = true;
                changed = true;
            }

            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                changed = true;
            }

            if (changed)
            {
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static Sprite LoadFenceSprite(string name)
        {
            string path = $"{FenceSpriteDir}/{name}.png";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
            {
                Debug.LogWarning($"[SceneBuilder] Meadow fence sprite missing at {path} — ring piece skipped.");
            }

            return sprite;
        }

        private static void CreateFencePiece(Grid grid, Transform parent, Vector3Int cell, Sprite sprite)
        {
            var go = new GameObject($"Fence_{cell.x}_{cell.y}");
            go.transform.SetParent(parent, false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            // Above ground/mounds (0/8), below the backhoe (12) and dinos (15):
            // the fence stays a backdrop the actors always walk in front of.
            sr.sortingOrder = 9;

            // Scale the piece to one cell's width, then sit its bottom edge on the
            // cell diamond's bounding-box bottom (Kenney pieces pivot centered).
            float w = sprite.bounds.size.x;
            float h = sprite.bounds.size.y;
            float k = w > 0.0001f ? 1.0f / w : 1f;
            go.transform.localScale = new Vector3(k, k, 1f);

            Vector3 c = grid.GetCellCenterWorld(cell);
            go.transform.position = c + new Vector3(0f, h * k * 0.5f - 0.25f, 0f);
        }

        private static SpriteRenderer MakeChildRenderer(Transform parent, string name, Sprite sprite,
            int order, Vector3 worldPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = order;
            sr.sprite = sprite;
            return sr;
        }

        // Empty transform used as an IK joint node (local-space positioned).
        private static Transform MakeChildNode(Transform parent, string name, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go.transform;
        }

        // SpriteRenderer child positioned at its parent joint's origin (local zero).
        private static SpriteRenderer MakeChildRendererLocal(Transform parent, string name,
            Sprite sprite, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = order;
            sr.sprite = sprite;
            return sr;
        }

        // Editor-preview mounting for an anatomical rig segment: UNIFORM scale so
        // the drawn base-pin -> tip-pin distance equals the bone length, rotated
        // so the pin line lies on the bone's +x axis, base pin on the joint —
        // mirrors DigModeController.AssignSegmentPins (authoritative at runtime;
        // pin constants measured from the art, keep in sync). When `generated` is
        // false (placeholder square fallback) draws a plain thin bar instead.
        private static void PreviewSegment(SpriteRenderer sr, bool generated, float length,
            float thickness, Vector2 baseNorm, Vector2 tipNorm)
        {
            if (sr == null || sr.sprite == null)
            {
                return;
            }

            Sprite s = sr.sprite;
            sr.drawMode = SpriteDrawMode.Simple;

            if (!generated)
            {
                float w = s.bounds.size.x;
                float h = s.bounds.size.y;
                if (w <= 0.0001f || h <= 0.0001f)
                {
                    return;
                }

                sr.transform.localScale = new Vector3(length / w, thickness / h, 1f);
                float pnx = s.rect.width > 0f ? s.pivot.x / s.rect.width : 0f;
                float pny = s.rect.height > 0f ? s.pivot.y / s.rect.height : 0.5f;
                sr.transform.localPosition = new Vector3(pnx * length, (pny - 0.5f) * thickness, 0f);
                return;
            }

            float ppu = s.pixelsPerUnit;
            Rect r = s.rect;
            if (ppu <= 0.0001f || r.width <= 0f || r.height <= 0f)
            {
                return;
            }

            Vector2 basePin = (new Vector2(baseNorm.x * r.width, baseNorm.y * r.height) - s.pivot) / ppu;
            Vector2 tipPin = (new Vector2(tipNorm.x * r.width, tipNorm.y * r.height) - s.pivot) / ppu;
            Vector2 v = tipPin - basePin;
            if (v.magnitude <= 0.0001f)
            {
                return;
            }

            float scale = length / v.magnitude;
            float phiDeg = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            sr.transform.localRotation = Quaternion.Euler(0f, 0f, -phiDeg);
            sr.transform.localScale = new Vector3(scale, scale, 1f);
            float c = Mathf.Cos(-phiDeg * Mathf.Deg2Rad);
            float sn = Mathf.Sin(-phiDeg * Mathf.Deg2Rad);
            Vector2 rot = new Vector2(c * basePin.x - sn * basePin.y, sn * basePin.x + c * basePin.y);
            sr.transform.localPosition = new Vector3(-rot.x * scale, -rot.y * scale, 0f);
        }

        // Editor-preview sizing for the bucket: uniform, by target height.
        private static void PreviewScaleUniformByHeight(SpriteRenderer sr, float height)
        {
            if (sr == null || sr.sprite == null)
            {
                return;
            }

            float h = sr.sprite.bounds.size.y;
            if (h <= 0.0001f)
            {
                return;
            }

            float k = height / h;
            sr.transform.localScale = new Vector3(k, k, 1f);
        }

        private static ParticleSystem CreateParticle(Transform parent, string name, Sprite sprite,
            Color color, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.playOnAwake = loop;
            main.loop = loop;
            main.startLifetime = 0.8f;
            main.startSpeed = loop ? 0.4f : 2.5f;
            main.startSize = 0.3f;
            main.gravityModifier = loop ? 0f : 0.5f;
            main.startColor = color;
            main.maxParticles = 128;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = loop ? 3f : 0f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.25f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                Shader sh = Shader.Find("Sprites/Default");
                if (sh != null)
                {
                    var mat = new Material(sh);
                    if (sprite != null && sprite.texture != null)
                    {
                        mat.mainTexture = sprite.texture;
                    }

                    renderer.material = mat;
                }

                renderer.sortingOrder = 60;
            }

            return ps;
        }

        // ------------------------------------------------------------------ UI

        private static void BuildUI(PlaceholderLibrary lib, out TreasureCounter counter, out MuteButton mute)
        {
            var canvasGo = new GameObject("Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            Font font = null;
            try
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch
            {
                // older name fallback
                try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
                catch { font = null; }
            }

            // Treasure counter (top-right).
            var counterGo = new GameObject("TreasureCounter");
            counterGo.transform.SetParent(canvasGo.transform, false);
            var counterRt = counterGo.AddComponent<RectTransform>();
            counterRt.anchorMin = counterRt.anchorMax = new Vector2(1f, 1f);
            counterRt.pivot = new Vector2(1f, 1f);
            counterRt.anchoredPosition = new Vector2(-30f, -30f);
            counterRt.sizeDelta = new Vector2(220f, 140f);
            counter = counterGo.AddComponent<TreasureCounter>();

            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(counterGo.transform, false);
            var iconRt = iconGo.AddComponent<RectTransform>();
            iconRt.anchorMin = iconRt.anchorMax = new Vector2(1f, 1f);
            iconRt.pivot = new Vector2(1f, 1f);
            iconRt.anchoredPosition = new Vector2(0f, 0f);
            iconRt.sizeDelta = new Vector2(120f, 120f);
            var iconImg = iconGo.AddComponent<Image>();
            if (lib != null)
            {
                iconImg.sprite = lib.TreasureIcon;
            }

            var textGo = new GameObject("Count");
            textGo.transform.SetParent(counterGo.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = textRt.anchorMax = new Vector2(1f, 1f);
            textRt.pivot = new Vector2(1f, 1f);
            textRt.anchoredPosition = new Vector2(-130f, -10f);
            textRt.sizeDelta = new Vector2(90f, 100f);
            var text = textGo.AddComponent<Text>();
            text.font = font;
            text.fontSize = 64;
            text.alignment = TextAnchor.MiddleRight;
            text.color = new Color(0.25f, 0.18f, 0.05f);
            text.text = "0";

            Wire(counter, "_icon", iconImg);
            Wire(counter, "_countText", text);
            Wire(counter, "_iconRect", iconRt);

            // Mute button (top-left) — parent-gated hold.
            var muteGo = new GameObject("MuteButton");
            muteGo.transform.SetParent(canvasGo.transform, false);
            var muteRt = muteGo.AddComponent<RectTransform>();
            muteRt.anchorMin = muteRt.anchorMax = new Vector2(0f, 1f);
            muteRt.pivot = new Vector2(0f, 1f);
            muteRt.anchoredPosition = new Vector2(30f, -30f);
            muteRt.sizeDelta = new Vector2(120f, 120f);
            var muteImg = muteGo.AddComponent<Image>();
            if (lib != null)
            {
                muteImg.sprite = lib.SoundIcon;
            }

            mute = muteGo.AddComponent<MuteButton>();

            var fillGo = new GameObject("HoldFill");
            fillGo.transform.SetParent(muteGo.transform, false);
            var fillRt = fillGo.AddComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            var fillImg = fillGo.AddComponent<Image>();
            fillImg.color = new Color(1f, 1f, 1f, 0.35f);
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Radial360;
            fillImg.fillAmount = 0f;
            if (lib != null)
            {
                fillImg.sprite = lib.SoundIcon;
            }

            Wire(mute, "_iconImage", muteImg);
            Wire(mute, "_holdFill", fillImg);
            Wire(mute, "_soundSprite", lib != null ? lib.SoundIcon : null);
            Wire(mute, "_muteSprite", lib != null ? lib.MuteIcon : null);
        }

        // -------------------------------------------------------------- wiring

        private static void Wire(Object comp, string prop, Object value)
        {
            if (comp == null || string.IsNullOrEmpty(prop))
            {
                return;
            }

            var so = new SerializedObject(comp);
            SerializedProperty p = so.FindProperty(prop);
            if (p != null)
            {
                p.objectReferenceValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning($"[SceneBuilder] Missing serialized field '{prop}' on {comp.GetType().Name}");
            }
        }

        private static void WireArray(Object comp, string prop, Sprite[] values)
        {
            if (comp == null || values == null)
            {
                return;
            }

            var so = new SerializedObject(comp);
            SerializedProperty p = so.FindProperty(prop);
            if (p == null)
            {
                return;
            }

            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WireMoundList(Object comp, string prop, List<DigMound> values)
        {
            var so = new SerializedObject(comp);
            SerializedProperty p = so.FindProperty(prop);
            if (p == null)
            {
                return;
            }

            p.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++)
            {
                p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Sprite SafeSprite(Sprite[] arr, int i)
        {
            if (arr == null || arr.Length == 0)
            {
                return null;
            }

            return arr[Mathf.Clamp(i, 0, arr.Length - 1)];
        }

        private static void AddSceneToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (EditorBuildSettingsScene s in scenes)
            {
                if (s.path == path)
                {
                    s.enabled = true;
                    EditorBuildSettings.scenes = scenes.ToArray();
                    return;
                }
            }

            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
