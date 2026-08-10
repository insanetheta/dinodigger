using UnityEditor;
using UnityEngine;

namespace DinoDigger.EditorTools
{
    /// <summary>
    /// The DIG feel harness (DinoDigger-73a). Play-mode-only helpers that put a live dig site
    /// into whatever state the cascade needs to be judged by eye: enter a dig, plant each toy at
    /// a known cell, collapse a column on demand, and slow the whole thing to a quarter speed so
    /// a fall can be watched frame by frame.
    ///
    /// Like DemoTownMenu, everything here pokes only PUBLIC members of Assembly-CSharp — editor
    /// scripts live in a separate assembly and cannot reach the internal Test* hooks — which is
    /// why DigModeController carries a small public Demo* surface alongside them.
    ///
    /// Every item is null-tolerant and no-ops with a warning outside play mode or outside a dig,
    /// so a stray click while roaming the overworld does nothing at all.
    ///
    /// FRAMING FOR CAPTURE: the dig site is built at the dig root, far from the overworld —
    /// "Log Dig Framing" prints its exact world position and the camera's ortho size so a
    /// scene-view capture can be pointed straight at it.
    /// </summary>
    public static class DemoDigMenu
    {
        private const string Menu = "DinoDigger/Demo/Dig/";

        [MenuItem(Menu + "Enter First Dig")]
        public static void EnterFirstDig()
        {
            if (!Ready("Enter First Dig"))
            {
                return;
            }

            var gm = DinoDigger.Core.GameManager.Instance;
            if (gm == null)
            {
                Debug.LogWarning("[Demo/Dig] GameManager.Instance is null — is the Main scene loaded?");
                return;
            }

            var dig = Object.FindFirstObjectByType<DinoDigger.Dig.DigModeController>();
            if (dig != null && dig.IsOpen)
            {
                Debug.Log("[Demo/Dig] A dig site is already open — nothing to do.");
                return;
            }

            DinoDigger.Overworld.DigMound mound = NearestMound(gm);
            if (mound == null)
            {
                Debug.LogWarning("[Demo/Dig] No active dig mound on the island to enter.");
                return;
            }

            // Straight in, rather than driving the backhoe across the island: this is a framing
            // harness, and a 15-second commute between clicking and seeing anything is exactly
            // the friction it exists to remove. EnterDig is the same public entry point the
            // backhoe calls when it arrives, so the site is built identically.
            gm.EnterDig(mound);
            Debug.Log($"[Demo/Dig] Entered the dig at mound {mound.transform.position}. " +
                      "Give the camera ~0.5s to ease in, then Log Dig Framing for the capture.");
        }

        [MenuItem(Menu + "Spawn Crystal Cluster")]
        public static void SpawnCrystalCluster()
        {
            var dig = ActiveDig("Spawn Crystal Cluster");
            if (dig == null)
            {
                return;
            }

            // Rotate the colour each click so repeated clusters are tappable separately (and so
            // a second cluster landing beside the first does not auto-pop on the next settle).
            int color = Mathf.Abs((int)(EditorApplication.timeSinceStartup * 3d)) % 3;
            int cells = dig.DemoSpawnCrystalCluster(color);
            if (cells > 0)
            {
                Debug.Log($"[Demo/Dig] Planted a {cells}-cell crystal cluster (colour {color}). " +
                          "Tap any of them to pop the whole blob.");
            }
            else
            {
                Debug.LogWarning("[Demo/Dig] No 2x2 patch of plain dirt left for a crystal cluster.");
            }
        }

        [MenuItem(Menu + "Spawn Boom Geode")]
        public static void SpawnBoomGeode()
        {
            var dig = ActiveDig("Spawn Boom Geode");
            if (dig == null)
            {
                return;
            }

            if (dig.DemoSpawnGeode())
            {
                Debug.Log("[Demo/Dig] Planted a boom geode near the middle of the board. " +
                          "Tap it (or drop a tile on it) for the fuse, then the 3x3 whumph.");
            }
            else
            {
                Debug.LogWarning("[Demo/Dig] No free cell left for a boom geode.");
            }
        }

        [MenuItem(Menu + "Spawn Pinata Pot")]
        public static void SpawnPinataPot()
        {
            var dig = ActiveDig("Spawn Pinata Pot");
            if (dig == null)
            {
                return;
            }

            if (dig.DemoSpawnPot())
            {
                Debug.Log("[Demo/Dig] Planted a pinata pot near the middle of the board. " +
                          "Two taps: crack, then the coin fountain.");
            }
            else
            {
                Debug.LogWarning("[Demo/Dig] No free cell left for a pinata pot.");
            }
        }

        [MenuItem(Menu + "Trigger Column Collapse")]
        public static void TriggerColumnCollapse()
        {
            var dig = ActiveDig("Trigger Column Collapse");
            if (dig == null)
            {
                return;
            }

            if (dig.DemoCollapseColumn())
            {
                Debug.Log("[Demo/Dig] Cleared a bottom-middle tile — watch the column tumble and " +
                          "crack whatever it lands on.");
            }
            else
            {
                Debug.LogWarning("[Demo/Dig] Nothing left to clear — the board is empty.");
            }
        }

        // THE WAY DOWN (DinoDigger-n05). The descent is the one dig beat that can only be judged
        // by eye — the question it has to answer is "does this read as going DOWN?", and the
        // shipped answer was "no, it reads as night falling on the same hole". Reaching it
        // through play took ~60% of a board cleared, which is far too much friction for a
        // flourish that has to be watched a dozen times while it is tuned.
        [MenuItem(Menu + "Offer Ladder Down")]
        public static void OfferLadderDown()
        {
            var dig = ActiveDig("Offer Ladder Down");
            if (dig == null)
            {
                return;
            }

            if (dig.DemoOfferLadder())
            {
                Debug.Log("[Demo/Dig] The way down is standing in the pit — the wooden ladder " +
                          "with the chevron nodding under it. Tap it, or use Descend One Layer.");
            }
            else
            {
                Debug.LogWarning("[Demo/Dig] No ladder offered: the board may have no empty cell " +
                                 "yet (collapse a column first), or this is already the deepest " +
                                 "layer / a mega-fossil site, which never offer one.");
            }
        }

        [MenuItem(Menu + "Descend One Layer")]
        public static void DescendOneLayer()
        {
            var dig = ActiveDig("Descend One Layer");
            if (dig == null)
            {
                return;
            }

            int from = dig.DemoLayer;
            if (dig.DemoDescend())
            {
                Debug.Log($"[Demo/Dig] Descending from layer {from} — watch the ladder climb away " +
                          "above, the strata stream up past the frame and the dirt puff at its " +
                          "foot while the camera dips.");
            }
            else
            {
                Debug.LogWarning($"[Demo/Dig] No descent from layer {from} — a layer is a one-way " +
                                 "door and the deepest stratum has no door at all.");
            }
        }

        [MenuItem(Menu + "Slow Motion 0.25x")]
        public static void SlowMotion()
        {
            if (!Ready("Slow Motion 0.25x"))
            {
                return;
            }

            Time.timeScale = 0.25f;
            Debug.Log("[Demo/Dig] timeScale 0.25 — every fall, squash and pop plays at a quarter " +
                      "speed. Use Normal Speed to put it back (it does NOT reset itself).");
        }

        [MenuItem(Menu + "Normal Speed")]
        public static void NormalSpeed()
        {
            if (!Ready("Normal Speed"))
            {
                return;
            }

            Time.timeScale = 1f;
            Debug.Log("[Demo/Dig] timeScale 1.0 — back to real time.");
        }

        [MenuItem(Menu + "Log Dig Framing")]
        public static void LogDigFraming()
        {
            if (!Ready("Log Dig Framing"))
            {
                return;
            }

            var dig = Object.FindFirstObjectByType<DinoDigger.Dig.DigModeController>();
            if (dig == null)
            {
                Debug.LogWarning("[Demo/Dig] No DigModeController in the scene.");
                return;
            }

            Camera cam = Camera.main;
            Debug.Log(
                $"[Demo/Dig] dig root at {dig.DigRootPosition:F2}; camera frames DigCenter " +
                $"{dig.DigCenter:F2} at ortho size {(cam != null ? cam.orthographicSize : -1f):F2}; " +
                $"site open: {dig.IsOpen}; timeScale {Time.timeScale:F2}. " +
                "Point a scene-view capture at DigCenter with that ortho size to see the whole pit.");
        }

        // ----- helpers -----

        /// <summary>Play-mode gate shared by every item: these all mutate live runtime state,
        /// which is meaningless without a running player loop.</summary>
        private static bool Ready(string item)
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning($"[Demo/Dig] {item} only works in play mode — enter play mode first.");
                return false;
            }

            // Keep the player loop alive while the editor is unfocused (e.g. during a capture),
            // matching DemoTownMenu and the integration runner.
            Application.runInBackground = true;
            return true;
        }

        /// <summary>The dig controller, but only when a site is actually open — every toy hook
        /// needs a live grid to plant into.</summary>
        private static DinoDigger.Dig.DigModeController ActiveDig(string item)
        {
            if (!Ready(item))
            {
                return null;
            }

            var dig = Object.FindFirstObjectByType<DinoDigger.Dig.DigModeController>();
            if (dig == null)
            {
                Debug.LogWarning($"[Demo/Dig] {item}: no DigModeController in the scene.");
                return null;
            }

            if (!dig.IsOpen)
            {
                Debug.LogWarning($"[Demo/Dig] {item}: no dig site is open — run Enter First Dig.");
                return null;
            }

            return dig;
        }

        private static DinoDigger.Overworld.DigMound NearestMound(DinoDigger.Core.GameManager gm)
        {
            var mounds = Object.FindObjectsByType<DinoDigger.Overworld.DigMound>(FindObjectsSortMode.None);
            DinoDigger.Overworld.DigMound best = null;
            float bestSq = float.MaxValue;

            // Nearest to the PLAYER, so the site that opens is the one the child would have
            // driven to. The backhoe is a public component; the GameManager's own transform
            // would just be the scene origin.
            var backhoe = Object.FindFirstObjectByType<DinoDigger.Overworld.BackhoeController>();
            Vector3 from = backhoe != null ? backhoe.transform.position : gm.transform.position;

            for (int i = 0; i < mounds.Length; i++)
            {
                DinoDigger.Overworld.DigMound m = mounds[i];
                if (m == null || !m.IsActive)
                {
                    continue;
                }

                float sq = (m.transform.position - from).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = m;
                }
            }

            return best;
        }
    }
}
