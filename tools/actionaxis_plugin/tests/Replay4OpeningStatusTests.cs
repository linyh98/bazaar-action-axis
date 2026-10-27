using System;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class Replay4OpeningStatusTests
    {
        public static void Run()
        {
            // fb7f06c3 frame 0: a 2000ms opening Haste action is already at
            // 1950ms in the first native snapshot, unlike later-frame actions.
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 0 };
            CardDef card = new CardDef { Id = "opening-status", Cooldown = 10 };
            TriggerDef start = new TriggerDef { Event = "atCombatStart" };
            foreach (string kind in new[] { "haste", "slow", "freeze" })
                start.Effects.Add(new Effect { Kind = kind, Seconds = 2, Target = new Target { Kind = "selfItem" } });
            card.Triggers.Add(start);
            config.Cards[card.Id] = card;
            config.Player.Items.Add(new LoadoutItem { CardId = card.Id, Slot = 0 });
            BattleResult result = BattleEngine.Simulate(config);
            ItemSnapshot frame = result.ItemSnapshots.Find(delegate(ItemSnapshot s) { return Math.Abs(s.Time) < 1e-9; });
            Assert.True(frame != null);
            if (frame != null)
                foreach (string attribute in new[] { "Haste", "Slow", "Freeze" }) Assert.Near(1950, frame.Attributes[attribute]);
            BattleContext normal = BattleEngine.InitBattle(config);
            Effects.ResolveEffect(normal, new Effect { Kind = "haste", Seconds = 2, Target = new Target { Kind = "selfItem" } },
                normal.Items[0], 0.5, 0, 1);
            Assert.Near(2.5, normal.Items[0].HasteUntil);
            OpeningObserversWaitUntilNextFrame();
        }

        private static void OpeningObserversWaitUntilNextFrame()
        {
            // fb7f06c3: Boombox Hastes in frame 0; Sharkray's Low observer
            // applies Damage gains in frames 1, 6, 11... rather than 0, 5, 10.
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 0.1 };
            CardDef card = new CardDef { Id = "opening-observer", Passive = true };
            TriggerDef start = new TriggerDef { Event = "atCombatStart" };
            start.Effects.Add(new Effect { Kind = "haste", Seconds = 2, Target = new Target { Kind = "selfItem" } });
            TriggerDef observer = new TriggerDef { Event = "onHaste", Priority = "Low" };
            observer.Effects.Add(new Effect { Kind = "shield", Amount = new NumExpr { Value = 10 }, Target = new Target { Kind = "selfHero" } });
            card.Triggers.Add(start); card.Triggers.Add(observer);
            config.Cards[card.Id] = card;
            config.Player.Items.Add(new LoadoutItem { CardId = card.Id, Slot = 0 });
            BattleResult result = BattleEngine.Simulate(config);
            BattleEvent gain = result.Events.Find(delegate(BattleEvent ev) { return ev.Kind == "shield"; });
            Assert.True(gain != null);
            if (gain != null) Assert.Near(0.05, gain.Time);
        }
    }
}
