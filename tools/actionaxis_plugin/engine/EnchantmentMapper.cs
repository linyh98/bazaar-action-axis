using System;
using System.Collections.Generic;

namespace BazaarActionAxis.Engine
{
    public static class EnchantmentMapper
    {
        // The catalog contains the unenchanted item. Only the selected native
        // enchantment's added abilities are appended. Its persistent auras have
        // already been evaluated by the game into live card attributes.
        public static CardDef Apply(CardDef source, LiveCardEntry live, IList<string> warnings)
        {
            if (string.IsNullOrEmpty(live.Enchantment) || live.Enchantment == "None") return source;
            try
            {
                JsonValue native = live.EnchantmentDefinition;
                if (NativeSkillSupport.Empty(native) || native.Kind != JsonKind.Object)
                    throw new FormatException(live.EnchantmentReadError ?? "缺少原生附魔定义");
                if (native["Abilities"].Kind != JsonKind.Object || native["Auras"].Kind != JsonKind.Object)
                    throw new FormatException("附魔规则不完整");
                List<Effect> added = new List<Effect>();
                foreach (KeyValuePair<string, JsonValue> pair in native["Abilities"].AsObject())
                {
                    JsonValue ability = pair.Value;
                    CheckArea(ability);
                    if (!NativeSkillSupport.Empty(ability["Prerequisites"])) throw new FormatException("附魔能力含尚未支持的触发条件或次数限制");
                    if (NativeSkillSupport.Type(ability["Trigger"]) != "TTriggerOnCardFired")
                        throw new FormatException("尚不支持附魔触发器 " + NativeSkillSupport.Type(ability["Trigger"]));
                    added.Add(MapAction(ability["Action"], live.Attributes));
                }
                foreach (KeyValuePair<string, JsonValue> pair in native["Auras"].AsObject())
                    ValidateInheritedAura(pair.Value, live.Attributes);
                // These live-only attributes need engine mechanics of their own.
                // Do not report Radiant/Obsidian immunity as supported just because
                // the raw value was captured.
                foreach (KeyValuePair<string, JsonValue> pair in native["Attributes"].AsObject())
                    if (!InheritedAttribute(pair.Key)) throw new FormatException("尚不支持附魔属性 " + pair.Key);

                CardDef result = source.Clone();
                result.Effects = new List<Effect>(source.Effects);
                result.Effects.AddRange(added);
                result.Tags = new List<string>(source.Tags);
                foreach (JsonValue tag in native["Tags"].AsArray())
                    if (!result.Tags.Contains(tag.AsString())) result.Tags.Add(tag.AsString());
                return result;
            }
            catch (Exception error)
            {
                warnings.Add(source.DisplayName() + " [" + live.Enchantment + "]：" + error.Message + "；附魔新增效果未加入计算");
                return source;
            }
        }

        private static void CheckArea(JsonValue value)
        {
            string active = NativeSkillSupport.Text(value, "ActiveIn");
            string works = NativeSkillSupport.Text(value, "WorksIn");
            if (active != "HandOnly" && active != "HandAndStash") throw new FormatException("附魔作用区域 " + active);
            if (works != null && works != "Anywhere" && works != "CombatOnly") throw new FormatException("附魔作用场景 " + works);
        }

        private static Effect MapAction(JsonValue raw, Dictionary<string, int> attrs)
        {
            if (!NativeSkillSupport.Empty(raw["Cost"])) throw new FormatException("尚不支持附魔行动代价");
            string type = NativeSkillSupport.Type(raw);
            string attribute;
            Effect result = new Effect();
            switch (type)
            {
                case "TActionPlayerDamage": result.Kind = "damage"; attribute = "DamageAmount"; break;
                case "TActionPlayerHeal": result.Kind = "heal"; attribute = "HealAmount"; break;
                case "TActionPlayerShieldApply": result.Kind = "shield"; attribute = "ShieldApplyAmount"; break;
                case "TActionPlayerBurnApply": result.Kind = "burn"; attribute = "BurnApplyAmount"; break;
                case "TActionPlayerPoisonApply": result.Kind = "poison"; attribute = "PoisonApplyAmount"; break;
                case "TActionPlayerRegenApply": result.Kind = "regen"; attribute = "RegenApplyAmount"; break;
                case "TActionCardHaste": result.Kind = "haste"; attribute = "HasteAmount"; break;
                case "TActionCardSlow": result.Kind = "slow"; attribute = "SlowAmount"; break;
                case "TActionCardFreeze": result.Kind = "freeze"; attribute = "FreezeAmount"; break;
                case "TActionCardCharge": result.Kind = "charge"; attribute = "ChargeAmount"; break;
                default: throw new FormatException("尚不支持附魔行动 " + type);
            }
            JsonValue target = raw["Target"];
            if (type.StartsWith("TActionPlayer", StringComparison.Ordinal))
            {
                JsonValue duration = raw["Duration"];
                if (!NativeSkillSupport.Empty(duration) &&
                    (NativeSkillSupport.Type(duration) != "TDeterminantDuration" || NativeSkillSupport.Text(duration, "DurationType") != "UntilEndOfCombat"))
                    throw new FormatException("尚不支持附魔持续时间");
                if (NativeSkillSupport.Type(target) != "TTargetPlayerRelative" || !NativeSkillSupport.Empty(target["Conditions"]))
                    throw new FormatException("尚不支持附魔人物目标条件");
                string mode = NativeSkillSupport.Text(target, "TargetMode");
                if (mode != "Self" && mode != "Opponent") throw new FormatException("尚不支持附魔人物目标 " + mode);
                result.Target = new Target { Kind = mode == "Self" ? "selfHero" : "enemyHero" };
                result.Amount = new NumExpr { Value = ReadValue(raw["ReferenceValue"], attrs, attribute) };
            }
            else
            {
                string targetType = NativeSkillSupport.Type(target);
                string section = NativeSkillSupport.Text(target, "TargetSection");
                if (targetType != "TTargetCardRandom" && targetType != "TTargetCardSection")
                    throw new FormatException("尚不支持附魔卡牌目标 " + targetType);
                if (section != "SelfHand" && section != "OpponentHand") throw new FormatException("尚不支持附魔目标区域 " + section);
                if (target["ExcludeSelf"].AsBool()) throw new FormatException("尚不支持附魔排除自身目标");
                if (!NativeSkillSupport.ConditionSupported(target["Conditions"])) throw new FormatException("尚不支持附魔目标条件");
                result.Target = new Target
                {
                    Kind = targetType == "TTargetCardRandom" ? (section == "SelfHand" ? "randomSelfItem" : "randomEnemyItem") :
                        (section == "SelfHand" ? "allSelfItems" : "allEnemyItems"),
                    NativeCondition = target["Conditions"]
                };
                result.Seconds = ReadValue(raw["Value"], attrs, attribute) / 1000.0;
                result.Count = targetType == "TTargetCardSection" && NativeSkillSupport.Empty(raw["TargetCount"])
                    ? 1 : (int)ReadValue(raw["TargetCount"], attrs, attribute.Replace("Amount", "Targets"));
                if (result.Count.Value <= 0) throw new FormatException("缺少有效附魔目标数量");
            }
            return result;
        }

        private static double ReadValue(JsonValue raw, Dictionary<string, int> attrs, string defaultAttribute)
        {
            double value;
            if (NativeSkillSupport.Empty(raw))
            {
                int live;
                if (defaultAttribute == null || attrs == null || !attrs.TryGetValue(defaultAttribute, out live))
                    throw new FormatException("缺少实时附魔属性 " + defaultAttribute);
                value = live;
            }
            else if (NativeSkillSupport.Type(raw) == "TFixedValue" && raw["Value"].Kind == JsonKind.Number) value = raw["Value"].AsDouble();
            else if (NativeSkillSupport.Type(raw) == "TReferenceValueCardAttribute" &&
                NativeSkillSupport.Type(raw["Target"]) == "TTargetCardSelf" && NativeSkillSupport.Empty(raw["Target"]["Conditions"]))
                value = ReadValue(JsonValue.Null, attrs, NativeSkillSupport.Text(raw, "AttributeType"));
            else throw new FormatException("尚不支持附魔数值引用 " + NativeSkillSupport.Type(raw));
            if (!NativeSkillSupport.Empty(raw))
            {
                JsonValue modifier = raw["Modifier"];
                if (!NativeSkillSupport.Empty(modifier))
                {
                    double other = ReadValue(modifier["Value"], attrs, null);
                    string mode = NativeSkillSupport.Text(modifier, "ModifyMode");
                    if (mode == "Multiply") value *= other;
                    else if (mode == "Add") value += other;
                    else throw new FormatException("尚不支持附魔数值修正 " + mode);
                    if (modifier["ShouldRound"].AsBool()) value = JsMath.Round(value);
                }
            }
            if (value < 0 || double.IsNaN(value) || double.IsInfinity(value)) throw new FormatException("无效附魔数值");
            return value;
        }

        private static void ValidateInheritedAura(JsonValue aura, Dictionary<string, int> attrs)
        {
            CheckArea(aura);
            if (!NativeSkillSupport.Empty(aura["Prerequisites"])) throw new FormatException("尚不支持附魔光环条件");
            JsonValue action = aura["Action"];
            string attribute = NativeSkillSupport.Text(action, "AttributeType");
            int value;
            if (NativeSkillSupport.Type(action) != "TAuraActionCardModifyAttribute" || !InheritedAttribute(attribute) ||
                attrs == null || !attrs.TryGetValue(attribute, out value)) throw new FormatException("缺少可继承的实时光环属性 " + attribute);
            string operation = NativeSkillSupport.Text(action, "Operation");
            if (operation != "Add" && operation != "Multiply" && operation != "Set") throw new FormatException("未知附魔光环操作");
            if (!StaticValue(action["Value"])) throw new FormatException("尚不支持动态附魔光环");
            JsonValue target = action["Target"];
            string type = NativeSkillSupport.Type(target);
            if (type == "TTargetCardSelf" && NativeSkillSupport.Empty(target["Conditions"])) return;
            // Multi-card/conditional auras must be reconstructed against the
            // simulated board rather than assumed from one source's attributes.
            throw new FormatException("尚不支持非自身附魔光环");
        }

        private static bool StaticValue(JsonValue value)
        {
            string type = NativeSkillSupport.Type(value);
            if (type == "TFixedValue") return value["Value"].Kind == JsonKind.Number;
            if (type != "TReferenceValueCardAttribute" || NativeSkillSupport.Type(value["Target"]) != "TTargetCardSelf" ||
                !NativeSkillSupport.Empty(value["Target"]["Conditions"]) || !InheritedAttribute(NativeSkillSupport.Text(value, "AttributeType"))) return false;
            return NativeSkillSupport.Empty(value["Modifier"]) || StaticValue(value["Modifier"]["Value"]);
        }

        private static bool InheritedAttribute(string attribute)
        {
            string[] inherited = { "DamageAmount", "HealAmount", "ShieldApplyAmount", "BurnApplyAmount", "PoisonApplyAmount", "RegenApplyAmount",
                "CritChance", "Lifesteal", "LifestealPercentage", "CooldownMax", "AmmoMax", "Multicast", "HasteAmount", "HasteTargets", "SlowAmount", "SlowTargets",
                "FreezeAmount", "FreezeTargets", "ChargeAmount", "ChargeTargets" };
            return Array.IndexOf(inherited, attribute) >= 0;
        }
    }
}
