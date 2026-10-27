using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class ReplayImmediateEligibilityTests
    {
        public static void Run()
        {
            FalseImmediateGateDoesNotObserveLaterPacket();
            FalseImmediateListenerDoesNotSnapshotOrdinaryObserver();
            NoImmediateListenerKeepsOrdinaryEvaluationDeferred();
        }

        private static JsonValue BelowHalfPrerequisite()
        {
            return Json.Parse(@"[{""$type"":""TPrerequisitePlayer"",""Subject"":{
                ""$type"":""TTargetPlayerRelative"",""TargetMode"":""Self"",""Conditions"":{
                ""$type"":""TPlayerConditionalAttribute"",""Attribute"":""Health"",""ComparisonOperator"":""LessThan"",
                ""ComparisonValue"":{""$type"":""TReferenceValuePlayerAttribute"",""AttributeType"":""HealthMax"",
                    ""DefaultValue"":0,""Target"":{""$type"":""TTargetPlayerRelative"",""TargetMode"":""Self""},
                    ""Modifier"":{""ModifyMode"":""Multiply"",""ShouldRound"":true,""Value"":{""$type"":""TFixedValue"",""Value"":0.5}}}}}}]");
        }

        private static JsonValue UnusedPrerequisite()
        {
            return Json.Parse(@"[{""$type"":""TPrerequisiteCardCount"",""Amount"":1,""Comparison"":""Equal"",
                ""Subject"":{""$type"":""TTargetCardSelf"",""Conditions"":{""$type"":""TCardConditionalAttribute"",
                ""Attribute"":""Custom_0"",""ComparisonOperator"":""Equal"",""ComparisonValue"":{""$type"":""TFixedValue"",""Value"":0}}}}]");
        }

        private static TriggerDef Observer(string priority, JsonValue prerequisite)
        {
            return new TriggerDef { Event = "onPlayerAttributeChanged", AttributeFilter = "Health", ChangeType = "Loss",
                Priority = priority, SkillRule = new NativeSkillRule { PlayerOwner = "self", Prerequisites = prerequisite } };
        }

        private static Effect Counter()
        {
            return new Effect { Kind = "modifyAttribute", Attribute = "Custom_0", Operation = "Add",
                Amount = new NumExpr { Value = 1 }, Target = new Target { Kind = "selfItem" } };
        }

        private static BattleContext Board(CardDef watcher)
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true,
                PlayerMaxHp = 4710, EnemyMaxHp = 10000, PlayerStartHp = 2707 };
            CardDef attacker = new CardDef { Id = "packet-source", Passive = true };
            config.Cards[attacker.Id] = attacker;
            config.Cards[watcher.Id] = watcher;
            config.Enemy.Items.Add(new LoadoutItem { CardId = attacker.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = watcher.Id, Slot = 0 });
            return BattleEngine.InitBattle(config);
        }

        private static InternalItem Watcher(BattleContext ctx)
        {
            return ctx.Items.Find(delegate(InternalItem item) { return item.Id == "packet-watcher"; });
        }

        private static void Packet(BattleContext ctx, double amount, string kind)
        {
            Effects.ApplyDamage(ctx, new DamageSource { Id = "packet-source", CardId = "packet-source", Side = Side.Enemy },
                Side.Player, amount, 10, new DamageOptions { IsDot = true, DotKind = kind, PreservePeriodicHealth = true });
            Triggers.DrainImmediateActions(ctx, 10);
        }

        private static void FalseImmediateGateDoesNotObserveLaterPacket()
        {
            foreach (bool conditionInSubject in new[] { false, true })
            {
                CardDef watcher = new CardDef { Id = "packet-watcher", Passive = true };
                watcher.LiveAttributes["Custom_0"] = 0;
                TriggerDef counter = Observer("Immediate", conditionInSubject ? null : BelowHalfPrerequisite());
                if (conditionInSubject)
                    counter.SkillRule.PlayerCondition = Json.Parse(@"{""$type"":""TPlayerConditionalAttribute"",
                        ""Attribute"":""Health"",""ComparisonOperator"":""LessThan"",""ComparisonValue"":{""$type"":""TFixedValue"",""Value"":2355}}");
                counter.Effects.Add(Counter());
                watcher.Triggers.Add(counter);
                BattleContext ctx = Board(watcher);
                Packet(ctx, 120, "poison");
                Assert.Near(2587, ctx.Player.Hp);
                Assert.Near(0, ItemAttributes.Get(Watcher(ctx), "Custom_0"));
                Packet(ctx, 346, "burn");
                Assert.Near(2241, ctx.Player.Hp);
                Assert.Near(1, ItemAttributes.Get(Watcher(ctx), "Custom_0"));
                Triggers.DrainNativeFrame(ctx, 10.05);
                Assert.Near(1, ItemAttributes.Get(Watcher(ctx), "Custom_0"));
            }
        }

        private static void OrdinaryObserverSeesLaterCounter(bool hasFalseImmediate)
        {
            CardDef watcher = new CardDef { Id = "packet-watcher", Passive = true };
            watcher.LiveAttributes["Custom_0"] = 0;
            if (hasFalseImmediate)
            {
                TriggerDef counter = Observer("Immediate", BelowHalfPrerequisite());
                counter.Effects.Add(Counter());
                watcher.Triggers.Add(counter);
            }
            TriggerDef ordinary = Observer("Medium", UnusedPrerequisite());
            ordinary.Effects.Add(new Effect { Kind = "shield", Amount = new NumExpr { Value = 7 }, Target = new Target { Kind = "selfHero" } });
            watcher.Triggers.Add(ordinary);
            BattleContext ctx = Board(watcher);
            Packet(ctx, 120, "poison");
            Assert.Near(0, ctx.Player.Shield);
            Effects.ResolveEffect(ctx, Counter(), Watcher(ctx), 10, 0, null);
            Triggers.DrainNativeFrame(ctx, 10.05);
            Assert.Near(0, ctx.Player.Shield);
        }

        private static void FalseImmediateListenerDoesNotSnapshotOrdinaryObserver()
        {
            OrdinaryObserverSeesLaterCounter(true);
        }

        private static void NoImmediateListenerKeepsOrdinaryEvaluationDeferred()
        {
            OrdinaryObserverSeesLaterCounter(false);
        }
    }
}
