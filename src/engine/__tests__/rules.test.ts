import { describe, it, expect } from 'vitest';
import { DEFAULT_RULES, resolveRules } from '../rules';

describe('rules', () => {
  it('uses the spec defaults', () => {
    expect(resolveRules()).toEqual(DEFAULT_RULES);
    expect(DEFAULT_RULES.dt).toBe(0.1);
    expect(DEFAULT_RULES.maxTime).toBe(60);
    expect(DEFAULT_RULES.hasteMultiplier).toBe(2);
    expect(DEFAULT_RULES.slowMultiplier).toBe(0.5);
    expect(DEFAULT_RULES.maxSlots).toBe(10);
    expect(DEFAULT_RULES.burnDecay).toBe(1);
    expect(DEFAULT_RULES.poisonDecay).toBe(0);
    expect(DEFAULT_RULES.sandstorm).toBeNull();
  });
  it('applies overrides', () => {
    expect(resolveRules({ dt: 0.2 }).dt).toBe(0.2);
  });
});
