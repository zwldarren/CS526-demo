using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Facet.EditorTools
{
    /// <summary>
    /// The WebGL build, in one command, because the assignment ships as a WebGL build on GitHub Pages
    /// and a build that is only reproducible by hand in the Editor is a build that breaks quietly.
    ///
    /// One setting it fixes on the way: GitHub Pages does not send Content-Encoding, so a
    /// Brotli-compressed payload arrives unusable. Unity's decompression fallback is enabled so the
    /// loader decompresses in JavaScript when the host cannot.
    ///
    /// The vertex-colour shader needs no such handling: the Palette's view material is a real asset
    /// referencing it, and the scene references the Palette, so the build carries the shader through
    /// that chain (guarded by ProjectTests).
    ///
    /// Batchmode: <c>Unity -batchmode -quit -projectPath &lt;project&gt; -executeMethod Facet.EditorTools.BuildWebGL.Build</c>
    /// </summary>
    public static class BuildWebGL
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";

        /// <summary>Where the build lands. Git-ignored: it is an artefact, and Pages serves a copy.</summary>
        private const string OutputDirectory = "Builds/WebGL";

        /// <summary>A build that cannot be published is not finished, so a failure exits non-zero.</summary>
        private const int FailureExitCode = 1;

        [MenuItem("FACET/Build WebGL")]
        public static void Build()
        {
            try
            {
                ConfigureForStaticHosting();

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = OutputDirectory,
                    target = BuildTarget.WebGL,
                    options = BuildOptions.None,
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;

                if (summary.result != BuildResult.Succeeded)
                {
                    Debug.LogError("FACET: WebGL build " + summary.result + " with " + summary.totalErrors + " errors.");
                    EditorApplication.Exit(FailureExitCode);
                    return;
                }

                // Pages refuses to serve paths starting with an underscore unless this file is there,
                // and the WebGL template ships a folder called Build/.
                File.WriteAllText(Path.Combine(OutputDirectory, ".nojekyll"), string.Empty);

                Debug.Log("FACET: WebGL build at " + Path.GetFullPath(OutputDirectory) +
                    " (" + (summary.totalSize / (1024 * 1024)) + " MB). Publish that folder as it stands.");
            }
            catch (Exception error)
            {
                Debug.LogError("FACET: WebGL build threw: " + error);
                EditorApplication.Exit(FailureExitCode);
            }
        }

        /// <summary>Compression settings a plain static host can actually serve.</summary>
        private static void ConfigureForStaticHosting()
        {
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true;
        }
    }
}
