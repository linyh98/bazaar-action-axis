import { app } from 'electron';
import { promises as fs } from 'node:fs';
import path from 'node:path';
import type { AppConfig } from '@shared/types';
import { readTextIfExists } from './services/fsutil';

const DEFAULTS: AppConfig = { gamePath: null, autoDetect: true };

let cached: AppConfig | null = null;

export function configFilePath(): string {
  return path.join(app.getPath('userData'), 'config.json');
}

export async function loadConfig(): Promise<AppConfig> {
  if (cached) return cached;
  const text = await readTextIfExists(configFilePath(), 1024 * 1024);
  if (!text) {
    cached = { ...DEFAULTS };
    return cached;
  }
  try {
    const parsed = JSON.parse(text) as Partial<AppConfig>;
    cached = {
      gamePath: typeof parsed.gamePath === 'string' && parsed.gamePath.trim() ? parsed.gamePath : null,
      autoDetect: parsed.autoDetect !== false
    };
  } catch {
    cached = { ...DEFAULTS };
  }
  return cached;
}

export async function saveConfig(patch: Partial<AppConfig>): Promise<AppConfig> {
  const current = await loadConfig();
  const next: AppConfig = {
    gamePath: patch.gamePath !== undefined ? patch.gamePath : current.gamePath,
    autoDetect: patch.autoDetect !== undefined ? patch.autoDetect : current.autoDetect
  };
  cached = next;
  await fs.mkdir(path.dirname(configFilePath()), { recursive: true });
  await fs.writeFile(configFilePath(), `${JSON.stringify(next, null, 2)}\n`, 'utf8');
  return next;
}
