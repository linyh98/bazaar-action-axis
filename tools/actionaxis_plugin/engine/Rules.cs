using System;

namespace BazaarActionAxis.Engine
{
    public static class Rules
    {
        public static ResolvedRules Resolve(RuleOverrides overrides)
        {
            return Resolve(overrides, false);
        }

        public static ResolvedRules Resolve(RuleOverrides overrides, bool nativeMechanics)
        {
            ResolvedRules merged = new ResolvedRules();
            if (nativeMechanics) merged.Dt = 0.05;
            if (overrides == null) return merged;

            if (overrides.Dt.HasValue) merged.Dt = overrides.Dt.Value;
            if (overrides.MaxTime.HasValue) merged.MaxTime = overrides.MaxTime.Value;
            if (overrides.HasteMultiplier.HasValue) merged.HasteMultiplier = overrides.HasteMultiplier.Value;
            if (overrides.SlowMultiplier.HasValue) merged.SlowMultiplier = overrides.SlowMultiplier.Value;
            if (overrides.StartReady.HasValue) merged.StartReady = overrides.StartReady.Value;
            if (overrides.BurnDecay.HasValue) merged.BurnDecay = overrides.BurnDecay.Value;
            if (overrides.PoisonDecay.HasValue) merged.PoisonDecay = overrides.PoisonDecay.Value;
            if (overrides.DotBypassesShield.HasValue) merged.DotBypassesShield = overrides.DotBypassesShield.Value;
            if (overrides.CritMultiplier.HasValue) merged.CritMultiplier = overrides.CritMultiplier.Value;
            if (overrides.LifestealOnShieldDamage.HasValue) merged.LifestealOnShieldDamage = overrides.LifestealOnShieldDamage.Value;
            if (overrides.ActivationOrder != null) merged.ActivationOrder = overrides.ActivationOrder;
            if (overrides.MaxEventsPerTick.HasValue) merged.MaxEventsPerTick = overrides.MaxEventsPerTick.Value;
            if (overrides.MaxTriggerDepth.HasValue) merged.MaxTriggerDepth = overrides.MaxTriggerDepth.Value;
            if (overrides.MaxSlots.HasValue) merged.MaxSlots = overrides.MaxSlots.Value;

            if (!Finite(merged.Dt) || merged.Dt <= 0) merged.Dt = 0.1;
            if (!Finite(merged.MaxTime) || merged.MaxTime <= 0) merged.MaxTime = 60;
            if (!Finite(merged.HasteMultiplier) || merged.HasteMultiplier <= 0) merged.HasteMultiplier = 2;
            if (!Finite(merged.SlowMultiplier) || merged.SlowMultiplier <= 0) merged.SlowMultiplier = 0.5;
            if (!Finite(merged.BurnDecay) || merged.BurnDecay < 0) merged.BurnDecay = 1;
            if (!Finite(merged.PoisonDecay) || merged.PoisonDecay < 0) merged.PoisonDecay = 0;
            if (!Finite(merged.MaxSlots) || merged.MaxSlots < 1) merged.MaxSlots = 10;
            // Native actions and multicasts are scheduled on the game's 50 ms frame grid.
            if (nativeMechanics) merged.Dt = 0.05;

            return merged;
        }

        private static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
