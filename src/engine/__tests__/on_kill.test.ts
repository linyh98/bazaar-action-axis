import { describe, it, expect } from 'vitest';
import { simulate } from '../battle';
import type { CardDef, Effect, TriggerDef } from '../types';

const card = (
  id: string,
  cooldown: number,
  effects: Effect[],
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

describe('onKill triggers', () => {
  it('fires the killer side onKill effect after the killing blow', () => {
    const killer = card('k', 1, [
      { kind: 'damage', amount: { type: 'const', value: 10 }, target: { kind: 'enemyHero' } },
    ]);
    const onKill = card(
      'ok',
      99,
      [],
      [
        {
          event: 'onKill',
          effects: [
            { kind: 'shield', amount: { type: 'const', value: 5 }, target: { kind: 'selfHero' } },
          ],
        },
      ],
      { startReady: true },
    );
    const r = simulate({
      cards: { k: killer, ok: onKill },
      player: {
        items: [
          { cardId: 'k', slot: 0 },
          { cardId: 'ok', slot: 1 },
        ],
      },
      enemy: { items: [] },
      playerMaxHp: 100,
      enemyMaxHp: 10,
      seed: 1,
      rules: {},
      maxTime: 1,
    });
    expect(r.events.some((event) => event.kind === 'death')).toBe(true);
    expect(r.snapshots.at(-1)!.player.shield).toBe(5);
  });
});
