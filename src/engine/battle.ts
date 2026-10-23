import { applyDamage, applyHeal, resolveEffect } from './effects';
import { pushEvent } from './events';
import { drainTriggerQueue } from './triggers';
import { createRng, type Rng } from './rng';
import { resolveRules, type ResolvedRules } from './rules';
import type {
  BattleConfig,
  BattleEvent,
  BattleResult,
  CardDef,
  Effect,
  Side,
  Snapshot,
  Totals,
  TriggerPayload,
} from './types';

export interface InternalItem {
  id: string;
  cardId: string;
  card: CardDef;
  side: Side;
  slot: number;
  cooldown: number;
  hasteUntil: number;
  slowUntil: number;
  frozenUntil: number;
  multicastBonus: number;
  critBonus: number;
  damageBonus: number;
  lifesteal: number;
  ammo: number;
  ammoMax?: number;
}

export interface InternalHero {
  side: Side;
  hp: number;
  maxHp: number;
  shield: number;
  burn: number;
  poison: number;
  regen: number;
  burnSource?: Side;
  burnSourceItemId?: string;
  burnSourceCardId?: string;
  poisonSource?: Side;
  poisonSourceItemId?: string;
  poisonSourceCardId?: string;
  regenSourceItemId?: string;
  regenSourceCardId?: string;
}

export interface BattleContext {
  config: BattleConfig;
  rules: ResolvedRules;
  rng: Rng;
  player: InternalHero;
  enemy: InternalHero;
  items: InternalItem[];
  events: BattleEvent[];
  snapshots: Snapshot[];
  triggerQueue: TriggerPayload[];
  totals: Totals;
  nextEventId: number;
  nextChainId: number;
  eventsThisTick: number;
  ended: boolean;
  endTime: number;
  winner: 'player' | 'enemy' | 'draw' | null;
  warnings: string[];
}

function emptyTotals(): Totals {
  return {
    player: { damage: 0, effectiveDamage: 0, heal: 0, shield: 0 },
    enemy: { damage: 0, effectiveDamage: 0, heal: 0, shield: 0 },
  };
}

function applyStaticEffects(item: InternalItem): void {
  for (const effect of item.card.effects) {
    if (effect.kind === 'multicast') {
      item.multicastBonus += Math.max(0, effect.count - 1);
    } else if (effect.kind === 'crit') {
      item.critBonus += effect.chance;
    } else if (effect.kind === 'lifesteal') {
      item.lifesteal += effect.percent;
    }
  }
}

export function initBattle(config: BattleConfig): BattleContext {
  const rules = resolveRules(config.rules);
  const ctx: BattleContext = {
    config,
    rules,
    rng: createRng(config.seed),
    player: { side: 'player', hp: config.playerMaxHp, maxHp: config.playerMaxHp, shield: 0, burn: 0, poison: 0, regen: 0 },
    enemy: { side: 'enemy', hp: config.enemyMaxHp, maxHp: config.enemyMaxHp, shield: 0, burn: 0, poison: 0, regen: 0 },
    items: [],
    events: [],
    snapshots: [],
    triggerQueue: [],
    totals: emptyTotals(),
    nextEventId: 1,
    nextChainId: 1,
    eventsThisTick: 0,
    ended: false,
    endTime: 0,
    winner: null,
    warnings: [],
  };

  const cardCounts = new Map<string, number>();
  for (const side of ['player', 'enemy'] as const) {
    for (const entry of config[side].items) {
      const card = config.cards[entry.cardId];
      if (card) cardCounts.set(card.id, (cardCounts.get(card.id) ?? 0) + 1);
    }
  }

  for (const side of ['player', 'enemy'] as const) {
    for (const entry of config[side].items) {
      const card = config.cards[entry.cardId];
      if (!card) {
        ctx.warnings.push(`unknown card: ${entry.cardId}`);
        continue;
      }
      const startReady = card.startReady ?? rules.startReady;
      const instanceId =
        (cardCounts.get(card.id) ?? 0) > 1 ? `${side}:${entry.slot}:${card.id}` : card.id;
      const item: InternalItem = {
        id: instanceId,
        cardId: card.id,
        card,
        side,
        slot: entry.slot,
        cooldown: startReady ? 0 : card.cooldown,
        hasteUntil: -1,
        slowUntil: -1,
        frozenUntil: -1,
        multicastBonus: 0,
        critBonus: 0,
        damageBonus: 0,
        lifesteal: 0,
        ammo: card.ammoMax ?? 0,
        ammoMax: card.ammoMax,
      };
      applyStaticEffects(item);
      ctx.items.push(item);
    }
  }

  return ctx;
}

function advanceCooldowns(ctx: BattleContext, t: number): void {
  for (const item of ctx.items) {
    const frozen = t <= item.frozenUntil + 1e-9;
    const hasted = t <= item.hasteUntil + 1e-9;
    const slowed = t <= item.slowUntil + 1e-9;
    const rate = frozen
      ? 0
      : (hasted ? ctx.rules.hasteMultiplier : 1) *
        (slowed ? ctx.rules.slowMultiplier : 1);
    item.cooldown = Math.max(0, item.cooldown - ctx.rules.dt * rate);
  }
}

function collectReady(ctx: BattleContext): InternalItem[] {
  return ctx.items
    .filter(
      (item) => !item.card.passive && item.card.cooldown > 0 && item.cooldown <= 1e-9,
    )
    .sort((a, b) => {
      if (a.side !== b.side) return a.side === 'player' ? -1 : 1;
      return a.slot - b.slot;
    });
}

function activate(ctx: BattleContext, item: InternalItem, t: number): void {
  if (t <= item.frozenUntil + 1e-9) return;
  if (item.ammoMax != null && item.ammo <= 0) return;

  const chainId = ctx.nextChainId++;
  const casts = 1 + item.multicastBonus;
  item.cooldown = item.card.cooldown;
  for (let cast = 0; cast < casts; cast++) {
    if (ctx.ended) break;
    if (item.ammoMax != null && item.ammo <= 0) break;
    pushEvent(ctx, {
      time: t,
      side: item.side,
      sourceItemId: item.id,
      sourceCardId: item.cardId,
      kind: 'activate',
      chainId,
      depth: 0,
    });

    for (const effect of item.card.effects) {
      if (
        effect.kind === 'multicast' ||
        effect.kind === 'crit' ||
        effect.kind === 'lifesteal'
      ) {
        continue;
      }
      resolveEffect(ctx, effect, item, t, 0, chainId);
      if (ctx.ended) break;
    }

    ctx.triggerQueue.push({
      event: 'onUse',
      sourceSide: item.side,
      sourceItemId: item.id,
      chainId,
      depth: 0,
    });
    drainTriggerQueue(ctx, t);
    if (ctx.player.hp <= 0 || ctx.enemy.hp <= 0) checkDeath(ctx, t);

    if (item.ammoMax != null) {
      item.ammo = Math.max(0, item.ammo - 1);
    }
  }
}

function checkDeath(ctx: BattleContext, t: number): void {
  if (ctx.ended) return;
  const playerDead = ctx.player.hp <= 0;
  const enemyDead = ctx.enemy.hp <= 0;
  if (!playerDead && !enemyDead) return;

  ctx.ended = true;
  ctx.endTime = t;
  ctx.winner = playerDead && enemyDead ? 'draw' : playerDead ? 'enemy' : 'player';
  pushEvent(ctx, {
    time: t,
    side: playerDead ? 'player' : 'enemy',
    kind: 'death',
    value: 0,
  });

  if (!(playerDead && enemyDead)) {
    const killerSide: Side = playerDead ? 'enemy' : 'player';
    const killChain = ctx.nextChainId++;
    for (const item of ctx.items) {
      if (item.side !== killerSide) continue;
      for (const trigger of item.card.triggers) {
        if (trigger.event !== 'onKill') continue;
        for (const effect of trigger.effects) {
          resolveEffect(ctx, effect, item, t, 1, killChain);
        }
      }
    }
  }
}

function computeWinner(ctx: BattleContext): 'player' | 'enemy' | 'draw' {
  const playerRatio = ctx.player.hp / ctx.player.maxHp;
  const enemyRatio = ctx.enemy.hp / ctx.enemy.maxHp;
  if (playerRatio > enemyRatio) return 'player';
  if (enemyRatio > playerRatio) return 'enemy';

  const playerHasItems = ctx.items.some((item) => item.side === 'player');
  const enemyHasItems = ctx.items.some((item) => item.side === 'enemy');
  if (playerHasItems && !enemyHasItems) return 'player';
  if (enemyHasItems && !playerHasItems) return 'enemy';
  return 'draw';
}

function snapshotHero(ctx: BattleContext, side: Side): Snapshot['player'] {
  const hero = side === 'player' ? ctx.player : ctx.enemy;
  return {
    hp: hero.hp,
    maxHp: hero.maxHp,
    shield: hero.shield,
    burn: hero.burn,
    poison: hero.poison,
    regen: hero.regen,
    items: ctx.items
      .filter((item) => item.side === side)
      .sort((a, b) => a.slot - b.slot)
      .map((item) => ({
        itemId: item.id,
        cardId: item.cardId,
        slot: item.slot,
        cooldown: item.cooldown,
        cooldownMax: item.card.cooldown,
        hasteUntil: item.hasteUntil,
        slowUntil: item.slowUntil,
        frozenUntil: item.frozenUntil,
        ammo: item.ammo,
      })),
  };
}

export function snapshot(ctx: BattleContext, t: number): Snapshot {
  return {
    time: t,
    player: snapshotHero(ctx, 'player'),
    enemy: snapshotHero(ctx, 'enemy'),
  };
}

export function tickDots(ctx: BattleContext, t: number): void {
  if (ctx.ended) return;

  const dotChain = ctx.nextChainId++;
  for (const side of ['player', 'enemy'] as const) {
    const hero = ctx[side];

    if (hero.burn > 0) {
      const sourceSide = hero.burnSource ?? (side === 'player' ? 'enemy' : 'player');
      applyDamage(
        ctx,
        {
          id: hero.burnSourceItemId ?? 'dot:burn',
          cardId: hero.burnSourceCardId ?? 'burn',
          side: sourceSide,
          critBonus: 0,
          lifesteal: 0,
        },
        side,
        hero.burn,
        t,
        { isDot: true, dotKind: 'burn', chainId: dotChain },
      );
      if (ctx.player.hp <= 0 || ctx.enemy.hp <= 0) {
        checkDeath(ctx, t);
        return;
      }
      hero.burn = Math.max(0, hero.burn - ctx.rules.burnDecay);
    }

    if (hero.poison > 0) {
      const sourceSide = hero.poisonSource ?? (side === 'player' ? 'enemy' : 'player');
      applyDamage(
        ctx,
        {
          id: hero.poisonSourceItemId ?? 'dot:poison',
          cardId: hero.poisonSourceCardId ?? 'poison',
          side: sourceSide,
          critBonus: 0,
          lifesteal: 0,
        },
        side,
        hero.poison,
        t,
        { isDot: true, dotKind: 'poison', chainId: dotChain },
      );
      if (ctx.player.hp <= 0 || ctx.enemy.hp <= 0) {
        checkDeath(ctx, t);
        return;
      }
      hero.poison = Math.max(0, hero.poison - ctx.rules.poisonDecay);
    }

    if (hero.regen > 0) {
      applyHeal(
        ctx,
        side,
        hero.regen,
        t,
        hero.regenSourceItemId,
        hero.regenSourceCardId,
        dotChain,
      );
      pushEvent(ctx, {
        time: t,
        side,
        targetSide: side,
        sourceItemId: hero.regenSourceItemId,
        sourceCardId: hero.regenSourceCardId,
        kind: 'dotTick',
        value: hero.regen,
        hpAfter: hero.hp,
        chainId: dotChain,
        note: 'regen',
      });
    }
  }
}

function simulateUnsafe(config: BattleConfig): BattleResult {
  const ctx = initBattle(config);
  const dt = ctx.rules.dt;
  const maxTime = Math.min(config.maxTime, ctx.rules.maxTime);

  for (const item of ctx.items) {
    const chainId = ctx.nextChainId++;
    for (const trigger of item.card.triggers) {
      if (trigger.event !== 'atCombatStart') continue;
      for (const effect of trigger.effects) {
        resolveEffect(ctx, effect, item, 0, 1, chainId);
      }
    }
  }
  drainTriggerQueue(ctx, 0);
  checkDeath(ctx, 0);

  for (let step = 0; step * dt <= maxTime + 1e-9; step++) {
    const t = Number((step * dt).toFixed(6));
    ctx.eventsThisTick = 0;
    if (step > 0) advanceCooldowns(ctx, t);

    const previous = (step - 1) * dt;
    const crossedSecond =
      step > 0 && Math.floor(t + 1e-9) > Math.floor(previous + 1e-9);
    if (crossedSecond) {
      tickDots(ctx, t);

      for (const item of ctx.items) {
        const chainId = ctx.nextChainId++;
        for (const trigger of item.card.triggers) {
          if (trigger.event !== 'everySecond') continue;
          const every = trigger.everySeconds ?? 1;
          const quotient = t / every;
          if (Math.abs(quotient - Math.round(quotient)) > 1e-6) continue;
          for (const effect of trigger.effects) {
            resolveEffect(ctx, effect, item, t, 1, chainId);
          }
        }
      }

      drainTriggerQueue(ctx, t);
      checkDeath(ctx, t);
    }

    const ready = collectReady(ctx);
    for (const item of ready) {
      if (ctx.ended) break;
      activate(ctx, item, t);
    }

    drainTriggerQueue(ctx, t);
    checkDeath(ctx, t);
    ctx.snapshots.push(snapshot(ctx, t));
    if (ctx.ended) break;
  }

  if (!ctx.ended) {
    ctx.endTime = maxTime;
    ctx.winner = computeWinner(ctx);
  }

  return {
    events: ctx.events,
    snapshots: ctx.snapshots,
    totals: ctx.totals,
    winner: ctx.winner ?? 'draw',
    endTime: ctx.endTime,
    warnings: ctx.warnings,
  };
}

export function simulate(config: BattleConfig): BattleResult {
  try {
    return simulateUnsafe(config);
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    return {
      events: [],
      snapshots: [
        {
          time: 0,
          player: {
            hp: config.playerMaxHp,
            maxHp: config.playerMaxHp,
            shield: 0,
            burn: 0,
            poison: 0,
            regen: 0,
            items: [],
          },
          enemy: {
            hp: config.enemyMaxHp,
            maxHp: config.enemyMaxHp,
            shield: 0,
            burn: 0,
            poison: 0,
            regen: 0,
            items: [],
          },
        },
      ],
      totals: emptyTotals(),
      winner: 'draw',
      endTime: 0,
      warnings: [`simulation failed: ${message}`],
    };
  }
}
