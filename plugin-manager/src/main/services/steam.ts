import { promises as fs } from 'node:fs';
import path from 'node:path';
import { GAME_DIR_NAME, GAME_EXE, STEAM_APP_ID } from '@shared/constants';
import type { GameSource } from '@shared/types';
import { runCommand } from './exec';
import { isDirectory, pathExists, readTextIfExists } from './fsutil';

export interface GameCandidate {
  path: string;
  source: Exclude<GameSource, 'manual'>;
}

/** 解析 Steam libraryfolders.vdf 中的所有库路径 */
export function parseVdfPaths(text: string): string[] {
  const results: string[] = [];
  const re = /"path"\s+"([^"]+)"/gi;
  for (const match of text.matchAll(re)) {
    const value = match[1].replace(/\\\\/g, '\\').trim();
    if (!value) continue;
    const normalized = path.normalize(value);
    if (!results.some((item) => item.toLowerCase() === normalized.toLowerCase())) {
      results.push(normalized);
    }
  }
  return results;
}

export interface AppManifest {
  appid: string;
  installdir: string;
  name: string;
}

/** 解析 appmanifest_*.acf */
export function parseAppManifest(text: string): AppManifest | null {
  const appid = /"appid"\s+"(\d+)"/i.exec(text)?.[1];
  const installdir = /"installdir"\s+"([^"]+)"/i.exec(text)?.[1];
  const name = /"name"\s+"([^"]+)"/i.exec(text)?.[1] ?? '';
  if (!appid || !installdir) return null;
  return { appid, installdir, name };
}

export async function isGameDir(dir: string): Promise<boolean> {
  if (!(await isDirectory(dir))) return false;
  return pathExists(path.join(dir, GAME_EXE));
}

async function registryValue(key: string, name: string): Promise<string | null> {
  try {
    const output = await runCommand('reg', ['query', key, '/v', name]);
    const line = output
      .split(/\r?\n/)
      .map((item) => item.trim())
      .find((item) => item.toLowerCase().startsWith(name.toLowerCase()));
    if (!line) return null;
    const parts = line.split(/\s{2,}/).filter(Boolean);
    return parts.length >= 2 ? parts[parts.length - 1].trim() : null;
  } catch {
    return null;
  }
}

async function existingDriveRoots(): Promise<string[]> {
  const drives: string[] = [];
  const letters = 'CDEFGHIJKLMNOPQRSTUVWXYZ'.split('');
  await Promise.all(
    letters.map(async (letter) => {
      const root = `${letter}:\\`;
      try {
        await fs.access(root);
        drives.push(root);
      } catch {
        // 盘符不存在
      }
    })
  );
  return drives.sort();
}

export async function detectSteamRoots(): Promise<string[]> {
  const roots = new Set<string>();
  const fromRegistry = [
    await registryValue('HKCU\\Software\\Valve\\Steam', 'SteamPath'),
    await registryValue('HKLM\\SOFTWARE\\WOW6432Node\\Valve\\Steam', 'InstallPath'),
    await registryValue('HKLM\\SOFTWARE\\Valve\\Steam', 'InstallPath')
  ];
  for (const value of fromRegistry) {
    if (value) roots.add(path.normalize(value.replace(/\//g, '\\')));
  }

  if (process.platform === 'win32') {
    const drives = await existingDriveRoots();
    for (const drive of drives) {
      for (const rel of [
        'Program Files (x86)\\Steam',
        'Program Files\\Steam',
        'Steam',
        'SteamLibrary',
        'Games\\Steam',
        'Games\\SteamLibrary'
      ]) {
        roots.add(path.join(drive, rel));
      }
    }
  }

  return [...roots];
}

async function steamLibraries(steamRoot: string): Promise<string[]> {
  const libraries = [steamRoot];
  const vdf = await readTextIfExists(path.join(steamRoot, 'steamapps', 'libraryfolders.vdf'));
  if (vdf) {
    for (const lib of parseVdfPaths(vdf)) {
      if (!libraries.some((item) => item.toLowerCase() === lib.toLowerCase())) {
        libraries.push(lib);
      }
    }
  }
  return libraries;
}

/** 在一个 Steam 库中定位 The Bazaar */
export async function findGameInLibrary(library: string): Promise<string | null> {
  const steamapps = path.join(library, 'steamapps');
  const manifest = await readTextIfExists(path.join(steamapps, `appmanifest_${STEAM_APP_ID}.acf`));
  if (manifest) {
    const parsed = parseAppManifest(manifest);
    if (parsed && parsed.appid === STEAM_APP_ID) {
      const dir = path.join(steamapps, 'common', parsed.installdir);
      if (await isGameDir(dir)) return dir;
    }
  }
  const fallback = path.join(steamapps, 'common', GAME_DIR_NAME);
  if (await isGameDir(fallback)) return fallback;
  return null;
}

/** 按优先级返回所有可用的游戏目录候选 */
export async function findGameCandidates(savedPath: string | null): Promise<GameCandidate[]> {
  const candidates: GameCandidate[] = [];
  const seen = new Set<string>();
  const push = async (dir: string, source: GameCandidate['source']) => {
    const resolved = path.resolve(dir);
    const key = resolved.toLowerCase();
    if (seen.has(key)) return;
    if (await isGameDir(resolved)) {
      seen.add(key);
      candidates.push({ path: resolved, source });
    }
  };

  if (savedPath) await push(savedPath, 'saved');

  for (const root of await detectSteamRoots()) {
    for (const library of await steamLibraries(root)) {
      const game = await findGameInLibrary(library);
      if (game) await push(game, 'steam');
    }
  }

  for (const drive of await existingDriveRoots()) {
    for (const rel of [
      `Program Files (x86)\\Steam\\steamapps\\common\\${GAME_DIR_NAME}`,
      `Program Files\\Steam\\steamapps\\common\\${GAME_DIR_NAME}`,
      `Steam\\steamapps\\common\\${GAME_DIR_NAME}`,
      `SteamLibrary\\steamapps\\common\\${GAME_DIR_NAME}`,
      `Games\\SteamLibrary\\steamapps\\common\\${GAME_DIR_NAME}`
    ]) {
      await push(path.join(drive, rel), 'scan');
    }
  }

  return candidates;
}
