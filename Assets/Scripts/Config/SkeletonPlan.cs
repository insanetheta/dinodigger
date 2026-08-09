using UnityEngine;

namespace DinoDigger.Config
{
    /// <summary>
    /// THE SKELETON BOARD'S DATA MODEL (DinoDigger-5ve). One place that answers every question
    /// the fossil finale asks: which species have a skeleton, how many bone SLOTS each one has,
    /// which <see cref="BoneType"/> goes in each slot, and where on the silhouette that slot
    /// sits. The board UI, the dig site's bone roll, the revival gate and the save migration all
    /// read this table, so they can never disagree about what "complete" means.
    ///
    /// FIVE SKELETONS, TWO SIZES. The five fossil species (DinoType 4-8) are the ones the old
    /// egg-shard nest used to hatch; bones replace shards as the way you earn them. The two
    /// SMALL species get a 3-bone skeleton (skull + rib + femur — the minimum that still reads
    /// as a dinosaur), the three BIG ones a 6-bone skeleton (skull, two ribs, two femurs and a
    /// little stub bone). 3 + 3 + 6 + 6 + 6 = 24 bones for the whole board.
    ///
    /// PACING. <see cref="FocusOrder"/> is the order the dig FILLS them in — one skeleton at a
    /// time, smallest first — which reproduces the shape of the old escalating shard curve
    /// (5 / 8 / 15 / 20) in bone counts (3 / 3 / 6 / 6 / 6) while giving a toddler a single
    /// "this one's nearly done!" to care about instead of five half-finished ghosts.
    ///
    /// STABLE CONTRACT. Slot ORDER is what the save migration spreads converted shards across
    /// and what the board draws, so append at the end, never renumber.
    /// </summary>
    public static class SkeletonPlan
    {
        /// <summary>The five fossil species with a skeleton on the board, in DinoType order —
        /// which is also the left-to-right order the board draws them in.</summary>
        public static readonly DinoType[] Species =
        {
            DinoType.Pteranodon,
            DinoType.Ankylosaurus,
            DinoType.Spinosaurus,
            DinoType.Parasaurolophus,
            DinoType.Velociraptor,
        };

        /// <summary>The order the dig site FILLS skeletons in: the two small ones first (3 bones
        /// each, so the first revival comes quickly), then the three big ones (6 each). Only the
        /// first species in this order with an incomplete skeleton is rolled for, so bones always
        /// go somewhere the child can see them landing.</summary>
        public static readonly DinoType[] FocusOrder =
        {
            DinoType.Pteranodon,      // 3 bones — the first revival, the cheap one
            DinoType.Velociraptor,    // 3 bones
            DinoType.Ankylosaurus,    // 6 bones
            DinoType.Parasaurolophus, // 6 bones
            DinoType.Spinosaurus,     // 6 bones — the finale skeleton
        };

        /// <summary>Bone slots in a SMALL species' skeleton (Pteranodon, Velociraptor).</summary>
        public const int SmallSlots = 3;

        /// <summary>Bone slots in a BIG species' skeleton (the other three).</summary>
        public const int BigSlots = 6;

        // Slot layouts, as BoneType ordinals in DRAW order (top of the silhouette down).
        private static readonly int[] SmallLayout =
        {
            (int)BoneType.Skull, (int)BoneType.Rib, (int)BoneType.Femur,
        };

        private static readonly int[] BigLayout =
        {
            (int)BoneType.Skull, (int)BoneType.Rib, (int)BoneType.Rib,
            (int)BoneType.Femur, (int)BoneType.Femur, (int)BoneType.SmallBone,
        };

        // Where each slot sits on its silhouette, in NORMALISED card space (0,0 = bottom-left,
        // 1,1 = top-right of the silhouette image). Hand-placed to read as a skeleton: skull up
        // and forward, ribs through the middle, legs low. The board multiplies these by the
        // silhouette's rect, so the layout survives any card size.
        private static readonly Vector2[] SmallAnchors =
        {
            new Vector2(0.30f, 0.74f), // skull
            new Vector2(0.52f, 0.52f), // rib
            new Vector2(0.62f, 0.26f), // femur
        };

        private static readonly Vector2[] BigAnchors =
        {
            new Vector2(0.26f, 0.76f), // skull
            new Vector2(0.46f, 0.60f), // rib (front)
            new Vector2(0.60f, 0.56f), // rib (back)
            new Vector2(0.44f, 0.24f), // femur (front leg)
            new Vector2(0.70f, 0.24f), // femur (back leg)
            new Vector2(0.84f, 0.46f), // small bone (tail stub)
        };

        /// <summary>True for one of the five fossil species that owns a skeleton.</summary>
        public static bool IsFossilSpecies(DinoType species) => BoardIndex(species) >= 0;

        /// <summary>Board slot (0-4) for a fossil species, or -1 for the egg-hatchable four.</summary>
        public static int BoardIndex(DinoType species)
        {
            for (int i = 0; i < Species.Length; i++)
            {
                if (Species[i] == species)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>True for a species that gets the 3-bone (rather than 6-bone) skeleton.</summary>
        public static bool IsSmall(DinoType species) =>
            species == DinoType.Pteranodon || species == DinoType.Velociraptor;

        /// <summary>How many bone slots <paramref name="species"/>' skeleton has (0 for a
        /// non-fossil species, which has no skeleton at all).</summary>
        public static int SlotCount(DinoType species)
        {
            if (!IsFossilSpecies(species))
            {
                return 0;
            }

            return IsSmall(species) ? SmallSlots : BigSlots;
        }

        /// <summary>The <see cref="BoneType"/> ordinal that fills slot <paramref name="slot"/> of
        /// <paramref name="species"/>' skeleton, or -1 when there is no such slot.</summary>
        public static int SlotBone(DinoType species, int slot)
        {
            int[] layout = LayoutOf(species);
            return layout != null && slot >= 0 && slot < layout.Length ? layout[slot] : -1;
        }

        /// <summary>Where slot <paramref name="slot"/> sits on the silhouette, normalised 0..1.</summary>
        public static Vector2 SlotAnchor(DinoType species, int slot)
        {
            Vector2[] anchors = IsSmall(species) ? SmallAnchors : BigAnchors;
            if (!IsFossilSpecies(species) || slot < 0 || slot >= anchors.Length)
            {
                return new Vector2(0.5f, 0.5f);
            }

            return anchors[slot];
        }

        /// <summary>How many bones of <paramref name="boneIndex"/> this skeleton NEEDS (e.g. a
        /// big skeleton needs two ribs and two femurs, a small one needs one of each).</summary>
        public static int NeedOf(DinoType species, int boneIndex)
        {
            int[] layout = LayoutOf(species);
            if (layout == null)
            {
                return 0;
            }

            int n = 0;
            for (int i = 0; i < layout.Length; i++)
            {
                if (layout[i] == boneIndex)
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>Index of <paramref name="slot"/> AMONG the slots of its own bone type — i.e.
        /// "this is the 2nd rib". A slot is FILLED once the bank holds more than this many of
        /// that bone, which is what lets a count-per-bone bank drive a slot-by-slot board with
        /// no extra bookkeeping.</summary>
        public static int SlotRankWithinBone(DinoType species, int slot)
        {
            int[] layout = LayoutOf(species);
            if (layout == null || slot < 0 || slot >= layout.Length)
            {
                return 0;
            }

            int bone = layout[slot];
            int rank = 0;
            for (int i = 0; i < slot; i++)
            {
                if (layout[i] == bone)
                {
                    rank++;
                }
            }

            return rank;
        }

        private static int[] LayoutOf(DinoType species)
        {
            if (!IsFossilSpecies(species))
            {
                return null;
            }

            return IsSmall(species) ? SmallLayout : BigLayout;
        }
    }
}
