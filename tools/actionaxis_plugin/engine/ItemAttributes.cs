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
            if (attribute == "Ammo") return item.Ammo;
            if (attribute == "AmmoMax" && item.AmmoMax.HasValue) return item.AmmoMax.Value;
            int amount;
            if (item.Card.LiveAttributes != null && item.Card.LiveAttributes.TryGetValue(attribute, out amount)) return amount;
            if (attribute == "CooldownMax") return item.CooldownMax > 0 ? item.CooldownMax * 1000 : item.Card.Cooldown * 1000;
            return 0;
        }

        public static double Get(InternalItem item, string attribute)
        {
            if (item == null || string.IsNullOrEmpty(attribute)) return 0;
            if (attribute == "Flying") return item.Flying ? 1 : 0;
            // 原生 TReferenceValueCardAttribute(CooldownMax) 读卡牌当前面板值（已含
            // 冷却缩减），而 AuraAttributes 里存的是反推基底。07f044ee 帧 139
            // Oblivion Vortex：ChargeAmount += 最左卡 CooldownMax(13580) × Custom_0
            // × 0.01 = 13580；若读基底 14000 会多 420。
            // 原生 TReferenceValueCardAttribute(CooldownMax) 读当前有效冷却：
            // 光环基底 + 运行时偏移，再按冷却缩减折算（不加回已并入的偏移）。
            if (attribute == "CooldownMax") return NativeDynamicAuras.EffectiveCooldownMaxMs(item);
            double aura;
            if (item.AuraAttributes.TryGetValue(attribute, out aura)) return aura;
            double value;
            item.AttributeDeltas.TryGetValue(attribute, out value);
            return Base(item, attribute) + value;
        }

        // 原生属性引用在属性表没有该键时返回 DefaultValue，而不是 0。
        public static bool Has(InternalItem item, string attribute)
        {
            if (item == null || item.Card == null || string.IsNullOrEmpty(attribute)) return false;
            if (attribute == "Flying" || attribute == "CooldownMax") return true;
            if (item.AuraAttributes.ContainsKey(attribute) || item.AttributeDeltas.ContainsKey(attribute)) return true;
            return item.Card.LiveAttributes != null && item.Card.LiveAttributes.ContainsKey(attribute);
        }

        public static double GetOrDefault(InternalItem item, string attribute, double fallback)
        {
            return Has(item, attribute) ? Get(item, attribute) : fallback;
        }

        // Unscaled 读取入场基底：优先战斗初始化记录的剥离光环基底，
        // 其次映射期快照；战斗内 AttributeDeltas 与 AuraAttributes 都不参与。
        public static double Unscaled(InternalItem item, string attribute, double fallback)
        {
            if (item == null || item.Card == null || string.IsNullOrEmpty(attribute)) return fallback;
            if (attribute == "Flying") return item.Flying ? 1 : 0;
            double baseValue;
            if (item.AuraBaseAttributes != null && item.AuraBaseAttributes.TryGetValue(attribute, out baseValue)) return baseValue;
            int seeded;
            if (item.Card.NativeAuraSeedAttributes != null && item.Card.NativeAuraSeedAttributes.TryGetValue(attribute, out seeded)) return seeded;
            if (item.Card.LiveAttributes != null && item.Card.LiveAttributes.TryGetValue(attribute, out seeded)) return seeded;
            if (attribute == "CooldownMax") return item.CooldownMax > 0 ? item.CooldownMax * 1000 : item.Card.Cooldown * 1000;
            return fallback;
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
                    double flatBefore = Get(item, "FlatCooldownReduction");
                    double percentBefore = Get(item, "PercentCooldownReduction");
                    AddDelta(item, timed.Attribute, -timed.Amount);
                    if (ctx.Config.NativeMechanics && item.AuraBaseAttributes == null &&
                        (timed.Attribute == "FlatCooldownReduction" || timed.Attribute == "PercentCooldownReduction"))
                        Effects.RebaseNativeCooldown(item, flatBefore, percentBefore);
                }
                item.TimedAttributes.RemoveAll(delegate(TimedAttribute timed) { return timed.Expired; });
            }
        }

        public static bool HasTag(InternalItem item, string tag)
        {
            if (item == null || string.IsNullOrEmpty(tag)) return false;
            if (item.ExtraTags.Contains(tag) || item.AuraTags.Contains(tag)) return true;
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
