import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import { LoadoutEditor } from '../LoadoutEditor';
import { ImportDialog } from '../ImportDialog';
import type { CardDef } from '../../engine/types';

const c = (id: string, name: string, nameCn?: string): CardDef => ({
  id,
  name,
  nameCn,
  size: 'Small',
  tier: 'Bronze',
  cooldown: 4,
  effects: [],
  triggers: [],
  tags: [],
  parseStatus: 'structured',
  rawTooltips: [],
});

describe('LoadoutEditor', () => {
  it('removes an item', () => {
    const onChange = vi.fn();
    render(
      <LoadoutEditor
        side="player"
        loadout={{ items: [{ cardId: 'a', slot: 0 }] }}
        cards={{ a: c('a', 'A') }}
        maxSlots={10}
        onChange={onChange}
        onOpenImport={() => {}}
      />,
    );
    fireEvent.click(screen.getByLabelText('移除 A'));
    expect(onChange).toHaveBeenCalledWith({ items: [] });
  });
});

describe('ImportDialog', () => {
  it('filters by chinese name and picks', () => {
    const onPick = vi.fn();
    render(
      <ImportDialog
        open
        cards={{ a: c('a', 'Ambergris', '龙涎香'), b: c('b', 'Anchor', '船锚') }}
        onPick={onPick}
        onClose={() => {}}
      />,
    );
    fireEvent.change(screen.getByPlaceholderText('搜索卡牌'), {
      target: { value: '龙涎' },
    });
    expect(screen.getByText('龙涎香')).toBeTruthy();
    expect(screen.queryByText('船锚')).toBeNull();
    fireEvent.click(screen.getByText('龙涎香'));
    expect(onPick).toHaveBeenCalledWith('a');
  });
});
