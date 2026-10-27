using System;
using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class SkillsTests
    {
        private const string ShieldAction = "{\"$type\":\"TActionPlayerShieldApply\",\"ReferenceValue\":null,\"Target\":{\"$type\":\"TTargetPlayerRelative\",\"TargetMode\":\"Self\",\"Conditions\":null},\"Cost\":null}";

        public static void Run()
        {
            SkillCatalog catalog = SkillCatalog.LoadFile("data/skills.json");
            CurrentLiveSkills(catalog);
            TriggerOncePerUse(catalog);
            SubjectSideAndTags();
            ChargeChangesActivationTime();
            DamageBuffChangesDamage();
            UnsupportedFailsClosed(catalog);
            NativeNullTierInheritance();
            FoodComa(catalog);
        }

        private static BattleConfig Board()
        {
            BattleConfig config = new BattleConfig();
            config.PlayerMaxHp = 1000;
            config.EnemyMaxHp = 10000;
            config.MaxTime = 2;
            return config;
        }

        private static void Item(BattleConfig config, string id, Side side, int slot, double cooldown, double damage, params string[] tags)
        {
            CardDef card = new CardDef();
            card.Id = id;
            card.Cooldown = cooldown;
            card.Passive = cooldown == 0;
            card.Tags.AddRange(tags);
            if (damage > 0)
            {
                // 原生数值在结算时读实时属性；测试卡也走同一路径。
                card.LiveAttributes["DamageAmount"] = (int)damage;
                card.Effects.Add(new Effect { Kind = "damage", Amount = new NumExpr { Type = "cardAttribute", Attribute = "DamageAmount", TargetKind = "selfItem", Value = damage } });
            }
            config.Cards[id] = card;
            (side == Side.Player ? config.Player : config.Enemy).Items.Add(new LoadoutItem { CardId = id, Slot = slot });
        }

        private static LiveSkillEntry CatalogSkill(string id, string tier)
        {
            return new LiveSkillEntry { TemplateId = id, InstanceId = id + "-instance", Tier = tier };
        }

        private static LiveSkillEntry Synthetic(string attrs, string action, string condition, string section, string prerequisites)
        {
            string subject = section == "any" ? "{\"$type\":\"TTargetCardTriggerSource\",\"Conditions\":" + condition + "}" :
                "{\"$type\":\"TTargetCardSection\",\"TargetSection\":\"" + section + "\",\"ExcludeSelf\":false,\"Conditions\":" + condition + "}";
            string template = "{\"$type\":\"TCardSkill\",\"StartingTier\":\"Gold\",\"Tiers\":{\"Gold\":{\"Attributes\":" + attrs +
                ",\"AbilityIds\":[\"0\"],\"AuraIds\":[],\"TooltipIds\":[]}},\"Abilities\":{\"0\":{\"Id\":\"0\",\"Trigger\":{\"$type\":\"TTriggerOnItemUsed\",\"Subject\":" + subject +
                "},\"Action\":" + action + ",\"ActiveIn\":\"HandOnly\",\"WorksIn\":\"Anywhere\",\"Prerequisites\":" + prerequisites + "}},\"Auras\":{}}";
            return new LiveSkillEntry { TemplateId = "test-skill", InstanceId = "test-instance", Name = "Test Skill", Tier = "Gold", Template = Json.Parse(template) };
        }

        private static void CurrentLiveSkills(SkillCatalog catalog)
        {
            BattleConfig config = Board();
            config.PlayerMaxHp = 1075;
            config.PlayerStartHp = 900;
            for (int i = 0; i < 8; i++) Item(config, "item" + i, Side.Player, i, 0, 0);
            List<LiveSkillEntry> skills = new List<LiveSkillEntry>();
            skills.Add(CatalogSkill("d48611f7-c777-4825-bf38-cefd468775b5", "Gold"));
            skills.Add(CatalogSkill("663ca415-c46a-46d1-8f63-c04080fb6a30", "Silver"));
            skills.Add(CatalogSkill("f422485c-b23a-4c3f-9ad4-9d22718dc204", "Silver"));
            SkillMappingResult mapped = SkillMapper.Apply(config, skills, catalog);
            Assert.Equal(3, mapped.Skills.Count);
            Assert.Equal("live-applied", mapped.Skills[0].Status);
            Assert.Equal("out-of-combat", mapped.Skills[1].Status);
            Assert.Equal("inactive", mapped.Skills[2].Status);
            Assert.Equal(0, mapped.UnsupportedCount);
            Assert.Equal(0, config.PlayerSkills.Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            Assert.Near(1075, ctx.Player.MaxHp);
            Assert.Near(900, ctx.Player.Hp);
            Assert.Equal(8, ctx.Items.Count);
            Assert.Equal(0, ctx.Skills.Count);
            config.Player.Items.RemoveRange(4, 4);
            Assert.Equal("live-applied", SkillMapper.Apply(config, skills, catalog).Skills[2].Status);
        }

        private static void TriggerOncePerUse(SkillCatalog catalog)
        {
            BattleConfig config = Board();
            Item(config, "one", Side.Player, 0, 1, 1, "Friend");
            Item(config, "two", Side.Player, 1, 1, 1);
            Item(config, "idle", Side.Player, 2, 0, 0);
            Item(config, "foe", Side.Enemy, 0, 1, 1);
            LiveSkillEntry skill = CatalogSkill("56450d16-bc69-43fd-bb89-4aff071cda29", "Gold");
            skill.Attributes["ShieldApplyAmount"] = 7;
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { skill }, catalog);
            Assert.Equal("simulated", mapped.Skills[0].Status);
            Assert.True(mapped.Skills[0].SourceId.StartsWith("skill:"));
            BattleContext ctx = BattleEngine.InitBattle(config);
            Assert.Equal(4, ctx.Items.Count);
            Assert.Equal(1, ctx.Skills.Count);
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Near(28, result.Totals.Player.Shield); // four own uses, never multiplied by three board items
            Assert.Equal(4, EventCount(result, "skillTrigger", mapped.Skills[0].SourceId));
            Assert.Equal(0, EventCount(result, "activate", mapped.Skills[0].SourceId));
            Assert.Equal(3, result.PlayerItems.Count);
            // 技能实例自原生技能卡牌更新对齐起进属性快照流（Battle.CaptureItemSnapshots）；
            // 物品视图仍只含物品（上方 Count==3 已覆盖）。
            foreach (ItemSnapshot snapshot in result.ItemSnapshots)
                if (snapshot.ItemId.StartsWith("skill:"))
                    Assert.Equal(mapped.Skills[0].SourceId, snapshot.ItemId);
            Assert.Equal(1, SkillMapper.Apply(config, new[] { skill }, catalog).CombatCount);
            Assert.Equal(1, config.PlayerSkills.Count);
        }

        private static void SubjectSideAndTags()
        {
            string tag = "{\"$type\":\"TCardConditionalTag\",\"Tags\":[\"Friend\"],\"Operator\":\"Any\"}";
            BattleConfig config = Board();
            Item(config, "friend", Side.Player, 0, 1, 0, "Friend");
            Item(config, "other", Side.Player, 1, 1, 0);
            Item(config, "foe", Side.Enemy, 0, 1, 0, "Friend");
            SkillMapper.Apply(config, new[] { Synthetic("{\"ShieldApplyAmount\":5}", ShieldAction, tag, "SelfHand", "null") });
            Assert.Near(10, BattleEngine.Simulate(config).Totals.Player.Shield);
            SkillMapper.Apply(config, new[] { Synthetic("{\"ShieldApplyAmount\":5}", ShieldAction, tag, "OpponentHand", "null") });
            Assert.Near(10, BattleEngine.Simulate(config).Totals.Player.Shield);
            SkillMapper.Apply(config, new[] { Synthetic("{\"ShieldApplyAmount\":5}", ShieldAction, tag, "any", "null") });
            Assert.Near(20, BattleEngine.Simulate(config).Totals.Player.Shield);
            tag = tag.Replace("\"Any\"", "\"None\"");
            SkillMapper.Apply(config, new[] { Synthetic("{\"ShieldApplyAmount\":5}", ShieldAction, tag, "SelfHand", "null") });
            Assert.Near(10, BattleEngine.Simulate(config).Totals.Player.Shield);
            string sizes = "{\"$type\":\"TCardConditionalSize\",\"Sizes\":[\"Large\"],\"IsNot\":false}";
            config.Cards["friend"].Size = "Large";
            SkillMapper.Apply(config, new[] { Synthetic("{\"ShieldApplyAmount\":5}", ShieldAction, sizes, "SelfHand", "null") });
            Assert.Near(10, BattleEngine.Simulate(config).Totals.Player.Shield);
        }

        private static void ChargeChangesActivationTime()
        {
            BattleConfig config = Board();
            Item(config, "tool", Side.Player, 0, 1, 0, "Tool");
            Item(config, "weapon", Side.Player, 1, 3, 10, "Weapon");
            string action = "{\"$type\":\"TActionCardCharge\",\"Value\":null,\"TargetCount\":null,\"Target\":{\"$type\":\"TTargetCardRandom\",\"TargetSection\":\"SelfHand\",\"Conditions\":{\"$type\":\"TCardConditionalTag\",\"Tags\":[\"Weapon\"],\"Operator\":\"Any\"}},\"Cost\":null}";
            string condition = "{\"$type\":\"TCardConditionalTag\",\"Tags\":[\"Tool\"],\"Operator\":\"Any\"}";
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { Synthetic("{\"ChargeAmount\":1000,\"ChargeTargets\":1}", action, condition, "SelfHand", "null") });
            Assert.Equal("simulated", mapped.Skills[0].Status);
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Near(10, result.Totals.Player.Damage);
            foreach (BattleEvent ev in result.Events)
            {
                if (ev.Kind == "damage") Assert.Near(2, ev.Time);
                if (ev.Kind == "charge") Assert.Equal("weapon", ev.TargetItemId);
            }
        }

        private static void DamageBuffChangesDamage()
        {
            BattleConfig config = Board();
            Item(config, "tool", Side.Player, 0, 1, 0, "Tool");
            Item(config, "weapon", Side.Player, 1, 1, 10, "Weapon");
            string action = "{\"$type\":\"TActionCardModifyAttribute\",\"Value\":{\"$type\":\"TReferenceValueCardAttribute\",\"AttributeType\":\"Custom_0\",\"Target\":{\"$type\":\"TTargetCardSelf\",\"Conditions\":null},\"Modifier\":null},\"AttributeType\":\"DamageAmount\",\"Operation\":\"Add\",\"Duration\":{\"$type\":\"TDeterminantDuration\",\"DurationType\":\"UntilEndOfCombat\"},\"TargetCount\":null,\"Target\":{\"$type\":\"TTargetCardSection\",\"TargetSection\":\"SelfHand\",\"Conditions\":{\"$type\":\"TCardConditionalTag\",\"Tags\":[\"Weapon\"],\"Operator\":\"Any\"}},\"Cost\":null}";
            string condition = "{\"$type\":\"TCardConditionalTag\",\"Tags\":[\"Tool\"],\"Operator\":\"Any\"}";
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { Synthetic("{\"Custom_0\":3}", action, condition, "SelfHand", "null") });
            Assert.Equal("simulated", mapped.Skills[0].Status);
            Assert.Near(29, BattleEngine.Simulate(config).Totals.Player.Damage); // 13 + 16
            Assert.Near(10, config.Cards["weapon"].Effects[0].Amount.Value); // reusable catalog graph stays immutable
        }

        private static void UnsupportedFailsClosed(SkillCatalog catalog)
        {
            BattleConfig config = Board();
            Item(config, "one", Side.Player, 0, 1, 1);
            string prerequisite = "[{\"$type\":\"TPrerequisiteCardCount\",\"Subject\":{\"$type\":\"TTargetCardSelf\",\"Conditions\":{\"$type\":\"TCardConditionalAttribute\",\"Attribute\":\"Custom_0\"}},\"Comparison\":\"Equal\",\"Amount\":1}]";
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { Synthetic("{\"ShieldApplyAmount\":50}", ShieldAction, "null", "SelfHand", prerequisite) });
            Assert.Equal("unsupported", mapped.Skills[0].Status);
            Assert.Equal(1, mapped.UnsupportedCount);
            Assert.Equal(0, config.PlayerSkills.Count);
            Assert.Near(0, BattleEngine.Simulate(config).Totals.Player.Shield);
            LiveSkillEntry unknown = new LiveSkillEntry { TemplateId = "missing", Tier = "Gold" };
            Assert.Equal("missing-template", SkillMapper.Apply(config, new[] { unknown }, catalog).Skills[0].Status);
            LiveSkillEntry dynamicAura = CatalogSkill("0dd4d40e-ac14-4a57-8c7f-9d47a5bdd38a", "Gold");
            mapped = SkillMapper.Apply(config, new[] { dynamicAura }, catalog);
            // 人物条件光环现在作为战斗内动态层接入，不再整条技能回退。
            Assert.Equal("simulated", mapped.Skills[0].Status);
            Assert.Equal(1, config.PlayerSkills.Count);
            Assert.Equal(1, config.PlayerSkills[0].NativeRuntimeAuras.Count);
            LiveSkillEntry fire = CatalogSkill("ffceb0c7-7913-4339-8013-f1bcc67cca9c", "Silver");
            mapped = SkillMapper.Apply(config, new[] { fire }, catalog);
            Assert.Equal("simulated", mapped.Skills[0].Status);
            Assert.Equal(1, EventCount(BattleEngine.Simulate(config), "burn", mapped.Skills[0].SourceId));
        }

        private static void NativeNullTierInheritance()
        {
            BattleConfig config = Board();
            Item(config, "one", Side.Player, 0, 1, 0);
            LiveSkillEntry skill = Synthetic("{\"ShieldApplyAmount\":7}", ShieldAction, "null", "SelfHand", "null");
            skill.Template["Tiers"].AsObject()["Diamond"] = Json.Parse("{\"Attributes\":{\"ShieldApplyAmount\":9},\"AbilityIds\":null,\"AuraIds\":null,\"TooltipIds\":null}");
            skill.Tier = "Diamond";
            Assert.Equal("simulated", SkillMapper.Apply(config, new[] { skill }).Skills[0].Status);
            Assert.Near(18, BattleEngine.Simulate(config).Totals.Player.Shield);
            skill.Template["Tiers"]["Diamond"].AsObject()["AbilityIds"] = Json.Parse("[]");
            Assert.Equal("inactive", SkillMapper.Apply(config, new[] { skill }).Skills[0].Status);
        }

        private static void FoodComa(SkillCatalog catalog)
        {
            BattleConfig config = Board();
            Item(config, "food", Side.Player, 0, 1, 0, "Food");
            Item(config, "other", Side.Player, 1, 1, 0);
            LiveSkillEntry skill = CatalogSkill("778da878-7748-4782-b348-d5e8df792882", "Gold");
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { skill }, catalog);
            Assert.Equal("simulated", mapped.Skills[0].Status);
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(1, EventCount(result, "slow", mapped.Skills[0].SourceId));
            Assert.Equal(1, EventCount(result, "regen", mapped.Skills[0].SourceId));
            foreach (BattleEvent ev in result.Events)
                if (ev.Kind == "slow") { Assert.Equal("food", ev.TargetItemId); Assert.Near(1, ev.Value.Value); }
        }

        private static int EventCount(BattleResult result, string kind, string sourceId)
        {
            int count = 0;
            foreach (BattleEvent ev in result.Events) if (ev.Kind == kind && ev.SourceItemId == sourceId) count++;
            return count;
        }
    }
}
