using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class LiveItemOverridesTests
    {
        public static void Run()
        {
            PreservesDistinctAmountsOnChemsnail();
            PreservesDistinctControlDurations();
            CardDef card = new CardDef();
            card.Id = "weapon";
            card.Cooldown = 1;
            Effect damage = new Effect();
            damage.Kind = "damage";
            damage.Amount = new NumExpr();
            damage.Amount.Value = 10;
            card.Effects.Add(damage);
            Effect oldCrit = new Effect(); oldCrit.Kind = "crit"; oldCrit.Chance = .2;
            card.Effects.Add(oldCrit);
            TriggerDef trigger = new TriggerDef();
            trigger.Event = "atCombatStart";
            Effect shield = new Effect(); shield.Kind = "shield";
            shield.Amount = new NumExpr(); shield.Amount.Value = 5;
            trigger.Effects.Add(shield);
            card.Triggers.Add(trigger);
            Dictionary<string, int> attributes = new Dictionary<string, int>();
            attributes["DamageAmount"] = 25;
            attributes["ShieldApplyAmount"] = 30;
            attributes["CritChance"] = 100;
            attributes["Lifesteal"] = 50;
            CardDef live = LiveItemOverrides.Apply(card, attributes);
            Assert.Near(10, damage.Amount.Value);
            Assert.Near(5, shield.Amount.Value);
            Assert.Near(25, live.Effects[0].Amount.Value);
            Assert.Near(30, live.Triggers[0].Effects[0].Amount.Value);
            BattleConfig config = new BattleConfig();
            config.PlayerMaxHp = config.EnemyMaxHp = 1000;
            config.MaxTime = 1;
            config.Cards[live.Id] = live;
            LoadoutItem item = new LoadoutItem(); item.CardId = live.Id;
            config.Player.Items.Add(item);
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Near(50, result.Totals.Player.Damage);
            Assert.Near(60, result.Totals.Player.Shield);
            attributes.Remove("Lifesteal");
            attributes["LifestealPercentage"] = 50;
            config.Cards[live.Id] = LiveItemOverrides.Apply(card, attributes);
            config.PlayerStartHp = 500;
            Assert.Near(25, BattleEngine.Simulate(config).Totals.Player.Heal);
            attributes["Lifesteal"] = 0;
            config.Cards[live.Id] = LiveItemOverrides.Apply(card, attributes);
            Assert.Near(0, BattleEngine.Simulate(config).Totals.Player.Heal);
            CardDef second = LiveItemOverrides.Apply(card, new Dictionary<string, int>());
            Assert.Near(10, second.Effects[0].Amount.Value);
            attributes["DamageAmount"] = 0;
            Assert.Near(0, LiveItemOverrides.Apply(card, attributes).Effects[0].Amount.Value);
        }

        private static void PreservesDistinctAmountsOnChemsnail()
        {
            CardCatalog catalog = CardCatalog.Load("src/data/cards.json");
            LiveCardEntry entry = new LiveCardEntry
            {
                TemplateId = "0e02eed0-3d9b-4651-8ce8-a48ee1c59fdf", Tier = "Silver"
            };
            entry.Attributes["PoisonApplyAmount"] = 10;
            entry.Attributes["SlowAmount"] = 2000;
            entry.Attributes["SlowTargets"] = 1;
            MappedBoard mapped = CardMapper.Build(new List<LiveCardEntry> { entry }, catalog, 1000, 100000, 6);
            Assert.Equal(1, mapped.LiveAttributeWarnings.Count);
            Assert.True(mapped.LiveAttributeWarnings[0].Contains("PoisonApplyAmount"));
            CardDef card = mapped.Config.Cards[mapped.Config.Player.Items[0].CardId];
            Assert.Near(10, card.Effects[1].Amount.Value);
            Assert.Near(2, card.Triggers[0].Effects[0].Amount.Value);
            CardDef opponent = new CardDef { Id = "slow-target", Cooldown = 20 };
            mapped.Config.Cards[opponent.Id] = opponent;
            mapped.Config.Enemy.Items.Add(new LoadoutItem { CardId = opponent.Id });
            BattleResult result = BattleEngine.Simulate(mapped.Config);
            CardStats stats = Aggregator.Build(result, new string[] { result.PlayerItems[0].Id }, .1)[result.PlayerItems[0].Id].At(6);
            // One activation applies 10 directly and 2 through onSlow. Replacing
            // every poison constant with the live attribute incorrectly made 20.
            Assert.Equal(1, stats.ActivateCount);
            Assert.Near(12, stats.PoisonStacks);
            Assert.Near(2, catalog.Find(entry.TemplateId, entry.Tier).Triggers[0].Effects[0].Amount.Value);
            // Distinct values in separate triggers are also ambiguous.
            entry.TemplateId = "f0a58209-1a26-42ac-a243-7c2fd9d6ce33";
            entry.Tier = "Gold";
            entry.Attributes["PoisonApplyAmount"] = 15;
            mapped = CardMapper.Build(new List<LiveCardEntry> { entry }, catalog, 1000, 100000, 6);
            card = mapped.Config.Cards[mapped.Config.Player.Items[0].CardId];
            Assert.Near(15, card.Triggers[0].Effects[0].Amount.Value);
            Assert.Near(10, card.Triggers[1].Effects[0].Amount.Value);
            Assert.Equal(1, mapped.LiveAttributeWarnings.Count);
        }

        private static void PreservesDistinctControlDurations()
        {
            CardDef card = new CardDef { Id = "control" };
            card.Effects.Add(new Effect { Kind = "freeze", Seconds = 1, Count = 1 });
            card.Effects.Add(new Effect { Kind = "haste", Seconds = 1, Count = 1 });
            TriggerDef trigger = new TriggerDef { Event = "atCombatStart" };
            trigger.Effects.Add(new Effect { Kind = "freeze", Seconds = .5, Count = 2 });
            card.Triggers.Add(trigger);
            Dictionary<string, int> attributes = new Dictionary<string, int>
            {
                { "FreezeAmount", 4000 }, { "FreezeTargets", 3 }, { "HasteAmount", 2000 }
            };
            List<string> warnings = new List<string>();
            CardDef live = LiveItemOverrides.Apply(card, attributes, warnings);
            Assert.Near(1, live.Effects[0].Seconds);
            Assert.Equal(1, live.Effects[0].Count.Value);
            Assert.Near(.5, live.Triggers[0].Effects[0].Seconds);
            Assert.Equal(2, live.Triggers[0].Effects[0].Count.Value);
            Assert.Near(2, live.Effects[1].Seconds);
            Assert.Equal(1, warnings.Count);
            Assert.True(warnings[0].Contains("freeze"));
            Assert.Near(1, card.Effects[1].Seconds);
        }
    }
}
