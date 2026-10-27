using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // 阶段 2b/3/4/5：运行时数值、人物属性与怒气、属性变化触发、卡牌生命周期、标签。
    // 模板均为手写原生片段，只验证映射与结算语义。
    public static class NativeRuntimeRulesTests
    {
        private static JsonValue G04Fixtures;
        private static JsonValue G04RelatedFixtures;
        private static JsonValue G04AllFixtures;

        private const string StreamingSetupId = "2a0624d0-8257-42f9-8e9d-bbe0a5218b44";
        private const string SpacescraperId = "40d8b44a-8e2f-4081-8163-043d76ddbafa";
        private const string GeyserId = "77df36bf-a1d0-4815-9f07-5c330542e76a";
        private const string PauldronsId = "8666a1de-066f-47c4-9d9b-87eea7d9f25a";

        public static void Run()
        {
            G04Fixtures = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_g04_cards.json"));
            G04RelatedFixtures = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_g04_related.json"));
            G04AllFixtures = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_g04_all.json"));
            RuntimeCardAttributeDrivesDamage();
            PlayerAttributeAndHalfMaxHp();
            TriggerSourceConditionSkipsSource();
            TriggerTargetFollowsAffectedCard();
            PlayerHealthLossAndCondition();
            CardAttributeChangeAndRage();
            RageEnrageAndEndTrigger();
            CounterAttributeGatesNextCast();
            TimedAttributeExpires();
            DestroyDisableRepairFlow();
            ForceUseAndFlyingToggle();
            AddedTagsReachConditions();
            PlayerDiedCanReviveOnce();
            AttributeChangeValueFollowsTriggerAmount();
            UnsupportedRulesFailClosed();
            PlayerConditionAuraTogglesWithHealth();
            SkillAuraUsesOwnAttributes();
            CooldownReductionAuraRescalesTimeline();
            LiveCooldownReductionIsNotCountedTwice();
            StreamingSetupReadsPerTargetSellPrice();
            StreamingSetupLiveTagAndTimedRollback();
            SpacescraperAggregatesLiveSellPrice();
            GeyserGrowthRestoresAfterNestedTrigger();
            PauldronsEnrageMultiplierRollsBack();
            UnscaledReferenceKeepsOpeningBase();
            ValueReferenceFallbacksAndRounding();
            CounterPrerequisiteGatesCast();
            SeafaringAuraTagsOnlySmallItems();
            AncientTechnologyAuraTagsRelicsOnly();
            CauldronAuraTagsReachTagCount();
            SkyscraperAggregatesLiveSellPrice();
            VatOfAcidLiveTagsScaleBurn();
            AtlasStoneUnscaledGrowthRepeatsBase();
            BurstOfFlameFirstHalfHealthLossOnly();
            PygGymReadsPlayerCustom4();
            OrangeJulianReadsPlayerCustom3();
            BuildingCrescendoMissingAuraStillUsesAbility();
            UnsupportedPlayerAttributeValueFailsClosed();
            RangeValueStaysWithinBounds();
            AllG04PrimaryTemplatesMap();
            AllG04PrimaryTemplatesSmokeRuntime();
        }

        private const string DefaultAttrs = "{\"CooldownMax\":1000,\"DamageAmount\":10,\"ShieldApplyAmount\":5,\"HealAmount\":5,\"RegenApplyAmount\":2,\"RageApplyAmount\":100,\"ChargeAmount\":1000,\"Custom_0\":0}";

        private const string Fired = "{\"$type\":\"TTriggerOnCardFired\"}";
        private const string OnUseSelf = "{\"$type\":\"TTriggerOnItemUsed\",\"Subject\":{\"$type\":\"TTargetCardSection\",\"TargetSection\":\"SelfHand\",\"ExcludeSelf\":false,\"Conditions\":null}}";
        private const string OnCombatStart = "{\"$type\":\"TTriggerOnFightStarted\",\"CombatType\":null}";

        private static string Ability(string trigger, string action, string prerequisites = "null", string active = "HandOnly", string works = "Anywhere")
        {
            return "{\"Trigger\":" + trigger + ",\"Action\":" + action + ",\"ActiveIn\":\"" + active +
                "\",\"WorksIn\":\"" + works + "\",\"Prerequisites\":" + prerequisites + "}";
        }

        private static string SelfTarget(string extra = "")
        {
            return "{\"$type\":\"TTargetCardSelf\",\"Conditions\":null" + extra + "}";
        }

        private static string EnemyHeroTarget()
        {
            return "{\"$type\":\"TTargetPlayerRelative\",\"TargetMode\":\"Opponent\",\"Conditions\":null}";
        }

        private static string SelfHeroTarget()
        {
            return "{\"$type\":\"TTargetPlayerRelative\",\"TargetMode\":\"Self\",\"Conditions\":null}";
        }

        private static string DamageAction(string reference = "null")
        {
            return "{\"$type\":\"TActionPlayerDamage\",\"ReferenceValue\":" + reference + ",\"Target\":" + EnemyHeroTarget() + ",\"Cost\":null}";
        }

        private static string ShieldAction(string reference = "null")
        {
            return "{\"$type\":\"TActionPlayerShieldApply\",\"ReferenceValue\":" + reference + ",\"Target\":" + SelfHeroTarget() + ",\"Cost\":null}";
        }

        private static string HealAction(string reference = "null")
        {
            return "{\"$type\":\"TActionPlayerHeal\",\"ReferenceValue\":" + reference + ",\"Target\":" + SelfHeroTarget() + ",\"Cost\":null}";
        }

        private static string Fixed(double value)
        {
            return "{\"$type\":\"TFixedValue\",\"Value\":" + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
        }

        private static string ModifyAttribute(string attribute, string operation, string value, string target, string duration = "null")
        {
            return "{\"$type\":\"TActionCardModifyAttribute\",\"AttributeType\":\"" + attribute + "\",\"Operation\":\"" + operation +
                "\",\"Value\":" + value + ",\"Duration\":" + duration + ",\"TargetCount\":null,\"Target\":" + target + "}";
        }

        private static LiveCardEntry Entry(string id, int slot, string[] abilities, string[] auras = null, string tags = "[]", string attrs = null, string hiddenTags = "[]", string size = "Small")
        {
            StringBuilder abilityJson = new StringBuilder("{");
            StringBuilder abilityIds = new StringBuilder("[");
            for (int i = 0; i < (abilities == null ? 0 : abilities.Length); i++)
            {
                if (i > 0) { abilityJson.Append(","); abilityIds.Append(","); }
                abilityJson.Append("\"").Append(i).Append("\":").Append(abilities[i]);
                abilityIds.Append("\"").Append(i).Append("\"");
            }
            abilityJson.Append("}");
            abilityIds.Append("]");
            StringBuilder auraJson = new StringBuilder("{");
            StringBuilder auraIds = new StringBuilder("[");
            for (int i = 0; i < (auras == null ? 0 : auras.Length); i++)
            {
                if (i > 0) { auraJson.Append(","); auraIds.Append(","); }
                auraJson.Append("\"").Append(i).Append("\":").Append(auras[i]);
                auraIds.Append("\"").Append(i).Append("\"");
            }
            auraJson.Append("}");
            auraIds.Append("]");
            string json = "{\"$type\":\"TCardItem\",\"Size\":\"" + size + "\",\"Tags\":" + tags + ",\"HiddenTags\":" + hiddenTags + ",\"Tiers\":{\"Bronze\":{\"Attributes\":" +
                (attrs ?? DefaultAttrs) + ",\"AbilityIds\":" + abilityIds + ",\"AuraIds\":" + auraIds +
                ",\"TooltipIds\":[]}},\"Abilities\":" + abilityJson + ",\"Auras\":" + auraJson + "}";
            JsonValue template = Json.Parse(json);
            return new LiveCardEntry
            {
                TemplateId = id,
                Tier = "Bronze",
                Socket = slot,
                Template = template,
                Size = size,
                AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, "Bronze")
            };
        }

        private static BattleConfig EnemyBoard(params LiveCardEntry[] entries)
        {
            BattleConfig config = CardMapper.Build(new List<LiveCardEntry>(entries),
                CardCatalog.FromCards(new List<CardDef>()), 1000, 1000, 20).Config;
            config.NativeMechanics = true;
            config.Enemy = config.Player;
            config.Player = new Loadout();
            return config;
        }

        private static BattleConfig PlayerBoard(params LiveCardEntry[] entries)
        {
            BattleConfig config = CardMapper.Build(new List<LiveCardEntry>(entries),
                CardCatalog.FromCards(new List<CardDef>()), 1000, 1000, 20).Config;
            config.NativeMechanics = true;
            return config;
        }

        private static InternalItem Item(BattleContext ctx, string cardId)
        {
            return ctx.Items.Find(delegate(InternalItem item) { return item.CardId == cardId || item.Card.BaseItemId == cardId; });
        }

        private static LiveCardEntry G04Real(string name, int slot, string tier = "Gold")
        {
            JsonValue row = G04Fixtures[name], template = row["template"];
            return new LiveCardEntry
            {
                TemplateId = row["id"].AsString(),
                Template = template,
                Tier = tier,
                Socket = slot,
                Size = template["Size"].AsString(),
                AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, tier)
            };
        }

        private static LiveCardEntry G04RelatedReal(string name, int slot, string tier = "Gold")
        {
            JsonValue row = G04RelatedFixtures[name], template = row["template"];
            return new LiveCardEntry
            {
                TemplateId = row["id"].AsString(),
                Template = template,
                Tier = tier,
                Socket = slot,
                Size = template["Size"].AsString(),
                AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, tier)
            };
        }

        private static LiveSkillEntry G04RelatedSkill(string name, string tier)
        {
            JsonValue row = G04RelatedFixtures[name], template = row["template"];
            return new LiveSkillEntry { TemplateId = row["id"].AsString(), Tier = tier, Template = template, Name = name };
        }

        private static InternalItem SkillItem(BattleContext ctx, string cardId)
        {
            return ctx.Skills.Find(delegate(InternalItem item) { return item.CardId == cardId || item.Card.BaseItemId == cardId; });
        }

        private static double LastDamage(BattleContext ctx, InternalItem source)
        {
            BattleEvent hit = null;
            foreach (BattleEvent ev in ctx.Events)
                if (ev.Kind == "damage" && ev.SourceItemId == source.Id) hit = ev;
            Assert.True(hit != null);
            return hit.Value.Value;
        }

        private static string SelfReference(string type, string attribute, double fallback, string modifier = "null")
        {
            return "{\"$type\":\"" + type + "\",\"AttributeType\":\"" + attribute + "\",\"Target\":" + SelfTarget() +
                ",\"DefaultValue\":" + fallback.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"Modifier\":" + modifier + "}";
        }

        private static string RageGainTrigger()
        {
            return "{\"$type\":\"TTriggerOnPlayerAttributeChanged\",\"Subject\":" + SelfHeroTarget() +
                ",\"AttributeType\":\"Rage\",\"ChangeType\":\"Gain\",\"Source\":null}";
        }

        private static void RuntimeCardAttributeDrivesDamage()
        {
            LiveCardEntry grow = Entry("grow", 0, new[]
            {
                Ability(Fired, DamageAction()),
                Ability(Fired, ModifyAttribute("DamageAmount", "Add", Fixed(5), SelfTarget()))
            });
            BattleConfig config = PlayerBoard(grow);
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleResult result = BattleEngine.Simulate(config);
            List<BattleEvent> hits = result.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "damage"; });
            // 同一张卡先结算伤害再 +5，所以每击递增 5。
            Assert.Near(10, hits[0].Value.Value);
            Assert.Near(15, hits[1].Value.Value);
            Assert.Near(10 + 5 * (hits.Count - 1), hits[hits.Count - 1].Value.Value);
        }

        private static void PlayerAttributeAndHalfMaxHp()
        {
            string half = "{\"$type\":\"TReferenceValuePlayerAttribute\",\"AttributeType\":\"HealthMax\",\"Target\":" + SelfHeroTarget() +
                ",\"Modifier\":{\"ModifyMode\":\"Multiply\",\"Value\":" + Fixed(0.5) + ",\"ShouldRound\":true}}";
            LiveCardEntry hitter = Entry("hitter", 0, new[] { Ability(Fired, DamageAction(half)) });
            BattleConfig config = PlayerBoard(hitter);
            config.PlayerMaxHp = 1000;
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleResult result = BattleEngine.Simulate(config);
            BattleEvent hit = result.Events.Find(delegate(BattleEvent ev) { return ev.Kind == "damage"; });
            Assert.Near(500, hit.Value.Value);
        }

        private static void TriggerSourceConditionSkipsSource()
        {
            string toy = "[\"Toy\"]";
            string condition = "{\"$type\":\"TCardConditionalAnd\",\"Conditions\":[{\"$type\":\"TCardConditionalTag\",\"Tags\":[\"Toy\"],\"Operator\":\"Any\"}," +
                "{\"$type\":\"TCardConditionalTriggerSource\",\"IsNot\":true}]}";
            string charge = "{\"$type\":\"TActionCardCharge\",\"Value\":null,\"TargetCount\":null,\"Target\":{\"$type\":\"TTargetCardRandom\",\"TargetSection\":\"SelfHand\",\"ExcludeSelf\":false,\"Conditions\":" + condition + "}}";
            LiveCardEntry board = Entry("board", 0, new[] { Ability(OnUseSelf, charge) });
            LiveCardEntry first = Entry("first", 1, new string[0], null, toy);
            LiveCardEntry second = Entry("second", 2, new string[0], null, toy);
            BattleConfig config = PlayerBoard(board, first, second);
            List<string> warnings = NativeCardAbilityMapper.Apply(config);
            Assert.Equal(0, warnings.Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem source = Item(ctx, "first");
            source.Cooldown = 6;
            InternalItem other = Item(ctx, "second");
            other.Cooldown = 6;
            Triggers.Enqueue(ctx, new TriggerPayload { Event = "onUse", SourceSide = Side.Player, SourceItemId = source.Id });
            Triggers.Drain(ctx, 1);
            Assert.Near(6, source.Cooldown);
            Assert.Near(5, other.Cooldown);
        }

        private static void TriggerTargetFollowsAffectedCard()
        {
            string hasteSelf = "{\"$type\":\"TActionCardHaste\",\"Value\":" + Fixed(1000) + ",\"TargetCount\":null,\"Target\":" + SelfTarget() + "}";
            string flyTarget = "{\"$type\":\"TActionCardFlyingStart\",\"Duration\":null,\"TargetCount\":null,\"Target\":{\"$type\":\"TTargetCardTriggerTarget\",\"ExcludeSelf\":false,\"Conditions\":null}}";
            LiveCardEntry source = Entry("source", 0, new[] { Ability(Fired, hasteSelf) });
            LiveCardEntry hawk = Entry("hawk", 1, new[]
            {
                Ability("{\"$type\":\"TTriggerOnCardPerformedHaste\",\"Subject\":{\"$type\":\"TTargetCardSection\",\"TargetSection\":\"SelfBoard\",\"ExcludeSelf\":false,\"Conditions\":null}}", flyTarget)
            });
            BattleConfig config = PlayerBoard(source, hawk);
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem sourceItem = Item(ctx, "source");
            foreach (Effect effect in sourceItem.Card.Effects)
                Effects.ResolveEffect(ctx, effect, sourceItem, 1, 0, null);
            Triggers.Drain(ctx, 1);
            Assert.True(sourceItem.Flying);
        }

        private static void PlayerHealthLossAndCondition()
        {
            string lossTrigger = "{\"$type\":\"TTriggerOnPlayerAttributeChanged\",\"Subject\":" + SelfHeroTarget() +
                ",\"AttributeType\":\"Health\",\"ChangeType\":\"Loss\",\"Source\":null}";
            string belowHalf = "[{\"$type\":\"TPrerequisitePlayer\",\"Subject\":{\"$type\":\"TTargetPlayerRelative\",\"TargetMode\":\"Self\"," +
                "\"Conditions\":{\"$type\":\"TPlayerConditionalAttribute\",\"Attribute\":\"Health\",\"ComparisonOperator\":\"LessThan\"," +
                "\"ComparisonValue\":{\"$type\":\"TReferenceValuePlayerAttribute\",\"AttributeType\":\"HealthMax\",\"Target\":" + SelfHeroTarget() +
                ",\"Modifier\":{\"ModifyMode\":\"Multiply\",\"Value\":" + Fixed(0.5) + ",\"ShouldRound\":true}}}}}]";
            LiveCardEntry shell = Entry("shell", 0, new[]
            {
                Ability(lossTrigger, ShieldAction(Fixed(10))),
                Ability(lossTrigger, ShieldAction(Fixed(20)), belowHalf)
            });
            BattleConfig config = EnemyBoard(shell);
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            DamageSource source = new DamageSource { Id = "hit", CardId = "hit", Side = Side.Player };
            Effects.ApplyDamage(ctx, source, Side.Enemy, 100, 1, new DamageOptions());
            Triggers.Drain(ctx, 1);
            Assert.Near(10, ctx.Enemy.Shield);
            Effects.ApplyDamage(ctx, source, Side.Enemy, 600, 2, new DamageOptions());
            Triggers.Drain(ctx, 2);
            Assert.Near(30, ctx.Enemy.Shield);
        }

        private static void CardAttributeChangeAndRage()
        {
            string slowed = "{\"$type\":\"TTriggerOnCardAttributeChanged\",\"Subject\":" + SelfTarget() +
                ",\"AttributeChanged\":\"Slow\",\"ChangeType\":\"Gain\",\"Source\":null}";
            string rage = "{\"$type\":\"TActionPlayerRageApply\",\"ReferenceValue\":" + Fixed(40) + ",\"Target\":" + SelfHeroTarget() + "}";
            LiveCardEntry blanket = Entry("blanket", 0, new[] { Ability(slowed, rage) });
            BattleConfig config = PlayerBoard(blanket);
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = Item(ctx, "blanket");
            Effects.ResolveEffect(ctx, new Effect { Kind = "slow", Seconds = 2, Count = 1, Target = new Target { Kind = "selfItem" } }, item, 1, 0, null);
            Triggers.Drain(ctx, 1);
            Assert.Near(40, ctx.Player.Rage);
            Assert.Near(-1, ctx.Player.EnragedUntil);
        }

        private static void RageEnrageAndEndTrigger()
        {
            string gainRage = "{\"$type\":\"TActionPlayerRageApply\",\"ReferenceValue\":null,\"Target\":" + SelfHeroTarget() + "}";
            string enraged = "{\"$type\":\"TTriggerOnPlayerEnraged\",\"Subject\":" + SelfHeroTarget() + "}";
            string ended = "{\"$type\":\"TTriggerOnPlayerEnrageEnded\",\"Subject\":" + SelfHeroTarget() + "}";
            LiveCardEntry rager = Entry("rager", 0, new[] { Ability(OnCombatStart, gainRage) });
            LiveCardEntry onRage = Entry("on-rage", 1, new[] { Ability(enraged, ShieldAction(Fixed(7))) });
            LiveCardEntry onEnd = Entry("on-end", 2, new[] { Ability(ended, HealAction(Fixed(3))) });
            BattleConfig config = PlayerBoard(rager, onRage, onEnd);
            config.PlayerMaxHp = 1000;
            config.PlayerStartHp = 500;
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Near(7, result.Totals.Player.Shield);
            Assert.Near(3, result.Totals.Player.Heal);
            Assert.True(result.Events.Exists(delegate(BattleEvent ev) { return ev.Kind == "enrage"; }));
            Assert.True(result.Events.Exists(delegate(BattleEvent ev) { return ev.Kind == "enrageEnd"; }));
        }

        private static void CounterAttributeGatesNextCast()
        {
            string counter = "[{\"$type\":\"TPrerequisiteCardCount\",\"Subject\":" + SelfTarget() +
                ",\"Comparison\":\"GreaterThanOrEqual\",\"Amount\":1}]";
            LiveCardEntry cookies = Entry("cookies", 0, new[]
            {
                Ability(Fired, DamageAction(), counter),
                Ability(Fired, ModifyAttribute("Custom_0", "Add", Fixed(1), SelfTarget()))
            });
            BattleConfig config = PlayerBoard(cookies);
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleResult result = BattleEngine.Simulate(config);
            // 首击时计数为 0，不计伤害；后续每次 10 点。
            List<BattleEvent> hits = result.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "damage"; });
            Assert.True(hits.Count >= 10);
            Assert.Near(10, hits[0].Value.Value);
            Assert.True(result.EndTime > 5);
        }

        private static void TimedAttributeExpires()
        {
            BattleConfig config = PlayerBoard(Entry("dish", 0, new string[0]));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = Item(ctx, "dish");
            Effect effect = new Effect
            {
                Kind = "modifyAttribute",
                Attribute = "Heated",
                Operation = "Add",
                Amount = new NumExpr { Type = "const", Value = 1 },
                Seconds = 2,
                Count = 1,
                Target = new Target { Kind = "selfItem" }
            };
            Effects.ResolveEffect(ctx, effect, item, 0, 0, null);
            Assert.Near(1, ItemAttributes.Get(item, "Heated"));
            ItemAttributes.Expire(ctx, 1.9);
            Assert.Near(1, ItemAttributes.Get(item, "Heated"));
            ItemAttributes.Expire(ctx, 2.1);
            Assert.Near(0, ItemAttributes.Get(item, "Heated"));
        }

        private static void DestroyDisableRepairFlow()
        {
            string observer = "{\"$type\":\"TTriggerOnCardPerformedDestruction\",\"Subject\":{\"$type\":\"TTargetCardSection\",\"TargetSection\":\"SelfBoard\",\"ExcludeSelf\":false,\"Conditions\":null},\"Target\":null}";
            LiveCardEntry watcher = Entry("watcher", 0, new[] { Ability(observer, ShieldAction(Fixed(4))) });
            BattleConfig config = EnemyBoard(watcher, Entry("victim", 1, new string[0]), Entry("victim2", 2, new string[0]), Entry("destroyer", 9, new string[0]));
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem victim = Item(ctx, "victim");
            InternalItem victim2 = Item(ctx, "victim2");
            InternalItem watcherItem = Item(ctx, "watcher");
            // PerformedDestruction 的 Subject 是行动者，不是被摧毁的卡。
            InternalItem destroyerItem = Item(ctx, "destroyer");
            Effects.ResolveEffect(ctx, new Effect { Kind = "destroy", Count = 1, Target = new Target { Kind = "selfItem", ItemId = victim.Id } }, destroyerItem, 1, 0, null);
            Triggers.DrainNativeFrame(ctx, 1);
            Assert.True(victim.Destroyed);
            Assert.False(watcherItem.Destroyed);
            Assert.Near(4, ctx.Enemy.Shield);

            Effects.ResolveEffect(ctx, new Effect { Kind = "disable", Count = 1, Target = new Target { Kind = "selfItem", ItemId = victim2.Id } }, destroyerItem, 2, 0, null);
            Assert.True(victim2.Disabled);
            Triggers.DrainNativeFrame(ctx, 2);
            Effects.ResolveEffect(ctx, new Effect { Kind = "repair", Count = 1, Target = new Target { Kind = "selfItem", ItemId = victim2.Id } }, destroyerItem, 3, 0, null);
            Assert.False(victim2.Disabled);
        }

        private static void ForceUseAndFlyingToggle()
        {
            BattleConfig config = PlayerBoard(Entry("parachute", 0, new[] { Ability(OnUseSelf, DamageAction()) }));
            config.NativeMechanics = true;
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = Item(ctx, "parachute");
            int cooldownBefore = (int)item.Cooldown;
            Effects.ResolveEffect(ctx, new Effect { Kind = "forceUse", Count = 1, Target = new Target { Kind = "selfItem" } }, item, 1, 0, null);
            Assert.Near(10, ctx.Totals.Player.Damage);
            Assert.Equal(cooldownBefore, (int)item.Cooldown);

            Effects.ResolveEffect(ctx, new Effect { Kind = "flyToggle", Count = 1, Target = new Target { Kind = "selfItem" } }, item, 2, 0, null);
            Assert.True(item.Flying);
            Effects.ResolveEffect(ctx, new Effect { Kind = "flyToggle", Count = 1, Target = new Target { Kind = "selfItem" } }, item, 3, 0, null);
            Assert.False(item.Flying);
        }

        private static void AddedTagsReachConditions()
        {
            string addToy = "{\"$type\":\"TActionCardAddTagsList\",\"Tags\":[\"Toy\"],\"Target\":" + SelfTarget() + "}";
            string copyTags = "{\"$type\":\"TActionCardAddTagsBySource\",\"Source\":{\"$type\":\"TTargetCardTriggerSource\",\"ExcludeSelf\":true,\"Conditions\":null},\"Target\":" + SelfTarget() + "}";
            LiveCardEntry board = Entry("board", 0, new[]
            {
                Ability(OnUseSelf, addToy),
                Ability(OnUseSelf, copyTags)
            });
            LiveCardEntry potion = Entry("potion", 1, new string[0], null, "[\"Potion\"]");
            BattleConfig config = PlayerBoard(board, potion);
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem boardItem = Item(ctx, "board");
            InternalItem potionItem = Item(ctx, "potion");
            Triggers.Enqueue(ctx, new TriggerPayload { Event = "onUse", SourceSide = Side.Player, SourceItemId = potionItem.Id });
            Triggers.Drain(ctx, 1);
            Assert.True(ItemAttributes.HasTag(boardItem, "Toy"));
            Assert.True(ItemAttributes.HasTag(boardItem, "Potion"));
        }

        private static void PlayerDiedCanReviveOnce()
        {
            string died = "{\"$type\":\"TTriggerOnPlayerDied\",\"Subject\":" + SelfHeroTarget() + "}";
            string revive = "{\"$type\":\"TActionPlayerReviveHeal\",\"ReferenceValue\":" + Fixed(30) + ",\"Target\":" + SelfHeroTarget() + "}";
            LiveCardEntry rebirth = Entry("rebirth", 0, new[] { Ability(died, revive) });
            LiveCardEntry killer = Entry("killer", 1, new[] { Ability(OnCombatStart, DamageAction(Fixed(200))) });
            BattleConfig config = PlayerBoard(rebirth, killer);
            config.PlayerMaxHp = 100;
            config.PlayerStartHp = 100;
            config.EnemyMaxHp = 10000;
            config.MaxTime = 5;
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            // 把 killer（槽位 1）移到敌方阵营。
            LoadoutItem killerItem = config.Player.Items.Find(delegate(LoadoutItem item) { return item.Slot == 1; });
            if (killerItem != null) { config.Player.Items.Remove(killerItem); config.Enemy.Items.Add(killerItem); }
            BattleResult result = BattleEngine.Simulate(config);
            Assert.True(result.Events.Exists(delegate(BattleEvent ev) { return ev.Kind == "heal" && ev.Value.Value >= 30; }));
            Assert.Near(5, result.EndTime);
            Assert.False(result.EndedByDeath);
            BattleHealthSeries health = new BattleHealthSeries(result);
            Assert.Near(100, health.At(-1).Player.Hp);
            Assert.Near(30, health.At(0).Player.Hp);
            Assert.Near(30, health.At(result.EndTime).Player.Hp);
        }

        private static void AttributeChangeValueFollowsTriggerAmount()
        {
            // Geyser / Pauldrons 语义：被动数值等于本次触发携带的变化量。
            string change = "{\"$type\":\"TReferenceValueAttributeChange\",\"DefaultValue\":0,\"Modifier\":{\"ModifyMode\":\"Multiply\",\"Value\":" + Fixed(2) + ",\"ShouldRound\":true}}";
            string onRage = "{\"$type\":\"TTriggerOnPlayerAttributeChanged\",\"Subject\":" + SelfHeroTarget() +
                ",\"AttributeType\":\"Rage\",\"ChangeType\":\"Gain\",\"Source\":null}";
            LiveCardEntry card = Entry("charged", 0, new[] { Ability(onRage, ModifyAttribute("ShieldApplyAmount", "Add", change, SelfTarget())) },
                null, "[]", "{\"CooldownMax\":1000,\"ShieldApplyAmount\":5,\"RageApplyAmount\":10,\"Custom_0\":0}");
            BattleConfig config = PlayerBoard(card);
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = Item(ctx, "charged");
            Assert.Near(5, ItemAttributes.Get(item, "ShieldApplyAmount"));

            Rage(ctx, item, 10);
            Assert.Near(25, ItemAttributes.Get(item, "ShieldApplyAmount"));
            // 第二次触发量叠加到同一张卡上，而不是重算成固定值。
            Rage(ctx, item, 5);
            Assert.Near(35, ItemAttributes.Get(item, "ShieldApplyAmount"));

            // 没有触发来源（静态光环/主动能力）时回退到 DefaultValue。
            NumExpr standalone = new NumExpr { Type = "attributeChange", Value = 3 };
            Assert.Near(3, Effects.EvaluateNum(standalone, ctx, item, ctx.Player));
        }

        private static void Rage(BattleContext ctx, InternalItem item, double amount)
        {
            Effects.ResolveEffect(ctx, new Effect
            {
                Kind = "rage",
                Amount = new NumExpr { Type = "const", Value = amount },
                Target = new Target { Kind = "selfHero" }
            }, item, 1, 0, null);
            Triggers.DrainNativeFrame(ctx, 1);
        }

        // 技能光环的数值引用指向技能自身（Desperate Strike 类），
        // 技能不在 ctx.Items 里，不能走目标集求值。
        private static void SkillAuraUsesOwnAttributes()
        {
            string template = "{\"$type\":\"TCardSkill\",\"InternalName\":\"Focused\"," +
                "\"Tiers\":{\"Gold\":{\"Attributes\":{\"Custom_0\":40},\"AbilityIds\":[],\"AuraIds\":[\"0\"],\"TooltipIds\":[]}},\"Abilities\":{}," +
                "\"Auras\":{\"0\":{\"ActiveIn\":\"HandOnly\",\"WorksIn\":\"CombatOnly\",\"Prerequisites\":[{\"$type\":\"TPrerequisitePlayer\"," +
                "\"Subject\":{\"$type\":\"TTargetPlayerRelative\",\"TargetMode\":\"Self\",\"Conditions\":{\"$type\":\"TPlayerConditionalAttribute\"," +
                "\"Attribute\":\"Health\",\"ComparisonOperator\":\"LessThan\",\"ComparisonValue\":{\"$type\":\"TReferenceValuePlayerAttribute\"," +
                "\"AttributeType\":\"Health\",\"Target\":{\"$type\":\"TTargetPlayerRelative\",\"TargetMode\":\"Opponent\",\"Conditions\":null}," +
                "\"DefaultValue\":0,\"Modifier\":null}}}}],\"Action\":{\"$type\":\"TAuraActionCardModifyAttribute\",\"AttributeType\":\"CritChance\"," +
                "\"Operation\":\"Add\",\"Value\":{\"$type\":\"TReferenceValueCardAttribute\",\"AttributeType\":\"Custom_0\"," +
                "\"Target\":{\"$type\":\"TTargetCardSelf\",\"Conditions\":null},\"DefaultValue\":0,\"Modifier\":null}," +
                "\"Target\":{\"$type\":\"TTargetCardSection\",\"TargetSection\":\"SelfHand\",\"ExcludeSelf\":false," +
                "\"Conditions\":{\"$type\":\"TCardConditionalCanCrit\",\"IsNot\":false}}}}}}";
            LiveSkillEntry skill = new LiveSkillEntry { TemplateId = "focused", Tier = "Gold", Template = Json.Parse(template), Name = "Focused" };
            LiveCardEntry weapon = Entry("hit", 0, new[] { Ability(Fired, DamageAction()) }, null, "[]",
                "{\"CooldownMax\":1000,\"DamageAmount\":10,\"CritChance\":10}");
            BattleConfig config = PlayerBoard(weapon);
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { skill });
            Assert.Equal("simulated", mapped.Skills[0].Status);
            Assert.Equal(1, config.PlayerSkills.Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = Item(ctx, "hit");
            // 未满足“生命低于对手”时不加成。
            Assert.Near(0.1, item.CritBonus);

            Effects.ApplyDamage(ctx, new DamageSource { Id = "probe", Side = Side.Enemy }, Side.Player, 600, 1, new DamageOptions());
            NativeDynamicAuras.Refresh(ctx);
            // 技能自身 Custom_0=40，加上模板的 10。
            Assert.Near(50, ItemAttributes.Get(item, "CritChance"));
            Assert.Near(0.5, item.CritBonus);
        }

        // 持续冷却缩减：有效冷却 =（CooldownMax - 固定缩减）×（1 - 百分比）。
        private static string BelowMaxPrerequisite()
        {
            return "{\"$type\":\"TPrerequisitePlayer\",\"Subject\":{\"$type\":\"TTargetPlayerRelative\",\"TargetMode\":\"Self\",\"Conditions\":{" +
                "\"$type\":\"TPlayerConditionalAttribute\",\"Attribute\":\"Health\",\"ComparisonOperator\":\"LessThan\",\"ComparisonValue\":{" +
                "\"$type\":\"TReferenceValuePlayerAttribute\",\"AttributeType\":\"HealthMax\",\"Target\":" + SelfHeroTarget() +
                ",\"DefaultValue\":0,\"Modifier\":null}}}}";
        }

        private static string CooldownAura(string attribute, double value)
        {
            return "{\"$type\":\"TAuraActionCardModifyAttribute\",\"ActiveIn\":\"HandOnly\",\"WorksIn\":\"CombatOnly\",\"Prerequisites\":[" + BelowMaxPrerequisite() +
                "],\"Action\":{\"$type\":\"TAuraActionCardModifyAttribute\",\"AttributeType\":\"" + attribute + "\",\"Operation\":\"Add\",\"Value\":" +
                Fixed(value) + ",\"Target\":" + SelfTarget() + "}}";
        }

        private static void CooldownReductionAuraRescalesTimeline()
        {
            LiveCardEntry card = Entry("cooled", 0, new string[0],
                new[] { CooldownAura("PercentCooldownReduction", 50), CooldownAura("FlatCooldownReduction", 1000) },
                "[]", "{\"CooldownMax\":4000,\"DamageAmount\":10}");
            BattleConfig config = PlayerBoard(card);
            config.PlayerMaxHp = 100;
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = Item(ctx, "cooled");
            Assert.Near(4, item.CooldownMax);

            Effects.ApplyDamage(ctx, new DamageSource { Id = "probe", Side = Side.Enemy }, Side.Player, 60, 1, new DamageOptions());
            NativeDynamicAuras.Refresh(ctx);
            Assert.Near(1.5, item.CooldownMax);
            // 当前进度按上限差值同步收缩：4s 等待 - 2.5s 缩减 = 1.5s。
            Assert.Near(1.5, item.Cooldown);

            Effects.ApplyHeal(ctx, Side.Player, 60, 2, null, null, null, 0);
            NativeDynamicAuras.Refresh(ctx);
            Assert.Near(4, item.CooldownMax);
        }

        // 实时 CooldownMax 已含当时生效的缩减，开场先反推基础值。
        private static void LiveCooldownReductionIsNotCountedTwice()
        {
            LiveCardEntry entry = Entry("live", 0, new string[0], new[] { CooldownAura("PercentCooldownReduction", 50) }, "[]", "{\"CooldownMax\":1500,\"DamageAmount\":10}");
            entry.AttributesAreLive = true;
            BattleConfig config = PlayerBoard(entry);
            config.PlayerMaxHp = 100;
            config.PlayerStartHp = 50;
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = Item(ctx, "live");
            // 反推基础 3000，再应用 50% 缩减，回到实时值 1.5。
            Assert.Near(1.5, item.CooldownMax);
            ctx.Player.Hp = ctx.Player.MaxHp;
            NativeDynamicAuras.Refresh(ctx);
            Assert.Near(3, item.CooldownMax);
        }

        // G04：Streaming Setup 的数值引用 Target:null 指向当前光环作用卡，
        // 加价只作用于被使用的 Toy/Friend，不能冻结成开场时的单一售价。
        private static void StreamingSetupReadsPerTargetSellPrice()
        {
            LiveCardEntry sword = Entry("sword", 1, new string[0], null, "[\"Toy\",\"Weapon\"]",
                "{\"CooldownMax\":1000,\"DamageAmount\":10,\"ShieldApplyAmount\":5,\"HealAmount\":5,\"SellPrice\":7}",
                "[\"Shield\",\"Heal\"]");
            LiveCardEntry axe = Entry("axe", 2, new string[0], null, "[\"Toy\",\"Weapon\"]",
                "{\"CooldownMax\":1000,\"DamageAmount\":10,\"ShieldApplyAmount\":5,\"HealAmount\":5,\"SellPrice\":3}",
                "[\"Shield\",\"Heal\"]");
            LiveCardEntry plain = Entry("plain", 3, new string[0], null, "[\"Item\"]",
                "{\"CooldownMax\":1000,\"DamageAmount\":10,\"SellPrice\":50}");
            BattleConfig config = PlayerBoard(G04Real("Streaming Setup", 0), sword, axe, plain);
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem stream = Item(ctx, StreamingSetupId);
            InternalItem swordItem = Item(ctx, "sword"), axeItem = Item(ctx, "axe"), plainItem = Item(ctx, "plain");
            Assert.Near(17, ItemAttributes.Get(swordItem, "DamageAmount"));
            Assert.Near(12, ItemAttributes.Get(swordItem, "ShieldApplyAmount"));
            Assert.Near(12, ItemAttributes.Get(swordItem, "HealAmount"));
            Assert.Near(13, ItemAttributes.Get(axeItem, "DamageAmount"));
            Assert.Near(0, ItemAttributes.Get(stream, "DamageAmount"));

            // 非 Toy/Friend 不会触发加价。
            Triggers.Enqueue(ctx, new TriggerPayload { Event = "onUse", SourceSide = Side.Player, SourceItemId = plainItem.Id });
            Triggers.DrainNativeFrame(ctx, 1);
            Assert.Near(50, ItemAttributes.Get(plainItem, "SellPrice"));

            // 只提高本次被使用的 sword 的售价，其他武器与 Streaming Setup 自身不跟着变。
            Triggers.Enqueue(ctx, new TriggerPayload { Event = "onUse", SourceSide = Side.Player, SourceItemId = swordItem.Id });
            Triggers.DrainNativeFrame(ctx, 1);
            Assert.Near(11, ItemAttributes.Get(swordItem, "SellPrice"));
            Assert.Near(21, ItemAttributes.Get(swordItem, "DamageAmount"));
            Assert.Near(16, ItemAttributes.Get(swordItem, "ShieldApplyAmount"));
            Assert.Near(16, ItemAttributes.Get(swordItem, "HealAmount"));
            Assert.Near(13, ItemAttributes.Get(axeItem, "DamageAmount"));
            Assert.Near(0, ItemAttributes.Get(stream, "DamageAmount"));
        }

        // 标签实时参与光环目标匹配；时效属性到期后引用与光环值都必须回滚。
        private static void StreamingSetupLiveTagAndTimedRollback()
        {
            LiveCardEntry blade = Entry("blade", 1, new string[0], null, "[\"Toy\"]",
                "{\"CooldownMax\":1000,\"DamageAmount\":10,\"ShieldApplyAmount\":5,\"SellPrice\":7}",
                "[\"Shield\"]");
            BattleConfig config = PlayerBoard(G04Real("Streaming Setup", 0), blade);
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem bladeItem = Item(ctx, "blade");
            // 缺少 Weapon 标签时，伤害光环不匹配。
            Assert.Near(10, ItemAttributes.Get(bladeItem, "DamageAmount"));
            Assert.Near(12, ItemAttributes.Get(bladeItem, "ShieldApplyAmount"));

            ItemAttributes.AddTag(bladeItem, "Weapon");
            NativeDynamicAuras.Refresh(ctx);
            Assert.Near(17, ItemAttributes.Get(bladeItem, "DamageAmount"));

            ItemAttributes.AddTimed(bladeItem, "SellPrice", 4, 2);
            NativeDynamicAuras.Refresh(ctx);
            Assert.Near(21, ItemAttributes.Get(bladeItem, "DamageAmount"));
            ItemAttributes.Expire(ctx, 1.9);
            NativeDynamicAuras.Refresh(ctx);
            Assert.Near(21, ItemAttributes.Get(bladeItem, "DamageAmount"));
            ItemAttributes.Expire(ctx, 2.1);
            NativeDynamicAuras.Refresh(ctx);
            Assert.Near(7, ItemAttributes.Get(bladeItem, "SellPrice"));
            Assert.Near(17, ItemAttributes.Get(bladeItem, "DamageAmount"));
        }

        // G04：Spacescraper 的聚合引用按当前手牌售价求和，缺值按 DefaultValue 0 计。
        private static void SpacescraperAggregatesLiveSellPrice()
        {
            LiveCardEntry gem = Entry("gem", 1, new string[0], null, "[\"Item\"]",
                "{\"CooldownMax\":1000,\"SellPrice\":10}");
            LiveCardEntry empty = Entry("empty", 2, new string[0], null, "[\"Item\"]",
                "{\"CooldownMax\":1000}");
            LiveCardEntry zero = Entry("zero", 3, new string[0], null, "[\"Item\"]",
                "{\"CooldownMax\":1000,\"SellPrice\":0}");
            BattleConfig config = PlayerBoard(G04Real("Spacescraper", 0), gem, empty, zero);
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem spacescraper = Item(ctx, SpacescraperId);
            InternalItem gemItem = Item(ctx, "gem");
            // 2 × (10 + 0 + 0)；缺 SellPrice 的卡不能凭空贡献其他值。
            Assert.Near(20, ItemAttributes.Get(spacescraper, "ShieldApplyAmount"));

            ItemAttributes.AddDelta(gemItem, "SellPrice", 5);
            NativeDynamicAuras.Refresh(ctx);
            Assert.Near(30, ItemAttributes.Get(spacescraper, "ShieldApplyAmount"));

            BattleEngine.ForceUse(ctx, spacescraper, 2, 0);
            Assert.Near(30, ctx.Player.Shield);
        }

        // G04：怒气变化量在嵌套触发后仍按各自 payload 恢复，Geyser 累加两次真实变化量。
        private static void GeyserGrowthRestoresAfterNestedTrigger()
        {
            string comparator = "[{\"$type\":\"TPrerequisiteCardAttributeComparator\",\"Subject\":" + SelfTarget() +
                ",\"Attribute\":\"Custom_0\",\"AttributeOther\":\"Custom_1\",\"Comparison\":\"GreaterThan\"}]";
            string echoRage = "{\"$type\":\"TActionPlayerRageApply\",\"ReferenceValue\":" + Fixed(2) + ",\"Target\":" + SelfHeroTarget() + "}";
            LiveCardEntry echo = Entry("echo", 1, new[]
            {
                Ability(RageGainTrigger(), echoRage, comparator),
                Ability(RageGainTrigger(), ModifyAttribute("Custom_1", "Add", Fixed(1), SelfTarget()))
            }, null, "[]", "{\"CooldownMax\":1000,\"Custom_0\":1,\"Custom_1\":0}");
            BattleConfig config = PlayerBoard(G04Real("Geyser", 0), echo);
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem geyser = Item(ctx, GeyserId);
            Assert.Near(0, ItemAttributes.Get(geyser, "BurnApplyAmount"));

            Rage(ctx, geyser, 10);
            Assert.Near(10, ItemAttributes.Get(geyser, "BurnApplyAmount"));
            // echo 在同一 payload 内再造成 +2 怒气，下一帧结算；外层 10 不能被覆盖成 2。
            Triggers.DrainNativeFrame(ctx, 1);
            Assert.Near(12, ctx.Player.Rage);
            // The nested event keeps its +2 payload while the same Low
            // ability waits for its next native execution slot.
            Triggers.DrainNativeFrame(ctx, 1.25);
            Assert.Near(12, ItemAttributes.Get(geyser, "BurnApplyAmount"));

            Rage(ctx, geyser, 0);
            Assert.Near(12, ItemAttributes.Get(geyser, "BurnApplyAmount"));
            BattleEngine.ForceUse(ctx, geyser, 3, 0);
            Assert.Near(12, ctx.Enemy.Burn);
        }

        // G04：Pauldrons 的暴怒倍率光环是派生层；暴怒结束必须撤销，不能残留双倍护盾。
        private static void PauldronsEnrageMultiplierRollsBack()
        {
            LiveCardEntry mate = Entry("shieldmate", 1, new string[0], null, "[\"Friend\"]",
                "{\"CooldownMax\":1000,\"ShieldApplyAmount\":5}", "[\"Shield\"]");
            BattleConfig config = PlayerBoard(G04Real("Pauldrons", 0), mate);
            config.PlayerRageMax = 20;
            config.PlayerEnragedDurationMs = 5000;
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem pauldrons = Item(ctx, PauldronsId);
            InternalItem mateItem = Item(ctx, "shieldmate");
            Assert.Near(0, ItemAttributes.Get(pauldrons, "ShieldApplyAmount"));
            Assert.Near(5, ItemAttributes.Get(mateItem, "ShieldApplyAmount"));
            Assert.True(ctx.Player.EnragedUntil < 0);

            Rage(ctx, pauldrons, 10);
            Assert.Near(20, ItemAttributes.Get(pauldrons, "ShieldApplyAmount"));
            Assert.Near(5, ItemAttributes.Get(mateItem, "ShieldApplyAmount"));

            Rage(ctx, pauldrons, 10);
            // Repeated Low-priority ability executions are 250 ms apart.
            Triggers.DrainNativeFrame(ctx, 1.25);
            Assert.Near(80, ItemAttributes.Get(pauldrons, "ShieldApplyAmount"));
            Assert.Near(10, ItemAttributes.Get(mateItem, "ShieldApplyAmount"));
            Assert.True(ctx.Player.EnragedUntil > 0);

            ctx.Player.EnragedUntil = -1;
            NativeDynamicAuras.Refresh(ctx);
            Assert.Near(40, ItemAttributes.Get(pauldrons, "ShieldApplyAmount"));
            Assert.Near(5, ItemAttributes.Get(mateItem, "ShieldApplyAmount"));
        }

        // G04：Unscaled 读取入场基底，普通引用读取战斗内增量，两者不能等价。
        private static void UnscaledReferenceKeepsOpeningBase()
        {
            string normal = SelfReference("TReferenceValueCardAttribute", "DamageAmount", 0);
            string unscaled = SelfReference("TReferenceValueCardAttributeUnscaled", "DamageAmount", 0);
            LiveCardEntry scaled = Entry("scaled", 0, new[] { Ability(Fired, DamageAction(normal)) });
            LiveCardEntry opening = Entry("opening", 1, new[] { Ability(Fired, DamageAction(unscaled)) });
            BattleConfig config = PlayerBoard(scaled, opening);
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem scaledItem = Item(ctx, "scaled"), openingItem = Item(ctx, "opening");
            ItemAttributes.AddDelta(scaledItem, "DamageAmount", 5);
            ItemAttributes.AddDelta(openingItem, "DamageAmount", 5);
            Assert.Near(15, ItemAttributes.Get(scaledItem, "DamageAmount"));
            Assert.Near(10, ItemAttributes.Unscaled(openingItem, "DamageAmount", 0));

            BattleEngine.ForceUse(ctx, scaledItem, 1, 0);
            BattleEngine.ForceUse(ctx, openingItem, 2, 0);
            BattleEvent scaledHit = ctx.Events.Find(delegate(BattleEvent ev) { return ev.Kind == "damage" && ev.SourceItemId == scaledItem.Id; });
            BattleEvent openingHit = ctx.Events.Find(delegate(BattleEvent ev) { return ev.Kind == "damage" && ev.SourceItemId == openingItem.Id; });
            Assert.True(scaledHit != null && openingHit != null);
            Assert.Near(15, scaledHit.Value.Value);
            Assert.Near(10, openingHit.Value.Value);
        }

        // G04 独立交付：缺值回退 DefaultValue，负值/零值/比例与取整都走同一求值入口。
        private static void ValueReferenceFallbacksAndRounding()
        {
            LiveCardEntry values = Entry("values", 0, new string[0], null, "[]",
                "{\"CooldownMax\":1000,\"DamageAmount\":10,\"SellPrice\":3,\"Custom_0\":-3}");
            BattleConfig config = PlayerBoard(values);
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = Item(ctx, "values");
            Dictionary<string, double> attrs = new Dictionary<string, double>();

            JsonValue missing = Json.Parse(SelfReference("TReferenceValueCardAttribute", "Missing", 7));
            Assert.Near(7, Effects.EvaluateNum(SkillMapper.MapValue(missing, attrs, null), ctx, item, ctx.Player));

            JsonValue negative = Json.Parse(SelfReference("TReferenceValueCardAttribute", "Custom_0", 0));
            Assert.Near(-3, Effects.EvaluateNum(SkillMapper.MapValue(negative, attrs, null), ctx, item, ctx.Player));

            string zeroModifier = "{\"ModifyMode\":\"Multiply\",\"Value\":" + Fixed(0) + ",\"ShouldRound\":true}";
            JsonValue zero = Json.Parse(SelfReference("TReferenceValueCardAttribute", "SellPrice", 0, zeroModifier));
            Assert.Near(0, Effects.EvaluateNum(SkillMapper.MapValue(zero, attrs, null), ctx, item, ctx.Player));

            string ratioModifier = "{\"ModifyMode\":\"Multiply\",\"Value\":" + Fixed(1.5) + ",\"ShouldRound\":true}";
            JsonValue ratio = Json.Parse(SelfReference("TReferenceValueCardAttribute", "SellPrice", 0, ratioModifier));
            Assert.Near(5, Effects.EvaluateNum(SkillMapper.MapValue(ratio, attrs, null), ctx, item, ctx.Player));
        }

        // G04：Custom_N 计数器由修改动作成长，并作为属性比较前提决定下一次能力是否执行。
        private static void CounterPrerequisiteGatesCast()
        {
            string comparator = "[{\"$type\":\"TPrerequisiteCardAttributeComparator\",\"Subject\":" + SelfTarget() +
                ",\"Attribute\":\"Custom_0\",\"AttributeOther\":\"Custom_1\",\"Comparison\":\"GreaterThanOrEqual\"}]";
            LiveCardEntry counter = Entry("counter", 0, new[]
            {
                Ability(Fired, DamageAction(Fixed(7)), comparator),
                Ability(Fired, ModifyAttribute("Custom_0", "Add", Fixed(1), SelfTarget()))
            }, null, "[]", "{\"CooldownMax\":1000,\"DamageAmount\":10,\"Custom_0\":0,\"Custom_1\":1}");
            BattleConfig config = PlayerBoard(counter);
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = Item(ctx, "counter");

            BattleEngine.ForceUse(ctx, item, 1, 0);
            Assert.Near(0, ctx.Totals.Player.Damage);
            Assert.Near(1, ItemAttributes.Get(item, "Custom_0"));
            BattleEngine.ForceUse(ctx, item, 2, 0);
            Assert.Near(7, ctx.Totals.Player.Damage);
            Assert.Near(2, ItemAttributes.Get(item, "Custom_0"));
        }

        // G04 关联卡：Seafaring 的光环标签是静态光环动作，不能因 Operation 为空而整条拒绝。
        private static void SeafaringAuraTagsOnlySmallItems()
        {
            LiveSkillEntry skill = G04RelatedSkill("Seafaring", "Diamond");
            BattleConfig config = PlayerBoard(
                Entry("small", 0, new string[0], null, "[\"Item\"]", "{\"CooldownMax\":1000}", "[]", "Small"),
                Entry("medium", 1, new string[0], null, "[\"Item\"]", "{\"CooldownMax\":1000}", "[]", "Medium"));
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { skill });
            Assert.Equal("live-applied", mapped.Skills[0].Status);
            BattleContext ctx = BattleEngine.InitBattle(config);
            Assert.True(ItemAttributes.HasTag(Item(ctx, "small"), "Aquatic"));
            Assert.False(ItemAttributes.HasTag(Item(ctx, "medium"), "Aquatic"));
        }

        // G04 主责卡：Ancient Technology 只给满足 Relic 条件的卡加 Tech 标签。
        private static void AncientTechnologyAuraTagsRelicsOnly()
        {
            LiveSkillEntry skill = G04RelatedSkill("Ancient Technology (Skill)", "Diamond");
            BattleConfig config = PlayerBoard(
                Entry("relic", 0, new string[0], null, "[\"Relic\"]", "{\"CooldownMax\":1000}"),
                Entry("plain", 1, new string[0], null, "[\"Item\"]", "{\"CooldownMax\":1000}"));
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { skill });
            Assert.Equal("live-applied", mapped.Skills[0].Status);
            BattleContext ctx = BattleEngine.InitBattle(config);
            Assert.True(ItemAttributes.HasTag(Item(ctx, "relic"), "Tech"));
            Assert.False(ItemAttributes.HasTag(Item(ctx, "plain"), "Tech"));
        }

        // G04 关联卡：Cauldron 的 BySource 标签光环先复制标签，TagCount 光环再按
        // 当前手牌去重标签数成长；不能把 TagCount 固定成开场常量。
        private static void CauldronAuraTagsReachTagCount()
        {
            LiveCardEntry spice = Entry("spice", 1, new string[0], null, "[\"Food\",\"Potion\"]", "{\"CooldownMax\":1000}");
            LiveCardEntry weapon = Entry("weapon", 2, new string[0], null, "[\"Weapon\"]", "{\"CooldownMax\":1000}");
            BattleConfig config = PlayerBoard(G04RelatedReal("Cauldron", 0), spice, weapon);
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem cauldron = Item(ctx, G04RelatedFixtures["Cauldron"]["id"].AsString());
            Assert.True(ItemAttributes.HasTag(cauldron, "Food"));
            Assert.True(ItemAttributes.HasTag(cauldron, "Potion"));
            Assert.True(ItemAttributes.HasTag(cauldron, "Weapon"));
            // SelfHand 去重标签 Tool/Food/Potion/Weapon=4；Gold Custom_0=4。
            Assert.Near(16, ItemAttributes.Get(cauldron, "BurnApplyAmount"));
            Assert.Near(16, ItemAttributes.Get(cauldron, "PoisonApplyAmount"));

            ItemAttributes.AddTag(Item(ctx, "weapon"), "Fire");
            NativeDynamicAuras.Refresh(ctx);
            Assert.Near(20, ItemAttributes.Get(cauldron, "BurnApplyAmount"));
        }

        // G04 关联卡：Skyscraper 的跨卡聚合必须读取当前手牌售价，
        // 不是开场时算好的一次常量。
        private static void SkyscraperAggregatesLiveSellPrice()
        {
            LiveCardEntry gem = Entry("gem", 1, new string[0], null, "[\"Item\"]", "{\"CooldownMax\":1000,\"SellPrice\":10}");
            LiveCardEntry weapon = Entry("weapon", 2, new string[0], null, "[\"Weapon\"]", "{\"CooldownMax\":1000,\"SellPrice\":5}");
            BattleConfig config = PlayerBoard(G04RelatedReal("Skyscraper", 0), gem, weapon);
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem skyscraper = Item(ctx, G04RelatedFixtures["Skyscraper"]["id"].AsString());
            Assert.Near(30, ItemAttributes.Get(skyscraper, "DamageAmount"));

            ItemAttributes.AddDelta(Item(ctx, "gem"), "SellPrice", 7);
            NativeDynamicAuras.Refresh(ctx);
            Assert.Near(44, ItemAttributes.Get(skyscraper, "DamageAmount"));
            BattleEngine.ForceUse(ctx, skyscraper, 2, 0);
            Assert.Near(44, LastDamage(ctx, skyscraper));
        }

        // G04 关联卡：Vat of Acid 的 Distinct TagCount 读取自身实时标签。
        private static void VatOfAcidLiveTagsScaleBurn()
        {
            BattleConfig config = PlayerBoard(G04RelatedReal("Vat of Acid", 0));
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem vat = Item(ctx, G04RelatedFixtures["Vat of Acid"]["id"].AsString());
            // 自身没有可见标签；TagCount=0，Gold Custom_0=6。
            Assert.Near(0, ItemAttributes.Get(vat, "BurnApplyAmount"));
            ItemAttributes.AddTag(vat, "Food");
            ItemAttributes.AddTag(vat, "Potion");
            NativeDynamicAuras.Refresh(ctx);
            Assert.Near(12, ItemAttributes.Get(vat, "BurnApplyAmount"));
            ItemAttributes.AddTag(vat, "Food");
            NativeDynamicAuras.Refresh(ctx);
            Assert.Near(12, ItemAttributes.Get(vat, "BurnApplyAmount"));
            BattleEngine.ForceUse(ctx, vat, 2, 0);
            Assert.Near(12, ctx.Enemy.Burn);
            Assert.Near(12, ctx.Enemy.Poison);
        }

        // G04 关联卡：Unscaled 读取入场基底，成长后的运行时偏移不能反馈回自身。
        private static void AtlasStoneUnscaledGrowthRepeatsBase()
        {
            BattleConfig config = PlayerBoard(G04RelatedReal("Atlas Stone", 0, "Bronze"));
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem atlas = Item(ctx, G04RelatedFixtures["Atlas Stone"]["id"].AsString());
            BattleEngine.ForceUse(ctx, atlas, 1, 0);
            // Atlas Stone: High "double base damage" resolves before Medium damage,
            // so the first hit already includes the +20 growth (base 20 -> 40).
            Assert.Near(40, LastDamage(ctx, atlas));
            Assert.Near(40, ItemAttributes.Get(atlas, "DamageAmount"));
            BattleEngine.ForceUse(ctx, atlas, 2, 0);
            Assert.Near(60, LastDamage(ctx, atlas));
            Assert.Near(60, ItemAttributes.Get(atlas, "DamageAmount"));
        }

        // G04 主责卡：Burst of Flame 用人物 HealthMax 百分比作前提，
        // 并用 Custom_0 保证每次战斗只触发一次半血效果。
        private static void BurstOfFlameFirstHalfHealthLossOnly()
        {
            LiveSkillEntry skill = G04RelatedSkill("Burst of Flame", "Gold");
            BattleConfig config = PlayerBoard(Entry("probe", 0, new string[0]));
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { skill });
            Assert.Equal("simulated", mapped.Skills[0].Status);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem burst = SkillItem(ctx, G04RelatedFixtures["Burst of Flame"]["id"].AsString());
            Effects.ApplyDamage(ctx, new DamageSource { Id = "hit", Side = Side.Player }, Side.Enemy, 600, 1, new DamageOptions());
            Triggers.Drain(ctx, 1);
            Assert.Near(20, ctx.Enemy.Burn);
            Assert.Near(1, ItemAttributes.Get(burst, "Custom_0"));

            Effects.ApplyDamage(ctx, new DamageSource { Id = "hit", Side = Side.Player }, Side.Enemy, 100, 2, new DamageOptions());
            Triggers.Drain(ctx, 2);
            Assert.Near(20, ctx.Enemy.Burn);
            Assert.Near(2, ItemAttributes.Get(burst, "Custom_0"));
        }

        // G04 主责卡：Pyg's Gym 读取人物 Custom_4；回放中敌方 Custom_4=46、
        // Gold Custom_0=6，给武器 +276 Damage。
        private static void PygGymReadsPlayerCustom4()
        {
            LiveCardEntry sword = Entry("sword", 1, new string[0], null, "[\"Weapon\"]",
                "{\"CooldownMax\":1000,\"DamageAmount\":10}");
            BattleConfig config = PlayerBoard(G04RelatedReal("Pyg's Gym", 0), sword);
            config.PlayerAttributes["Custom_4"] = 46;
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem gym = Item(ctx, G04RelatedFixtures["Pyg's Gym"]["id"].AsString());
            BattleEngine.ForceUse(ctx, gym, 1, 0);
            Assert.Near(286, ItemAttributes.Get(Item(ctx, "sword"), "DamageAmount"));
        }

        // G04 主责卡：Orange Julian 读取人物 Custom_3；回放中 Custom_3=267、
        // Gold Custom_0=50，模板算术为 267 × 50 × 0.01 = 133.5 → 134。
        private static void OrangeJulianReadsPlayerCustom3()
        {
            LiveCardEntry sword = Entry("sword", 1, new string[0], null, "[\"Weapon\"]",
                "{\"CooldownMax\":1000,\"DamageAmount\":10}");
            BattleConfig config = PlayerBoard(G04RelatedReal("Orange Julian", 0, "Silver"), sword);
            config.PlayerAttributes["Custom_3"] = 267;
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem julian = Item(ctx, G04RelatedFixtures["Orange Julian"]["id"].AsString());
            BattleEngine.ForceUse(ctx, julian, 1, 0);
            Assert.Near(144, ItemAttributes.Get(Item(ctx, "sword"), "DamageAmount"));
        }

        // G04 数据缺口：Building Crescendo 的 AuraIds 引用了不存在的光环 1；
        // 能力 0 本身已完成“使用物品时给可暴击手牌加 Custom_0 CritChance”，
        // 不能因缺失的冗余光环把整张技能判为 unsupported。
        private static void BuildingCrescendoMissingAuraStillUsesAbility()
        {
            JsonValue row = G04AllFixtures["Building Crescendo"], template = row["template"];
            LiveSkillEntry skill = new LiveSkillEntry
            {
                TemplateId = row["id"].AsString(),
                Tier = "Diamond",
                Template = template,
                Name = "Building Crescendo"
            };
            LiveCardEntry first = Entry("first", 0, new string[0], null, "[\"Weapon\"]",
                "{\"CooldownMax\":1000,\"DamageAmount\":10,\"CritChance\":0}", "[\"CanCrit\"]");
            LiveCardEntry second = Entry("second", 1, new string[0], null, "[\"Weapon\"]",
                "{\"CooldownMax\":1000,\"DamageAmount\":10,\"CritChance\":0}", "[\"CanCrit\"]");
            BattleConfig config = PlayerBoard(first, second);
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { skill });
            Assert.Equal("simulated", mapped.Skills[0].Status);
            BattleContext ctx = BattleEngine.InitBattle(config);
            Triggers.Enqueue(ctx, new TriggerPayload { Event = "onUse", SourceSide = Side.Player, SourceItemId = Item(ctx, "first").Id });
            Triggers.Drain(ctx, 1);
            Assert.Near(2, ItemAttributes.Get(Item(ctx, "first"), "CritChance"));
            Assert.Near(2, ItemAttributes.Get(Item(ctx, "second"), "CritChance"));
        }

        // G04 全量主责：150 个模板逐个走真实映射入口；除已确认的数据缺口
        // （Building Crescendo 缺光环定义、Masterpiece 缺能力定义告警）外，
        // 不允许任何 G04 卡因数值/标签/计数器路径被整卡拒绝。
        private static void AllG04PrimaryTemplatesMap()
        {
            int count = 0;
            foreach (KeyValuePair<string, JsonValue> pair in G04AllFixtures.AsObject())
            {
                string name = pair.Key;
                JsonValue row = pair.Value, template = row["template"];
                string id = row["id"].AsString(), type = template["$type"].AsString();
                string tier = G04Tier(template);
                count++;
                if (type == "TCardSkill")
                {
                    LiveSkillEntry skill = new LiveSkillEntry
                    {
                        TemplateId = id,
                        Tier = tier,
                        Template = template,
                        Name = name
                    };
                    BattleConfig config = PlayerBoard(Entry("neighbor", 0, new string[0], null, "[\"Weapon\",\"Toy\"]",
                        "{\"CooldownMax\":1000,\"DamageAmount\":10}"));
                    SkillMappingResult mapped = SkillMapper.Apply(config, new[] { skill });
                    string status = mapped.Skills[0].Status;
                    if (status == "unsupported" || status == "missing-template")
                        throw new Exception("G04 技能被拒绝 " + name + ": " + mapped.Skills[0].Reason);
                    try { BattleEngine.InitBattle(config); }
                    catch (Exception error) { throw new Exception("G04 技能初始化失败 " + name + ": " + error.Message); }
                    continue;
                }

                LiveCardEntry card = new LiveCardEntry
                {
                    TemplateId = id,
                    Template = template,
                    Tier = tier,
                    Socket = 0,
                    Size = template["Size"].AsString(),
                    AttributesAreLive = false,
                    Attributes = MonsterTemplateExtractor.TierAttributes(template, tier)
                };
                BattleConfig board = PlayerBoard(card, Entry("neighbor", 1, new string[0], null, "[\"Weapon\",\"Toy\"]",
                    "{\"CooldownMax\":1000,\"DamageAmount\":10}"));
                List<string> warnings = NativeCardAbilityMapper.Apply(board);
                if (warnings.Count > 0 && name != "Masterpiece")
                    throw new Exception("G04 物品被拒绝 " + name + ": " + string.Join(" | ", warnings.ToArray()));
                try { BattleEngine.InitBattle(board); }
                catch (Exception error) { throw new Exception("G04 物品初始化失败 " + name + ": " + error.Message); }
            }
            Assert.Equal(150, count);
        }

        // G04 全量主责运行时冒烟：每张卡在真实棋盘上至少执行一次主动能力，
        // 并触发其原生触发器一次；只证明不抛异常/不死循环，不替代逐卡语义断言。
        private static void AllG04PrimaryTemplatesSmokeRuntime()
        {
            int count = 0;
            foreach (KeyValuePair<string, JsonValue> pair in G04AllFixtures.AsObject())
            {
                string name = pair.Key;
                JsonValue row = pair.Value, template = row["template"];
                string id = row["id"].AsString(), type = template["$type"].AsString();
                string tier = G04Tier(template);
                count++;
                try
                {
                    if (type == "TCardSkill")
                    {
                        LiveSkillEntry skill = new LiveSkillEntry { TemplateId = id, Tier = tier, Template = template, Name = name };
                        BattleConfig config = PlayerBoard(Entry("neighbor", 0, new string[0], null, "[\"Weapon\",\"Toy\"]",
                            "{\"CooldownMax\":1000,\"DamageAmount\":10}"));
                        SkillMappingResult mapped = SkillMapper.Apply(config, new[] { skill });
                        string status = mapped.Skills[0].Status;
                        if (status == "out-of-combat" || status == "inactive" || status == "unsupported") continue;
                        BattleContext ctx = BattleEngine.InitBattle(config);
                        InternalItem source = SkillItem(ctx, id);
                        if (source == null) continue; // 静态标签/开场光环已直接应用，无需运行时触发器。
                        foreach (TriggerDef trigger in source.Card.Triggers)
                        {
                            if (trigger.Event == "atCombatStart") continue;
                            Triggers.Enqueue(ctx, new TriggerPayload
                            {
                                Event = trigger.Event,
                                SourceSide = Side.Player,
                                SourceItemId = Item(ctx, "neighbor").Id,
                                TargetSide = Side.Player,
                                TargetItemId = Item(ctx, "neighbor").Id,
                                AttributeName = trigger.AttributeFilter,
                                ChangeType = trigger.ChangeType,
                                Amount = 1,
                                ChainId = ctx.NextChainId++,
                                Depth = 0
                            });
                            Triggers.Drain(ctx, 1);
                            if (ctx.Ended) break;
                        }
                    }
                    else
                    {
                        LiveCardEntry card = new LiveCardEntry
                        {
                            TemplateId = id,
                            Template = template,
                            Tier = tier,
                            Socket = 0,
                            Size = template["Size"].AsString(),
                            AttributesAreLive = false,
                            Attributes = MonsterTemplateExtractor.TierAttributes(template, tier)
                        };
                        BattleConfig config = PlayerBoard(card, Entry("neighbor", 1, new string[0], null, "[\"Weapon\",\"Toy\"]",
                            "{\"CooldownMax\":1000,\"DamageAmount\":10}"));
                        NativeCardAbilityMapper.Apply(config);
                        BattleContext ctx = BattleEngine.InitBattle(config);
                        InternalItem source = Item(ctx, id);
                        if (source == null) throw new Exception("物品未进入 ctx.Items");
                        BattleEngine.ForceUse(ctx, source, 1, 0);
                        Triggers.DrainNativeFrame(ctx, 1);
                    }
                }
                catch (Exception error)
                {
                    throw new Exception("G04 运行时冒烟失败 " + name + ": " + error.Message);
                }
            }
            Assert.Equal(150, count);
        }

        private static string G04Tier(JsonValue template)
        {
            JsonValue tiers = template["Tiers"];
            string fallback = null;
            foreach (string candidate in new[] { "Gold", "Diamond", "Silver", "Bronze", "Legendary" })
            {
                if (!tiers.Has(candidate)) continue;
                if (fallback == null) fallback = candidate;
                JsonValue tier = tiers[candidate];
                if (tier["AbilityIds"].Count > 0 || tier["AuraIds"].Count > 0) return candidate;
            }
            return fallback ?? "Gold";
        }

        // G04 独立交付：未支持的人物属性必须整条拒绝，不能静默当 0。
        // Custom_N 已由 G05 全量人物属性快照支持，改用真正未知的属性验证 fail-closed。
        private static void UnsupportedPlayerAttributeValueFailsClosed()
        {
            Assert.Throws<FormatException>(delegate
            {
                SkillMapper.MapValue(Json.Parse("{\"$type\":\"TReferenceValuePlayerAttribute\",\"AttributeType\":\"DefinitelyUnsupported\"," +
                    "\"Target\":" + SelfHeroTarget() + ",\"DefaultValue\":0,\"Modifier\":null}"), new Dictionary<string, double>(), null);
            });
        }

        // G04 独立交付：TRangeValue 使用引擎 RNG，结果必须落在原生区间内。
        private static void RangeValueStaysWithinBounds()
        {
            BattleConfig config = PlayerBoard(Entry("probe", 0, new string[0]));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = Item(ctx, "probe");
            JsonValue raw = Json.Parse("{\"$type\":\"TRangeValue\",\"MinValue\":3,\"MaxValue\":5,\"DefaultValue\":0}");
            NumExpr expr = SkillMapper.MapValue(raw, new Dictionary<string, double>(), null);
            List<double> seen = new List<double>();
            for (int i = 0; i < 100; i++)
            {
                double value = Effects.EvaluateNum(expr, ctx, item, ctx.Player);
                Assert.True(value >= 3 && value <= 5);
                Assert.Near(Math.Truncate(value), value);
                if (!seen.Contains(value)) seen.Add(value);
            }
            Assert.True(seen.Count >= 2);
        }

        private static void UnsupportedRulesFailClosed()
        {
            Assert.Throws<FormatException>(delegate
            {
                SkillMapper.MapAction(Json.Parse("{\"$type\":\"TActionPlayerDamage\",\"ReferenceValue\":{\"$type\":\"TReferenceValueRunAttribute\"},\"Target\":" + EnemyHeroTarget() + "}"), new Dictionary<string, double>());
            });
            Assert.Throws<FormatException>(delegate
            {
                NativeSkillSupport.MapTrigger(Json.Parse("{\"$type\":\"TTriggerOnCardSelected\"}"));
            });
            Assert.Throws<FormatException>(delegate
            {
                SkillMapper.MapAction(Json.Parse("{\"$type\":\"TActionPlayerModifyAttribute\",\"AttributeType\":\"UnmappedAttribute\",\"Operation\":\"Add\",\"Value\":" + Fixed(1) + ",\"Target\":" + SelfHeroTarget() + "}"), new Dictionary<string, double>());
            });
        }

        // 人物条件动态光环：开场不算固定加成，满足条件时按帧自动加上。
        private static void PlayerConditionAuraTogglesWithHealth()
        {
            string half = "{\"$type\":\"TPrerequisitePlayer\",\"Subject\":{\"$type\":\"TTargetPlayerRelative\",\"TargetMode\":\"Self\",\"Conditions\":{" +
                "\"$type\":\"TPlayerConditionalAttribute\",\"Attribute\":\"Health\",\"ComparisonOperator\":\"LessThanOrEqual\",\"ComparisonValue\":{" +
                "\"$type\":\"TReferenceValuePlayerAttribute\",\"AttributeType\":\"HealthMax\",\"Target\":" + SelfHeroTarget() +
                ",\"Modifier\":{\"ModifyMode\":\"Multiply\",\"Value\":" + Fixed(0.5) + ",\"ShouldRound\":true}}}}}";
            LiveCardEntry card = Entry("raging", 0, new string[0], new[]
            {
                "{\"$type\":\"TAuraActionCardModifyAttribute\",\"ActiveIn\":\"HandOnly\",\"WorksIn\":\"CombatOnly\",\"Prerequisites\":[" + half +
                "],\"Action\":{\"$type\":\"TAuraActionCardModifyAttribute\",\"AttributeType\":\"DamageAmount\",\"Operation\":\"Add\",\"Value\":" + Fixed(5) + ",\"Target\":" + SelfTarget() + "}}"
            });
            BattleConfig config = PlayerBoard(card);
            config.PlayerMaxHp = 100;
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = Item(ctx, "raging");
            // 满血时不满足条件，属性保持模板值。
            Assert.Near(10, ItemAttributes.Get(item, "DamageAmount"));

            Effects.ApplyDamage(ctx, new DamageSource { Id = "probe", Side = Side.Enemy }, Side.Player, 60, 1, new DamageOptions());
            NativeDynamicAuras.Refresh(ctx);
            Assert.Near(15, ItemAttributes.Get(item, "DamageAmount"));

            // 回满血后光环自动撤销，不得残留。
            Effects.ApplyHeal(ctx, Side.Player, 60, 2, null, null, null, 0);
            NativeDynamicAuras.Refresh(ctx);
            Assert.Near(10, ItemAttributes.Get(item, "DamageAmount"));
        }
    }
}
