using System;
using System.Collections.Generic;

namespace BazaarActionAxis.Engine
{
    // 战斗内附魔变化。战斗开始时已附魔的卡没有可还原的基底，
    // 覆盖/移除一律 fail-closed，而不是伪造一个未附魔状态。
    public static class NativeEnchant
    {
        public static bool Apply(BattleContext ctx, InternalItem source, InternalItem item, string enchantment,
            bool preventOverride, double time, int depth, int chainId)
        {
            if (item == null || item.Card == null || item.Destroyed || string.IsNullOrEmpty(enchantment)) return false;
            if (string.Equals(item.Card.EnchantmentName, enchantment, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(item.Card.EnchantmentName))
            {
                if (preventOverride)
                {
                    Block(ctx, source, item, enchantment, "目标已有附魔且不可覆盖", time, depth, chainId);
                    return false;
                }
                if (item.PreEnchantCard == null)
                {
                    Block(ctx, source, item, enchantment, "战斗开始时附魔的卡无法在战斗内覆盖", time, depth, chainId);
                    return false;
                }
            }

            CardDef baseCard = item.PreEnchantCard ?? item.Card;
            // 战斗开始时没有光环的卡不会建立 AuraBaseAttributes；战斗内附魔
            // 不能因此把基础属性（Multicast/SellPrice/CritChance 等）当成 0。
            Dictionary<string, double> baseAura = item.PreEnchantCard == null
                ? (item.AuraBaseAttributes != null && item.AuraBaseAttributes.Count > 0
                    ? CopyAura(item.AuraBaseAttributes) : SnapshotAttributes(baseCard))
                : CopyAura(item.PreEnchantAuraBase);
            CardDef enchanted;
            try
            {
                enchanted = NativeCardAbilityMapper.BattleEnchant(baseCard, enchantment);
            }
            catch (Exception error)
            {
                Block(ctx, source, item, enchantment, error.Message, time, depth, chainId);
                return false;
            }

            if (item.PreEnchantCard == null)
            {
                item.PreEnchantCard = baseCard;
                item.PreEnchantAuraBase = CopyAura(baseAura);
            }
            item.Card = enchanted;
            item.AuraBaseAttributes = OverlayAttributes(baseAura, enchanted.NativeEnchantment);
            NativeDynamicAuras.Refresh(ctx);
            ctx.RecordItemState(item, time);
            Emit(ctx, source, item, "enchant", 1, enchantment, time, depth, chainId);
            return true;
        }

        public static bool Remove(BattleContext ctx, InternalItem source, InternalItem item, double time, int depth, int chainId)
        {
            if (item == null || item.Card == null || item.Destroyed || string.IsNullOrEmpty(item.Card.EnchantmentName)) return false;
            if (item.PreEnchantCard == null)
            {
                Block(ctx, source, item, item.Card.EnchantmentName, "战斗开始时附魔的卡无法在战斗内移除", time, depth, chainId);
                return false;
            }
            string removed = item.Card.EnchantmentName;
            item.Card = item.PreEnchantCard;
            item.AuraBaseAttributes = CopyAura(item.PreEnchantAuraBase);
            item.PreEnchantCard = null;
            item.PreEnchantAuraBase = null;
            NativeDynamicAuras.Refresh(ctx);
            ctx.RecordItemState(item, time);
            Emit(ctx, source, item, "enchantRemove", 1, removed, time, depth, chainId);
            return true;
        }

        public static string Pick(BattleContext ctx, InternalItem item, List<string> names, List<double> weights)
        {
            if (item == null || item.Card == null || names == null || names.Count == 0) return null;
            List<string> available = new List<string>();
            List<double> availableWeights = new List<double>();
            double total = 0;
            for (int index = 0; index < names.Count; index++)
            {
                string name = names[index];
                double weight = weights != null && index < weights.Count ? weights[index] : 1;
                if (string.IsNullOrEmpty(name) || weight <= 0) continue;
                if (!HasDefinition(item.Card, name)) continue;
                available.Add(name);
                availableWeights.Add(weight);
                total += weight;
            }
            if (available.Count == 0)
            {
                string warning = item.Card.DisplayName() + "：附魔池没有该卡可用的定义";
                if (!ctx.Warnings.Contains(warning)) ctx.Warnings.Add(warning);
                return null;
            }
            // 先按普通抽取消耗随机数流，再让回放观测结果覆盖：磁带命中与否
            // 都不改变流位置，附魔化的决策与未磁带化的判定互不干扰。
            double roll = ctx.Rng.Next() * total;
            string rolled = available[available.Count - 1];
            for (int index = 0; index < available.Count; index++)
            {
                roll -= availableWeights[index];
                if (roll < 0)
                {
                    rolled = available[index];
                    break;
                }
            }
            // 回放里已观测的随机附魔结果优先；仅当该名不在可用池时用抽签结果。
            if (ctx.ReplayRandom != null && ctx.ReplayRandom.Scope != null)
            {
                string recorded = ctx.ReplayRandom.PickEnchantment(
                    ctx.ReplayRandom.Scope.Source, ctx.ReplayRandom.Scope.Frame, item.Id);
                if (recorded != null)
                {
                    if (available.Contains(recorded)) return recorded;
                    ctx.ReplayRandom.Audit.InvalidChoices++;
                    if (ctx.ReplayRandom.Audit.Issues.Count < 30)
                        ctx.ReplayRandom.Audit.Issues.Add("enchant not in pool: " + recorded +
                            " on " + item.Id + " at frame " + ctx.ReplayRandom.Scope.Frame);
                }
            }
            return rolled;
        }

        private static bool HasDefinition(CardDef card, string enchantment)
        {
            JsonValue template = card.NativeTemplate;
            return !NativeSkillSupport.Empty(template) && template["Enchantments"].Kind == JsonKind.Object &&
                template["Enchantments"].Has(enchantment);
        }

        private static Dictionary<string, double> CopyAura(Dictionary<string, double> aura)
        {
            return aura == null ? new Dictionary<string, double>() : new Dictionary<string, double>(aura);
        }

        // 与 NativeDynamicAuras.Initialize 使用同一基准：优先实时种子表。
        private static Dictionary<string, double> SnapshotAttributes(CardDef card)
        {
            Dictionary<string, double> result = new Dictionary<string, double>();
            if (card == null) return result;
            Dictionary<string, int> seed = card.NativeAuraSeedAttributes ?? card.LiveAttributes;
            if (seed != null)
                foreach (KeyValuePair<string, int> pair in seed) result[pair.Key] = pair.Value;
            if (!result.ContainsKey("CooldownMax")) result["CooldownMax"] = card.Cooldown * 1000;
            return result;
        }

        private static Dictionary<string, double> OverlayAttributes(Dictionary<string, double> aura, JsonValue definition)
        {
            if (NativeSkillSupport.Empty(definition)) return aura;
            foreach (KeyValuePair<string, JsonValue> attr in definition["Attributes"].AsObject())
                aura[attr.Key] = attr.Value.AsDouble();
            return aura;
        }

        private static void Block(BattleContext ctx, InternalItem source, InternalItem item, string enchantment,
            string reason, double time, int depth, int chainId)
        {
            string warning = item.Card.DisplayName() + "：" + reason + "（" + enchantment + "）";
            if (!ctx.Warnings.Contains(warning)) ctx.Warnings.Add(warning);
            Emit(ctx, source, item, "enchantBlocked", 0, enchantment + "：" + reason, time, depth, chainId);
        }

        private static void Emit(BattleContext ctx, InternalItem source, InternalItem item, string kind,
            double value, string note, double time, int depth, int chainId)
        {
            BattleEvent ev = new BattleEvent();
            ev.Time = time;
            ev.Side = source == null ? item.Side : source.Side;
            ev.SourceItemId = source == null ? item.Id : source.Id;
            ev.SourceCardId = source == null ? item.CardId : source.CardId;
            ev.TargetSide = item.Side;
            ev.TargetItemId = item.Id;
            ev.Kind = kind;
            ev.Value = value;
            ev.Note = note;
            ev.ChainId = chainId;
            ev.Depth = depth;
            ctx.Events.Add(Effects.FinishEvent(ctx, ev));
        }
    }
}
