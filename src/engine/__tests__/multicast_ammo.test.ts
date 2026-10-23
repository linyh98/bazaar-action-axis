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
  enemy: string[] = [],
  maxTime = 8,
) => ({
  cards,
  player: { items: player.map((cid, i) => ({ cardId: cid, slot: i })) },
  enemy: { items: enemy.map((cid, i) => ({ cardId: cid, slot: i })) },
  playerMaxHp: 100,
  enemyMaxHp: 100,
  seed: 1,
  rules: {},
  maxTime,
});

describe('multicast and ammo', () => {
  it('multicast fires the effect multiple times', () => {
    const c = card('m', 1, [
      { kind: 'multicast', count: 2 },
      { kind: 'damage', amount: { type: 'const', value: 10 }, target: { kind: 'enemyHero' } },
    ]);
    const r = simulate(cfg({ m: c }, ['m'], [], 1) as any);
    expect(r.events.filter((e) => e.kind === 'damage').length).toBe(2);
    expect(r.snapshots.at(-1)!.enemy.hp).toBe(80);
  });

  it('ammo blocks activation until reloaded', () => {
    const a = card(
      'a',
      1,
      [{ kind: 'damage', amount: { type: 'const', value: 5 }, target: { kind: 'enemyHero' } }],
      { ammoMax: 2 },
    );
    const rl = card('rl', 5, [
      { kind: 'reload', amount: 1, target: { kind: 'selfItem', itemId: 'a' } },
    ]);
    const r = simulate(cfg({ a, rl }, ['a', 'rl'], [], 8) as any);
    const hits = r.events.filter((e) => e.kind === 'damage' && e.sourceItemId === 'a');
    expect(hits.length).toBeGreaterThanOrEqual(3);
    expect(hits.every((h) => h.time <= 8)).toBe(true);
  });
});
