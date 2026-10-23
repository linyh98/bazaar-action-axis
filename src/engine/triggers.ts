import type { BattleContext } from './battle';
import { resolveEffect } from './effects';
import type { TriggerEvent, TriggerPayload } from './types';

const SOURCE_SCOPED = new Set<TriggerEvent>([
  'onHeal',
  'onShield',
  'onBurnApplied',
  'onPoisonApplied',
  'onRegen',
  'onHaste',
  'onSlow',
  'onFreeze',
  'onCrit',
  'onReload',
]);

export function enqueueTrigger(ctx: BattleContext, payload: TriggerPayload): void {
  ctx.triggerQueue.push(payload);
}

export function drainTriggerQueue(ctx: BattleContext, time: number): void {
  while (ctx.triggerQueue.length > 0) {
    if (ctx.player.hp <= 0 || ctx.enemy.hp <= 0) {
      ctx.triggerQueue.length = 0;
      break;
    }

    if (ctx.eventsThisTick > ctx.rules.maxEventsPerTick) {
      ctx.warnings.push(`event cap reached at t=${time}`);
      ctx.events.push({
        id: ctx.nextEventId++,
        time,
        side: 'player',
        kind: 'warning',
        chainId: ctx.nextChainId++,
        depth: 0,
        value: 0,
        note: 'event cap reached',
      });
      ctx.triggerQueue.length = 0;
      break;
    }

    const payload = ctx.triggerQueue.shift()!;
    if (payload.depth > ctx.rules.maxTriggerDepth) {
      ctx.warnings.push(`trigger depth cap reached at t=${time}`);
      ctx.events.push({
        id: ctx.nextEventId++,
        time,
        side: payload.sourceSide,
        kind: 'warning',
        chainId: payload.chainId ?? ctx.nextChainId++,
        depth: 0,
        value: 0,
        note: 'trigger depth cap reached',
      });
      continue;
    }

    const sourceItem = payload.sourceItemId
      ? ctx.items.find((item) => item.id === payload.sourceItemId)
      : undefined;

    for (const item of ctx.items) {
      if (payload.event === 'onDamageTaken') {
        if (item.side !== payload.targetSide) continue;
      } else if (
        payload.event === 'onDamageDealt' ||
        payload.event === 'onUse' ||
        SOURCE_SCOPED.has(payload.event)
      ) {
        if (item.side !== payload.sourceSide) continue;
      }

      for (const trigger of item.card.triggers) {
        if (trigger.event !== payload.event) continue;
        if (
          trigger.filter?.sourceItemId &&
          trigger.filter.sourceItemId !== payload.sourceItemId
        ) {
          continue;
        }
        if (trigger.filter?.sourceTag) {
          if (!sourceItem || !sourceItem.card.tags.includes(trigger.filter.sourceTag)) {
            continue;
          }
        }
        if (trigger.filter?.targetSide && trigger.filter.targetSide !== payload.targetSide) {
          continue;
        }
        if (trigger.filter?.self && payload.targetItemId !== item.id) {
          continue;
        }

        for (const effect of trigger.effects) {
          resolveEffect(ctx, effect, item, time, payload.depth + 1, payload.chainId);
        }
      }
    }
  }
}
