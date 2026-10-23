using System;
using System.Collections.Generic;
using System.IO;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class MonsterOpponentTests
    {
        public static void Run()
        {
            SparseTierAttributesAndOverrides();
            ExactNativeMonsterBoard();
            EnemyNamespacesAndPlayerSkills();
            NativeMonsterFightEndToEnd();
        }

        private static void SparseTierAttributesAndOverrides()
        {
            JsonValue template = Json.Parse(@"{""$type"":""TCardItem"",""Size"":""Medium"",""Tiers"":{""Bronze"":{""Attributes"":{""CooldownMax"":4000,""DamageAmount"":10,""Multicast"":1}},""Silver"":{""Attributes"":{""DamageAmount"":20}},""Gold"":{""Attributes"":{""DamageAmount"":30}}},""Enchantments"":{""Toxic"":{""Attributes"":{""PoisonApplyAmount"":3}}}}");
            JsonValue monster = Json.Parse(@"{""Id"":""m"",""InternalName"":""test"",""Player"":{""Attributes"":{""HealthMax"":900},""Hand"":{""Items"":[{""TemplateId"":""x"",""Tier"":""Silver"",""SocketId"":""Socket_4"",""EnchantmentType"":""Toxic"",""Attributes"":{""DamageAmount"":27}}]},""Skills"":[]}}");
            LiveMonsterEntry entry = MonsterTemplateExtractor.FromJson(monster, delegate(string id) { return template; });
            Assert.Near(900, entry.Health);
            Assert.Near(900, entry.MaxHp);
            Assert.Equal(4, entry.Cards[0].Socket);
            Assert.Equal(4000, entry.Cards[0].Attributes["CooldownMax"]);
            Assert.Equal(27, entry.Cards[0].Attributes["DamageAmount"]);
            Assert.Equal(3, entry.Cards[0].Attributes["PoisonApplyAmount"]);
            Assert.Equal("Medium", entry.Cards[0].Size);
            Assert.False(entry.Cards[0].AttributesAreLive);
            Assert.True(entry.Cards[0].Template == template);
            Assert.True(entry.Cards[0].EnchantmentDefinition.Kind == JsonKind.Object);
            Assert.Throws<InvalidOperationException>(delegate {
                MonsterTemplateExtractor.FromJson(Json.Parse(@"{""Player"":{}}"), null);
            });
        }

        private static LiveMonsterEntry Kyver()
        {
            JsonValue fixture = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_kyver_monster.json"));
            return MonsterTemplateExtractor.FromJson(fixture["monster"], delegate(string id) { return fixture["cards"][id]; });
        }

        private static void ExactNativeMonsterBoard()
        {
            LiveMonsterEntry monster = Kyver();
            Assert.Equal("Kyver Commander", monster.Name);
            Assert.Near(900, monster.MaxHp);
            Assert.Equal(6, monster.Cards.Count);
            Assert.Equal(1, monster.Skills.Count);
            Assert.Equal(0, monster.Warnings.Count);
            Assert.Equal(1, monster.Cards[0].Socket);
            Assert.Equal(8, monster.Cards[5].Socket);
            LiveCardEntry honeycomb = monster.Cards[4];
            Assert.Equal("618271c5-5721-40ac-b1c7-43aa06a02d07", honeycomb.TemplateId);
            Assert.Equal("Gold", honeycomb.Tier);
            Assert.Equal(5000, honeycomb.Attributes["CooldownMax"]);
            Assert.Equal(1000, honeycomb.Attributes["SlowAmount"]);
            Assert.Equal(3, honeycomb.Attributes["SlowTargets"]);
            Assert.Equal(3, honeycomb.Attributes["RegenApplyAmount"]);
        }

        private static void EnemyNamespacesAndPlayerSkills()
        {
            LiveMonsterEntry monster = Kyver();
            monster.Attributes["Shield"] = 7;
            monster.Attributes["HealthRegen"] = 2;
            CardCatalog catalog = CardCatalog.FromCards(new List<CardDef>());
            BattleConfig config = new BattleConfig();
            config.PlayerMaxHp = 1175;
            config.MaxTime = 30;
            config.NativeMechanics = true;
            CardDef playerSkill = new CardDef { Id = "player:skill" };
            config.PlayerSkills.Add(playerSkill);
            MonsterMappingResult mapped = MonsterOpponentMapper.Apply(config, monster, catalog, null);
            Assert.False(config.EnemyIsTrainingDummy);
            Assert.Near(900, config.EnemyMaxHp);
            Assert.Near(900, config.EnemyStartHp.Value);
            Assert.Near(7, config.EnemyStartShield);
            Assert.Near(2, config.EnemyStartRegen);
            Assert.Equal(1, config.PlayerSkills.Count);
            Assert.True(config.PlayerSkills[0] == playerSkill);
            Assert.Equal(6, config.Enemy.Items.Count);
            foreach (LoadoutItem item in config.Enemy.Items)
            {
                Assert.True(item.CardId.StartsWith("enemy:", StringComparison.Ordinal));
                Assert.True(config.Cards.ContainsKey(item.CardId));
                Assert.True(config.Cards[item.CardId].NativeTemplate.Kind == JsonKind.Object);
            }
            Assert.True(mapped.Skills != null);
        }

        private static void NativeMonsterFightEndToEnd()
        {
            BattleConfig config = new BattleConfig { PlayerMaxHp = 5000, NativeMechanics = true, MaxTime = 11 };
            CardDef player = new CardDef { Id = "player", Cooldown = 10 };
            config.Cards[player.Id] = player;
            config.Player.Items.Add(new LoadoutItem { CardId = player.Id, Slot = 0 });
            MonsterMappingResult mapped = MonsterOpponentMapper.Apply(config, Kyver(), CardCatalog.Load("src/data/cards.json"), null);
            Assert.Equal(0, mapped.Warnings.Count);
            Assert.Equal(1, mapped.Skills.CombatCount);
            Assert.Equal(0, mapped.Skills.UnsupportedCount);
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            Assert.Equal(0, NativeItemMapper.Apply(config).Count);
            CardDef lance = config.Cards[config.Enemy.Items[1].CardId];
            Assert.Near(500, lance.Effects[0].Amount.Value);
            CardDef honeycomb = config.Cards[config.Enemy.Items[4].CardId];
            Assert.Near(5, honeycomb.Cooldown);
            Assert.Equal("slow", honeycomb.Effects[0].Kind);
            Assert.Equal(3, honeycomb.Effects[0].Count.Value);
            Assert.Equal("regen", honeycomb.Effects[1].Kind);
            Assert.Near(3, honeycomb.Effects[1].Amount.Value);
            BattleResult fight = BattleEngine.Simulate(config);
            Assert.True(fight.Totals.Enemy.Damage > 500);
            Assert.Equal(6, fight.EnemyItems.Count);
            Assert.Equal(1, fight.PlayerItems.Count);
        }
    }
}
