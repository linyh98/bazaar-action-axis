import type { CardDef, Loadout, RuleOverrides } from '../engine/types';

export const STORAGE_KEYS = {
  customCards: 'bazaar.customCards.v1',
  loadouts: 'bazaar.loadouts.v1',
  rules: 'bazaar.rules.v1',
} as const;

export function readJson<T>(key: string, fallback: T): T {
  const raw = localStorage.getItem(key);
  if (raw == null) return fallback;
  try {
    return JSON.parse(raw) as T;
  } catch {
    localStorage.setItem(`${key}.bak`, raw);
    return fallback;
  }
}

export function writeJson(key: string, value: unknown): void {
  try {
    localStorage.setItem(key, JSON.stringify(value));
  } catch {
    // Ignore quota or privacy-mode failures; in-memory state still works.
  }
}

function isCardDef(value: unknown): value is CardDef {
  if (!value || typeof value !== 'object') return false;
  const card = value as Record<string, unknown>;
  return (
    typeof card.id === 'string' &&
    typeof card.name === 'string' &&
    typeof card.size === 'string' &&
    typeof card.tier === 'string' &&
    typeof card.cooldown === 'number' &&
    typeof card.parseStatus === 'string' &&
    Array.isArray(card.effects) &&
    Array.isArray(card.triggers) &&
    Array.isArray(card.tags) &&
    Array.isArray(card.rawTooltips)
  );
}

function isLoadout(value: unknown): value is Loadout {
  if (!value || typeof value !== 'object') return false;
  const items = (value as { items?: unknown }).items;
  if (!Array.isArray(items)) return false;
  return items.every((item) => {
    if (!item || typeof item !== 'object') return false;
    const entry = item as Record<string, unknown>;
    return (
      typeof entry.cardId === 'string' &&
      typeof entry.slot === 'number' &&
      Number.isFinite(entry.slot)
    );
  });
}

export function loadCustomCards(): CardDef[] {
  const value = readJson<unknown>(STORAGE_KEYS.customCards, []);
  return Array.isArray(value) ? value.filter(isCardDef) : [];
}

export function saveCustomCards(cards: CardDef[]): void {
  writeJson(STORAGE_KEYS.customCards, cards);
}

export function loadLoadouts(): { player: Loadout; enemy: Loadout } | null {
  const value = readJson<unknown>(STORAGE_KEYS.loadouts, null);
  if (!value || typeof value !== 'object') return null;
  const { player, enemy } = value as { player?: unknown; enemy?: unknown };
  if (!isLoadout(player) || !isLoadout(enemy)) return null;
  return { player, enemy };
}

export function saveLoadouts(player: Loadout, enemy: Loadout): void {
  writeJson(STORAGE_KEYS.loadouts, { player, enemy });
}

export function loadRules(): RuleOverrides {
  const value = readJson<unknown>(STORAGE_KEYS.rules, {});
  if (!value || typeof value !== 'object' || Array.isArray(value)) return {};
  return value as RuleOverrides;
}

export function saveRules(rules: RuleOverrides): void {
  writeJson(STORAGE_KEYS.rules, rules);
}
