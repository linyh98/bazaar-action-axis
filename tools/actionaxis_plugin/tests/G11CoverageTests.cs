using System;
using System.Collections.Generic;
using System.IO;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // G11 full-set gate: all 36 primary templates plus the related hybrid
    // blockers must have an explicit expected entry status, not a sample.
    public static class G11CoverageTests
    {
        private static JsonValue _fixtures;
        private static readonly CardCatalog EmptyCatalog = CardCatalog.FromCards(new List<CardDef>());

        public static void Run()
        {
            _fixtures = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_g11_all.json"));
            AllG11ItemsHaveExpectedEntryStatus();
            AllG11SkillsHaveExpectedEntryStatus();
            RelatedG11BlockersHaveExpectedStatus();
        }

        private static string TierFor(JsonValue template)
        {
            string[] tiers = { "Diamond", "Gold", "Silver", "Bronze", "Legendary" };
            foreach (string tier in tiers)
                if (template["Tiers"].Has(tier)) return tier;
            return "Bronze";
        }

        private static string Kind(JsonValue template) { return NativeSkillSupport.Type(template); }

        private static void AllG11ItemsHaveExpectedEntryStatus()
        {
            int items = 0;
            foreach (KeyValuePair<string, JsonValue> pair in _fixtures.AsObject())
            {
                JsonValue row = pair.Value;
                if (row["primary_group"].AsString() != "G11") continue;
                JsonValue template = row["template"];
                if (Kind(template) != "TCardItem") continue;
                items++;
                string tier = TierFor(template);
                LiveCardEntry entry = new LiveCardEntry
                {
                    TemplateId = row["id"].AsString(),
                    Template = template,
                    Tier = tier,
                    Socket = 0,
                    Size = NativeSkillSupport.Text(template, "Size"),
                    AttributesAreLive = false,
                    Attributes = MonsterTemplateExtractor.TierAttributes(template, tier)
                };
                BattleConfig config = CardMapper.Build(new List<LiveCardEntry> { entry }, EmptyCatalog, 1000, 1000, 1).Config;
                config.NativeMechanics = true;
                List<string> warnings = NativeCardAbilityMapper.Apply(config);
                CardDef card = config.Cards[config.Player.Items[0].CardId];
                Assert.Equal("structured", card.ParseStatus);
                Assert.Equal("", string.Join(" | ", warnings.ToArray()));
                string id = row["id"].AsString();
                bool missingAbility = id == "ef20ec33-7990-41b8-8d8e-47514b503612" ||
                    id == "96b9cc27-b148-4d71-ab5d-21a9c3b64998" ||
                    id == "3130ec0b-ee54-4bd1-8bf9-706c41066f11";
                if (missingAbility)
                {
                    Assert.True(card.MappingWarnings.Count > 0);
                    Assert.True(string.Join(" | ", card.MappingWarnings.ToArray()).Contains("能力定义缺失"));
                }
                else
                {
                    Assert.Equal(0, card.MappingWarnings.Count);
                }
            }
            Assert.Equal(27, items);
        }

        private static void AllG11SkillsHaveExpectedEntryStatus()
        {
            int skills = 0;
            foreach (KeyValuePair<string, JsonValue> pair in _fixtures.AsObject())
            {
                JsonValue row = pair.Value;
                if (row["primary_group"].AsString() != "G11") continue;
                JsonValue template = row["template"];
                if (Kind(template) != "TCardSkill") continue;
                skills++;
                string tier = TierFor(template);
                BattleConfig config = new BattleConfig { NativeMechanics = true, PlayerMaxHp = 1000, EnemyMaxHp = 1000 };
                LiveSkillEntry entry = new LiveSkillEntry
                {
                    TemplateId = row["id"].AsString(),
                    Tier = tier,
                    Template = template,
                    AttributesAreLive = false,
                    Attributes = MonsterTemplateExtractor.TierAttributes(template, tier)
                };
                SkillMappingResult mapped = SkillMapper.Apply(config, new[] { entry }, null, Side.Player);
                Assert.Equal(1, mapped.Skills.Count);
                string status = mapped.Skills[0].Status;
                Assert.True(status != "unsupported");
                if (row["id"].AsString() == "45e83b0f-26a1-468e-a9ca-0075e58af7a0")
                    Assert.True(string.Join(" | ", mapped.Skills[0].MappingWarnings.ToArray()).Contains("光环"));
            }
            Assert.Equal(9, skills);
        }

        private static void RelatedG11BlockersHaveExpectedStatus()
        {
            AssertItemMapped("Hunker Down", true);
            AssertItemMapped("Leather Jacket", false);
            AssertItemMapped("Peacewrought", false);
            // [DEBUG] Peacewrought intentionally skips the debug in-combat
            // TActionGameSpawnCards action; the rest of the card still maps.
            AssertItemMapped("[DEBUG] Peacewrought", false);
        }

        private static void AssertItemMapped(string name, bool skill)
        {
            JsonValue row = _fixtures[name];
            JsonValue template = row["template"];
            string tier = TierFor(template);
            if (skill)
            {
                BattleConfig config = new BattleConfig { NativeMechanics = true, PlayerMaxHp = 1000, EnemyMaxHp = 1000 };
                LiveSkillEntry entry = new LiveSkillEntry { TemplateId = row["id"].AsString(), Tier = tier, Template = template,
                    AttributesAreLive = false, Attributes = MonsterTemplateExtractor.TierAttributes(template, tier) };
                SkillMappingResult mapped = SkillMapper.Apply(config, new[] { entry }, null, Side.Player);
                Assert.True(mapped.Skills[0].Status != "unsupported");
                return;
            }
            LiveCardEntry item = Entry(row, tier);
            BattleConfig itemConfig = CardMapper.Build(new List<LiveCardEntry> { item }, EmptyCatalog, 1000, 1000, 1).Config;
            itemConfig.NativeMechanics = true;
            List<string> warnings = NativeCardAbilityMapper.Apply(itemConfig);
            Assert.Equal("", string.Join(" | ", warnings.ToArray()));
            Assert.Equal("structured", itemConfig.Cards[itemConfig.Player.Items[0].CardId].ParseStatus);
        }

        private static LiveCardEntry Entry(JsonValue row, string tier)
        {
            JsonValue template = row["template"];
            return new LiveCardEntry
            {
                TemplateId = row["id"].AsString(),
                Template = template,
                Tier = tier,
                Socket = 0,
                Size = NativeSkillSupport.Text(template, "Size"),
                AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, tier)
            };
        }
    }
}
