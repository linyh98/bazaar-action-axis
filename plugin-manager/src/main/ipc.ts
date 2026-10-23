import { app, BrowserWindow, clipboard, dialog, ipcMain, shell } from 'electron';
import type { OpenDialogOptions } from 'electron';
import { spawn } from 'node:child_process';
import { promises as fs } from 'node:fs';
import path from 'node:path';
import { GAME_EXE, STEAM_APP_ID } from '@shared/constants';
import type {
  ActionResult,
  AppConfig,
  Bootstrap,
  BuiltinPluginId,
  CleanResult,
  DataTarget
} from '@shared/types';
import { loadConfig, saveConfig } from './config';
import { describeError, log } from './logger';
import { getBuiltinFilePlan, getBuiltinPluginInfo } from './services/builtin';
import { cleanData, collectDataTargets } from './services/data';
import { isInside, pathExists, removePath } from './services/fsutil';
import { getGameState, validateGamePath } from './services/game';
import { deployFiles, scanPlugins, uninstallPlugin } from './services/plugins';
import { isProcessRunning } from './services/process';
import { downloadFile, installZipArchive } from './services/zip';

const BACKUP_DIR = () => path.join(app.getPath('userData'), 'backups');
const WORK_DIR = () => path.join(app.getPath('userData'), 'tmp');

function workspaceRoot(): string {
  return app.isPackaged ? app.getAppPath() : path.resolve(app.getAppPath(), '..');
}

function mainWindow(): BrowserWindow | null {
  return BrowserWindow.getAllWindows()[0] ?? null;
}

/** 汇总当前完整状态 */
export async function buildBootstrap(): Promise<Bootstrap> {
  const config = await loadConfig();
  const game = await getGameState(config.gamePath);

  if (game.found && game.path && config.gamePath !== game.path && (config.autoDetect || !config.gamePath)) {
    await saveConfig({ gamePath: game.path });
    config.gamePath = game.path;
  }

  const pluginsDir = game.bepinex?.pluginsDir ?? null;
  const plugins = pluginsDir ? await scanPlugins(pluginsDir) : [];
  const builtin = await getBuiltinPluginInfo(plugins);
  const dataTargets = await collectDataTargets({
    game,
    workspaceRoot: workspaceRoot(),
    appDataDir: app.getPath('appData')
  });

  return {
    config,
    game,
    plugins,
    builtin,
    dataTargets,
    userDataDir: app.getPath('userData'),
    appVersion: app.getVersion(),
    platform: process.platform
  };
}

async function requireGame(): Promise<{ gamePath: string; pluginsDir: string }> {
  const config = await loadConfig();
  const game = await getGameState(config.gamePath);
  if (!game.found || !game.path) {
    throw new Error('未找到 The Bazaar 游戏目录，请先在「设置」中选择游戏目录');
  }
  if (!game.bepinex?.installed) {
    throw new Error('游戏目录中没有检测到 BepInEx，请先安装 BepInEx 5.x');
  }
  if (await isProcessRunning(GAME_EXE)) {
    throw new Error('游戏正在运行，请先关闭游戏再执行此操作');
  }
  return { gamePath: game.path, pluginsDir: game.bepinex.pluginsDir };
}

async function result(ok: boolean, message: string, detail?: string): Promise<ActionResult> {
  return { ok, message, detail, bootstrap: ok ? await buildBootstrap() : undefined };
}

async function guard(action: () => Promise<ActionResult>): Promise<ActionResult> {
  try {
    return await action();
  } catch (error) {
    const message = describeError(error);
    await log(`[ipc] 操作失败：${message}`);
    return { ok: false, message };
  }
}

function safeFileName(value: string): string {
  return value.replace(/[^\w.\-]+/g, '_').slice(0, 80) || 'download';
}

function isBuiltinPluginId(value: unknown): value is BuiltinPluginId {
  return value === 'actionaxis' || value === 'livecardsdump';
}

export function registerIpc(): void {
  ipcMain.handle('manager:bootstrap', () => buildBootstrap());

  ipcMain.handle('manager:detect', async () => {
    const config = await loadConfig();
    const game = await getGameState(config.gamePath);
    if (game.found && game.path && config.gamePath !== game.path) {
      await saveConfig({ gamePath: game.path });
    }
    return buildBootstrap();
  });

  ipcMain.handle('manager:chooseGamePath', async () => {
    const config = await loadConfig();
    const window = mainWindow();
    const options: OpenDialogOptions = {
      title: '选择 The Bazaar 游戏目录',
      properties: ['openDirectory'],
      defaultPath: config.gamePath ?? undefined
    };
    const picked = window
      ? await dialog.showOpenDialog(window, options)
      : await dialog.showOpenDialog(options);
    if (picked.canceled || picked.filePaths.length === 0) return null;

    const dir = picked.filePaths[0];
    if (!(await validateGamePath(dir))) {
      throw new Error('该目录下没有找到 TheBazaar.exe，请选择游戏根目录');
    }
    await saveConfig({ gamePath: dir });
    return buildBootstrap();
  });

  ipcMain.handle('manager:launch', () =>
    guard(async () => {
      const config = await loadConfig();
      const game = await getGameState(config.gamePath);
      if (!game.found || !game.path || !game.exePath) {
        return { ok: false, message: '未找到游戏目录，无法启动' };
      }
      if (game.running) {
        return { ok: true, message: '游戏已经在运行中' };
      }
      try {
        await shell.openExternal(`steam://rungameid/${STEAM_APP_ID}`);
        await log('[launch] 通过 Steam 启动游戏');
        return { ok: true, message: '已请求 Steam 启动游戏' };
      } catch {
        spawn(game.exePath, [], { cwd: game.path, detached: true, stdio: 'ignore' }).unref();
        await log('[launch] 直接启动 TheBazaar.exe');
        return { ok: true, message: '已直接启动游戏' };
      }
    })
  );

  ipcMain.handle('manager:installBuiltin', (_, id: unknown) =>
    guard(async () => {
      if (!isBuiltinPluginId(id)) return { ok: false, message: '未知的内置插件' };
      const { pluginsDir } = await requireGame();
      const plan = await getBuiltinFilePlan(id);
      const deployed = await deployFiles(pluginsDir, plan.files, BACKUP_DIR());
      await log(`[install] 内置插件 ${id}：复制 ${deployed.copied}，跳过 ${deployed.skipped}`);
      const missing = deployed.missing.length > 0 ? `，${deployed.missing.length} 个可选资源缺失` : '';
      return result(true, `已部署 ${plan.files.length - deployed.missing.length} 个文件${missing}`);
    })
  );

  ipcMain.handle('manager:installZip', () =>
    guard(async () => {
      const window = mainWindow();
      const options: OpenDialogOptions = {
        title: '选择 BepInEx 插件压缩包',
        properties: ['openFile'],
        filters: [{ name: '插件压缩包', extensions: ['zip'] }]
      };
      const picked = window
        ? await dialog.showOpenDialog(window, options)
        : await dialog.showOpenDialog(options);
      if (picked.canceled || picked.filePaths.length === 0) {
        return { ok: false, message: '已取消安装' };
      }

      const { pluginsDir } = await requireGame();
      await fs.mkdir(WORK_DIR(), { recursive: true });
      const installed = await installZipArchive(picked.filePaths[0], pluginsDir, WORK_DIR(), BACKUP_DIR());
      await log(`[install] 本地插件 ${installed.name}：复制 ${installed.copied} 个文件`);
      return result(true, `已安装 ${installed.name}（${installed.copied} 个文件）`);
    })
  );

  ipcMain.handle('manager:installUrl', (_, url: unknown) =>
    guard(async () => {
      if (typeof url !== 'string' || !/^https?:\/\//i.test(url)) {
        return { ok: false, message: '请输入以 http:// 或 https:// 开头的下载地址' };
      }
      const { pluginsDir } = await requireGame();
      await fs.mkdir(WORK_DIR(), { recursive: true });

      const baseName = safeFileName(decodeURIComponent(new URL(url).pathname.split('/').pop() ?? 'plugin'));
      const downloadPath = path.join(WORK_DIR(), `download-${Date.now()}-${baseName}`);
      await downloadFile(url, downloadPath);
      await log(`[install] 已下载 ${url} -> ${downloadPath}`);

      try {
        if (baseName.toLowerCase().endsWith('.dll')) {
          const deployed = await deployFiles(
            pluginsDir,
            [{ source: downloadPath, destRel: baseName }],
            BACKUP_DIR()
          );
          return result(true, `已安装 ${baseName}（${deployed.copied} 个文件）`);
        }
        const installed = await installZipArchive(downloadPath, pluginsDir, WORK_DIR(), BACKUP_DIR());
        await log(`[install] URL 插件 ${installed.name}：复制 ${installed.copied} 个文件`);
        return result(true, `已安装 ${installed.name}（${installed.copied} 个文件）`);
      } finally {
        await removePath(downloadPath).catch(() => undefined);
      }
    })
  );

  ipcMain.handle('manager:uninstallPlugin', (_, pluginId: unknown) =>
    guard(async () => {
      if (typeof pluginId !== 'string' || !pluginId) {
        return { ok: false, message: '无效的插件标识' };
      }
      const { pluginsDir } = await requireGame();
      const plugins = await scanPlugins(pluginsDir);
      const plugin = plugins.find((item) => item.id === pluginId);
      if (!plugin) {
        return { ok: false, message: '插件不存在或已被移除' };
      }
      const removed = await uninstallPlugin(pluginsDir, plugin, BACKUP_DIR());
      await log(`[uninstall] ${plugin.name}：移除 ${removed.removed} 项，备份于 ${removed.backupDir}`);
      return result(
        true,
        `已卸载 ${plugin.name}，删除 ${removed.removed} 项（备份保留在管理器数据目录）`
      );
    })
  );

  ipcMain.handle('manager:refreshData', async (): Promise<DataTarget[]> => {
    const config = await loadConfig();
    const game = await getGameState(config.gamePath);
    return collectDataTargets({
      game,
      workspaceRoot: workspaceRoot(),
      appDataDir: app.getPath('appData')
    });
  });

  ipcMain.handle('manager:cleanData', (_, ids: unknown) =>
    guard(async (): Promise<CleanResult> => {
      if (!Array.isArray(ids) || ids.some((id) => typeof id !== 'string')) {
        return { ok: false, message: '无效的选择', trashed: [], failed: [] };
      }
      const config = await loadConfig();
      const game = await getGameState(config.gamePath);
      const targets = await collectDataTargets({
        game,
        workspaceRoot: workspaceRoot(),
        appDataDir: app.getPath('appData')
      });
      const selected = targets.filter((target) => (ids as string[]).includes(target.id));
      if (selected.length === 0) {
        return { ok: false, message: '所选数据已不存在', trashed: [], failed: [] };
      }
      if (game.running && selected.some((target) => target.requiresGameStopped)) {
        return { ok: false, message: '游戏正在运行，请先关闭游戏再清理这些数据', trashed: [], failed: [] };
      }

      const outcome = await cleanData(targets, ids as string[]);
      const ok = outcome.failed.length === 0;
      const message = ok
        ? `已把 ${outcome.trashed.length} 项移入回收站`
        : `已清理 ${outcome.trashed.length} 项，${outcome.failed.length} 项失败`;
      return {
        ok,
        message,
        detail: outcome.failed.map((item) => `${item.path}：${item.message}`).join('\n') || undefined,
        trashed: outcome.trashed,
        failed: outcome.failed,
        bootstrap: ok ? await buildBootstrap() : undefined
      };
    })
  );

  ipcMain.handle('manager:openPath', async (_, target: unknown) => {
    if (typeof target !== 'string' || !target) return '无效路径';
    const config = await loadConfig();
    const allowed = [
      workspaceRoot(),
      app.getPath('userData'),
      app.getPath('appData'),
      config.gamePath ?? ''
    ].filter(Boolean);
    const resolved = path.resolve(target);
    if (!allowed.some((root) => isInside(root, resolved))) {
      return '该路径不在允许打开的范围内';
    }
    if (!(await pathExists(resolved))) return '路径不存在';
    return shell.openPath(resolved);
  });

  ipcMain.handle('manager:openUserData', () => shell.openPath(app.getPath('userData')));

  ipcMain.handle('manager:openExternal', async (_, url: unknown) => {
    if (typeof url !== 'string' || !/^https?:\/\//i.test(url)) return;
    await shell.openExternal(url);
  });

  ipcMain.handle('manager:updateConfig', async (_, patch: unknown) => {
    if (!patch || typeof patch !== 'object') return buildBootstrap();
    const partial = patch as Partial<AppConfig>;
    const next: Partial<AppConfig> = {};

    if (partial.gamePath !== undefined) {
      if (partial.gamePath !== null) {
        if (typeof partial.gamePath !== 'string' || !(await validateGamePath(partial.gamePath))) {
          throw new Error('该目录不是有效的 The Bazaar 游戏目录');
        }
      }
      next.gamePath = partial.gamePath;
    }
    if (typeof partial.autoDetect === 'boolean') {
      next.autoDetect = partial.autoDetect;
    }

    await saveConfig(next);
    return buildBootstrap();
  });

  ipcMain.handle('manager:copy', (_, text: unknown) => {
    if (typeof text === 'string') clipboard.writeText(text);
  });
}
