using System.Collections.Generic;

namespace BazaarActionAxis.Engine
{
    public sealed class TimedAttribute
    {
        public string Attribute;
        public double Amount;
        public double ExpiresAt;
        public bool Expired;
    }

    public sealed class InternalItem
    {
        public string Id;
        public string CardId;
        public CardDef Card;
        public Side Side;
        public int Slot;
        public bool IsSkill;
        public double Cooldown;
        // 战斗内可被 CooldownMax 修改；快照与重置都用它。
        public double CooldownMax;
        // 运行时属性偏移：叠加在 Card.LiveAttributes 之上。
        public Dictionary<string, double> AttributeDeltas = new Dictionary<string, double>();
        public Dictionary<string, double> AuraBaseAttributes;
        public Dictionary<string, double> AuraAttributes = new Dictionary<string, double>();
        // 当前生效的持续冷却缩减光环（每帧重建）：Flat 单位 ms，Percent 为百分比。
        public double AuraCooldownFlat;
        public double AuraCooldownPercent;
        public List<TimedAttribute> TimedAttributes = new List<TimedAttribute>();
        // 战斗内新增标签（TActionCardAddTagsList 等），不修改共享的 CardDef。
        public List<string> ExtraTags = new List<string>();
        public bool Disabled;
        public bool Destroyed;
        public double HasteUntil = -1;
        public double SlowUntil = -1;
        public double FrozenUntil = -1;
        public int MulticastBonus;
        public double CritBonus;
        public double DamageBonus;
        public double Lifesteal;
        public int Ammo;
        public int? AmmoMax;
        // A combat state, not a permanent property copied from a mid-replay board.
        public bool Flying;
        public Dictionary<TriggerDef, int> NativeTriggerCounts = new Dictionary<TriggerDef, int>();
    }

    public sealed class InternalHero
    {
        public Side Side;
        public double Hp;
        public double MaxHp;
        public double Shield;
        public double Burn;
        public double Poison;
        public double Regen;
        // 原生 RageMax 默认 100，暴怒持续 EnragedDurationMs（默认 5000 ms）。
        public double Rage;
        public double RageMax = 100;
        public double EnragedDurationMs = 5000;
        public double EnragedUntil = -1;
        public Side? BurnSource;
        public string BurnSourceItemId;
        public string BurnSourceCardId;
        public Side? PoisonSource;
        public string PoisonSourceItemId;
        public string PoisonSourceCardId;
        public string RegenSourceItemId;
        public string RegenSourceCardId;

        public InternalHero(Side side)
        {
            Side = side;
        }
    }

    public sealed class TriggerPayload
    {
        public string Event;
        public Side SourceSide;
        public string SourceItemId;
        public Side? TargetSide;
        public string TargetItemId;
        // TTriggerOnCardAttributeChanged / PlayerAttributeChanged 的负载。
        public string AttributeName;
        public string ChangeType;
        public double? Amount;
        public int? ChainId;
        public int Depth;
    }

    public sealed class BattleContext
    {
        public BattleConfig Config;
        public ResolvedRules Rules;
        public Rng Rng;
        public InternalHero Player;
        public InternalHero Enemy;
        public List<InternalItem> Items = new List<InternalItem>();
        // 变形产生的新卡不能在遍历 ctx.Items 时插入，先排队再由战斗循环追加。
        public List<InternalItem> PendingItems = new List<InternalItem>();
        // Skills emit effects but never take board slots, cooldowns or item targets.
        public List<InternalItem> Skills = new List<InternalItem>();
        public List<BattleEvent> Events = new List<BattleEvent>();
        public List<EffectStat> EffectStats = new List<EffectStat>();
        public List<ItemSnapshot> ItemSnapshots = new List<ItemSnapshot>();
        public Queue<TriggerPayload> TriggerQueue = new Queue<TriggerPayload>();
        public Totals Totals = new Totals();
        public int NextEventId = 1;
        public int NextChainId = 1;
        public int EventsThisTick;
        public bool Ended;
        public double EndTime;
        public string Winner;
        // 每方一次复活机会，避免死亡触发反复回血。
        public bool PlayerReviveUsed;
        public bool EnemyReviveUsed;
        public List<string> Warnings = new List<string>();
        public bool DynamicAurasInitialized;
        public bool EvaluatingDynamicAuras;

        // TReferenceValueAttributeChange 读取“当前触发事件携带的变化量”；
        // 用栈保存，嵌套触发结束后恢复外层数值。
        private readonly Stack<double?> _triggerAmounts = new Stack<double?>();

        public double? TriggerAmount
        {
            get { return _triggerAmounts.Count > 0 ? _triggerAmounts.Peek() : (double?)null; }
        }

        public void PushTriggerAmount(double? amount) { _triggerAmounts.Push(amount); }

        public void PopTriggerAmount() { if (_triggerAmounts.Count > 0) _triggerAmounts.Pop(); }

        public InternalHero Hero(Side side)
        {
            return side == Side.Player ? Player : Enemy;
        }

        public bool IsTrainingDummySource(Side side)
        {
            return Config.EnemyIsTrainingDummy && side == Side.Enemy;
        }
    }
}
