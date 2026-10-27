using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // 怪物读取矩阵：只验证 Player/Hand/Items/Skills 的模板读取、品质来源与附魔定义，
    // 不执行 Ability/Aura 效果。166 个怪物是 G00 的横向输入，缺模板与稀疏品质必须
    // 保留原始路径和分类，不能只输出一个总数。
    public static class MonsterCoverageRunner
    {
        public const string Header = "record\tmonster_id\tmonster_name\tindex\ttemplate_id\ttemplate_name\tkind\ttier\ttier_source\tenchantment\tenchantment_status\tsocket\tstatus\treason";

        public static int Run(string monsterInput, string cardInput, string output)
        {
            Dictionary<string, JsonValue> cards = LoadCards(cardInput);
            List<JsonValue> monsters = new List<JsonValue>();
            foreach (string line in File.ReadLines(monsterInput))
                if (line.Trim().Length > 0) monsters.Add(Json.Parse(line));
            return Run(monsters, cards, output);
        }

        public static int Run(List<JsonValue> monsters, Dictionary<string, JsonValue> cards, string output)
        {
            int monsterCount = 0, warningMonsters = 0, exceptions = 0;
            int itemRows = 0, skillRows = 0, missingTemplates = 0, sparseTiers = 0, missingEnchantments = 0;
            using (StreamWriter writer = new StreamWriter(output, false, new UTF8Encoding(false)))
            {
                writer.WriteLine(Header);
                foreach (JsonValue row in monsters)
                {
                    JsonValue template = row["template"];
                    string id = row["id"].AsString();
                    string name = NativeSkillSupport.Text(template, "InternalName") ?? id;
                    monsterCount++;
                    LiveMonsterEntry read;
                    try
                    {
                        read = MonsterTemplateExtractor.FromJson(template, delegate(string cardId)
                        {
                            JsonValue found;
                            return cardId != null && cards.TryGetValue(cardId, out found) ? found : null;
                        });
                    }
                    catch (Exception error)
                    {
                        exceptions++;
                        WriteMonster(writer, id, name, "exception", error.GetType().Name + ": " + error.Message);
                        continue;
                    }

                    string monsterStatus = read.Warnings.Count > 0 ? "warning" : "read";
                    if (read.Warnings.Count > 0) warningMonsters++;
                    WriteMonster(writer, id, name, monsterStatus, string.Join(" || ", read.Warnings.ToArray()));

                    List<LiveCardEntry> hand = new List<LiveCardEntry>(read.Cards);
                    hand.Sort(delegate(LiveCardEntry left, LiveCardEntry right) { return left.Order.CompareTo(right.Order); });
                    foreach (LiveCardEntry card in hand)
                    {
                        ItemRow detail = DescribeItem(card);
                        writer.WriteLine(string.Join("\t", new[] {
                            "item", Clean(id), Clean(name), detail.Index, Clean(detail.TemplateId),
                            Clean(detail.TemplateName), "item", Clean(detail.Tier), detail.TierSource,
                            Clean(detail.Enchantment), detail.EnchantmentStatus, detail.Socket, detail.Status,
                            Clean(detail.Reason)
                        }));
                        itemRows++;
                        if (detail.Status == "missing") missingTemplates++;
                        if (detail.TierSource == "undeclared-sparse") sparseTiers++;
                        if (detail.EnchantmentStatus == "missing") missingEnchantments++;
                    }
                    foreach (LiveSkillEntry skill in read.Skills)
                    {
                        SkillRow detail = DescribeSkill(skill);
                        writer.WriteLine(string.Join("\t", new[] {
                            "skill", Clean(id), Clean(name), detail.Index, Clean(detail.TemplateId),
                            Clean(detail.TemplateName), "skill", Clean(detail.Tier), detail.TierSource,
                            "", "", "", detail.Status, Clean(detail.Reason)
                        }));
                        skillRows++;
                        if (detail.Status == "missing") missingTemplates++;
                        if (detail.TierSource == "undeclared-sparse") sparseTiers++;
                    }
                }
            }
            Console.WriteLine("怪物读取矩阵完成；只读取模板，不执行 Ability/Aura 效果。");
            Console.WriteLine("怪物模板=" + monsterCount + "；读取告警=" + warningMonsters + "；异常=" + exceptions);
            Console.WriteLine("手牌行=" + itemRows + "；技能行=" + skillRows + "；缺失模板=" + missingTemplates +
                "；未声明品质=" + sparseTiers + "；缺失附魔=" + missingEnchantments);
            Console.WriteLine("明细=" + output);
            return 0;
        }

        private sealed class ItemRow
        {
            public string Index;
            public string TemplateId;
            public string TemplateName;
            public string Tier;
            public string TierSource;
            public string Enchantment;
            public string EnchantmentStatus;
            public string Socket;
            public string Status;
            public string Reason = "";
        }

        private sealed class SkillRow
        {
            public string Index;
            public string TemplateId;
            public string TemplateName;
            public string Tier;
            public string TierSource;
            public string Status;
            public string Reason = "";
        }

        private static ItemRow DescribeItem(LiveCardEntry card)
        {
            ItemRow result = new ItemRow();
            result.Index = card.Order.ToString();
            result.TemplateId = card.TemplateId ?? "";
            result.Tier = string.IsNullOrEmpty(card.Tier) ? "Bronze" : card.Tier;
            result.Socket = card.Socket.ToString();
            result.Enchantment = string.IsNullOrEmpty(card.Enchantment) ? "None" : card.Enchantment;
            if (card.Template == null || card.Template.Kind != JsonKind.Object)
            {
                result.TemplateName = "";
                result.TierSource = "missing";
                result.EnchantmentStatus = result.Enchantment == "None" ? "none" : "unknown";
                result.Status = "missing";
                result.Reason = Path("Player.Hand.Items", card.Order) + ".TemplateId=" + result.TemplateId;
                return result;
            }
            result.TemplateName = NativeSkillSupport.Text(card.Template, "InternalName") ?? "";
            result.TierSource = TierSource(card.Template, result.Tier);
            result.EnchantmentStatus = EnchantmentStatus(card.Template, result.Enchantment);
            result.Status = result.TierSource == "undeclared-sparse" || result.EnchantmentStatus == "missing"
                ? "warning" : "resolved";
            List<string> reasons = new List<string>();
            if (result.TierSource == "undeclared-sparse")
                reasons.Add(Path("Player.Hand.Items", card.Order) + " Tier=" + result.Tier +
                    " 未声明（declared=" + DeclaredTiers(card.Template) + "）");
            if (result.EnchantmentStatus == "missing")
                reasons.Add(Path("Player.Hand.Items", card.Order) + " Enchantment=" + result.Enchantment + " 未定义");
            result.Reason = string.Join(" || ", reasons.ToArray());
            return result;
        }

        private static SkillRow DescribeSkill(LiveSkillEntry skill)
        {
            SkillRow result = new SkillRow();
            result.Index = skill.Order.ToString();
            result.TemplateId = skill.TemplateId ?? "";
            result.Tier = string.IsNullOrEmpty(skill.Tier) ? "Bronze" : skill.Tier;
            if (skill.Template == null || skill.Template.Kind != JsonKind.Object)
            {
                result.TemplateName = "";
                result.TierSource = "missing";
                result.Status = "missing";
                result.Reason = Path("Player.Skills", skill.Order) + ".TemplateId=" + result.TemplateId;
                return result;
            }
            result.TemplateName = NativeSkillSupport.Text(skill.Template, "InternalName") ?? "";
            result.TierSource = TierSource(skill.Template, result.Tier);
            result.Status = result.TierSource == "undeclared-sparse" ? "warning" : "resolved";
            if (result.TierSource == "undeclared-sparse")
                result.Reason = Path("Player.Skills", skill.Order) + " Tier=" + result.Tier +
                    " 未声明（declared=" + DeclaredTiers(skill.Template) + "）";
            return result;
        }

        private static string TierSource(JsonValue template, string tier)
        {
            JsonValue tiers = template["Tiers"];
            string lookup = tier == "Legendary" ? "Diamond" : tier;
            if (!tiers.Has(lookup)) return "undeclared-sparse";
            if (tier == "Legendary") return "legendary-alias";
            return "declared";
        }

        private static string EnchantmentStatus(JsonValue template, string enchantment)
        {
            if (string.IsNullOrEmpty(enchantment) || enchantment == "None") return "none";
            return template["Enchantments"].Has(enchantment) ? "declared" : "missing";
        }

        private static string DeclaredTiers(JsonValue template)
        {
            List<string> names = new List<string>();
            foreach (string name in template["Tiers"].AsObject().Keys) names.Add(name);
            names.Sort(StringComparer.Ordinal);
            return string.Join(";", names.ToArray());
        }

        private static void WriteMonster(StreamWriter writer, string id, string name, string status, string reason)
        {
            writer.WriteLine(string.Join("\t", new[] {
                "monster", Clean(id), Clean(name), "", "", "", "monster", "", "", "", "", "", status, Clean(reason)
            }));
        }

        private static string Path(string prefix, int index)
        {
            return prefix + "[" + index + "]";
        }

        private static string Clean(string value)
        {
            return (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        private static Dictionary<string, JsonValue> LoadCards(string path)
        {
            Dictionary<string, JsonValue> cards = new Dictionary<string, JsonValue>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadLines(path))
            {
                if (line.Trim().Length == 0) continue;
                JsonValue row = Json.Parse(line);
                string id = row["id"].AsString();
                if (id == null) throw new FormatException("卡牌模板缺少 ID");
                if (cards.ContainsKey(id)) throw new FormatException("卡牌模板 ID 重复：" + id);
                cards[id] = row["template"];
            }
            return cards;
        }
    }
}
