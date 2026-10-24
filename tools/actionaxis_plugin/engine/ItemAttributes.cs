using System;
using System.Collections.Generic;

namespace BazaarActionAxis.Engine
{
    // 战斗内属性 = 模板实时属性 + 运行时偏移（含限时偏移）。
    // 映射期只写 CardDef.LiveAttributes；战斗期写 InternalItem.AttributeDeltas。
    public static class ItemAttributes
    {
        public static double Base(InternalItem item, string attribute)
        {
            if (item == null || item.Card == null || string.IsNullOrEmpty(attribute)) return 0;
            if (attribute == "Flying") return item.Flying ? 1 : 0;
            int amount;
            if (item.Card.LiveAttributes != null && item.Card.LiveAttributes.TryGetValue(attribute, out amount)) return amount;
            if (attribute == "CooldownMax") return item.CooldownMax > 0 ? item.CooldownMax * 1000 : item.Card.Cooldown * 1000;
            return 0;
        }

        public static double Get(InternalItem item, string attribute)
        {
            if (item == null || string.IsNullOrEmpty(attribute)) return 0;
            if (attribute == "Flying") return item.Flying ? 1 : 0;
            double aura;
            if (item.AuraAttributes.TryGetValue(attribute, out aura)) return aura;
            double value;
            item.AttributeDeltas.TryGetValue(attribute, out value);
            return Base(item, attribute) + value;
        }

        public static void AddDelta(InternalItem item, string attribute, double amount)
        {
            if (item == null || string.IsNullOrEmpty(attribute) || Math.Abs(amount) < 1e-9) return;
            double current;
            item.AttributeDeltas.TryGetValue(attribute, out current);
            item.AttributeDeltas[attribute] = current + amount;
        }

        public static void AddTimed(InternalItem item, string attribute, double amount, double expiresAt)
        {
            if (item == null || string.IsNullOrEmpty(attribute) || Math.Abs(amount) < 1e-9) return;
            AddDelta(item, attribute, amount);
            TimedAttribute timed = new TimedAttribute();
            timed.Attribute = attribute;
            timed.Amount = amount;
            timed.ExpiresAt = expiresAt;
            item.TimedAttributes.Add(timed);
        }

        // 每帧调用：把到期偏移从属性中扣回。限时效果不会永久污染战斗属性。
        public static void Expire(BattleContext ctx, double time)
        {
            foreach (InternalItem item in ctx.Items)
            {
                if (item.TimedAttributes.Count == 0) continue;
                foreach (TimedAttribute timed in item.TimedAttributes)
                {
                    if (timed.Expired || time < timed.ExpiresAt - 1e-9) continue;
                    timed.Expired = true;
                    AddDelta(item, timed.Attribute, -timed.Amount);
                }
                item.TimedAttributes.RemoveAll(delegate(TimedAttribute timed) { return timed.Expired; });
            }
        }

        public static bool HasTag(InternalItem item, string tag)
        {
            if (item == null || string.IsNullOrEmpty(tag)) return false;
            if (item.ExtraTags.Contains(tag)) return true;
            return item.Card != null && item.Card.Tags.Contains(tag);
        }

        public static void AddTag(InternalItem item, string tag)
        {
            if (item == null || string.IsNullOrEmpty(tag) || HasTag(item, tag)) return;
            item.ExtraTags.Add(tag);
        }

        // 升序：Small < Medium < Large。未知尺寸不参与排行。
        public static int SizeRank(string size)
        {
            return size == "Small" ? 0 : size == "Medium" ? 1 : size == "Large" ? 2 : -1;
        }
    }
}
