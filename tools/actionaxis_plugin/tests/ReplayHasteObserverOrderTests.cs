using System;
using System.Linq;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class ReplayHasteObserverOrderTests
    {
        public static void Run()
        {
            MediumHasteGrowthFollowsAlreadyQueuedCast();
            HighHasteGrowthStillPrecedesAlreadyQueuedCast();
            HasteGrowthStillRunsWithoutAQueuedCast();
        }

        private static BattleResult Simulate(double regenCooldown, string observerPriority)
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 1.1,
                PlayerMaxHp = 1000, EnemyMaxHp = 1000 };
            CardDef haste = new CardDef { Id = "haste-order-emitter", Cooldown = 1 };
            haste.Effects.Add(new Effect { Kind = "haste", Priority = "Medium",
                Seconds = 1, Target = new Target { Kind = "selfItem" } });
            CardDef regen = new CardDef { Id = "haste-order-regen", Cooldown = regenCooldown };
            regen.LiveAttributes["RegenApplyAmount"] = 10;
            regen.Effects.Add(new Effect { Kind = "regen", Priority = "Medium",
                Amount = new NumExpr { Type = "cardAttribute", Attribute = "RegenApplyAmount" },
                Target = new Target { Kind = "selfHero" } });
            CardDef observer = new CardDef { Id = "haste-order-observer", Passive = true };
            TriggerDef trigger = new TriggerDef { Event = "onHaste", Priority = observerPriority };
            trigger.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "RegenApplyAmount", Operation = "Add",
                Amount = new NumExpr { Value = 2 }, Target = new Target { Kind = "selfItem", ItemId = regen.Id } });
            observer.Triggers.Add(trigger);
            foreach (CardDef card in new[] { haste, regen, observer }) config.Cards[card.Id] = card;
            config.Player.Items.Add(new LoadoutItem { CardId = haste.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = regen.Id, Slot = 1 });
            config.Player.Items.Add(new LoadoutItem { CardId = observer.Id, Slot = 2 });
            return BattleEngine.Simulate(config);
        }

        private static void MediumHasteGrowthFollowsAlreadyQueuedCast()
        {
            BattleResult result = Simulate(1.05, "Medium");
            Assert.Equal(0, result.Warnings.Count);
            BattleEvent regen = result.Events.Single(delegate(BattleEvent ev) { return ev.Kind == "regen"; });
            BattleEvent growth = result.Events.Single(delegate(BattleEvent ev) { return ev.Kind == "modifyAttribute"; });
            Assert.Near(1.05, regen.Time);
            Assert.Near(1.05, growth.Time);
            Assert.Near(10, regen.Value.Value);
            Assert.True(result.Events.IndexOf(regen) < result.Events.IndexOf(growth));
            ItemSnapshot final = result.ItemSnapshots.Last(delegate(ItemSnapshot snapshot) { return snapshot.ItemId == "haste-order-regen"; });
            Assert.Near(12, final.Attributes["RegenApplyAmount"]);
        }

        private static void HighHasteGrowthStillPrecedesAlreadyQueuedCast()
        {
            BattleResult result = Simulate(1.05, "High");
            Assert.Equal(0, result.Warnings.Count);
            BattleEvent regen = result.Events.Single(delegate(BattleEvent ev) { return ev.Kind == "regen"; });
            BattleEvent growth = result.Events.Single(delegate(BattleEvent ev) { return ev.Kind == "modifyAttribute"; });
            Assert.Near(1.05, regen.Time);
            Assert.Near(1.05, growth.Time);
            Assert.Near(12, regen.Value.Value);
            Assert.True(result.Events.IndexOf(growth) < result.Events.IndexOf(regen));
        }

        private static void HasteGrowthStillRunsWithoutAQueuedCast()
        {
            BattleResult result = Simulate(2, "Medium");
            Assert.Equal(0, result.Warnings.Count);
            Assert.Equal(0, result.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "regen"; }));
            BattleEvent growth = result.Events.Single(delegate(BattleEvent ev) { return ev.Kind == "modifyAttribute"; });
            Assert.Near(1.05, growth.Time);
            ItemSnapshot final = result.ItemSnapshots.Last(delegate(ItemSnapshot snapshot) { return snapshot.ItemId == "haste-order-regen"; });
            Assert.Near(12, final.Attributes["RegenApplyAmount"]);
        }
    }
}
