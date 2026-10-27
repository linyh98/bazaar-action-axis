using System;
using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class ReplayFourthCastQueueTests
    {
        public static void Run()
        {
            NaturalAndForcedCastsShareTheirExecutionWindow();
        }

        private static void NaturalAndForcedCastsShareTheirExecutionWindow()
        {
            // b60fa630: two accepted uses spend Ammo in frame 161. Their
            // damage must run at 162 and 167, then the third at 172.
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 1.7 };
            CardDef first = new CardDef { Id = "first-force-source", Cooldown = 1 };
            CardDef second = new CardDef { Id = "second-force-source", Cooldown = 1.25 };
            CardDef repeated = new CardDef { Id = "natural-and-forced", Cooldown = 1.1, AmmoMax = 3 };
            repeated.Effects.Add(new Effect { Kind = "damage", Priority = "Medium", NativeAbilityId = "0",
                Amount = new NumExpr { Value = 10 }, Target = new Target { Kind = "enemyHero" } });
            foreach (CardDef source in new[] { first, second })
            {
                TriggerDef trigger = new TriggerDef { Event = "onUse", Priority = "Medium",
                    Filter = new TriggerFilter { SourceItemId = source.Id } };
                trigger.Effects.Add(new Effect { Kind = "forceUse", Target = new Target { Kind = "selfItem" } });
                repeated.Triggers.Add(trigger);
            }
            int slot = 0;
            foreach (CardDef card in new[] { first, second, repeated })
            {
                config.Cards[card.Id] = card;
                config.Player.Items.Add(new LoadoutItem { CardId = card.Id, Slot = slot++ });
            }
            BattleResult result = BattleEngine.Simulate(config);
            List<BattleEvent> hits = result.Events.FindAll(delegate(BattleEvent e) {
                return e.Kind == "damage" && e.SourceItemId == repeated.Id;
            });
            Assert.Equal(3, hits.Count);
            if (hits.Count == 3)
            {
                Assert.Near(1.1, hits[0].Time);
                Assert.Near(1.35, hits[1].Time);
                Assert.Near(1.6, hits[2].Time);
            }
            foreach (double time in new[] { 1.05, 1.3 })
            {
                ItemSnapshot snapshot = result.ItemSnapshots.Find(delegate(ItemSnapshot s) {
                    return s.ItemId == repeated.Id && Math.Abs(s.Time - time) < 1e-9;
                });
                Assert.Equal(time < 1.1 ? 1 : 0, snapshot.Ammo);
            }
        }
    }
}
