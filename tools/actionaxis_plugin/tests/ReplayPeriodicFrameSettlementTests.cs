using System;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class ReplayPeriodicFrameSettlementTests
    {
        public static void Run()
        {
            LethalPeriodicPacketKeepsBothAcceptedCastsAndBothRegenerationPackets();
            OrdinaryLethalCardStopsTheNextCast();
        }

        private static void Add(BattleConfig config, CardDef card, Side side, int slot)
        {
            config.Cards[card.Id] = card;
            (side == Side.Player ? config.Player : config.Enemy).Items.Add(new LoadoutItem { CardId = card.Id, Slot = slot });
        }

        private static CardDef Attack(string id, double amount)
        {
            CardDef card = new CardDef { Id = id, Cooldown = 1, AmmoMax = 1 };
            card.Effects.Add(new Effect { Kind = "damage", Priority = "Medium",
                Amount = new NumExpr { Value = amount }, Target = new Target { Kind = "enemyHero" } });
            return card;
        }

        private static void LethalPeriodicPacketKeepsBothAcceptedCastsAndBothRegenerationPackets()
        {
            // Both player cards reserve their casts at 0.95. Poison becomes
            // lethal at 1.00, before either reserved action executes. The
            // opponent's same-second regeneration still precedes both attacks.
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 1.1,
                PlayerMaxHp = 100, EnemyMaxHp = 100, PlayerStartHp = 50, EnemyStartHp = 40,
                PlayerStartRegen = 5, EnemyStartRegen = 7 };
            Add(config, Attack("accepted-first", 10), Side.Player, 0);
            Add(config, Attack("accepted-second", 15), Side.Player, 1);
            CardDef poison = new CardDef { Id = "periodic-poison", Cooldown = 0.5, AmmoMax = 1 };
            poison.Effects.Add(new Effect { Kind = "poison", Priority = "Medium",
                Amount = new NumExpr { Value = 70 }, Target = new Target { Kind = "enemyHero" } });
            Add(config, poison, Side.Enemy, 0);
            BattleResult result = BattleEngine.Simulate(config);
            Assert.True(result.EndedByDeath);
            Assert.Equal("enemy", result.Winner);
            Assert.Near(1, result.EndTime);
            int first = result.Events.FindIndex(delegate(BattleEvent ev) {
                return ev.Kind == "damage" && ev.SourceItemId == "accepted-first"; });
            int second = result.Events.FindIndex(delegate(BattleEvent ev) {
                return ev.Kind == "damage" && ev.SourceItemId == "accepted-second"; });
            int death = result.Events.FindIndex(delegate(BattleEvent ev) { return ev.Kind == "death"; });
            Assert.True(first >= 0 && second > first && death > second);
            if (first >= 0) { Assert.Near(1, result.Events[first].Time); Assert.Near(10, result.Events[first].Value.Value); }
            if (second >= 0) { Assert.Near(1, result.Events[second].Time); Assert.Near(15, result.Events[second].Value.Value); }
            foreach (Side side in new[] { Side.Player, Side.Enemy })
            {
                int regen = result.Events.FindIndex(delegate(BattleEvent ev) {
                    return ev.Kind == "dotTick" && ev.Note == "regen" && ev.Side == side && Math.Abs(ev.Time - 1) < 1e-9; });
                Assert.True(regen >= 0 && regen < first);
                if (regen >= 0) Assert.Near(side == Side.Player ? 5 : 7, result.Events[regen].Value.Value);
            }
            BattleHealthSnapshot last = result.HealthSnapshots[result.HealthSnapshots.Count - 1];
            Assert.Near(0, last.Player.Hp);
            Assert.Near(29, last.Enemy.Hp); // 40 + 7 at t=0 + 7 at t=1 - 10 - 15.
        }

        private static void OrdinaryLethalCardStopsTheNextCast()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 1.1,
                PlayerMaxHp = 100, EnemyMaxHp = 100, EnemyStartHp = 15 };
            Add(config, Attack("ordinary-first", 20), Side.Player, 0);
            Add(config, Attack("ordinary-second", 30), Side.Player, 1);
            BattleResult result = BattleEngine.Simulate(config);
            Assert.True(result.EndedByDeath);
            Assert.Equal("player", result.Winner);
            Assert.Near(1, result.EndTime);
            Assert.Equal(1, result.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "damage"; }).Count);
            Assert.Equal(0, result.Events.FindAll(delegate(BattleEvent ev) {
                return ev.Kind == "damage" && ev.SourceItemId == "ordinary-second"; }).Count);
        }
    }
}
