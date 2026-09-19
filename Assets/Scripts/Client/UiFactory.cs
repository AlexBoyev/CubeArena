using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CubeArena.Client
{
    // All UI is built from code, never from a .prefab/.unity scene asset — see CLAUDE.md.
    public static class UiFactory
    {
        public static Canvas CreateCanvas()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);

            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var eventSystemGo = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                Object.DontDestroyOnLoad(eventSystemGo);
            }

            return canvas;
        }

        public static RectTransform CreatePanel(Transform parent, Vector2 size)
        {
            var go = new GameObject("Panel", typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            go.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);
            return rect;
        }

        public static Text CreateText(Transform parent, string text, int fontSize, Vector2 anchoredPosition, Vector2 size)
        {
            var go = new GameObject("Text", typeof(Text));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var label = go.GetComponent<Text>();
            label.text = text;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = fontSize;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            return label;
        }

        public static InputField CreateInputField(Transform parent, string placeholder, Vector2 anchoredPosition, bool isPassword = false)
        {
            var go = new GameObject($"Input_{placeholder}", typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(300, 40);
            go.GetComponent<Image>().color = Color.white;

            var textGo = new GameObject("Text", typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8, 4);
            textRect.offsetMax = new Vector2(-8, -4);
            var text = textGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 18;
            text.color = Color.black;
            text.alignment = TextAnchor.MiddleLeft;

            var placeholderGo = new GameObject("Placeholder", typeof(Text));
            placeholderGo.transform.SetParent(go.transform, false);
            var placeholderRect = placeholderGo.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(8, 4);
            placeholderRect.offsetMax = new Vector2(-8, -4);
            var placeholderText = placeholderGo.GetComponent<Text>();
            placeholderText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            placeholderText.fontSize = 18;
            placeholderText.color = new Color(0, 0, 0, 0.5f);
            placeholderText.text = placeholder;
            placeholderText.fontStyle = FontStyle.Italic;

            var inputField = go.GetComponent<InputField>();
            inputField.textComponent = text;
            inputField.placeholder = placeholderText;
            if (isPassword)
            {
                inputField.contentType = InputField.ContentType.Password;
            }

            return inputField;
        }

        public static Button CreateButton(Transform parent, string label, Vector2 anchoredPosition, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject($"Button_{label}", typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(180, 44);
            go.GetComponent<Image>().color = new Color(0.2f, 0.5f, 0.9f);

            var textGo = new GameObject("Text", typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            var text = textGo.GetComponent<Text>();
            text.text = label;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 20;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;

            var button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            return button;
        }
    }
}
