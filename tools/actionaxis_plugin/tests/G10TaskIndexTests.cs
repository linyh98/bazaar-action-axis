using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // G10 task-index guard: every primary_group=G10 template from
    // docs/native-rule-task-index.csv must be present in the exported native
    // fixture, every G10 player action must still map, and every mapped card
    // must keep the damage/heal ledger identities on both sides.
    public static class G10TaskIndexTests
    {
        private const string FixturePath = "tools/actionaxis_plugin/tests/fixtures/native_g10_templates.json";
        private const string IndexPath = "docs/native-rule-task-index.csv";
        private static readonly CardCatalog EmptyCatalog = CardCatalog.FromCards(new List<CardDef>());
        private static readonly string[] G10ActionTypes =
        {
            "TActionPlayerDamage",
            "TActionPlayerHeal",
            "TActionPlayerShieldApply",
            "TActionPlayerBurnApply",
            "TActionPlayerPoisonApply",
            "TActionPlayerRegenApply"
        };

        public static void Run()
        {
            JsonValue fixtures = Json.Parse(File.ReadAllText(FixturePath));
            List<IndexRow> rows = ReadIndex(IndexPath).FindAll(delegate(IndexRow row)
            {
                return row.PrimaryGroup == "G10";
            });
            Assert.Equal(62, rows.Count);
            Assert.Equal(rows.Count, fixtures.AsObject().Count);

            HashSet<string> seen = new HashSet<string>();
            HashSet<string> kinds = new HashSet<string>();
            int mappedActions = 0;
            foreach (IndexRow row in rows)
            {
                Assert.True(seen.Add(row.TemplateId));
                JsonValue fixture = fixtures[row.TemplateId];
                Assert.Equal(JsonKind.Object, fixture.Kind);
                if (fixture.Kind != JsonKind.Object) continue;
                Assert.Equal("cards", fixture["table"].AsString());
                Assert.Equal(row.TemplateId, fixture["id"].AsString());
                Assert.Equal(row.Name, fixture["name"].AsString());
                JsonValue template = fixture["template"];
                string tier = TierFor(template);
                Dictionary<string, int> attributes = MonsterTemplateExtractor.TierAttributes(template, tier);

                List<ActionCase> actions = new List<ActionCase>();
                CollectActions(template, attributes, actions);
                Assert.True(actions.Count > 0);
                foreach (ActionCase actionCase in actions)
                {
                    JsonValue action = actionCase.Action;
                    string type = action["$type"].AsString();
                    string expected = KindFor(type);
                    if (expected == null) continue;
                    Effect effect;
                    try
                    {
                        effect = SkillMapper.MapAction(action, ToDoubles(actionCase.Attributes));
                    }
                    catch (Exception error)
                    {
                        throw new InvalidOperationException(
                            "G10 action mapping failed: " + row.Name + " / " + type + " / " + error.Message, error);
                    }
                    Assert.Equal(expected, effect.Kind);
                    Assert.True(effect.Target != null);
                    kinds.Add(effect.Kind);
                    mappedActions++;
                }

                // At least the real card must simulate without an engine
                // exception; unrelated mapper warnings are not promoted here.
                BattleConfig config = Build(row, template, tier, attributes);
                CardDef mapped = config.Cards[config.Player.Items[0].CardId];
                Assert.Equal(0, mapped.MappingWarnings.Count);
                BattleResult result = BattleEngine.Simulate(config);
                Assert.False(result.Warnings.Any(delegate(string warning)
                {
                    return warning.StartsWith("simulation failed", StringComparison.Ordinal);
                }));
                AssertLedger(result, Side.Enemy, config.EnemyMaxHp);
                AssertLedger(result, Side.Player, config.PlayerMaxHp);
                AssertAttribution(result, Side.Enemy, config.EnemyMaxHp);
                AssertAttribution(result, Side.Player, config.PlayerMaxHp);
            }

            // The family as a whole must exercise all six action kinds, not
            // merely the three most common ones.
            Assert.True(mappedActions >= rows.Count);
            foreach (string kind in new[] { "damage", "heal", "shield", "burn", "poison", "regen" })
                Assert.True(kinds.Contains(kind));
        }

        private sealed class IndexRow
        {
            public string TemplateId;
            public string Name;
            public string PrimaryGroup;
        }

        private sealed class ActionCase
        {
            public JsonValue Action;
            public Dictionary<string, int> Attributes;
        }

        private static BattleConfig Build(
            IndexRow row,
            JsonValue template,
            string tier,
            Dictionary<string, int> attributes)
        {
            LiveCardEntry entry = new LiveCardEntry
            {
                TemplateId = row.TemplateId,
                Template = template,
                Tier = tier,
                Socket = 0,
                Size = template["Size"].AsString(),
                AttributesAreLive = false,
                Attributes = attributes
            };
            List<LiveCardEntry> all = new List<LiveCardEntry>();
            all.Add(entry);
            BattleConfig config = CardMapper.Build(all, EmptyCatalog, 1000, 1000, 8).Config;
            config.NativeMechanics = true;
            config.PlayerMaxHp = 1000;
            config.EnemyMaxHp = 1000;
            config.PlayerStartHp = 800;
            config.EnemyStartHp = 800;
            // Exercise shield absorption and overkill splits for every card.
            config.PlayerStartShield = 7;
            config.EnemyStartShield = 7;
            NativeCardAbilityMapper.Apply(config);
            return config;
        }

        private static string TierFor(JsonValue template)
        {
            foreach (string tier in new[] { "Bronze", "Silver", "Gold", "Diamond", "Legendary" })
                if (template["Tiers"].Has(tier)) return tier;
            return "Bronze";
        }

        private static Dictionary<string, double> ToDoubles(Dictionary<string, int> values)
        {
            Dictionary<string, double> result = new Dictionary<string, double>();
            foreach (KeyValuePair<string, int> pair in values) result[pair.Key] = pair.Value;
            return result;
        }

        private static string KindFor(string type)
        {
            switch (type)
            {
                case "TActionPlayerDamage": return "damage";
                case "TActionPlayerHeal": return "heal";
                case "TActionPlayerShieldApply": return "shield";
                case "TActionPlayerBurnApply": return "burn";
                case "TActionPlayerPoisonApply": return "poison";
                case "TActionPlayerRegenApply": return "regen";
                default: return null;
            }
        }

        private static void CollectActions(
            JsonValue value,
            Dictionary<string, int> inherited,
            List<ActionCase> actions)
        {
            if (value.Kind == JsonKind.Object)
            {
                Dictionary<string, int> attributes = new Dictionary<string, int>(inherited);
                // Enchantments carry their own Attributes at the definition
                // level; merge them so Fiery/Mossy abilities are checked with
                // the same values the native mapper sees when enchanted.
                JsonValue declared = value["Attributes"];
                if (declared.Kind == JsonKind.Object)
                    foreach (KeyValuePair<string, JsonValue> pair in declared.AsObject())
                        if (pair.Value.Kind == JsonKind.Number)
                            attributes[pair.Key] = pair.Value.AsInt();

                string type = value["$type"].AsString();
                if (type != null && G10ActionTypes.Contains(type))
                {
                    ActionCase action = new ActionCase();
                    action.Action = value;
                    action.Attributes = attributes;
                    actions.Add(action);
                }
                foreach (JsonValue child in value.AsObject().Values)
                    CollectActions(child, attributes, actions);
            }
            else if (value.Kind == JsonKind.Array)
            {
                foreach (JsonValue child in value.AsArray())
                    CollectActions(child, inherited, actions);
            }
        }

        private static void AssertAttribution(BattleResult result, Side targetSide, double maxHp)
        {
            // Card series must add up to the same realized output/healing as
            // the side ledger: no phantom crit/DoT tick and no lost share.
            BattleDamageSnapshot target = Aggregator.BuildDamage(result, targetSide, maxHp)
                .At(double.PositiveInfinity);
            // Output belongs to the opposing-side source; healing belongs to
            // the target-side source. A combined map is built once, then each
            // channel is summed over the items that can own it.
            List<string> itemIds = new List<string>();
            List<string> damageIds = new List<string>();
            List<string> healIds = new List<string>();
            foreach (BattleItemView item in result.PlayerItems) itemIds.Add(item.Id);
            foreach (BattleItemView item in result.EnemyItems) itemIds.Add(item.Id);
            foreach (BattleItemView item in targetSide == Side.Enemy ? result.PlayerItems : result.EnemyItems)
                damageIds.Add(item.Id);
            foreach (BattleItemView item in targetSide == Side.Enemy ? result.EnemyItems : result.PlayerItems)
                healIds.Add(item.Id);
            Dictionary<string, CardStatsSeries> stats = Aggregator.Build(result, itemIds, 0.05);
            double damage = 0, heal = 0;
            foreach (string id in damageIds)
            {
                CardStatsSeries series;
                if (stats.TryGetValue(id, out series)) damage += series.At(8).Damage;
            }
            foreach (string id in healIds)
            {
                CardStatsSeries series;
                if (stats.TryGetValue(id, out series)) heal += series.At(8).Heal;
            }
            Assert.Near(target.OutputDamage, damage);
            Assert.Near(target.Healing, heal);
        }

        private static void AssertLedger(BattleResult result, Side side, double maxHp)
        {
            BattleDamageSnapshot final = Aggregator.BuildDamage(result, side, maxHp).At(double.PositiveInfinity);
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

        private static List<IndexRow> ReadIndex(string path)
        {
            List<IndexRow> result = new List<IndexRow>();
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int index = 1; index < lines.Length; index++)
            {
                if (lines[index].Trim().Length == 0) continue;
                List<string> fields = SplitCsv(lines[index]);
                if (fields.Count < 5) continue;
                IndexRow row = new IndexRow();
                row.TemplateId = fields[1];
                row.Name = fields[2];
                row.PrimaryGroup = fields[4];
                result.Add(row);
            }
            return result;
        }

        private static List<string> SplitCsv(string line)
        {
            List<string> fields = new List<string>();
            StringBuilder current = new StringBuilder();
            bool quoted = false;
            for (int index = 0; index < line.Length; index++)
            {
                char value = line[index];
                if (quoted)
                {
                    if (value == '"')
                    {
                        if (index + 1 < line.Length && line[index + 1] == '"')
                        {
                            current.Append('"');
                            index++;
                        }
                        else quoted = false;
                    }
                    else current.Append(value);
                }
                else if (value == '"') quoted = true;
                else if (value == ',')
                {
                    fields.Add(current.ToString());
                    current.Length = 0;
                }
                else current.Append(value);
            }
            fields.Add(current.ToString());
            return fields;
        }
    }
}
