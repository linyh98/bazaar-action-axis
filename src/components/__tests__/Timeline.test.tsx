import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import { Timeline } from '../Timeline';
import type { BattleResult, CardDef } from '../../engine/types';

const card = (id: string, cooldown: number): CardDef => ({
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
});

const result = {
  events: [
    { id: 1, time: 1, side: 'player', sourceItemId: 'a', kind: 'activate', chainId: 1, depth: 0 },
    { id: 2, time: 2, side: 'enemy', sourceItemId: 'b', kind: 'activate', chainId: 2, depth: 0 },
  ],
  snapshots: [
    {
      time: 0,
      player: {
        hp: 100,
        maxHp: 100,
        shield: 0,
        burn: 0,
        poison: 0,
        regen: 0,
        items: [
          {
            itemId: 'a',
            cardId: 'a',
            slot: 0,
            cooldown: 1,
            cooldownMax: 1,
            hasteUntil: 0,
            slowUntil: 0,
            frozenUntil: 0,
            ammo: 0,
          },
        ],
      },
      enemy: {
        hp: 100,
        maxHp: 100,
        shield: 0,
        burn: 0,
        poison: 0,
        regen: 0,
        items: [
          {
            itemId: 'b',
            cardId: 'b',
            slot: 0,
            cooldown: 2,
            cooldownMax: 2,
            hasteUntil: 0,
            slowUntil: 0,
            frozenUntil: 0,
            ammo: 0,
          },
        ],
      },
    },
  ],
  totals: {
    player: { damage: 0, effectiveDamage: 0, heal: 0, shield: 0 },
    enemy: { damage: 0, effectiveDamage: 0, heal: 0, shield: 0 },
  },
  winner: 'draw',
  endTime: 60,
  warnings: [],
} as BattleResult;

const renderTimeline = (overrides: Partial<Parameters<typeof Timeline>[0]> = {}) =>
  render(
    <Timeline
      result={result}
      cards={{ a: card('a', 1), b: card('b', 2) }}
      playhead={0}
      onPlayheadChange={() => {}}
      selectedEventId={null}
      onSelectEvent={() => {}}
      selectedItemId={null}
      onSelectItem={() => {}}
      {...overrides}
    />,
  );

describe('Timeline', () => {
  it('renders four lanes for two items', () => {
    renderTimeline();
    expect(screen.getAllByTestId('timeline-lane')).toHaveLength(4);
  });

  it('reports playhead changes', () => {
    const onChange = vi.fn();
    renderTimeline({ onPlayheadChange: onChange });
    fireEvent.change(screen.getByLabelText('时间轴播放头'), { target: { value: '30' } });
    expect(onChange).toHaveBeenCalledWith(30);
  });

  it('selects a marker', () => {
    const onSelect = vi.fn();
    renderTimeline({ onSelectEvent: onSelect });
    fireEvent.click(screen.getByTestId('event-marker-1'));
    expect(onSelect).toHaveBeenCalledWith(1);
  });
});
