using System;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class ReplaySecondBattleRegressionTests
    {
        public static void Run()
        {
            FreezeExpiryDoesNotAdvanceCooldownUntilTheFollowingFrame();
            RuntimeFreezeBlocksItsCompleteDuration();
            LowObserverDoesNotBuffAnEarlierPriorityAttackOnTheSameFrame(false);
            LowObserverDoesNotBuffAnEarlierPriorityAttackOnTheSameFrame(true);
        }

        private static void FreezeExpiryDoesNotAdvanceCooldownUntilTheFollowingFrame()
        {
            // b1621d7b frame 140: Freeze 50 -> 0 still holds the cooldown at
            // 950 ms. Haste is active, but resumes the clock in frame 141.
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 1.1 };
            CardDef target = new CardDef { Id = "frozen-target", Cooldown = 10 };
            TriggerDef opening = new TriggerDef { Event = "atCombatStart" };
            opening.Effects.Add(new Effect { Kind = "freeze", Seconds = 1, Target = new Target { Kind = "selfItem" } });
            opening.Effects.Add(new Effect { Kind = "haste", Seconds = 2, Target = new Target { Kind = "selfItem" } });
            target.Triggers.Add(opening);
            config.Cards[target.Id] = target;
            config.Player.Items.Add(new LoadoutItem { CardId = target.Id, Slot = 0 });
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);
            ItemSnapshot before = Snapshot(result, 0.9);
            ItemSnapshot expires = Snapshot(result, 0.95);
            ItemSnapshot after = Snapshot(result, 1.0);
            Assert.Near(50, before.Attributes["Freeze"]);
            Assert.Near(0, expires.Attributes["Freeze"]);
            Assert.Near(10, before.CooldownRemaining);
            Assert.Near(10, expires.CooldownRemaining);
            Assert.Near(9.9, after.CooldownRemaining);
        }

        private static ItemSnapshot Snapshot(BattleResult result, double time)
        {
            return result.ItemSnapshots.Find(delegate(ItemSnapshot s) { return Math.Abs(s.Time - time) < 1e-9; });
        }

        private static void RuntimeFreezeBlocksItsCompleteDuration()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 1.6 };
            CardDef target = new CardDef { Id = "runtime-freeze-target", Cooldown = 10 };
            CardDef freezer = new CardDef { Id = "runtime-freezer", Cooldown = 0.5, AmmoMax = 1 };
            freezer.Effects.Add(new Effect { Kind = "freeze", Priority = "Medium", Seconds = 1, Count = 1,
                Target = new Target { Kind = "randomEnemyItem" } });
            config.Cards[target.Id] = target;
            config.Cards[freezer.Id] = freezer;
            config.Player.Items.Add(new LoadoutItem { CardId = target.Id, Slot = 0 });
            config.Enemy.Items.Add(new LoadoutItem { CardId = freezer.Id, Slot = 0 });
            BattleResult result = BattleEngine.Simulate(config);
            foreach (double time in new[] { 0.5, 1.45, 1.5 })
            {
                ItemSnapshot snapshot = result.ItemSnapshots.Find(delegate(ItemSnapshot s) {
                    return s.ItemId == target.Id && Math.Abs(s.Time - time) < 1e-9;
                });
                Assert.Near(9.45, snapshot.CooldownRemaining);
                Assert.Near(Math.Max(0, (1.5 - time) * 1000), snapshot.Attributes["Freeze"]);
            }
            ItemSnapshot after = result.ItemSnapshots.Find(delegate(ItemSnapshot s) {
                return s.ItemId == target.Id && Math.Abs(s.Time - 1.55) < 1e-9;
            });
            Assert.Near(9.4, after.CooldownRemaining);
        }

        private static void LowObserverDoesNotBuffAnEarlierPriorityAttackOnTheSameFrame(bool lethal)
        {
            // b1621d7b frame 151: Marlon's High damage reads 115, then
            // Sharkray's Low observer raises DamageAmount to 125.
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 1.1 };
            if (lethal) config.EnemyStartHp = 20;
            CardDef source = new CardDef { Id = "observer-source", Cooldown = 1 };
            CardDef attack = new CardDef { Id = "same-frame-attack", Cooldown = 1.05 };
            attack.LiveAttributes["DamageAmount"] = 20;
            attack.Effects.Add(new Effect { Kind = "damage", Priority = "High",
                Amount = new NumExpr { Type = "cardAttribute", Attribute = "DamageAmount" },
                Target = new Target { Kind = "enemyHero" } });
            CardDef passive = new CardDef { Id = "low-damage-observer", Passive = true };
            TriggerDef used = new TriggerDef { Event = "onUse", Priority = "Low",
                Filter = new TriggerFilter { SourceItemId = source.Id } };
            used.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "DamageAmount", Operation = "Add",
                Amount = new NumExpr { Value = 10 }, Target = new Target { Kind = "allSelfItems", ItemId = attack.Id } });
            passive.Triggers.Add(used);
            int slot = 0;
            foreach (CardDef card in new[] { source, attack, passive })
            {
                config.Cards[card.Id] = card;
                config.Player.Items.Add(new LoadoutItem { CardId = card.Id, Slot = slot++ });
            }
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(lethal, result.EndedByDeath);
            BattleEvent damage = result.Events.Find(delegate(BattleEvent e) { return e.Kind == "damage"; });
            Assert.True(damage != null);
            if (damage != null) { Assert.Near(1.05, damage.Time); Assert.Near(20, damage.Value.Value); }
            ItemSnapshot after = result.ItemSnapshots.Find(delegate(ItemSnapshot s) {
                return s.ItemId == attack.Id && Math.Abs(s.Time - 1.05) < 1e-9;
            });
            Assert.Near(30, after.Attributes["DamageAmount"]);
        }
    }
}
