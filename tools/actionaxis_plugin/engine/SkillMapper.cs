using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BazaarActionAxis.Engine
{
    public sealed class SkillCatalogEntry
    {
        public string Name;
        public JsonValue Template;
        public JsonValue Tooltips;
    }

    public sealed class SkillCatalog
    {
        private readonly Dictionary<string, SkillCatalogEntry> entries = new Dictionary<string, SkillCatalogEntry>(StringComparer.OrdinalIgnoreCase);

        public static SkillCatalog LoadFile(string path) { return LoadJson(File.ReadAllText(path)); }

        public static SkillCatalog LoadJson(string json)
        {
            SkillCatalog catalog = new SkillCatalog();
            JsonValue root = Json.Parse(json);
            if (root.Kind != JsonKind.Array) throw new FormatException("skills catalog must be an array");
            foreach (JsonValue row in root.AsArray())
            {
                if (row.Kind != JsonKind.Object) continue;
                string id = NativeSkillSupport.Text(row, "templateId");
                JsonValue template = row["template"];
                if (string.IsNullOrEmpty(id) || NativeSkillSupport.Type(template) != "TCardSkill") continue;
                SkillCatalogEntry entry = new SkillCatalogEntry();
                entry.Name = NativeSkillSupport.Text(row, "nameCn") ?? NativeSkillSupport.Text(row, "name");
                entry.Template = template;
                entry.Tooltips = row["tooltips"];
                catalog.entries[id] = entry;
            }
            return catalog;
        }

        public SkillCatalogEntry Find(string templateId)
        {
            SkillCatalogEntry result;
            return templateId != null && entries.TryGetValue(templateId, out result) ? result : null;
        }
    }

    public sealed class SkillMappingStatus
    {
        public string SourceId;
        public string InstanceId;
        public string TemplateId;
        public string Name;
        public string Tier;
        public string Status;
        public string Reason;
        public bool NativeTierHasNoEffects;
        public List<string> RawTooltips = new List<string>();
        public List<string> MappingWarnings = new List<string>();
    }

    public sealed class SkillMappingResult
    {
        public List<SkillMappingStatus> Skills = new List<SkillMappingStatus>();
        public List<string> Warnings = new List<string>();
        public int CombatCount;
        public int UnsupportedCount;
        public string Summary;
    }

    public sealed class NativeSkillRule
    {
        public string SourceOwner;
        public JsonValue SourceCondition;
        public JsonValue Prerequisites;
        public bool SourceSelf;
        public bool SourceOther;
        public bool SourceAdjacent;
        public string SourcePositionMode;
        public bool IncludeOrigin;
        public bool SourceOccupying;
        // Trigger.Subject 为 TTargetCardXMost 时的具体最左/最右目标。
        public Target SourceXMostTarget;
        // 原生 Trigger.Target：行动受体。卡牌受体用 Target* 字段，
        // 英雄受体（如“你中毒时”）用 TargetIsPlayer。
        public string TargetOwner;
        public bool TargetSelf;
        public bool TargetOther;
        public string TargetPositionMode;
        public bool TargetIncludeOrigin;
        public bool TargetOccupying;
        public JsonValue TargetCondition;
        public bool TargetIsPlayer;
        public string TargetPlayerOwner;
        public JsonValue TargetPlayerCondition;
        // 原生 Trigger.Source：造成修理／变形／摧毁的行动者。
        public string ActorOwner;
        public bool ActorSelf;
        public bool ActorOther;
        public string ActorPositionMode;
        public bool ActorIncludeOrigin;
        public bool ActorOccupying;
        public JsonValue ActorCondition;
        // 人物触发（暴怒、受伤、死亡）：self / enemy，按英雄而非卡牌匹配。
        public string PlayerOwner;
        public JsonValue PlayerCondition;
    }

    // Exact, bounded subset of native skill definitions. Unknown rules reject the
    // complete skill rather than dropping a prerequisite or inventing an effect.
    public static class SkillMapper
    {
        private static readonly string[] TierOrder = { "Bronze", "Silver", "Gold", "Diamond", "Legendary" };

        public static SkillMappingResult Apply(BattleConfig config, IEnumerable<LiveSkillEntry> skills)
        {
            return Apply(config, skills, null);
        }

        public static SkillMappingResult Apply(BattleConfig config, IEnumerable<LiveSkillEntry> skills, SkillCatalog catalog)
        {
            return Apply(config, skills, catalog, Side.Player);
        }

        public static SkillMappingResult Apply(BattleConfig config, IEnumerable<LiveSkillEntry> skills, SkillCatalog catalog, Side side)
        {
            SkillMappingResult result = new SkillMappingResult();
            List<CardDef> mappedSkills = side == Side.Player ? config.PlayerSkills : config.EnemySkills;
            mappedSkills.Clear();
            if (skills != null)
            {
                int index = 0;
                foreach (LiveSkillEntry live in skills)
                {
                    if (live == null) continue;
                    SkillMappingStatus status = new SkillMappingStatus();
                    status.InstanceId = live.InstanceId;
                    status.TemplateId = live.TemplateId;
                    status.SourceId = "skill:" + (string.IsNullOrEmpty(live.InstanceId) ? (live.TemplateId ?? "unknown") + ":" + index : live.InstanceId);
                    if (side == Side.Enemy) status.SourceId = "enemy:" + status.SourceId;
                    status.Tier = live.Tier;
                    SkillCatalogEntry catalogEntry = catalog == null ? null : catalog.Find(live.TemplateId);
                    JsonValue template = live.Template;
                    if (template == null || template.Kind == JsonKind.Null)
                        template = catalogEntry == null ? null : catalogEntry.Template;
                    status.Name = !string.IsNullOrEmpty(live.Name) ? live.Name : catalogEntry == null ? null : catalogEntry.Name;
                    if (string.IsNullOrEmpty(status.Name) && template != null && template.Kind == JsonKind.Object)
                        status.Name = NativeSkillSupport.Text(template, "InternalName");
                    if (string.IsNullOrEmpty(status.Name)) status.Name = live.TemplateId ?? "未知技能";
                    if (live.RawTooltips != null) status.RawTooltips.AddRange(live.RawTooltips);
                    try
                    {
                        if (NativeSkillSupport.Type(template) != "TCardSkill")
                        {
                            status.Status = "missing-template";
                            status.Reason = "已记录技能；缺少有效原生模板，未加入模拟";
                        }
                        else Map(config, live, template, catalogEntry, status, side);
                    }
                    catch (Exception error)
                    {
                        status.Status = "unsupported";
                        status.Reason = "未加入模拟：" + error.Message;
                    }
                    if (status.MappingWarnings.Count > 0)
                        result.Warnings.AddRange(status.MappingWarnings);
                    if (status.Status == "simulated") result.CombatCount++;
                    if (status.Status == "unsupported" || status.Status == "missing-template")
                    {
                        result.UnsupportedCount++;
                        result.Warnings.Add(status.Name + "：" + status.Reason);
                    }
                    result.Skills.Add(status);
                    index++;
                }
            }
            result.Summary = "记录 " + result.Skills.Count + " 个技能，" + result.CombatCount + " 个参与战斗计算";
            if (result.UnsupportedCount > 0) result.Summary += "，" + result.UnsupportedCount + " 个规则待支持";
            return result;
        }

        private static void Map(BattleConfig config, LiveSkillEntry live, JsonValue template, SkillCatalogEntry catalog, SkillMappingStatus status, Side side)
        {
            JsonValue tiers = template["Tiers"];
            string tier = string.IsNullOrEmpty(live.Tier) ? NativeSkillSupport.Text(template, "StartingTier") : live.Tier;
            if (tiers.Kind != JsonKind.Object || Array.IndexOf(TierOrder, tier) < 0)
                throw new FormatException("未知技能品质 " + tier);
            // Native TCardSkill.TryGetTierTemplate normalizes Legendary to
            // Diamond and otherwise uses exact dictionary lookup. A known tier
            // missing from the template has no abilities or auras; it must not
            // be upgraded to StartingTier or treated as a reader failure.
            if (tier == "Legendary") tier = "Diamond";
            if (!tiers.Has(tier))
            {
                status.Status = "inactive";
                status.Reason = "该品质没有原生技能效果";
                status.NativeTierHasNoEffects = true;
                return;
            }
            Dictionary<string, double> attrs = new Dictionary<string, double>();
            JsonValue abilities = JsonValue.Null;
            JsonValue auras = JsonValue.Null;
            JsonValue tooltipIds = JsonValue.Null;
            bool foundTier = false;
            foreach (string candidate in TierOrder)
            {
                if (tiers.Has(candidate))
                {
                    JsonValue row = tiers[candidate];
                    foreach (KeyValuePair<string, JsonValue> attr in row["Attributes"].AsObject())
                        if (attr.Value.Kind == JsonKind.Number) attrs[attr.Key] = attr.Value.AsDouble();
                    // Native deserialization materializes omitted properties as
                    // null. Only explicit arrays override sparse tier inheritance.
                    if (row["AbilityIds"].Kind == JsonKind.Array) abilities = row["AbilityIds"];
                    if (row["AuraIds"].Kind == JsonKind.Array) auras = row["AuraIds"];
                    if (row["TooltipIds"].Kind == JsonKind.Array) tooltipIds = row["TooltipIds"];
                }
                if (candidate == tier) { foundTier = true; break; }
            }
            if (!foundTier || abilities.Kind != JsonKind.Array || auras.Kind != JsonKind.Array)
                throw new FormatException("技能品质规则不完整");
            if (live.Attributes != null)
                foreach (KeyValuePair<string, int> attr in live.Attributes) attrs[attr.Key] = attr.Value;
            AddTooltips(status, template, catalog, tooltipIds);

            CardDef skill = new CardDef();
            skill.Id = status.SourceId;
            skill.NativeAttributesAreLive = live.AttributesAreLive;
            skill.BaseItemId = live.TemplateId;
            skill.NameCn = status.Name;
            // 效果数值在结算时读 LiveAttributes；这里必须把技能属性带上。
            skill.LiveAttributes = new Dictionary<string, int>();
            foreach (KeyValuePair<string, double> attr in attrs) skill.LiveAttributes[attr.Key] = (int)JsMath.Round(attr.Value);
            skill.Tier = tier;
            skill.Passive = true;
            skill.DataSource = "live-skill";
            skill.RawTooltips = status.RawTooltips;
            int noncombat = 0;
            foreach (JsonValue id in abilities.AsArray())
            {
                string key = id.AsString();
                if (key == null || !template["Abilities"].Has(key))
                {
                    status.MappingWarnings.Add(status.Name + "：能力定义缺失 " + key + "（已跳过，保留其余战斗部分）");
                    continue;
                }
                JsonValue ability = template["Abilities"][key];
                if (IsNonCombatTrigger(ability["Trigger"])) { noncombat++; continue; }
                string active = NativeSkillSupport.Text(ability, "ActiveIn");
                string works = NativeSkillSupport.Text(ability, "WorksIn");
                if (active != "HandOnly" && active != "HandAndStash") throw new FormatException("能力区域 " + active);
                if (works != null && works != "Anywhere" && works != "CombatOnly") throw new FormatException("能力场景 " + works);
                if (!NativeSkillSupport.PrerequisitesSupported(ability["Prerequisites"])) throw new FormatException("尚不支持条件或次数限制");
                Effect effect = MapAction(ability["Action"], attrs);
                effect.NativeAbilityId = id.AsString();
                foreach (TriggerDef trigger in NativeSkillSupport.MapTriggers(ability["Trigger"]))
                {
                    trigger.SkillRule.Prerequisites = ability["Prerequisites"];
                    trigger.Priority = NativeSkillSupport.Text(ability, "Priority");
                    trigger.Effects.Add(effect);
                    skill.Triggers.Add(trigger);
                }
            }

            int auraCount = 0;
            int noncombatAura = 0;
            bool anyAuraActive = false;
            List<JsonValue> runtimeAuras = new List<JsonValue>();
            List<JsonValue> playerStaticAuras = new List<JsonValue>();
            BattleContext ctx = null;
            foreach (JsonValue id in auras.AsArray())
            {
                string key = id.AsString();
                if (key == null || !template["Auras"].Has(key))
                {
                    // 缺定义的光环按 DATA 告警跳过，保留同卡已完整映射的主动能力；
                    // 不能因为一个缺失/过期的 AuraId 把整张战斗技能判为 unsupported。
                    status.MappingWarnings.Add(status.Name + "：技能缺少光环定义 " + key + "（已跳过，保留其余战斗部分）");
                    continue;
                }
                auraCount++;
                JsonValue aura = template["Auras"][key];
                if (aura.Kind != JsonKind.Object)
                {
                    status.MappingWarnings.Add(status.Name + "：技能光环定义为空 " + key + "（已跳过，保留其余战斗部分）");
                    continue;
                }
                bool inherited = IsInheritedStaticAura(aura);
                string auraType = NativeSkillSupport.Type(aura["Action"]);
                // 经济类人物光环不改变战斗结果；跳过动作但保留同卡战斗光环。
                if (IsNonCombatPlayerAura(aura))
                {
                    noncombatAura++;
                    continue;
                }
                // 标签光环的目标集合随棋盘变化（It's a Drone：最左手牌；Free Ride：
                // 最右手牌）。静态烘焙会把标签永久留在开场那张卡上，必须交给战斗内
                // 动态层逐帧重建（07f044ee 帧 140/142 Drone 标签随目标移动）。
                if (auraType == "TAuraActionCardAddTagsList" &&
                    NativeSkillSupport.Type(aura["Action"]["Target"]) == "TTargetCardXMost")
                {
                    NativeDynamicAuras.ValidateTagAura(aura);
                    runtimeAuras.Add(aura);
                    anyAuraActive = true;
                    continue;
                }
                // 玩家技能的人物光环统一走可撤销派生层。纯模板在 InitBattle
                // 应用；实时快照已包含时不重复叠加。
                if (side == Side.Player && auraType == "TAuraActionPlayerModifyAttribute")
                {
                    // 实时棋盘的光环已计入快照；无条件的静态光环保持
                    // live-applied。带人物条件/暴击/减伤的光环必须运行时重算。
                    bool needsRuntime = PlayerAuraNeedsRuntime(aura);
                    if (!live.TemplateBoard && !needsRuntime) { anyAuraActive = true; continue; }
                    if (live.AttributesAreLive && !needsRuntime) { anyAuraActive = true; continue; }
                    // 快照只含开场值；值引用卡牌/卡牌数量时（Fairy Haze：
                    // “冻结物品数量 × Custom_0 回复”）必须在战斗内逐帧重算。
                    NativeDynamicAuras.Validate(aura);
                    runtimeAuras.Add(aura);
                    continue;
                }
                // 怪物技能里的人物暴击/减伤光环也必须在运行时按英雄结算，
                // 不能写死成开场常量或被 ApplyStaticAuras 拒绝。
                if (side == Side.Enemy && auraType == "TAuraActionPlayerModifyAttribute" &&
                    PlayerAuraNeedsRuntimeEnemy(aura))
                {
                    if (live.AttributesAreLive) { anyAuraActive = true; continue; }
                    NativeDynamicAuras.Validate(aura);
                    runtimeAuras.Add(aura);
                    continue;
                }
                // 人物条件/其他区域无法当作开场静态加成：交给战斗内动态光环层。
                if (auraType == "TAuraActionCardModifyAttribute" && (!inherited || skill.Triggers.Count > 0))
                {
                    // Even an unconditional live aura can read a counter that
                    // changes in battle (Inexorable/Lethargy). The derived layer
                    // removes its opening contribution before rebuilding it.
                    NativeDynamicAuras.Validate(aura);
                    runtimeAuras.Add(aura);
                    continue;
                }
                if (side == Side.Player && !inherited)
                {
                    // 纯模板/测试棋盘需要现场加标签；实时棋盘重复加同一标签是幂等的。
                    if (IsStaticTagAura(aura))
                    {
                        playerStaticAuras.Add(aura);
                        anyAuraActive = true;
                        continue;
                    }
                    throw new FormatException("尚不支持动态光环、未知光环属性或条件");
                }

                if (!NativeSkillSupport.PrerequisitesSupported(aura["Prerequisites"])) throw new FormatException("尚不支持光环条件");
                JsonValue prereqs = aura["Prerequisites"];
                if (ctx == null) ctx = BattleEngine.InitBattle(config);
                InternalItem source = new InternalItem();
                source.Side = side;
                if (NativeSkillSupport.PrerequisitesMatch(ctx, source, new NativeSkillRule { Prerequisites = prereqs })) anyAuraActive = true;
            }

            // 标签光环幂等，实时棋盘也必须在现场补齐同侧标签。
            if (side == Side.Player && playerStaticAuras.Count > 0)
                NativeCardAbilityMapper.ApplyStaticAuras(config, skill, side, attrs, playerStaticAuras, false);

            if (skill.Triggers.Count > 0)
            {
                // 战斗技能可以同时有静态与动态光环：静态层先映射，动态层
                // 交由 NativeDynamicAuras 在每帧重算。
                List<JsonValue> staticAuras = new List<JsonValue>();
                foreach (JsonValue id in auras.AsArray())
                {
                    JsonValue definition = template["Auras"][id.AsString()];
                    if (definition.Kind == JsonKind.Object && !runtimeAuras.Contains(definition)) staticAuras.Add(definition);
                }
                if (live.TemplateBoard && !live.AttributesAreLive && staticAuras.Count > 0)
                    NativeCardAbilityMapper.ApplyStaticAuras(config, skill, side, attrs, staticAuras, false);
                skill.NativeRuntimeAuras = new List<JsonValue>(runtimeAuras);
                status.Status = "simulated";
                status.Reason = "战斗触发效果已加入计算";
                (side == Side.Player ? config.PlayerSkills : config.EnemySkills).Add(skill);
            }
            else if (auraCount > 0)
            {
                if (noncombatAura == auraCount)
                {
                    status.Status = "out-of-combat";
                    status.Reason = "经济类人物光环不参与行动轴战斗模拟";
                    return;
                }
                List<JsonValue> staticAuras = new List<JsonValue>();
                foreach (JsonValue id in auras.AsArray())
                {
                    JsonValue definition = template["Auras"][id.AsString()];
                    if (definition.Kind == JsonKind.Object && !runtimeAuras.Contains(definition) && !IsNonCombatPlayerAura(definition)) staticAuras.Add(definition);
                }
                if (side == Side.Player && live.TemplateBoard && !live.AttributesAreLive && staticAuras.Count > 0)
                    NativeCardAbilityMapper.ApplyStaticAuras(config, skill, side, attrs, staticAuras, false);
                if (side == Side.Enemy)
                {
                    if (staticAuras.Count > 0) NativeCardAbilityMapper.ApplyStaticAuras(config, skill, side, attrs, staticAuras, false);
                    // 静态属性光环也属于可重算层，保留来源以免后续动态重算丢掉怪物技能加成。
                    foreach (JsonValue definition in skill.NativeRuntimeAuras)
                        if (!runtimeAuras.Contains(definition)) runtimeAuras.Add(definition);
                    skill.NativeRuntimeAuras = new List<JsonValue>(runtimeAuras);
                    bool active = anyAuraActive || runtimeAuras.Count > 0;
                    status.Status = active ? "simulated" : "inactive";
                    status.Reason = active ? "怪物光环已按模板加入计算（含战斗内动态层）" : "当前怪物棋盘不满足光环条件";
                    if (runtimeAuras.Count > 0) config.EnemySkills.Add(skill);
                }
                else if (runtimeAuras.Count > 0)
                {
                    skill.NativeRuntimeAuras = new List<JsonValue>(runtimeAuras);
                    config.PlayerSkills.Add(skill);
                    status.Status = "simulated";
                    status.Reason = "动态光环按战斗内生命/怒气实时计算";
                }
                else
                {
                    status.Status = !anyAuraActive ? "inactive" : "live-applied";
                    status.Reason = status.Status == "inactive" ? "当前棋盘不满足光环条件" : "静态加成沿用实测卡牌及人物属性，避免重复叠加";
                }
            }
            else if (noncombat > 0)
            {
                status.Status = "out-of-combat";
                status.Reason = "已记录；升级、购买等非战斗效果不触发行动轴";
            }
            else
            {
                status.Status = "inactive";
                status.Reason = "当前品质没有战斗能力";
            }
        }

        private static void AddTooltips(SkillMappingStatus status, JsonValue template, SkillCatalogEntry catalog, JsonValue ids)
        {
            if (status.RawTooltips.Count > 0) return;
            JsonValue localization = template["Localization"];
            foreach (JsonValue id in ids.AsArray())
            {
                int index = id.AsInt();
                string text = null;
                if (catalog != null)
                    foreach (JsonValue row in catalog.Tooltips == null ? new List<JsonValue>() : catalog.Tooltips.AsArray())
                        if (row["index"].AsInt() == index) text = NativeSkillSupport.Text(row, "textCn") ?? NativeSkillSupport.Text(row, "text");
                if (text == null && localization.Kind == JsonKind.Object && localization["Tooltips"].Kind == JsonKind.Array && index >= 0 && index < localization["Tooltips"].Count)
                    text = NativeSkillSupport.Text(localization["Tooltips"][index]["Content"], "Text");
                if (!string.IsNullOrEmpty(text)) status.RawTooltips.Add(text);
            }
        }

        private static bool IsStaticTagAura(JsonValue aura)
        {
            string active = NativeSkillSupport.Text(aura, "ActiveIn");
            string works = NativeSkillSupport.Text(aura, "WorksIn");
            if (active != "HandOnly" && active != "HandAndStash" || works != null && works != "Anywhere" && works != "CombatOnly") return false;
            string type = NativeSkillSupport.Type(aura["Action"]);
            if (type != "TAuraActionCardAddTagsList" && type != "TAuraActionCardAddTagsBySource") return false;
            if (!NativeSkillSupport.PrerequisitesSupported(aura["Prerequisites"])) return false;
            if (NativeSkillSupport.HasPlayerPrerequisite(aura["Prerequisites"])) return false;
            if (HasDynamicCardDependency(aura["Prerequisites"])) return false;
            return true;
        }

        private static bool PlayerAuraNeedsRuntime(JsonValue aura)
        {
            JsonValue action = aura["Action"];
            if (NativeSkillSupport.Type(action) != "TAuraActionPlayerModifyAttribute") return false;
            string attribute = NativeSkillSupport.Text(action, "AttributeType");
            if (attribute == "CritChance" || attribute == "PercentDamageReduction" ||
                NativeSkillSupport.HasPlayerPrerequisite(aura["Prerequisites"])) return true;
            // 回复类（HealthRegen）在实时快照里只是开场值；值引用卡牌/卡牌数量
            // 时必须逐帧重算（07f044ee Fairy Haze：冻结/减速物品数 × Custom_0 回复，
            // 原生帧 70-140 的 HealthRegen 随状态数量变化）。其余人物属性暂保持
            // 既有的 live-applied 快照语义。
            return attribute == "HealthRegen" && ReferencesBoardValue(action["Value"]);
        }

        // 怪物技能的动态人物光环沿用既有判定（暴击/减伤/人物前提），
        // 避免把静态人物上限改成逐帧重算（Swole 的 Max Health 由武器伤害推导）。
        private static bool PlayerAuraNeedsRuntimeEnemy(JsonValue aura)
        {
            JsonValue action = aura["Action"];
            if (NativeSkillSupport.Type(action) != "TAuraActionPlayerModifyAttribute") return false;
            string attribute = NativeSkillSupport.Text(action, "AttributeType");
            return attribute == "CritChance" || attribute == "PercentDamageReduction" ||
                NativeSkillSupport.HasPlayerPrerequisite(aura["Prerequisites"]);
        }

        // 光环数值是否随战斗内的棋盘状态变化（卡牌属性/数量/标签数）。
        private static bool ReferencesBoardValue(JsonValue value)
        {
            string type = NativeSkillSupport.Type(value);
            if (string.IsNullOrEmpty(type)) return false;
            if (type == "TReferenceValueCardCount" || type == "TReferenceValueCardAttribute" ||
                type == "TReferenceValueCardAttributeUnscaled" ||
                type == "TReferenceValueCardAttributeAggregate" || type == "TReferenceValueCardTagCount")
                return true;
            if (type == "TFixedValue" || type == "TReferenceValueAttributeChange" ||
                type == "TReferenceValuePlayerAttribute" || type == "TReferenceValuePlayerAttributeUnscaled")
                return false;
            JsonValue modifier = value["Modifier"];
            return !NativeSkillSupport.Empty(modifier) && ReferencesBoardValue(modifier["Value"]);
        }

        private static bool IsNonCombatPlayerAura(JsonValue aura)
        {
            if (aura.Kind != JsonKind.Object) return false;
            JsonValue action = aura["Action"];
            if (NativeSkillSupport.Type(action) != "TAuraActionPlayerModifyAttribute") return false;
            string attribute = NativeSkillSupport.Text(action, "AttributeType");
            return attribute == "Gold" || attribute == "Income" || attribute == "Experience" ||
                attribute == "Prestige" || attribute == "RerollCostModifier" || attribute == "BuyPrice";
        }

        private static bool IsInheritedStaticAura(JsonValue aura)
        {
            string active = NativeSkillSupport.Text(aura, "ActiveIn");
            string works = NativeSkillSupport.Text(aura, "WorksIn");
            if (active != "HandOnly" && active != "HandAndStash" || works != null && works != "Anywhere" && works != "CombatOnly") return false;
            if (!NativeSkillSupport.PrerequisitesSupported(aura["Prerequisites"])) return false;
            // 人物条件（生命、暴怒）会随战斗变化，不能当作开场静态光环。
            if (NativeSkillSupport.HasPlayerPrerequisite(aura["Prerequisites"])) return false;
            if (HasDynamicCardDependency(aura["Prerequisites"])) return false;
            JsonValue action = aura["Action"];
            string type = NativeSkillSupport.Type(action);
            string attribute = NativeSkillSupport.Text(action, "AttributeType");
            if (type == "TAuraActionPlayerModifyAttribute")
            {
                if (attribute != "HealthMax" || NativeSkillSupport.Type(action["Target"]) != "TTargetPlayerRelative" ||
                    NativeSkillSupport.Text(action["Target"], "TargetMode") != "Self" || !NativeSkillSupport.Empty(action["Target"]["Conditions"])) return false;
            }
            else if (type == "TAuraActionCardModifyAttribute")
            {
                string[] inherited = { "DamageAmount", "HealAmount", "ShieldApplyAmount", "BurnApplyAmount", "PoisonApplyAmount", "RegenApplyAmount", "CritChance", "Lifesteal", "CooldownMax", "AmmoMax", "Multicast", "HasteAmount", "HasteTargets", "SlowAmount", "SlowTargets", "FreezeAmount", "FreezeTargets", "ChargeAmount", "ChargeTargets" };
                if (Array.IndexOf(inherited, attribute) < 0 || NativeSkillSupport.Type(action["Target"]) != "TTargetCardSection" ||
                    NativeSkillSupport.Text(action["Target"], "TargetSection") != "SelfHand" || !StaticTargetCondition(action["Target"]["Conditions"])) return false;
            }
            else return false;
            string operation = NativeSkillSupport.Text(action, "Operation");
            return (operation == "Add" || operation == "Multiply" || operation == "Set") && StaticValue(action["Value"]);
        }

        private static bool StaticTargetCondition(JsonValue value)
        {
            if (HasDynamicCardDependency(value)) return false;
            if (NativeSkillSupport.ConditionSupported(value)) return true;
            string type = NativeSkillSupport.Type(value);
            if (type == "TCardConditionalCanCrit" || type == "TCardConditionalHiddenTag") return true;
            if (type == "TCardConditionalAnd" || type == "TCardConditionalOr")
            {
                if (value["Conditions"].Kind != JsonKind.Array) return false;
                foreach (JsonValue child in value["Conditions"].AsArray()) if (!StaticTargetCondition(child)) return false;
                return true;
            }
            return false;
        }

        private static bool StaticValue(JsonValue value)
        {
            if (HasDynamicCardDependency(value)) return false;
            string type = NativeSkillSupport.Type(value);
            if (type == "TFixedValue") return value["Value"].Kind == JsonKind.Number;
            if (type == "TReferenceValueCardAttribute")
            {
                if (NativeSkillSupport.Type(value["Target"]) != "TTargetCardSelf" || !NativeSkillSupport.Empty(value["Target"]["Conditions"])) return false;
            }
            else if (type == "TReferenceValueCardCount")
            {
                JsonValue target = value["Target"];
                string section = NativeSkillSupport.Text(target, "TargetSection");
                if (NativeSkillSupport.Type(target) != "TTargetCardSection" || section != "SelfHand" && section != "SelfBoard" ||
                    !StaticTargetCondition(target["Conditions"])) return false;
            }
            else return false;
            return NativeSkillSupport.Empty(value["Modifier"]) || StaticValue(value["Modifier"]["Value"]);
        }

        // Supported does not mean invariant: Flying, current attributes and
        // attribute rankings can change after the live snapshot was recorded.
        private static bool HasDynamicCardDependency(JsonValue value)
        {
            if (NativeSkillSupport.Empty(value)) return false;
            if (value.Kind == JsonKind.Array)
            {
                foreach (JsonValue child in value.AsArray()) if (HasDynamicCardDependency(child)) return true;
                return false;
            }
            if (value.Kind != JsonKind.Object) return false;
            string type = NativeSkillSupport.Type(value);
            if (!string.IsNullOrEmpty(type) && type.StartsWith("TCardConditionalAttribute", StringComparison.Ordinal) ||
                type == "TCardConditionalState" || type == "TPrerequisiteCardAttributeComparator") return true;
            if (type == "TReferenceValueCardAttribute" || type == "TReferenceValueCardAttributeUnscaled")
            {
                string attribute = NativeSkillSupport.Text(value, "AttributeType");
                // A pure skill's own Custom_N is its fixed tier value. Keep the
                // inherited handling of ordinary flat bonuses using that value.
                if (string.IsNullOrEmpty(attribute) || !attribute.StartsWith("Custom_", StringComparison.Ordinal) ||
                    NativeSkillSupport.Type(value["Target"]) != "TTargetCardSelf") return true;
            }
            foreach (KeyValuePair<string, JsonValue> pair in value.AsObject())
                if (HasDynamicCardDependency(pair.Value)) return true;
            return false;
        }

        internal static bool IsNonCombatTrigger(JsonValue trigger)
        {
            string type = NativeSkillSupport.Type(trigger);
            if (type == "TTriggerOr" || type == "TTriggerAnd")
            {
                JsonValue children = trigger["Triggers"];
                if (children.Kind != JsonKind.Array || children.Count == 0) return false;
                foreach (JsonValue child in children.AsArray()) if (!IsNonCombatTrigger(child)) return false;
                return true;
            }
            if (type == "TTriggerOnPlayerAttributeChanged")
                return NativeSkillSupport.Text(trigger, "AttributeType") == "Level" ||
                    NativeSkillSupport.Text(trigger, "AttributeType") == "Experience" ||
                    NativeSkillSupport.Text(trigger, "AttributeType") == "Gold" ||
                    NativeSkillSupport.Text(trigger, "AttributeType") == "Income" ||
                    NativeSkillSupport.Text(trigger, "AttributeType") == "Prestige";
            return type == "TTriggerOnPlayerLevelUp" || type == "TTriggerOnPlayerLevelChanged" ||
                type == "TTriggerOnCardPurchased" || type == "TTriggerOnCardSold" || type == "TTriggerOnDayStarted" ||
                type == "TTriggerOnHourStarted" || type == "TTriggerOnCardUpgraded" || type == "TTriggerOnCardEnchanted" ||
                type == "TTriggerOnFightEnded" || type == "TTriggerOnPlayerExperienceGained" ||
                type == "TTriggerOnCardSelected" || type == "TTriggerOnEncounterSelected" ||
                type == "TTriggerOnEncounterEntered" || type == "TTriggerOnEncounterExited" ||
                type == "TTriggerOnEncounterCardsDealt";
        }

        internal static TriggerDef MapTrigger(JsonValue raw)
        {
            return NativeSkillSupport.MapTrigger(raw);
        }

        internal static Effect MapAction(JsonValue raw, Dictionary<string, double> attrs)
        {
            string costAttribute = null;
            NumExpr costAmount = null;
            JsonValue cost = raw["Cost"];
            if (!NativeSkillSupport.Empty(cost))
            {
                // 已取证的行动代价只有“花费人物 Tempo”（Tuba 的三段减速，
                // 0276c46b 帧 82/140/189/234/241 各有 CardActionCostSpent）。
                if (NativeSkillSupport.Type(cost) != "TActionCostPlayerAttribute" ||
                    NativeSkillSupport.Text(cost, "PlayerAttributeType") != "Tempo")
                    throw new FormatException("尚不支持行动代价");
                costAttribute = "Tempo";
                costAmount = MapValue(cost["Value"], attrs, "TempoCost", true);
            }
            Effect result = MapActionCore(raw, attrs);
            if (costAttribute == null) return result;
            if (result.Kind == "composite") throw new FormatException("复合行动尚不支持行动代价");
            result.CostAttribute = costAttribute;
            result.CostAmount = costAmount;
            return result;
        }

        private static Effect MapActionCore(JsonValue raw, Dictionary<string, double> attrs)
        {
            string type = NativeSkillSupport.Type(raw);
            Effect result = new Effect();
            if (type == "TActionAnd")
            {
                JsonValue actions = raw["Actions"];
                if (actions.Kind != JsonKind.Array || actions.Count == 0) throw new FormatException("复合动作为空");
                result.Kind = "composite";
                result.Effects = new List<Effect>();
                foreach (JsonValue child in actions.AsArray()) result.Effects.Add(MapAction(child, attrs));
                return result;
            }
            if (type == "TActionCardReload")
            {
                // 原生空值表示装到满；有值时按弹药数增加。
                result.Kind = "reload";
                result.Target = MapCardTarget(raw["Target"]);
                SetTargetCount(result, raw, attrs, "ReloadTargets");
                result.AmountValue = -1;
                if (!NativeSkillSupport.Empty(raw["Value"])) result.Amount = MapValue(raw["Value"], attrs, "ReloadAmount");
                return result;
            }
            if (type == "TActionCardFlyingStart")
            {
                if (!NativeSkillSupport.Empty(raw["Duration"])) throw new FormatException("限时飞行");
                result.Kind = "fly";
                result.Target = MapCardTarget(raw["Target"]);
                SetTargetCount(result, raw, attrs, "FlyingTargets");
                return result;
            }
            if (type == "TActionCardFlyingStop" || type == "TActionCardFlyingToggle")
            {
                result.Kind = type == "TActionCardFlyingStop" ? "flyStop" : "flyToggle";
                result.Target = MapCardTarget(raw["Target"]);
                SetTargetCount(result, raw, attrs, "FlyingTargets");
                return result;
            }
            if (type == "TActionCardBeginSandstorm")
            {
                // 沙暴开始事件与默认伤害/步进参数分开：这里只标记开始并派发
                // onSandstorm，不猜 DB 中不存在的伤害数值。
                foreach (KeyValuePair<string, JsonValue> pair in raw.AsObject())
                    if (pair.Key != "$type" && pair.Key != "Target") throw new FormatException("沙暴开始行动字段待支持 " + pair.Key);
                result.Kind = "beginSandstorm";
                result.Target = MapCardTarget(raw["Target"]);
                return result;
            }
            if (type == "TActionCardTransform")
            {
                // 只支持“变成场上已有道具”的复制变形；场外随机卡池（Groups/Behaviors）拒绝。
                if (!NativeSkillSupport.Empty(raw["Abilities"])) throw new FormatException("变形内嵌能力待支持");
                if (!NativeSkillSupport.Empty(raw["TargetCount"])) throw new FormatException("变形目标数量待支持");
                JsonValue duration = raw["Duration"];
                if (!NativeSkillSupport.Empty(duration) &&
                    (NativeSkillSupport.Type(duration) != "TDeterminantDuration" ||
                     NativeSkillSupport.Text(duration, "DurationType") != "UntilEndOfCombat"))
                    throw new FormatException("限时变形待支持");
                JsonValue spawn = raw["SpawnContext"];
                if (NativeSkillSupport.Type(spawn) != "TSpawnContextQuery") throw new FormatException("未知变形来源");
                ValidateSpawnContext(spawn);
                int copies = 1;
                if (!NativeSkillSupport.Empty(spawn["Limit"]))
                    copies = (int)JsMath.Round(ReadValue(spawn["Limit"], attrs, null));
                if (copies < 1 || copies > 10) throw new FormatException("变形副本数超出范围");
                result.Kind = "transform";
                result.Target = MapCardTarget(raw["Target"]);
                result.Count = copies;
                return result;
            }
            if (type == "TActionCardTransformDestroyed")
            {
                // 原生该动作挂在 TTriggerOnBeforeCardDestroyed 上：Target 是“本应被
                // 摧毁的卡”（Drone Crusher 为 TriggerTarget，Abducted Cow 为自身），
                // 不是施动者。SpawnContext 只给数量，供体由 NativeTransform 校准。
                if (!NativeSkillSupport.Empty(raw["Abilities"])) throw new FormatException("TransformDestroyed 内嵌能力暂不支持");
                if (!NativeSkillSupport.Empty(raw["TargetCount"])) throw new FormatException("TransformDestroyed 目标数量暂不支持");
                JsonValue duration = raw["Duration"];
                if (!NativeSkillSupport.Empty(duration) &&
                    (NativeSkillSupport.Type(duration) != "TDeterminantDuration" ||
                     NativeSkillSupport.Text(duration, "DurationType") != "UntilEndOfCombat"))
                    throw new FormatException("尚不支持 TransformDestroyed 持续时间");
                JsonValue spawn = raw["SpawnContext"];
                if (NativeSkillSupport.Type(spawn) != "TSpawnContextQuery") throw new FormatException("TransformDestroyed 缺少生成上下文");
                int copies = 1;
                if (!NativeSkillSupport.Empty(spawn["Limit"]))
                    copies = (int)JsMath.Round(ReadValue(spawn["Limit"], attrs, null));
                if (copies < 1 || copies > 10) throw new FormatException("TransformDestroyed 数量超出范围");
                result.Kind = "transformDestroyed";
                result.Target = MapCardTarget(raw["Target"]);
                result.Count = copies;
                return result;
            }
            if (type == "TActionPlayerBurnRemove" || type == "TActionPlayerPoisonRemove")
            {
                result.Kind = type == "TActionPlayerBurnRemove" ? "burnRemove" : "poisonRemove";
                result.Target = MapHeroTarget(raw["Target"]);
                string removeAttr = type == "TActionPlayerBurnRemove" ? "BurnRemoveAmount" : "PoisonRemoveAmount";
                if (!NativeSkillSupport.Empty(raw["ReferenceValue"]))
                    result.Amount = MapValue(raw["ReferenceValue"], attrs, null);
                else if (attrs != null && attrs.ContainsKey(removeAttr))
                    result.Amount = MapValue(JsonValue.Null, attrs, removeAttr);
                return result;
            }
            if (type == "TActionCardModifyAttribute")
            {
                string attribute = NativeSkillSupport.Text(raw, "AttributeType");
                string operation = NativeSkillSupport.Text(raw, "Operation");
                if (string.IsNullOrEmpty(attribute)) throw new FormatException("属性修改缺少属性名");
                if (operation != "Add" && operation != "Subtract" && operation != "Multiply" && operation != "Set")
                    throw new FormatException("尚不支持属性操作 " + operation);
                result.Kind = "modifyAttribute";
                result.Attribute = attribute;
                result.Operation = operation;
                result.Amount = MapValue(raw["Value"], attrs, null);
                result.Seconds = DurationSeconds(raw["Duration"]);
                result.Target = MapCardTarget(raw["Target"]);
                SetTargetCount(result, raw, attrs, "ModifyTargets");
                return result;
            }
            if (type == "TActionPlayerModifyAttribute")
            {
                string attribute = NativeSkillSupport.Text(raw, "AttributeType");
                string operation = NativeSkillSupport.Text(raw, "Operation");
                // 经济属性本轮 OUT。挂在战斗触发器上的同类动作必须跳过该动作，
                // 不能因经济效果回退整卡，也不能把它伪装成战斗属性。
                string[] economy = { "Gold", "Income", "Experience", "Prestige" };
                if (Array.IndexOf(economy, attribute) >= 0)
                {
                    result.Kind = "noop";
                    result.Attribute = attribute;
                    return result;
                }
                string[] supported = { "HealthMax", "HealthRegen", "RageMax", "EnragedDurationMax", "PercentDamageReduction", "Rage", "Tempo" };
                if (Array.IndexOf(supported, attribute) < 0) throw new FormatException("尚不支持人物属性 " + attribute);
                if (operation != "Add" && operation != "Subtract" && operation != "Multiply" && operation != "Set")
                    throw new FormatException("尚不支持人物属性操作 " + operation);
                result.Kind = "playerAttribute";
                result.Attribute = attribute;
                result.Operation = operation;
                result.Amount = MapValue(raw["Value"], attrs, null);
                result.Target = MapHeroTarget(raw["Target"]);
                if (!NativeSkillSupport.Empty(raw["Duration"])) result.Seconds = DurationSeconds(raw["Duration"]);
                return result;
            }
            if (type == "TActionPlayerRageApply")
            {
                result.Kind = "rage";
                result.Amount = MapValue(raw["ReferenceValue"], attrs, "RageApplyAmount");
                result.Target = MapHeroTarget(raw["Target"]);
                return result;
            }
            if (type == "TActionPlayerTempoApply")
            {
                result.Kind = "tempo";
                result.Amount = MapValue(raw["ReferenceValue"], attrs, "TempoApplyAmount");
                result.Target = MapHeroTarget(raw["Target"]);
                return result;
            }
            if (type == "TActionPlayerReviveHeal")
            {
                result.Kind = "revive";
                result.Amount = MapValue(raw["ReferenceValue"], attrs, "HealAmount");
                result.Target = MapHeroTarget(raw["Target"]);
                return result;
            }
            if (type == "TActionCardDestroy" || type == "TActionCardDisable" || type == "TActionCardRepair" || type == "TActionCardForceUse")
            {
                result.Kind = type == "TActionCardDestroy" ? "destroy" : type == "TActionCardDisable" ? "disable" :
                    type == "TActionCardRepair" ? "repair" : "forceUse";
                result.Target = MapCardTarget(raw["Target"]);
                string countAttr = type == "TActionCardDestroy" ? "DestroyTargets" :
                    type == "TActionCardDisable" ? "DisableTargets" : type == "TActionCardRepair" ? "RepairTargets" : "ForceUseTargets";
                SetTargetCount(result, raw, attrs, countAttr);
                return result;
            }
            if (type == "TActionCardAddTagsList" || type == "TActionCardAddTagsRandom" || type == "TActionCardAddTagsBySource")
            {
                result.Kind = type == "TActionCardAddTagsList" ? "addTags" :
                    type == "TActionCardAddTagsRandom" ? "addTagsRandom" : "addTagsBySource";
                result.Target = MapCardTarget(raw["Target"]);
                result.Tags = new List<string>();
                if (type != "TActionCardAddTagsBySource")
                {
                    if (raw["Tags"].Kind != JsonKind.Array || raw["Tags"].Count == 0) throw new FormatException("标签列表为空");
                    foreach (JsonValue tag in raw["Tags"].AsArray()) result.Tags.Add(tag.AsString());
                }
                else result.SourceTarget = MapCardTarget(raw["Source"]);
                SetTargetCount(result, raw, attrs, "AddTagsTargets");
                return result;
            }
            if (type == "TActionCardEnchant" || type == "TActionCardEnchantRandom" || type == "TActionCardEnchantRemove")
            {
                // 原生附魔时长只出现 null 或 UntilEndOfCombat；限时附魔需额外的到期移除。
                if (DurationSeconds(raw["Duration"]) > 0) throw new FormatException("限时附魔待支持");
                result.Kind = type == "TActionCardEnchant" ? "enchant" :
                    type == "TActionCardEnchantRandom" ? "enchantRandom" : "enchantRemove";
                result.Target = MapCardTarget(raw["Target"]);
                SetTargetCount(result, raw, attrs,
                    type == "TActionCardEnchantRemove" ? "EnchantRemoveTargets" : "EnchantTargets");
                result.PreventOverride = raw["PreventOverride"].AsBool();
                if (type == "TActionCardEnchant")
                {
                    string enchantment = NativeSkillSupport.Text(raw, "Enchantment");
                    if (string.IsNullOrEmpty(enchantment)) throw new FormatException("附魔名称缺失");
                    result.EnchantmentNames = new List<string> { enchantment };
                    result.EnchantmentWeights = new List<double> { 1 };
                }
                else if (type == "TActionCardEnchantRandom")
                {
                    JsonValue pool = raw["Enchantments"];
                    if (pool.Kind != JsonKind.Array || pool.Count == 0) throw new FormatException("附魔池为空");
                    result.EnchantmentNames = new List<string>();
                    result.EnchantmentWeights = new List<double>();
                    foreach (JsonValue entry in pool.AsArray())
                    {
                        string enchantment = NativeSkillSupport.Text(entry, "Enchantment");
                        if (string.IsNullOrEmpty(enchantment)) throw new FormatException("附魔池名称缺失");
                        double weight = entry["Weight"].AsDouble();
                        if (weight < 0) throw new FormatException("附魔权重无效");
                        result.EnchantmentNames.Add(enchantment);
                        result.EnchantmentWeights.Add(weight);
                    }
                }
                return result;
            }
            string attr = null;
            switch (type)
            {
                case "TActionPlayerDamage": result.Kind = "damage"; attr = "DamageAmount"; break;
                case "TActionPlayerHeal": result.Kind = "heal"; attr = "HealAmount"; break;
                case "TActionPlayerShieldApply": result.Kind = "shield"; attr = "ShieldApplyAmount"; break;
                case "TActionPlayerBurnApply": result.Kind = "burn"; attr = "BurnApplyAmount"; break;
                case "TActionPlayerPoisonApply": result.Kind = "poison"; attr = "PoisonApplyAmount"; break;
                case "TActionPlayerRegenApply": result.Kind = "regen"; attr = "RegenApplyAmount"; break;
                case "TActionCardCharge": result.Kind = "charge"; attr = "ChargeAmount"; break;
                case "TActionCardHaste": result.Kind = "haste"; attr = "HasteAmount"; break;
                case "TActionCardSlow": result.Kind = "slow"; attr = "SlowAmount"; break;
                case "TActionCardFreeze": result.Kind = "freeze"; attr = "FreezeAmount"; break;
                default: throw new FormatException("尚不支持行动 " + type);
            }
            bool control = type.StartsWith("TActionCard", StringComparison.Ordinal);
            if (!control)
            {
                if (!DurationSupported(raw["Duration"])) throw new FormatException("尚不支持效果持续时间");
                // Nanobot Orange 的 DamageAmount 由本卡光环在运行时提供；模板期
                // 没有该键时不能把卡判死，生成读自身运行时属性的表达式。
                bool runtimeDefault = type == "TActionPlayerDamage" &&
                    NativeSkillSupport.Empty(raw["ReferenceValue"]);
                result.Amount = MapValue(raw["ReferenceValue"], attrs, attr, runtimeDefault);
                result.Target = MapHeroTarget(raw["Target"]);
            }
            else
            {
                result.DurationValue = MapValue(raw["Value"], attrs, attr);
                result.Seconds = result.DurationValue.Value / 1000.0;
                result.Target = MapCardTarget(raw["Target"]);
                string countAttr = attr.Replace("Amount", "Targets");
                SetTargetCount(result, raw, attrs, countAttr);
            }
            return result;
        }

        // 显式 TargetCount / XxxTargets 限制目标数量；只有两者都缺省时，
        // 全体目标才作用于全部。Panic 的 SelfHand 仍受 ReloadTargets 限制。
        private static void SetTargetCount(Effect effect, JsonValue raw, Dictionary<string, double> attrs, string countAttr)
        {
            if (!NativeSkillSupport.Empty(raw["TargetCount"]))
                effect.CountValue = MapValue(raw["TargetCount"], attrs, null);
            else if (countAttr != null && attrs != null && attrs.ContainsKey(countAttr))
                effect.CountValue = MapValue(JsonValue.Null, attrs, countAttr);
            else if (effect.Target.Kind != null && effect.Target.Kind.StartsWith("all", StringComparison.Ordinal) ||
                effect.Target.PositionMode != null && NativeSkillSupport.Type(raw) == "TActionCardModifyAttribute")
            {
                effect.Count = int.MaxValue;
                return;
            }
            else if (effect.Target.PositionMode != null)
            {
                // No explicit limit or target-count attribute means the full
                // positional set, e.g. Freefall Simulator's two neighbors.
                effect.Count = int.MaxValue;
                return;
            }
            if (effect.CountValue == null && effect.Target.Kind == "randomHandsItem")
            {
                // TTargetCardRandom + AllHands 且模板没有 XxxTargets 计数属性时，
                // 原生对双方棋盘所有匹配卡各生效一次：07f044ee 帧 60 气象机
                // SlowAmount 1000 命中双方 11 张卡，逐张产生一次 CardSlow。
                effect.Count = int.MaxValue;
                return;
            }
            effect.Count = effect.CountValue == null ? 1 : (int)Math.Max(0, effect.CountValue.Value);
        }

        private static bool DurationSupported(JsonValue duration)
        {
            if (NativeSkillSupport.Empty(duration)) return true;
            string type = NativeSkillSupport.Type(duration);
            return type == "TDeterminantDuration" && NativeSkillSupport.Text(duration, "DurationType") == "UntilEndOfCombat" ||
                type == "TCombatDuration";
        }

        // 0 表示整场战斗；TCombatDuration 返回具体秒数。
        internal static double DurationSeconds(JsonValue duration)
        {
            if (NativeSkillSupport.Empty(duration)) return 0;
            string type = NativeSkillSupport.Type(duration);
            if (type == "TCombatDuration")
            {
                double ms = duration["DurationInMs"].AsDouble();
                if (ms <= 0) throw new FormatException("无效持续时间");
                return ms / 1000.0;
            }
            if (type == "TDeterminantDuration" && NativeSkillSupport.Text(duration, "DurationType") == "UntilEndOfCombat") return 0;
            throw new FormatException("尚不支持效果持续时间 " + type);
        }

        internal static Target MapHeroTarget(JsonValue target)
        {
            if (!NativeSkillSupport.Empty(target["Conditions"])) throw new FormatException("尚不支持人物目标条件");
            string type = NativeSkillSupport.Type(target);
            string mode = NativeSkillSupport.Text(target, "TargetMode");
            if (type == "TTargetPlayerRelative")
            {
                if (mode == "Both") return new Target { Kind = "bothHeroes" };
                if (mode != "Self" && mode != "Opponent") throw new FormatException("尚不支持人物目标 " + mode);
                return new Target { Kind = mode == "Self" ? "selfHero" : "enemyHero" };
            }
            if (type == "TTargetPlayerAbsolute" || type == "TTargetPlayer")
            {
                if (mode == "Both") return new Target { Kind = "bothHeroes" };
                if (mode == "Opponent") return new Target { Kind = "enemyHero" };
                if (mode == "Self" || mode == "Player") return new Target { Kind = "selfHero" };
                throw new FormatException("尚不支持人物目标 " + mode);
            }
            throw new FormatException("尚不支持人物目标类型 " + type);
        }

        internal static Target MapCardTarget(JsonValue raw)
        {
            Target result = new Target();
            string type = NativeSkillSupport.Type(raw);
            string section = NativeSkillSupport.Text(raw, "TargetSection");
            bool self = NativeSkillSupport.RelativeSection(section, true);
            bool enemy = NativeSkillSupport.RelativeSection(section, false);
            bool absolutePlayer = NativeSkillSupport.AbsoluteSection(section, Side.Player);
            bool absoluteEnemy = NativeSkillSupport.AbsoluteSection(section, Side.Enemy);
            // 槽位占用目标只由 TCardSocketEffect 拥有；解析时要求来源是插槽效果，
            // 并命中同一槽位的物品，不能退化为 Self。
            if (type == "TTargetCardOccupying") result.Kind = "occupyingItem";
            else if (type == "TTargetCardTriggerSource") result.Kind = "triggerSourceItem";
            else if (type == "TTargetCardTriggerTarget") result.Kind = "triggerTargetItem";
            else if (type == "TTargetCardSelf") result.Kind = "selfItem";
            else if (type == "TTargetCardNeighbor") result.Kind = "randomAdjacentSelf";
            else if (type == "TTargetCardPositional")
            {
                string origin = NativeSkillSupport.Text(raw, "Origin");
                string mode = NativeSkillSupport.Text(raw, "TargetMode");
                if (origin != "Self" && origin != "TriggerSource") throw new FormatException("尚不支持位置原点 " + origin);
                if (!NativeSkillSupport.PositionModeSupported(mode)) throw new FormatException("尚不支持位置目标 " + mode);
                result.PositionMode = mode;
                result.PositionOrigin = origin;
                result.IncludeOrigin = raw["IncludeOrigin"].AsBool();
                bool fromTrigger = origin == "TriggerSource";
                if (mode == "Neighbor") result.Kind = fromTrigger ? "triggerSourceAdjacent" : "randomAdjacentSelf";
                else if (mode == "LeftCard") result.Kind = fromTrigger ? "triggerSourceLeft" : "leftSelf";
                else if (mode == "RightCard") result.Kind = fromTrigger ? "triggerSourceRight" : "rightSelf";
                else if (mode == "AllLeftCards" || mode == "AllRightCards") result.Kind = "allPositionItems";
                else throw new FormatException("尚不支持位置目标 " + origin + "/" + mode);
            }
            else if (type == "TTargetCardXMost")
            {
                if (!self && !enemy && !absolutePlayer && !absoluteEnemy) throw new FormatException("尚不支持卡牌目标区域 " + section);
                string mode = NativeSkillSupport.Text(raw, "TargetMode");
                bool right = mode == "RightMostCard";
                if (!right && mode != "LeftMostCard") throw new FormatException("尚不支持最左/最右目标 " + mode);
                result.Kind = (right ? "rightMost" : "leftMost") +
                    (self ? "Self" : enemy ? "Enemy" : absolutePlayer ? "Player" : "Opponent");
            }
            else if (section == "SelfNeighbors" && (type == "TTargetCardRandom" || type == "TTargetCardSection"))
            {
                result.Kind = "randomAdjacentSelf";
            }
            else if (section == "TriggerSourceNeighbors" && (type == "TTargetCardRandom" || type == "TTargetCardSection"))
            {
                // 相邻集合以触发来源（被使用的卡）为中心；随机目标在条件过滤后取样。
                result.Kind = type == "TTargetCardSection" ? "triggerSourceAdjacent" : "randomTriggerSourceAdjacent";
            }
            else if (type == "TTargetCardSection" || type == "TTargetCardRandom")
            {
                if (section == "AllHands" || section == "AllBoards")
                    result.Kind = type == "TTargetCardSection" ? "allHandsItems" : "randomHandsItem";
                else if (!self && !enemy && !absolutePlayer && !absoluteEnemy) throw new FormatException("尚不支持卡牌目标区域 " + section);
                else
                    result.Kind = type == "TTargetCardSection"
                        ? (self ? "allSelfItems" : enemy ? "allEnemyItems" : absolutePlayer ? "allPlayerItems" : "allOpponentItems")
                        : (self ? "randomSelfItem" : enemy ? "randomEnemyItem" : absolutePlayer ? "randomPlayerItem" : "randomOpponentItem");
            }
            else throw new FormatException("尚不支持卡牌目标 " + type);
            if (!NativeSkillSupport.ConditionSupported(raw["Conditions"])) throw new FormatException("尚不支持卡牌目标条件");
            result.NativeCondition = raw["Conditions"];
            result.ExcludeSelf = raw["ExcludeSelf"].AsBool();
            return result;
        }

        internal static double ReadValue(JsonValue raw, Dictionary<string, double> attrs, string defaultAttr)
        {
            double result;
            if (NativeSkillSupport.Empty(raw))
            {
                if (defaultAttr == null || !attrs.TryGetValue(defaultAttr, out result)) throw new FormatException("缺少属性 " + defaultAttr);
                return result;
            }
            string type = NativeSkillSupport.Type(raw);
            if (type == "TFixedValue" && raw["Value"].Kind == JsonKind.Number) result = raw["Value"].AsDouble();
            else if (type == "TReferenceValueAttributeChange") result = raw["DefaultValue"].Kind == JsonKind.Number ? raw["DefaultValue"].AsDouble() : 0;
            else if (type == "TReferenceValueCardAttribute" && NativeSkillSupport.Type(raw["Target"]) == "TTargetCardSelf" && NativeSkillSupport.Empty(raw["Target"]["Conditions"]))
            {
                string attribute = NativeSkillSupport.Text(raw, "AttributeType");
                if (attribute == null || !attrs.TryGetValue(attribute, out result)) throw new FormatException("缺少技能引用属性 " + attribute);
            }
            else throw new FormatException("尚不支持数值引用 " + type);
            JsonValue modifier = raw["Modifier"];
            if (!NativeSkillSupport.Empty(modifier))
            {
                double value = ReadValue(modifier["Value"], attrs, null);
                string mode = NativeSkillSupport.Text(modifier, "ModifyMode");
                if (mode == "Multiply") result *= value;
                else if (mode == "Add") result += value;
                else if (mode == "Subtract") result -= value;
                else throw new FormatException("尚不支持数值修正 " + mode);
                if (modifier["ShouldRound"].AsBool()) result = JsMath.Round(result);
            }
            if (double.IsInfinity(result) || double.IsNaN(result) || result < 0) throw new FormatException("无效技能数值");
            return result;
        }

        // 映射期只校验结构并在属性存在时锁定初值；正式数值在结算时重算，
        // 这样战斗中获得的属性加成会真正影响后续伤害/治疗。
        internal static NumExpr MapValue(JsonValue raw, Dictionary<string, double> attrs, string defaultAttr,
            bool allowRuntimeDefault = false)
        {
            if (NativeSkillSupport.Empty(raw))
            {
                if (defaultAttr == null) throw new FormatException("缺少数值引用");
                if (attrs == null || !attrs.ContainsKey(defaultAttr))
                {
                    if (!allowRuntimeDefault) throw new FormatException("缺少属性 " + defaultAttr);
                    // 值由本卡动态光环在运行时写入；模板期快照为 0。
                    return new NumExpr { Type = "cardAttribute", Attribute = defaultAttr, TargetKind = "selfItem", Value = 0 };
                }
                // Value 只是映射期快照（面板/测试可见），结算仍按运行时属性重算。
                return new NumExpr { Type = "cardAttribute", Attribute = defaultAttr, TargetKind = "selfItem", Value = attrs[defaultAttr] };
            }
            string type = NativeSkillSupport.Type(raw);
            NumExpr result;
            if (type == "TFixedValue" && raw["Value"].Kind == JsonKind.Number)
                result = new NumExpr { Type = "const", Value = raw["Value"].AsDouble() };
            else if (type == "TReferenceValueCardAttribute" || type == "TReferenceValueCardAttributeUnscaled")
            {
                string attribute = NativeSkillSupport.Text(raw, "AttributeType");
                if (string.IsNullOrEmpty(attribute)) throw new FormatException("数值引用缺少属性名");
                double fallback = raw["DefaultValue"].Kind == JsonKind.Number ? raw["DefaultValue"].AsDouble() : 0;
                NumExpr reference = new NumExpr
                {
                    Type = "cardAttribute",
                    Attribute = attribute,
                    Unscaled = type == "TReferenceValueCardAttributeUnscaled",
                    DefaultValue = fallback
                };
                JsonValue target = raw["Target"];
                if (NativeSkillSupport.Empty(target)) reference.TargetKind = "actionTargetItem";
                else
                {
                    string targetType = NativeSkillSupport.Type(target);
                    if (targetType == "TTargetCardTriggerSource") reference.TargetKind = "triggerSourceItem";
                    else if (targetType == "TTargetCardTriggerTarget") reference.TargetKind = "triggerTargetItem";
                    else
                    {
                        Target cardTarget = MapCardTarget(target);
                        // 无条件的自身引用走最轻路径；其余目标保留完整匹配条件。
                        if (cardTarget.Kind == "selfItem" && NativeSkillSupport.Empty(cardTarget.NativeCondition) && !cardTarget.ExcludeSelf)
                            reference.TargetKind = "selfItem";
                        else
                        {
                            reference.CardTarget = cardTarget;
                            reference.TargetKind = cardTarget.Kind;
                            reference.Condition = cardTarget.NativeCondition;
                            reference.Exclude = cardTarget.ExcludeSelf;
                        }
                    }
                }
                // 快照只用于面板；结算始终按运行时属性重算，缺失属性回退 DefaultValue。
                double snapshot = fallback, found;
                if (attrs != null && attrs.TryGetValue(attribute, out found)) snapshot = found;
                reference.Value = snapshot;
                result = reference;
            }
            else if (type == "TReferenceValuePlayerAttribute" || type == "TReferenceValuePlayerAttributeUnscaled")
            {
                string attribute = NativeSkillSupport.Text(raw, "AttributeType");
                if (string.IsNullOrEmpty(attribute)) throw new FormatException("人物属性引用缺少属性名");
                if (type == "TReferenceValuePlayerAttributeUnscaled" && attribute != "HealthMax")
                    throw new FormatException("尚不支持无缩放人物属性 " + attribute);
                if (!NativeSkillSupport.PlayerAttributeValueSupported(attribute))
                    throw new FormatException("尚不支持人物属性数值 " + attribute);
                string mode = NativeSkillSupport.Text(raw["Target"], "TargetMode");
                if (mode != "Self" && mode != "Player" && mode != "Opponent") throw new FormatException("尚不支持人物属性目标 " + mode);
                result = new NumExpr { Type = "playerAttribute", Attribute = attribute, Side = mode == "Opponent" ? "enemy" : "self" };
            }
            else if (type == "TReferenceValueCardTagCount")
            {
                Target target = MapCardTarget(raw["Target"]);
                result = new NumExpr { Type = "tagCount", CardTarget = target, TargetKind = target.Kind, Condition = target.NativeCondition, Distinct = raw["Distinct"].AsBool(), Exclude = target.ExcludeSelf };
            }
            else if (type == "TReferenceValueCardAttributeAggregate")
            {
                string attribute = NativeSkillSupport.Text(raw, "AttributeType");
                if (string.IsNullOrEmpty(attribute)) throw new FormatException("聚合属性缺少属性名");
                Target target = NativeSkillSupport.Empty(raw["Target"]) ? null : MapCardTarget(raw["Target"]);
                result = new NumExpr
                {
                    Type = "aggregate", Attribute = attribute, TargetKind = target == null ? "actionTargetItem" : target.Kind,
                    CardTarget = target, Condition = target == null ? null : target.NativeCondition,
                    Exclude = target != null && target.ExcludeSelf,
                    Unscaled = false,
                    DefaultValue = raw["DefaultValue"].Kind == JsonKind.Number ? raw["DefaultValue"].AsDouble() : 0
                };
            }
            else if (type == "TReferenceValueCardCount")
            {
                Target target = MapCardTarget(raw["Target"]);
                result = new NumExpr { Type = "cardCount", CardTarget = target, TargetKind = target.Kind, Condition = target.NativeCondition, Exclude = target.ExcludeSelf };
            }
            else if (type == "TRangeValue")
            {
                double min = raw["MinValue"].AsDouble(), max = raw["MaxValue"].AsDouble();
                if (max < min) throw new FormatException("无效取值范围");
                result = new NumExpr { Type = "range", Min = min, Max = max };
            }
            // 触发事件携带的变化量（如“失去的护盾值”）。没有 DefaultValue 时按 0。
            else if (type == "TReferenceValueAttributeChange")
            {
                double fallback = raw["DefaultValue"].Kind == JsonKind.Number ? raw["DefaultValue"].AsDouble() : 0;
                result = new NumExpr { Type = "attributeChange", Value = fallback };
            }
            else throw new FormatException("尚不支持数值引用 " + type);
            JsonValue modifier = raw["Modifier"];
            if (!NativeSkillSupport.Empty(modifier))
            {
                string mode = NativeSkillSupport.Text(modifier, "ModifyMode");
                if (mode != "Multiply" && mode != "Add" && mode != "Subtract") throw new FormatException("尚不支持数值修正 " + mode);
                result.Modifier = MapValue(modifier["Value"], attrs, null);
                result.ModifyMode = mode;
                result.ShouldRound = modifier["ShouldRound"].AsBool();
            }
            return result;
        }

        // G08 的 TSpawnContextQuery 不是场外随机卡池：Groups 描述的是场上供体
        // 筛选或明确的复制模板。只接受已取证的筛选、选择方式和行为；未知字段
        // 继续 fail-closed，避免把真正的随机卡池悄悄放进战斗模拟。
        private static void ValidateSpawnContext(JsonValue spawn)
        {
            foreach (KeyValuePair<string, JsonValue> pair in spawn.AsObject())
                if (pair.Key != "$type" && pair.Key != "Limit" && pair.Key != "Groups" &&
                    pair.Key != "SelectionMethod" && pair.Key != "Behaviors")
                    throw new FormatException("变形卡池待支持 " + pair.Key);
            ValidateSpawnMethod(spawn);
            ValidateSpawnLimit(spawn["Limit"]);
            ValidateSpawnBehaviors(spawn["Behaviors"]);

            JsonValue groups = spawn["Groups"];
            if (NativeSkillSupport.Empty(groups)) return;
            if (groups.Kind != JsonKind.Array || groups.Count != 1)
                throw new FormatException("变形供体组数量待支持");
            JsonValue group = groups[0];
            foreach (KeyValuePair<string, JsonValue> pair in group.AsObject())
                if (pair.Key != "$type" && pair.Key != "Filters" && pair.Key != "SelectionMethod" &&
                    pair.Key != "Limit" && pair.Key != "Prerequisites" && pair.Key != "RandomWeight" &&
                    pair.Key != "Behaviors")
                    throw new FormatException("变形供体组字段待支持 " + pair.Key);
            ValidateSpawnMethod(group);
            ValidateSpawnLimit(group["Limit"]);
            ValidateSpawnBehaviors(group["Behaviors"]);
            if (!NativeSkillSupport.Empty(group["Prerequisites"]) &&
                !NativeSkillSupport.PrerequisitesSupported(group["Prerequisites"]))
                throw new FormatException("变形供体前提待支持");
            if (!NativeSkillSupport.Empty(group["RandomWeight"]) &&
                group["RandomWeight"].Kind != JsonKind.Number)
                throw new FormatException("变形供体权重待支持");

            JsonValue filters = group["Filters"];
            if (filters.Kind != JsonKind.Array || filters.Count == 0)
                throw new FormatException("变形供体筛选待支持");
            foreach (JsonValue filter in filters.AsArray()) ValidateSpawnFilter(filter);
        }

        private static void ValidateSpawnMethod(JsonValue raw)
        {
            string method = NativeSkillSupport.Text(raw, "SelectionMethod");
            if (!string.IsNullOrEmpty(method) && method != "Sequential" && method != "Random")
                throw new FormatException("变形选择方式待支持 " + method);
        }

        private static void ValidateSpawnLimit(JsonValue raw)
        {
            if (NativeSkillSupport.Empty(raw)) return;
            if (NativeSkillSupport.Type(raw) != "TFixedValue" || raw["Value"].Kind != JsonKind.Number)
                throw new FormatException("变形数量待支持");
        }

        private static void ValidateSpawnBehaviors(JsonValue raw)
        {
            if (NativeSkillSupport.Empty(raw)) return;
            if (raw.Kind != JsonKind.Array) throw new FormatException("变形行为待支持");
            foreach (JsonValue behavior in raw.AsArray())
            {
                string type = NativeSkillSupport.Type(behavior);
                if (type != "TSpawnBehaviorIgnoreTierTable" && type != "TSpawnBehaviorAllowDuplicates" &&
                    type != "TSpawnBehaviorIgnoreHero" && type != "TSpawnBehaviorInheritTier" &&
                    type != "TSpawnBehaviorInheritEnchantment")
                    throw new FormatException("变形行为待支持 " + type);
            }
        }

        private static void ValidateSpawnFilter(JsonValue filter)
        {
            string type = NativeSkillSupport.Type(filter);
            if (type == "TSpawnFilterIdList")
            {
                JsonValue ids = filter["Ids"];
                if (ids.Kind != JsonKind.Array || ids.Count == 0)
                    throw new FormatException("变形模板列表待支持");
                foreach (JsonValue id in ids.AsArray())
                    if (string.IsNullOrEmpty(id.AsString()))
                        throw new FormatException("变形模板列表待支持");
                return;
            }
            if (type != "TSpawnFilterTarget") throw new FormatException("变形筛选待支持 " + type);
            JsonValue target = filter["Target"];
            string targetType = NativeSkillSupport.Type(target);
            if (targetType == "TTargetCardPositional")
            {
                if (NativeSkillSupport.Text(target, "Origin") != "Self" ||
                    !NativeSkillSupport.PositionModeSupported(NativeSkillSupport.Text(target, "TargetMode")))
                    throw new FormatException("变形位置待支持");
            }
            else if (targetType == "TTargetCardRandom" || targetType == "TTargetCardSection")
            {
                string section = NativeSkillSupport.Text(target, "TargetSection");
                if (section != "SelfHand" && section != "SelfBoard" && section != "OpponentHand" &&
                    section != "OpponentBoard" && section != "AllHands" && section != "AllBoards" && section != "SelfNeighbors")
                    throw new FormatException("变形区域待支持 " + section);
            }
            else if (targetType == "TTargetCardXMost")
            {
                string section = NativeSkillSupport.Text(target, "TargetSection");
                string mode = NativeSkillSupport.Text(target, "TargetMode");
                if (!NativeSkillSupport.SectionSupported(section) ||
                    mode != "LeftMostCard" && mode != "RightMostCard")
                    throw new FormatException("变形 XMost 筛选取待支持");
            }
            else throw new FormatException("变形筛选取待支持 " + targetType);
            if (!NativeSkillSupport.Empty(target["Conditions"]) &&
                !NativeSkillSupport.CardConditionSupported(target["Conditions"]))
                throw new FormatException("变形筛选条件待支持");
        }
    }

    public static class NativeSkillSupport
    {
        public static string Text(JsonValue raw, string key) { return raw != null && raw.Kind == JsonKind.Object ? raw[key].AsString() : null; }
        public static string Type(JsonValue raw) { return Text(raw, "$type"); }
        public static bool Empty(JsonValue raw) { return raw == null || raw.Kind == JsonKind.Null; }

        private static readonly Dictionary<string, string> TriggerEvents = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "TTriggerOnItemUsed", "onUse" },
            { "TTriggerOnBeforeItemUsed", "onBeforeUse" },
            { "TTriggerOnCardPerformedHaste", "onHaste" },
            { "TTriggerOnCardPerformedSlow", "onSlow" },
            { "TTriggerOnCardPerformedFreeze", "onFreeze" },
            { "TTriggerOnCardPerformedBurn", "onBurnApplied" },
            { "TTriggerOnCardPerformedPoison", "onPoisonApplied" },
            { "TTriggerOnCardPerformedShield", "onShield" },
            { "TTriggerOnCardPerformedHeal", "onHeal" },
            { "TTriggerOnCardPerformedRegen", "onRegen" },
            { "TTriggerOnCardPerformedReload", "onReload" },
            { "TTriggerOnCardPerformedCharge", "onCharge" },
            { "TTriggerOnCardPerformedOverHeal", "onOverHeal" },
            { "TTriggerOnCardPerformedDamage", "onDamageDealt" },
            { "TTriggerOnCardCritted", "onCrit" },
            { "TTriggerOnCardStartedFlying", "onFly" },
            { "TTriggerOnCardStartsFlying", "onFly" },
            { "TTriggerOnCardStoppedFlying", "onFlyStop" },
            { "TTriggerOnPlayerEnraged", "onPlayerEnraged" },
            { "TTriggerOnPlayerEnrageEnded", "onPlayerEnrageEnded" },
            { "TTriggerOnPlayerAttributeChanged", "onPlayerAttributeChanged" },
            { "TTriggerOnCardAttributeChanged", "onCardAttributeChanged" },
            { "TTriggerOnBeforeCardDestroyed", "onBeforeDestroy" },
            { "TTriggerOnCardPerformedDestruction", "onCardDestroyed" },
            { "TTriggerOnCardDisabled", "onCardDisabled" },
            { "TTriggerOnCardRepaired", "onCardRepaired" },
            { "TTriggerOnCardTransformed", "onCardTransformed" },
            { "TTriggerOnSandstorm", "onSandstorm" },
            { "TTriggerOnPlayerDied", "onPlayerDied" }
        };

        public static List<TriggerDef> MapTriggers(JsonValue raw)
        {
            List<TriggerDef> result = new List<TriggerDef>();
            string type = Type(raw);
            if (type == "TTriggerOr")
            {
                if (raw["Triggers"].Kind != JsonKind.Array || raw["Triggers"].Count == 0) throw new FormatException("触发组合为空");
                foreach (JsonValue child in raw["Triggers"].AsArray()) result.AddRange(MapTriggers(child));
                return result;
            }
            if (type == "TTriggerAnd") throw new FormatException("尚不支持触发组合 TTriggerAnd");
            result.Add(MapTrigger(raw));
            return result;
        }

        public static TriggerDef MapTrigger(JsonValue raw)
        {
            string type = Type(raw);
            TriggerDef result = new TriggerDef();
            result.SkillRule = new NativeSkillRule();
            if (type == "TTriggerOnFightStarted")
            {
                string combatType = Text(raw, "CombatType");
                if (!string.IsNullOrEmpty(combatType) && combatType != "PVE" && combatType != "PVP")
                    throw new FormatException("未知战斗类型条件 " + combatType);
                result.Event = "atCombatStart";
                return result;
            }
            string ev;
            if (!TriggerEvents.TryGetValue(type, out ev)) throw new FormatException("尚不支持触发器 " + type);
            result.Event = ev;
            if (type == "TTriggerOnPlayerAttributeChanged")
            {
                string attribute = Text(raw, "AttributeType");
                // 目前只有 Health / HealthMax / Shield / Rage 会发 onPlayerAttributeChanged。
                if (attribute != "Health" && attribute != "HealthMax" && attribute != "Shield" && attribute != "Rage" &&
                    attribute != "Burn" && attribute != "Poison" && attribute != "HealthRegen")
                    throw new FormatException("尚不支持人物属性变化 " + attribute);
                result.AttributeFilter = attribute;
                result.ChangeType = Text(raw, "ChangeType");
            }
            else if (type == "TTriggerOnCardAttributeChanged")
            {
                string attribute = Text(raw, "AttributeChanged");
                string[] supported = { "Haste", "Slow", "Freeze", "Flying", "Ammo", "CritChance", "RegenApplyAmount", "HealAmount", "ShieldApplyAmount", "DamageAmount", "BurnApplyAmount", "PoisonApplyAmount", "SellPrice" };
                if (Array.IndexOf(supported, attribute) < 0) throw new FormatException("尚不支持卡牌属性变化 " + attribute);
                result.AttributeFilter = attribute;
                result.ChangeType = Text(raw, "ChangeType");
            }
            result.SkillRule = MapSubject(raw["Subject"]);
            if (IsPlayerEvent(type) && result.SkillRule.PlayerOwner == null)
                throw new FormatException("人物事件的触发主体必须是英雄");
            MapTriggerFilters(result.SkillRule, raw, type, result.Event);
            return result;
        }

        // Trigger.Target 是行动受体，Trigger.Source 是造成影响的行动者。
        // 两者都只在已确认角色语义的事件上映射，其余 fail-closed。
        private static void MapTriggerFilters(NativeSkillRule rule, JsonValue raw, string type, string ev)
        {
            bool heroRecipient = ev == "onBurnApplied" || ev == "onPoisonApplied" || ev == "onRegen" ||
                ev == "onHeal" || ev == "onShield" || ev == "onOverHeal" || ev == "onDamageDealt";
            bool cardRecipient = ev == "onHaste" || ev == "onSlow" || ev == "onFreeze" || ev == "onReload";
            // 修理/变形/摧毁前的 Subject 是被影响的卡，Source 才能表示行动者。
            bool affectedCard = ev == "onBeforeDestroy" || ev == "onCardRepaired" ||
                ev == "onCardDisabled" || ev == "onCardTransformed";
            if (!Empty(raw["Target"]))
            {
                if (type == "TTriggerOnCardPerformedDestruction")
                    throw new FormatException("尚不支持摧毁后的指定受体");
                if (heroRecipient) MapPlayerTriggerTarget(rule, raw["Target"]);
                else if (cardRecipient) MapCardTriggerTarget(rule, raw["Target"]);
                else throw new FormatException("尚不支持指定行动受体的被动");
            }
            if (!Empty(raw["Source"]))
            {
                if (!affectedCard) throw new FormatException("尚不支持触发来源限制");
                MapTriggerActor(rule, raw["Source"]);
            }
        }

        private static void MapCardTriggerTarget(NativeSkillRule rule, JsonValue target)
        {
            string type = Type(target);
            if (type == "TTargetCardSelf")
            {
                rule.TargetOwner = "self";
                rule.TargetSelf = true;
            }
            else if (type == "TTargetCardOccupying")
            {
                rule.TargetOwner = "self";
                rule.TargetOccupying = true;
            }
            else if (type == "TTargetCardSection")
            {
                MapSectionOwner(Text(target, "TargetSection"), "行动受体", out rule.TargetOwner);
                rule.TargetOther = target["ExcludeSelf"].AsBool();
            }
            else if (type == "TTargetCardPositional" && Text(target, "Origin") == "Self" &&
                PositionModeSupported(Text(target, "TargetMode")))
            {
                rule.TargetOwner = "self";
                rule.TargetPositionMode = Text(target, "TargetMode");
                rule.TargetIncludeOrigin = target["IncludeOrigin"].AsBool();
            }
            else throw new FormatException("尚不支持行动受体 " + type);
            rule.TargetCondition = target["Conditions"];
            if (!CardConditionSupported(rule.TargetCondition)) throw new FormatException("未知行动受体条件");
        }

        private static void MapPlayerTriggerTarget(NativeSkillRule rule, JsonValue target)
        {
            if (Type(target) != "TTargetPlayerRelative") throw new FormatException("尚不支持英雄行动受体 " + Type(target));
            string mode = Text(target, "TargetMode");
            if (mode != "Self" && mode != "Player" && mode != "Opponent") throw new FormatException("尚不支持英雄行动受体 " + mode);
            rule.TargetIsPlayer = true;
            rule.TargetPlayerOwner = mode == "Opponent" ? "enemy" : "self";
            rule.TargetPlayerCondition = target["Conditions"];
            if (!PlayerConditionSupported(rule.TargetPlayerCondition)) throw new FormatException("尚不支持英雄行动受体条件");
        }

        private static void MapTriggerActor(NativeSkillRule rule, JsonValue source)
        {
            string type = Type(source);
            if (type == "TTargetCardSelf")
            {
                rule.ActorOwner = "self";
                rule.ActorSelf = true;
            }
            else if (type == "TTargetCardOccupying")
            {
                rule.ActorOwner = "self";
                rule.ActorOccupying = true;
            }
            else if (type == "TTargetCardSection")
            {
                MapSectionOwner(Text(source, "TargetSection"), "触发来源", out rule.ActorOwner);
                rule.ActorOther = source["ExcludeSelf"].AsBool();
            }
            else if (type == "TTargetCardPositional" && Text(source, "Origin") == "Self" &&
                PositionModeSupported(Text(source, "TargetMode")))
            {
                rule.ActorOwner = "self";
                rule.ActorPositionMode = Text(source, "TargetMode");
                rule.ActorIncludeOrigin = source["IncludeOrigin"].AsBool();
            }
            else throw new FormatException("尚不支持触发来源 " + type);
            rule.ActorCondition = source["Conditions"];
            if (!CardConditionSupported(rule.ActorCondition)) throw new FormatException("未知触发来源条件");
        }

        private static void MapSectionOwner(string section, string what, out string owner)
        {
            owner = SectionOwner(section, what);
        }

        private static bool IsPlayerEvent(string type)
        {
            return type == "TTriggerOnPlayerEnraged" || type == "TTriggerOnPlayerEnrageEnded" ||
                type == "TTriggerOnPlayerAttributeChanged" || type == "TTriggerOnPlayerDied";
        }

        public static NativeSkillRule MapSubject(JsonValue subject)
        {
            NativeSkillRule rule = new NativeSkillRule();
            if (Empty(subject)) return rule;
            string subjectType = Type(subject);
            if (subjectType == "TTargetPlayerRelative" || subjectType == "TTargetPlayerAbsolute")
            {
                string mode = Text(subject, "TargetMode");
                if (mode != "Self" && mode != "Player" && mode != "Opponent") throw new FormatException("尚不支持人物触发主体 " + mode);
                rule.PlayerOwner = mode == "Opponent" ? "enemy" : "self";
                rule.PlayerCondition = subject["Conditions"];
                if (!PlayerConditionSupported(rule.PlayerCondition)) throw new FormatException("尚不支持人物触发条件");
                return rule;
            }
            if (subjectType == "TTargetCardSelf")
            {
                rule.SourceOwner = "self";
                rule.SourceSelf = true;
            }
            else if (subjectType == "TTargetCardOccupying")
            {
                rule.SourceOwner = "self";
                rule.SourceOccupying = true;
            }
            else if (subjectType == "TTargetCardSection")
            {
                rule.SourceOwner = SectionOwner(Text(subject, "TargetSection"), "触发");
                rule.SourceOther = subject["ExcludeSelf"].AsBool();
            }
            else if (subjectType == "TTargetCardPositional" && Text(subject, "Origin") == "Self" &&
                PositionModeSupported(Text(subject, "TargetMode")))
            {
                rule.SourceOwner = "self";
                rule.SourcePositionMode = Text(subject, "TargetMode");
                rule.IncludeOrigin = subject["IncludeOrigin"].AsBool();
            }
            else if (subjectType == "TTargetCardTriggerSource") rule.SourceOwner = "any";
            else if (subjectType == "TTargetCardXMost")
            {
                string section = Text(subject, "TargetSection");
                string mode = Text(subject, "TargetMode");
                if (mode != "LeftMostCard" && mode != "RightMostCard") throw new FormatException("尚不支持最值触发主体 " + mode);
                rule.SourceOwner = SectionOwner(section, "触发");
                rule.SourceXMostTarget = SkillMapper.MapCardTarget(subject);
            }
            else throw new FormatException("未知触发主体 " + subjectType);
            rule.SourceCondition = subject["Conditions"];
            if (!CardConditionSupported(rule.SourceCondition)) throw new FormatException("未知触发主体条件");
            return rule;
        }

        private static readonly string[] SupportedAttributes = { "CooldownMax", "Flying", "DamageAmount", "HealAmount", "ShieldApplyAmount", "BurnApplyAmount", "PoisonApplyAmount", "RegenApplyAmount", "CritChance", "AmmoMax", "Multicast" };
        private static readonly string[] SupportedTiers = { "Bronze", "Silver", "Gold", "Diamond", "Legendary" };
        private static readonly string[] Comparisons = { "Equal", "NotEqual", "LessThan", "LessThanOrEqual", "GreaterThan", "GreaterThanOrEqual" };

        // 条件比较值可以是固定值，也可以引用被判定卡自己的属性
        // （例：Frozen Secrets 的 Custom_0 < Custom_1）。其他引用目标 fail-closed。
        public static bool CardComparisonValueSupported(JsonValue raw)
        {
            if (Empty(raw)) return false;
            string type = Type(raw);
            if (type == "TFixedValue") return raw["Value"].Kind == JsonKind.Number;
            if (type != "TReferenceValueCardAttribute") return false;
            JsonValue target = raw["Target"];
            if (Type(target) != "TTargetCardSelf" || !Empty(target["Conditions"])) return false;
            if (string.IsNullOrEmpty(Text(raw, "AttributeType"))) return false;
            JsonValue modifier = raw["Modifier"];
            if (Empty(modifier)) return true;
            string mode = Text(modifier, "ModifyMode");
            return (mode == "Multiply" || mode == "Add") && CardComparisonValueSupported(modifier["Value"]);
        }

        public static double CardComparisonValue(InternalItem item, JsonValue raw)
        {
            if (Empty(raw)) return 0;
            string type = Type(raw);
            if (type == "TFixedValue") return raw["Value"].AsDouble();
            if (type != "TReferenceValueCardAttribute") return 0;
            double value = item == null ? 0 : AttributeValue(item, Text(raw, "AttributeType"));
            JsonValue modifier = raw["Modifier"];
            if (!Empty(modifier))
            {
                double other = CardComparisonValue(item, modifier["Value"]);
                value = Text(modifier, "ModifyMode") == "Add" ? value + other : value * other;
                if (modifier["ShouldRound"].AsBool()) value = JsMath.Round(value);
            }
            return value;
        }

        // 卡牌条件允许任意属性名，但比较前必须确实具有该属性。
        // 引用表达式的 DefaultValue 不适用于目标条件：缺失不等于数值 0。
        public static bool CardConditionSupported(JsonValue raw)
        {
            if (Empty(raw)) return true;
            string type = Type(raw);
            if (type == "TCardConditionalAttribute")
            {
                string attr = Text(raw, "Attribute");
                string op = Text(raw, "ComparisonOperator");
                return !string.IsNullOrEmpty(attr) && Array.IndexOf(Comparisons, op) >= 0 &&
                    CardComparisonValueSupported(raw["ComparisonValue"]);
            }
            if (type == "TCardConditionalTier")
            {
                if (raw["Tiers"].Kind != JsonKind.Array || raw["Tiers"].Count == 0) return false;
                foreach (JsonValue tier in raw["Tiers"].AsArray())
                    if (Array.IndexOf(SupportedTiers, tier.AsString()) < 0) return false;
                return true;
            }
            if (type == "TCardConditionalId") return !string.IsNullOrEmpty(Text(raw, "Id"));
            if (type == "TCardConditionalState")
            {
                // 原生 ECardState：Alive=0 / Casting=1 / Disabled=2 / Transformed=3。
                // 战斗中只有 Alive/Disabled/Transformed 可观测；Casting 更细的时序
                // 无法从回放区分，保持 fail-closed。
                string stateOperator = Text(raw, "Operator");
                if (stateOperator != "Any" && stateOperator != "All" && stateOperator != "None") return false;
                if (raw["States"].Kind != JsonKind.Array || raw["States"].Count == 0) return false;
                foreach (JsonValue state in raw["States"].AsArray())
                {
                    string name = state.Kind == JsonKind.String ? state.AsString() : null;
                    if (name != "Alive" && name != "Disabled" && name != "Transformed") return false;
                }
                return true;
            }
            if (type == "TCardConditionalHasEnchantment")
                return Empty(raw["Enchantment"]) || raw["Enchantment"].Kind == JsonKind.String;
            if (type == "TCardConditionalEnchantmentEligible")
                return !string.IsNullOrEmpty(Text(raw, "Enchantment"));
            if (type == "TCardConditionalTriggerSource") return true;
            if (type == "TCardConditionalPlayerHero") return true;
            if (type == "TCardConditionalCanCrit" || type == "TCardConditionalSizeLargest" || type == "TCardConditionalSizeSmallest") return true;
            if (type == "TCardConditionalAttributeHighest" || type == "TCardConditionalAttributeLowest")
                return !string.IsNullOrEmpty(Text(raw, "AttributeType"));
            if (type == "TCardConditionalTag" || type == "TCardConditionalHiddenTag")
            {
                string op = Text(raw, "Operator");
                if (op != "Any" && op != "All" && op != "None" || raw["Tags"].Kind != JsonKind.Array) return false;
                foreach (JsonValue tag in raw["Tags"].AsArray()) if (tag.Kind != JsonKind.String) return false;
                return true;
            }
            if (type == "TCardConditionalSize")
            {
                if (raw["Sizes"].Kind != JsonKind.Array) return false;
                foreach (JsonValue size in raw["Sizes"].AsArray())
                    if (size.AsString() != "Small" && size.AsString() != "Medium" && size.AsString() != "Large") return false;
                return true;
            }
            if (type == "TCardConditionalType") return Text(raw, "CardType") == "Item" || Text(raw, "CardType") == "Skill";
            if (type == "TCardConditionalAnd" || type == "TCardConditionalOr")
            {
                if (raw["Conditions"].Kind != JsonKind.Array || raw["Conditions"].Count == 0) return false;
                foreach (JsonValue child in raw["Conditions"].AsArray()) if (!CardConditionSupported(child)) return false;
                return true;
            }
            return false;
        }

        // 兼容旧调用名；现有映射代码与测试都使用 ConditionSupported。
        public static bool ConditionSupported(JsonValue raw) { return CardConditionSupported(raw); }

        // 人物条件（TPlayerConditionalAttribute）。Health 使用当前生命，
        // Enraged 使用暴怒剩余时间，其余取卡牌所属英雄的原生属性。
        public static bool PlayerConditionSupported(JsonValue raw)
        {
            if (Empty(raw)) return true;
            if (Type(raw) != "TPlayerConditionalAttribute") return false;
            string attribute = Text(raw, "Attribute");
            string op = Text(raw, "ComparisonOperator");
            if (attribute != "Health" && attribute != "Enraged" && attribute != "HealthMax" && attribute != "Rage" &&
                attribute != "HealthRegen" && attribute != "Shield" && attribute != "Burn" && attribute != "Poison" &&
                attribute != "Gold" && attribute != "Income") return false;
            if (Array.IndexOf(Comparisons, op) < 0) return false;
            string valueType = Type(raw["ComparisonValue"]);
            if (valueType == "TFixedValue") return raw["ComparisonValue"]["Value"].Kind == JsonKind.Number;
            if (valueType == "TReferenceValueCardAttribute")
            {
                JsonValue target = raw["ComparisonValue"]["Target"];
                return !string.IsNullOrEmpty(Text(raw["ComparisonValue"], "AttributeType")) &&
                    Type(target) == "TTargetCardSelf" && Empty(target["Conditions"]);
            }
            return valueType == "TReferenceValuePlayerAttribute" && PlayerReferenceSupported(raw["ComparisonValue"]);
        }

        private static bool PlayerReferenceSupported(JsonValue raw)
        {
            if (string.IsNullOrEmpty(Text(raw, "AttributeType"))) return false;
            string type = Type(raw["Target"]);
            string mode = Text(raw["Target"], "TargetMode");
            bool targetSupported = type == "TTargetPlayerRelative"
                ? mode == "Self" || mode == "Player" || mode == "Opponent"
                : type == "TTargetPlayerAbsolute" && (mode == "Player" || mode == "Opponent");
            if (!targetSupported) return false;
            JsonValue modifier = raw["Modifier"];
            if (Empty(modifier)) return true;
            string modifyMode = Text(modifier, "ModifyMode");
            return (modifyMode == "Multiply" || modifyMode == "Add") &&
                Type(modifier["Value"]) == "TFixedValue" && modifier["Value"]["Value"].Kind == JsonKind.Number;
        }

        public static bool PlayerConditionMatches(BattleContext ctx, InternalHero hero, JsonValue raw, Side? ownerSide = null)
        {
            if (Empty(raw)) return true;
            if (ctx == null || hero == null) return false;
            if (Type(raw) == "TCardConditionalAnd" || Type(raw) == "TCardConditionalOr")
            {
                bool and = Type(raw) == "TCardConditionalAnd";
                foreach (JsonValue child in raw["Conditions"].AsArray())
                    if (PlayerConditionMatches(ctx, hero, child, ownerSide) != and) return !and;
                return and;
            }
            if (Type(raw) != "TPlayerConditionalAttribute") return false;
            string attribute = Text(raw, "Attribute");
            // 经济属性属于战斗外状态；ActionAxis 不建模金币/收入时，
            // 战斗内经济前提按“不满足”处理，不能让它错误触发战斗效果。
            if (attribute == "Gold" || attribute == "Income" || attribute == "Experience" || attribute == "Prestige")
                return false;
            double value = PlayerAttributeValue(ctx, hero, attribute);
            double threshold = ConditionValue(ctx, hero, raw["ComparisonValue"], ownerSide);
            string op = Text(raw, "ComparisonOperator");
            return op == "Equal" ? Math.Abs(value - threshold) < 1e-9 : op == "NotEqual" ? Math.Abs(value - threshold) >= 1e-9 :
                op == "LessThan" ? value < threshold : op == "LessThanOrEqual" ? value <= threshold :
                op == "GreaterThan" ? value > threshold : value >= threshold;
        }

        // 比较值可能引用人物属性（如“生命低于最大生命的 50%”）。
        // 引用的目标模式相对效果拥有者，而不是相对被判定英雄。
        private static double ConditionValue(BattleContext ctx, InternalHero hero, JsonValue raw, Side? ownerSide = null)
        {
            string type = Type(raw);
            if (type == "TFixedValue") return raw["Value"].AsDouble();
            if (type != "TReferenceValuePlayerAttribute" && type != "TReferenceValuePlayerAttributeUnscaled") return 0;
            Side owner = ownerSide ?? hero.Side;
            string mode = Text(raw["Target"], "TargetMode");
            Side targetSide = Type(raw["Target"]) == "TTargetPlayerAbsolute"
                ? mode == "Opponent" ? Side.Enemy : Side.Player
                : mode == "Opponent" ? SideUtil.Opposing(owner) : owner;
            double value = PlayerAttributeValue(ctx, ctx.Hero(targetSide), Text(raw, "AttributeType"));
            JsonValue modifier = raw["Modifier"];
            if (!Empty(modifier))
            {
                double other = ConditionValue(ctx, hero, modifier["Value"], ownerSide);
                value = Text(modifier, "ModifyMode") == "Add" ? value + other : value * other;
                if (modifier["ShouldRound"].AsBool()) value = JsMath.Round(value);
            }
            return value;
        }

        public static bool PlayerAttributeValueSupported(string attribute)
        {
            string[] known = { "Health", "HealthMax", "HealthRegen", "Shield", "Burn", "Poison", "Rage", "RageMax", "Enraged", "EnragedDurationMax", "PercentDamageReduction", "Gold", "Income", "Experience", "Prestige", "Level", "CritChance", "RerollCostModifier", "Tempo" };
            return Array.IndexOf(known, attribute) >= 0 ||
                !string.IsNullOrEmpty(attribute) && attribute.StartsWith("Custom_", StringComparison.Ordinal);
        }

        public static double PlayerAttributeValue(BattleContext ctx, InternalHero hero, string attribute)
        {            if (hero == null) return 0;
            if (attribute == "Health") return hero.Hp;
            if (attribute == "HealthMax") return hero.MaxHp;
            if (attribute == "HealthRegen") return hero.Regen;
            if (attribute == "Shield") return hero.Shield;
            if (attribute == "Burn") return hero.Burn;
            if (attribute == "Poison") return hero.Poison;
            if (attribute == "Rage") return hero.Rage;
            // Tempo 是战斗内资源（Black Lipstick / 音符插槽累积），不走人物属性快照。
            if (attribute == "Tempo") return hero.Tempo;
            if (attribute == "RageMax") return hero.RageMax;
            if (attribute == "EnragedDurationMax") return hero.EnragedDurationMs;
            // 暴怒只作为布尔读取；到期时战斗循环会把 EnragedUntil 置为 -1。
            if (attribute == "Enraged") return hero.EnragedUntil > 0 ? 1 : 0;
            // Custom_N/Level/CritChance/RerollCostModifier 等使用原生人物
            // 属性快照；CritChance 还叠加动态人物光环。
            double raw;
            if (hero.Attributes != null && hero.Attributes.TryGetValue(attribute, out raw))
                return raw + (attribute == "CritChance" ? hero.AuraCritChance : 0);
            if (attribute == "CritChance") return hero.AuraCritChance;
            // Gold/Income 等经济属性不进入战斗账本；未提供运行时值按 0。
            return 0;
        }

        public static double AttributeValue(InternalItem item, string attribute, BattleContext ctx = null)
        {
            if (item == null) return 0;
            // 弹药是 InternalItem 的运行时状态，不总在 LiveAttributes 里。
            if (attribute == "Ammo") return item.Ammo;
            if (attribute == "AmmoMax") return item.AmmoMax.HasValue ? item.AmmoMax.Value : 0;
            // 时效状态不是普通属性表项；条件只关心“当前是否仍生效”。
            if (attribute == "Haste" || attribute == "Slow" || attribute == "Freeze")
            {
                if (ctx == null) return 0;
                double until = attribute == "Haste" ? item.HasteUntil : attribute == "Slow" ? item.SlowUntil : item.FrozenUntil;
                return until > ctx.Now ? 1 : 0;
            }
            // 映射期光环先于 RefreshAttributes；ItemAttributes 会优先读 LiveAttributes。
            return ItemAttributes.Get(item, attribute);
        }

        public static string ItemTemplateId(CardDef card)
        {
            if (!string.IsNullOrEmpty(card.BaseItemId)) return card.BaseItemId;
            if (string.IsNullOrEmpty(card.Id)) return null;
            return card.Id.Split('@')[0].Split('#')[0];
        }

        // Matches the native TCardConditionalCanCrit: capability comes from an
        // active numerical ability, not from already having positive CritChance.
        private static bool CanCrit(InternalItem item)
        {
            if (item == null || item.Card == null) return false;
            CardDef card = item.Card;
            JsonValue enchantment = card.NativeEnchantment ?? JsonValue.Null;
            if (card.HiddenTags.Contains("CanCrit")) return true;
            // 无附魔时 NativeEnchantment 是 Null，索引器只能读对象。
            if (!Empty(enchantment))
                foreach (JsonValue tag in enchantment["HiddenTags"].AsArray())
                    if (tag.AsString() == "CanCrit") return true;
            if (item.IsSkill) return false;
            if (!Empty(card.NativeTemplate))
            {
                if (Type(card.NativeTemplate) != "TCardItem") return false;
                JsonValue ids = JsonValue.Null;
                string lookupTier = card.Tier == "Legendary" ? "Diamond" : card.Tier;
                foreach (string tier in new[] { "Bronze", "Silver", "Gold", "Diamond", "Legendary" })
                {
                    JsonValue row = card.NativeTemplate["Tiers"][tier];
                    if (!Empty(row) && row["AbilityIds"].Kind == JsonKind.Array) ids = row["AbilityIds"];
                    if (tier == lookupTier) break;
                }
                foreach (JsonValue id in ids.AsArray())
                    if (CritAbility(card.NativeTemplate["Abilities"][id.AsString()])) return true;
                if (!Empty(enchantment))
                    foreach (KeyValuePair<string, JsonValue> ability in enchantment["Abilities"].AsObject())
                        if (CritAbility(ability.Value)) return true;
                return false;
            }
            // Legacy cards have active effects and passive triggers separated.
            // This also allows an active card to receive its first crit bonus.
            foreach (Effect effect in card.Effects)
                if (effect.Kind == "damage" || effect.Kind == "shield" || effect.Kind == "heal" ||
                    effect.Kind == "burn" || effect.Kind == "poison" || effect.Kind == "regen") return true;
            return false;
        }

        private static bool CritAbility(JsonValue ability)
        {
            if (Empty(ability)) return false;
            if (Type(ability["Trigger"]) != "TTriggerOnCardFired") return false;
            string action = Type(ability["Action"]);
            return action == "TActionPlayerDamage" || action == "TActionPlayerShieldApply" ||
                action == "TActionPlayerHeal" || action == "TActionPlayerReviveHeal" ||
                action == "TActionPlayerBurnApply" || action == "TActionPlayerPoisonApply" ||
                action == "TActionPlayerRegenApply";
        }

        public static bool CardMatches(InternalItem item, JsonValue raw, BattleContext ctx = null, string triggerSourceItemId = null)
        {
            if (Empty(raw)) return true;
            string type = Type(raw);
            if (type == "TCardConditionalAttribute")
            {
                string attribute = Text(raw, "Attribute");
                // Timed state flags are implicit on every item. Ordinary numeric
                // attributes must exist: Carving Fork's DamageAmount >= 0 targets
                // a zero-damage weapon, but cannot create damage on a utility item.
                bool state = attribute == "Haste" || attribute == "Slow" || attribute == "Freeze" ||
                    attribute == "Heated" || attribute == "Chilled";
                bool ammo = (attribute == "Ammo" || attribute == "AmmoMax") && item != null && item.AmmoMax.HasValue;
                if (!state && !ammo && !ItemAttributes.Has(item, attribute)) return false;
                double value = AttributeValue(item, attribute, ctx);
                double threshold = CardComparisonValue(item, raw["ComparisonValue"]);
                string op = Text(raw, "ComparisonOperator");
                return op == "Equal" ? value == threshold : op == "NotEqual" ? value != threshold : op == "LessThan" ? value < threshold :
                    op == "LessThanOrEqual" ? value <= threshold : op == "GreaterThan" ? value > threshold : op == "GreaterThanOrEqual" && value >= threshold;
            }
            if (type == "TCardConditionalTriggerSource")
            {
                // 没有触发来源时（静态光环），“不是触发源”恒为真。
                bool isSource = !string.IsNullOrEmpty(triggerSourceItemId) && item.Id == triggerSourceItemId;
                return isSource != raw["IsNot"].AsBool();
            }
            if (type == "TCardConditionalCanCrit")
            {
                return CanCrit(item) != raw["IsNot"].AsBool();
            }
            if (type == "TCardConditionalTier")
            {
                bool contains = false;
                foreach (JsonValue tier in raw["Tiers"].AsArray())
                    if (string.Equals(tier.AsString(), item.Card.Tier, StringComparison.OrdinalIgnoreCase)) contains = true;
                return contains != raw["IsNot"].AsBool();
            }
            if (type == "TCardConditionalId")
            {
                string expected = Text(raw, "Id");
                bool matches = string.Equals(ItemTemplateId(item.Card), expected, StringComparison.OrdinalIgnoreCase);
                return matches != raw["IsNot"].AsBool();
            }
            if (type == "TCardConditionalState")
            {
                string stateOperator = Text(raw, "Operator");
                bool any = false, all = true, seen = false;
                foreach (JsonValue state in raw["States"].AsArray())
                {
                    string name = state.Kind == JsonKind.String ? state.AsString() : null;
                    bool matches = name == "Alive" ? item != null && !item.Destroyed && !item.Disabled :
                        name == "Disabled" ? item != null && !item.Destroyed && item.Disabled :
                        name == "Transformed" && item != null && item.Generation > 0;
                    seen = true;
                    any |= matches;
                    all &= matches;
                }
                bool result = !seen ? false : stateOperator == "All" ? all : stateOperator == "None" ? !any : any;
                return result != raw["IsNot"].AsBool();
            }
            if (type == "TCardConditionalHasEnchantment")
            {
                // 原生不带名字时：IsNot=false 表示“尚无附魔”（附魔动作的目标），
                // IsNot=true 表示“已有任意附魔”（Spirit Diffuser 的附魔被动）。
                // 证据：Spirit Diffuser Abilities/0,1 的目标用 null/false，
                // Abilities/2 与被附魔后的被动用 null/true。
                string filter = Text(raw, "Enchantment");
                bool enchanted = !string.IsNullOrEmpty(item.Card.EnchantmentName);
                bool isNot = raw["IsNot"].AsBool();
                if (filter == null) return enchanted == isNot;
                return string.Equals(filter, item.Card.EnchantmentName, StringComparison.OrdinalIgnoreCase) != isNot;
            }
            if (type == "TCardConditionalEnchantmentEligible")
            {
                // 附魔资格由附魔定义与卡牌模板共同决定；未附魔或已是同名附魔都可安全接受。
                string filter = Text(raw, "Enchantment");
                if (string.IsNullOrEmpty(filter) || item.Card == null) return false;
                bool same = string.Equals(filter, item.Card.EnchantmentName, StringComparison.OrdinalIgnoreCase);
                bool available = string.IsNullOrEmpty(item.Card.EnchantmentName) || same;
                if (!available) return false;
                JsonValue template = item.Card.NativeTemplate;
                return !Empty(template) && template["Enchantments"].Kind == JsonKind.Object &&
                    template["Enchantments"].Has(filter);
            }
            if (type == "TCardConditionalPlayerHero")
            {
                // “另一个英雄的物品”：Common 卡属全英雄通用，算作本方英雄。
                // 读不到英雄身份时按不匹配处理，并在面板提示一次。
                string hero = ctx == null ? null
                    : item.Side == Side.Player ? ctx.Config.PlayerHero : ctx.Config.EnemyHero;
                bool same;
                if (string.IsNullOrEmpty(hero) || item.Card.Heroes.Count == 0)
                {
                    // 未知时按“本方英雄”处理：宁可少算“另一个英雄”的加成。
                    same = true;
                    if (ctx != null && !ctx.Warnings.Contains("缺少英雄身份，英雄条件按本方处理"))
                        ctx.Warnings.Add("缺少英雄身份，英雄条件按本方处理");
                }
                else
                {
                    same = item.Card.Heroes.Contains("Common") ||
                        item.Card.Heroes.Exists(delegate(string value) { return string.Equals(value, hero, StringComparison.OrdinalIgnoreCase); });
                }
                return same == raw["IsSameAsPlayerHero"].AsBool();
            }
            if (type == "TCardConditionalAttributeHighest" || type == "TCardConditionalAttributeLowest")
            {
                // 同侧排行；并列都算最高/最低。比较集合是当前战场同侧全部卡。
                if (ctx == null) return false;
                string attribute = Text(raw, "AttributeType");
                double value = AttributeValue(item, attribute, ctx);
                bool highest = type == "TCardConditionalAttributeHighest";
                foreach (InternalItem other in ctx.Items)
                {
                    if (other.Side != item.Side || other.Id == item.Id) continue;
                    double candidate = AttributeValue(other, attribute, ctx);
                    if (highest ? candidate > value : candidate < value) return false;
                }
                return true;
            }
            if (type == "TCardConditionalSizeLargest" || type == "TCardConditionalSizeSmallest")
            {
                if (ctx == null) return false;
                int rank = ItemAttributes.SizeRank(item.Card.Size);
                if (rank < 0) return false;
                bool largest = type == "TCardConditionalSizeLargest";
                foreach (InternalItem other in ctx.Items)
                {
                    if (other.Side != item.Side || other.Id == item.Id) continue;
                    int otherRank = ItemAttributes.SizeRank(other.Card.Size);
                    if (otherRank < 0) continue;
                    if (largest ? otherRank > rank : otherRank < rank) return false;
                }
                return true;
            }
            if (type == "TCardConditionalTag" || type == "TCardConditionalHiddenTag")
            {
                bool hidden = type == "TCardConditionalHiddenTag";
                int matches = 0;
                foreach (JsonValue tag in raw["Tags"].AsArray())
                {
                    string name = tag.AsString();
                    bool present = hidden ? item.Card.HiddenTags.Contains(name) : ItemAttributes.HasTag(item, name);
                    if (present) matches++;
                }
                string op = Text(raw, "Operator");
                return op == "Any" ? matches > 0 : op == "All" ? matches == raw["Tags"].Count : matches == 0;
            }
            if (type == "TCardConditionalSize")
            {
                bool contains = false;
                foreach (JsonValue size in raw["Sizes"].AsArray()) if (size.AsString() == item.Card.Size) contains = true;
                return contains != raw["IsNot"].AsBool();
            }
            if (type == "TCardConditionalType")
            {
                bool isItem = item.Card != null && !item.IsSkill;
                bool matches = Text(raw, "CardType") == "Item" ? isItem : !isItem;
                return matches != raw["IsNot"].AsBool();
            }
            if (type == "TCardConditionalAnd" || type == "TCardConditionalOr")
            {
                bool and = type == "TCardConditionalAnd";
                foreach (JsonValue child in raw["Conditions"].AsArray())
                    if (CardMatches(item, child, ctx, triggerSourceItemId) != and) return !and;
                return and;
            }
            return false;
        }

        // 人物前提（生命/怒气比较）会随战斗变化，静态光环不能当作开场快照。
        internal static bool HasPlayerPrerequisite(JsonValue raw)
        {
            if (Empty(raw) || raw.Kind != JsonKind.Array) return false;
            foreach (JsonValue requirement in raw.AsArray())
                if (Type(requirement) == "TPrerequisitePlayer") return true;
            return false;
        }

        public static bool PrerequisitesSupported(JsonValue raw)
        {
            if (Empty(raw)) return true;
            if (raw.Kind != JsonKind.Array) return false;
            foreach (JsonValue requirement in raw.AsArray())
            {
                string type = Type(requirement);
                if (type == "TPrerequisitePlayer")
                {
                    JsonValue playerSubject = requirement["Subject"];
                    string playerType = Type(playerSubject);
                    if (playerType != "TTargetPlayerRelative" && playerType != "TTargetPlayerAbsolute") return false;
                    string playerMode = Text(playerSubject, "TargetMode");
                    if (playerMode != "Self" && playerMode != "Player" && playerMode != "Opponent") return false;
                    if (!PlayerConditionSupported(playerSubject["Conditions"])) return false;
                    continue;
                }
                if (type == "TPrerequisiteCardAttributeComparator")
                {
                    JsonValue subject = requirement["Subject"];
                    if (Type(subject) != "TTargetCardSelf" || !CardConditionSupported(subject["Conditions"])) return false;
                    if (string.IsNullOrEmpty(Text(requirement, "Attribute")) || string.IsNullOrEmpty(Text(requirement, "AttributeOther"))) return false;
                    if (Array.IndexOf(Comparisons, Text(requirement, "Comparison")) < 0) return false;
                    continue;
                }
                if (type != "TPrerequisiteCardCount") return false;
                JsonValue cardSubject = requirement["Subject"];
                string subjectType = Type(cardSubject);
                if (subjectType == "TTargetCardPositional")
                {
                    string mode = Text(cardSubject, "TargetMode");
                    if (Text(cardSubject, "Origin") != "Self" || !PositionModeSupported(mode)) return false;
                }
                else if (subjectType == "TTargetCardSelf") { }
                else if (subjectType == "TTargetCardTriggerSource") { }
                else if (subjectType == "TTargetCardRandom" || subjectType == "TTargetCardOccupying")
                {
                    if (subjectType == "TTargetCardRandom" && !SectionSupported(Text(cardSubject, "TargetSection"))) return false;
                }
                else if (subjectType != "TTargetCardSection" ||
                    !SectionSupported(Text(cardSubject, "TargetSection"))) return false;
                if (!CardConditionSupported(cardSubject["Conditions"])) return false;
                if (Array.IndexOf(Comparisons, Text(requirement, "Comparison")) < 0) return false;
                if (requirement["Amount"].Kind != JsonKind.Number) return false;
            }
            return true;
        }

        public static bool PrerequisitesMatch(BattleContext ctx, InternalItem source, NativeSkillRule rule, string triggerSourceItemId = null)
        {
            if (rule == null || Empty(rule.Prerequisites)) return true;
            Side ownerSide = source != null ? source.Side : Side.Player;
            foreach (JsonValue requirement in rule.Prerequisites.AsArray())
            {
                string type = Type(requirement);
                if (type == "TPrerequisitePlayer")
                {
                    InternalHero hero = PlayerSubjectHero(ctx, requirement["Subject"], ownerSide);
                    if (hero == null || !PlayerConditionMatches(ctx, hero, requirement["Subject"]["Conditions"], ownerSide)) return false;
                    continue;
                }
                if (type == "TPrerequisiteCardAttributeComparator")
                {
                    if (source == null) return false;
                    double left = AttributeValue(source, Text(requirement, "Attribute"), ctx);
                    double right = AttributeValue(source, Text(requirement, "AttributeOther"), ctx);
                    bool matches = Compare(left, right, Text(requirement, "Comparison"));
                    if (!matches) return false;
                    continue;
                }
                JsonValue subject = requirement["Subject"];
                bool selfSubject = Type(subject) == "TTargetCardSelf";
                int count = 0;
                if (selfSubject)
                {
                    if (source != null && CardMatches(source, subject["Conditions"], ctx)) count = 1;
                }
                else if (Type(subject) == "TTargetCardTriggerSource")
                {
                    InternalItem triggerSource = string.IsNullOrEmpty(triggerSourceItemId) ? null : ctx.Items.Find(
                        delegate(InternalItem candidate) { return candidate.Id == triggerSourceItemId && !candidate.Destroyed; });
                    if (triggerSource != null && CardMatches(triggerSource, subject["Conditions"], ctx, triggerSourceItemId)) count = 1;
                }
                else if (Type(subject) == "TTargetCardOccupying")
                {
                    if (IsSocketEffect(source))
                        foreach (InternalItem item in ctx.Items)
                            if (!item.Destroyed && item.Id != source.Id && item.Side == source.Side && item.Slot == source.Slot &&
                                CardMatches(item, subject["Conditions"], ctx)) count++;
                }
                else
                {
                    string section = Type(subject) == "TTargetCardSection" || Type(subject) == "TTargetCardRandom"
                        ? Text(subject, "TargetSection") : null;
                    List<InternalItem> candidates = section != null && SkillSection(section) ? ctx.Skills : ctx.Items;
                    foreach (InternalItem item in candidates)
                    {
                        if (item.Destroyed) continue;
                        if (section != null && !SectionSideMatches(section, source.Side, item.Side)) continue;
                        if (Type(subject) == "TTargetCardPositional" && item.Side != source.Side) continue;
                        if (!CardMatches(item, subject["Conditions"], ctx)) continue;
                        if (Type(subject) == "TTargetCardPositional")
                        {
                            if (item == source)
                            {
                                if (!subject["IncludeOrigin"].AsBool()) continue;
                            }
                            else if (!PositionalMatch(ctx, source, item, Text(subject, "TargetMode"))) continue;
                        }
                        else if (subject["ExcludeSelf"].AsBool() && item == source) continue;
                        count++;
                    }
                }
                double threshold = requirement["Amount"].AsDouble();
                if (!Compare(count, threshold, Text(requirement, "Comparison"))) return false;
            }
            return true;
        }

        // 前提里的英雄主体同样按效果拥有者解析相对侧。
        private static InternalHero PlayerSubjectHero(BattleContext ctx, JsonValue subject, Side ownerSide)
        {
            string type = Type(subject);
            string mode = Text(subject, "TargetMode");
            if (type == "TTargetPlayerAbsolute") return ctx.Hero(mode == "Opponent" ? Side.Enemy : Side.Player);
            if (type != "TTargetPlayerRelative") return null;
            return ctx.Hero(mode == "Opponent" ? SideUtil.Opposing(ownerSide) : ownerSide);
        }

        internal static bool Compare(double left, double right, string comparison)
        {
            return comparison == "Equal" ? Math.Abs(left - right) < 1e-9 : comparison == "NotEqual" ? Math.Abs(left - right) >= 1e-9 :
                comparison == "LessThan" ? left < right : comparison == "LessThanOrEqual" ? left <= right :
                comparison == "GreaterThan" ? left > right : comparison == "GreaterThanOrEqual" && left >= right;
        }

        internal static bool RelativeSection(string section, bool self)
        {
            return self
                ? section == "SelfHand" || section == "SelfBoard" || section == "SelfHandAndStash" || section == "SelfStash" ||
                    section == "SelfSkills"
                : section == "OpponentHand" || section == "OpponentBoard" || section == "OpponentHandAndStash" || section == "OpponentStash";
        }

        internal static bool AbsoluteSection(string section, Side side)
        {
            return side == Side.Player
                ? section == "AbsolutePlayerHand" || section == "AbsolutePlayerBoard" || section == "AbsolutePlayerHandAndStash" ||
                    section == "AbsolutePlayerStash" || section == "AbsolutePlayerSkills"
                : section == "AbsoluteOpponentHand" || section == "AbsoluteOpponentBoard" || section == "AbsoluteOpponentHandAndStash" ||
                    section == "AbsoluteOpponentStash" || section == "AbsoluteOpponentSkills";
        }

        internal static bool SkillSection(string section)
        {
            return section == "AbsolutePlayerSkills" || section == "AbsoluteOpponentSkills" || section == "SelfSkills";
        }

        internal static List<Effect> OrderedEffects(List<Effect> effects)
        {
            return effects.OrderBy(delegate(Effect effect) { return EffectPriorityRank(effect.Priority); }).ToList();
        }

        internal static int EffectPriorityRank(string priority)
        {
            if (priority == "Immediate") return 0;
            if (priority == "Highest") return 1;
            if (priority == "High") return 2;
            if (priority == "Low") return 4;
            if (priority == "Lowest") return 5;
            return 3;
        }

        // 插槽效果有多个子类型（TCardSocketEffect、TCardMusicNoteSocketEffect）。
        // 它们都占据宿主卡槽位、不进入邻位/随机目标：原生帧 80 的 Wings
        // 邻位飞行只取 slot 4/8，跳过了 slot 5 的 G Note 插槽效果。
        internal static bool IsSocketEffect(InternalItem item)
        {
            return item != null && item.Card != null && IsSocketEffect(item.Card);
        }

        internal static bool IsSocketEffect(CardDef card)
        {
            if (card == null) return false;
            string type = Type(card.NativeTemplate);
            return type != null && type.EndsWith("SocketEffect", StringComparison.Ordinal);
        }

        internal static bool SectionSupported(string section)
        {
            return section == "AllHands" || section == "AllBoards" || RelativeSection(section, true) || RelativeSection(section, false) ||
                AbsoluteSection(section, Side.Player) || AbsoluteSection(section, Side.Enemy);
        }

        // 相对区域跟随拥有者阵营；Absolute* 固定为玩家/对手，不随拥有者翻转。
        internal static bool SectionSideMatches(string section, Side ownerSide, Side candidateSide)
        {
            if (string.IsNullOrEmpty(section) || section == "AllHands" || section == "AllBoards") return true;
            if (RelativeSection(section, true)) return candidateSide == ownerSide;
            if (RelativeSection(section, false)) return candidateSide != ownerSide;
            if (AbsoluteSection(section, Side.Player)) return candidateSide == Side.Player;
            if (AbsoluteSection(section, Side.Enemy)) return candidateSide == Side.Enemy;
            return false;
        }

        internal static string SectionOwner(string section, string what)
        {
            if (section == "AllHands" || section == "AllBoards") return "any";
            if (RelativeSection(section, true)) return "self";
            if (RelativeSection(section, false)) return "enemy";
            if (AbsoluteSection(section, Side.Player)) return "absolutePlayer";
            if (AbsoluteSection(section, Side.Enemy)) return "absoluteEnemy";
            throw new FormatException("尚不支持" + what + "区域 " + section);
        }

        internal static bool OwnerMatches(string owner, Side ownerSide, Side candidateSide)
        {
            if (owner == null || owner == "any") return true;
            if (owner == "self") return candidateSide == ownerSide;
            if (owner == "enemy") return candidateSide != ownerSide;
            if (owner == "absolutePlayer") return candidateSide == Side.Player;
            if (owner == "absoluteEnemy") return candidateSide == Side.Enemy;
            return false;
        }

        internal static bool PositionModeSupported(string mode)
        {
            return mode == "Neighbor" || mode == "LeftCard" || mode == "RightCard" ||
                mode == "AllLeftCards" || mode == "AllRightCards";
        }

        internal static bool PositionalMatch(BattleContext ctx, InternalItem source, InternalItem target, string mode, bool includeOrigin = false)
        {
            if (source == null || target == null || source.Side != target.Side || source.Slot < 0 || target.Slot < 0 || target.Destroyed) return false;
            if (IsSocketEffect(target)) return false;
            if (source.Id == target.Id) return includeOrigin;
            if (mode == "AllLeftCards") return target.Slot < source.Slot;
            if (mode == "AllRightCards") return target.Slot > source.Slot;
            if (!Adjacent(ctx, source, target)) return false;
            return mode == "Neighbor" || mode == "LeftCard" && target.Slot < source.Slot ||
                mode == "RightCard" && target.Slot > source.Slot;
        }

        internal static bool Adjacent(BattleContext ctx, InternalItem source, InternalItem target)
        {
            if (source == null || target == null || source.Side != target.Side || source.Id == target.Id || source.Slot < 0) return false;
            foreach (InternalItem item in ctx.Items)
                if (!item.Destroyed && !IsSocketEffect(item) && item.Side == source.Side && item.Slot > Math.Min(source.Slot, target.Slot) && item.Slot < Math.Max(source.Slot, target.Slot)) return false;
            return true;
        }
    }
}
