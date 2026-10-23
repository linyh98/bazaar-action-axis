using System;
using System.Collections.Generic;
using System.IO;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class ParityRunner
    {
        public static void Run(string path)
        {
            JsonValue root = Json.Parse(File.ReadAllText(path));
            List<JsonValue> scenarios = root["scenarios"].AsArray();
            foreach (JsonValue scenario in scenarios)
            {
                string name = scenario["name"].AsString();
                int before = Assert.Failures;
                try
                {
                    RunScenario(scenario);
                }
                catch (Exception error)
                {
                    Assert.Failures++;
                    Console.WriteLine("  exception: " + error);
                }

                Console.WriteLine((Assert.Failures == before ? "PASS " : "FAIL ") + name);
            }
        }

        private static void RunScenario(JsonValue scenario)
        {
            BattleConfig config = ParseConfig(scenario["config"]);
            BattleResult actual = BattleEngine.Simulate(config);
            JsonValue expected = scenario["expected"];

            List<JsonValue> expectedEvents = expected["events"].AsArray();
            if (expectedEvents.Count != actual.Events.Count)
            {
                Assert.Failures++;
                Console.WriteLine(
                    "  event count expected " + expectedEvents.Count +
                    " but was " + actual.Events.Count);
            }

            int count = Math.Min(expectedEvents.Count, actual.Events.Count);
            for (int i = 0; i < count; i++)
                CompareEvent(expectedEvents[i], actual.Events[i], i);

            CompareTotalsSide(expected["totals"]["player"], actual.Totals.Player, "player");
            CompareTotalsSide(expected["totals"]["enemy"], actual.Totals.Enemy, "enemy");

            Assert.Equal(expected["winner"].AsString(), actual.Winner);
            Assert.Near(expected["endTime"].AsDouble(), actual.EndTime, 1e-6);

            List<JsonValue> expectedWarnings = expected["warnings"].AsArray();
            Assert.Equal(expectedWarnings.Count, actual.Warnings.Count);
            int warningCount = Math.Min(expectedWarnings.Count, actual.Warnings.Count);
            for (int i = 0; i < warningCount; i++)
                Assert.Equal(expectedWarnings[i].AsString(), actual.Warnings[i]);
        }

        private static void CompareEvent(JsonValue expected, BattleEvent actual, int index)
        {
            int before = Assert.Failures;
            Assert.Equal(expected["kind"].AsString(), actual.Kind);
            Assert.Near(expected["time"].AsDouble(), actual.Time, 1e-6);
            Assert.Equal(expected["side"].AsString(), SideUtil.Key(actual.Side));
            Assert.Equal(OptionalString(expected["sourceItemId"]), actual.SourceItemId);
            Assert.Equal(OptionalString(expected["sourceCardId"]), actual.SourceCardId);
            Assert.Equal(OptionalString(expected["targetItemId"]), actual.TargetItemId);
            Assert.Equal(
                OptionalString(expected["targetSide"]),
                actual.TargetSide.HasValue ? SideUtil.Key(actual.TargetSide.Value) : null);
            CompareOptional(expected["value"], actual.Value);
            CompareOptional(expected["absorbed"], actual.Absorbed);
            CompareOptional(expected["hpAfter"], actual.HpAfter);
            CompareOptional(expected["shieldAfter"], actual.ShieldAfter);
            Assert.Equal(OptionalString(expected["note"]), actual.Note);
            if (Assert.Failures > before)
                Console.WriteLine("  ^ event " + index + " kind=" + actual.Kind + " t=" + actual.Time);
        }

        private static void CompareTotalsSide(JsonValue expected, TotalsSide actual, string label)
        {
            Assert.Near(expected["damage"].AsDouble(), actual.Damage);
            Assert.Near(expected["effectiveDamage"].AsDouble(), actual.EffectiveDamage);
            Assert.Near(expected["heal"].AsDouble(), actual.Heal);
            Assert.Near(expected["shield"].AsDouble(), actual.Shield);
        }

        private static void CompareOptional(JsonValue expected, double? actual)
        {
            bool expectedNull = expected.Kind == JsonKind.Null;
            if (expectedNull && !actual.HasValue) return;
            if (expectedNull || !actual.HasValue)
            {
                Assert.Failures++;
                Console.WriteLine(
                    "  optional mismatch expected <" + (expectedNull ? "null" : expected.AsDouble().ToString()) +
                    "> actual <" + (actual.HasValue ? actual.Value.ToString() : "null") + ">");
                return;
            }

            Assert.Near(expected.AsDouble(), actual.Value);
        }

        private static string OptionalString(JsonValue value)
        {
            return value.Kind == JsonKind.Null ? null : value.AsString();
        }

        private static BattleConfig ParseConfig(JsonValue value)
        {
            BattleConfig config = new BattleConfig();
            Dictionary<string, JsonValue> cards = value["cards"].AsObject();
            foreach (KeyValuePair<string, JsonValue> pair in cards)
            {
                CardDef card = CardData.FromJson(pair.Value);
                if (card != null) config.Cards[pair.Key] = card;
            }

            config.Player = ParseLoadout(value["player"]);
            config.Enemy = ParseLoadout(value["enemy"]);
            config.PlayerMaxHp = value["playerMaxHp"].AsDouble();
            config.EnemyMaxHp = value["enemyMaxHp"].AsDouble();
            config.Seed = value["seed"].AsDouble();
            config.MaxTime = value["maxTime"].AsDouble();
            config.Rules = ParseRules(value["rules"]);
            return config;
        }

        private static Loadout ParseLoadout(JsonValue value)
        {
            Loadout loadout = new Loadout();
            foreach (JsonValue entry in value["items"].AsArray())
            {
                LoadoutItem item = new LoadoutItem();
                item.CardId = entry["cardId"].AsString();
                item.Slot = entry["slot"].AsInt();
                loadout.Items.Add(item);
            }
            return loadout;
        }

        private static RuleOverrides ParseRules(JsonValue value)
        {
            RuleOverrides rules = new RuleOverrides();
            if (value.Has("dt")) rules.Dt = value["dt"].AsDouble();
            if (value.Has("maxTime")) rules.MaxTime = value["maxTime"].AsDouble();
            if (value.Has("hasteMultiplier")) rules.HasteMultiplier = value["hasteMultiplier"].AsDouble();
            if (value.Has("slowMultiplier")) rules.SlowMultiplier = value["slowMultiplier"].AsDouble();
            if (value.Has("startReady")) rules.StartReady = value["startReady"].AsBool();
            if (value.Has("burnDecay")) rules.BurnDecay = value["burnDecay"].AsDouble();
            if (value.Has("poisonDecay")) rules.PoisonDecay = value["poisonDecay"].AsDouble();
            if (value.Has("dotBypassesShield")) rules.DotBypassesShield = value["dotBypassesShield"].AsBool();
            if (value.Has("critMultiplier")) rules.CritMultiplier = value["critMultiplier"].AsDouble();
            if (value.Has("lifestealOnShieldDamage")) rules.LifestealOnShieldDamage = value["lifestealOnShieldDamage"].AsBool();
            if (value.Has("activationOrder")) rules.ActivationOrder = value["activationOrder"].AsString();
            if (value.Has("maxEventsPerTick")) rules.MaxEventsPerTick = value["maxEventsPerTick"].AsInt();
            if (value.Has("maxTriggerDepth")) rules.MaxTriggerDepth = value["maxTriggerDepth"].AsInt();
            if (value.Has("maxSlots")) rules.MaxSlots = value["maxSlots"].AsInt();
            return rules;
        }
    }
}
