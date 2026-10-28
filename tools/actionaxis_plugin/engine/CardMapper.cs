using System;
using System.Collections.Generic;

namespace BazaarActionAxis.Engine
{
    public sealed class MappedBoard
    {
        public BattleConfig Config = new BattleConfig();
        public Dictionary<string, string> Enchantments = new Dictionary<string, string>();
        public List<string> Unknown = new List<string>();
        public List<string> EnchantmentWarnings = new List<string>();
        public List<string> LiveAttributeWarnings = new List<string>();
        internal Dictionary<string, List<string>> LiveAttributeWarningsByCard = new Dictionary<string, List<string>>();

        // Text-catalog override ambiguity belongs to that imported graph. Once
        // native mapping replaces a particular card's graph completely, its
        // native attribute references resolve that ambiguity. Fallback cards
        // retain their warnings; native mapping warnings are handled separately.
        public List<string> UnresolvedLiveAttributeWarnings(BattleConfig finalConfig)
        {
            if (finalConfig == null) return new List<string>(LiveAttributeWarnings);
            List<string> unresolved = new List<string>();
            foreach (KeyValuePair<string, List<string>> entry in LiveAttributeWarningsByCard)
            {
                CardDef card;
                if (finalConfig.Cards.TryGetValue(entry.Key, out card) &&
                    card.DataSource == "native-template" && card.ParseStatus == "structured") continue;
                unresolved.AddRange(entry.Value);
            }
            return unresolved;
        }
    }

    public static class CardMapper
    {
        public static MappedBoard Build(
            IList<LiveCardEntry> entries,
            CardCatalog catalog,
            double playerMaxHp,
            double enemyMaxHp,
            double maxTime)
        {
            return Build(entries, catalog, playerMaxHp, enemyMaxHp, maxTime, false);
        }

        public static MappedBoard Build(
            IList<LiveCardEntry> entries,
            CardCatalog catalog,
            double playerMaxHp,
            double enemyMaxHp,
            double maxTime,
            bool useSnapshotCooldown)
        {
            MappedBoard mapped = new MappedBoard();
            BattleConfig config = mapped.Config;
            config.PlayerMaxHp = playerMaxHp;
            config.EnemyMaxHp = enemyMaxHp;
            config.Seed = 1;
            config.MaxTime = maxTime;
            config.Rules = new RuleOverrides();

            int index = 0;
            foreach (LiveCardEntry entry in entries)
            {
                string template = entry.TemplateId ?? string.Empty;
                CardDef baseCard = catalog.Find(template, entry.Tier);
                bool unknown = baseCard == null;

                CardDef card;
                if (unknown)
                {
                    card = new CardDef();
                    card.Id = template.Length > 0 ? template : "unknown";
                    card.BaseItemId = template;
                    card.NameCn = "未知卡 " + (template.Length >= 8 ? template.Substring(0, 8) : template);
                    card.Tier = string.IsNullOrEmpty(entry.Tier) ? "Bronze" : entry.Tier;
                    card.Cooldown = 0;
                    card.Passive = true;
                }
                else
                {
                    card = baseCard;
                }

                double? cooldown = null;
                int value;
                if (entry.Attributes != null &&
                    entry.Attributes.TryGetValue("CooldownMax", out value))
                    cooldown = value / 1000.0;

                double? startCooldown = null;
                // A fresh forecast starts from CooldownMax; the board outside
                // combat commonly reports Cooldown = 0. Only replay imports
                // should resume the captured remaining cooldown.
                if (useSnapshotCooldown && entry.Attributes != null &&
                    entry.Attributes.TryGetValue("Cooldown", out value) && value >= 0)
                    startCooldown = value / 1000.0;

                int? ammoMax = null;
                if (entry.Attributes != null &&
                    entry.Attributes.TryGetValue("AmmoMax", out value) && value > 0)
                    ammoMax = value;

                int? multicastBonus = null;
                if (entry.Attributes != null &&
                    entry.Attributes.TryGetValue("Multicast", out value) && value > 0)
                    multicastBonus = value - 1;

                CardDef live = card.WithLiveOverrides(cooldown, ammoMax, multicastBonus);
                List<string> liveWarnings = new List<string>();
                live = LiveItemOverrides.Apply(live, entry.Attributes, liveWarnings);
                mapped.LiveAttributeWarnings.AddRange(liveWarnings);
                // 原生模板的附魔由 NativeCardAbilityMapper 按完整定义重建；旧文本
                // 映射只负责原生层同样拒收的告警（例如未知前置条件），避免重复或假警告。
                if (NeedsTextEnchantmentWarning(entry))
                    live = EnchantmentMapper.Apply(live, entry, mapped.EnchantmentWarnings);
                // Preserve raw combat-state attributes for diagnostics and known
                // action amounts, but do not copy Flying or its derived 50% Slow/
                // Freeze resistances into the simulated opening card definition.
                live.LiveAttributes = entry.Attributes != null
                    ? new Dictionary<string, int>(entry.Attributes) : new Dictionary<string, int>();
                live.NativeTemplate = entry.Template;
                live.NativeEnchantment = entry.EnchantmentDefinition;
                live.EnchantmentName = string.IsNullOrEmpty(entry.Enchantment) || entry.Enchantment == "None"
                    ? null : entry.Enchantment;
                live.NativeAttributesAreLive = entry.AttributesAreLive;
                // 技能光环先于物品规则映射执行，必须在此提供原生标签、名称与品质。
                if (!NativeSkillSupport.Empty(entry.Template))
                {
                    if (!string.IsNullOrEmpty(entry.Tier)) live.Tier = entry.Tier;
                    if (unknown)
                    {
                        live.Name = NativeSkillSupport.Text(entry.Template, "InternalName");
                        JsonValue localization = entry.Template["Localization"];
                        live.NameCn = (localization.Kind == JsonKind.Object ?
                            NativeSkillSupport.Text(localization["Title"], "Text") : null) ?? live.Name ?? live.NameCn;
                    }
                    if (entry.Template["Tags"].Kind == JsonKind.Array)
                    {
                        live.Tags = new List<string>();
                        foreach (JsonValue tag in entry.Template["Tags"].AsArray()) live.Tags.Add(tag.AsString());
                        if (!NativeSkillSupport.Empty(entry.EnchantmentDefinition))
                            foreach (JsonValue tag in entry.EnchantmentDefinition["Tags"].AsArray())
                                if (!live.Tags.Contains(tag.AsString())) live.Tags.Add(tag.AsString());
                    }
                    if (entry.Template["HiddenTags"].Kind == JsonKind.Array)
                    {
                        live.HiddenTags = new List<string>();
                        foreach (JsonValue tag in entry.Template["HiddenTags"].AsArray()) live.HiddenTags.Add(tag.AsString());
                    }
                    if (entry.Template["Heroes"].Kind == JsonKind.Array)
                    {
                        live.Heroes = new List<string>();
                        foreach (JsonValue hero in entry.Template["Heroes"].AsArray()) live.Heroes.Add(hero.AsString());
                    }
                }
                if (entry.Size == "Small" || entry.Size == "Medium" || entry.Size == "Large")
                    live.Size = entry.Size;
                string uniqueId = entry.UseInstanceIdAsId && !string.IsNullOrEmpty(entry.InstanceId)
                    ? entry.InstanceId
                    : card.Id + "#" + index;
                live.Id = uniqueId;
                if (liveWarnings.Count > 0) mapped.LiveAttributeWarningsByCard[uniqueId] = liveWarnings;

                config.Cards[uniqueId] = live;
                LoadoutItem item = new LoadoutItem();
                item.CardId = uniqueId;
                item.Slot = entry.Socket;
                item.StartCooldown = startCooldown;
                config.Player.Items.Add(item);

                mapped.Enchantments[uniqueId] = entry.Enchantment;
                if (unknown) mapped.Unknown.Add(live.DisplayName());
                index++;
            }

            return mapped;
        }

        // 原生模板的附魔能力默认由 NativeCardAbilityMapper 完整映射。只有原生层
        // 同样会拒收的未知前置条件才需要旧文本映射补一条可见告警。
        private static bool NeedsTextEnchantmentWarning(LiveCardEntry entry)
        {
            if (NativeSkillSupport.Empty(entry.Template)) return true;
            if (string.IsNullOrEmpty(entry.Enchantment) || entry.Enchantment == "None") return false;
            JsonValue native = entry.EnchantmentDefinition;
            if (NativeSkillSupport.Empty(native) || native.Kind != JsonKind.Object) return true;
            foreach (KeyValuePair<string, JsonValue> ability in native["Abilities"].AsObject())
                if (!NativeSkillSupport.PrerequisitesSupported(ability.Value["Prerequisites"])) return true;
            return false;
        }
    }
}
