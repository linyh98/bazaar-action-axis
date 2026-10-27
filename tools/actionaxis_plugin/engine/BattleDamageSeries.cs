using System;
using System.Collections.Generic;

namespace BazaarActionAxis.Engine
{
    public sealed class BattleDamageSnapshot
    {
        // True output against the observed side: HP removed plus Shield consumed.
        public double OutputDamage;
        public double Damage;
        public double EffectiveDamage;
        public double DirectDamage;
        public double BurnDamage;
        public double PoisonDamage;
        public double EnemyHp;
        public double EnemyShield;
        public double OutputDirectDamage;
        public double OutputBurnDamage;
        public double OutputPoisonDamage;
        public double EnemyHealing;
        // Damage negated by shield rules (native Burn can prevent more damage
        // than the Shield points it consumes).
        public double PreventedDamage;
        public double ShieldAbsorbed;
        // Raw damage beyond the remaining HP. Included in native TotalDamage
        // but never in true output.
        public double OverkillDamage;
        public double NominalHealing;
        public double NetHpLoss;
        // HP movement from Max-HP changes, not from damage or healing.
        public double OtherHpChange;
        public double SelfDamage;

        public double Healing { get { return EnemyHealing; } }
        public double Overheal { get { return Math.Max(0, NominalHealing - EnemyHealing); } }
        // Native TotalDamage keeps overkill while true output excludes it.
        public double SettledDamage { get { return OutputDamage + OverkillDamage; } }

        internal BattleDamageSnapshot Copy()
        {
            return (BattleDamageSnapshot)MemberwiseClone();
        }
    }

    public sealed class BattleHealthSeries
    {
        private readonly List<BattleHealthSnapshot> _points = new List<BattleHealthSnapshot>();
        public readonly bool EndedByDeath;

        public BattleHealthSeries(BattleResult result)
        {
            if (result == null) return;
            EndedByDeath = result.EndedByDeath;
            foreach (BattleHealthSnapshot point in result.HealthSnapshots) _points.Add(point.Copy());
        }

        public BattleHealthSnapshot At(double time)
        {
            if (_points.Count == 0) return null;
            if (double.IsNaN(time)) return _points[0].Copy();
            int low = 0, high = _points.Count - 1;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (_points[middle].Time <= time + 1e-9) low = middle;
                else high = middle - 1;
            }
            return _points[low].Copy();
        }
    }

    public sealed class BattleDamageSeries
    {
        private readonly List<double> _times = new List<double>();
        private readonly List<BattleDamageSnapshot> _points = new List<BattleDamageSnapshot>();
        private readonly BattleDamageSnapshot _initial;
        private readonly double _endTime;
        private readonly Side _targetSide;

        internal BattleDamageSeries(BattleResult result, double enemyMaxHp)
            : this(result, Side.Enemy, enemyMaxHp)
        {
        }

        internal BattleDamageSeries(BattleResult result, Side targetSide, double targetMaxHp)
        {
            _targetSide = targetSide;
            _endTime = result != null ? result.EndTime : 0;
            _initial = new BattleDamageSnapshot();
            if (result == null)
            {
                _initial.EnemyHp = Math.Max(0, targetMaxHp);
                return;
            }
            if (targetSide == Side.Enemy)
            {
                _initial.EnemyHp = Math.Max(0, result.EnemyStartHp ?? targetMaxHp);
                _initial.EnemyShield = Math.Max(0, result.EnemyStartShield);
            }
            else
            {
                _initial.EnemyHp = Math.Max(0, result.PlayerStartHp ?? targetMaxHp);
                _initial.EnemyShield = Math.Max(0, result.PlayerStartShield);
            }
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
                if (!ev.TargetSide.HasValue || ev.TargetSide.Value != targetSide) continue;
                bool direct = ev.Kind == "damage";
                bool burn = ev.Kind == "dotTick" && ev.Note == "burn";
                bool poison = ev.Kind == "dotTick" && ev.Note == "poison";
                bool damage = direct || burn || poison;
                bool heal = ev.Kind == "heal";
                double value = Math.Max(0, ev.Value ?? 0);
                double absorbed = Math.Max(0, ev.Absorbed ?? 0);
                double hpBefore = current.EnemyHp;
                double hpAfter = ev.HpAfter.HasValue
                    ? Math.Max(0, ev.HpAfter.Value)
                    : damage
                        ? Math.Max(0, current.EnemyHp - Math.Max(0, value - absorbed))
                        : heal ? current.EnemyHp + value : current.EnemyHp;

                if (damage)
                {
                    List<BattleEventShare> shares = ev.Shares;
                    if (shares != null && shares.Count > 0)
                    {
                        // A multi-source DoT tick is measured once and then split.
                        // Shares never cross the observed side for output.
                        foreach (BattleEventShare share in shares)
                        {
                            if (share.Side == targetSide)
                            {
                                // Same-side damage reduces own HP but is never output.
                                // Keep only its HP-loss part for the net-loss identity.
                                current.SelfDamage += Math.Max(0, share.ActualAmount - Math.Max(0, share.Absorbed));
                                continue;
                            }
                            current.Damage += Math.Max(0, share.Amount);
                            double actual = Math.Max(0, share.ActualAmount);
                            current.EffectiveDamage += Math.Max(0, actual - Math.Max(0, share.Absorbed));
                            current.OutputDamage += actual;
                            current.ShieldAbsorbed += Math.Max(0, share.Absorbed);
                            current.OverkillDamage += Math.Max(0, share.Overkill);
                            current.PreventedDamage += Math.Max(0, share.Prevented);
                            if (direct) current.OutputDirectDamage += actual;
                            else if (burn) current.OutputBurnDamage += actual;
                            else current.OutputPoisonDamage += actual;
                            if (direct) current.DirectDamage += Math.Max(0, share.Amount);
                            else if (burn) current.BurnDamage += Math.Max(0, share.Amount);
                            else current.PoisonDamage += Math.Max(0, share.Amount);
                        }
                    }
                    else
                    {
                        double hpLoss = Math.Max(0, hpBefore - hpAfter);
                        if (ev.Side != targetSide)
                        {
                            // ApplyDamage emits one terminal damage event. Crit markers,
                            // DOT stack applications and EffectStats are not extra hits.
                            current.Damage += value;
                            current.EffectiveDamage += hpLoss;
                            double actual = Math.Max(0, ev.ActualDamage ?? (hpLoss + absorbed));
                            current.OutputDamage += actual;
                            current.ShieldAbsorbed += absorbed;
                            double overkill = ev.Overkill.HasValue
                                ? Math.Max(0, ev.Overkill.Value)
                                : direct ? Math.Max(0, value - absorbed - hpLoss) : poison ? Math.Max(0, value - hpLoss) : 0;
                            current.OverkillDamage += overkill;
                            current.PreventedDamage += ev.Prevented.HasValue
                                ? Math.Max(0, ev.Prevented.Value)
                                : burn ? Math.Max(0, value - absorbed - hpLoss) : 0;
                            if (direct) current.OutputDirectDamage += actual;
                            else if (burn) current.OutputBurnDamage += actual;
                            else current.OutputPoisonDamage += actual;
                            if (direct) current.DirectDamage += value;
                            else if (burn) current.BurnDamage += value;
                            else current.PoisonDamage += value;
                        }
                        else
                        {
                            current.SelfDamage += hpLoss;
                        }
                    }
                }
                // Regen emits both heal and dotTick markers. Only the terminal
                // heal event contributes, and it never increases player output.
                if (heal)
                {
                    double accepted = Math.Max(0, hpAfter - hpBefore);
                    current.EnemyHealing += accepted;
                    current.NominalHealing += Math.Max(0, ev.NominalAmount ?? (value + (ev.Overheal ?? 0)));
                }
                else if (!damage && ev.HpAfter.HasValue && Math.Abs(hpAfter - hpBefore) > 1e-9)
                {
                    // Max-HP changes can move current HP without a damage or
                    // heal event. Keep that movement separate from both.
                    current.OtherHpChange += hpAfter - hpBefore;
                }

                current.EnemyHp = Math.Max(0, hpAfter);
                current.EnemyShield = Math.Max(0, ev.ShieldAfter ?? (damage ? current.EnemyShield - absorbed : current.EnemyShield));
                current.NetHpLoss = _initial.EnemyHp - current.EnemyHp;
                _times.Add(ev.Time);
                _points.Add(current.Copy());
            }
        }

        public Side TargetSide { get { return _targetSide; } }

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
