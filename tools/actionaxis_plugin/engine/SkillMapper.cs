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
            if (tiers.Kind != JsonKind.Object || !tiers.Has(tier)) throw new FormatException("未知技能品质 " + tier);
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
            BattleContext ctx = null;
            foreach (JsonValue id in auras.AsArray())
            {
                string key = id.AsString();
                if (key == null || !template["Auras"].Has(key)) throw new FormatException("技能缺少光环定义");
                auraCount++;
                JsonValue aura = template["Auras"][key];
                if (side == Side.Player && !IsInheritedStaticAura(aura)) throw new FormatException("尚不支持动态光环、未知光环属性或条件");
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
                if (auraCount > 0) throw new FormatException("尚不支持同时含动态光环的战斗技能");
                status.Status = "simulated";
                status.Reason = "战斗触发效果已加入计算" + (auraCount > 0 ? "；静态加成沿用实测属性" : "");
                (side == Side.Player ? config.PlayerSkills : config.EnemySkills).Add(skill);
            }
            else if (auraCount > 0)
            {
                if (side == Side.Enemy)
                {
                    List<JsonValue> definitions = new List<JsonValue>();
                    foreach (JsonValue id in auras.AsArray()) definitions.Add(template["Auras"][id.AsString()]);
                    NativeCardAbilityMapper.ApplyStaticAuras(config, skill, side, attrs, definitions, false);
                    status.Status = anyAuraActive ? "simulated" : "inactive";
                    status.Reason = anyAuraActive ? "怪物静态光环已按模板加入计算" : "当前怪物棋盘不满足光环条件";
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
            TriggerDef result = new TriggerDef();
            result.SkillRule = new NativeSkillRule();
            string type = NativeSkillSupport.Type(raw);
            if (type == "TTriggerOnFightStarted")
            {
                if (!NativeSkillSupport.Empty(raw["CombatType"])) throw new FormatException("指定战斗类型条件");
                result.Event = "atCombatStart";
                return result;
            }
            if (type != "TTriggerOnItemUsed") throw new FormatException("尚不支持触发器 " + type);
            result.Event = "onUse";
            JsonValue subject = raw["Subject"];
            string subjectType = NativeSkillSupport.Type(subject);
            if (subjectType == "TTargetCardSection")
            {
                string section = NativeSkillSupport.Text(subject, "TargetSection");
                if (section == "SelfHand") result.SkillRule.SourceOwner = "self";
                else if (section == "OpponentHand") result.SkillRule.SourceOwner = "enemy";
                else throw new FormatException("尚不支持触发区域 " + section);
            }
            else if (subjectType == "TTargetCardTriggerSource") result.SkillRule.SourceOwner = "any";
            else throw new FormatException("尚不支持触发主体 " + subjectType);
            if (!NativeSkillSupport.ConditionSupported(subject["Conditions"])) throw new FormatException("尚不支持触发主体条件");
            result.SkillRule.SourceCondition = subject["Conditions"];
            return result;
        }

        internal static Effect MapAction(JsonValue raw, Dictionary<string, double> attrs)
        {
            if (!NativeSkillSupport.Empty(raw["Cost"])) throw new FormatException("尚不支持行动代价");
            string type = NativeSkillSupport.Type(raw);
            Effect result = new Effect();
            if (type == "TActionCardModifyAttribute")
            {
                if (NativeSkillSupport.Text(raw, "AttributeType") != "DamageAmount" || NativeSkillSupport.Text(raw, "Operation") != "Add" ||
                    NativeSkillSupport.Type(raw["Duration"]) != "TDeterminantDuration" || NativeSkillSupport.Text(raw["Duration"], "DurationType") != "UntilEndOfCombat")
                    throw new FormatException("尚不支持卡牌属性修改或次数计数器");
                result.Kind = "buffDamage";
                result.Amount = new NumExpr { Value = ReadValue(raw["Value"], attrs, null) };
                result.Target = MapCardTarget(raw["Target"]);
                if (result.Target.Kind != "allSelfItems" && result.Target.Kind != "allEnemyItems" && result.Target.Kind != "triggerSourceItem" && result.Target.Kind != "selfItem")
                    throw new FormatException("尚不支持随机属性修改目标");
                if (!NativeSkillSupport.Empty(raw["TargetCount"])) throw new FormatException("尚不支持属性修改目标数量限制");
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
                result.Amount = new NumExpr { Value = ReadValue(raw["ReferenceValue"], attrs, attr) };
                JsonValue target = raw["Target"];
                if (NativeSkillSupport.Type(target) != "TTargetPlayerRelative" || !NativeSkillSupport.Empty(target["Conditions"]))
                    throw new FormatException("尚不支持人物目标条件");
                string mode = NativeSkillSupport.Text(target, "TargetMode");
                if (mode != "Self" && mode != "Opponent") throw new FormatException("尚不支持人物目标 " + mode);
                result.Target = new Target { Kind = mode == "Self" ? "selfHero" : "enemyHero" };
            }
            else
            {
                result.Seconds = ReadValue(raw["Value"], attrs, attr) / 1000.0;
                result.Target = MapCardTarget(raw["Target"]);
                string countAttr = attr.Replace("Amount", "Targets");
                result.Count = NativeSkillSupport.Empty(raw["TargetCount"]) &&
                    (result.Target.Kind == "allSelfItems" || result.Target.Kind == "allEnemyItems" || result.Target.Kind == "triggerSourceItem" || result.Target.Kind == "selfItem")
                    ? 1 : (int)ReadValue(raw["TargetCount"], attrs, countAttr);
                if (result.Count.Value <= 0) throw new FormatException("缺少有效目标数量");
            }
            return result;
        }

        private static bool DurationSupported(JsonValue duration)
        {
            return NativeSkillSupport.Empty(duration) || NativeSkillSupport.Type(duration) == "TDeterminantDuration" && NativeSkillSupport.Text(duration, "DurationType") == "UntilEndOfCombat";
        }

        internal static Target MapCardTarget(JsonValue raw)
        {
            Target result = new Target();
            string type = NativeSkillSupport.Type(raw);
            string section = NativeSkillSupport.Text(raw, "TargetSection");
            if (type == "TTargetCardTriggerSource") result.Kind = "triggerSourceItem";
            else if (type == "TTargetCardSelf") result.Kind = "selfItem";
            else if (type == "TTargetCardNeighbor") result.Kind = "randomAdjacentSelf";
            else if (type == "TTargetCardPositional" && NativeSkillSupport.Text(raw, "Origin") == "Self" && NativeSkillSupport.Text(raw, "TargetMode") == "Neighbor" && !raw["IncludeOrigin"].AsBool())
                result.Kind = "randomAdjacentSelf";
            else if (type == "TTargetCardSection" || type == "TTargetCardRandom")
            {
                if (section != "SelfHand" && section != "OpponentHand") throw new FormatException("尚不支持卡牌目标区域 " + section);
                result.Kind = type == "TTargetCardSection" ? (section == "SelfHand" ? "allSelfItems" : "allEnemyItems") :
                    (section == "SelfHand" ? "randomSelfItem" : "randomEnemyItem");
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
    }

    public static class NativeSkillSupport
    {
        public static string Text(JsonValue raw, string key) { return raw != null && raw.Kind == JsonKind.Object ? raw[key].AsString() : null; }
        public static string Type(JsonValue raw) { return Text(raw, "$type"); }
        public static bool Empty(JsonValue raw) { return raw == null || raw.Kind == JsonKind.Null; }

        public static bool ConditionSupported(JsonValue raw)
        {
            if (Empty(raw)) return true;
            string type = Type(raw);
            if (type == "TCardConditionalAttribute")
            {
                string attr = Text(raw, "Attribute");
                string op = Text(raw, "ComparisonOperator");
                string[] attributes = { "CooldownMax", "Flying", "DamageAmount", "HealAmount", "ShieldApplyAmount", "BurnApplyAmount", "PoisonApplyAmount", "RegenApplyAmount", "CritChance", "AmmoMax", "Multicast" };
                string[] operators = { "Equal", "NotEqual", "LessThan", "LessThanOrEqual", "GreaterThan", "GreaterThanOrEqual" };
                return Array.IndexOf(attributes, attr) >= 0 && Array.IndexOf(operators, op) >= 0 &&
                    Type(raw["ComparisonValue"]) == "TFixedValue" && raw["ComparisonValue"]["Value"].Kind == JsonKind.Number;
            }
            if (type == "TCardConditionalTag")
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
                foreach (JsonValue child in raw["Conditions"].AsArray()) if (!ConditionSupported(child)) return false;
                return true;
            }
            // HiddenTags and arbitrary attribute references require native item data;
            // the parsed item catalog cannot establish them reliably.
            return false;
        }

        public static bool CardMatches(InternalItem item, JsonValue raw)
        {
            if (Empty(raw)) return true;
            string type = Type(raw);
            if (type == "TCardConditionalAttribute")
            {
                string attr = Text(raw, "Attribute");
                double value;
                if (attr == "CooldownMax") value = item.Card.Cooldown * 1000;
                else if (attr == "Flying") value = item.Flying ? 1 : 0;
                else { int amount; value = item.Card.LiveAttributes.TryGetValue(attr, out amount) ? amount : 0; }
                double threshold = raw["ComparisonValue"]["Value"].AsDouble();
                string op = Text(raw, "ComparisonOperator");
                return op == "Equal" ? value == threshold : op == "NotEqual" ? value != threshold : op == "LessThan" ? value < threshold :
                    op == "LessThanOrEqual" ? value <= threshold : op == "GreaterThan" ? value > threshold : op == "GreaterThanOrEqual" && value >= threshold;
            }
            if (type == "TCardConditionalTag")
            {
                int matches = 0;
                foreach (JsonValue tag in raw["Tags"].AsArray()) if (item.Card.Tags.Contains(tag.AsString())) matches++;
                string op = Text(raw, "Operator");
                return op == "Any" ? matches > 0 : op == "All" ? matches == raw["Tags"].Count : matches == 0;
            }
            if (type == "TCardConditionalSize")
            {
                bool contains = false;
                foreach (JsonValue size in raw["Sizes"].AsArray()) if (size.AsString() == item.Card.Size) contains = true;
                return contains != raw["IsNot"].AsBool();
            }
            if (type == "TCardConditionalType") return (Text(raw, "CardType") == "Item") != raw["IsNot"].AsBool();
            if (type == "TCardConditionalAnd" || type == "TCardConditionalOr")
            {
                bool and = type == "TCardConditionalAnd";
                foreach (JsonValue child in raw["Conditions"].AsArray())
                    if (CardMatches(item, child) != and) return !and;
                return and;
            }
            return false;
        }

        public static bool PrerequisitesSupported(JsonValue raw)
        {
            if (Empty(raw)) return true;
            if (raw.Kind != JsonKind.Array) return false;
            foreach (JsonValue requirement in raw.AsArray())
            {
                if (Type(requirement) != "TPrerequisiteCardCount") return false;
                JsonValue subject = requirement["Subject"];
                string subjectType = Type(subject);
                if (subjectType == "TTargetCardPositional")
                {
                    if (Text(subject, "Origin") != "Self" || Text(subject, "TargetMode") != "Neighbor" || subject["IncludeOrigin"].AsBool()) return false;
                }
                else if (subjectType != "TTargetCardSection" || Text(subject, "TargetSection") != "SelfHand" && Text(subject, "TargetSection") != "SelfBoard") return false;
                if (!ConditionSupported(subject["Conditions"])) return false;
                string comparison = Text(requirement, "Comparison");
                if (comparison != "Equal" && comparison != "LessThan" && comparison != "LessThanOrEqual" && comparison != "GreaterThan" && comparison != "GreaterThanOrEqual" && comparison != "NotEqual") return false;
                if (requirement["Amount"].Kind != JsonKind.Number) return false;
            }
            return true;
        }

        public static bool PrerequisitesMatch(BattleContext ctx, InternalItem source, NativeSkillRule rule)
        {
            if (rule == null || Empty(rule.Prerequisites)) return true;
            foreach (JsonValue requirement in rule.Prerequisites.AsArray())
            {
                int count = 0;
                foreach (InternalItem item in ctx.Items)
                {
                    if (item.Side != source.Side || !CardMatches(item, requirement["Subject"]["Conditions"])) continue;
                    if (Type(requirement["Subject"]) == "TTargetCardPositional" && !Adjacent(ctx, source, item)) continue;
                    count++;
                }
                double threshold = requirement["Amount"].AsDouble();
                string comparison = Text(requirement, "Comparison");
                bool matches = comparison == "Equal" ? count == threshold : comparison == "LessThan" ? count < threshold :
                    comparison == "LessThanOrEqual" ? count <= threshold : comparison == "GreaterThan" ? count > threshold :
                    comparison == "GreaterThanOrEqual" ? count >= threshold : count != threshold;
                if (!matches) return false;
            }
            return true;
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
