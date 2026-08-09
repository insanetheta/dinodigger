using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DinoDigger.Config;
using DinoDigger.Core;

namespace DinoDigger.UI
{
    /// <summary>
    /// ONE BONE SLOT on the skeleton board: a spot on a species' silhouette that is either
    /// EMPTY (a faint ghost of the bone, so the child can see what is still missing) or FILLED
    /// (the real bone sprite, bright). Tapping a filled bone makes it wiggle — the whole
    /// interaction, and the only one. There is nothing to get wrong and nothing to lose.
    ///
    /// RESTING-SCALE-SAFE, the same discipline the machine friends enforce: the slot keeps ONE
    /// authoritative scale and every wiggle cancels any in-flight punch and re-bases onto it,
    /// so hammering a bone can never inflate it forever.
    /// </summary>
    public class SkeletonBoardSlot : MonoBehaviour, IPointerClickHandler
    {
        // Empty slots are drawn as a faint ghost rather than hidden: "there is a bone missing
        // HERE" is the whole information the board carries, and an invisible slot carries none.
        private static readonly Color EmptyTint = new Color(1f, 1f, 1f, 0.16f);
        private static readonly Color FilledTint = Color.white;

        private Image _image;
        private RectTransform _rect;
        private Vector3 _restingScale = Vector3.one;
        private bool _filled;
        private int _wiggles;

        /// <summary>Which species' skeleton this slot belongs to, and which slot it is.</summary>
        public DinoType Species { get; private set; }
        public int Slot { get; private set; }

        /// <summary>True once a banked bone has filled this slot.</summary>
        public bool IsFilled => _filled;

        // TEST HOOKS (integration runner; no reflection).
        internal int TestWiggles => _wiggles;
        internal bool TestFilled => _filled;
        internal Color TestColor => _image != null ? _image.color : Color.clear;

        /// <summary>Wire the slot to its image + identity. Captures the resting scale ONCE,
        /// before any wiggle can have inflated it.</summary>
        internal void Bind(Image image, DinoType species, int slot)
        {
            _image = image;
            _rect = image != null ? image.rectTransform : null;
            _restingScale = _rect != null ? _rect.localScale : Vector3.one;
            Species = species;
            Slot = slot;
        }

        /// <summary>State-derived visuals: the slot's look is recomputed from "is it filled?"
        /// every refresh, never toggled at a call site. A newly filled slot pops so the child
        /// sees WHICH bone just landed.</summary>
        internal void SetFilled(bool filled, Sprite boneArt)
        {
            bool wasFilled = _filled;
            _filled = filled;

            if (_image != null)
            {
                if (boneArt != null)
                {
                    _image.sprite = boneArt;
                }

                _image.color = filled ? FilledTint : EmptyTint;
            }

            if (filled && !wasFilled)
            {
                Pop(0.5f, 0.4f);
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            Wiggle();
        }

        /// <summary>Tap answer. A FILLED bone wiggles and chimes; an empty spot still nudges,
        /// because every tap is answered — that rule has no exceptions here either.</summary>
        internal void Wiggle()
        {
            _wiggles++;

            if (_filled)
            {
                Pop(0.35f, 0.3f);
                if (_rect != null)
                {
                    Tween.ShakeRotation(_rect, 14f, 0.3f, 2);
                }

                GameManager.Instance?.Audio?.Chime();
                return;
            }

            Pop(0.14f, 0.25f);
        }

        private void Pop(float amount, float duration)
        {
            if (_rect == null)
            {
                return;
            }

            Tween.CancelPunch(_rect);          // hand the scale over from any in-flight punch
            _rect.localScale = _restingScale;  // ...and re-base before punching again
            Tween.PunchScale(_rect, amount, duration);
        }
    }
}
