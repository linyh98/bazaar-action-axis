import { describe, it, expect } from 'vitest';
import { simulate } from '../battle';
import type { CardDef, Effect } from '../types';

const base = (
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

const cfg = (cards: Record<string, CardDef>, p: string[], e: string[]) => ({
  cards,
  player: { items: p.map((id, i) => ({ cardId: id, slot: i })) },
  enemy: { items: e.map((id, i) => ({ cardId: id, slot: i })) },
  playerMaxHp: 100,
  enemyMaxHp: 100,
  seed: 1,
  rules: {},
  maxTime: 1,
});

describe('damage / heal / shield', () => {
  it('deals damage to the enemy hero', () => {
    const r = simulate(
      cfg(
        {
          d: base('d', 1, [
            { kind: 'damage', amount: { type: 'const', value: 10 }, target: { kind: 'enemyHero' } },
          ]),
        },
        ['d'],
        [],
      ) as any,
    );
    expect(r.snapshots.at(-1)!.enemy.hp).toBe(90);
  });

  it('absorbs with shield before health', () => {
    const r = simulate(
      cfg(
        {
          s: base('s', 0.5, [
            { kind: 'shield', amount: { type: 'const', value: 6 }, target: { kind: 'selfHero' } },
          ]),
          d: base('d', 1, [
            { kind: 'damage', amount: { type: 'const', value: 10 }, target: { kind: 'enemyHero' } },
          ]),
        },
        ['d'],
        ['s'],
      ) as any,
    );
    const hit = r.events.find((e) => e.kind === 'damage')!;
    expect(hit.absorbed).toBe(6);
    expect(r.snapshots.at(-1)!.enemy.hp).toBe(96);
  });

  it('heals without exceeding max hp', () => {
    const r = simulate(
      cfg(
        {
          h: base('h', 1, [
            { kind: 'heal', amount: { type: 'const', value: 10 }, target: { kind: 'selfHero' } },
          ]),
        },
        ['h'],
        [],
      ) as any,
    );
    expect(r.snapshots.at(-1)!.player.hp).toBe(100);
  });

  it('crit doubles the amount', () => {
    const r = simulate(
      cfg(
        {
          d: base('d', 1, [
            { kind: 'crit', chance: 1 },
            { kind: 'damage', amount: { type: 'const', value: 10 }, target: { kind: 'enemyHero' } },
          ]),
        },
        ['d'],
        [],
      ) as any,
    );
    expect(r.snapshots.at(-1)!.enemy.hp).toBe(80);
  });

  it('lifesteal heals by actual hp damage', () => {
    const r = simulate(
      cfg(
        {
          d: base('d', 1, [
            { kind: 'lifesteal', percent: 1 },
            { kind: 'damage', amount: { type: 'const', value: 10 }, target: { kind: 'enemyHero' } },
          ]),
          e: base(
            'e',
            99,
            [
              { kind: 'damage', amount: { type: 'const', value: 10 }, target: { kind: 'enemyHero' } },
            ],
            { startReady: true },
          ),
        },
        ['d'],
        ['e'],
      ) as any,
    );
    expect(r.snapshots.at(-1)!.player.hp).toBe(100);
  });
});
