import AdmZip from 'adm-zip';
import { promises as fs } from 'node:fs';
import path from 'node:path';
import { deployFiles, type DeployFile } from './plugins';
import {
  isDirectory,
  isStrictlyInside,
  listFilesRecursive,
  pathExists,
  readDirSafe,
  removePath,
  safeArchivePath
} from './fsutil';

const MAX_ENTRIES = 20000;
const MAX_UNCOMPRESSED = 500 * 1024 * 1024;

/** 安全解压 zip：拒绝绝对路径与 .. 穿越，限制条目数量与总大小 */
export async function extractZip(zipPath: string, destRoot: string): Promise<number> {
  const zip = new AdmZip(zipPath);
  const entries = zip.getEntries();
  if (entries.length > MAX_ENTRIES) {
    throw new Error(`压缩包条目过多（${entries.length}），已拒绝解压`);
  }

  let extracted = 0;
  let totalBytes = 0;

  for (const entry of entries) {
    if (entry.isDirectory) continue;
    const safe = safeArchivePath(entry.entryName);
    if (!safe) {
      throw new Error(`压缩包包含非法路径：${entry.entryName}`);
    }
    totalBytes += entry.header.size;
    if (totalBytes > MAX_UNCOMPRESSED) {
      throw new Error('压缩包解压后超过 500 MB，已拒绝解压');
    }
    const target = path.join(destRoot, ...safe.split('/'));
    if (!isStrictlyInside(destRoot, target)) {
      throw new Error(`压缩包路径越界：${entry.entryName}`);
    }
    await fs.mkdir(path.dirname(target), { recursive: true });
    await fs.writeFile(target, entry.getData());
    extracted += 1;
  }

  return extracted;
}

async function findPluginsDir(root: string, depth = 0): Promise<string | null> {
  if (depth > 3) return null;
  for (const entry of await readDirSafe(root)) {
    if (!entry.isDirectory()) continue;
    const abs = path.join(root, entry.name);
    if (entry.name.toLowerCase() === 'bepinex') {
      const pluginsDir = path.join(abs, 'plugins');
      if (await isDirectory(pluginsDir)) return pluginsDir;
    }
    const nested = await findPluginsDir(abs, depth + 1);
    if (nested) return nested;
  }
  return null;
}

export interface ZipLayout {
  sourceRoot: string;
  destPrefix: string;
}

/** 判定 zip 内容应复制到 BepInEx/plugins 的哪个位置 */
export async function resolveLayout(tempRoot: string): Promise<ZipLayout> {
  const pluginsRoot = await findPluginsDir(tempRoot);
  if (pluginsRoot) return { sourceRoot: pluginsRoot, destPrefix: '' };

  const entries = await readDirSafe(tempRoot);
  const dirs = entries.filter((entry) => entry.isDirectory());
  const files = entries.filter((entry) => entry.isFile());

  if (dirs.length === 1 && files.length === 0) {
    const single = path.join(tempRoot, dirs[0].name);
    const nestedFiles = await listFilesRecursive(single, 4000);
    if (nestedFiles.some((file) => file.toLowerCase().endsWith('.dll'))) {
      return { sourceRoot: single, destPrefix: dirs[0].name };
    }
  }

  if (files.some((file) => file.name.toLowerCase().endsWith('.dll'))) {
    return { sourceRoot: tempRoot, destPrefix: '' };
  }

  const nestedFiles = await listFilesRecursive(tempRoot, 4000);
  if (nestedFiles.some((file) => file.toLowerCase().endsWith('.dll'))) {
    return { sourceRoot: tempRoot, destPrefix: '' };
  }

  throw new Error('压缩包中没有找到 DLL 插件文件');
}

export interface ZipInstallResult {
  name: string;
  copied: number;
  skipped: number;
  backupDir: string;
}

/** 解压并安装本地 zip 插件包 */
export async function installZipArchive(
  zipPath: string,
  pluginsDir: string,
  workDir: string,
  backupDir: string
): Promise<ZipInstallResult> {
  const tempRoot = path.join(workDir, `zip-${Date.now()}`);
  await fs.mkdir(tempRoot, { recursive: true });

  try {
    await extractZip(zipPath, tempRoot);
    const layout = await resolveLayout(tempRoot);
    const relFiles = await listFilesRecursive(layout.sourceRoot);
    const files: DeployFile[] = relFiles.map((rel) => ({
      source: path.join(layout.sourceRoot, ...rel.split('/')),
      destRel: layout.destPrefix ? `${layout.destPrefix}/${rel}` : rel
    }));
    const deployed = await deployFiles(pluginsDir, files, backupDir);
    const name = layout.destPrefix || path.parse(zipPath).name;
    return { name, copied: deployed.copied, skipped: deployed.skipped, backupDir };
  } finally {
    await removePath(tempRoot).catch(() => undefined);
  }
}

/** 下载 URL 到本地临时文件（仅 http/https，限制 200 MB） */
export async function downloadFile(url: string, destPath: string, maxBytes = 200 * 1024 * 1024): Promise<void> {
  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    throw new Error('无效的下载地址');
  }
  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
    throw new Error('只支持 http / https 下载地址');
  }

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 120000);
  try {
    const response = await fetch(url, { redirect: 'follow', signal: controller.signal });
    if (!response.ok) {
      throw new Error(`下载失败：HTTP ${response.status} ${response.statusText}`);
    }
    const length = Number(response.headers.get('content-length') ?? 0);
    if (length > maxBytes) {
      throw new Error('文件超过 200 MB，已拒绝下载');
    }
    const buffer = Buffer.from(await response.arrayBuffer());
    if (buffer.length > maxBytes) {
      throw new Error('文件超过 200 MB，已拒绝下载');
    }
    await fs.mkdir(path.dirname(destPath), { recursive: true });
    await fs.writeFile(destPath, buffer);
  } catch (error) {
    if (error instanceof Error && error.name === 'AbortError') {
      throw new Error('下载超时（120 秒）');
    }
    throw error;
  } finally {
    clearTimeout(timer);
  }
}

export async function isZipFile(filePath: string): Promise<boolean> {
  return (await pathExists(filePath)) && filePath.toLowerCase().endsWith('.zip');
}
