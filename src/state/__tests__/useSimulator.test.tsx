import { renderHook, act } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import { useSimulator } from '../useSimulator';

describe('useSimulator', () => {
  it('runs a battle and advances the playhead', () => {
    const { result } = renderHook(() => useSimulator());
    act(() => result.current.run());
    expect(result.current.result).not.toBeNull();
    act(() => result.current.setPlayhead(5));
    expect(result.current.playhead).toBe(5);
    act(() => result.current.play());
    act(() => result.current.advance(1));
    expect(result.current.playhead).toBeGreaterThan(5);
  });

  it('runs with an explicit player loadout', () => {
    const { result } = renderHook(() => useSimulator());
    const player = { items: [] };
    act(() => result.current.run({ player }));
    expect(result.current.player).toEqual(player);
    expect(result.current.result).not.toBeNull();
  });
});
