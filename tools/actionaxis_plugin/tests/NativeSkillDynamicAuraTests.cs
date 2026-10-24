using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class NativeSkillDynamicAuraTests
    {
        private const string Flying = "{\"$type\":\"TCardConditionalAttribute\",\"Attribute\":\"Flying\",\"ComparisonOperator\":\"Equal\",\"ComparisonValue\":{\"$type\":\"TFixedValue\",\"Value\":1}}";
        private const string CanCrit = "{\"$type\":\"TCardConditionalCanCrit\",\"IsNot\":false}";
        private const string Fixed = "{\"$type\":\"TFixedValue\",\"Value\":20}";

        public static void Run()
        {
            FlyingTargetTracksBothTransitions();
            FlyingCountPrerequisiteTracksBothTransitions();
            FlyingCountValueTracksBothTransitions();
            InheritedLiveFlightBonusIsNotDuplicated();
            MutableAttributeRankIsNotClassifiedAsStatic();
            MutableCardAttributePrerequisiteIsNotClassifiedAsStatic();
            MutableAttributeReferenceIsNotClassifiedAsStatic();
            TierAndSizeFiltersRemainInherited();
        }

        private static string Section(string condition)
        {
            return "{\"$type\":\"TTargetCardSection\",\"TargetSection\":\"SelfHand\",\"ExcludeSelf\":false,\"Conditions\":" + condition + "}";
        }

        private static LiveSkillEntry Skill(string condition, string prerequisites = "null", string value = Fixed)
        {
            return new LiveSkillEntry { InstanceId = "dynamic-aura", TemplateId = "dynamic-aura", Tier = "Bronze", Template = Json.Parse(
                "{\"$type\":\"TCardSkill\",\"InternalName\":\"Dynamic aura\",\"Tiers\":{\"Bronze\":{\"Attributes\":{\"Custom_0\":20,\"Custom_1\":0},\"AbilityIds\":[],\"AuraIds\":[\"0\"]}}," +
                "\"Abilities\":{},\"Auras\":{\"0\":{\"ActiveIn\":\"HandOnly\",\"WorksIn\":\"Anywhere\",\"Prerequisites\":" + prerequisites +
                ",\"Action\":{\"$type\":\"TAuraActionCardModifyAttribute\",\"AttributeType\":\"CritChance\",\"Operation\":\"Add\",\"Target\":" + Section(condition) + ",\"Value\":" + value + "}}}}") };
        }

        private static BattleConfig Board(int liveCrit = 0, bool wasFlying = false)
        {
            CardDef card = new CardDef { Id = "weapon", Tier = "Bronze", Size = "Small", Cooldown = 5,
                NativeAttributesAreLive = true };
            card.LiveAttributes["CritChance"] = liveCrit;
            card.LiveAttributes["Flying"] = wasFlying ? 1 : 0;
            card.LiveAttributes["DamageAmount"] = 10;
            card.Effects.Add(new Effect { Kind = "damage", Amount = new NumExpr { Type = "cardAttribute", Attribute = "DamageAmount" } });
            BattleConfig config = new BattleConfig { NativeMechanics = true, PlayerMaxHp = 1000, EnemyMaxHp = 1000 };
            config.Cards[card.Id] = card;
            config.Player.Items.Add(new LoadoutItem { CardId = card.Id, Slot = 0 });
            return config;
        }

        private static BattleContext Runtime(BattleConfig config, LiveSkillEntry skill)
        {
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { skill });
            Assert.Equal(0, mapped.Warnings.Count);
            Assert.Equal("simulated", mapped.Skills[0].Status);
            Assert.Equal(1, config.PlayerSkills.Count);
            Assert.Equal(1, config.PlayerSkills[0].NativeRuntimeAuras.Count);
            return BattleEngine.InitBattle(config);
        }

        private static void Flight(BattleContext ctx, InternalItem item, bool flying)
        {
            Effects.ResolveEffect(ctx, new Effect { Kind = flying ? "fly" : "flyStop", Count = 1,
                Target = new Target { Kind = "selfItem" } }, item, 1, 0, null);
        }

        private static void Transitions(BattleContext ctx, double normal, double flying)
        {
            InternalItem item = ctx.Items[0];
            Assert.Near(normal, ItemAttributes.Get(item, "CritChance"));
            Flight(ctx, item, true);
            Assert.Near(flying, ItemAttributes.Get(item, "CritChance"));
            for (int i = 0; i < 4; i++) NativeDynamicAuras.Refresh(ctx);
            Assert.Near(flying, ItemAttributes.Get(item, "CritChance"));
            Flight(ctx, item, false);
            Assert.Near(normal, ItemAttributes.Get(item, "CritChance"));
        }

        private static void FlyingTargetTracksBothTransitions()
        {
            // Same nested target shape as the native flying crit skill: a zero
            // crit-chance active weapon must be eligible when it starts flying.
            string condition = "{\"$type\":\"TCardConditionalAnd\",\"Conditions\":[" + CanCrit + "," + Flying + "]}";
            Transitions(Runtime(Board(), Skill(condition)), 0, 20);
        }

        private static void FlyingCountPrerequisiteTracksBothTransitions()
        {
            string prereq = "[{\"$type\":\"TPrerequisiteCardCount\",\"Subject\":" + Section(Flying) +
                ",\"Comparison\":\"GreaterThanOrEqual\",\"Amount\":1}]";
            Transitions(Runtime(Board(), Skill(CanCrit, prereq)), 0, 20);
        }

        private static void FlyingCountValueTracksBothTransitions()
        {
            string value = "{\"$type\":\"TReferenceValueCardCount\",\"Target\":" + Section(Flying) +
                ",\"Modifier\":{\"ModifyMode\":\"Multiply\",\"Value\":" + Fixed + ",\"ShouldRound\":true}}";
            Transitions(Runtime(Board(), Skill(CanCrit, "null", value)), 0, 20);
        }

        private static void InheritedLiveFlightBonusIsNotDuplicated()
        {
            // Observed 25 = permanent 5 plus the current flying skill's 20.
            // A fresh prediction begins grounded, then re-evaluates each change.
            BattleContext ctx = Runtime(Board(25, true), Skill(Flying));
            Transitions(ctx, 5, 25);
            Assert.Equal(0, ctx.Warnings.Count);
        }

        private static void MutableAttributeRankIsNotClassifiedAsStatic()
        {
            BattleConfig config = Board();
            CardDef other = config.Cards["weapon"].Clone();
            other.Id = "other";
            other.LiveAttributes["DamageAmount"] = 20;
            other.LiveAttributes["CritChance"] = 20; // Snapshot already includes the highest-Damage aura.
            config.Cards[other.Id] = other;
            config.Player.Items.Add(new LoadoutItem { CardId = other.Id, Slot = 1 });
            BattleContext ctx = Runtime(config, Skill("{\"$type\":\"TCardConditionalAttributeHighest\",\"AttributeType\":\"DamageAmount\"}"));
            Assert.Near(0, ItemAttributes.Get(ctx.Items[0], "CritChance"));
            Assert.Near(20, ItemAttributes.Get(ctx.Items[1], "CritChance"));
            Effects.ResolveEffect(ctx, new Effect { Kind = "modifyAttribute", Attribute = "DamageAmount", Operation = "Add",
                Amount = new NumExpr { Type = "const", Value = 15 }, Target = new Target { Kind = "selfItem" } }, ctx.Items[0], 1, 0, null);
            Assert.Near(20, ItemAttributes.Get(ctx.Items[0], "CritChance"));
            Assert.Near(0, ItemAttributes.Get(ctx.Items[1], "CritChance"));
        }

        private static void MutableCardAttributePrerequisiteIsNotClassifiedAsStatic()
        {
            string prereq = "[{\"$type\":\"TPrerequisiteCardAttributeComparator\",\"Subject\":{\"$type\":\"TTargetCardSelf\",\"Conditions\":null}," +
                "\"Attribute\":\"Custom_0\",\"AttributeOther\":\"Custom_1\",\"Comparison\":\"GreaterThan\"}]";
            BattleContext ctx = Runtime(Board(20), Skill(CanCrit, prereq));
            Assert.Near(20, ItemAttributes.Get(ctx.Items[0], "CritChance"));
        }

        private static void MutableAttributeReferenceIsNotClassifiedAsStatic()
        {
            LiveSkillEntry skill = Skill(CanCrit, "null", "{\"$type\":\"TReferenceValueCardAttribute\",\"AttributeType\":\"Flying\",\"Target\":{\"$type\":\"TTargetCardSelf\",\"Conditions\":null}}");
            // A skill cannot fly, but a mutable card-attribute reference still
            // must not be declared inherited and silently omitted at runtime.
            BattleContext ctx = Runtime(Board(), skill);
            Assert.Near(0, ItemAttributes.Get(ctx.Items[0], "CritChance"));
        }

        private static void TierAndSizeFiltersRemainInherited()
        {
            string stableCondition = "{\"$type\":\"TCardConditionalAnd\",\"Conditions\":[" +
                "{\"$type\":\"TCardConditionalTier\",\"Tiers\":[\"Bronze\"],\"IsNot\":false}," +
                "{\"$type\":\"TCardConditionalSize\",\"Sizes\":[\"Small\"],\"IsNot\":false}]}";
            string ownTierValue = "{\"$type\":\"TReferenceValueCardAttribute\",\"AttributeType\":\"Custom_0\",\"Target\":{\"$type\":\"TTargetCardSelf\",\"Conditions\":null}}";
            BattleConfig config = Board(20);
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { Skill(stableCondition, "null", ownTierValue) });
            Assert.Equal(0, mapped.Warnings.Count);
            Assert.Equal("live-applied", mapped.Skills[0].Status);
            Assert.Equal(0, config.PlayerSkills.Count);
        }
    }
}
