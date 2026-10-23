export interface CardLite {
  id: string;
  baseItemId?: string;
  tier: string;
  name: string;
  nameCn?: string;
}

export interface LiveEntry {
  section: 'shop' | 'board' | 'stash';
  slot: number;
  tier: string | null;
  cardId: string | null;
  name: string;
  found: boolean;
}

export type LiveMapResult =
  | {
      source: 'bpp';
      hero: string | null;
      section: 'shop' | 'board' | 'stash';
      cards: LiveEntry[];
    }
  | { source: 'ocr'; section: 'shop'; cards: LiveEntry[] };

const TIERS = ['Bronze', 'Silver', 'Gold', 'Diamond', 'Legendary'];

export function tierRank(tier: string): number {
  const index = TIERS.indexOf(tier);
  return index < 0 ? 99 : index;
}

export function findCard(
  cards: CardLite[],
  templateId: string,
  tier?: string,
): CardLite | null {
  const id = (templateId || '').toLowerCase();
  if (!id) return null;

  const exact = cards.find(
    (card) => (card.baseItemId || '').toLowerCase() === id && card.tier === tier,
  );
  if (exact) return exact;

  return (
    cards
      .filter((card) => (card.baseItemId || '').toLowerCase() === id)
      .sort((a, b) => tierRank(a.tier) - tierRank(b.tier))[0] ?? null
  );
}

export function mapBpp(
  dump: any,
  cards: CardLite[],
  preferred: 'shop' | 'board' | 'stash' = 'shop',
): LiveMapResult {
  const sections: Array<'shop' | 'board' | 'stash'> = ['shop', 'board', 'stash'];
  const section =
    Array.isArray(dump?.[preferred]) && dump[preferred].length > 0
      ? preferred
      : (sections.find(
          (name) => Array.isArray(dump?.[name]) && dump[name].length > 0,
        ) ?? 'shop');

  const entries: LiveEntry[] = (dump?.[section] ?? []).map(
    (raw: any, index: number) => {
      const card = findCard(cards, raw?.templateId, raw?.tier);
      return {
        section,
        slot: typeof raw?.order === 'number' ? raw.order : index,
        tier: raw?.tier ?? null,
        cardId: card?.id ?? null,
        name: card
          ? (card.nameCn ?? card.name)
          : `未知卡 ${String(raw?.templateId ?? '').slice(0, 8)}`,
        found: Boolean(card),
      };
    },
  );

  return {
    source: 'bpp',
    hero: dump?.hero ?? null,
    section,
    cards: entries,
  };
}

export function mapOcr(ocr: any, cards: CardLite[]): LiveMapResult {
  const entries: LiveEntry[] = (ocr?.cards ?? []).map(
    (raw: any, index: number) => {
      let card: CardLite | null = null;
      if (raw?.matchedId) {
        card = findCard(cards, raw.matchedId);
      }
      if (!card && raw?.name) {
        card =
          cards.find((candidate) => candidate.nameCn === raw.name) ??
          cards.find((candidate) => candidate.name === raw.name) ??
          null;
        if (card) {
          card =
            cards
              .filter((candidate) => candidate.baseItemId === card!.baseItemId)
              .sort((a, b) => tierRank(a.tier) - tierRank(b.tier))[0] ?? card;
        }
      }

      return {
        section: 'shop',
        slot: typeof raw?.slot === 'number' ? raw.slot : index + 1,
        tier: card?.tier ?? null,
        cardId: card?.id ?? null,
        name: raw?.name || (card ? (card.nameCn ?? card.name) : ''),
        found: Boolean(card),
      };
    },
  );

  return { source: 'ocr', section: 'shop', cards: entries };
}
