import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import { TotalsPanel } from '../TotalsPanel';

describe('TotalsPanel', () => {
  it('shows both sides and dps', () => {
    render(
      <TotalsPanel
        endTime={10}
        totals={{
          player: { damage: 100, effectiveDamage: 80, heal: 20, shield: 30 },
          enemy: { damage: 50, effectiveDamage: 40, heal: 10, shield: 5 },
        }}
      />,
    );
    expect(screen.getByText('总伤害')).toBeTruthy();
    expect(screen.getByText('100')).toBeTruthy();
    expect(screen.getByText('8.0')).toBeTruthy();
  });
});
