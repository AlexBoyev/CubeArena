using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PocketHeist.EditorTools
{
    // Verification tool: opens SleepClipPreview.unity, enters actual Play mode
    // (not just an edit-time pose sample), lets it run a few real seconds so the
    // Animator/breathing oscillation settle, then renders each station's
    // (disabled-by-default) camera to a PNG. Not part of the shipped project -
    // deleted once the sleep-clip decision is made.
    //
    // Bug found the hard way: entering Play mode triggers a domain reload
    // (Unity's default "Reload Domain" enter-play-mode setting), which wipes
    // every static field and event subscription made *before* that reload -
    // including a plain `EditorApplication.update += OnUpdate` made inside
    // Capture(). The first attempt at this subscribed then called
    // EditorApplication.isPlaying = true, the reload fired, the subscription
    // was gone, and OnUpdate never ran again - Unity just sat in Play mode
    // indefinitely with no forward progress and no error. Fixed by persisting
    // state through SessionState (which *does* survive domain reloads) and
    // re-subscribing via [InitializeOnLoadMethod], which Unity guarantees runs
    // after every reload, including the one entering Play mode causes.
    [InitializeOnLoad]
    public static class SleepPreviewScreenshotter
    {
        private const string OutputDir = "F:/Unity/CubeArena/preview_screenshots";
        private const float SettleSeconds = 6f;
        private const int StationCount = 4;
        private const int Width = 1280;
        private const int Height = 720;

        private const string StateKey = "PocketHeist.SleepPreviewScreenshotter.Armed";
        private const string PlayStartKey = "PocketHeist.SleepPreviewScreenshotter.PlayStartTime";

        static SleepPreviewScreenshotter()
        {
            if (SessionState.GetBool(StateKey, false))
            {
                EditorApplication.update += OnUpdate;
            }
        }

        public static void Capture()
        {
            Debug.Log("SCREENSHOT_STEP: starting Capture()");
            Directory.CreateDirectory(OutputDir);
            EditorSceneManager.OpenScene(SleepPreviewBuilder.ScenePath);
            Debug.Log("SCREENSHOT_STEP: scene opened, arming and entering play mode");
            SessionState.SetBool(StateKey, true);
            SessionState.SetFloat(PlayStartKey, 0f);
            EditorApplication.update += OnUpdate;
            EditorApplication.isPlaying = true;
            Debug.Log("SCREENSHOT_STEP: isPlaying set to true, returning from Capture()");
        }

        private static void OnUpdate()
        {
            if (!SessionState.GetBool(StateKey, false))
            {
                EditorApplication.update -= OnUpdate;
                return;
            }

            if (!EditorApplication.isPlaying || EditorApplication.isPaused)
            {
                return;
            }

            var playStart = SessionState.GetFloat(PlayStartKey, 0f);
            if (playStart == 0f)
            {
                SessionState.SetFloat(PlayStartKey, (float)EditorApplication.timeSinceStartup);
                Debug.Log("SCREENSHOT_STEP: play mode confirmed active, settling timer started");
                return;
            }

            if (EditorApplication.timeSinceStartup - playStart < SettleSeconds)
            {
                return;
            }

            SessionState.SetBool(StateKey, false);
            EditorApplication.update -= OnUpdate;
            Debug.Log("SCREENSHOT_STEP: settle time elapsed, capturing");
            CaptureAllStations();
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
        }

        private static void CaptureAllStations()
        {
            for (var i = 0; i < StationCount; i++)
            {
                var cameraGo = GameObject.Find($"StationCamera_{i}");
                if (cameraGo == null)
                {
                    Debug.LogError($"StationCamera_{i} not found - skipping.");
                    continue;
                }

                var camera = cameraGo.GetComponent<Camera>();
                var rt = new RenderTexture(Width, Height, 24);
                camera.targetTexture = rt;
                // First screenshot pass came back with every giant in Unity's
                // magenta missing-shader fallback despite the material being
                // confirmed valid (shader assigned, textures assigned, reloaded
                // correctly from disk) - URP/Lit's shader variant for this
                // material's keyword combination (_NORMALMAP) most likely hadn't
                // finished async-compiling by the moment of the single Render()
                // call. Two renders a frame apart gives it a chance to compile
                // and land before the one that's actually kept.
                camera.Render();
                camera.Render();

                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                RenderTexture.active = previous;
                camera.targetTexture = null;
                rt.Release();

                var bytes = tex.EncodeToPNG();
                var path = Path.Combine(OutputDir, $"station_{i}.png");
                File.WriteAllBytes(path, bytes);
                Debug.Log($"Saved: {path}");
                Object.DestroyImmediate(tex);
            }
        }
    }
}
