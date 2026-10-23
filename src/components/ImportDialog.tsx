import { useMemo, useState } from 'react';
import type { CardDef, Tier } from '../engine/types';
import { CardThumb } from './CardThumb';

export interface ImportDialogProps {
  open: boolean;
  cards: Record<string, CardDef>;
  onPick: (cardId: string) => void;
  onClose: () => void;
}

export function ImportDialog({ open, cards, onPick, onClose }: ImportDialogProps) {
  const [query, setQuery] = useState('');
  const [tier, setTier] = useState<Tier | 'all'>('all');

  const list = useMemo(() => {
    const q = query.trim().toLowerCase();
    return Object.values(cards)
      .filter((card) => tier === 'all' || card.tier === tier)
      .filter((card) => {
        if (!q) return true;
        return (
          card.name.toLowerCase().includes(q) ||
          (card.nameCn ?? '').toLowerCase().includes(q)
        );
      })
      .slice(0, 120);
  }, [cards, query, tier]);

  if (!open) return null;

  return (
    <div className="dialog-backdrop" onClick={onClose}>
      <div className="panel dialog" onClick={(event) => event.stopPropagation()}>
        <h3>导入图鉴卡牌</h3>
        <input
          placeholder="搜索卡牌"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
        />
        <select
          value={tier}
          onChange={(event) => setTier(event.target.value as Tier | 'all')}
        >
          <option value="all">全部品阶</option>
          <option value="Bronze">青铜</option>
          <option value="Silver">白银</option>
          <option value="Gold">黄金</option>
          <option value="Diamond">钻石</option>
          <option value="Legendary">传奇</option>
        </select>

        <div className="dim">共 {list.length} 张</div>

        <div className="import-list">
          {list.map((card) => (
            <button
              key={card.id}
              type="button"
              className="import-item"
              onClick={() => onPick(card.id)}
            >
              <span className="import-item-main">
                <CardThumb card={card} />
                <span>{card.nameCn ?? card.name}</span>
              </span>
              <span className="dim">
                {card.tier} · {card.cooldown}s · {card.parseStatus}
              </span>
            </button>
          ))}
        </div>

        <button type="button" onClick={onClose}>
          关闭
        </button>
      </div>
    </div>
  );
}
