import { describe, it, expect } from 'vitest';
import { simulate } from '../battle';
import type { CardDef, Effect } from '../types';

const card = (
  id: string,
  cooldown: number,
  effects: Effect[],
  startReady = false,
): CardDef => ({
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
  startReady,
});

const setup = (mod: Effect) => {
  const dmg = card('dmg', 4, [
    { kind: 'damage', amount: { type: 'const', value: 1 }, target: { kind: 'enemyHero' } },
  ]);
  const modCard = card('mod', 10, [mod], true);
  return {
    cards: { dmg, mod: modCard },
    player: { items: [{ cardId: 'mod', slot: 0 }, { cardId: 'dmg', slot: 1 }] },
    enemy: { items: [] },
    playerMaxHp: 100,
    enemyMaxHp: 100,
    seed: 1,
    rules: {},
    maxTime: 12,
  };
};

const firstHit = (r: any) => +r.events.find((e: any) => e.kind === 'damage')!.time.toFixed(1);

describe('cooldown modifiers', () => {
  it('haste doubles the rate for its duration', () => {
    expect(
      firstHit(simulate(setup({ kind: 'haste', seconds: 2, target: { kind: 'selfItem', itemId: 'dmg' } }) as any)),
    ).toBe(2);
  });

  it('slow halves the rate for its duration', () => {
    expect(
      firstHit(simulate(setup({ kind: 'slow', seconds: 2, target: { kind: 'selfItem', itemId: 'dmg' } }) as any)),
    ).toBe(5);
  });

  it('freeze pauses the cooldown', () => {
    expect(
      firstHit(simulate(setup({ kind: 'freeze', seconds: 2, target: { kind: 'selfItem', itemId: 'dmg' } }) as any)),
    ).toBe(6);
  });

  it('charge removes seconds immediately', () => {
    expect(
      firstHit(simulate(setup({ kind: 'charge', seconds: 2, target: { kind: 'selfItem', itemId: 'dmg' } }) as any)),
    ).toBe(2);
  });
});
