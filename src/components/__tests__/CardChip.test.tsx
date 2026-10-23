import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import { CardChip } from '../CardChip';
import type { CardDef } from '../../engine/types';

const card: CardDef = {
  id: 'a@Bronze',
  baseItemId: 'a',
  name: 'A',
  image: 'https://example.com/a.avif',
  size: 'Small',
  tier: 'Bronze',
  cooldown: 4,
  effects: [],
  triggers: [],
  tags: [],
  parseStatus: 'structured',
  rawTooltips: [],
};

describe('CardChip', () => {
  it('renders a card image when the card has one', () => {
    render(<CardChip card={card} cooldown={1} selected={false} onClick={() => {}} />);
    expect(screen.getByRole('img')).toBeTruthy();
  });
});
