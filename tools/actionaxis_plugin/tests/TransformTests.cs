using System;
using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // 战斗内复制变形：只覆盖“场上已有道具互相复制”，场外卡池变形必须 fail-closed。
    public static class TransformTests
    {
        public static void Run()
        {
            PrinterCopiesLeftSmallItemThreeTimes();
            UncalibratedTransformFailsClosed();
        }

        private static string PrinterTemplate(string name, double copies)
        {
            return @"{""$type"":""TCardItem"",""InternalName"":""" + name + @""",""Size"":""Large"",""Tags"":[],""HiddenTags"":[],
              ""Tiers"":{""Gold"":{""Attributes"":{""CooldownMax"":5000},""AbilityIds"":[""0""],""AuraIds"":[],""TooltipIds"":[]}},
              ""Abilities"":{""0"":{""Trigger"":{""$type"":""TTriggerOnCardFired""},""ActiveIn"":""HandOnly"",""WorksIn"":""Anywhere"",
                ""Action"":{""$type"":""TActionCardTransform"",
                  ""SpawnContext"":{""$type"":""TSpawnContextQuery"",""Limit"":{""$type"":""TFixedValue"",""Value"":" + copies + @"}},
                  ""Duration"":{""$type"":""TDeterminantDuration"",""DurationType"":""UntilEndOfCombat""},
                  ""Abilities"":null,""TargetCount"":null,
                  ""Target"":{""$type"":""TTargetCardSelf"",""Conditions"":null}},
                ""Prerequisites"":null}},""Auras"":{}}";
        }

        private static LiveCardEntry Entry(string id, int slot, string template, string tier = "Gold")
        {
            JsonValue parsed = Json.Parse(template);
            return new LiveCardEntry
            {
                TemplateId = id,
                Tier = tier,
                Socket = slot,
                Template = parsed,
                Size = NativeSkillSupport.Text(parsed, "Size"),
                AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(parsed, tier)
            };
        }

        private const string GemTemplate = @"{""$type"":""TCardItem"",""InternalName"":""Gem"",""Size"":""Small"",""Tags"":[],""HiddenTags"":[],
          ""Tiers"":{""Gold"":{""Attributes"":{""CooldownMax"":2000,""DamageAmount"":10},""AbilityIds"":[],""AuraIds"":[],""TooltipIds"":[]}},
          ""Abilities"":{},""Auras"":{}}";

        private static BattleConfig Board(string printerName)
        {
            List<LiveCardEntry> entries = new List<LiveCardEntry>();
            entries.Add(Entry("gem", 0, GemTemplate));
            entries.Add(Entry("printer", 1, PrinterTemplate(printerName, 3)));
            BattleConfig config = CardMapper.Build(entries, CardCatalog.FromCards(new List<CardDef>()), 1000, 1000, 20).Config;
            config.NativeMechanics = true;
            return config;
        }

        private static void PrinterCopiesLeftSmallItemThreeTimes()
        {
            BattleConfig config = Board("3D Printer");
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem printer = null;
            foreach (InternalItem item in ctx.Items) if (item.CardId == "printer#1") printer = item;
            Assert.True(printer != null);
            Assert.Equal(2, ctx.Items.Count);

            foreach (Effect effect in printer.Card.Effects)
                Effects.ResolveEffect(ctx, effect, printer, 0.05, 0, null);
            BattleEngine.FlushPendingItems(ctx);

            // 原打印机位置变成第一份副本，另外两份排在其后。
            Assert.Equal(4, ctx.Items.Count);
            int copies = 0;
            foreach (InternalItem item in ctx.Items)
            {
                if (item.CardId != "gem#0" || item.Id == "gem#0") continue;
                copies++;
                Assert.Near(2, item.CooldownMax);
            }
            Assert.Equal(3, copies);
            Assert.Equal("gem#0", printer.CardId);
            int transforms = 0;
            foreach (BattleEvent ev in ctx.Events) if (ev.Kind == "transform") transforms++;
            Assert.Equal(3, transforms);
        }

        private static void UncalibratedTransformFailsClosed()
        {
            BattleConfig config = Board("Mystery Box");
            Assert.Equal(1, NativeCardAbilityMapper.Apply(config).Count);
            CardDef card = config.Cards[config.Player.Items[1].CardId];
            Assert.True(card.DataSource != "native-template");
        }
    }
}
