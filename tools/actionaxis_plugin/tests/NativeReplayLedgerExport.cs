using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // G12: export the same normalized ledger shape as tools/native_replay_ledger.py
    // from the opening fixture. This is deliberately a test harness, not a
    // deployable plug-in entry point: it never writes to the game directory.
    public static class NativeReplayLedgerExport
    {
        private const double FrameSeconds = 0.05;

        public static int Run(string fixturePath, string templatesPath, string catalogPath, string outputPath)
        {
            try
            {
                JsonValue fixture = Json.Parse(File.ReadAllText(fixturePath));
                Dictionary<string, JsonValue> templates = LoadTemplates(templatesPath);
                CardCatalog catalog = CardCatalog.Load(catalogPath);
                Dictionary<string, object> ledger = Build(fixture, templates, catalog, fixturePath, templatesPath, catalogPath);
                File.WriteAllText(outputPath, JsonText.Write(ledger) + "\n", new UTF8Encoding(false));
                Console.WriteLine("simulated ledger -> " + outputPath);
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("ledger export failed: " + error.Message);
                return 2;
            }
        }

        public static int RunBatch(string fixtureDirectory, string templatesPath, string catalogPath, string outputDirectory)
        {
            try
            {
                Directory.CreateDirectory(outputDirectory);
                Dictionary<string, JsonValue> templates = LoadTemplates(templatesPath);
                CardCatalog catalog = CardCatalog.Load(catalogPath);
                string templateSha = Sha256(templatesPath);
                string catalogSha = Sha256(catalogPath);
                string[] files = Directory.GetFiles(fixtureDirectory, "*.json");
                Array.Sort(files, StringComparer.Ordinal);
                int failed = 0;
                int index = 0;
                foreach (string fixturePath in files)
                {
                    index++;
                    string outputPath = Path.Combine(outputDirectory,
                        Path.GetFileNameWithoutExtension(fixturePath) + ".sim.json");
                    try
                    {
                        JsonValue fixture = Json.Parse(File.ReadAllText(fixturePath));
                        Dictionary<string, object> ledger = Build(fixture, templates, catalog,
                            fixturePath, templatesPath, catalogPath, templateSha, catalogSha);
                        File.WriteAllText(outputPath, JsonText.Write(ledger) + "\n", new UTF8Encoding(false));
                    }
                    catch (Exception error)
                    {
                        failed++;
                        Dictionary<string, object> failure = new Dictionary<string, object>();
                        failure["ledgerVersion"] = 1;
                        failure["kind"] = "simulated";
                        failure["complete"] = false;
                        failure["source"] = new Dictionary<string, object>
                        {
                            { "fixturePath", fixturePath },
                            { "error", error.Message }
                        };
                        File.WriteAllText(outputPath, JsonText.Write(failure) + "\n", new UTF8Encoding(false));
                        Console.Error.WriteLine("ledger export failed for " + fixturePath + ": " + error.Message);
                    }
                    if (index % 25 == 0) Console.WriteLine("exported " + index + "/" + files.Length);
                }
                Console.WriteLine("batch exported " + (files.Length - failed) + "/" + files.Length +
                    " -> " + outputDirectory);
                return failed == 0 ? 0 : 2;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("batch ledger export failed: " + error.Message);
                return 2;
            }
        }

        private static Dictionary<string, object> Build(JsonValue fixture, Dictionary<string, JsonValue> templates,
            CardCatalog catalog, string fixturePath, string templatesPath, string catalogPath,
            string templateSha = null, string catalogSha = null)
        {
            List<LiveCardEntry> playerItems = ReadItems(fixture, "player", templates, true);
            List<LiveCardEntry> enemyItems = ReadItems(fixture, "enemy", templates, true);
            double? playerStartHp = NullableNumber(fixture["player"]["startHp"]);
            double? enemyStartHp = NullableNumber(fixture["enemy"]["startHp"]);
            double? playerMaxHp = NullableNumber(fixture["player"]["maxHp"]);
            double? enemyMaxHp = NullableNumber(fixture["enemy"]["maxHp"]);
            double? playerShield = NullableNumber(fixture["player"]["startShield"]);
            double? enemyShield = NullableNumber(fixture["enemy"]["startShield"]);
            double? playerRegen = NullableNumber(fixture["player"]["startRegen"]);
            double? enemyRegen = NullableNumber(fixture["enemy"]["startRegen"]);
            double? playerRage = NullableNumber(fixture["player"]["rageMax"]);
            double? enemyRage = NullableNumber(fixture["enemy"]["rageMax"]);
            double? playerEnrage = NullableNumber(fixture["player"]["enragedDurationMax"]);
            double? enemyEnrage = NullableNumber(fixture["enemy"]["enragedDurationMax"]);
            double maxTime = NullableNumber(fixture["maxTime"]) ?? 30.0;

            List<string> assumptions = new List<string>();
            double playerMax = ResolveMax("player", playerMaxHp, playerStartHp, assumptions);
            double enemyMax = ResolveMax("enemy", enemyMaxHp, enemyStartHp, assumptions);

            MappedBoard playerMap = CardMapper.Build(playerItems, catalog, playerMax, enemyMax, maxTime, true);
            MappedBoard enemyMap = CardMapper.Build(enemyItems, catalog, playerMax, enemyMax, maxTime, true);
            BattleConfig config = playerMap.Config;
            config.Player.Items = playerMap.Config.Player.Items;
            config.Enemy.Items = enemyMap.Config.Player.Items;
            foreach (KeyValuePair<string, CardDef> pair in enemyMap.Config.Cards) config.Cards[pair.Key] = pair.Value;
            config.PlayerMaxHp = playerMax;
            config.EnemyMaxHp = enemyMax;
            config.PlayerStartHp = playerStartHp;
            config.EnemyStartHp = enemyStartHp;
            config.PlayerStartShield = playerShield ?? 0;
            config.EnemyStartShield = enemyShield ?? 0;
            config.PlayerStartRegen = playerRegen ?? 0;
            config.EnemyStartRegen = enemyRegen ?? 0;
            config.PlayerRageMax = playerRage ?? 100;
            config.EnemyRageMax = enemyRage ?? 100;
            config.PlayerEnragedDurationMs = playerEnrage ?? 5000;
            config.EnemyEnragedDurationMs = enemyEnrage ?? 5000;
            config.PlayerHero = fixture["player"]["hero"].AsString();
            config.EnemyHero = fixture["enemy"]["hero"].AsString();
            foreach (string side in new[] { "player", "enemy" })
            {
                JsonValue attributes = fixture[side]["attributes"];
                Dictionary<string, int> captured = new Dictionary<string, int>();
                if (attributes.Kind == JsonKind.Object)
                    foreach (KeyValuePair<string, JsonValue> pair in attributes.AsObject())
                        if (pair.Value.Kind == JsonKind.Number) captured[pair.Key] = (int)Math.Round(pair.Value.AsDouble());
                MonsterOpponentMapper.ApplyHeroAttributes(config, captured, side == "player" ? Side.Player : Side.Enemy);
            }
            config.PlayerHeroAttributesAreLive = fixture["player"]["attributesAreLive"].AsBool();
            config.EnemyHeroAttributesAreLive = fixture["enemy"]["attributesAreLive"].AsBool();
            config.NativeMechanics = true;
            config.ReplayRandom = ReplayRandomTape.FromJson(fixture["replayRandom"]);
            config.Seed = 1;
            config.MaxTime = maxTime;
            if (playerMaxHp == null) assumptions.Add("player.maxHp 使用开局 Health；原生 SpawnMessage 没有 HealthMax");
            if (playerShield == null) assumptions.Add("player.startShield 使用 0；原生载荷没有该字段");
            if (playerRegen == null) assumptions.Add("player.startRegen 使用 0；原生载荷没有该字段");
            if (playerRage == null) assumptions.Add("player.rageMax 使用引擎默认 100；原生载荷没有该字段");
            if (playerEnrage == null) assumptions.Add("player.enragedDurationMax 使用引擎默认 5000ms；原生载荷没有该字段");
            if (config.ReplayRandom == null) assumptions.Add("随机种子固定为 1；回放载荷没有可重建的种子");

            List<LiveSkillEntry> playerSkills = ReadSkills(fixture, "player", templates);
            List<LiveSkillEntry> enemySkills = ReadSkills(fixture, "enemy", templates);
            List<string> skillWarnings = new List<string>();
            if (playerSkills.Count > 0)
                skillWarnings.AddRange(SkillMapper.Apply(config, playerSkills, null, Side.Player).Warnings);
            if (enemySkills.Count > 0)
                skillWarnings.AddRange(SkillMapper.Apply(config, enemySkills, null, Side.Enemy).Warnings);

            List<string> warnings = new List<string>();
            warnings.AddRange(playerMap.EnchantmentWarnings);
            warnings.AddRange(enemyMap.EnchantmentWarnings);
            warnings.AddRange(playerMap.LiveAttributeWarnings);
            warnings.AddRange(enemyMap.LiveAttributeWarnings);
            warnings.AddRange(skillWarnings);
            warnings.AddRange(NativeCardAbilityMapper.Apply(config));
            warnings.AddRange(NativeItemMapper.Apply(config));
            AppendUnmappedCatalogWarnings(warnings, playerMap, config, config.Player);
            AppendUnmappedCatalogWarnings(warnings, enemyMap, config, config.Enemy);

            BattleResult result = BattleEngine.Simulate(config);
            warnings.AddRange(result.Warnings);
            bool complete = true;
            foreach (string warning in result.Warnings)
                if (warning.StartsWith("simulation failed", StringComparison.Ordinal)) complete = false;

            Dictionary<string, object> ledger = new Dictionary<string, object>();
            ledger["ledgerVersion"] = 1;
            ledger["kind"] = "simulated";
            ledger["generator"] = "tools/actionaxis_plugin/tests/NativeReplayLedgerExport.cs";
            ledger["complete"] = complete;
            ledger["source"] = new Dictionary<string, object>
            {
                { "fixturePath", fixturePath },
                { "fixtureSha256", Sha256(fixturePath) },
                { "templatePath", templatesPath },
                { "templateSha256", templateSha ?? Sha256(templatesPath) },
                { "catalogPath", catalogPath },
                { "catalogSha256", catalogSha ?? Sha256(catalogPath) },
                { "seed", 1 },
                { "assumptions", assumptions }
            };
            ledger["opening"] = new Dictionary<string, object>
            {
                { "player", OpeningSide("player", fixture, config) },
                { "enemy", OpeningSide("enemy", fixture, config) }
            };
            ledger["identity"] = Identity(result, config);
            ledger["combat"] = Combat(result, config);
            if (result.ReplayRandom != null)
            {
                ReplayRandomAudit audit = result.ReplayRandom;
                ledger["replayRandom"] = new Dictionary<string, object> {
                    { "mode", "observed-decisions" }, { "replaySha256", config.ReplayRandom.ReplaySha256 },
                    { "targetRecords", audit.TargetRecords }, { "targetApplied", audit.TargetApplied },
                    { "criticalRecords", audit.CriticalRecords }, { "criticalApplied", audit.CriticalApplied },
                    { "targetFallbacks", audit.TargetFallbacks }, { "criticalFallbacks", audit.CriticalFallbacks },
                    { "orderRecords", audit.OrderRecords }, { "orderApplied", audit.OrderApplied },
                    { "orderFallbacks", audit.OrderFallbacks },
                    { "enchantRecords", audit.EnchantRecords }, { "enchantApplied", audit.EnchantApplied },
                    { "enchantFallbacks", audit.EnchantFallbacks },
                    { "transformRecords", audit.TransformRecords }, { "transformApplied", audit.TransformApplied },
                    { "transformFallbacks", audit.TransformFallbacks },
                    { "identityRecords", audit.IdentityRecords }, { "identityApplied", audit.IdentityApplied },
                    { "identityFallbacks", audit.IdentityFallbacks },
                    { "invalidChoices", audit.InvalidChoices }, { "issues", audit.Issues }
                };
            }
            ledger["warnings"] = new List<object>(ToObjects(warnings));
            ledger["unknowns"] = new List<object>(ToObjects(assumptions));
            return ledger;
        }

        private static void AppendUnmappedCatalogWarnings(List<string> warnings, MappedBoard mapped,
            BattleConfig config, Loadout board)
        {
            foreach (LoadoutItem item in board.Items)
            {
                CardDef card = config.Cards[item.CardId];
                if (mapped.Unknown.Contains(card.DisplayName()) &&
                    (card.DataSource != "native-template" || card.ParseStatus != "structured"))
                    warnings.Add(card.DisplayName());
            }
        }

        private static double ResolveMax(string side, double? maxHp, double? startHp, IList<string> assumptions)
        {
            if (maxHp.HasValue && maxHp.Value > 0) return maxHp.Value;
            if (startHp.HasValue && startHp.Value > 0) return startHp.Value;
            assumptions.Add(side + ".maxHp 缺失，使用 100");
            return 100;
        }

        private static List<LiveCardEntry> ReadItems(JsonValue fixture, string side, Dictionary<string, JsonValue> templates,
            bool attributesAreLive)
        {
            List<LiveCardEntry> entries = new List<LiveCardEntry>();
            foreach (JsonValue row in fixture["items"].AsArray())
            {
                if (row["side"].AsString() != side) continue;
                string templateId = row["templateId"].AsString();
                JsonValue template;
                if (string.IsNullOrEmpty(templateId) || !templates.TryGetValue(templateId, out template))
                    throw new FormatException("缺少模板 " + templateId);
                LiveCardEntry entry = new LiveCardEntry();
                entry.InstanceId = row["instanceId"].AsString();
                entry.UseInstanceIdAsId = true;
                entry.TemplateId = templateId;
                entry.Tier = Text(row, "tier", "Bronze");
                entry.Size = Text(row, "size", null);
                entry.Socket = (int)Math.Round(Number(row, "slot", 0));
                entry.Enchantment = Text(row, "enchantment", null);
                JsonValue questIds = row.Has("questAbilities") ? row["questAbilities"] : JsonValue.Null;
                if (questIds.Kind == JsonKind.Array && questIds.Count > 0)
                    template = ApplyQuestRewards(template, entry.Tier, questIds);
                entry.Template = template;
                entry.AttributesAreLive = attributesAreLive;
                JsonValue enchantments = template["Enchantments"];
                if (!string.IsNullOrEmpty(entry.Enchantment) && enchantments.Kind == JsonKind.Object &&
                    enchantments.Has(entry.Enchantment))
                    entry.EnchantmentDefinition = enchantments[entry.Enchantment];
                foreach (KeyValuePair<string, JsonValue> pair in row["attributes"].AsObject())
                    if (pair.Value.Kind == JsonKind.Number) entry.Attributes[pair.Key] = (int)Math.Round(pair.Value.AsDouble());
                entries.Add(entry);
            }
            return entries;
        }

        // 任务奖励能力（如腐朽圣像“施毒时+Custom_0 毒量”的 q1）在购物阶段完成后
        // 随卡进战斗：定义藏在模板 Quests 下而不是 Abilities 字典里。按实例克隆
        // 合并，避免把奖励泄漏到同模板的其他实例。
        private static JsonValue ApplyQuestRewards(JsonValue template, string tier, JsonValue questIds)
        {
            string lookupTier = tier == "Legendary" ? "Diamond" : tier;
            JsonValue tiers = template["Tiers"];
            if (tiers.Kind != JsonKind.Object || !tiers.Has(lookupTier))
                throw new FormatException("任务奖励缺少品质定义 " + lookupTier);
            Dictionary<string, JsonValue> root = new Dictionary<string, JsonValue>(template.AsObject());
            Dictionary<string, JsonValue> tiersClone = new Dictionary<string, JsonValue>(tiers.AsObject());
            Dictionary<string, JsonValue> tierClone = new Dictionary<string, JsonValue>(tiersClone[lookupTier].AsObject());
            List<JsonValue> ids = tierClone.ContainsKey("AbilityIds") && tierClone["AbilityIds"].Kind == JsonKind.Array
                ? new List<JsonValue>(tierClone["AbilityIds"].AsArray())
                : new List<JsonValue>();
            Dictionary<string, JsonValue> abilities = root.ContainsKey("Abilities") && root["Abilities"].Kind == JsonKind.Object
                ? new Dictionary<string, JsonValue>(root["Abilities"].AsObject())
                : new Dictionary<string, JsonValue>();
            foreach (JsonValue questId in questIds.AsArray())
            {
                string id = questId.AsString();
                JsonValue definition = FindQuestAbility(template, id);
                if (definition == null)
                    throw new FormatException("缺少任务奖励能力定义 " + id);
                if (!abilities.ContainsKey(id)) abilities[id] = definition;
                bool present = false;
                foreach (JsonValue existing in ids) if (existing.AsString() == id) { present = true; break; }
                if (!present) ids.Add(JsonValue.FromString(id));
            }
            tierClone["AbilityIds"] = JsonValue.FromArray(ids);
            tiersClone[lookupTier] = JsonValue.FromObject(tierClone);
            root["Tiers"] = JsonValue.FromObject(tiersClone);
            root["Abilities"] = JsonValue.FromObject(abilities);
            return JsonValue.FromObject(root);
        }

        private static JsonValue FindQuestAbility(JsonValue template, string id)
        {
            JsonValue quests = template.Has("Quests") ? template["Quests"] : JsonValue.Null;
            if (quests.Kind != JsonKind.Array) return null;
            foreach (JsonValue quest in quests.AsArray())
            {
                JsonValue entries = quest.Has("Entries") ? quest["Entries"] : JsonValue.Null;
                if (entries.Kind != JsonKind.Array) continue;
                foreach (JsonValue entry in entries.AsArray())
                {
                    JsonValue reward = entry.Has("Reward") ? entry["Reward"] : JsonValue.Null;
                    JsonValue abilities = reward.Kind == JsonKind.Object && reward.Has("Abilities")
                        ? reward["Abilities"] : JsonValue.Null;
                    if (abilities.Kind == JsonKind.Object && abilities.Has(id)) return abilities[id];
                }
            }
            return null;
        }

        // BPP 快照偶尔把“起始 Gold”的技能记成 Silver（0276c46b 的
        // Bloodhound / Sharp Corners）；原生战斗仍执行该技能的能力。
        // 模板缺该品质行时按起始品质读取，避免整条技能被判为无效果。
        private static string NormalizedSkillTier(string tier, JsonValue template)
        {
            JsonValue tiers = template["Tiers"];
            if (tiers.Kind != JsonKind.Object || string.IsNullOrEmpty(tier)) return tier;
            string normalized = tier == "Legendary" ? "Diamond" : tier;
            if (tiers.Has(normalized)) return tier;
            string starting = NativeSkillSupport.Text(template, "StartingTier");
            if (string.IsNullOrEmpty(starting) || !tiers.Has(starting)) return tier;
            return TierRank(normalized) < TierRank(starting) ? starting : tier;
        }

        private static int TierRank(string tier)
        {
            string[] order = { "Bronze", "Silver", "Gold", "Diamond", "Legendary" };
            int index = Array.IndexOf(order, tier);
            return index < 0 ? -1 : index;
        }

        private static List<LiveSkillEntry> ReadSkills(JsonValue fixture, string side, Dictionary<string, JsonValue> templates)
        {
            List<LiveSkillEntry> entries = new List<LiveSkillEntry>();
            foreach (JsonValue row in fixture["skills"].AsArray())
            {
                if (row["side"].AsString() != side) continue;
                string templateId = row["templateId"].AsString();
                JsonValue template;
                if (string.IsNullOrEmpty(templateId) || !templates.TryGetValue(templateId, out template))
                    throw new FormatException("缺少技能模板 " + templateId);
                if (row["template"].Kind == JsonKind.Object)
                {
                    JsonValue archived = row["template"];
                    string version = fixture["gameDataVersion"].AsString();
                    if (string.IsNullOrEmpty(version) || archived["Id"].AsString() != templateId ||
                        archived["Version"].AsString() != version ||
                        row["templateSource"]["version"].AsString() != version)
                        throw new FormatException("历史技能模板身份或版本不匹配 " + templateId);
                    template = archived;
                }
                LiveSkillEntry entry = new LiveSkillEntry();
                entry.InstanceId = row["instanceId"].AsString();
                entry.TemplateId = templateId;
                entry.Tier = NormalizedSkillTier(Text(row, "tier", "Bronze"), template);
                entry.Template = template;
                entry.AttributesAreLive = true;
                foreach (KeyValuePair<string, JsonValue> pair in row["attributes"].AsObject())
                    if (pair.Value.Kind == JsonKind.Number) entry.Attributes[pair.Key] = (int)Math.Round(pair.Value.AsDouble());
                entries.Add(entry);
            }
            return entries;
        }

        private static Dictionary<string, object> OpeningSide(string side, JsonValue fixture, BattleConfig config)
        {
            JsonValue source = fixture[side];
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["side"] = side;
            result["hero"] = source["hero"].AsString();
            result["health"] = NullableNumber(source["startHp"]);
            result["healthMax"] = NullableNumber(source["maxHp"]);
            result["shield"] = NullableNumber(source["startShield"]);
            result["healthRegen"] = NullableNumber(source["startRegen"]);
            result["rageMax"] = NullableNumber(source["rageMax"]);
            result["enragedDurationMax"] = NullableNumber(source["enragedDurationMax"]);
            result["itemCount"] = side == "player" ? config.Player.Items.Count : config.Enemy.Items.Count;
            return result;
        }

        private static Dictionary<string, object> Identity(BattleResult result, BattleConfig config)
        {
            Dictionary<string, object> identity = new Dictionary<string, object>();
            Dictionary<string, object> instances = new Dictionary<string, object>();
            AddInstances(instances, result.PlayerItems, "player");
            AddInstances(instances, result.EnemyItems, "enemy");
            AddSkills(instances, config.PlayerSkills, "player");
            AddSkills(instances, config.EnemySkills, "enemy");
            identity["keyFormat"] = "side + ':' + instanceId";
            identity["instances"] = instances;
            List<object> lineage = new List<object>();
            foreach (BattleEvent ev in result.Events)
            {
                if (ev.Kind != "transform" || string.IsNullOrEmpty(ev.TargetItemId)) continue;
                Dictionary<string, object> row = new Dictionary<string, object>();
                row["kind"] = "transform";
                row["time"] = Round(ev.Time);
                row["frame"] = Frame(ev.Time);
                row["originalInstanceId"] = ev.SourceItemId;
                List<object> children = new List<object>();
                Dictionary<string, object> child = new Dictionary<string, object>();
                child["instanceId"] = ev.TargetItemId;
                child["templateId"] = TemplateAt(result, ev.TargetSide, ev.TargetItemId, ev.Time);
                child["time"] = Round(ev.Time);
                children.Add(child);
                row["children"] = children;
                lineage.Add(row);
            }
            foreach (BattleEvent ev in result.Events)
            {
                if (ev.Kind != "transform" || ev.TargetItemId == ev.SourceItemId) continue;
                object raw;
                string key = (ev.TargetSide == Side.Enemy ? "enemy:" : "player:") + ev.TargetItemId;
                if (!instances.TryGetValue(key, out raw)) continue;
                Dictionary<string, object> child = (Dictionary<string, object>)raw;
                child["parent"] = (ev.Side == Side.Enemy ? "enemy:" : "player:") + ev.SourceItemId;
                // Until-end-of-combat replacements revert during the next
                // lifecycle cleanup frame, after the final combat snapshot.
                if (child["removed"] == null) child["removed"] = Round(result.EndTime + FrameSeconds);
            }
            identity["lineage"] = lineage;
            return identity;
        }

        private static void AddInstances(Dictionary<string, object> target, List<BattleItemView> items, string side)
        {
            foreach (BattleItemView history in items)
            {
                Dictionary<string, object> item = new Dictionary<string, object>();
                string key = side + ":" + history.Id;
                item["key"] = key;
                item["side"] = side;
                item["instanceId"] = history.Id;
                item["born"] = Round(history.BornAt);
                item["removed"] = history.RemovedAt.HasValue ? (object)Round(history.RemovedAt.Value) : null;
                item["slot"] = history.Slot;
                item["templateId"] = history.Card == null ? null : TemplateId(history.Card);
                List<object> states = new List<object>();
                foreach (BattleItemState state in history.States)
                {
                    Dictionary<string, object> row = new Dictionary<string, object>();
                    row["time"] = Round(state.Time);
                    row["frame"] = Frame(state.Time);
                    row["templateId"] = state.Card == null ? null : TemplateId(state.Card);
                    row["cardId"] = state.Card == null ? null : state.Card.Id;
                    row["name"] = state.Card == null ? null : state.Card.DisplayName();
                    row["slot"] = state.Slot;
                    states.Add(row);
                }
                item["states"] = states;
                target[key] = item;
            }
        }

        private static void AddSkills(Dictionary<string, object> target, List<CardDef> skills, string side)
        {
            foreach (CardDef skill in skills)
            {
                string raw = skill.Id ?? string.Empty;
                if (raw.StartsWith("skill:", StringComparison.Ordinal))
                    raw = raw.Substring("skill:".Length);
                if (raw.StartsWith("enemy:skill:", StringComparison.Ordinal))
                    raw = raw.Substring("enemy:skill:".Length);
                else if (raw.StartsWith("player:skill:", StringComparison.Ordinal))
                    raw = raw.Substring("player:skill:".Length);
                string key = side + ":" + raw;
                if (target.ContainsKey(key)) continue;
                Dictionary<string, object> item = new Dictionary<string, object>();
                item["key"] = key;
                item["side"] = side;
                item["instanceId"] = raw;
                item["born"] = 0.0;
                item["removed"] = null;
                item["slot"] = null;
                item["templateId"] = raw;
                List<object> states = new List<object>();
                Dictionary<string, object> row = new Dictionary<string, object>();
                row["time"] = 0.0;
                row["frame"] = 0;
                row["templateId"] = raw;
                row["cardId"] = skill.Id;
                row["name"] = skill.DisplayName();
                row["slot"] = null;
                states.Add(row);
                item["states"] = states;
                target[key] = item;
            }
        }

        private static string TemplateAt(BattleResult result, Side? side, string instanceId, double time)
        {
            List<BattleItemView> items = side == Side.Enemy ? result.EnemyItems : result.PlayerItems;
            foreach (BattleItemView history in items)
                if (history.Id == instanceId)
                {
                    BattleItemState state = history.At(time);
                    return state == null || state.Card == null ? null : TemplateId(state.Card);
                }
            return null;
        }

        private static Dictionary<string, object> Combat(BattleResult result, BattleConfig config)
        {
            Dictionary<string, object> combat = new Dictionary<string, object>();
            combat["frames"] = Frame(result.EndTime);
            combat["duration"] = Round(result.EndTime);
            List<object> events = new List<object>();
            foreach (BattleEvent ev in result.Events)
            {
                Dictionary<string, object> row = Event(ev);
                if (ev.Kind == "transform") row["value"] = TemplateAt(result, ev.TargetSide, ev.TargetItemId, ev.Time);
                events.Add(row);
            }
            combat["events"] = events;
            combat["healthChanges"] = HealthChanges(result);
            combat["cardUpdates"] = CardUpdates(result);
            combat["outcome"] = new Dictionary<string, object>
            {
                { "winnerSide", result.Winner },
                { "endTime", Round(result.EndTime) },
                { "endedByDeath", result.EndedByDeath }
            };
            return combat;
        }

        private static Dictionary<string, object> Event(BattleEvent ev)
        {
            Dictionary<string, object> row = new Dictionary<string, object>();
            row["time"] = Round(ev.Time);
            row["frame"] = Frame(ev.Time);
            row["kind"] = SimKind(ev.Kind);
            string sourceId = ev.SourceItemId;
            object value = ev.Value;
            if (ev.Kind == "enchant" || ev.Kind == "enchantRemove")
            {
                // 原生 CardEnchanted/CardEnchantRemove 只记录被附魔实例本身：
                // source 与 target 同为该实例，value 是附魔名（07f044ee 帧 0 Obsidian）。
                sourceId = ev.TargetItemId ?? ev.SourceItemId;
                value = ev.Note;
            }
            row["source"] = sourceId;
            row["sourceSide"] = ev.Side == Side.Player ? "player" : "enemy";
            row["target"] = ev.TargetItemId;
            row["targetSide"] = ev.TargetSide.HasValue ? (ev.TargetSide.Value == Side.Player ? "player" : "enemy") : null;
            row["value"] = value;
            row["actualDamage"] = ev.ActualDamage;
            row["absorbed"] = ev.Absorbed;
            row["overkill"] = ev.Overkill;
            row["prevented"] = ev.Prevented;
            row["nominalAmount"] = ev.NominalAmount;
            row["overheal"] = ev.Overheal;
            row["hpAfter"] = ev.HpAfter;
            row["shieldAfter"] = ev.ShieldAfter;
            row["note"] = ev.Note;
            row["chainId"] = ev.ChainId;
            row["depth"] = ev.Depth;
            return row;
        }

        private static List<object> HealthChanges(BattleResult result)
        {
            List<object> rows = new List<object>();
            BattleHealthSnapshot previous = null;
            foreach (BattleHealthSnapshot snapshot in result.HealthSnapshots)
            {
                if (previous != null)
                {
                    if (Changed(previous.Player, snapshot.Player) || HasHealPacket(result, Side.Player, snapshot.Time))
                        rows.Add(HealthRow("player", snapshot.Time, snapshot.Player));
                    if (Changed(previous.Enemy, snapshot.Enemy) || HasHealPacket(result, Side.Enemy, snapshot.Time))
                        rows.Add(HealthRow("enemy", snapshot.Time, snapshot.Enemy));
                }
                previous = snapshot;
            }
            return rows;
        }

        private static bool HasHealPacket(BattleResult result, Side side, double time)
        {
            return result.Events.Exists(delegate(BattleEvent ev) {
                return ev.Kind == "heal" && ev.Note == null && ev.NominalAmount.GetValueOrDefault() > 0 &&
                    ev.TargetSide == side && Math.Abs(ev.Time - time) < 1e-9;
            });
        }

        private static Dictionary<string, object> HealthRow(string side, double time, HeroVitals vitals)
        {
            Dictionary<string, object> row = new Dictionary<string, object>();
            row["frame"] = Frame(time);
            row["time"] = Round(time);
            row["side"] = side;
            row["hp"] = Round(vitals.Hp);
            row["hpMax"] = Round(vitals.MaxHp);
            row["shield"] = Round(vitals.Shield);
            row["burn"] = Round(vitals.Burn);
            row["poison"] = Round(vitals.Poison);
            row["regen"] = Round(vitals.Regen);
            row["dead"] = vitals.Hp <= 0;
            return row;
        }

        private static bool Changed(HeroVitals a, HeroVitals b)
        {
            return Math.Abs(a.Hp - b.Hp) > 1e-9 || Math.Abs(a.MaxHp - b.MaxHp) > 1e-9 ||
                Math.Abs(a.Shield - b.Shield) > 1e-9 || Math.Abs(a.Burn - b.Burn) > 1e-9 ||
                Math.Abs(a.Poison - b.Poison) > 1e-9 || Math.Abs(a.Regen - b.Regen) > 1e-9;
        }

        private static List<object> CardUpdates(BattleResult result)
        {
            Dictionary<string, CardDef> openingCards = new Dictionary<string, CardDef>();
            List<BattleItemView> histories = new List<BattleItemView>(result.PlayerItems);
            histories.AddRange(result.EnemyItems);
            foreach (BattleItemView history in histories)
                openingCards[history.Id] = history.States.Count > 0 ? history.States[0].Card : history.Card;
            Dictionary<string, BattleItemView> byId = new Dictionary<string, BattleItemView>();
            foreach (BattleItemView history in histories) byId[history.Id] = history;
            HashSet<string> created = new HashSet<string>();
            foreach (BattleEvent ev in result.Events)
                if (ev.Kind == "transform" && ev.TargetItemId != ev.SourceItemId) created.Add(ev.TargetItemId);
            List<object> rows = new List<object>();
            Dictionary<string, ItemSnapshot> previous = new Dictionary<string, ItemSnapshot>();
            List<ItemSnapshot> snapshots = new List<ItemSnapshot>(result.ItemSnapshots);
            snapshots.Sort(delegate(ItemSnapshot a, ItemSnapshot b)
            {
                int time = a.Time.CompareTo(b.Time);
                return time != 0 ? time : string.CompareOrdinal(a.ItemId, b.ItemId);
            });
            foreach (ItemSnapshot snapshot in snapshots)
            {
                BattleItemView history;
                byId.TryGetValue(snapshot.ItemId, out history);
                if (history != null && history.RemovedAt.HasValue && snapshot.Time > history.RemovedAt.Value + 1e-9) continue;
                ItemSnapshot last;
                if (!previous.TryGetValue(snapshot.ItemId, out last))
                {
                    if (created.Contains(snapshot.ItemId))
                    {
                        // A fresh instance is initialized from zero, not from
                        // the attributes of the item it replaced.
                        foreach (KeyValuePair<string, double> pair in snapshot.Attributes)
                            if (Math.Abs(pair.Value) > 1e-9)
                                AddCardUpdate(rows, snapshot, pair.Key, 0, Math.Round(pair.Value));
                        if (history != null) AppendBirthMetadata(rows, snapshot, history);
                        previous[snapshot.ItemId] = snapshot;
                        continue;
                    }
                    // Native frame 0 reports PreviousValue = opening CooldownMax
                    // and CurrentValue after the first 50 ms tick. Static/passive
                    // items with no cooldown are not present in the native update.
                    if (snapshot.HasCooldown)
                    {
                        double opening = Math.Round(snapshot.CooldownMax * 1000);
                        Dictionary<string, double> captured;
                        double capturedCooldown;
                        if (result.OpeningItemAttributes.TryGetValue(snapshot.ItemId, out captured) &&
                            captured.TryGetValue("Cooldown", out capturedCooldown)) opening = Math.Round(capturedCooldown);
                        AddCardUpdate(rows, snapshot, "Cooldown", opening,
                            Math.Round(snapshot.CooldownRemaining * 1000));
                    }
                    // Statuses applied on the opening tick have no previous
                    // snapshot; report them from zero like the native payload.
                    foreach (string status in new string[] { "Haste", "Slow", "Freeze", "Flying" })
                    {
                        double value;
                        if (snapshot.Attributes.TryGetValue(status, out value) && Math.Abs(value) > 1e-9)
                            AddCardUpdate(rows, snapshot, status, 0, value);
                    }
                    // Items are initialized with full ammo. That opening state
                    // is not a reload from zero; only export an actual change.
                    int openingAmmo = snapshot.AmmoMax.HasValue ? snapshot.AmmoMax.Value : 0;
                    if (snapshot.Ammo != openingAmmo)
                        AddCardUpdate(rows, snapshot, "Ammo", openingAmmo, snapshot.Ammo);
                    Dictionary<string, double> openingValues;
                    if (result.OpeningItemAttributes.TryGetValue(snapshot.ItemId, out openingValues))
                    {
                        // Compare with the derived state before fight-start
                        // actions, including aura changes caused by skill counters.
                        foreach (KeyValuePair<string, double> pair in snapshot.Attributes)
                        {
                            if (pair.Key == "Cooldown" || pair.Key == "Ammo" || pair.Key == "Flying" ||
                                pair.Key == "Haste" || pair.Key == "Slow" || pair.Key == "Freeze") continue;
                            double before;
                            openingValues.TryGetValue(pair.Key, out before);
                            if (Math.Abs(before - pair.Value) > 1e-9)
                                AddCardUpdate(rows, snapshot, pair.Key, Math.Round(before), Math.Round(pair.Value));
                        }
                        previous[snapshot.ItemId] = snapshot;
                        continue;
                    }
                    CardDef openingCard;
                    if (openingCards.TryGetValue(snapshot.ItemId, out openingCard) && openingCard != null)
                    {
                        foreach (KeyValuePair<string, double> pair in snapshot.Attributes)
                        {
                            if (pair.Key == "Cooldown" || pair.Key == "Ammo" || pair.Key == "Flying" ||
                                pair.Key == "Haste" || pair.Key == "Slow" || pair.Key == "Freeze") continue;
                            // An initial continuous aura is part of the opening
                            // baseline. Export a frame-zero transition only for
                            // an action that actually executed on this tick.
                            bool mutated = result.Events.Exists(delegate(BattleEvent ev) {
                                if (Math.Abs(ev.Time - snapshot.Time) > 1e-9) return false;
                                bool target = ev.TargetItemId == snapshot.ItemId;
                                if (target && ev.Kind == "modifyAttribute") return ev.Note == pair.Key;
                                return (target || ev.SourceItemId == snapshot.ItemId) &&
                                    (ev.Kind == "enchant" || ev.Kind == "enchantRemove" || ev.Kind == "transform");
                            });
                            if (!mutated) continue;
                            int before;
                            Dictionary<string, int> openingAttributes = openingCard.NativeAuraSeedAttributes ?? openingCard.LiveAttributes;
                            openingAttributes.TryGetValue(pair.Key, out before);
                            if (Math.Abs(before - pair.Value) > 1e-9)
                                AddCardUpdate(rows, snapshot, pair.Key, before, Math.Round(pair.Value));
                        }
                    }
                }
                else
                {
                    foreach (KeyValuePair<string, double> pair in snapshot.Attributes)
                    {
                        double before;
                        if (!last.Attributes.TryGetValue(pair.Key, out before) ||
                            Math.Abs(before - pair.Value) > 1e-9)
                        {
                            object previousValue = last.Attributes.ContainsKey(pair.Key)
                                ? (object)Math.Round(last.Attributes[pair.Key]) : 0;
                            AddCardUpdate(rows, snapshot, pair.Key, previousValue,
                                Math.Round(pair.Value));
                        }
                    }
                }
                previous[snapshot.ItemId] = snapshot;
            }
            AppendEnchantMetadata(rows, result, byId);
            AppendTagMetadata(rows, result, byId);
            AppendAcceptedCardStates(rows, result.Events);
            HashSet<string> retired = new HashSet<string>();
            foreach (BattleEvent ev in result.Events)
            {
                if (ev.Kind != "transform" || ev.SourceItemId == ev.TargetItemId ||
                    !byId.ContainsKey(ev.SourceItemId) || !byId.ContainsKey(ev.TargetItemId) || !retired.Add(ev.SourceItemId)) continue;
                rows.Add(new Dictionary<string, object> {
                    { "frame", Frame(ev.Time) }, { "time", Round(ev.Time) },
                    { "instanceId", ev.SourceItemId }, { "kind", "state" },
                    { "previous", "Alive" }, { "current", "Transformed" }
                });
            }
            return rows;
        }

        private static JsonValue metadataEnums;

        private static List<object> MetadataValues(IList<string> names, string enumType)
        {
            if (metadataEnums == null)
                metadataEnums = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_card_metadata_enums.json"));
            List<object> values = new List<object>();
            foreach (string name in names)
            {
                if (!metadataEnums[enumType].Has(name))
                    throw new FormatException("Unknown native metadata " + enumType + ":" + name);
                values.Add(metadataEnums[enumType][name].AsInt());
            }
            return values;
        }

        private static void AddMetadataRow(List<object> rows, double time, string instanceId, Side side,
            string kind, object current)
        {
            Dictionary<string, object> row = new Dictionary<string, object> {
                { "frame", Frame(time) }, { "time", Round(time) },
                { "instanceId", instanceId }, { "kind", kind },
                { "previous", null }, { "current", current }
            };
            rows.Add(row);
        }

        // 原生在卡牌被附魔（或附魔移除）时重发整组元数据：07f044ee 帧 0 的
        // kuMM209 / 0ZgAVEN 都带 enchantment + tags + hiddentags 三行。
        private static void AppendEnchantMetadata(List<object> rows, BattleResult result,
            Dictionary<string, BattleItemView> byId)
        {
            foreach (BattleEvent ev in result.Events)
            {
                if (ev.Kind != "enchant" || string.IsNullOrEmpty(ev.TargetItemId)) continue;
                BattleItemView history;
                if (!byId.TryGetValue(ev.TargetItemId, out history) || history.Card == null) continue;
                CardDef card = history.Card;
                AddMetadataRow(rows, ev.Time, ev.TargetItemId, history.Side, "enchantment", ev.Note);
                AddMetadataRow(rows, ev.Time, ev.TargetItemId, history.Side, "tags",
                    MetadataValues(card.Tags ?? new List<string>(), "ECardTag"));
                AddMetadataRow(rows, ev.Time, ev.TargetItemId, history.Side, "hiddentags",
                    MetadataValues(card.HiddenTags ?? new List<string>(), "EHiddenTag"));
            }
        }

        // 动态标签光环换目标时原生重发 tags 元数据行（07f044ee 帧 140/142 的
        // Drone 标签移动）。事件 Note 存的是本轮光环标签，追加在卡面标签之后。
        private static void AppendTagMetadata(List<object> rows, BattleResult result,
            Dictionary<string, BattleItemView> byId)
        {
            foreach (BattleEvent ev in result.Events)
            {
                if (ev.Kind != "tags" || string.IsNullOrEmpty(ev.TargetItemId)) continue;
                BattleItemView history;
                if (!byId.TryGetValue(ev.TargetItemId, out history) || history.Card == null) continue;
                List<string> tags = new List<string>(history.Card.Tags ?? new List<string>());
                if (!string.IsNullOrEmpty(ev.Note))
                    foreach (string tag in ev.Note.Split(','))
                        if (tag.Length > 0 && !tags.Contains(tag)) tags.Add(tag);
                AddMetadataRow(rows, ev.Time, ev.TargetItemId, history.Side, "tags", MetadataValues(tags, "ECardTag"));
            }
        }

        private static void AppendBirthMetadata(List<object> rows, ItemSnapshot snapshot, BattleItemView history)
        {
            CardDef card = history.Card;
            Dictionary<string, object> values = new Dictionary<string, object>();
            values["tier"] = card.Tier;
            values["size"] = card.Size;
            values["placement"] = new List<object> { history.Side == Side.Enemy ? 1 : 0, history.Slot, 0 };
            List<string> heroes = new List<string>();
            foreach (JsonValue hero in card.NativeTemplate["Heroes"].AsArray()) heroes.Add(hero.AsString());
            values["heroes"] = MetadataValues(heroes, "EHero");
            values["tags"] = MetadataValues(card.Tags, "ECardTag");
            values["hiddentags"] = MetadataValues(card.HiddenTags, "EHiddenTag");
            if (!string.IsNullOrEmpty(card.EnchantmentName)) values["enchantment"] = card.EnchantmentName;
            foreach (KeyValuePair<string, object> pair in values)
                rows.Add(new Dictionary<string, object> {
                    { "frame", Frame(snapshot.Time) }, { "time", Round(snapshot.Time) },
                    { "instanceId", snapshot.ItemId }, { "kind", pair.Key },
                    { "previous", null }, { "current", pair.Value }
                });
        }

        private static void AppendAcceptedCardStates(List<object> rows, List<BattleEvent> events)
        {
            Dictionary<string, string> states = new Dictionary<string, string>();
            Dictionary<string, Dictionary<int, Dictionary<string, object>>> changes =
                new Dictionary<string, Dictionary<int, Dictionary<string, object>>>();
            List<Dictionary<string, object>> ordered = new List<Dictionary<string, object>>();
            foreach (BattleEvent ev in events)
            {
                // These are accepted engine transitions. A numeric snapshot
                // cannot establish this categorical state, and destroy or
                // transform has different native semantics: do not infer it.
                if ((ev.Kind != "disable" && ev.Kind != "repair") ||
                    string.IsNullOrEmpty(ev.TargetItemId)) continue;
                string previous;
                if (!states.TryGetValue(ev.TargetItemId, out previous)) previous = "Alive";
                string current = ev.Kind == "disable" ? "Disabled" : "Alive";
                if (previous == current) continue;
                states[ev.TargetItemId] = current;
                Dictionary<int, Dictionary<string, object>> frames;
                if (!changes.TryGetValue(ev.TargetItemId, out frames))
                {
                    frames = new Dictionary<int, Dictionary<string, object>>();
                    changes[ev.TargetItemId] = frames;
                }
                int frame = Frame(ev.Time);
                Dictionary<string, object> row;
                if (!frames.TryGetValue(frame, out row))
                {
                    row = new Dictionary<string, object> {
                        { "frame", frame }, { "time", Round(ev.Time) },
                        { "instanceId", ev.TargetItemId }, { "kind", "state" },
                        { "previous", previous }, { "current", current }
                    };
                    if (ev.TargetSide.HasValue)
                        row["side"] = ev.TargetSide.Value == Side.Player ? "player" : "enemy";
                    frames[frame] = row;
                    ordered.Add(row);
                }
                else row["current"] = current;
            }
            // Native has one StateUpdate per item/frame: a same-frame
            // disable/repair round trip has no final state change to export.
            foreach (Dictionary<string, object> row in ordered)
                if ((string)row["previous"] != (string)row["current"]) rows.Add(row);
        }

        private static void AddCardUpdate(List<object> rows, ItemSnapshot snapshot, string attribute,
            object previous, object current)
        {
            Dictionary<string, object> row = new Dictionary<string, object>();
            row["frame"] = Frame(snapshot.Time);
            row["time"] = Round(snapshot.Time);
            row["instanceId"] = snapshot.ItemId;
            row["attribute"] = attribute;
            row["previous"] = previous;
            row["current"] = current;
            rows.Add(row);
        }

        private static string SimKind(string kind)
        {
            if (kind == "shield") return "shieldApply";
            if (kind == "burn") return "burnApply";
            if (kind == "poison") return "poisonApply";
            if (kind == "regen") return "regenApply";
            if (kind == "playerAttribute") return "playerModifyAttribute";
            if (kind == "tempo") return "tempoApply";
            return kind;
        }

        private static string TemplateId(CardDef card)
        {
            if (card == null) return null;
            if (!string.IsNullOrEmpty(card.BaseItemId)) return card.BaseItemId;
            string id = card.Id ?? string.Empty;
            int cut = id.IndexOf('#');
            if (cut >= 0) id = id.Substring(0, cut);
            cut = id.IndexOf('@');
            if (cut >= 0) id = id.Substring(0, cut);
            return id;
        }

        private static Dictionary<string, JsonValue> LoadTemplates(string path)
        {
            Dictionary<string, JsonValue> templates = new Dictionary<string, JsonValue>();
            foreach (string line in File.ReadLines(path))
            {
                string text = line.Trim();
                if (text.Length == 0) continue;
                JsonValue row = Json.Parse(text);
                string id = row["id"].AsString();
                if (!string.IsNullOrEmpty(id) && row["template"].Kind == JsonKind.Object)
                    templates[id] = row["template"];
            }
            return templates;
        }

        private static string Sha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static string Text(JsonValue row, string key, string fallback)
        {
            JsonValue value = row[key];
            return value.Kind == JsonKind.String ? value.AsString() : fallback;
        }

        private static double Number(JsonValue row, string key, double fallback)
        {
            JsonValue value = row[key];
            return value.Kind == JsonKind.Number ? value.AsDouble() : fallback;
        }

        private static double? NullableNumber(JsonValue value)
        {
            return value.Kind == JsonKind.Number ? (double?)value.AsDouble() : null;
        }

        private static int Frame(double time)
        {
            return (int)Math.Round(time / FrameSeconds);
        }

        private static double Round(double value)
        {
            return Math.Round(value, 6);
        }

        private static List<object> ToObjects(IEnumerable<string> values)
        {
            List<object> result = new List<object>();
            foreach (string value in values) result.Add(value);
            return result;
        }
    }

    // Minimal JSON writer for the ledger dictionaries. Keeping it local avoids
    // changing the engine's parse-only JsonValue contract.
    public static class JsonText
    {
        public static string Write(object value)
        {
            StringBuilder builder = new StringBuilder();
            WriteValue(builder, value);
            return builder.ToString();
        }

        private static void WriteValue(StringBuilder builder, object value)
        {
            if (value == null) { builder.Append("null"); return; }
            string text = value as string;
            if (text != null) { WriteString(builder, text); return; }
            if (value is bool) { builder.Append((bool)value ? "true" : "false"); return; }
            if (value is int || value is long) { builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture)); return; }
            if (value is double)
            {
                double number = (double)value;
                if (double.IsNaN(number) || double.IsInfinity(number)) builder.Append("null");
                else builder.Append(number.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            IDictionary<string, object> map = value as IDictionary<string, object>;
            if (map != null)
            {
                builder.Append('{');
                bool first = true;
                foreach (KeyValuePair<string, object> pair in map)
                {
                    if (!first) builder.Append(',');
                    first = false;
                    WriteString(builder, pair.Key);
                    builder.Append(':');
                    WriteValue(builder, pair.Value);
                }
                builder.Append('}');
                return;
            }
            IEnumerable sequence = value as IEnumerable;
            if (sequence != null)
            {
                builder.Append('[');
                bool first = true;
                foreach (object item in sequence)
                {
                    if (!first) builder.Append(',');
                    first = false;
                    WriteValue(builder, item);
                }
                builder.Append(']');
                return;
            }
            WriteString(builder, Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static void WriteString(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else builder.Append(c);
                        break;
                }
            }
            builder.Append('"');
        }
    }
}
