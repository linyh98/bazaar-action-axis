using System;
using System.Linq;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class Replay4ImmediateActionTests
    {
        public static void Run()
        {
            // Healthy Jolt in b61c28ab: the Immediate counter runs on heal
            // frame 76; its Medium Haste reaction runs on frame 77.
            BattleConfig config = new BattleConfig { NativeMechanics = true, PlayerMaxHp = 400 };
            CardDef healer = new CardDef { Id = "immediate-healer", Passive = true };
            CardDef watcher = new CardDef { Id = "heal-watcher", Passive = true };
            watcher.LiveAttributes["Custom_0"] = 0;
            TriggerDef immediate = new TriggerDef { Event = "onOverHeal", Priority = "Immediate" };
            immediate.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "Custom_0", Operation = "Add",
                Amount = new NumExpr { Value = 1 }, Target = new Target { Kind = "selfItem" } });
            TriggerDef delayed = new TriggerDef { Event = "onOverHeal", Priority = "Medium" };
            delayed.Effects.Add(new Effect { Kind = "shield", Amount = new NumExpr { Value = 7 }, Target = new Target { Kind = "selfHero" } });
            watcher.Triggers.Add(immediate); watcher.Triggers.Add(delayed);
            config.Cards[healer.Id] = healer; config.Cards[watcher.Id] = watcher;
            config.Player.Items.Add(new LoadoutItem { CardId = healer.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = watcher.Id, Slot = 1 });
            BattleContext ctx = BattleEngine.InitBattle(config);
            Effects.ResolveEffect(ctx, new Effect { Kind = "heal", Amount = new NumExpr { Value = 50 },
                Target = new Target { Kind = "selfHero" } }, ctx.Items[0], 3.8, 0, 1);
            Assert.Near(1, ItemAttributes.Get(ctx.Items[1], "Custom_0"));
            Assert.Equal(0, ctx.Events.Count(delegate(BattleEvent e) { return e.Kind == "shield"; }));
            Triggers.DrainNativeFrame(ctx, 3.85);
            Assert.Near(1, ItemAttributes.Get(ctx.Items[1], "Custom_0"));
            BattleEvent shield = ctx.Events.Find(delegate(BattleEvent e) { return e.Kind == "shield"; });
            Assert.True(shield != null);
            if (shield != null) Assert.Near(3.85, shield.Time);
        }
    }
}
