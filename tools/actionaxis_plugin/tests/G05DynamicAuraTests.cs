using System;
using System.Collections.Generic;
using System.IO;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // G05：动态倍率、敌方目标、人物光环与持续冷却缩减。
    // 数值来源见 fixtures/native_g05_seeds.json 与
    // fixtures/native_g05_replay_calibration.json（真实 BPP 回放导出值）。
    public static class G05DynamicAuraTests
    {
        private static JsonValue _seeds;
        private static JsonValue _calibration;
        private static readonly CardCatalog EmptyCatalog = CardCatalog.FromCards(new List<CardDef>());

        public static void Run()
        {
            _seeds = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_g05_seeds.json"));
            _calibration = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_g05_replay_calibration.json"));
            PassivePowerMultipliesZeroCooldownCardsWithoutCompounding();
            RateLimiterMultipliesBothBoardsAndDrivesCharge();
            ForceFieldAddsCurrentShieldWithoutCompounding();
            SpikyShieldLiveSnapshotIsNotCountedTwice();
            DynamicHeroAuraTogglesWithoutStacking();
            DynamicHeroHealthMaxTogglesWithoutStacking();
            CooldownAuraChangesActualActivationTime();
        }

        private static JsonValue Template(string key)
        {
            return _seeds[key];
        }

        private static string Id(JsonValue template)
        {
            return NativeSkillSupport.Text(template, "Id");
        }

        private static LiveCardEntry LiveEntry(JsonValue template, string tier, Dictionary<string, int> attrs, bool live)
        {
            return new LiveCardEntry
            {
                TemplateId = Id(template), Template = template, Tier = tier,
                Size = NativeSkillSupport.Text(template, "Size"), Socket = 0,
                AttributesAreLive = live, Attributes = attrs
            };
        }

        private static BattleConfig MappedCardBoard(JsonValue template, string tier, Dictionary<string, int> attrs, bool live, double maxTime, double playerHp = 1000, double enemyHp = 1000)
        {
            BattleConfig config = CardMapper.Build(new[] { LiveEntry(template, tier, attrs, live) },
                EmptyCatalog, playerHp, enemyHp, maxTime).Config;
            config.NativeMechanics = true;
            List<string> warnings = NativeCardAbilityMapper.Apply(config);
            Assert.Equal(0, warnings.Count);
            return config;
        }

        private static CardDef Card(string id, double cooldown, string[] tags, string[] hidden, Dictionary<string, int> attrs)
        {
            CardDef card = new CardDef { Id = id, Name = id, Cooldown = cooldown, Size = "Small", NativeAttributesAreLive = false };
            if (tags != null) card.Tags.AddRange(tags);
            if (hidden != null) card.HiddenTags.AddRange(hidden);
            if (attrs != null) foreach (KeyValuePair<string, int> pair in attrs) card.LiveAttributes[pair.Key] = pair.Value;
            return card;
        }

        private static BattleConfig EmptyBoard(double playerHp = 1000, double enemyHp = 1000, double maxTime = 20)
        {
            return new BattleConfig
            {
                NativeMechanics = true, PlayerMaxHp = playerHp, EnemyMaxHp = enemyHp,
                PlayerStartHp = playerHp, EnemyStartHp = enemyHp, MaxTime = maxTime
            };
        }

        private static LiveSkillEntry Skill(JsonValue template, string tier)
        {
            return new LiveSkillEntry
            {
                TemplateId = Id(template), Template = template, Tier = tier,
                Name = NativeSkillSupport.Text(template, "InternalName")
            };
        }

        private static LiveSkillEntry CooldownSkill()
        {
            const string prerequisite = "[{\"$type\":\"TPrerequisitePlayer\",\"Subject\":{\"$type\":\"TTargetPlayerRelative\",\"TargetMode\":\"Self\",\"Conditions\":{" +
                "\"$type\":\"TPlayerConditionalAttribute\",\"Attribute\":\"Health\",\"ComparisonOperator\":\"LessThan\",\"ComparisonValue\":{" +
                "\"$type\":\"TReferenceValuePlayerAttribute\",\"AttributeType\":\"HealthMax\",\"Target\":{\"$type\":\"TTargetPlayerRelative\",\"TargetMode\":\"Self\",\"Conditions\":null}," +
                "\"DefaultValue\":0,\"Modifier\":{\"ModifyMode\":\"Multiply\",\"Value\":{\"$type\":\"TFixedValue\",\"Value\":0.5},\"ShouldRound\":true}}}}}]";
            const string aura = "{\"$type\":\"TAuraActionCardModifyAttribute\",\"AttributeType\":\"PercentCooldownReduction\",\"Operation\":\"Add\"," +
                "\"Value\":{\"$type\":\"TFixedValue\",\"Value\":50},\"Target\":{\"$type\":\"TTargetCardSection\",\"TargetSection\":\"SelfHand\",\"ExcludeSelf\":false,\"Conditions\":null}}";
            JsonValue template = Json.Parse("{\"$type\":\"TCardSkill\",\"InternalName\":\"G05 cooldown\",\"Tiers\":{\"Bronze\":{\"Attributes\":{},\"AbilityIds\":[],\"AuraIds\":[\"0\"],\"TooltipIds\":[]}},\"Abilities\":{},\"Auras\":{\"0\":{\"ActiveIn\":\"HandOnly\",\"WorksIn\":\"Anywhere\",\"Prerequisites\":" +
                prerequisite + ",\"Action\":" + aura + "}}}");
            return new LiveSkillEntry { TemplateId = "g05-cooldown", Template = template, Tier = "Bronze", Name = "G05 cooldown" };
        }

        private static InternalItem Item(BattleContext ctx, string id)
        {
            return ctx.Items.Find(delegate(InternalItem item) { return item.CardId == id; });
        }

        private static void PassivePowerMultipliesZeroCooldownCardsWithoutCompounding()
        {
            JsonValue template = Template("passivePower");
            BattleConfig config = EmptyBoard();
            CardDef passive = Card("passive", 0, new[] { "Weapon" }, null,
                new Dictionary<string, int> { { "CooldownMax", 0 }, { "DamageAmount", 20 } });
            CardDef active = Card("active", 1, new[] { "Weapon" }, null,
                new Dictionary<string, int> { { "CooldownMax", 1000 }, { "DamageAmount", 20 } });
            CardDef burn = Card("burn", 0, null, new[] { "Burn" },
                new Dictionary<string, int> { { "CooldownMax", 0 }, { "BurnApplyAmount", 4 } });
            config.Cards[passive.Id] = passive;
            config.Cards[active.Id] = active;
            config.Cards[burn.Id] = burn;
            config.Player.Items.Add(new LoadoutItem { CardId = passive.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = active.Id, Slot = 1 });
            config.Player.Items.Add(new LoadoutItem { CardId = burn.Id, Slot = 2 });

            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { Skill(template, "Gold") }, null, Side.Player);
            Assert.Equal(0, mapped.Warnings.Count);
            Assert.Equal("simulated", mapped.Skills[0].Status);
            Assert.Equal(1, config.PlayerSkills.Count);

            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem passiveItem = Item(ctx, "passive"), activeItem = Item(ctx, "active"), burnItem = Item(ctx, "burn");
            Assert.Near(25, ItemAttributes.Get(passiveItem, "DamageAmount"));
            Assert.Near(20, ItemAttributes.Get(activeItem, "DamageAmount"));
            Assert.Near(5, ItemAttributes.Get(burnItem, "BurnApplyAmount"));
            for (int i = 0; i < 8; i++) NativeDynamicAuras.Refresh(ctx);
            Assert.Near(25, ItemAttributes.Get(passiveItem, "DamageAmount"));

            // 运行时 CooldownMax 进出 0：倍率应可逆，不能每帧再乘一次。
            for (int i = 0; i < 5; i++)
            {
                Change(ctx, activeItem, "CooldownMax", "Set", 0);
                Assert.Near(25, ItemAttributes.Get(activeItem, "DamageAmount"));
                for (int j = 0; j < 3; j++) NativeDynamicAuras.Refresh(ctx);
                Assert.Near(25, ItemAttributes.Get(activeItem, "DamageAmount"));
                Change(ctx, activeItem, "CooldownMax", "Set", 1000);
                Assert.Near(20, ItemAttributes.Get(activeItem, "DamageAmount"));
            }

            // 实时快照 25 已含 ×1.25；初始化必须先反推出 20，再只应用一次。
            BattleConfig live = EmptyBoard();
            CardDef livePassive = Card("live-passive", 0, new[] { "Weapon" }, null,
                new Dictionary<string, int> { { "CooldownMax", 0 }, { "DamageAmount", 25 } });
            livePassive.NativeAttributesAreLive = true;
            live.Cards[livePassive.Id] = livePassive;
            live.Player.Items.Add(new LoadoutItem { CardId = livePassive.Id, Slot = 0 });
            Assert.Equal(0, SkillMapper.Apply(live, new[] { Skill(template, "Gold") }, null, Side.Player).Warnings.Count);
            BattleContext liveCtx = BattleEngine.InitBattle(live);
            InternalItem liveItem = Item(liveCtx, "live-passive");
            Assert.Near(20, liveItem.AuraBaseAttributes["DamageAmount"]);
            Assert.Near(25, ItemAttributes.Get(liveItem, "DamageAmount"));
            for (int i = 0; i < 8; i++) NativeDynamicAuras.Refresh(liveCtx);
            Assert.Near(25, ItemAttributes.Get(liveItem, "DamageAmount"));
        }

        private static void RateLimiterMultipliesBothBoardsAndDrivesCharge()
        {
            BattleConfig config = EmptyBoard();
            CardDef self = Card("self", 2, null, null, new Dictionary<string, int> { { "CooldownMax", 2000 }, { "ChargeAmount", 4000 } });
            CardDef enemy = Card("enemy", 2, null, null, new Dictionary<string, int> { { "CooldownMax", 2000 }, { "ChargeAmount", 2000 } });
            CardDef target = Card("target", 10, null, null, new Dictionary<string, int> { { "CooldownMax", 10000 } });
            config.Cards[self.Id] = self;
            config.Cards[enemy.Id] = enemy;
            config.Cards[target.Id] = target;
            config.Player.Items.Add(new LoadoutItem { CardId = self.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = target.Id, Slot = 1 });
            config.Enemy.Items.Add(new LoadoutItem { CardId = enemy.Id, Slot = 0 });
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { Skill(Template("rateLimiter"), "Diamond") }, null, Side.Player);
            Assert.Equal(0, mapped.Warnings.Count);
            Assert.Equal("simulated", mapped.Skills[0].Status);

            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem selfItem = Item(ctx, "self"), enemyItem = Item(ctx, "enemy"), targetItem = Item(ctx, "target");
            Assert.Near(2000, ItemAttributes.Get(selfItem, "ChargeAmount"));
            Assert.Near(1000, ItemAttributes.Get(enemyItem, "ChargeAmount"));
            for (int i = 0; i < 8; i++) NativeDynamicAuras.Refresh(ctx);
            Assert.Near(2000, ItemAttributes.Get(selfItem, "ChargeAmount"));
            Assert.Near(1000, ItemAttributes.Get(enemyItem, "ChargeAmount"));

            // 实际充能动作读取被光环倍率后的 ChargeAmount：2 秒，而不是 4 秒。
            Effects.ResolveEffect(ctx, new Effect
            {
                Kind = "charge",
                DurationValue = new NumExpr { Type = "cardAttribute", Attribute = "ChargeAmount" },
                Target = new Target { Kind = "selfItem", ItemId = "target" }
            }, selfItem, 1, 0, null);
            Assert.Near(8, targetItem.Cooldown);
        }

        private static void ForceFieldAddsCurrentShieldWithoutCompounding()
        {
            JsonValue template = Template("forceField");
            JsonValue calibration = _calibration["forceField"];
            Dictionary<string, int> attrs = MonsterTemplateExtractor.TierAttributes(template, "Gold");
            attrs["DamageAmount"] = (int)calibration["samples"][0]["damageAmount"].AsDouble();
            BattleConfig config = MappedCardBoard(template, "Gold", attrs, true, 20);
            config.PlayerStartShield = (int)calibration["samples"][0]["shield"].AsDouble();
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = ctx.Items[0];
            // 实时快照 553 已包含 +542 护盾；反推一次后应仍是 553。
            Assert.Near(11, item.AuraBaseAttributes["DamageAmount"]);
            Assert.Near(553, ItemAttributes.Get(item, "DamageAmount"));
            for (int i = 0; i < 8; i++) NativeDynamicAuras.Refresh(ctx);
            Assert.Near(553, ItemAttributes.Get(item, "DamageAmount"));

            ctx.Player.Shield = (int)calibration["samples"][1]["shield"].AsDouble();
            NativeDynamicAuras.Refresh(ctx);
            Assert.Near(425, ItemAttributes.Get(item, "DamageAmount"));
            for (int i = 0; i < 8; i++) NativeDynamicAuras.Refresh(ctx);
            Assert.Near(425, ItemAttributes.Get(item, "DamageAmount"));

            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);
            BattleEvent activate = result.Events.Find(delegate(BattleEvent ev) { return ev.Kind == "activate" && ev.SourceCardId == item.CardId; });
            Assert.True(activate != null);
            // Gold 模板 5 秒冷却；原生帧在 5.0 出手。
            Assert.Near(5, activate.Time);
            BattleEvent damage = result.Events.Find(delegate(BattleEvent ev) { return ev.Kind == "damage" && ev.SourceCardId == item.CardId && Math.Abs(ev.Time - activate.Time) < 1e-9; });
            Assert.True(damage != null);
            // 先加 60 护盾，再按当前 542+60+11 造成伤害。
            Assert.Near(613, damage.Value.Value);
        }

        private static void SpikyShieldLiveSnapshotIsNotCountedTwice()
        {
            JsonValue template = Template("spikyShield");
            JsonValue calibration = _calibration["spikyShield"];
            Dictionary<string, int> attrs = MonsterTemplateExtractor.TierAttributes(template, "Silver");
            attrs["CooldownMax"] = (int)calibration["opening"]["cooldownMax"].AsDouble();
            attrs["PercentCooldownReduction"] = (int)calibration["opening"]["percentCooldownReduction"].AsDouble();
            attrs["DamageAmount"] = (int)calibration["cast"]["damageAmount"].AsDouble();
            attrs["ShieldApplyAmount"] = 79; // 回放 4.55s 的实时值；Silver 模板基础值为 40。
            BattleConfig config = MappedCardBoard(template, "Silver", attrs, true, 20, 10000, 10000);
            config.PlayerStartShield = (int)calibration["cast"]["shield"].AsDouble();
            LiveSkillEntry cooldown = CooldownSkill();
            cooldown.Template = Json.Parse("{\"$type\":\"TCardSkill\",\"InternalName\":\"G05 cooldown 5\",\"Tiers\":{\"Bronze\":{\"Attributes\":{},\"AbilityIds\":[],\"AuraIds\":[\"0\"],\"TooltipIds\":[]}},\"Abilities\":{},\"Auras\":{\"0\":{\"ActiveIn\":\"HandOnly\",\"WorksIn\":\"Anywhere\",\"Prerequisites\":null,\"Action\":" +
                "{\"$type\":\"TAuraActionCardModifyAttribute\",\"AttributeType\":\"PercentCooldownReduction\",\"Operation\":\"Add\",\"Value\":{\"$type\":\"TFixedValue\",\"Value\":5},\"Target\":{\"$type\":\"TTargetCardSection\",\"TargetSection\":\"SelfHand\",\"ExcludeSelf\":false,\"Conditions\":null}}}}}");
            SkillMappingResult cooldownMapped = SkillMapper.Apply(config, new[] { cooldown }, null, Side.Player);
            Assert.Equal(0, cooldownMapped.Warnings.Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = ctx.Items[0];
            // 5699 = 6000 × (1 - 5%)；不能先按 5699 再扣一次 5%。
            Assert.Near(5.699, item.CooldownMax);
            Assert.Near(5.699, item.Cooldown);
            Assert.Near(5, item.AuraCooldownPercent);
            Assert.Near(922, ItemAttributes.Get(item, "DamageAmount"));

            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);
            BattleEvent activate = result.Events.Find(delegate(BattleEvent ev) { return ev.Kind == "activate" && ev.SourceCardId == item.CardId; });
            Assert.True(activate != null);
            // 5.699s 的有效冷却在原生 50ms 帧下首次出手于 5.7s。
            Assert.Near(5.7, activate.Time);
            BattleEvent damage = result.Events.Find(delegate(BattleEvent ev) { return ev.Kind == "damage" && ev.SourceCardId == item.CardId && Math.Abs(ev.Time - activate.Time) < 1e-9; });
            Assert.True(damage != null);
            Assert.Near(1001, damage.Value.Value);
        }

        private static void DynamicHeroAuraTogglesWithoutStacking()
        {
            JsonValue template = Template("forestCloak");
            Dictionary<string, int> attrs = MonsterTemplateExtractor.TierAttributes(template, "Silver");
            BattleConfig config = MappedCardBoard(template, "Silver", attrs, false, 20, 10000, 10000);
            // 无条件的 HealthMax AdditiveMultiply 20% 在开场静态层结算。
            Assert.Near(12000, config.PlayerMaxHp);
            BattleContext ctx = BattleEngine.InitBattle(config);
            Assert.Near(12000, ctx.Player.MaxHp);
            Assert.Near(0, ctx.Player.AuraPercentDamageReduction);

            for (int i = 0; i < 4; i++)
            {
                Effects.ResolveEffect(ctx, new Effect { Kind = "rage", Amount = new NumExpr { Type = "const", Value = 100 } },
                    ctx.Items[0], 1 + i, 0, null);
                Assert.Near(50, ctx.Player.AuraPercentDamageReduction);
                Assert.Near(12000, ctx.Player.MaxHp);
                ctx.Player.Hp = 12000;
                Effects.ApplyDamage(ctx, new DamageSource { Id = "probe", Side = Side.Enemy }, Side.Player, 100, 1.5 + i, new DamageOptions());
                Assert.Near(11950, ctx.Player.Hp);
                ctx.Player.EnragedUntil = -1;
                NativeDynamicAuras.Refresh(ctx);
                Assert.Near(0, ctx.Player.AuraPercentDamageReduction);
                ctx.Player.Hp = 12000;
                Effects.ApplyDamage(ctx, new DamageSource { Id = "probe", Side = Side.Enemy }, Side.Player, 100, 1.75 + i, new DamageOptions());
                Assert.Near(11900, ctx.Player.Hp);
            }
        }

        private static void DynamicHeroHealthMaxTogglesWithoutStacking()
        {
            const string prerequisite = "[{\"$type\":\"TPrerequisitePlayer\",\"Subject\":{\"$type\":\"TTargetPlayerRelative\",\"TargetMode\":\"Self\",\"Conditions\":{" +
                "\"$type\":\"TPlayerConditionalAttribute\",\"Attribute\":\"Enraged\",\"ComparisonOperator\":\"GreaterThan\"," +
                "\"ComparisonValue\":{\"$type\":\"TFixedValue\",\"Value\":0}}}}]";
            JsonValue template = Json.Parse("{\"$type\":\"TCardSkill\",\"InternalName\":\"G05 max hp\",\"Tiers\":{\"Bronze\":{\"Attributes\":{},\"AbilityIds\":[],\"AuraIds\":[\"0\"],\"TooltipIds\":[]}},\"Abilities\":{},\"Auras\":{\"0\":{\"ActiveIn\":\"HandOnly\",\"WorksIn\":\"Anywhere\",\"Prerequisites\":" +
                prerequisite + ",\"Action\":{\"$type\":\"TAuraActionPlayerModifyAttribute\",\"AttributeType\":\"HealthMax\",\"Operation\":\"Add\",\"Value\":{\"$type\":\"TFixedValue\",\"Value\":1000},\"Target\":{\"$type\":\"TTargetPlayerRelative\",\"TargetMode\":\"Self\",\"Conditions\":null}}}}}");
            BattleConfig config = EmptyBoard(1000, 1000, 20);
            CardDef dummy = Card("max-hp-dummy", 0, null, null, new Dictionary<string, int> { { "CooldownMax", 0 } });
            config.Cards[dummy.Id] = dummy;
            config.Player.Items.Add(new LoadoutItem { CardId = dummy.Id, Slot = 0 });
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { new LiveSkillEntry { TemplateId = "g05-max-hp", Template = template, Tier = "Bronze" } }, null, Side.Player);
            Assert.Equal(0, mapped.Warnings.Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            Assert.Near(1000, ctx.Player.MaxHp);
            for (int i = 0; i < 4; i++)
            {
                Effects.ResolveEffect(ctx, new Effect { Kind = "rage", Amount = new NumExpr { Type = "const", Value = 100 } },
                    ctx.Items[0], 1 + i, 0, null);
                Assert.Near(2000, ctx.Player.MaxHp);
                Assert.Near(2000, ctx.Player.Hp);
                ctx.Player.EnragedUntil = -1;
                NativeDynamicAuras.Refresh(ctx);
                Assert.Near(1000, ctx.Player.MaxHp);
                Assert.Near(1000, ctx.Player.Hp);
            }
        }

        private static void CooldownAuraChangesActualActivationTime()
        {
            BattleConfig config = EmptyBoard(100, 1000, 5);
            config.PlayerStartHp = 40; // 低于 50%，光环开场生效。
            CardDef card = Card("cdr-card", 4, null, null,
                new Dictionary<string, int> { { "CooldownMax", 4000 }, { "DamageAmount", 1 } });
            card.Effects.Add(new Effect { Kind = "damage", Amount = new NumExpr { Type = "cardAttribute", Attribute = "DamageAmount" }, Target = new Target { Kind = "enemyHero" } });
            config.Cards[card.Id] = card;
            config.Player.Items.Add(new LoadoutItem { CardId = card.Id, Slot = 0 });
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { CooldownSkill() }, null, Side.Player);
            Assert.Equal(0, mapped.Warnings.Count);
            Assert.Equal(1, config.PlayerSkills.Count);
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);
            List<double> activations = new List<double>();
            foreach (BattleEvent ev in result.Events) if (ev.Kind == "activate" && ev.SourceCardId == card.Id) activations.Add(ev.Time);
            Assert.Equal(2, activations.Count);
            // 4 秒基础冷却 × 50% = 2 秒；不是只改面板，实际出手时间同步。
            Assert.Near(2, activations[0]);
            Assert.Near(4, activations[1]);

            // 同一张卡在开关之间反复切换，冷却上限与剩余进度都必须回原值。
            BattleConfig toggle = EmptyBoard(100, 1000, 20);
            toggle.Cards[card.Id] = card;
            toggle.Player.Items.Add(new LoadoutItem { CardId = card.Id, Slot = 0 });
            SkillMappingResult toggleMapped = SkillMapper.Apply(toggle, new[] { CooldownSkill() }, null, Side.Player);
            Assert.Equal(0, toggleMapped.Warnings.Count);
            BattleContext ctx = BattleEngine.InitBattle(toggle);
            InternalItem item = ctx.Items[0];
            Assert.Near(4, item.CooldownMax);
            item.Cooldown = 3;
            for (int i = 0; i < 6; i++)
            {
                ctx.Player.Hp = 40;
                NativeDynamicAuras.Refresh(ctx);
                Assert.Near(2, item.CooldownMax);
                Assert.Near(1, item.Cooldown);
                ctx.Player.Hp = 100;
                NativeDynamicAuras.Refresh(ctx);
                Assert.Near(4, item.CooldownMax);
                Assert.Near(3, item.Cooldown);
            }
        }

        private static void Change(BattleContext ctx, InternalItem item, string attribute, string operation, double value)
        {
            Effects.ResolveEffect(ctx, new Effect
            {
                Kind = "modifyAttribute", Attribute = attribute, Operation = operation,
                Amount = new NumExpr { Type = "const", Value = value }, Target = new Target { Kind = "selfItem" }
            }, item, 1, 0, null);
        }
    }
}
