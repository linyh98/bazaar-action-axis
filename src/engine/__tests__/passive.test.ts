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

describe('passive cards', () => {
  it('does not activate a zero-cooldown passive card every tick', () => {
    const p = card(
      'p',
      0,
      [{ kind: 'shield', amount: { type: 'const', value: 10 }, target: { kind: 'selfHero' } }],
      { passive: true },
    );
    const r = simulate({
      cards: { p },
      player: { items: [{ cardId: 'p', slot: 0 }] },
      enemy: { items: [] },
      playerMaxHp: 100,
      enemyMaxHp: 100,
      seed: 1,
      rules: {},
      maxTime: 60,
    });
    expect(r.events.filter((event) => event.kind === 'activate')).toHaveLength(0);
    expect(r.snapshots.at(-1)!.player.shield).toBe(0);
  });

  it('still activates a normal card with a positive cooldown', () => {
    const c = card('c', 2, [
      { kind: 'shield', amount: { type: 'const', value: 10 }, target: { kind: 'selfHero' } },
    ]);
    const r = simulate({
      cards: { c },
      player: { items: [{ cardId: 'c', slot: 0 }] },
      enemy: { items: [] },
      playerMaxHp: 100,
      enemyMaxHp: 100,
      seed: 1,
      rules: {},
      maxTime: 4,
    });
    expect(r.events.filter((event) => event.kind === 'activate')).toHaveLength(2);
  });
});
