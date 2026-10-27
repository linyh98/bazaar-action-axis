using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class Replay4ReloadRegressionTests
    {
        public static void Run()
        {
            FullAmmoDoesNotPublishNativeReload();
            AcceptedAmmoPublishesExactlyOneReload();
            LegacyReloadBehaviourRemainsAvailable();
        }

        private static BattleContext Context(bool native)
        {
            BattleConfig config = new BattleConfig { NativeMechanics = native };
            CardDef card = new CardDef { Id = "replay4-ammo", Cooldown = 6, AmmoMax = 3 };
            config.Cards[card.Id] = card;
            config.Player.Items.Add(new LoadoutItem { CardId = card.Id, Slot = 0 });
            return BattleEngine.InitBattle(config);
        }

        private static void Reload(BattleContext ctx)
        {
            Effects.ResolveEffect(ctx, new Effect { Kind = "reload", Amount = new NumExpr { Value = 1 },
                Target = new Target { Kind = "selfItem" } }, ctx.Items[0], 1, 0, 1);
        }

        private static void FullAmmoDoesNotPublishNativeReload()
        {
            // dabb63f6: Powder Horn attempts to reload a full Cannon. The
            // native replay publishes neither Reload nor Ammo change here.
            BattleContext ctx = Context(true);
            Reload(ctx);
            Assert.Equal(3, ctx.Items[0].Ammo);
            Assert.Equal(0, ctx.Events.FindAll(delegate(BattleEvent e) { return e.Kind == "reload"; }).Count);
            Assert.Equal(0, System.Linq.Enumerable.Count(ctx.TriggerQueue,
                delegate(TriggerPayload p) { return p.Event == "onReload"; }));
        }

        private static void AcceptedAmmoPublishesExactlyOneReload()
        {
            BattleContext ctx = Context(true);
            ctx.Items[0].Ammo = 1;
            Reload(ctx);
            Assert.Equal(2, ctx.Items[0].Ammo);
            Assert.Equal(1, ctx.Events.FindAll(delegate(BattleEvent e) { return e.Kind == "reload"; }).Count);
            Assert.Near(1, ctx.Events.Find(delegate(BattleEvent e) { return e.Kind == "reload"; }).Value.Value);
            Assert.Equal(1, System.Linq.Enumerable.Count(ctx.TriggerQueue,
                delegate(TriggerPayload p) { return p.Event == "onReload"; }));
        }

        private static void LegacyReloadBehaviourRemainsAvailable()
        {
            BattleContext ctx = Context(false);
            Reload(ctx);
            Assert.Equal(1, ctx.Events.FindAll(delegate(BattleEvent e) { return e.Kind == "reload"; }).Count);
        }
    }
}
