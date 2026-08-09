using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DinoDigger.UI
{
    /// <summary>
    /// A uGUI graphic that answers a tap with a delegate. Used by the skeleton board for its
    /// three plain "press here" surfaces — the HUD bone button that opens it, the big X, and
    /// the whole-screen backdrop that closes it when a toddler taps anywhere else.
    ///
    /// Deliberately dumb: it holds no state and draws nothing, so the board owns every visual
    /// decision in one place. <see cref="TestTap"/> fires the same delegate the pointer would,
    /// so an integration case can press a HUD button without synthesising a uGUI event.
    /// </summary>
    public class SkeletonBoardTap : MonoBehaviour, IPointerClickHandler
    {
        private Action _onTap;

        /// <summary>Wire what this surface does. Rebinding replaces the previous action.</summary>
        public void Bind(Action onTap)
        {
            _onTap = onTap;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            _onTap?.Invoke();
        }

        /// <summary>TEST HOOK. Press this surface exactly as a pointer click would.</summary>
        internal void TestTap()
        {
            _onTap?.Invoke();
        }
    }
}
