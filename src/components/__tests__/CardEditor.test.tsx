import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import { CardEditor } from '../CardEditor';

describe('CardEditor', () => {
  it('rejects an empty name', () => {
    const onSave = vi.fn();
    render(<CardEditor onSave={onSave} onCancel={() => {}} />);
    fireEvent.change(screen.getByLabelText('冷却（秒）'), { target: { value: '4' } });
    fireEvent.click(screen.getByText('保存'));
    expect(screen.getByText('名称不能为空')).toBeTruthy();
    expect(onSave).not.toHaveBeenCalled();
  });

  it('rejects cooldown below 0.1', () => {
    render(<CardEditor onSave={() => {}} onCancel={() => {}} />);
    fireEvent.change(screen.getByLabelText('名称'), { target: { value: '测试卡' } });
    fireEvent.change(screen.getByLabelText('冷却（秒）'), { target: { value: '0' } });
    fireEvent.click(screen.getByText('保存'));
    expect(screen.getByText('冷却必须 ≥ 0.1 秒')).toBeTruthy();
  });

  it('saves a valid custom card', () => {
    const onSave = vi.fn();
    render(<CardEditor onSave={onSave} onCancel={() => {}} />);
    fireEvent.change(screen.getByLabelText('名称'), { target: { value: '测试卡' } });
    fireEvent.change(screen.getByLabelText('冷却（秒）'), { target: { value: '4' } });
    fireEvent.click(screen.getByText('添加效果'));
    fireEvent.click(screen.getByText('保存'));
    expect(onSave).toHaveBeenCalledTimes(1);
    expect(onSave.mock.calls[0][0].id).toMatch(/^custom-/);
  });
});
