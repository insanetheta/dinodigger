using UnityEngine;
using UnityEngine.UI;
using DinoDigger.Core;

namespace DinoDigger.UI
{
    /// <summary>
    /// PURE HELPERS for a HUD that has to survive both orientations (DinoDigger-avw). Separated
    /// out so the awkward cases are provable in the editor, which cannot rotate a phone.
    /// </summary>
    public static class ResponsiveUI
    {
        /// <summary>The shape the whole UI was authored at.</summary>
        public static readonly Vector2 LandscapeReference = new Vector2(1920f, 1080f);

        /// <summary>The same reference, turned on its side.</summary>
        public static readonly Vector2 PortraitReference = new Vector2(1080f, 1920f);

        /// <summary>CanvasScaler match, kept at the shipped 0.5 in BOTH orientations — see
        /// <see cref="CanvasReference"/> for why that is now the right answer.</summary>
        public const float ReferenceMatch = 0.5f;

        /// <summary>
        /// PURE. The CanvasScaler reference resolution for a screen of this aspect.
        ///
        /// WHY SWAP THE REFERENCE RATHER THAN THE MATCH. "match 0.5 against 1920x1080" means
        /// "split the difference between scaling by width and by height", which is correct only
        /// while the screen is roughly the reference SHAPE. On a 9:19.5 phone it hands the HUD a
        /// 979-unit-wide canvas, so a 220-unit treasure counter eats a fifth of the screen and
        /// the 1760-unit board tray runs off both edges. Turning the REFERENCE on its side
        /// instead keeps match at 0.5 and is continuous through square screens (flipping the
        /// match from 0 to 1 at exactly 1:1 is a 1.78x jump in HUD size); and because every
        /// landscape aspect keeps the original reference, the shipped desktop HUD is untouched.
        /// </summary>
        public static Vector2 CanvasReference(float aspect)
        {
            return aspect >= 1f ? LandscapeReference : PortraitReference;
        }

        /// <summary>
        /// PURE. Anchor fractions for a rect that fills the unobscured part of the screen —
        /// inside the notch, above the home indicator, clear of rounded corners.
        /// Degenerate inputs (a zero screen, a safe area bigger than the screen) fall back to
        /// the full rect, because a HUD nobody can see is worse than a HUD under a notch.
        /// </summary>
        public static void SafeAreaAnchors(Rect safeArea, Vector2 screen,
            out Vector2 anchorMin, out Vector2 anchorMax)
        {
            anchorMin = Vector2.zero;
            anchorMax = Vector2.one;

            if (screen.x <= 0.5f || screen.y <= 0.5f || safeArea.width <= 0.5f || safeArea.height <= 0.5f)
            {
                return;
            }

            anchorMin = new Vector2(
                Mathf.Clamp01(safeArea.xMin / screen.x),
                Mathf.Clamp01(safeArea.yMin / screen.y));
            anchorMax = new Vector2(
                Mathf.Clamp01(safeArea.xMax / screen.x),
                Mathf.Clamp01(safeArea.yMax / screen.y));

            if (anchorMax.x - anchorMin.x < 0.1f || anchorMax.y - anchorMin.y < 0.1f)
            {
                anchorMin = Vector2.zero;
                anchorMax = Vector2.one;
            }
        }

        /// <summary>PURE. Uniform scale that fits <paramref name="content"/> inside
        /// <paramref name="frame"/>, never magnifying (1 is the ceiling — a modal that GREW on a
        /// big screen would just look wrong).</summary>
        public static float FitScale(Vector2 content, Vector2 frame)
        {
            if (content.x <= 0.0001f || content.y <= 0.0001f)
            {
                return 1f;
            }

            return Mathf.Clamp(Mathf.Min(frame.x / content.x, frame.y / content.y), 0.2f, 1f);
        }
    }

    /// <summary>
    /// THE HUD'S ORIENTATION BRAIN (DinoDigger-avw). One component on the HUD canvas that:
    ///
    ///   RESHAPES THE SCALER. Reference resolution follows the orientation (see
    ///     <see cref="ResponsiveUI.CanvasReference"/>), so HUD elements keep a sane size in
    ///     portrait instead of ballooning or shrinking.
    ///   OWNS A SAFE-AREA RECT. A single stretched child, tracked to <c>Screen.safeArea</c>,
    ///     that every HUD affordance lives inside — the treasure counter, the parent-gated mute
    ///     button, the bone-board button. Notches and home indicators stop being a per-widget
    ///     problem the moment there is one rect that already accounts for them.
    ///   PUBLISHES <see cref="SafeRect"/>. Full-screen modals (the skeleton board) lay their
    ///     content out inside this rather than inside the raw canvas.
    ///
    /// SELF-HEALING, like the skeleton board and the machine friends: <see cref="Ensure"/>
    /// adds the component and re-parents an already-built HUD, so a scene serialized before this
    /// existed gains safe areas without a rebuild.
    ///
    /// It polls rather than subscribes, for the same reason the camera does: a WebGL canvas
    /// resizing under a browser rotate raises nothing worth waiting for, and two float compares
    /// a frame is not a cost.
    /// </summary>
    [DisallowMultipleComponent]
    public class ResponsiveCanvas : MonoBehaviour
    {
        private const string SafeAreaName = "SafeArea";

        private RectTransform _canvasRect;
        private CanvasScaler _scaler;
        private RectTransform _safeArea;

        private Vector2 _appliedScreen = new Vector2(-1f, -1f);
        private Rect _appliedSafe = new Rect(-1f, -1f, -1f, -1f);

        /// <summary>The rect every HUD affordance is parented under. Never null after
        /// <see cref="Ensure"/>.</summary>
        public RectTransform SafeArea => _safeArea;

        // TEST HOOKS (integration runner; no reflection).
        internal Vector2 TestReference => _scaler != null ? _scaler.referenceResolution : Vector2.zero;
        internal float TestMatch => _scaler != null ? _scaler.matchWidthOrHeight : -1f;
        internal Vector2 TestAnchorMin => _safeArea != null ? _safeArea.anchorMin : Vector2.zero;
        internal Vector2 TestAnchorMax => _safeArea != null ? _safeArea.anchorMax : Vector2.one;

        /// <summary>Add the brain to a canvas (idempotent) and move any HUD that was built
        /// before it existed inside the safe-area rect. Returns null without a canvas.</summary>
        public static ResponsiveCanvas Ensure(Canvas canvas)
        {
            if (canvas == null)
            {
                return null;
            }

            var rc = canvas.GetComponent<ResponsiveCanvas>();
            if (rc == null)
            {
                rc = canvas.gameObject.AddComponent<ResponsiveCanvas>();
            }

            rc.Bind();
            rc.AdoptExistingHud();
            rc.Apply(force: true);
            return rc;
        }

        private void Awake()
        {
            Bind();
        }

        private void Start()
        {
            AdoptExistingHud();
            Apply(force: true);
        }

        private void LateUpdate()
        {
            Apply(force: false);
        }

        private void Bind()
        {
            if (_canvasRect == null)
            {
                _canvasRect = transform as RectTransform;
            }

            if (_scaler == null)
            {
                _scaler = GetComponent<CanvasScaler>();
            }

            if (_safeArea == null)
            {
                Transform found = transform.Find(SafeAreaName);
                _safeArea = found != null ? found as RectTransform : null;
            }

            if (_safeArea == null)
            {
                var go = new GameObject(SafeAreaName, typeof(RectTransform));
                go.transform.SetParent(transform, false);
                _safeArea = (RectTransform)go.transform;
                // FIRST sibling: the HUD drew under the modals before this rect existed and
                // must keep doing so, or the board's backdrop would dim nothing.
                _safeArea.SetSiblingIndex(0);

                // Full-screen until Apply insets it. Set ONCE, on creation — resetting these
                // every Bind would wipe the safe-area anchors Apply had just written, and the
                // early-out in Apply would never put them back.
                _safeArea.anchorMin = Vector2.zero;
                _safeArea.anchorMax = Vector2.one;
                _safeArea.pivot = new Vector2(0.5f, 0.5f);
            }

            _safeArea.offsetMin = Vector2.zero;
            _safeArea.offsetMax = Vector2.zero;
        }

        /// <summary>Move HUD that was built straight onto the canvas (a scene baked before this
        /// component, or SceneBuilder's own order) inside the safe rect. Parenting with
        /// <c>worldPositionStays:false</c> keeps each widget's anchors and inset, so a counter
        /// pinned 30 units off the top-right corner is now pinned 30 units off the SAFE
        /// top-right corner — which is the entire fix, with no per-widget maths.</summary>
        private void AdoptExistingHud()
        {
            if (_safeArea == null)
            {
                return;
            }

            Adopt(GetComponentInChildren<TreasureCounter>(true));
            Adopt(GetComponentInChildren<MuteButton>(true));
        }

        private void Adopt(Component hud)
        {
            if (hud == null || _safeArea == null)
            {
                return;
            }

            if (hud.transform.parent == transform)
            {
                hud.transform.SetParent(_safeArea, false);
            }
        }

        /// <summary>Re-derive the scaler shape and the safe rect. Cheap and idempotent: it
        /// writes nothing unless the screen actually changed.</summary>
        public void Apply(bool force)
        {
            Bind();
            if (_scaler == null)
            {
                return;
            }

            Vector2 screen = CameraFraming.ScreenSize;
            Rect safe = CameraFraming.ScreenSafeArea;
            if (!force && screen == _appliedScreen && safe == _appliedSafe)
            {
                return;
            }

            _appliedScreen = screen;
            _appliedSafe = safe;

            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = ResponsiveUI.CanvasReference(CameraFraming.ScreenAspect);
            _scaler.matchWidthOrHeight = ResponsiveUI.ReferenceMatch;

            ResponsiveUI.SafeAreaAnchors(safe, screen, out Vector2 min, out Vector2 max);
            _safeArea.anchorMin = min;
            _safeArea.anchorMax = max;
            _safeArea.offsetMin = Vector2.zero;
            _safeArea.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// The safe area expressed in CANVAS-LOCAL units (origin at the canvas centre) — the
        /// space a centred full-screen modal positions its content in. Falls back to the whole
        /// canvas when there is nothing to ask.
        /// </summary>
        public Rect SafeRect
        {
            get
            {
                if (_canvasRect == null)
                {
                    _canvasRect = transform as RectTransform;
                }

                Vector2 size = _canvasRect != null ? _canvasRect.rect.size : ResponsiveUI.LandscapeReference;
                Vector2 min = Vector2.zero;
                Vector2 max = Vector2.one;
                if (_safeArea != null)
                {
                    min = _safeArea.anchorMin;
                    max = _safeArea.anchorMax;
                }

                return Rect.MinMaxRect(
                    min.x * size.x - size.x * 0.5f,
                    min.y * size.y - size.y * 0.5f,
                    max.x * size.x - size.x * 0.5f,
                    max.y * size.y - size.y * 0.5f);
            }
        }
    }
}
