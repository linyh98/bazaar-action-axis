using System;
using System.Collections.Generic;

namespace BazaarActionAxis.Engine
{
    public enum Side
    {
        Player,
        Enemy
    }

    public static class SideUtil
    {
        public static Side Opposing(Side side)
        {
            return side == Side.Player ? Side.Enemy : Side.Player;
        }

        public static string Key(Side side)
        {
            return side == Side.Player ? "player" : "enemy";
        }

        public static Side Parse(string value)
        {
            return string.Equals(value, "enemy", StringComparison.OrdinalIgnoreCase)
                ? Side.Enemy
                : Side.Player;
        }
    }

    public sealed class NumExpr
    {
        public string Type = "const";
        public double Value;
        public string Basis;
        public string Stack;
        public double Per;
        public string Tag;
    }

    public sealed class Target
    {
        public string Kind = "selfHero";
        public string ItemId;
        public JsonValue NativeCondition;
        public bool ExcludeSelf;
        public bool FlyingOnly;
        public bool NotFlyingOnly;
        public bool HasCooldownOnly;
        public bool AdjacentOnly;
    }

    public sealed class Effect
    {
        public string Kind;
        public NumExpr Amount;
        public double AmountValue;
        public Target Target;
        public double Seconds;
        public int? Count;
        public double Chance;
        public double Percent;
        public bool Crit;
    }

    public sealed class TriggerFilter
    {
        public string SourceTag;
        public string SourceItemId;
        public Side? TargetSide;
        public bool Self;
        public bool SourceOther;
        public bool SourceAdjacent;
        public bool SourceFlyingOrFriend;
    }

    public sealed class TriggerDef
    {
        public string Event;
        public TriggerFilter Filter;
        public double? EverySeconds;
        public List<Effect> Effects = new List<Effect>();
        public NativeSkillRule SkillRule;
    }

    public sealed class CardDef
    {
        public string Id;
        public string BaseItemId;
        public string Name;
        public string NameCn;
        public string Image;
        public string Size = "Small";
        public string Tier = "Bronze";
        public double Cooldown;
        public List<Effect> Effects = new List<Effect>();
        public List<TriggerDef> Triggers = new List<TriggerDef>();
        public List<string> Tags = new List<string>();
        public int? AmmoMax;
        public bool StartReady;
        public bool Passive;
        public string ParseStatus = "text";
        public List<string> RawTooltips = new List<string>();
        public string DataSource;
        public int MulticastBonus;
        public bool HasMulticastOverride;
        public double PercentSlowReduction;
        public double PercentFreezeReduction;
        public double PercentChargeReduction;
        public Dictionary<string, int> LiveAttributes = new Dictionary<string, int>();
        public JsonValue NativeTemplate;
        public JsonValue NativeEnchantment;
        public bool NativeAttributesAreLive = true;
        public bool NativeAurasApplied;

        public string DisplayName()
        {
            if (!string.IsNullOrEmpty(NameCn)) return NameCn;
            if (!string.IsNullOrEmpty(Name)) return Name;
            return Id;
        }

        // cooldown in seconds; ammoMax = live AmmoMax; multicastBonus = live Multicast - 1.
        public CardDef WithLiveOverrides(double? cooldown, int? ammoMax, int? multicastBonus)
        {
            CardDef copy = Clone();
            if (cooldown.HasValue) copy.Cooldown = cooldown.Value;
            if (ammoMax.HasValue && ammoMax.Value > 0) copy.AmmoMax = ammoMax.Value;
            if (multicastBonus.HasValue)
            {
                copy.MulticastBonus = multicastBonus.Value;
                copy.HasMulticastOverride = true;
            }
            return copy;
        }

        public CardDef Clone()
        {
            CardDef copy = new CardDef();
            copy.Id = Id;
            copy.BaseItemId = BaseItemId;
            copy.Name = Name;
            copy.NameCn = NameCn;
            copy.Image = Image;
            copy.Size = Size;
            copy.Tier = Tier;
            copy.Cooldown = Cooldown;
            copy.Effects = Effects;
            copy.Triggers = Triggers;
            copy.Tags = Tags;
            copy.AmmoMax = AmmoMax;
            copy.StartReady = StartReady;
            copy.Passive = Passive;
            copy.ParseStatus = ParseStatus;
            copy.RawTooltips = RawTooltips;
            copy.DataSource = DataSource;
            copy.MulticastBonus = MulticastBonus;
            copy.HasMulticastOverride = HasMulticastOverride;
            copy.PercentSlowReduction = PercentSlowReduction;
            copy.PercentFreezeReduction = PercentFreezeReduction;
            copy.PercentChargeReduction = PercentChargeReduction;
            copy.LiveAttributes = new Dictionary<string, int>(LiveAttributes);
            copy.NativeTemplate = NativeTemplate;
            copy.NativeEnchantment = NativeEnchantment;
            copy.NativeAttributesAreLive = NativeAttributesAreLive;
            copy.NativeAurasApplied = NativeAurasApplied;
            return copy;
        }
    }

    public sealed class LoadoutItem
    {
        public string CardId;
        public int Slot;
    }

    public sealed class Loadout
    {
        public List<LoadoutItem> Items = new List<LoadoutItem>();
    }

    public sealed class BattleEvent
    {
        public int Id;
        public double Time;
        public Side Side;
        public string SourceItemId;
        public string SourceCardId;
        public string Kind;
        public Side? TargetSide;
        public string TargetItemId;
        public double? Value;
        // Damage that actually hit HP or shield, after reduction and overkill.
        public double? ActualDamage;
        public double? Absorbed;
        public double? HpAfter;
        public double? ShieldAfter;
        public int ChainId;
        public int Depth;
        public string Note;
    }

    public sealed class BattleItemView
    {
        public string Id;
        public string CardId;
        public int Slot;
        public Side Side;
        public CardDef Card;
    }

    // Captured after every simulation tick, including all activations and effects.
    public sealed class ItemSnapshot
    {
        public double Time;
        public string ItemId;
        public double CooldownRemaining;
        public double CooldownMax;
        public double CooldownRate;
        public bool HasCooldown;
        public bool Activated;
        public int Ammo;
        public int? AmmoMax;
    }

    public sealed class EffectStat
    {
        public double Time;
        public string SourceItemId;
        public string SourceCardId;
        public string Kind;
        public double Value;
        public int TargetCount;
    }

    public sealed class TotalsSide
    {
        public double Damage;
        public double EffectiveDamage;
        public double Heal;
        public double Shield;
    }

    public sealed class Totals
    {
        public TotalsSide Player = new TotalsSide();
        public TotalsSide Enemy = new TotalsSide();

        public TotalsSide For(Side side)
        {
            return side == Side.Player ? Player : Enemy;
        }
    }

    public sealed class BattleResult
    {
        public List<BattleEvent> Events = new List<BattleEvent>();
        public List<EffectStat> EffectStats = new List<EffectStat>();
        public List<ItemSnapshot> ItemSnapshots = new List<ItemSnapshot>();
        public List<BattleItemView> PlayerItems = new List<BattleItemView>();
        public List<BattleItemView> EnemyItems = new List<BattleItemView>();
        public double? EnemyStartHp;
        public double EnemyStartShield;
        public Totals Totals = new Totals();
        public string Winner = "draw";
        public double EndTime;
        public List<string> Warnings = new List<string>();
    }

    public sealed class RuleOverrides
    {
        public double? Dt;
        public double? MaxTime;
        public double? HasteMultiplier;
        public double? SlowMultiplier;
        public bool? StartReady;
        public double? BurnDecay;
        public double? PoisonDecay;
        public bool? DotBypassesShield;
        public double? CritMultiplier;
        public bool? LifestealOnShieldDamage;
        public string ActivationOrder;
        public int? MaxEventsPerTick;
        public int? MaxTriggerDepth;
        public int? MaxSlots;
    }

    public sealed class ResolvedRules
    {
        public double Dt = 0.1;
        public double MaxTime = 60;
        public double HasteMultiplier = 2;
        public double SlowMultiplier = 0.5;
        public bool StartReady;
        public double BurnDecay = 1;
        public double PoisonDecay;
        public bool DotBypassesShield;
        public double CritMultiplier = 2;
        public bool LifestealOnShieldDamage;
        public string ActivationOrder = "player-first-left-to-right";
        public int MaxEventsPerTick = 500;
        public int MaxTriggerDepth = 20;
        public int MaxSlots = 10;
    }

    public sealed class BattleConfig
    {
        public Dictionary<string, CardDef> Cards = new Dictionary<string, CardDef>();
        public Loadout Player = new Loadout();
        public Loadout Enemy = new Loadout();
        public List<CardDef> PlayerSkills = new List<CardDef>();
        public List<CardDef> EnemySkills = new List<CardDef>();
        public double PlayerMaxHp = 100;
        public double EnemyMaxHp = 100;
        public double? PlayerStartHp;
        public double? EnemyStartHp;
        public double EnemyStartShield;
        public double EnemyStartRegen;
        // Random enemy cards remain valid targets and retain their board tags,
        // but a training dummy never activates cards or their passive triggers.
        public bool EnemyIsTrainingDummy;
        // Keep historical fixture semantics unless the live plugin opts into current game rules.
        public bool NativeMechanics;
        public double Seed = 1;
        public RuleOverrides Rules = new RuleOverrides();
        public double MaxTime = 60;
    }
}
