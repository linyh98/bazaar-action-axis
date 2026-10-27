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
            InfiniteDummyKeepsThirtySecondsOfOutputAndEnemyUseReactions();
            OutputExcludesHealingSelfDamageMitigationAndOverkill();
            DpsStopsAtBattleEnd();
            HealthSnapshotsTrackBothSidesAndRewind();
            HealthSnapshotsKeepExactDeathAndTimeoutDistinct();
        }

        private static void HealthSnapshotsTrackBothSidesAndRewind()
        {
            CardDef player = new CardDef { Id = "health-player", Cooldown = 1 };
            player.Effects.Add(Amount("damage", 5));
            player.Effects.Add(Amount("shield", 9));
            player.Effects.Add(Amount("heal", 6));
            player.Effects.Add(Amount("burn", 4));
            player.Effects.Add(Amount("poison", 2));
            CardDef enemy = new CardDef { Id = "health-enemy", Cooldown = 1 };
            enemy.Effects.Add(Amount("damage", 13));
            enemy.Effects.Add(Amount("burn", 3));
            enemy.Effects.Add(Amount("poison", 2));
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 3,
                PlayerMaxHp = 200, PlayerStartHp = 80, EnemyMaxHp = 1000, EnemyStartShield = 20,
                PlayerStartRegen = 3, EnemyStartRegen = 2 };
            config.Cards[player.Id] = player;
            config.Cards[enemy.Id] = enemy;
            config.Player.Items.Add(new LoadoutItem { CardId = player.Id });
            config.Enemy.Items.Add(new LoadoutItem { CardId = enemy.Id });
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);
            BattleHealthSeries series = new BattleHealthSeries(result);
            Assert.Near(80, series.At(-1).Player.Hp);
            Assert.Near(200, series.At(0).Player.MaxHp);
            Assert.Near(1000, series.At(0).Enemy.MaxHp);
            Assert.Near(20, series.At(0).Enemy.Shield);
            Assert.Near(83, series.At(0.999).Player.Hp);
            Assert.Near(0, series.At(0.999).Player.Burn);
            Assert.True(series.At(1).Player.Burn > 0);
            Assert.True(series.At(1).Enemy.Poison > 0);
            Assert.Near(3, series.At(2).Player.Regen);
            Assert.Near(2, series.At(2).Enemy.Regen);
            // 双方血条逐帧对齐结算事件，不用“最大生命减累计伤害”推算。
            foreach (BattleHealthSnapshot snapshot in result.HealthSnapshots)
            {
                foreach (Side side in new[] { Side.Player, Side.Enemy })
                {
                    BattleEvent last = result.Events.LastOrDefault(delegate(BattleEvent ev)
                    { return ev.TargetSide == side && ev.HpAfter.HasValue && ev.Time <= snapshot.Time + 1e-9; });
                    if (last != null) Assert.Near(last.HpAfter.Value,
                        side == Side.Player ? snapshot.Player.Hp : snapshot.Enemy.Hp);
                }
            }
            Assert.Near(series.At(3).Player.Hp, series.At(30).Player.Hp);
            series.At(1).Player.Hp = -1;
            Assert.True(series.At(1).Player.Hp > 0);
            result.HealthSnapshots[0].Player.Hp = -1;
            Assert.Near(80, series.At(-1).Player.Hp);
            Assert.Near(80, series.At(double.NaN).Player.Hp);
            Assert.True(new BattleHealthSeries(null).At(0) == null);
        }

        private static void HealthSnapshotsKeepExactDeathAndTimeoutDistinct()
        {
            CardDef enemy = new CardDef { Id = "lethal-enemy", Cooldown = 0.05 };
            enemy.Effects.Add(Amount("damage", 100));
            BattleConfig config = new BattleConfig { NativeMechanics = true, PlayerMaxHp = 20, EnemyMaxHp = 100, MaxTime = 1 };
            config.Cards[enemy.Id] = enemy;
            config.Enemy.Items.Add(new LoadoutItem { CardId = enemy.Id });
            BattleResult result = BattleEngine.Simulate(config);
            Assert.True(result.EndedByDeath);
            Assert.Equal("enemy", result.Winner);
            Assert.Near(0.05, result.EndTime);
            BattleHealthSeries series = new BattleHealthSeries(result);
            Assert.Near(20, series.At(0.049).Player.Hp);
            Assert.Near(0, series.At(result.EndTime).Player.Hp);
            Assert.Near(100, series.At(result.EndTime).Enemy.Hp);
            Assert.Near(0, series.At(30).Player.Hp);
            config.PlayerStartHp = 0;
            result = BattleEngine.Simulate(config);
            Assert.Near(0, result.EndTime);
            Assert.True(result.EndedByDeath);
            Assert.Near(0, new BattleHealthSeries(result).At(0).Player.Hp);

            config = new BattleConfig { NativeMechanics = true, PlayerStartHp = 50, MaxTime = 1 };
            result = BattleEngine.Simulate(config);
            Assert.False(result.EndedByDeath);
            Assert.Equal("enemy", result.Winner);
            Assert.Equal("已到推演上限 · 尚未分胜负", PanelModel.BattleOutcome(result.EndedByDeath, result.Winner));

            CardDef player = new CardDef { Id = "late-lethal", Cooldown = 20 };
            player.Effects.Add(Amount("damage", 4));
            config = new BattleConfig { NativeMechanics = true, EnemyMaxHp = 7, MaxTime = 60 };
            config.Cards[player.Id] = player;
            config.Player.Items.Add(new LoadoutItem { CardId = player.Id });
            result = BattleEngine.Simulate(config);
            Assert.True(result.EndedByDeath);
            Assert.True(result.EndTime > 30 && result.EndTime < 60);
            series = new BattleHealthSeries(result);
            Assert.True(series.At(30).Enemy.Hp > 0);
            Assert.Near(0, series.At(result.EndTime).Enemy.Hp);
            Assert.Equal("player", result.Winner);
        }

        private static void DpsStopsAtBattleEnd()
        {
            BattleResult result = new BattleResult { EnemyStartHp = 100, EndTime = 5 };
            result.Events.Add(Event(2, "damage", 40, 60, 0, null));
            result.Events.Add(Event(3, "heal", 20, 80, 0, null));
            result.Events.Add(Event(5, "damage", 80, 0, 0, null));
            BattleDamageSeries series = Aggregator.BuildDamage(result, 100);
            Assert.Near(40, series.At(3).OutputDamage);
            Assert.Near(20, series.At(3).EnemyHealing);
            Assert.Near(120, series.At(30).OutputDamage);
            Assert.Near(24, series.DpsAt(5));
            Assert.Near(24, series.DpsAt(30));
            Assert.Near(20, series.DpsAt(2));
            Assert.Near(0, series.DpsAt(0));
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
            Assert.True(result.Events.Any(delegate(BattleEvent ev) { return ev.Side == Side.Enemy && ev.Kind == "activate"; }));
            Assert.False(result.Events.Any(delegate(BattleEvent ev) { return ev.Side == Side.Enemy && ev.Kind == "skillTrigger"; }));
            Assert.True(result.Events.Any(delegate(BattleEvent ev) { return ev.Kind == "freeze" && ev.TargetItemId == enemy.Id; }));
            Dictionary<string, CardStatsSeries> stats = Aggregator.Build(result, new List<string> { enemy.Id }, 0.1);
            Assert.True(stats[enemy.Id].HasCooldown);
            Assert.True(stats[enemy.Id].At(30).ActivateCount > 0);
            Assert.True(result.ItemSnapshots.Any(delegate(ItemSnapshot snapshot) { return snapshot.ItemId == enemy.Id && snapshot.Time == 30; }));

            config.EnemyIsTrainingDummy = false;
            config.MaxTime = 2;
            result = BattleEngine.Simulate(config);
            Assert.True(result.Totals.Enemy.Damage > 0); // Ordinary combat still enables all sources.
        }

        private static string Signature(BattleConfig config)
        {
            return string.Join("|", config.Enemy.Items.Select(delegate(LoadoutItem entry)
            { return entry.Slot + ":" + entry.CardId + ":" + config.Cards[entry.CardId].Cooldown; }).ToArray());
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
            BattleConfig config = new BattleConfig { MaxTime = 4, EnemyStartShield = 500,
                EnemyStartRegen = 10, EnemyHero = "Vanessa", Rules = new RuleOverrides { MaxTime = 4 } };
            CardDef player = new CardDef { Id = "live:player" };
            config.Cards.Add(player.Id, player);
            config.Player.Items.Add(new LoadoutItem { CardId = player.Id });
            TrainingDummy.Populate(config, catalog, 123);
            string first = Signature(config);
            int definitions = config.Cards.Count;
            Assert.True(config.EnemyIsTrainingDummy);
            Assert.Near(100000, config.EnemyMaxHp);
            Assert.Near(100000, config.EnemyStartHp.Value);
            Assert.Near(0, config.EnemyStartShield);
            Assert.Near(0, config.EnemyStartRegen);
            Assert.True(config.EnemyHero == null);
            Assert.Near(30, config.MaxTime);
            Assert.Near(30, config.Rules.MaxTime.Value);
            Assert.Equal(1, config.Player.Items.Count);
            Assert.True(config.Cards.ContainsKey(player.Id));
            int occupied = 0;
            HashSet<string> sizes = new HashSet<string>();
            bool hasCooldown = false, hasPassive = false;
            foreach (LoadoutItem entry in config.Enemy.Items)
            {
                Assert.Equal(occupied, entry.Slot);
                CardDef card = config.Cards[entry.CardId];
                occupied += PanelModel.CardSpan(card);
                sizes.Add(card.Size);
                Assert.True(card.Id.StartsWith("dummy:"));
                Assert.Equal(0, card.Effects.Count);
                Assert.Equal(0, card.Triggers.Count);
                Assert.Equal(0, card.Tags.Count);
                Assert.Equal(0, card.LiveAttributes.Count);
                Assert.True(string.IsNullOrEmpty(card.Image));
                Assert.True(card.NativeTemplate == null);
                Assert.True(card.BaseItemId == null);
                Assert.Equal("空白卡牌", card.DisplayName());
                if (card.Cooldown > 0)
                {
                    hasCooldown = true;
                    Assert.True(card.Cooldown >= 3 && card.Cooldown <= 8);
                    Assert.False(card.Passive);
                }
                else
                {
                    hasPassive = true;
                    Assert.True(card.Passive);
                }
            }
            Assert.Equal(10, occupied);
            Assert.Equal(3, sizes.Count);
            Assert.True(hasCooldown && hasPassive);
            TrainingDummy.Populate(config, catalog, 123);
            Assert.Equal(first, Signature(config));
            Assert.Equal(definitions, config.Cards.Count); // Rebuilding does not leak definitions.
            cards.Reverse();
            TrainingDummy.Populate(config, CardCatalog.FromCards(cards), 123);
            Assert.Equal(first, Signature(config));
            TrainingDummy.Populate(config, catalog, 456);
            Assert.False(first == Signature(config));
            TrainingDummy.Populate(config, null, 123);
            Assert.Equal(first, Signature(config)); // Templates do not depend on installed card data.
            Assert.Equal(definitions, config.Cards.Count);
            Assert.True(config.Cards.ContainsKey(player.Id));
        }

        private static void InfiniteDummyKeepsThirtySecondsOfOutputAndEnemyUseReactions()
        {
            foreach (bool native in new[] { false, true })
            {
                CardDef player = new CardDef { Id = "lethal-practice", Cooldown = 1 };
                player.Effects.Add(Amount("damage", 200000));
                player.Effects.Add(new Effect { Kind = "damage", Amount = new NumExpr { Value = 1000 },
                    Target = new Target { Kind = "selfHero" } });
                player.Effects.Add(Amount("burn", 3));
                player.Effects.Add(Amount("poison", 2));
                CardDef skill = new CardDef { Id = "skill:enemy-use-practice" };
                TriggerDef enemyUse = new TriggerDef { Event = "onUse",
                    SkillRule = new NativeSkillRule { SourceOwner = "enemy" } };
                enemyUse.Effects.Add(Amount("shield", 7));
                skill.Triggers.Add(enemyUse);
                BattleConfig config = new BattleConfig { NativeMechanics = native, PlayerMaxHp = 1 };
                config.Cards[player.Id] = player;
                config.Player.Items.Add(new LoadoutItem { CardId = player.Id });
                config.PlayerSkills.Add(skill);
                TrainingDummy.Populate(config, null, 123);
                BattleResult result = BattleEngine.Simulate(config);
                Assert.Equal(0, result.Warnings.Count);
                Assert.Near(30, result.EndTime);
                Assert.False(result.EndedByDeath);
                Assert.False(result.Events.Any(delegate(BattleEvent ev) { return ev.Kind == "death"; }));
                Assert.True(result.Events.Any(delegate(BattleEvent ev)
                { return ev.Side == Side.Enemy && ev.Kind == "activate" && ev.Time > 20; }));
                Assert.True(result.Events.Any(delegate(BattleEvent ev)
                { return ev.Kind == "skillTrigger" && ev.SourceItemId == skill.Id && ev.Time > 20; }));
                Assert.Near(0, result.Totals.Enemy.Damage);
                BattleDamageSeries damage = Aggregator.BuildDamage(result, TrainingDummy.MaxHp);
                Assert.True(damage.At(10).OutputDamage > TrainingDummy.MaxHp);
                Assert.True(damage.At(30).OutputDamage > damage.At(20).OutputDamage);
                Assert.Near(0, damage.At(30).OverkillDamage);
                Assert.True(damage.At(30).OutputBurnDamage > damage.At(20).OutputBurnDamage);
                Assert.True(damage.At(30).OutputPoisonDamage > damage.At(20).OutputPoisonDamage);
                Assert.True(new BattleHealthSeries(result).At(30).Enemy.Hp > 0);
                Assert.True(result.ItemSnapshots.Any(delegate(ItemSnapshot snapshot)
                { return snapshot.Time == 30 && snapshot.HasCooldown && snapshot.ItemId.StartsWith("dummy:"); }));
            }
        }
    }
}
