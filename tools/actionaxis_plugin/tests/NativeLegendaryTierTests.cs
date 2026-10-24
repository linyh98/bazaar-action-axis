using System;
using System.Collections.Generic;
using System.Linq;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class NativeLegendaryTierTests
    {
        public static void Run()
        {
            LegendaryCoreUsesDiamondFallback();
            LiveOverridesStillWin();
            MonsterLegendaryUsesDiamondRulesAndKeepsDisplayTier();
        }

        private static BattleConfig CoreBoard()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true };
            CardDef core = new CardDef {
                Id = "legendary-core", BaseItemId = NativeItemMapper.LauncherCore,
                Tier = "Legendary", Cooldown = 6, Size = "Medium"
            };
            config.Cards[core.Id] = core;
            config.Player.Items.Add(new LoadoutItem { CardId = core.Id, Slot = 0 });
            for (int i = 0; i < 5; i++)
            {
                CardDef target = new CardDef { Id = "target-" + i, Cooldown = 20, DataSource = "native-template" };
                config.Cards[target.Id] = target;
                config.Player.Items.Add(new LoadoutItem { CardId = target.Id, Slot = i + 2 });
            }
            return config;
        }

        private static void LegendaryCoreUsesDiamondFallback()
        {
            // Native TCardItem.TryGetTierTemplate maps Legendary to Diamond.
            // A missing live FlyingTargets must not fall back to Bronze's two.
            BattleConfig config = CoreBoard();
            Assert.Equal(0, NativeItemMapper.Apply(config).Count);
            CardDef core = config.Cards["legendary-core"];
            Assert.Equal("Legendary", core.Tier);
            Effect flying = core.Effects.Single(delegate(Effect effect) { return effect.Kind == "fly"; });
            BattleContext ctx = BattleEngine.InitBattle(config);
            Effects.ResolveEffect(ctx, flying, ctx.Items[0], 0, 0, null);
            Assert.Equal(4, ctx.Items.Count(delegate(InternalItem item) { return item.Flying; }));
            Assert.False(ctx.Items[0].Flying);
        }

        private static void LiveOverridesStillWin()
        {
            BattleConfig config = CoreBoard();
            config.Cards["legendary-core"].LiveAttributes["FlyingTargets"] = 3;
            NativeItemMapper.Apply(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            Effect flying = ctx.Items[0].Card.Effects.Single(delegate(Effect effect) { return effect.Kind == "fly"; });
            Effects.ResolveEffect(ctx, flying, ctx.Items[0], 0, 0, null);
            Assert.Equal(3, ctx.Items.Count(delegate(InternalItem item) { return item.Flying; }));

            CardDef cog = new CardDef { Id = "legendary-cog", BaseItemId = NativeItemMapper.Cog, Tier = "Legendary", Cooldown = 4 };
            config = new BattleConfig { NativeMechanics = true };
            config.Cards[cog.Id] = cog;
            config.Player.Items.Add(new LoadoutItem { CardId = cog.Id, Slot = 0 });
            NativeItemMapper.Apply(config);
            Assert.Near(4, config.Cards[cog.Id].Effects.Single(delegate(Effect effect) { return effect.Kind == "haste"; }).Seconds);
            Assert.Equal("Legendary", config.Cards[cog.Id].Tier);
        }

        private static void MonsterLegendaryUsesDiamondRulesAndKeepsDisplayTier()
        {
            // Deliberately conflicting Legendary rows establish that native
            // lookup uses Diamond, rather than merging an extra upgrade tier.
            JsonValue item = Json.Parse(@"{""$type"":""TCardItem"",""Size"":""Small"",""Tiers"":{
                ""Bronze"":{""Attributes"":{""CooldownMax"":6000,""DamageAmount"":10}},
                ""Diamond"":{""Attributes"":{""DamageAmount"":40}},
                ""Legendary"":{""Attributes"":{""CooldownMax"":25,""DamageAmount"":999,""LegendaryOnly"":1}}}}");
            JsonValue skill = Json.Parse(@"{""$type"":""TCardSkill"",""Tiers"":{
                ""Gold"":{""Attributes"":{""ChargeAmount"":1000}},
                ""Diamond"":{""Attributes"":{""ChargeAmount"":2000}},
                ""Legendary"":{""Attributes"":{""ChargeAmount"":9999}}}}");
            JsonValue monster = Json.Parse(@"{""Id"":""legendary-monster"",""Player"":{
                ""Attributes"":{""HealthMax"":900},""Hand"":{""Items"":[
                    {""TemplateId"":""item"",""Tier"":""Legendary"",""SocketId"":""Socket_0"",""Attributes"":{}},
                    {""TemplateId"":""item"",""Tier"":""Legendary"",""SocketId"":""Socket_1"",""Attributes"":{""DamageAmount"":55}}]},
                ""Skills"":[{""TemplateId"":""skill"",""Tier"":""Legendary"",""Attributes"":{}}]}}");
            LiveMonsterEntry parsed = MonsterTemplateExtractor.FromJson(monster, delegate(string id) { return id == "item" ? item : skill; });
            Assert.Equal("Legendary", parsed.Cards[0].Tier);
            Assert.Equal(40, parsed.Cards[0].Attributes["DamageAmount"]);
            Assert.Equal(6000, parsed.Cards[0].Attributes["CooldownMax"]);
            Assert.False(parsed.Cards[0].Attributes.ContainsKey("LegendaryOnly"));
            Assert.Equal(55, parsed.Cards[1].Attributes["DamageAmount"]);
            Assert.Equal("Legendary", parsed.Skills[0].Tier);
            Assert.Equal(2000, parsed.Skills[0].Attributes["ChargeAmount"]);
            Assert.Equal(999, item["Tiers"]["Legendary"]["Attributes"]["DamageAmount"].AsInt());
        }
    }
}
