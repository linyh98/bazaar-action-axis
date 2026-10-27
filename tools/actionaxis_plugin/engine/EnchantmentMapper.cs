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
                List<TriggerDef> addedTriggers = new List<TriggerDef>();
                foreach (KeyValuePair<string, JsonValue> pair in native["Abilities"].AsObject())
                {
                    JsonValue ability = pair.Value;
                    CheckArea(ability);
                    if (!NativeSkillSupport.Empty(ability["Prerequisites"])) throw new FormatException("附魔能力含尚未支持的触发条件或次数限制");
                    string trigger = NativeSkillSupport.Type(ability["Trigger"]);
                    // 战斗外附魔分支仍按白名单跳过；同卡的战斗内能力必须保留。
                    if (SkillMapper.IsNonCombatTrigger(ability["Trigger"])) continue;
                    Effect effect = MapAction(ability["Action"], live.Attributes);
                    effect.FromEnchantment = true;
                    effect.Priority = NativeSkillSupport.Text(ability, "Priority");
                    if (trigger == "TTriggerOnCardFired")
                    {
                        added.Add(effect);
                        continue;
                    }
                    // 附魔被动（例如 Deadly 的 TTriggerOnItemUsed）与主动同样执行。
                    foreach (TriggerDef mapped in NativeSkillSupport.MapTriggers(ability["Trigger"]))
                    {
                        mapped.SkillRule.Prerequisites = ability["Prerequisites"];
                        mapped.Effects.Add(effect);
                        mapped.FromEnchantment = true;
                        addedTriggers.Add(mapped);
                    }
                }
                foreach (KeyValuePair<string, JsonValue> pair in native["Auras"].AsObject())
                    ValidateInheritedAura(pair.Value, live.Attributes);
                foreach (KeyValuePair<string, JsonValue> pair in native["Attributes"].AsObject())
                    if (!InheritedAttribute(pair.Key)) throw new FormatException("尚不支持附魔属性 " + pair.Key);

                CardDef result = source.Clone();
                result.EnchantmentName = live.Enchantment;
                result.NativeEnchantment = native;
                result.Effects = new List<Effect>(source.Effects);
                result.Effects.AddRange(added);
                result.Triggers = new List<TriggerDef>(source.Triggers);
                result.Triggers.AddRange(addedTriggers);
                result.Tags = new List<string>(source.Tags);
                foreach (JsonValue tag in native["Tags"].AsArray())
                    if (!result.Tags.Contains(tag.AsString())) result.Tags.Add(tag.AsString());
                result.HiddenTags = new List<string>(source.HiddenTags);
                foreach (JsonValue tag in native["HiddenTags"].AsArray())
                    if (!result.HiddenTags.Contains(tag.AsString())) result.HiddenTags.Add(tag.AsString());
                // 直接附魔属性（Radiant 的缓速/冻结抗性与摧毁免疫）必须落到
                // 实际结算字段，不能只作为快照里的诊断值保存。
                foreach (KeyValuePair<string, JsonValue> pair in native["Attributes"].AsObject())
                {
                    int value = pair.Value.AsInt();
                    result.LiveAttributes[pair.Key] = value;
                    if (pair.Key == "PercentSlowReduction") result.PercentSlowReduction = value;
                    else if (pair.Key == "PercentFreezeReduction") result.PercentFreezeReduction = value;
                    else if (pair.Key == "PercentHasteReduction") result.PercentHasteReduction = value;
                    else if (pair.Key == "DestroyImmunity") result.DestroyImmunity = value;
                }
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
                // 附魔主动效果与普通动作共享同一目标契约：AllHands、Absolute、
                // 位置、XMost 和 Occupying 不能各自维护一套近似解析。
                result.Target = SkillMapper.MapCardTarget(target);
                result.Seconds = ReadValue(raw["Value"], attrs, attribute) / 1000.0;
                if (!NativeSkillSupport.Empty(raw["TargetCount"]))
                    result.Count = (int)ReadValue(raw["TargetCount"], attrs, null);
                else if (result.Target.Kind != null && result.Target.Kind.StartsWith("all", StringComparison.Ordinal) ||
                    result.Target.PositionMode != null)
                    result.Count = int.MaxValue;
                else result.Count = 1;
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
            string[] inherited = { "DamageAmount", "HealAmount", "ShieldApplyAmount", "BurnApplyAmount", "PoisonApplyAmount", "RegenApplyAmount", "RageApplyAmount",
                "CritChance", "DamageCrit", "Lifesteal", "LifestealPercentage", "CooldownMax", "AmmoMax", "Multicast", "HasteAmount", "HasteTargets", "SlowAmount", "SlowTargets",
                "FreezeAmount", "FreezeTargets", "FlyingTargets", "EnchantTargets", "ChargeAmount", "ChargeTargets", "BurnRemoveAmount", "PoisonRemoveAmount",
                "SellPrice", "BuyPrice", "PercentSlowReduction", "PercentFreezeReduction", "PercentHasteReduction", "DestroyImmunity", "Heated", "Chilled" };
            return Array.IndexOf(inherited, attribute) >= 0;
        }
    }
}
