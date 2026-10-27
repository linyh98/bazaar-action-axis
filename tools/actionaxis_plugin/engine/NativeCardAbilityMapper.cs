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
                    card.HiddenTags = new List<string>();
                    foreach (JsonValue tag in card.NativeTemplate["HiddenTags"].AsArray()) card.HiddenTags.Add(tag.AsString());
                    if (card.NativeEnchantment != null && card.NativeEnchantment.Kind == JsonKind.Object)
                        foreach (JsonValue tag in card.NativeEnchantment["HiddenTags"].AsArray())
                            if (!card.HiddenTags.Contains(tag.AsString())) card.HiddenTags.Add(tag.AsString());
                    if (!NativeSkillSupport.Empty(card.NativeEnchantment))
                        foreach (JsonValue tag in card.NativeEnchantment["Tags"].AsArray()) if (!card.Tags.Contains(tag.AsString())) card.Tags.Add(tag.AsString());
                    // 光环/条件可能引用其他卡的原生属性。先把整侧模板属性铺好，
                    // 否则先映射的卡会把尚未映射的邻居看成 0。实时覆盖值优先。
                    Dictionary<string, int> native = MonsterTemplateExtractor.TierAttributes(card.NativeTemplate, card.Tier);
                    foreach (KeyValuePair<string, int> attr in old.LiveAttributes) native[attr.Key] = attr.Value;
                    card.LiveAttributes = native;
                    if (card.NativeAuraSeedAttributes == null) card.NativeAuraSeedAttributes = new Dictionary<string, int>(native);
                    int cooldown;
                    if (native.TryGetValue("CooldownMax", out cooldown))
                    {
                        card.Cooldown = cooldown / 1000.0;
                        card.Passive = card.Cooldown <= 0;
                    }
                    config.Cards[item.CardId] = card;
                }
            List<KeyValuePair<Side, string>> valid = new List<KeyValuePair<Side, string>>();
            foreach (Side side in new[] { Side.Player, Side.Enemy })
            {
                if (side == Side.Enemy && config.EnemyIsTrainingDummy) continue;
                foreach (LoadoutItem item in Board(config, side).Items)
                {
                    CardDef original;
                    if (!config.Cards.TryGetValue(item.CardId, out original) || NativeSkillSupport.Empty(original.NativeTemplate)) continue;
                    Dictionary<string, CardDef> before = new Dictionary<string, CardDef>(config.Cards);
                    double hpPlayer = config.PlayerMaxHp, hpEnemy = config.EnemyMaxHp, regen = config.EnemyStartRegen;
                    double? start = config.EnemyStartHp;
                    double playerRegen = config.PlayerStartRegen, playerRage = config.PlayerRageMax, enemyRage = config.EnemyRageMax;
                    double playerEnrage = config.PlayerEnragedDurationMs, enemyEnrage = config.EnemyEnragedDurationMs;
                    try { Map(config, original, side, false, warnings); valid.Add(new KeyValuePair<Side, string>(side, item.CardId)); }
                    catch (Exception error)
                    {
                        warnings.Add(original.DisplayName() + "：原生战斗规则未完整模拟（" + error.Message + "）");
                        CardDef partial = before[item.CardId].Clone();
                        partial.ParseStatus = "partial";
                        before[item.CardId] = partial;
                    }
                    finally
                    {
                        config.Cards = before; config.PlayerMaxHp = hpPlayer; config.EnemyMaxHp = hpEnemy;
                        config.EnemyStartRegen = regen; config.EnemyStartHp = start; config.PlayerStartRegen = playerRegen;
                        config.PlayerRageMax = playerRage; config.EnemyRageMax = enemyRage;
                        config.PlayerEnragedDurationMs = playerEnrage; config.EnemyEnragedDurationMs = enemyEnrage;
                    }
                }
            }
            // A later card can buff an earlier card. Evaluate every opening aura
            // before compiling any final constant effect amounts or cooldowns.
            List<KeyValuePair<Side, string>> withAuras = new List<KeyValuePair<Side, string>>();
            foreach (KeyValuePair<Side, string> entry in valid)
            {
                try { Map(config, config.Cards[entry.Value], entry.Key, true, warnings); withAuras.Add(entry); }
                catch (Exception error) { warnings.Add(config.Cards[entry.Value].DisplayName() + "：原生光环未加入（" + error.Message + "）"); }
            }
            foreach (KeyValuePair<Side, string> entry in withAuras)
                Map(config, config.Cards[entry.Value], entry.Key, false, warnings);
            return warnings;
        }

        private static bool IsCalibrated(string id)
        {
            return id == NativeItemMapper.Firefly || id == NativeItemMapper.Cog || id == NativeItemMapper.LauncherCore ||
                id == NativeItemMapper.Spider || id == NativeItemMapper.SolderingGun || id == NativeItemMapper.Aludel ||
                id == NativeItemMapper.Drowsdrop || id == NativeItemMapper.Aviators;
        }

        private static Loadout Board(BattleConfig config, Side side) { return side == Side.Player ? config.Player : config.Enemy; }

        private static void Map(BattleConfig config, CardDef original, Side side, bool aurasOnly, List<string> warnings = null)
        {
            JsonValue template = original.NativeTemplate;
            string templateType = NativeSkillSupport.Type(template);
            // Socket effects have the same tier/ability/aura structure as items.
            // Their Occupying target is already resolved relative to the socket slot.
            // Music-note socket effects (TCardMusicNoteSocketEffect) are the
            // Tempo deck's sockets; they carry the same structure (0276c46b).
            if (templateType != "TCardItem" &&
                !(templateType != null && templateType.EndsWith("SocketEffect", StringComparison.Ordinal)))
                throw new FormatException("缺少卡牌模板");
            Dictionary<string, double> attrs = new Dictionary<string, double>();
            JsonValue abilityIds = JsonValue.Null, auraIds = JsonValue.Null;
            bool found = false;
            string lookupTier = original.Tier == "Legendary" ? "Diamond" : original.Tier;
            foreach (string tier in Tiers)
            {
                JsonValue row = template["Tiers"].Has(tier) ? template["Tiers"][tier] : JsonValue.Null;
                if (!NativeSkillSupport.Empty(row))
                {
                    foreach (KeyValuePair<string, JsonValue> attr in row["Attributes"].AsObject()) attrs[attr.Key] = attr.Value.AsDouble();
                    if (row["AbilityIds"].Kind == JsonKind.Array) abilityIds = row["AbilityIds"];
                    if (row["AuraIds"].Kind == JsonKind.Array) auraIds = row["AuraIds"];
                }
                if (tier == lookupTier) { found = true; break; }
            }
            if (!found || abilityIds.Kind != JsonKind.Array || auraIds.Kind != JsonKind.Array) throw new FormatException("品质定义不完整");
            foreach (KeyValuePair<string, int> attr in original.LiveAttributes) attrs[attr.Key] = attr.Value;
            // 附魔属性是所选附魔定义的一部分，按覆盖语义并入；实时快照已包含时也保持幂等。
            if (!NativeSkillSupport.Empty(original.NativeEnchantment))
                foreach (KeyValuePair<string, JsonValue> attr in original.NativeEnchantment["Attributes"].AsObject())
                    attrs[attr.Key] = attr.Value.AsDouble();
            CardDef card = original.Clone();
            card.LiveAttributes = ToInts(attrs);
            if (!card.NativeAttributesAreLive)
            {
                // 纯模板的基准属性必须含附魔固定属性，实时卡则由附魔定义单独提供抗性/免疫。
                // 不能整表覆盖：跨卡静态光环会写进 original.LiveAttributes，重复写种子会双计。
                if (card.NativeAuraSeedAttributes == null)
                {
                    card.NativeAuraSeedAttributes = new Dictionary<string, int>();
                    foreach (KeyValuePair<string, double> attr in attrs)
                        card.NativeAuraSeedAttributes[attr.Key] = (int)JsMath.Round(attr.Value);
                }
                else if (!NativeSkillSupport.Empty(original.NativeEnchantment))
                    foreach (KeyValuePair<string, JsonValue> attr in original.NativeEnchantment["Attributes"].AsObject())
                        card.NativeAuraSeedAttributes[attr.Key] = attr.Value.AsInt();
            }
            List<JsonValue> auras = new List<JsonValue>();
            foreach (JsonValue id in auraIds.AsArray())
            {
                string key = id.AsString();
                // Native loot templates occasionally retain a stale AuraId.
                // Missing nonempty definitions cannot be silently guessed.
                if (!template["Auras"].Has(key))
                {
                    // 原生战利品模板偶有残留 AuraId。缺失定义不能猜数值，
                    // 但可跳过该条并保留同卡其余战斗部分，告警必须可见。
                    card.MappingWarnings.Add(original.DisplayName() + "：光环定义缺失 " + key + "（已跳过，保留其余战斗部分）");
                    continue;
                }
                auras.Add(template["Auras"][key]);
            }
            if (!NativeSkillSupport.Empty(card.NativeEnchantment))
                foreach (KeyValuePair<string, JsonValue> aura in card.NativeEnchantment["Auras"].AsObject()) auras.Add(aura.Value);
            // Nanobot Orange 等卡的基础模板没有 DamageAmount，但自身光环会加上；
            // 能力引用先按原生缺省 0 映射，运行时再读取光环值。
            if (!attrs.ContainsKey("DamageAmount") && AddsSelfAttribute(template, auraIds, "DamageAmount"))
                attrs["DamageAmount"] = 0;
            // Build on a temporary card so failure cannot partially replace it.
            CardDef prior = config.Cards[card.Id];
            config.Cards[card.Id] = card;
            Dictionary<string, CardDef> beforeCards = new Dictionary<string, CardDef>(config.Cards);
            double beforePlayerHp = config.PlayerMaxHp, beforeEnemyHp = config.EnemyMaxHp, beforeRegen = config.EnemyStartRegen;
            double? beforeEnemyStart = config.EnemyStartHp;
            double beforePlayerRegen = config.PlayerStartRegen, beforePlayerRage = config.PlayerRageMax, beforeEnemyRage = config.EnemyRageMax;
            double beforePlayerEnrage = config.PlayerEnragedDurationMs, beforeEnemyEnrage = config.EnemyEnragedDurationMs;
            try
            {
                if (!card.NativeAurasApplied)
                    ApplyStaticAuras(config, card, side, attrs, auras, card.NativeAttributesAreLive);
                card = config.Cards[card.Id];
                card.NativeAurasApplied = true;
                attrs = ToDoubles(card.LiveAttributes);
                // 静态光环映射可能还没把自身 DamageAmount 写进 LiveAttributes；
                // 在能力映射前补回原生缺省 0，避免把整卡判成缺属性。
                if (!attrs.ContainsKey("DamageAmount") && AddsSelfAttribute(template, auraIds, "DamageAmount"))
                {
                    attrs["DamageAmount"] = 0;
                    card.LiveAttributes["DamageAmount"] = 0;
                }
                if (aurasOnly) { config.Cards[card.Id] = card; return; }
                if (IsCalibrated(card.BaseItemId))
                {
                    // 主动效果从原生模板读取；被动过滤和特殊顺序交由 NativeItemMapper 校准。
                    // 纯模板怪物不能依赖本地卡库的主动效果；缺卡或卡库过期也应能出手。
                    card.Effects = new List<Effect>();
                    card.Triggers = new List<TriggerDef>();
                    foreach (JsonValue id in abilityIds.AsArray())
                    {
                        if (!template["Abilities"].Has(id.AsString()))
                        {
                            card.MappingWarnings.Add(original.DisplayName() + "：能力定义缺失 " + id.AsString() + "（已跳过，保留其余战斗部分）");
                            continue;
                        }
                        JsonValue ability = template["Abilities"][id.AsString()];
                        if (NativeSkillSupport.Type(ability["Trigger"]) == "TTriggerOnCardFired")
                            AddAbility(card, ability, attrs);
                    }
                    // 校准只接管基础被动；附魔主动/被动是附加层，不能被校准路径覆盖。
                    if (!NativeSkillSupport.Empty(card.NativeEnchantment))
                        foreach (KeyValuePair<string, JsonValue> ability in card.NativeEnchantment["Abilities"].AsObject())
                            AddAbility(card, ability.Value, attrs, true);
                    RefreshAttributes(card);
                    config.Cards[card.Id] = card;
                    return;
                }
                card.Effects = new List<Effect>();
                card.Triggers = new List<TriggerDef>();
                foreach (JsonValue id in abilityIds.AsArray())
                {
                    if (!template["Abilities"].Has(id.AsString()))
                    {
                        card.MappingWarnings.Add(original.DisplayName() + "：能力定义缺失 " + id.AsString() + "（已跳过，保留其余战斗部分）");
                        continue;
                    }
                    AddAbility(card, template["Abilities"][id.AsString()], attrs);
                }
                if (!NativeSkillSupport.Empty(card.NativeEnchantment))
                    foreach (KeyValuePair<string, JsonValue> ability in card.NativeEnchantment["Abilities"].AsObject()) AddAbility(card, ability.Value, attrs, true);
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
                config.PlayerStartRegen = beforePlayerRegen;
                config.PlayerRageMax = beforePlayerRage; config.EnemyRageMax = beforeEnemyRage;
                config.PlayerEnragedDurationMs = beforePlayerEnrage; config.EnemyEnragedDurationMs = beforeEnemyEnrage;
                throw;
            }
        }

        private static bool AddsSelfAttribute(JsonValue template, JsonValue auraIds, string attribute)
        {
            if (auraIds.Kind != JsonKind.Array) return false;
            foreach (JsonValue id in auraIds.AsArray())
            {
                string key = id.AsString();
                if (key == null || !template["Auras"].Has(key)) continue;
                JsonValue aura = template["Auras"][key];
                JsonValue action = aura["Action"];
                if (NativeSkillSupport.Type(action) != "TAuraActionCardModifyAttribute") continue;
                if (NativeSkillSupport.Text(action, "AttributeType") != attribute) continue;
                JsonValue target = action["Target"];
                if (NativeSkillSupport.Type(target) == "TTargetCardSelf") return true;
            }
            return false;
        }

        private static bool AllNonCombat(JsonValue template, JsonValue ids)
        {
            foreach (JsonValue id in ids.AsArray())
                if (!template["Abilities"].Has(id.AsString()) || !SkillMapper.IsNonCombatTrigger(template["Abilities"][id.AsString()]["Trigger"])) return false;
            return true;
        }

        private static void AddAbility(CardDef card, JsonValue raw, Dictionary<string, double> attrs)
        {
            AddAbility(card, raw, attrs, false);
        }

        private static void AddAbility(CardDef card, JsonValue raw, Dictionary<string, double> attrs, bool fromEnchantment)
        {
            JsonValue trigger = raw["Trigger"];
            if (SkillMapper.IsNonCombatTrigger(trigger)) return;
            // DEBUG 卡的战斗内卡池生成不属于正式规则；跳过该动作，保留同卡其余能力。
            if (card.DisplayName().StartsWith("[DEBUG]", StringComparison.OrdinalIgnoreCase) &&
                NativeSkillSupport.Type(raw["Action"]) == "TActionGameSpawnCards") return;
            // 战斗内卡也可能带 OutOfCombatOnly 的升级/经济分支；跳过该分支，
            // 不能让一个场外能力回退同卡的战斗能力。
            if (NativeSkillSupport.Text(raw, "WorksIn") == "OutOfCombatOnly") return;
            CheckArea(raw);
            if (!NativeSkillSupport.PrerequisitesSupported(raw["Prerequisites"])) throw new FormatException("未知触发条件或次数限制");
            Effect effect = MapAction(raw["Action"], attrs);
            effect.NativeAbilityId = NativeSkillSupport.Text(raw, "Id");
            // Artificial Heart 的原生 Heal 没有 ReferenceValue 且 HealAmount=0，
            // 但 tooltip/InternalDescription 明确 “Heal to full”；按人物最大生命
            // 校准，避免 0 治疗让首次致死直接结束。其他 Heal 卡仍有正 HealAmount
            // 或由光环设置，不走此分支。
            if (card.DisplayName() == "Artificial Heart" &&
                NativeSkillSupport.Type(raw["Action"]) == "TActionPlayerHeal" &&
                NativeSkillSupport.Empty(raw["Action"]["ReferenceValue"]) &&
                (!attrs.ContainsKey("HealAmount") || attrs["HealAmount"] <= 0))
                effect.Amount = new NumExpr { Type = "playerAttribute", Attribute = "HealthMax", Side = "self" };
            effect.FromEnchantment = fromEnchantment;
            effect.Priority = NativeSkillSupport.Text(raw, "Priority");
            // 原生模板不携带“复制哪张卡”的引用；未校准的变形必须整卡回退。
            ValidateTransforms(card, effect);
            if (NativeSkillSupport.Type(trigger) == "TTriggerOnCardFired")
            {
                // 主动能力的前提（计数器/相邻数量）在出手时判定，而不是直接放弃整张卡。
                if (raw["Prerequisites"].Kind == JsonKind.Array && raw["Prerequisites"].Count > 0)
                    effect.Prerequisites = raw["Prerequisites"];
                card.Effects.Add(effect);
                return;
            }
            foreach (TriggerDef mapped in MapTriggers(trigger))
            {
                // Artificial Heart 的模板把 OnPlayerDied Subject 写成 Opponent，
                // 但 tooltip/InternalDescription 与其余复活卡都是“自己首次被击败”。
                // 这是模板字段与卡面语义冲突，按卡面校准为 Self。
                if (card.DisplayName() == "Artificial Heart" && mapped.Event == "onPlayerDied" &&
                    mapped.SkillRule != null && mapped.SkillRule.PlayerOwner == "enemy")
                    mapped.SkillRule.PlayerOwner = "self";
                mapped.SkillRule.Prerequisites = raw["Prerequisites"];
                mapped.Priority = NativeSkillSupport.Text(raw, "Priority");
                mapped.Effects.Add(effect);
                mapped.FromEnchantment = fromEnchantment;
                card.Triggers.Add(mapped);
            }
        }

        private static void ValidateTransforms(CardDef card, Effect effect)
        {
            if (effect.Kind == "transform" && NativeTransform.DonorRule(card) == null)
                throw new FormatException("变形供体未校准");
            if (effect.Kind == "transformDestroyed" && NativeTransform.DestroyDonorRule(card) == null)
                throw new FormatException("摧毁变形供体未校准");
            if (effect.Effects != null)
                foreach (Effect child in effect.Effects) ValidateTransforms(card, child);
        }

        // G08: 复制品按来源卡品质重建，而不是克隆供体当前的实时属性。
        // 原生证据：Gold Mirror 复制 Diamond The Big One 得到 Gold 副本；
        // Gold Hologram 复制 Silver Gamma Ray 得到 Gold 副本（Poison 4 -> 6）。
        internal static CardDef CopyAtTier(CardDef donor, string tier, string enchantment)
        {
            if (donor == null || NativeSkillSupport.Empty(donor.NativeTemplate))
                throw new FormatException("复制供体缺少原生模板");
            if (string.IsNullOrEmpty(tier)) tier = donor.Tier;
            CardDef copy = donor.Clone();
            copy.Tier = tier;
            copy.EnchantmentName = null;
            copy.NativeEnchantment = null;
            copy.Tags = new List<string>();
            copy.HiddenTags = new List<string>();
            copy.LiveAttributes = MonsterTemplateExtractor.TierAttributes(copy.NativeTemplate, tier);
            // 品质模板只给出 CooldownMax/DamageAmount 等数值；Multicast 与充能量是
            // 卡牌自身静态属性，原生变形副本会带上（ba635047 帧 48：Mirror → The Core
            // 的 Multicast 1 / ChargeAmount 1000）。从供体的开场种子补齐，不复制战斗内
            // 临时加成。买卖价不是供体属性：原生按“副本母版的尺寸 + 品质”重算
            // （07f044ee：Gold Mirror 复制 Diamond The Big One → Gold Medium 副本
            // 16/8，而非供体的 24/16；084bd840/40bcb27a/9370ffbd/ba635047 同表）。
            Dictionary<string, int> seed = donor.NativeAuraSeedAttributes ?? donor.LiveAttributes;
            if (seed != null)
            {
                foreach (string key in new string[] { "Multicast", "ChargeAmount" })
                {
                    int value;
                    if (!copy.LiveAttributes.ContainsKey(key) && seed.TryGetValue(key, out value))
                        copy.LiveAttributes[key] = value;
                }
            }
            int sizeUnits = copy.Size == "Large" ? 6 : copy.Size == "Medium" ? 4 : 2;
            int tierUnits = tier == "Diamond" || tier == "Legendary" ? 8 : tier == "Gold" ? 4 : tier == "Silver" ? 2 : 1;
            copy.LiveAttributes["BuyPrice"] = sizeUnits * tierUnits;
            copy.LiveAttributes["SellPrice"] = sizeUnits * tierUnits / 2;
            copy.NativeAuraSeedAttributes = new Dictionary<string, int>(copy.LiveAttributes);
            copy.NativeAttributesAreLive = false;
            copy.NativeAurasApplied = false;
            copy.NativeRuntimeAuras = new List<JsonValue>();
            copy.NativeUseAuras = new List<JsonValue>();
            copy.Effects = new List<Effect>();
            copy.Triggers = new List<TriggerDef>();
            int cooldown;
            copy.Cooldown = copy.LiveAttributes.TryGetValue("CooldownMax", out cooldown) ? cooldown / 1000.0 : 0;
            copy.Passive = copy.Cooldown <= 0;
            copy.AmmoMax = null;
            copy.MulticastBonus = 0;
            copy.HasMulticastOverride = false;
            copy.PercentSlowReduction = 0;
            copy.PercentFreezeReduction = 0;
            copy.PercentChargeReduction = 0;
            copy.DestroyImmunity = 0;
            copy.ParseStatus = "native-template";
            copy.DataSource = "native-template";

            BattleConfig config = new BattleConfig { NativeMechanics = true };
            config.Cards[copy.Id] = copy;
            config.Player.Items.Add(new LoadoutItem { CardId = copy.Id, Slot = 0 });
            List<string> warnings = Apply(config);
            warnings.AddRange(NativeItemMapper.Apply(config));
            if (warnings.Count > 0) throw new FormatException(string.Join("；", warnings.ToArray()));

            CardDef mapped = config.Cards[copy.Id];
            // 临时单卡棋盘的静态光环不能代表新槽位/新邻居；保留模板基底，
            // 由 NativeDynamicAuras.Refresh 在真实棋盘上重建。
            mapped.LiveAttributes = new Dictionary<string, int>(copy.NativeAuraSeedAttributes);
            mapped.NativeAuraSeedAttributes = new Dictionary<string, int>(copy.NativeAuraSeedAttributes);
            mapped.NativeAurasApplied = true;
            if (!string.IsNullOrEmpty(enchantment)) mapped = EnchantCopy(mapped, enchantment);
            return mapped;
        }

        // 复制品附魔必须重新读取供体模板的附魔规则，不能沿用打印机的空附魔能力。
        internal static CardDef EnchantCopy(CardDef copy, string enchantment)
        {
            if (string.IsNullOrEmpty(enchantment) || !string.IsNullOrEmpty(copy.EnchantmentName)) return copy;
            JsonValue template = copy.NativeTemplate;
            if (NativeSkillSupport.Empty(template) || template["Enchantments"].Kind != JsonKind.Object ||
                !template["Enchantments"].Has(enchantment)) return copy;
            copy.EnchantmentName = enchantment;
            copy.NativeEnchantment = template["Enchantments"][enchantment];
            foreach (KeyValuePair<string, JsonValue> attr in copy.NativeEnchantment["Attributes"].AsObject())
                copy.LiveAttributes[attr.Key] = attr.Value.AsInt();
            copy.NativeAuraSeedAttributes = new Dictionary<string, int>(copy.LiveAttributes);
            copy.NativeAttributesAreLive = false;
            copy.NativeAurasApplied = false;
            BattleConfig config = new BattleConfig { NativeMechanics = true };
            config.Cards[copy.Id] = copy;
            config.Player.Items.Add(new LoadoutItem { CardId = copy.Id, Slot = 0 });
            List<string> warnings = Apply(config);
            warnings.AddRange(NativeItemMapper.Apply(config));
            if (warnings.Count > 0) throw new FormatException(string.Join("；", warnings.ToArray()));
            return config.Cards[copy.Id];
        }

        // 战斗内附魔：在未附魔基底上重建一份卡，附魔主动/被动/光环全部附加。
        // 失败必须抛给调用方，不能留下半张附魔卡。
        internal static CardDef BattleEnchant(CardDef baseCard, string enchantment)
        {
            if (baseCard == null || string.IsNullOrEmpty(enchantment)) throw new FormatException("附魔目标或名称缺失");
            JsonValue template = baseCard.NativeTemplate;
            if (NativeSkillSupport.Empty(template) || template["Enchantments"].Kind != JsonKind.Object ||
                !template["Enchantments"].Has(enchantment))
                throw new FormatException("附魔定义缺失 " + enchantment);
            JsonValue definition = template["Enchantments"][enchantment];
            CardDef card = baseCard.Clone();
            card.Effects = new List<Effect>(baseCard.Effects);
            card.Triggers = new List<TriggerDef>(baseCard.Triggers);
            card.Tags = new List<string>(baseCard.Tags);
            card.HiddenTags = new List<string>(baseCard.HiddenTags);
            card.NativeRuntimeAuras = new List<JsonValue>(baseCard.NativeRuntimeAuras ?? new List<JsonValue>());
            card.LiveAttributes = new Dictionary<string, int>(baseCard.LiveAttributes);
            if (baseCard.NativeAuraSeedAttributes != null)
                card.NativeAuraSeedAttributes = new Dictionary<string, int>(baseCard.NativeAuraSeedAttributes);
            card.EnchantmentName = enchantment;
            card.NativeEnchantment = definition;
            foreach (JsonValue tag in definition["Tags"].AsArray())
                if (!card.Tags.Contains(tag.AsString())) card.Tags.Add(tag.AsString());
            foreach (JsonValue tag in definition["HiddenTags"].AsArray())
                if (!card.HiddenTags.Contains(tag.AsString())) card.HiddenTags.Add(tag.AsString());
            foreach (KeyValuePair<string, JsonValue> attr in definition["Attributes"].AsObject())
            {
                card.LiveAttributes[attr.Key] = attr.Value.AsInt();
                if (card.NativeAuraSeedAttributes != null) card.NativeAuraSeedAttributes[attr.Key] = attr.Value.AsInt();
            }
            foreach (KeyValuePair<string, JsonValue> aura in definition["Auras"].AsObject())
            {
                NativeDynamicAuras.Validate(aura.Value);
                card.NativeRuntimeAuras.Add(aura.Value);
            }
            Dictionary<string, double> attrs = ToDoubles(card.LiveAttributes);
            foreach (KeyValuePair<string, JsonValue> ability in definition["Abilities"].AsObject())
                AddAbility(card, ability.Value, attrs, true);
            RefreshAttributes(card);
            return card;
        }

        private static Effect MapAction(JsonValue raw, Dictionary<string, double> attrs)
        {
            return SkillMapper.MapAction(raw, attrs);
        }

        private static List<TriggerDef> MapTriggers(JsonValue raw)
        {
            return NativeSkillSupport.MapTriggers(raw);
        }

        internal static void RefreshAttributes(CardDef card)
        {
            int value;
            if (card.LiveAttributes.TryGetValue("CooldownMax", out value)) card.Cooldown = value / 1000.0;
            card.Passive = card.Cooldown <= 0;
            if (card.LiveAttributes.TryGetValue("AmmoMax", out value)) card.AmmoMax = value > 0 ? (int?)value : null;
            if (card.LiveAttributes.TryGetValue("Multicast", out value)) { card.MulticastBonus = Math.Max(0, value - 1); card.HasMulticastOverride = true; }
            // 抗性/免疫只从明确的模板基准或所选附魔定义读取。
            // 实时快照可能把飞行衍生的 50% 抗性混进来，不能当作开场属性。
            card.PercentSlowReduction = 0;
            card.PercentFreezeReduction = 0;
            card.PercentHasteReduction = 0;
            card.DestroyImmunity = 0;
            if (!card.NativeAttributesAreLive)
            {
                Dictionary<string, int> seed = card.NativeAuraSeedAttributes ?? card.LiveAttributes;
                if (seed != null)
                {
                    if (seed.TryGetValue("PercentSlowReduction", out value)) card.PercentSlowReduction = value;
                    if (seed.TryGetValue("PercentFreezeReduction", out value)) card.PercentFreezeReduction = value;
                    if (seed.TryGetValue("PercentHasteReduction", out value)) card.PercentHasteReduction = value;
                    if (seed.TryGetValue("DestroyImmunity", out value)) card.DestroyImmunity = value;
                }
            }
            else if (!NativeSkillSupport.Empty(card.NativeEnchantment))
            {
                foreach (KeyValuePair<string, JsonValue> attr in card.NativeEnchantment["Attributes"].AsObject())
                {
                    string name = attr.Key;
                    value = attr.Value.AsInt();
                    if (name == "PercentSlowReduction") card.PercentSlowReduction = value;
                    else if (name == "PercentFreezeReduction") card.PercentFreezeReduction = value;
                    else if (name == "PercentHasteReduction") card.PercentHasteReduction = value;
                    else if (name == "DestroyImmunity") card.DestroyImmunity = value;
                }
            }
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
            double regen = side == Side.Player ? config.PlayerStartRegen : config.EnemyStartRegen;
            double rageMax = side == Side.Player ? config.PlayerRageMax : config.EnemyRageMax;
            double enragedMs = side == Side.Player ? config.PlayerEnragedDurationMs : config.EnemyEnragedDurationMs;
            List<JsonValue> runtime = new List<JsonValue>();
            // CombatOnly Add 光环按“每次使用叠加”执行，不进连续派生层，也不静态烘焙。
            List<JsonValue> useAuras = NativeDynamicAuras.ClassifyUseAuras(auras);
            foreach (JsonValue aura in auras)
            {
                CheckArea(aura);
                if (useAuras.Contains(aura)) continue;
                // Day cannot advance during a fight. The live attribute snapshot
                // already includes these fixed self bonuses; retaining that layer
                // also lets the card's remaining combat auras map normally. Pure
                // templates have no run day and must retain the explicit warning.
                if (alreadyLive && IsInheritedFixedDayAura(aura, source)) continue;
                if (!NativeSkillSupport.PrerequisitesSupported(aura["Prerequisites"])) throw new FormatException("动态光环条件");
                bool dynamic = NativeSkillSupport.HasPlayerPrerequisite(aura["Prerequisites"]);
                JsonValue auraAction = aura["Action"];
                JsonValue action = aura["Action"], target = action["Target"];
                string type = NativeSkillSupport.Type(action), attribute = NativeSkillSupport.Text(action, "AttributeType");
                string operation = NativeSkillSupport.Text(action, "Operation");
                // A live hero snapshot already contains permanent skill bonuses.
                // Keep the aura reversible without baking it into MaxHp again.
                bool heroIsLive = side == Side.Player ? config.PlayerHeroAttributesAreLive : config.EnemyHeroAttributesAreLive;
                Dictionary<string, int> heroAttributes = side == Side.Player ? config.PlayerAttributes : config.EnemyAttributes;
                if (type == "TAuraActionPlayerModifyAttribute" && heroIsLive && heroAttributes.ContainsKey(attribute) &&
                    (attribute == "HealthMax" || attribute == "HealthRegen" || attribute == "RageMax" || attribute == "EnragedDurationMax"))
                {
                    NativeDynamicAuras.Validate(aura);
                    runtime.Add(aura);
                    continue;
                }
                // 远征/商店选择集不是战斗棋盘；按场外 no-op 跳过。
                if (NativeSkillSupport.Type(target) == "TTargetCardSection" &&
                    NativeSkillSupport.Text(target, "TargetSection") == "SelectionSet") continue;
                if (type == "TAuraActionCardModifyAttribute")
                {
                    NativeDynamicAuras.Validate(aura);
                    runtime.Add(aura);
                    // 随机/人物条件目标必须由运行时层固定并逐帧重算，不能静态写到所有候选卡。
                    if (dynamic || NativeSkillSupport.Type(target) == "TTargetCardRandom") continue;
                }
                else if (type == "TAuraActionPlayerModifyAttribute" &&
                    (dynamic || attribute == "PercentDamageReduction" || attribute == "CritChance"))
                {
                    // 动态人物光环进入可撤销派生层；不能静态写死到 BattleConfig。
                    // 百分比减伤没有静态人物字段，即使无条件也必须走派生层。
                    NativeDynamicAuras.Validate(aura);
                    runtime.Add(aura);
                    continue;
                }
                BattleContext ctx = BattleEngine.InitBattle(config);
                InternalItem sourceItem = ctx.Items.Find(delegate(InternalItem item) { return item.CardId == source.Id && item.Side == side; });
                if (sourceItem == null) sourceItem = new InternalItem { Id = source.Id, Card = source, Side = side, Slot = -1 };
                if (!NativeSkillSupport.PrerequisitesMatch(ctx, sourceItem, new NativeSkillRule { Prerequisites = aura["Prerequisites"] })) continue;
                // 标签光环没有 Add/Set/Multiply 操作；不要用属性光环的校验把它整条拒绝。
                bool tagAura = type == "TAuraActionCardAddTagsList" || type == "TAuraActionCardAddTagsBySource";
                if (!tagAura && operation != "Add" && operation != "Subtract" && operation != "Set" && operation != "Multiply" && operation != "AdditiveMultiply") throw new FormatException("未知光环操作");
                if (type == "TAuraActionPlayerModifyAttribute")
                {
                    // 经济光环本轮 OUT，和战斗触发器上的经济动作一样按跳过处理，
                    // 不能因为一个场外 Income/Gold 光环回退整张卡。
                    string[] economy = { "Gold", "Income", "Experience", "Prestige", "RerollCostModifier" };
                    if (Array.IndexOf(economy, attribute) >= 0) continue;
                    if (NativeSkillSupport.Type(target) != "TTargetPlayerRelative" || NativeSkillSupport.Text(target, "TargetMode") != "Self" || !NativeSkillSupport.Empty(target["Conditions"]) ||
                        attribute != "HealthMax" && attribute != "HealthRegen" && attribute != "RageMax" && attribute != "EnragedDurationMax")
                        throw new FormatException("未知人物光环 " + attribute);
                    double value = StaticValue(config, source, side, action["Value"], attrs, ctx);
                    if (attribute == "HealthMax") maxHp = Operate(maxHp, value, operation);
                    else if (attribute == "HealthRegen") regen = Operate(regen, value, operation);
                    else if (attribute == "RageMax") rageMax = Operate(rageMax, value, operation);
                    else enragedMs = Operate(enragedMs, value, operation);
                }
                else if (type == "TAuraActionCardAddTagsList" || type == "TAuraActionCardAddTagsBySource")
                {
                    List<string> tags = new List<string>();
                    if (type == "TAuraActionCardAddTagsList")
                    {
                        if (action["Tags"].Kind != JsonKind.Array || action["Tags"].Count == 0) throw new FormatException("光环标签为空");
                        foreach (JsonValue tag in action["Tags"].AsArray()) tags.Add(tag.AsString());
                    }
                    else
                    {
                        foreach (LoadoutItem other in Board(config, side).Items)
                        {
                            CardDef origin = config.Cards[other.CardId];
                            if (!StaticTargetMatches(config, origin, source, action["Source"], side, ctx)) continue;
                            foreach (string tag in origin.Tags) if (!tags.Contains(tag)) tags.Add(tag);
                        }
                        if (tags.Count == 0) continue;
                    }
                    foreach (KeyValuePair<Side, LoadoutItem> entry in TargetEntries(config, source, side, target))
                    {
                        LoadoutItem loadout = entry.Value;
                        CardDef card;
                        if (!changed.TryGetValue(loadout.CardId, out card)) card = config.Cards[loadout.CardId].Clone();
                        if (!StaticTargetMatches(config, card, source, target, entry.Key, ctx)) continue;
                        card.Tags = new List<string>(card.Tags);
                        foreach (string tag in tags) if (!card.Tags.Contains(tag)) card.Tags.Add(tag);
                        changed[card.Id] = card;
                    }
                }
                else if (type == "TAuraActionCardModifyAttribute")
                {
                    if (!AuraAttribute(attribute)) throw new FormatException("未知卡牌光环属性 " + attribute);
                    foreach (KeyValuePair<Side, LoadoutItem> entry in TargetEntries(config, source, side, target))
                    {
                        LoadoutItem loadout = entry.Value;
                        Side targetSide = entry.Key;
                        CardDef card;
                        if (!changed.TryGetValue(loadout.CardId, out card)) card = config.Cards[loadout.CardId].Clone();
                        if (!StaticTargetMatches(config, card, source, target, targetSide, ctx)) continue;
                        if (card.NativeAuraSeedAttributes == null) card.NativeAuraSeedAttributes = new Dictionary<string, int>(card.LiveAttributes);
                        changed[card.Id] = card;
                        if (!alreadyLive)
                        {
                            // Target:null 的引用按当前被加成的卡求值，不能先算成同一个开场常量。
                            InternalItem valueTarget = new InternalItem { Id = card.Id, Card = card, Side = targetSide, Slot = loadout.Slot };
                            double value = StaticValue(config, source, side, action["Value"], attrs, ctx, valueTarget);
                            int current;
                            if (!card.LiveAttributes.TryGetValue(attribute, out current))
                                current = attribute == "Multicast" ? NativeDynamicAuras.BaseMulticast(card) : 0;
                            if (attribute == "Multicast" && card.NativeAuraSeedAttributes != null &&
                                !card.NativeAuraSeedAttributes.ContainsKey("Multicast"))
                                card.NativeAuraSeedAttributes["Multicast"] = current;
                            card.LiveAttributes[attribute] = (int)JsMath.Round(Operate(current, value, operation));
                            changed[card.Id] = card;
                            if (card.Id == source.Id) attrs[attribute] = card.LiveAttributes[attribute];
                        }
                    }
                }
                else throw new FormatException("未知光环 " + type);
            }
            source.NativeRuntimeAuras = runtime;
            source.NativeUseAuras = useAuras;
            CardDef updatedSource;
            if (changed.TryGetValue(source.Id, out updatedSource))
            {
                updatedSource.NativeRuntimeAuras = runtime;
                updatedSource.NativeUseAuras = useAuras;
            }
            foreach (KeyValuePair<string, CardDef> pair in changed) config.Cards[pair.Key] = pair.Value;
            if (!alreadyLive)
            {
                maxHp = Math.Max(1, maxHp);
                if (side == Side.Player)
                {
                    config.PlayerMaxHp = maxHp;
                    config.PlayerStartRegen = regen;
                    config.PlayerRageMax = Math.Max(0, rageMax);
                    config.PlayerEnragedDurationMs = Math.Max(0, enragedMs);
                }
                else
                {
                    if (config.EnemyStartHp.HasValue)
                    {
                        bool wasFull = Math.Abs(config.EnemyStartHp.Value - config.EnemyMaxHp) < 1e-9;
                        config.EnemyStartHp = wasFull ? maxHp : Math.Min(config.EnemyStartHp.Value, maxHp);
                    }
                    config.EnemyMaxHp = maxHp;
                    config.EnemyStartRegen = regen;
                    config.EnemyRageMax = Math.Max(0, rageMax);
                    config.EnemyEnragedDurationMs = Math.Max(0, enragedMs);
                }
            }
        }

        private static bool AuraAttribute(string attr)
        {
            string[] attrs = { "DamageAmount", "HealAmount", "ShieldApplyAmount", "BurnApplyAmount", "PoisonApplyAmount", "RegenApplyAmount", "RageApplyAmount", "TempoApplyAmount", "CritChance", "DamageCrit", "Lifesteal", "LifestealPercentage", "CooldownMax", "AmmoMax", "Multicast", "HasteAmount", "HasteTargets", "SlowAmount", "SlowTargets", "FreezeAmount", "FreezeTargets", "FlyingTargets", "EnchantTargets", "ChargeAmount", "ChargeTargets", "ReloadAmount", "ReloadTargets", "BurnRemoveAmount", "PoisonRemoveAmount", "PercentCooldownReduction", "FlatCooldownReduction", "CooldownDisabled", "SellPrice", "BuyPrice", "PercentSlowReduction", "PercentFreezeReduction", "PercentHasteReduction", "DestroyImmunity", "Heated", "Chilled" };
            // 原生计数器类属性（Custom_N）是卡的内部状态，允许光环直接修改。
            return Array.IndexOf(attrs, attr) >= 0 || !string.IsNullOrEmpty(attr) && attr.StartsWith("Custom_", StringComparison.Ordinal);
        }

        private static bool IsInheritedFixedDayAura(JsonValue aura, CardDef source)
        {
            JsonValue prerequisites = aura["Prerequisites"];
            if (prerequisites.Kind != JsonKind.Array || prerequisites.Count == 0) return false;
            foreach (JsonValue requirement in prerequisites.AsArray())
            {
                JsonValue condition = requirement["Conditions"];
                if (NativeSkillSupport.Type(requirement) != "TPrerequisiteRun" ||
                    NativeSkillSupport.Type(condition) != "TRunConditionalCurrentDay" ||
                    condition["CurrentDay"].Kind != JsonKind.Number ||
                    NativeSkillSupport.Text(condition, "ComparisonOperator") != "GreaterThanOrEqual") return false;
            }
            JsonValue action = aura["Action"], target = action["Target"], value = action["Value"];
            string attribute = NativeSkillSupport.Text(action, "AttributeType");
            if (NativeSkillSupport.Type(action) != "TAuraActionCardModifyAttribute" ||
                NativeSkillSupport.Text(action, "Operation") != "Add" ||
                NativeSkillSupport.Type(target) != "TTargetCardSelf" || !NativeSkillSupport.Empty(target["Conditions"]) ||
                !AuraAttribute(attribute) || !source.LiveAttributes.ContainsKey(attribute)) return false;
            if (NativeSkillSupport.Type(value) == "TFixedValue") return value["Value"].Kind == JsonKind.Number;
            string reference = NativeSkillSupport.Text(value, "AttributeType");
            return NativeSkillSupport.Type(value) == "TReferenceValueCardAttribute" &&
                !string.IsNullOrEmpty(reference) && reference.StartsWith("Custom_", StringComparison.Ordinal) &&
                source.LiveAttributes.ContainsKey(reference) && NativeSkillSupport.Empty(value["Modifier"]) &&
                NativeSkillSupport.Type(value["Target"]) == "TTargetCardSelf" && NativeSkillSupport.Empty(value["Target"]["Conditions"]);
        }

        // AllHands 光环作用于双方棋盘；其他目标仍只在光环所属一侧展开。
        private static List<KeyValuePair<Side, LoadoutItem>> TargetEntries(BattleConfig config, CardDef source, Side side, JsonValue target)
        {
            List<KeyValuePair<Side, LoadoutItem>> entries = new List<KeyValuePair<Side, LoadoutItem>>();
            string section = NativeSkillSupport.Text(target, "TargetSection");
            if (NativeSkillSupport.Type(target) == "TTargetCardOccupying")
            {
                int sourceSlot = -1;
                foreach (LoadoutItem item in Board(config, side).Items)
                    if (item.CardId == source.Id) { sourceSlot = item.Slot; break; }
                if (sourceSlot >= 0)
                    foreach (LoadoutItem item in Board(config, side).Items)
                        if (item.Slot == sourceSlot && item.CardId != source.Id)
                            entries.Add(new KeyValuePair<Side, LoadoutItem>(side, item));
                return entries;
            }
            bool allHands = NativeSkillSupport.Type(target) == "TTargetCardSection" && (section == "AllHands" || section == "AllBoards");
            Side[] sides = allHands ? new[] { Side.Player, Side.Enemy } :
                NativeSkillSupport.AbsoluteSection(section, Side.Player) ? new[] { Side.Player } :
                NativeSkillSupport.AbsoluteSection(section, Side.Enemy) ? new[] { Side.Enemy } :
                NativeSkillSupport.RelativeSection(section, false) ? new[] { SideUtil.Opposing(side) } : new[] { side };
            foreach (Side candidate in sides)
                foreach (LoadoutItem item in Board(config, candidate).Items)
                    entries.Add(new KeyValuePair<Side, LoadoutItem>(candidate, item));
            return entries;
        }

        private static bool StaticTargetMatches(BattleConfig config, CardDef card, CardDef source, JsonValue target, Side side, BattleContext ctx)
        {
            string type = NativeSkillSupport.Type(target);
            if (!NativeSkillSupport.ConditionSupported(target["Conditions"])) throw new FormatException("光环目标条件未支持");
            if (type == "TTargetCardSelf")
            {
                InternalItem selfItem = FindItem(ctx, card.Id, side) ?? new InternalItem { Card = card, Side = side };
                return card.Id == source.Id && NativeSkillSupport.CardMatches(selfItem, target["Conditions"], ctx);
            }
            if (type == "TTargetCardOccupying")
            {
                if (!NativeSkillSupport.IsSocketEffect(source)) return false;
                InternalItem sourceItem = FindSourceItem(ctx, source, side);
                InternalItem targetItem = FindItem(ctx, card.Id, side);
                return sourceItem != null && targetItem != null && sourceItem.Slot == targetItem.Slot &&
                    !(target["ExcludeSelf"].AsBool() && card.Id == source.Id) &&
                    NativeSkillSupport.CardMatches(targetItem, target["Conditions"], ctx);
            }
            string section = NativeSkillSupport.Text(target, "TargetSection");
            if (type == "TTargetCardSection" && section == "AllHands")
            {
                if (target["ExcludeSelf"].AsBool() && card.Id == source.Id) return false;
                InternalItem allHandsItem = FindItem(ctx, card.Id, side) ?? new InternalItem { Card = card, Side = side };
                return NativeSkillSupport.CardMatches(allHandsItem, target["Conditions"], ctx);
            }
            if (type == "TTargetCardXMost")
            {
                Target mapped = SkillMapper.MapCardTarget(target);
                InternalItem sourceItem = FindSourceItem(ctx, source, SourceSideForTarget(target, side));
                List<InternalItem> selected = Effects.NativeItemsForTarget(ctx, sourceItem, mapped, 1, null);
                return selected.Exists(delegate(InternalItem candidate) { return candidate.CardId == card.Id && candidate.Side == side; });
            }
            if (type == "TTargetCardPositional" && NativeSkillSupport.Text(target, "Origin") == "Self" &&
                NativeSkillSupport.PositionModeSupported(NativeSkillSupport.Text(target, "TargetMode")))
            {
                InternalItem sourceItem = FindSourceItem(ctx, source, SourceSideForTarget(target, side));
                InternalItem targetItem = FindItem(ctx, card.Id, side);
                return !(target["ExcludeSelf"].AsBool() && card.Id == source.Id) &&
                    NativeSkillSupport.PositionalMatch(ctx, sourceItem, targetItem, NativeSkillSupport.Text(target, "TargetMode"), target["IncludeOrigin"].AsBool()) &&
                    NativeSkillSupport.CardMatches(targetItem, target["Conditions"], ctx);
            }
            if (type == "TTargetCardRandom" && section == "SelfNeighbors")
            {
                InternalItem sourceItem = FindItem(ctx, source.Id, side);
                InternalItem targetItem = FindItem(ctx, card.Id, side) ?? new InternalItem { Card = card, Side = side };
                return NativeSkillSupport.Adjacent(ctx, sourceItem, targetItem) && NativeSkillSupport.CardMatches(targetItem, target["Conditions"], ctx);
            }
            if (type == "TTargetCardRandom" && NativeSkillSupport.SectionSupported(section))
            {
                if (!NativeSkillSupport.SectionSideMatches(section, side, side)) return false;
                InternalItem targetItem = FindItem(ctx, card.Id, side) ?? new InternalItem { Card = card, Side = side };
                return !(target["ExcludeSelf"].AsBool() && card.Id == source.Id) &&
                    NativeSkillSupport.CardMatches(targetItem, target["Conditions"], ctx);
            }
            if (type != "TTargetCardSection" || !NativeSkillSupport.SectionSupported(section)) throw new FormatException("光环目标区域未支持");
            if (!NativeSkillSupport.ConditionSupported(target["Conditions"])) throw new FormatException("光环目标条件未支持");
            InternalItem item = FindItem(ctx, card.Id, side) ?? new InternalItem { Card = card, Side = side };
            return !(target["ExcludeSelf"].AsBool() && card.Id == source.Id) && NativeSkillSupport.CardMatches(item, target["Conditions"], ctx);
        }

        private static InternalItem FindItem(BattleContext ctx, string cardId, Side side)
        {
            return ctx.Items.Find(delegate(InternalItem candidate) { return candidate.CardId == cardId && candidate.Side == side; });
        }

        // 相对 Opponent* 的目标侧是来源的对侧；技能来源不在棋盘时用它还原来源阵营。
        private static Side SourceSideForTarget(JsonValue target, Side targetSide)
        {
            return NativeSkillSupport.RelativeSection(NativeSkillSupport.Text(target, "TargetSection"), false)
                ? SideUtil.Opposing(targetSide) : targetSide;
        }

        // 光环来源可能是技能或异侧卡牌；按实例身份找来源，不能用目标侧过滤。
        private static InternalItem FindSourceItem(BattleContext ctx, CardDef source, Side fallbackSide)
        {
            InternalItem item = ctx.Items.Find(delegate(InternalItem candidate) { return candidate.CardId == source.Id; });
            if (item != null) return item;
            return new InternalItem { Id = source.Id, Card = source, Side = fallbackSide, Slot = -1 };
        }

        private static double StaticValue(BattleConfig config, CardDef source, Side side, JsonValue raw, Dictionary<string, double> attrs, BattleContext ctx, InternalItem valueTarget = null)
        {
            // 与结算期共用同一套 NumExpr 求值：映射期只决定结构，属性/计数/聚合
            // 都从当前棋盘读取，避免把动态值永久写成开场常量。
            NumExpr expr = SkillMapper.MapValue(raw, attrs, null);
            InternalItem sourceItem = FindItem(ctx, source.Id, side) ?? new InternalItem { Id = source.Id, Card = source, Side = side, Slot = -1 };
            return Effects.EvaluateNum(expr, ctx, sourceItem, ctx.Hero(side), null, null, valueTarget);
        }

        private static double Operate(double before, double value, string operation)
        {
            if (operation == "Add") return before + value;
            if (operation == "Subtract") return before - value;
            if (operation == "Set") return value;
            if (operation == "AdditiveMultiply") return before * (1 + value);
            return before * value;
        }

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
