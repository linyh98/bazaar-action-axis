using System;
using System.Collections.Generic;

namespace BazaarActionAxis.Engine
{
    // 战斗内“变成场上已有道具”的变形（打印机/镜子/病毒等）。
    // 场外随机卡池变形不在支持范围；规则未校准时由映射层 fail-closed。
    public static class NativeTransform
    {
        public const string SlusheeTemplate = "8307efa3-97b6-4645-a3cb-de4930880ef4";
        public static readonly string[] FixedTemplateIds = { SlusheeTemplate };

        // 原生模板没有给出“复制哪张卡”的引用，只能按卡牌身份校准。
        public static string DonorRule(CardDef card)
        {
            if (card == null) return null;
            string name = NativeSkillSupport.Text(card.NativeTemplate, "InternalName");
            if (string.IsNullOrEmpty(name)) name = card.Name;
            if (string.IsNullOrEmpty(name)) return null;
            switch (name)
            {
                // 变成左侧小型物品的 N 个副本（N 来自 SpawnContext.Limit）。
                case "3D Printer": return "leftSmall";
                // Mirror 的 live SpawnContext 只过滤 Medium，不再额外排除 Legendary。
                case "Mirror": return "leftMedium";
                // Green Screen 先随机附魔自身，再复制左侧 Medium；副本使用来源附魔。
                case "Green Screen": return "leftMediumEnchanted";
                // Hologram 的 live 过滤只有“另一张 Small + 排除自身模板”，没有 Legendary。
                case "Hologram Projector": return "otherSmallAny";
                // Quicksilver 明确要求另一张 Small 且非 Legendary。
                case "Quicksilver": return "otherSmall";
                // 把目标变成 Virus 本身。
                case "Virus": return "self";
                // Blender 把相邻 Small 变成固定的 Slushee 模板（供体不在棋盘）。
                case "Blender": return "fixedSlushee";
                // Grabinator 复制敌方最左 Medium 场上物品（跨侧）。
                case "Grabinator": return "enemyLeftMedium";
                // Blender/Potion Potion 的固定供体来自完整卡池；见 FindPoolDonor。
                case "Potion Potion": return "smallPotionSelf";
                // 把被使用的 Potion / 左侧 Small 变成另一个 Potion。
                case "Recycling Bin":
                case "[DEBUG] Recycling Bin":
                case "[DEBUG] Start of Fight Transform": return "potionTarget";
                // 把敌方 Medium 变成 2 个 Small 物品。
                case "Hacksaw": return "smallItemTarget";
                default: return null;
            }
        }

        // TActionCardTransformDestroyed 的原生模板同样只给 SpawnContext.Limit。
        // 供体按卡牌身份与原生 tooltip 校准：Abducted Cow → 同品质 Jerky；
        // Drone Crusher → 任意英雄的同品质小型 Drone。
        public static string DestroyDonorRule(CardDef card)
        {
            string name = DisplayName(card);
            if (name == "Abducted Cow") return "jerky";
            if (name == "Drone Crusher") return "smallDrone";
            return null;
        }

        // 回放先知：变形结果（新模板）在原生 CardTransformed 事件里有记录，
        // 由 ReplayRandomTape.Transforms 提供。按被变形实例取用；候选池里
        // 没有对应模板时回退 RNG（磁带记录保留给诊断）。
        private static CardDef OracleDonor(BattleContext ctx, InternalItem target, List<CardDef> candidates)
        {
            if (ctx == null || ctx.ReplayRandom == null || target == null ||
                candidates == null || candidates.Count == 0)
                return null;
            int frame = (int)Math.Round(ctx.Now / 0.05);
            string templateId = ctx.ReplayRandom.ConsumeTransform(target.Id, frame);
            if (string.IsNullOrEmpty(templateId)) return null;
            foreach (CardDef candidate in candidates)
            {
                if (string.Equals(candidate.BaseItemId, templateId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(candidate.Id, templateId, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            return null;
        }

        public static CardDef FindDestroyDonor(BattleContext ctx, InternalItem source, InternalItem target, string rule)
        {
            if (rule == null || source == null || source.Card == null || target == null || target.Card == null) return null;
            string tier = target.Card.Tier;
            if (string.IsNullOrEmpty(tier)) tier = source.Card.Tier;
            List<CardDef> candidates = new List<CardDef>();
            foreach (CardDef card in ctx.Config.SpawnPool)
            {
                if (card == null) continue;
                if (!string.IsNullOrEmpty(tier) && card.Tier != tier) continue;
                if (rule == "jerky")
                {
                    if (!string.Equals(DisplayName(card), "Jerky", StringComparison.Ordinal)) continue;
                }
                else if (rule == "smallDrone")
                {
                    if (card.Size != "Small") continue;
                    if (!HasTag(card, "Drone")) continue;
                }
                else return null;
                candidates.Add(card);
            }
            if (candidates.Count == 0)
            {
                string warning = "缺少 TransformDestroyed 供体：" + DisplayName(source.Card) + " → " + rule +
                    "（品质 " + (string.IsNullOrEmpty(tier) ? "UNKNOWN" : tier) + "）";
                if (!ctx.Warnings.Contains(warning)) ctx.Warnings.Add(warning);
                return null;
            }
            CardDef oracleDestroy = OracleDonor(ctx, target, candidates);
            if (oracleDestroy != null) return oracleDestroy;
            return candidates[ctx.Rng.Int(candidates.Count)];
        }

        private static string DisplayName(CardDef card)
        {
            if (card == null) return null;
            string name = NativeSkillSupport.Text(card.NativeTemplate, "InternalName");
            if (string.IsNullOrEmpty(name)) name = card.Name;
            if (string.IsNullOrEmpty(name)) name = card.BaseItemId;
            return name;
        }

        private static bool HasTag(CardDef card, string tag)
        {
            return card.Tags != null && card.Tags.Contains(tag) ||
                card.HiddenTags != null && card.HiddenTags.Contains(tag);
        }

        public static InternalItem FindDonor(BattleContext ctx, InternalItem source, InternalItem target, string rule)
        {
            if (rule == "self") return source;
            if (rule == "fixedSlushee") return null;
            if (rule == "smallPotionSelf" || rule == "potionTarget" || rule == "smallItemTarget")
                return FindPoolDonor(ctx, source, target, rule);
            List<InternalItem> candidates = new List<InternalItem>();
            foreach (InternalItem item in ctx.Items)
            {
                if (item.Card == null || item.Destroyed || item.Id == target.Id) continue;
                bool enemyDonor = rule == "enemyLeftMedium";
                if (enemyDonor ? item.Side == source.Side : item.Side != source.Side) continue;
                if (rule == "leftSmall" || rule == "leftMedium" || rule == "leftMediumEnchanted")
                {
                    if (!NativeSkillSupport.PositionalMatch(ctx, target, item, "LeftCard")) continue;
                    if (rule == "leftSmall" && item.Card.Size != "Small") continue;
                    if (rule != "leftSmall" && item.Card.Size != "Medium") continue;
                    candidates.Add(item);
                }
                else if (rule == "otherSmallAny" || rule == "otherSmall")
                {
                    if (item.Card.Size != "Small") continue;
                    // live 的 TCardConditionalId 排除的是该模板的所有实例，而不只是当前对象。
                    if (item.Card.BaseItemId == source.Card.BaseItemId) continue;
                    if (rule == "otherSmall" && item.Card.Tier == "Legendary") continue;
                    candidates.Add(item);
                }
                else if (enemyDonor)
                {
                    if (item.Card.Size != "Medium") continue;
                    candidates.Add(item);
                }
            }
            if (candidates.Count == 0) return null;
            if (rule == "leftSmall" || rule == "leftMedium" || rule == "leftMediumEnchanted")
            {
                InternalItem best = candidates[0];
                foreach (InternalItem item in candidates) if (item.Slot > best.Slot) best = item;
                return best;
            }
            if (rule == "enemyLeftMedium")
            {
                InternalItem best = candidates[0];
                foreach (InternalItem item in candidates) if (item.Slot < best.Slot) best = item;
                return best;
            }
            foreach (InternalItem item in candidates)
            {
                if (OracleDonor(ctx, target, new List<CardDef> { item.Card }) != null) return item;
            }
            return candidates[ctx.Rng.Int(candidates.Count)];
        }

        // 随机池供体（Potion／Small item）来自插件显式注入的 SpawnPool。
        // 只接受有完整 NativeTemplate 的同品质卡；缺失时告警并保留原卡。
        private static InternalItem FindPoolDonor(BattleContext ctx, InternalItem source, InternalItem target, string rule)
        {
            if (ctx == null || source == null || source.Card == null || target == null || target.Card == null) return null;
            List<CardDef> pool = new List<CardDef>(ctx.Config.SpawnPool);
            foreach (CardDef card in ctx.Config.Cards.Values)
                if (!pool.Contains(card)) pool.Add(card);
            List<CardDef> candidates = new List<CardDef>();
            foreach (CardDef card in pool)
            {
                if (card == null || NativeSkillSupport.Empty(card.NativeTemplate)) continue;
                if (!string.IsNullOrEmpty(source.Card.Tier) && card.Tier != source.Card.Tier) continue;
                if (rule == "smallPotionSelf" || rule == "potionTarget")
                {
                    if (!HasTag(card, "Potion")) continue;
                    if (rule == "smallPotionSelf" && card.Size != "Small") continue;
                    if (card.BaseItemId == source.Card.BaseItemId) continue;
                    if (rule == "potionTarget" && card.BaseItemId == target.Card.BaseItemId) continue;
                }
                else if (rule == "smallItemTarget")
                {
                    if (card.Size != "Small") continue;
                    if (card.BaseItemId == target.Card.BaseItemId) continue;
                }
                else return null;
                candidates.Add(card);
            }
            if (candidates.Count == 0)
            {
                string warning = "缺少变形池供体：" + DisplayName(source.Card) + " → " + rule +
                    "（品质 " + (string.IsNullOrEmpty(source.Card.Tier) ? "UNKNOWN" : source.Card.Tier) + "）";
                if (!ctx.Warnings.Contains(warning)) ctx.Warnings.Add(warning);
                return null;
            }
            CardDef chosen = OracleDonor(ctx, target, candidates) ?? candidates[ctx.Rng.Int(candidates.Count)];
            InternalItem donor = new InternalItem();
            donor.Side = source.Side;
            donor.Slot = target.Slot;
            donor.Card = chosen;
            donor.CardId = chosen.Id;
            return donor;
        }

        // 固定模板供体不在棋盘上，来自插件注入的 SpawnPool（完整卡库）。
        // 找不到同品质原生模板时必须 fail-closed，不能用本地文本近似或猜品质。
        public static CardDef FindFixedDonor(BattleContext ctx, InternalItem source, string templateId)
        {
            if (ctx == null || source == null || source.Card == null || string.IsNullOrEmpty(templateId)) return null;
            string tier = source.Card.Tier;
            List<CardDef> pool = new List<CardDef>(ctx.Config.SpawnPool);
            foreach (CardDef card in ctx.Config.Cards.Values)
                if (!pool.Contains(card)) pool.Add(card);
            foreach (CardDef card in pool)
            {
                if (card == null || card.BaseItemId != templateId) continue;
                if (!string.IsNullOrEmpty(tier) && card.Tier != tier) continue;
                if (NativeSkillSupport.Empty(card.NativeTemplate)) continue;
                return card;
            }
            JsonValue native;
            if (ctx.Config.NativeTemplates.TryGetValue(templateId, out native) &&
                !NativeSkillSupport.Empty(native))
            {
                CardDef card = new CardDef
                {
                    Id = "native:" + templateId + ":" + (tier ?? "default"),
                    BaseItemId = templateId,
                    Name = NativeSkillSupport.Text(native, "InternalName"),
                    Size = NativeSkillSupport.Text(native, "Size") ?? "Small",
                    Tier = tier,
                    NativeTemplate = native,
                    NativeAttributesAreLive = false,
                    ParseStatus = "native-template",
                    DataSource = "native-template"
                };
                card.LiveAttributes = MonsterTemplateExtractor.TierAttributes(native, tier);
                card.NativeAuraSeedAttributes = new Dictionary<string, int>(card.LiveAttributes);
                int cooldown;
                if (card.LiveAttributes.TryGetValue("CooldownMax", out cooldown)) card.Cooldown = cooldown / 1000.0;
                card.Passive = card.Cooldown <= 0;
                return card;
            }
            string warning = "缺少固定变形供体：" + DisplayName(source.Card) + " → " + templateId +
                "（品质 " + (string.IsNullOrEmpty(tier) ? "UNKNOWN" : tier) + "）";
            if (!ctx.Warnings.Contains(warning)) ctx.Warnings.Add(warning);
            return null;
        }

        // 旧实例离场保留历史，副本以新实例进入原槽位。
        public static bool Apply(BattleContext ctx, InternalItem source, InternalItem target, InternalItem donor,
            int copies, double time, int depth, int? chainId, bool sourceEnchantWins = false)
        {
            if (target == null || donor == null || donor.Card == null) return false;
            // The donor supplies a template, not its enchantment. The copied
            // card retains the target's enchantment (Green Screen
            // has already enchanted itself before this action).
            string enchantment = sourceEnchantWins ? source.Card.EnchantmentName : target.Card.EnchantmentName;
            return ApplyCard(ctx, source, target, donor.Card, enchantment, copies, time, depth, chainId);
        }

        // 固定模板供体（Blender → Slushee）同样按来源品质重建；InheritEnchantment=false
        // 的固定供体不把来源附魔带过去。
        public static bool ApplyFixed(BattleContext ctx, InternalItem source, InternalItem target, CardDef donorCard,
            int copies, double time, int depth, int? chainId)
        {
            return ApplyCard(ctx, source, target, donorCard, null, copies, time, depth, chainId);
        }

        private static bool ApplyCard(BattleContext ctx, InternalItem source, InternalItem target, CardDef donorCard,
            string enchantment, int copies, double time, int depth, int? chainId)
        {
            if (target == null || donorCard == null || target.Card == null || target.Destroyed) return false;
            // 原生证据：副本按来源卡品质生成，并保留被变形卡已经流逝的冷却进度。
            // 不能克隆供体当前实时属性；临时加成/光环应由新槽位重新计算。
            double elapsed = target.CooldownMax > 0 ? Math.Max(0, target.CooldownMax - target.Cooldown) : 0;
            CardDef card;
            try
            {
                card = NativeCardAbilityMapper.CopyAtTier(donorCard, source.Card.Tier, enchantment);
            }
            catch (Exception error)
            {
                ctx.Warnings.Add("变形复制未完整模拟，保留原物品：" + error.Message);
                return false;
            }
            double cooldown = Math.Max(0, card.Cooldown - elapsed);
            if (cooldown > card.Cooldown) cooldown = card.Cooldown;
            return ApplyCore(ctx, source, target, card, cooldown, copies, time, depth, chainId);
        }

        // TransformDestroyed 的原生事件（CombatSimEventCardTransformed）会创建
        // 新的实例 ID：旧实例在同槽位标记为 Transformed/离场，新实例按供体
        // 品质与满冷却出生。普通变形同样创建新实例，但继承冷却进度。
        public static bool ApplyDestroyed(BattleContext ctx, InternalItem source, InternalItem target,
            CardDef donor, int copies, double time, int depth, int? chainId)
        {
            if (target == null || donor == null || target.Card == null || target.Destroyed) return false;
            copies = Math.Max(1, copies);
            CardDef card = donor.Clone();
            if (!string.IsNullOrEmpty(card.DataSource) && card.DataSource != "native-template")
            {
                string warning = "变形生成卡使用本地卡库近似：" + DisplayName(card);
                if (!ctx.Warnings.Contains(warning)) ctx.Warnings.Add(warning);
                card.ParseStatus = "partial";
            }
            card.NativeAttributesAreLive = false;
            if (card.LiveAttributes == null) card.LiveAttributes = new Dictionary<string, int>();
            card.NativeAuraSeedAttributes = new Dictionary<string, int>(card.LiveAttributes);
            if (card.Tags == null) card.Tags = new List<string>();

            int span = PanelModel.CardSpan(card), startSlot = target.Slot;
            int endSlot = startSlot + copies * span;
            List<InternalItem> occupied = new List<InternalItem>(ctx.Items);
            occupied.AddRange(ctx.PendingItems);
            if (endSlot > ctx.Rules.MaxSlots || occupied.Exists(delegate(InternalItem item)
                { return item != target && !item.Destroyed && item.Side == target.Side &&
                    item.Slot < endSlot && item.Slot + PanelModel.CardSpan(item.Card) > startSlot; }))
            {
                ctx.Warnings.Add("变形空间不足，保留原物品：" + target.Card.DisplayName());
                return false;
            }

            string sourceId = source.Id, sourceCardId = source.CardId;
            Side sourceSide = source.Side;
            string originalId = target.Id, originalCardId = target.CardId;
            // 旧实例离开战斗：不删除历史，但不参与目标/冷却/触发；不发 destroy 事件。
            target.Destroyed = true;
            target.Disabled = true;
            target.CombatDisabled = true;
            target.Generation++;
            if (target.History != null) target.History.RemovedAt = time;

            for (int index = 0; index < copies; index++)
            {
                InternalItem clone = new InternalItem();
                clone.Id = "transform:" + (ctx.NextEventId++) + ":" + card.Id;
                clone.Side = target.Side;
                if (ctx.ReplayRandom != null)
                    ctx.ReplayRandom.BindTransformIdentity(originalId, clone.Id, card.BaseItemId,
                        clone.Side, startSlot + index * span, index, (int)Math.Round(time / 0.05));
                Adopt(ctx, sourceId, sourceSide, originalId, originalCardId, clone,
                    index == 0 ? card : card.Clone(), startSlot + index * span,
                    card.Cooldown, time, depth, chainId);
                ctx.PendingItems.Add(clone);
            }
            return true;
        }

        private static bool ApplyCore(BattleContext ctx, InternalItem source, InternalItem target, CardDef card,
            double cooldown, int copies, double time, int depth, int? chainId)
        {
            copies = Math.Max(1, copies);
            int startSlot = target.Slot;
            int span = PanelModel.CardSpan(card), endSlot = startSlot + copies * span;
            List<InternalItem> occupied = new List<InternalItem>(ctx.Items);
            occupied.AddRange(ctx.PendingItems);
            if (endSlot > ctx.Rules.MaxSlots || occupied.Exists(delegate(InternalItem item)
                { return item != target && !item.Destroyed && item.Side == target.Side &&
                    item.Slot < endSlot && item.Slot + PanelModel.CardSpan(item.Card) > startSlot; }))
            {
                ctx.Warnings.Add("变形空间不足，保留原物品：" + target.Card.DisplayName());
                return false;
            }
            string sourceId = source.Id, sourceCardId = source.CardId;
            Side sourceSide = source.Side;
            string originalId = target.Id, originalCardId = target.CardId;
            target.Destroyed = true;
            target.Disabled = true;
            target.CombatDisabled = true;
            target.Generation++;
            if (target.History != null) target.History.RemovedAt = time;
            for (int index = 0; index < copies; index++)
            {
                InternalItem clone = new InternalItem();
                clone.Id = "transform:" + (ctx.NextEventId++) + ":" + card.Id;
                clone.Side = target.Side;
                if (ctx.ReplayRandom != null)
                    ctx.ReplayRandom.BindTransformIdentity(originalId, clone.Id, card.BaseItemId,
                        clone.Side, startSlot + index * span, index, (int)Math.Round(time / 0.05));
                Adopt(ctx, sourceId, sourceSide, originalId, originalCardId, clone, index == 0 ? card : card.Clone(), startSlot + index * span,
                    cooldown, time, depth, chainId);
                ctx.PendingItems.Add(clone);
            }
            return true;
        }

        private static void Adopt(BattleContext ctx, string actorId, Side actorSide, string originalId, string originalCardId,
            InternalItem item, CardDef card, int slot, double cooldown, double time, int depth, int? chainId)
        {
            item.Generation++;
            item.Card = card;
            item.CardId = card.Id;
            item.Slot = slot;
            item.Cooldown = cooldown;
            item.CooldownMax = card.Cooldown;
            item.AmmoMax = card.AmmoMax;
            item.Ammo = card.AmmoMax ?? 0;
            item.MulticastBonus = StaticMulticast(card);
            item.CritBonus = StaticCrit(card);
            item.Lifesteal = StaticLifesteal(card);
            item.DamageBonus = 0;
            item.Disabled = false;
            item.CombatDisabled = false;
            item.Destroyed = false;
            item.Flying = false;
            item.FlyingSince = -1;
            item.FrozenUntil = -1;
            item.HasteUntil = -1;
            item.SlowUntil = -1;
            item.ExtraTags.Clear();
            item.AuraTags.Clear();
            item.TagAurasInitialized = false;
            item.TimedAttributes.Clear();
            item.AttributeDeltas.Clear();
            item.AuraBaseAttributes = new Dictionary<string, double>();
            foreach (KeyValuePair<string, int> pair in card.NativeAuraSeedAttributes ?? card.LiveAttributes) item.AuraBaseAttributes[pair.Key] = pair.Value;
            if (!item.AuraBaseAttributes.ContainsKey("CooldownMax")) item.AuraBaseAttributes["CooldownMax"] = card.Cooldown * 1000;
            item.AuraAttributes.Clear();
            item.AuraCooldownFlat = 0;
            item.AuraCooldownPercent = 0;
            ctx.RecordItemState(item, time);

            BattleEvent ev = new BattleEvent();
            ev.Time = time;
            ev.Side = item.Side;
            ev.SourceItemId = originalId;
            ev.SourceCardId = originalCardId;
            ev.TargetSide = item.Side;
            ev.TargetItemId = item.Id;
            ev.Kind = "transform";
            ev.Value = 1;
            ev.Note = card.DisplayName();
            ev.ChainId = chainId.HasValue ? chainId.Value : ctx.NextChainId++;
            ev.Depth = depth;
            ctx.Events.Add(Effects.FinishEvent(ctx, ev));

            // 变形后的卡是事件主体；Source 是造成变形的行动者。
            TriggerPayload payload = new TriggerPayload();
            payload.Event = "onCardTransformed";
            payload.SourceSide = item.Side;
            payload.SourceItemId = item.Id;
            payload.TargetSide = item.Side;
            payload.TargetItemId = item.Id;
            payload.ActorSide = actorSide;
            payload.ActorItemId = actorId;
            payload.ChainId = chainId;
            payload.Depth = depth;
            ctx.TriggerQueue.Enqueue(payload);
        }

        private static int StaticMulticast(CardDef card)
        {
            if (card.HasMulticastOverride) return card.MulticastBonus;
            int bonus = 0;
            foreach (Effect effect in card.Effects)
                if (effect.Kind == "multicast") bonus += Math.Max(0, (effect.Count ?? 1) - 1);
            return bonus;
        }

        private static double StaticCrit(CardDef card)
        {
            double bonus = 0;
            foreach (Effect effect in card.Effects)
                if (effect.Kind == "crit") bonus += effect.Chance;
            return bonus;
        }

        private static double StaticLifesteal(CardDef card)
        {
            double lifesteal = 0;
            foreach (Effect effect in card.Effects)
                if (effect.Kind == "lifesteal") lifesteal += effect.Percent;
            return lifesteal;
        }
    }
}
