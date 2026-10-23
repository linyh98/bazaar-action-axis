import { describe, it, expect } from 'vitest';
import { createRng } from '../rng';

describe('rng', () => {
  it('is deterministic for the same seed', () => {
    const a = createRng(42);
    const b = createRng(42);
    expect([a.next(), a.next(), a.next()]).toEqual([b.next(), b.next(), b.next()]);
  });
  it('chance bounds are exact', () => {
    expect(createRng(1).chance(0)).toBe(false);
    expect(createRng(1).chance(1)).toBe(true);
  });
  it('int stays in range', () => {
    const rng = createRng(7);
    for (let i = 0; i < 100; i++) {
      const n = rng.int(5);
      expect(n).toBeGreaterThanOrEqual(0);
      expect(n).toBeLessThan(5);
    }
  });
});
