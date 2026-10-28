using System;
using System.Linq;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class ReplayLethalLowPhaseTests
    {
        public static void Run()
        {
            LowPhaseKeepsAllAcceptedMediumActionsBeforeDeath();
            WithoutLowPhaseLethalCastStillStopsTheBatch();
        }

        private static void Add(BattleConfig config, CardDef card, Side side, int slot)
        {
            config.Cards[card.Id] = card;
            (side == Side.Player ? config.Player : config.Enemy).Items.Add(new LoadoutItem {
                CardId = card.Id, Slot = slot
            });
        }

        private static CardDef Attack(string id, double amount)
        {
            CardDef card = new CardDef { Id = id, Cooldown = 1, AmmoMax = 1 };
            card.Effects.Add(new Effect { Kind = "damage", Priority = "Medium",
                Amount = new NumExpr { Value = amount }, Target = new Target { Kind = "enemyHero" } });
            return card;
        }

        private static BattleResult Simulate(bool low)
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 1.1,
                PlayerMaxHp = 100, EnemyMaxHp = 100, EnemyStartHp = 15 };
            // 101f635: two accepted Venomander/Quill casts each contribute
            // Medium Poison and Low Regen around the lethal weapon's cast.
            Add(config, PoisonWithRegen("low-first", low), Side.Enemy, 0);
            Add(config, Attack("lethal-first", 20), Side.Player, 0);
            Add(config, PoisonWithRegen("low-second", low), Side.Enemy, 1);
            Add(config, Attack("accepted-second", 30), Side.Player, 1);
            return BattleEngine.Simulate(config);
        }

        private static CardDef PoisonWithRegen(string id, bool low)
        {
            CardDef card = new CardDef { Id = id, Cooldown = 1, AmmoMax = 1 };
            card.Effects.Add(new Effect { Kind = "poison", Priority = "Medium",
                Amount = new NumExpr { Value = 3 }, Target = new Target { Kind = "enemyHero" } });
            if (low)
                card.Effects.Add(new Effect { Kind = "regen", Priority = "Low",
                    Amount = new NumExpr { Value = 1 }, Target = new Target { Kind = "selfHero" } });
            return card;
        }

        private static void LowPhaseKeepsAllAcceptedMediumActionsBeforeDeath()
        {
            BattleResult result = Simulate(true);
            Assert.Equal(0, result.Warnings.Count);
            Assert.True(result.EndedByDeath);
            Assert.Equal("player", result.Winner);
            Assert.Near(1, result.EndTime);
            Assert.Equal(2, result.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "damage"; }));
            Assert.Equal(2, result.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "poison"; }));
            Assert.Equal(2, result.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "regen"; }));
            BattleEvent lastDamage = result.Events.Last(delegate(BattleEvent ev) { return ev.Kind == "damage"; });
            BattleEvent firstRegen = result.Events.First(delegate(BattleEvent ev) { return ev.Kind == "regen"; });
            BattleEvent death = result.Events.First(delegate(BattleEvent ev) { return ev.Kind == "death"; });
            Assert.True(result.Events.IndexOf(lastDamage) < result.Events.IndexOf(firstRegen));
            Assert.True(result.Events.IndexOf(result.Events.Last(delegate(BattleEvent ev) { return ev.Kind == "regen"; })) <
                result.Events.IndexOf(death));
            BattleHealthSnapshot final = result.HealthSnapshots[result.HealthSnapshots.Count - 1];
            Assert.Near(6, final.Player.Poison);
            Assert.Near(2, final.Enemy.Regen);
            Assert.Near(0, final.Enemy.Hp);
        }

        private static void WithoutLowPhaseLethalCastStillStopsTheBatch()
        {
            BattleResult result = Simulate(false);
            Assert.True(result.EndedByDeath);
            Assert.Equal(1, result.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "damage"; }));
            Assert.Equal(0, result.Events.Count(delegate(BattleEvent ev) {
                return ev.Kind == "damage" && ev.SourceItemId == "accepted-second";
            }));
        }
    }
}
