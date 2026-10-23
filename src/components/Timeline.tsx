import type { BattleResult, CardDef } from '../engine/types';
import { CardLane } from './CardLane';

export interface TimelineProps {
  result: BattleResult;
  cards: Record<string, CardDef>;
  playhead: number;
  onPlayheadChange: (t: number) => void;
  selectedEventId: number | null;
  onSelectEvent: (id: number) => void;
  selectedItemId: string | null;
  onSelectItem: (id: string) => void;
}

export function Timeline({
  result,
  cards,
  playhead,
  onPlayheadChange,
  selectedEventId,
  onSelectEvent,
  selectedItemId,
  onSelectItem,
}: TimelineProps) {
  const first = result.snapshots[0];
  const current =
    [...result.snapshots].reverse().find((snapshot) => snapshot.time <= playhead + 1e-9) ??
    first;

  const common = {
    events: result.events,
    endTime: result.endTime,
    selectedEventId,
    onSelectEvent,
    selectedItemId,
    onSelectItem,
  };

  return (
    <div className="panel timeline">
      <input
        type="range"
        aria-label="时间轴播放头"
        min={0}
        max={result.endTime}
        step={0.1}
        value={Math.min(playhead, result.endTime)}
        onChange={(event) => onPlayheadChange(Number(event.target.value))}
        className="timeline-slider"
      />

      <div className="timeline-lanes">
        <CardLane side="player" label="我方英雄" hero={current.player} {...common} />
        {first.player.items.map((item) => (
          <CardLane
            key={`player-${item.itemId}`}
            side="player"
            label={cards[item.cardId]?.name ?? item.cardId}
            card={cards[item.cardId]}
            itemId={item.itemId}
            hero={current.player}
            {...common}
          />
        ))}

        <CardLane side="enemy" label="敌方英雄" hero={current.enemy} {...common} />
        {first.enemy.items.map((item) => (
          <CardLane
            key={`enemy-${item.itemId}`}
            side="enemy"
            label={cards[item.cardId]?.name ?? item.cardId}
            card={cards[item.cardId]}
            itemId={item.itemId}
            hero={current.enemy}
            {...common}
          />
        ))}
      </div>
    </div>
  );
}
