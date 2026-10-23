import { app } from 'electron';
import { appendFile, mkdir } from 'node:fs/promises';
import path from 'node:path';

let cachedLogPath: string | null = null;

export function getLogPath(): string {
  if (!cachedLogPath) {
    cachedLogPath = path.join(app.getPath('userData'), 'logs', 'manager.log');
  }
  return cachedLogPath;
}

/** 追加一行日志；写日志失败不影响主流程 */
export async function log(message: string): Promise<void> {
  try {
    const target = getLogPath();
    await mkdir(path.dirname(target), { recursive: true });
    const line = `[${new Date().toISOString()}] ${message}\n`;
    await appendFile(target, line, 'utf8');
  } catch {
    // ignore
  }
}

export function describeError(error: unknown): string {
  if (error instanceof Error) return error.message;
  return String(error);
}
