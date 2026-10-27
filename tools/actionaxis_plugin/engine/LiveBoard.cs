using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace BazaarActionAxis.Engine
{
    public sealed class LiveCardEntry
    {
        public string InstanceId;
        // Replay ledgers must keep the native instance identity instead of the
        // catalog's template#index fallback.
        public bool UseInstanceIdAsId;
        public string TemplateId;
        public string Tier;
        public string Size;
        public int Order;
        public int Socket;
        public string Enchantment;
        public JsonValue Template;
        // False for pure monster templates; live preview attributes already include auras.
        public bool AttributesAreLive = true;
        // Exact selected native enchantment, including added abilities and auras.
        public JsonValue EnchantmentDefinition;
        public string EnchantmentReadError;
        public Dictionary<string, int> Attributes = new Dictionary<string, int>();
    }

    public sealed class BoardReadResult
    {
        public bool Ok;
        public string Error;
        public List<LiveCardEntry> Cards = new List<LiveCardEntry>();
        public List<LiveSkillEntry> Skills = new List<LiveSkillEntry>();
        public Dictionary<string, int> PlayerAttributes = new Dictionary<string, int>();
        // 玩家英雄身份（BPP snapshot 的 Hero），用于 TCardConditionalPlayerHero。
        public string PlayerHero;
        // 对手英雄身份（Run.Opponent.Hero）；怪物战为 null，不能拿玩家身份顶替。
        public string EnemyHero;
        public double? PlayerHealth;
        public double? PlayerMaxHealth;
        public string SkillReadError;
        public string CardReadError;

        public static BoardReadResult Fail(string message)
        {
            BoardReadResult result = new BoardReadResult();
            result.Ok = false;
            result.Error = message;
            return result;
        }
    }

    public sealed class LiveSkillEntry
    {
        public string InstanceId;
        public string TemplateId;
        public string Tier;
        public string Name;
        public int Order;
        public bool AttributesAreLive;
        // 纯模板矩阵/测试棋盘：允许把静态人物光环在现场结算；
        // 实时棋盘只沿用快照，避免重复叠加。
        public bool TemplateBoard;
        public Dictionary<string, int> Attributes = new Dictionary<string, int>();
        public JsonValue Template;
        public List<string> RawTooltips = new List<string>();
    }

    public static class ReflectionUtil
    {
        public static object GetProp(object target, string name)
        {
            if (target == null) return null;
            PropertyInfo property = target.GetType().GetProperty(
                name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return property == null ? null : property.GetValue(target, null);
        }

        public static IEnumerable<object> GetEnumerable(object target)
        {
            IEnumerable enumerable = target as IEnumerable;
            if (enumerable == null) yield break;
            foreach (object item in enumerable) yield return item;
        }

        public static string EnumText(object value)
        {
            return value == null ? null : value.ToString();
        }

        public static int IntText(object value)
        {
            if (value == null) return 0;
            return Convert.ToInt32(value);
        }
    }

    public static class SnapshotExtractor
    {
        public static BoardReadResult FromSnapshot(object snapshot)
        {
            if (snapshot == null)
                return BoardReadResult.Fail("BPP 没有返回游戏状态，请先进入一局游戏");

            object boardItems = ReflectionUtil.GetProp(snapshot, "BoardItems");
            string playerHero = HeroName(ReflectionUtil.GetProp(snapshot, "Hero"));
            List<LiveCardEntry> cards = new List<LiveCardEntry>();
            foreach (object raw in ReflectionUtil.GetEnumerable(boardItems))
            {
                if (raw == null) continue;
                cards.Add(FromCard(raw));
            }

            if (cards.Count == 0)
            {
                BoardReadResult empty = BoardReadResult.Fail("当前棋盘没有卡牌");
                empty.PlayerHero = playerHero;
                return empty;
            }

            BoardReadResult result = new BoardReadResult();
            result.Ok = true;
            result.Cards = cards;
            result.PlayerHero = playerHero;
            return result;
        }

        private static string HeroName(object value)
        {
            return value == null ? null : value.ToString();
        }

        private static LiveCardEntry FromCard(object raw)
        {
            LiveCardEntry entry = new LiveCardEntry();
            entry.InstanceId = ReflectionUtil.GetProp(raw, "InstanceId") as string;
            string templateId = ReflectionUtil.EnumText(ReflectionUtil.GetProp(raw, "TemplateId"));
            entry.TemplateId = templateId == null ? null : templateId.ToLowerInvariant();
            entry.Tier = ReflectionUtil.EnumText(ReflectionUtil.GetProp(raw, "Tier"));
            entry.Size = ReflectionUtil.EnumText(ReflectionUtil.GetProp(raw, "Size"));
            entry.Order = ReflectionUtil.IntText(ReflectionUtil.GetProp(raw, "Order"));
            entry.Enchantment = ReflectionUtil.EnumText(ReflectionUtil.GetProp(raw, "EnchantmentType"));

            string socket = ReflectionUtil.EnumText(ReflectionUtil.GetProp(raw, "SocketId"));
            entry.Socket = ParseSocket(socket, entry.Order);

            object attributes = ReflectionUtil.GetProp(raw, "Attributes");
            foreach (object pair in ReflectionUtil.GetEnumerable(attributes))
            {
                object key = ReflectionUtil.GetProp(pair, "Key");
                if (key == null) continue;
                int value = ReflectionUtil.IntText(ReflectionUtil.GetProp(pair, "Value"));
                entry.Attributes[key.ToString()] = value;
            }

            return entry;
        }

        private static int ParseSocket(string socket, int fallback)
        {
            if (string.IsNullOrEmpty(socket)) return fallback;
            int start = socket.Length;
            while (start > 0 && socket[start - 1] >= '0' && socket[start - 1] <= '9') start--;
            if (start == socket.Length) return fallback;
            int value;
            return int.TryParse(socket.Substring(start), out value) ? value : fallback;
        }
    }
}
