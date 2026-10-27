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
            // NativeRuntimeAuras 中的稳定下标，用于缓存随机光环目标。
            public int Index;
        }

        public static void Validate(JsonValue aura)
        {
            JsonValue action = aura["Action"];
            string type = NativeSkillSupport.Type(action);
            if (type == "TAuraActionCardModifyAttribute") { ValidateCardAura(aura); return; }
            if (type == "TAuraActionPlayerModifyAttribute") { ValidatePlayerAura(aura); return; }
            throw new FormatException("动态光环动作待支持 " + type);
        }

        // WorksIn=CombatOnly 的 SellPrice Add 光环在原生里于战斗开始时执行一次，
        // 之后每次使用不再叠加（02dae1ed：Abacus 帧 0 SellPrice 29→39，其后 5 次
        // 开火不再变化）。全库 CombatOnly Add SellPrice 卡牌光环（Abacus/Subscraper）
        // 都无前提。同源里引用这些被写属性的无前提 Add 光环按“新贡献-旧贡献”的
        // 增量重算（ShieldApplyAmount += SellPrice×Custom_0）。带前提的条件光环与
        // Multiply 类 CombatOnly 光环（Spacescraper ×3 等）没有逐次叠加证据，
        // 仍留在连续派生层。
        internal static List<JsonValue> ClassifyUseAuras(IList<JsonValue> auras)
        {
            List<JsonValue> result = new List<JsonValue>();
            HashSet<string> written = new HashSet<string>();
            foreach (JsonValue aura in auras)
            {
                JsonValue action = aura["Action"];
                if (NativeSkillSupport.Type(action) != "TAuraActionCardModifyAttribute") continue;
                if (NativeSkillSupport.Text(aura, "WorksIn") != "CombatOnly") continue;
                if (NativeSkillSupport.Text(action, "Operation") != "Add") continue;
                if (NativeSkillSupport.Text(action, "AttributeType") != "SellPrice") continue;
                if (!NativeSkillSupport.Empty(aura["Prerequisites"])) continue;
                if (!NativeSkillSupport.PrerequisitesSupported(aura["Prerequisites"])) continue;
                try { ValidateTarget(action["Target"]); ValidateValue(action["Value"]); }
                catch (FormatException) { continue; }
                result.Add(aura);
                written.Add(NativeSkillSupport.Text(action, "AttributeType"));
            }
            if (result.Count == 0) return result;
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (JsonValue aura in auras)
                {
                    if (result.Contains(aura)) continue;
                    JsonValue action = aura["Action"];
                    if (NativeSkillSupport.Type(action) != "TAuraActionCardModifyAttribute") continue;
                    string operation = NativeSkillSupport.Text(action, "Operation");
                    if (operation != "Add" && operation != "Subtract") continue;
                    if (!NativeSkillSupport.Empty(aura["Prerequisites"])) continue;
                    if (!ValueReferencesAny(action["Value"], written)) continue;
                    if (!NativeSkillSupport.PrerequisitesSupported(aura["Prerequisites"])) continue;
                    try { ValidateTarget(action["Target"]); ValidateValue(action["Value"]); }
                    catch (FormatException) { continue; }
                    result.Add(aura);
                    written.Add(NativeSkillSupport.Text(action, "AttributeType"));
                    grew = true;
                }
            }
            return result;
        }

        private static bool ValueReferencesAny(JsonValue value, HashSet<string> attributes)
        {
            if (NativeSkillSupport.Empty(value)) return false;
            if (attributes.Contains(NativeSkillSupport.Text(value, "AttributeType"))) return true;
            JsonValue modifier = value["Modifier"];
            return !NativeSkillSupport.Empty(modifier) && ValueReferencesAny(modifier["Value"], attributes);
        }

        // 战斗开始时执行本场全部 CombatOnly 开场光环（物品与技能）。叠加型
        // （CombatOnly SellPrice Add）在开场应用一次；依赖型（引用被写属性）按
        // “新贡献-已计入贡献”的增量在定点循环中收敛（SellPrice 喂给
        // ShieldApplyAmount，第二轮追平）。
        internal static void ExecuteOpeningAuras(BattleContext ctx)
        {
            if (!ctx.Config.NativeMechanics) return;
            List<InternalItem> sources = new List<InternalItem>(ctx.Items);
            sources.AddRange(ctx.Skills);
            foreach (InternalItem source in sources) ApplyOpeningAuras(ctx, source, 0);
        }

        private static void ApplyOpeningAuras(BattleContext ctx, InternalItem source, double time)
        {
            if (!ctx.Config.NativeMechanics || source.Destroyed) return;
            List<JsonValue> auras = source.Card.NativeUseAuras;
            if (auras == null || auras.Count == 0) return;
            if (source.UseAuraContributions == null)
            {
                source.UseAuraContributions = new Dictionary<string, double>();
                source.UseAuraTrackedIndexes = new HashSet<int>();
                HashSet<string> written = new HashSet<string>();
                foreach (JsonValue aura in auras)
                {
                    JsonValue action = aura["Action"];
                    if (NativeSkillSupport.Type(action) != "TAuraActionCardModifyAttribute") continue;
                    if (NativeSkillSupport.Text(aura, "WorksIn") == "CombatOnly" &&
                        NativeSkillSupport.Text(action, "Operation") == "Add")
                        written.Add(NativeSkillSupport.Text(action, "AttributeType"));
                }
                for (int index = 0; index < auras.Count; index++)
                {
                    JsonValue action = auras[index]["Action"];
                    if (NativeSkillSupport.Type(action) != "TAuraActionCardModifyAttribute") continue;
                    if (!ValueReferencesAny(action["Value"], written)) continue;
                    source.UseAuraTrackedIndexes.Add(index);
                    foreach (InternalItem target in Targets(ctx, source, action["Target"]))
                        source.UseAuraContributions[index + "#" + target.Id] =
                            JsMath.Round(Value(ctx, source, action["Value"], target));
                }
            }
            // 引用读取经过 ItemAttributes.Get（优先派生层 AuraAttributes），
            // 因此每一轮应用增量后都要重建派生层，后续轮次才能读到新值，
            // 账本快照也不会晚一帧。
            for (int pass = 0; pass < 16; pass++)
            {
                bool changedThisPass = false;
                for (int index = 0; index < auras.Count; index++)
                {
                    JsonValue aura = auras[index];
                    JsonValue action = aura["Action"];
                    if (NativeSkillSupport.Type(action) != "TAuraActionCardModifyAttribute") continue;
                    if (!NativeSkillSupport.PrerequisitesMatch(ctx, source,
                        new NativeSkillRule { Prerequisites = aura["Prerequisites"] })) continue;
                    string attribute = NativeSkillSupport.Text(action, "AttributeType");
                    bool tracked = source.UseAuraTrackedIndexes.Contains(index);
                    foreach (InternalItem target in Targets(ctx, source, action["Target"]))
                    {
                        if (target.Destroyed) continue;
                        double value = JsMath.Round(Value(ctx, source, action["Value"], target));
                        if (!tracked)
                        {
                            // 叠加型：每次使用只应用一次（首轮）。
                            if (pass > 0 || Math.Abs(value) < 1e-9) continue;
                            AddUseAuraDelta(ctx, target, attribute, value);
                            changedThisPass = true;
                            continue;
                        }
                        string trackedKey = index + "#" + target.Id;
                        double applied;
                        if (!source.UseAuraContributions.TryGetValue(trackedKey, out applied))
                        {
                            // 战斗中途新进入光环范围的目标：记当前贡献为基准。
                            source.UseAuraContributions[trackedKey] = value;
                            continue;
                        }
                        double delta = value - applied;
                        if (Math.Abs(delta) < 1e-9) continue;
                        source.UseAuraContributions[trackedKey] = value;
                        AddUseAuraDelta(ctx, target, attribute, delta);
                        changedThisPass = true;
                    }
                }
                if (!changedThisPass) break;
                Refresh(ctx);
            }
        }

        private static void AddUseAuraDelta(BattleContext ctx, InternalItem target, string attribute, double delta)
        {
            double prior;
            target.AttributeDeltas.TryGetValue(attribute, out prior);
            target.AttributeDeltas[attribute] = prior + delta;
        }


        private static void ValidateCardAura(JsonValue aura)
        {
            JsonValue action = aura["Action"];
            string attribute = NativeSkillSupport.Text(action, "AttributeType");
            string operation = NativeSkillSupport.Text(action, "Operation");
            if (operation != "Add" && operation != "Subtract" && operation != "Multiply")
                throw new FormatException("不可逆的持续属性光环待支持");
            // 人物条件（生命/暴怒/怒气）允许：Active() 每帧用 PrerequisitesMatch 重新判定。
            if (!NativeSkillSupport.PrerequisitesSupported(aura["Prerequisites"]))
                throw new FormatException("动态光环条件待支持");
            ValidateConditions(aura["Prerequisites"]);
            // RageApplyAmount 是一次怒气动作的数值；RageMax 是人物上限，两者不能混用。
            string[] runtime = { "DamageAmount", "ShieldApplyAmount", "HealAmount", "BurnApplyAmount", "PoisonApplyAmount", "RegenApplyAmount", "RageApplyAmount", "TempoApplyAmount", "CritChance", "DamageCrit", "Lifesteal", "LifestealPercentage", "Multicast", "AmmoMax", "CooldownMax", "CooldownDisabled", "FlatCooldownReduction", "PercentCooldownReduction", "ChargeAmount", "ChargeTargets", "HasteAmount", "HasteTargets", "SlowAmount", "SlowTargets", "FreezeAmount", "FreezeTargets", "FlyingTargets", "EnchantTargets", "ReloadAmount", "ReloadTargets", "BurnRemoveAmount", "PoisonRemoveAmount", "SellPrice", "BuyPrice", "PercentSlowReduction", "PercentFreezeReduction", "PercentHasteReduction", "DestroyImmunity", "Heated", "Chilled" };
            // 两个冷却缩减属性不进入属性表（实时 CooldownMax 已是有效值），
            // 而是由 Synchronize 推导有效冷却，避免重复扣减。
            bool cooldownReduction = attribute == "FlatCooldownReduction" || attribute == "PercentCooldownReduction";
            if (cooldownReduction && operation != "Add" && operation != "Subtract")
                throw new FormatException("冷却缩减光环仅支持 Add/Subtract");
            if (!cooldownReduction && Array.IndexOf(runtime, attribute) < 0 && !attribute.StartsWith("Custom_", StringComparison.Ordinal))
                throw new FormatException("动态光环属性待支持 " + attribute);
            ValidateTarget(action["Target"]);
            ValidateValue(action["Value"]);
            // 零倍率表示把目标置 0；实时快照的反推在 RemoveMultiplier 中
            // 优先用原生模板基底，无法还原时保留明确告警。
            // 只有“读取来源的同一属性”且“光环也作用于来源”时才会重复叠加。
            // Santoku 读取自身暴击但写给左卡；Giant Sub 读取其他食物、写给自己。
            if (ReferencesAttribute(action["Value"], attribute) &&
                ValueTargetsSource(action["Value"]) && TargetCanIncludeSource(action["Target"]))
                throw new FormatException("光环自引用属性待支持");
        }

        private static void ValidatePlayerAura(JsonValue aura)
        {
            JsonValue action = aura["Action"];
            string attribute = NativeSkillSupport.Text(action, "AttributeType");
            string operation = NativeSkillSupport.Text(action, "Operation");
            if (!NativeSkillSupport.PlayerAttributeValueSupported(attribute))
                throw new FormatException("动态人物光环属性待支持 " + attribute);
            if (operation != "Add" && operation != "Subtract" && operation != "Multiply" && operation != "AdditiveMultiply")
                throw new FormatException("人物光环操作待支持 " + operation);
            if (!NativeSkillSupport.PrerequisitesSupported(aura["Prerequisites"]))
                throw new FormatException("动态人物光环条件待支持");
            ValidateConditions(aura["Prerequisites"]);
            JsonValue target = action["Target"];
            string targetType = NativeSkillSupport.Type(target), mode = NativeSkillSupport.Text(target, "TargetMode");
            if (targetType != "TTargetPlayerRelative" && targetType != "TTargetPlayerAbsolute")
                throw new FormatException("人物光环目标类型待支持 " + targetType);
            if (mode != "Self" && mode != "Player" && mode != "Opponent")
                throw new FormatException("人物光环目标待支持 " + mode);
            if (!NativeSkillSupport.Empty(target["Conditions"]))
                throw new FormatException("人物光环目标条件待支持");
            ValidateValue(action["Value"]);
            if (ReferencesAttribute(action["Value"], attribute))
                throw new FormatException("人物光环自引用属性待支持");
        }

        private static bool ReferencesAttribute(JsonValue value, string attribute)
        {
            return NativeSkillSupport.Text(value, "AttributeType") == attribute ||
                !NativeSkillSupport.Empty(value["Modifier"]) && ReferencesAttribute(value["Modifier"]["Value"], attribute);
        }

        private static bool ValueTargetsSource(JsonValue value)
        {
            if (NativeSkillSupport.Empty(value)) return false;
            string type = NativeSkillSupport.Type(value);
            if (type == "TReferenceValueCardAttribute" || type == "TReferenceValueCardAttributeUnscaled" ||
                type == "TReferenceValueCardAttributeAggregate")
            {
                JsonValue target = value["Target"];
                // Target:null 按当前光环目标逐目标求值；只有链式引用时才等于来源。
                return !NativeSkillSupport.Empty(target) && TargetCanIncludeSource(target);
            }
            if (type == "TReferenceValueCardCount" || type == "TReferenceValueCardTagCount")
                return TargetCanIncludeSource(value["Target"]);
            return false;
        }

        private static bool TargetCanIncludeSource(JsonValue target)
        {
            string type = NativeSkillSupport.Type(target);
            if (type == "TTargetCardSelf") return true;
            if (type == "TTargetCardPositional") return target["IncludeOrigin"].AsBool();
            if (type == "TTargetCardSection" || type == "TTargetCardRandom" || type == "TTargetCardXMost")
                return !target["ExcludeSelf"].AsBool();
            return false;
        }

        private static void ValidateTarget(JsonValue target)
        {
            string type = NativeSkillSupport.Type(target), section = NativeSkillSupport.Text(target, "TargetSection");
            if (!NativeSkillSupport.ConditionSupported(target["Conditions"])) throw new FormatException("动态光环目标条件待支持");
            ValidateConditions(target["Conditions"]);
            if (type == "TTargetCardSelf") return;
            // 商店/远征选择集不是战斗目标；按场外 no-op 跳过，不把整卡回退。
            if (type == "TTargetCardSection" && section == "SelectionSet") return;
            if (type == "TTargetCardOccupying") return;
            if (type == "TTargetCardXMost")
            {
                SkillMapper.MapCardTarget(target);
                return;
            }
            if (type == "TTargetCardPositional" && NativeSkillSupport.Text(target, "Origin") == "Self" &&
                NativeSkillSupport.PositionModeSupported(NativeSkillSupport.Text(target, "TargetMode"))) return;
            if (type == "TTargetCardRandom" && NativeSkillSupport.SectionSupported(section)) return;
            if (type == "TTargetCardSection" && NativeSkillSupport.SectionSupported(section)) return;
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
            else if (type == "TReferenceValuePlayerAttribute" || type == "TReferenceValuePlayerAttributeUnscaled")
            {
                SkillMapper.MapHeroTarget(value["Target"]);
                string attribute = NativeSkillSupport.Text(value, "AttributeType");
                if (!NativeSkillSupport.PlayerAttributeValueSupported(attribute))
                    throw new FormatException("光环人物属性未支持 " + attribute);
            }
            else if (type == "TReferenceValueCardCount" || type == "TReferenceValueCardTagCount") ValidateTarget(value["Target"]);
            else if (type == "TReferenceValueCardAttribute" || type == "TReferenceValueCardAttributeUnscaled")
            {
                if (string.IsNullOrEmpty(NativeSkillSupport.Text(value, "AttributeType")))
                    throw new FormatException("光环属性引用缺少属性名");
                // Target:null 表示逐目标读取当前光环作用卡；显式目标必须可解析。
                if (!NativeSkillSupport.Empty(value["Target"])) SkillMapper.MapCardTarget(value["Target"]);
            }
            else if (type == "TReferenceValueCardAttributeAggregate")
            {
                if (string.IsNullOrEmpty(NativeSkillSupport.Text(value, "AttributeType")))
                    throw new FormatException("光环聚合引用缺少属性名");
                if (!NativeSkillSupport.Empty(value["Target"])) SkillMapper.MapCardTarget(value["Target"]);
            }
            else throw new FormatException("动态光环数值引用待支持 " + type);
            if (!NativeSkillSupport.Empty(value["Modifier"]))
            {
                string mode = NativeSkillSupport.Text(value["Modifier"], "ModifyMode");
                if (mode != "Add" && mode != "Subtract" && mode != "Multiply") throw new FormatException("动态光环数值修正待支持");
                ValidateValue(value["Modifier"]["Value"]);
            }
        }

        private static void ValidateConditions(JsonValue raw)
        {
            if (NativeSkillSupport.Empty(raw)) return;
            if (raw.Kind == JsonKind.Array) { foreach (JsonValue child in raw.AsArray()) ValidateConditions(child); return; }
            if (raw.Kind != JsonKind.Object) return;
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
                for (int index = 0; index < source.Card.NativeRuntimeAuras.Count; index++)
                {
                    JsonValue raw = source.Card.NativeRuntimeAuras[index];
                    Aura aura = new Aura { Source = source, Raw = raw, Index = index };
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

        private static List<InternalItem> Participants(BattleContext ctx)
        {
            List<InternalItem> result = new List<InternalItem>(ctx.Items);
            result.AddRange(ctx.Skills);
            return result;
        }

        // 动态人物光环是独立派生层。先按“当前值 - 已计入光环”求基底，
        // 再从同一基底重算；条件来回开关时不会把 +N 叠成 2N。
        private static void ApplyHeroAuras(BattleContext ctx, List<Aura> auras)
        {
            Dictionary<Side, double> maxHp = new Dictionary<Side, double>();
            Dictionary<Side, double> regen = new Dictionary<Side, double>();
            Dictionary<Side, double> rageMax = new Dictionary<Side, double>();
            Dictionary<Side, double> enragedMs = new Dictionary<Side, double>();
            Dictionary<Side, double> reduction = new Dictionary<Side, double>();
            Dictionary<Side, double> crit = new Dictionary<Side, double>();
            foreach (Side side in new[] { Side.Player, Side.Enemy })
            {
                InternalHero hero = ctx.Hero(side);
                maxHp[side] = hero.MaxHp - hero.AuraMaxHp;
                regen[side] = hero.Regen - hero.AuraRegen;
                rageMax[side] = hero.RageMax - hero.AuraRageMax;
                enragedMs[side] = hero.EnragedDurationMs - hero.AuraEnragedDurationMs;
                reduction[side] = 0;
                crit[side] = 0;
            }
            foreach (Aura aura in auras)
            {
                JsonValue action = aura.Raw["Action"];
                if (NativeSkillSupport.Type(action) != "TAuraActionPlayerModifyAttribute") continue;
                if (!Active(ctx, aura)) continue;
                string mode = NativeSkillSupport.Text(action["Target"], "TargetMode");
                string targetType = NativeSkillSupport.Type(action["Target"]);
                Side side = targetType == "TTargetPlayerAbsolute"
                    ? (mode == "Opponent" ? Side.Enemy : Side.Player)
                    : (mode == "Opponent" ? SideUtil.Opposing(aura.Source.Side) : aura.Source.Side);
                string attribute = NativeSkillSupport.Text(action, "AttributeType");
                string operation = NativeSkillSupport.Text(action, "Operation");
                double value = Value(ctx, aura.Source, action["Value"], aura.Source);
                if (attribute == "HealthMax") maxHp[side] = Operate(maxHp[side], value, operation);
                else if (attribute == "HealthRegen") regen[side] = Operate(regen[side], value, operation);
                else if (attribute == "RageMax") rageMax[side] = Operate(rageMax[side], value, operation);
                else if (attribute == "EnragedDurationMax") enragedMs[side] = Operate(enragedMs[side], value, operation);
                else if (attribute == "PercentDamageReduction") reduction[side] = Operate(reduction[side], value, operation);
                else if (attribute == "CritChance") crit[side] = Operate(crit[side], value, operation);
            }
            foreach (Side side in new[] { Side.Player, Side.Enemy })
            {
                InternalHero hero = ctx.Hero(side);
                double baseMax = hero.MaxHp - hero.AuraMaxHp;
                double wantedMax = Math.Max(1, maxHp[side]);
                double nextMaxBonus = wantedMax - baseMax;
                if (Math.Abs(nextMaxBonus - hero.AuraMaxHp) > 1e-9)
                {
                    double gained = nextMaxBonus - hero.AuraMaxHp;
                    hero.MaxHp = wantedMax;
                    // 与 TActionPlayerModifyAttribute(HealthMax) 一致：上限成长同步抬高当前生命。
                    if (gained > 0) hero.Hp = Math.Min(hero.MaxHp, hero.Hp + gained);
                    else hero.Hp = Math.Min(hero.Hp, hero.MaxHp);
                    hero.AuraMaxHp = nextMaxBonus;
                }
                double baseRegen = hero.Regen - hero.AuraRegen;
                double wantedRegen = Math.Max(0, regen[side]);
                hero.AuraRegen = wantedRegen - baseRegen;
                hero.Regen = wantedRegen;
                double baseRage = hero.RageMax - hero.AuraRageMax;
                double wantedRage = Math.Max(0, rageMax[side]);
                hero.AuraRageMax = wantedRage - baseRage;
                hero.RageMax = wantedRage;
                double baseEnraged = hero.EnragedDurationMs - hero.AuraEnragedDurationMs;
                double wantedEnraged = Math.Max(0, enragedMs[side]);
                hero.AuraEnragedDurationMs = wantedEnraged - baseEnraged;
                hero.EnragedDurationMs = wantedEnraged;
                hero.AuraPercentDamageReduction = Math.Min(100, Math.Max(0, reduction[side]));
                hero.AuraCritChance = Math.Max(0, crit[side]);
            }
        }

        private static double Operate(double before, double value, string operation)
        {
            if (operation == "Add") return before + value;
            if (operation == "Subtract") return before - value;
            if (operation == "AdditiveMultiply") return before * (1 + value);
            if (operation == "Multiply") return before * value;
            return value;
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
            Dictionary<InternalItem, bool> seededMulticast = new Dictionary<InternalItem, bool>();
            foreach (InternalItem item in Participants(ctx))
            {
                flying[item] = item.Flying;
                Dictionary<string, int> seed = item.Card.NativeAuraSeedAttributes ?? item.Card.LiveAttributes;
                seededMulticast[item] = seed.ContainsKey("Multicast");
                item.AuraBaseAttributes = new Dictionary<string, double>();
                foreach (KeyValuePair<string, int> pair in seed) item.AuraBaseAttributes[pair.Key] = pair.Value;
                // 实时 CooldownMax 已是游戏计算后的有效值，推导层必须有基准。
                if (!item.AuraBaseAttributes.ContainsKey("CooldownMax")) item.AuraBaseAttributes["CooldownMax"] = item.CooldownMax * 1000;
                // Shiny 等 +1 Multicast 的隐含基底是“1 + 静态多重”；没有显式
                // Multicast 字段的卡不能把 0+1 当成总共 1 次，也不能吞掉旧卡效果。
                if (!item.AuraBaseAttributes.ContainsKey("Multicast")) item.AuraBaseAttributes["Multicast"] = BaseMulticast(item.Card);
                if (item.Card.NativeAttributesAreLive)
                {
                    int flag;
                    item.Flying = seed.TryGetValue("Flying", out flag) && flag > 0;
                    item.FlyingSince = item.Flying ? 0 : -1;
                    foreach (KeyValuePair<string, int> pair in seed) item.AuraAttributes[pair.Key] = pair.Value;
                }
            }
            AnchorLiveHeroAuras(ctx, auras);
            // 先反推百分比/固定缩减（包含当时生效的 CooldownMax 光环），
            // 再由下面的逆序循环减去 CooldownMax 类光环，顺序不能颠倒。
            RemoveInheritedCooldownReduction(ctx, auras);
            for (int index = auras.Count - 1; index >= 0; index--)
            {
                Aura aura = auras[index];
                if (!Active(ctx, aura)) continue;
                JsonValue action = aura.Raw["Action"];
                if (NativeSkillSupport.Type(action) != "TAuraActionCardModifyAttribute") continue;
                string attribute = NativeSkillSupport.Text(action, "AttributeType");
                if (attribute == "FlatCooldownReduction" || attribute == "PercentCooldownReduction") continue;
                foreach (InternalItem target in Targets(ctx, aura, action["Target"]))
                {
                    if (!target.Card.NativeAttributesAreLive) continue;
                    // 实时快照没有 Multicast 时，上面的隐含基底没有被 Shiny 光环
                    // 加过；不能在这一步再从 1 里减掉一次，否则会变成 0。
                    if (attribute == "Multicast" && !seededMulticast[target]) continue;
                    double value = Value(ctx, aura.Source, action["Value"], target);
                    double before;
                    target.AuraBaseAttributes.TryGetValue(attribute, out before);
                    string operation = NativeSkillSupport.Text(action, "Operation");
                    if (operation == "Multiply")
                        target.AuraBaseAttributes[attribute] = RemoveMultiplier(ctx, auras, target, attribute, before, value);
                    else if (operation == "Subtract")
                        target.AuraBaseAttributes[attribute] = before + value;
                    else
                        target.AuraBaseAttributes[attribute] = before - value;
                }
            }
            foreach (InternalItem item in Participants(ctx))
            {
                item.Flying = flying[item];
                item.AuraAttributes.Clear();
            }
            Refresh(ctx);
        }

        private static void AnchorLiveHeroAuras(BattleContext ctx, List<Aura> auras)
        {
            Dictionary<Side, Dictionary<string, double>> bases = new Dictionary<Side, Dictionary<string, double>>();
            foreach (Side side in new[] { Side.Player, Side.Enemy })
            {
                bool live = side == Side.Player ? ctx.Config.PlayerHeroAttributesAreLive : ctx.Config.EnemyHeroAttributesAreLive;
                Dictionary<string, int> known = side == Side.Player ? ctx.Config.PlayerAttributes : ctx.Config.EnemyAttributes;
                InternalHero hero = ctx.Hero(side);
                Dictionary<string, double> attributes = new Dictionary<string, double>();
                if (live)
                    foreach (string attribute in new[] { "HealthMax", "HealthRegen", "RageMax", "EnragedDurationMax" })
                        if (known.ContainsKey(attribute))
                            attributes[attribute] = attribute == "HealthMax" ? hero.MaxHp : attribute == "HealthRegen" ? hero.Regen :
                                attribute == "RageMax" ? hero.RageMax : hero.EnragedDurationMs;
                bases[side] = attributes;
            }
            for (int i = auras.Count - 1; i >= 0; i--)
            {
                Aura aura = auras[i];
                JsonValue action = aura.Raw["Action"];
                if (NativeSkillSupport.Type(action) != "TAuraActionPlayerModifyAttribute" || !Active(ctx, aura)) continue;
                string mode = NativeSkillSupport.Text(action["Target"], "TargetMode");
                Side side = NativeSkillSupport.Type(action["Target"]) == "TTargetPlayerAbsolute"
                    ? (mode == "Opponent" ? Side.Enemy : Side.Player)
                    : (mode == "Opponent" ? SideUtil.Opposing(aura.Source.Side) : aura.Source.Side);
                string attribute = NativeSkillSupport.Text(action, "AttributeType");
                double before;
                if (!bases[side].TryGetValue(attribute, out before)) continue;
                double value = Value(ctx, aura.Source, action["Value"], aura.Source);
                string operation = NativeSkillSupport.Text(action, "Operation");
                if (operation == "Add") before -= value;
                else if (operation == "Subtract") before += value;
                else
                {
                    double factor = operation == "AdditiveMultiply" ? 1 + value : value;
                    if (factor <= 0) continue;
                    before /= factor;
                }
                bases[side][attribute] = before;
            }
            foreach (Side side in new[] { Side.Player, Side.Enemy })
            {
                InternalHero hero = ctx.Hero(side);
                double value;
                if (bases[side].TryGetValue("HealthMax", out value)) hero.AuraMaxHp = hero.MaxHp - value;
                if (bases[side].TryGetValue("HealthRegen", out value)) hero.AuraRegen = hero.Regen - value;
                if (bases[side].TryGetValue("RageMax", out value)) hero.AuraRageMax = hero.RageMax - value;
                if (bases[side].TryGetValue("EnragedDurationMax", out value)) hero.AuraEnragedDurationMs = hero.EnragedDurationMs - value;
            }
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
                    if (NativeSkillSupport.Type(action) != "TAuraActionCardModifyAttribute") continue;
                    string attribute = NativeSkillSupport.Text(action, "AttributeType");
                    if (attribute != "FlatCooldownReduction" && attribute != "PercentCooldownReduction") continue;
                    if (!Targets(ctx, aura, action["Target"]).Contains(target)) continue;
                    double value = Value(ctx, aura.Source, action["Value"], target);
                    double signed = NativeSkillSupport.Text(action, "Operation") == "Subtract" ? -value : value;
                    if (attribute == "FlatCooldownReduction") flat += signed; else percent += signed;
                }
                // Live reduction fields include both permanent gains and aura
                // contributions. Strip the latter from their attribute bases,
                // and unscale CooldownMax with the captured total exactly once.
                double capturedFlat, capturedPercent;
                if (!target.AuraBaseAttributes.TryGetValue("FlatCooldownReduction", out capturedFlat)) capturedFlat = flat;
                if (!target.AuraBaseAttributes.TryGetValue("PercentCooldownReduction", out capturedPercent)) capturedPercent = percent;
                target.AuraBaseAttributes["FlatCooldownReduction"] = capturedFlat - flat;
                target.AuraBaseAttributes["PercentCooldownReduction"] = capturedPercent - percent;
                flat = capturedFlat;
                percent = capturedPercent;
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
                target.AuraBaseAttributes["CooldownMax"] = UnscaledCooldownMs(current, flat, percent);
            }
        }

        public static void Refresh(BattleContext ctx)
        {
            if (!ctx.Config.NativeMechanics || ctx.EvaluatingDynamicAuras) return;
            if (!ctx.DynamicAurasInitialized) { Initialize(ctx); return; }
            List<Aura> auras = All(ctx);
            List<InternalItem> participants = Participants(ctx);
            if (auras.Count == 0 && !participants.Exists(delegate(InternalItem item) { return item.AuraBaseAttributes != null; })) return;
            ctx.EvaluatingDynamicAuras = true;
            try
            {
                ApplyHeroAuras(ctx, auras);
                // Values may depend on another aura's output, e.g. Shield equal
                // to Damage after Damage doubles. Rebuild from the same base on
                // each pass, so Multiply never compounds across refreshes.
                bool stable = false;
                for (int pass = 0; pass < 16; pass++)
                {
                    Dictionary<InternalItem, Dictionary<string, double>> next = new Dictionary<InternalItem, Dictionary<string, double>>();
                    Dictionary<InternalItem, double> flatCooldown = new Dictionary<InternalItem, double>();
                    Dictionary<InternalItem, double> percentCooldown = new Dictionary<InternalItem, double>();
                    Dictionary<InternalItem, double> slowResistance = new Dictionary<InternalItem, double>();
                    Dictionary<InternalItem, double> freezeResistance = new Dictionary<InternalItem, double>();
                    Dictionary<InternalItem, double> hasteResistance = new Dictionary<InternalItem, double>();
                    Dictionary<InternalItem, double> destroyImmunity = new Dictionary<InternalItem, double>();
                    foreach (InternalItem item in participants)
                    {
                        Dictionary<string, double> attrs = new Dictionary<string, double>();
                        if (item.AuraBaseAttributes != null)
                            foreach (KeyValuePair<string, double> pair in item.AuraBaseAttributes)
                            {
                                if (IsResistanceAttribute(pair.Key)) continue;
                                attrs[pair.Key] = pair.Value;
                            }
                        else foreach (KeyValuePair<string, int> pair in item.Card.LiveAttributes)
                        {
                            if (IsResistanceAttribute(pair.Key)) continue;
                            attrs[pair.Key] = pair.Value;
                        }
                        foreach (KeyValuePair<string, double> pair in item.AttributeDeltas)
                        {
                            if (IsResistanceAttribute(pair.Key)) continue;
                            // CooldownMax 的运行时偏移由 EffectiveCooldownMaxMs 统一折算，
                            // 不能先进属性表再被冷却缩减乘一次。
                            if (pair.Key == "CooldownMax") continue;
                            double prior; attrs.TryGetValue(pair.Key, out prior); attrs[pair.Key] = prior + pair.Value;
                        }
                        // 所有物品的隐含 Multicast 基底是“1 + 静态多重”；光环/附魔
                        // 的 +N 必须叠加在这个基底上，而不是从 0 开始。
                        if (!attrs.ContainsKey("Multicast")) attrs["Multicast"] = BaseMulticast(item.Card);
                        next[item] = attrs;
                        // 抗性/免疫的隐含基础只来自映射期清洗过的卡面字段；
                        // 实时快照里的飞行 50% 等派生值不能作为光环基底。
                        slowResistance[item] = CleanResistance(item, "PercentSlowReduction");
                        freezeResistance[item] = CleanResistance(item, "PercentFreezeReduction");
                        hasteResistance[item] = CleanResistance(item, "PercentHasteReduction");
                        destroyImmunity[item] = CleanResistance(item, "DestroyImmunity");
                    }
                    foreach (Aura aura in auras)
                    {
                        if (!Active(ctx, aura)) continue;
                        JsonValue action = aura.Raw["Action"];
                        if (NativeSkillSupport.Type(action) != "TAuraActionCardModifyAttribute") continue;
                        string attr = NativeSkillSupport.Text(action, "AttributeType");
                        string operation = NativeSkillSupport.Text(action, "Operation");
                        foreach (InternalItem target in Targets(ctx, aura, action["Target"]))
                        {
                            // 引用值为 Target:null 时每个目标读取自身属性；
                            // 聚合引用也在目标集上求和，不能提前冻结成开场常量。
                            double value = Value(ctx, aura.Source, action["Value"], target);
                            if (attr == "FlatCooldownReduction" || attr == "PercentCooldownReduction")
                            {
                                Dictionary<InternalItem, double> sink = attr == "FlatCooldownReduction" ? flatCooldown : percentCooldown;
                                double prior; sink.TryGetValue(target, out prior);
                                sink[target] = prior + (operation == "Subtract" ? -value : value);
                                continue;
                            }
                            Dictionary<InternalItem, double> resistance = attr == "PercentSlowReduction" ? slowResistance :
                                attr == "PercentFreezeReduction" ? freezeResistance :
                                attr == "PercentHasteReduction" ? hasteResistance :
                                attr == "DestroyImmunity" ? destroyImmunity : null;
                            if (resistance != null)
                            {
                                // 抗性/免疫光环单独派生，不写回属性表，避免实时
                                // 快照与光环叠加出双倍抗性。
                                double prior; resistance.TryGetValue(target, out prior);
                                resistance[target] = operation == "Multiply" ? prior * value :
                                    operation == "Subtract" ? prior - value : prior + value;
                                continue;
                            }
                            double before; next[target].TryGetValue(attr, out before);
                            // Native multiplication converts the result to an
                            // integer by truncation (257 * 1.5 becomes 385).
                            next[target][attr] = operation == "Multiply" ? Math.Truncate(before * value) :
                                JsMath.Round(Effects.Operate(before, value, operation));
                        }
                    }
                    stable = true;
                    foreach (InternalItem item in participants)
                    {
                        double auraFlat, auraPercent, baseFlat, basePercent;
                        flatCooldown.TryGetValue(item, out auraFlat);
                        percentCooldown.TryGetValue(item, out auraPercent);
                        next[item].TryGetValue("FlatCooldownReduction", out baseFlat);
                        next[item].TryGetValue("PercentCooldownReduction", out basePercent);
                        // Base Rage Effect（4472da8a）是人物级光环，不在卡牌
                        // NativeRuntimeAuras 里；暴怒期间把 +10% 并入百分比，
                        // 由 EffectiveCooldownMs 统一按 float32 截断重算
                        // （034c3648 帧 301：3960 -> 3559）。
                        // 原生 Base Rage 光环只作用于 SelfHand 卡牌；插槽效果
                        // 不进手牌属性表，不会出现 PercentCooldownReduction 行。
                        double ragePercent = item.BaseRageCooldownMax >= 0 &&
                            !NativeSkillSupport.IsSocketEffect(item) ? Effects.BaseRageCooldownPercent : 0;
                        next[item]["FlatCooldownReduction"] = baseFlat + auraFlat;
                        next[item]["PercentCooldownReduction"] = basePercent + auraPercent + ragePercent;
                        foreach (KeyValuePair<string, double> pair in next[item])
                        {
                            if (pair.Key == "Flying") continue;
                            // CooldownMax 在属性表里是反推基底，ItemAttributes.Get 返回
                            // 有效值（差一次冷却缩减），不能拿两者比较收敛性。
                            if (pair.Key == "CooldownMax") continue;
                            if (Math.Abs(ItemAttributes.Get(item, pair.Key) - pair.Value) > 1e-7) stable = false;
                        }
                        item.AuraAttributes = next[item];
                        item.AuraCooldownFlat = baseFlat + auraFlat;
                        item.AuraCooldownPercent = basePercent + auraPercent + ragePercent;
                        item.AuraSlowResistance = AuraGain(item, slowResistance, "PercentSlowReduction");
                        item.AuraFreezeResistance = AuraGain(item, freezeResistance, "PercentFreezeReduction");
                        item.AuraHasteResistance = AuraGain(item, hasteResistance, "PercentHasteReduction");
                        item.AuraDestroyImmunity = AuraGain(item, destroyImmunity, "DestroyImmunity");
                    }
                    if (stable) break;
                }
                ApplyTagAuras(ctx, auras);
                if (!stable && !ctx.Warnings.Contains("动态光环存在循环依赖，预测可能不准确")) ctx.Warnings.Add("动态光环存在循环依赖，预测可能不准确");
                foreach (InternalItem item in participants) Synchronize(item);
            }
            finally { ctx.EvaluatingDynamicAuras = false; }
        }

        // 标签光环（TAuraActionCardAddTagsList）的目标集合随棋盘变化（It's a Drone
        // 给“最左手牌”），静态烘焙会把标签永久留在开场那张卡上。这里每帧重建
        // AuraTags：目标获得标签、离开集合即移除；原生在变化帧重发该卡 tags 元数据
        // （07f044ee 帧 140：Drone 从被禁用的 The Big One 移到变形副本，帧 142 再
        // 移到 OBLIVION VORTEX）。开场首次建立只写状态，不产生变更事件。
        private static void ApplyTagAuras(BattleContext ctx, List<Aura> auras)
        {
            Dictionary<InternalItem, List<string>> desired = new Dictionary<InternalItem, List<string>>();
            foreach (Aura aura in auras)
            {
                if (!Active(ctx, aura)) continue;
                JsonValue action = aura.Raw["Action"];
                if (NativeSkillSupport.Type(action) != "TAuraActionCardAddTagsList") continue;
                List<string> names = new List<string>();
                foreach (JsonValue tag in action["Tags"].AsArray())
                {
                    string name = tag.AsString();
                    if (!string.IsNullOrEmpty(name)) names.Add(name);
                }
                if (names.Count == 0) continue;
                foreach (InternalItem target in Targets(ctx, aura, action["Target"]))
                {
                    List<string> list;
                    if (!desired.TryGetValue(target, out list)) { list = new List<string>(); desired[target] = list; }
                    foreach (string name in names) if (!list.Contains(name)) list.Add(name);
                }
            }
            foreach (InternalItem item in Participants(ctx))
            {
                List<string> wanted;
                if (!desired.TryGetValue(item, out wanted)) wanted = new List<string>();
                if (!item.TagAurasInitialized)
                {
                    item.TagAurasInitialized = true;
                    item.AuraTags = new List<string>(wanted);
                    continue;
                }
                if (SameTagSet(item.AuraTags, wanted)) continue;
                item.AuraTags = new List<string>(wanted);
                BattleEvent ev = new BattleEvent { Time = ctx.Now, Side = item.Side, TargetSide = item.Side,
                    TargetItemId = item.Id, Kind = "tags", Note = string.Join(",", wanted.ToArray()) };
                ctx.Events.Add(Effects.FinishEvent(ctx, ev));
            }
        }

        private static bool SameTagSet(List<string> left, List<string> right)
        {
            if (left == null || right == null) return (left == null ? 0 : left.Count) == (right == null ? 0 : right.Count);
            if (left.Count != right.Count) return false;
            for (int index = 0; index < left.Count; index++)
                if (!string.Equals(left[index], right[index], StringComparison.Ordinal)) return false;
            return true;
        }

        public static void ValidateTagAura(JsonValue aura)
        {
            JsonValue action = aura["Action"];
            if (NativeSkillSupport.Type(action) != "TAuraActionCardAddTagsList")
                throw new FormatException("动态标签光环动作待支持");
            JsonValue tags = action["Tags"];
            if (tags.Kind != JsonKind.Array || tags.Count == 0) throw new FormatException("动态标签光环标签为空");
            foreach (JsonValue tag in tags.AsArray())
                if (string.IsNullOrEmpty(tag.AsString())) throw new FormatException("动态标签光环标签缺失");
            if (!NativeSkillSupport.PrerequisitesSupported(aura["Prerequisites"]))
                throw new FormatException("动态标签光环条件待支持");
            ValidateTarget(action["Target"]);
        }

        private static double RemoveMultiplier(BattleContext ctx, List<Aura> auras, InternalItem target, string attribute, double captured, double factor)
        {
            // For factors >= 1 there is at most one integer input whose native
            // truncated product equals the snapshot. Never invent a fractional
            // base such as 385 / 1.5 = 256.666... when the real base was 257.
            if (Math.Abs(factor) < 1e-9)
            {
                if (!NativeSkillSupport.Empty(target.Card.NativeTemplate))
                {
                    Dictionary<string, int> template = MonsterTemplateExtractor.TierAttributes(target.Card.NativeTemplate, target.Card.Tier);
                    int raw;
                    if (template.TryGetValue(attribute, out raw)) return raw;
                }
                string zeroWarning = target.Card.DisplayName() + "：零倍率光环无法还原 " + attribute + " 的实时基底，按 0 处理";
                if (!ctx.Warnings.Contains(zeroWarning)) ctx.Warnings.Add(zeroWarning);
                return 0;
            }
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
                        if (Targets(ctx, aura, action["Target"]).Contains(target)) expected += Value(ctx, aura.Source, action["Value"], target);
                    }
                    if (Math.Abs(Math.Truncate(expected * factor) - captured) < 1e-9) return Math.Truncate(expected);
                }
            }
            string warning = target.Card.DisplayName() + "：实时属性 " + attribute + " 的光环舍入无法精确还原，预测使用整数近似";
            if (!ctx.Warnings.Contains(warning)) ctx.Warnings.Add(warning);
            return candidate;
        }

        // 原生 CooldownMax 面板值：光环基底 + 未并入的运行时偏移，再按冷却缩减
        // 折算成当前有效冷却。Get(CooldownMax)、数值引用与 Synchronize 共用。
        internal static double EffectiveCooldownMaxMs(InternalItem item)
        {
            if (item == null) return 0;
            double baseMs;
            int live;
            if (item.AuraAttributes != null && item.AuraAttributes.TryGetValue("CooldownMax", out baseMs)) { }
            else if (item.AuraBaseAttributes != null && item.AuraBaseAttributes.TryGetValue("CooldownMax", out baseMs)) { }
            else if (item.Card != null && item.Card.LiveAttributes != null &&
                item.Card.LiveAttributes.TryGetValue("CooldownMax", out live)) baseMs = live;
            else baseMs = item.CooldownMax * 1000;
            double delta;
            if (item.AttributeDeltas != null && item.AttributeDeltas.TryGetValue("CooldownMax", out delta)) baseMs += delta;
            double flat = 0, percent = 0;
            if (item.AuraAttributes != null)
            {
                item.AuraAttributes.TryGetValue("FlatCooldownReduction", out flat);
                item.AuraAttributes.TryGetValue("PercentCooldownReduction", out percent);
            }
            return EffectiveCooldownMs(baseMs, flat, percent);
        }

        private static bool IsResistanceAttribute(string attribute)
        {
            return attribute == "PercentSlowReduction" || attribute == "PercentFreezeReduction" ||
                attribute == "PercentHasteReduction" || attribute == "DestroyImmunity";
        }

        private static double CleanResistance(InternalItem item, string attribute)
        {
            if (item.Card == null) return 0;
            if (attribute == "PercentSlowReduction") return item.Card.PercentSlowReduction;
            if (attribute == "PercentFreezeReduction") return item.Card.PercentFreezeReduction;
            if (attribute == "PercentHasteReduction") return item.Card.PercentHasteReduction;
            if (attribute == "DestroyImmunity") return item.Card.DestroyImmunity;
            return 0;
        }

        private static double AuraGain(InternalItem item, Dictionary<InternalItem, double> sink, string attribute)
        {
            double value;
            return sink.TryGetValue(item, out value) ? value - CleanResistance(item, attribute) : 0;
        }

        // 原生属性面板显示 = 卡面基础 + 光环增量 + 运行时增量，不按 100 截断；
        // 实际减免仍由 SlowResistance 等截断到 [0,100]（07f044ee 帧 60：液氮锤
        // 基础 100 + 飞行 50 = 面板 150，实际仍按 100% 减免）。
        public static double ResistanceDisplay(InternalItem item, string attribute)
        {
            if (item == null || item.Card == null) return 0;
            double value = CleanResistance(item, attribute);
            value += attribute == "PercentSlowReduction" ? item.AuraSlowResistance :
                attribute == "PercentFreezeReduction" ? item.AuraFreezeResistance :
                attribute == "PercentHasteReduction" ? item.AuraHasteResistance :
                attribute == "DestroyImmunity" ? item.AuraDestroyImmunity : 0;
            double delta;
            if (item.AttributeDeltas.TryGetValue(attribute, out delta)) value += delta;
            return value;
        }

        // 有效抗性 = 映射期清洗后的卡面基础 + 运行时属性偏移 + 光环派生。
        public static double SlowResistance(InternalItem item)
        {
            return EffectiveResistance(item, "PercentSlowReduction", item.AuraSlowResistance);
        }

        public static double FreezeResistance(InternalItem item)
        {
            return EffectiveResistance(item, "PercentFreezeReduction", item.AuraFreezeResistance);
        }

        public static double HasteResistance(InternalItem item)
        {
            return EffectiveResistance(item, "PercentHasteReduction", item.AuraHasteResistance);
        }

        public static double DestroyImmunity(InternalItem item)
        {
            return EffectiveResistance(item, "DestroyImmunity", item.AuraDestroyImmunity);
        }

        private static double EffectiveResistance(InternalItem item, string attribute, double aura)
        {
            if (item == null || item.Card == null) return 0;
            double value = CleanResistance(item, attribute) + aura;
            double delta;
            if (item.AttributeDeltas.TryGetValue(attribute, out delta)) value += delta;
            return Math.Min(100, Math.Max(0, value));
        }

        private static void Synchronize(InternalItem item)
        {
            double value;
            // 原生 Multicast 属性可以为 0（07ae8be8 帧159 Shuriken：Ammo 归零后
            // Multicast 3→0 = 基底 1 + (Ammo-1)），保留负 bonus 由施放数取 max(0,1+bonus)。
            if (item.AuraAttributes.TryGetValue("Multicast", out value)) item.MulticastBonus = (int)JsMath.Round(value) - 1;
            else item.MulticastBonus = StaticMulticast(item.Card);
            // 有效冷却 = (CooldownMax 属性 - 固定缩减) × (1 - 百分比缩减)。
            // 两个缩减属性只作为派生量，不写入 LiveAttributes，避免与实时有效值重复扣减。
            double percent = Math.Min(100, Math.Max(0, item.AuraCooldownPercent));
            double effectiveMs = EffectiveCooldownMaxMs(item);
            // Opening snapshots can be rounded by the server differently from
            // later combat recalculations. Preserve that measured millisecond
            // while the captured reduction totals and effective base agree.
            int capturedMax, capturedPercent, capturedFlat;
            if (item.Card.NativeAttributesAreLive && item.Card.LiveAttributes.TryGetValue("CooldownMax", out capturedMax) &&
                item.Card.LiveAttributes.TryGetValue("PercentCooldownReduction", out capturedPercent) &&
                item.Card.LiveAttributes.TryGetValue("FlatCooldownReduction", out capturedFlat) &&
                Math.Abs(percent - capturedPercent) < 1e-9 && Math.Abs(item.AuraCooldownFlat - capturedFlat) < 1e-9 &&
                Math.Abs(effectiveMs - capturedMax) <= 1.000001)
                effectiveMs = capturedMax;
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
            else item.CritBonus = StaticCrit(item.Card);
            if (item.AuraAttributes.TryGetValue("Lifesteal", out value) || item.AuraAttributes.TryGetValue("LifestealPercentage", out value)) item.Lifesteal = Math.Max(0, value / 100.0);
            else item.Lifesteal = StaticLifesteal(item.Card);
            // 战斗内禁用是持久状态；光环层的 CooldownDisabled 只叠加，不能清除。
            double disabledValue;
            bool auraDisabled = item.AuraAttributes.TryGetValue("CooldownDisabled", out disabledValue) && disabledValue > 0;
            item.Disabled = item.CombatDisabled || auraDisabled;
        }

        internal static double EffectiveCooldownMs(double unscaled, double flat, double percent)
        {
            float factor = 1f - (float)(Math.Min(100, Math.Max(0, percent)) / 100.0);
            return Math.Max(0, Math.Truncate((unscaled - flat) * factor + 1e-7));
        }

        internal static double UnscaledCooldownMs(double current, double flat, double percent)
        {
            float factor = 1f - (float)(Math.Min(100, Math.Max(0, percent)) / 100.0);
            double unscaled = current / factor + flat;
            // Remove only floating point noise around an integer base. A
            // genuinely fractional inverse of a truncated live value is kept.
            double nearest = Math.Round(unscaled);
            return Math.Abs(unscaled - nearest) < 0.001 ? nearest : unscaled;
        }

        // 附魔移除后，属性表不再有派生键；静态效果是唯一正确的回退值。
        private static int StaticMulticast(CardDef card)
        {
            if (card == null) return 0;
            if (card.HasMulticastOverride) return card.MulticastBonus;
            int bonus = 0;
            foreach (Effect effect in card.Effects)
                if (effect.Kind == "multicast") bonus += Math.Max(0, (effect.Count ?? 1) - 1);
            return bonus;
        }

        // 原生 Multicast 的隐含基底是 1；旧卡还可能带静态 multicast 效果。
        public static int BaseMulticast(CardDef card)
        {
            return 1 + StaticMulticast(card);
        }

        private static double StaticCrit(CardDef card)
        {
            double chance = 0;
            if (card == null) return chance;
            foreach (Effect effect in card.Effects) if (effect.Kind == "crit") chance += effect.Chance;
            return chance;
        }

        private static double StaticLifesteal(CardDef card)
        {
            double percent = 0;
            if (card == null) return percent;
            foreach (Effect effect in card.Effects) if (effect.Kind == "lifesteal") percent += effect.Percent;
            return percent;
        }

        private static bool Active(BattleContext ctx, Aura aura)
        {

            if (aura.Source.AuraInactive) return false;
            return !aura.Source.Destroyed && NativeSkillSupport.PrerequisitesMatch(ctx, aura.Source, new NativeSkillRule { Prerequisites = aura.Raw["Prerequisites"] });
        }

        private static List<InternalItem> Targets(BattleContext ctx, Aura aura, JsonValue raw)
        {
            if (NativeSkillSupport.Type(raw) == "TTargetCardRandom")
                return RandomAuraTarget(ctx, aura, raw);
            return Targets(ctx, aura.Source, raw);
        }

        private static List<InternalItem> RandomAuraTarget(BattleContext ctx, Aura aura, JsonValue raw)
        {
            List<InternalItem> candidates = Targets(ctx, aura.Source, raw);
            if (candidates.Count == 0) return candidates;
            string key = aura.Source.Id + "#" + aura.Index;
            string selected;
            if (ctx.AuraTargets.TryGetValue(key, out selected))
            {
                foreach (InternalItem item in candidates)
                    if (item.Id == selected) return new List<InternalItem> { item };
            }
            int hash = StableHash(key + "|" + ctx.Config.Seed);
            InternalItem chosen = candidates[(int)((uint)hash % (uint)candidates.Count)];
            ctx.AuraTargets[key] = chosen.Id;
            return new List<InternalItem> { chosen };
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                int hash = (int)2166136261;
                foreach (char c in value) hash = (hash ^ c) * 16777619;
                return hash;
            }
        }

        private static List<InternalItem> Targets(BattleContext ctx, InternalItem source, JsonValue raw)
        {
            List<InternalItem> result = new List<InternalItem>();
            string type = NativeSkillSupport.Type(raw), section = NativeSkillSupport.Text(raw, "TargetSection");
            bool sectionTarget = type == "TTargetCardSection" || type == "TTargetCardRandom";
            if (type == "TTargetCardSelf")
            {
                if (!source.Destroyed && !raw["ExcludeSelf"].AsBool() && NativeSkillSupport.CardMatches(source, raw["Conditions"], ctx)) result.Add(source);
                return result;
            }
            if (type == "TTargetCardOccupying")
            {
                if (!NativeSkillSupport.IsSocketEffect(source) || source.Slot < 0) return result;
                foreach (InternalItem item in ctx.Items)
                {
                    if (!CanReceiveAura(ctx, source, item) || item.Id == source.Id || item.Side != source.Side || item.Slot != source.Slot) continue;
                    if (!NativeSkillSupport.CardMatches(item, raw["Conditions"], ctx)) continue;
                    result.Add(item);
                }
                return result;
            }
            if (type == "TTargetCardXMost")
            {
                List<InternalItem> ranked = Effects.NativeItemsForTarget(ctx, source, SkillMapper.MapCardTarget(raw), int.MaxValue, null);
                ranked.RemoveAll(delegate(InternalItem item) { return !CanReceiveAura(ctx, source, item); });
                return ranked.Count > 1 ? ranked.GetRange(0, 1) : ranked;
            }
            List<InternalItem> candidates = sectionTarget && NativeSkillSupport.SkillSection(section) ? ctx.Skills : ctx.Items;
            foreach (InternalItem item in candidates)
            {
                if (!CanReceiveAura(ctx, source, item) || raw["ExcludeSelf"].AsBool() && item == source) continue;
                if (sectionTarget)
                {
                    if (!NativeSkillSupport.SectionSideMatches(section, source.Side, item.Side)) continue;
                }
                else if (item.Side != source.Side) continue;
                if (type == "TTargetCardPositional" && !NativeSkillSupport.PositionalMatch(ctx, source, item,
                    NativeSkillSupport.Text(raw, "TargetMode"), raw["IncludeOrigin"].AsBool())) continue;
                if (section == "SelfNeighbors" && !NativeSkillSupport.Adjacent(ctx, source, item)) continue;
                if (section == "SelfStash" || section == "OpponentStash") continue;
                if (!NativeSkillSupport.CardMatches(item, raw["Conditions"], ctx)) continue;
                result.Add(item);
            }
            return result;
        }

        private static bool CanReceiveAura(BattleContext ctx, InternalItem source, InternalItem target)
        {
            // Disabled targets leave external aura selections. Keep their own
            // self-derived attributes available for explicit forced uses; action
            // buffs live separately in AttributeDeltas and are never removed.
            return !target.Destroyed && (!ctx.Config.NativeMechanics || !target.Disabled || target == source);
        }

        private static double Value(BattleContext ctx, InternalItem source, JsonValue raw, InternalItem valueTarget)
        {
            double value = BaseValue(ctx, source, raw, NativeSkillSupport.Type(raw), valueTarget);
            JsonValue modifier = raw["Modifier"];
            if (!NativeSkillSupport.Empty(modifier))
            {
                double other = Value(ctx, source, modifier["Value"], valueTarget);
                string mode = NativeSkillSupport.Text(modifier, "ModifyMode");
                value = mode == "Add" ? value + other : mode == "Subtract" ? value - other : value * other;
                if (modifier["ShouldRound"].AsBool()) value = JsMath.Round(value);
            }
            return value;
        }

        private static double BaseValue(BattleContext ctx, InternalItem source, JsonValue raw, string type, InternalItem valueTarget)
        {
            if (type == "TFixedValue") return raw["Value"].AsDouble();
            if (type == "TReferenceValueAttributeChange")
                return raw["DefaultValue"].Kind == JsonKind.Number ? raw["DefaultValue"].AsDouble() : 0;
            if (type == "TReferenceValueCardAttribute" || type == "TReferenceValueCardAttributeUnscaled")
            {
                string attribute = NativeSkillSupport.Text(raw, "AttributeType");
                bool unscaled = type == "TReferenceValueCardAttributeUnscaled";
                double fallback = raw["DefaultValue"].Kind == JsonKind.Number ? raw["DefaultValue"].AsDouble() : 0;
                if (NativeSkillSupport.Empty(raw["Target"]))
                    return valueTarget == null ? fallback : ReadAttribute(valueTarget, attribute, unscaled, fallback);
                List<InternalItem> items = AttributeTargets(ctx, source, raw["Target"]);
                return items.Count > 0 ? ReadAttribute(items[0], attribute, unscaled, fallback) : fallback;
            }
            if (type == "TReferenceValueCardAttributeAggregate")
            {
                string attribute = NativeSkillSupport.Text(raw, "AttributeType");
                double fallback = raw["DefaultValue"].Kind == JsonKind.Number ? raw["DefaultValue"].AsDouble() : 0;
                if (NativeSkillSupport.Empty(raw["Target"]))
                    return valueTarget == null ? fallback : ReadAttribute(valueTarget, attribute, false, fallback);
                double sum = 0;
                foreach (InternalItem item in AttributeTargets(ctx, source, raw["Target"]))
                    sum += ReadAttribute(item, attribute, false, fallback);
                return sum;
            }
            if (type == "TReferenceValuePlayerAttribute" || type == "TReferenceValuePlayerAttributeUnscaled")
            {
                Target target = SkillMapper.MapHeroTarget(raw["Target"]);
                Side side = target.Kind == "enemyHero" ? SideUtil.Opposing(source.Side) : source.Side;
                return NativeSkillSupport.PlayerAttributeValue(ctx, ctx.Hero(side), NativeSkillSupport.Text(raw, "AttributeType"));
            }
            List<InternalItem> items2 = Targets(ctx, source, raw["Target"]);
            if (type == "TReferenceValueCardCount") return items2.Count;
            if (type == "TReferenceValueCardTagCount")
            {
                if (!raw["Distinct"].AsBool())
                {
                    int total = 0;
                    foreach (InternalItem item in items2) total += item.Card.Tags.Count + item.ExtraTags.Count + item.AuraTags.Count;
                    return total;
                }
                HashSet<string> tags = new HashSet<string>();
                foreach (InternalItem item in items2)
                {
                    foreach (string tag in item.Card.Tags) tags.Add(tag);
                    foreach (string tag in item.ExtraTags) tags.Add(tag);
                    foreach (string tag in item.AuraTags) tags.Add(tag);
                }
                return tags.Count;
            }
            return 0;
        }

        private static List<InternalItem> AttributeTargets(BattleContext ctx, InternalItem source, JsonValue raw)
        {
            return Effects.NativeItemsForTarget(ctx, source, SkillMapper.MapCardTarget(raw), int.MaxValue, null);
        }

        private static double ReadAttribute(InternalItem item, string attribute, bool unscaled, double fallback)
        {
            if (attribute == "Ammo") return item.Ammo;
            if (attribute == "AmmoMax") return item.AmmoMax.HasValue ? item.AmmoMax.Value : fallback;
            return unscaled ? ItemAttributes.Unscaled(item, attribute, fallback) : ItemAttributes.GetOrDefault(item, attribute, fallback);
        }
    }
}
