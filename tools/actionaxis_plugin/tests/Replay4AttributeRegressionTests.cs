using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class Replay4AttributeRegressionTests
    {
        public static void Run()
        {
            MissingNativeDummyCounterStaysMissing();
            DeclaredZeroCounterStillIncrements();
            MissingNativeCombatAttributesStayMissing();
        }

        private static BattleContext Context(bool declared)
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true };
            CardDef card = new CardDef { Id = "dummy-counter", Passive = true,
                NativeTemplate = Json.Parse("{}"), NativeAttributesAreLive = true };
            if (declared) card.LiveAttributes["Custom_8"] = 0;
            config.Cards[card.Id] = card;
            config.Player.Items.Add(new LoadoutItem { CardId = card.Id, Slot = 0 });
            return BattleEngine.InitBattle(config);
        }

        private static void Increment(BattleContext ctx)
        {
            Effects.ResolveEffect(ctx, new Effect { Kind = "modifyAttribute", Attribute = "Custom_8", Operation = "Add",
                Amount = new NumExpr { Value = 1 }, Target = new Target { Kind = "selfItem" } }, ctx.Items[0], 5, 0, 1);
        }

        private static void MissingNativeDummyCounterStaysMissing()
        {
            // Paper Airplane's dummy ability executes at frames 100/200/300,
            // but Custom_8 is absent in both its live opening and every native
            // attribute update. Executing an action cannot invent that state.
            BattleContext ctx = Context(false);
            Increment(ctx);
            Assert.False(ItemAttributes.Has(ctx.Items[0], "Custom_8"));
            Assert.Near(0, ItemAttributes.Get(ctx.Items[0], "Custom_8"));
            Assert.Equal(1, ctx.Events.FindAll(delegate(BattleEvent e) { return e.Kind == "modifyAttribute"; }).Count);
        }

        private static void DeclaredZeroCounterStillIncrements()
        {
            BattleContext ctx = Context(true);
            Increment(ctx);
            Assert.True(ItemAttributes.Has(ctx.Items[0], "Custom_8"));
            Assert.Near(1, ItemAttributes.Get(ctx.Items[0], "Custom_8"));
        }

        private static void MissingNativeCombatAttributesStayMissing()
        {
            // b60fa630 / ee4190aa: global Damage/Shield gains execute on a
            // utility item too, but only declared attributes can be changed.
            foreach (string attribute in new[] { "DamageAmount", "ShieldApplyAmount", "BurnApplyAmount" })
            {
                BattleContext ctx = Context(false);
                Effect gain = new Effect { Kind = "modifyAttribute", Attribute = attribute, Operation = "Add",
                    Amount = new NumExpr { Value = 6 }, Target = new Target { Kind = "selfItem" } };
                Effects.ResolveEffect(ctx, gain, ctx.Items[0], 4.05, 0, 1);
                Assert.False(ItemAttributes.Has(ctx.Items[0], attribute));
                ctx.Items[0].Card.LiveAttributes[attribute] = 0;
                Effects.ResolveEffect(ctx, gain, ctx.Items[0], 4.10, 0, 2);
                Assert.Near(6, ItemAttributes.Get(ctx.Items[0], attribute));
            }
        }
    }
}
