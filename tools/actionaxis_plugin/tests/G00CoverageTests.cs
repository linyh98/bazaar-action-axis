using System;
using System.Collections.Generic;
using System.IO;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // G00 矩阵盲区回归：品质来源、稀疏技能品质、双方/混合棋盘、实时属性场景
    // 与附魔告警都必须出现在明细里，不能只凭“映射通过”计数。
    public static class G00CoverageTests
    {
        private const string FixturePath = "tools/actionaxis_plugin/tests/fixtures/native_g00_templates.json";
        private const string Scythe = "0c710f33-d0fd-40c8-aa2d-34fc20f23140";
        private const string Counterstrike = "c5bff6ad-5d13-4cf3-9941-98f3cf913949";
        private const string Header = "kind\ttable\tid\tname\ttier\ttier_source\tenchantment\tside\tscenario\tattributes_live\tplayer_hero\tenemy_hero\tstatus\tenchantment_status\tlive_attribute_status\tenchantment_warning\tlive_attribute_warning\treason";

        public static void Run()
        {
            JsonValue fixtures = Json.Parse(File.ReadAllText(FixturePath));
            List<JsonValue> rows = InputRows(fixtures);
            string output = Path.Combine(Path.GetTempPath(), "g00_coverage_" + Guid.NewGuid().ToString("N") + ".tsv");
            string summary;
            TextWriter original = Console.Out;
            StringWriter captured = new StringWriter();
            Console.SetOut(captured);
            try { Assert.Equal(0, NativeCoverageRunner.Run(rows, output)); }
            finally { Console.SetOut(original); }
            summary = captured.ToString();
            try
            {
                List<Dictionary<string, string>> parsed = ReadRows(output);
                MatrixHeaderAndRowShape(output, parsed, summary);
                ItemsCoverBothTiersSidesAndScenarios(parsed);
                EnchantmentWarningsStayVisible(parsed);
                SparseSkillTiersAndLegendaryAlias(parsed);
            }
            finally { File.Delete(output); }
        }

        private static List<JsonValue> InputRows(JsonValue fixtures)
        {
            JsonValue scythe = fixtures["cards:Scythe"]["template"];
            Dictionary<string, JsonValue> enchantments = scythe["Enchantments"].AsObject();
            JsonValue toxic = enchantments["Toxic"];
            // 仅保留一个附魔并注入一条永不支持的前置条件，确保矩阵必须保留告警。
            enchantments.Clear();
            enchantments["Toxic"] = toxic;
            toxic["Abilities"]["e1"].AsObject()["Prerequisites"] =
                Json.Parse("{\"$type\":\"TPrerequisiteUnsupportedG00Probe\"}");
            List<JsonValue> rows = new List<JsonValue>();
            rows.Add(InputRow("cards", Scythe, scythe));
            rows.Add(InputRow("cards", Counterstrike, fixtures["cards:Counterstrike"]["template"]));
            return rows;
        }

        private static JsonValue InputRow(string table, string id, JsonValue template)
        {
            Dictionary<string, JsonValue> row = new Dictionary<string, JsonValue>();
            row["table"] = JsonValue.FromString(table);
            row["id"] = JsonValue.FromString(id);
            row["template"] = template;
            return JsonValue.FromObject(row);
        }

        private static List<Dictionary<string, string>> ReadRows(string path)
        {
            string[] lines = File.ReadAllLines(path);
            Assert.Equal(Header, lines[0]);
            string[] header = lines[0].Split('\t');
            List<Dictionary<string, string>> result = new List<Dictionary<string, string>>();
            for (int i = 1; i < lines.Length; i++)
            {
                string[] cells = lines[i].Split('\t');
                Assert.Equal(header.Length, cells.Length);
                Dictionary<string, string> row = new Dictionary<string, string>();
                for (int column = 0; column < header.Length; column++) row[header[column]] = cells[column];
                result.Add(row);
            }
            return result;
        }

        private static void MatrixHeaderAndRowShape(string output, List<Dictionary<string, string>> rows, string summary)
        {
            // 物品 2 品质 × 2 附魔 × 2 方 × 3 场景；技能 5 品质 × 2 方 × 3 场景。
            Assert.Equal(54, rows.Count);
            Assert.True(File.Exists(output));
            Assert.True(summary.Contains("scenario/template=18"));
            Assert.True(summary.Contains("scenario/live=18"));
            Assert.True(summary.Contains("scenario/mixed=18"));
            Assert.True(summary.Contains("tier_source/declared=24"));
            Assert.True(summary.Contains("tier_source/legendary-alias=18"));
            Assert.True(summary.Contains("tier_source/inactive-sparse=12"));
            int warned = 0;
            foreach (Dictionary<string, string> row in rows)
                if (row["enchantment_warning"].Length > 0) warned++;
            Assert.True(warned > 0);
            Assert.True(summary.Contains("附魔告警行=" + warned));
            HashSet<string> keys = new HashSet<string>();
            foreach (Dictionary<string, string> row in rows)
            {
                string key = row["kind"] + "|" + row["id"] + "|" + row["tier"] + "|" + row["tier_source"] + "|" +
                    row["enchantment"] + "|" + row["side"] + "|" + row["scenario"];
                Assert.True(keys.Add(key));
            }
        }

        private static void ItemsCoverBothTiersSidesAndScenarios(List<Dictionary<string, string>> rows)
        {
            List<Dictionary<string, string>> items = Select(rows, "item", Scythe);
            Assert.Equal(24, items.Count);
            Dictionary<string, int> tiers = new Dictionary<string, int>(), sides = new Dictionary<string, int>(),
                scenarios = new Dictionary<string, int>(), enchants = new Dictionary<string, int>();
            foreach (Dictionary<string, string> row in items)
            {
                Increment(tiers, row["tier"]);
                Increment(sides, row["side"]);
                Increment(scenarios, row["scenario"]);
                Increment(enchants, row["enchantment"]);
                Assert.Equal(row["tier"] == "Legendary" ? "legendary-alias" : "declared", row["tier_source"]);
                Assert.Equal(row["scenario"] == "template" ? "false" : "true", row["attributes_live"]);
                if (row["scenario"] == "template")
                {
                    Assert.Equal("Dooley", row["player_hero"]);
                    Assert.Equal("Dooley", row["enemy_hero"]);
                }
                else if (row["scenario"] == "live")
                {
                    Assert.Equal("Dooley", row["player_hero"]);
                    Assert.Equal("Dooley", row["enemy_hero"]);
                }
                else
                {
                    Assert.Equal("Mak", row["player_hero"]);
                    Assert.Equal("Dooley", row["enemy_hero"]);
                }
                if (row["scenario"] == "template") Assert.Equal("template", row["live_attribute_status"]);
                else Assert.True(row["live_attribute_status"] != "template");
            }
            Assert.Equal(12, tiers["Diamond"]);
            Assert.Equal(12, tiers["Legendary"]);
            Assert.Equal(12, sides["Player"]);
            Assert.Equal(12, sides["Enemy"]);
            Assert.Equal(8, scenarios["template"]);
            Assert.Equal(8, scenarios["live"]);
            Assert.Equal(8, scenarios["mixed"]);
            Assert.Equal(12, enchants["None"]);
            Assert.Equal(12, enchants["Toxic"]);
        }

        private static void EnchantmentWarningsStayVisible(List<Dictionary<string, string>> rows)
        {
            List<Dictionary<string, string>> items = Select(rows, "item", Scythe);
            int toxic = 0, none = 0;
            foreach (Dictionary<string, string> row in items)
            {
                if (row["enchantment"] == "None")
                {
                    none++;
                    Assert.Equal("none", row["enchantment_status"]);
                    Assert.Equal("", row["enchantment_warning"]);
                    continue;
                }
                toxic++;
                Assert.Equal("Toxic", row["enchantment"]);
                Assert.Equal("warning", row["enchantment_status"]);
                Assert.True(row["enchantment_warning"].Contains("附魔"));
                Assert.Equal("unsupported", row["status"]);
            }
            Assert.Equal(12, toxic);
            Assert.Equal(12, none);
        }

        private static void SparseSkillTiersAndLegendaryAlias(List<Dictionary<string, string>> rows)
        {
            List<Dictionary<string, string>> skills = Select(rows, "skill", Counterstrike);
            Assert.Equal(30, skills.Count);
            Dictionary<string, int> tiers = new Dictionary<string, int>();
            Dictionary<string, string> statuses = new Dictionary<string, string>();
            foreach (Dictionary<string, string> row in skills)
            {
                Increment(tiers, row["tier"]);
                statuses[row["tier"] + "/" + row["side"] + "/" + row["scenario"]] = row["status"];
                Assert.Equal(row["tier"] == "Legendary" ? "legendary-alias" :
                    (row["tier"] == "Bronze" || row["tier"] == "Silver") ? "inactive-sparse" : "declared",
                    row["tier_source"]);
                Assert.Equal(row["scenario"] == "template" ? "false" : "true", row["attributes_live"]);
                Assert.Equal("None", row["enchantment"]);
                Assert.Equal("none", row["enchantment_status"]);
                Assert.Equal("", row["enchantment_warning"]);
                if (row["tier"] == "Bronze" || row["tier"] == "Silver") Assert.Equal("inactive", row["status"]);
                else Assert.True(row["status"] != "inactive" && row["status"] != "missing-template");
            }
            foreach (string tier in new[] { "Bronze", "Silver", "Gold", "Diamond", "Legendary" })
                Assert.Equal(6, tiers[tier]);
            foreach (string side in new[] { "Player", "Enemy" })
                foreach (string scenario in new[] { "template", "live", "mixed" })
                {
                    string diamond = statuses["Diamond/" + side + "/" + scenario];
                    Assert.Equal(diamond, statuses["Legendary/" + side + "/" + scenario]);
                    Assert.True(statuses["Gold/" + side + "/" + scenario] != "inactive");
                }
        }

        private static List<Dictionary<string, string>> Select(List<Dictionary<string, string>> rows, string kind, string id)
        {
            List<Dictionary<string, string>> result = new List<Dictionary<string, string>>();
            foreach (Dictionary<string, string> row in rows)
                if (row["kind"] == kind && row["id"] == id) result.Add(row);
            return result;
        }

        private static void Increment(Dictionary<string, int> values, string key)
        {
            int count;
            values.TryGetValue(key, out count);
            values[key] = count + 1;
        }
    }
}
