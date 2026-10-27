using System;
using BazaarActionAxis.Engine;
using UnityEngine;

namespace BazaarActionAxis.Plugin.Ui
{
    public static class Theme
    {
        public static float Scale = 1f;
        public static UnityEngine.Font UiFont;

        public static GUIStyle TitleStyle;
        public static GUIStyle LabelStyle;
        public static GUIStyle SmallStyle;
        public static GUIStyle BadgeStyle;
        public static GUIStyle TileNameStyle;
        public static GUIStyle ButtonStyle;
        public static GUIStyle TimeStyle;
        public static GUIStyle PlaceholderStyle;
        public static GUIStyle EnchantStyle;
        public static GUIStyle TriggerStyle;
        public static GUIStyle CenterSmallStyle;
        public static GUIStyle SkillScrollbarStyle;

        // BPP's black glass and restrained gold, shared by the dock and combat board.
        public static readonly Color Panel = new Color(0.01f, 0.01f, 0.01f, 0.88f);
        public static readonly Color Tile = new Color(0.055f, 0.053f, 0.047f, 0.96f);
        public static readonly Color TileHover = new Color(0.13f, 0.115f, 0.078f, 1f);
        public static readonly Color Border = new Color(0.84f, 0.71f, 0.44f, 1f);
        public static readonly Color FrameDim = new Color(0.30f, 0.265f, 0.185f, 1f);
        public static readonly Color Track = new Color(0.008f, 0.009f, 0.010f, 0.88f);
        public static readonly Color Text = new Color(0.95f, 0.92f, 0.84f, 1f);
        public static readonly Color Dim = new Color(0.64f, 0.62f, 0.56f, 1f);
        public static readonly Color Water = new Color(0.19f, 0.64f, 0.62f, 0.36f);
        public static readonly Color WaterLine = new Color(0.52f, 0.91f, 0.82f, 0.90f);

        private static float _builtScale = -1f;
        private static Texture2D _buttonNormal;
        private static Texture2D _buttonHover;
        private static Texture2D _skillFrame;

        public static void EnsureInit()
        {
            if (_builtScale >= 0f && Math.Abs(_builtScale - Scale) < 0.01f) return;
            _builtScale = Scale;

            if (UiFont == null)
            {
                try
                {
                    UiFont = UnityEngine.Font.CreateDynamicFontFromOSFont(
                        new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei" },
                        16);
                }
                catch
                {
                    UiFont = null;
                }
            }

            TitleStyle = NewLabel(20, FontStyle.Bold, Text, false);
            LabelStyle = NewLabel(13, FontStyle.Normal, Text, false);
            SmallStyle = NewLabel(11, FontStyle.Normal, Dim, false);
            BadgeStyle = NewLabel(13, FontStyle.Bold, Text, false);
            BadgeStyle.alignment = TextAnchor.MiddleCenter;
            TileNameStyle = NewLabel(14, FontStyle.Bold, Text, true);
            TileNameStyle.alignment = TextAnchor.MiddleCenter;
            TimeStyle = NewLabel(14, FontStyle.Bold, Text, false);
            PlaceholderStyle = NewLabel(18, FontStyle.Bold, Text, false);
            PlaceholderStyle.alignment = TextAnchor.MiddleCenter;
            EnchantStyle = NewLabel(10, FontStyle.Normal, Border, false);
            EnchantStyle.alignment = TextAnchor.MiddleCenter;
            TriggerStyle = NewLabel(20, FontStyle.Bold, Text, false);
            TriggerStyle.alignment = TextAnchor.MiddleCenter;
            CenterSmallStyle = NewLabel(11, FontStyle.Normal, Dim, false);
            CenterSmallStyle.alignment = TextAnchor.MiddleCenter;
            SkillScrollbarStyle = new GUIStyle(GUI.skin.horizontalScrollbar) { fixedHeight = 6f * Scale };

            ButtonStyle = new GUIStyle(GUI.skin.button);
            ButtonStyle.font = UiFont;
            ButtonStyle.fontSize = Mathf.RoundToInt(13 * Scale);
            ButtonStyle.normal.textColor = Text;
            ButtonStyle.hover.textColor = Border;
            ButtonStyle.active.textColor = Border;
            ButtonStyle.focused.textColor = Text;
            if (_buttonNormal == null) _buttonNormal = Solid(Tile);
            if (_buttonHover == null) _buttonHover = Solid(TileHover);
            ButtonStyle.normal.background = _buttonNormal;
            ButtonStyle.hover.background = _buttonHover;
            ButtonStyle.active.background = _buttonHover;
            ButtonStyle.focused.background = _buttonNormal;
            ButtonStyle.border = new RectOffset(0, 0, 0, 0);
            ButtonStyle.padding = new RectOffset(8, 8, 4, 4);
        }

        public static bool Button(Rect rect, string text)
        {
            bool clicked = GUI.Button(rect, text, ButtonStyle);
            Outline(rect, rect.Contains(Event.current.mousePosition) ? Border : FrameDim, Scale);
            return clicked;
        }

        public static void SkillFrame(Rect rect, Color border, Texture2D nativeFrame = null)
        {
            if (nativeFrame != null)
            {
                GUI.DrawTexture(rect, nativeFrame, ScaleMode.ScaleToFit);
                return;
            }
            if (_skillFrame == null)
            {
                const int size = 64;
                _skillFrame = new Texture2D(size, size, TextureFormat.RGBA32, false);
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        double dx = x + 0.5 - size * 0.5, dy = y + 0.5 - size * 0.5;
                        double radius = Math.Sqrt(dx * dx + dy * dy);
                        float edge = (float)Math.Max(0, Math.Min(1, size * 0.5 - radius));
                        float ring = (float)Math.Max(0, Math.Min(1, radius - 28.5));
                        _skillFrame.SetPixel(x, y, new Color(ring, ring, ring, edge * ring));
                    }
                _skillFrame.Apply();
            }
            Color old = GUI.color;
            GUI.color = border;
            GUI.DrawTexture(rect, _skillFrame);
            GUI.color = old;
        }

        public static void Frame(Rect rect)
        {
            Fill(new Rect(rect.x + 5f * Scale, rect.y + 7f * Scale, rect.width, rect.height),
                new Color(0f, 0f, 0f, 0.45f));
            Fill(rect, Panel);
            Outline(rect, Border, Scale);
            Outline(new Rect(rect.x + 4f * Scale, rect.y + 4f * Scale,
                rect.width - 8f * Scale, rect.height - 8f * Scale), FrameDim, Scale);
        }

        private static Texture2D Solid(Color color)
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        public static void Dispose()
        {
            if (_buttonNormal != null) UnityEngine.Object.Destroy(_buttonNormal);
            if (_buttonHover != null) UnityEngine.Object.Destroy(_buttonHover);
            if (_skillFrame != null) UnityEngine.Object.Destroy(_skillFrame);
            if (UiFont != null) UnityEngine.Object.Destroy(UiFont);
            _buttonNormal = null;
            _buttonHover = null;
            _skillFrame = null;
            UiFont = null;
            _builtScale = -1f;
        }

        public static void Fill(Rect rect, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }

        public static void Outline(Rect rect, Color color, float width)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, width), color);
            Fill(new Rect(rect.x, rect.yMax - width, rect.width, width), color);
            Fill(new Rect(rect.x, rect.y, width, rect.height), color);
            Fill(new Rect(rect.xMax - width, rect.y, width, rect.height), color);
        }

        public static Color TierColor(string tier)
        {
            string key = ThemeKeys.TierColorKey(tier);
            if (key == "silver") return new Color(0.75f, 0.75f, 0.78f, 1f);
            if (key == "gold") return new Color(1.00f, 0.84f, 0.00f, 1f);
            if (key == "diamond") return new Color(0.73f, 0.95f, 1.00f, 1f);
            if (key == "legendary") return new Color(1.00f, 0.42f, 0.21f, 1f);
            return new Color(0.72f, 0.45f, 0.20f, 1f);
        }

        public static Color BadgeColor(string key)
        {
            if (key == "damage") return new Color(1.00f, 0.45f, 0.45f, 1f);
            if (key == "haste") return new Color(0.40f, 0.90f, 0.95f, 1f);
            if (key == "freeze") return new Color(0.55f, 0.75f, 1.00f, 1f);
            if (key == "slow") return new Color(0.80f, 0.60f, 1.00f, 1f);
            if (key == "shield") return new Color(1.00f, 0.85f, 0.35f, 1f);
            if (key == "heal") return new Color(0.50f, 0.95f, 0.55f, 1f);
            if (key == "burn") return new Color(1.00f, 0.60f, 0.25f, 1f);
            if (key == "poison") return new Color(0.55f, 0.85f, 0.40f, 1f);
            if (key == "charge") return new Color(0.35f, 0.90f, 0.80f, 1f);
            return Text;
        }

        private static GUIStyle NewLabel(int size, FontStyle style, Color color, bool wrap)
        {
            GUIStyle result = new GUIStyle(GUI.skin.label);
            result.font = UiFont;
            result.fontSize = Mathf.RoundToInt(size * Scale);
            result.fontStyle = style;
            result.normal.textColor = color;
            result.wordWrap = wrap;
            result.richText = true;
            result.clipping = wrap ? TextClipping.Clip : TextClipping.Overflow;
            return result;
        }
    }
}
