using UnityEngine;
using DinoDigger.Core;

namespace DinoDigger.Dig
{
    /// <summary>
    /// DIG BACKDROP COVERAGE (DinoDigger-5k8.1).
    ///
    /// The dig's full-bleed backdrop is ONE 14x14-unit sprite, placed so its painted grass lip
    /// lands on the surface line. That was sized for a 16:10 screen and it fits nothing else:
    ///
    ///   PORTRAIT runs off the top and bottom. At 9:19.5 the dig frames 11.80 x 25.57 units of
    ///     world, against a backdrop that spans y in [-7.18, +6.82] — 4.46 units of nothing
    ///     above the sky (17% of the screen) and 7.10 below the soil (28%).
    ///   WIDE LANDSCAPE runs off the sides, and always has: the sprite is +-7 across, but 16:9
    ///     needs 7.47, 19.5:9 needs 9.10, and the mega pit needs 9.28 even at 16:10.
    ///
    /// THE FIX IS THREE FLAT PIECES, and it works because the art's edges are flat. Measured off
    /// dig_background.png: the top 34 rows are one sky blue with a spread of 5/255 across the
    /// whole 1024px width, and the bottom 46 rows are one soil brown with a spread of 6. So:
    ///
    ///   SKY BAND — a flat quad in the sprite's own top-row colour, butted to the sprite's top
    ///     edge and stretched to the top of the camera rect. Seamless because the pixel row it
    ///     meets IS that colour.
    ///   SOIL BAND — the same trick under the sprite's bottom edge.
    ///   WINGS — the backdrop again, MIRRORED (flipX), butted to its own left and right edges.
    ///     Mirroring is pixel-exact at the seam by definition, and it carries the horizon and
    ///     the soil strata out with it, which no flat colour could. They stretch horizontally
    ///     only if a window is wider than two backdrops, and stretching horizontal bands
    ///     horizontally is invisible.
    ///
    /// WHY NOT SCALE THE BACKDROP TO COVER. It would have to grow 1.83x to cover a portrait
    /// view, which blows the sun and clouds up to 1.8x and — worse — makes the painted soil
    /// texture 1.8x the size of the dirt tiles sitting on top of it. The composition breaks
    /// before the coverage is fixed.
    ///
    /// Everything here is driven off the LIVE CAMERA rather than off the framing request, so it
    /// is correct mid-zoom (EnterDig), mid-dip (the descent, which drives the camera 6 units
    /// down), mid-shake, and at any aspect — it shares no state with the fit, it just answers
    /// the question "what can the camera see right now".
    /// </summary>
    public partial class DigModeController
    {
        // Sampled from dig_background.png's flat edge rows. These are FALLBACKS: the importer
        // re-samples them into the library whenever the art is regenerated (see
        // GeneratedArtImporter), so a new sky colour cannot leave a seam behind.
        private static readonly Color FallbackSkyColor = new Color(103f / 255f, 205f / 255f, 249f / 255f);
        private static readonly Color FallbackSoilColor = new Color(163f / 255f, 105f / 255f, 53f / 255f);

        // Draw under the backdrop sprite (sorting order 2) so every butt-join is hidden by art
        // rather than by luck, and far under the tiles and the machine.
        private const int WingOrder = 1;
        private const int BandOrder = 0;

        // A little past the camera rect so a rotate cannot flash a hairline along an edge.
        private const float CoverOverscan = 0.6f;

        // Only bother while the camera is actually looking at the dig. The dig root is parked
        // 1000 units from the island, so roaming never pays for this at all.
        private const float CoverActiveRange = 60f;

        private SpriteRenderer _skyBand;
        private SpriteRenderer _soilBand;
        private SpriteRenderer _wingLeft;
        private SpriteRenderer _wingRight;
        private Camera _coverCamera;
        private Rect _coveredView = new Rect(-1f, -1f, -1f, -1f);
        private Color _backdropTint = Color.white;

        private static Sprite _coverQuad;

        // ------------------------------------------------------------ TEST HOOKS

        /// <summary>TEST HOOK. The backdrop sprite's own world rect — the art that exists.</summary>
        internal Rect TestBackdropArtRect => RendererRect(_background);

        internal Rect TestSkyBandRect => RendererRect(_skyBand);
        internal Rect TestSoilBandRect => RendererRect(_soilBand);
        internal Rect TestWingLeftRect => RendererRect(_wingLeft);
        internal Rect TestWingRightRect => RendererRect(_wingRight);

        /// <summary>TEST HOOK. Does the live backdrop (art + wings + bands) cover
        /// <paramref name="view"/>? The invariant the framing cases cannot see.</summary>
        internal bool TestBackdropCovers(Rect view)
        {
            return CoverageContains(TestBackdropArtRect, TestSkyBandRect, TestSoilBandRect,
                TestWingLeftRect, TestWingRightRect, view);
        }

        private static Rect RendererRect(SpriteRenderer sr)
        {
            if (sr == null || !sr.enabled || sr.sprite == null)
            {
                return default;
            }

            Bounds b = sr.bounds;
            return Rect.MinMaxRect(b.min.x, b.min.y, b.max.x, b.max.y);
        }

        // -------------------------------------------------------------- geometry

        /// <summary>
        /// PURE. Where the three covering pieces go, given the art's world rect and the camera
        /// rect. Split out from the renderers so the invariant is provable at aspects no editor
        /// can be resized to.
        ///
        /// By construction the four rects plus <paramref name="art"/> contain
        /// <paramref name="view"/>: the bands span the full view width and reach from the art's
        /// horizontal edges to the view's, and each wing is at least as wide as the gap between
        /// the art's side and the view's. An empty (zero-size) rect comes back for a piece that
        /// has nothing to do, which is the 16:10 case for all four — the shipped framing needs
        /// no coverage at all, so nothing is drawn and the shipped look is untouched.
        /// </summary>
        public static void ComputeBackdropCoverage(Rect art, Rect view, float overscan,
            out Rect sky, out Rect soil, out Rect wingLeft, out Rect wingRight)
        {
            float pad = Mathf.Max(0f, overscan);
            float x0 = view.xMin - pad;
            float x1 = view.xMax + pad;

            sky = view.yMax > art.yMax
                ? Rect.MinMaxRect(x0, art.yMax, x1, view.yMax + pad)
                : default;

            soil = view.yMin < art.yMin
                ? Rect.MinMaxRect(x0, view.yMin - pad, x1, art.yMin)
                : default;

            // A wing is never narrower than the art (so the mirror reads 1:1 in every framing a
            // real device produces) and never narrower than the gap it has to fill.
            float artW = Mathf.Max(0.01f, art.width);

            float gapLeft = art.xMin - x0;
            wingLeft = gapLeft > 0f
                ? Rect.MinMaxRect(art.xMin - Mathf.Max(artW, gapLeft), art.yMin, art.xMin, art.yMax)
                : default;

            float gapRight = x1 - art.xMax;
            wingRight = gapRight > 0f
                ? Rect.MinMaxRect(art.xMax, art.yMin, art.xMax + Mathf.Max(artW, gapRight), art.yMax)
                : default;
        }

        /// <summary>PURE. Is every point of <paramref name="view"/> painted by the art, the two
        /// bands or the two wings? Stated as the three conditions that actually have to hold
        /// rather than by sampling, so a failure says WHICH edge leaked.</summary>
        public static bool CoverageContains(Rect art, Rect sky, Rect soil,
            Rect wingLeft, Rect wingRight, Rect view)
        {
            const float Eps = 0.001f;

            // Above the art: the sky band must span the full view width and reach the top.
            if (view.yMax > art.yMax + Eps
                && !(sky.height > 0f && sky.yMin <= art.yMax + Eps && sky.yMax >= view.yMax - Eps
                     && sky.xMin <= view.xMin + Eps && sky.xMax >= view.xMax - Eps))
            {
                return false;
            }

            // Below the art: likewise for the soil band.
            if (view.yMin < art.yMin - Eps
                && !(soil.height > 0f && soil.yMax >= art.yMin - Eps && soil.yMin <= view.yMin + Eps
                     && soil.xMin <= view.xMin + Eps && soil.xMax >= view.xMax - Eps))
            {
                return false;
            }

            // The art's own horizontal band: art plus wings must span the view.
            float left = wingLeft.width > 0f ? Mathf.Min(art.xMin, wingLeft.xMin) : art.xMin;
            float right = wingRight.width > 0f ? Mathf.Max(art.xMax, wingRight.xMax) : art.xMax;
            return left <= view.xMin + Eps && right >= view.xMax - Eps;
        }

        // --------------------------------------------------------------- runtime

        /// <summary>Re-cover the camera rect. Called every frame the dig is on screen; it writes
        /// nothing unless the view actually moved or changed shape, so the steady state is four
        /// float compares.</summary>
        private void RefreshBackdropCoverage()
        {
            if (_root == null)
            {
                return;
            }

            if (_coverCamera == null)
            {
                _coverCamera = Camera.main;
            }

            // Roaming: the dig root is 1000 units away, so this is the whole cost.
            if (_coverCamera == null || !_coverCamera.orthographic
                || Mathf.Abs(_coverCamera.transform.position.x - _root.position.x) > CoverActiveRange)
            {
                return;
            }

            if (_background == null || _background.sprite == null)
            {
                return;
            }

            Vector3 c = _coverCamera.transform.position;
            Rect view = CameraFraming.VisibleRect(new Vector2(c.x, c.y),
                _coverCamera.orthographicSize, CameraFraming.ScreenAspect);
            if (view == _coveredView)
            {
                return;
            }

            Rect art = RendererRect(_background);
            if (art.width <= 0.01f || art.height <= 0.01f)
            {
                return; // a disabled/empty backdrop has no edges to extend
            }

            _coveredView = view;
            EnsureCoverRenderers();

            ComputeBackdropCoverage(art, view, CoverOverscan,
                out Rect sky, out Rect soil, out Rect wingL, out Rect wingR);

            PlaceBand(_skyBand, sky, SkyColor());
            PlaceBand(_soilBand, soil, SoilColor());
            PlaceWing(_wingLeft, wingL, art, true);
            PlaceWing(_wingRight, wingR, art, false);
        }

        /// <summary>Build the four covering renderers under the dig root, once. They are plain
        /// SpriteRenderers so a placeholder-only run (no imported art) still covers — the bands
        /// need no art at all and the wings simply inherit whatever the backdrop is.</summary>
        private void EnsureCoverRenderers()
        {
            if (_skyBand == null)
            {
                _skyBand = MakeCoverRenderer("BackdropSkyBand", CoverQuad(), BandOrder);
            }

            if (_soilBand == null)
            {
                _soilBand = MakeCoverRenderer("BackdropSoilBand", CoverQuad(), BandOrder);
            }

            if (_wingLeft == null)
            {
                _wingLeft = MakeCoverRenderer("BackdropWingLeft", _background.sprite, WingOrder);
                _wingLeft.flipX = true;
            }

            if (_wingRight == null)
            {
                _wingRight = MakeCoverRenderer("BackdropWingRight", _background.sprite, WingOrder);
                _wingRight.flipX = true;
            }

            // The backdrop sprite can change (a legacy scene wiring itself up late); the wings
            // are only ever a mirror of whatever it is now.
            if (_wingLeft.sprite != _background.sprite)
            {
                _wingLeft.sprite = _background.sprite;
            }

            if (_wingRight.sprite != _background.sprite)
            {
                _wingRight.sprite = _background.sprite;
            }
        }

        private SpriteRenderer MakeCoverRenderer(string name, Sprite sprite, int order)
        {
            Transform existing = _root.Find(name);
            var go = existing != null ? existing.gameObject : new GameObject(name);
            if (existing == null)
            {
                go.transform.SetParent(_root, false);
            }

            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null)
            {
                sr = go.AddComponent<SpriteRenderer>();
            }

            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>Size a flat band to a world rect (its sprite is 1x1, so scale IS size), or
        /// switch it off when there is nothing to cover.</summary>
        private void PlaceBand(SpriteRenderer sr, Rect rect, Color baseColor)
        {
            if (sr == null)
            {
                return;
            }

            if (rect.width <= 0.0001f || rect.height <= 0.0001f)
            {
                sr.enabled = false;
                return;
            }

            sr.enabled = true;
            sr.transform.position = new Vector3(rect.center.x, rect.center.y, _root.position.z);
            sr.transform.localScale = new Vector3(rect.width, rect.height, 1f);
            sr.color = Tinted(baseColor);
        }

        /// <summary>Butt a mirrored copy of the backdrop against one of its own edges. Scale is
        /// 1 (a pixel-exact mirror) in every framing a real device produces; it only stretches
        /// when a window is wider than two backdrops, and a horizontal stretch of horizontal
        /// bands is invisible.</summary>
        private void PlaceWing(SpriteRenderer sr, Rect rect, Rect art, bool left)
        {
            if (sr == null)
            {
                return;
            }

            if (rect.width <= 0.0001f || art.width <= 0.0001f || art.height <= 0.0001f)
            {
                sr.enabled = false;
                return;
            }

            sr.enabled = true;
            sr.transform.position = new Vector3(rect.center.x, rect.center.y, _root.position.z);
            sr.transform.localScale = new Vector3(
                rect.width / art.width, rect.height / art.height, 1f);
            sr.color = _background != null ? _background.color : Color.white;
        }

        /// <summary>The dig's sky/soil colours: the importer's samples of the backdrop's own
        /// flat edge rows, else the measured fallbacks.</summary>
        private Color SkyColor()
        {
            return _lib != null && _lib.DigSkyColor.a > 0.01f ? _lib.DigSkyColor : FallbackSkyColor;
        }

        private Color SoilColor()
        {
            return _lib != null && _lib.DigSoilColor.a > 0.01f ? _lib.DigSoilColor : FallbackSoilColor;
        }

        /// <summary>Bands carry the SAME theme + depth multiply the backdrop does, so a themed
        /// or deep-stratum dig tints all the way out to the screen edge instead of ending in a
        /// rectangle of surface-coloured sky.</summary>
        private Color Tinted(Color baseColor)
        {
            return new Color(
                baseColor.r * _backdropTint.r,
                baseColor.g * _backdropTint.g,
                baseColor.b * _backdropTint.b,
                1f);
        }

        /// <summary>Called by ApplyBackgroundTint: remember the tint and push it through the
        /// covering pieces immediately, so a theme change is not held until the next reframe.</summary>
        private void ApplyCoverageTint(Color tint)
        {
            _backdropTint = tint;

            if (_skyBand != null && _skyBand.enabled)
            {
                _skyBand.color = Tinted(SkyColor());
            }

            if (_soilBand != null && _soilBand.enabled)
            {
                _soilBand.color = Tinted(SoilColor());
            }

            if (_wingLeft != null)
            {
                _wingLeft.color = tint;
            }

            if (_wingRight != null)
            {
                _wingRight.color = tint;
            }
        }

        /// <summary>A 1x1 white sprite for the flat bands, built once. Same idiom as the white
        /// bone fallback — a flat fill must never depend on imported art.</summary>
        private static Sprite CoverQuad()
        {
            if (_coverQuad == null)
            {
                var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                _coverQuad = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            }

            return _coverQuad;
        }
    }
}
