using System;
using System.Collections.Generic;
using System.IO;

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
        public int? MaxActivations;
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
            bool firstWeapon = live.TemplateId == "c5bff6ad-5d13-4cf3-9941-98f3cf913949" && NativeSkillSupport.Text(template, "InternalName") == "Counterstrike";
            foreach (JsonValue id in abilities.AsArray())
            {
                string key = id.AsString();
                if (key == null || !template["Abilities"].Has(key)) throw new FormatException("技能缺少能力定义");
                JsonValue ability = template["Abilities"][key];
                if (firstWeapon && key == "1") continue; // Verified native counter bookkeeping; bounded trigger replaces it.
                if (IsNonCombatTrigger(ability["Trigger"])) { noncombat++; continue; }
                string active = NativeSkillSupport.Text(ability, "ActiveIn");
                string works = NativeSkillSupport.Text(ability, "WorksIn");
                if (active != "HandOnly" && active != "HandAndStash") throw new FormatException("能力区域 " + active);
                if (works != null && works != "Anywhere" && works != "CombatOnly") throw new FormatException("能力场景 " + works);
                if (!firstWeapon && !NativeSkillSupport.PrerequisitesSupported(ability["Prerequisites"])) throw new FormatException("尚不支持条件或次数限制");
                TriggerDef trigger = MapTrigger(ability["Trigger"]);
                trigger.SkillRule.Prerequisites = firstWeapon ? JsonValue.Null : ability["Prerequisites"];
                if (firstWeapon) trigger.SkillRule.MaxActivations = 1;
                trigger.Effects.Add(MapAction(ability["Action"], attrs));
                skill.Triggers.Add(trigger);
            }

            int auraCount = 0;
            bool anyAuraActive = false;
            List<JsonValue> runtimeAuras = new List<JsonValue>();
            BattleContext ctx = null;
            foreach (JsonValue id in auras.AsArray())
            {
                string key = id.AsString();
                if (key == null || !template["Auras"].Has(key)) throw new FormatException("技能缺少光环定义");
                auraCount++;
                JsonValue aura = template["Auras"][key];
                bool inherited = IsInheritedStaticAura(aura);
                // 人物条件/其他区域无法当作开场静态加成：交给战斗内动态光环层。
                if (!inherited && NativeSkillSupport.Type(aura["Action"]) == "TAuraActionCardModifyAttribute")
                {
                    NativeDynamicAuras.Validate(aura);
                    runtimeAuras.Add(aura);
                    continue;
                }
                if (side == Side.Player && !inherited) throw new FormatException("尚不支持动态光环、未知光环属性或条件");
                if (!NativeSkillSupport.PrerequisitesSupported(aura["Prerequisites"])) throw new FormatException("尚不支持光环条件");
                JsonValue prereqs = aura["Prerequisites"];
                if (ctx == null) ctx = BattleEngine.InitBattle(config);
                InternalItem source = new InternalItem();
                source.Side = side;
                if (NativeSkillSupport.PrerequisitesMatch(ctx, source, new NativeSkillRule { Prerequisites = prereqs })) anyAuraActive = true;
            }

            if (skill.Triggers.Count > 0)
            {
                // A combat skill's own auras can depend on opponent HP, current
                // Shield, or other changing values. Pre-fight live attributes do
                // not establish those values for this simulated opponent.
                if (auraCount > runtimeAuras.Count) throw new FormatException("尚不支持同时含静态光环的战斗技能");
                skill.NativeRuntimeAuras = new List<JsonValue>(runtimeAuras);
                status.Status = "simulated";
                status.Reason = "战斗触发效果已加入计算";
                (side == Side.Player ? config.PlayerSkills : config.EnemySkills).Add(skill);
            }
            else if (auraCount > 0)
            {
                List<JsonValue> staticAuras = new List<JsonValue>();
                foreach (JsonValue id in auras.AsArray())
                {
                    JsonValue definition = template["Auras"][id.AsString()];
                    if (!runtimeAuras.Contains(definition)) staticAuras.Add(definition);
                }
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
                return NativeSkillSupport.Text(trigger, "AttributeType") == "Level" || NativeSkillSupport.Text(trigger, "AttributeType") == "Experience";
            return type == "TTriggerOnPlayerLevelUp" || type == "TTriggerOnPlayerLevelChanged" ||
                type == "TTriggerOnCardPurchased" || type == "TTriggerOnCardSold" || type == "TTriggerOnDayStarted" ||
                type == "TTriggerOnHourStarted" || type == "TTriggerOnCardUpgraded" || type == "TTriggerOnCardEnchanted" ||
                type == "TTriggerOnFightEnded" || type == "TTriggerOnPlayerExperienceGained";
        }

        internal static TriggerDef MapTrigger(JsonValue raw)
        {
            return NativeSkillSupport.MapTrigger(raw);
        }

        internal static Effect MapAction(JsonValue raw, Dictionary<string, double> attrs)
        {
            if (!NativeSkillSupport.Empty(raw["Cost"])) throw new FormatException("尚不支持行动代价");
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
                result.Count = TargetCount(raw, attrs, result.Target, "ReloadTargets");
                result.AmountValue = NativeSkillSupport.Empty(raw["Value"])
                    ? -1 : ReadValue(raw["Value"], attrs, "ReloadAmount");
                return result;
            }
            if (type == "TActionCardFlyingStart")
            {
                if (!NativeSkillSupport.Empty(raw["Cost"]) || !NativeSkillSupport.Empty(raw["Duration"])) throw new FormatException("限时飞行或行动代价");
                result.Kind = "fly";
                result.Target = MapCardTarget(raw["Target"]);
                result.Count = TargetCount(raw, attrs, result.Target, "FlyingTargets");
                return result;
            }
            if (type == "TActionCardFlyingStop" || type == "TActionCardFlyingToggle")
            {
                result.Kind = type == "TActionCardFlyingStop" ? "flyStop" : "flyToggle";
                result.Target = MapCardTarget(raw["Target"]);
                result.Count = TargetCount(raw, attrs, result.Target, "FlyingTargets");
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
                foreach (KeyValuePair<string, JsonValue> pair in spawn.AsObject())
                    if (pair.Key != "$type" && pair.Key != "Limit") throw new FormatException("变形卡池待支持 " + pair.Key);
                int copies = 1;
                if (!NativeSkillSupport.Empty(spawn["Limit"]))
                    copies = (int)JsMath.Round(ReadValue(spawn["Limit"], attrs, null));
                if (copies < 1 || copies > 10) throw new FormatException("变形副本数超出范围");
                result.Kind = "transform";
                result.Target = MapCardTarget(raw["Target"]);
                result.Count = copies;
                return result;
            }
            if (type == "TActionPlayerBurnRemove" || type == "TActionPlayerPoisonRemove")
            {
                result.Kind = type == "TActionPlayerBurnRemove" ? "burnRemove" : "poisonRemove";
                result.Target = MapHeroTarget(raw["Target"]);
                if (!NativeSkillSupport.Empty(raw["ReferenceValue"]))
                    result.Amount = MapValue(raw["ReferenceValue"], attrs, null);
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
                result.Count = TargetCount(raw, attrs, result.Target, "ModifyTargets");
                return result;
            }
            if (type == "TActionPlayerModifyAttribute")
            {
                string attribute = NativeSkillSupport.Text(raw, "AttributeType");
                string operation = NativeSkillSupport.Text(raw, "Operation");
                string[] supported = { "HealthMax", "HealthRegen", "RageMax", "EnragedDurationMax" };
                if (Array.IndexOf(supported, attribute) < 0) throw new FormatException("尚不支持人物属性 " + attribute);
                if (operation != "Add" && operation != "Subtract" && operation != "Multiply" && operation != "Set")
                    throw new FormatException("尚不支持人物属性操作 " + operation);
                result.Kind = "playerAttribute";
                result.Attribute = attribute;
                result.Operation = operation;
                result.Amount = MapValue(raw["Value"], attrs, null);
                result.Target = MapHeroTarget(raw["Target"]);
                return result;
            }
            if (type == "TActionPlayerRageApply")
            {
                result.Kind = "rage";
                result.Amount = MapValue(raw["ReferenceValue"], attrs, "RageApplyAmount");
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
                result.Count = TargetCount(raw, attrs, result.Target, countAttr);
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
                result.Count = TargetCount(raw, attrs, result.Target, "AddTagsTargets");
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
                result.Amount = MapValue(raw["ReferenceValue"], attrs, attr);
                result.Target = MapHeroTarget(raw["Target"]);
            }
            else
            {
                result.Seconds = ReadValue(raw["Value"], attrs, attr) / 1000.0;
                result.Target = MapCardTarget(raw["Target"]);
                string countAttr = attr.Replace("Amount", "Targets");
                result.Count = TargetCount(raw, attrs, result.Target, countAttr);
            }
            return result;
        }

        // TargetCount 缺省时：全体目标作用于全部，具体目标作用于 1 张。
        private static int TargetCount(JsonValue raw, Dictionary<string, double> attrs, Target target, string countAttr)
        {
            int count;
            if (!NativeSkillSupport.Empty(raw["TargetCount"]))
                count = (int)ReadValue(raw["TargetCount"], attrs, null);
            else if (target.Kind != null && target.Kind.StartsWith("all", StringComparison.Ordinal))
                count = int.MaxValue;
            else if (countAttr != null && attrs != null && attrs.ContainsKey(countAttr))
                count = (int)ReadValue(JsonValue.Null, attrs, countAttr);
            else count = 1;
            if (count <= 0) throw new FormatException("缺少有效目标数量");
            return count;
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
                if (mode != "Self" && mode != "Opponent") throw new FormatException("尚不支持人物目标 " + mode);
                return new Target { Kind = mode == "Self" ? "selfHero" : "enemyHero" };
            }
            if (type == "TTargetPlayerAbsolute" || type == "TTargetPlayer")
            {
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
            bool self = section == "SelfHand" || section == "SelfBoard" || section == "SelfHandAndStash" || section == "SelfStash";
            bool enemy = section == "OpponentHand" || section == "OpponentBoard" || section == "OpponentHandAndStash" || section == "OpponentStash";
            if (type == "TTargetCardTriggerSource") result.Kind = "triggerSourceItem";
            else if (type == "TTargetCardTriggerTarget") result.Kind = "triggerTargetItem";
            else if (type == "TTargetCardSelf") result.Kind = "selfItem";
            else if (type == "TTargetCardNeighbor") result.Kind = "randomAdjacentSelf";
            else if (type == "TTargetCardPositional")
            {
                string origin = NativeSkillSupport.Text(raw, "Origin");
                string mode = NativeSkillSupport.Text(raw, "TargetMode");
                if (raw["IncludeOrigin"].AsBool()) throw new FormatException("尚不支持包含自身的位置目标");
                bool fromTrigger = origin == "TriggerSource";
                if (mode == "Neighbor") result.Kind = fromTrigger ? "triggerSourceAdjacent" : "randomAdjacentSelf";
                else if (mode == "LeftCard") result.Kind = fromTrigger ? "triggerSourceLeft" : "leftSelf";
                else if (mode == "RightCard") result.Kind = fromTrigger ? "triggerSourceRight" : "rightSelf";
                else throw new FormatException("尚不支持位置目标 " + origin + "/" + mode);
            }
            else if (type == "TTargetCardXMost")
            {
                if (!self && !enemy) throw new FormatException("尚不支持卡牌目标区域 " + section);
                string mode = NativeSkillSupport.Text(raw, "TargetMode");
                bool right = mode == "RightMostCard";
                if (!right && mode != "LeftMostCard") throw new FormatException("尚不支持最左/最右目标 " + mode);
                result.Kind = (right ? "rightMost" : "leftMost") + (self ? "Self" : "Enemy");
            }
            else if (section == "SelfNeighbors" && (type == "TTargetCardRandom" || type == "TTargetCardSection"))
            {
                result.Kind = "randomAdjacentSelf";
            }
            else if (type == "TTargetCardSection" || type == "TTargetCardRandom")
            {
                if (!self && !enemy) throw new FormatException("尚不支持卡牌目标区域 " + section);
                result.Kind = type == "TTargetCardSection" ? (self ? "allSelfItems" : "allEnemyItems") :
                    (self ? "randomSelfItem" : "randomEnemyItem");
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
                else throw new FormatException("尚不支持数值修正 " + mode);
                if (modifier["ShouldRound"].AsBool()) result = JsMath.Round(result);
            }
            if (double.IsInfinity(result) || double.IsNaN(result) || result < 0) throw new FormatException("无效技能数值");
            return result;
        }

        // 映射期只校验结构并在属性存在时锁定初值；正式数值在结算时重算，
        // 这样战斗中获得的属性加成会真正影响后续伤害/治疗。
        internal static NumExpr MapValue(JsonValue raw, Dictionary<string, double> attrs, string defaultAttr)
        {
            if (NativeSkillSupport.Empty(raw))
            {
                if (defaultAttr == null) throw new FormatException("缺少数值引用");
                if (attrs == null || !attrs.ContainsKey(defaultAttr)) throw new FormatException("缺少属性 " + defaultAttr);
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
                JsonValue target = raw["Target"];
                string kind = NativeSkillSupport.Type(target) == "TTargetCardTriggerSource" ? "triggerSourceItem" :
                    NativeSkillSupport.Type(target) == "TTargetCardTriggerTarget" ? "triggerTargetItem" : "selfItem";
                if (kind == "selfItem" && (NativeSkillSupport.Type(target) != "TTargetCardSelf" || !NativeSkillSupport.Empty(target["Conditions"])))
                    throw new FormatException("尚不支持其他卡牌的属性引用");
                // 引用他人属性时不能要求本卡已声明该属性；自有属性则必须在映射期存在。
                if (kind == "selfItem" && (attrs == null || !attrs.ContainsKey(attribute))) throw new FormatException("缺少技能引用属性 " + attribute);
                double snapshot;
                if (attrs != null) attrs.TryGetValue(attribute, out snapshot); else snapshot = 0;
                result = new NumExpr { Type = "cardAttribute", Attribute = attribute, TargetKind = kind, Value = snapshot };
            }
            else if (type == "TReferenceValuePlayerAttribute" || type == "TReferenceValuePlayerAttributeUnscaled")
            {
                string attribute = NativeSkillSupport.Text(raw, "AttributeType");
                if (string.IsNullOrEmpty(attribute)) throw new FormatException("人物属性引用缺少属性名");
                if (type == "TReferenceValuePlayerAttributeUnscaled" && attribute != "HealthMax")
                    throw new FormatException("尚不支持无缩放人物属性 " + attribute);
                string mode = NativeSkillSupport.Text(raw["Target"], "TargetMode");
                if (mode != "Self" && mode != "Player" && mode != "Opponent") throw new FormatException("尚不支持人物属性目标 " + mode);
                result = new NumExpr { Type = "playerAttribute", Attribute = attribute, Side = mode == "Opponent" ? "enemy" : "self" };
            }
            else if (type == "TReferenceValueCardTagCount")
            {
                Target target = MapCardTarget(raw["Target"]);
                result = new NumExpr { Type = "tagCount", TargetKind = target.Kind, Condition = target.NativeCondition, Distinct = raw["Distinct"].AsBool(), Exclude = target.ExcludeSelf };
            }
            else if (type == "TReferenceValueCardAttributeAggregate")
            {
                string attribute = NativeSkillSupport.Text(raw, "AttributeType");
                if (string.IsNullOrEmpty(attribute)) throw new FormatException("聚合属性缺少属性名");
                Target target = MapCardTarget(raw["Target"]);
                result = new NumExpr { Type = "aggregate", Attribute = attribute, TargetKind = target.Kind, Condition = target.NativeCondition, Exclude = target.ExcludeSelf };
            }
            else if (type == "TReferenceValueCardCount")
            {
                Target target = MapCardTarget(raw["Target"]);
                result = new NumExpr { Type = "cardCount", TargetKind = target.Kind, Condition = target.NativeCondition, Exclude = target.ExcludeSelf };
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
                if (mode != "Multiply" && mode != "Add") throw new FormatException("尚不支持数值修正 " + mode);
                result.Modifier = MapValue(modifier["Value"], attrs, null);
                result.ModifyMode = mode;
                result.ShouldRound = modifier["ShouldRound"].AsBool();
            }
            return result;
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
            { "TTriggerOnPlayerDied", "onPlayerDied" }
        };

        public static TriggerDef MapTrigger(JsonValue raw)
        {
            string type = Type(raw);
            TriggerDef result = new TriggerDef();
            result.SkillRule = new NativeSkillRule();
            if (type == "TTriggerOnFightStarted")
            {
                if (!Empty(raw["CombatType"])) throw new FormatException("指定战斗类型条件");
                result.Event = "atCombatStart";
                return result;
            }
            string ev;
            if (!TriggerEvents.TryGetValue(type, out ev)) throw new FormatException("尚不支持触发器 " + type);
            if (!Empty(raw["Source"])) throw new FormatException("尚不支持触发来源限制");
            if (!Empty(raw["Target"]) && type != "TTriggerOnCardPerformedDestruction")
                throw new FormatException("指定行动受体的被动待支持");
            result.Event = ev;
            if (type == "TTriggerOnPlayerAttributeChanged")
            {
                string attribute = Text(raw, "AttributeType");
                // 目前只有 Health / Shield / Rage 会发 onPlayerAttributeChanged。
                if (attribute != "Health" && attribute != "Shield" && attribute != "Rage") throw new FormatException("尚不支持人物属性变化 " + attribute);
                result.AttributeFilter = attribute;
                result.ChangeType = Text(raw, "ChangeType");
            }
            else if (type == "TTriggerOnCardAttributeChanged")
            {
                string attribute = Text(raw, "AttributeChanged");
                string[] supported = { "Haste", "Slow", "Freeze", "Flying" };
                if (Array.IndexOf(supported, attribute) < 0) throw new FormatException("尚不支持卡牌属性变化 " + attribute);
                result.AttributeFilter = attribute;
                result.ChangeType = Text(raw, "ChangeType");
            }
            result.SkillRule = MapSubject(raw["Subject"]);
            if (IsPlayerEvent(type) && result.SkillRule.PlayerOwner == null)
                throw new FormatException("人物事件的触发主体必须是英雄");
            return result;
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
            else if (subjectType == "TTargetCardSection")
            {
                string section = Text(subject, "TargetSection");
                if (section == "SelfHand" || section == "SelfBoard" || section == "SelfHandAndStash" || section == "SelfStash") rule.SourceOwner = "self";
                else if (section == "OpponentHand" || section == "OpponentBoard" || section == "OpponentHandAndStash" || section == "OpponentStash") rule.SourceOwner = "enemy";
                else throw new FormatException("尚不支持触发区域 " + section);
                rule.SourceOther = subject["ExcludeSelf"].AsBool();
            }
            else if (subjectType == "TTargetCardPositional" && Text(subject, "Origin") == "Self" &&
                Text(subject, "TargetMode") == "Neighbor" && !subject["IncludeOrigin"].AsBool())
            {
                rule.SourceOwner = "self";
                rule.SourceAdjacent = true;
            }
            else if (subjectType == "TTargetCardTriggerSource") rule.SourceOwner = "any";
            else throw new FormatException("未知触发主体 " + subjectType);
            rule.SourceCondition = subject["Conditions"];
            if (!CardConditionSupported(rule.SourceCondition)) throw new FormatException("未知触发主体条件");
            return rule;
        }

        private static readonly string[] SupportedAttributes = { "CooldownMax", "Flying", "DamageAmount", "HealAmount", "ShieldApplyAmount", "BurnApplyAmount", "PoisonApplyAmount", "RegenApplyAmount", "CritChance", "AmmoMax", "Multicast" };
        private static readonly string[] SupportedTiers = { "Bronze", "Silver", "Gold", "Diamond", "Legendary" };
        private static readonly string[] Comparisons = { "Equal", "NotEqual", "LessThan", "LessThanOrEqual", "GreaterThan", "GreaterThanOrEqual" };

        // 卡牌条件。属性条件允许任意属性名：模板未声明的属性按 0 处理，
        // 这是原生属性表的缺省语义，不会凭空造出加成。
        public static bool CardConditionSupported(JsonValue raw)
        {
            if (Empty(raw)) return true;
            string type = Type(raw);
            if (type == "TCardConditionalAttribute")
            {
                string attr = Text(raw, "Attribute");
                string op = Text(raw, "ComparisonOperator");
                return !string.IsNullOrEmpty(attr) && Array.IndexOf(Comparisons, op) >= 0 &&
                    Type(raw["ComparisonValue"]) == "TFixedValue" && raw["ComparisonValue"]["Value"].Kind == JsonKind.Number;
            }
            if (type == "TCardConditionalTier")
            {
                if (raw["Tiers"].Kind != JsonKind.Array || raw["Tiers"].Count == 0) return false;
                foreach (JsonValue tier in raw["Tiers"].AsArray())
                    if (Array.IndexOf(SupportedTiers, tier.AsString()) < 0) return false;
                return true;
            }
            if (type == "TCardConditionalId") return !string.IsNullOrEmpty(Text(raw, "Id"));
            if (type == "TCardConditionalHasEnchantment")
                return Empty(raw["Enchantment"]) || raw["Enchantment"].Kind == JsonKind.String;
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
            if (attribute != "Health" && attribute != "Enraged" && attribute != "HealthMax" && attribute != "Rage") return false;
            if (Array.IndexOf(Comparisons, op) < 0) return false;
            string valueType = Type(raw["ComparisonValue"]);
            return valueType == "TFixedValue" || valueType == "TReferenceValuePlayerAttribute";
        }

        public static bool PlayerConditionMatches(BattleContext ctx, InternalHero hero, JsonValue raw)
        {
            if (Empty(raw)) return true;
            if (ctx == null || hero == null) return false;
            if (Type(raw) == "TCardConditionalAnd" || Type(raw) == "TCardConditionalOr")
            {
                bool and = Type(raw) == "TCardConditionalAnd";
                foreach (JsonValue child in raw["Conditions"].AsArray())
                    if (PlayerConditionMatches(ctx, hero, child) != and) return !and;
                return and;
            }
            if (Type(raw) != "TPlayerConditionalAttribute") return false;
            double value = PlayerAttributeValue(ctx, hero, Text(raw, "Attribute"));
            double threshold = ConditionValue(ctx, hero, raw["ComparisonValue"]);
            string op = Text(raw, "ComparisonOperator");
            return op == "Equal" ? Math.Abs(value - threshold) < 1e-9 : op == "NotEqual" ? Math.Abs(value - threshold) >= 1e-9 :
                op == "LessThan" ? value < threshold : op == "LessThanOrEqual" ? value <= threshold :
                op == "GreaterThan" ? value > threshold : value >= threshold;
        }

        // 比较值可能引用人物属性（如“生命低于最大生命的 50%”）。
        private static double ConditionValue(BattleContext ctx, InternalHero hero, JsonValue raw)
        {
            string type = Type(raw);
            if (type == "TFixedValue") return raw["Value"].AsDouble();
            if (type != "TReferenceValuePlayerAttribute" && type != "TReferenceValuePlayerAttributeUnscaled") return 0;
            string mode = Text(raw["Target"], "TargetMode");
            InternalHero target = mode == "Opponent" ? ctx.Hero(SideUtil.Opposing(hero.Side)) : hero;
            double value = PlayerAttributeValue(ctx, target, Text(raw, "AttributeType"));
            JsonValue modifier = raw["Modifier"];
            if (!Empty(modifier))
            {
                double other = ConditionValue(ctx, hero, modifier["Value"]);
                value = Text(modifier, "ModifyMode") == "Add" ? value + other : value * other;
                if (modifier["ShouldRound"].AsBool()) value = JsMath.Round(value);
            }
            return value;
        }

        public static double PlayerAttributeValue(BattleContext ctx, InternalHero hero, string attribute)
        {
            if (hero == null) return 0;
            if (attribute == "Health") return hero.Hp;
            if (attribute == "HealthMax") return hero.MaxHp;
            if (attribute == "HealthRegen") return hero.Regen;
            if (attribute == "Rage") return hero.Rage;
            if (attribute == "RageMax") return hero.RageMax;
            if (attribute == "EnragedDurationMax") return hero.EnragedDurationMs;
            // 暴怒只作为布尔读取；到期时战斗循环会把 EnragedUntil 置为 -1。
            if (attribute == "Enraged") return hero.EnragedUntil > 0 ? 1 : 0;
            return 0;
        }

        public static double AttributeValue(InternalItem item, string attribute)
        {
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
                foreach (string tier in new[] { "Bronze", "Silver", "Gold", "Diamond", "Legendary" })
                {
                    JsonValue row = card.NativeTemplate["Tiers"][tier];
                    if (row["AbilityIds"].Kind == JsonKind.Array) ids = row["AbilityIds"];
                    if (tier == card.Tier) break;
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
                double value = AttributeValue(item, Text(raw, "Attribute"));
                double threshold = raw["ComparisonValue"]["Value"].AsDouble();
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
            if (type == "TCardConditionalHasEnchantment")
            {
                string filter = Text(raw, "Enchantment");
                bool enchanted = !string.IsNullOrEmpty(item.Card.EnchantmentName);
                bool matches = filter == null ? enchanted :
                    enchanted && string.Equals(filter, item.Card.EnchantmentName, StringComparison.OrdinalIgnoreCase);
                return matches != raw["IsNot"].AsBool();
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
                double value = AttributeValue(item, attribute);
                bool highest = type == "TCardConditionalAttributeHighest";
                foreach (InternalItem other in ctx.Items)
                {
                    if (other.Side != item.Side || other.Id == item.Id) continue;
                    double candidate = AttributeValue(other, attribute);
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
                    if (Type(playerSubject) != "TTargetPlayerRelative" && Type(playerSubject) != "TTargetPlayerAbsolute") return false;
                    if (Text(playerSubject, "TargetMode") != "Self" && Text(playerSubject, "TargetMode") != "Player") return false;
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
                    if (Text(cardSubject, "Origin") != "Self" || Text(cardSubject, "TargetMode") != "Neighbor" || cardSubject["IncludeOrigin"].AsBool()) return false;
                }
                else if (subjectType == "TTargetCardSelf") { }
                else if (subjectType != "TTargetCardSection" || Text(cardSubject, "TargetSection") != "SelfHand" && Text(cardSubject, "TargetSection") != "SelfBoard") return false;
                if (!CardConditionSupported(cardSubject["Conditions"])) return false;
                if (Array.IndexOf(Comparisons, Text(requirement, "Comparison")) < 0) return false;
                if (requirement["Amount"].Kind != JsonKind.Number) return false;
            }
            return true;
        }

        public static bool PrerequisitesMatch(BattleContext ctx, InternalItem source, NativeSkillRule rule)
        {
            if (rule == null || Empty(rule.Prerequisites)) return true;
            InternalHero hero = source != null ? ctx.Hero(source.Side) : null;
            foreach (JsonValue requirement in rule.Prerequisites.AsArray())
            {
                string type = Type(requirement);
                if (type == "TPrerequisitePlayer")
                {
                    if (!PlayerConditionMatches(ctx, hero, requirement["Subject"]["Conditions"])) return false;
                    continue;
                }
                if (type == "TPrerequisiteCardAttributeComparator")
                {
                    if (source == null) return false;
                    double left = AttributeValue(source, Text(requirement, "Attribute"));
                    double right = AttributeValue(source, Text(requirement, "AttributeOther"));
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
                else
                {
                    foreach (InternalItem item in ctx.Items)
                    {
                        if (item.Destroyed || item.Side != source.Side) continue;
                        if (!CardMatches(item, subject["Conditions"], ctx)) continue;
                        if (Type(subject) == "TTargetCardPositional" && !Adjacent(ctx, source, item)) continue;
                        count++;
                    }
                }
                double threshold = requirement["Amount"].AsDouble();
                if (!Compare(count, threshold, Text(requirement, "Comparison"))) return false;
            }
            return true;
        }

        internal static bool Compare(double left, double right, string comparison)
        {
            return comparison == "Equal" ? Math.Abs(left - right) < 1e-9 : comparison == "NotEqual" ? Math.Abs(left - right) >= 1e-9 :
                comparison == "LessThan" ? left < right : comparison == "LessThanOrEqual" ? left <= right :
                comparison == "GreaterThan" ? left > right : comparison == "GreaterThanOrEqual" && left >= right;
        }

        internal static bool Adjacent(BattleContext ctx, InternalItem source, InternalItem target)
        {
            if (source == null || target == null || source.Side != target.Side || source.Id == target.Id || source.Slot < 0) return false;
            foreach (InternalItem item in ctx.Items)
                if (item.Side == source.Side && item.Slot > Math.Min(source.Slot, target.Slot) && item.Slot < Math.Max(source.Slot, target.Slot)) return false;
            return true;
        }
    }
}
