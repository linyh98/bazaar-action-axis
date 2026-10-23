import { describe, it, expect } from 'vitest';
import { simulate } from '../battle';
import type { CardDef, Effect } from '../types';

const card = (id: string, cooldown: number, effects: Effect[], extra: Partial<CardDef> = {}): CardDef => ({
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
  maxTime = 3,
) => ({
  cards,
  player: { items: player.map((id, i) => ({ cardId: id, slot: i })) },
  enemy: { items: enemy.map((id, i) => ({ cardId: id, slot: i })) },
  playerMaxHp: 100,
  enemyMaxHp: 100,
  seed: 1,
  rules: {},
  maxTime,
});

describe('damage over time', () => {
  it('burn ticks and decays by 1 each second', () => {
    const r = simulate(
      cfg(
        {
          b: card(
            'b',
            99,
            [
              { kind: 'burn', amount: { type: 'const', value: 3 }, target: { kind: 'enemyHero' } },
            ],
            { startReady: true },
          ),
        },
        ['b'],
      ) as any,
    );
    const ticks = r.events.filter((e) => e.kind === 'dotTick' && e.note === 'burn');
    expect(ticks.map((e) => e.value)).toEqual([3, 2, 1]);
  });

  it('poison ticks without decaying', () => {
    const r = simulate(
      cfg(
        {
          p: card(
            'p',
            99,
            [
              { kind: 'poison', amount: { type: 'const', value: 3 }, target: { kind: 'enemyHero' } },
            ],
            { startReady: true },
          ),
        },
        ['p'],
      ) as any,
    );
    const ticks = r.events.filter((e) => e.kind === 'dotTick' && e.note === 'poison');
    expect(ticks.map((e) => e.value)).toEqual([3, 3, 3]);
  });

  it('regen heals each second', () => {
    const r = simulate(
      cfg(
        {
          r: card(
            'r',
            99,
            [
              { kind: 'regen', amount: { type: 'const', value: 2 }, target: { kind: 'selfHero' } },
            ],
            { startReady: true },
          ),
        },
        ['r'],
      ) as any,
    );
    expect(r.events.filter((e) => e.kind === 'dotTick' && e.note === 'regen').length).toBeGreaterThan(0);
  });

  it('burn hits shield first by default', () => {
    const r = simulate(
      cfg(
        {
          b: card(
            'b',
            99,
            [
              { kind: 'burn', amount: { type: 'const', value: 3 }, target: { kind: 'enemyHero' } },
            ],
            { startReady: true },
          ),
          s: card(
            's',
            99,
            [
              { kind: 'shield', amount: { type: 'const', value: 10 }, target: { kind: 'selfHero' } },
            ],
            { startReady: true },
          ),
        },
        ['b'],
        ['s'],
      ) as any,
    );
    const tick = r.events.find((e) => e.kind === 'dotTick' && e.note === 'burn')!;
    expect(tick.absorbed).toBeGreaterThan(0);
  });
});
