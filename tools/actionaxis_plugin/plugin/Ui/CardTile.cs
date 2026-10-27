using System;
using System.Collections.Generic;
using BazaarActionAxis.Engine;
using UnityEngine;

namespace BazaarActionAxis.Plugin.Ui
{
    public sealed class TileModel
    {
        public string Id;
        public CardDef Card;
        public int Slot;
        public string Enchantment;
        public CardStatsSeries Series;
        public BattleItemView History;

        public TileModel At(double time)
        {
            if (History == null) return this;
            BattleItemState state = History.At(time);
            if (state == null) return null;
            return new TileModel { Id = Id, Card = state.Card, Slot = state.Slot,
                Enchantment = state.Card.EnchantmentName, Series = Series };
        }
    }

    public static class CardTile
    {
        public static void Draw(Rect rect, TileModel model, CardStats stats,
            IconCache icons, double playhead, bool compact = false)
        {
            float scale = Theme.Scale;
            float pad = 7f * scale;
            Rect body = new Rect(rect.x + 3f * scale, rect.y + 3f * scale,
                rect.width - 6f * scale, rect.height - 6f * scale);
            Theme.Fill(rect, Theme.Tile);
            bool dummy = model.Card.Id != null && model.Card.Id.StartsWith("dummy:", StringComparison.Ordinal);
            Texture2D texture = !dummy && icons != null ? icons.Get(model.Card.BaseItemId, model.Enchantment) : null;
            if (texture != null)
            {
                GUI.DrawTexture(body, texture, ScaleMode.ScaleToFit);
                Theme.Fill(body, new Color(0f, 0f, 0f, 0.16f));
            }

            double progress = model.Series != null ? model.Series.CooldownProgressAt(playhead) : 0;
            if (model.Series != null && model.Series.CooldownCompletedAt(playhead)) progress = 1;
            if (progress > 0)
            {
                float filledHeight = body.height * (float)Math.Max(0, Math.Min(1, progress));
                float waterY = body.yMax - filledHeight;
                Theme.Fill(new Rect(body.x, waterY, body.width, filledHeight), Theme.Water);
                Theme.Fill(new Rect(body.x, waterY, body.width, 2f * scale), Theme.WaterLine);
            }

            Theme.Outline(rect, dummy ? Theme.FrameDim : Theme.TierColor(model.Card.Tier), scale);
            Theme.Outline(body, Theme.FrameDim, scale);

            Rect nameRect = new Rect(rect.x + pad, rect.y + 8f * scale,
                rect.width - pad * 2f, (compact ? 32f : 42f) * scale);
            Theme.Fill(new Rect(body.x, body.y, body.width, (compact ? 52f : 66f) * scale),
                new Color(0f, 0f, 0f, 0.68f));
            GUIStyle nameStyle = Theme.TileNameStyle;
            int nameSize = nameStyle.fontSize;
            GUIContent name = new GUIContent(model.Card.DisplayName());
            while (nameStyle.fontSize > Mathf.Max(6, Mathf.RoundToInt(10f * scale)) &&
                nameStyle.CalcHeight(name, nameRect.width) > nameRect.height)
                nameStyle.fontSize--;
            GUI.Label(nameRect, name, nameStyle);
            nameStyle.fontSize = nameSize;
            if (!string.IsNullOrEmpty(model.Enchantment))
                GUI.Label(new Rect(nameRect.x, rect.y + (compact ? 42f : 49f) * scale, nameRect.width, (compact ? 12f : 17f) * scale),
                    EnchantmentName(model.Enchantment), Theme.EnchantStyle);

            if (compact)
            {
                if (dummy)
                {
                    GUI.Label(new Rect(body.x, body.y + 55f * scale, body.width, 35f * scale),
                        model.Card.Size == "Large" ? "III" : model.Card.Size == "Medium" ? "II" : "I", Theme.PlaceholderStyle);
                    GUI.Label(new Rect(body.x, rect.yMax - 36f * scale, body.width, 18f * scale),
                        model.Card.Cooldown > 0 ? model.Card.Cooldown.ToString("0.#") + "s 冷却" : "无冷却", Theme.CenterSmallStyle);
                }
                else if (rect.height >= 120f * scale)
                {
                    List<BadgeLine> summary = PanelModel.BadgeLines(stats);
                    for (int i = 0; i < Math.Min(2, summary.Count); i++)
                        DrawEffect(new Rect(rect.x + pad, rect.yMax - (60f - i * 20f) * scale,
                            rect.width - pad * 2f, 18f * scale), summary[i]);
                }
                if (model.Series != null && model.Series.HasCooldown)
                {
                    Rect counter = new Rect(body.x, rect.yMax - 21f * scale, body.width, 18f * scale);
                    Theme.Fill(counter, new Color(0f, 0f, 0f, 0.82f));
                    GUI.Label(counter, PanelModel.TriggerText(stats), Theme.BadgeStyle);
                }
                return;
            }

            List<BadgeLine> badges = PanelModel.BadgeLines(stats);
            float top = rect.y + 75f * scale;
            float bottom = rect.yMax - 45f * scale;
            float lineHeight = Math.Min(22f * scale, (bottom - top) / Math.Max(1, badges.Count));
            float startY = top + (bottom - top - badges.Count * lineHeight) * 0.5f;
            for (int i = 0; i < badges.Count; i++)
            {
                Rect labelRect = new Rect(rect.x + pad, startY + i * lineHeight,
                    rect.width - pad * 2f, lineHeight);
                DrawEffect(labelRect, badges[i]);
            }

            Rect footer = new Rect(body.x, rect.yMax - 37f * scale, body.width, 34f * scale);
            Theme.Fill(footer, new Color(0f, 0f, 0f, 0.86f));
            Theme.Fill(new Rect(footer.x + pad, footer.y, footer.width - pad * 2f, scale), Theme.FrameDim);
            GUI.Label(footer, PanelModel.TriggerText(stats), Theme.TriggerStyle);
        }

        private static void DrawEffect(Rect rect, BadgeLine badge)
        {
            GUIStyle style = Theme.BadgeStyle;
            int originalSize = style.fontSize;
            Color original = GUI.contentColor;
            GUIContent content = new GUIContent(badge.Text);
            while (style.fontSize > Mathf.Max(6, Mathf.RoundToInt(8f * Theme.Scale)) &&
                style.CalcSize(content).x > rect.width)
                style.fontSize--;
            Theme.Fill(rect, new Color(0f, 0f, 0f, 0.70f));
            GUI.contentColor = Theme.BadgeColor(badge.ColorKey);
            GUI.Label(rect, content, style);
            GUI.contentColor = original;
            style.fontSize = originalSize;
        }

        private static string EnchantmentName(string name)
        {
            if (name == "Toxic") return "毒素";
            if (name == "Icy") return "寒冰";
            return name;
        }
    }
}
