using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DinoDigger.EditorTools
{
    /// <summary>
    /// Builds the WebGL player into <c>docs/</c> so GitHub Pages can serve it
    /// straight from the main branch (Settings → Pages → main /docs).
    ///
    /// GitHub Pages does not send Content-Encoding headers for Unity's compressed
    /// bundles, so we build Brotli WITH decompression fallback — the loader ships
    /// its own decompressor and works on any static host, including mobile Safari
    /// and Chrome for Android.
    ///
    /// Uses the project WebGL template <c>Assets/WebGLTemplates/DinoDigger</c>
    /// (stock Default + baked-in mobile viewport fix, DinoDigger-vi2), so
    /// <c>docs/index.html</c> is emitted mobile-ready — no post-build patch.
    ///
    /// Menu: DinoDigger/Build WebGL (docs).
    /// </summary>
    public static class WebGLBuilder
    {
        private const string OutputDir = "docs";

        /// <summary>Per-platform WebGL texture overrides: characters render at ~95-230
        /// screen px, so 256px crunched textures are still oversampled and keep the single
        /// data bundle under GitHub's 100MB per-file limit. The full-screen dig background
        /// is the exception — it stays at 512 to avoid visible blur.</summary>
        private const int WebGLMaxTex = 256;
        private const int WebGLQuality = 40;
        private const int DigBgMaxTex = 512;
        private const string DigBgDir = "Assets/Art/Generated/digbg/";

        /// <summary>The Jurassic-earth environment set (DinoDigger-y1g). VERIFIED COVERED by
        /// the sweep below with no new skip-check needed: the sweep searches all of
        /// "Assets/Art" recursively, so every one of the 117 env sprites already gets the
        /// standard 256px crunched override. Every shipped env PNG is authored at &lt;= 256px
        /// (256x128 ground/bridge/mound, 256x256 props/nest, 256x512 fence, trimmed decals),
        /// so the override costs no resolution — it is the crunch compression that buys the
        /// budget. Named here purely so the coverage is reported explicitly instead of being
        /// assumed. The 1024^2 pipeline masters (env/*/plate_*.png) and the review sheets
        /// (contact_sheet / verify_*) live in the same tree and are swept too, which is
        /// harmless: nothing references them, so they never enter the player bundle.</summary>
        private const string EnvDir = "Assets/Art/Generated/env/";

        /// <summary>
        /// GROUND TILES ARE THE ONE PLACE CRUNCH QUALITY IS LOAD-BEARING (DinoDigger-ajm).
        /// Every other sprite is a free-standing shape whose edge alpha only has to look
        /// clean against the background. A ground tile's edge alpha has to ADD UP with its
        /// neighbour's: the diamond carries a soft ~4px ramp centred 2.5px OUTSIDE the true
        /// cell boundary, and the tessellation is seam-free only because tile A's ramp and
        /// tile B's ramp compose to 1.0 at every shared pixel. Crunch quantises alpha
        /// endpoints per block AND clusters those endpoints across blocks, so at q40 both
        /// neighbours' ramps can sag at the same pixel — and any coverage the pair loses is
        /// camera clear colour, i.e. exactly the hairline lattice this ticket is about.
        /// Measured composite coverage at q100/BC3 is >= 0.995; crunch is the only step in
        /// the chain that can push it visibly lower, and it is the one step the Editor
        /// never shows us (the Scene view samples the Standalone import).
        ///
        /// So the ground folder ships at crunch quality 90 while everything else stays at
        /// 40. It is ~250 small tiles, so the download cost is a fraction of a MB; the GPU
        /// footprint is identical either way (crunch is a transport codec — the runtime
        /// format is DXT5 at both qualities).
        /// </summary>
        private const string EnvGroundDir = "Assets/Art/Generated/env/ground/";
        private const int EnvGroundQuality = 90;

        private static void ApplyWebGLTextureOverrides()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D",
                new[] { "Assets/Art" });
            int changed = 0;
            int envSeen = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    continue;
                }

                if (path.StartsWith(EnvDir, System.StringComparison.Ordinal))
                {
                    envSeen++;
                }

                // Dig background fills the whole screen; keep it at full 512 to avoid blur.
                bool isDigBg = path.StartsWith(DigBgDir, System.StringComparison.Ordinal);
                int targetMaxTex = isDigBg ? DigBgMaxTex : WebGLMaxTex;

                // Ground tiles keep their edge alpha (see EnvGroundDir): their ramps have
                // to compose with the neighbour's, so they ride at a higher crunch quality.
                bool isGround = path.StartsWith(EnvGroundDir, System.StringComparison.Ordinal);
                int targetQuality = isGround ? EnvGroundQuality : WebGLQuality;

                var settings = importer.GetPlatformTextureSettings("WebGL");
                if (settings.overridden && settings.maxTextureSize == targetMaxTex &&
                    settings.crunchedCompression && settings.compressionQuality == targetQuality)
                {
                    continue; // already applied
                }

                settings.overridden = true;
                settings.maxTextureSize = targetMaxTex;
                settings.format = TextureImporterFormat.Automatic;
                settings.textureCompression = TextureImporterCompression.Compressed;
                settings.crunchedCompression = true;
                settings.compressionQuality = targetQuality;
                importer.SetPlatformTextureSettings(settings);
                importer.SaveAndReimport();
                changed++;
            }

            Debug.Log($"[WebGLBuilder] WebGL texture overrides applied to {changed} textures " +
                      $"({envSeen} of them under {EnvDir} — the env set is inside the sweep, " +
                      $"capped at {WebGLMaxTex}px crunched @{WebGLQuality}; the ground tiles " +
                      $"under {EnvGroundDir} ride at @{EnvGroundQuality} so their edge alpha " +
                      "still composes seam-free with the neighbour's)");
        }

        [MenuItem("DinoDigger/Build WebGL (docs)")]
        public static void Build()
        {
            ApplyWebGLTextureOverrides();

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 800;

            string outPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutputDir));
            Directory.CreateDirectory(outPath);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Main.unity" },
                locationPathName = outPath,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[WebGLBuilder] Build OK -> {outPath} " +
                          $"({report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalTime.TotalMinutes:F1} min)");
            }
            else
            {
                Debug.LogError($"[WebGLBuilder] Build {report.summary.result}: " +
                               $"{report.summary.totalErrors} errors");
            }
        }
    }
}
