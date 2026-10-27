using System;
using System.Linq;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class Replay4TerminalStateTests
    {
        public static void Run()
        {
            // b57a frame 360 and dabb frame 247 clear Flying on BOTH sides,
            // including the winner, without executing a FlyingStop action.
            BattleConfig config = new BattleConfig { NativeMechanics = true,
                PlayerMaxHp = 1000, EnemyMaxHp = 1, MaxTime = 2 };
            CardDef source = new CardDef { Id = "terminal-flyer", Cooldown = 1, PercentSlowReduction = 30 };
            source.Effects.Add(new Effect { Kind = "damage", Amount = new NumExpr { Value = 1 },
                Target = new Target { Kind = "enemyHero" } });
            TriggerDef start = new TriggerDef { Event = "atCombatStart" };
            start.Effects.Add(new Effect { Kind = "fly", Target = new Target { Kind = "selfItem" } });
            source.Triggers.Add(start);
            config.Cards[source.Id] = source;
            config.Player.Items.Add(new LoadoutItem { CardId = source.Id, Slot = 0 });
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal("player", result.Winner);
            ItemSnapshot last = result.ItemSnapshots.Last(delegate(ItemSnapshot s) { return s.ItemId == source.Id; });
            Assert.Near(0, last.Attributes["Flying"]);
            Assert.Near(0, last.Attributes["PercentFreezeReduction"]);
            Assert.Near(30, last.Attributes["PercentSlowReduction"]);
            Assert.Equal(0, result.Events.Count(delegate(BattleEvent e) { return e.Kind == "flyStop"; }));
        }
    }
}
