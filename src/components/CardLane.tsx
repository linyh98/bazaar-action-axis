import type { BattleEvent, CardDef, Side, Snapshot } from '../engine/types';
import { CardChip } from './CardChip';

export interface CardLaneProps {
  side: Side;
  label: string;
  card?: CardDef;
  itemId?: string;
  hero?: Snapshot['player'];
  events: BattleEvent[];
  endTime: number;
  selectedEventId: number | null;
  onSelectEvent: (id: number) => void;
  selectedItemId: string | null;
  onSelectItem: (id: string) => void;
}

export function CardLane({
  side,
  label,
  card,
  itemId,
  hero,
  events,
  endTime,
  selectedEventId,
  onSelectEvent,
  selectedItemId,
  onSelectItem,
}: CardLaneProps) {
  const laneEvents = itemId
    ? events.filter((event) => event.side === side && event.sourceItemId === itemId)
    : [];
  const itemSnapshot = itemId
    ? hero?.items.find((item) => item.itemId === itemId)
    : undefined;

  return (
    <div className={`timeline-lane side-${side}`} data-testid="timeline-lane">
      <div className="lane-label">
        {card && itemId ? (
          <CardChip
            card={card}
            cooldown={itemSnapshot?.cooldown ?? card.cooldown}
            selected={selectedItemId === itemId}
            onClick={() => onSelectItem(itemId)}
          />
        ) : (
          <span>{label}</span>
        )}
      </div>
      <div className="lane-track">
        {hero && !itemId && (
          <span className="hero-stats">
            HP {hero.hp}/{hero.maxHp} · 盾 {hero.shield} · 灼烧 {hero.burn} · 中毒 {hero.poison}
          </span>
        )}
        {laneEvents.map((event) => (
          <button
            key={event.id}
            type="button"
            data-testid={`event-marker-${event.id}`}
            className={`event-marker kind-${event.kind}${
              selectedEventId === event.id ? ' selected' : ''
            }`}
            style={{ left: `${(event.time / Math.max(endTime, 1)) * 100}%` }}
            onClick={() => onSelectEvent(event.id)}
            title={`${event.time.toFixed(1)}s ${event.kind}`}
          />
        ))}
      </div>
    </div>
  );
}
