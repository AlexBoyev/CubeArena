using CubeArena.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace CubeArena.Client
{
    // Section 6: "an orthographic camera rendering to a RenderTexture, shown in a UI
    // RawImage corner, with a dot per player in that player's colour." Player cubes
    // viewed from directly above already render as small coloured squares, so no
    // separate marker system is needed — the camera IS the dot renderer.
    public static class Minimap
    {
        public static RawImage Create(Transform canvasTransform)
        {
            const int textureSize = 256;
            var renderTexture = new RenderTexture(textureSize, textureSize, 16) { name = "MinimapRT" };

            var cameraGo = new GameObject("MinimapCamera");
            var camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = MovementConstants.ArenaHalfExtent;
            camera.transform.position = new Vector3(0, 60, 0);
            camera.transform.rotation = Quaternion.Euler(90, 0, 0);
            camera.targetTexture = renderTexture;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.05f, 0.05f);
            camera.nearClipPlane = 1f;
            camera.farClipPlane = 100f;

            var imageGo = new GameObject("Minimap", typeof(RawImage));
            imageGo.transform.SetParent(canvasTransform, false);
            var rect = imageGo.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-16, -16);
            rect.sizeDelta = new Vector2(180, 180);

            var rawImage = imageGo.GetComponent<RawImage>();
            rawImage.texture = renderTexture;
            return rawImage;
        }
    }
}
