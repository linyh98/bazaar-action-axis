using System.Linq;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class Replay4RepairRegressionTests
    {
        public static void Run()
        {
            RepairOnlyPublishesAnActualNativeStateChange();
        }

        private static void RepairOnlyPublishesAnActualNativeStateChange()
        {
            // d9710e92 frame 100: the native shield card cannot repair its
            // already enabled neighbours, and publishes no repair observers.
            BattleConfig config = new BattleConfig { NativeMechanics = true };
            CardDef card = new CardDef { Id = "repair-target", Cooldown = 10 };
            config.Cards[card.Id] = card;
            config.Player.Items.Add(new LoadoutItem { CardId = card.Id, Slot = 0 });
            BattleContext ctx = BattleEngine.InitBattle(config);
            Effect repair = new Effect { Kind = "repair", Target = new Target { Kind = "selfItem" } };
            Effects.ResolveEffect(ctx, repair, ctx.Items[0], 1, 0, 1);
            Assert.Equal(0, ctx.Events.Count(delegate(BattleEvent e) { return e.Kind == "repair"; }));
            Assert.Equal(0, ctx.TriggerQueue.Count(delegate(TriggerPayload p) { return p.Event == "onCardRepaired"; }));
            ctx.Items[0].Disabled = true;
            ctx.Items[0].CombatDisabled = true;
            Effects.ResolveEffect(ctx, repair, ctx.Items[0], 1.05, 0, 2);
            Assert.False(ctx.Items[0].Disabled);
            Assert.False(ctx.Items[0].CombatDisabled);
            Assert.Equal(1, ctx.Events.Count(delegate(BattleEvent e) { return e.Kind == "repair"; }));
            Assert.Equal(1, ctx.TriggerQueue.Count(delegate(TriggerPayload p) { return p.Event == "onCardRepaired"; }));
        }
    }
}
