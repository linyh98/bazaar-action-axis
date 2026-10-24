using System;
using System.Collections.Generic;

namespace BazaarActionAxis.Engine
{
    // 战斗内“变成场上已有道具”的变形（打印机/镜子/病毒等）。
    // 场外随机卡池变形不在支持范围；规则未校准时由映射层 fail-closed。
    public static class NativeTransform
    {
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
                // 变成左侧中型、非传说物品的副本。
                case "Mirror": return "leftMedium";
                // 变成自己拥有的另一张小型、非传说物品的副本。
                case "Hologram Projector":
                case "Quicksilver": return "otherSmall";
                // 把目标变成 Virus 本身。
                case "Virus": return "self";
                default: return null;
            }
        }

        public static InternalItem FindDonor(BattleContext ctx, InternalItem source, InternalItem target, string rule)
        {
            if (rule == "self") return source;
            List<InternalItem> candidates = new List<InternalItem>();
            foreach (InternalItem item in ctx.Items)
            {
                if (item.Side != source.Side || item.Destroyed || item.Id == target.Id) continue;
                if (item.Card == null) continue;
                if (rule == "leftSmall" || rule == "leftMedium")
                {
                    if (item.Slot >= target.Slot) continue;
                    if (rule == "leftSmall" && item.Card.Size != "Small") continue;
                    if (rule == "leftMedium" && item.Card.Size != "Medium") continue;
                    if (item.Card.Tier == "Legendary") continue;
                    candidates.Add(item);
                }
                else if (rule == "otherSmall")
                {
                    if (item.Card.Size != "Small" || item.Card.Tier == "Legendary") continue;
                    candidates.Add(item);
                }
            }
            if (candidates.Count == 0) return null;
            if (rule == "leftSmall" || rule == "leftMedium")
            {
                InternalItem best = candidates[0];
                foreach (InternalItem item in candidates) if (item.Slot > best.Slot) best = item;
                return best;
            }
            return candidates[ctx.Rng.Int(candidates.Count)];
        }

        // 第一份原位替换目标，其余副本按槽位插到目标之后并排入待加入队列。
        public static bool Apply(BattleContext ctx, InternalItem source, InternalItem target, InternalItem donor,
            int copies, double time, int depth, int? chainId)
        {
            if (target == null || donor == null || donor.Card == null || target.Card == null) return false;
            if (copies < 1) copies = 1;
            int startSlot = target.Slot;
            if (copies > 1)
                foreach (InternalItem item in ctx.Items)
                    if (item.Side == target.Side && item.Slot > startSlot) item.Slot += copies - 1;
            Adopt(ctx, source, target, donor, startSlot, time, depth, chainId);
            for (int index = 1; index < copies; index++)
            {
                InternalItem clone = new InternalItem();
                clone.Id = "transform:" + (ctx.NextEventId++) + ":" + donor.CardId;
                clone.CardId = donor.CardId;
                clone.Side = target.Side;
                Adopt(ctx, source, clone, donor, startSlot + index, time, depth, chainId);
                ctx.PendingItems.Add(clone);
            }
            return true;
        }

        private static void Adopt(BattleContext ctx, InternalItem source, InternalItem item, InternalItem donor,
            int slot, double time, int depth, int? chainId)
        {
            CardDef card = donor.Card;
            item.Card = card;
            item.CardId = donor.CardId;
            item.Slot = slot;
            item.Cooldown = card.Cooldown;
            item.CooldownMax = card.Cooldown;
            item.AmmoMax = card.AmmoMax;
            item.Ammo = card.AmmoMax ?? 0;
            item.MulticastBonus = StaticMulticast(card);
            item.CritBonus = StaticCrit(card);
            item.Lifesteal = StaticLifesteal(card);
            item.DamageBonus = 0;
            item.Disabled = false;
            item.FrozenUntil = -1;
            item.HasteUntil = -1;
            item.SlowUntil = -1;
            item.ExtraTags.Clear();
            item.TimedAttributes.Clear();
            item.AttributeDeltas.Clear();
            item.NativeTriggerCounts.Clear();
            item.AuraBaseAttributes = new Dictionary<string, double>();
            foreach (KeyValuePair<string, int> pair in card.LiveAttributes) item.AuraBaseAttributes[pair.Key] = pair.Value;
            item.AuraAttributes.Clear();
            item.AuraCooldownFlat = 0;
            item.AuraCooldownPercent = 0;

            BattleEvent ev = new BattleEvent();
            ev.Time = time;
            ev.Side = source.Side;
            ev.SourceItemId = source.Id;
            ev.SourceCardId = source.CardId;
            ev.TargetSide = item.Side;
            ev.TargetItemId = item.Id;
            ev.Kind = "transform";
            ev.Value = 1;
            ev.Note = card.DisplayName();
            ev.ChainId = chainId.HasValue ? chainId.Value : ctx.NextChainId++;
            ev.Depth = depth;
            ctx.Events.Add(Effects.FinishEvent(ctx, ev));

            // 变形后的卡是事件主体；观察者按原生下一帧排空。
            TriggerPayload payload = new TriggerPayload();
            payload.Event = "onCardTransformed";
            payload.SourceSide = item.Side;
            payload.SourceItemId = item.Id;
            payload.TargetSide = item.Side;
            payload.TargetItemId = item.Id;
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
