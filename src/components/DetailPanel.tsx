import type { BattleEvent, CardDef } from '../engine/types';

export interface DetailPanelProps {
  events: BattleEvent[];
  cards: Record<string, CardDef>;
  selectedEventId: number | null;
  selectedItemId: string | null;
}

function EventRow({
  event,
  cards,
}: {
  event: BattleEvent;
  cards: Record<string, CardDef>;
}) {
  const sourceKey = event.sourceCardId ?? event.sourceItemId ?? '';
  const source = cards[sourceKey];
  const sourceLabel =
    source?.nameCn ?? source?.name ?? event.sourceCardId ?? event.sourceItemId ?? '系统';
  return (
    <div className="detail-row">
      事件 #{event.id} · {event.time.toFixed(1)}s · {sourceLabel} · {event.kind} · 值{' '}
      {event.value ?? 0} · 吸收 {event.absorbed ?? 0} · HP {event.hpAfter ?? '-'}
    </div>
  );
}

export function DetailPanel({
  events,
  cards,
  selectedEventId,
  selectedItemId,
}: DetailPanelProps) {
  if (selectedEventId !== null) {
    const selected = events.find((event) => event.id === selectedEventId);
    if (!selected) {
      return <div className="panel detail-panel">未找到事件 #{selectedEventId}</div>;
    }
    const chain = events
      .filter((event) => event.chainId === selected.chainId)
      .sort((a, b) => a.id - b.id);
    return (
      <div className="panel detail-panel">
        <h3>触发链 #{selected.chainId}</h3>
        {chain.map((event) => (
          <EventRow key={event.id} event={event} cards={cards} />
        ))}
      </div>
    );
  }

  if (selectedItemId) {
    const list = events
      .filter((event) => event.sourceItemId === selectedItemId)
      .sort((a, b) => a.id - b.id);
    const name =
      cards[selectedItemId]?.nameCn ?? cards[selectedItemId]?.name ?? selectedItemId;
    return (
      <div className="panel detail-panel">
        <h3>{name} 的事件</h3>
        {list.map((event) => (
          <EventRow key={event.id} event={event} cards={cards} />
        ))}
      </div>
    );
  }

  return <div className="panel detail-panel">点击卡牌或触发点查看详情</div>;
}
