using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class Replay4HeroAuraSnapshotTests
    {
        public static void Run()
        {
            BattleContext live = Context(true);
            Assert.Near(4695, live.Enemy.MaxHp);
            Assert.Near(4525, live.Enemy.Hp);
            Assert.Near(7, live.Enemy.Regen);
            NativeDynamicAuras.Refresh(live);
            Assert.Near(4695, live.Enemy.MaxHp);
            live.Items[0].Destroyed = true;
            NativeDynamicAuras.Refresh(live);
            Assert.Near(4195, live.Enemy.MaxHp);
            Assert.Near(4, live.Enemy.Regen);
            live.Items[0].Destroyed = false;
            NativeDynamicAuras.Refresh(live);
            Assert.Near(4695, live.Enemy.MaxHp);
            Assert.Near(7, live.Enemy.Regen);
            BattleContext pure = Context(false);
            Assert.Near(5195, pure.Enemy.MaxHp);
            Assert.Near(10, pure.Enemy.Regen);
            MappedLiveSkillDoesNotBakeBonusAgain();
        }

        private static void MappedLiveSkillDoesNotBakeBonusAgain()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, EnemyMaxHp = 4695,
                EnemyStartHp = 4525, EnemyHeroAttributesAreLive = true };
            config.EnemyAttributes["HealthMax"] = 4695;
            JsonValue template = Json.Parse(@"{""$type"":""TCardSkill"",""StartingTier"":""Silver"",
                ""Tiers"":{""Silver"":{""Attributes"":{""Custom_0"":500},""AbilityIds"":[],""AuraIds"":[""0""],""TooltipIds"":[]}},
                ""Abilities"":{},""Auras"":{""0"":{""Action"":{""$type"":""TAuraActionPlayerModifyAttribute"",
                ""AttributeType"":""HealthMax"",""Operation"":""Add"",""Target"":{""$type"":""TTargetPlayerRelative"",""TargetMode"":""Self""},
                ""Value"":{""$type"":""TReferenceValueCardAttribute"",""AttributeType"":""Custom_0"",""DefaultValue"":0,
                ""Target"":{""$type"":""TTargetCardSelf""}}},""ActiveIn"":""HandOnly"",""WorksIn"":""Anywhere""}}}");
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { new LiveSkillEntry {
                TemplateId = "large-appetites", InstanceId = "large-appetites", Tier = "Silver",
                AttributesAreLive = true, TemplateBoard = true, Template = template } }, null, Side.Enemy);
            Assert.Equal(0, mapped.Warnings.Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            Assert.Near(4695, ctx.Enemy.MaxHp);
            Assert.Near(4525, ctx.Enemy.Hp);
        }

        private static BattleContext Context(bool live)
        {
            // e4646002: captured MaxHp4695 already contains Large Appetites500.
            BattleConfig config = new BattleConfig { NativeMechanics = true, EnemyMaxHp = 4695,
                EnemyStartHp = 4525, EnemyStartRegen = 7, EnemyHeroAttributesAreLive = live };
            config.EnemyAttributes["HealthMax"] = 4695;
            config.EnemyAttributes["HealthRegen"] = 7;
            CardDef source = new CardDef { Id = "live-hero-aura", Passive = true };
            foreach (string attribute in new[] { "HealthMax", "HealthRegen" })
                source.NativeRuntimeAuras.Add(Json.Parse("{\"Action\":{\"$type\":\"TAuraActionPlayerModifyAttribute\",\"AttributeType\":\"" + attribute +
                    "\",\"Operation\":\"Add\",\"Target\":{\"$type\":\"TTargetPlayerRelative\",\"TargetMode\":\"Self\"},\"Value\":{\"$type\":\"TFixedValue\",\"Value\":" +
                    (attribute == "HealthMax" ? "500" : "3") + "}}}"));
            config.Cards[source.Id] = source;
            config.Enemy.Items.Add(new LoadoutItem { CardId = source.Id, Slot = 0 });
            return BattleEngine.InitBattle(config);
        }
    }
}
