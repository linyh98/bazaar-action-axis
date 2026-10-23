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
  maxTime = 1,
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

describe('chain ids', () => {
  it('does not give a DoT tick the next activation chain', () => {
    const burn = card(
      'burn',
      99,
      [{ kind: 'burn', amount: { type: 'const', value: 1 }, target: { kind: 'enemyHero' } }],
      [],
      { startReady: true },
    );
    const hit = card('hit', 1, [damage(1)]);
    const r = simulate(cfg({ burn, hit }, ['burn', 'hit'], [], 1));
    const dot = r.events.find((event) => event.kind === 'dotTick' && event.note === 'burn')!;
    const activate = r.events.find(
      (event) => event.kind === 'activate' && event.sourceItemId === 'hit',
    )!;
    expect(dot.chainId).not.toBe(activate.chainId);
  });

  it('keeps a triggered effect in the same chain as the triggering damage', () => {
    const a = card('a', 1, [damage(1)]);
    const b = card(
      'b',
      99,
      [],
      [
        {
          event: 'onDamageDealt',
          effects: [
            { kind: 'shield', amount: { type: 'const', value: 1 }, target: { kind: 'selfHero' } },
          ],
        },
      ],
      { startReady: true },
    );
    const r = simulate(cfg({ a, b }, ['a', 'b'], [], 1));
    const hit = r.events.find((event) => event.kind === 'damage')!;
    const shield = r.events.find((event) => event.kind === 'shield')!;
    expect(hit.chainId).toBe(shield.chainId);
  });
});
