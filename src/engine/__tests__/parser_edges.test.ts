import { describe, it, expect } from 'vitest';
import { parseTooltips } from '../parse';

describe('parser edge cases', () => {
  it('does not parse a conditional tail as a fixed amount', () => {
    const r = parseTooltips(['Deal 50 Damage for each Weapon you have']);
    expect(r.effects).toHaveLength(0);
    expect(r.unparsed).toHaveLength(1);
  });

  it('treats +N Multicast as extra casts', () => {
    const r = parseTooltips(['This has +1 Multicast']);
    expect(r.effects).toEqual([{ kind: 'multicast', count: 2 }]);
  });

  it('does not parse conditional +N Multicast', () => {
    const r = parseTooltips(['This has +1 Multicast for each adjacent Potion']);
    expect(r.effects).toHaveLength(0);
    expect(r.unparsed).toHaveLength(1);
  });

  it('marks "your Max Health" as source-based', () => {
    const r = parseTooltips(['Deal Damage equal to 20% of your Max Health']);
    expect(r.effects[0]).toEqual({
      kind: 'damage',
      amount: { type: 'percentMaxHp', value: 20, basis: 'source' },
      target: { kind: 'enemyHero' },
    });
  });

  it('keeps "enemy Max Health" target-based', () => {
    const r = parseTooltips(["Deal Damage equal to 20% of an enemy's Max Health"]);
    expect(r.effects[0]).toEqual({
      kind: 'damage',
      amount: { type: 'percentMaxHp', value: 20 },
      target: { kind: 'enemyHero' },
    });
  });
});
