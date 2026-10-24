using System;
using System.Collections.Generic;

namespace BazaarActionAxis.Engine
{
    public sealed class BattleDamageSnapshot
    {
        public double Damage;
        public double EffectiveDamage;
        public double DirectDamage;
        public double BurnDamage;
        public double PoisonDamage;
        public double EnemyHp;
        public double EnemyShield;
        public double OutputDamage;
        public double OutputDirectDamage;
        public double OutputBurnDamage;
        public double OutputPoisonDamage;
        public double EnemyHealing;

        internal BattleDamageSnapshot Copy()
        {
            return (BattleDamageSnapshot)MemberwiseClone();
        }
    }

    public sealed class BattleDamageSeries
    {
        private readonly List<double> _times = new List<double>();
        private readonly List<BattleDamageSnapshot> _points = new List<BattleDamageSnapshot>();
        private readonly BattleDamageSnapshot _initial;
        private readonly double _endTime;

        internal BattleDamageSeries(BattleResult result, double enemyMaxHp)
        {
            _endTime = result.EndTime;
            _initial = new BattleDamageSnapshot();
            _initial.EnemyHp = Math.Max(0, result.EnemyStartHp ?? enemyMaxHp);
            _initial.EnemyShield = Math.Max(0, result.EnemyStartShield);
            BattleDamageSnapshot current = _initial.Copy();
            List<KeyValuePair<int, BattleEvent>> events = new List<KeyValuePair<int, BattleEvent>>();
            for (int index = 0; index < result.Events.Count; index++)
                events.Add(new KeyValuePair<int, BattleEvent>(index, result.Events[index]));
            events.Sort(delegate(KeyValuePair<int, BattleEvent> a, KeyValuePair<int, BattleEvent> b)
            {
                int order = a.Value.Time.CompareTo(b.Value.Time);
                return order != 0 ? order : a.Key.CompareTo(b.Key);
            });

            foreach (KeyValuePair<int, BattleEvent> entry in events)
            {
                BattleEvent ev = entry.Value;
                if (!ev.TargetSide.HasValue || ev.TargetSide.Value != Side.Enemy) continue;
                bool direct = ev.Kind == "damage";
                bool burn = ev.Kind == "dotTick" && ev.Note == "burn";
                bool poison = ev.Kind == "dotTick" && ev.Note == "poison";
                bool damage = direct || burn || poison;
                double value = Math.Max(0, ev.Value ?? 0);
                double absorbed = Math.Max(0, ev.Absorbed ?? 0);
                double hpAfter = ev.HpAfter ?? (damage ? Math.Max(0, current.EnemyHp - Math.Max(0, value - absorbed)) : current.EnemyHp);
                if (damage && ev.Side == Side.Player)
                {
                    // ApplyDamage emits one terminal damage event. Crit markers,
                    // DOT stack applications and EffectStats are not extra hits.
                    current.Damage += value;
                    current.EffectiveDamage += Math.Max(0, current.EnemyHp - hpAfter);
                    double actual = Math.Max(0, ev.ActualDamage ?? (Math.Max(0, current.EnemyHp - hpAfter) + absorbed));
                    current.OutputDamage += actual;
                    if (direct) current.OutputDirectDamage += actual;
                    else if (burn) current.OutputBurnDamage += actual;
                    else current.OutputPoisonDamage += actual;
                    if (direct) current.DirectDamage += value;
                    else if (burn) current.BurnDamage += value;
                    else current.PoisonDamage += value;
                }
                // Regen emits both heal and dotTick markers. Only the terminal
                // heal event contributes, and it never increases player output.
                if (ev.Kind == "heal") current.EnemyHealing += Math.Max(0, hpAfter - current.EnemyHp);
                current.EnemyHp = Math.Max(0, hpAfter);
                current.EnemyShield = Math.Max(0, ev.ShieldAfter ?? (damage ? current.EnemyShield - absorbed : current.EnemyShield));
                _times.Add(ev.Time);
                _points.Add(current.Copy());
            }
        }

        // Exact event timestamps let dragging backward remove later hits and
        // avoid exposing the next tick's result between samples.
        public BattleDamageSnapshot At(double time)
        {
            if (double.IsNaN(time) || _times.Count == 0 || time < _times[0] - 1e-9) return _initial.Copy();
            int low = 0;
            int high = _times.Count - 1;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (_times[middle] <= time + 1e-9) low = middle;
                else high = middle - 1;
            }
            return _points[low].Copy();
        }

        public double DpsAt(double time)
        {
            if (double.IsNaN(time) || time <= 0) return 0;
            // 战斗结束后的空白时间不稀释 DPS；拖回结束前仍按当前时刻计算。
            double elapsed = _endTime > 0 ? Math.Min(time, _endTime) : time;
            return At(time).OutputDamage / elapsed;
        }
    }
}
