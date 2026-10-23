import type { BattleContext } from './battle';
import type { BattleEvent } from './types';

export function pushEvent(
  ctx: BattleContext,
  event: Omit<BattleEvent, 'id' | 'chainId' | 'depth'> & {
    chainId?: number;
    depth?: number;
  },
): BattleEvent {
  ctx.eventsThisTick += 1;
  const full: BattleEvent = {
    id: ctx.nextEventId++,
    time: event.time,
    side: event.side,
    sourceItemId: event.sourceItemId,
    sourceCardId: event.sourceCardId,
    kind: event.kind,
    targetSide: event.targetSide,
    targetItemId: event.targetItemId,
    value: event.value,
    absorbed: event.absorbed,
    hpAfter: event.hpAfter,
    shieldAfter: event.shieldAfter,
    chainId: event.chainId ?? ctx.nextChainId++,
    depth: event.depth ?? 0,
    note: event.note,
  };
  ctx.events.push(full);
  return full;
}
