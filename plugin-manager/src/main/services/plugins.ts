import { promises as fs } from 'node:fs';
import path from 'node:path';
import { KNOWN_PLUGIN_SPECS } from '@shared/constants';
import type { PluginInfo } from '@shared/types';
import {
  copyFileEnsuringDir,
  isDirectory,
  isStrictlyInside,
  pathExists,
  readDirSafe,
  removePath
} from './fsutil';

export interface TopNode {
  name: string;
  rel: string;
  isDir: boolean;
  files: string[];
  dirs: string[];
  sizeBytes: number;
  mtime: number;
  hasDll: boolean;
}

export interface DeployFile {
  source: string;
  destRel: string;
  optional?: boolean;
  skipExisting?: boolean;
}

function stemOf(name: string): string {
  return path.parse(name).name;
}

async function scanTree(
  root: string,
  relPrefix: string
): Promise<{ files: string[]; dirs: string[]; sizeBytes: number; mtime: number }> {
  const files: string[] = [];
  const dirs: string[] = [];
  const stack: { abs: string; rel: string }[] = [{ abs: root, rel: relPrefix }];
  let sizeBytes = 0;
  let mtime = 0;

  while (stack.length > 0) {
    const current = stack.pop();
    if (!current) break;
    const entries = await readDirSafe(current.abs);
    for (const entry of entries) {
      const abs = path.join(current.abs, entry.name);
      const rel = `${current.rel}/${entry.name}`;
      if (entry.isDirectory()) {
        dirs.push(rel);
        stack.push({ abs, rel });
      } else if (entry.isFile()) {
        files.push(rel);
        try {
          const stat = await fs.stat(abs);
          sizeBytes += stat.size;
          mtime = Math.max(mtime, stat.mtimeMs);
        } catch {
          // 忽略读取失败的文件
        }
      }
    }
  }

  return { files, dirs, sizeBytes, mtime };
}

/** 把顶层条目按已知规则聚合成插件 */
export function groupPlugins(nodes: TopNode[]): PluginInfo[] {
  const claimed = new Set<TopNode>();
  const result: PluginInfo[] = [];

  for (const spec of KNOWN_PLUGIN_SPECS) {
    const matched = nodes.filter((node) => {
      if (claimed.has(node)) return false;
      if (node.isDir) return spec.dirNames.includes(node.name);
      return spec.fileStems.includes(stemOf(node.name));
    });
    if (matched.length === 0) continue;
    for (const node of matched) claimed.add(node);

    result.push({
      id: `known:${spec.id}`,
      name: spec.name,
      knownId: spec.id,
      description: spec.description,
      kind: 'known',
      files: matched.flatMap((node) => node.files).sort(),
      dirs: matched.filter((node) => node.isDir).map((node) => node.rel).sort(),
      sizeBytes: matched.reduce((sum, node) => sum + node.sizeBytes, 0),
      modifiedAt: Math.max(...matched.map((node) => node.mtime))
    });
  }

  for (const node of nodes) {
    if (claimed.has(node) || !node.isDir || !node.hasDll) continue;
    claimed.add(node);
    result.push({
      id: `folder:${node.rel}`,
      name: node.name,
      knownId: null,
      description: '在 BepInEx/plugins 中扫描到的目录插件',
      kind: 'folder',
      files: node.files.sort(),
      dirs: [node.rel],
      sizeBytes: node.sizeBytes,
      modifiedAt: node.mtime
    });
  }

  for (const node of nodes) {
    if (claimed.has(node) || node.isDir) continue;
    if (!node.name.toLowerCase().endsWith('.dll')) continue;
    claimed.add(node);
    result.push({
      id: `file:${node.rel}`,
      name: stemOf(node.name),
      knownId: null,
      description: '单个 DLL 插件',
      kind: 'file',
      files: node.files,
      dirs: [],
      sizeBytes: node.sizeBytes,
      modifiedAt: node.mtime
    });
  }

  return result.sort((a, b) => {
    const knownOrder = Number(Boolean(b.knownId)) - Number(Boolean(a.knownId));
    return knownOrder || a.name.localeCompare(b.name, 'zh-CN');
  });
}

export async function scanPlugins(pluginsDir: string): Promise<PluginInfo[]> {
  if (!(await isDirectory(pluginsDir))) return [];
  const entries = await readDirSafe(pluginsDir);
  const nodes: TopNode[] = [];

  for (const entry of entries) {
    if (entry.name === '.gitkeep') continue;
    const abs = path.join(pluginsDir, entry.name);
    if (entry.isDirectory()) {
      const tree = await scanTree(abs, entry.name);
      let mtime = tree.mtime;
      try {
        mtime = Math.max(mtime, (await fs.stat(abs)).mtimeMs);
      } catch {
        // ignore
      }
      nodes.push({
        name: entry.name,
        rel: entry.name,
        isDir: true,
        files: tree.files,
        dirs: tree.dirs,
        sizeBytes: tree.sizeBytes,
        mtime,
        hasDll: tree.files.some((file) => file.toLowerCase().endsWith('.dll'))
      });
    } else if (entry.isFile()) {
      try {
        const stat = await fs.stat(abs);
        nodes.push({
          name: entry.name,
          rel: entry.name,
          isDir: false,
          files: [entry.name],
          dirs: [],
          sizeBytes: stat.size,
          mtime: stat.mtimeMs,
          hasDll: entry.name.toLowerCase().endsWith('.dll')
        });
      } catch {
        // ignore
      }
    }
  }

  return groupPlugins(nodes);
}

function toAbsolute(base: string, rel: string): string {
  return path.join(base, ...rel.split('/'));
}

/** 复制安装文件；已存在的目标先备份到 backupDir */
export async function deployFiles(
  pluginsDir: string,
  files: DeployFile[],
  backupDir: string
): Promise<{ copied: number; skipped: number; missing: string[] }> {
  await fs.mkdir(pluginsDir, { recursive: true });
  let copied = 0;
  let skipped = 0;
  const missing: string[] = [];

  for (const file of files) {
    if (!(await pathExists(file.source))) {
      if (file.optional) {
        missing.push(file.destRel);
        continue;
      }
      throw new Error(`缺少插件文件：${file.source}`);
    }

    const dest = toAbsolute(pluginsDir, file.destRel);
    if (!isStrictlyInside(pluginsDir, dest)) {
      throw new Error(`非法的目标路径：${file.destRel}`);
    }

    if (await pathExists(dest)) {
      if (file.skipExisting) {
        skipped += 1;
        continue;
      }
      const backup = toAbsolute(backupDir, file.destRel);
      await copyFileEnsuringDir(dest, backup);
    }

    await copyFileEnsuringDir(file.source, dest);
    copied += 1;
  }

  return { copied, skipped, missing };
}

function stamp(): string {
  return new Date().toISOString().replace(/[:.]/g, '-');
}

/** 备份后卸载插件；返回备份位置 */
export async function uninstallPlugin(
  pluginsDir: string,
  plugin: PluginInfo,
  backupRoot: string
): Promise<{ backupDir: string; removed: number }> {
  const backupDir = path.join(backupRoot, `uninstall-${stamp()}`);

  for (const rel of plugin.files) {
    const source = toAbsolute(pluginsDir, rel);
    if (!isStrictlyInside(pluginsDir, source)) {
      throw new Error(`非法的插件路径：${rel}`);
    }
    if (!(await pathExists(source))) continue;
    await copyFileEnsuringDir(source, path.join(backupDir, 'plugins', ...rel.split('/')));
  }

  let removed = 0;
  for (const rel of plugin.dirs) {
    const target = toAbsolute(pluginsDir, rel);
    if (!isStrictlyInside(pluginsDir, target)) {
      throw new Error(`非法的插件目录：${rel}`);
    }
    if (await pathExists(target)) {
      await removePath(target);
      removed += 1;
    }
  }

  for (const rel of plugin.files) {
    if (plugin.dirs.some((dir) => rel === dir || rel.startsWith(`${dir}/`))) continue;
    const target = toAbsolute(pluginsDir, rel);
    if (!isStrictlyInside(pluginsDir, target)) {
      throw new Error(`非法的插件文件：${rel}`);
    }
    if (await pathExists(target)) {
      await removePath(target);
      removed += 1;
    }
  }

  return { backupDir, removed };
}
