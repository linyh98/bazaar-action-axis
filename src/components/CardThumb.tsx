import { useState } from 'react';
import type { CardDef } from '../engine/types';

export interface CardThumbProps {
  card: CardDef;
  size?: number;
}

export function CardThumb({ card, size = 32 }: CardThumbProps) {
  const itemId = card.baseItemId ?? card.id.split('@')[0];
  const local = itemId ? `/images/${itemId}.avif` : undefined;
  const [src, setSrc] = useState<string | undefined>(local ?? card.image);
  const [failed, setFailed] = useState(false);

  if (!src || failed) {
    return (
      <span
        className="card-thumb placeholder"
        style={{ width: size, height: size }}
        aria-hidden="true"
      />
    );
  }

  return (
    <img
      src={src}
      alt={card.nameCn ?? card.name}
      width={size}
      height={size}
      className="card-thumb"
      onError={() => {
        if (card.image && src !== card.image) setSrc(card.image);
        else setFailed(true);
      }}
    />
  );
}
