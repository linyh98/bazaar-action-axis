using System;
using System.Collections.Generic;

namespace BazaarActionAxis.Engine
{
    public sealed class CardStats
    {
        public int ActivateCount;
        public double Damage;
        public double Heal;
        public double Shield;
        public int HasteCount;
        public double HasteSeconds;
        public int FreezeCount;
        public double FreezeSeconds;
        public int SlowCount;
        public double SlowSeconds;
        public double ChargeSeconds;
        public double BurnStacks;
        public double PoisonStacks;
    }

    public sealed class CardStatsSeries
    {
        private readonly double _dt;
        private readonly double _endTime;
        private readonly int _ticks;
        private readonly int[] _activate;
        private readonly double[] _damage;
        private readonly double[] _heal;
        private readonly double[] _shield;
        private readonly int[] _hasteCount;
        private readonly int[] _freezeCount;
        private readonly int[] _slowCount;
        private readonly double[] _hasteSeconds;
        private readonly double[] _freezeSeconds;
        private readonly double[] _slowSeconds;
        private readonly double[] _chargeSeconds;
        private readonly double[] _burnStacks;
        private readonly double[] _poisonStacks;
        private readonly List<ItemSnapshot> _cooldowns = new List<ItemSnapshot>();
        private bool _hasCooldown;

        internal CardStatsSeries(double dt, int ticks, double endTime)
        {
            _dt = dt;
            _endTime = endTime;
            _ticks = ticks;
            _activate = new int[ticks];
            _damage = new double[ticks];
            _heal = new double[ticks];
            _shield = new double[ticks];
            _hasteCount = new int[ticks];
            _freezeCount = new int[ticks];
            _slowCount = new int[ticks];
            _hasteSeconds = new double[ticks];
            _freezeSeconds = new double[ticks];
            _slowSeconds = new double[ticks];
            _chargeSeconds = new double[ticks];
            _burnStacks = new double[ticks];
            _poisonStacks = new double[ticks];
        }

        public double Dt { get { return _dt; } }
        public int Ticks { get { return _ticks; } }
        public bool HasCooldown { get { return _hasCooldown; } }

        internal void AddCooldown(ItemSnapshot snapshot)
        {
            _cooldowns.Add(snapshot);
            _hasCooldown |= snapshot.HasCooldown;
        }

        // 0 = new cooldown, 1 = ready. At an activation tick the next cooldown
        // has already begun; charge applied by that activation is retained.
        // Interpolation advances only natural cooldown time, never future effects.
        public double CooldownProgressAt(double time)
        {
            if (!HasCooldown || double.IsNaN(time)) return 0;
            if (time < _cooldowns[0].Time) return 0;
            if (time > _endTime) time = _endTime;

            ItemSnapshot snapshot = _cooldowns[CooldownIndexAt(time)];
            if (!snapshot.HasCooldown || snapshot.CooldownMax <= 0) return 0;
            double elapsed = Math.Max(0, time - snapshot.Time);
            double remaining = Math.Max(0, snapshot.CooldownRemaining - elapsed * snapshot.CooldownRate);
            return Math.Max(0, Math.Min(1, 1 - remaining / snapshot.CooldownMax));
        }

        // An actual activation completed a cooldown in this sample. The UI may
        // show a full-water pulse, then resume the next cooldown's progress.
        public bool CooldownCompletedAt(double time)
        {
            if (!HasCooldown || double.IsNaN(time) || double.IsInfinity(time) ||
                time < _cooldowns[0].Time || time > _endTime + 1e-9) return false;
            return _cooldowns[CooldownIndexAt(time)].Activated;
        }

        private int CooldownIndexAt(double time)
        {
            int low = 0;
            int high = _cooldowns.Count - 1;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (_cooldowns[middle].Time <= time + 1e-9) low = middle;
                else high = middle - 1;
            }
            return low;
        }

        internal int IndexOf(double time)
        {
            int index = (int)Math.Floor(time / _dt + 1e-9);
            if (index < 0) index = 0;
            if (index >= _ticks) index = _ticks - 1;
            return index;
        }

        internal void AddActivation(double time)
        {
            _activate[IndexOf(time)]++;
        }

        internal void AddDamage(double time, double value)
        {
            _damage[IndexOf(time)] += value;
        }

        internal void AddHeal(double time, double value)
        {
            _heal[IndexOf(time)] += value;
        }

        internal void AddShield(double time, double value)
        {
            _shield[IndexOf(time)] += value;
        }

        internal void AddControl(string kind, double time, double seconds)
        {
            int index = IndexOf(time);
            if (kind == "haste")
            {
                _hasteCount[index]++;
                _hasteSeconds[index] += seconds;
            }
            else if (kind == "freeze")
            {
                _freezeCount[index]++;
                _freezeSeconds[index] += seconds;
            }
            else if (kind == "slow")
            {
                _slowCount[index]++;
                _slowSeconds[index] += seconds;
            }
            else if (kind == "charge")
            {
                _chargeSeconds[index] += seconds;
            }
            else if (kind == "burn")
            {
                _burnStacks[index] += seconds;
            }
            else if (kind == "poison")
            {
                _poisonStacks[index] += seconds;
            }
        }

        public CardStats At(double time)
        {
            int end = IndexOf(time);
            CardStats stats = new CardStats();
            for (int index = 0; index <= end; index++)
            {
                stats.ActivateCount += _activate[index];
                stats.Damage += _damage[index];
                stats.Heal += _heal[index];
                stats.Shield += _shield[index];
                stats.HasteCount += _hasteCount[index];
                stats.HasteSeconds += _hasteSeconds[index];
                stats.FreezeCount += _freezeCount[index];
                stats.FreezeSeconds += _freezeSeconds[index];
                stats.SlowCount += _slowCount[index];
                stats.SlowSeconds += _slowSeconds[index];
                stats.ChargeSeconds += _chargeSeconds[index];
                stats.BurnStacks += _burnStacks[index];
                stats.PoisonStacks += _poisonStacks[index];
            }
            return stats;
        }
    }

    public static class Aggregator
    {
        public static BattleDamageSeries BuildDamage(BattleResult result, double enemyMaxHp)
        {
            return new BattleDamageSeries(result ?? new BattleResult(), Side.Enemy, enemyMaxHp);
        }

        public static BattleDamageSeries BuildDamage(BattleResult result, Side targetSide, double targetMaxHp)
        {
            return new BattleDamageSeries(result ?? new BattleResult(), targetSide, targetMaxHp);
        }

        public static Dictionary<string, CardStatsSeries> Build(BattleResult result, double dt)
        {
            List<string> ids = new List<string>();
            foreach (BattleItemView item in result.PlayerItems) ids.Add(item.Id);
            return Build(result, ids, dt);
        }

        public static Dictionary<string, CardStatsSeries> Build(
            BattleResult result,
            IList<string> playerItemIds,
            double dt)
        {
            if (dt <= 0) dt = 0.1;
            int ticks = (int)Math.Floor(result.EndTime / dt + 1e-9) + 1;
            if (ticks < 1) ticks = 1;

            Dictionary<string, CardStatsSeries> map = new Dictionary<string, CardStatsSeries>();
            foreach (string id in playerItemIds)
            {
                if (!map.ContainsKey(id)) map[id] = new CardStatsSeries(dt, ticks, result.EndTime);
            }

            foreach (BattleEvent ev in result.Events)
            {
                double value = ev.Value.HasValue ? ev.Value.Value : 0;
                bool damage = ev.Kind == "damage" ||
                    (ev.Kind == "dotTick" && (ev.Note == "burn" || ev.Note == "poison"));
                if (ev.Kind == "activate" || ev.Kind == "skillTrigger")
                {
                    CardStatsSeries action;
                    if (ev.SourceItemId != null && map.TryGetValue(ev.SourceItemId, out action))
                        action.AddActivation(ev.Time);
                    continue;
                }
                if (damage && ev.Shares != null && ev.Shares.Count > 0)
                {
                    // DoT ticks are split among the current stack owners. The
                    // observed side is never the output side for its own hit.
                    foreach (BattleEventShare share in ev.Shares)
                    {
                        if (ev.TargetSide.HasValue && share.Side == ev.TargetSide.Value) continue;
                        CardStatsSeries shared;
                        if (share.SourceItemId != null && map.TryGetValue(share.SourceItemId, out shared))
                            shared.AddDamage(ev.Time, Math.Max(0, share.ActualAmount));
                    }
                    continue;
                }
                if (ev.Kind == "heal" && ev.Shares != null && ev.Shares.Count > 0)
                {
                    foreach (BattleEventShare share in ev.Shares)
                    {
                        CardStatsSeries shared;
                        if (share.SourceItemId != null && map.TryGetValue(share.SourceItemId, out shared))
                            shared.AddHeal(ev.Time, Math.Max(0, share.ActualAmount));
                    }
                    continue;
                }
                if (ev.SourceItemId == null) continue;
                CardStatsSeries series;
                if (!map.TryGetValue(ev.SourceItemId, out series)) continue;

                if (damage)
                {
                    if (ev.TargetSide.HasValue && ev.Side == ev.TargetSide.Value) continue;
                    series.AddDamage(ev.Time, ev.ActualDamage ?? value);
                }
                else if (ev.Kind == "heal") series.AddHeal(ev.Time, value);
                else if (ev.Kind == "shield") series.AddShield(ev.Time, value);
            }

            foreach (EffectStat stat in result.EffectStats)
            {
                if (stat.SourceItemId == null) continue;
                CardStatsSeries series;
                if (!map.TryGetValue(stat.SourceItemId, out series)) continue;
                if (stat.Kind == "haste" || stat.Kind == "freeze" || stat.Kind == "slow" ||
                    stat.Kind == "charge" || stat.Kind == "burn" || stat.Kind == "poison")
                {
                    series.AddControl(stat.Kind, stat.Time, stat.Value);
                }
            }

            foreach (ItemSnapshot snapshot in result.ItemSnapshots)
            {
                CardStatsSeries series;
                if (snapshot.ItemId != null && map.TryGetValue(snapshot.ItemId, out series))
                    series.AddCooldown(snapshot);
            }

            return map;
        }
    }
}
