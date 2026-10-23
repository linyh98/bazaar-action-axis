import { promises as fs } from 'node:fs';
import path from 'node:path';

export interface Measurement {
  exists: boolean;
  kind: 'dir' | 'file';
  sizeBytes: number;
  itemCount: number;
}

export async function pathExists(target: string): Promise<boolean> {
  try {
    await fs.access(target);
    return true;
  } catch {
    return false;
  }
}

export async function isDirectory(target: string): Promise<boolean> {
  try {
    const stat = await fs.stat(target);
    return stat.isDirectory();
  } catch {
    return false;
  }
}

export async function readTextIfExists(target: string, maxBytes = 4 * 1024 * 1024): Promise<string | null> {
  try {
    const stat = await fs.stat(target);
    if (!stat.isFile() || stat.size > maxBytes) return null;
    return await fs.readFile(target, 'utf8');
  } catch {
    return null;
  }
}

export async function readDirSafe(target: string) {
  try {
    return await fs.readdir(target, { withFileTypes: true });
  } catch {
    return [];
  }
}

/** 统计文件/目录大小，不跟随符号链接，最多统计 limit 个条目 */
export async function measurePath(target: string, limit = 20000): Promise<Measurement> {
  let stat;
  try {
    stat = await fs.lstat(target);
  } catch {
    return { exists: false, kind: 'file', sizeBytes: 0, itemCount: 0 };
  }

  if (stat.isFile()) {
    return { exists: true, kind: 'file', sizeBytes: stat.size, itemCount: 1 };
  }
  if (!stat.isDirectory()) {
    return { exists: true, kind: 'file', sizeBytes: stat.size, itemCount: 1 };
  }

  let sizeBytes = 0;
  let itemCount = 0;
  const stack: string[] = [target];

  while (stack.length > 0 && itemCount < limit) {
    const dir = stack.pop();
    if (!dir) break;
    const entries = await readDirSafe(dir);
    for (const entry of entries) {
      const full = path.join(dir, entry.name);
      itemCount += 1;
      if (itemCount >= limit) break;
      if (entry.isDirectory()) {
        stack.push(full);
      } else if (entry.isFile()) {
        try {
          sizeBytes += (await fs.stat(full)).size;
        } catch {
          // 文件在扫描过程中被移除时忽略
        }
      }
    }
  }

  return { exists: true, kind: 'dir', sizeBytes, itemCount };
}

/** 判断 child 是否位于 parent 内（含自身） */
export function isInside(parent: string, child: string): boolean {
  const rel = path.relative(path.resolve(parent), path.resolve(child));
  return rel === '' || (!rel.startsWith('..') && !path.isAbsolute(rel));
}

export function isStrictlyInside(parent: string, child: string): boolean {
  const rel = path.relative(path.resolve(parent), path.resolve(child));
  return rel !== '' && !rel.startsWith('..') && !path.isAbsolute(rel);
}

/** 规范化压缩包内的相对路径；非法（绝对路径 / 穿越）返回 null */
export function safeArchivePath(name: string): string | null {
  const normalized = name.replace(/\\/g, '/').replace(/^\.\/+/, '');
  if (!normalized || normalized.startsWith('/') || /^[a-zA-Z]:/.test(normalized)) return null;
  const parts = normalized.split('/').filter(Boolean);
  if (parts.some((part) => part === '..')) return null;
  return parts.join('/');
}

/** 递归列出目录下的相对文件路径 */
export async function listFilesRecursive(root: string, limit = 20000): Promise<string[]> {
  const result: string[] = [];
  const stack: string[] = [''];
  while (stack.length > 0 && result.length < limit) {
    const rel = stack.pop();
    if (rel === undefined) break;
    const dir = rel ? path.join(root, rel) : root;
    const entries = await readDirSafe(dir);
    for (const entry of entries) {
      const childRel = rel ? `${rel}/${entry.name}` : entry.name;
      if (entry.isDirectory()) {
        stack.push(childRel);
      } else if (entry.isFile()) {
        result.push(childRel);
        if (result.length >= limit) break;
      }
    }
  }
  return result;
}

export async function copyFileEnsuringDir(source: string, dest: string): Promise<void> {
  await fs.mkdir(path.dirname(dest), { recursive: true });
  await fs.copyFile(source, dest);
}

export async function removePath(target: string): Promise<void> {
  await fs.rm(target, { recursive: true, force: true, maxRetries: 3, retryDelay: 120 });
}

/** 把目录内容复制到目标目录（逐个文件，便于统一处理冲突） */
export async function copyTree(
  sourceRoot: string,
  destRoot: string,
  onFile: (relPath: string) => Promise<void>
): Promise<number> {
  const files = await listFilesRecursive(sourceRoot);
  for (const rel of files) {
    await onFile(rel);
    await copyFileEnsuringDir(path.join(sourceRoot, rel), path.join(destRoot, rel));
  }
  return files.length;
}
