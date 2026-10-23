import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import { DetailPanel } from '../DetailPanel';
import type { BattleEvent } from '../../engine/types';

const e = (
  id: number,
  chainId: number,
  kind: any = 'damage',
  sourceItemId = 'a',
): BattleEvent => ({
  id,
  time: id,
  side: 'player',
  sourceItemId,
  kind,
  chainId,
  depth: 0,
  value: id * 10,
});

describe('DetailPanel', () => {
  it('shows the full chain of the selected event', () => {
    render(
      <DetailPanel
        events={[e(1, 7), e(2, 7), e(3, 8)]}
        cards={{}}
        selectedEventId={1}
        selectedItemId={null}
      />,
    );
    expect(screen.getByText(/事件 #1/)).toBeTruthy();
    expect(screen.getByText(/事件 #2/)).toBeTruthy();
    expect(screen.queryByText(/事件 #3/)).toBeNull();
  });

  it('filters by selected item', () => {
    render(
      <DetailPanel
        events={[e(1, 7, 'damage', 'a'), e(2, 8, 'damage', 'b')]}
        cards={{}}
        selectedEventId={null}
        selectedItemId="a"
      />,
    );
    expect(screen.getByText(/事件 #1/)).toBeTruthy();
    expect(screen.queryByText(/事件 #2/)).toBeNull();
  });
});
