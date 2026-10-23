import { describe, it, expect } from 'vitest';
import { simulate } from '../battle';

describe('simulate error handling', () => {
  it('returns a warning result instead of throwing on malformed data', () => {
    const bad = {
      id: 'bad',
      name: 'bad',
      size: 'Small',
      tier: 'Bronze',
      cooldown: 1,
      effects: [],
      triggers: [null],
      tags: [],
      parseStatus: 'structured',
      rawTooltips: [],
    } as any;

    const r = simulate({
      cards: { bad },
      player: { items: [{ cardId: 'bad', slot: 0 }] },
      enemy: { items: [] },
      playerMaxHp: 100,
      enemyMaxHp: 100,
      seed: 1,
      rules: {},
      maxTime: 1,
    });

    expect(r.warnings.length).toBeGreaterThan(0);
    expect(r.snapshots.length).toBeGreaterThan(0);
    expect(r.winner).toBe('draw');
  });
});
