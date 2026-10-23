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

                int? ammoMax = null;
                if (entry.Attributes != null &&
                    entry.Attributes.TryGetValue("AmmoMax", out value) && value > 0)
                    ammoMax = value;

                int? multicastBonus = null;
                if (entry.Attributes != null &&
                    entry.Attributes.TryGetValue("Multicast", out value) && value > 0)
                    multicastBonus = value - 1;

                CardDef live = card.WithLiveOverrides(cooldown, ammoMax, multicastBonus);
                live = LiveItemOverrides.Apply(live, entry.Attributes, mapped.LiveAttributeWarnings);
                live = EnchantmentMapper.Apply(live, entry, mapped.EnchantmentWarnings);
                // Preserve raw combat-state attributes for diagnostics and known
                // action amounts, but do not copy Flying or its derived 50% Slow/
                // Freeze resistances into the simulated opening card definition.
                live.LiveAttributes = entry.Attributes != null
                    ? new Dictionary<string, int>(entry.Attributes) : new Dictionary<string, int>();
                live.NativeTemplate = entry.Template;
                live.NativeEnchantment = entry.EnchantmentDefinition;
                live.NativeAttributesAreLive = entry.AttributesAreLive;
                if (entry.Size == "Small" || entry.Size == "Medium" || entry.Size == "Large")
                    live.Size = entry.Size;
                string uniqueId = card.Id + "#" + index;
                live.Id = uniqueId;

                config.Cards[uniqueId] = live;
                LoadoutItem item = new LoadoutItem();
                item.CardId = uniqueId;
                item.Slot = entry.Socket;
                config.Player.Items.Add(item);

                mapped.Enchantments[uniqueId] = entry.Enchantment;
                if (unknown) mapped.Unknown.Add(live.DisplayName());
                index++;
            }

            return mapped;
        }
    }
}
