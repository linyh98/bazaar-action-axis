using System.Collections.Generic;

namespace BazaarActionAxis.Engine
{
    // Live item attributes already include equipped skills' persistent auras.
    // Copy the catalog graph before replacing values so repeated recordings cannot
    // accumulate buffs or leak one instance's attributes into another instance.
    public static class LiveItemOverrides
    {
        public static CardDef Apply(CardDef source, Dictionary<string, int> attributes)
        {
            return Apply(source, attributes, null);
        }

        public static CardDef Apply(CardDef source, Dictionary<string, int> attributes, IList<string> warnings)
        {
            if (attributes == null) return source;
            HashSet<string> ambiguousAmounts = new HashSet<string>();
            HashSet<string> ambiguousControls = new HashSet<string>();
            Dictionary<string, double> amounts = new Dictionary<string, double>();
            Dictionary<string, double> seconds = new Dictionary<string, double>();
            Dictionary<string, int> counts = new Dictionary<string, int>();
            InspectEffects(source.Effects, amounts, seconds, counts, ambiguousAmounts, ambiguousControls);
            foreach (TriggerDef original in source.Triggers)
                InspectEffects(original.Effects, amounts, seconds, counts, ambiguousAmounts, ambiguousControls);
            if (warnings != null)
            {
                foreach (string kind in ambiguousAmounts)
                {
                    string attribute = AmountAttribute(kind);
                    if (attribute != null && attributes.ContainsKey(attribute))
                        warnings.Add(source.DisplayName() + "：" + kind + " 含多个不同基础数值，无法确定实时 " + attribute +
                            " 对应哪个效果；保留各效果原始数值，相关实时加成可能未计入");
                }
                foreach (string kind in ambiguousControls)
                {
                    string prefix = ControlPrefix(kind);
                    if (attributes.ContainsKey(prefix + "Amount") || attributes.ContainsKey(prefix + "Targets"))
                        warnings.Add(source.DisplayName() + "：" + kind + " 含不同持续时间或目标数量，无法确定实时属性对应哪个效果；" +
                            "保留各效果原始持续时间和目标数量，相关实时加成可能未计入");
                }
            }
            CardDef card = source.Clone();
            card.Effects = CopyEffects(source.Effects, attributes, ambiguousAmounts, ambiguousControls);
            card.Triggers = new List<TriggerDef>();
            foreach (TriggerDef original in source.Triggers)
            {
                TriggerDef trigger = new TriggerDef();
                trigger.Event = original.Event;
                trigger.EverySeconds = original.EverySeconds;
                trigger.Filter = original.Filter;
                trigger.SkillRule = original.SkillRule;
                trigger.Effects = CopyEffects(original.Effects, attributes, ambiguousAmounts, ambiguousControls);
                card.Triggers.Add(trigger);
            }
            ReplaceStatic(card.Effects, attributes, "CritChance", "crit", 100.0);
            ReplaceStatic(card.Effects, attributes, "Lifesteal", "lifesteal", 100.0);
            if (!attributes.ContainsKey("Lifesteal"))
                ReplaceStatic(card.Effects, attributes, "LifestealPercentage", "lifesteal", 100.0);
            return card;
        }

        private static void InspectEffects(List<Effect> effects, Dictionary<string, double> amounts,
            Dictionary<string, double> seconds, Dictionary<string, int> counts,
            HashSet<string> ambiguousAmounts, HashSet<string> ambiguousControls)
        {
            foreach (Effect effect in effects)
            {
                double prior;
                if (AmountAttribute(effect.Kind) != null && effect.Amount != null && effect.Amount.Type == "const")
                {
                    if (amounts.TryGetValue(effect.Kind, out prior) && prior != effect.Amount.Value) ambiguousAmounts.Add(effect.Kind);
                    else amounts[effect.Kind] = effect.Amount.Value;
                }
                if (ControlPrefix(effect.Kind) == null) continue;
                int count = effect.Count.HasValue ? effect.Count.Value : 1;
                int priorCount;
                if (seconds.TryGetValue(effect.Kind, out prior) && prior != effect.Seconds ||
                    counts.TryGetValue(effect.Kind, out priorCount) && priorCount != count) ambiguousControls.Add(effect.Kind);
                else
                {
                    seconds[effect.Kind] = effect.Seconds;
                    counts[effect.Kind] = count;
                }
            }
        }

        private static List<Effect> CopyEffects(List<Effect> originals, Dictionary<string, int> attributes,
            HashSet<string> ambiguousAmounts, HashSet<string> ambiguousControls)
        {
            List<Effect> copies = new List<Effect>();
            foreach (Effect original in originals)
            {
                Effect effect = new Effect();
                effect.Kind = original.Kind;
                effect.AmountValue = original.AmountValue;
                effect.Target = original.Target;
                effect.Seconds = original.Seconds;
                effect.Count = original.Count;
                effect.Chance = original.Chance;
                effect.Percent = original.Percent;
                effect.Crit = original.Crit;
                if (original.Amount != null)
                {
                    effect.Amount = new NumExpr();
                    effect.Amount.Type = original.Amount.Type;
                    effect.Amount.Value = original.Amount.Value;
                    effect.Amount.Basis = original.Amount.Basis;
                    effect.Amount.Stack = original.Amount.Stack;
                    effect.Amount.Per = original.Amount.Per;
                    effect.Amount.Tag = original.Amount.Tag;
                    int amount;
                    string key = AmountAttribute(effect.Kind);
                    if (effect.Amount.Type == "const" && key != null && !ambiguousAmounts.Contains(effect.Kind) && attributes.TryGetValue(key, out amount))
                        effect.Amount.Value = amount;
                }
                string prefix = ControlPrefix(effect.Kind);
                string duration = prefix == null ? null : prefix + "Amount";
                string targets = prefix == null ? null : prefix + "Targets";
                int value;
                if (!ambiguousControls.Contains(effect.Kind))
                {
                    if (duration != null && attributes.TryGetValue(duration, out value)) effect.Seconds = value / 1000.0;
                    if (targets != null && attributes.TryGetValue(targets, out value) && value > 0) effect.Count = value;
                }
                copies.Add(effect);
            }
            return copies;
        }

        private static string ControlPrefix(string kind)
        {
            switch (kind)
            {
                case "haste": return "Haste";
                case "slow": return "Slow";
                case "freeze": return "Freeze";
                case "charge": return "Charge";
                default: return null;
            }
        }

        private static string AmountAttribute(string kind)
        {
            switch (kind)
            {
                case "damage": return "DamageAmount";
                case "heal": return "HealAmount";
                case "shield": return "ShieldApplyAmount";
                case "burn": return "BurnApplyAmount";
                case "poison": return "PoisonApplyAmount";
                case "regen": return "RegenApplyAmount";
                default: return null;
            }
        }

        private static void ReplaceStatic(List<Effect> effects, Dictionary<string, int> attributes,
            string attribute, string kind, double divisor)
        {
            int value;
            if (!attributes.TryGetValue(attribute, out value)) return;
            effects.RemoveAll(delegate(Effect effect) { return effect.Kind == kind; });
            if (value == 0) return;
            Effect live = new Effect();
            live.Kind = kind;
            if (kind == "crit") live.Chance = value / divisor;
            else live.Percent = value / divisor;
            effects.Add(live);
        }
    }
}
