import { describe, it, expect } from 'vitest';
import { findCard, mapBpp, mapOcr } from '../../../tools/live_cards_map';

const cards = [
  { id: 'a@Bronze', baseItemId: 'a', tier: 'Bronze', name: 'A' },
  { id: 'a@Silver', baseItemId: 'a', tier: 'Silver', name: 'A' },
  { id: 'b@Bronze', baseItemId: 'b', tier: 'Bronze', name: 'B', nameCn: '乙' },
];

describe('live card mapping', () => {
  it('prefers the exact tier', () => {
    expect(findCard(cards, 'a', 'Silver')!.id).toBe('a@Silver');
  });

  it('falls back to the lowest tier', () => {
    expect(findCard(cards, 'a', 'Diamond')!.id).toBe('a@Bronze');
  });

  it('maps a BPP shop dump', () => {
    const result = mapBpp(
      {
        hero: 'Dooley',
        shop: [{ templateId: 'a', tier: 'Silver', order: 0 }],
        board: [],
        stash: [],
      },
      cards,
    );
    expect(result.source).toBe('bpp');
    expect(result.cards[0].cardId).toBe('a@Silver');
    expect(result.cards[0].found).toBe(true);
  });

  it('maps OCR by matched id, then by name', () => {
    const result = mapOcr(
      {
        cards: [
          { slot: 1, name: '乙', matchedId: 'b' },
          { slot: 2, name: '龙心吊坠', matchedId: null },
        ],
      },
      cards,
    );
    expect(result.cards[0].cardId).toBe('b@Bronze');
    expect(result.cards[1].cardId).toBeNull();
    expect(result.cards[1].name).toBe('龙心吊坠');
  });
});
