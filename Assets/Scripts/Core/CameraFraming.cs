using UnityEngine;

namespace DinoDigger.Core
{
    /// <summary>
    /// FIT-TO-CONTENT CAMERA FRAMING (DinoDigger-kgm).
    ///
    /// THE ONE PIECE OF MATHS THIS FILE EXISTS FOR: Unity's <c>orthographicSize</c> is HALF the
    /// VERTICAL extent of the view. The horizontal extent is <c>orthographicSize * aspect</c>.
    /// So a FIXED ortho size is only ever correct at ONE aspect ratio — which is exactly why the
    /// dig playfield was clipped in portrait (at 9:19.5 the old DigOrthoSize 4.2 showed 3.9 world
    /// units of width for a 7-unit-wide grid) while looking perfect on a 16:9 desktop.
    ///
    /// The fix is to stop storing sizes and start storing CONTENT: how much world must be on
    /// screen. Then
    ///
    ///     ortho = max(contentHalfHeight, contentHalfWidth / aspect) * (1 + padding)
    ///
    /// clamped to sane bounds. Every aspect is correct BY CONSTRUCTION — portrait, landscape,
    /// ultrawide, a browser window dragged to a silly shape — and because the formula takes the
    /// aspect as an argument rather than baking it in, it is inherently reactive: feed it a new
    /// aspect and you get the new correct size, mid-transition or not.
    ///
    /// THE SCREEN SEAM. Everything that frames anything reads the screen through
    /// <see cref="ScreenSize"/> / <see cref="ScreenAspect"/> / <see cref="ScreenSafeArea"/>
    /// rather than touching <c>Screen</c> directly, and a test can substitute a phone-shaped
    /// screen for a few frames (<see cref="TestSetScreen(float,float)"/>). The editor cannot
    /// rotate a phone, so this seam is the only way portrait behaviour is provable at all — and
    /// production goes through the very same property, so what a case drives is what a child
    /// gets. No reflection anywhere near it.
    /// </summary>
    public static class CameraFraming
    {
        /// <summary>Aspect used when there is no screen to ask (headless, a zero-height window).
        /// 16:9 — the shape the game's landscape framing was authored at.</summary>
        public const float DefaultAspect = 16f / 9f;

        // Absurdity guards. A browser window really can report a 40:1 sliver for one frame
        // during a resize, and dividing by that produces a camera the size of a postage stamp.
        private const float MinAspect = 0.2f;
        private const float MaxAspect = 6f;

        // ---- the test screen seam (no reflection) ----
        private static bool _screenOverridden;
        private static Vector2 _screenSize;
        private static Rect _screenSafeArea;

        /// <summary>The screen in pixels — the real one, or the one a test substituted.</summary>
        public static Vector2 ScreenSize =>
            _screenOverridden ? _screenSize : new Vector2(Screen.width, Screen.height);

        /// <summary>Width / height of <see cref="ScreenSize"/>, clamped away from absurdity.</summary>
        public static float ScreenAspect
        {
            get
            {
                Vector2 s = ScreenSize;
                if (s.x <= 0.5f || s.y <= 0.5f)
                {
                    return DefaultAspect;
                }

                return Mathf.Clamp(s.x / s.y, MinAspect, MaxAspect);
            }
        }

        /// <summary>The unobscured part of the screen (notch / home indicator / rounded
        /// corners), in pixels. Full-screen when nothing is obscured, which is every desktop.</summary>
        public static Rect ScreenSafeArea
        {
            get
            {
                if (_screenOverridden)
                {
                    return _screenSafeArea;
                }

                Rect safe = Screen.safeArea;
                if (safe.width <= 0.5f || safe.height <= 0.5f)
                {
                    return new Rect(0f, 0f, Screen.width, Screen.height);
                }

                return safe;
            }
        }

        /// <summary>True while a test is standing in for the real screen. Production never
        /// sets this; the runner clears it between cases as a backstop.</summary>
        internal static bool TestScreenOverridden => _screenOverridden;

        /// <summary>TEST HOOK. Pretend the screen is this many pixels, with no obscured edges.</summary>
        internal static void TestSetScreen(float width, float height)
        {
            TestSetScreen(width, height, new Rect(0f, 0f, width, height));
        }

        /// <summary>TEST HOOK. Pretend the screen is this many pixels with this safe area —
        /// a notched phone in portrait, which is the shape the HUD has to survive.</summary>
        internal static void TestSetScreen(float width, float height, Rect safeArea)
        {
            _screenOverridden = true;
            _screenSize = new Vector2(Mathf.Max(1f, width), Mathf.Max(1f, height));
            _screenSafeArea = safeArea;
        }

        /// <summary>TEST HOOK. Hand the real screen back.</summary>
        internal static void TestClearScreen()
        {
            _screenOverridden = false;
            _screenSize = Vector2.zero;
            _screenSafeArea = default;
        }

        // ---- the fit itself ----

        /// <summary>
        /// PURE. The orthographic size that shows a <paramref name="contentHalfW"/> x
        /// <paramref name="contentHalfH"/> half-rect of world at <paramref name="aspect"/>.
        ///
        /// <paramref name="padding"/> is a FRACTION of breathing room (0.05 = 5% out).
        /// <paramref name="minSize"/> is the LANDSCAPE BASELINE: the framing the game already
        /// ships with, held as a floor so no aspect can ever zoom in tighter than today.
        /// <paramref name="maxSize"/> is an absurdity ceiling (pass 0 for none) — it is allowed
        /// to be raised by the floor, because a baseline is a promise and a ceiling is a guard.
        /// </summary>
        public static float ComputeOrtho(float contentHalfW, float contentHalfH, float aspect,
            float padding, float minSize, float maxSize)
        {
            float a = Mathf.Clamp(aspect, MinAspect, MaxAspect);
            float halfW = Mathf.Max(0f, contentHalfW);
            float halfH = Mathf.Max(0f, contentHalfH);

            // ORTHO IS HALF THE HEIGHT. To honour a width you divide by the aspect; whichever
            // of the two demands more wins, and that single max is what makes every aspect
            // correct at once.
            float need = Mathf.Max(halfH, halfW / a) * (1f + Mathf.Max(0f, padding));

            float lo = Mathf.Max(0.1f, minSize);
            float hi = maxSize > 0.0001f ? Mathf.Max(lo, maxSize) : float.MaxValue;
            return Mathf.Clamp(need, lo, hi);
        }

        /// <summary>PURE. How much world is on screen horizontally at this framing.</summary>
        public static float VisibleWidth(float ortho, float aspect)
        {
            return 2f * Mathf.Max(0f, ortho) * Mathf.Clamp(aspect, MinAspect, MaxAspect);
        }

        /// <summary>PURE. How much world is on screen vertically at this framing.</summary>
        public static float VisibleHeight(float ortho)
        {
            return 2f * Mathf.Max(0f, ortho);
        }

        /// <summary>PURE. The world rectangle an orthographic camera at <paramref name="center"/>
        /// can see. This is the rect a framing case asserts its content against.</summary>
        public static Rect VisibleRect(Vector2 center, float ortho, float aspect)
        {
            float halfW = VisibleWidth(ortho, aspect) * 0.5f;
            float halfH = VisibleHeight(ortho) * 0.5f;
            return Rect.MinMaxRect(center.x - halfW, center.y - halfH, center.x + halfW, center.y + halfH);
        }

        /// <summary>PURE. Is <paramref name="point"/> inside <paramref name="view"/> with at
        /// least <paramref name="margin"/> world units to spare on every side?</summary>
        public static bool Contains(Rect view, Vector2 point, float margin)
        {
            return point.x >= view.xMin + margin && point.x <= view.xMax - margin
                && point.y >= view.yMin + margin && point.y <= view.yMax - margin;
        }
    }

    /// <summary>
    /// A FRAMING REQUEST: the content a camera has to show, plus the bounds it may not leave.
    /// Cameras store one of these instead of a number, which is the whole trick — a stored size
    /// is stale the moment the device rotates, whereas a stored request answers correctly for
    /// whatever aspect it is asked about, including halfway through a transition tween.
    /// </summary>
    public struct CameraFit
    {
        /// <summary>Half the world WIDTH that must be visible, measured about the framing centre.</summary>
        public float HalfWidth;

        /// <summary>Half the world HEIGHT that must be visible, measured about the framing centre.</summary>
        public float HalfHeight;

        /// <summary>Breathing room as a fraction (0 = the extents already carry their margins).</summary>
        public float Padding;

        /// <summary>Landscape baseline: never frame tighter than this, at any aspect.</summary>
        public float MinSize;

        /// <summary>Absurdity ceiling. Raised to <see cref="MinSize"/> if it would undercut it.</summary>
        public float MaxSize;

        /// <summary>A default-constructed fit has no ceiling and would frame nothing; callers
        /// use this to fall back rather than to divide by zero.</summary>
        public bool IsValid => MaxSize > 0.0001f || MinSize > 0.0001f;

        /// <summary>Fit to a content half-rect.</summary>
        public static CameraFit Content(float halfWidth, float halfHeight, float padding,
            float minSize, float maxSize)
        {
            return new CameraFit
            {
                HalfWidth = halfWidth,
                HalfHeight = halfHeight,
                Padding = padding,
                MinSize = minSize,
                MaxSize = maxSize,
            };
        }

        /// <summary>A framing that is the SAME at every aspect — the old behaviour, kept for
        /// callers that genuinely want one number (and as the null-config fallback). Nothing in
        /// normal play uses it any more.</summary>
        public static CameraFit Fixed(float orthoSize)
        {
            float s = Mathf.Max(0.1f, orthoSize);
            return new CameraFit { HalfWidth = 0f, HalfHeight = s, Padding = 0f, MinSize = s, MaxSize = s };
        }

        /// <summary>The orthographic size this request resolves to at <paramref name="aspect"/>.</summary>
        public float Ortho(float aspect)
        {
            return CameraFraming.ComputeOrtho(HalfWidth, HalfHeight, aspect, Padding, MinSize, MaxSize);
        }

        /// <summary>The orthographic size this request resolves to on the LIVE screen.</summary>
        public float Ortho()
        {
            return Ortho(CameraFraming.ScreenAspect);
        }
    }
}
