import { describe, it, expect, beforeEach } from 'vitest';
import { loadCustomCards, loadLoadouts, loadRules } from '../storage';
import { resolveRules } from '../../engine/rules';

describe('storage validation', () => {
  beforeEach(() => localStorage.clear());

  it('rejects a malformed loadout instead of crashing', () => {
    localStorage.setItem(
      'bazaar.loadouts.v1',
      '{"player":{"items":5},"enemy":{"items":[]}}',
    );
    expect(loadLoadouts()).toBeNull();
  });

  it('filters invalid custom cards', () => {
    localStorage.setItem(
      'bazaar.customCards.v1',
      JSON.stringify([
        null,
        {
          id: 'ok',
          name: 'Ok',
          size: 'Small',
          tier: 'Bronze',
          cooldown: 1,
          effects: [],
          triggers: [],
          tags: [],
          parseStatus: 'structured',
          rawTooltips: [],
        },
      ]),
    );
    const cards = loadCustomCards();
    expect(cards).toHaveLength(1);
    expect(cards[0].id).toBe('ok');
  });

  it('clamps invalid stored rules', () => {
    localStorage.setItem('bazaar.rules.v1', '{"dt":0,"maxTime":null}');
    const rules = resolveRules(loadRules());
    expect(rules.dt).toBe(0.1);
    expect(rules.maxTime).toBe(60);
  });

  it('clamps invalid rule overrides', () => {
    expect(resolveRules({ dt: -1 }).dt).toBe(0.1);
    expect(resolveRules({ dt: 0 }).dt).toBe(0.1);
    expect(resolveRules({ maxTime: 0 }).maxTime).toBe(60);
  });
});
