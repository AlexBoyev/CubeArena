using System;
using UnityEditor;
using UnityEditor.Build.Reporting;

// Invoked via `Unity -batchmode -executeMethod BuildScript.<Method>` both locally and
// from .github/workflows/gameserver.yml (GameCI's unity-builder action).
public static class BuildScript
{
    private const string ServerScenePath = "Assets/Scenes/SampleScene.unity";

    public static void BuildLinuxDedicatedServer() =>
        BuildDedicatedServer(BuildTarget.StandaloneLinux64, "Builds/LinuxServer/CubeArena.x86_64");

    public static void BuildWindowsDedicatedServer() =>
        BuildDedicatedServer(BuildTarget.StandaloneWindows64, "Builds/WindowsServer/CubeArena.exe");

    // Diagnostic-only: a plain Standalone build (this machine lacks the Dedicated Server
    // module) used with DiagnosticServerEntryPoint/plain client bootstrap for local testing.
    public static void BuildWindowsDiagnostic()
    {
        EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ServerScenePath },
            locationPathName = "Builds/Diagnostic/CubeArenaDiagnostic.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });

        UnityEngine.Debug.Log($"Build result: {report.summary.result}, errors: {report.summary.totalErrors}");
    }

    private static void BuildDedicatedServer(BuildTarget target, string outputPath)
    {
        EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Server;

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ServerScenePath },
            locationPathName = outputPath,
            target = target,
            subtarget = (int)StandaloneBuildSubtarget.Server,
            options = BuildOptions.None
        });

        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new Exception($"Dedicated server build failed: {report.summary.result}, errors: {report.summary.totalErrors}");
        }

        UnityEngine.Debug.Log($"Dedicated server build succeeded: {outputPath}");
    }
}
