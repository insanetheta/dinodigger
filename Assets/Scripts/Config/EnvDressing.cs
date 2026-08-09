using System;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DinoDigger.Config
{
    /// <summary>
    /// Which ground family a map cell belongs to, for the environment dressing pass.
    /// The Jurassic-earth art set (DinoDigger-c7m) ships base variants for Grass/Path/
    /// Water/Bed and grass-to-X transition tiles for Path/Water/Bed, so those four are
    /// the only biomes the dressing needs to distinguish.
    /// </summary>
    public enum EnvBiome
    {
        /// <summary>No ground of interest (open ocean, or a cell the dressing skips).</summary>
        None = 0,
        Grass = 1,
        Path = 2,
        Water = 3,
        /// <summary>Tilled berry-garden bed — the Berry Patch plot's ground.</summary>
        Bed = 4,
    }

    /// <summary>
    /// One biome's interchangeable ground-tile variants (or one decal bucket's tiles).
    ///
    /// The 4x4-sliced plates give 16 mottle variants per biome (4 for the garden bed);
    /// painting a deterministic pick per cell is what stops a 48x48 island reading as
    /// one repeated stamp. Every slot is OPTIONAL: an un-imported set leaves
    /// <see cref="Variants"/> empty (or full of nulls) and the caller falls back to the
    /// flat placeholder tile, so the island can never render worse than it does today.
    /// </summary>
    [Serializable]
    public class EnvTileSet
    {
        [Tooltip("Interchangeable tiles for this biome/bucket. Order is the slice order " +
                 "(tile_<biome>_00..15); it only has to be STABLE, not meaningful.")]
        public TileBase[] Variants = new TileBase[0];

        /// <summary>How many non-null variants this set actually carries.</summary>
        public int Count
        {
            get
            {
                if (Variants == null)
                {
                    return 0;
                }

                int n = 0;
                for (int i = 0; i < Variants.Length; i++)
                {
                    if (Variants[i] != null)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        /// <summary>True when at least one variant is present (i.e. the art is imported).</summary>
        public bool HasAny => Count > 0;

        /// <summary>
        /// THE single source of truth for "which variant does this cell get". A pure
        /// function of the cell coordinate and <paramref name="salt"/>, so two scene
        /// builds — or a scene build and a test re-deriving the expected paint — always
        /// agree. Null slots are skipped (a half-imported set still paints), and a set
        /// with no art at all returns null so the caller can fall back.
        /// </summary>
        public TileBase Variant(Vector3Int cell, int salt)
        {
            if (Variants == null || Variants.Length == 0)
            {
                return null;
            }

            int start = EnvDressing.Index(cell, salt, Variants.Length);
            for (int i = 0; i < Variants.Length; i++)
            {
                TileBase t = Variants[(start + i) % Variants.Length];
                if (t != null)
                {
                    return t;
                }
            }

            return null;
        }

        /// <summary>True when <paramref name="tile"/> is one of this set's variants —
        /// how a test asks "is the paint at this cell from the right bucket?".</summary>
        public bool Contains(TileBase tile)
        {
            if (tile == null || Variants == null)
            {
                return false;
            }

            for (int i = 0; i < Variants.Length; i++)
            {
                if (Variants[i] == tile)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// One grass-to-X transition family, keyed by the 4-bit NEIGHBOUR MASK of the cell
    /// being painted. The art ships all 15 non-empty masks per family, so the lookup is
    /// a straight index — there is no rotation/mirroring to get wrong.
    ///
    /// Mask bits (map-cell space, the axes OverworldMap uses — see
    /// <see cref="EnvDressing.EdgeOffsets"/> and Tools/generate_env.py):
    ///   bit0 (1) = -Y neighbour  -> screen UPPER-RIGHT diamond edge
    ///   bit1 (2) = +X neighbour  -> screen LOWER-RIGHT diamond edge
    ///   bit2 (4) = +Y neighbour  -> screen LOWER-LEFT diamond edge
    ///   bit3 (8) = -X neighbour  -> screen UPPER-LEFT diamond edge
    /// </summary>
    [Serializable]
    public class EnvEdgeSet
    {
        /// <summary>Number of mask slots (masks 1..15; index 0 is unused and stays null).</summary>
        public const int MaskCount = 16;

        [Tooltip("Transition tiles indexed by the 4-bit neighbour mask; index 0 is unused. " +
                 "Any slot left null falls back to the plain base variant for that cell.")]
        public TileBase[] ByMask = new TileBase[MaskCount];

        /// <summary>The transition tile for <paramref name="mask"/>, or null when this
        /// family has no art for it (caller then paints the plain base variant).</summary>
        public TileBase Edge(int mask)
        {
            if (ByMask == null || mask <= 0 || mask >= ByMask.Length)
            {
                return null;
            }

            return ByMask[mask];
        }

        /// <summary>True when at least one transition tile is present.</summary>
        public bool HasAny
        {
            get
            {
                if (ByMask == null)
                {
                    return false;
                }

                for (int i = 1; i < ByMask.Length; i++)
                {
                    if (ByMask[i] != null)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>True when <paramref name="tile"/> is one of this family's transitions.</summary>
        public bool Contains(TileBase tile)
        {
            if (tile == null || ByMask == null)
            {
                return false;
            }

            for (int i = 0; i < ByMask.Length; i++)
            {
                if (ByMask[i] == tile)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// The DETERMINISTIC rules of the environment dressing pass (DinoDigger-y1g), kept in
    /// the runtime assembly on purpose: SceneBuilder (editor) paints with them and the
    /// EnvDressingApplied integration case (runtime) re-derives the expected paint with the
    /// SAME functions. That shared derivation is the determinism guarantee — every rule
    /// here is a pure function of the cell coordinate, so a rebuild reproduces the island
    /// dressing byte for byte and a test can predict it without rebuilding anything.
    ///
    /// Nothing in this class touches walkability. The dressing only ever changes WHICH
    /// tile asset is painted on the ground/water layers (all equally walkable/blocking)
    /// and adds a purely decorative decal layer that OverworldMap does not consult.
    /// </summary>
    public static class EnvDressing
    {
        // Salts keep the independent decisions from correlating: without them the cell
        // that rolls variant 0 would also always be the cell that rolls "no decal".
        public const int SaltGrass = 0x51ED;
        public const int SaltPath = 0x2C0F;
        public const int SaltWater = 0x7A31;
        public const int SaltBed = 0x1B95;
        public const int SaltDecalPlace = 0x3F6B;
        public const int SaltDecalPick = 0x6D2A;

        /// <summary>Neighbour offsets in MASK BIT ORDER: bit0 -Y, bit1 +X, bit2 +Y, bit3 -X.
        /// Must stay in this order — it is the contract with the baked edge tiles.</summary>
        public static readonly Vector3Int[] EdgeOffsets =
        {
            new Vector3Int(0, -1, 0), // bit0 (1)
            new Vector3Int(1, 0, 0),  // bit1 (2)
            new Vector3Int(0, 1, 0),  // bit2 (4)
            new Vector3Int(-1, 0, 0), // bit3 (8)
        };

        /// <summary>Default decal density per biome (chance a dressable cell gets one).
        /// Mirrors the grammar the art's own contact-sheet composer used, so the island
        /// reads like the approved review artifact. GameConfig can override these.</summary>
        public const float DefaultGrassDecalChance = 0.20f;
        public const float DefaultPathDecalChance = 0.34f;
        public const float DefaultWaterDecalChance = 0.22f;

        /// <summary>Chance that a path cell's decal is the WARM ACCENT (stones) rather
        /// than the everyday footprints/pebbles. Rule 4 of the style contract wants the
        /// warm accent to be a rare treat, not a carpet.</summary>
        public const float DefaultAccentShare = 0.12f;

        /// <summary>A well-mixed, platform-stable hash of a cell + salt. Pure integer
        /// math (no System.Random, no float ops), so the same cell answers the same on
        /// every machine, every rebuild and every run.</summary>
        public static uint Hash(Vector3Int cell, int salt)
        {
            unchecked
            {
                uint h = (uint)(cell.x * 73856093) ^ (uint)(cell.y * 19349663) ^
                         (uint)(salt * 83492791);
                h ^= h >> 16;
                h *= 0x7feb352du;
                h ^= h >> 15;
                h *= 0x846ca68bu;
                h ^= h >> 16;
                return h;
            }
        }

        /// <summary>A stable index in [0, count) for this cell (0 when count &lt;= 0).</summary>
        public static int Index(Vector3Int cell, int salt, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            return (int)(Hash(cell, salt) % (uint)count);
        }

        /// <summary>A stable "roll" in [0,1) for this cell — the decal placement die.</summary>
        public static float Roll(Vector3Int cell, int salt)
        {
            return (Hash(cell, salt) % 100000u) / 100000f;
        }

        /// <summary>The variant salt for a biome, so every caller salts the same way.</summary>
        public static int SaltFor(EnvBiome biome)
        {
            switch (biome)
            {
                case EnvBiome.Path: return SaltPath;
                case EnvBiome.Water: return SaltWater;
                case EnvBiome.Bed: return SaltBed;
                default: return SaltGrass;
            }
        }

        /// <summary>
        /// The transition mask for a GRASS cell, given a neighbour-biome lookup: which of
        /// the four neighbours are the SAME non-grass biome, expressed in the baked art's
        /// bit order. Only ONE foreign biome ever contributes (the first found in bit
        /// order) — the art has no three-way transitions, and mixing families would paint
        /// a shoreline where a path meets grass. Returns 0 (paint the plain variant) when
        /// the cell has no foreign neighbour, and reports the chosen family in
        /// <paramref name="other"/>.
        /// </summary>
        public static int EdgeMask(Vector3Int cell, Func<Vector3Int, EnvBiome> biomeAt,
            out EnvBiome other)
        {
            other = EnvBiome.None;
            if (biomeAt == null)
            {
                return 0;
            }

            int mask = 0;
            for (int bit = 0; bit < EdgeOffsets.Length; bit++)
            {
                EnvBiome nb = biomeAt(cell + EdgeOffsets[bit]);
                if (nb == EnvBiome.Grass || nb == EnvBiome.None)
                {
                    continue;
                }

                if (other == EnvBiome.None)
                {
                    other = nb;
                }

                if (nb == other)
                {
                    mask |= 1 << bit;
                }
            }

            return mask;
        }
    }
}
