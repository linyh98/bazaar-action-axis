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

const crit: Effect = { kind: 'crit', chance: 1 };

describe('critical heal and shield', () => {
  it('crit doubles a heal', () => {
    const enemy = card(
      'e',
      99,
      [{ kind: 'damage', amount: { type: 'const', value: 60 }, target: { kind: 'enemyHero' } }],
      { startReady: true },
    );
    const heal = card('h', 1, [
      crit,
      { kind: 'heal', amount: { type: 'const', value: 10 }, target: { kind: 'selfHero' } },
    ]);
    const r = simulate(cfg({ e: enemy, h: heal }, ['h'], ['e'], 1));
    expect(r.snapshots.at(-1)!.player.hp).toBe(60);
  });

  it('crit doubles a shield', () => {
    const shield = card('s', 1, [
      crit,
      { kind: 'shield', amount: { type: 'const', value: 10 }, target: { kind: 'selfHero' } },
    ]);
    const r = simulate(cfg({ s: shield }, ['s'], [], 1));
    expect(r.snapshots.at(-1)!.player.shield).toBe(20);
  });
});
