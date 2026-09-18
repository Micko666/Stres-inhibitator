using StressTraining.Core;
using UnityEngine;
using UnityEngine.UI;

namespace StressTraining.UI
{
    /// <summary>
    /// Shared world-space UI construction helpers (legacy uGUI Text — no TMP
    /// asset dependency, guaranteed to render). Consistent font/colors across
    /// all panels (spec §31).
    /// </summary>
    public static class UiBuilder
    {
        // Sourced from the central UiTheme (spec §13) so the whole UI shares one
        // palette. These aliases keep the existing UiBuilder API stable.
        public static readonly Color PanelBg = UiTheme.PanelBackground;
        public static readonly Color HeaderBg = UiTheme.HeaderBackground;
        public static readonly Color TextColor = UiTheme.TextPrimary;
        public static readonly Color TextDim = UiTheme.TextSecondary;
        public static readonly Color Accent = UiTheme.Accent;
        public static readonly Color RowBg = UiTheme.ButtonNormal;
        public static readonly Color RowSelectedBg = UiTheme.ButtonSelected;
        public static readonly Color Warning = UiTheme.Warning;

        public static Canvas CreateWorldCanvas(string name, Transform parent, Vector2 sizePx, float metersPerPx = 0.0012f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = canvas.GetComponent<RectTransform>();
            rt.sizeDelta = sizePx;
            rt.localScale = Vector3.one * metersPerPx;
            rt.localPosition = Vector3.zero;
            return canvas;
        }

        public static Image Image(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Text Text(Transform parent, string name, string value, int size,
            TextAnchor anchor = TextAnchor.MiddleLeft, Color? color = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = RuntimeVisualUtil.BuiltinFont;
            t.text = value;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color ?? TextColor;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public static void Stretch(RectTransform rt, float left = 0, float bottom = 0, float right = 0, float top = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        public static void TopBar(RectTransform rt, float height)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, height);
            rt.anchoredPosition = Vector2.zero;
        }

        public static void BottomBar(RectTransform rt, float height, float yOffset = 0)
        {
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.sizeDelta = new Vector2(0, height);
            rt.anchoredPosition = new Vector2(0, yOffset);
        }
    }
}
