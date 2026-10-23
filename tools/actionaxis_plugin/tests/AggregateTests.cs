using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class AggregateTests
    {
        public static void Run()
        {
            List<BattleEvent> events = new List<BattleEvent>();
            events.Add(Ev(0.1, "p:0", "activate", 0));
            events.Add(Ev(0.1, "p:0", "damage", 5));
            events.Add(Ev(1.0, "p:0", "damage", 7));
            events.Add(Ev(1.0, "p:0", "activate", 0));
            events.Add(Ev(1.0, "p:0", "dotTick", 99));

            List<EffectStat> stats = new List<EffectStat>();
            stats.Add(St(0.1, "p:0", "haste", 3, 2));
            stats.Add(St(2.0, "p:0", "freeze", 2, 1));
            stats.Add(St(2.0, "p:0", "burn", 4, 1));

            BattleResult result = new BattleResult();
            result.Events = events;
            result.EffectStats = stats;
            result.EndTime = 60;

            List<string> ids = new List<string>();
            ids.Add("p:0");
            CardStatsSeries series = Aggregator.Build(result, ids, 0.1)["p:0"];

            var at0 = series.At(0);
            Assert.Equal(0, at0.ActivateCount);

            var at01 = series.At(0.1);
            Assert.Equal(1, at01.ActivateCount);
            Assert.Near(5, at01.Damage);
            Assert.Equal(1, at01.HasteCount);
            Assert.Near(3, at01.HasteSeconds);

            var at1 = series.At(1.0);
            Assert.Equal(2, at1.ActivateCount);
            Assert.Near(12, at1.Damage);
            Assert.Near(0, at1.FreezeSeconds);

            var at2 = series.At(2.0);
            Assert.Equal(1, at2.FreezeCount);
            Assert.Near(2, at2.FreezeSeconds);
            Assert.Near(4, at2.BurnStacks);

            var at60 = series.At(60);
            Assert.Equal(2, at60.ActivateCount);
            Assert.Near(12, at60.Damage);
            Assert.Near(4, at60.BurnStacks);

            var empty = Aggregator.Build(new BattleResult(), ids, 0.1)["p:0"].At(0);
            Assert.Equal(0, empty.ActivateCount);

            CooldownFollowsActualSimulation();
            CooldownFollowsControlAndCharge();
            CooldownTracksAmmoAndReload();
        }

        private static BattleConfig ConfigWith(CardDef card, double maxTime)
        {
            BattleConfig config = new BattleConfig();
            config.MaxTime = maxTime;
            config.PlayerMaxHp = 10000;
            config.EnemyMaxHp = 10000;
            config.Cards[card.Id] = card;
            LoadoutItem item = new LoadoutItem();
            item.CardId = card.Id;
            item.Slot = 2;
            config.Player.Items.Add(item);
            return config;
        }

        private static void CooldownFollowsActualSimulation()
        {
            CardDef card = new CardDef();
            card.Id = "cooldown";
            card.Cooldown = 2;
            BattleResult result = BattleEngine.Simulate(ConfigWith(card, 4));
            CardStatsSeries series = Aggregator.Build(result, 0.1)[card.Id];
            Assert.True(series.HasCooldown);
            Assert.Near(0, series.CooldownProgressAt(-1));
            Assert.Near(0.25, series.CooldownProgressAt(0.5));
            Assert.Near(0.975, series.CooldownProgressAt(1.95));
            Assert.Near(0, series.CooldownProgressAt(2));
            Assert.Near(0.025, series.CooldownProgressAt(2.05));
            Assert.Near(0, series.CooldownProgressAt(20));
            Assert.False(series.CooldownCompletedAt(1.9));
            Assert.True(series.CooldownCompletedAt(2));
            Assert.True(series.CooldownCompletedAt(2.05));
            Assert.False(series.CooldownCompletedAt(2.1));
            Assert.False(series.CooldownCompletedAt(20));

            series = Aggregator.Build(BattleEngine.Simulate(ConfigWith(card, 0.95)), 0.1)[card.Id];
            Assert.Near(0.475, series.CooldownProgressAt(0.95));
            Assert.Near(0.475, series.CooldownProgressAt(1));

            card.Passive = true;
            series = Aggregator.Build(BattleEngine.Simulate(ConfigWith(card, 1)), 0.1)[card.Id];
            Assert.False(series.HasCooldown);
            Assert.Near(0, series.CooldownProgressAt(0.5));
        }

        private static Effect Control(string kind, double seconds, string targetId)
        {
            Effect effect = new Effect();
            effect.Kind = kind;
            effect.Seconds = seconds;
            effect.Target = new Target();
            effect.Target.Kind = "selfItem";
            effect.Target.ItemId = targetId;
            return effect;
        }

        private static void CooldownFollowsControlAndCharge()
        {
            CardDef card = new CardDef();
            card.Id = "controlled";
            card.Cooldown = 4;
            TriggerDef start = new TriggerDef();
            start.Event = "atCombatStart";
            start.Effects.Add(Control("charge", 1.5, card.Id));
            start.Effects.Add(Control("freeze", 1, card.Id));
            start.Effects.Add(Control("haste", 3, card.Id));
            start.Effects.Add(Control("slow", 2, card.Id));
            card.Triggers.Add(start);
            // Self-charge after activation must survive the reset.
            card.Effects.Add(Control("charge", 0.5, card.Id));

            BattleResult result = BattleEngine.Simulate(ConfigWith(card, 3));
            CardStatsSeries series = Aggregator.Build(result, 0.1)[card.Id];
            Assert.Near(0.375, series.CooldownProgressAt(0));
            Assert.Near(0.375, series.CooldownProgressAt(0.55));
            Assert.Near(0.375, series.CooldownProgressAt(1));
            Assert.Near(0.5, series.CooldownProgressAt(1.5));
            Assert.Near(0.625, series.CooldownProgressAt(2));
            Assert.Near(0.875, series.CooldownProgressAt(2.5));
            Assert.Near(0.125, series.CooldownProgressAt(2.8));
            Assert.True(series.CooldownCompletedAt(2.8));
            Assert.Near(0.225, series.CooldownProgressAt(3));
            Assert.Equal(1, series.At(3).ActivateCount);
        }

        private static void CooldownTracksAmmoAndReload()
        {
            CardDef card = new CardDef();
            card.Id = "ammo";
            card.Cooldown = 1;
            card.AmmoMax = 1;
            BattleConfig config = ConfigWith(card, 4);
            CardDef reload = new CardDef();
            reload.Id = "reload";
            reload.Cooldown = 3;
            Effect effect = Control("reload", 0, card.Id);
            effect.AmountValue = 1;
            reload.Effects.Add(effect);
            config.Cards[reload.Id] = reload;
            LoadoutItem item = new LoadoutItem();
            item.CardId = reload.Id;
            item.Slot = 5;
            config.Player.Items.Add(item);

            BattleResult result = BattleEngine.Simulate(config);
            CardStatsSeries series = Aggregator.Build(result, 0.1)[card.Id];
            Assert.Near(1, series.CooldownProgressAt(2));
            Assert.Near(1, series.CooldownProgressAt(3));
            Assert.False(series.CooldownCompletedAt(3));
            Assert.Near(0, series.CooldownProgressAt(3.1));
            Assert.True(series.CooldownCompletedAt(3.1));
            Assert.Near(0.9, series.CooldownProgressAt(4));
            Assert.Equal(2, series.At(4).ActivateCount);
        }

        private static BattleEvent Ev(double time, string itemId, string kind, double value)
        {
            BattleEvent ev = new BattleEvent();
            ev.Time = time;
            ev.SourceItemId = itemId;
            ev.Kind = kind;
            ev.Value = value;
            ev.Side = Side.Player;
            return ev;
        }

        private static EffectStat St(
            double time,
            string itemId,
            string kind,
            double value,
            int targetCount)
        {
            EffectStat stat = new EffectStat();
            stat.Time = time;
            stat.SourceItemId = itemId;
            stat.SourceCardId = itemId;
            stat.Kind = kind;
            stat.Value = value;
            stat.TargetCount = targetCount;
            return stat;
        }
    }
}
