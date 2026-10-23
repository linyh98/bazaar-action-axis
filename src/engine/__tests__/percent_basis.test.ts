import { describe, it, expect } from 'vitest';
import { simulate } from '../battle';
import type { CardDef, Effect } from '../types';

const card = (effects: Effect[]): CardDef => ({
  id: 'p',
  name: 'p',
  size: 'Small',
  tier: 'Bronze',
  cooldown: 1,
  effects,
  triggers: [],
  tags: [],
  parseStatus: 'structured',
  rawTooltips: [],
});

const cfg = (effect: Effect) => ({
  cards: { p: card([effect]) },
  player: { items: [{ cardId: 'p', slot: 0 }] },
  enemy: { items: [] },
  playerMaxHp: 200,
  enemyMaxHp: 50,
  seed: 1,
  rules: {},
  maxTime: 1,
});

describe('percent max hp basis', () => {
  it('uses the source max hp for "your" percent damage', () => {
    const r = simulate(
      cfg({
        kind: 'damage',
        amount: { type: 'percentMaxHp', value: 10, basis: 'source' },
        target: { kind: 'enemyHero' },
      }),
    );
    expect(r.snapshots.at(-1)!.enemy.hp).toBe(30);
  });

  it('uses the target max hp for enemy percent damage', () => {
    const r = simulate(
      cfg({
        kind: 'damage',
        amount: { type: 'percentMaxHp', value: 10 },
        target: { kind: 'enemyHero' },
      }),
    );
    expect(r.snapshots.at(-1)!.enemy.hp).toBe(45);
  });
});
