import { shell } from 'electron';
import path from 'node:path';
import { BPP_INSTALLER_DIRS } from '@shared/constants';
import type { DataGroup, DataTarget, GameState } from '@shared/types';
import { describeError, log } from '../logger';
import { measurePath, pathExists, readDirSafe } from './fsutil';

export interface DataContext {
  game: GameState;
  workspaceRoot: string;
  appDataDir: string;
}

interface TargetInput {
  id: string;
  group: DataGroup;
  name: string;
  description: string;
  path: string;
  paths: string[];
  requiresGameStopped: boolean;
}

async function finalizeTarget(input: TargetInput): Promise<DataTarget | null> {
  const uniquePaths = [...new Set(input.paths.map((item) => path.resolve(item)))];
  const measurements = await Promise.all(uniquePaths.map((item) => measurePath(item)));
  const existing = measurements.filter((item) => item.exists);
  if (existing.length === 0) return null;

  return {
    ...input,
    paths: uniquePaths,
    kind: existing.some((item) => item.kind === 'dir') ? 'dir' : 'file',
    exists: true,
    sizeBytes: existing.reduce((sum, item) => sum + item.sizeBytes, 0),
    itemCount: existing.reduce((sum, item) => sum + item.itemCount, 0)
  };
}

const CONFIG_PATTERN = /bazaarplusplus|bazaarsim|actionaxis|livecards/i;

/** 扫描所有可清理的本地数据目标 */
export async function collectDataTargets(context: DataContext): Promise<DataTarget[]> {
  const targets: DataTarget[] = [];
  const gamePath = context.game.path;

  if (gamePath) {
    for (const entry of await readDirSafe(gamePath)) {
      if (!entry.isDirectory() || !/^BazaarPlusPlusV\d+$/i.test(entry.name)) continue;
      const dir = path.join(gamePath, entry.name);
      const target = await finalizeTarget({
        id: `bpp-data:${entry.name}`,
        group: 'bpp',
        name: `BPP 数据（${entry.name}）`,
        description: 'BPP 保存的对局历史、回放与本地数据库。删除后历史记录和回放无法恢复。',
        path: dir,
        paths: [dir],
        requiresGameStopped: true
      });
      if (target) targets.push(target);
    }

    const pluginsDir = context.game.bepinex?.pluginsDir ?? path.join(gamePath, 'BepInEx', 'plugins');
    const actionDir = path.join(pluginsDir, 'BazaarActionAxis');
    const runtimeTarget = await finalizeTarget({
      id: 'actionaxis-runtime',
      group: 'actionaxis',
      name: '行动轴运行数据',
      description: '最近一次棋盘快照、技能 / 怪物报告与实战回放。删除后下次打开面板会重新生成。',
      path: actionDir,
      paths: [
        path.join(actionDir, 'last_snapshot.json'),
        path.join(actionDir, 'last_skill_report.txt'),
        path.join(actionDir, 'last_monster_choices.json'),
        path.join(actionDir, 'last_monster_report.txt'),
        path.join(actionDir, 'last_combat_replay.json'),
        path.join(actionDir, 'combats')
      ],
      requiresGameStopped: true
    });
    if (runtimeTarget) targets.push(runtimeTarget);

    const iconsDir = path.join(actionDir, 'icons');
    const iconsTarget = await finalizeTarget({
      id: 'actionaxis-icons',
      group: 'actionaxis',
      name: '行动轴卡图缓存',
      description: '游戏内自动采集的卡牌插画，删除后会在下次记录时重新采集。',
      path: iconsDir,
      paths: [iconsDir],
      requiresGameStopped: true
    });
    if (iconsTarget) targets.push(iconsTarget);

    const configDir = context.game.bepinex?.configDir ?? path.join(gamePath, 'BepInEx', 'config');
    for (const entry of await readDirSafe(configDir)) {
      if (!entry.isFile() || !entry.name.toLowerCase().endsWith('.cfg')) continue;
      if (!CONFIG_PATTERN.test(entry.name)) continue;
      const file = path.join(configDir, entry.name);
      const target = await finalizeTarget({
        id: `bepinex-config:${entry.name}`,
        group: 'bepinex',
        name: `插件配置：${entry.name}`,
        description: '插件写入 BepInEx/config 的设置。删除后插件会恢复默认配置。',
        path: file,
        paths: [file],
        requiresGameStopped: false
      });
      if (target) targets.push(target);
    }
  }

  const installerPaths = BPP_INSTALLER_DIRS.map((name) => path.join(context.appDataDir, name));
  const installerTarget = await finalizeTarget({
    id: 'bpp-installer-state',
    group: 'bpp',
    name: 'BPP 安装器状态',
    description: 'BPP 官方安装器保存的配置、更新与 WebView 数据。不影响已安装的插件。',
    path: installerPaths[0],
    paths: installerPaths,
    requiresGameStopped: false
  });
  if (installerTarget) targets.push(installerTarget);

  const capturesDir = path.join(context.workspaceRoot, 'captures');
  const liveCardsTarget = await finalizeTarget({
    id: 'livecards-output',
    group: 'livecards',
    name: 'LiveCardsDump 输出',
    description: 'LiveCardsDump 写出的卡牌快照与导入结果，删除后游戏运行时会自动重建。',
    path: capturesDir,
    paths: [
      path.join(capturesDir, 'live_cards_bpp.json'),
      path.join(capturesDir, 'live_cards_bpp.error.txt'),
      path.join(capturesDir, 'live_loadout.json')
    ],
    requiresGameStopped: false
  });
  if (liveCardsTarget) targets.push(liveCardsTarget);

  return targets.sort((a, b) => a.group.localeCompare(b.group) || a.name.localeCompare(b.name, 'zh-CN'));
}

export interface CleanOutcome {
  trashed: string[];
  failed: { path: string; message: string }[];
}

/** 把选中的数据目标移入回收站 */
export async function cleanData(targets: DataTarget[], ids: string[]): Promise<CleanOutcome> {
  const selected = targets.filter((target) => ids.includes(target.id));
  const trashed: string[] = [];
  const failed: { path: string; message: string }[] = [];

  for (const target of selected) {
    for (const targetPath of target.paths) {
      if (!(await pathExists(targetPath))) continue;
      try {
        await shell.trashItem(targetPath);
        trashed.push(targetPath);
        await log(`[data] 已移入回收站：${targetPath}`);
      } catch (error) {
        const message = describeError(error);
        failed.push({ path: targetPath, message });
        await log(`[data] 删除失败 ${targetPath}：${message}`);
      }
    }
  }

  return { trashed, failed };
}
