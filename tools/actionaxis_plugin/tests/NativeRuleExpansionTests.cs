using System;
using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // 阶段 1 新增的战斗事件与动作回归。模板均为手写原生片段，
    // 只验证映射与结算顺序，不依赖本地卡库。
    public static class NativeRuleExpansionTests
    {
        public static void Run()
        {
            BeforeUsePassiveBuffsSameActivation();
            StartedFlyingTriggerUsesAffectedCard();
            ReloadAndFlyingStop();
            CleanseAndCompositeActions();
            TriggerSourcePositionalTarget();
            UnsupportedRulesStillFailClosed();
        }

        private static LiveCardEntry Entry(string id, JsonValue template, string tier = "Bronze")
        {
            LiveCardEntry entry = new LiveCardEntry();
            entry.TemplateId = id;
            entry.Tier = tier;
            entry.Socket = 0;
            entry.Template = template;
            entry.Size = NativeSkillSupport.Text(template, "Size") ?? "Small";
            entry.Attributes = MonsterTemplateExtractor.TierAttributes(template, tier);
            return entry;
        }

        private static BattleConfig Build(JsonValue template, double maxTime, out MappedBoard mapped)
        {
            LiveCardEntry entry = Entry("test-item", template);
            mapped = CardMapper.Build(new List<LiveCardEntry> { entry }, CardCatalog.FromCards(new List<CardDef>()), 1000, 10000, maxTime);
            mapped.Config.NativeMechanics = true;
            return mapped.Config;
        }

        private static void BeforeUsePassiveBuffsSameActivation()
        {
            JsonValue template = Json.Parse(@"{
              ""$type"":""TCardItem"",""Size"":""Small"",""Tags"":[],
              ""Tiers"":{""Bronze"":{""Attributes"":{""CooldownMax"":1000,""DamageAmount"":10},""AbilityIds"":[""0"",""1""],""AuraIds"":[],""TooltipIds"":[]}},
              ""Abilities"":{""0"":{
                ""Trigger"":{""$type"":""TTriggerOnBeforeItemUsed"",""Subject"":{""$type"":""TTargetCardSelf"",""Conditions"":null}},
                ""ActiveIn"":""HandOnly"",""WorksIn"":""CombatOnly"",""Prerequisites"":null,
                ""Action"":{""$type"":""TActionCardModifyAttribute"",""AttributeType"":""DamageAmount"",""Operation"":""Add"",
                  ""Value"":{""$type"":""TFixedValue"",""Value"":5},
                  ""Duration"":{""$type"":""TDeterminantDuration"",""DurationType"":""UntilEndOfCombat""},
                  ""TargetCount"":null,""Target"":{""$type"":""TTargetCardTriggerSource"",""Conditions"":null},""Cost"":null}
              },""1"":{
                ""Trigger"":{""$type"":""TTriggerOnCardFired""},""ActiveIn"":""HandOnly"",""WorksIn"":""CombatOnly"",""Prerequisites"":null,
                ""Action"":{""$type"":""TActionPlayerDamage"",""ReferenceValue"":null,""Duration"":null,""Cost"":null,
                  ""Target"":{""$type"":""TTargetPlayerRelative"",""TargetMode"":""Opponent"",""Conditions"":null}}
              }},""Auras"":{}}");
            MappedBoard mapped;
            BattleConfig config = Build(template, 1.2, out mapped);
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            CardDef card = config.Cards[config.Player.Items[0].CardId];
            Assert.Equal("onBeforeUse", card.Triggers[0].Event);
            Assert.Equal("triggerSourceItem", card.Triggers[0].Effects[0].Target.Kind);
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Near(15, result.Totals.Player.Damage);
            BattleEvent damage = result.Events.Find(delegate(BattleEvent ev) { return ev.Kind == "damage"; });
            Assert.Near(1, damage.Time);
        }

        private static void StartedFlyingTriggerUsesAffectedCard()
        {
            JsonValue template = Json.Parse(@"{
              ""$type"":""TCardItem"",""Size"":""Small"",""Tags"":[],
              ""Tiers"":{""Bronze"":{""Attributes"":{""CooldownMax"":0},""AbilityIds"":[""0"",""1""],""AuraIds"":[],""TooltipIds"":[]}},
              ""Abilities"":{
                ""0"":{""Trigger"":{""$type"":""TTriggerOnFightStarted""},""ActiveIn"":""HandOnly"",""WorksIn"":""CombatOnly"",""Prerequisites"":null,
                  ""Action"":{""$type"":""TActionCardFlyingStart"",""Cost"":null,""Duration"":null,
                    ""TargetCount"":null,""Target"":{""$type"":""TTargetCardSelf"",""Conditions"":null}}},
                ""1"":{""Trigger"":{""$type"":""TTriggerOnCardStartedFlying"",""Subject"":{""$type"":""TTargetCardSelf"",""Conditions"":null},""Source"":null},
                  ""ActiveIn"":""HandOnly"",""WorksIn"":""CombatOnly"",""Prerequisites"":null,
                  ""Action"":{""$type"":""TActionPlayerDamage"",""ReferenceValue"":{""$type"":""TFixedValue"",""Value"":7},""Duration"":null,""Cost"":null,
                    ""Target"":{""$type"":""TTargetPlayerRelative"",""TargetMode"":""Opponent"",""Conditions"":null}}}
              },""Auras"":{}}");
            MappedBoard mapped;
            BattleConfig config = Build(template, 2, out mapped);
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            CardDef card = config.Cards[config.Player.Items[0].CardId];
            Assert.Equal("onFly", card.Triggers[1].Event);
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Near(7, result.Totals.Player.Damage);
            BattleEvent damage = result.Events.Find(delegate(BattleEvent ev) { return ev.Kind == "damage"; });
            Assert.Equal(card.Id, damage.SourceItemId);
            // A reaction to an opening action lands on the following frame.
            Assert.Near(0.05, damage.Time);
        }

        private static void ReloadAndFlyingStop()
        {
            JsonValue reloadTemplate = Json.Parse(@"{
              ""$type"":""TCardItem"",""Size"":""Small"",""Tags"":[],
              ""Tiers"":{""Bronze"":{""Attributes"":{""CooldownMax"":10000},""AbilityIds"":[""0""],""AuraIds"":[],""TooltipIds"":[]}},
              ""Abilities"":{""0"":{""Trigger"":{""$type"":""TTriggerOnCardFired""},""ActiveIn"":""HandOnly"",""WorksIn"":""CombatOnly"",""Prerequisites"":null,
                ""Action"":{""$type"":""TActionCardReload"",""Value"":null,""TargetCount"":null,""Cost"":null,
                  ""Target"":{""$type"":""TTargetCardPositional"",""Origin"":""Self"",""TargetMode"":""RightCard"",""IncludeOrigin"":false,
                    ""Conditions"":{""$type"":""TCardConditionalAttribute"",""Attribute"":""AmmoMax"",""ComparisonOperator"":""GreaterThan"",
                      ""ComparisonValue"":{""$type"":""TFixedValue"",""Value"":0}}}}}},""Auras"":{}}");
            MappedBoard mapped;
            BattleConfig config = Build(reloadTemplate, 2, out mapped);
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            CardDef reloader = config.Cards[config.Player.Items[0].CardId];
            Effect reload = reloader.Effects[0];
            Assert.Equal("reload", reload.Kind);
            Assert.Near(-1, reload.AmountValue);
            Assert.Equal("rightSelf", reload.Target.Kind);

            CardDef ammo = new CardDef { Id = "ammo", Cooldown = 10, AmmoMax = 2 };
            ammo.LiveAttributes["AmmoMax"] = 2;
            config.Cards[ammo.Id] = ammo;
            config.Player.Items.Add(new LoadoutItem { CardId = ammo.Id, Slot = 1 });
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem ammoItem = ctx.Items.Find(delegate(InternalItem i) { return i.CardId == "ammo"; });
            ammoItem.Ammo = 0;
            Effects.ResolveEffect(ctx, reload, ctx.Items[0], 1, 0, null);
            Assert.Equal(2, ammoItem.Ammo);
            Assert.True(ctx.Events.Exists(delegate(BattleEvent ev) { return ev.Kind == "reload" && ev.TargetItemId == ammoItem.Id; }));

            BattleConfig stopConfig = Config();
            CardDef flyer = new CardDef { Id = "flyer", Cooldown = 0 };
            flyer.Effects.Add(new Effect { Kind = "fly", Count = 1, Target = new Target { Kind = "selfItem" } });
            stopConfig.Cards[flyer.Id] = flyer;
            stopConfig.Player.Items.Add(new LoadoutItem { CardId = flyer.Id, Slot = 0 });
            BattleContext stopCtx = BattleEngine.InitBattle(stopConfig);
            InternalItem flyerItem = stopCtx.Items[0];
            Effects.ResolveEffect(stopCtx, flyer.Effects[0], flyerItem, 0, 0, null);
            Assert.True(flyerItem.Flying);
            Effects.ResolveEffect(stopCtx, new Effect { Kind = "flyStop", Count = 1,
                Target = new Target { Kind = "selfItem" } }, flyerItem, 1, 0, null);
            Assert.False(flyerItem.Flying);
            Assert.True(stopCtx.Events.Exists(delegate(BattleEvent ev) { return ev.Kind == "flyStop"; }));
        }

        private static BattleConfig Config()
        {
            return new BattleConfig { NativeMechanics = true, PlayerMaxHp = 1000, EnemyMaxHp = 10000, MaxTime = 30 };
        }

        private static void CleanseAndCompositeActions()
        {
            Dictionary<string, double> attrs = new Dictionary<string, double>();
            JsonValue and = Json.Parse(@"{
              ""$type"":""TActionAnd"",""Cost"":null,""Actions"":[
                {""$type"":""TActionPlayerBurnRemove"",""ReferenceValue"":null,
                  ""Target"":{""$type"":""TTargetPlayerRelative"",""TargetMode"":""Self"",""Conditions"":null}},
                {""$type"":""TActionPlayerPoisonRemove"",""ReferenceValue"":null,
                  ""Target"":{""$type"":""TTargetPlayerRelative"",""TargetMode"":""Self"",""Conditions"":null}}
              ]}");
            Effect composite = SkillMapper.MapAction(and, attrs);
            Assert.Equal("composite", composite.Kind);
            Assert.Equal(2, composite.Effects.Count);
            Assert.Equal("burnRemove", composite.Effects[0].Kind);
            Assert.Equal("poisonRemove", composite.Effects[1].Kind);

            BattleConfig config = Config();
            CardDef cleaner = new CardDef { Id = "cleaner", Cooldown = 0 };
            config.Cards[cleaner.Id] = cleaner;
            config.Player.Items.Add(new LoadoutItem { CardId = cleaner.Id, Slot = 0 });
            BattleContext ctx = BattleEngine.InitBattle(config);
            ctx.Player.Burn = 5;
            ctx.Player.Poison = 3;
            Effects.ResolveEffect(ctx, composite, ctx.Items[0], 1, 0, null);
            Assert.Near(0, ctx.Player.Burn);
            Assert.Near(0, ctx.Player.Poison);
            Assert.True(ctx.Events.Exists(delegate(BattleEvent ev) { return ev.Kind == "burnRemove" && ev.Value == 5; }));
            Assert.True(ctx.Events.Exists(delegate(BattleEvent ev) { return ev.Kind == "poisonRemove" && ev.Value == 3; }));
        }

        private static void TriggerSourcePositionalTarget()
        {
            JsonValue target = Json.Parse(@"{""$type"":""TTargetCardPositional"",""Origin"":""TriggerSource"",""TargetMode"":""LeftCard"",
              ""IncludeOrigin"":false,""Conditions"":null}");
            Target mapped = SkillMapper.MapCardTarget(target);
            Assert.Equal("triggerSourceLeft", mapped.Kind);

            BattleConfig config = Config();
            CardDef ammo = new CardDef { Id = "ammo", Cooldown = 10, AmmoMax = 2 };
            ammo.LiveAttributes["AmmoMax"] = 2;
            CardDef trigger = new CardDef { Id = "trigger", Cooldown = 10 };
            config.Cards[ammo.Id] = ammo;
            config.Cards[trigger.Id] = trigger;
            config.Player.Items.Add(new LoadoutItem { CardId = ammo.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = trigger.Id, Slot = 1 });
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem ammoItem = ctx.Items.Find(delegate(InternalItem i) { return i.CardId == "ammo"; });
            InternalItem triggerItem = ctx.Items.Find(delegate(InternalItem i) { return i.CardId == "trigger"; });
            ammoItem.Ammo = 0;
            Effect reload = new Effect { Kind = "reload", AmountValue = -1, Count = 1, Target = mapped };
            Effects.ResolveEffect(ctx, reload, triggerItem, 1, 0, null, triggerItem.Id);
            Assert.Equal(2, ammoItem.Ammo);
        }

        private static void UnsupportedRulesStillFailClosed()
        {
            Assert.Throws<FormatException>(delegate {
                NativeSkillSupport.MapTrigger(Json.Parse("{\"$type\":\"TTriggerOnPlayerEnraged\",\"Subject\":{\"$type\":\"TTargetCardSelf\",\"Conditions\":null},\"Source\":null}"));
            });
            Assert.Throws<FormatException>(delegate {
                NativeSkillSupport.MapTrigger(Json.Parse("{\"$type\":\"TTriggerOnCardStartedFlying\",\"Subject\":{\"$type\":\"TTargetCardSection\",\"TargetSection\":\"SelectionSet\",\"Conditions\":null},\"Source\":null}"));
            });
            Assert.Throws<FormatException>(delegate {
                NativeSkillSupport.MapSubject(Json.Parse("{\"$type\":\"TTargetCardXMost\",\"TargetSection\":\"SelfHand\"}"));
            });
        }
    }
}
