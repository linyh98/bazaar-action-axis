using System;
using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class ReplayRandomTests
    {
        public static void Run()
        {
            ObservedTargetKeepsTimingAndResetsBetweenRuns();
            ObservedCriticalStillCalculatesDamage();
            IllegalTargetAndImpossibleCriticalAreRejected();
            WrongFrameDoesNotRetimingEffect();
            BeforeUseObserversSeeFlyingStartedByOwnUse();
            TransformIdentityRequiresExactIndependentBirth();
        }

        private static BattleConfig Board(double chance)
        {
            CardDef user = new CardDef { Id = "random-user", Cooldown = 1 };
            user.Effects.Add(new Effect { Kind = "crit", Chance = chance });
            user.Effects.Add(new Effect { Kind = "fly", NativeAbilityId = "0", Count = 1,
                Target = new Target { Kind = "randomSelfItem" } });
            user.Effects.Add(new Effect { Kind = "damage", NativeAbilityId = "1",
                Amount = new NumExpr { Value = 10 }, Target = new Target { Kind = "enemyHero" } });
            CardDef target = new CardDef { Id = "random-target", Passive = true };
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 1.1,
                PlayerMaxHp = 1000, EnemyMaxHp = 1000, ReplayRandom = new ReplayRandomTape() };
            config.Cards[user.Id] = user;
            config.Cards[target.Id] = target;
            config.Player.Items.Add(new LoadoutItem { CardId = user.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = target.Id, Slot = 1 });
            config.ReplayRandom.Targets.Add(new ReplayRandomChoice { Frame = 20,
                Source = user.Id, Ability = "0", Kind = "fly", Targets = new List<string> { target.Id } });
            return config;
        }

        private static void ObservedTargetKeepsTimingAndResetsBetweenRuns()
        {
            BattleConfig config = Board(0);
            for (int i = 0; i < 2; i++)
            {
                BattleResult result = BattleEngine.Simulate(config);
                BattleEvent fly = result.Events.Find(delegate(BattleEvent ev) { return ev.Kind == "fly"; });
                Assert.True(fly != null);
                Assert.Equal("random-target", fly.TargetItemId);
                Assert.Near(1, fly.Time);
                Assert.Equal(1, result.ReplayRandom.TargetApplied);
                Assert.Equal(0, result.ReplayRandom.InvalidChoices);
            }
        }

        private static void ObservedCriticalStillCalculatesDamage()
        {
            foreach (bool critical in new[] { false, true })
            {
                BattleConfig config = Board(0.5);
                config.ReplayRandom.Criticals.Add(new ReplayRandomChoice { Frame = 20,
                    Source = "random-user", Ability = "1", Kind = "damage", TargetSide = "enemy", Critical = critical });
                BattleResult result = BattleEngine.Simulate(config);
                Assert.Near(critical ? 20 : 10, result.Totals.Player.Damage);
                Assert.Equal(1, result.ReplayRandom.CriticalApplied);
                Assert.Equal(0, result.ReplayRandom.InvalidChoices);
            }
        }

        private static void IllegalTargetAndImpossibleCriticalAreRejected()
        {
            BattleConfig config = Board(0);
            config.ReplayRandom.Targets[0].Targets[0] = "not-on-this-board";
            config.ReplayRandom.Criticals.Add(new ReplayRandomChoice { Frame = 20,
                Source = "random-user", Ability = "1", Kind = "damage", TargetSide = "enemy", Critical = true });
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Near(10, result.Totals.Player.Damage);
            Assert.Equal(0, result.ReplayRandom.TargetApplied);
            Assert.Equal(0, result.ReplayRandom.CriticalApplied);
            Assert.Equal(2, result.ReplayRandom.InvalidChoices);
        }

        private static void WrongFrameDoesNotRetimingEffect()
        {
            BattleConfig config = Board(0);
            config.ReplayRandom.Targets[0].Frame = 21;
            BattleResult result = BattleEngine.Simulate(config);
            BattleEvent fly = result.Events.Find(delegate(BattleEvent ev) { return ev.Kind == "fly"; });
            Assert.Near(1, fly.Time);
            Assert.Equal(0, result.ReplayRandom.TargetApplied);
            Assert.Equal(1, result.ReplayRandom.TargetFallbacks);
        }

        private static void BeforeUseObserversSeeFlyingStartedByOwnUse()
        {
            foreach (bool startsFlying in new[] { false, true })
            {
                BattleConfig config = Board(0);
                config.ReplayRandom.Targets[0].Targets[0] = "random-user";
                CardDef user = config.Cards["random-user"];
                JsonValue flying = Json.Parse("{\"$type\":\"TCardConditionalAttribute\",\"Attribute\":\"Flying\",\"ComparisonOperator\":\"Equal\",\"ComparisonValue\":{\"$type\":\"TFixedValue\",\"Value\":1}}");
                TriggerDef growth = new TriggerDef { Event = "onBeforeUse", Priority = "Immediate",
                    SkillRule = new NativeSkillRule { SourceOwner = "self", SourceSelf = true, SourceCondition = flying } };
                growth.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "CritChance", Operation = "Add",
                    Amount = new NumExpr { Value = 20 }, Target = new Target { Kind = "selfItem", ItemId = user.Id } });
                user.Triggers.Add(growth);
                if (startsFlying)
                {
                    TriggerDef start = new TriggerDef { Event = "atCombatStart" };
                    start.Effects.Add(new Effect { Kind = "fly", Target = new Target { Kind = "selfItem", ItemId = user.Id } });
                    user.Triggers.Add(start);
                }
                CardDef observer = config.Cards["random-target"];
                observer.Passive = false;
                observer.Cooldown = 5;
                TriggerDef haste = new TriggerDef { Event = "onBeforeUse", Priority = "Medium",
                    SkillRule = new NativeSkillRule { SourceOwner = "self", SourceCondition = flying } };
                haste.Effects.Add(new Effect { Kind = "haste", Seconds = 1,
                    Target = new Target { Kind = "selfItem", ItemId = observer.Id } });
                observer.Triggers.Add(haste);
                // Another item used in the same frame must not drain the
                // first user's next-frame observers prematurely.
                CardDef other = new CardDef { Id = "other-user", Cooldown = 1 };
                config.Cards[other.Id] = other;
                config.Enemy.Items.Add(new LoadoutItem { CardId = other.Id, Slot = 0 });
                BattleResult result = BattleEngine.Simulate(config);
                List<BattleEvent> growthEvents = result.Events.FindAll(delegate(BattleEvent ev) {
                    return ev.Kind == "modifyAttribute" && ev.SourceItemId == user.Id;
                });
                List<BattleEvent> hasteEvents = result.Events.FindAll(delegate(BattleEvent ev) {
                    return ev.Kind == "haste" && ev.SourceItemId == observer.Id;
                });
                Assert.Equal(1, growthEvents.Count);
                Assert.Equal(1, hasteEvents.Count);
                if (growthEvents.Count > 0) Assert.Near(1, growthEvents[0].Time);
                if (hasteEvents.Count > 0) Assert.Near(1.05, hasteEvents[0].Time);
            }
        }
        private static void TransformIdentityRequiresExactIndependentBirth()
        {
            ReplayRandomTape tape = new ReplayRandomTape();
            tape.Identities.Add(new ReplayTransformIdentity { Frame = 20, Original = "old", InstanceId = "native-new",
                TemplateId = "virus", Side = "player", Slot = 2, Index = 0 });
            tape.Targets.Add(new ReplayRandomChoice { Frame = 40, Source = "native-new", Kind = "slow",
                Targets = new List<string> { "native-new" } });
            ReplayRandomSession session = new ReplayRandomSession(tape);
            Assert.False(session.BindTransformIdentity("old", "wrong", "other", Side.Player, 2, 0, 20));
            Assert.False(session.BindTransformIdentity("old", "wrong", "virus", Side.Player, 2, 0, 21));
            Assert.Equal(0, session.Audit.IdentityApplied);
            Assert.True(session.BindTransformIdentity("old", "sim-new", "virus", Side.Player, 2, 0, 20));
            InternalItem item = new InternalItem { Id = "sim-new" };
            session.Scope = new ReplayRandomScope { Frame = 40, Source = item.Id, Kind = "slow" };
            List<InternalItem> selected = session.ChooseTargets(new List<InternalItem> { item }, 1);
            Assert.True(selected != null && selected.Count == 1 && selected[0] == item);
            Assert.Equal("sim-new", item.Id);
            Assert.False(session.BindTransformIdentity("old", "duplicate", "virus", Side.Player, 2, 0, 20));
        }
    }
}
