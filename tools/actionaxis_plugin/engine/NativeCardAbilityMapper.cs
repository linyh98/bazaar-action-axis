using System;
using System.Collections.Generic;

namespace BazaarActionAxis.Engine
{
    // Bounded native-template translation. A card is promoted to structured only
    // when every combat ability and opening aura is understood. Unsupported
    // definitions retain the existing approximation and an explicit warning.
    public static class NativeCardAbilityMapper
    {
        private static readonly string[] Tiers = { "Bronze", "Silver", "Gold", "Diamond", "Legendary" };

        public static List<string> Apply(BattleConfig config)
        {
            List<string> warnings = new List<string>();
            if (config == null || !config.NativeMechanics) return warnings;
            // Make native sizes/tags available before evaluating board-count auras.
            foreach (Side side in new[] { Side.Player, Side.Enemy })
                foreach (LoadoutItem item in Board(config, side).Items)
                {
                    CardDef old;
                    if (!config.Cards.TryGetValue(item.CardId, out old) || NativeSkillSupport.Empty(old.NativeTemplate)) continue;
                    CardDef card = old.Clone();
                    card.Tags = new List<string>();
                    foreach (JsonValue tag in card.NativeTemplate["Tags"].AsArray()) card.Tags.Add(tag.AsString());
                    if (!NativeSkillSupport.Empty(card.NativeEnchantment))
                        foreach (JsonValue tag in card.NativeEnchantment["Tags"].AsArray()) if (!card.Tags.Contains(tag.AsString())) card.Tags.Add(tag.AsString());
                    config.Cards[item.CardId] = card;
                }
            List<KeyValuePair<Side, string>> valid = new List<KeyValuePair<Side, string>>();
            foreach (Side side in new[] { Side.Player, Side.Enemy })
            {
                if (side == Side.Enemy && config.EnemyIsTrainingDummy) continue;
                foreach (LoadoutItem item in Board(config, side).Items)
                {
                    CardDef original;
                    if (!config.Cards.TryGetValue(item.CardId, out original) || NativeSkillSupport.Empty(original.NativeTemplate) || IsCalibrated(original.BaseItemId) && original.NativeAttributesAreLive) continue;
                    Dictionary<string, CardDef> before = new Dictionary<string, CardDef>(config.Cards);
                    double hpPlayer = config.PlayerMaxHp, hpEnemy = config.EnemyMaxHp, regen = config.EnemyStartRegen;
                    double? start = config.EnemyStartHp;
                    try { Map(config, original, side, false); valid.Add(new KeyValuePair<Side, string>(side, item.CardId)); }
                    catch (Exception error) { warnings.Add(original.DisplayName() + "：原生战斗规则未完整模拟（" + error.Message + "）"); }
                    finally { config.Cards = before; config.PlayerMaxHp = hpPlayer; config.EnemyMaxHp = hpEnemy; config.EnemyStartRegen = regen; config.EnemyStartHp = start; }
                }
            }
            // A later card can buff an earlier card. Evaluate every opening aura
            // before compiling any final constant effect amounts or cooldowns.
            List<KeyValuePair<Side, string>> withAuras = new List<KeyValuePair<Side, string>>();
            foreach (KeyValuePair<Side, string> entry in valid)
            {
                try { Map(config, config.Cards[entry.Value], entry.Key, true); withAuras.Add(entry); }
                catch (Exception error) { warnings.Add(config.Cards[entry.Value].DisplayName() + "：原生光环未加入（" + error.Message + "）"); }
            }
            foreach (KeyValuePair<Side, string> entry in withAuras)
                Map(config, config.Cards[entry.Value], entry.Key, false);
            return warnings;
        }

        private static bool IsCalibrated(string id)
        {
            return id == NativeItemMapper.Firefly || id == NativeItemMapper.Cog || id == NativeItemMapper.LauncherCore ||
                id == NativeItemMapper.Spider || id == NativeItemMapper.SolderingGun || id == NativeItemMapper.Aludel ||
                id == NativeItemMapper.Drowsdrop || id == NativeItemMapper.Aviators;
        }

        private static Loadout Board(BattleConfig config, Side side) { return side == Side.Player ? config.Player : config.Enemy; }

        private static void Map(BattleConfig config, CardDef original, Side side, bool aurasOnly)
        {
            JsonValue template = original.NativeTemplate;
            if (NativeSkillSupport.Type(template) != "TCardItem") throw new FormatException("缺少卡牌模板");
            Dictionary<string, double> attrs = new Dictionary<string, double>();
            JsonValue abilityIds = JsonValue.Null, auraIds = JsonValue.Null;
            bool found = false;
            foreach (string tier in Tiers)
            {
                JsonValue row = template["Tiers"].Has(tier) ? template["Tiers"][tier] : JsonValue.Null;
                if (!NativeSkillSupport.Empty(row))
                {
                    foreach (KeyValuePair<string, JsonValue> attr in row["Attributes"].AsObject()) attrs[attr.Key] = attr.Value.AsDouble();
                    if (row["AbilityIds"].Kind == JsonKind.Array) abilityIds = row["AbilityIds"];
                    if (row["AuraIds"].Kind == JsonKind.Array) auraIds = row["AuraIds"];
                }
                if (tier == original.Tier) { found = true; break; }
            }
            if (!found || abilityIds.Kind != JsonKind.Array || auraIds.Kind != JsonKind.Array) throw new FormatException("品质定义不完整");
            foreach (KeyValuePair<string, int> attr in original.LiveAttributes) attrs[attr.Key] = attr.Value;
            CardDef card = original.Clone();
            card.LiveAttributes = ToInts(attrs);
            List<JsonValue> auras = new List<JsonValue>();
            foreach (JsonValue id in auraIds.AsArray())
            {
                string key = id.AsString();
                // Native loot templates occasionally retain a stale AuraId.
                // Missing nonempty definitions cannot be silently guessed.
                if (!template["Auras"].Has(key))
                {
                    if (card.Cooldown <= 0 && AllNonCombat(template, abilityIds)) continue;
                    throw new FormatException("光环定义缺失 " + key);
                }
                auras.Add(template["Auras"][key]);
            }
            if (!NativeSkillSupport.Empty(card.NativeEnchantment))
                foreach (KeyValuePair<string, JsonValue> aura in card.NativeEnchantment["Auras"].AsObject()) auras.Add(aura.Value);
            // Build on a temporary card so failure cannot partially replace it.
            CardDef prior = config.Cards[card.Id];
            config.Cards[card.Id] = card;
            Dictionary<string, CardDef> beforeCards = new Dictionary<string, CardDef>(config.Cards);
            double beforePlayerHp = config.PlayerMaxHp, beforeEnemyHp = config.EnemyMaxHp, beforeRegen = config.EnemyStartRegen;
            double? beforeEnemyStart = config.EnemyStartHp;
            try
            {
                if (!card.NativeAurasApplied)
                    ApplyStaticAuras(config, card, side, attrs, auras, card.NativeAttributesAreLive);
                card = config.Cards[card.Id];
                card.NativeAurasApplied = true;
                attrs = ToDoubles(card.LiveAttributes);
                if (aurasOnly) { config.Cards[card.Id] = card; return; }
                if (IsCalibrated(card.BaseItemId))
                {
                    // Use the established effect ordering and passive filters of
                    // the eight calibrated items; only fill their unevaluated
                    // monster attributes here before NativeItemMapper runs.
                    card = LiveItemOverrides.Apply(card, card.LiveAttributes);
                    RefreshAttributes(card);
                    config.Cards[card.Id] = card;
                    return;
                }
                card.Effects = new List<Effect>();
                card.Triggers = new List<TriggerDef>();
                foreach (JsonValue id in abilityIds.AsArray())
                {
                    if (!template["Abilities"].Has(id.AsString())) throw new FormatException("能力定义缺失");
                    AddAbility(card, template["Abilities"][id.AsString()], attrs);
                }
                if (!NativeSkillSupport.Empty(card.NativeEnchantment))
                    foreach (KeyValuePair<string, JsonValue> ability in card.NativeEnchantment["Abilities"].AsObject()) AddAbility(card, ability.Value, attrs);
                RefreshAttributes(card);
                card.ParseStatus = "structured";
                card.DataSource = "native-template";
                config.Cards[card.Id] = card;
            }
            catch
            {
                config.Cards = beforeCards;
                config.Cards[card.Id] = prior;
                config.PlayerMaxHp = beforePlayerHp;
                config.EnemyMaxHp = beforeEnemyHp;
                config.EnemyStartRegen = beforeRegen;
                config.EnemyStartHp = beforeEnemyStart;
                throw;
            }
        }

        private static bool AllNonCombat(JsonValue template, JsonValue ids)
        {
            foreach (JsonValue id in ids.AsArray())
                if (!template["Abilities"].Has(id.AsString()) || !SkillMapper.IsNonCombatTrigger(template["Abilities"][id.AsString()]["Trigger"])) return false;
            return true;
        }

        private static void AddAbility(CardDef card, JsonValue raw, Dictionary<string, double> attrs)
        {
            JsonValue trigger = raw["Trigger"];
            if (SkillMapper.IsNonCombatTrigger(trigger)) return;
            CheckArea(raw);
            if (!NativeSkillSupport.PrerequisitesSupported(raw["Prerequisites"])) throw new FormatException("未知触发条件或次数限制");
            Effect effect = MapAction(raw["Action"], attrs);
            if (NativeSkillSupport.Type(trigger) == "TTriggerOnCardFired")
            {
                if (raw["Prerequisites"].AsArray().Count > 0) throw new FormatException("主动能力的条件待支持");
                card.Effects.Add(effect);
                return;
            }
            foreach (TriggerDef mapped in MapTriggers(trigger))
            {
                mapped.SkillRule.Prerequisites = raw["Prerequisites"];
                mapped.Effects.Add(effect);
                card.Triggers.Add(mapped);
            }
        }

        private static Effect MapAction(JsonValue raw, Dictionary<string, double> attrs)
        {
            string type = NativeSkillSupport.Type(raw);
            if (type == "TActionCardFlyingStart")
            {
                if (!NativeSkillSupport.Empty(raw["Cost"]) || !NativeSkillSupport.Empty(raw["Duration"])) throw new FormatException("限时飞行或行动代价");
                Target target = SkillMapper.MapCardTarget(raw["Target"]);
                return new Effect { Kind = "fly", Target = target, Count = NativeSkillSupport.Empty(raw["TargetCount"]) && target.Kind == "selfItem" ? 1 :
                    (int)SkillMapper.ReadValue(raw["TargetCount"], attrs, "FlyingTargets") };
            }
            return SkillMapper.MapAction(raw, attrs);
        }

        private static List<TriggerDef> MapTriggers(JsonValue raw)
        {
            string type = NativeSkillSupport.Type(raw);
            List<TriggerDef> result = new List<TriggerDef>();
            if (type == "TTriggerOr")
            {
                if (raw["Triggers"].Kind != JsonKind.Array || raw["Triggers"].Count == 0) throw new FormatException("触发组合为空");
                foreach (JsonValue child in raw["Triggers"].AsArray()) result.AddRange(MapTriggers(child));
                return result;
            }
            if (type == "TTriggerOnFightStarted") { result.Add(SkillMapper.MapTrigger(raw)); return result; }
            Dictionary<string, string> events = new Dictionary<string, string> {
                { "TTriggerOnItemUsed", "onUse" }, { "TTriggerOnCardPerformedHaste", "onHaste" },
                { "TTriggerOnCardPerformedSlow", "onSlow" }, { "TTriggerOnCardPerformedFreeze", "onFreeze" },
                { "TTriggerOnCardPerformedBurn", "onBurnApplied" }, { "TTriggerOnCardPerformedPoison", "onPoisonApplied" },
                { "TTriggerOnCardPerformedShield", "onShield" }, { "TTriggerOnCardPerformedHeal", "onHeal" },
                { "TTriggerOnCardPerformedRegen", "onRegen" }, { "TTriggerOnCardCritted", "onCrit" }
            };
            string ev;
            if (!events.TryGetValue(type, out ev)) throw new FormatException("未知触发器 " + type);
            if (raw.Has("Target") && !NativeSkillSupport.Empty(raw["Target"])) throw new FormatException("指定行动受体的被动待支持");
            JsonValue subject = raw["Subject"];
            NativeSkillRule rule = new NativeSkillRule { SourceCondition = subject["Conditions"] };
            string subjectType = NativeSkillSupport.Type(subject);
            if (subjectType == "TTargetCardSelf") { rule.SourceOwner = "self"; rule.SourceSelf = true; }
            else if (subjectType == "TTargetCardSection")
            {
                string section = NativeSkillSupport.Text(subject, "TargetSection");
                if (section == "SelfHand" || section == "SelfBoard") rule.SourceOwner = "self";
                else if (section == "OpponentHand" || section == "OpponentBoard") rule.SourceOwner = "enemy";
                else throw new FormatException("未知触发区域 " + section);
                rule.SourceOther = subject["ExcludeSelf"].AsBool();
            }
            else if (subjectType == "TTargetCardPositional" && NativeSkillSupport.Text(subject, "Origin") == "Self" && NativeSkillSupport.Text(subject, "TargetMode") == "Neighbor")
            { rule.SourceOwner = "self"; rule.SourceAdjacent = true; }
            else throw new FormatException("未知触发主体 " + subjectType);
            if (!NativeSkillSupport.ConditionSupported(rule.SourceCondition)) throw new FormatException("未知触发主体条件");
            result.Add(new TriggerDef { Event = ev, SkillRule = rule });
            return result;
        }

        internal static void RefreshAttributes(CardDef card)
        {
            int value;
            if (card.LiveAttributes.TryGetValue("CooldownMax", out value)) card.Cooldown = value / 1000.0;
            card.Passive = card.Cooldown <= 0;
            if (card.LiveAttributes.TryGetValue("AmmoMax", out value)) card.AmmoMax = value > 0 ? (int?)value : null;
            if (card.LiveAttributes.TryGetValue("Multicast", out value)) { card.MulticastBonus = Math.Max(0, value - 1); card.HasMulticastOverride = true; }
            if (card.LiveAttributes.TryGetValue("PercentSlowReduction", out value) && !card.NativeAttributesAreLive) card.PercentSlowReduction = value;
            if (card.LiveAttributes.TryGetValue("PercentFreezeReduction", out value) && !card.NativeAttributesAreLive) card.PercentFreezeReduction = value;
            if (card.LiveAttributes.TryGetValue("CritChance", out value))
            {
                card.Effects.RemoveAll(delegate(Effect e) { return e.Kind == "crit"; });
                if (value > 0) card.Effects.Add(new Effect { Kind = "crit", Chance = value / 100.0 });
            }
            if (card.LiveAttributes.TryGetValue("Lifesteal", out value) || card.LiveAttributes.TryGetValue("LifestealPercentage", out value))
            {
                card.Effects.RemoveAll(delegate(Effect e) { return e.Kind == "lifesteal"; });
                if (value > 0) card.Effects.Add(new Effect { Kind = "lifesteal", Percent = value / 100.0 });
            }
        }

        // Pure monster templates have no evaluated game auras. Stage changes and
        // commit only after every selected aura is validated, never mark inherited.
        internal static void ApplyStaticAuras(BattleConfig config, CardDef source, Side side, Dictionary<string, double> attrs, IList<JsonValue> auras, bool alreadyLive)
        {
            Dictionary<string, CardDef> changed = new Dictionary<string, CardDef>();
            double maxHp = side == Side.Player ? config.PlayerMaxHp : config.EnemyMaxHp;
            double regen = config.EnemyStartRegen;
            foreach (JsonValue aura in auras)
            {
                CheckArea(aura);
                if (!NativeSkillSupport.PrerequisitesSupported(aura["Prerequisites"])) throw new FormatException("动态光环条件");
                BattleContext ctx = BattleEngine.InitBattle(config);
                InternalItem sourceItem = ctx.Items.Find(delegate(InternalItem item) { return item.CardId == source.Id && item.Side == side; });
                if (sourceItem == null) sourceItem = new InternalItem { Id = source.Id, Card = source, Side = side, Slot = -1 };
                if (!NativeSkillSupport.PrerequisitesMatch(ctx, sourceItem, new NativeSkillRule { Prerequisites = aura["Prerequisites"] })) continue;
                JsonValue action = aura["Action"], target = action["Target"];
                string type = NativeSkillSupport.Type(action), attribute = NativeSkillSupport.Text(action, "AttributeType");
                string operation = NativeSkillSupport.Text(action, "Operation");
                if (operation != "Add" && operation != "Set" && operation != "Multiply") throw new FormatException("未知光环操作");
                if (type == "TAuraActionPlayerModifyAttribute")
                {
                    if (NativeSkillSupport.Type(target) != "TTargetPlayerRelative" || NativeSkillSupport.Text(target, "TargetMode") != "Self" || !NativeSkillSupport.Empty(target["Conditions"]) ||
                        attribute != "HealthMax" && attribute != "HealthRegen") throw new FormatException("未知人物光环 " + attribute);
                    double value = StaticValue(config, source, side, action["Value"], attrs);
                    if (!alreadyLive)
                    {
                        if (attribute == "HealthMax") maxHp = Operate(maxHp, value, operation);
                        else if (side == Side.Enemy) regen = Operate(regen, value, operation);
                        else throw new FormatException("纯模板我方再生初始化未支持");
                    }
                }
                else if (type == "TAuraActionCardModifyAttribute")
                {
                    if (!AuraAttribute(attribute)) throw new FormatException("未知卡牌光环属性 " + attribute);
                    double value = StaticValue(config, source, side, action["Value"], attrs);
                    foreach (LoadoutItem loadout in Board(config, side).Items)
                    {
                        CardDef card;
                        if (!changed.TryGetValue(loadout.CardId, out card)) card = config.Cards[loadout.CardId].Clone();
                        if (!StaticTargetMatches(config, card, source, target, side)) continue;
                        if (!alreadyLive)
                        {
                            int current; card.LiveAttributes.TryGetValue(attribute, out current);
                            card.LiveAttributes[attribute] = (int)JsMath.Round(Operate(current, value, operation));
                            changed[card.Id] = card;
                            if (card.Id == source.Id) attrs[attribute] = card.LiveAttributes[attribute];
                        }
                    }
                }
                else throw new FormatException("未知光环 " + type);
            }
            foreach (KeyValuePair<string, CardDef> pair in changed) config.Cards[pair.Key] = pair.Value;
            if (!alreadyLive)
            {
                if (side == Side.Player) config.PlayerMaxHp = maxHp;
                else
                {
                    if (config.EnemyStartHp.HasValue)
                    {
                        bool wasFull = Math.Abs(config.EnemyStartHp.Value - config.EnemyMaxHp) < 1e-9;
                        config.EnemyStartHp = wasFull ? maxHp : Math.Min(config.EnemyStartHp.Value, maxHp);
                    }
                    config.EnemyMaxHp = maxHp;
                    config.EnemyStartRegen = regen;
                }
            }
        }

        private static bool AuraAttribute(string attr)
        {
            string[] attrs = { "DamageAmount", "HealAmount", "ShieldApplyAmount", "BurnApplyAmount", "PoisonApplyAmount", "RegenApplyAmount", "CritChance", "Lifesteal", "LifestealPercentage", "CooldownMax", "AmmoMax", "Multicast", "HasteAmount", "HasteTargets", "SlowAmount", "SlowTargets", "FreezeAmount", "FreezeTargets", "ChargeAmount", "ChargeTargets", "PercentCooldownReduction", "Custom_0", "Custom_1", "Custom_2" };
            return Array.IndexOf(attrs, attr) >= 0;
        }

        private static bool StaticTargetMatches(BattleConfig config, CardDef card, CardDef source, JsonValue target, Side side)
        {
            string type = NativeSkillSupport.Type(target);
            if (!NativeSkillSupport.ConditionSupported(target["Conditions"])) throw new FormatException("光环目标条件未支持");
            if (type == "TTargetCardSelf") return card.Id == source.Id && NativeSkillSupport.CardMatches(new InternalItem { Card = card, Side = side }, target["Conditions"]);
            if (type == "TTargetCardPositional" && NativeSkillSupport.Text(target, "Origin") == "Self" && NativeSkillSupport.Text(target, "TargetMode") == "Neighbor" && !target["IncludeOrigin"].AsBool())
            {
                BattleContext ctx = BattleEngine.InitBattle(config);
                InternalItem sourceItem = ctx.Items.Find(delegate(InternalItem item) { return item.CardId == source.Id && item.Side == side; });
                InternalItem targetItem = ctx.Items.Find(delegate(InternalItem item) { return item.CardId == card.Id && item.Side == side; });
                return NativeSkillSupport.Adjacent(ctx, sourceItem, targetItem) && NativeSkillSupport.CardMatches(targetItem, target["Conditions"]);
            }
            string section = NativeSkillSupport.Text(target, "TargetSection");
            if (type != "TTargetCardSection" || section != "SelfHand" && section != "SelfBoard" && !(side == Side.Enemy && section == "SelfHandAndStash")) throw new FormatException("光环目标区域未支持");
            if (!NativeSkillSupport.ConditionSupported(target["Conditions"])) throw new FormatException("光环目标条件未支持");
            return !(target["ExcludeSelf"].AsBool() && card.Id == source.Id) && NativeSkillSupport.CardMatches(new InternalItem { Card = card, Side = side }, target["Conditions"]);
        }

        private static double StaticValue(BattleConfig config, CardDef source, Side side, JsonValue raw, Dictionary<string, double> attrs)
        {
            string type = NativeSkillSupport.Type(raw);
            double value;
            if (type == "TReferenceValueCardCount")
            {
                value = 0;
                foreach (LoadoutItem item in Board(config, side).Items) if (StaticTargetMatches(config, config.Cards[item.CardId], source, raw["Target"], side)) value++;
            }
            else return SkillMapper.ReadValue(raw, attrs, null);
            JsonValue modifier = raw["Modifier"];
            if (!NativeSkillSupport.Empty(modifier))
            {
                double other = StaticValue(config, source, side, modifier["Value"], attrs);
                string mode = NativeSkillSupport.Text(modifier, "ModifyMode");
                if (mode == "Multiply") value *= other;
                else if (mode == "Add") value += other;
                else throw new FormatException("光环数值修正未支持");
                if (modifier["ShouldRound"].AsBool()) value = JsMath.Round(value);
            }
            return value;
        }

        private static double Operate(double before, double value, string operation) { return operation == "Add" ? before + value : operation == "Set" ? value : before * value; }

        private static void CheckArea(JsonValue raw)
        {
            string active = NativeSkillSupport.Text(raw, "ActiveIn"), works = NativeSkillSupport.Text(raw, "WorksIn");
            if (active != "HandOnly" && active != "HandAndStash" || works != null && works != "Anywhere" && works != "CombatOnly") throw new FormatException("能力作用区域或场景未支持");
        }

        private static Dictionary<string, int> ToInts(Dictionary<string, double> attrs)
        {
            Dictionary<string, int> result = new Dictionary<string, int>();
            foreach (KeyValuePair<string, double> attr in attrs) result[attr.Key] = (int)JsMath.Round(attr.Value);
            return result;
        }
        private static Dictionary<string, double> ToDoubles(Dictionary<string, int> attrs)
        {
            Dictionary<string, double> result = new Dictionary<string, double>();
            foreach (KeyValuePair<string, int> attr in attrs) result[attr.Key] = attr.Value;
            return result;
        }
    }
}
