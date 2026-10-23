using System;
using System.Collections.Generic;
using System.IO;
using BazaarActionAxis.Engine;
using BazaarActionAxis.Plugin;

namespace BazaarActionAxis.Tests
{
    public sealed class NativeItemTemplateForEnchantTest
    {
        public Dictionary<string, JsonValue> Enchantments = new Dictionary<string, JsonValue>();
    }

    public static class EnchantmentMapperTests
    {
        private const string Gun = "666f4084-48cc-4558-b678-a46ba18c1bf2";
        private const string Cog = "ca77281c-f912-4db3-86ce-64842a4ccdaa";

        public static void Run()
        {
            ToxicSolderingGunUsesLiveValuesAndMulticast();
            IcyCogFreezesAndPreservesBaseAbility();
            UnknownRulesDoNotInventEffects();
            ReaderSelectsTheCurrentEnchantmentByInstance();
        }

        private static JsonValue Definition(string id, string enchantment)
        {
            // Exact native definitions from the installed GameData.db v5.0.0.
            string path = "tools/actionaxis_plugin/tests/fixtures/" + id + "-" + enchantment + ".json";
            return Json.Parse(File.ReadAllText(path))["definition"];
        }

        private static CardDef BaseCard(string id, string kind)
        {
            CardDef card = new CardDef();
            card.Id = id + "@Bronze";
            card.BaseItemId = id;
            card.Cooldown = 6;
            card.Effects.Add(new Effect { Kind = kind, Amount = new NumExpr { Value = 1 }, Target = new Target { Kind = "enemyHero" } });
            return card;
        }

        private static void ToxicSolderingGunUsesLiveValuesAndMulticast()
        {
            CardDef card = BaseCard(Gun, "burn");
            LiveCardEntry entry = new LiveCardEntry { InstanceId = "gun-1", TemplateId = Gun, Tier = "Bronze", Enchantment = "Toxic", EnchantmentDefinition = Definition(Gun, "Toxic") };
            entry.Attributes["CooldownMax"] = 6000;
            entry.Attributes["BurnApplyAmount"] = 3;
            entry.Attributes["PoisonApplyAmount"] = 3;
            entry.Attributes["Multicast"] = 3;
            CardCatalog catalog = CardCatalog.FromCards(new List<CardDef> { card });
            MappedBoard mapped = CardMapper.Build(new List<LiveCardEntry> { entry }, catalog, 1000, 100000, 7);
            Assert.Equal(0, mapped.EnchantmentWarnings.Count);
            CardDef live = mapped.Config.Cards[Gun + "@Bronze#0"];
            Assert.Equal(2, live.Effects.Count);
            Assert.Near(3, live.Effects[0].Amount.Value);
            Assert.Near(3, live.Effects[1].Amount.Value);
            BattleResult result = BattleEngine.Simulate(mapped.Config);
            CardStatsSeries stats = Aggregator.Build(result, new string[] { result.PlayerItems[0].Id }, .1)[result.PlayerItems[0].Id];
            Assert.Equal(3, stats.At(6).ActivateCount);
            Assert.Near(9, stats.At(6).BurnStacks);
            Assert.Near(9, stats.At(6).PoisonStacks);
            Assert.True(result.Totals.Player.Damage > 0);
            // A later recording must neither accumulate abilities nor reuse the
            // old poison number. The template's aura is not applied a second time.
            entry.Attributes["PoisonApplyAmount"] = 5;
            MappedBoard next = CardMapper.Build(new List<LiveCardEntry> { entry }, catalog, 1000, 100000, 6);
            Assert.Near(5, next.Config.Cards[Gun + "@Bronze#0"].Effects[1].Amount.Value);
            Assert.Equal(1, card.Effects.Count);
            Assert.Near(1, card.Effects[0].Amount.Value);
        }

        private static void IcyCogFreezesAndPreservesBaseAbility()
        {
            CardDef cog = new CardDef { Id = Cog + "@Bronze", BaseItemId = Cog, Cooldown = 1 };
            cog.Effects.Add(new Effect { Kind = "haste", Seconds = 1, Count = 1, Target = new Target { Kind = "selfItem" } });
            LiveCardEntry entry = new LiveCardEntry { TemplateId = Cog, Tier = "Bronze", Enchantment = "Icy", EnchantmentDefinition = Definition(Cog, "Icy") };
            entry.Attributes["HasteAmount"] = 2000;
            entry.Attributes["FreezeAmount"] = 500;
            entry.Attributes["FreezeTargets"] = 1;
            MappedBoard mapped = CardMapper.Build(new List<LiveCardEntry> { entry }, CardCatalog.FromCards(new List<CardDef> { cog }), 1000, 100000, 1);
            Assert.Equal(0, mapped.EnchantmentWarnings.Count);
            CardDef live = mapped.Config.Cards[Cog + "@Bronze#0"];
            Assert.Equal(2, live.Effects.Count);
            Assert.Near(2, live.Effects[0].Seconds);
            Assert.Equal("freeze", live.Effects[1].Kind);
            Assert.Near(.5, live.Effects[1].Seconds);
            Assert.Equal(1, live.Effects[1].Count.Value);
            CardDef opponent = new CardDef { Id = "opponent", Cooldown = 20 };
            mapped.Config.Cards[opponent.Id] = opponent;
            mapped.Config.Enemy.Items.Add(new LoadoutItem { CardId = opponent.Id, Slot = 0 });
            BattleResult result = BattleEngine.Simulate(mapped.Config);
            CardStats stats = Aggregator.Build(result, new string[] { result.PlayerItems[0].Id }, .1)[result.PlayerItems[0].Id].At(1);
            Assert.Equal(1, stats.FreezeCount);
            Assert.Near(.5, stats.FreezeSeconds);
            Assert.Equal(1, cog.Effects.Count);
        }

        private static void UnknownRulesDoNotInventEffects()
        {
            CardDef card = BaseCard(Gun, "burn");
            LiveCardEntry entry = new LiveCardEntry { Enchantment = "Toxic", EnchantmentDefinition = Definition(Gun, "Toxic") };
            entry.Attributes["PoisonApplyAmount"] = 3;
            List<string> warnings = new List<string>();
            entry.EnchantmentDefinition["Abilities"]["e1"].AsObject()["Prerequisites"] = Json.Parse("{\"$type\":\"TPrerequisiteFirstNTimes\"}");
            CardDef unsupported = EnchantmentMapper.Apply(card, entry, warnings);
            Assert.Equal(1, warnings.Count);
            Assert.Equal(1, unsupported.Effects.Count);
            entry.EnchantmentDefinition = null;
            EnchantmentMapper.Apply(card, entry, warnings);
            Assert.Equal(2, warnings.Count);
            // Merely observing a Poison attribute on an unenchanted item does
            // not establish a Poison action or trigger.
            entry.Enchantment = null;
            Assert.Equal(1, EnchantmentMapper.Apply(card, entry, warnings).Effects.Count);
            Assert.Equal(2, warnings.Count);
        }

        private static void ReaderSelectsTheCurrentEnchantmentByInstance()
        {
            try
            {
                NativeItemTemplateForEnchantTest template = new NativeItemTemplateForEnchantTest();
                template.Enchantments["Toxic"] = Definition(Gun, "Toxic");
                template.Enchantments["Icy"] = Definition(Cog, "Icy");
                NativeSkillCard native = new NativeSkillCard { Template = template };
                TheBazaar.Data.Entities = new Dictionary<string, object> { { "ignored-key", native } };
                BoardReadResult result = new BoardReadResult();
                result.Cards.Add(new LiveCardEntry { InstanceId = "skill-instance-1", TemplateId = Gun, Enchantment = "Toxic" });
                result.Cards.Add(new LiveCardEntry { InstanceId = "missing", TemplateId = Cog, Enchantment = "Icy" });
                LiveCardDetailReader.Populate(result);
                Assert.Equal("TActionPlayerPoisonApply", result.Cards[0].EnchantmentDefinition["Abilities"]["e1"]["Action"]["$type"].AsString());
                Assert.True(result.Cards[0].EnchantmentReadError == null);
                Assert.True(result.Cards[1].EnchantmentReadError != null);
                Assert.True(result.CardReadError.Contains("未找到对应的卡牌实例"));
                JsonValue dump = Json.Parse(LiveSkillReader.BuildDebugJson(result));
                Assert.Equal("TActionPlayerPoisonApply", dump["board"][0]["EnchantmentDefinition"]["Abilities"]["e1"]["Action"]["$type"].AsString());
                // Changing the selected enchantment must clear and replace the
                // prior definition, never leave Toxic attached to an Icy item.
                result.Cards[0].Enchantment = "Icy";
                result.Cards.RemoveAt(1);
                LiveCardDetailReader.Populate(result);
                Assert.Equal<string>(null, result.CardReadError);
                Assert.Equal("TActionCardFreeze", result.Cards[0].EnchantmentDefinition["Abilities"]["e1"]["Action"]["$type"].AsString());
            }
            finally { TheBazaar.Data.Entities = null; }
        }
    }
}
