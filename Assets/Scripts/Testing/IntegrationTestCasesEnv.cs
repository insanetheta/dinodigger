using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Tilemaps;
using DinoDigger.Config;
using DinoDigger.Core;
using DinoDigger.Overworld;

namespace DinoDigger.Testing
{
    /// <summary>
    /// Environment-dressing integration case (DinoDigger-y1g): proves the Jurassic-earth
    /// env set is actually WIRED INTO THE BUILT ISLAND, and — just as importantly — that
    /// wiring it changed nothing a child can walk into.
    ///
    /// The interaction cases in IntegrationTestCases.cs are the other half of the contract
    /// and are deliberately untouched: BrachioTreeShake / AnkyRockSmash find their targets
    /// by comparing the obstacle tile against <c>lib.TreeTile</c> / <c>lib.RockTile</c>, so
    /// the env pass restyles the SPRITE INSIDE those Tile assets and never introduces new
    /// tile references. StreamsConnectivity / PathfindingAnywhere / TownAvoidsMoundAndStream
    /// read walkability, which OverworldMap computes from tile PRESENCE — swapping one
    /// walkable tile asset for another walkable tile asset cannot move a cell.
    ///
    /// See IntegrationTestRunner.cs for the driver.
    /// </summary>
    public partial class IntegrationTestRunner
    {
        // Where the art lives on disk. Used ONLY to tell two very different states apart:
        //   art absent   -> this checkout never generated it; the flat-tile fallback is the
        //                   correct behaviour and the case passes with a note.
        //   art present  -> it must be imported and painted; anything less is the exact
        //                   integration gap this ticket closed, so the case fails loudly.
        private const string EnvProbeRel = "Art/Generated/env/ground/tile_grass_00.png";

        private static string EnvProbePng =>
            Path.Combine(Application.dataPath, EnvProbeRel);

        // World footprints every env prop MUST preserve, because a collider/cell pitch was
        // tuned against them (see the mapping table in GeneratedArtImporter). Checked
        // against the actual imported sprite bounds — this is the assertion that would fire
        // if someone re-baked the art at a new size or slipped a wrong PPU into the importer.
        private const float EnvSizeTol = 0.02f;

        private IEnumerator Case_EnvDressingApplied(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            OverworldMap map = gm.TestMap;
            PlaceholderLibrary lib = gm.TestLibrary;
            GameConfig config = gm.TestConfig;
            ctx.Assert(map != null && lib != null, "missing overworld map / placeholder library");

            bool artOnDisk = File.Exists(EnvProbePng);
            if (!lib.HasEnvGround)
            {
                ctx.Assert(!artOnDisk,
                    $"the env art is on disk ({EnvProbePng}) but the library carries no env " +
                    "ground tiles — run DinoDigger/Import Generated Art, then " +
                    "DinoDigger/Build Main Scene");

                // Legitimate no-art checkout: prove the fallback held rather than skipping.
                int flat = 0;
                for (int x = 0; x < MapCells; x++)
                {
                    for (int y = 0; y < MapCells; y++)
                    {
                        if (map.TestGroundTile(new Vector3Int(x, y, 0)) == lib.GrassTile)
                        {
                            flat++;
                        }
                    }
                }

                ctx.Assert(flat > 100,
                    $"no env art AND no flat grass painted ({flat} cells) — the island is bare");
                ctx.Log($"env set not present in this checkout: island correctly falls back to " +
                        $"the flat placeholder tiles ({flat} flat grass cells)");
                yield break;
            }

            // ---------------------------------------------------------------- 1) variety
            // A 48x48 island painted from one stamp is exactly what the env set exists to
            // fix, so count the DISTINCT SPRITES actually rendered on plain grass.
            var grassSprites = new HashSet<Sprite>();
            var grassTiles = new HashSet<TileBase>();
            var pathTiles = new HashSet<TileBase>();
            var waterTiles = new HashSet<TileBase>();
            var bedTiles = new HashSet<TileBase>();
            int pathEdges = 0, waterEdges = 0, bedEdges = 0;

            for (int x = 0; x < MapCells; x++)
            {
                for (int y = 0; y < MapCells; y++)
                {
                    var cell = new Vector3Int(x, y, 0);
                    TileBase g = map.TestGroundTile(cell);
                    TileBase w = map.TestWaterTile(cell);

                    if (g != null && lib.GrassTiles.Contains(g))
                    {
                        grassTiles.Add(g);
                        Sprite s = map.TestGroundSprite(cell);
                        if (s != null)
                        {
                            grassSprites.Add(s);
                        }
                    }

                    if (g != null && lib.PathTiles.Contains(g)) { pathTiles.Add(g); }
                    if (g != null && lib.BedTiles.Contains(g)) { bedTiles.Add(g); }
                    if (w != null && lib.WaterTiles.Contains(w)) { waterTiles.Add(w); }

                    if (g != null && lib.GrassPathEdges.Contains(g)) { pathEdges++; }
                    if (g != null && lib.GrassWaterEdges.Contains(g)) { waterEdges++; }
                    if (g != null && lib.GrassBedEdges.Contains(g)) { bedEdges++; }
                }
            }

            ctx.Assert(grassSprites.Count > 1,
                $"the island paints only {grassSprites.Count} distinct grass sprite(s) — the " +
                "variant pass is not running (one stamp over 48x48 is the bug this fixes)");
            ctx.Assert(grassTiles.Count >= 4,
                $"only {grassTiles.Count}/{lib.GrassTiles.Count} grass variants ever got " +
                "painted — the per-cell hash is not spreading over the set");
            ctx.Assert(pathTiles.Count > 1,
                $"the path paints only {pathTiles.Count} distinct variant(s)");
            ctx.Assert(waterTiles.Count > 1,
                $"the water paints only {waterTiles.Count} distinct variant(s)");

            // ------------------------------------------------------------- 2) transitions
            ctx.Assert(pathEdges > 0,
                "no grass->path transition tiles painted — grass and path still butt up " +
                "against each other with a hard seam");
            ctx.Assert(waterEdges > 0,
                "no grass->water shoreline tiles painted — the pond/stream banks are hard cuts");
            ctx.Assert(bedTiles.Count > 0,
                "the Berry Patch plot is not painted with the tilled garden-bed ground");
            ctx.Assert(bedEdges > 0,
                "no grass->bed transition tiles around the Berry Patch plot");

            // Every transition tile must carry the RIGHT mask for its neighbours, in the
            // baked art's bit order (bit0 -Y, bit1 +X, bit2 +Y, bit3 -X). A rotation bug
            // here paints a shoreline melting in from the wrong side — subtle on screen,
            // obvious to this check.
            int checkedEdges = 0;
            for (int x = 0; x < MapCells; x++)
            {
                for (int y = 0; y < MapCells; y++)
                {
                    var cell = new Vector3Int(x, y, 0);
                    TileBase g = map.TestGroundTile(cell);
                    if (g == null || EnvBiomeAt(map, lib, cell) != EnvBiome.Grass)
                    {
                        continue;
                    }

                    EnvEdgeSet family = lib.GrassPathEdges.Contains(g) ? lib.GrassPathEdges
                        : lib.GrassWaterEdges.Contains(g) ? lib.GrassWaterEdges
                        : lib.GrassBedEdges.Contains(g) ? lib.GrassBedEdges : null;
                    if (family == null)
                    {
                        continue; // a plain variant, checked by the derivation pass below
                    }

                    int mask = EnvDressing.EdgeMask(cell, c => EnvBiomeAt(map, lib, c), out _);
                    ctx.Assert(family.Edge(mask) == g,
                        $"cell {cell} carries a transition tile that does not match its " +
                        $"neighbour mask {mask} — the edge orientation mapping is wrong");
                    checkedEdges++;
                }
            }

            // ------------------------------------------------- 3) determinism / rebuild
            // Every painted ground/water tile must equal the library's PURE derivation for
            // that cell. Because the derivation is a hash of the cell coordinate and nothing
            // else — no System.Random, no build order — this is the "same cell, same variant
            // across two scene builds" guarantee: a rebuild feeds the same map into the same
            // function and can only produce the same island. The second evaluation below
            // proves the function itself is stable rather than memoised off the paint.
            int derived = 0, mismatched = 0;
            Vector3Int firstBad = Vector3Int.zero;
            for (int x = 0; x < MapCells; x++)
            {
                for (int y = 0; y < MapCells; y++)
                {
                    var cell = new Vector3Int(x, y, 0);
                    EnvBiome biome = EnvBiomeAt(map, lib, cell);
                    if (biome == EnvBiome.None || IsEnvBridgeCell(map, lib, cell))
                    {
                        continue; // ocean, an un-dressed fallback cell, or a bridge deck
                    }

                    TileBase expected = lib.GroundTileFor(cell, biome, c => EnvBiomeAt(map, lib, c));
                    TileBase again = lib.GroundTileFor(cell, biome, c => EnvBiomeAt(map, lib, c));
                    if (expected != again)
                    {
                        ctx.Assert(false,
                            $"GroundTileFor({cell}) answered differently on two calls — the " +
                            "dressing is not deterministic and a rebuild would repaint the island");
                    }

                    TileBase painted = biome == EnvBiome.Water
                        ? map.TestWaterTile(cell)
                        : map.TestGroundTile(cell);
                    derived++;
                    if (painted != expected)
                    {
                        if (mismatched == 0)
                        {
                            firstBad = cell;
                        }

                        mismatched++;
                    }
                }
            }

            ctx.Assert(derived > 500,
                $"only {derived} cells could be re-derived — the island is barely dressed");
            ctx.Assert(mismatched == 0,
                $"{mismatched} cell(s) are painted with something other than the library's " +
                $"deterministic derivation (first: {firstBad}) — a rebuild would NOT " +
                "reproduce this island");

            // ------------------------------------------------------------------ 4) decals
            ctx.Assert(map.TestHasDecalLayer,
                "the scene has no decal layer wired (rebuild via DinoDigger/Build Main Scene)");

            float accentShare = config != null ? config.EnvAccentShare
                                               : EnvDressing.DefaultAccentShare;
            int decalCount = 0, offGrammar = 0, nonDeterministic = 0;
            var decalBiomes = new HashSet<EnvBiome>();
            for (int x = 0; x < MapCells; x++)
            {
                for (int y = 0; y < MapCells; y++)
                {
                    var cell = new Vector3Int(x, y, 0);
                    TileBase d = map.TestDecalTile(cell);
                    if (d == null)
                    {
                        continue;
                    }

                    decalCount++;
                    EnvBiome biome = EnvBiomeAt(map, lib, cell);
                    decalBiomes.Add(biome);

                    // GRAMMAR (style rule 4): a decal may only appear on the biome its
                    // bucket belongs to. A fern on the path or a lily on the grass is the
                    // failure this guards.
                    EnvTileSet bucket = lib.DecalSet(biome);
                    bool legal = bucket != null && bucket.Contains(d);
                    if (!legal && biome == EnvBiome.Path && lib.AccentDecals != null)
                    {
                        legal = lib.AccentDecals.Contains(d); // the rare warm-stone accent
                    }

                    if (!legal)
                    {
                        offGrammar++;
                        if (offGrammar == 1)
                        {
                            ctx.Assert(false,
                                $"decal '{d.name}' at {cell} is not legal on biome {biome} — " +
                                "the scatter escaped its grammar bucket");
                        }
                    }

                    // Same determinism contract as the ground: a painted decal must equal
                    // the pure derivation. (Only this direction: the builder additionally
                    // SKIPS mound cells, reserved plots and prop cells, so a derived decal
                    // may legitimately be absent.)
                    float chance = config != null ? config.EnvDecalChance(biome)
                                                  : DefaultEnvDecalChance(biome);
                    if (lib.DecalTileFor(cell, biome, chance, accentShare) != d)
                    {
                        nonDeterministic++;
                    }
                }
            }

            ctx.Assert(decalCount > 0,
                "no decals scattered at all — the ferns/footprints/lilies layer is missing");
            ctx.Assert(nonDeterministic == 0,
                $"{nonDeterministic} decal(s) do not match the deterministic derivation");
            ctx.Assert(decalBiomes.Contains(EnvBiome.Grass),
                "no decals landed on grass");
            // Sparse, not a carpet: the whole island is ~1800 land cells and the grammar
            // densities are 0.20/0.34/0.22, so anything approaching every cell means the
            // density config was bypassed.
            ctx.Assert(decalCount < MapCells * MapCells / 3,
                $"{decalCount} decals is a carpet, not a scatter — style rule 4 wants clusters");

            // ------------------------------------------------------- 5) props + footprints
            // The prop swap must be ART ONLY. Two things prove that: the Tile REFERENCES
            // are the same ones the tap router compares against (so tree/rock interactions
            // are structurally untouched), and every sprite occupies the SAME world
            // footprint the placeholder did (so no collider, cell pitch or spawn rect
            // needed retuning).
            var tree = lib.TreeTile as Tile;
            var rock = lib.RockTile as Tile;
            ctx.Assert(tree != null && rock != null,
                "the tree/rock tiles are not plain Tile assets any more — the tap router " +
                "compares against these references");
            ctx.Assert(EnvSpriteIn(lib.TreeSprites, tree.sprite),
                "TreeTile does not render one of the imported env tree sprites");
            ctx.Assert(EnvSpriteIn(lib.RockSprites, rock.sprite),
                "RockTile does not render one of the imported env rock sprites");
            ctx.Assert(lib.MoundSprite != null && lib.MoundSprite.name == "mound",
                $"the overworld mound still renders '{(lib.MoundSprite != null ? lib.MoundSprite.name : "null")}' " +
                "instead of the env mound art");
            ctx.Assert(lib.NestSprite != null && lib.NestSprite.name == "nest",
                $"the meadow nest still renders '{(lib.NestSprite != null ? lib.NestSprite.name : "null")}' " +
                "instead of the env nest art");

            AssertEnvFootprint(ctx, tree.sprite, 1.00f, 1.00f, "tree tile");
            AssertEnvFootprint(ctx, rock.sprite, 1.00f, 1.00f, "rock tile");
            AssertEnvFootprint(ctx, lib.MoundSprite, 1.00f, 0.50f, "mound prop");
            AssertEnvFootprint(ctx, lib.NestSprite, 1.28f, 1.28f, "nest prop");
            if (lib.BridgeSprites != null && lib.BridgeSprites.Length > 0)
            {
                AssertEnvFootprint(ctx, lib.BridgeSprites[0], 1.00f, 0.50f, "bridge deck");
            }

            AssertEnvFootprint(ctx, lib.FenceAlongX, 2.56f, 5.12f, "fence (along X)");
            AssertEnvFootprint(ctx, lib.FenceAlongY, 2.56f, 5.12f, "fence (along Y)");

            // The mound colliders themselves: same generous 0.7 touch radius as before, and
            // still centred on the (unchanged) sprite.
            int mounds = 0;
            foreach (DigMound dm in gm.TestMounds)
            {
                if (dm == null)
                {
                    continue;
                }

                var col = dm.GetComponent<CircleCollider2D>();
                ctx.Assert(col != null && Mathf.Abs(col.radius - 0.7f) < 0.001f,
                    "a dig mound's tap collider changed with the art swap");
                mounds++;
            }

            ctx.Log($"env dressing live: {grassSprites.Count} distinct grass sprites over " +
                    $"{grassTiles.Count}/{lib.GrassTiles.Count} variants, path {pathTiles.Count}, " +
                    $"water {waterTiles.Count}, bed {bedTiles.Count}; transitions " +
                    $"path {pathEdges} / water {waterEdges} / bed {bedEdges} " +
                    $"({checkedEdges} mask-checked); {derived} cells re-derived with 0 mismatches; " +
                    $"{decalCount} decals, all in grammar and deterministic; props restyled in " +
                    $"place with unchanged footprints across {mounds} mound colliders");
            yield break;
        }

        /// <summary>
        /// Classify a cell's ground family FROM THE PAINTED SCENE (never from SceneBuilder's
        /// char map, which is editor-only and long gone by the time this runs). Membership
        /// of the library's typed sets is what identifies each family; bridge decks read as
        /// water because that is how the grass beside them was edged.
        /// </summary>
        private static EnvBiome EnvBiomeAt(OverworldMap map, PlaceholderLibrary lib, Vector3Int cell)
        {
            TileBase w = map.TestWaterTile(cell);
            if (w != null)
            {
                return EnvBiome.Water;
            }

            TileBase g = map.TestGroundTile(cell);
            if (g == null)
            {
                return EnvBiome.None; // open ocean
            }

            if (IsEnvBridgeTile(lib, g))
            {
                return EnvBiome.Water; // a deck over a channel
            }

            if (lib.PathTiles.Contains(g))
            {
                return EnvBiome.Path;
            }

            if (lib.BedTiles.Contains(g))
            {
                return EnvBiome.Bed;
            }

            if (lib.GrassTiles.Contains(g) || lib.GrassPathEdges.Contains(g) ||
                lib.GrassWaterEdges.Contains(g) || lib.GrassBedEdges.Contains(g))
            {
                return EnvBiome.Grass;
            }

            return EnvBiome.None; // a flat fallback tile: nothing to verify here
        }

        private static bool IsEnvBridgeTile(PlaceholderLibrary lib, TileBase t)
        {
            return t != null &&
                   (t == lib.BridgeTile ||
                    (lib.BridgeTiles != null && lib.BridgeTiles.Contains(t)));
        }

        private static bool IsEnvBridgeCell(OverworldMap map, PlaceholderLibrary lib, Vector3Int cell)
        {
            return IsEnvBridgeTile(lib, map.TestGroundTile(cell));
        }

        private static float DefaultEnvDecalChance(EnvBiome biome)
        {
            switch (biome)
            {
                case EnvBiome.Grass: return EnvDressing.DefaultGrassDecalChance;
                case EnvBiome.Path: return EnvDressing.DefaultPathDecalChance;
                case EnvBiome.Water: return EnvDressing.DefaultWaterDecalChance;
                default: return 0f;
            }
        }

        private static bool EnvSpriteIn(Sprite[] set, Sprite s)
        {
            if (set == null || s == null)
            {
                return false;
            }

            for (int i = 0; i < set.Length; i++)
            {
                if (set[i] == s)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Assert an env sprite occupies exactly the world footprint the placeholder it
        /// replaced did — the single number every collider, cell pitch and spawn rect was
        /// tuned against, and the one a wrong PPU in the importer would silently break.
        ///
        /// Measured as rect / pixelsPerUnit rather than <c>sprite.bounds</c> on purpose:
        /// that is precisely the pair the importer sets (source pixels, chosen PPU), it is
        /// unambiguous for a Single-mode sprite, and it does not depend on whether the mesh
        /// type trims transparent margins out of the bounds.
        /// </summary>
        private void AssertEnvFootprint(TestContext ctx, Sprite s, float w, float h, string what)
        {
            if (s == null)
            {
                return; // absent art is a legal, already-reported state
            }

            float ppu = s.pixelsPerUnit;
            ctx.Assert(ppu > 0.0001f, $"{what} imported with a nonsense PPU ({ppu})");
            float sx = s.rect.width / ppu;
            float sy = s.rect.height / ppu;
            ctx.Assert(Mathf.Abs(sx - w) < EnvSizeTol && Mathf.Abs(sy - h) < EnvSizeTol,
                $"{what} imports at {sx:0.###} x {sy:0.###} world units (PPU {ppu:0.#}), " +
                $"expected {w:0.##} x {h:0.##} — the footprint changed, so its collider, " +
                "cell pitch or spawn rect no longer fits");
        }
    }
}
