import { describe, it, expect } from 'vitest';
import { simulate } from '../battle';
import type { CardDef } from '../types';

const card = (id: string, cooldown: number, extra: Partial<CardDef> = {}): CardDef => ({
  id,
  name: id,
  size: 'Small',
  tier: 'Bronze',
  cooldown,
  effects: [],
  triggers: [],
  tags: [],
  parseStatus: 'structured',
  rawTooltips: [],
  ...extra,
});

const config = (cards: Record<string, CardDef>, overrides: Record<string, unknown> = {}) => ({
  cards,
  player: { items: [{ cardId: 'a', slot: 0 }] },
  enemy: { items: [{ cardId: 'b', slot: 0 }] },
  playerMaxHp: 100,
  enemyMaxHp: 100,
  seed: 1,
  rules: {},
  maxTime: 13,
  ...overrides,
});

describe('cooldown tick', () => {
  it('activates on cooldown boundaries', () => {
    const r = simulate(config({ a: card('a', 4), b: card('b', 6) }) as any);
    const times = (id: string) =>
      r.events
        .filter((e) => e.kind === 'activate' && e.sourceItemId === id)
        .map((e) => +e.time.toFixed(1));
    expect(times('a')).toEqual([4, 8, 12]);
    expect(times('b')).toEqual([6, 12]);
  });

  it('activates player before enemy on the same tick', () => {
    const r = simulate(config({ a: card('a', 12), b: card('b', 12) }) as any);
    const at12 = r.events.filter(
      (e) => e.kind === 'activate' && Math.abs(e.time - 12) < 1e-6,
    );
    expect(at12.map((e) => e.side)).toEqual(['player', 'enemy']);
  });

  it('snapshots every tick', () => {
    const r = simulate(config({ a: card('a', 4), b: card('b', 6) }) as any);
    expect(r.snapshots).toHaveLength(131);
  });

  it('handles an empty loadout', () => {
    const r = simulate(
      config({ a: card('a', 4) }, { enemy: { items: [] } }) as any,
    );
    expect(r.events.some((e) => e.kind === 'activate' && e.side === 'player')).toBe(true);
    expect(r.winner).toBe('player');
  });
});
