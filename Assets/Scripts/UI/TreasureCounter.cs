using UnityEngine;
using UnityEngine.UI;
using DinoDigger.Core;

namespace DinoDigger.UI
{
    /// <summary>
    /// Corner treasure counter. Treasure items fly to this icon; the count bumps
    /// with a little bounce. Uses legacy uGUI Text so there is no TMP dependency.
    /// </summary>
    public class TreasureCounter : MonoBehaviour
    {
        [SerializeField] private Image _icon;
        [SerializeField] private Text _countText;
        [SerializeField] private RectTransform _iconRect;

        private int _count;

        // TEST HOOKS: read the displayed value for the integration runner.
        internal int TestCount => _count;
        internal string TestCountText => _countText != null ? _countText.text : string.Empty;

        private void OnEnable()
        {
            GameEvents.TreasureCollected += OnTreasureCollected;
        }

        private void OnDisable()
        {
            GameEvents.TreasureCollected -= OnTreasureCollected;
        }

        public void SetCount(int count)
        {
            _count = count;
            Refresh();
        }

        private void OnTreasureCollected(int total)
        {
            // Value banked by this collect (denominations bank >1), inferred from the
            // jump in total so no extra plumbing is needed. A bigger coin pops harder.
            int gained = total - _count;
            _count = total;
            Refresh();
            if (_iconRect != null)
            {
                float punch = Mathf.Min(0.4f + 0.08f * Mathf.Max(0, gained - 1), 0.7f);
                Tween.PunchScale(_iconRect, punch, 0.3f);
            }
        }

        private void Refresh()
        {
            if (_countText != null)
            {
                _countText.text = _count.ToString();
            }
        }

        /// <summary>World position of the icon, for the treasure fly-to tween.</summary>
        public Vector3 GetWorldTarget(Camera cam)
        {
            if (cam == null)
            {
                return transform.position;
            }

            RectTransform rt = _iconRect != null ? _iconRect : (transform as RectTransform);
            Vector3 screen = rt != null
                ? RectTransformUtility.WorldToScreenPoint(null, rt.position)
                : new Vector3(Screen.width - 80f, Screen.height - 80f, 0f);

            float depth = Mathf.Abs(cam.transform.position.z) + 1f;
            Vector3 world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, depth));
            world.z = 0f;
            return world;
        }
    }
}
