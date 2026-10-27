using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // 映射入口矩阵：实际调用映射器，不将类型字符串存在视为支持证据。
    // 每个组合独立棋盘；通过不等于实战语义和随机序列已对齐。报告保留品质来源、
    // 实时属性场景、双方/混合棋盘及附魔、实时属性告警，不能只输出通过率。
    public static class NativeCoverageRunner
    {
        private static readonly CardCatalog Catalog = CardCatalog.FromCards(new List<CardDef>());
        private static readonly string[] CanonicalTiers = { "Bronze", "Silver", "Gold", "Diamond" };
        private static readonly string[] SkillTiers = { "Bronze", "Silver", "Gold", "Diamond", "Legendary" };
        // template：纯模板属性；live：实时属性；mixed：其他英雄且双方均有卡。
        private static readonly string[] Scenarios = { "template", "live", "mixed" };

        public static int Run(string input, string output)
        {
            List<JsonValue> rows = new List<JsonValue>();
            foreach (string line in File.ReadLines(input))
                if (line.Trim().Length > 0) rows.Add(Json.Parse(line));
            return Run(rows, output);
        }

        public static int Run(List<JsonValue> rows, string output)
        {
            Dictionary<string, int> totals = new Dictionary<string, int>();
            Dictionary<string, int> tierSources = new Dictionary<string, int>();
            Dictionary<string, int> scenarioCounts = new Dictionary<string, int>();
            HashSet<string> items = new HashSet<string>(), skills = new HashSet<string>();
            HashSet<string> blockedItems = new HashSet<string>(), blockedSkills = new HashSet<string>();
            HashSet<string> warnedItems = new HashSet<string>(), warnedSkills = new HashSet<string>();
            int cases = 0, enchantmentWarningRows = 0, liveWarningRows = 0;
            using (StreamWriter writer = new StreamWriter(output, false, new UTF8Encoding(false)))
            {
                writer.WriteLine("kind\ttable\tid\tname\ttier\ttier_source\tenchantment\tside\tscenario\tattributes_live\tplayer_hero\tenemy_hero\tstatus\tenchantment_status\tlive_attribute_status\tenchantment_warning\tlive_attribute_warning\treason");
                foreach (JsonValue row in rows)
                {
                    JsonValue template = row["template"];
                    string id = row["id"].AsString();
                    string table = row["table"].AsString() ?? "cards";
                    string name = NativeSkillSupport.Text(template, "InternalName");
                    string type = NativeSkillSupport.Type(template);
                    bool skill = type == "TCardSkill";
                    if (skill) skills.Add(id); else items.Add(id);
                    List<TierCase> tiers = TierCases(template, skill);
                    List<string> enchants = Enchantments(template, skill);
                    foreach (TierCase tier in tiers)
                        foreach (string enchant in enchants)
                            foreach (Side side in new[] { Side.Player, Side.Enemy })
                                foreach (string scenario in Scenarios)
                                {
                                    CaseResult result;
                                    try { result = Check(id, template, tier.Tier, enchant, side, scenario, skill); }
                                    catch (Exception error)
                                    {
                                        result = CaseResult.Exception(error);
                                    }
                                    string kind = skill ? "skill" : "item";
                                    string statusKey = kind + "/" + result.Status;
                                    Increment(totals, statusKey);
                                    Increment(tierSources, tier.Source);
                                    Increment(scenarioCounts, scenario);
                                    if (result.EnchantmentWarnings.Count > 0) enchantmentWarningRows++;
                                    if (result.LiveAttributeWarnings.Count > 0) liveWarningRows++;
                                    if (result.Status == "unsupported" || result.Status == "exception")
                                    {
                                        if (skill) blockedSkills.Add(id); else blockedItems.Add(id);
                                    }
                                    if (result.Status == "mapped-warning" || result.Status == "warning")
                                    {
                                        if (skill) warnedSkills.Add(id); else warnedItems.Add(id);
                                    }
                                    writer.WriteLine(string.Join("\t", new[] {
                                        kind, Clean(table), id, Clean(name), tier.Tier, tier.Source,
                                        enchant.Length == 0 ? "None" : enchant, side.ToString(), scenario,
                                        result.AttributesAreLive ? "true" : "false", Clean(result.PlayerHero),
                                        Clean(result.EnemyHero), result.Status, result.EnchantmentStatus,
                                        result.LiveAttributeStatus, Clean(string.Join(" || ", result.EnchantmentWarnings.ToArray())),
                                        Clean(string.Join(" || ", result.LiveAttributeWarnings.ToArray())), Clean(result.Reason)
                                    }));
                                    cases++;
                                }
                }
            }
            Console.WriteLine("映射入口检查完成；不是语义覆盖率或原生回放对账。");
            Console.WriteLine("物品模板=" + items.Count + "；至少一个组合受阻=" + blockedItems.Count +
                "；至少一个组合带附魔/实时属性告警=" + warnedItems.Count);
            Console.WriteLine("技能模板=" + skills.Count + "；至少一个组合受阻=" + blockedSkills.Count +
                "；至少一个组合带告警=" + warnedSkills.Count);
            Console.WriteLine("检查组合=" + cases + "；附魔告警行=" + enchantmentWarningRows + "；实时属性告警行=" + liveWarningRows);
            foreach (KeyValuePair<string, int> pair in totals) Console.WriteLine(pair.Key + "=" + pair.Value);
            foreach (KeyValuePair<string, int> pair in tierSources) Console.WriteLine("tier_source/" + pair.Key + "=" + pair.Value);
            foreach (KeyValuePair<string, int> pair in scenarioCounts) Console.WriteLine("scenario/" + pair.Key + "=" + pair.Value);
            Console.WriteLine("明细=" + output);
            return 0;
        }

        private static void Increment(Dictionary<string, int> values, string key)
        {
            int count;
            values.TryGetValue(key, out count);
            values[key] = count + 1;
        }

        private sealed class TierCase
        {
            public string Tier;
            public string Source;

            public TierCase(string tier, string source)
            {
                Tier = tier;
                Source = source;
            }
        }

        private sealed class CaseResult
        {
            public string Status;
            public string Reason = "";
            public string EnchantmentStatus = "none";
            public string LiveAttributeStatus = "template";
            public bool AttributesAreLive;
            public string PlayerHero;
            public string EnemyHero;
            public List<string> EnchantmentWarnings = new List<string>();
            public List<string> LiveAttributeWarnings = new List<string>();

            public static CaseResult Exception(Exception error)
            {
                CaseResult result = new CaseResult();
                result.Status = "exception";
                result.Reason = error.GetType().Name + ": " + error.Message;
                return result;
            }
        }

        private sealed class ItemMapping
        {
            public string ParseStatus;
            public string Reason = "";
            public List<string> EnchantmentWarnings = new List<string>();
            public List<string> LiveAttributeWarnings = new List<string>();
            public List<string> HardWarnings = new List<string>();
        }

        private static List<TierCase> TierCases(JsonValue template, bool skill)
        {
            JsonValue tiers = template["Tiers"];
            List<TierCase> result = new List<TierCase>();
            if (skill)
            {
                foreach (string tier in SkillTiers)
                {
                    if (tier == "Legendary") result.Add(new TierCase(tier, "legendary-alias"));
                    else result.Add(new TierCase(tier, tiers.Has(tier) ? "declared" : "inactive-sparse"));
                }
                return result;
            }
            // 物品品质区间是连续的原生可获取区间；未声明的低品质不可到达，
            // 因此只枚举声明品质。所有物品都以 Diamond 为终点（由基线审计复核）。
            foreach (string tier in CanonicalTiers)
                if (tiers.Has(tier)) result.Add(new TierCase(tier, "declared"));
            if (tiers.Has("Diamond")) result.Add(new TierCase("Legendary", "legendary-alias"));
            else if (tiers.Has("Legendary")) result.Add(new TierCase("Legendary", "declared"));
            if (result.Count == 0)
            {
                string starting = NativeSkillSupport.Text(template, "StartingTier");
                result.Add(new TierCase(string.IsNullOrEmpty(starting) ? "Bronze" : starting, "missing-tier"));
            }
            return result;
        }

        private static List<string> Enchantments(JsonValue template, bool skill)
        {
            List<string> result = new List<string>();
            result.Add("");
            if (skill || template["Enchantments"].Kind != JsonKind.Object) return result;
            List<string> names = new List<string>(template["Enchantments"].AsObject().Keys);
            names.Sort(StringComparer.Ordinal);
            result.AddRange(names);
            return result;
        }

        private static string Clean(string value)
        {
            return (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        private static CaseResult Check(string id, JsonValue template, string tier, string enchant,
            Side side, string scenario, bool skill)
        {
            bool mixed = scenario == "mixed";
            CaseResult result = new CaseResult();
            result.AttributesAreLive = scenario != "template";
            result.PlayerHero = mixed ? "Mak" : "Dooley";
            result.EnemyHero = "Dooley";
            if (skill)
            {
                BattleConfig config = NewConfig(result.PlayerHero, result.EnemyHero);
                AddPeer(config, side, 0);
                AddPeer(config, mixed ? Opposite(side) : side, 8);
                LiveSkillEntry entry = new LiveSkillEntry { TemplateId = id, Tier = tier, Template = template,
                    AttributesAreLive = result.AttributesAreLive, TemplateBoard = scenario == "template" };
                if (result.AttributesAreLive)
                    entry.Attributes = MonsterTemplateExtractor.TierAttributes(template, tier);
                SkillMappingResult mapped = SkillMapper.Apply(config, new[] { entry }, null, side);
                result.Status = mapped.Skills[0].Status;
                result.Reason = mapped.Skills[0].Reason;
                result.LiveAttributeStatus = result.AttributesAreLive ? "live" : "template";
                return result;
            }

            ItemMapping mapping = MapItem(id, template, tier, enchant, side, scenario);
            result.EnchantmentWarnings.AddRange(mapping.EnchantmentWarnings);
            result.LiveAttributeWarnings.AddRange(mapping.LiveAttributeWarnings);
            result.Reason = mapping.Reason;
            if (enchant.Length > 0)
            {
                if (result.EnchantmentWarnings.Count > 0) result.EnchantmentStatus = "warning";
                else if (mapping.HardWarnings.Count == 0) result.EnchantmentStatus = "applied";
                else
                {
                    // 原生路径的拒绝按整卡返回。去掉附魔再跑一次，只有多出来的
                    // 告警才归因于附魔；基准本身失败则不能替附魔认账。
                    ItemMapping baseline = MapItem(id, template, tier, "", side, scenario);
                    List<string> extra = ExtraWarnings(mapping.HardWarnings, baseline.HardWarnings);
                    if (extra.Count > 0)
                    {
                        result.EnchantmentStatus = "warning";
                        foreach (string warning in extra) result.EnchantmentWarnings.Add("附魔未应用：" + warning);
                    }
                    else
                    {
                        result.EnchantmentStatus = "blocked-base";
                    }
                }
            }
            result.LiveAttributeStatus = !result.AttributesAreLive ? "template" :
                result.LiveAttributeWarnings.Count > 0 ? "warning" : "live";
            if (mapping.ParseStatus != "structured" || mapping.HardWarnings.Count > 0)
                result.Status = "unsupported";
            else if (result.EnchantmentWarnings.Count > 0 || result.LiveAttributeWarnings.Count > 0)
                result.Status = "mapped-warning";
            else
                result.Status = "mapped";
            if (result.Status == "mapped-warning" && result.Reason.Length == 0)
                result.Reason = "入口映射成功，但附魔或实时属性存在待支持告警；语义不完整";
            return result;
        }

        private static ItemMapping MapItem(string id, JsonValue template, string tier, string enchant,
            Side side, string scenario)
        {
            string playerHero = scenario == "mixed" ? "Mak" : "Dooley";
            BattleConfig config = NewConfig(playerHero, "Dooley");
            AddPeer(config, side, 0);
            AddPeer(config, scenario == "mixed" ? Opposite(side) : side, 8);
            LiveCardEntry item = new LiveCardEntry { TemplateId = id, Socket = 3, Tier = tier, Template = template,
                Size = NativeSkillSupport.Text(template, "Size"), AttributesAreLive = scenario != "template",
                Attributes = MonsterTemplateExtractor.TierAttributes(template, tier), Enchantment = enchant };
            ItemMapping result = new ItemMapping();
            if (enchant.Length > 0)
            {
                item.EnchantmentDefinition = template["Enchantments"][enchant];
                foreach (KeyValuePair<string, JsonValue> attr in item.EnchantmentDefinition["Attributes"].AsObject())
                    if (attr.Value.Kind == JsonKind.Number) item.Attributes[attr.Key] = attr.Value.AsInt();
            }
            MappedBoard board = CardMapper.Build(new List<LiveCardEntry> { item }, Catalog, 10000, 10000, 5);
            result.EnchantmentWarnings.AddRange(board.EnchantmentWarnings);
            result.LiveAttributeWarnings.AddRange(board.LiveAttributeWarnings);
            LoadoutItem slot = board.Config.Player.Items[0];
            config.Cards[slot.CardId] = board.Config.Cards[slot.CardId];
            (side == Side.Player ? config.Player : config.Enemy).Items.Add(slot);
            result.HardWarnings.AddRange(NativeCardAbilityMapper.Apply(config));
            foreach (string warning in NativeItemMapper.Apply(config))
                if (!warning.StartsWith("coverage-peer-", StringComparison.Ordinal)) result.HardWarnings.Add(warning);
            CardDef mappedCard = config.Cards[slot.CardId];
            result.ParseStatus = mappedCard.ParseStatus;
            if (mappedCard.MappingWarnings.Count > 0)
            {
                result.LiveAttributeWarnings.AddRange(mappedCard.MappingWarnings);
                if (result.Reason.Length == 0)
                    result.Reason = string.Join(" | ", mappedCard.MappingWarnings.ToArray());
            }
            result.Reason = string.Join(" | ", result.HardWarnings.ToArray());
            return result;
        }

        private static BattleConfig NewConfig(string playerHero, string enemyHero)
        {
            return new BattleConfig { NativeMechanics = true, PlayerHero = playerHero,
                EnemyHero = enemyHero, PlayerMaxHp = 10000, EnemyMaxHp = 10000 };
        }

        private static List<string> ExtraWarnings(List<string> all, List<string> baseline)
        {
            List<string> remaining = new List<string>(baseline);
            List<string> extra = new List<string>();
            foreach (string warning in all)
            {
                int index = remaining.IndexOf(warning);
                if (index >= 0) remaining.RemoveAt(index); else extra.Add(warning);
            }
            return extra;
        }

        private static Side Opposite(Side side)
        {
            return side == Side.Player ? Side.Enemy : Side.Player;
        }

        private static void AddPeer(BattleConfig config, Side side, int slot)
        {
            CardDef card = new CardDef { Id = "coverage-peer-" + side + slot, Cooldown = 5, NativeAttributesAreLive = false };
            card.Tags.AddRange(new[] { "Weapon", "Friend", "Shield" });
            card.Heroes.Add("Common");
            card.LiveAttributes["DamageAmount"] = 10;
            card.LiveAttributes["ShieldApplyAmount"] = 10;
            card.LiveAttributes["CooldownMax"] = 5000;
            config.Cards[card.Id] = card;
            (side == Side.Player ? config.Player : config.Enemy).Items.Add(new LoadoutItem { CardId = card.Id, Slot = slot });
        }
    }
}
