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

        private static void ApplyWebGLTextureOverrides()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D",
                new[] { "Assets/Art" });
            int changed = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    continue;
                }

                // Dig background fills the whole screen; keep it at full 512 to avoid blur.
                bool isDigBg = path.StartsWith(DigBgDir, System.StringComparison.Ordinal);
                int targetMaxTex = isDigBg ? DigBgMaxTex : WebGLMaxTex;

                var settings = importer.GetPlatformTextureSettings("WebGL");
                if (settings.overridden && settings.maxTextureSize == targetMaxTex &&
                    settings.crunchedCompression && settings.compressionQuality == WebGLQuality)
                {
                    continue; // already applied
                }

                settings.overridden = true;
                settings.maxTextureSize = targetMaxTex;
                settings.format = TextureImporterFormat.Automatic;
                settings.textureCompression = TextureImporterCompression.Compressed;
                settings.crunchedCompression = true;
                settings.compressionQuality = WebGLQuality;
                importer.SetPlatformTextureSettings(settings);
                importer.SaveAndReimport();
                changed++;
            }

            Debug.Log($"[WebGLBuilder] WebGL texture overrides applied to {changed} textures");
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
