using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // G10: 直伤 / DoT / 护盾 / 治疗 / 暴击 / 吸血 / 累计输出归因。
    // All cards come from exported native templates; only live cooldown,
    // crit and starting-health values are overridden per scenario.
    public static class G10DamageLedgerTests
    {
        private static JsonValue _fixtures;
        private static JsonValue _allTemplates;
        private static readonly CardCatalog EmptyCatalog = CardCatalog.FromCards(new List<CardDef>());

        public static void Run()
        {
            _fixtures = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_g10_damage.json"));
            _allTemplates = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_g10_templates.json"));
            JsonValue shard = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_g10_shard.json"));
            JsonValue shardRow = shard["Shard of Obsidian"];
            _allTemplates.AsObject()[shardRow["id"].AsString()] = shardRow;
            MultiSourceBurnSplitsOneTickByOwner();
            PoisonFollowsNativeCadenceWithoutDecay();
            HealingAndShieldNeverCountAsDamage();
            MaxHealthGrowthIsNeitherDamageNorHealing();
            DirectDamageLifestealAndCritStaySeparate();
            BothSidesKeepIndependentLedgers();
            DummyAndNormalEnemyUseTheSameOutputRules();
            OverkillAndPreventedStayOutOfOutput();
            BurnDoesNotDecayWhenNoDamageWasDealt();
            ShardOfObsidianIgnoresStaleAbilityId();
        }

        private static LiveCardEntry Real(string name, int slot, string tier = "Bronze", int? cooldownMs = null)
        {
            JsonValue row = _fixtures[name], template = row["template"];
            Dictionary<string, int> attributes = MonsterTemplateExtractor.TierAttributes(template, tier);
            if (cooldownMs.HasValue) attributes["CooldownMax"] = cooldownMs.Value;
            return new LiveCardEntry
            {
                TemplateId = row["id"].AsString(),
                Template = template,
                Tier = tier,
                Socket = slot,
                Size = template["Size"].AsString(),
                AttributesAreLive = false,
                Attributes = attributes
            };
        }

        private static LiveCardEntry RealById(string id, int slot, string tier = "Bronze")
        {
            JsonValue row = _allTemplates[id];
            JsonValue template = row["template"];
            return new LiveCardEntry
            {
                TemplateId = id,
                Template = template,
                Tier = tier,
                Socket = slot,
                Size = template["Size"].AsString(),
                AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, tier)
            };
        }

        private static BattleConfig Build(double maxTime, LiveCardEntry[] player, LiveCardEntry[] enemy)
        {
            List<LiveCardEntry> all = new List<LiveCardEntry>();
            all.AddRange(player);
            all.AddRange(enemy);
            BattleConfig config = CardMapper.Build(all, EmptyCatalog, 1000, 1000, maxTime).Config;
            for (int index = 0; index < enemy.Length; index++)
            {
                LoadoutItem item = config.Player.Items[player.Length];
                config.Player.Items.RemoveAt(player.Length);
                config.Enemy.Items.Add(item);
            }
            config.NativeMechanics = true;
            List<string> warnings = NativeCardAbilityMapper.Apply(config);
            Assert.Equal("", string.Join(" | ", warnings.ToArray()));
            return config;
        }

        private static BattleConfig PlayerBoard(double maxTime, params LiveCardEntry[] cards)
        {
            return Build(maxTime, cards, new LiveCardEntry[0]);
        }

        private static string Id(BattleConfig config, string name)
        {
            foreach (CardDef card in config.Cards.Values)
                if (card.Name == name) return card.Id;
            throw new InvalidOperationException("fixture card not mapped: " + name);
        }

        private static BattleEventShare Share(BattleEvent ev, string itemId)
        {
            foreach (BattleEventShare share in ev.Shares)
                if (share.SourceItemId == itemId) return share;
            throw new InvalidOperationException("share not found: " + itemId);
        }

        private static void AssertLedger(BattleResult result, Side side, double maxHp)
        {
            BattleDamageSnapshot final = Aggregator.BuildDamage(result, side, maxHp).At(double.PositiveInfinity);
            // Native TotalDamage is represented by SettledDamage (realized HP +
            // Shield damage, including overkill). Raw Burn prevented by the
            // native half-shield rule stays in Damage but never in output.
            Assert.Near(final.Damage, final.DirectDamage + final.BurnDamage + final.PoisonDamage);
            Assert.Near(final.OutputDamage, final.OutputDirectDamage + final.OutputBurnDamage + final.OutputPoisonDamage);
            Assert.Near(final.Damage, final.SettledDamage + final.PreventedDamage);
            Assert.Near(final.OutputDamage, final.EffectiveDamage + final.ShieldAbsorbed);
            Assert.Near(final.NetHpLoss,
                final.SettledDamage - final.OverkillDamage - final.ShieldAbsorbed -
                final.Healing - final.OtherHpChange + final.SelfDamage);
            Assert.Near(final.Healing, final.NominalHealing - final.Overheal);
            Assert.True(final.Overheal >= -1e-9);
            Assert.True(final.PreventedDamage >= -1e-9);
        }

        private static void MultiSourceBurnSplitsOneTickByOwner()
        {
            // Two real Burn cards land on the same frame. The 4:1 stack pool is
            // measured once per tick and only attributed afterwards. Chocolate
            // Bar keeps the two Tools apart so no adjacency aura grants extra
            // Multicast; it has only a sell trigger and never acts in combat.
            BattleConfig config = PlayerBoard(5,
                Real("Blow Torch", 0), Real("Chocolate Bar", 1), Real("Soldering Gun", 2, "Bronze", 4000));
            config.EnemyMaxHp = 1000;
            config.EnemyStartHp = 1000;
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Near(5, result.EndTime);
            string torch = Id(config, "Blow Torch");
            string gun = Id(config, "Soldering Gun");

            List<BattleEvent> ticks = result.Events.Where(delegate(BattleEvent ev)
            { return ev.Kind == "dotTick" && ev.Note == "burn"; }).ToList();
            Assert.Equal(2, ticks.Count);
            Assert.Near(5, ticks[0].Value.Value);
            Assert.Near(4, ticks[1].Value.Value);
            Assert.Equal(2, ticks[0].Shares.Count);
            Assert.Near(4, Share(ticks[0], torch).Amount);
            Assert.Near(1, Share(ticks[0], gun).Amount);
            Assert.Near(4, Share(ticks[0], torch).ActualAmount);
            Assert.Near(1, Share(ticks[0], gun).ActualAmount);
            Assert.Near(0.8, Share(ticks[1], gun).Amount);
            Assert.Near(3.2, Share(ticks[1], torch).Amount);

            BattleDamageSnapshot enemy = Aggregator.BuildDamage(result, 1000).At(5);
            Assert.Near(9, enemy.BurnDamage);
            Assert.Near(9, enemy.OutputDamage);
            Assert.Near(9, enemy.EffectiveDamage);
            Assert.Near(9, enemy.NetHpLoss);
            Assert.Near(0, enemy.DirectDamage);
            Assert.Near(0, enemy.OverkillDamage);
            Assert.Near(0, enemy.PreventedDamage);
            Assert.Near(991, enemy.EnemyHp);
            Assert.Near(9, result.Totals.Player.Damage);
            Assert.Near(9, result.Totals.Player.EffectiveDamage);

            Dictionary<string, CardStatsSeries> stats = Aggregator.Build(result, 0.05);
            Assert.Near(7.2, stats[torch].At(5).Damage);
            Assert.Near(1.8, stats[gun].At(5).Damage);
            Assert.Near(9, stats[torch].At(5).Damage + stats[gun].At(5).Damage);
            AssertLedger(result, Side.Enemy, 1000);
        }

        private static void PoisonFollowsNativeCadenceWithoutDecay()
        {
            BattleConfig config = PlayerBoard(9, Real("Trained Spider", 0));
            config.EnemyMaxHp = 1000;
            config.EnemyStartHp = 1000;
            BattleResult result = BattleEngine.Simulate(config);
            string spider = Id(config, "Trained Spider");
            List<BattleEvent> ticks = result.Events.Where(delegate(BattleEvent ev)
            { return ev.Kind == "dotTick" && ev.Note == "poison"; }).ToList();
            Assert.Equal(3, ticks.Count);
            Assert.Near(7, ticks[0].Time);
            Assert.Near(8, ticks[1].Time);
            Assert.Near(9, ticks[2].Time);
            foreach (BattleEvent tick in ticks)
            {
                Assert.Near(1, tick.Value.Value);
                Assert.Equal(1, tick.Shares.Count);
                Assert.Equal(spider, tick.Shares[0].SourceItemId);
                Assert.Near(1, tick.Shares[0].ActualAmount);
            }

            BattleDamageSnapshot enemy = Aggregator.BuildDamage(result, 1000).At(9);
            Assert.Near(3, enemy.PoisonDamage);
            Assert.Near(3, enemy.OutputDamage);
            Assert.Near(3, enemy.NetHpLoss);
            Assert.Near(997, enemy.EnemyHp);
            Assert.Near(3, result.Totals.Player.Damage);
            Assert.Near(3, Aggregator.Build(result, 0.05)[spider].At(9).Damage);
            AssertLedger(result, Side.Enemy, 1000);
        }

        private static void HealingAndShieldNeverCountAsDamage()
        {
            BattleConfig config = PlayerBoard(6, Real("Bandages", 0));
            config.PlayerMaxHp = 100;
            config.PlayerStartHp = 90;
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Near(0, result.Totals.Player.Damage);
            Assert.Near(0, result.Totals.Player.EffectiveDamage);
            Assert.Near(10, result.Totals.Player.Heal);
            BattleDamageSnapshot player = Aggregator.BuildDamage(result, Side.Player, 100).At(6);
            Assert.Near(100, player.EnemyHp);
            Assert.Near(10, player.EnemyShield);
            Assert.Near(10, player.Healing);
            Assert.Near(10, player.NominalHealing);
            Assert.Near(0, player.Overheal);
            Assert.Near(-10, player.NetHpLoss);
            Assert.Near(0, player.OutputDamage);
            Assert.Near(0, player.DirectDamage);
            Assert.Near(0, player.OtherHpChange);

            // The same heal at full health is pure overheal: no damage, no
            // accepted healing, and no net-HP movement.
            BattleConfig full = PlayerBoard(6, Real("Bandages", 0));
            full.PlayerMaxHp = 100;
            full.PlayerStartHp = 100;
            BattleResult overheal = BattleEngine.Simulate(full);
            Assert.Near(0, overheal.Totals.Player.Damage);
            Assert.Near(0, overheal.Totals.Player.Heal);
            BattleDamageSnapshot fullPlayer = Aggregator.BuildDamage(overheal, Side.Player, 100).At(6);
            Assert.Near(10, fullPlayer.EnemyShield);
            Assert.Near(0, fullPlayer.Healing);
            Assert.Near(10, fullPlayer.NominalHealing);
            Assert.Near(10, fullPlayer.Overheal);
            Assert.Near(0, fullPlayer.NetHpLoss);
            Assert.Near(0, fullPlayer.OutputDamage);
            AssertLedger(overheal, Side.Player, 100);
        }

        private static void MaxHealthGrowthIsNeitherDamageNorHealing()
        {
            BattleConfig config = PlayerBoard(8, Real("Walter Cooler", 0, "Silver"));
            config.PlayerMaxHp = 100;
            config.PlayerStartHp = 100;
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Near(0, result.Totals.Player.Damage);
            Assert.Near(0, result.Totals.Player.EffectiveDamage);
            Assert.Near(0, result.Totals.Player.Heal);
            BattleEvent growth = result.Events.Single(delegate(BattleEvent ev)
            { return ev.Kind == "playerAttribute" && ev.Note == "HealthMax"; });
            Assert.Near(10, growth.Value.Value);
            Assert.Near(110, growth.HpAfter.Value);
            BattleDamageSnapshot player = Aggregator.BuildDamage(result, Side.Player, 100).At(8);
            Assert.Near(110, player.EnemyHp);
            Assert.Near(10, player.OtherHpChange);
            Assert.Near(-10, player.NetHpLoss);
            Assert.Near(0, player.OutputDamage);
            Assert.Near(0, player.Healing);
            Assert.Near(0, player.NominalHealing);
            Assert.Near(0, player.DirectDamage);
            AssertLedger(result, Side.Player, 100);
        }

        private static void DirectDamageLifestealAndCritStaySeparate()
        {
            BattleConfig config = PlayerBoard(7, Real("Rune Axe", 0));
            config.PlayerMaxHp = 100;
            config.PlayerStartHp = 80;
            config.EnemyMaxHp = 1000;
            config.EnemyStartHp = 1000;
            BattleResult result = BattleEngine.Simulate(config);
            BattleDamageSnapshot enemy = Aggregator.BuildDamage(result, 1000).At(7);
            Assert.Near(15, enemy.DirectDamage);
            Assert.Near(15, enemy.OutputDamage);
            Assert.Near(15, enemy.EffectiveDamage);
            Assert.Near(15, enemy.NetHpLoss);
            Assert.Near(985, enemy.EnemyHp);
            Assert.Near(0, enemy.EnemyHealing);
            BattleDamageSnapshot player = Aggregator.BuildDamage(result, Side.Player, 100).At(7);
            Assert.Near(95, player.EnemyHp);
            Assert.Near(15, player.Healing);
            Assert.Near(-15, player.NetHpLoss);
            Assert.Near(0, player.OutputDamage);
            Assert.Near(15, result.Totals.Player.Damage);
            Assert.Near(15, result.Totals.Player.Heal);

            // Deterministic crit: the real Rune Axe template with +100% Crit
            // Chance must double both the hit and the lifesteal basis.
            BattleConfig crit = PlayerBoard(7, Real("Rune Axe", 0));
            crit.PlayerMaxHp = 100;
            crit.PlayerStartHp = 60;
            crit.EnemyMaxHp = 1000;
            crit.EnemyStartHp = 1000;
            crit.Cards[Id(crit, "Rune Axe")].Effects.Add(new Effect { Kind = "crit", Chance = 1 });
            BattleResult critResult = BattleEngine.Simulate(crit);
            Assert.Equal(1, critResult.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "crit"; }));
            BattleEvent hit = critResult.Events.Single(delegate(BattleEvent ev)
            { return ev.Kind == "damage" && ev.SourceCardId == Id(crit, "Rune Axe"); });
            Assert.Near(30, hit.Value.Value);
            Assert.Equal("critical", hit.Note);
            BattleDamageSnapshot critEnemy = Aggregator.BuildDamage(critResult, 1000).At(7);
            Assert.Near(30, critEnemy.DirectDamage);
            Assert.Near(30, critEnemy.OutputDamage);
            Assert.Near(30, critEnemy.NetHpLoss);
            Assert.Near(970, critEnemy.EnemyHp);
            BattleDamageSnapshot critPlayer = Aggregator.BuildDamage(critResult, Side.Player, 100).At(7);
            Assert.Near(90, critPlayer.EnemyHp);
            Assert.Near(30, critPlayer.Healing);
            Assert.Near(30, critResult.Totals.Player.Damage);
            Assert.Near(30, critResult.Totals.Player.Heal);
            AssertLedger(critResult, Side.Enemy, 1000);
            AssertLedger(critResult, Side.Player, 100);
        }

        private static void BothSidesKeepIndependentLedgers()
        {
            BattleConfig config = Build(10,
                new[] { Real("Rune Axe", 0) },
                new[] { Real("Trained Spider", 0) });
            config.PlayerMaxHp = 100;
            config.PlayerStartHp = 80;
            config.EnemyMaxHp = 1000;
            config.EnemyStartHp = 1000;
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Near(10, result.EndTime);
            BattleDamageSnapshot enemy = Aggregator.BuildDamage(result, 1000).At(10);
            Assert.Near(15, enemy.DirectDamage);
            Assert.Near(0, enemy.PoisonDamage);
            Assert.Near(15, enemy.OutputDamage);
            Assert.Near(15, enemy.NetHpLoss);
            Assert.Near(985, enemy.EnemyHp);
            Assert.Near(0, enemy.EnemyHealing);
            BattleDamageSnapshot player = Aggregator.BuildDamage(result, Side.Player, 100).At(10);
            Assert.Near(0, player.DirectDamage);
            Assert.Near(4, player.PoisonDamage);
            Assert.Near(4, player.OutputDamage);
            Assert.Near(-11, player.NetHpLoss);
            Assert.Near(91, player.EnemyHp);
            Assert.Near(15, player.Healing);
            Assert.Near(15, player.NominalHealing);
            Assert.Near(15, result.Totals.Player.Damage);
            Assert.Near(4, result.Totals.Enemy.Damage);
            Assert.Near(15, result.Totals.Player.Heal);
            Assert.Near(0, result.Totals.Enemy.Heal);

            List<string> playerIds = result.PlayerItems.Select(delegate(BattleItemView item) { return item.Id; }).ToList();
            List<string> enemyIds = result.EnemyItems.Select(delegate(BattleItemView item) { return item.Id; }).ToList();
            Dictionary<string, CardStatsSeries> playerStats = Aggregator.Build(result, playerIds, 0.05);
            Dictionary<string, CardStatsSeries> enemyStats = Aggregator.Build(result, enemyIds, 0.05);
            Assert.Near(15, playerStats[Id(config, "Rune Axe")].At(10).Damage);
            Assert.Near(4, enemyStats[Id(config, "Trained Spider")].At(10).Damage);
            Assert.False(playerStats.ContainsKey(Id(config, "Trained Spider")));
            Assert.False(enemyStats.ContainsKey(Id(config, "Rune Axe")));
            AssertLedger(result, Side.Enemy, 1000);
            AssertLedger(result, Side.Player, 100);
        }

        private static void DummyAndNormalEnemyUseTheSameOutputRules()
        {
            BattleConfig dummy = PlayerBoard(5, Real("Rune Axe", 0, "Bronze", 4000));
            dummy.PlayerMaxHp = 100;
            dummy.PlayerStartHp = 80;
            dummy.EnemyIsTrainingDummy = true;
            dummy.EnemyMaxHp = 100000;
            dummy.EnemyStartHp = 100000;
            BattleResult dummyResult = BattleEngine.Simulate(dummy);
            BattleDamageSnapshot dummyTarget = Aggregator.BuildDamage(dummyResult, 100000).At(5);
            Assert.Near(15, dummyTarget.OutputDamage);
            Assert.Near(15, dummyTarget.EffectiveDamage);
            Assert.Near(15, dummyTarget.NetHpLoss);
            Assert.Near(15, dummyResult.Totals.Player.Damage);

            BattleConfig normal = PlayerBoard(5, Real("Rune Axe", 0, "Bronze", 4000));
            normal.PlayerMaxHp = 100;
            normal.PlayerStartHp = 80;
            normal.EnemyMaxHp = 100000;
            normal.EnemyStartHp = 100000;
            BattleResult normalResult = BattleEngine.Simulate(normal);
            BattleDamageSnapshot normalTarget = Aggregator.BuildDamage(normalResult, 100000).At(5);
            Assert.Near(15, normalTarget.OutputDamage);
            Assert.Near(15, normalTarget.EffectiveDamage);
            Assert.Near(15, normalTarget.NetHpLoss);
            Assert.Near(15, normalResult.Totals.Player.Damage);
            Assert.Near(0, normalResult.Totals.Enemy.Damage);
            Assert.Near(dummyTarget.OutputDamage, normalTarget.OutputDamage);
            Assert.Near(dummyTarget.NetHpLoss, normalTarget.NetHpLoss);
            AssertLedger(dummyResult, Side.Enemy, 100000);
            AssertLedger(normalResult, Side.Enemy, 100000);
        }

        private static void OverkillAndPreventedStayOutOfOutput()
        {
            // 4 Burn into 2 HP: native keeps the full 4 in TotalDamage, but
            // true output is the 2 HP actually removed.
            BattleConfig lethal = PlayerBoard(5, Real("Blow Torch", 0));
            lethal.EnemyMaxHp = 2;
            lethal.EnemyStartHp = 2;
            BattleResult killed = BattleEngine.Simulate(lethal);
            Assert.True(killed.EndedByDeath);
            BattleDamageSnapshot dead = Aggregator.BuildDamage(killed, 2).At(double.PositiveInfinity);
            Assert.Near(4, dead.Damage);
            Assert.Near(2, dead.OutputDamage);
            Assert.Near(2, dead.EffectiveDamage);
            Assert.Near(2, dead.OverkillDamage);
            Assert.Near(0, dead.PreventedDamage);
            Assert.Near(4, dead.SettledDamage);
            Assert.Near(2, dead.NetHpLoss);
            Assert.Near(0, dead.EnemyHp);
            Assert.Near(4, killed.Totals.Player.Damage);
            Assert.Near(2, killed.Totals.Player.EffectiveDamage);
            Assert.Near(2, killed.Totals.Player.Overkill);
            AssertLedger(killed, Side.Enemy, 2);

            // 4 Burn into 1 Shield: the shield point prevents 2 damage, so the
            // raw 4 splits into 3 realized output and 1 prevented packet.
            BattleConfig shielded = PlayerBoard(5, Real("Blow Torch", 0));
            shielded.EnemyMaxHp = 100;
            shielded.EnemyStartHp = 100;
            shielded.EnemyStartShield = 1;
            BattleResult guarded = BattleEngine.Simulate(shielded);
            BattleDamageSnapshot block = Aggregator.BuildDamage(guarded, 100).At(5);
            Assert.Near(7, block.Damage);
            Assert.Near(6, block.OutputDamage);
            Assert.Near(5, block.EffectiveDamage);
            Assert.Near(1, block.ShieldAbsorbed);
            Assert.Near(1, block.PreventedDamage);
            Assert.Near(0, block.OverkillDamage);
            Assert.Near(7, block.SettledDamage + block.PreventedDamage);
            Assert.Near(5, block.NetHpLoss);
            Assert.Near(95, block.EnemyHp);
            Assert.Near(0, block.EnemyShield);
            AssertLedger(guarded, Side.Enemy, 100);
        }

        private static void BurnDoesNotDecayWhenNoDamageWasDealt()
        {
            // Native replay 084bd840: 1 Burn applied at 6.0s survives behind
            // 50 Shield until a second Burn arrives at 12.0s. A 1-stack Burn
            // tick costs floor(1/2)=0 Shield, changes neither HP nor Shield,
            // and therefore must not decay. Two Burn against 1 Shield does
            // consume the Shield point and then decays.
            BattleConfig config = PlayerBoard(9, Real("Soldering Gun", 0, "Bronze", 4000));
            config.EnemyMaxHp = 1000;
            config.EnemyStartHp = 1000;
            config.EnemyStartShield = 50;
            BattleResult result = BattleEngine.Simulate(config);
            BattleHealthSeries health = new BattleHealthSeries(result);
            Assert.Near(1, health.At(5).Enemy.Burn);
            Assert.Near(50, health.At(5).Enemy.Shield);
            Assert.Near(1000, health.At(5).Enemy.Hp);
            // The first two activations (4s, 8s) stack to 2 Burn; the tick at
            // 8.5s consumes one Shield point and decays to 1.
            Assert.Near(2, health.At(8).Enemy.Burn);
            Assert.Near(1, health.At(8.5).Enemy.Burn);
            Assert.Near(49, health.At(8.5).Enemy.Shield);
            Assert.Near(1000, health.At(8.5).Enemy.Hp);
            AssertLedger(result, Side.Enemy, 1000);
        }

        private static void ShardOfObsidianIgnoresStaleAbilityId()
        {
            // DB 5.0.0 lists AbilityIds ["0","1"] but Abilities only has "0".
            // Ability 1 is a stale data id; ability 0 is the complete
            // Damage+Lifesteal card. It must not fail-closed the whole card.
            BattleConfig config = PlayerBoard(6, RealById(
                "8981892b-ce2a-43dc-a08e-2b5e8f8884fc", 0, "Bronze"));
            config.PlayerMaxHp = 100;
            config.PlayerStartHp = 80;
            config.EnemyMaxHp = 1000;
            config.EnemyStartHp = 1000;
            BattleResult result = BattleEngine.Simulate(config);
            BattleDamageSnapshot enemy = Aggregator.BuildDamage(result, 1000).At(6);
            Assert.Near(5, enemy.DirectDamage);
            Assert.Near(5, enemy.OutputDamage);
            Assert.Near(5, result.Totals.Player.Damage);
            BattleDamageSnapshot player = Aggregator.BuildDamage(result, Side.Player, 100).At(6);
            Assert.Near(85, player.EnemyHp);
            Assert.Near(5, player.Healing);
            Assert.Near(5, result.Totals.Player.Heal);
            AssertLedger(result, Side.Enemy, 1000);
            AssertLedger(result, Side.Player, 100);
        }
    }
}
