using UnityEngine;
using DinoDigger.Core;

namespace DinoDigger.Config
{
    /// <summary>
    /// Data for one dinosaur species. Adding a new dino = one sprite sheet plus
    /// one of these assets referenced from <see cref="GameConfig.Dinos"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "DinoDigger/Dino Definition", fileName = "Dino_")]
    public class DinoDefinition : ScriptableObject
    {
        [Header("Identity")]
        public DinoType Type = DinoType.TRex;
        public string DisplayName = "Dino";
        public DanceType Dance = DanceType.StompRoar;

        [Header("Colors (placeholder tinting + egg telegraph)")]
        public Color BodyColor = Color.green;
        public Color EggColor = Color.green;

        [Header("Sprites")]
        [Tooltip("Egg sprite whose color telegraphs this dino.")]
        public Sprite EggSprite;

        [Tooltip("8-directional ADULT (Big) walk sprites, indexed by Dir8 (N,NE,E,SE,S,SW,W,NW). " +
                 "The default/full-grown set; also the fallback when a stage set below is empty.")]
        public Sprite[] WalkSprites = new Sprite[8];

        [Tooltip("8-dir walk sprites for the BABY stage (falls back to WalkSprites when empty).")]
        public Sprite[] BabySprites = new Sprite[8];

        [Tooltip("8-dir walk sprites for the KID stage (falls back to WalkSprites when empty).")]
        public Sprite[] KidSprites = new Sprite[8];

        [Tooltip("Optional dedicated idle sprite; falls back to the S walk sprite.")]
        public Sprite IdleSprite;

        [Header("Walk-cycle stride frames (optional; empty = static walk)")]
        [Tooltip("8-dir mid-stride frame A for the ADULT stage (left leg forward). " +
                 "Leave empty for dinos without generated stride art.")]
        public Sprite[] WalkASprites = new Sprite[8];

        [Tooltip("8-dir mid-stride frame B for the ADULT stage (right leg forward).")]
        public Sprite[] WalkBSprites = new Sprite[8];

        [Tooltip("8-dir stride frame A for the BABY stage.")]
        public Sprite[] BabyWalkASprites = new Sprite[8];

        [Tooltip("8-dir stride frame B for the BABY stage.")]
        public Sprite[] BabyWalkBSprites = new Sprite[8];

        [Tooltip("8-dir stride frame A for the KID stage.")]
        public Sprite[] KidWalkASprites = new Sprite[8];

        [Tooltip("8-dir stride frame B for the KID stage.")]
        public Sprite[] KidWalkBSprites = new Sprite[8];

        /// <summary>The 8-dir sprite set for a growth stage. Baby/Kid fall back to the
        /// adult set when their array is empty (art not yet generated), so a dino is
        /// never left blank. Big always uses the adult <see cref="WalkSprites"/>.</summary>
        public Sprite[] StageSprites(GrowthStage stage)
        {
            switch (stage)
            {
                case GrowthStage.Baby: return HasAny(BabySprites) ? BabySprites : WalkSprites;
                case GrowthStage.Kid: return HasAny(KidSprites) ? KidSprites : WalkSprites;
                default: return WalkSprites; // Big / adult
            }
        }

        /// <summary>The 8-dir mid-stride set for a growth stage and phase (0 = stride A,
        /// 1 = stride B), or NULL when no matching stride art exists — callers must treat
        /// null as "no walk animation" and keep the static idle frame.
        ///
        /// Fallback rule: strides must come from the SAME art set the idle frame comes
        /// from, or the character would flicker between two identities mid-walk. So a
        /// stage that fell back to the adult idles (its stage set is empty) uses the
        /// adult strides, while a stage with its OWN idles only ever animates with its
        /// own strides (null while that stage's stride art is not yet generated).</summary>
        public Sprite[] StrideSprites(GrowthStage stage, int phase)
        {
            switch (stage)
            {
                case GrowthStage.Baby:
                    if (HasAny(BabySprites))
                    {
                        return NullIfEmpty(phase == 0 ? BabyWalkASprites : BabyWalkBSprites);
                    }

                    break; // baby uses the adult idles -> adult strides
                case GrowthStage.Kid:
                    if (HasAny(KidSprites))
                    {
                        return NullIfEmpty(phase == 0 ? KidWalkASprites : KidWalkBSprites);
                    }

                    break; // kid uses the adult idles -> adult strides
            }

            return NullIfEmpty(phase == 0 ? WalkASprites : WalkBSprites);
        }

        private static Sprite[] NullIfEmpty(Sprite[] set)
        {
            return HasAny(set) ? set : null;
        }

        /// <summary>Resolve the sprite for a facing at the ADULT stage (back-compat).</summary>
        public Sprite GetSprite(Dir8 dir)
        {
            return Direction8.Pick(WalkSprites, dir, IdleSprite);
        }

        /// <summary>Resolve the sprite for a facing at a specific growth stage.</summary>
        public Sprite GetSprite(Dir8 dir, GrowthStage stage)
        {
            return Direction8.Pick(StageSprites(stage), dir, IdleSprite);
        }

        private static bool HasAny(Sprite[] set)
        {
            if (set == null)
            {
                return false;
            }

            for (int i = 0; i < set.Length; i++)
            {
                if (set[i] != null)
                {
                    return true;
                }
            }

            return false;
        }

        public Sprite GetIdle()
        {
            if (IdleSprite != null)
            {
                return IdleSprite;
            }

            return Direction8.Pick(WalkSprites, Dir8.S, null);
        }
    }
}
