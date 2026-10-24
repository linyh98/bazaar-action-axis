using System;
using System.Collections.Generic;

namespace BazaarActionAxis.Engine
{
    // Targeted corrections verified against the installed 5.0.0 native templates.
    // Live numbers (including permanent upgrades and multicasts) are retained.
    // This deliberately does not label the rest of the text-parsed catalog exact.
    public static class NativeItemMapper
    {
        public const string Firefly = "d4c0cf1e-7856-4e40-877f-c77b34f596ed";
        public const string Cog = "ca77281c-f912-4db3-86ce-64842a4ccdaa";
        public const string LauncherCore = "9fafc0a5-6675-4e17-9457-590f13354a39";
        public const string Spider = "014d9c98-e823-443c-98a3-6367ab81c956";
        public const string SolderingGun = "666f4084-48cc-4558-b678-a46ba18c1bf2";
        public const string Aludel = "d5a56d21-6892-4a08-b57a-1af53687ff37";
        public const string Drowsdrop = "a655fb50-b9e1-41ec-b658-07459014f96c";
        public const string Aviators = "edceffa1-9a9f-4ab3-b6a9-8a899b5726e3";

        public static List<string> Apply(BattleConfig config)
        {
            List<string> warnings = new List<string>();
            if (config == null || !config.NativeMechanics) return warnings;
            foreach (LoadoutItem item in config.Player.Items) Apply(config, item, warnings);
            if (!config.EnemyIsTrainingDummy)
                foreach (LoadoutItem item in config.Enemy.Items) Apply(config, item, warnings);
            return warnings;
        }

        private static void Apply(BattleConfig config, LoadoutItem item, IList<string> warnings)
        {
            CardDef original;
            if (!config.Cards.TryGetValue(item.CardId, out original)) return;
            string template = original.BaseItemId;
            if (template == null) template = original.Id.Split('@')[0].Split('#')[0];
            CardDef card = original.Clone();
            card.Effects = new List<Effect>(original.Effects);
            card.Triggers = new List<TriggerDef>(original.Triggers);
            card.Tags = new List<string>(original.Tags);
            double charge = Read(card, "ChargeAmount", 1000) / 1000.0;
            switch (template.ToLowerInvariant())
            {
                case Firefly:
                    card.Triggers.Clear();
                    card.Triggers.Add(Trigger("onHaste", Charge(charge), new TriggerFilter { SourceAdjacent = true }));
                    card.Triggers.Add(Trigger("onSlow", Charge(charge), new TriggerFilter { SourceAdjacent = true }));
                    break;
                case Cog:
                    ReplaceFirst(card, "haste", new Effect {
                        Kind = "haste", Seconds = Read(card, "HasteAmount", Tier(card, 1000, 2000, 3000, 4000)) / 1000.0,
                        Count = (int)Read(card, "HasteTargets", 1),
                        Target = new Target { Kind = "randomAdjacentSelf", HasCooldownOnly = true }
                    });
                    break;
                case LauncherCore:
                    // TTriggerOnCardFired + TActionCardFlyingStart means start flying
                    // on each use. It is not an at-combat-start trigger.
                    card.Effects.RemoveAll(delegate(Effect e) { return e.Kind == "fly"; });
                    ReplaceFirst(card, "charge", new Effect {
                        Kind = "charge", Seconds = charge,
                        Target = new Target { Kind = "allSelfItems", ExcludeSelf = true, FlyingOnly = true }
                    });
                    card.Effects.Insert(0, new Effect {
                        Kind = "fly", Count = (int)Read(card, "FlyingTargets", Tier(card, 2, 2, 3, 4)),
                        Target = new Target { Kind = "randomSelfItem", ExcludeSelf = true, NotFlyingOnly = true }
                    });
                    card.Triggers.Clear();
                    card.Triggers.Add(Trigger("onUse", Charge(charge), new TriggerFilter { SourceOther = true, SourceFlyingOrFriend = true }));
                    break;
                case Drowsdrop:
                    if (!card.Tags.Contains("Potion")) card.Tags.Add("Potion");
                    card.Triggers.Clear();
                    card.Triggers.Add(Trigger("onSlow", new Effect {
                        Kind = "reload", AmountValue = Read(card, "ReloadAmount", 99),
                        Count = 1, Target = new Target { Kind = "selfItem" }
                    }, new TriggerFilter { Self = true }));
                    // Its outgoing slow must only target items with a cooldown.
                    for (int i = 0; i < card.Effects.Count; i++)
                    {
                        if (card.Effects[i].Kind != "slow") continue;
                        Effect slow = card.Effects[i];
                        card.Effects[i] = new Effect { Kind = "slow", Seconds = slow.Seconds, Count = slow.Count,
                            Target = new Target { Kind = "randomEnemyItem", HasCooldownOnly = true } };
                        break;
                    }
                    break;
                case Aviators:
                    card.Triggers.Clear();
                    card.Triggers.Add(Trigger("onBurnApplied", Charge(charge), null));
                    card.Triggers.Add(Trigger("onDestroy", Charge(charge), null));
                    break;
                case Spider:
                case SolderingGun:
                case Aludel:
                    // Sale/day abilities are outside combat. Permanent auras such as
                    // multicast are already included in the live native attributes.
                    break;
                default:
                    if (original.DataSource == "native-template") return;
                    warnings.Add(original.DisplayName() + "：战斗联动尚未按原生模板校准，木桩推演可能存在偏差");
                    return;
            }
            // All combat abilities of these templates are represented here;
            // day/sale actions are deliberately outside the combat simulation.
            // Unsupported enchantments continue to report their own warnings.
            card.ParseStatus = "structured";
            CheckUnresolvedResistance(card, warnings);
            config.Cards[item.CardId] = card;
        }

        private static void CheckUnresolvedResistance(CardDef card, IList<string> warnings)
        {
            int flying, slow, freeze;
            bool isFlying = card.LiveAttributes.TryGetValue("Flying", out flying) && flying != 0;
            if (card.LiveAttributes.TryGetValue("PercentSlowReduction", out slow) &&
                !ResistanceExplained(slow, card.PercentSlowReduction, isFlying) ||
                card.LiveAttributes.TryGetValue("PercentFreezeReduction", out freeze) &&
                !ResistanceExplained(freeze, card.PercentFreezeReduction, isFlying))
                warnings.Add(card.DisplayName() + "：实时减速/冻结抗性无法区分永久加成与战斗临时效果，保留已知基础抗性，额外抗性未加入木桩推演");
        }

        private static bool ResistanceExplained(int live, double permanent, bool flying)
        {
            // All eight verified native templates have zero innate resistance.
            // A captured 50 on a flying card is the known derived flight value.
            // Other values cannot safely be normalized by subtracting 50.
            return live == permanent || flying && permanent == 0 && live == 50;
        }

        private static Effect Charge(double seconds)
        {
            return new Effect { Kind = "charge", Seconds = seconds, Count = 1, Target = new Target { Kind = "selfItem" } };
        }

        private static TriggerDef Trigger(string name, Effect effect, TriggerFilter filter)
        {
            TriggerDef trigger = new TriggerDef { Event = name, Filter = filter };
            trigger.Effects.Add(effect);
            return trigger;
        }

        private static void ReplaceFirst(CardDef card, string kind, Effect replacement)
        {
            for (int i = 0; i < card.Effects.Count; i++)
                if (card.Effects[i].Kind == kind) { card.Effects[i] = replacement; return; }
            card.Effects.Add(replacement);
        }

        private static double Read(CardDef card, string attribute, double fallback)
        {
            int value;
            return card.LiveAttributes != null && card.LiveAttributes.TryGetValue(attribute, out value) ? value : fallback;
        }

        private static double Tier(CardDef card, double bronze, double silver, double gold, double diamond)
        {
            // Native TCardItem.TryGetTierTemplate uses Diamond rules for Legendary.
            // Only the lookup changes; keep the instance's displayed Tier intact.
            return card.Tier == "Diamond" || card.Tier == "Legendary" ? diamond : card.Tier == "Gold" ? gold : card.Tier == "Silver" ? silver : bronze;
        }
    }
}
