import { describe, it, expect } from 'vitest';
import cards from '../../data/cards.json';
import report from '../../data/parse_report.json';

describe('generated card data', () => {
  it('contains at least 500 structured cards', () => {
    expect((cards as any[]).filter((c) => c.parseStatus === 'structured').length).toBeGreaterThanOrEqual(500);
  });

  it('contains at least 80 game-db cards', () => {
    expect((cards as any[]).filter((c) => c.dataSource === 'game-db').length).toBeGreaterThanOrEqual(80);
  });

  it('has no duplicate ids', () => {
    const ids = (cards as any[]).map((c) => c.id);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it('marks zero-cooldown cards passive', () => {
    const zero = (cards as any[]).filter((c) => c.cooldown <= 0);
    expect(zero.length).toBeGreaterThan(0);
    expect(zero.every((c) => c.passive === true)).toBe(true);
  });

  it('does not turn game-db OnItemUsed into onUse triggers', () => {
    const offending = (cards as any[]).filter(
      (c) =>
        c.dataSource === 'game-db' &&
        c.triggers.some((trigger: any) => trigger.event === 'onUse'),
    );
    expect(offending).toHaveLength(0);
  });

  it('report has per-source counts', () => {
    expect((report as any).byDataSource['game-db']).toBeGreaterThanOrEqual(80);
    expect(
      (report as any).byDataSource.tooltip + (report as any).byDataSource.merged,
    ).toBeGreaterThan(0);
  });

  it('report distinguishes GUID matches from tier matches', () => {
    expect((report as any).guidIntersection).toBeGreaterThanOrEqual(80);
    expect((report as any).extraGameDbEntries).toBeGreaterThan(0);
  });
});
