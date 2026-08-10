using System.Collections;
using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;
using DinoDigger.Dig;
using DinoDigger.Overworld;
using DinoDigger.UI;

namespace DinoDigger.Testing
{
    /// <summary>
    /// PORTRAIT-FIRST RESPONSIVE FRAMING cases (DinoDigger-kgm / -avw).
    ///
    /// THE PROBLEM WITH TESTING THIS AT ALL: the editor cannot rotate a phone. Play mode runs at
    /// whatever shape the Game view happens to be, so "does the dig fit in portrait" is not a
    /// question the running game can be asked directly.
    ///
    /// THE ANSWER IS THE SAME ONE THE FEATURE USES: framing is a PURE FUNCTION of content and
    /// aspect (<see cref="CameraFraming.ComputeOrtho"/>), and everything that frames anything
    /// reads the screen through ONE SEAM (<see cref="CameraFraming.ScreenSize"/>). So these
    /// cases drive the real production functions with phone-shaped inputs — 9:19.5 portrait and
    /// 19.5:9 landscape, the two ends of a modern handset — and, where behaviour rather than
    /// arithmetic is in question, substitute a portrait screen on the live camera for a few
    /// frames and watch it reframe. No reflection, no parallel maths: every number below comes
    /// out of the same call the game makes.
    ///
    /// Registered from IntegrationTestCases.BuildCases; see IntegrationTestRunner.cs for the
    /// driver.
    /// </summary>
    public partial class IntegrationTestRunner
    {
        // The two ends of a modern handset, and the desktop shape the game was authored at.
        private const float PortraitAspect = 9f / 19.5f;     // 0.4615
        private const float LandscapeAspect = 19.5f / 9f;    // 2.1667
        private const float DesktopAspect = 16f / 9f;

        // A notched phone in portrait: status bar / camera cutout at the top, home indicator at
        // the bottom. These are iPhone-class insets, in pixels.
        private const float PhoneWidthPx = 1170f;
        private const float PhoneHeightPx = 2532f;
        private const float NotchPx = 130f;
        private const float HomeIndicatorPx = 68f;

        /// <summary>Framing margin every content corner must clear. Small on purpose: this is
        /// "nothing is touching the glass", not a composition opinion.</summary>
        private const float FrameSlack = 0.05f;

        // ===================================================== DIG FITS IN PORTRAIT

        /// <summary>
        /// The headline bug: at 9:19.5 the shipped DigOrthoSize 4.2 showed 3.9 world units of
        /// width for a 7-unit grid, so the playfield was cropped and a child could not see where
        /// to dig. Prove the fit-to-content framing puts EVERY grid cell corner, the backhoe body
        /// at both ends of its traverse, and the arm's overhead sweep inside the camera rect —
        /// in portrait AND in landscape, off the same function.
        /// </summary>
        private IEnumerator Case_DigFitsPortrait(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            GameConfig cfg = gm.TestConfig;
            ctx.Assert(cfg != null, "no GameConfig");

            cfg.GetDigGridSize(false, out int rows, out int cols);
            AssertDigFrames(ctx, cfg, rows, cols, cfg.DigOrthoSize, "dig");
            yield return null;
        }

        /// <summary>The mega-fossil pit (DinoDigger-84f) is 7x9 — a whole skeleton laid out — and
        /// it goes through the SAME fit with no constant of its own. Same assertions, bigger
        /// board: if the shared function needed a special case, this is where it would show.</summary>
        private IEnumerator Case_MegaDigFitsPortrait(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            GameConfig cfg = gm.TestConfig;
            ctx.Assert(cfg != null, "no GameConfig");

            cfg.GetDigGridSize(true, out int rows, out int cols);
            ctx.Assert(rows > cfg.DigRows || cols > cfg.DigColumns,
                $"the mega pit ({rows}x{cols}) is not bigger than the standard board — this case " +
                "would be testing the same thing twice");

            AssertDigFrames(ctx, cfg, rows, cols, cfg.DigMegaOrthoSize, "mega dig");
            yield return null;
        }

        /// <summary>Both dig cases' body: frame a rows x cols board at portrait, landscape and
        /// desktop and assert the content is inside the camera rect every time — plus that the
        /// landscape baseline is honoured exactly, so a phone held sideways and a desktop still
        /// see the framing this game shipped with.</summary>
        private void AssertDigFrames(TestContext ctx, GameConfig cfg, int rows, int cols,
            float baseline, string what)
        {
            DigModeController.ComputeDigFrame(rows, cols,
                out float centreY, out float halfW, out float halfH);

            CameraFit fit = CameraFit.Content(halfW, halfH, 0f, baseline, cfg.DigMaxOrthoSize);

            // The dig centre is on the grid's own x (the frame is symmetric about the board).
            var centre = new Vector2(0f, centreY);

            AssertDigContentInside(ctx, rows, cols, centre, fit, PortraitAspect, $"{what} @9:19.5");
            AssertDigContentInside(ctx, rows, cols, centre, fit, LandscapeAspect, $"{what} @19.5:9");
            AssertDigContentInside(ctx, rows, cols, centre, fit, DesktopAspect, $"{what} @16:9");

            // NO LANDSCAPE REGRESSION. On every landscape shape the content is comfortably
            // inside the baseline framing, so the baseline (the shipped number) must win
            // outright — the picture a desktop player sees has to be bit-identical.
            ctx.Assert(Mathf.Approximately(fit.Ortho(DesktopAspect), baseline),
                $"{what} at 16:9 framed at {fit.Ortho(DesktopAspect):F3}, not the shipped {baseline:F3}");
            ctx.Assert(Mathf.Approximately(fit.Ortho(LandscapeAspect), baseline),
                $"{what} at 19.5:9 framed at {fit.Ortho(LandscapeAspect):F3}, not the shipped {baseline:F3}");
            ctx.Assert(fit.Ortho(PortraitAspect) > baseline + 0.5f,
                $"{what} in portrait framed at {fit.Ortho(PortraitAspect):F3} — that is no wider " +
                "than landscape, so the narrow screen is still cropping the board");
        }

        /// <summary>Every corner of every grid cell, the body at both ends of its traverse, and
        /// the arm's overhead crest, inside the camera rect with slack to spare.</summary>
        private void AssertDigContentInside(TestContext ctx, int rows, int cols, Vector2 centre,
            CameraFit fit, float aspect, string label)
        {
            float ortho = fit.Ortho(aspect);
            Rect view = CameraFraming.VisibleRect(centre, ortho, aspect);

            // Grid cells: tile (r,c) is centred at (c - (cols-1)/2, -(r+1)) and is 1 unit square.
            float gridHalf = (cols - 1) * 0.5f;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    var cell = new Vector2(c - gridHalf, -(r + 1));
                    for (int k = 0; k < 4; k++)
                    {
                        var corner = new Vector2(
                            cell.x + ((k & 1) == 0 ? -0.5f : 0.5f),
                            cell.y + ((k & 2) == 0 ? -0.5f : 0.5f));
                        ctx.Assert(CameraFraming.Contains(view, corner, FrameSlack),
                            $"{label}: grid cell [{r},{c}] corner {corner} is outside the camera " +
                            $"rect {view} (ortho {ortho:F2}) — the playfield is clipped");
                    }
                }
            }

            // The machine and its reach: ComputeDigFrame already folded the body's traverse and
            // the arm's overhead sweep into the extents, so asserting the VIEW covers those
            // extents is asserting the machine is in shot. (A frame that held the grid but
            // cropped the excavator would look just as broken as one that cropped the grid.)
            // Compared as half-extents with an epsilon rather than as points, because the two
            // sides are the same number arrived at by different arithmetic.
            const float Eps = 0.001f;
            ctx.Assert(ortho * aspect >= fit.HalfWidth - Eps,
                $"{label}: the view is {ortho * aspect:F3} half-units wide but the machine's " +
                $"traverse needs {fit.HalfWidth:F3}");
            ctx.Assert(ortho >= fit.HalfHeight - Eps,
                $"{label}: the view is {ortho:F3} half-units tall but the body roof and the arm's " +
                $"overhead sweep need {fit.HalfHeight:F3}");
        }

        // ===================================================== ROAM ZOOMS OUT IN PORTRAIT

        /// <summary>
        /// Greg's overworld ask: portrait should be "a bit more zoomed out than now". Today it is
        /// the opposite — the fixed 5.5 ortho shows 5.1 world units of width at 9:19.5 against
        /// 19.6 on a 16:9 desktop, i.e. a quarter of the island through a letterbox slot.
        ///
        /// Assert three things about the replacement, in the order they matter:
        ///   PORTRAIT SEES MORE THAN IT USED TO — wider AND taller than the shipped fixed size.
        ///   PORTRAIT SEES AT LEAST AS MUCH WORLD AS LANDSCAPE. It cannot do that by width on a
        ///     screen four times taller than it is wide, so the measure is how much island is on
        ///     the glass. The target width is tuned to twice the baseline, which makes the two
        ///     areas come out EQUAL at complementary aspects: turning the phone changes the
        ///     shape of the view, not the amount of world in it.
        ///   LANDSCAPE DID NOT MOVE — every aspect from 1:1 up frames exactly 5.5, as shipped.
        /// </summary>
        private IEnumerator Case_RoamZoomsOutInPortrait(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            GameConfig cfg = gm.TestConfig;
            ctx.Assert(cfg != null, "no GameConfig");

            CameraFit fit = cfg.RoamFit();
            float shipped = cfg.RoamOrthoSize;

            float portrait = fit.Ortho(PortraitAspect);
            float landscape = fit.Ortho(LandscapeAspect);
            float desktop = fit.Ortho(DesktopAspect);

            float portraitW = CameraFraming.VisibleWidth(portrait, PortraitAspect);
            float landscapeW = CameraFraming.VisibleWidth(landscape, LandscapeAspect);
            float wasPortraitW = CameraFraming.VisibleWidth(shipped, PortraitAspect);

            // (1) Portrait zooms OUT relative to what ships today.
            ctx.Assert(portrait > shipped + 0.5f,
                $"portrait still frames at {portrait:F2} against the shipped {shipped:F2} — that " +
                "is not zoomed out");
            ctx.Assert(portraitW >= wasPortraitW * 1.5f,
                $"portrait shows {portraitW:F2} units of width against {wasPortraitW:F2} today — " +
                "barely more world than the slot Greg is complaining about");

            // (2) Portrait shows at least as much world as landscape. Measured as area, because
            // a 9:19.5 screen cannot win on width against a 19.5:9 one at any sane zoom — and
            // by construction the two come out equal, so the compare carries a hair of slack
            // for the float arithmetic rather than for the design.
            ctx.Assert(portrait > landscape,
                $"portrait ({portrait:F2}) frames no further out than landscape ({landscape:F2})");
            float portraitArea = portraitW * CameraFraming.VisibleHeight(portrait);
            float landscapeArea = landscapeW * CameraFraming.VisibleHeight(landscape);
            ctx.Assert(portraitArea >= landscapeArea * 0.999f,
                $"portrait shows {portraitArea:F1} square units of island against landscape's " +
                $"{landscapeArea:F1} — portrait is the primary play style and is seeing less");

            // (3) No landscape regression, at any landscape shape.
            ctx.Assert(Mathf.Approximately(desktop, shipped),
                $"16:9 now frames at {desktop:F3}, not the shipped {shipped:F3}");
            ctx.Assert(Mathf.Approximately(landscape, shipped),
                $"19.5:9 now frames at {landscape:F3}, not the shipped {shipped:F3}");
            ctx.Assert(Mathf.Approximately(fit.Ortho(4f / 3f), shipped),
                $"4:3 now frames at {fit.Ortho(4f / 3f):F3}, not the shipped {shipped:F3}");
            ctx.Assert(Mathf.Approximately(fit.Ortho(16f / 10f), shipped),
                $"16:10 now frames at {fit.Ortho(16f / 10f):F3}, not the shipped {shipped:F3}");

            // The ceremony / attract push-in follows the same rule: untouched in landscape,
            // widened in portrait so the machine and the new baby both stay in shot.
            CameraFit ceremony = cfg.CeremonyFit();
            ctx.Assert(Mathf.Approximately(ceremony.Ortho(DesktopAspect), cfg.CeremonyOrthoSize),
                "the ceremony push-in changed shape on a 16:9 screen");
            ctx.Assert(ceremony.Ortho(PortraitAspect) > cfg.CeremonyOrthoSize + 0.5f,
                "the ceremony push-in still crops the machine + baby pair in portrait");

            ctx.Detail = $"portrait {portraitW:F1}x{CameraFraming.VisibleHeight(portrait):F1} " +
                         $"(was {wasPortraitW:F1}x{CameraFraming.VisibleHeight(shipped):F1}), " +
                         $"landscape {landscapeW:F1}x{CameraFraming.VisibleHeight(landscape):F1}";
            yield return null;
        }

        // ===================================================== LIVE REFRAMING

        /// <summary>
        /// Reactivity, which is the half of this that arithmetic cannot prove: a WebGL canvas
        /// resizes when the browser rotates, and a camera that only framed itself at boot stays
        /// wrong until the next scene load. Substitute a portrait screen on the LIVE camera and
        /// assert it reframes within a couple of frames — while roaming, and (the nastier case)
        /// halfway through a transition tween, which must still LAND on the size the new
        /// orientation asks for rather than the one it was aiming at when it set off.
        /// </summary>
        private IEnumerator Case_AspectChangeReframesLive(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            CameraFollow cam = gm.TestCameraFollow;
            GameConfig cfg = gm.TestConfig;
            ctx.Assert(cam != null && cfg != null, "no CameraFollow/GameConfig");

            // Slow the glide down for the mid-tween half: the point is to change the screen
            // WHILE the camera is flying, and at the shipped 0.5s (0.17s of realtime under the
            // suite's 3x timescale) a hitchy frame could land after it had already arrived.
            // Restored in the finally, because GameConfig is an asset and a leaked value would
            // outlive play mode.
            float shippedTransition = cfg.TransitionSeconds;

            try
            {
                // ---- (1) Roaming: rotate, and the island reframes. ----
                CameraFraming.TestSetScreen(PhoneHeightPx, PhoneWidthPx);   // landscape phone
                yield return ctx.WaitFrames(3);
                float wide = cam.TestOrthoSize;
                ctx.Assert(Mathf.Abs(wide - cfg.RoamFit().Ortho(LandscapeAspect)) < 0.05f,
                    $"landscape roam framed at {wide:F3}, expected " +
                    $"{cfg.RoamFit().Ortho(LandscapeAspect):F3}");

                CameraFraming.TestSetScreen(PhoneWidthPx, PhoneHeightPx);   // ...now turn it
                yield return ctx.WaitFrames(3);
                float tall = cam.TestOrthoSize;
                ctx.Assert(Mathf.Abs(tall - cfg.RoamFit().Ortho(PortraitAspect)) < 0.05f,
                    $"portrait roam framed at {tall:F3} after a rotate, expected " +
                    $"{cfg.RoamFit().Ortho(PortraitAspect):F3} — the camera is not reacting to " +
                    "the screen, only to boot");
                ctx.Assert(tall > wide,
                    $"rotating to portrait framed TIGHTER ({tall:F2} against {wide:F2})");

                // ---- (2) Mid-tween: rotate DURING a camera glide. ----
                // EnterFocus is the attract tour's own path (it changes no game state), so this
                // exercises a real transition without disturbing anything the next case reads.
                CameraFraming.TestSetScreen(PhoneHeightPx, PhoneWidthPx);
                cfg.TransitionSeconds = 1.5f;
                yield return ctx.WaitFrames(3);

                bool arrived = false;
                cam.EnterFocus(cam.transform.position + new Vector3(1.5f, 0f, 0f), () => arrived = true);
                yield return ctx.WaitFrames(1);
                ctx.Assert(cam.TestTransitioning, "EnterFocus did not start a transition");

                // Turn the phone while the camera is still flying.
                CameraFraming.TestSetScreen(PhoneWidthPx, PhoneHeightPx);

                float deadline = Time.realtimeSinceStartup + 6f;
                while (!arrived && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                ctx.Assert(arrived, "the focus transition never finished");
                yield return ctx.WaitFrames(2);

                float want = cfg.CeremonyFit().Ortho(PortraitAspect);
                ctx.Assert(Mathf.Abs(cam.TestOrthoSize - want) < 0.05f,
                    $"a rotate mid-glide landed on {cam.TestOrthoSize:F3}, expected {want:F3} — " +
                    "the tween is easing toward a size it captured before the rotate");

                // ---- (3) Back out, and the roam framing is portrait-correct too. ----
                bool back = false;
                cam.ExitFocus(() => back = true);
                deadline = Time.realtimeSinceStartup + 6f;
                while (!back && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                ctx.Assert(back, "the focus exit never finished");
                yield return ctx.WaitFrames(2);
                ctx.Assert(Mathf.Abs(cam.TestOrthoSize - cfg.RoamFit().Ortho(PortraitAspect)) < 0.05f,
                    $"after ExitFocus in portrait the roam framing is {cam.TestOrthoSize:F3}, " +
                    $"expected {cfg.RoamFit().Ortho(PortraitAspect):F3}");
            }
            finally
            {
                // Always hand the real screen (and the shipped glide) back — a leaked override
                // would reframe every case after this one, and a leaked transition length would
                // re-time them (the runner clears the screen again as a backstop).
                cfg.TransitionSeconds = shippedTransition;
                CameraFraming.TestClearScreen();
                cam.TestForceRoam();
            }

            yield return ctx.WaitFrames(2);
        }

        // ===================================================== PORTRAIT HUD

        /// <summary>
        /// The HUD half (DinoDigger-avw). uGUI derives its canvas from the REAL window, which no
        /// test can turn, so this drives the two pure decisions with a notched portrait phone and
        /// then checks the live wiring that applies them:
        ///   the scaler's reference resolution turns on its side (so a 220-unit counter does not
        ///     eat a fifth of a narrow screen);
        ///   the safe-area rect matches the notch and the home indicator exactly;
        ///   every HUD affordance is INSIDE that rect (parented to it, so it insets for free);
        ///   and the full-screen board reflows so all five cards are on the glass at once —
        ///     asserted through the real layout code, at a portrait frame.
        /// </summary>
        private IEnumerator Case_PortraitHudOnScreen(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            SkeletonBoard board = gm.TestSkeletonBoard;
            ctx.Assert(board != null, "no SkeletonBoard");

            ResponsiveCanvas hud = gm.TestResponsiveCanvas;
            ctx.Assert(hud != null && hud.SafeArea != null,
                "no ResponsiveCanvas — GameManager should ensure one on the HUD canvas at boot");

            // ---- (1) HUD affordances live inside the safe rect. ----
            ctx.Assert(gm.TestTreasureCounter != null
                && gm.TestTreasureCounter.transform.parent == hud.SafeArea,
                "the treasure counter is not parented into the safe area — a notch can eat it");
            ctx.Assert(gm.TestMuteButton != null
                && gm.TestMuteButton.transform.parent == hud.SafeArea,
                "the parent-gate mute button is not parented into the safe area");
            ctx.Assert(board.TestButtonRect != null
                && board.TestButtonRect.parent == hud.SafeArea,
                "the bone-board button is not parented into the safe area");

            try
            {
                // ---- (2) A notched phone, upright. ----
                var safe = new Rect(0f, HomeIndicatorPx, PhoneWidthPx,
                    PhoneHeightPx - NotchPx - HomeIndicatorPx);
                CameraFraming.TestSetScreen(PhoneWidthPx, PhoneHeightPx, safe);
                yield return ctx.WaitFrames(3);

                ctx.Assert(hud.TestReference == ResponsiveUI.PortraitReference,
                    $"portrait canvas reference is {hud.TestReference}, expected " +
                    $"{ResponsiveUI.PortraitReference} — a landscape reference mis-scales the HUD");

                ResponsiveUI.SafeAreaAnchors(safe, new Vector2(PhoneWidthPx, PhoneHeightPx),
                    out Vector2 min, out Vector2 max);
                ctx.Assert((hud.TestAnchorMin - min).sqrMagnitude < 1e-6f
                    && (hud.TestAnchorMax - max).sqrMagnitude < 1e-6f,
                    $"safe-area anchors are {hud.TestAnchorMin}..{hud.TestAnchorMax}, expected " +
                    $"{min}..{max}");
                ctx.Assert(min.y > 0.001f && max.y < 0.999f,
                    "the safe area did not inset for the notch/home indicator at all");

                // Landscape hands the shipped reference straight back.
                CameraFraming.TestSetScreen(PhoneHeightPx, PhoneWidthPx);
                yield return ctx.WaitFrames(3);
                ctx.Assert(hud.TestReference == ResponsiveUI.LandscapeReference,
                    $"landscape canvas reference changed to {hud.TestReference}");
            }
            finally
            {
                CameraFraming.TestClearScreen();
            }

            yield return ctx.WaitFrames(2);

            // ---- (3) The full-screen modal reflows. ----
            // Landscape first: the shipped 5-in-a-row tray, to the unit.
            board.TestLayoutFor(new Rect(-960f, -540f, 1920f, 1080f));
            ctx.Assert(board.TestTrayColumns == board.TestCardCount && board.TestTrayRows == 1,
                $"landscape packs the board {board.TestTrayColumns}x{board.TestTrayRows}, not " +
                $"{board.TestCardCount}x1 — the desktop layout moved");
            ctx.Assert(Mathf.Approximately(board.TestTrayScale, 1f)
                && board.TestTraySize == new Vector2(1760f, 540f),
                $"landscape tray is {board.TestTraySize} at {board.TestTrayScale:F3}x, expected " +
                "(1760, 540) at 1x");

            // Now a portrait canvas: the same cards, wrapped, and every one of them on screen.
            var frame = new Rect(-490f, -1059f, 980f, 2118f);   // 9:19.5 in portrait reference units
            board.TestLayoutFor(frame);
            ctx.Assert(board.TestTrayRows > 1,
                "the board is still one row wide in portrait — the outer cards are off screen");

            for (int i = 0; i < board.TestCardCount; i++)
            {
                Rect card = board.TestCardRect(i);
                ctx.Assert(card.width > 1f, $"card {i} has no rect");
                ctx.Assert(card.xMin >= frame.xMin - 0.5f && card.xMax <= frame.xMax + 0.5f
                    && card.yMin >= frame.yMin - 0.5f && card.yMax <= frame.yMax + 0.5f,
                    $"card {i} at {card} is outside the portrait frame {frame} — a child cannot " +
                    "see their own collection");
            }

            ctx.Detail = $"portrait board {board.TestTrayColumns}x{board.TestTrayRows} at " +
                         $"{board.TestTrayScale:F2}x";

            // Restore the layout the rest of the suite (and the editor) is looking at.
            board.TestLayoutFor(new Rect(-960f, -540f, 1920f, 1080f));
            yield return null;
        }

        // ===================================================== COVERAGE (DinoDigger-5k8.1)

        /// <summary>
        /// THE INVARIANT THE FRAMING CASES CANNOT SEE. Framing decides what the camera looks at;
        /// COVERAGE is whether anything is painted there. They are independent, and fixing the
        /// first exposed a gap in the second: the dig backdrop is ONE 14x14-unit sprite, so a
        /// portrait dig rect (11.80 x 25.57) ran 4.46 units off the top and 7.10 off the bottom.
        ///
        /// It was never only a portrait problem either. The sprite is +-7 across, and 16:9 has
        /// always needed 7.47 and 19.5:9 9.10; the MEGA pit needs 9.28 at 16:10 and reaches 1.12
        /// units below the art's bottom edge at EVERY landscape aspect. Those were shipped bugs
        /// that no case could fail on, because every case asserted framing.
        ///
        /// So: drive the real coverage geometry at five aspects for both board sizes and assert
        /// the union of art + wings + bands CONTAINS the camera rect — then open an actual dig
        /// and assert it of the live renderers, before and after a rotate. At 16:10, the shape
        /// this game was composed at, assert the standard dig draws NO covering pieces at all,
        /// so the shipped look is provably untouched.
        /// </summary>
        private IEnumerator Case_DigBackdropCoversView(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            GameConfig cfg = gm.TestConfig;
            ctx.Assert(cfg != null, "no GameConfig");

            // The backdrop's world rect about the dig origin: a 14-unit square placed so its
            // painted grass lip (48% down the image) lands on the surface line at +0.1.
            const float ArtSize = 14f;
            const float LipFraction = 0.48f;
            float artCentreY = 0.1f - ArtSize * (0.5f - LipFraction);
            Rect art = Rect.MinMaxRect(-ArtSize * 0.5f, artCentreY - ArtSize * 0.5f,
                                        ArtSize * 0.5f, artCentreY + ArtSize * 0.5f);

            float[] aspects = { PortraitAspect, 4f / 3f, 1.6f, DesktopAspect, LandscapeAspect };
            string[] names = { "9:19.5", "4:3", "16:10", "16:9", "19.5:9" };

            for (int m = 0; m < 2; m++)
            {
                bool mega = m == 1;
                cfg.GetDigGridSize(mega, out int rows, out int cols);
                DigModeController.ComputeDigFrame(rows, cols,
                    out float centreY, out float halfW, out float halfH);
                CameraFit fit = CameraFit.Content(halfW, halfH, 0f,
                    mega ? cfg.DigMegaOrthoSize : cfg.DigOrthoSize, cfg.DigMaxOrthoSize);

                for (int i = 0; i < aspects.Length; i++)
                {
                    Rect view = CameraFraming.VisibleRect(new Vector2(0f, centreY),
                        fit.Ortho(aspects[i]), aspects[i]);
                    DigModeController.ComputeBackdropCoverage(art, view, 0.6f,
                        out Rect sky, out Rect soil, out Rect wingL, out Rect wingR);
                    ctx.Assert(
                        DigModeController.CoverageContains(art, sky, soil, wingL, wingR, view),
                        $"{(mega ? "mega" : "dig")} at {names[i]}: the backdrop does not cover the " +
                        $"camera rect {view} (art {art}) — the screen shows nothing there");
                }
            }

            // 16:10 is the shape this game was composed at: the standard dig fits inside the
            // sprite outright, so with no overscan there is nothing to cover — i.e. no covering
            // piece is ever ON SCREEN there and the shipped composition is untouched. (At
            // runtime the overscan pushes slivers of band/wing just past the view edge on
            // purpose, so a rotate cannot flash a hairline; those are off screen by definition.)
            {
                cfg.GetDigGridSize(false, out int rows, out int cols);
                DigModeController.ComputeDigFrame(rows, cols,
                    out float centreY, out float halfW, out float halfH);
                CameraFit fit = CameraFit.Content(halfW, halfH, 0f, cfg.DigOrthoSize, cfg.DigMaxOrthoSize);
                Rect view = CameraFraming.VisibleRect(new Vector2(0f, centreY), fit.Ortho(1.6f), 1.6f);
                DigModeController.ComputeBackdropCoverage(art, view, 0f,
                    out Rect sky, out Rect soil, out Rect wingL, out Rect wingR);
                ctx.Assert(sky.height <= 0f && soil.height <= 0f
                    && wingL.width <= 0f && wingR.width <= 0f,
                    "the 16:10 dig is drawing covering pieces it does not need — the shipped " +
                    "composition has moved");
            }

            // ---- Live: open a real dig and check the RENDERERS, not just the arithmetic. ----
            yield return EnterDig(ctx);
            yield return ctx.WaitFrames(3);

            DigModeController dm = gm.TestDigMode;
            Camera cam = gm.TestCamera;
            ctx.Assert(dm != null && cam != null, "no dig/camera after entering a dig");
            ctx.Assert(dm.TestBackdropArtRect.width > 1f,
                "the dig has no backdrop renderer to extend");
            // The geometry above assumed a 14-unit square; if the art is re-imported at another
            // size, say so here rather than letting the pure half of this case quietly test a
            // backdrop the game does not have.
            ctx.Assert(Mathf.Abs(dm.TestBackdropArtRect.width - ArtSize) < 1f
                && Mathf.Abs(dm.TestBackdropArtRect.height - ArtSize) < 1f,
                $"the live backdrop is {dm.TestBackdropArtRect.size} — this case's geometry " +
                $"assumes {ArtSize}x{ArtSize} (GeneratedArtImporter.DigBgTargetW)");

            Rect live = CameraFraming.VisibleRect(
                new Vector2(cam.transform.position.x, cam.transform.position.y),
                cam.orthographicSize, CameraFraming.ScreenAspect);
            ctx.Assert(dm.TestBackdropCovers(live),
                $"the LIVE dig backdrop (art {dm.TestBackdropArtRect}, sky {dm.TestSkyBandRect}, " +
                $"soil {dm.TestSoilBandRect}, wings {dm.TestWingLeftRect}/{dm.TestWingRightRect}) " +
                $"does not cover the camera rect {live}");

            // ...and again after a rotate mid-dig, which is the whole reason it is reactive.
            try
            {
                CameraFraming.TestSetScreen(PhoneWidthPx, PhoneHeightPx);
                yield return ctx.WaitFrames(4);
                Rect portrait = CameraFraming.VisibleRect(
                    new Vector2(cam.transform.position.x, cam.transform.position.y),
                    cam.orthographicSize, CameraFraming.ScreenAspect);
                ctx.Assert(dm.TestBackdropCovers(portrait),
                    $"after rotating to portrait mid-dig the backdrop does not cover {portrait} " +
                    $"(sky {dm.TestSkyBandRect}, soil {dm.TestSoilBandRect})");
                ctx.Detail = $"portrait dig view {portrait.width:F1}x{portrait.height:F1} covered";
            }
            finally
            {
                CameraFraming.TestClearScreen();
            }

            yield return ctx.WaitFrames(3);
        }

        /// <summary>
        /// The overworld half of the same invariant. The island is a 48x48 isometric diamond
        /// reaching |x| + 2|y - 11.75| &lt;= 23.5, and a camera rect of half-extents (hw, hh)
        /// only fits inside it where |cx| + 2|cy - 11.75| &lt;= 23.5 - (hw + 2*hh). A portrait
        /// roam view spends 29.33 of that 23.50 budget, so THERE IS NO CAMERA POSITION THAT
        /// FITS — no clamp can hold this; and even at 19.5:9 the budget leaves 0.58 units of
        /// slack, which would pin the camera to the island's exact centre. More painted world
        /// is the only answer, and the cheapest honest painted world is open sea.
        ///
        /// So the assertion is not "the view stays inside the map" (it provably cannot be) but
        /// "whatever the view reaches is painted": the backstop quad rides the camera and covers
        /// its rect at every aspect, and the clear colour behind it is the same sea.
        /// </summary>
        private IEnumerator Case_SeaCoversBeyondTheMap(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            GameConfig cfg = gm.TestConfig;
            Camera cam = gm.TestCamera;
            ctx.Assert(cfg != null && cam != null, "no GameConfig/camera");

            CameraBackdrop sea = cam.GetComponentInChildren<CameraBackdrop>(true);
            ctx.Assert(sea != null,
                "no CameraBackdrop on the camera — nothing guarantees the view is painted");
            ctx.Assert(sea.TestRenderer != null && sea.TestRenderer.sortingOrder < 0,
                "the sea backstop is not drawn behind the world");

            // The clear colour must agree with the backstop, or the first frame after a resize
            // (before LateUpdate re-sizes the quad) would flash a different blue.
            Color clear = cam.backgroundColor;
            ctx.Assert(Mathf.Abs(clear.r - cfg.SeaColor.r) < 0.01f
                && Mathf.Abs(clear.g - cfg.SeaColor.g) < 0.01f
                && Mathf.Abs(clear.b - cfg.SeaColor.b) < 0.01f,
                $"the camera clears to {clear} but the sea is {cfg.SeaColor}");

            try
            {
                float[,] screens =
                {
                    { PhoneWidthPx, PhoneHeightPx },
                    { 1920f, 1080f },
                    { PhoneHeightPx, PhoneWidthPx },
                };
                string[] names = { "9:19.5", "16:9", "19.5:9" };

                for (int i = 0; i < names.Length; i++)
                {
                    CameraFraming.TestSetScreen(screens[i, 0], screens[i, 1]);
                    yield return ctx.WaitFrames(3);

                    Rect view = CameraFraming.VisibleRect(
                        new Vector2(cam.transform.position.x, cam.transform.position.y),
                        cam.orthographicSize, CameraFraming.ScreenAspect);
                    Rect painted = sea.TestWorldRect;
                    ctx.Assert(painted.xMin <= view.xMin && painted.xMax >= view.xMax
                        && painted.yMin <= view.yMin && painted.yMax >= view.yMax,
                        $"at {names[i]} the sea backstop {painted} does not cover the camera " +
                        $"rect {view} — the screen has an unpainted region");
                }

                ctx.Detail = "sea backstop covers roam at 9:19.5 / 16:9 / 19.5:9";
            }
            finally
            {
                CameraFraming.TestClearScreen();
            }

            yield return ctx.WaitFrames(3);
        }
    }
}
