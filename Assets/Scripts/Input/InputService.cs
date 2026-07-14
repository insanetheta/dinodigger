using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace DinoDigger.Input
{
    /// <summary>
    /// Single-touch tap abstraction over the new Input System. Fires
    /// <see cref="Tapped"/> with the screen position on the frame a touch or the
    /// left mouse button is pressed. Taps that land on UI are ignored. Mouse and
    /// touch behave identically (editor / WebGL / device).
    /// </summary>
    public class InputService : MonoBehaviour
    {
        /// <summary>Screen-space position of a confirmed tap (not over UI).</summary>
        public event Action<Vector2> Tapped;

        [SerializeField] private bool _blockWhenOverUI = true;

        private void Update()
        {
            Vector2 pos;
            if (TryGetTapThisFrame(out pos))
            {
                Fire(pos);
            }
        }

        /// <summary>
        /// Shared tap dispatch. Applies the same over-UI guard as a real tap and
        /// raises the same <see cref="Tapped"/> event / handler chain.
        /// </summary>
        private void Fire(Vector2 screenPosition)
        {
            if (_blockWhenOverUI && IsPointerOverUI())
            {
                return;
            }

            Tapped?.Invoke(screenPosition);
        }

        /// <summary>
        /// TEST HOOK. Drives a tap through the exact same code path as a real touch
        /// / mouse press (same UI guard, same <see cref="Tapped"/> event), so the
        /// integration runner exercises the real camera raycast + collider pipeline.
        /// Screen-space, matching what the Input System would report.
        /// </summary>
        public void SimulateTap(Vector2 screenPosition)
        {
            Fire(screenPosition);
        }

        private static bool TryGetTapThisFrame(out Vector2 position)
        {
            // Touch takes priority so a device with both doesn't double-fire.
            Touchscreen touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
            {
                position = touch.primaryTouch.position.ReadValue();
                return true;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                position = mouse.position.ReadValue();
                return true;
            }

            position = default;
            return false;
        }

        private static bool IsPointerOverUI()
        {
            EventSystem es = EventSystem.current;
            if (es == null)
            {
                return false;
            }

            // -1 works for mouse; touch fingerId defaults are also handled by uGUI.
            return es.IsPointerOverGameObject();
        }
    }
}
