using System.Collections.Generic;
using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;

namespace DinoDigger.Dig
{
    /// <summary>
    /// MEGA-FOSSIL SITES (DinoDigger-84f) — the rare mound with a skull on it.
    ///
    /// Ordinary digs bury ONE bone of the skeleton the board is currently filling. A mega-fossil
    /// site buries the WHOLE REST OF IT: every bone that species is still missing, laid out as
    /// separate multi-cell shapes across a much bigger 7x9 pit. Finding one and digging it out
    /// finishes a skeleton in a single sitting — which is the payoff the bone-by-bone drip is
    /// pacing toward, delivered as an event rather than as an eventual.
    ///
    /// THREE RULES MAKE IT AN EVENT RATHER THAN A LOTTERY:
    ///
    ///   IT IS SIGNPOSTED. The mound wears a skull marker (the bone art the pit already uses)
    ///     before the child ever taps it, so the surprise happens in the overworld and the dig
    ///     itself delivers on a promise the child chose to accept.
    ///   IT IS GUARANTEED EVENTUALLY, AND ONLY ONCE OVER. A pure chance roll is a thing some
    ///     children never see. So the roll carries PITY: if the board still has an incomplete
    ///     skeleton and no mega site has been seen this session, the Nth mound is one for certain
    ///     (<c>GameConfig.DigMegaFossilPityMounds</c>). Pity is paid the moment the SKULL GOES ON
    ///     — and at most one skull stands on the island at a time (DinoDigger-tyf), so the
    ///     guarantee hands the child one landmark rather than turning the whole island into one.
    ///   IT NEVER ENDS EARLY. A normal round ends when the last buried item is uncovered. On a
    ///     mega site that would be a way to LOSE the skeleton, so the round stays open until the
    ///     bones are out (see <see cref="MegaSkeletonPending"/>). The child cannot accidentally
    ///     walk away from the thing they came for.
    ///
    /// PERSISTENCE, EXPLICITLY: a mega site is NOT multi-session state and does not need to be.
    /// The mega flag lives on the live mound for the session; the dig is not saved mid-round by
    /// anything (no dig ever has been), and a mound that has not been dug out simply stays in
    /// the world exactly like any other mound. A restart re-rolls mound flavours from scratch —
    /// which costs the child nothing, because the bones a mega site would have buried are still
    /// exactly the bones their board is missing, and the pity counter starts again in their
    /// favour.
    ///
    /// NO LADDER ON A MEGA SITE. Depth layers rebuild the board (DinoDigger-dv1), and a rebuild
    /// would take an un-dug skeleton with it. The two features are deliberately exclusive: a
    /// mega site is wide, not deep.
    /// </summary>
    public partial class DigModeController
    {
        // This site is a mega-fossil dig (set by Open, cleared by Close).
        private bool _mega;

        // Which skeleton this site is burying, and how many of its bones went into the ground.
        private DinoType _megaSpecies;
        private int _megaBonesPlanned;

        /// <summary>Camera ortho size this site wants — the mega pit needs a wider frame than
        /// the standard board. Read by GameManager when it hands the camera to the dig.</summary>
        public float DigOrthoSize
        {
            get
            {
                if (_config == null)
                {
                    return _mega ? 5.8f : 4.2f;
                }

                return _mega ? _config.DigMegaOrthoSize : _config.DigOrthoSize;
            }
        }

        /// <summary>Grid size for this site: the mega pit, or the standard board.</summary>
        private void ResolveGridSize(out int rows, out int cols)
        {
            if (_config != null)
            {
                _config.GetDigGridSize(_mega, out rows, out cols);
                return;
            }

            rows = _mega ? 7 : 5;
            cols = _mega ? 9 : 7;
        }

        /// <summary>Bury the whole remaining skeleton (the mega site's replacement for the
        /// ordinary single-bone roll).
        ///
        /// Every bone the focus species still needs goes in as its own multi-cell shape. Two
        /// passes: the first demands a one-cell GAP around each shape so the child reads a
        /// scattered skeleton rather than one blob of bone-peeks; the second drops that demand so
        /// a cramped board still gets every bone in. A bone that could not be placed at all is
        /// simply not buried — the site is still a huge dig with everything else in it — and the
        /// skeleton is still finishable, one ordinary dig at a time, exactly as before.</summary>
        private void PlaceMegaSkeleton()
        {
            _megaSpecies = default;
            _megaBonesPlanned = 0;

            if (_grid == null || _tiles.Count == 0 || TestSuppressBones)
            {
                return;
            }

            var wanted = new List<int>();
            if (!TryMegaSkeleton(out DinoType species, wanted) || wanted.Count == 0)
            {
                // Nothing left to collect: fall back to the ordinary roll, which will also find
                // nothing and quietly bury no bone. A mega site with no skeleton left to bury is
                // still a big generous pit full of toys and loot.
                _boneAssigned = TryPlaceRolledBone();
                return;
            }

            _megaSpecies = species;

            // Big bones first (a skull needs a 2x2; a stub needs two cells): placing the
            // fussiest shapes while the board is still empty is what stops the last bone from
            // being the one with nowhere to go.
            wanted.Sort((a, b) => BoneCellCount(b).CompareTo(BoneCellCount(a)));

            for (int i = 0; i < wanted.Count; i++)
            {
                if (TryPlaceMegaBone(wanted[i], species, requireGap: true) ||
                    TryPlaceMegaBone(wanted[i], species, requireGap: false))
                {
                    _megaBonesPlanned++;
                }
            }

            _boneAssigned = _megaBonesPlanned > 0;
        }

        /// <summary>Ask the skeleton board which species is being filled and EVERY bone it still
        /// needs (a big skeleton wants two ribs and two femurs, so the same bone index can appear
        /// twice). False with no GameManager (a bare test rig) or once the collection is done.</summary>
        private bool TryMegaSkeleton(out DinoType species, List<int> into)
        {
            species = default;
            GameManager gm = GameManager.Instance;
            return gm != null && gm.TryRemainingBones(out species, into);
        }

        /// <summary>Cells the smallest template of <paramref name="boneIndex"/> occupies — the
        /// sort key that places the fussy shapes first.</summary>
        private static int BoneCellCount(int boneIndex)
        {
            int best = 0;
            for (int i = 0; i < BoneTemplates.Length; i++)
            {
                if (BoneTemplateType[i] == boneIndex)
                {
                    best = Mathf.Max(best, BoneTemplates[i].Length / 2);
                }
            }

            return best;
        }

        /// <summary>Find somewhere on the big board for one bone of <paramref name="boneIndex"/>.
        /// Templates for that bone are tried in a shuffled order (a femur has two shapes), and
        /// anchors are scanned from a random start so the same skeleton never lands twice in the
        /// same arrangement. Row 0 is skipped while anything deeper fits, exactly as the ordinary
        /// bone roll does: a bone lying along the surface uncovers itself on the first bite.</summary>
        private bool TryPlaceMegaBone(int boneIndex, DinoType species, bool requireGap)
        {
            var templates = new List<int>();
            for (int i = 0; i < BoneTemplates.Length; i++)
            {
                if (BoneTemplateType[i] == boneIndex)
                {
                    templates.Add(i);
                }
            }

            if (templates.Count == 0)
            {
                return false;
            }

            Shuffle(templates);

            int cells = Mathf.Max(1, _rows * _cols);
            for (int pass = 0; pass < 2; pass++)
            {
                int minRow = pass == 0 ? 1 : 0;
                for (int i = 0; i < templates.Count; i++)
                {
                    int template = templates[i];
                    int start = Random.Range(0, cells);
                    for (int step = 0; step < cells; step++)
                    {
                        int cell = (start + step) % cells;
                        int r = cell / _cols;
                        int c = cell % _cols;
                        if (r < minRow)
                        {
                            continue;
                        }

                        if (requireGap && !HasBoneGap(r, c, template))
                        {
                            continue;
                        }

                        if (PlaceBoneAt(r, c, template, species))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>True when no cell of <paramref name="template"/> anchored at r,c — nor any
        /// of their 8-neighbours — already belongs to a bone. The "spread across the grid"
        /// requirement, expressed as the one thing that actually reads on screen: separate
        /// clusters of bone-peek rather than one continuous smear of them.</summary>
        private bool HasBoneGap(int r, int c, int template)
        {
            if (template < 0 || template >= BoneTemplates.Length)
            {
                return false;
            }

            int[] offsets = BoneTemplates[template];
            for (int i = 0; i < offsets.Length; i += 2)
            {
                int cr = r + offsets[i];
                int cc = c + offsets[i + 1];
                for (int dr = -1; dr <= 1; dr++)
                {
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        if (FindBoneAt(cr + dr, cc + dc) != null)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>True while a mega site still owes the child bones. This is the ONE reason a
        /// mega round does not end the moment its buried loot runs out: ending there would let a
        /// child lose the skeleton they came for, and this game does not take things away.</summary>
        private bool MegaSkeletonPending()
        {
            if (!_mega)
            {
                return false;
            }

            for (int i = 0; i < _bones.Count; i++)
            {
                if (_bones[i] != null && !_bones[i].Popped)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The mega round's other ending: the last BONE came out while the loot was
        /// already all uncovered. Called from the bone pop, so whichever of the two finishes
        /// last is the one that ends the round.</summary>
        private void MaybeFinishMegaRound()
        {
            if (!_mega || _finished || !_open || _buried.Count > 0 || MegaSkeletonPending())
            {
                return;
            }

            _finished = true;
            GameManager.Instance?.FinishDig(_found);
        }

        // ------------------------------------------------------------ TEST HOOKS

        /// <summary>TEST HOOK. Is this a mega-fossil site?</summary>
        internal bool TestMega => _mega;

        /// <summary>TEST HOOK. Which skeleton the mega site is burying.</summary>
        internal DinoType TestMegaSpecies => _megaSpecies;

        /// <summary>TEST HOOK. Bones this mega site actually got into the ground.</summary>
        internal int TestMegaBonesPlanned => _megaBonesPlanned;

        /// <summary>TEST HOOK. Build a mega-fossil site off-screen (the mega twin of
        /// <see cref="TestBuildThemedSite"/>), so a case can dig a whole skeleton without first
        /// driving the island waiting for a rare mound to roll.</summary>
        internal void TestBuildMegaSite(DigTheme theme)
        {
            _open = true;
            _finished = false;
            _found.Clear();
            _crewBuddies = null;
            _theme = theme;
            ResetDepthForNewSite();
            _mega = true;
            BuildGrid();
        }

        /// <summary>TEST HOOK. Uncover every cell of every buried bone through the ordinary
        /// clear chokepoint — the same route a bite, a cascade or a crew power takes — and return
        /// how many cells were cleared. The board's own bone bookkeeping (monotonic uncovering,
        /// the pop on the last cell, the bank) is untouched by this: it just digs, quickly.</summary>
        internal int TestUncoverAllBones()
        {
            int cleared = 0;
            for (int i = 0; i < _bones.Count; i++)
            {
                Bone b = _bones[i];
                if (b == null || b.Popped)
                {
                    continue;
                }

                for (int k = 0; k < b.Rows.Length; k++)
                {
                    DirtTile t = TileAt(b.Rows[k], b.Cols[k]);
                    if (t == null || t.IsDestroyed)
                    {
                        continue;
                    }

                    ClearTileFully(t, "test bone uncover");
                    cleared++;
                }
            }

            return cleared;
        }
    }
}
