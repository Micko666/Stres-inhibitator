using UnityEngine;

namespace StressTraining.UI
{
    /// <summary>
    /// Single source of truth for the UI design system (spec §13): one dark
    /// blue-grey research-facility palette with a restrained cyan/aurora accent,
    /// consistent spacing and type sizes. Panels read these tokens instead of
    /// hard-coding colours, so the look stays consistent and is tuned in one place.
    ///
    /// Kept as plain static fields (no ScriptableObject) so it is available in edit
    /// mode, tests and the installer without an asset dependency, and so it does not
    /// introduce a new package or framework.
    /// </summary>
    public static class UiTheme
    {
        // ── surfaces ─────────────────────────────────────────────────────
        public static readonly Color PanelBackground = new Color(0.09f, 0.11f, 0.15f, 0.97f);
        public static readonly Color PanelBorder = new Color(0.20f, 0.30f, 0.40f, 0.9f);
        public static readonly Color HeaderBackground = new Color(0.13f, 0.17f, 0.23f);
        public static readonly Color Card = new Color(0.14f, 0.17f, 0.22f, 0.92f);
        public static readonly Color CardAlt = new Color(0.12f, 0.15f, 0.20f, 0.92f);

        // ── text ─────────────────────────────────────────────────────────
        public static readonly Color TextPrimary = new Color(0.93f, 0.95f, 0.98f);
        public static readonly Color TextSecondary = new Color(0.62f, 0.68f, 0.76f);

        // ── accent + status ──────────────────────────────────────────────
        public static readonly Color Accent = new Color(0.28f, 0.72f, 0.85f);   // cyan / aurora
        public static readonly Color AccentSoft = new Color(0.22f, 0.55f, 0.66f);
        public static readonly Color Success = new Color(0.30f, 0.78f, 0.52f);
        public static readonly Color Warning = new Color(0.95f, 0.72f, 0.25f);
        public static readonly Color Danger = new Color(0.86f, 0.32f, 0.30f);

        // ── buttons ──────────────────────────────────────────────────────
        public static readonly Color ButtonNormal = new Color(0.16f, 0.20f, 0.27f);
        public static readonly Color ButtonHighlighted = new Color(0.24f, 0.50f, 0.68f);
        public static readonly Color ButtonPressed = new Color(0.16f, 0.62f, 0.60f);
        public static readonly Color ButtonSelected = new Color(0.22f, 0.40f, 0.52f);
        public static readonly Color ButtonDisabled = new Color(0.10f, 0.11f, 0.14f, 0.6f);

        // ── spacing (px, world-canvas units) ─────────────────────────────
        public const float SpacingXs = 4f;
        public const float SpacingSm = 8f;
        public const float SpacingMd = 14f;
        public const float SpacingLg = 22f;

        // ── type sizes ───────────────────────────────────────────────────
        public const int TitleSize = 28;
        public const int SubtitleSize = 22;
        public const int BodySize = 20;
        public const int CaptionSize = 16;
        public const int ButtonSize = 21;

        public static ColorBlockLite Button(bool danger = false)
        {
            return new ColorBlockLite
            {
                normal = danger ? new Color(0.30f, 0.14f, 0.15f) : ButtonNormal,
                highlighted = danger ? new Color(0.62f, 0.26f, 0.26f) : ButtonHighlighted,
                pressed = ButtonPressed,
                selected = danger ? new Color(0.5f, 0.22f, 0.22f) : ButtonSelected,
                disabled = ButtonDisabled
            };
        }

        public struct ColorBlockLite
        {
            public Color normal, highlighted, pressed, selected, disabled;
        }
    }
}
