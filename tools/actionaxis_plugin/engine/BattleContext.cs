using System.Collections.Generic;

namespace BazaarActionAxis.Engine
{
    public sealed class InternalItem
    {
        public string Id;
        public string CardId;
        public CardDef Card;
        public Side Side;
        public int Slot;
        public double Cooldown;
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
        public List<string> Warnings = new List<string>();

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
