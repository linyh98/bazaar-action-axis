using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // G11: sandstorm start vs. default damage split, encounter-selected
    // whitelist, and hybrid combat/out-of-combat abilities.
    // All card templates are exported from the native DB; cooldowns are
    // shortened only so the tests do not wait for the real 25-35 s timers.
    public static class G11SandstormTests
    {
        private static JsonValue _fixtures;
        private static readonly CardCatalog EmptyCatalog = CardCatalog.FromCards(new List<CardDef>());

        public static void Run()
        {
            _fixtures = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_g11_sandstorm.json"));
            SandstormActionMapsAndStartsOnce();
            DefaultSandstormMatchesReplayCadence();
            SandstormTriggerDoublesMaxHealthOnce();
            EncounterSelectedIsNonCombatAndKeepsPotionAuras();
            PeacewroughtMapsWithoutEconomicRollback();
            CashRegisterGoldenEconomicAuraIsSkipped();
            GoldActionOnCombatTriggerIsNoOp();
        }

        private static LiveCardEntry Real(string name, int slot, string tier = "Gold", int? cooldownMs = null)
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

        private static LiveCardEntry RealEnchanted(string name, int slot, string tier, string enchantment)
        {
            LiveCardEntry entry = Real(name, slot, tier);
            entry.Enchantment = enchantment;
            entry.EnchantmentDefinition = _fixtures[name]["template"]["Enchantments"][enchantment];
            return entry;
        }

        private static BattleConfig PlayerBoard(double maxTime, params LiveCardEntry[] cards)
        {
            BattleConfig config = CardMapper.Build(new List<LiveCardEntry>(cards), EmptyCatalog, 100, 100, maxTime).Config;
            config.NativeMechanics = true;
            List<string> warnings = NativeCardAbilityMapper.Apply(config);
            Assert.Equal("", string.Join(" | ", warnings.ToArray()));
            return config;
        }

        private static string Id(BattleConfig config, string name)
        {
            foreach (CardDef card in config.Cards.Values)
                if (card.Name == name) return card.Id;
            throw new InvalidOperationException("fixture card not mapped: " + name);
        }

        private static void SandstormActionMapsAndStartsOnce()
        {
            BattleConfig config = PlayerBoard(1.2, Real("Bottled Tornado", 0, "Silver", 1000));
            string id = Id(config, "Bottled Tornado");
            CardDef card = config.Cards[id];
            Assert.Equal("structured", card.ParseStatus);
            Assert.True(card.Effects.Any(delegate(Effect effect) { return effect.Kind == "beginSandstorm"; }));
            // A0 must not hide the card's other actions.
            Assert.True(card.Effects.Any(delegate(Effect effect) { return effect.Kind == "slow"; }));
            Assert.True(card.Effects.Any(delegate(Effect effect) { return effect.Kind == "modifyAttribute"; }));

            BattleResult result = BattleEngine.Simulate(config);
            List<BattleEvent> starts = result.Events.Where(delegate(BattleEvent ev)
            { return ev.Kind == "beginSandstorm"; }).ToList();
            Assert.Equal(1, starts.Count);
            Assert.Equal(id, starts[0].SourceItemId);
            Assert.Near(1.05, starts[0].Time, 0.06);
            Assert.Near(1.05, result.SandstormStartedAt, 0.06);
            // The card start replaces the default 30 s start rather than adding one.
            Assert.Equal(0, result.Events.Count(delegate(BattleEvent ev)
            { return ev.Kind == "beginSandstorm" && ev.Time > 2; }));
        }

        private static void DefaultSandstormMatchesReplayCadence()
        {
            // CombatReplays: countdown starts at 20.0 s, sandstorm at 30.0 s;
            // 5-frame ticks x5, then 4-frame ticks x5, then 3-frame floor;
            // damage 1 for the first ten ticks, then +2 per tick.
            BattleConfig config = PlayerBoard(42.6);
            config.PlayerMaxHp = 10000;
            config.EnemyMaxHp = 10000;
            config.PlayerStartHp = 10000;
            config.EnemyStartHp = 10000;
            BattleResult result = BattleEngine.Simulate(config);
            List<BattleEvent> ticks = result.Events.Where(delegate(BattleEvent ev)
            { return ev.Kind == "dotTick" && ev.Note == "sandstorm" && ev.TargetSide == Side.Player; }).ToList();
            double[] times = { 30.0, 30.25, 30.5, 30.75, 31.0, 31.2, 31.4, 31.6, 31.8, 32.0, 32.15, 32.3, 32.45 };
            double[] values = { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 3, 5, 7 };
            for (int index = 0; index < times.Length; index++)
            {
                BattleEvent tick = ticks.Find(delegate(BattleEvent ev) { return Math.Abs(ev.Time - times[index]) < 1e-6; });
                Assert.True(tick != null);
                if (tick == null) continue;
                Assert.Near(values[index], tick.Value.Value, 1e-9);
                Assert.Equal(Side.Player, tick.TargetSide.Value);
            }
            // Continuous replay anchors beyond the initial ramp (06158435).
            AssertTick(ticks, 35.0, 41);
            AssertTick(ticks, 38.0, 81);
            AssertTick(ticks, 41.0, 121);
            AssertTick(ticks, 42.5, 141);

            BattleDamageSnapshot ledger = Aggregator.BuildDamage(result, Side.Player, 10000).At(42.6);
            // Sandstorm is environmental: health loss is tracked as OtherHpChange,
            // never as player output.
            Assert.Near(0, ledger.OutputDamage);
            Assert.Near(0, ledger.Damage);
            Assert.True(ledger.NetHpLoss > 0);
            Assert.True(ledger.OtherHpChange < 0);
            Assert.Near(ledger.NetHpLoss, -ledger.OtherHpChange);
        }

        private static void AssertTick(List<BattleEvent> ticks, double time, double value)
        {
            BattleEvent tick = ticks.Find(delegate(BattleEvent ev) { return Math.Abs(ev.Time - time) < 1e-6; });
            Assert.True(tick != null);
            if (tick != null) Assert.Near(value, tick.Value.Value, 1e-9);
        }

        private static void SandstormTriggerDoublesMaxHealthOnce()
        {
            BattleConfig config = PlayerBoard(1.2, Real("Windmill", 0, "Gold", 1000));
            BattleResult result = BattleEngine.Simulate(config);
            List<BattleEvent> starts = result.Events.Where(delegate(BattleEvent ev)
            { return ev.Kind == "beginSandstorm"; }).ToList();
            Assert.Equal(1, starts.Count);
            List<BattleEvent> maxHealth = result.Events.Where(delegate(BattleEvent ev)
            { return ev.Kind == "playerAttribute" && ev.Note == "HealthMax"; }).ToList();
            Assert.Equal(1, maxHealth.Count);
            Assert.Near(100, maxHealth[0].Value.Value);
            Assert.Equal(200.0, result.HealthSnapshots[result.HealthSnapshots.Count - 1].Player.MaxHp);
        }

        private static void EncounterSelectedIsNonCombatAndKeepsPotionAuras()
        {
            JsonValue trigger = Json.Parse("{\"$type\":\"TTriggerOnEncounterSelected\"}");
            Assert.True(SkillMapper.IsNonCombatTrigger(trigger));
            // CardSelected is a card-selection screen trigger, not a combat
            // trigger; G03 needs it skipped so Machine Learning/Renovation
            // are OUT instead of rejected by their upgrade action.
            Assert.True(SkillMapper.IsNonCombatTrigger(Json.Parse("{\"$type\":\"TTriggerOnCardSelected\"}")));

            BattleConfig config = PlayerBoard(1, Real("Potion Distillery", 0, "Silver"));
            CardDef card = config.Cards[Id(config, "Potion Distillery")];
            Assert.Equal("structured", card.ParseStatus);
            Assert.Equal(0, card.Triggers.Count);
            Assert.Equal(2, card.NativeRuntimeAuras.Count);
        }

        private static void PeacewroughtMapsWithoutEconomicRollback()
        {
            BattleConfig config = PlayerBoard(1, Real("Peacewrought", 0, "Bronze"));
            CardDef card = config.Cards[Id(config, "Peacewrought")];
            Assert.Equal("structured", card.ParseStatus);
            Assert.True(card.Effects.Any(delegate(Effect effect) { return effect.Kind == "regen"; }));
            Assert.True(card.Triggers.Any(delegate(TriggerDef trigger) { return trigger.Event == "onBeforeDestroy"; }));
            Assert.True(card.Triggers.Any(delegate(TriggerDef trigger) { return trigger.Event == "onCardDestroyed"; }));
        }

        private static void CashRegisterGoldenEconomicAuraIsSkipped()
        {
            // Golden adds an out-of-combat Income aura. It must not be turned
            // into a combat stat, and it must not roll back the whole card.
            BattleConfig config = PlayerBoard(1, RealEnchanted("Cash Register", 0, "Gold", "Golden"));
            CardDef card = config.Cards[Id(config, "Cash Register")];
            Assert.Equal("structured", card.ParseStatus);
            Assert.Equal(0, card.NativeRuntimeAuras.Count);
        }

        private static void GoldActionOnCombatTriggerIsNoOp()
        {
            Dictionary<string, double> attrs = new Dictionary<string, double>();
            string gold = "{\"$type\":\"TActionPlayerModifyAttribute\",\"AttributeType\":\"Gold\",\"Operation\":\"Add\",\"Value\":{\"$type\":\"TFixedValue\",\"Value\":5},\"Target\":{\"$type\":\"TTargetPlayerAbsolute\",\"TargetMode\":\"Player\",\"Conditions\":null}}";
            Effect effect = SkillMapper.MapAction(Json.Parse(gold), attrs);
            Assert.Equal("noop", effect.Kind);
            Assert.Equal("Gold", effect.Attribute);

            string damage = "{\"$type\":\"TActionPlayerDamage\",\"ReferenceValue\":{\"$type\":\"TFixedValue\",\"Value\":7},\"Target\":{\"$type\":\"TTargetPlayerRelative\",\"TargetMode\":\"Opponent\",\"Conditions\":null}}";
            string combined = "{\"$type\":\"TActionAnd\",\"Actions\":[" + gold + "," + damage + "]}";
            Effect composite = SkillMapper.MapAction(Json.Parse(combined), attrs);
            Assert.Equal("composite", composite.Kind);
            Assert.Equal(2, composite.Effects.Count);
            Assert.Equal("noop", composite.Effects[0].Kind);
            Assert.Equal("damage", composite.Effects[1].Kind);
        }
    }
}
