using System.Linq;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class Replay4CriticalRegressionTests
    {
        public static void Run()
        {
            foreach (string kind in new[] { "damage", "poison", "burn", "heal", "shield" })
                NativeCriticalPublishesExactlyOneObserverEvent(kind);
        }

        private static void NativeCriticalPublishesExactlyOneObserverEvent(string kind)
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, PlayerMaxHp = 1000, EnemyMaxHp = 1000 };
            CardDef source = new CardDef { Id = "critical-source", Passive = true };
            source.Effects.Add(new Effect { Kind = "crit", Chance = 1 });
            CardDef observer = new CardDef { Id = "critical-observer", Passive = true };
            TriggerDef trigger = new TriggerDef { Event = "onCrit", Priority = "Medium" };
            trigger.Effects.Add(new Effect { Kind = "shield", Amount = new NumExpr { Value = 5 },
                Target = new Target { Kind = "selfHero" } });
            observer.Triggers.Add(trigger);
            config.Cards[source.Id] = source;
            config.Cards[observer.Id] = observer;
            config.Player.Items.Add(new LoadoutItem { CardId = source.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = observer.Id, Slot = 1 });
            BattleContext ctx = BattleEngine.InitBattle(config);
            Effects.ResolveEffect(ctx, new Effect { Kind = kind, Amount = new NumExpr { Value = 10 },
                Target = new Target { Kind = kind == "heal" || kind == "shield" ? "selfHero" : "enemyHero" } },
                ctx.Items[0], 1, 0, 1);
            Assert.Equal(1, ctx.Events.Count(delegate(BattleEvent e) { return e.Kind == "crit"; }));
            Assert.Equal(1, ctx.TriggerQueue.Count(delegate(TriggerPayload p) { return p.Event == "onCrit"; }));
            Triggers.DrainNativeFrame(ctx, 1.05);
            Assert.Equal(1, ctx.Events.Count(delegate(BattleEvent e) {
                return e.Kind == "shield" && e.SourceItemId == observer.Id;
            }));
        }
    }
}
