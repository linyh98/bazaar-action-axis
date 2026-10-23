import type { CardDef, Loadout, Side } from '../engine/types';

export interface LoadoutEditorProps {
  side: Side;
  loadout: Loadout;
  cards: Record<string, CardDef>;
  maxSlots: number;
  onChange: (loadout: Loadout) => void;
  onOpenImport: (side: Side) => void;
}

export function LoadoutEditor({
  side,
  loadout,
  cards,
  maxSlots,
  onChange,
  onOpenImport,
}: LoadoutEditorProps) {
  const items = [...loadout.items].sort((a, b) => a.slot - b.slot);

  const remove = (slot: number) => {
    onChange({ items: loadout.items.filter((item) => item.slot !== slot) });
  };

  return (
    <div className="panel loadout-editor">
      <div className="loadout-header">
        <h3>{side === 'player' ? '我方卡组' : '敌方卡组'}</h3>
        <button
          type="button"
          onClick={() => onOpenImport(side)}
          disabled={items.length >= maxSlots}
        >
          添加卡牌
        </button>
      </div>

      {items.length === 0 && <p className="dim">还没有卡牌</p>}

      <div className="loadout-items">
        {items.map((item) => {
          const card = cards[item.cardId];
          const name = card?.nameCn ?? card?.name ?? item.cardId;
          return (
            <div key={`${item.cardId}:${item.slot}`} className="loadout-item">
              <span>{name}</span>
              <span className="dim">
                {card?.tier ?? '?'} · {card?.cooldown ?? 0}s
              </span>
              <button
                type="button"
                aria-label={`移除 ${name}`}
                onClick={() => remove(item.slot)}
              >
                ×
              </button>
            </div>
          );
        })}
      </div>

      {items.length >= maxSlots && <p className="dim">已达到 {maxSlots} 张上限</p>}
    </div>
  );
}
