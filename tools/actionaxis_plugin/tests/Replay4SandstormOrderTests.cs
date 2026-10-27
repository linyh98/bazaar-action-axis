using System;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class Replay4SandstormOrderTests
    {
        public static void Run()
        {
            // baca5f5b frame 640: Burn 1 is blocked by the last Shield before
            // Sandstorm spends that Shield. It must not deal 1 HP this frame.
            BattleConfig config = new BattleConfig { NativeMechanics = true,
                MaxTime = 32.05, PlayerMaxHp = 10000, EnemyMaxHp = 400, EnemyStartShield = 10 };
            CardDef source = new CardDef { Id = "burn-before-storm", Passive = true };
            TriggerDef start = new TriggerDef { Event = "atCombatStart" };
            start.Effects.Add(new Effect { Kind = "burn", Amount = new NumExpr { Value = 1 },
                Target = new Target { Kind = "enemyHero" } });
            source.Triggers.Add(start);
            config.Cards[source.Id] = source;
            config.Player.Items.Add(new LoadoutItem { CardId = source.Id, Slot = 0 });
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);
            BattleHealthSnapshot frame = result.HealthSnapshots.Find(delegate(BattleHealthSnapshot s) { return Math.Abs(s.Time - 32) < 1e-9; });
            Assert.True(frame != null);
            if (frame != null)
            {
                Assert.Near(400, frame.Enemy.Hp);
                Assert.Near(0, frame.Enemy.Shield);
                Assert.Near(1, frame.Enemy.Burn);
            }
            SandstormPrecedesCardActionsOnTheSameFrame();
        }

        private static void SandstormPrecedesCardActionsOnTheSameFrame()
        {
            // baca5f5b frame 661: storm hits HP before the card grants Shield.
            BattleConfig config = new BattleConfig { NativeMechanics = true,
                MaxTime = 30.05, PlayerMaxHp = 1000, EnemyMaxHp = 400 };
            CardDef card = new CardDef { Id = "shield-after-storm", Cooldown = 30 };
            card.Effects.Add(new Effect { Kind = "shield", Amount = new NumExpr { Value = 14 },
                Target = new Target { Kind = "enemyHero" } });
            config.Cards[card.Id] = card;
            config.Player.Items.Add(new LoadoutItem { CardId = card.Id, Slot = 0 });
            BattleResult result = BattleEngine.Simulate(config);
            BattleHealthSnapshot frame = result.HealthSnapshots.Find(delegate(BattleHealthSnapshot s) { return Math.Abs(s.Time - 30) < 1e-9; });
            Assert.True(frame != null);
            if (frame != null) { Assert.Near(399, frame.Enemy.Hp); Assert.Near(14, frame.Enemy.Shield); }
        }
    }
}
