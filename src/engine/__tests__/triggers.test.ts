import { describe, it, expect } from 'vitest';
import { simulate } from '../battle';
import type { CardDef, Effect, TriggerDef } from '../types';

const card = (
  id: string,
  cooldown: number,
  effects: Effect[] = [],
  triggers: TriggerDef[] = [],
  extra: Partial<CardDef> = {},
): CardDef => ({
  id,
  name: id,
  size: 'Small',
  tier: 'Bronze',
  cooldown,
  effects,
  triggers,
  tags: [],
  parseStatus: 'structured',
  rawTooltips: [],
  ...extra,
});

const cfg = (
  cards: Record<string, CardDef>,
  player: string[],
  enemy: string[] = [],
  maxTime = 2,
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

describe('triggers', () => {
  it('fires onDamageDealt', () => {
    const a = card('a', 1, [
      { kind: 'damage', amount: { type: 'const', value: 10 }, target: { kind: 'enemyHero' } },
    ]);
    const b = card(
      'b',
      99,
      [],
      [
        {
          event: 'onDamageDealt',
          effects: [
            { kind: 'shield', amount: { type: 'const', value: 5 }, target: { kind: 'selfHero' } },
          ],
        },
      ],
      { startReady: true },
    );
    const r = simulate(cfg({ a, b }, ['a', 'b'], [], 1) as any);
    expect(r.snapshots.at(-1)!.player.shield).toBe(5);
  });

  it('fires atCombatStart once', () => {
    const b = card(
      'b',
      99,
      [],
      [
        {
          event: 'atCombatStart',
          effects: [
            { kind: 'shield', amount: { type: 'const', value: 7 }, target: { kind: 'selfHero' } },
          ],
        },
      ],
      { startReady: true },
    );
    const r = simulate(cfg({ b }, ['b'], [], 1) as any);
    expect(r.events.filter((e) => e.kind === 'shield').length).toBe(1);
  });

  it('caps a self-trigger loop', () => {
    const loop = card(
      'l',
      1,
      [
        {
          kind: 'damage',
          amount: { type: 'const', value: 1 },
          target: { kind: 'enemyHero' },
        },
      ],
      [
        {
          event: 'onDamageDealt',
          effects: [
            {
              kind: 'damage',
              amount: { type: 'const', value: 1 },
              target: { kind: 'enemyHero' },
            },
          ],
        },
      ],
      { startReady: true },
    );
    const r = simulate(cfg({ l: loop }, ['l'], [], 0.2) as any);
    expect(r.warnings.some((w) => w.includes('depth'))).toBe(true);
  });

  it('does not activate newly-charged items in the same tick', () => {
    const charger = card(
      'c',
      99,
      [{ kind: 'charge', seconds: 1, target: { kind: 'selfItem', itemId: 'b' } }],
      [],
      { startReady: true },
    );
    const b = card('b', 1, [
      { kind: 'damage', amount: { type: 'const', value: 3 }, target: { kind: 'enemyHero' } },
    ]);
    const r = simulate(cfg({ c: charger, b }, ['c', 'b'], [], 0.3) as any);
    const firstB = r.events.find(
      (e) => e.kind === 'activate' && e.sourceItemId === 'b',
    )!;
    expect(firstB.time).toBeGreaterThan(0);
  });
});
