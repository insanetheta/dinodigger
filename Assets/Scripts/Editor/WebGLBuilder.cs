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

        [MenuItem("DinoDigger/Build WebGL (docs)")]
        public static void Build()
        {
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
