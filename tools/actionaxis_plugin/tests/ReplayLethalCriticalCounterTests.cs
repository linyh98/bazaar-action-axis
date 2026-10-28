using System;
using System.Linq;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class ReplayLethalCriticalCounterTests
    {
        public static void Run()
        {
            ImmediateCounterPrecedesDeathAndRunsOnce();
            DeferredCriticalReactionRemainsSilent();
        }

        private static BattleResult Simulate(string priority)
        {
            BattleConfig config = new BattleConfig {
                NativeMechanics = true, EnemyMaxHp = 1, PlayerMaxHp = 1000, MaxTime = 2
            };
            CardDef source = new CardDef { Id = "lethal-crit-source", Cooldown = 1 };
            source.Effects.Add(new Effect { Kind = "crit", Chance = 1 });
            source.Effects.Add(new Effect { Kind = "damage", Priority = "Medium",
                Amount = new NumExpr { Value = 10 }, Target = new Target { Kind = "enemyHero" } });
            // A second critical action in the same cast must not count twice.
            source.Effects.Add(new Effect { Kind = "shield", Priority = "Medium",
                Amount = new NumExpr { Value = 10 }, Target = new Target { Kind = "selfHero" } });
            CardDef observer = new CardDef { Id = "lethal-crit-counter", Passive = true };
            observer.LiveAttributes["Custom_0"] = 0;
            TriggerDef trigger = new TriggerDef { Event = "onCrit", Priority = priority };
            trigger.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "Custom_0", Operation = "Add",
                Amount = new NumExpr { Value = 1 }, Target = new Target { Kind = "selfItem" } });
            // Pending death cannot start another attack, heal the victim, or
            // guess a random attribute target while recording the counter.
            trigger.Effects.Add(new Effect { Kind = "damage", Amount = new NumExpr { Value = 100 },
                Target = new Target { Kind = "enemyHero" } });
            trigger.Effects.Add(new Effect { Kind = "heal", Amount = new NumExpr { Value = 100 },
                Target = new Target { Kind = "enemyHero" } });
            trigger.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "Custom_0", Operation = "Add",
                Amount = new NumExpr { Value = 100 }, Target = new Target { Kind = "randomSelfItem" } });
            observer.Triggers.Add(trigger);
            config.Cards[source.Id] = source;
            config.Cards[observer.Id] = observer;
            config.Player.Items.Add(new LoadoutItem { CardId = source.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = observer.Id, Slot = 1 });
            return BattleEngine.Simulate(config);
        }

        private static void ImmediateCounterPrecedesDeathAndRunsOnce()
        {
            BattleResult result = Simulate("Immediate");
            Assert.Equal(0, result.Warnings.Count);
            Assert.True(result.EndedByDeath);
            Assert.Equal(1, result.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "damage"; }));
            Assert.Equal(0, result.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "heal"; }));
            BattleEvent[] counters = result.Events.Where(delegate(BattleEvent ev) {
                return ev.Kind == "modifyAttribute" && ev.SourceItemId == "lethal-crit-counter";
            }).ToArray();
            Assert.Equal(1, counters.Length);
            if (counters.Length == 0) return;
            BattleEvent hit = result.Events.First(delegate(BattleEvent ev) { return ev.Kind == "damage"; });
            BattleEvent death = result.Events.First(delegate(BattleEvent ev) { return ev.Kind == "death"; });
            Assert.Near(hit.Time, counters[0].Time);
            Assert.True(result.Events.IndexOf(hit) < result.Events.IndexOf(counters[0]));
            Assert.True(result.Events.IndexOf(counters[0]) < result.Events.IndexOf(death));
            Assert.Near(1, result.ItemSnapshots.Last(delegate(ItemSnapshot item) {
                return item.ItemId == "lethal-crit-counter";
            }).Attributes["Custom_0"]);
        }

        private static void DeferredCriticalReactionRemainsSilent()
        {
            BattleResult result = Simulate("Medium");
            Assert.Equal(0, result.Events.Count(delegate(BattleEvent ev) {
                return ev.Kind == "modifyAttribute" && ev.SourceItemId == "lethal-crit-counter";
            }));
            Assert.Near(1, result.ItemSnapshots.Last(delegate(ItemSnapshot item) {
                return item.ItemId == "lethal-crit-counter";
            }).Attributes["Custom_0"]);
        }
    }
}
