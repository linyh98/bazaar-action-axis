using System;
using System.Collections.Generic;

namespace BazaarActionAxis.Engine
{
    public sealed class MonsterReadResult
    {
        public List<LiveMonsterEntry> Choices = new List<LiveMonsterEntry>();
        public string Error;
        public string ChoiceKey;
        public int DefaultIndex;
    }

    public sealed class MonsterSelection
    {
        private string _key;
        private bool _hasChoices;
        public int Index;

        public LiveMonsterEntry Resolve(MonsterReadResult read)
        {
            if (read == null || read.Choices.Count == 0)
            {
                _key = null;
                _hasChoices = false;
                Index = 0;
                return null;
            }
            if (!_hasChoices || _key != read.ChoiceKey) Index = read.DefaultIndex;
            _key = read.ChoiceKey;
            _hasChoices = true;
            Index = Math.Max(0, Math.Min(read.Choices.Count - 1, Index));
            return read.Choices[Index];
        }
    }

    public sealed class LiveMonsterEntry
    {
        public string Id;
        public string EncounterId;
        public string Name;
        public double Health;
        public double MaxHp;
        public Dictionary<string, int> Attributes = new Dictionary<string, int>();
        public List<LiveCardEntry> Cards = new List<LiveCardEntry>();
        public List<LiveSkillEntry> Skills = new List<LiveSkillEntry>();
        public JsonValue TemplateDebug;
        public List<string> Warnings = new List<string>();
    }

    // TMonster contains instance overrides, while TCardItem.Tiers contains sparse
    // deltas. Merge every tier up to the selected tier before applying overrides.
    public static class MonsterTemplateExtractor
    {
        private static readonly string[] Tiers = { "Bronze", "Silver", "Gold", "Diamond", "Legendary" };

        public static LiveMonsterEntry FromJson(JsonValue monster, Func<string, JsonValue> getCard)
        {
            if (monster == null || monster.Kind != JsonKind.Object)
                throw new ArgumentException("怪物模板为空");
            LiveMonsterEntry result = new LiveMonsterEntry();
            result.Id = Text(monster, "Id");
            result.Name = Text(monster, "InternalName") ?? result.Id;
            result.TemplateDebug = monster;
            JsonValue player = Get(monster, "Player");
            CopyAttributes(Get(player, "Attributes"), result.Attributes);
            int value;
            result.MaxHp = result.Attributes.TryGetValue("HealthMax", out value) ? value : 0;
            result.Health = result.Attributes.TryGetValue("Health", out value) ? value : result.MaxHp;
            if (result.MaxHp <= 0) throw new InvalidOperationException("怪物缺少有效生命上限");
            int order = 0;
            foreach (JsonValue raw in Get(Get(player, "Hand"), "Items").AsArray())
            {
                LiveCardEntry card = new LiveCardEntry();
                card.TemplateId = Text(raw, "TemplateId");
                card.InstanceId = Text(raw, "InstanceId") ?? "monster:" + result.Id + ":item:" + order;
                card.Tier = Text(raw, "Tier") ?? "Bronze";
                card.Enchantment = Text(raw, "EnchantmentType");
                card.Order = order;
                card.Socket = Socket(Get(raw, "SocketId"), order);
                card.Template = getCard == null ? null : getCard(card.TemplateId);
                card.AttributesAreLive = false;
                card.Size = Text(card.Template, "Size") ?? "Small";
                card.Attributes = TierAttributes(card.Template, card.Tier);
                card.EnchantmentDefinition = Get(Get(card.Template, "Enchantments"), card.Enchantment);
                CopyAttributes(Get(card.EnchantmentDefinition, "Attributes"), card.Attributes);
                CopyAttributes(Get(raw, "Attributes"), card.Attributes);
                if (card.Template == null || card.Template.Kind != JsonKind.Object)
                    result.Warnings.Add("怪物卡牌缺少原生模板：" + card.TemplateId);
                result.Cards.Add(card);
                order++;
            }
            result.Cards.Sort(delegate(LiveCardEntry a, LiveCardEntry b) { return a.Socket.CompareTo(b.Socket); });
            order = 0;
            foreach (JsonValue raw in Get(player, "Skills").AsArray())
            {
                LiveSkillEntry skill = new LiveSkillEntry();
                skill.TemplateId = Text(raw, "TemplateId");
                skill.InstanceId = Text(raw, "InstanceId") ?? "monster:" + result.Id + ":skill:" + order;
                skill.Tier = Text(raw, "Tier") ?? "Bronze";
                skill.Order = order++;
                skill.Template = getCard == null ? null : getCard(skill.TemplateId);
                skill.Name = Text(Get(Get(skill.Template, "Localization"), "Title"), "Text") ?? Text(skill.Template, "InternalName") ?? skill.TemplateId;
                skill.Attributes = TierAttributes(skill.Template, skill.Tier);
                CopyAttributes(Get(raw, "Attributes"), skill.Attributes);
                foreach (JsonValue line in Get(Get(skill.Template, "Localization"), "Tooltips").AsArray())
                {
                    string text = Text(Get(line, "Content"), "Text");
                    if (!string.IsNullOrEmpty(text)) skill.RawTooltips.Add(text);
                }
                if (skill.Template == null || skill.Template.Kind != JsonKind.Object)
                    result.Warnings.Add("怪物技能缺少原生模板：" + skill.TemplateId);
                result.Skills.Add(skill);
            }
            foreach (JsonValue effect in Get(player, "Effects").AsArray())
                result.Warnings.Add("怪物初始额外效果尚未映射：" + (Text(effect, "$type") ?? "unknown"));
            if (Get(Get(player, "Socket"), "Effects").Count > 0)
                result.Warnings.Add("怪物插槽效果尚未加入模拟");
            return result;
        }

        public static Dictionary<string, int> TierAttributes(JsonValue template, string tier)
        {
            Dictionary<string, int> result = new Dictionary<string, int>();
            JsonValue tiers = Get(template, "Tiers");
            // Native item and skill tier lookups alias Legendary to Diamond.
            // Preserve the monster instance Tier and apply instance overrides later.
            string lookupTier = tier == "Legendary" ? "Diamond" : tier;
            int selected = Array.IndexOf(Tiers, lookupTier);
            if (selected < 0) selected = 0;
            for (int i = 0; i <= selected; i++)
                CopyAttributes(Get(Get(tiers, Tiers[i]), "Attributes"), result);
            return result;
        }

        private static int Socket(JsonValue value, int fallback)
        {
            if (value.Kind == JsonKind.Number) return value.AsInt();
            string text = value.AsString();
            if (string.IsNullOrEmpty(text)) return fallback;
            int start = text.Length;
            while (start > 0 && char.IsDigit(text[start - 1])) start--;
            int socket;
            return int.TryParse(text.Substring(start), out socket) ? socket : fallback;
        }

        private static void CopyAttributes(JsonValue source, Dictionary<string, int> target)
        {
            foreach (KeyValuePair<string, JsonValue> pair in source.AsObject())
                if (pair.Value.Kind == JsonKind.Number) target[pair.Key] = pair.Value.AsInt();
        }

        private static JsonValue Get(JsonValue value, string key)
        {
            return value != null && value.Kind == JsonKind.Object && key != null ? value[key] : JsonValue.Null;
        }

        private static string Text(JsonValue value, string key) { return Get(value, key).AsString(); }
    }
}
