import { describe, it, expect } from 'vitest';
import { simulate } from '../battle';
import type { CardDef, Effect } from '../types';

const card = (
  id: string,
  effects: Effect[],
  extra: Partial<CardDef> = {},
): CardDef => ({
  id,
  name: id,
  size: 'Small',
  tier: 'Bronze',
  cooldown: 99,
  effects,
  triggers: [],
  tags: [],
  parseStatus: 'structured',
  rawTooltips: [],
  ...extra,
});

describe('DoT death', () => {
  it('does not let regen revive a hero killed by burn in the same tick', () => {
    const burn = card(
      'burn',
      [{ kind: 'burn', amount: { type: 'const', value: 20 }, target: { kind: 'enemyHero' } }],
      { startReady: true },
    );
    const regen = card(
      'regen',
      [{ kind: 'regen', amount: { type: 'const', value: 10 }, target: { kind: 'selfHero' } }],
      { startReady: true },
    );
    const r = simulate({
      cards: { burn, regen },
      player: { items: [{ cardId: 'regen', slot: 0 }] },
      enemy: { items: [{ cardId: 'burn', slot: 0 }] },
      playerMaxHp: 5,
      enemyMaxHp: 100,
      seed: 1,
      rules: {},
      maxTime: 2,
    });
    expect(r.events.some((event) => event.kind === 'death')).toBe(true);
    expect(r.winner).toBe('enemy');
    expect(r.endTime).toBe(1);
  });
});
