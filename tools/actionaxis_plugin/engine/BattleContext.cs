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
        public int Generation;
        // Accepted native uses share one non-Immediate execution window,
        // including forced uses that coincide with a natural activation.
        public double NativeCastNextAt;
        public int NativeCastGeneration;
        // Reserve ordering when cooldown first becomes ready, including charge
        // inside another cast, before that cast publishes its onUse observers.
        public long NativeReadyOrder;
        public double NativeReadyAfter;
        public BattleItemView History;
        public double Cooldown;
        // 原生：充能落在本帧已就绪（冷却 0、即将重置）的卡上时，在重置后的
        // 新冷却上扣减（170666bf 帧 161：重置 5000 后同帧 -1000 -> 4000）。
        public double PendingChargeOnReset;
        // An empty Ammo item becoming usable this frame does not carry an
        // already-zero cooldown charge into its newly scheduled cooldown.
        public double LastReloadFromEmptyAt = -1;
        // 每次使用叠加光环的已计入贡献（键 = 光环下标#目标Id），首次使用时初始化。
        public Dictionary<string, double> UseAuraContributions;
        // 依赖型（引用被写属性、按贡献增量重算）光环的下标集合。
        public HashSet<int> UseAuraTrackedIndexes;
        // 战斗内可被 CooldownMax 修改；快照与重置都用它。
        public double CooldownMax;
        // Base Rage Effect 的 10% 冷却缩减在本场暴怒期间的原始 CooldownMax。
        public double BaseRageCooldownMax = -1;
        // 运行时属性偏移：叠加在 Card.LiveAttributes 之上。
        public Dictionary<string, double> AttributeDeltas = new Dictionary<string, double>();
        public Dictionary<string, double> AuraBaseAttributes;
        public Dictionary<string, double> AuraAttributes = new Dictionary<string, double>();
        // 当前生效的持续冷却缩减光环（每帧重建）：Flat 单位 ms，Percent 为百分比。
        public double AuraCooldownFlat;
        public double AuraCooldownPercent;
        // 当前生效的抗性/免疫光环。与实时快照隔离，避免飞行衍生的 50% 抗性泄漏进开场。
        public double AuraSlowResistance;
        public double AuraFreezeResistance;
        public double AuraHasteResistance;
        public double AuraDestroyImmunity;
        // 战斗内附魔前的卡与光环基准，用于移除附魔时精确还原。
        public CardDef PreEnchantCard;
        public Dictionary<string, double> PreEnchantAuraBase;
        public List<TimedAttribute> TimedAttributes = new List<TimedAttribute>();
        // 战斗内新增标签（TActionCardAddTagsList 等），不修改共享的 CardDef。
        public List<string> ExtraTags = new List<string>();
        // 动态标签光环（It's a Drone 的“最左手牌”等）每帧重建的标签：
        // 与动作型 ExtraTags 分开，光环换目标时只清空这一层。
        public List<string> AuraTags = new List<string>();
        public bool TagAurasInitialized;
        public bool Disabled;
        // 战斗内 CardDisable 的持久状态；不能被派生层每帧重建的
        // CooldownDisabled=0 覆盖（原生禁用后冷却停走，ebde2082 帧 79 实证）。
        public bool CombatDisabled;
        // 原生 CardDisable 生效后该卡光环不再执行（02dae1ed 帧390：Pepper Spray 自禁用
        // 同帧 SlowAmount 4000→2000，CooldownMax/FlatCooldownReduction 回落到卡面基底）。
        // 禁用链当帧内仍需读到光环值（Elixir of Immortality 满血复活），故在帧末生效。
        public bool AuraInactive;
        public bool Destroyed;
        // Re-entrant destroy guard: an on-before-destroy effect may destroy the
        // same card or the actor again in the same frame.
        public bool Destroying;
        public bool Disabling;
        // Artificial Heart 的模板没有 Custom_0 前提；卡面写“first time each
        // fight”，在触发结算后锁一次，避免每次致死都满血。
        public bool OncePerFightUsed;
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
        // 起飞的战斗时间。同帧仍可被选中；从下一帧起，“摧毁/禁用”类动作不再
        // 选择飞行中的卡（07f044ee 帧 157 Wrecking Ball 的随机敌方手牌目标在
        // 敌方 6 张全部飞行时为空，没有 CardDisable 事件）。
        public double FlyingSince = -1;
        // The temporary +50 resistance contribution is tracked separately so
        // landing/end-of-combat preserves genuine card/aura resistance.
        public bool FlyingResistanceApplied;
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
        // 本场战斗累计获得的 Tempo（Ticket Terra/Debut Album 等）。
        public double Tempo;
        // 原生 TempoGain：每 TempoGainCooldownMax 毫秒自动 +1 Tempo
        // （0276c46b 帧 20/41/62/83/104… 每 21 帧一次）。-1 表示本场不增长。
        public double TempoGainInterval;
        public double NextTempoGainAt = -1;
        // 原生人物属性（Custom_N/Level/CritChance 等）；运行时字段优先。
        public Dictionary<string, double> Attributes = new Dictionary<string, double>();
        public double AuraCritChance;
        // 动态人物光环的派生层：记录当前已计入 MaxHp/Regen 等的增量，
        // 每次 Refresh 先回到基底再加新值，避免条件开关重复叠加。
        public double AuraMaxHp;
        public double AuraRegen;
        public double AuraRageMax;
        public double AuraEnragedDurationMs;
        public double AuraPercentDamageReduction;
        // 战斗内道具给予的限时百分比减伤（Invulnerability Potion 等）。
        public double ActionPercentDamageReduction;
        public double ActionPercentDamageReductionUntil = -1;
        public Side? BurnSource;
        public string BurnSourceItemId;
        public string BurnSourceCardId;
        public Side? PoisonSource;
        public string PoisonSourceItemId;
        public string PoisonSourceCardId;
        public string RegenSourceItemId;
        public string RegenSourceCardId;
        // 每个状态按施加来源保留份额，tick 时按比例归因。
        public List<StatusContribution> BurnPools = new List<StatusContribution>();
        public List<StatusContribution> PoisonPools = new List<StatusContribution>();
        public List<StatusContribution> RegenPools = new List<StatusContribution>();

        public InternalHero(Side side)
        {
            Side = side;
        }
    }

    // onUse 分帧结算：非 Immediate 的匹配在使用帧先完成前提判定并快照，
    // 下一帧只执行、不再重评（计数器被 Immediate 能力 +1 后不能反噬判定）。
    public sealed class DeferredTriggerMatch
    {
        public InternalItem Owner;
        public TriggerDef Trigger;
        public bool IsSkill;
    }

    // A native ability has one 250 ms execution queue, shared by events from
    // different source items. Preserve the matched payload until its turn.
    public sealed class DeferredNativeTrigger
    {
        public DeferredTriggerMatch Match;
        public TriggerPayload Payload;
        public double DueAt;
        public int Generation;
        public bool AfterEarlierCast;
    }

    public sealed class TriggerPayload
    {
        public string Event;
        public Side SourceSide;
        public string SourceItemId;
        public Side? TargetSide;
        public string TargetItemId;
        // 原生 Trigger.Source：造成该事件的行动者。Subject 是被影响的卡
        // （修理／变形／摧毁前）时，它与 SourceItemId 不是同一张卡。
        public Side? ActorSide;
        public string ActorItemId;
        // TTriggerOnCardAttributeChanged / PlayerAttributeChanged 的负载。
        public string AttributeName;
        public string ChangeType;
        public double? Amount;
        public int? ChainId;
        public int Depth;
        // 原生按 EffectPriority 分帧：Immediate 的 onUse 被动在使用帧同帧结算
        // （ebde2082：技能 Immediate modifyAttribute 与被用物品自身效果同帧），
        // 其余优先级下一帧；该标志防止下一帧重复结算 Immediate 部分。
        public bool ImmediateSettled;
        public int BeforeUseStage;
        public List<DeferredTriggerMatch> BeforeUseSettled;
        // 使用帧判定通过、留待下一帧执行的非 Immediate 匹配（Owner+Trigger）。
        public List<DeferredTriggerMatch> DeferredMatches;
        // 沙暴开始等全场事件没有卡牌来源；触发器没有 Subject/Source/过滤时仍应结算。
        public bool Global;
        public long NativeQueueOrder;
    }

    // 原生对同一（充能者,目标）的 CardCharge 串行结算：250ms 内到达的后续
    // 充能延后到上次结算+0.25s。语料 12,930 次相邻充能间隔恰为 5 帧，
    // 同帧同对仅 3 例；跨目标不受影响（1,257 组同帧多目标）。
    public sealed class DeferredCharge
    {
        public InternalItem Charger;
        public InternalItem Target;
        public double Seconds;
        public double DueAt;
        public int Depth;
        public int? ChainId;
    }

    public sealed class BattleContext
    {
        public BattleConfig Config;
        public ReplayRandomSession ReplayRandom;
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
        public long NativeQueueSequence;
        public long NativeEarliestDueCastOrder;
        public bool NativePeriodicBeforeObservers;
        public Dictionary<InternalItem, Dictionary<TriggerDef, double>> NativeTriggerNextAt =
            new Dictionary<InternalItem, Dictionary<TriggerDef, double>>();
        public List<DeferredNativeTrigger> DeferredNativeTriggers = new List<DeferredNativeTrigger>();
        // Installed by the native simulation loop. Direct rule evaluation can
        // still use ForceUse synchronously without inventing a frame loop.
        public System.Action<InternalItem, double, int> NativeForceUseScheduler;
        public bool NativeOpeningEffects;
        public Totals Totals = new Totals();
        public int NextEventId = 1;
        public int NextChainId = 1;
        public int EventsThisTick;
        public bool Ended;
        public double EndTime;
        public string Winner;
        public List<string> Warnings = new List<string>();
        public bool DynamicAurasInitialized;
        public bool EvaluatingDynamicAuras;
        // 当前结算时刻；时效状态条件（Haste/Slow/Freeze）用它判断是否仍在生效。
        public double Now;
        // TTargetCardRandom 光环在开场选定目标后保持稳定，避免每次刷新重新抽取。
        public Dictionary<string, string> AuraTargets = new Dictionary<string, string>();
        // 原生 TTriggerOnCardCritted 在一次卡牌结算里只派发一次：同一张卡的
        // 直伤暴击与随附的燃烧/中毒暴击不重复触发（键 = 来源|帧）。
        public HashSet<string> CritTriggerFrames = new HashSet<string>();
        // 终局快照只接受确定性属性反应：非属性效果与观察者派发在此模式静默。
        public bool TerminalAttributesOnly;
        // 同一（充能者,目标）最近一次 CardCharge 结算时刻；键 = chargerId|targetId。
        public Dictionary<string, double> LastChargeAt = new Dictionary<string, double>();
        public List<DeferredCharge> DeferredCharges = new List<DeferredCharge>();
        // 沙暴开始只发生一次；默认倒计时/伤害/步进来自 CombatReplays 时间序列。
        public bool SandstormCountdownStarted;
        public double SandstormCountdownAt = -1;
        public bool SandstormStarted;
        public double SandstormStartedAt = -1;
        public int SandstormTickIndex;
        public double NextSandstormTickAt = -1;
        public double LastSandstormTickAt = -1;

        // TReferenceValueAttributeChange 读取“当前触发事件携带的变化量”；
        // 用栈保存，嵌套触发结束后恢复外层数值。
        private readonly Stack<double?> _triggerAmounts = new Stack<double?>();

        public double? TriggerAmount
        {
            get { return _triggerAmounts.Count > 0 ? _triggerAmounts.Peek() : (double?)null; }
        }

        public void PushTriggerAmount(double? amount) { _triggerAmounts.Push(amount); }

        public void PopTriggerAmount() { if (_triggerAmounts.Count > 0) _triggerAmounts.Pop(); }

        public void RecordItemState(InternalItem item, double time)
        {
            if (item.History == null)
                item.History = new BattleItemView { Id = item.Id, Side = item.Side, BornAt = time };
            item.History.Card = item.Card;
            item.History.CardId = item.CardId;
            item.History.Slot = item.Slot;
            item.History.States.Add(new BattleItemState { Time = time, Card = item.Card, Slot = item.Slot });
        }

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
