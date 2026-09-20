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

    // The real Windows client build — a plain Standalone Player (not the Server
    // subtarget), which is exactly what ClientBootstrap's `#if !UNITY_SERVER`
    // auto-bootstrap needs. This is what gets zipped up for players to run;
    // see infra/compose/package-client.ps1.
    public static void BuildWindowsClient()
    {
        EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ServerScenePath },
            locationPathName = "Builds/WindowsClient/CubeArena.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });

        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new Exception($"Client build failed: {report.summary.result}, errors: {report.summary.totalErrors}");
        }

        UnityEngine.Debug.Log($"Client build succeeded: Builds/WindowsClient/CubeArena.exe");
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
