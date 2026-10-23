import { describe, it, expect } from 'vitest';
import { simulate } from '../battle';
import type { CardDef } from '../types';

const card: CardDef = {
  id: 'c',
  name: 'c',
  size: 'Small',
  tier: 'Bronze',
  cooldown: 5,
  effects: [{ kind: 'charge', seconds: 3, target: { kind: 'selfItem', itemId: 'c' } }],
  triggers: [],
  tags: [],
  parseStatus: 'structured',
  rawTooltips: [],
};

describe('charge self', () => {
  it('applies self-charge after the cooldown reset', () => {
    const r = simulate({
      cards: { c: card },
      player: { items: [{ cardId: 'c', slot: 0 }] },
      enemy: { items: [] },
      playerMaxHp: 100,
      enemyMaxHp: 100,
      seed: 1,
      rules: {},
      maxTime: 12,
    });
    const times = r.events
      .filter((event) => event.kind === 'activate' && event.sourceItemId === 'c')
      .map((event) => +event.time.toFixed(1));
    expect(times).toEqual([5, 7, 9, 11]);
  });
});
