import { describe, it, expect } from 'vitest';
import { simulate } from '../battle';
import type { CardDef, Effect, TriggerDef } from '../types';

const card = (
  id: string,
  triggers: TriggerDef[],
): CardDef => ({
  id,
  name: id,
  size: 'Small',
  tier: 'Bronze',
  cooldown: 99,
  effects: [],
  triggers,
  tags: [],
  parseStatus: 'structured',
  rawTooltips: [],
});

const damage = (value: number): Effect => ({
  kind: 'damage',
  amount: { type: 'const', value },
  target: { kind: 'enemyHero' },
});

const cfg = (cards: Record<string, CardDef>, player: string[], maxTime: number) => ({
  cards,
  player: { items: player.map((cid, i) => ({ cardId: cid, slot: i })) },
  enemy: { items: [] },
  playerMaxHp: 100,
  enemyMaxHp: 100,
  seed: 1,
  rules: {},
  maxTime,
});

describe('everySecond triggers', () => {
  it('fires each card once per second, not once per (card × card)', () => {
    const a = card('a', [
      { event: 'everySecond', everySeconds: 1, effects: [damage(1)] },
    ]);
    const b = card('b', [
      { event: 'everySecond', everySeconds: 1, effects: [damage(1)] },
    ]);
    const r = simulate(cfg({ a, b }, ['a', 'b'], 2));
    expect(r.events.filter((event) => event.kind === 'damage')).toHaveLength(4);
  });

  it('honors each trigger interval independently', () => {
    const fast = card('fast', [
      { event: 'everySecond', everySeconds: 1, effects: [damage(1)] },
    ]);
    const slow = card('slow', [
      { event: 'everySecond', everySeconds: 3, effects: [damage(10)] },
    ]);
    const r = simulate(cfg({ fast, slow }, ['fast', 'slow'], 6));
    expect(
      r.events.filter((event) => event.kind === 'damage' && event.sourceItemId === 'fast'),
    ).toHaveLength(6);
    expect(
      r.events.filter((event) => event.kind === 'damage' && event.sourceItemId === 'slow'),
    ).toHaveLength(2);
  });
});
