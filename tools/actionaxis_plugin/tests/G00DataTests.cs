using System;
using System.Collections.Generic;
using System.IO;
using BazaarActionAxis.Engine;
using BazaarActionAxis.Plugin;

namespace BazaarActionAxis.Tests
{
    // G00 数据/品质/读取基线与矩阵盲区回归。完整模板来自
    // fixtures/native_g00_templates.json，由 rule_inventory.py 从 GameData.db 导出。
    public static class G00DataTests
    {
        private const string FixturePath = "tools/actionaxis_plugin/tests/fixtures/native_g00_templates.json";
        private const string Counterstrike = "c5bff6ad-5d13-4cf3-9941-98f3cf913949";
        private const string WildBoarItem = "e01ac7ab-1ad5-4235-81b4-44d057878953";
        private const string WildBoarMonster = "5d779101-3fa5-44b7-b885-f8bb9b0f7438";

        public static void Run()
        {
            JsonValue fixtures = Json.Parse(File.ReadAllText(FixturePath));
            SameNameWildBoarItemAndMonsterStayDistinct(fixtures);
            SparseSkillTierIsInactiveAndLegendaryUsesDiamond(fixtures);
            ReaderErrorsCarryOriginalPaths();
            MonsterWarningsCarryOriginalPaths();
            CardDetailErrorsCarryOriginalPaths(fixtures);
            MonsterCoverageMatrixKeepsPathsAndTierSources();
            MonsterSocketEffectsApplyHeatedAndChilled();
        }

        private static JsonValue Template(JsonValue fixtures, string key)
        {
            return fixtures[key]["template"];
        }

        private static void SameNameWildBoarItemAndMonsterStayDistinct(JsonValue fixtures)
        {
            JsonValue monster = Template(fixtures, "monsters:Wild Boar");
            JsonValue item = Template(fixtures, "cards:Wild Boar");
            Assert.Equal("5d779101-3fa5-44b7-b885-f8bb9b0f7438", TemplateId(fixtures, "monsters:Wild Boar"));
            Assert.Equal(WildBoarItem, TemplateId(fixtures, "cards:Wild Boar"));
            Assert.Equal("Wild Boar", NativeSkillSupport.Text(monster, "InternalName"));
            Assert.Equal("Wild Boar", NativeSkillSupport.Text(item, "InternalName"));
            Assert.Equal("TCardItem", NativeSkillSupport.Type(item));

            Dictionary<string, JsonValue> cards = new Dictionary<string, JsonValue>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in new[] { "cards:Wild Boar", "cards:Wildroot Amulet", "cards:Blueberry Pie", "cards:Basket",
                "cards:Truffles", "cards:Feeding Frenzy" })
                cards[TemplateId(fixtures, key)] = Template(fixtures, key);
            LiveMonsterEntry read = MonsterTemplateExtractor.FromJson(monster,
                delegate(string id) { JsonValue found; return cards.TryGetValue(id, out found) ? found : null; });
            Assert.Equal(WildBoarMonster, read.Id);
            Assert.Equal("Wild Boar", read.Name);
            Assert.Near(650, read.MaxHp);
            Assert.Equal(50, read.Attributes["RageMax"]);
            Assert.Equal(5, read.Cards.Count);
            Assert.Equal(1, read.Skills.Count);
            Assert.Equal(0, read.Warnings.Count);
            LiveCardEntry nativeItemCard = null;
            foreach (LiveCardEntry card in read.Cards)
                if (card.TemplateId == WildBoarItem) nativeItemCard = card;
            Assert.True(nativeItemCard != null);
            Assert.Equal("Silver", nativeItemCard.Tier);
            Assert.Equal(4, nativeItemCard.Socket);
            Assert.Equal("Wild Boar", NativeSkillSupport.Text(nativeItemCard.Template, "InternalName"));

            LiveCardEntry entry = new LiveCardEntry { TemplateId = WildBoarItem, Tier = "Silver", Template = item,
                Size = NativeSkillSupport.Text(item, "Size"), AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(item, "Silver") };
            MappedBoard mapped = CardMapper.Build(new List<LiveCardEntry> { entry },
                CardCatalog.FromCards(new List<CardDef>()), 10000, 10000, 5);
            CardDef mappedItem = mapped.Config.Cards[WildBoarItem + "#0"];
            Assert.Equal(WildBoarItem, mappedItem.BaseItemId);
            Assert.True(mappedItem.BaseItemId != read.Id);
        }

        private static void SparseSkillTierIsInactiveAndLegendaryUsesDiamond(JsonValue fixtures)
        {
            JsonValue counterstrike = Template(fixtures, "cards:Counterstrike");
            JsonValue shortened = Template(fixtures, "cards:Shortened Rage");
            JsonValue rebirth = Template(fixtures, "cards:Fiery Rebirth");
            Assert.False(counterstrike["Tiers"].Has("Bronze"));
            Assert.True(counterstrike["Tiers"].Has("Gold"));
            Assert.True(counterstrike["Tiers"].Has("Diamond"));
            Assert.False(shortened["Tiers"].Has("Gold"));
            Assert.True(rebirth["Tiers"].Has("Diamond"));
            Assert.True(rebirth["Tiers"].Has("Legendary"));

            BattleConfig config = new BattleConfig { NativeMechanics = true, PlayerMaxHp = 10000, EnemyMaxHp = 10000 };
            SkillMappingResult bronze = SkillMapper.Apply(config,
                new[] { new LiveSkillEntry { TemplateId = Counterstrike, Tier = "Bronze", Template = counterstrike } }, null);
            Assert.Equal("inactive", bronze.Skills[0].Status);
            Assert.True(bronze.Skills[0].NativeTierHasNoEffects);
            SkillMappingResult diamond = SkillMapper.Apply(config,
                new[] { new LiveSkillEntry { TemplateId = Counterstrike, Tier = "Diamond", Template = counterstrike } }, null);
            SkillMappingResult legendary = SkillMapper.Apply(config,
                new[] { new LiveSkillEntry { TemplateId = Counterstrike, Tier = "Legendary", Template = counterstrike } }, null);
            Assert.Equal("simulated", diamond.Skills[0].Status);
            Assert.Equal(diamond.Skills[0].Status, legendary.Skills[0].Status);
            Assert.Equal("Legendary", legendary.Skills[0].Tier);
            Assert.Equal("Diamond", config.PlayerSkills[0].Tier);
            Assert.Equal(2000, config.PlayerSkills[0].LiveAttributes["ChargeAmount"]);

            // 显式 Legendary 行不参与原生查询：Fiery Rebirth 的 Legendary 行
            // 没有 BuyPrice，映射必须回落到 Diamond 的 50。
            Assert.False(rebirth["Tiers"]["Legendary"]["Attributes"].Has("BuyPrice"));
            Assert.True(rebirth["Tiers"]["Diamond"]["Attributes"].Has("BuyPrice"));
            BattleConfig rebirthConfig = new BattleConfig { NativeMechanics = true, PlayerMaxHp = 10000, EnemyMaxHp = 10000 };
            SkillMappingResult rebirthLegendary = SkillMapper.Apply(rebirthConfig,
                new[] { new LiveSkillEntry { TemplateId = "09c59ebf-d2f9-4862-9c30-396b9ddfdd89", Tier = "Legendary", Template = rebirth } }, null);
            Assert.Equal("simulated", rebirthLegendary.Skills[0].Status);
            Assert.Equal("Diamond", rebirthConfig.PlayerSkills[0].Tier);
            Assert.Equal(50, rebirthConfig.PlayerSkills[0].LiveAttributes["BuyPrice"]);

            Dictionary<string, int> diamondAttributes = MonsterTemplateExtractor.TierAttributes(rebirth, "Diamond");
            Dictionary<string, int> legendaryAttributes = MonsterTemplateExtractor.TierAttributes(rebirth, "Legendary");
            Assert.Equal(diamondAttributes.Count, legendaryAttributes.Count);
            foreach (KeyValuePair<string, int> pair in diamondAttributes)
                Assert.Equal(pair.Value, legendaryAttributes[pair.Key]);
        }

        private static void ReaderErrorsCarryOriginalPaths()
        {
            try
            {
                NativeSkillRun run = new NativeSkillRun();
                run.Player = new NativeSkillPlayer();
                TheBazaar.Data.Run = run;
                NativeSkillCard cyclic = NewSkill();
                BazaarGameShared.Fakes.TCardSkillFake template =
                    (BazaarGameShared.Fakes.TCardSkillFake)cyclic.Template;
                template.Extra = template;
                run.Player.Skills.Add(cyclic);
                run.Player.Skills.Add(new NativeSkillBrokenAttributes());
                run.Player.Skills.Add(NewSkill());
                BoardReadResult result = new BoardReadResult();
                LiveSkillReader.Populate(result);
                Assert.True(result.SkillReadError.Contains("Run.Player.Skills[0].Template"));
                Assert.True(result.SkillReadError.Contains(".Extra"));
                Assert.True(result.SkillReadError.Contains("Run.Player.Skills[1]"));
            }
            finally { TheBazaar.Data.Run = null; }
        }

        private static NativeSkillCard NewSkill()
        {
            NativeSkillCard skill = new NativeSkillCard();
            skill.Template = new BazaarGameShared.Fakes.TCardSkillFake { InternalName = "Path Check" };
            return skill;
        }

        private static void MonsterWarningsCarryOriginalPaths()
        {
            JsonValue monster = Json.Parse("{\"Id\":\"broken-monster\",\"InternalName\":\"Broken\"," +
                "\"Player\":{\"Attributes\":{\"HealthMax\":100},\"Hand\":{\"Items\":[{\"$type\":\"TCardInstanceItem\"," +
                "\"TemplateId\":\"missing-item\",\"SocketId\":\"Socket_2\"}]},\"Skills\":[{\"TemplateId\":\"missing-skill\"}]," +
                "\"Effects\":[{\"$type\":\"TUnknownEffect\"}],\"Socket\":{\"Effects\":[{\"$type\":\"TUnknownSocket\"}]}}}");
            LiveMonsterEntry read = MonsterTemplateExtractor.FromJson(monster, delegate(string id) { return null; });
            Assert.Equal(4, read.Warnings.Count);
            Assert.True(read.Warnings[0].Contains("Player.Hand.Items[0]"));
            Assert.True(read.Warnings[1].Contains("Player.Skills[0]"));
            Assert.True(read.Warnings[2].Contains("Player.Effects[0]"));
            Assert.True(read.Warnings[3].Contains("Player.Socket.Effects"));
            try
            {
                MonsterTemplateExtractor.FromJson(Json.Parse("{\"Id\":\"no-health\",\"Player\":{\"Attributes\":{}}}"),
                    delegate(string id) { return null; });
                Assert.True(false);
            }
            catch (InvalidOperationException error)
            {
                Assert.True(error.Message.Contains("Player.Attributes.HealthMax"));
            }
        }

        private static void CardDetailErrorsCarryOriginalPaths(JsonValue fixtures)
        {
            try
            {
                NativeItemTemplateForEnchantTest template = new NativeItemTemplateForEnchantTest();
                template.Enchantments["Toxic"] = Template(fixtures, "cards:Scythe")["Enchantments"]["Toxic"];
                NativeSkillCard first = new NativeSkillCard { Template = template };
                NativeSkillCard noTemplate = new NativeSkillCard { InstanceId = new NativeSkillInstance { Value = "item-2" } };
                TheBazaar.Data.Entities = new Dictionary<string, object>
                {
                    { "first", first }, { "second", noTemplate }
                };
                BoardReadResult result = new BoardReadResult();
                result.Cards.Add(new LiveCardEntry { InstanceId = "skill-instance-1", TemplateId = "item-1", Enchantment = "Missing" });
                result.Cards.Add(new LiveCardEntry { InstanceId = "missing", TemplateId = "item-2", Enchantment = "None" });
                result.Cards.Add(new LiveCardEntry { InstanceId = "item-2", TemplateId = "item-3", Enchantment = "None" });
                LiveCardDetailReader.Populate(result);
                Assert.True(result.CardReadError.Contains("Cards[0].Template.Enchantments.Missing"));
                Assert.True(result.CardReadError.Contains("Cards[1].InstanceId=missing"));
                Assert.True(result.CardReadError.Contains("Cards[2].Template"));
            }
            finally { TheBazaar.Data.Entities = null; }
        }

        private static void MonsterSocketEffectsApplyHeatedAndChilled()
        {
            JsonValue socketFixtures = Json.Parse(File.ReadAllText(
                "tools/actionaxis_plugin/tests/fixtures/native_g00_socket_effects.json"));
            JsonValue cooler = socketFixtures["cards:[Cooler] Socket Effect"]["template"];
            JsonValue stove = socketFixtures["cards:[Stove] Socket Effect"]["template"];
            JsonValue item = Json.Parse("{\"$type\":\"TCardItem\",\"InternalName\":\"Item\",\"Size\":\"Small\"," +
                "\"Tiers\":{\"Gold\":{\"Attributes\":{\"CooldownMax\":1000},\"AbilityIds\":[],\"AuraIds\":[]}}," +
                "\"Abilities\":{},\"Auras\":{}}");
            JsonValue monster = Json.Parse("{\"InternalName\":\"Socket Monster\",\"Player\":{" +
                "\"Attributes\":{\"HealthMax\":100},\"Hand\":{\"Items\":[" +
                "{\"$type\":\"TCardInstanceItem\",\"TemplateId\":\"item\",\"SocketId\":\"Socket_0\",\"Tier\":\"Gold\",\"Attributes\":{}}," +
                "{\"$type\":\"TCardInstanceItem\",\"TemplateId\":\"item\",\"SocketId\":\"Socket_1\",\"Tier\":\"Gold\",\"Attributes\":{}}]}," +
                "\"Socket\":{\"Effects\":[" +
                "{\"$type\":\"TCardInstanceSocketEffect\",\"SocketId\":\"Socket_0\",\"TemplateId\":\"cooler\",\"Tier\":\"Diamond\",\"Attributes\":{}}," +
                "{\"$type\":\"TCardInstanceSocketEffect\",\"SocketId\":\"Socket_1\",\"TemplateId\":\"stove\",\"Tier\":\"Diamond\",\"Attributes\":{}}]}}}");
            Dictionary<string, JsonValue> cards = new Dictionary<string, JsonValue>(StringComparer.OrdinalIgnoreCase);
            cards["item"] = item;
            cards["cooler"] = cooler;
            cards["stove"] = stove;
            LiveMonsterEntry read = MonsterTemplateExtractor.FromJson(monster,
                delegate(string id) { JsonValue found; return cards.TryGetValue(id, out found) ? found : null; });
            Assert.Equal(0, read.Warnings.Count);
            Assert.Equal(1, read.Cards[0].Attributes["Chilled"]);
            Assert.Equal(1, read.Cards[1].Attributes["Heated"]);

            JsonValue broken = Json.Parse("{\"InternalName\":\"Broken Socket\",\"Player\":{\"Attributes\":{\"HealthMax\":100}," +
                "\"Hand\":{\"Items\":[{\"$type\":\"TCardInstanceItem\",\"TemplateId\":\"item\",\"SocketId\":\"Socket_0\",\"Tier\":\"Gold\"}]}," +
                "\"Socket\":{\"Effects\":[{\"$type\":\"TCardInstanceSocketEffect\",\"SocketId\":\"Socket_0\"," +
                "\"TemplateId\":\"missing\",\"Tier\":\"Diamond\"}]}}}");
            LiveMonsterEntry missing = MonsterTemplateExtractor.FromJson(broken,
                delegate(string id) { return id == "item" ? item : null; });
            Assert.True(missing.Warnings.Count > 0);
            Assert.True(missing.Warnings[0].Contains("Player.Socket.Effects[0].TemplateId=missing"));
        }

        private static void MonsterCoverageMatrixKeepsPathsAndTierSources()
        {
            JsonValue monster = Json.Parse("{\"InternalName\":\"Monster Probe\",\"Player\":{" +
                "\"Attributes\":{\"HealthMax\":100},\"Hand\":{\"Items\":[" +
                "{\"TemplateId\":\"item-ok\",\"Tier\":\"Gold\",\"SocketId\":\"Socket_0\"}," +
                "{\"TemplateId\":\"item-low\",\"Tier\":\"Bronze\",\"EnchantmentType\":\"Toxic\",\"SocketId\":\"Socket_1\"}," +
                "{\"TemplateId\":\"missing-item\",\"Tier\":\"Gold\",\"SocketId\":\"Socket_2\"}]}," +
                "\"Skills\":[{\"TemplateId\":\"skill-ok\",\"Tier\":\"Diamond\"}," +
                "{\"TemplateId\":\"missing-skill\",\"Tier\":\"Gold\"}]}}");
            Dictionary<string, JsonValue> cards = new Dictionary<string, JsonValue>(StringComparer.OrdinalIgnoreCase);
            cards["item-ok"] = Json.Parse("{\"$type\":\"TCardItem\",\"InternalName\":\"Item OK\"," +
                "\"Tiers\":{\"Gold\":{}},\"Enchantments\":{}}");
            cards["item-low"] = Json.Parse("{\"$type\":\"TCardItem\",\"InternalName\":\"Item Low\"," +
                "\"Tiers\":{\"Silver\":{},\"Gold\":{}},\"Enchantments\":{}}");
            cards["skill-ok"] = Json.Parse("{\"$type\":\"TCardSkill\",\"InternalName\":\"Skill OK\"," +
                "\"Tiers\":{\"Diamond\":{}}}");
            List<JsonValue> rows = new List<JsonValue>();
            Dictionary<string, JsonValue> input = new Dictionary<string, JsonValue>();
            input["table"] = JsonValue.FromString("monsters");
            input["id"] = JsonValue.FromString("monster-probe");
            input["template"] = monster;
            rows.Add(JsonValue.FromObject(input));
            string output = Path.Combine(Path.GetTempPath(), "g00_monsters_" + Guid.NewGuid().ToString("N") + ".tsv");
            TextWriter original = Console.Out;
            StringWriter captured = new StringWriter();
            Console.SetOut(captured);
            try { Assert.Equal(0, MonsterCoverageRunner.Run(rows, cards, output)); }
            finally { Console.SetOut(original); }
            string summary = captured.ToString();
            try
            {
                string[] lines = File.ReadAllLines(output);
                Assert.Equal(7, lines.Length);
                Assert.Equal(MonsterCoverageRunner.Header, lines[0]);
                Dictionary<string, Dictionary<string, string>> parsed = new Dictionary<string, Dictionary<string, string>>();
                string[] header = lines[0].Split('\t');
                for (int index = 1; index < lines.Length; index++)
                {
                    string[] cells = lines[index].Split('\t');
                    Assert.Equal(header.Length, cells.Length);
                    Dictionary<string, string> row = new Dictionary<string, string>();
                    for (int column = 0; column < header.Length; column++) row[header[column]] = cells[column];
                    parsed[row["record"] + "/" + row["index"]] = row;
                }
                Dictionary<string, string> monsterRow = parsed["monster/"];
                Assert.Equal("warning", monsterRow["status"]);
                Assert.True(monsterRow["reason"].Contains("Player.Hand.Items[2].TemplateId=missing-item"));
                Assert.True(monsterRow["reason"].Contains("Player.Skills[1].TemplateId=missing-skill"));

                Dictionary<string, string> ok = parsed["item/0"];
                Assert.Equal("item-ok", ok["template_id"]);
                Assert.Equal("Item OK", ok["template_name"]);
                Assert.Equal("declared", ok["tier_source"]);
                Assert.Equal("None", ok["enchantment"]);
                Assert.Equal("none", ok["enchantment_status"]);
                Assert.Equal("resolved", ok["status"]);

                Dictionary<string, string> low = parsed["item/1"];
                Assert.Equal("undeclared-sparse", low["tier_source"]);
                Assert.Equal("missing", low["enchantment_status"]);
                Assert.Equal("warning", low["status"]);
                Assert.True(low["reason"].Contains("Player.Hand.Items[1]"));

                Dictionary<string, string> missingItem = parsed["item/2"];
                Assert.Equal("missing", missingItem["status"]);
                Assert.True(missingItem["reason"].Contains("Player.Hand.Items[2].TemplateId=missing-item"));

                Assert.Equal("resolved", parsed["skill/0"]["status"]);
                Assert.Equal("Diamond", parsed["skill/0"]["tier"]);
                Assert.Equal("missing", parsed["skill/1"]["status"]);
                Assert.True(parsed["skill/1"]["reason"].Contains("Player.Skills[1].TemplateId=missing-skill"));
                Assert.True(summary.Contains("怪物模板=1"));
                Assert.True(summary.Contains("缺失模板=2"));
                Assert.True(summary.Contains("未声明品质=1"));
                Assert.True(summary.Contains("缺失附魔=1"));
            }
            finally { File.Delete(output); }
        }

        private static string TemplateId(JsonValue fixtures, string key)
        {
            return fixtures[key]["id"].AsString();
        }
    }
}
