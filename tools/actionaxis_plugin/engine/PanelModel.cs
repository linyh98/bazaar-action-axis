using System;
using System.Collections.Generic;
using System.Globalization;

namespace BazaarActionAxis.Engine
{
    public sealed class BadgeLine
    {
        public string Text;
        public string ColorKey;
    }

    public static class PanelModel
    {
        public const int BoardSlots = 10;

        public static int CardSpan(CardDef card)
        {
            string size = card != null ? card.Size : null;
            if (string.Equals(size, "Large", StringComparison.OrdinalIgnoreCase) || size == "3") return 3;
            if (string.Equals(size, "Medium", StringComparison.OrdinalIgnoreCase) || size == "2") return 2;
            return 1;
        }

        public static float BoardCardX(int slot, float boardX, float boardWidth, float gap)
        {
            return boardX + slot * (BoardUnitWidth(boardWidth, gap) + Math.Max(0, gap));
        }

        public static float BoardCardWidth(CardDef card, float boardWidth, float gap)
        {
            int span = CardSpan(card);
            return span * BoardUnitWidth(boardWidth, gap) + (span - 1) * Math.Max(0, gap);
        }

        public static float BoardUnitWidth(float boardWidth, float gap)
        {
            return Math.Max(0, (boardWidth - (BoardSlots - 1) * Math.Max(0, gap)) / BoardSlots);
        }

        public static string TriggerText(CardStats stats)
        {
            int count = stats != null ? stats.ActivateCount : 0;
            return "x" + count.ToString(CultureInfo.InvariantCulture);
        }

        public static List<BadgeLine> BadgeLines(CardStats stats)
        {
            List<BadgeLine> lines = new List<BadgeLine>();
            if (stats == null) return lines;

            if (NonZero(stats.Damage))
                Add(lines, "伤害 " + FormatNumber(stats.Damage), "damage");
            if (stats.HasteCount != 0)
                Add(lines, "加速 ×" + stats.HasteCount + " · " + FormatNumber(stats.HasteSeconds) + "s", "haste");
            if (stats.FreezeCount != 0)
                Add(lines, "冻结 ×" + stats.FreezeCount + " · " + FormatNumber(stats.FreezeSeconds) + "s", "freeze");
            if (stats.SlowCount != 0)
                Add(lines, "减速 ×" + stats.SlowCount + " · " + FormatNumber(stats.SlowSeconds) + "s", "slow");
            if (NonZero(stats.Shield))
                Add(lines, "护盾 " + FormatNumber(stats.Shield), "shield");
            if (NonZero(stats.Heal))
                Add(lines, "治疗 " + FormatNumber(stats.Heal), "heal");
            if (NonZero(stats.BurnStacks))
                Add(lines, "灼烧 " + FormatNumber(stats.BurnStacks) + " 层", "burn");
            if (NonZero(stats.PoisonStacks))
                Add(lines, "中毒 " + FormatNumber(stats.PoisonStacks) + " 层", "poison");
            if (NonZero(stats.ChargeSeconds))
                Add(lines, "充能 " + FormatNumber(stats.ChargeSeconds) + "s", "charge");
            return lines;
        }

        public static double PlayheadFromMouse(
            float mouseX,
            float barX,
            float barWidth,
            double endTime)
        {
            if (endTime <= 0 || barWidth <= 0) return 0;
            double ratio = (mouseX - barX) / barWidth;
            if (ratio < 0) ratio = 0;
            if (ratio > 1) ratio = 1;
            double time = ratio * endTime;
            time = JsMath.Round(time * 10) / 10.0;
            if (time < 0) time = 0;
            if (time > endTime) time = endTime;
            return time;
        }

        public static string FormatNumber(double value)
        {
            double rounded = JsMath.Round(value);
            if (Math.Abs(value - rounded) < 1e-9)
                return ((long)rounded).ToString(CultureInfo.InvariantCulture);
            return value.ToString("0.#", CultureInfo.InvariantCulture);
        }

        private static bool NonZero(double value)
        {
            return Math.Abs(value) > 1e-9;
        }

        private static void Add(List<BadgeLine> lines, string text, string colorKey)
        {
            BadgeLine line = new BadgeLine();
            line.Text = text;
            line.ColorKey = colorKey;
            lines.Add(line);
        }
    }
}
