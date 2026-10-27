using System;
using System.Collections.Generic;
using BazaarActionAxis.Engine;
namespace BazaarActionAxis.Tests
{
    public static class ReplayLowPriorityFrameTests
    {
        public static void Run()
        {
            LowHealFollowsEarlierPriorityDamage();
            LowHealRetainsItsItemUsedReservationOrder();
        }

        private static void LowHealFollowsEarlierPriorityDamage()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, PlayerMaxHp = 100,
                EnemyMaxHp = 100, MaxTime = 1.05, ReplayRandom = new ReplayRandomTape() };
            CardDef aiden = new CardDef { Id = "low-healer", Cooldown = 1 };
            aiden.Effects.Add(new Effect { Kind = "regen", Priority = "Medium",
                Amount = new NumExpr { Value = 1 }, Target = new Target { Kind = "selfHero" } });
            aiden.Effects.Add(new Effect { Kind = "heal", Priority = "Low",
                Amount = new NumExpr { Value = 10 }, Target = new Target { Kind = "selfHero" } });
            CardDef weapon = new CardDef { Id = "medium-weapon", Cooldown = 1 };
            weapon.Effects.Add(new Effect { Kind = "damage", Priority = "Medium",
                Amount = new NumExpr { Value = 30 }, Target = new Target { Kind = "enemyHero" } });
            config.Cards[aiden.Id] = aiden; config.Cards[weapon.Id] = weapon;
            config.Player.Items.Add(new LoadoutItem { CardId = aiden.Id, Slot = 0 });
            config.Enemy.Items.Add(new LoadoutItem { CardId = weapon.Id, Slot = 0 });
            config.ReplayRandom.Orders.Add(new ReplayRandomOrder { Frame = 20,
                Sources = new List<string> { aiden.Id, weapon.Id } });
            BattleResult result = BattleEngine.Simulate(config);
            int regen = result.Events.FindIndex(delegate(BattleEvent ev) { return ev.Kind == "regen"; });
            int damage = result.Events.FindIndex(delegate(BattleEvent ev) { return ev.Kind == "damage"; });
            int heal = result.Events.FindIndex(delegate(BattleEvent ev) { return ev.Kind == "heal" && ev.SourceItemId == aiden.Id; });
            Assert.True(regen >= 0 && damage > regen && heal > damage);
            if (heal >= 0) { Assert.Near(1, result.Events[heal].Time); Assert.Near(10, result.Events[heal].Value.Value); }
            Assert.Near(80, result.HealthSnapshots[result.HealthSnapshots.Count - 1].Player.Hp);
        }

        private static void LowHealRetainsItsItemUsedReservationOrder()
        {
            // 0276c46b: deferring First Aiden's heal must not move its ItemUsed
            // behind another Friend in Piano's shared 250 ms observer queue.
            BattleConfig config = new BattleConfig { NativeMechanics = true, PlayerMaxHp = 100,
                EnemyMaxHp = 1000, MaxTime = 1.35, ReplayRandom = new ReplayRandomTape() };
            CardDef healer = new CardDef { Id = "ordered-low-healer", Cooldown = 1, AmmoMax = 1 };
            healer.Tags.Add("Friend");
            healer.Effects.Add(new Effect { Kind = "heal", Priority = "Low",
                Amount = new NumExpr { Value = 10 }, Target = new Target { Kind = "selfHero" } });
            CardDef friend = new CardDef { Id = "ordered-medium-friend", Cooldown = 1, AmmoMax = 1 };
            friend.Tags.Add("Friend");
            friend.Effects.Add(new Effect { Kind = "damage", Priority = "Medium",
                Amount = new NumExpr { Value = 1 }, Target = new Target { Kind = "enemyHero" } });
            CardDef opponent = new CardDef { Id = "ordered-enemy-damage", Cooldown = 1, AmmoMax = 1 };
            opponent.Effects.Add(new Effect { Kind = "damage", Priority = "Medium",
                Amount = new NumExpr { Value = 30 }, Target = new Target { Kind = "enemyHero" } });
            CardDef piano = new CardDef { Id = "ordered-piano", Passive = true };
            TriggerDef used = new TriggerDef { Event = "onUse", Priority = "Medium",
                Filter = new TriggerFilter { SourceTag = "Friend" } };
            used.Effects.Add(new Effect { Kind = "haste", Seconds = 1, Target = new Target { Kind = "triggerSourceItem" } });
            piano.Triggers.Add(used);
            foreach (CardDef card in new[] { healer, friend, piano, opponent }) config.Cards[card.Id] = card;
            config.Player.Items.Add(new LoadoutItem { CardId = healer.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = friend.Id, Slot = 1 });
            config.Player.Items.Add(new LoadoutItem { CardId = piano.Id, Slot = 3 });
            config.Enemy.Items.Add(new LoadoutItem { CardId = opponent.Id, Slot = 0 });
            config.ReplayRandom.Orders.Add(new ReplayRandomOrder { Frame = 20,
                Sources = new List<string> { healer.Id, friend.Id, opponent.Id } });
            BattleResult result = BattleEngine.Simulate(config);
            int damage = result.Events.FindIndex(delegate(BattleEvent ev) {
                return ev.Kind == "damage" && ev.SourceItemId == opponent.Id; });
            int heal = result.Events.FindIndex(delegate(BattleEvent ev) {
                return ev.Kind == "heal" && ev.SourceItemId == healer.Id; });
            Assert.True(damage >= 0 && heal > damage);
            List<BattleEvent> hastes = result.Events.FindAll(delegate(BattleEvent ev) {
                return ev.Kind == "haste" && ev.SourceItemId == piano.Id; });
            Assert.Equal(2, hastes.Count);
            if (hastes.Count == 2)
            {
                Assert.Equal(healer.Id, hastes[0].TargetItemId);
                Assert.Near(1.05, hastes[0].Time);
                Assert.Equal(friend.Id, hastes[1].TargetItemId);
                Assert.Near(1.30, hastes[1].Time);
            }
            Assert.Near(80, result.HealthSnapshots[result.HealthSnapshots.Count - 1].Player.Hp);
        }
    }
}
