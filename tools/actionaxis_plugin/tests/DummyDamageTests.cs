using System;
using System.Collections.Generic;
using System.Linq;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class DummyDamageTests
    {
        public static void Run()
        {
            DamageAtPlayheadCountsTerminalHitsOnce();
            DamageSeriesMatchesEngineAndClampsOverkill();
            DummyPreservesTargetsWithoutEnemyActions();
            RandomBoardFitsAndRepeatsWithSeed();
            OutputExcludesHealingSelfDamageMitigationAndOverkill();
        }

        private static void OutputExcludesHealingSelfDamageMitigationAndOverkill()
        {
            BattleResult result = new BattleResult { EnemyStartHp = 100 };
            BattleEvent shield = Event(0, "shield", 20, null, 20, null);
            shield.Side = Side.Enemy;
            result.Events.Add(shield);
            BattleEvent hit = Event(1, "damage", 30, 90, 0, null);
            hit.Absorbed = 20;
            hit.ActualDamage = 30;
            result.Events.Add(hit);
            BattleEvent heal = Event(2, "heal", 30, 100, null, null);
            heal.Side = Side.Enemy;
            result.Events.Add(heal);
            BattleEvent regenMarker = Event(2, "dotTick", 30, 100, null, "regen");
            regenMarker.Side = Side.Enemy;
            result.Events.Add(regenMarker);
            BattleEvent selfHit = Event(3, "damage", 10, 90, 0, null);
            selfHit.Side = Side.Enemy;
            result.Events.Add(selfHit);
            BattleEvent moreShield = Event(3.5, "shield", 4, null, 4, null);
            moreShield.Side = Side.Enemy;
            result.Events.Add(moreShield);
            BattleEvent mitigated = Event(4, "dotTick", 9, 90, 0, "burn");
            mitigated.Absorbed = 4;
            mitigated.ActualDamage = 4;
            result.Events.Add(mitigated);
            BattleEvent lethal = Event(5, "damage", 200, 0, 0, null);
            lethal.ActualDamage = 90;
            result.Events.Add(lethal);
            BattleDamageSeries series = Aggregator.BuildDamage(result, 100);
            Assert.Near(30, series.At(3).OutputDamage);
            Assert.Near(10, series.At(3).EnemyHealing);
            Assert.Near(10, series.DpsAt(3));
            Assert.Near(34, series.At(4).OutputDamage);
            Assert.Near(124, series.At(5).OutputDamage);
            Assert.Near(24.8, series.DpsAt(5));
            Assert.Near(0, series.DpsAt(0));
            Assert.Near(0, series.DpsAt(double.NaN));
            Assert.Near(30, series.At(1).OutputDamage); // Scrubbing cannot retain later hits.

            CardDef card = new CardDef { Id = "overkill", Cooldown = 1 };
            card.Effects.Add(Amount("damage", 50));
            BattleConfig config = new BattleConfig { NativeMechanics = true, EnemyMaxHp = 7, MaxTime = 2 };
            config.Cards[card.Id] = card;
            config.Player.Items.Add(new LoadoutItem { CardId = card.Id });
            result = BattleEngine.Simulate(config);
            Assert.Near(7, Aggregator.BuildDamage(result, 7).At(1).OutputDamage);
            Assert.Near(7, Aggregator.Build(result, 0.05)[card.Id].At(1).Damage);
        }

        private static BattleEvent Event(double time, string kind, double value, double? hp, double? shield, string note)
        {
            return new BattleEvent
            {
                Time = time, Kind = kind, Value = value, HpAfter = hp, ShieldAfter = shield,
                Note = note, Side = Side.Player, TargetSide = Side.Enemy
            };
        }

        private static void DamageAtPlayheadCountsTerminalHitsOnce()
        {
            BattleResult result = new BattleResult { EnemyStartHp = 100 };
            BattleEvent shield = Event(0, "shield", 20, null, 20, null);
            shield.Side = Side.Enemy;
            result.Events.Add(shield);
            result.Events.Add(Event(1, "crit", 30, null, null, null));
            BattleEvent hit = Event(1, "damage", 30, 90, 0, "critical");
            hit.Absorbed = 20;
            result.Events.Add(hit);
            result.Events.Add(Event(1, "burn", 6, null, null, null));
            result.Events.Add(Event(1, "poison", 4, null, null, null));
            result.Events.Add(Event(2, "dotTick", 6, 84, 0, "burn"));
            result.Events.Add(Event(2, "dotTick", 4, 80, 0, "poison"));
            BattleEvent heal = Event(3, "heal", 5, 85, null, null);
            heal.Side = Side.Enemy;
            result.Events.Add(heal);
            BattleEvent regen = Event(3, "dotTick", 5, 85, null, "regen");
            regen.Side = Side.Enemy;
            result.Events.Add(regen);
            BattleEvent skill = Event(4, "damage", 10, 75, 0, null);
            skill.SourceItemId = "skill:example";
            result.Events.Add(skill);
            BattleEvent enemyHit = Event(5, "damage", 100, 0, 0, null);
            enemyHit.Side = Side.Enemy;
            enemyHit.TargetSide = Side.Player;
            result.Events.Add(enemyHit);
            result.Totals.Player.Damage = 99999; // The final total must not leak to earlier times.

            BattleDamageSeries series = Aggregator.BuildDamage(result, 1000);
            Assert.Near(100, series.At(-1).EnemyHp);
            Assert.Near(20, series.At(0).EnemyShield);
            Assert.Near(0, series.At(0.999).Damage);
            Assert.Near(30, series.At(1).Damage);
            Assert.Near(10, series.At(1).EffectiveDamage);
            Assert.Near(90, series.At(1).EnemyHp);
            Assert.Near(40, series.At(2).Damage);
            Assert.Near(20, series.At(2).EffectiveDamage);
            Assert.Near(6, series.At(2).BurnDamage);
            Assert.Near(4, series.At(2).PoisonDamage);
            Assert.Near(85, series.At(3).EnemyHp);
            Assert.Near(40, series.At(3).Damage);
            Assert.Near(50, series.At(4).Damage);
            Assert.Near(30, series.At(4).EffectiveDamage);
            Assert.Near(40, series.At(4).DirectDamage);
            Assert.Near(50, series.At(30).Damage);
            Assert.Near(75, series.At(30).EnemyHp);
            Assert.Near(30, series.At(1).Damage); // Seeking backward recomputes correctly.
            BattleDamageSnapshot mutable = series.At(2);
            mutable.Damage = -1;
            Assert.Near(40, series.At(2).Damage);
            Assert.Near(0, series.At(double.NaN).Damage);
            Assert.Near(0, series.At(double.NegativeInfinity).Damage);
            Assert.Near(50, series.At(double.PositiveInfinity).Damage);
            Assert.Near(500, Aggregator.BuildDamage(null, 500).At(15).EnemyHp);
        }

        private static Effect Amount(string kind, double amount)
        {
            return new Effect { Kind = kind, Amount = new NumExpr { Value = amount } };
        }

        private static void DamageSeriesMatchesEngineAndClampsOverkill()
        {
            CardDef card = new CardDef { Id = "attacker", Cooldown = 2 };
            card.Effects.Add(Amount("damage", 5));
            card.Effects.Add(Amount("burn", 3));
            card.Effects.Add(Amount("poison", 2));
            card.Effects.Add(new Effect { Kind = "crit", Chance = 1 });
            BattleConfig config = new BattleConfig { EnemyMaxHp = 1000, MaxTime = 5 };
            config.Cards.Add(card.Id, card);
            config.Player.Items.Add(new LoadoutItem { CardId = card.Id });
            BattleResult result = BattleEngine.Simulate(config);
            BattleDamageSnapshot final = Aggregator.BuildDamage(result, 1000).At(5);
            Assert.Near(result.Totals.Player.Damage, final.Damage);
            Assert.Near(result.Totals.Player.EffectiveDamage, final.EffectiveDamage);
            Assert.True(final.BurnDamage > 0);
            Assert.True(final.PoisonDamage > 0);
            Assert.Near(final.Damage, final.DirectDamage + final.BurnDamage + final.PoisonDamage);

            card.Effects.Clear();
            card.Effects.Add(Amount("damage", 50));
            config.EnemyMaxHp = 10;
            config.EnemyStartHp = 7;
            result = BattleEngine.Simulate(config);
            final = Aggregator.BuildDamage(result, 10).At(5);
            Assert.Near(50, final.Damage);
            Assert.Near(7, final.EffectiveDamage);
            Assert.Near(0, final.EnemyHp);
        }

        private static void DummyPreservesTargetsWithoutEnemyActions()
        {
            CardDef player = new CardDef { Id = "player", Cooldown = 1 };
            player.Effects.Add(Amount("damage", 1));
            player.Effects.Add(new Effect { Kind = "freeze", Seconds = 0.2, Target = new Target { Kind = "randomEnemyItem" } });
            CardDef enemy = new CardDef { Id = "enemy", Cooldown = 0.5, StartReady = true };
            enemy.Effects.Add(Amount("damage", 10));
            foreach (string triggerName in new[] { "atCombatStart", "everySecond", "onDamageTaken", "onUse" })
            {
                TriggerDef trigger = new TriggerDef { Event = triggerName };
                trigger.Effects.Add(Amount("damage", 10));
                enemy.Triggers.Add(trigger);
            }
            CardDef skill = new CardDef { Id = "skill:enemy" };
            TriggerDef start = new TriggerDef { Event = "atCombatStart" };
            start.Effects.Add(Amount("damage", 10));
            skill.Triggers.Add(start);
            TriggerDef use = new TriggerDef { Event = "onUse", SkillRule = new NativeSkillRule { SourceOwner = "enemy" } };
            use.Effects.Add(Amount("damage", 10));
            skill.Triggers.Add(use);
            BattleConfig config = new BattleConfig { PlayerMaxHp = 10000, EnemyMaxHp = 100000, MaxTime = 30, EnemyIsTrainingDummy = true };
            config.Cards.Add(player.Id, player);
            config.Cards.Add(enemy.Id, enemy);
            config.Player.Items.Add(new LoadoutItem { CardId = player.Id });
            config.Enemy.Items.Add(new LoadoutItem { CardId = enemy.Id, Slot = 3 });
            config.EnemySkills.Add(skill);

            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);
            Assert.Near(30, result.EndTime);
            Assert.Equal(1, result.EnemyItems.Count);
            Assert.Equal(3, result.EnemyItems[0].Slot);
            Assert.Equal(Side.Enemy, result.EnemyItems[0].Side);
            Assert.Near(30, result.Totals.Player.Damage);
            Assert.Near(0, result.Totals.Enemy.Damage);
            Assert.False(result.Events.Any(delegate(BattleEvent ev) { return ev.Side == Side.Enemy && (ev.Kind == "activate" || ev.Kind == "skillTrigger"); }));
            Assert.True(result.Events.Any(delegate(BattleEvent ev) { return ev.Kind == "freeze" && ev.TargetItemId == enemy.Id; }));
            Dictionary<string, CardStatsSeries> stats = Aggregator.Build(result, new List<string> { enemy.Id }, 0.1);
            Assert.False(stats[enemy.Id].HasCooldown);
            Assert.Equal(0, stats[enemy.Id].At(30).ActivateCount);
            Assert.True(result.ItemSnapshots.Any(delegate(ItemSnapshot snapshot) { return snapshot.ItemId == enemy.Id && snapshot.Time == 30; }));

            config.EnemyIsTrainingDummy = false;
            config.MaxTime = 2;
            result = BattleEngine.Simulate(config);
            Assert.True(result.Totals.Enemy.Damage > 0); // Ordinary combat still enables all sources.
        }

        private static string Signature(BattleConfig config)
        {
            return string.Join("|", config.Enemy.Items.Select(delegate(LoadoutItem entry) { return entry.Slot + ":" + entry.CardId; }).ToArray());
        }

        private static void RandomBoardFitsAndRepeatsWithSeed()
        {
            List<CardDef> cards = new List<CardDef>();
            for (int i = 0; i < 30; i++)
                cards.Add(new CardDef
                {
                    Id = "card" + i, BaseItemId = "base" + i, ParseStatus = "structured",
                    Size = i % 3 == 0 ? "Large" : i % 3 == 1 ? "Medium" : "Small", Cooldown = 1
                });
            cards.Add(new CardDef { Id = "unsafe-text", ParseStatus = "text" });
            CardCatalog catalog = CardCatalog.FromCards(cards);
            BattleConfig config = new BattleConfig { MaxTime = 30 };
            CardDef player = new CardDef { Id = "live:player" };
            config.Cards.Add(player.Id, player);
            config.Player.Items.Add(new LoadoutItem { CardId = player.Id });
            TrainingDummy.Populate(config, catalog, 123);
            string first = Signature(config);
            int definitions = config.Cards.Count;
            Assert.True(config.EnemyIsTrainingDummy);
            Assert.Near(100000, config.EnemyMaxHp);
            Assert.Near(100000, config.EnemyStartHp.Value);
            Assert.Equal(1, config.Player.Items.Count);
            Assert.True(config.Cards.ContainsKey(player.Id));
            int occupied = 0;
            HashSet<string> bases = new HashSet<string>();
            foreach (LoadoutItem entry in config.Enemy.Items)
            {
                Assert.Equal(occupied, entry.Slot);
                CardDef card = config.Cards[entry.CardId];
                occupied += PanelModel.CardSpan(card);
                Assert.True(bases.Add(card.BaseItemId));
                Assert.False(card.Id == "unsafe-text");
            }
            Assert.Equal(10, occupied);
            TrainingDummy.Populate(config, catalog, 123);
            Assert.Equal(first, Signature(config));
            Assert.Equal(definitions, config.Cards.Count); // Rebuilding does not leak definitions.
            cards.Reverse();
            TrainingDummy.Populate(config, CardCatalog.FromCards(cards), 123);
            Assert.Equal(first, Signature(config));
            TrainingDummy.Populate(config, catalog, 456);
            Assert.False(first == Signature(config));
            TrainingDummy.Populate(config, null, 123);
            Assert.Equal(0, config.Enemy.Items.Count);
            Assert.True(config.Cards.ContainsKey(player.Id));
        }
    }
}
