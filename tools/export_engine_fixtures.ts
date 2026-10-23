import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import { simulate } from '../src/engine/battle';
import type { BattleConfig, CardDef, Effect, LoadoutItem, Target } from '../src/engine/types';

function card(id: string, overrides: Partial<CardDef> = {}): CardDef {
  return {
    id,
    name: id,
    size: 'Small',
    tier: 'Bronze',
    cooldown: 5,
    effects: [],
    triggers: [],
    tags: [],
    parseStatus: 'structured',
    rawTooltips: [],
    ...overrides,
  };
}

function item(cardId: string, slot: number): LoadoutItem {
  return { cardId, slot };
}

function config(
  cards: CardDef[],
  player: LoadoutItem[],
  enemy: LoadoutItem[] = [],
  overrides: Partial<BattleConfig> = {},
): BattleConfig {
  const map: Record<string, CardDef> = {};
  for (const entry of cards) map[entry.id] = entry;
  return {
    cards: map,
    player: { items: player },
    enemy: { items: enemy },
    playerMaxHp: 100,
    enemyMaxHp: 100,
    seed: 1,
    rules: {},
    maxTime: 60,
    ...overrides,
  };
}

const damage = (value: number, target: Target = { kind: 'enemyHero' }): Effect => ({
  kind: 'damage',
  amount: { type: 'const', value },
  target,
});

const scenarios: Array<{ name: string; config: BattleConfig }> = [
  {
    name: 'basic-damage',
    config: config([card('p1', { effects: [damage(5)] })], [item('p1', 0)]),
  },
  {
    name: 'haste-slow',
    config: config(
      [
        card('p1', {
          effects: [{ kind: 'haste', seconds: 3, target: { kind: 'selfItem', itemId: 'p1' } }],
        }),
        card('p2', {
          effects: [{ kind: 'slow', seconds: 2, target: { kind: 'enemyItem', itemId: 'e1' } }],
        }),
        card('e1'),
      ],
      [item('p1', 0), item('p2', 1)],
      [item('e1', 0)],
    ),
  },
  {
    name: 'freeze',
    config: config(
      [
        card('p1', {
          effects: [{ kind: 'freeze', seconds: 2, target: { kind: 'enemyItem', itemId: 'e1' } }],
        }),
        card('e1'),
      ],
      [item('p1', 0)],
      [item('e1', 0)],
    ),
  },
  {
    name: 'charge',
    config: config(
      [
        card('p1', {
          cooldown: 10,
          effects: [{ kind: 'charge', seconds: 3, target: { kind: 'selfItem', itemId: 'p1' } }],
        }),
      ],
      [item('p1', 0)],
    ),
  },
  {
    name: 'multicast-ammo',
    config: config(
      [
        card('p1', {
          ammoMax: 2,
          effects: [
            { kind: 'multicast', count: 3 },
            damage(3),
          ],
        }),
      ],
      [item('p1', 0)],
    ),
  },
  {
    name: 'reload',
    config: config(
      [
        card('p1', { cooldown: 4, ammoMax: 3, effects: [damage(4)] }),
        card('p2', {
          effects: [{ kind: 'reload', amount: 2, target: { kind: 'selfItem', itemId: 'p1' } }],
        }),
      ],
      [item('p1', 0), item('p2', 1)],
    ),
  },
  {
    name: 'triggers',
    config: config(
      [
        card('p1', {
          effects: [damage(2)],
          triggers: [
            {
              event: 'onUse',
              effects: [damage(2)],
            },
          ],
        }),
      ],
      [item('p1', 0)],
    ),
  },
  {
    name: 'dot',
    config: config(
      [
        card('p1', {
          effects: [
            { kind: 'burn', amount: { type: 'const', value: 4 }, target: { kind: 'enemyHero' } },
            { kind: 'poison', amount: { type: 'const', value: 3 }, target: { kind: 'enemyHero' } },
          ],
        }),
      ],
      [item('p1', 0)],
    ),
  },
  {
    name: 'crit-lifesteal',
    config: config(
      [
        card('p1', {
          effects: [
            damage(5),
            { kind: 'crit', chance: 1 },
            { kind: 'lifesteal', percent: 0.5 },
          ],
        }),
        card('e1', { effects: [damage(10)] }),
      ],
      [item('p1', 0)],
      [item('e1', 0)],
    ),
  },
  {
    name: 'on-kill',
    config: config(
      [
        card('p1', {
          effects: [damage(5)],
          triggers: [
            {
              event: 'onKill',
              effects: [damage(3)],
            },
          ],
        }),
      ],
      [item('p1', 0)],
      [],
      { enemyMaxHp: 5 },
    ),
  },
  {
    name: 'every-second',
    config: config(
      [
        card('p1', {
          triggers: [
            {
              event: 'everySecond',
              everySeconds: 2,
              effects: [
                {
                  kind: 'shield',
                  amount: { type: 'const', value: 1 },
                  target: { kind: 'selfHero' },
                },
              ],
            },
          ],
        }),
      ],
      [item('p1', 0)],
    ),
  },
  {
    name: 'multi-target',
    config: config(
      [
        card('p1', {
          effects: [{ kind: 'freeze', seconds: 2, target: { kind: 'allEnemyItems' } }],
        }),
        card('e1'),
        card('e2'),
        card('e3'),
      ],
      [item('p1', 0)],
      [item('e1', 0), item('e2', 1), item('e3', 2)],
    ),
  },
  {
    name: 'percent-max-hp',
    config: config(
      [
        card('p1', {
          effects: [
            {
              kind: 'damage',
              amount: { type: 'percentMaxHp', value: 10 },
              target: { kind: 'enemyHero' },
            },
          ],
        }),
      ],
      [item('p1', 0)],
    ),
  },
  {
    name: 'duplicate-cards',
    config: config(
      [card('p1', { effects: [damage(1)] })],
      [item('p1', 0), item('p1', 1)],
    ),
  },
  {
    name: 'unknown-card',
    config: config([], [item('missing', 0)]),
  },
  {
    name: 'empty-board',
    config: config([], []),
  },
  {
    name: 'time-cap',
    config: config(
      [card('p1', { effects: [damage(1)] })],
      [item('p1', 0)],
      [],
      { enemyMaxHp: 100000, maxTime: 60 },
    ),
  },
];

const fixtures = {
  scenarios: scenarios.map((scenario) => {
    const result = simulate(scenario.config);
    return {
      name: scenario.name,
      config: scenario.config,
      expected: {
        events: result.events,
        totals: result.totals,
        winner: result.winner,
        endTime: result.endTime,
        warnings: result.warnings,
      },
    };
  }),
};

const out = 'tools/actionaxis_plugin/tests/fixtures/engine_fixtures.json';
mkdirSync(dirname(out), { recursive: true });
writeFileSync(out, JSON.stringify(fixtures, null, 2), 'utf8');
console.log(`wrote ${fixtures.scenarios.length} scenarios to ${out}`);
