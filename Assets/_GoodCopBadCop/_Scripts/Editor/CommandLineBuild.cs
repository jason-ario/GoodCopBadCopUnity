using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GoodCopBadCop.Cli
{
    /// <summary>
    /// Entry point for headless builds: Tools/Unity/unity.ps1 build
    /// Unity args: -executeMethod GoodCopBadCop.Cli.CommandLineBuild.Build [-cliOutput path] [-cliDevelopment]
    /// Builds the enabled scenes from Build Settings for the active (or -buildTarget) platform.
    /// </summary>
    public static class CommandLineBuild
    {
        public static void Build()
        {
            string[] args = Environment.GetCommandLineArgs();
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            bool development = args.Contains("-cliDevelopment");

            string output = GetArg(args, "-cliOutput");
            if (string.IsNullOrEmpty(output))
            {
                output = Path.Combine("Builds", target.ToString(), PlayerSettings.productName + GetExtension(target));
            }

            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[CLI BUILD] No enabled scenes in Build Settings.");
                EditorApplication.Exit(1);
                return;
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = target,
                options = development ? BuildOptions.Development : BuildOptions.None
            };

            Debug.Log($"[CLI BUILD] target={target} development={development} output={output} scenes={string.Join(", ", scenes)}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            Debug.Log($"[CLI BUILD] result={summary.result} errors={summary.totalErrors} warnings={summary.totalWarnings} " +
                      $"size={summary.totalSize / (1024 * 1024)}MB time={summary.totalTime}");

            EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        private static string GetArg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static string GetExtension(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return ".exe";
                case BuildTarget.StandaloneOSX:
                    return ".app";
                case BuildTarget.StandaloneLinux64:
                    return ".x86_64";
                default:
                    return string.Empty;
            }
        }
    }
}
