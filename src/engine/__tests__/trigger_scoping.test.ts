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

describe('trigger scoping', () => {
  it('does not fire the player onHeal trigger when the enemy heals', () => {
    const b = card(
      'b',
      99,
      [],
      [
        {
          event: 'onHeal',
          effects: [
            { kind: 'shield', amount: { type: 'const', value: 5 }, target: { kind: 'selfHero' } },
          ],
        },
      ],
      { startReady: true },
    );
    const e = card(
      'e',
      99,
      [{ kind: 'heal', amount: { type: 'const', value: 10 }, target: { kind: 'selfHero' } }],
      [],
      { startReady: true },
    );
    const r = simulate(cfg({ b, e }, ['b'], ['e'], 1) as any);
    expect(r.snapshots.at(-1)!.player.shield).toBe(0);
  });

  it('only fires "when this is hasted" on the hasted item', () => {
    const h = card(
      'h',
      99,
      [{ kind: 'haste', seconds: 2, target: { kind: 'selfItem', itemId: 'b' } }],
      [],
      { startReady: true },
    );
    const b = card(
      'b',
      10,
      [],
      [
        {
          event: 'onHaste',
          filter: { self: true },
          effects: [
            { kind: 'shield', amount: { type: 'const', value: 5 }, target: { kind: 'selfHero' } },
          ],
        },
      ],
    );
    const c = card(
      'c',
      10,
      [],
      [
        {
          event: 'onHaste',
          filter: { self: true },
          effects: [
            { kind: 'shield', amount: { type: 'const', value: 7 }, target: { kind: 'selfHero' } },
          ],
        },
      ],
    );
    const r = simulate(cfg({ h, b, c }, ['h', 'b', 'c'], [], 1) as any);
    expect(r.snapshots.at(-1)!.player.shield).toBe(5);
  });
});
