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
    /// One biome's CONNECTED tile set (DinoDigger-l9g): 47 pieces indexed by the
    /// neighbourhood the cell sits in, rather than interchangeable variants.
    ///
    /// This is what replaced the flat-variant painting for water, path and garden bed.
    /// A variant set can only ever paint the middle of a biome; it has no way to know it
    /// is a 1-cell stream, so its features ran off the diamond into the grass and its
    /// runs had no banks. A connected piece carries its own transition: the biome body
    /// only reaches the edges it actually connects across, and everywhere else the tile
    /// paints grass — which is why any two biomes meet through grass and no pairwise
    /// tile is ever needed.
    /// </summary>
    [Serializable]
    public class EnvBlobSet
    {
        [Tooltip("The 47 connected pieces, in EnvDressing's canonical key order " +
                 "(ascending normalised key). Empty = this biome has no connected art " +
                 "and the painter falls back to flat variants + grass-side transitions.")]
        public TileBase[] Pieces = new TileBase[EnvDressing.BlobPieceCount];

        public int Count
        {
            get
            {
                if (Pieces == null)
                {
                    return 0;
                }

                int n = 0;
                for (int i = 0; i < Pieces.Length; i++)
                {
                    if (Pieces[i] != null)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        /// <summary>True only when the set is COMPLETE. A partial connected set is worse
        /// than none — the missing neighbourhoods would fall back to a flat variant that
        /// runs its colour off the diamond, i.e. the exact bug this replaced — so the
        /// painter uses this set all-or-nothing.</summary>
        public bool HasAll => Count == EnvDressing.BlobPieceCount;

        /// <summary>The piece for a raw or normalised 8-neighbour key.</summary>
        public TileBase Piece(int key)
        {
            if (Pieces == null || Pieces.Length == 0)
            {
                return null;
            }

            int slot = EnvDressing.BlobSlot(key);
            return slot >= 0 && slot < Pieces.Length ? Pieces[slot] : null;
        }

        public bool Contains(TileBase tile)
        {
            if (tile == null || Pieces == null)
            {
                return false;
            }

            for (int i = 0; i < Pieces.Length; i++)
            {
                if (Pieces[i] == tile)
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

        // ===================== CONNECTED (blob) TILING — DinoDigger-l9g
        //
        // Water, path and garden bed no longer paint an interchangeable variant per cell.
        // Each one paints the piece that matches its NEIGHBOURHOOD, so a run has banks
        // along it and a body that meets its neighbours' bodies exactly at the shared
        // diamond edge. See the long proof in Tools/generate_env.py: two same-biome cells
        // always agree about the edge between them, and the DIAGONALS in the key are what
        // make the perpendicular bank agree too.
        //
        // Key layout (same bit order as the transition masks, which is the baked art's):
        //   bits 0-3  cardinals  -Y, +X, +Y, -X
        //   bits 4-7  diagonals, where diagonal i is the cell between cardinals i and
        //             (i+1)%4:  (+X,-Y), (+X,+Y), (-X,+Y), (-X,-Y)
        // A diagonal only changes the tile when BOTH its cardinals are same-biome (that
        // is the only time a corner gets carved), so the 256 raw neighbourhoods collapse
        // onto exactly 47 pieces.

        /// <summary>Diagonal offsets, in the key's bit order (bits 4..7).</summary>
        public static readonly Vector3Int[] DiagonalOffsets =
        {
            new Vector3Int(1, -1, 0),  // bit4: between -Y and +X
            new Vector3Int(1, 1, 0),   // bit5: between +X and +Y
            new Vector3Int(-1, 1, 0),  // bit6: between +Y and -X
            new Vector3Int(-1, -1, 0), // bit7: between -X and -Y
        };

        /// <summary>How many distinct connected pieces one biome ships.</summary>
        public const int BlobPieceCount = 47;

        // key -> piece slot (or -1), and slot -> key. Built once, identically to
        // Tools/generate_env.py's blob_keys(): the canonical keys ASCENDING. That order
        // is the contract between the baker, the importer and this lookup.
        private static readonly int[] _blobSlot = new int[256];
        private static readonly int[] _blobKeys;

        static EnvDressing()
        {
            var seen = new System.Collections.Generic.List<int>();
            var known = new bool[256];
            for (int k = 0; k < 256; k++)
            {
                int n = BlobNormalise(k);
                if (!known[n])
                {
                    known[n] = true;
                    seen.Add(n);
                }
            }

            seen.Sort();
            _blobKeys = seen.ToArray();

            var slotOfKey = new int[256];
            for (int i = 0; i < _blobKeys.Length; i++)
            {
                slotOfKey[_blobKeys[i]] = i;
            }

            for (int k = 0; k < 256; k++)
            {
                _blobSlot[k] = slotOfKey[BlobNormalise(k)];
            }
        }

        /// <summary>The canonical key for piece <paramref name="slot"/> (0..46) — how the
        /// importer knows which baked file feeds which slot.</summary>
        public static int BlobKeyAt(int slot) =>
            slot >= 0 && slot < _blobKeys.Length ? _blobKeys[slot] : 255;

        /// <summary>Collapse a raw 8-neighbour key onto the canonical one: a diagonal bit
        /// only survives when both its cardinals are set, otherwise it is pinned to 1
        /// (no corner to carve).</summary>
        public static int BlobNormalise(int key)
        {
            int outKey = key & 0x0F;
            for (int i = 0; i < 4; i++)
            {
                bool both = ((key >> i) & 1) != 0 && ((key >> ((i + 1) & 3)) & 1) != 0;
                outKey |= both ? (key & (1 << (4 + i))) : (1 << (4 + i));
            }

            return outKey;
        }

        /// <summary>Piece slot (0..46) for any raw or normalised key.</summary>
        public static int BlobSlot(int key)
        {
            if (key < 0 || key > 255)
            {
                return _blobSlot[255];
            }

            return _blobSlot[key];
        }

        /// <summary>
        /// The 8-neighbour key for a cell, over whatever <paramref name="sameBiome"/>
        /// counts as "more of me". The caller owns that decision because it is not the
        /// same question per biome: open sea and a bridge deck both read as WATER (a grass
        /// bank in the middle of the ocean is nonsense, and a channel continues under a
        /// deck), while for a PATH only a bridge does — a path that runs out at the coast
        /// should end in a grass shoulder, and it does.
        /// </summary>
        public static int BlobKey(Vector3Int cell, Func<Vector3Int, bool> sameBiome)
        {
            if (sameBiome == null)
            {
                return 255;
            }

            int key = 0;
            for (int i = 0; i < 4; i++)
            {
                if (sameBiome(cell + EdgeOffsets[i]))
                {
                    key |= 1 << i;
                }
            }

            for (int i = 0; i < 4; i++)
            {
                if (sameBiome(cell + DiagonalOffsets[i]))
                {
                    key |= 1 << (4 + i);
                }
            }

            return BlobNormalise(key);
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
