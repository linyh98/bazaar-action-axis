import type { BattleContext, InternalHero, InternalItem } from './battle';
import type { Effect, NumExpr, Side, Target, TriggerPayload } from './types';
import { pushEvent } from './events';

export function evaluateNum(
  expr: NumExpr,
  ctx: BattleContext,
  source: InternalItem,
  targetHero: InternalHero,
): number {
  switch (expr.type) {
    case 'const':
      return expr.value;
    case 'percentMaxHp': {
      const basis =
        expr.basis === 'source' ? ctx[source.side].maxHp : targetHero.maxHp;
      return (basis * expr.value) / 100;
    }
    case 'regen':
      return targetHero.regen;
    case 'perStack':
      return (expr.stack === 'burn' ? targetHero.burn : targetHero.poison) * expr.per;
    case 'value':
    case 'income':
    case 'perTag':
      return 0;
  }
}

export function applyHeal(
  ctx: BattleContext,
  side: Side,
  amount: number,
  time: number,
  sourceItemId?: string,
  sourceCardId?: string,
  chainId?: number,
  depth = 0,
): number {
  const hero = ctx[side];
  const before = hero.hp;
  hero.hp = Math.min(hero.maxHp, hero.hp + Math.max(0, Math.round(amount)));
  const healed = hero.hp - before;
  ctx.totals[side].heal += healed;
  emitTrigger(
    ctx,
    'onHeal',
    {
      id: sourceItemId ?? 'heal',
      cardId: sourceCardId ?? 'heal',
      side,
      critBonus: 0,
      lifesteal: 0,
    },
    side,
    depth,
    undefined,
    chainId,
  );
  pushEvent(ctx, {
    time,
    side,
    targetSide: side,
    sourceItemId,
    sourceCardId,
    kind: 'heal',
    value: healed,
    hpAfter: hero.hp,
    chainId,
    depth,
  });
  return healed;
}

export function applyShield(
  ctx: BattleContext,
  side: Side,
  amount: number,
  time: number,
  sourceItemId?: string,
  sourceCardId?: string,
  chainId?: number,
  depth = 0,
): void {
  const hero = ctx[side];
  const gained = Math.max(0, Math.round(amount));
  hero.shield += gained;
  ctx.totals[side].shield += gained;
  emitTrigger(
    ctx,
    'onShield',
    {
      id: sourceItemId ?? 'shield',
      cardId: sourceCardId ?? 'shield',
      side,
      critBonus: 0,
      lifesteal: 0,
    },
    side,
    depth,
    undefined,
    chainId,
  );
  pushEvent(ctx, {
    time,
    side,
    targetSide: side,
    sourceItemId,
    sourceCardId,
    kind: 'shield',
    value: gained,
    shieldAfter: hero.shield,
    chainId,
    depth,
  });
}

export interface DamageOptions {
  isDot?: boolean;
  dotKind?: 'burn' | 'poison';
  depth?: number;
  chainId?: number;
}

export interface DamageSource {
  id: string;
  cardId: string;
  side: Side;
  critBonus: number;
  lifesteal: number;
}

function emitTrigger(
  ctx: BattleContext,
  event: TriggerPayload['event'],
  source: DamageSource,
  targetSide?: Side,
  depth = 0,
  targetItemId?: string,
  chainId?: number,
): void {
  ctx.triggerQueue.push({
    event,
    sourceSide: source.side,
    sourceItemId: source.id,
    targetSide,
    targetItemId,
    chainId,
    depth,
  });
}

export function applyDamage(
  ctx: BattleContext,
  source: DamageSource,
  targetSide: Side,
  rawAmount: number,
  time: number,
  opts: DamageOptions = {},
): void {
  let amount = Math.max(0, Math.round(rawAmount));
  const depth = opts.depth ?? 0;
  const chainId = opts.chainId;

  let critical = false;
  if (!opts.isDot && source.critBonus > 0 && ctx.rng.chance(source.critBonus)) {
    critical = true;
    amount = Math.round(amount * ctx.rules.critMultiplier);
    pushEvent(ctx, {
      time,
      side: source.side,
      sourceItemId: source.id,
      sourceCardId: source.cardId,
      kind: 'crit',
      targetSide,
      value: amount,
      chainId,
      depth,
    });
  }

  const hero = ctx[targetSide];
  const absorbed =
    opts.isDot && ctx.rules.dotBypassesShield ? 0 : Math.min(hero.shield, amount);
  hero.shield -= absorbed;
  const hpLoss = Math.min(hero.hp, amount - absorbed);
  hero.hp -= hpLoss;

  ctx.totals[source.side].damage += amount;
  ctx.totals[source.side].effectiveDamage += hpLoss;

  pushEvent(ctx, {
    time,
    side: source.side,
    sourceItemId: source.id,
    sourceCardId: source.cardId,
    kind: opts.isDot ? 'dotTick' : 'damage',
    targetSide,
    value: amount,
    absorbed,
    hpAfter: hero.hp,
    shieldAfter: hero.shield,
    chainId,
    depth,
    note: opts.isDot ? opts.dotKind : critical ? 'critical' : undefined,
  });

  if (amount > 0) {
    emitTrigger(ctx, 'onDamageDealt', source, targetSide, depth, undefined, chainId);
    ctx.triggerQueue.push({
      event: 'onDamageTaken',
      sourceSide: targetSide,
      sourceItemId: source.id,
      targetSide,
      chainId,
      depth,
    });
    if (critical) {
      emitTrigger(ctx, 'onCrit', source, targetSide, depth, undefined, chainId);
    }
  }

  if (source.lifesteal > 0 && hpLoss > 0 && !opts.isDot) {
    applyHeal(
      ctx,
      source.side,
      hpLoss * source.lifesteal,
      time,
      source.id,
      source.cardId,
      chainId,
      depth,
    );
  }
}

function opposingSide(side: Side): Side {
  return side === 'player' ? 'enemy' : 'player';
}

function heroFor(
  ctx: BattleContext,
  source: InternalItem,
  target: Target,
  defaultSide: Side,
): InternalHero {
  if (target.kind === 'selfHero') return ctx[source.side];
  if (target.kind === 'enemyHero') return ctx[opposingSide(source.side)];
  return ctx[defaultSide];
}

function itemsForTarget(
  ctx: BattleContext,
  source: InternalItem,
  target: Target,
  count = 1,
): InternalItem[] {
  const selfItems = ctx.items
    .filter((item) => item.side === source.side)
    .sort((a, b) => a.slot - b.slot);
  const enemyItems = ctx.items
    .filter((item) => item.side !== source.side)
    .sort((a, b) => a.slot - b.slot);
  const sourceIndex = selfItems.findIndex((item) => item.id === source.id);

  const sample = (pool: InternalItem[]): InternalItem[] => {
    const copy = [...pool];
    const out: InternalItem[] = [];
    const n = Math.min(Math.max(1, count), copy.length);
    for (let i = 0; i < n; i += 1) {
      out.push(copy.splice(ctx.rng.int(copy.length), 1)[0]);
    }
    return out;
  };

  let list: InternalItem[] = [];
  switch (target.kind) {
    case 'selfItem':
      list = target.itemId
        ? ctx.items.filter(
            (item) =>
              (item.cardId === target.itemId || item.id === target.itemId) &&
              item.side === source.side,
          )
        : [source];
      break;
    case 'enemyItem':
      list = ctx.items.filter(
        (item) =>
          (item.cardId === target.itemId || item.id === target.itemId) &&
          item.side !== source.side,
      );
      break;
    case 'allSelfItems':
      list = selfItems;
      break;
    case 'allEnemyItems':
      list = enemyItems;
      break;
    case 'adjacentSelf':
      list = [selfItems[sourceIndex - 1], selfItems[sourceIndex + 1]].filter(
        Boolean,
      ) as InternalItem[];
      break;
    case 'leftSelf':
      list = sourceIndex > 0 ? [selfItems[sourceIndex - 1]] : [];
      break;
    case 'rightSelf':
      list =
        sourceIndex >= 0 && sourceIndex < selfItems.length - 1
          ? [selfItems[sourceIndex + 1]]
          : [];
      break;
    case 'randomSelfItem':
      list = sample(selfItems);
      break;
    case 'randomEnemyItem':
      list = sample(enemyItems);
      break;
    case 'lowestHpEnemyItem':
      list = enemyItems.slice(0, 1);
      break;
    default:
      list = [];
  }

  if (
    target.kind === 'allSelfItems' ||
    target.kind === 'allEnemyItems' ||
    target.kind === 'adjacentSelf'
  ) {
    return list;
  }
  return list.slice(0, Math.max(1, count));
}

function criticalAmount(
  ctx: BattleContext,
  source: InternalItem,
  amount: number,
  time: number,
  depth: number,
  chainId: number,
  targetSide: Side,
): number {
  if (source.critBonus <= 0 || !ctx.rng.chance(source.critBonus)) return amount;
  const next = Math.round(amount * ctx.rules.critMultiplier);
  pushEvent(ctx, {
    time,
    side: source.side,
    sourceItemId: source.id,
    sourceCardId: source.cardId,
    kind: 'crit',
    targetSide,
    value: next,
    chainId,
    depth,
  });
  return next;
}

export function resolveEffect(
  ctx: BattleContext,
  effect: Effect,
  source: InternalItem,
  time: number,
  depth = 0,
  chainId?: number,
): void {
  const ch = chainId ?? ctx.nextChainId++;

  switch (effect.kind) {
    case 'damage': {
      const hero = heroFor(ctx, source, effect.target, opposingSide(source.side));
      applyDamage(ctx, source, hero.side, evaluateNum(effect.amount, ctx, source, hero), time, {
        depth,
        chainId: ch,
      });
      return;
    }
    case 'heal': {
      const hero = heroFor(ctx, source, effect.target, source.side);
      applyHeal(
        ctx,
        hero.side,
        criticalAmount(
          ctx,
          source,
          evaluateNum(effect.amount, ctx, source, hero),
          time,
          depth,
          ch,
          hero.side,
        ),
        time,
        source.id,
        source.cardId,
        ch,
        depth,
      );
      return;
    }
    case 'shield': {
      const hero = heroFor(ctx, source, effect.target, source.side);
      applyShield(
        ctx,
        hero.side,
        criticalAmount(
          ctx,
          source,
          evaluateNum(effect.amount, ctx, source, hero),
          time,
          depth,
          ch,
          hero.side,
        ),
        time,
        source.id,
        source.cardId,
        ch,
        depth,
      );
      return;
    }
    case 'burn': {
      const hero = heroFor(ctx, source, effect.target, opposingSide(source.side));
      const amount = Math.max(0, Math.round(evaluateNum(effect.amount, ctx, source, hero)));
      hero.burn += amount;
      hero.burnSource = source.side;
      hero.burnSourceItemId = source.id;
      hero.burnSourceCardId = source.cardId;
      pushEvent(ctx, {
        time,
        side: source.side,
        targetSide: hero.side,
        sourceItemId: source.id,
        sourceCardId: source.cardId,
        kind: 'burn',
        value: amount,
        chainId: ch,
        depth,
      });
      emitTrigger(ctx, 'onBurnApplied', source, hero.side, depth, undefined, ch);
      return;
    }
    case 'poison': {
      const hero = heroFor(ctx, source, effect.target, opposingSide(source.side));
      const amount = Math.max(0, Math.round(evaluateNum(effect.amount, ctx, source, hero)));
      hero.poison += amount;
      hero.poisonSource = source.side;
      hero.poisonSourceItemId = source.id;
      hero.poisonSourceCardId = source.cardId;
      pushEvent(ctx, {
        time,
        side: source.side,
        targetSide: hero.side,
        sourceItemId: source.id,
        sourceCardId: source.cardId,
        kind: 'poison',
        value: amount,
        chainId: ch,
        depth,
      });
      emitTrigger(ctx, 'onPoisonApplied', source, hero.side, depth, undefined, ch);
      return;
    }
    case 'regen': {
      const hero = heroFor(ctx, source, effect.target, source.side);
      const amount = Math.max(0, Math.round(evaluateNum(effect.amount, ctx, source, hero)));
      hero.regen += amount;
      hero.regenSourceItemId = source.id;
      hero.regenSourceCardId = source.cardId;
      pushEvent(ctx, {
        time,
        side: source.side,
        targetSide: hero.side,
        sourceItemId: source.id,
        sourceCardId: source.cardId,
        kind: 'regen',
        value: amount,
        chainId: ch,
        depth,
      });
      emitTrigger(ctx, 'onRegen', source, hero.side, depth, undefined, ch);
      return;
    }
    case 'haste':
    case 'slow':
    case 'freeze':
    case 'charge': {
      const targets = itemsForTarget(ctx, source, effect.target, effect.count ?? 1);
      for (const item of targets) {
        if (effect.kind === 'haste') {
          item.hasteUntil = Math.max(item.hasteUntil, time + effect.seconds);
        } else if (effect.kind === 'slow') {
          item.slowUntil = Math.max(item.slowUntil, time + effect.seconds);
        } else if (effect.kind === 'freeze') {
          item.frozenUntil = Math.max(item.frozenUntil, time + effect.seconds);
        } else {
          item.cooldown = Math.max(0, item.cooldown - effect.seconds);
        }

        pushEvent(ctx, {
          time,
          side: source.side,
          targetSide: item.side,
          sourceItemId: source.id,
          targetItemId: item.id,
          kind: effect.kind,
          value: effect.seconds,
          chainId: ch,
          depth,
        });

        if (effect.kind === 'haste') {
          emitTrigger(ctx, 'onHaste', source, item.side, depth, item.id, ch);
        } else if (effect.kind === 'slow') {
          emitTrigger(ctx, 'onSlow', source, item.side, depth, item.id, ch);
        } else if (effect.kind === 'freeze') {
          emitTrigger(ctx, 'onFreeze', source, item.side, depth, item.id, ch);
        }
      }
      return;
    }
    case 'reload': {
      const targets = itemsForTarget(ctx, source, effect.target, effect.count ?? 1);
      for (const item of targets) {
        if (item.ammoMax == null) continue;
        const before = item.ammo;
        item.ammo = Math.min(item.ammoMax, item.ammo + effect.amount);
        pushEvent(ctx, {
          time,
          side: source.side,
          targetSide: item.side,
          sourceItemId: source.id,
          targetItemId: item.id,
          kind: 'reload',
          value: item.ammo - before,
          chainId: ch,
          depth,
        });
        emitTrigger(ctx, 'onReload', source, item.side, depth, undefined, ch);
      }
      return;
    }
    case 'ammo': {
      source.ammoMax = (source.ammoMax ?? 0) + effect.amount;
      source.ammo += effect.amount;
      pushEvent(ctx, {
        time,
        side: source.side,
        sourceItemId: source.id,
        kind: 'ammo',
        value: effect.amount,
        chainId: ch,
        depth,
      });
      return;
    }
    case 'crit':
    case 'multicast':
    case 'lifesteal':
      return;
    default:
      return;
  }
}
