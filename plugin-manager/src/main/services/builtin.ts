import { app } from 'electron';
import path from 'node:path';
import { BUILTIN_PLUGINS } from '@shared/constants';
import type { BuiltinPluginId, BuiltinPluginInfo, PluginInfo } from '@shared/types';
import { listFilesRecursive, pathExists } from './fsutil';
import type { DeployFile } from './plugins';

interface BuiltinPaths {
  actionAxis: string;
  livecards: string;
  cards: string;
  skills: string;
  icons: string;
  note: string;
}

function workspaceRoot(): string {
  return path.resolve(app.getAppPath(), '..');
}

function builtinPaths(): BuiltinPaths {
  if (app.isPackaged) {
    const base = path.join(process.resourcesPath, 'builtin');
    return {
      actionAxis: path.join(base, 'actionaxis'),
      livecards: path.join(base, 'livecards'),
      cards: path.join(base, 'data', 'cards.json'),
      skills: path.join(base, 'data', 'skills.json'),
      icons: path.join(base, 'data', 'icons'),
      note: '安装包内置资源'
    };
  }

  const base = workspaceRoot();
  return {
    actionAxis: path.join(base, 'tools', 'actionaxis_plugin', 'build'),
    livecards: path.join(base, 'tools', 'livecards_plugin'),
    cards: path.join(base, 'src', 'data', 'cards.json'),
    skills: path.join(base, 'data', 'skills.json'),
    icons: path.join(base, 'captures', 'card_icons'),
    note: '本仓库构建产物'
  };
}

function requiredSourceFiles(id: BuiltinPluginId, paths: BuiltinPaths): string[] {
  if (id === 'actionaxis') {
    return [
      path.join(paths.actionAxis, 'ActionAxisPlugin.dll'),
      path.join(paths.actionAxis, 'ActionAxisEngine.dll')
    ];
  }
  return [path.join(paths.livecards, 'LiveCardsDump.dll')];
}

export interface BuiltinFilePlan {
  files: DeployFile[];
  required: DeployFile[];
  note: string;
}

/** 生成内置插件的部署清单（含 ActionAxis 的 cards/skills/icons） */
export async function getBuiltinFilePlan(id: BuiltinPluginId): Promise<BuiltinFilePlan> {
  const paths = builtinPaths();

  if (id === 'actionaxis') {
    const required: DeployFile[] = [
      { source: path.join(paths.actionAxis, 'ActionAxisPlugin.dll'), destRel: 'ActionAxisPlugin.dll' },
      { source: path.join(paths.actionAxis, 'ActionAxisEngine.dll'), destRel: 'ActionAxisEngine.dll' }
    ];
    const files: DeployFile[] = [
      ...required,
      { source: paths.cards, destRel: 'BazaarActionAxis/cards.json', optional: true },
      { source: paths.skills, destRel: 'BazaarActionAxis/skills.json', optional: true }
    ];

    const icons = await listFilesRecursive(paths.icons, 5000);
    for (const icon of icons) {
      if (!icon.toLowerCase().endsWith('.png')) continue;
      files.push({
        source: path.join(paths.icons, ...icon.split('/')),
        destRel: `BazaarActionAxis/icons/${icon}`,
        optional: true,
        skipExisting: true
      });
    }
    return { files, required, note: paths.note };
  }

  const required: DeployFile[] = [
    { source: path.join(paths.livecards, 'LiveCardsDump.dll'), destRel: 'LiveCardsDump.dll' }
  ];
  return { files: required, required, note: paths.note };
}

export async function getBuiltinPluginInfo(plugins: PluginInfo[]): Promise<BuiltinPluginInfo[]> {
  const paths = builtinPaths();
  const results: BuiltinPluginInfo[] = [];

  for (const definition of BUILTIN_PLUGINS) {
    const required = requiredSourceFiles(definition.id, paths);
    const availability = await Promise.all(required.map((file) => pathExists(file)));
    const installedPlugin = plugins.find((plugin) => plugin.knownId === definition.id);

    results.push({
      id: definition.id,
      name: definition.name,
      description: definition.description,
      version: definition.version,
      installed: Boolean(installedPlugin),
      available: availability.every(Boolean),
      sourceNote: paths.note,
      files: installedPlugin?.files.length ?? 0,
      sizeBytes: installedPlugin?.sizeBytes ?? 0
    });
  }

  return results;
}
