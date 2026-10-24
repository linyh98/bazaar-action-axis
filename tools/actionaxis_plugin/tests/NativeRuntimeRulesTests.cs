using System;
using System.Collections.Generic;
using System.Text;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // 阶段 2b/3/4/5：运行时数值、人物属性与怒气、属性变化触发、卡牌生命周期、标签。
    // 模板均为手写原生片段，只验证映射与结算语义。
    public static class NativeRuntimeRulesTests
    {
        public static void Run()
        {
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

        private static LiveCardEntry Entry(string id, int slot, string[] abilities, string[] auras = null, string tags = "[]", string attrs = null)
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
            string json = "{\"$type\":\"TCardItem\",\"Size\":\"Small\",\"Tags\":" + tags + ",\"HiddenTags\":[],\"Tiers\":{\"Bronze\":{\"Attributes\":" +
                (attrs ?? DefaultAttrs) + ",\"AbilityIds\":" + abilityIds + ",\"AuraIds\":" + auraIds +
                ",\"TooltipIds\":[]}},\"Abilities\":" + abilityJson + ",\"Auras\":" + auraJson + "}";
            JsonValue template = Json.Parse(json);
            return new LiveCardEntry
            {
                TemplateId = id,
                Tier = "Bronze",
                Socket = slot,
                Template = template,
                Size = "Small",
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
            BattleConfig config = EnemyBoard(watcher, Entry("victim", 1, new string[0]), Entry("victim2", 2, new string[0]));
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem victim = Item(ctx, "victim");
            InternalItem victim2 = Item(ctx, "victim2");
            InternalItem watcherItem = Item(ctx, "watcher");
            InternalItem destroyerItem = new InternalItem { Id = "destroyer", CardId = "destroyer", Side = Side.Player, Slot = 9, Card = new CardDef { Id = "destroyer" } };
            Effects.ResolveEffect(ctx, new Effect { Kind = "destroy", Count = 1, Target = new Target { Kind = "enemyItem", ItemId = victim.Id } }, destroyerItem, 1, 0, null);
            Triggers.DrainNativeFrame(ctx, 1);
            Assert.True(victim.Destroyed);
            Assert.False(watcherItem.Destroyed);
            Assert.Near(4, ctx.Enemy.Shield);

            Effects.ResolveEffect(ctx, new Effect { Kind = "disable", Count = 1, Target = new Target { Kind = "enemyItem", ItemId = victim2.Id } }, destroyerItem, 2, 0, null);
            Assert.True(victim2.Disabled);
            Triggers.DrainNativeFrame(ctx, 2);
            Effects.ResolveEffect(ctx, new Effect { Kind = "repair", Count = 1, Target = new Target { Kind = "enemyItem", ItemId = victim2.Id } }, destroyerItem, 3, 0, null);
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

        private static void UnsupportedRulesFailClosed()
        {
            Assert.Throws<FormatException>(delegate
            {
                SkillMapper.MapAction(Json.Parse("{\"$type\":\"TActionPlayerDamage\",\"ReferenceValue\":{\"$type\":\"TReferenceValueRunAttribute\"},\"Target\":" + EnemyHeroTarget() + "}"), new Dictionary<string, double>());
            });
            Assert.Throws<FormatException>(delegate
            {
                NativeSkillSupport.MapTrigger(Json.Parse("{\"$type\":\"TTriggerOnSandstorm\"}"));
            });
            Assert.Throws<FormatException>(delegate
            {
                SkillMapper.MapAction(Json.Parse("{\"$type\":\"TActionPlayerModifyAttribute\",\"AttributeType\":\"Experience\",\"Operation\":\"Add\",\"Value\":" + Fixed(1) + ",\"Target\":" + SelfHeroTarget() + "}"), new Dictionary<string, double>());
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
