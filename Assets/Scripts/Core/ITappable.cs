using UnityEngine;

namespace DinoDigger.Core
{
    /// <summary>
    /// Anything the child can tap directly (mound, fruit, dino, dirt tile).
    /// GameManager resolves taps via Physics2D.OverlapPoint and calls OnTapped.
    /// </summary>
    public interface ITappable
    {
        void OnTapped(Vector2 worldPoint);
    }
}
