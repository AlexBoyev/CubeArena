using System;
using CubeArena.Shared;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Invoked via `Unity -batchmode -executeMethod BuildScript.<Method>` both locally and
// from .github/workflows/gameserver.yml (GameCI's unity-builder action).
public static class BuildScript
{
    // Shared with ServerBootstrap/ClientBootstrap's auto-bootstrap scene check
    // (CubeArena.Shared.BootConfig) — this is the one scene every real build
    // includes, and the one scene name that's allowed to auto-boot the game.
    private static readonly string ServerScenePath = $"Assets/Scenes/{BootConfig.BootSceneName}.unity";

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
        // Nothing in this project references a URP shader via a real Material asset
        // (everything is built at runtime — see CLAUDE.md), so Unity's build-time shader
        // stripping drops "Universal Render Pipeline/Lit" from the player entirely and
        // every Shader.Find(...) for it returns null at runtime (MaterialUtil.cs), which
        // renders as the pink/magenta "shader not found" material. Always Included
        // Shaders is the supported way to keep a shader that's only ever reached via
        // Shader.Find — this keeps that list in sync on every build instead of relying on
        // a one-time manual Editor step.
        EnsureAlwaysIncludedShader("Universal Render Pipeline/Lit");
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

    // Adds shaderName to Graphics Settings > Always Included Shaders if it isn't already
    // there — the supported way to stop the build pipeline stripping a shader that's
    // only ever reached via Shader.Find at runtime, with no Material asset referencing
    // it. Edits ProjectSettings/GraphicsSettings.asset the same way the Editor's own
    // Graphics Settings UI would (via SerializedObject), not by hand-editing the file.
    private static void EnsureAlwaysIncludedShader(string shaderName)
    {
        var shader = Shader.Find(shaderName);
        if (shader == null)
        {
            throw new Exception($"Shader not found: {shaderName}");
        }

        var graphicsSettings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("ProjectSettings/GraphicsSettings.asset");
        var serializedObject = new SerializedObject(graphicsSettings);
        var shadersProperty = serializedObject.FindProperty("m_AlwaysIncludedShaders");

        for (var i = 0; i < shadersProperty.arraySize; i++)
        {
            if (shadersProperty.GetArrayElementAtIndex(i).objectReferenceValue == shader)
            {
                return;
            }
        }

        shadersProperty.InsertArrayElementAtIndex(shadersProperty.arraySize);
        shadersProperty.GetArrayElementAtIndex(shadersProperty.arraySize - 1).objectReferenceValue = shader;
        serializedObject.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
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
