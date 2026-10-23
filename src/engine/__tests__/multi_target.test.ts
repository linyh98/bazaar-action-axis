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
  maxTime: number,
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

const damage = (value = 1): Effect => ({
  kind: 'damage',
  amount: { type: 'const', value },
  target: { kind: 'enemyHero' },
});

describe('multi-target effects', () => {
  it('freezes every enemy item for allEnemyItems', () => {
    const f = card(
      'f',
      99,
      [{ kind: 'freeze', seconds: 5, target: { kind: 'allEnemyItems' } }],
      { startReady: true },
    );
    const e1 = card('e1', 1, [damage()]);
    const e2 = card('e2', 1, [damage()]);
    const r = simulate(cfg({ f, e1, e2 }, ['f'], ['e1', 'e2'], 1));
    expect(
      r.events.filter((event) => event.kind === 'activate' && event.side === 'enemy'),
    ).toHaveLength(0);
  });

  it('haste two distinct random enemy items', () => {
    const h = card(
      'h',
      99,
      [{ kind: 'haste', seconds: 2, target: { kind: 'randomEnemyItem' }, count: 2 }],
      { startReady: true },
    );
    const e1 = card('e1', 4, [damage()]);
    const e2 = card('e2', 4, [damage()]);
    const e3 = card('e3', 4, [damage()]);
    const r = simulate(cfg({ h, e1, e2, e3 }, ['h'], ['e1', 'e2', 'e3'], 2));
    expect(
      r.events.filter(
        (event) =>
          event.kind === 'activate' &&
          event.side === 'enemy' &&
          Math.abs(event.time - 2) < 1e-6,
      ),
    ).toHaveLength(2);
  });

  it('hastes both adjacent items', () => {
    const a = card('a', 4, [damage()]);
    const h = card(
      'h',
      99,
      [{ kind: 'haste', seconds: 2, target: { kind: 'adjacentSelf' } }],
      { startReady: true },
    );
    const c = card('c', 4, [damage()]);
    const r = simulate(cfg({ a, h, c }, ['a', 'h', 'c'], [], 2));
    expect(
      r.events.filter(
        (event) =>
          event.kind === 'activate' &&
          event.side === 'player' &&
          Math.abs(event.time - 2) < 1e-6,
      ),
    ).toHaveLength(2);
  });
});
