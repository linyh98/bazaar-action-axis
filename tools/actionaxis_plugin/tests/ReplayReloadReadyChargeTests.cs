using System;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class ReplayReloadReadyChargeTests
    {
        public static void Run()
        {
            ReloadedEmptyItemStartsItsFullCooldown();
            AlreadyUsableReadyItemKeepsChargeCarry();
            EarlierReloadDoesNotBlockLaterChargeCarry();
            ChargeAfterResetStillReducesCooldown();
            EmptyReadyItemCannotAccumulateChargeForLaterReload();
        }

        private static BattleConfig Board(int initialAmmo)
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 0.1,
                PlayerMaxHp = 1000, EnemyMaxHp = 1000 };
            CardDef ammo = new CardDef { Id = "ready-ammo", Cooldown = 3, AmmoMax = 3, StartReady = true };
            TriggerDef empty = new TriggerDef { Event = "atCombatStart" };
            empty.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "Ammo", Operation = "Set",
                Amount = new NumExpr { Value = initialAmmo }, Target = new Target { Kind = "selfItem" } });
            ammo.Triggers.Add(empty);
            CardDef reloader = new CardDef { Id = "ready-reloader", Passive = true };
            TriggerDef start = new TriggerDef { Event = "atCombatStart" };
            start.Effects.Add(Reload());
            start.Effects.Add(Charge());
            reloader.Triggers.Add(start);
            config.Cards[ammo.Id] = ammo;
            config.Cards[reloader.Id] = reloader;
            config.Player.Items.Add(new LoadoutItem { CardId = ammo.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = reloader.Id, Slot = 2 });
            return config;
        }

        private static Effect Reload()
        {
            return new Effect { Kind = "reload", AmountValue = -1,
                Target = new Target { Kind = "selfItem", ItemId = "ready-ammo" } };
        }

        private static Effect Charge()
        {
            return new Effect { Kind = "charge", Seconds = 1,
                Target = new Target { Kind = "selfItem", ItemId = "ready-ammo" } };
        }

        private static void ReloadedEmptyItemStartsItsFullCooldown()
        {
            BattleResult result = BattleEngine.Simulate(Board(0));
            ItemSnapshot reset = result.ItemSnapshots.Find(delegate(ItemSnapshot row) {
                return row.ItemId == "ready-ammo" && Math.Abs(row.Time) < 1e-9; });
            Assert.True(reset != null);
            Assert.Near(3, reset.CooldownRemaining);
            Assert.Equal(2, reset.Ammo);
            Assert.Equal(1, result.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "charge"; }).Count);
        }

        private static void AlreadyUsableReadyItemKeepsChargeCarry()
        {
            BattleResult result = BattleEngine.Simulate(Board(1));
            ItemSnapshot reset = result.ItemSnapshots.Find(delegate(ItemSnapshot row) {
                return row.ItemId == "ready-ammo" && Math.Abs(row.Time) < 1e-9; });
            Assert.True(reset != null);
            Assert.Near(2, reset.CooldownRemaining);
        }

        private static void EarlierReloadDoesNotBlockLaterChargeCarry()
        {
            BattleContext ctx = BattleEngine.InitBattle(Board(0));
            InternalItem target = ctx.Items.Find(delegate(InternalItem item) { return item.Id == "ready-ammo"; });
            InternalItem source = ctx.Items.Find(delegate(InternalItem item) { return item.Id == "ready-reloader"; });
            target.Ammo = 0;
            Effects.ResolveEffect(ctx, Reload(), source, 1, 0, null);
            Effects.ResolveEffect(ctx, Charge(), source, 1.5, 0, null);
            Assert.Near(1, target.PendingChargeOnReset);
        }

        private static void ChargeAfterResetStillReducesCooldown()
        {
            BattleContext ctx = BattleEngine.InitBattle(Board(0));
            InternalItem target = ctx.Items.Find(delegate(InternalItem item) { return item.Id == "ready-ammo"; });
            InternalItem source = ctx.Items.Find(delegate(InternalItem item) { return item.Id == "ready-reloader"; });
            target.Ammo = 0;
            Effects.ResolveEffect(ctx, Reload(), source, 1, 0, null);
            target.Cooldown = 3;
            Effects.ResolveEffect(ctx, Charge(), source, 1, 0, null);
            Assert.Near(2, target.Cooldown);
            Assert.Near(0, target.PendingChargeOnReset);
        }

        private static void EmptyReadyItemCannotAccumulateChargeForLaterReload()
        {
            BattleContext ctx = BattleEngine.InitBattle(Board(0));
            InternalItem target = ctx.Items.Find(delegate(InternalItem item) { return item.Id == "ready-ammo"; });
            InternalItem source = ctx.Items.Find(delegate(InternalItem item) { return item.Id == "ready-reloader"; });
            target.Ammo = 0;
            Effects.ResolveEffect(ctx, Charge(), source, 1, 0, null);
            Assert.Near(0, target.PendingChargeOnReset);
            Assert.Near(0, target.Cooldown);
        }
    }
}
