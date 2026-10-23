import path from 'node:path';
import { GAME_EXE, STEAM_APP_ID } from '@shared/constants';
import type { BepInExState, GameState } from '@shared/types';
import { isDirectory, pathExists, readTextIfExists } from './fsutil';
import { isProcessRunning } from './process';
import { findGameCandidates, isGameDir } from './steam';

/** 读取 BepInEx 安装状态（版本尽量从日志首行推断） */
export async function readBepInEx(gamePath: string): Promise<BepInExState> {
  const root = path.join(gamePath, 'BepInEx');
  const installed = await pathExists(path.join(root, 'core', 'BepInEx.dll'));
  let version: string | null = null;
  if (installed) {
    const log = await readTextIfExists(path.join(root, 'LogOutput.log'), 256 * 1024);
    const match = log ? /BepInEx\s+([\d][\w.\-]*)/i.exec(log.slice(0, 4000)) : null;
    if (match) version = match[1];
  }
  return {
    installed,
    version,
    pluginsDir: path.join(root, 'plugins'),
    configDir: path.join(root, 'config')
  };
}

export async function getGameState(savedPath: string | null): Promise<GameState> {
  const candidates = await findGameCandidates(savedPath);
  const running = await isProcessRunning(GAME_EXE);

  if (candidates.length === 0) {
    return {
      found: false,
      path: null,
      exePath: null,
      source: null,
      bepinex: null,
      running,
      steamAppId: STEAM_APP_ID
    };
  }

  const best = candidates[0];
  return {
    found: true,
    path: best.path,
    exePath: path.join(best.path, GAME_EXE),
    source: best.source,
    bepinex: await readBepInEx(best.path),
    running,
    steamAppId: STEAM_APP_ID
  };
}

/** 手动选择的目录是否是一个有效的游戏根目录 */
export async function validateGamePath(dir: string): Promise<boolean> {
  if (!(await isDirectory(dir))) return false;
  return isGameDir(dir);
}
