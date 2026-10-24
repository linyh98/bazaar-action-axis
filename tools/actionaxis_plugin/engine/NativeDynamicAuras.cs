using System;
using System.Collections.Generic;

namespace BazaarActionAxis.Engine
{
    // Auras are a derived layer, never permanent mutations. Rebuilding this layer
    // leaves permanent and expiring combat gains in AttributeDeltas untouched.
    public static class NativeDynamicAuras
    {
        private sealed class Aura
        {
            public InternalItem Source;
            public JsonValue Raw;
        }

        public static void Validate(JsonValue aura)
        {
            JsonValue action = aura["Action"];
            string attribute = NativeSkillSupport.Text(action, "AttributeType");
            string operation = NativeSkillSupport.Text(action, "Operation");
            if (operation != "Add" && operation != "Multiply")
                throw new FormatException("不可逆的持续属性光环待支持");
            // 人物条件（生命/暴怒/怒气）允许：Active() 每帧用 PrerequisitesMatch 重新判定。
            if (!NativeSkillSupport.PrerequisitesSupported(aura["Prerequisites"]))
                throw new FormatException("动态光环条件待支持");
            ValidateConditions(aura["Prerequisites"]);
            string[] runtime = { "DamageAmount", "ShieldApplyAmount", "HealAmount", "BurnApplyAmount", "PoisonApplyAmount", "RegenApplyAmount", "CritChance", "Lifesteal", "LifestealPercentage", "Multicast", "AmmoMax", "CooldownMax", "CooldownDisabled", "FlatCooldownReduction", "PercentCooldownReduction" };
            // 两个冷却缩减属性不进入属性表（实时 CooldownMax 已是有效值），
            // 而是由 Synchronize 推导有效冷却，避免重复扣减。
            bool cooldownReduction = attribute == "FlatCooldownReduction" || attribute == "PercentCooldownReduction";
            if (cooldownReduction && operation != "Add") throw new FormatException("冷却缩减光环仅支持 Add");
            if (!cooldownReduction && Array.IndexOf(runtime, attribute) < 0 && !attribute.StartsWith("Custom_", StringComparison.Ordinal))
                throw new FormatException("动态光环属性待支持 " + attribute);
            ValidateTarget(action["Target"]);
            ValidateValue(action["Value"]);
            if (operation == "Multiply" && NativeSkillSupport.Type(action["Value"]) != "TFixedValue")
                throw new FormatException("动态倍率光环待支持");
            if (operation == "Multiply" && action["Value"]["Value"].AsDouble() <= 0)
                throw new FormatException("零倍率光环无法还原实时属性");
            if (ReferencesAttribute(action["Value"], attribute))
                throw new FormatException("光环自引用属性待支持");
        }

        private static bool ReferencesAttribute(JsonValue value, string attribute)
        {
            return NativeSkillSupport.Text(value, "AttributeType") == attribute ||
                !NativeSkillSupport.Empty(value["Modifier"]) && ReferencesAttribute(value["Modifier"]["Value"], attribute);
        }

        private static void ValidateTarget(JsonValue target)
        {
            string type = NativeSkillSupport.Type(target), section = NativeSkillSupport.Text(target, "TargetSection");
            if (!NativeSkillSupport.ConditionSupported(target["Conditions"])) throw new FormatException("动态光环目标条件待支持");
            ValidateConditions(target["Conditions"]);
            if (type == "TTargetCardSelf") return;
            if (type == "TTargetCardPositional" && NativeSkillSupport.Text(target, "Origin") == "Self" &&
                NativeSkillSupport.Text(target, "TargetMode") == "Neighbor" && !target["IncludeOrigin"].AsBool()) return;
            if (type == "TTargetCardRandom" && section == "SelfNeighbors") return;
            if (type == "TTargetCardSection" && (section == "SelfHand" || section == "SelfBoard" || section == "SelfHandAndStash" || section == "SelfStash")) return;
            throw new FormatException("动态光环目标区域待支持");
        }

        private static void ValidateValue(JsonValue value)
        {
            string type = NativeSkillSupport.Type(value);
            if (type == "TFixedValue")
            {
                if (value["Value"].Kind != JsonKind.Number) throw new FormatException("光环数值缺失");
            }
            else if (type == "TReferenceValueAttributeChange")
            {
                if (!NativeSkillSupport.Empty(value["DefaultValue"]) && value["DefaultValue"].Kind != JsonKind.Number)
                    throw new FormatException("光环变化量缺少数值");
            }
            else if (type == "TReferenceValueCardCount" || type == "TReferenceValueCardTagCount") ValidateTarget(value["Target"]);
            else if (type == "TReferenceValueCardAttribute" && NativeSkillSupport.Type(value["Target"]) == "TTargetCardSelf" && NativeSkillSupport.Empty(value["Target"]["Conditions"])) { }
            else throw new FormatException("动态光环数值引用待支持 " + type);
            if (!NativeSkillSupport.Empty(value["Modifier"]))
            {
                string mode = NativeSkillSupport.Text(value["Modifier"], "ModifyMode");
                if (mode != "Add" && mode != "Multiply") throw new FormatException("动态光环数值修正待支持");
                ValidateValue(value["Modifier"]["Value"]);
            }
        }

        private static void ValidateConditions(JsonValue raw)
        {
            if (NativeSkillSupport.Empty(raw)) return;
            if (raw.Kind == JsonKind.Array) { foreach (JsonValue child in raw.AsArray()) ValidateConditions(child); return; }
            if (raw.Kind != JsonKind.Object) return;
            string attribute = NativeSkillSupport.Text(raw, "Attribute");
            if (attribute == "Haste" || attribute == "Slow" || attribute == "Freeze")
                throw new FormatException("时效状态条件光环待支持");
            foreach (KeyValuePair<string, JsonValue> pair in raw.AsObject()) ValidateConditions(pair.Value);
        }

        public static double BeforeAura(InternalItem item, string attribute)
        {
            double value = 0, delta = 0;
            if (item.AuraBaseAttributes == null) return ItemAttributes.Get(item, attribute);
            item.AuraBaseAttributes.TryGetValue(attribute, out value);
            item.AttributeDeltas.TryGetValue(attribute, out delta);
            return value + delta;
        }

        private static List<Aura> All(BattleContext ctx)
        {
            List<Aura> additions = new List<Aura>();
            List<Aura> multipliers = new List<Aura>();
            List<InternalItem> sources = new List<InternalItem>(ctx.Items);
            sources.AddRange(ctx.Skills);
            foreach (InternalItem source in sources)
            {
                if (ctx.IsTrainingDummySource(source.Side)) continue;
                foreach (JsonValue raw in source.Card.NativeRuntimeAuras)
                {
                    Aura aura = new Aura { Source = source, Raw = raw };
                    if (NativeSkillSupport.Text(raw["Action"], "Operation") == "Multiply") multipliers.Add(aura);
                    else additions.Add(aura);
                }
            }
            // Native attributes apply flat gains before aura multipliers,
            // independently of card/skill order or emitted aura event order.
            // Stable grouping also makes Initialize's reverse traversal divide
            // the inherited multiplier before subtracting inherited flat gains.
            additions.AddRange(multipliers);
            return additions;
        }

        public static void Initialize(BattleContext ctx)
        {
            if (ctx.DynamicAurasInitialized || !ctx.Config.NativeMechanics) return;
            ctx.DynamicAurasInitialized = true;
            List<Aura> auras = All(ctx);
            if (auras.Count == 0) return;
            // Snapshot attributes already include game-evaluated live auras. Read
            // their current Flying flags only while removing that inherited layer;
            // the simulated battle itself starts with no temporary flight.
            Dictionary<InternalItem, bool> flying = new Dictionary<InternalItem, bool>();
            foreach (InternalItem item in ctx.Items)
            {
                flying[item] = item.Flying;
                Dictionary<string, int> seed = item.Card.NativeAuraSeedAttributes ?? item.Card.LiveAttributes;
                item.AuraBaseAttributes = new Dictionary<string, double>();
                foreach (KeyValuePair<string, int> pair in seed) item.AuraBaseAttributes[pair.Key] = pair.Value;
                // 实时 CooldownMax 已是游戏计算后的有效值，推导层必须有基准。
                if (!item.AuraBaseAttributes.ContainsKey("CooldownMax")) item.AuraBaseAttributes["CooldownMax"] = item.CooldownMax * 1000;
                if (item.Card.NativeAttributesAreLive)
                {
                    int flag;
                    item.Flying = seed.TryGetValue("Flying", out flag) && flag > 0;
                    foreach (KeyValuePair<string, int> pair in seed) item.AuraAttributes[pair.Key] = pair.Value;
                }
            }
            // 先反推百分比/固定缩减（包含当时生效的 CooldownMax 光环），
            // 再由下面的逆序循环减去 CooldownMax 类光环，顺序不能颠倒。
            RemoveInheritedCooldownReduction(ctx, auras);
            for (int index = auras.Count - 1; index >= 0; index--)
            {
                Aura aura = auras[index];
                if (!Active(ctx, aura)) continue;
                JsonValue action = aura.Raw["Action"];
                string attribute = NativeSkillSupport.Text(action, "AttributeType");
                if (attribute == "FlatCooldownReduction" || attribute == "PercentCooldownReduction") continue;
                double value = Value(ctx, aura.Source, action["Value"]);
                foreach (InternalItem target in Targets(ctx, aura.Source, action["Target"]))
                {
                    if (!target.Card.NativeAttributesAreLive) continue;
                    double before;
                    target.AuraBaseAttributes.TryGetValue(attribute, out before);
                    target.AuraBaseAttributes[attribute] = NativeSkillSupport.Text(action, "Operation") == "Multiply" ?
                        RemoveMultiplier(ctx, auras, target, attribute, before, value) : before - value;
                }
            }
            foreach (InternalItem item in ctx.Items)
            {
                item.Flying = flying[item];
                item.AuraAttributes.Clear();
            }
            Refresh(ctx);
        }

        // 实时 CooldownMax 已包含当时生效的缩减光环；先反推基础值，
        // 再由 Refresh 重新应用，才能在光环开关时正确回退。
        private static void RemoveInheritedCooldownReduction(BattleContext ctx, List<Aura> auras)
        {
            foreach (InternalItem target in ctx.Items)
            {
                if (!target.Card.NativeAttributesAreLive) continue;
                double flat = 0, percent = 0;
                foreach (Aura aura in auras)
                {
                    if (!Active(ctx, aura)) continue;
                    JsonValue action = aura.Raw["Action"];
                    string attribute = NativeSkillSupport.Text(action, "AttributeType");
                    if (attribute != "FlatCooldownReduction" && attribute != "PercentCooldownReduction") continue;
                    if (!Targets(ctx, aura.Source, action["Target"]).Contains(target)) continue;
                    double value = Value(ctx, aura.Source, action["Value"]);
                    if (attribute == "FlatCooldownReduction") flat += value; else percent += value;
                }
                if (Math.Abs(flat) < 1e-9 && Math.Abs(percent) < 1e-9) continue;
                double current;
                target.AuraBaseAttributes.TryGetValue("CooldownMax", out current);
                if (percent >= 100)
                {
                    string warning = target.Card.DisplayName() + "：100% 冷却缩减无法反推基础冷却，按 0 处理";
                    if (!ctx.Warnings.Contains(warning)) ctx.Warnings.Add(warning);
                    target.AuraBaseAttributes["CooldownMax"] = 0;
                    continue;
                }
                target.AuraBaseAttributes["CooldownMax"] = current / (1 - percent / 100.0) + flat;
            }
        }

        public static void Refresh(BattleContext ctx)
        {
            if (!ctx.Config.NativeMechanics || ctx.EvaluatingDynamicAuras) return;
            if (!ctx.DynamicAurasInitialized) { Initialize(ctx); return; }
            List<Aura> auras = All(ctx);
            if (auras.Count == 0) return;
            ctx.EvaluatingDynamicAuras = true;
            try
            {
                // Values may depend on another aura's output, e.g. Shield equal
                // to Damage after Damage doubles. Rebuild from the same base on
                // each pass, so Multiply never compounds across refreshes.
                bool stable = false;
                for (int pass = 0; pass < 16; pass++)
                {
                    Dictionary<InternalItem, Dictionary<string, double>> next = new Dictionary<InternalItem, Dictionary<string, double>>();
                    Dictionary<InternalItem, double> flatCooldown = new Dictionary<InternalItem, double>();
                    Dictionary<InternalItem, double> percentCooldown = new Dictionary<InternalItem, double>();
                    foreach (InternalItem item in ctx.Items)
                    {
                        Dictionary<string, double> attrs = new Dictionary<string, double>();
                        if (item.AuraBaseAttributes != null)
                            foreach (KeyValuePair<string, double> pair in item.AuraBaseAttributes) attrs[pair.Key] = pair.Value;
                        else foreach (KeyValuePair<string, int> pair in item.Card.LiveAttributes) attrs[pair.Key] = pair.Value;
                        foreach (KeyValuePair<string, double> pair in item.AttributeDeltas)
                        {
                            double prior; attrs.TryGetValue(pair.Key, out prior); attrs[pair.Key] = prior + pair.Value;
                        }
                        next[item] = attrs;
                    }
                    foreach (Aura aura in auras)
                    {
                        if (!Active(ctx, aura)) continue;
                        JsonValue action = aura.Raw["Action"];
                        string attr = NativeSkillSupport.Text(action, "AttributeType");
                        double value = Value(ctx, aura.Source, action["Value"]);
                        foreach (InternalItem target in Targets(ctx, aura.Source, action["Target"]))
                        {
                            if (attr == "FlatCooldownReduction" || attr == "PercentCooldownReduction")
                            {
                                Dictionary<InternalItem, double> sink = attr == "FlatCooldownReduction" ? flatCooldown : percentCooldown;
                                double prior; sink.TryGetValue(target, out prior); sink[target] = prior + value;
                                continue;
                            }
                            double before; next[target].TryGetValue(attr, out before);
                            // Native multiplication converts the result to an
                            // integer by truncation (257 * 1.5 becomes 385).
                            next[target][attr] = NativeSkillSupport.Text(action, "Operation") == "Multiply" ? Math.Truncate(before * value) : JsMath.Round(before + value);
                        }
                    }
                    stable = true;
                    foreach (InternalItem item in ctx.Items)
                    {
                        foreach (KeyValuePair<string, double> pair in next[item])
                            if (Math.Abs(ItemAttributes.Get(item, pair.Key) - pair.Value) > 1e-7 && pair.Key != "Flying") stable = false;
                        item.AuraAttributes = next[item];
                        double flatValue, percentValue;
                        item.AuraCooldownFlat = flatCooldown.TryGetValue(item, out flatValue) ? flatValue : 0;
                        item.AuraCooldownPercent = percentCooldown.TryGetValue(item, out percentValue) ? percentValue : 0;
                    }
                    if (stable) break;
                }
                if (!stable && !ctx.Warnings.Contains("动态光环存在循环依赖，预测可能不准确")) ctx.Warnings.Add("动态光环存在循环依赖，预测可能不准确");
                foreach (InternalItem item in ctx.Items) Synchronize(item);
            }
            finally { ctx.EvaluatingDynamicAuras = false; }
        }

        private static double RemoveMultiplier(BattleContext ctx, List<Aura> auras, InternalItem target, string attribute, double captured, double factor)
        {
            // For factors >= 1 there is at most one integer input whose native
            // truncated product equals the snapshot. Never invent a fractional
            // base such as 385 / 1.5 = 256.666... when the real base was 257.
            double candidate = captured >= 0 ? Math.Ceiling(captured / factor - 1e-9) : Math.Floor(captured / factor + 1e-9);
            if (factor >= 1 && Math.Abs(Math.Truncate(candidate * factor) - captured) < 1e-9) return candidate;
            // A factor below 1 loses information. Prefer the template plus known
            // flat auras only when it reproduces the captured effective value.
            if (factor < 1 && !NativeSkillSupport.Empty(target.Card.NativeTemplate))
            {
                Dictionary<string, int> template = MonsterTemplateExtractor.TierAttributes(target.Card.NativeTemplate, target.Card.Tier);
                int raw;
                if (template.TryGetValue(attribute, out raw))
                {
                    double expected = raw;
                    foreach (Aura aura in auras)
                    {
                        JsonValue action = aura.Raw["Action"];
                        if (NativeSkillSupport.Text(action, "AttributeType") != attribute || NativeSkillSupport.Text(action, "Operation") != "Add" || !Active(ctx, aura)) continue;
                        if (Targets(ctx, aura.Source, action["Target"]).Contains(target)) expected += Value(ctx, aura.Source, action["Value"]);
                    }
                    if (Math.Abs(Math.Truncate(expected * factor) - captured) < 1e-9) return Math.Truncate(expected);
                }
            }
            string warning = target.Card.DisplayName() + "：实时属性 " + attribute + " 的光环舍入无法精确还原，预测使用整数近似";
            if (!ctx.Warnings.Contains(warning)) ctx.Warnings.Add(warning);
            return candidate;
        }

        private static void Synchronize(InternalItem item)
        {
            double value;
            if (item.AuraAttributes.TryGetValue("Multicast", out value)) item.MulticastBonus = Math.Max(0, (int)JsMath.Round(value) - 1);
            // 有效冷却 = (CooldownMax 属性 - 固定缩减) × (1 - 百分比缩减)。
            // 两个缩减属性只作为派生量，不写入 LiveAttributes，避免与实时有效值重复扣减。
            double maxMs;
            if (!item.AuraAttributes.TryGetValue("CooldownMax", out maxMs)) maxMs = item.CooldownMax * 1000;
            double percent = Math.Min(100, Math.Max(0, item.AuraCooldownPercent));
            double effectiveMs = Math.Max(0, (maxMs - item.AuraCooldownFlat) * (1 - percent / 100.0));
            double max = effectiveMs / 1000.0;
            if (Math.Abs(max - item.CooldownMax) > 1e-9)
            {
                item.Cooldown = Math.Max(0, item.Cooldown + max - item.CooldownMax);
                item.CooldownMax = max;
            }
            if (item.AuraAttributes.TryGetValue("AmmoMax", out value))
            {
                item.AmmoMax = value > 0 ? (int?)(int)JsMath.Round(value) : null;
                if (item.AmmoMax.HasValue) item.Ammo = Math.Min(item.Ammo, item.AmmoMax.Value);
            }
            if (item.AuraAttributes.TryGetValue("CritChance", out value)) item.CritBonus = Math.Max(0, value / 100.0);
            if (item.AuraAttributes.TryGetValue("Lifesteal", out value) || item.AuraAttributes.TryGetValue("LifestealPercentage", out value)) item.Lifesteal = Math.Max(0, value / 100.0);
            if (item.AuraAttributes.TryGetValue("CooldownDisabled", out value)) item.Disabled = value > 0;
        }

        private static bool Active(BattleContext ctx, Aura aura)
        {
            return !aura.Source.Destroyed && NativeSkillSupport.PrerequisitesMatch(ctx, aura.Source, new NativeSkillRule { Prerequisites = aura.Raw["Prerequisites"] });
        }

        private static List<InternalItem> Targets(BattleContext ctx, InternalItem source, JsonValue raw)
        {
            List<InternalItem> result = new List<InternalItem>();
            string type = NativeSkillSupport.Type(raw), section = NativeSkillSupport.Text(raw, "TargetSection");
            foreach (InternalItem item in ctx.Items)
            {
                if (item.Side != source.Side || item.Destroyed || raw["ExcludeSelf"].AsBool() && item == source) continue;
                if (type == "TTargetCardSelf" && item != source) continue;
                if ((type == "TTargetCardPositional" || section == "SelfNeighbors") && !NativeSkillSupport.Adjacent(ctx, source, item)) continue;
                if (section == "SelfStash") continue;
                if (!NativeSkillSupport.CardMatches(item, raw["Conditions"], ctx)) continue;
                result.Add(item);
            }
            return result;
        }

        private static double Value(BattleContext ctx, InternalItem source, JsonValue raw)
        {
            double value = BaseValue(ctx, source, raw, NativeSkillSupport.Type(raw));
            JsonValue modifier = raw["Modifier"];
            if (!NativeSkillSupport.Empty(modifier))
            {
                double other = Value(ctx, source, modifier["Value"]);
                value = NativeSkillSupport.Text(modifier, "ModifyMode") == "Add" ? value + other : value * other;
                if (modifier["ShouldRound"].AsBool()) value = JsMath.Round(value);
            }
            return value;
        }

        private static double BaseValue(BattleContext ctx, InternalItem source, JsonValue raw, string type)
        {
            if (type == "TFixedValue") return raw["Value"].AsDouble();
            if (type == "TReferenceValueAttributeChange")
                return raw["DefaultValue"].Kind == JsonKind.Number ? raw["DefaultValue"].AsDouble() : 0;
            // 指向自身的属性引用（常见于技能光环）直接读来源卡；
            // 技能不在 ctx.Items 里，走目标集只会得到 0。
            if (type == "TReferenceValueCardAttribute" && NativeSkillSupport.Type(raw["Target"]) == "TTargetCardSelf")
                return ItemAttributes.Get(source, NativeSkillSupport.Text(raw, "AttributeType"));
            List<InternalItem> items = Targets(ctx, source, raw["Target"]);
            if (type == "TReferenceValueCardCount") return items.Count;
            if (type == "TReferenceValueCardAttribute" || type == "TReferenceValueCardAttributeAggregate")
            {
                double sum = 0;
                foreach (InternalItem item in items) sum += ItemAttributes.Get(item, NativeSkillSupport.Text(raw, "AttributeType"));
                return sum;
            }
            if (type == "TReferenceValueCardTagCount")
            {
                if (!raw["Distinct"].AsBool())
                {
                    int total = 0;
                    foreach (InternalItem item in items) total += item.Card.Tags.Count + item.ExtraTags.Count;
                    return total;
                }
                HashSet<string> tags = new HashSet<string>();
                foreach (InternalItem item in items)
                {
                    foreach (string tag in item.Card.Tags) tags.Add(tag);
                    foreach (string tag in item.ExtraTags) tags.Add(tag);
                }
                return tags.Count;
            }
            return 0;
        }
    }
}
