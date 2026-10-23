import { describe, it, expect } from 'vitest';
import { simulate } from '../battle';
import type { CardDef, Effect } from '../types';

const card = (
  id: string,
  cooldown: number,
  effects: Effect[],
  extra: Partial<CardDef> = {},
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
  ...extra,
});

const cfg = (
  cards: Record<string, CardDef>,
  player: string[],
  enemy: string[],
  maxTime = 60,
  hp = 100,
) => ({
  cards,
  player: { items: player.map((cid, i) => ({ cardId: cid, slot: i })) },
  enemy: { items: enemy.map((cid, i) => ({ cardId: cid, slot: i })) },
  playerMaxHp: hp,
  enemyMaxHp: hp,
  seed: 1,
  rules: {},
  maxTime,
});

describe('battle result', () => {
  it('ends immediately on death', () => {
    const d = card('d', 1, [
      { kind: 'damage', amount: { type: 'const', value: 10 }, target: { kind: 'enemyHero' } },
    ]);
    const r = simulate(cfg({ d }, ['d'], [], 60, 10) as any);
    expect(r.winner).toBe('player');
    expect(r.endTime).toBe(1);
    expect(r.events.some((e) => e.kind === 'death')).toBe(true);
  });

  it('decides a timeout by hp percentage', () => {
    const d = card('d', 1, [
      { kind: 'damage', amount: { type: 'const', value: 5 }, target: { kind: 'enemyHero' } },
    ]);
    const r = simulate(cfg({ d }, ['d'], [], 2, 100) as any);
    expect(r.winner).toBe('player');
    expect(r.endTime).toBe(2);
  });

  it('totals match event sums', () => {
    const d = card('d', 1, [
      { kind: 'damage', amount: { type: 'const', value: 10 }, target: { kind: 'enemyHero' } },
    ]);
    const r = simulate(cfg({ d }, ['d'], [], 3) as any);
    const sum = r.events
      .filter((e) => e.kind === 'damage')
      .reduce((acc, e) => acc + (e.value ?? 0), 0);
    expect(r.totals.player.damage).toBe(sum);
  });

  it('is deterministic for the same seed', () => {
    const d = card('d', 1, [
      { kind: 'damage', amount: { type: 'const', value: 10 }, target: { kind: 'enemyHero' } },
    ]);
    const a = simulate(cfg({ d }, ['d'], [], 5) as any);
    const b = simulate(cfg({ d }, ['d'], [], 5) as any);
    expect(JSON.stringify(a.events)).toBe(JSON.stringify(b.events));
  });
});
