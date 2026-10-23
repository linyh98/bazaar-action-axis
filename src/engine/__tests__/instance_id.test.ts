import { describe, it, expect } from 'vitest';
import { simulate } from '../battle';
import type { CardDef, Effect } from '../types';

const card = (id: string, cooldown: number, effects: Effect[]): CardDef => ({
  id,
  name: id,
  size: 'Small',
  tier: 'Bronze',
  cooldown,
  effects,
  triggers: [],
  tags: [],
  parseStatus: 'structured',
  rawTooltips: [],
});

const damage: Effect = {
  kind: 'damage',
  amount: { type: 'const', value: 1 },
  target: { kind: 'enemyHero' },
};

describe('card instance identity', () => {
  it('distinguishes the same card on both sides', () => {
    const dup = card('dup', 1, [damage]);
    const r = simulate({
      cards: { dup },
      player: { items: [{ cardId: 'dup', slot: 0 }] },
      enemy: { items: [{ cardId: 'dup', slot: 0 }] },
      playerMaxHp: 100,
      enemyMaxHp: 100,
      seed: 1,
      rules: {},
      maxTime: 1,
    });
    const ids = r.events
      .filter((event) => event.kind === 'activate')
      .map((event) => event.sourceItemId);
    expect(new Set(ids).size).toBe(2);
    expect(ids).toContain('player:0:dup');
    expect(ids).toContain('enemy:0:dup');
  });

  it('distinguishes duplicate copies on one side', () => {
    const dup = card('dup', 1, [damage]);
    const r = simulate({
      cards: { dup },
      player: {
        items: [
          { cardId: 'dup', slot: 0 },
          { cardId: 'dup', slot: 1 },
        ],
      },
      enemy: { items: [] },
      playerMaxHp: 100,
      enemyMaxHp: 100,
      seed: 1,
      rules: {},
      maxTime: 1,
    });
    const ids = r.events
      .filter((event) => event.kind === 'activate')
      .map((event) => event.sourceItemId);
    expect(new Set(ids).size).toBe(2);
    expect(ids).toContain('player:0:dup');
    expect(ids).toContain('player:1:dup');
  });
});
