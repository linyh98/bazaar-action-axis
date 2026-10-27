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
        // 运行时引用：映射期只记录结构，结算时按当时属性求值。
        public string Attribute;
        public string TargetKind;
        public string Side;
        public JsonValue Condition;
        public bool Distinct;
        public bool Exclude;
        public Target CardTarget;
        // 原生 TReferenceValue 的缺省值；属性不存在时回退，不把缺省当成 0。
        public double DefaultValue;
        // TReferenceValueCardAttributeUnscaled 读取入场基底，忽略战斗内增量与派生光环。
        public bool Unscaled;
        public double Min;
        public double Max;
        public NumExpr Modifier;
        public string ModifyMode;
        public bool ShouldRound;
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
        public string PositionMode;
        public string PositionOrigin;
        public bool IncludeOrigin;
    }

    public sealed class Effect
    {
        public string Kind;
        public string NativeAbilityId;
        // 原生 Ability.Priority：同一张卡主动效果按 Immediate/Highest/High/Medium/Low/Lowest 结算。
        public string Priority;
        public NumExpr Amount;
        public double AmountValue;
        public Target Target;
        public double Seconds;
        public int? Count;
        // 原生时长单位为毫秒，结算时再读取成长后的数值。
        public NumExpr DurationValue;
        public NumExpr CountValue;
        public double Chance;
        public double Percent;
        public bool Crit;
        // TActionAnd 展开后的子动作；其余效果为空。
        public List<Effect> Effects;
        // 运行时改属性 / 加标签使用。
        public string Attribute;
        public string Operation;
        public List<string> Tags;
        // TActionCardAddTagsBySource 的标签来源。
        public Target SourceTarget;
        // 主动能力自带的前提（例如计数器达到 N 才能出手）。
        public JsonValue Prerequisites;
        // 行动代价（TActionCostPlayerAttribute）：目前只取证了花费人物 Tempo
        // （Tuba，0276c46b 帧 82）。代价不足时整个能力不执行。
        public string CostAttribute;
        public NumExpr CostAmount;
        // 附魔新增的效果；校准器重建主动效果时必须保留。
        public bool FromEnchantment;
        public bool PreventOverride;
        // TActionCardEnchantRandom 的加权附魔池。
        public List<string> EnchantmentNames;
        public List<double> EnchantmentWeights;
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
        // TTriggerOnCardAttributeChanged / PlayerAttributeChanged 的属性与增减方向。
        public string AttributeFilter;
        public string ChangeType;
        // 原生 EffectPriority；同一张卡的多个触发按 Immediate→Lowest 顺序结算。
        public string Priority;
        // 附魔新增的被动；校准器重建基础触发器时必须保留。
        public bool FromEnchantment;
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
        // 原生附魔枚举名；未附魔为 null。用于 TCardConditionalHasEnchantment。
        public string EnchantmentName;
        public double Cooldown;
        public List<Effect> Effects = new List<Effect>();
        public List<TriggerDef> Triggers = new List<TriggerDef>();
        public List<string> Tags = new List<string>();
        // 原生 HiddenTags：用于 TCardConditionalHiddenTag。
        public List<string> HiddenTags = new List<string>();
        // 原生 Heroes：卡牌所属英雄（Common 表示全英雄通用）。
        public List<string> Heroes = new List<string>();
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
        public double PercentHasteReduction;
        public double PercentChargeReduction;
        // 原生 DestroyImmunity；只来自明确的模板/附魔定义，不读实时快照。
        public int DestroyImmunity;
        // 原生模板中确实缺失的 Ability/Aura 引用：跳过但保留可见告警，
        // 不把它当成整卡 unsupported，也不猜缺失数值。
        public List<string> MappingWarnings = new List<string>();
        public Dictionary<string, int> LiveAttributes = new Dictionary<string, int>();
        public JsonValue NativeTemplate;
        public JsonValue NativeEnchantment;
        public bool NativeAttributesAreLive = true;
        public bool NativeAurasApplied;
        // Card attribute auras are evaluated again when combat state changes.
        public List<JsonValue> NativeRuntimeAuras = new List<JsonValue>();
        // WorksIn=CombatOnly 的 Add 光环（及引用其写入属性的同源 Add 光环）在
        // 来源每次使用时重执行并永久叠加，不进入连续派生层。
        public List<JsonValue> NativeUseAuras = new List<JsonValue>();
        // Before native card auras: pure templates contain raw stats; live boards
        // contain already evaluated stats, normalized once by NativeDynamicAuras.
        public Dictionary<string, int> NativeAuraSeedAttributes;

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
            copy.EnchantmentName = EnchantmentName;
            copy.Cooldown = Cooldown;
            copy.Effects = Effects;
            copy.Triggers = Triggers;
            copy.Tags = Tags;
            copy.HiddenTags = HiddenTags;
            copy.Heroes = Heroes;
            copy.AmmoMax = AmmoMax;
            copy.StartReady = StartReady;
            copy.Passive = Passive;
            copy.ParseStatus = ParseStatus;
            copy.RawTooltips = RawTooltips;
            copy.MappingWarnings = new List<string>(MappingWarnings);
            copy.DataSource = DataSource;
            copy.MulticastBonus = MulticastBonus;
            copy.HasMulticastOverride = HasMulticastOverride;
            copy.PercentSlowReduction = PercentSlowReduction;
            copy.PercentFreezeReduction = PercentFreezeReduction;
            copy.PercentHasteReduction = PercentHasteReduction;
            copy.PercentChargeReduction = PercentChargeReduction;
            copy.DestroyImmunity = DestroyImmunity;
            copy.LiveAttributes = new Dictionary<string, int>(LiveAttributes);
            copy.NativeTemplate = NativeTemplate;
            copy.NativeEnchantment = NativeEnchantment;
            copy.NativeAttributesAreLive = NativeAttributesAreLive;
            copy.NativeAurasApplied = NativeAurasApplied;
            copy.NativeRuntimeAuras = new List<JsonValue>(NativeRuntimeAuras);
            copy.NativeUseAuras = new List<JsonValue>(NativeUseAuras);
            copy.NativeAuraSeedAttributes = NativeAuraSeedAttributes == null ? null : new Dictionary<string, int>(NativeAuraSeedAttributes);
            return copy;
        }
    }

    public sealed class LoadoutItem
    {
        public string CardId;
        public int Slot;
        // Opening cooldown remaining in seconds from the native snapshot.
        // Null means the catalog's full cooldown.
        public double? StartCooldown;
    }

    public sealed class Loadout
    {
        public List<LoadoutItem> Items = new List<LoadoutItem>();
    }

    // One native DoT tick can belong to several sources (for example two Burn
    // items). Shares keep the measured pool movement attributable without
    // emitting one HP change per source.
    public sealed class BattleEventShare
    {
        public Side Side;
        public string SourceItemId;
        public string SourceCardId;
        // Nominal contribution before shield/overkill.
        public double Amount;
        // Realized output: HP loss plus Shield consumed. Excludes overkill.
        public double ActualAmount;
        public double Absorbed;
        public double Overkill;
        // Damage negated by the native Burn/Shield rule.
        public double Prevented;
        // For heal events: nominal minus accepted healing.
        public double Overheal;
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
        // HP damage beyond the remaining health. Not output.
        public double? Overkill;
        // Damage negated by shield rules but not converted into Shield loss.
        public double? Prevented;
        // Heal/Regen request before the Max-HP cap, and the discarded part.
        public double? NominalAmount;
        public double? Overheal;
        public double? HpAfter;
        public double? ShieldAfter;
        public List<BattleEventShare> Shares;
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
        public double BornAt;
        public double? RemovedAt;
        public List<BattleItemState> States = new List<BattleItemState>();

        public BattleItemState At(double time)
        {
            if (double.IsNaN(time) || time < BornAt || RemovedAt.HasValue && time >= RemovedAt.Value) return null;
            for (int i = States.Count - 1; i >= 0; i--)
                if (States[i].Time <= time + 1e-9) return States[i];
            return null;
        }
    }

    public sealed class BattleItemState
    {
        public double Time;
        public CardDef Card;
        public int Slot;
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
        // Live item attributes at this tick.  The native ledger reports every
        // attribute change (Haste/Slow/Freeze, DamageAmount, CritChance, ...),
        // so the simulated ledger must expose the same surface.
        public Dictionary<string, double> Attributes = new Dictionary<string, double>();
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
        public double ShieldAbsorbed;
        public double Overkill;
        public double Prevented;
        public double Heal;
        public double NominalHeal;
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

    public sealed class HeroVitals
    {
        public double Hp;
        public double MaxHp;
        public double Shield;
        public double Burn;
        public double Poison;
        public double Regen;

        public HeroVitals Copy() { return (HeroVitals)MemberwiseClone(); }
    }

    public sealed class BattleHealthSnapshot
    {
        public double Time;
        public HeroVitals Player;
        public HeroVitals Enemy;

        public BattleHealthSnapshot Copy()
        {
            return new BattleHealthSnapshot { Time = Time, Player = Player.Copy(), Enemy = Enemy.Copy() };
        }
    }

    public sealed class BattleResult
    {
        public ReplayRandomAudit ReplayRandom;
        public List<BattleEvent> Events = new List<BattleEvent>();
        public List<EffectStat> EffectStats = new List<EffectStat>();
        public List<ItemSnapshot> ItemSnapshots = new List<ItemSnapshot>();
        public Dictionary<string, Dictionary<string, double>> OpeningItemAttributes = new Dictionary<string, Dictionary<string, double>>();
        public List<BattleHealthSnapshot> HealthSnapshots = new List<BattleHealthSnapshot>();
        public bool EndedByDeath;
        public List<BattleItemView> PlayerItems = new List<BattleItemView>();
        public List<BattleItemView> EnemyItems = new List<BattleItemView>();
        public double? EnemyStartHp;
        public double EnemyStartShield;
        public double? PlayerStartHp;
        public double PlayerStartShield;
        public Totals Totals = new Totals();
        public string Winner = "draw";
        public double EndTime;
        public bool SandstormStarted;
        public double SandstormStartedAt = -1;
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
        public ReplayRandomTape ReplayRandom;
        public Dictionary<string, CardDef> Cards = new Dictionary<string, CardDef>();
        // TransformDestroyed 的供体候选（Jerky／小型 Drone 池）。不从当前棋盘
        // 猜候选：测试与插件必须显式提供，缺失时整次变形告警并保持原卡。
        public List<CardDef> SpawnPool = new List<CardDef>();
        // 固定模板变形供体（Blender → Slushee 等）的原始模板。插件从游戏
        // Data.GetCardById 读取；测试可直接注入，避免把本地文本卡当成原生规则。
        public Dictionary<string, JsonValue> NativeTemplates = new Dictionary<string, JsonValue>(StringComparer.OrdinalIgnoreCase);
        public Loadout Player = new Loadout();
        public Loadout Enemy = new Loadout();
        public List<CardDef> PlayerSkills = new List<CardDef>();
        public List<CardDef> EnemySkills = new List<CardDef>();
        public double PlayerMaxHp = 100;
        public double EnemyMaxHp = 100;
        public double? PlayerStartHp;
        public double? EnemyStartHp;
        public double EnemyStartShield;
        public double PlayerStartShield;
        public double EnemyStartRegen;
        public double PlayerStartRegen;
        // 原生怪物模板的 Player.Attributes 默认值：RageMax 100，暴怒 5000 ms。
        public double PlayerRageMax = 100;
        public double EnemyRageMax = 100;
        public double PlayerEnragedDurationMs = 5000;
        public double EnemyEnragedDurationMs = 5000;
        // 原生人物属性快照（Custom_N/Level/CritChance 等）供光环与条件引用。
        // HP/Shield/Burn/Poison 等战斗状态仍以 InternalHero 运行时字段为准。
        public Dictionary<string, int> PlayerAttributes = new Dictionary<string, int>();
        public Dictionary<string, int> EnemyAttributes = new Dictionary<string, int>();
        public bool PlayerHeroAttributesAreLive;
        public bool EnemyHeroAttributesAreLive;
        // Blank enemy cards remain valid size/cooldown targets and publish use
        // events, but cannot deal damage or run their own effects or passives.
        public bool EnemyIsTrainingDummy;
        // Keep historical fixture semantics unless the live plugin opts into current game rules.
        public bool NativeMechanics;
        // 玩家/对手的英雄身份（原生 EHero 枚举名，如 Vanessa）。
        // 未读取到时保留 null，TCardConditionalPlayerHero 按不匹配处理并警告。
        public string PlayerHero;
        public string EnemyHero;
        public double Seed = 1;
        public RuleOverrides Rules = new RuleOverrides();
        public double MaxTime = 60;
        // 原生战斗默认开启沙暴。数值来自 2026-10-25 的 200 份 CombatReplays：
        // 54 份出现倒计时（20.0s），23 份出现伤害；23/23 的 tick 间隔与伤害
        // 序列吻合（5 帧 x5、4 帧 x5、3 帧起，前 10 tick 1 伤，之后每 tick +2）。
        public bool SandstormEnabled = true;
        public double SandstormCountdownStart = 20.0;
        public double SandstormCountdownDuration = 10.0;
        public int SandstormBaseTickRate = 5;
        public int SandstormTickRateDecrement = 1;
        public int SandstormTickRateFloor = 3;
        public double SandstormBaseDamage = 1;
        public double SandstormAdditiveScalar = 2;
    }
}
