import type { CardDef } from '../engine/types';
import { CardThumb } from './CardThumb';

export interface CardChipProps {
  card: CardDef;
  cooldown: number;
  selected: boolean;
  onClick: () => void;
}

export function CardChip({ card, cooldown, selected, onClick }: CardChipProps) {
  return (
    <button
      type="button"
      className={`card-chip${selected ? ' selected' : ''}`}
      onClick={onClick}
    >
      <CardThumb card={card} size={24} />
      <span className="card-chip-name">{card.nameCn ?? card.name}</span>
      <span className="card-chip-cd">{cooldown.toFixed(1)}s</span>
    </button>
  );
}
