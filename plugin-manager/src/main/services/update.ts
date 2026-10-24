import { app, net } from 'electron';
import { createHash } from 'node:crypto';
import { createReadStream, promises as fs } from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import type { ActionResult, ReleaseAsset, UpdateCheckResult, UpdateInfo } from '@shared/types';
import { log } from '../logger';
import { builtinManifestPath, builtinOverrideRoot } from './builtin';
import { pathExists, removePath } from './fsutil';
import {
  BUILTIN_ASSET_TARGETS,
  RELEASE_REPO,
  buildConventionUpdateInfo,
  expectedSha256,
  parseReleasePayload
} from './update-core';
import { downloadFile } from './zip';

const RELEASES_API = `https://api.github.com/repos/${RELEASE_REPO}/releases/latest`;
const CHECK_TIMEOUT_MS = 20000;
const MAX_MANAGER_BYTES = 400 * 1024 * 1024;
const MAX_BUILTIN_BYTES = 64 * 1024 * 1024;

export interface PendingUpdate {
  version: string;
  filePath: string;
  assetName: string;
  downloadedAt: number;
}

function updatesDir(): string {
  // 不能放进 tmp/：那里每次启动都会被清空，待应用的安装包必须跨重启保留。
  return path.join(app.getPath('userData'), 'updates');
}

function pendingFilePath(): string {
  return path.join(updatesDir(), 'pending.json');
}

export async function readPendingUpdate(): Promise<PendingUpdate | null> {
  try {
    const text = await fs.readFile(pendingFilePath(), 'utf8');
    const parsed = JSON.parse(text) as Partial<PendingUpdate>;
    if (typeof parsed.version !== 'string' || typeof parsed.filePath !== 'string') return null;
    if (!(await pathExists(parsed.filePath))) return null;
    return {
      version: parsed.version,
      filePath: parsed.filePath,
      assetName: typeof parsed.assetName === 'string' ? parsed.assetName : path.basename(parsed.filePath),
      downloadedAt: typeof parsed.downloadedAt === 'number' ? parsed.downloadedAt : 0
    };
  } catch {
    return null;
  }
}

async function sha256File(filePath: string): Promise<string> {
  const hash = createHash('sha256');
  await new Promise<void>((resolve, reject) => {
    createReadStream(filePath)
      .on('data', (chunk) => hash.update(chunk))
      .on('error', reject)
      .on('end', () => resolve());
  });
  return hash.digest('hex');
}

/** 有摘要时校验下载结果，不一致则删除文件并报错 */
async function verifyDigest(filePath: string, asset: ReleaseAsset): Promise<void> {
  const expected = expectedSha256(asset.digest);
  if (!expected) return;
  const actual = await sha256File(filePath);
  if (actual !== expected) {
    await removePath(filePath).catch(() => undefined);
    throw new Error(`${asset.name} 校验失败：期望 ${expected.slice(0, 12)}…，实际 ${actual.slice(0, 12)}…`);
  }
}

async function fetchLatestRelease(): Promise<unknown> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), CHECK_TIMEOUT_MS);
  try {
    const response = await net.fetch(RELEASES_API, {
      redirect: 'follow',
      signal: controller.signal,
      headers: {
        Accept: 'application/vnd.github+json',
        'User-Agent': 'bazaar-plugin-manager'
      }
    });
    if (response.status === 404) return null;
    if (!response.ok) {
      throw new Error(`GitHub API 返回 HTTP ${response.status}，请稍后再试`);
    }
    return (await response.json()) as unknown;
  } catch (error) {
    if (error instanceof Error && error.name === 'AbortError') {
      throw new Error('检查更新超时，请检查网络或代理设置');
    }
    throw error;
  } finally {
    clearTimeout(timer);
  }
}

/**
 * 未登录的 GitHub API 每个 IP 每小时只有 60 次，共享代理下极易被限流。
 * 这里跟随 /releases/latest 的 302 拿真实 tag；net.fetch 拿不到重定向地址，
 * 所以改用 net.request 的 redirect 事件。
 */
function resolveLatestTag(): Promise<string | null> {
  return new Promise((resolve) => {
    let settled = false;
    const finish = (value: string | null) => {
      if (settled) return;
      settled = true;
      resolve(value);
    };

    try {
      const request = net.request({
        method: 'GET',
        url: `https://github.com/${RELEASE_REPO}/releases/latest`,
        redirect: 'manual'
      });
      request.on('redirect', (_status, _method, redirectUrl) => {
        const match = /\/releases\/tag\/([^/?#]+)/.exec(redirectUrl);
        finish(match ? decodeURIComponent(match[1]) : null);
        request.abort();
      });
      request.on('response', () => finish(null));
      request.on('error', () => finish(null));
      request.end();
      setTimeout(() => finish(null), CHECK_TIMEOUT_MS).unref();
    } catch {
      finish(null);
    }
  });
}

/** 发布页重定向也拿不到时的最后备用：仓库 package.json（raw 有 CDN 缓存延迟） */
async function resolveLatestVersionFromRepo(): Promise<string | null> {
  const response = await net.fetch(
    `https://raw.githubusercontent.com/${RELEASE_REPO}/main/plugin-manager/package.json`,
    { headers: { 'User-Agent': 'bazaar-plugin-manager' } }
  );
  if (!response.ok) return null;
  const pkg = (await response.json()) as { version?: unknown };
  return typeof pkg.version === 'string' ? pkg.version : null;
}

function describeUpdate(info: UpdateInfo): string {
  if (info.hasUpdate) return `发现新版本 v${info.latestVersion}`;
  if (info.latestVersion === info.currentVersion) return `已是最新版本 v${info.currentVersion}`;
  return `本地版本 v${info.currentVersion} 高于发布版本 v${info.latestVersion}`;
}

/** 读取 GitHub 上最新的 Release 并与当前版本比对 */
export async function checkForUpdate(currentVersion: string): Promise<UpdateCheckResult> {
  const pending = await readPendingUpdate();
  const pendingVersion = pending?.version ?? null;

  try {
    const payload = await fetchLatestRelease();
    if (!payload) {
      return {
        ok: true,
        message: 'GitHub 上还没有发布任何 Release，暂时无法检查更新',
        pendingVersion
      };
    }
    const info = parseReleasePayload(payload, currentVersion);
    if (!info) return { ok: false, message: 'Release 数据格式无法识别', pendingVersion };

    await log(
      `[update] 检查更新：本地 ${info.currentVersion}，最新 ${info.latestVersion}，` +
        `资产 ${info.managerAsset ? info.managerAsset.name : '缺少 exe'}`
    );
    return { ok: true, message: describeUpdate(info), info, pendingVersion };
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    // API 限流或网络异常时退化为只看版本号，资产地址按发布命名约定推断。
    const tag = await resolveLatestTag();
    if (tag) {
      const info = buildConventionUpdateInfo(tag, currentVersion);
      await log(`[update] GitHub API 不可用（${message}），改用发布页版本 v${info.latestVersion}`);
      return {
        ok: true,
        message: `${describeUpdate(info)}（GitHub API 受限，资产地址按发布命名推断）`,
        info,
        pendingVersion
      };
    }

    const version = await resolveLatestVersionFromRepo().catch(() => null);
    if (!version) return { ok: false, message, pendingVersion };

    const info = buildConventionUpdateInfo(version, currentVersion);
    await log(`[update] GitHub API 与发布页都不可用（${message}），改用仓库版本 v${info.latestVersion}`);
    return {
      ok: true,
      message: `${describeUpdate(info)}（版本号取自仓库 package.json，可能有几分钟缓存延迟）`,
      info,
      pendingVersion
    };
  }
}

/** 下载最新便携版 exe 到本地，等待重启后替换 */
export async function downloadManagerUpdate(info: UpdateInfo): Promise<ActionResult> {
  const asset = info.managerAsset;
  if (!asset) {
    return {
      ok: false,
      message: '该 Release 没有 portable exe 资产，请到 Release 页面手动下载',
      detail: info.releaseUrl
    };
  }

  const dir = path.join(updatesDir(), info.latestVersion);
  const dest = path.join(dir, path.basename(asset.name));
  await fs.mkdir(dir, { recursive: true });

  // 已下载过就先校验；大小或摘要不符则重新下载，避免沿用半截文件。
  let reusable = false;
  if (await pathExists(dest)) {
    const size = (await fs.stat(dest)).size;
    if (asset.size === 0 || size === asset.size) {
      try {
        await verifyDigest(dest, asset);
        reusable = true;
      } catch {
        reusable = false;
      }
    }
  }
  if (!reusable) {
    await removePath(dest).catch(() => undefined);
    await downloadFile(asset.url, dest, MAX_MANAGER_BYTES, net.fetch);
    await verifyDigest(dest, asset);
  }

  const pending: PendingUpdate = {
    version: info.latestVersion,
    filePath: dest,
    assetName: asset.name,
    downloadedAt: Date.now()
  };
  await fs.writeFile(pendingFilePath(), `${JSON.stringify(pending, null, 2)}\n`, 'utf8');
  await log(`[update] 已下载 v${info.latestVersion}：${dest}`);

  return {
    ok: true,
    message: `已下载 v${info.latestVersion}，点击「重启并更新」完成替换`,
    detail: dest
  };
}

function resolveExecutablePath(): string {
  const portable = process.env['PORTABLE_EXECUTABLE_FILE'];
  return portable && portable.trim() ? portable : app.getPath('exe');
}

function powerShellLiteral(value: string): string {
  return `'${value.replace(/'/g, "''")}'`;
}

/**
 * 便携版 exe 在运行时被系统锁定，无法原地覆盖。
 * 这里生成一个等待退出、替换文件并重新启动的脚本，再让管理器自己退出。
 */
export async function applyPendingUpdate(): Promise<ActionResult> {
  const pending = await readPendingUpdate();
  if (!pending) {
    return { ok: false, message: '没有已下载的更新，请先检查并下载' };
  }
  const target = resolveExecutablePath();
  if (!app.isPackaged) {
    return {
      ok: false,
      message: '开发模式下不会替换程序本体',
      detail: `已下载文件：${pending.filePath}`
    };
  }
  if (path.resolve(target) === path.resolve(pending.filePath)) {
    return { ok: false, message: '更新文件与被替换的程序是同一个文件' };
  }

  const script = [
    "$ErrorActionPreference = 'Stop'",
    `$target = ${powerShellLiteral(target)}`,
    `$source = ${powerShellLiteral(pending.filePath)}`,
    "$backup = $target + '.bak'",
    'for ($i = 0; $i -lt 240; $i++) {',
    '  try { $fs = [System.IO.File]::Open($target, "Open", "ReadWrite", "None"); $fs.Close(); break }',
    '  catch { Start-Sleep -Milliseconds 500 }',
    '}',
    'if (-not (Test-Path -LiteralPath $backup)) { Copy-Item -LiteralPath $target -Destination $backup -Force }',
    'Copy-Item -LiteralPath $source -Destination $target -Force',
    'Start-Process -FilePath $target',
    'Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue'
  ].join('\r\n');

  const scriptPath = path.join(updatesDir(), 'apply-update.ps1');
  await fs.mkdir(updatesDir(), { recursive: true });
  // PowerShell 5.1 只有带 BOM 才会按 UTF-8 读取脚本，否则中文路径会乱码。
  await fs.writeFile(scriptPath, `\uFEFF${script}\r\n`, 'utf8');

  spawn(
    'powershell.exe',
    ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-WindowStyle', 'Hidden', '-File', scriptPath],
    { detached: true, stdio: 'ignore', windowsHide: true }
  ).unref();

  await log(`[update] 准备把 ${target} 替换为 v${pending.version}`);

  setTimeout(() => app.quit(), 600).unref();
  return { ok: true, message: `正在退出并更新到 v${pending.version}，更新完成后会自动启动` };
}

/** 从同一个 Release 下载内置插件 DLL 到用户数据目录，部署时优先使用 */
export async function downloadBuiltinUpdate(info: UpdateInfo): Promise<ActionResult> {
  const assets = info.builtinAssets.filter((asset) => BUILTIN_ASSET_TARGETS[asset.name]);
  if (assets.length === 0) {
    return { ok: false, message: '该 Release 没有附带内置插件 DLL', detail: info.releaseUrl };
  }

  const root = builtinOverrideRoot();
  const saved: string[] = [];
  for (const asset of assets) {
    const target = path.join(root, ...BUILTIN_ASSET_TARGETS[asset.name].split('/'));
    await fs.mkdir(path.dirname(target), { recursive: true });
    const temp = `${target}.download`;
    await downloadFile(asset.url, temp, MAX_BUILTIN_BYTES, net.fetch);
    await verifyDigest(temp, asset);
    await fs.rename(temp, target);
    saved.push(path.basename(target));
  }

  await fs.mkdir(root, { recursive: true });
  await fs.writeFile(
    builtinManifestPath(),
    `${JSON.stringify({ version: info.latestVersion, downloadedAt: Date.now(), files: saved }, null, 2)}\n`,
    'utf8'
  );
  await log(`[update] 内置插件已更新到 v${info.latestVersion}：${saved.join('、')}`);

  return {
    ok: true,
    message: `内置插件已更新到 v${info.latestVersion}（${saved.length} 个 DLL）`,
    detail: root
  };
}
