import { describe, it, expect, beforeEach } from 'vitest';
import { readJson, loadCustomCards } from '../storage';

describe('storage', () => {
  beforeEach(() => localStorage.clear());

  it('falls back and backs up corrupt json', () => {
    localStorage.setItem('bazaar.customCards.v1', '{oops');
    expect(loadCustomCards()).toEqual([]);
    expect(localStorage.getItem('bazaar.customCards.v1.bak')).toBe('{oops');
  });

  it('reads valid json', () => {
    localStorage.setItem('x', '{"a":1}');
    expect(readJson('x', { a: 0 })).toEqual({ a: 1 });
  });
});
