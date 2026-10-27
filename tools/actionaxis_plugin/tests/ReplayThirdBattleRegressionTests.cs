using System;
using System.Linq;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class ReplayThirdBattleRegressionTests
    {
        public static void Run()
        {
            LethalBurnStillDecays();
            LethalBurnStillDecaysWhenRevived();
        }

        private static BattleConfig BurnBoard(bool revive)
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true,
                MaxTime = 0.1, PlayerMaxHp = 400, PlayerStartHp = 3, EnemyMaxHp = 400 };
            CardDef fire = new CardDef { Id = "lethal-opening-burn", Passive = true };
            TriggerDef opening = new TriggerDef { Event = "atCombatStart" };
            opening.Effects.Add(new Effect { Kind = "burn", Amount = new NumExpr { Value = 22 },
                Target = new Target { Kind = "enemyHero" } });
            fire.Triggers.Add(opening);
            config.Cards[fire.Id] = fire;
            config.Enemy.Items.Add(new LoadoutItem { CardId = fire.Id, Slot = 0 });
            if (revive)
            {
                CardDef preserver = new CardDef { Id = "burn-revive", Passive = true };
                TriggerDef death = new TriggerDef { Event = "onPlayerDied",
                    SkillRule = new NativeSkillRule { PlayerOwner = "self" } };
                death.Effects.Add(new Effect { Kind = "revive", Amount = new NumExpr { Value = 100 },
                    Target = new Target { Kind = "selfHero" } });
                preserver.Triggers.Add(death);
                config.Cards[preserver.Id] = preserver;
                config.Player.Items.Add(new LoadoutItem { CardId = preserver.Id, Slot = 0 });
            }
            return config;
        }

        private static void LethalBurnStillDecays()
        {
            // b57a frame 360: Health 3 -> -19 and Burn 22 -> 21.
            // Ending the battle must not cancel the decay of the lethal tick.
            BattleResult result = BattleEngine.Simulate(BurnBoard(false));
            Assert.True(result.EndedByDeath);
            Assert.Equal("enemy", result.Winner);
            var last = result.HealthSnapshots.Last();
            Assert.Near(0, last.Player.Hp);
            Assert.Near(21, last.Player.Burn);
        }

        private static void LethalBurnStillDecaysWhenRevived()
        {
            BattleResult result = BattleEngine.Simulate(BurnBoard(true));
            Assert.False(result.EndedByDeath);
            var last = result.HealthSnapshots.Last();
            Assert.Near(100, last.Player.Hp);
            // Revive raises HP above its pre-tick value. That must not make
            // the damaging tick appear prevented and cancel its decay.
            Assert.Near(21, last.Player.Burn);
        }
    }
}
