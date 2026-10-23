import type { GameSource } from '@shared/types';

export function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes <= 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  const digits = value >= 100 || unit === 0 ? 0 : value >= 10 ? 1 : 2;
  return `${value.toFixed(digits)} ${units[unit]}`;
}

export function formatCount(count: number): string {
  return count >= 10000 ? `${(count / 10000).toFixed(1)} 万` : String(count);
}

export function formatDate(ms: number): string {
  if (!ms) return '未知';
  const date = new Date(ms);
  const now = Date.now();
  const diff = now - ms;
  if (diff >= 0 && diff < 60_000) return '刚刚';
  if (diff >= 0 && diff < 3_600_000) return `${Math.floor(diff / 60_000)} 分钟前`;
  if (diff >= 0 && diff < 86_400_000) return `${Math.floor(diff / 3_600_000)} 小时前`;
  return date.toLocaleDateString('zh-CN', { year: 'numeric', month: '2-digit', day: '2-digit' });
}

export function sourceLabel(source: GameSource | null): string {
  switch (source) {
    case 'saved':
      return '上次记录';
    case 'steam':
      return 'Steam 自动检测';
    case 'scan':
      return '磁盘扫描';
    case 'manual':
      return '手动选择';
    default:
      return '未检测';
  }
}

export function shortPath(target: string, max = 68): string {
  if (target.length <= max) return target;
  const head = target.slice(0, Math.floor(max / 2) - 2);
  const tail = target.slice(-Math.floor(max / 2));
  return `${head}…${tail}`;
}
