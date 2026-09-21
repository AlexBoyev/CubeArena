using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CubeArena.Client
{
    // All UI is built from code, never from a .prefab/.unity scene asset — see CLAUDE.md.
    // Rounded panel/button corners and the menu background gradient are generated at
    // runtime (small Texture2Ds, cached once) rather than imported image assets, for the
    // same reason — no hand-authored/Editor-imported files to keep in sync.
    public static class UiFactory
    {
        // A single shared color/style palette so every screen looks consistent instead
        // of each panel improvising its own gray. Kept private — callers go through the
        // Create* methods, not raw colors, so this is the only place "the look" lives.
        private static class Theme
        {
            public static readonly Color BackgroundTop = new(0.07f, 0.08f, 0.12f);
            public static readonly Color BackgroundBottom = new(0.03f, 0.035f, 0.05f);
            public static readonly Color Panel = new(0.12f, 0.13f, 0.18f, 0.95f);
            public static readonly Color Field = new(0.92f, 0.93f, 0.95f);
            public static readonly Color TextPrimary = new(0.95f, 0.96f, 0.98f);
            public static readonly Color TextOnAccent = Color.white;
            public static readonly Color Disabled = new(0.32f, 0.33f, 0.37f);

            public static readonly ButtonPalette Accent = new(
                new Color(0.26f, 0.5f, 0.95f), new Color(0.38f, 0.62f, 1f), new Color(0.17f, 0.38f, 0.78f));

            public static readonly ButtonPalette Danger = new(
                new Color(0.72f, 0.26f, 0.26f), new Color(0.84f, 0.35f, 0.35f), new Color(0.56f, 0.17f, 0.17f));
        }

        private readonly struct ButtonPalette
        {
            public readonly Color Normal;
            public readonly Color Hover;
            public readonly Color Pressed;

            public ButtonPalette(Color normal, Color hover, Color pressed)
            {
                Normal = normal;
                Hover = hover;
                Pressed = pressed;
            }
        }

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

        // Full-screen gradient behind the menu flow (main menu, login, lobby-adjacent
        // screens) — ClientBootstrap shows/hides it opposite the HUD, since during actual
        // gameplay the 3D arena is the backdrop, not this. Raycast-transparent so it never
        // eats clicks meant for whatever's on top of it.
        public static Image CreateBackground(Transform parent)
        {
            var go = new GameObject("MenuBackground", typeof(Image));
            go.transform.SetParent(parent, false);
            go.transform.SetAsFirstSibling();
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            var image = go.GetComponent<Image>();
            image.sprite = GetGradientSprite();
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.raycastTarget = false;
            return image;
        }

        public static RectTransform CreatePanel(Transform parent, Vector2 size)
        {
            var go = new GameObject("Panel", typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;

            var image = go.GetComponent<Image>();
            image.sprite = GetRoundedSprite();
            image.type = Image.Type.Sliced;
            image.color = Theme.Panel;
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
            label.color = Theme.TextPrimary;
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
            var fieldImage = go.GetComponent<Image>();
            fieldImage.sprite = GetRoundedSprite();
            fieldImage.type = Image.Type.Sliced;
            fieldImage.color = Theme.Field;

            var textGo = new GameObject("Text", typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10, 4);
            textRect.offsetMax = new Vector2(-10, -4);
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
            placeholderRect.offsetMin = new Vector2(10, 4);
            placeholderRect.offsetMax = new Vector2(-10, -4);
            var placeholderText = placeholderGo.GetComponent<Text>();
            placeholderText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            placeholderText.fontSize = 18;
            placeholderText.color = new Color(0, 0, 0, 0.45f);
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

        public static Toggle CreateToggle(Transform parent, string label, Vector2 anchoredPosition, bool defaultValue = false)
        {
            var go = new GameObject($"Toggle_{label}", typeof(Toggle));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(300, 26);

            var backgroundGo = new GameObject("Background", typeof(Image));
            backgroundGo.transform.SetParent(go.transform, false);
            var backgroundRect = backgroundGo.GetComponent<RectTransform>();
            backgroundRect.anchorMin = backgroundRect.anchorMax = new Vector2(0f, 0.5f);
            backgroundRect.pivot = new Vector2(0f, 0.5f);
            backgroundRect.anchoredPosition = Vector2.zero;
            backgroundRect.sizeDelta = new Vector2(22, 22);
            var backgroundImage = backgroundGo.GetComponent<Image>();
            backgroundImage.sprite = GetRoundedSprite();
            backgroundImage.type = Image.Type.Sliced;
            backgroundImage.color = Theme.Field;

            var checkmarkGo = new GameObject("Checkmark", typeof(Image));
            checkmarkGo.transform.SetParent(backgroundGo.transform, false);
            var checkmarkRect = checkmarkGo.GetComponent<RectTransform>();
            checkmarkRect.anchorMin = Vector2.zero;
            checkmarkRect.anchorMax = Vector2.one;
            checkmarkRect.offsetMin = new Vector2(4, 4);
            checkmarkRect.offsetMax = new Vector2(-4, -4);
            var checkmarkImage = checkmarkGo.GetComponent<Image>();
            checkmarkImage.sprite = GetRoundedSprite();
            checkmarkImage.type = Image.Type.Sliced;
            checkmarkImage.color = Theme.Accent.Normal;

            var labelGo = new GameObject("Label", typeof(Text));
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(30, 0);
            labelRect.offsetMax = Vector2.zero;
            var labelText = labelGo.GetComponent<Text>();
            labelText.text = label;
            labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            labelText.fontSize = 16;
            labelText.color = Theme.TextPrimary;
            labelText.alignment = TextAnchor.MiddleLeft;

            var toggle = go.GetComponent<Toggle>();
            toggle.targetGraphic = backgroundImage;
            toggle.graphic = checkmarkImage;
            toggle.isOn = defaultValue;
            return toggle;
        }

        // A simple horizontal gauge (background + fill), for things like the sprint mana
        // bar — returns the fill Image so the caller can drive it every frame via
        // fillAmount (0..1) and recolor it as needed (e.g. green/yellow/red by level).
        public static Image CreateBar(Transform parent, Vector2 anchoredPosition, Vector2 size, Color fillColor)
        {
            var backgroundGo = new GameObject("Bar", typeof(Image));
            backgroundGo.transform.SetParent(parent, false);
            var backgroundRect = backgroundGo.GetComponent<RectTransform>();
            backgroundRect.anchorMin = backgroundRect.anchorMax = new Vector2(0.5f, 0.5f);
            backgroundRect.anchoredPosition = anchoredPosition;
            backgroundRect.sizeDelta = size;
            var backgroundImage = backgroundGo.GetComponent<Image>();
            backgroundImage.sprite = GetRoundedSprite();
            backgroundImage.type = Image.Type.Sliced;
            backgroundImage.color = new Color(0f, 0f, 0f, 0.6f);

            var fillGo = new GameObject("Fill", typeof(Image));
            fillGo.transform.SetParent(backgroundGo.transform, false);
            var fillRect = fillGo.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(2, 2);
            fillRect.offsetMax = new Vector2(-2, -2);

            var fillImage = fillGo.GetComponent<Image>();
            fillImage.sprite = GetRoundedSprite();
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.fillAmount = 1f;
            fillImage.color = fillColor;
            return fillImage;
        }

        // danger=true gives destructive actions (Leave Match, Exit) a red variant instead
        // of the standard accent blue, so they read as distinct from everyday navigation
        // at a glance rather than relying on label text alone.
        public static Button CreateButton(Transform parent, string label, Vector2 anchoredPosition, UnityEngine.Events.UnityAction onClick, Vector2? size = null, bool danger = false)
        {
            var go = new GameObject($"Button_{label}", typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size ?? new Vector2(180, 44);

            var palette = danger ? Theme.Danger : Theme.Accent;
            var image = go.GetComponent<Image>();
            image.sprite = GetRoundedSprite();
            image.type = Image.Type.Sliced;
            image.color = palette.Normal;

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
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Theme.TextOnAccent;

            var button = go.GetComponent<Button>();
            var colors = button.colors;
            colors.normalColor = palette.Normal;
            colors.highlightedColor = palette.Hover;
            colors.pressedColor = palette.Pressed;
            colors.selectedColor = palette.Hover;
            colors.disabledColor = Theme.Disabled;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener(onClick);
            return button;
        }

        // Generated once and reused everywhere (panels, buttons, fields, bars) via 9-slice
        // scaling — a single small antialiased rounded-rect texture, not per-caller
        // geometry, so every corner in the game matches and there's only one place to
        // tune the radius.
        private const int RoundedSpriteSize = 64;
        private const float RoundedCornerRadius = 16f;
        private static Sprite _roundedSprite;

        private static Sprite GetRoundedSprite()
        {
            if (_roundedSprite != null)
            {
                return _roundedSprite;
            }

            var tex = new Texture2D(RoundedSpriteSize, RoundedSpriteSize, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            for (var y = 0; y < RoundedSpriteSize; y++)
            {
                for (var x = 0; x < RoundedSpriteSize; x++)
                {
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, RoundedCornerAlpha(x, y)));
                }
            }

            tex.Apply();

            var border = RoundedCornerRadius;
            _roundedSprite = Sprite.Create(
                tex, new Rect(0, 0, RoundedSpriteSize, RoundedSpriteSize), new Vector2(0.5f, 0.5f), 100f,
                0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            return _roundedSprite;
        }

        // Signed-distance-ish rounded-rect coverage for one pixel: clamp to the nearest
        // corner's arc center, measure circular distance from it, and fall off over ~1px
        // for antialiasing. Full coverage (1) anywhere not near a corner.
        private static float RoundedCornerAlpha(int px, int py)
        {
            var x = px + 0.5f;
            var y = py + 0.5f;
            var cx = Mathf.Clamp(x, RoundedCornerRadius, RoundedSpriteSize - RoundedCornerRadius);
            var cy = Mathf.Clamp(y, RoundedCornerRadius, RoundedSpriteSize - RoundedCornerRadius);
            var dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            return Mathf.Clamp01(RoundedCornerRadius - dist + 0.5f);
        }

        // A tall, thin vertical-gradient texture stretched to fill the screen (see
        // CreateBackground) — cheaper than a shader/material for something this simple.
        private const int GradientTextureHeight = 64;
        private static Sprite _gradientSprite;

        private static Sprite GetGradientSprite()
        {
            if (_gradientSprite != null)
            {
                return _gradientSprite;
            }

            var tex = new Texture2D(1, GradientTextureHeight, TextureFormat.RGB24, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            for (var y = 0; y < GradientTextureHeight; y++)
            {
                var t = y / (float)(GradientTextureHeight - 1);
                tex.SetPixel(0, y, Color.Lerp(Theme.BackgroundBottom, Theme.BackgroundTop, t));
            }

            tex.Apply();
            _gradientSprite = Sprite.Create(tex, new Rect(0, 0, 1, GradientTextureHeight), new Vector2(0.5f, 0.5f));
            return _gradientSprite;
        }
    }
}
