using System.Collections;
using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;
using DinoDigger.Dig;

namespace DinoDigger.Testing
{
    /// <summary>Dig-arm V2 switch coverage (DinoDigger-rrn). Kept in its own partial
    /// file so the shared case file stays merge-friendly while the dig loop is under
    /// concurrent work; the case itself registers in BuildCases like any other.</summary>
    public partial class IntegrationTestRunner
    {
        /// <summary>DigArmV2Swaps: the arm-art switch is safe to flip LIVE mid-dig, in
        /// both directions. V1 digs a tile; the switch flips to V2 mid-site (arm still
        /// renders, V2 sprites mounted, a bite still resolves); flips back to V1 the
        /// same way. The rig skeleton is shared, so this is exactly the guarantee the
        /// demo toggle (DinoDigger/Demo/Dig Arm V2 On|Off) rides on. Ends with the
        /// config restored to its V1 default whatever happened in between.</summary>
        private IEnumerator Case_DigArmV2Swaps(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            GameConfig cfg = gm.TestConfig;
            ctx.Assert(cfg != null, "no GameConfig wired");
            cfg.DigArmVersion = DigArmVersion.V1; // canonical start, whatever a prior run left

            yield return EnterDig(ctx);
            DigModeController dm = gm.TestDigMode;

            ctx.Assert(dm.TestArmV2ArtAvailable,
                "V2 arm art not in the library — run DinoDigger/Import Generated Art " +
                "(digarm2/ sprites exist but PlaceholderLibrary has no Boom2/Stick2/Bucket2)");
            ctx.Assert(dm.TestArmRenders, "arm does not render under V1");
            ctx.Assert(!dm.TestArmV2Mounted, "V2 mounted while config selects V1");

            // One full V1 bite so the site is mid-dig (not pristine) when the swap lands.
            yield return DigOnePlainTile(ctx, dm, "V1");

            // ---- live switch to V2, mid-dig ----
            cfg.DigArmVersion = DigArmVersion.V2;
            dm.RefreshDigArmVersion();
            yield return ctx.WaitFrames(2); // let a render tick pass under the new art
            ctx.Assert(dm.TestArmV2Mounted, "switch to V2 did not mount the V2 sprites");
            ctx.Assert(dm.TestArmRenders, "arm does not render under V2");

            // Bites must still resolve under V2 on the same site.
            yield return DigOnePlainTile(ctx, dm, "V2");
            yield return ctx.WaitUntil(() => dm.TestArmReady, 10f,
                "arm never returned to ready after the V2 bite");

            // ---- and back to V1, still live ----
            cfg.DigArmVersion = DigArmVersion.V1;
            dm.RefreshDigArmVersion();
            yield return ctx.WaitFrames(2);
            ctx.Assert(!dm.TestArmV2Mounted, "switch back to V1 left V2 mounted");
            ctx.Assert(dm.TestArmRenders, "arm does not render after switching back to V1");
            yield return DigOnePlainTile(ctx, dm, "V1-again");

            ctx.Log("V1 bite -> live V2 swap + bite -> live V1 swap + bite, arm rendered throughout");
            cfg.DigArmVersion = DigArmVersion.V1; // leave the serialized default in place
            gm.TestForceRoam();
        }

        /// <summary>Fully crumble one plain (unburied) tile under whichever arm art is
        /// mounted, re-entering the dig if a cascade happened to finish the round (a
        /// finished site hands back to the overworld mid-case; the swap must survive
        /// that too, since the switch is config-level, not site-level).</summary>
        private IEnumerator DigOnePlainTile(TestContext ctx, DigModeController dm, string phase)
        {
            if (!dm.IsOpen)
            {
                yield return EnterDig(ctx);
            }

            DirtTile tile = FindPlainTile(dm);
            if (tile == null)
            {
                tile = dm.TestTileAt(0, 0); // tiny/loaded grids: any top tile will do
            }

            ctx.Assert(tile != null, $"[{phase}] no diggable tile found");
            yield return TapTileUntilDestroyed(ctx, dm, tile);
            ctx.Assert(tile == null || tile.IsDestroyed || !dm.IsOpen,
                $"[{phase}] bite did not resolve (tile survived and site still open)");
        }
    }
}
