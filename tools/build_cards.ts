import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseTooltips } from '../src/engine/parse';
import type { CardDef, Effect, TriggerDef } from '../src/engine/types';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(HERE, '..');

type RawItem = {
  id: string;
  name: string;
  nameCn?: string;
  image?: string;
  size: CardDef['size'];
  tags?: string[];
  tierData?: { tier: CardDef['tier']; tooltips?: string[] }[];
};

type StructuredCard = {
  guid: string;
  internalName: string;
  size: CardDef['size'];
  tier: CardDef['tier'];
  cooldown: number;
  multicastBonus: number;
  ammoMax: number | null;
  effects: Effect[];
  triggers: TriggerDef[];
  tags: string[];
  parseStatus: 'structured' | 'partial';
  dataSource: 'game-db';
};

const items: RawItem[] = JSON.parse(
  readFileSync(resolve(ROOT, 'data/items_raw.json'), 'utf-8'),
);
const fullCardsPath = resolve(ROOT, 'data/game_cards_full.json');
const structuredPath = existsSync(fullCardsPath)
  ? fullCardsPath
  : resolve(ROOT, 'data/game_cards_structured.json');
const structured: StructuredCard[] = JSON.parse(
  readFileSync(structuredPath, 'utf-8'),
);

const structuredByKey = new Map<string, StructuredCard>();
for (const card of structured) {
  structuredByKey.set(`${card.guid}@${card.tier}`, card);
}

const codexCardIds = new Set<string>();
for (const item of items) {
  for (const tierData of item.tierData ?? []) {
    codexCardIds.add(`${item.id}@${tierData.tier}`);
  }
}
const codexById = new Map(items.map((item) => [item.id, item]));
const codexGuids = new Set(items.map((item) => item.id).filter(Boolean));
const gameDbGuids = new Set(structured.map((card) => card.guid));
const guidIntersection = [...gameDbGuids].filter((guid) => codexGuids.has(guid)).length;
const extraEntries = structured.filter(
  (card) => !codexCardIds.has(`${card.guid}@${card.tier}`),
);

const seenIds = new Set<string>();
const cards: CardDef[] = [];
const matchedGuids = new Set<string>();
const byParseStatus = { structured: 0, partial: 0, text: 0 };
const byDataSource = { 'game-db': 0, tooltip: 0, merged: 0 };

function addCard(card: CardDef): void {
  if (seenIds.has(card.id)) return;
  seenIds.add(card.id);
  cards.push(card);
  byParseStatus[card.parseStatus] += 1;
  if (card.dataSource === 'game-db') byDataSource['game-db'] += 1;
  else if (card.dataSource === 'merged') byDataSource.merged += 1;
  else byDataSource.tooltip += 1;
}

for (const item of items) {
  if (!item.id) continue;
  for (const tierData of item.tierData ?? []) {
    const cardId = `${item.id}@${tierData.tier}`;
    const match = structuredByKey.get(cardId);
    const tooltips = tierData.tooltips ?? [];
    const parsed = parseTooltips(tooltips);

    if (match) {
      matchedGuids.add(item.id);
      const effects = [...match.effects];
      if (match.multicastBonus > 0) {
        effects.push({ kind: 'multicast', count: match.multicastBonus + 1 });
      }
      const triggers = [...match.triggers];
      const effectKinds = new Set(effects.map((effect) => effect.kind));
      for (const effect of parsed.effects) {
        if (effectKinds.has(effect.kind)) continue;
        effects.push(effect);
        effectKinds.add(effect.kind);
      }
      for (const trigger of parsed.triggers) {
        if (
          !triggers.some(
            (existing) => JSON.stringify(existing) === JSON.stringify(trigger),
          )
        ) {
          triggers.push(trigger);
        }
      }
      const cooldown = match.cooldown > 0 ? match.cooldown : (parsed.cooldown ?? 0);
      const parseStatus =
        match.parseStatus === 'structured' || parsed.parseStatus === 'structured'
          ? 'structured'
          : match.parseStatus;
      const dataSource =
        match.parseStatus === 'structured'
          ? 'game-db'
          : parsed.parseStatus === 'structured'
            ? 'merged'
            : 'game-db';
      addCard({
        id: cardId,
        baseItemId: item.id,
        name: item.name,
        nameCn: item.nameCn,
        image: item.image,
        size: item.size,
        tier: tierData.tier,
        cooldown,
        passive: cooldown <= 0,
        effects,
        triggers,
        tags: item.tags ?? [],
        ammoMax: match.ammoMax ?? undefined,
        parseStatus,
        rawTooltips: tooltips,
        dataSource,
      });
      continue;
    }

    const cooldown = parsed.cooldown ?? 0;
    addCard({
      id: cardId,
      baseItemId: item.id,
      name: item.name,
      nameCn: item.nameCn,
      image: item.image,
      size: item.size,
      tier: tierData.tier,
      cooldown,
      passive: cooldown <= 0,
      effects: parsed.effects,
      triggers: parsed.triggers,
      tags: item.tags ?? [],
      ammoMax: parsed.ammoMax,
      parseStatus: parsed.parseStatus,
      rawTooltips: tooltips,
      dataSource: 'tooltip',
    });
  }
}

for (const card of structured) {
  const cardId = `${card.guid}@${card.tier}`;
  if (seenIds.has(cardId)) continue;
  const codexItem = codexById.get(card.guid);
  const effects = [...card.effects];
  if (card.multicastBonus > 0) {
    effects.push({ kind: 'multicast', count: card.multicastBonus + 1 });
  }
  addCard({
    id: cardId,
    baseItemId: card.guid,
    name: card.internalName,
    nameCn: codexItem?.nameCn,
    image: codexItem?.image,
    size: card.size,
    tier: card.tier,
    cooldown: card.cooldown,
    passive: card.cooldown <= 0,
    effects,
    triggers: card.triggers,
    tags: codexItem?.tags ?? card.tags,
    ammoMax: card.ammoMax ?? undefined,
    parseStatus: card.parseStatus,
    rawTooltips: [],
    dataSource: 'game-db',
  });
}

const report = {
  total: cards.length,
  byParseStatus,
  byDataSource,
  uniqueGuidMatched: matchedGuids.size,
  guidIntersection,
  extraGameDbEntries: extraEntries.length,
  extraGameDbGuids: new Set(extraEntries.map((card) => card.guid)).size,
};

const outDir = resolve(ROOT, 'src/data');
mkdirSync(outDir, { recursive: true });
writeFileSync(resolve(outDir, 'cards.json'), JSON.stringify(cards, null, 2), 'utf-8');
writeFileSync(resolve(outDir, 'parse_report.json'), JSON.stringify(report, null, 2), 'utf-8');

console.log(
  `cards: ${report.total}, structured: ${byParseStatus.structured}, partial: ${byParseStatus.partial}, text: ${byParseStatus.text}`,
);
console.log(`dataSource: game-db ${byDataSource['game-db']}, tooltip ${byDataSource.tooltip}`);
console.log(`unique codex GUIDs matched to game-db: ${report.uniqueGuidMatched}`);
