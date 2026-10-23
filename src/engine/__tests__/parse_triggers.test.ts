import { describe, it, expect } from 'vitest';
import { parseTooltips } from '../parse';

describe('parseTooltips L3 triggers', () => {
  it('parses "when you heal"', () => {
    const r = parseTooltips(['Cooldown 4 seconds', 'When you Heal, Shield 5']);
    expect(r.triggers).toEqual([
      {
        event: 'onHeal',
        effects: [
          { kind: 'shield', amount: { type: 'const', value: 5 }, target: { kind: 'selfHero' } },
        ],
      },
    ]);
    expect(r.parseStatus).toBe('structured');
  });

  it('parses "at the start of combat"', () => {
    const r = parseTooltips(['At the start of combat, Haste 1 item for 2 seconds']);
    expect(r.triggers[0].event).toBe('atCombatStart');
  });

  it('parses "every N seconds"', () => {
    const r = parseTooltips(['Every 2 seconds, Deal 3 Damage']);
    expect(r.triggers[0]).toMatchObject({ event: 'everySecond', everySeconds: 2 });
  });

  it('leaves unparseable trigger bodies in unparsed', () => {
    const r = parseTooltips(['When you sneeze, do a backflip']);
    expect(r.triggers).toEqual([]);
    expect(r.unparsed).toHaveLength(1);
  });
});
