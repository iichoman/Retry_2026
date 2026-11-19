using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class DistributionBuild
{
    public static void Batch()
    {
        try
        {
            string output = Environment.GetEnvironmentVariable("RETRY_CLIENT_BUILD_OUTPUT");
            if (string.IsNullOrWhiteSpace(output)) throw new Exception("RETRY_CLIENT_BUILD_OUTPUT is required");
            Directory.CreateDirectory(output);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = Path.Combine(output, "Retry.exe"),
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
            });
            File.WriteAllText(Path.Combine(output, "BuildResult.txt"), $"{report.summary.result}; errors={report.summary.totalErrors}; warnings={report.summary.totalWarnings}; duration={report.summary.totalTime}");
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("Client build failed");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { UnityEngine.Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
