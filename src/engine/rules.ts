import type { RuleOverrides } from './types';

export interface ResolvedRules extends Required<Omit<RuleOverrides, 'sandstorm'>> {
  sandstorm: RuleOverrides['sandstorm'] | null;
}

export const DEFAULT_RULES: ResolvedRules = {
  dt: 0.1,
  maxTime: 60,
  hasteMultiplier: 2,
  slowMultiplier: 0.5,
  startReady: false,
  burnDecay: 1,
  poisonDecay: 0,
  dotBypassesShield: false,
  critMultiplier: 2,
  lifestealOnShieldDamage: false,
  activationOrder: 'player-first-left-to-right',
  maxEventsPerTick: 500,
  maxTriggerDepth: 20,
  maxSlots: 10,
  sandstorm: null,
};

export function resolveRules(overrides: RuleOverrides = {}): ResolvedRules {
  const merged: ResolvedRules = { ...DEFAULT_RULES, ...overrides };

  if (!Number.isFinite(merged.dt) || merged.dt <= 0) merged.dt = DEFAULT_RULES.dt;
  if (!Number.isFinite(merged.maxTime) || merged.maxTime <= 0) {
    merged.maxTime = DEFAULT_RULES.maxTime;
  }
  if (!Number.isFinite(merged.hasteMultiplier) || merged.hasteMultiplier <= 0) {
    merged.hasteMultiplier = DEFAULT_RULES.hasteMultiplier;
  }
  if (!Number.isFinite(merged.slowMultiplier) || merged.slowMultiplier <= 0) {
    merged.slowMultiplier = DEFAULT_RULES.slowMultiplier;
  }
  if (!Number.isFinite(merged.burnDecay) || merged.burnDecay < 0) {
    merged.burnDecay = DEFAULT_RULES.burnDecay;
  }
  if (!Number.isFinite(merged.poisonDecay) || merged.poisonDecay < 0) {
    merged.poisonDecay = DEFAULT_RULES.poisonDecay;
  }
  if (!Number.isFinite(merged.maxSlots) || merged.maxSlots < 1) {
    merged.maxSlots = DEFAULT_RULES.maxSlots;
  }

  return merged;
}
