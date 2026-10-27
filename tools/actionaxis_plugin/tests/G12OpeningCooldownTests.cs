using System;
using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // Fresh forecasts start from CooldownMax. Only replaying a captured combat
    // state should use the snapshot's remaining Cooldown, including zero.
    public static class G12OpeningCooldownTests
    {
        public static void Run()
        {
            FreshBattleIgnoresSnapshotCooldown();
            FreshNativeBoardWaitsForEachFullCooldown();
            MapsRemainingCooldownIntoLoadout();
            InitBattleUsesRemainingCooldown();
            ReplayZeroCooldownActsOnFirstNativeFrame();
            StartReadyStillWins();
        }

        private static BattleConfig Board(int remainingMs, bool startReady)
        {
            CardDef card = new CardDef
            {
                Id = "opening-cd", BaseItemId = "opening-cd", Name = "Opening CD",
                Tier = "Bronze", Size = "Small", Cooldown = 10, StartReady = startReady
            };
            LiveCardEntry entry = new LiveCardEntry
            {
                InstanceId = "opening-cd-instance",
                UseInstanceIdAsId = true,
                TemplateId = "opening-cd",
                Tier = "Bronze",
                Socket = 0,
                Size = "Small"
            };
            entry.Attributes["CooldownMax"] = 10000;
            entry.Attributes["Cooldown"] = remainingMs;
            CardCatalog catalog = CardCatalog.FromCards(new List<CardDef> { card });
            return CardMapper.Build(new List<LiveCardEntry> { entry }, catalog, 1000, 1000, 20, true).Config;
        }

        private static void FreshBattleIgnoresSnapshotCooldown()
        {
            foreach (int remainingMs in new[] { 0, 3250 })
            {
                LiveCardEntry entry = new LiveCardEntry
                {
                    TemplateId = "fresh-cd", Tier = "Bronze", Socket = 0, Size = "Small"
                };
                entry.Attributes["CooldownMax"] = 7000;
                entry.Attributes["Cooldown"] = remainingMs;
                CardCatalog catalog = CardCatalog.FromCards(new List<CardDef>
                {
                    new CardDef { Id = "fresh-cd", BaseItemId = "fresh-cd", Cooldown = 10 }
                });
                BattleConfig config = CardMapper.Build(new List<LiveCardEntry> { entry }, catalog,
                    1000, 1000, 20).Config;
                Assert.False(config.Player.Items[0].StartCooldown.HasValue);
                BattleContext ctx = BattleEngine.InitBattle(config);
                Assert.Near(7, ctx.Items[0].Cooldown);
                Assert.Near(7, ctx.Items[0].CooldownMax);
            }
        }

        private static void FreshNativeBoardWaitsForEachFullCooldown()
        {
            int[] cooldowns = { 6930, 4000, 5000, 6000, 7000, 5000, 5000, 4000 };
            List<CardDef> cards = new List<CardDef>();
            List<LiveCardEntry> entries = new List<LiveCardEntry>();
            for (int i = 0; i < cooldowns.Length; i++)
            {
                string id = "fresh-" + i;
                cards.Add(new CardDef { Id = id, BaseItemId = id, Cooldown = 10 });
                LiveCardEntry entry = new LiveCardEntry
                {
                    InstanceId = id, UseInstanceIdAsId = true, TemplateId = id,
                    Tier = "Bronze", Socket = i, Size = "Small"
                };
                entry.Attributes["CooldownMax"] = cooldowns[i];
                entry.Attributes["Cooldown"] = 0;
                entries.Add(entry);
            }
            BattleConfig config = CardMapper.Build(entries, CardCatalog.FromCards(cards),
                1000, 1000, 7.1).Config;
            config.NativeMechanics = true;
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);
            Assert.Equal(0, result.Events.FindAll(delegate(BattleEvent ev)
            {
                return ev.Kind == "activate" && ev.Time <= 0.05;
            }).Count);
            for (int i = 0; i < cooldowns.Length; i++)
            {
                string id = entries[i].InstanceId;
                List<BattleEvent> uses = result.Events.FindAll(delegate(BattleEvent ev)
                {
                    return ev.Kind == "activate" && ev.SourceItemId == id;
                });
                Assert.Equal(1, uses.Count);
                if (uses.Count > 0)
                    Assert.Near(Math.Ceiling(cooldowns[i] / 50.0) * 0.05, uses[0].Time);
            }
        }

        private static void MapsRemainingCooldownIntoLoadout()
        {
            foreach (int remainingMs in new[] { 0, 3250 })
            {
                BattleConfig config = Board(remainingMs, false);
                Assert.Equal(1, config.Player.Items.Count);
                Assert.True(config.Player.Items[0].StartCooldown.HasValue);
                if (config.Player.Items[0].StartCooldown.HasValue)
                    Assert.Near(remainingMs / 1000.0, config.Player.Items[0].StartCooldown.Value);
            }
        }

        private static void InitBattleUsesRemainingCooldown()
        {
            foreach (int remainingMs in new[] { 0, 3250 })
            {
                BattleConfig config = Board(remainingMs, false);
                BattleContext ctx = BattleEngine.InitBattle(config);
                Assert.Near(remainingMs / 1000.0, ctx.Items[0].Cooldown);
                Assert.Near(10, ctx.Items[0].CooldownMax);
            }
        }

        private static void ReplayZeroCooldownActsOnFirstNativeFrame()
        {
            BattleConfig config = Board(0, false);
            config.NativeMechanics = true;
            config.MaxTime = 0.1;
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);
            List<BattleEvent> uses = result.Events.FindAll(delegate(BattleEvent ev)
            {
                return ev.Kind == "activate";
            });
            Assert.Equal(1, uses.Count);
            if (uses.Count > 0) Assert.Near(0.05, uses[0].Time);
        }

        private static void StartReadyStillWins()
        {
            BattleConfig config = Board(3250, true);
            BattleContext ctx = BattleEngine.InitBattle(config);
            Assert.Near(0, ctx.Items[0].Cooldown);
        }
    }
}
