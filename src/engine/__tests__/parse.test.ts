import { describe, it, expect } from 'vitest';
import { parseTooltips } from '../parse';

const dmg = (v: number) => ({
  kind: 'damage',
  amount: { type: 'const', value: v },
  target: { kind: 'enemyHero' },
});

describe('parseTooltips L1/L2', () => {
  it('parses cooldown and damage', () => {
    const r = parseTooltips(['Cooldown 4 seconds', 'Deal 10 Damage']);
    expect(r.cooldown).toBe(4);
    expect(r.effects).toEqual([dmg(10)]);
    expect(r.parseStatus).toBe('structured');
  });

  it('parses percent max health damage', () => {
    const r = parseTooltips([
      'Cooldown 12 seconds',
      "Deal Damage equal to 20% of an enemy's Max Health",
    ]);
    expect(r.effects[0]).toEqual({
      kind: 'damage',
      amount: { type: 'percentMaxHp', value: 20 },
      target: { kind: 'enemyHero' },
    });
  });

  it('parses heal and shield with default self target', () => {
    const r = parseTooltips(['Heal 10', 'Shield 6']);
    expect(r.effects).toEqual([
      { kind: 'heal', amount: { type: 'const', value: 10 }, target: { kind: 'selfHero' } },
      { kind: 'shield', amount: { type: 'const', value: 6 }, target: { kind: 'selfHero' } },
    ]);
  });

  it('parses burn and poison with enemy target', () => {
    const r = parseTooltips(['Burn 3', 'Poison 2']);
    expect(r.effects[0]).toEqual({
      kind: 'burn',
      amount: { type: 'const', value: 3 },
      target: { kind: 'enemyHero' },
    });
    expect(r.effects[1]).toEqual({
      kind: 'poison',
      amount: { type: 'const', value: 2 },
      target: { kind: 'enemyHero' },
    });
  });

  it('parses haste/slow/freeze/charge', () => {
    const r = parseTooltips([
      'Haste 2 item(s) for 1 second(s)',
      'Slow 1 item(s) for 3 second(s)',
      'Freeze an item for 1.5 second(s)',
      'Charge an adjacent Relic 3 second(s)',
    ]);
    expect(r.effects).toEqual([
      { kind: 'haste', seconds: 1, count: 2, target: { kind: 'randomSelfItem' } },
      { kind: 'slow', seconds: 3, count: 1, target: { kind: 'randomEnemyItem' } },
      { kind: 'freeze', seconds: 1.5, count: 1, target: { kind: 'randomEnemyItem' } },
      { kind: 'charge', seconds: 3, count: 1, target: { kind: 'adjacentSelf' } },
    ]);
  });

  it('parses multicast, crit, ammo, reload', () => {
    const r = parseTooltips(['Multicast 2', 'Crit Chance 20%', 'Ammo 2', 'Reload adjacent items']);
    expect(r.effects).toContainEqual({ kind: 'multicast', count: 2 });
    expect(r.effects).toContainEqual({ kind: 'crit', chance: 0.2 });
    expect(r.ammoMax).toBe(2);
    expect(r.effects).toContainEqual({
      kind: 'reload',
      amount: 1,
      target: { kind: 'adjacentSelf' },
    });
  });

  it('keeps unknown lines as unparsed', () => {
    const r = parseTooltips(['Cooldown 4 seconds', 'When you sneeze, do a backflip']);
    expect(r.parseStatus).toBe('partial');
    expect(r.unparsed).toEqual(['When you sneeze, do a backflip']);
  });
});
