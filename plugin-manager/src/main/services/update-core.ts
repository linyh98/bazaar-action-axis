import type { ReleaseAsset, UpdateInfo } from '@shared/types';

/** GitHub 发布仓库（与 package.json 中的仓库信息保持一致） */
export const RELEASE_REPO = 'linyh98/bazaar-action-axis';

/** 内置插件 DLL 在 Release 中的固定资产名 */
export const BUILTIN_ASSET_NAMES = ['ActionAxisPlugin.dll', 'ActionAxisEngine.dll', 'LiveCardsDump.dll'];

/** 内置插件 DLL 写入用户数据目录时的相对位置 */
export const BUILTIN_ASSET_TARGETS: Record<string, string> = {
  'ActionAxisPlugin.dll': 'actionaxis/ActionAxisPlugin.dll',
  'ActionAxisEngine.dll': 'actionaxis/ActionAxisEngine.dll',
  'LiveCardsDump.dll': 'livecards/LiveCardsDump.dll'
};

const MANAGER_ASSET_PATTERN = /-portable\.exe$/i;

/** 允许的版本号形态：可选前缀 v、1~3 段数字、可选预发布后缀 */
const VERSION_PATTERN = /^v?(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:-([0-9A-Za-z.\-]+))?$/;

interface ParsedVersion {
  numbers: [number, number, number];
  prerelease: string | null;
}

/** 解析版本号；无法识别时返回 null，调用方据此判定为不可比较 */
export function parseVersion(value: string | null | undefined): ParsedVersion | null {
  if (typeof value !== 'string') return null;
  const match = VERSION_PATTERN.exec(value.trim());
  if (!match) return null;
  return {
    numbers: [Number(match[1] ?? 0), Number(match[2] ?? 0), Number(match[3] ?? 0)],
    // 没有预发布后缀时优先级高于同号的预发布版本。
    prerelease: match[4] ?? null
  };
}

/** 去掉 v 前缀，便于展示 */
export function normalizeVersion(value: string): string {
  return value.trim().replace(/^v/i, '');
}

/** 语义化比较：a > b 返回正数，a < b 返回负数，相等返回 0；无法解析时按字符串比较 */
export function compareVersions(a: string, b: string): number {
  const left = parseVersion(a);
  const right = parseVersion(b);
  if (!left || !right) {
    return a.localeCompare(b, 'en');
  }
  for (let index = 0; index < 3; index += 1) {
    const diff = left.numbers[index] - right.numbers[index];
    if (diff !== 0) return diff;
  }
  if (left.prerelease === right.prerelease) return 0;
  if (left.prerelease === null) return 1;
  if (right.prerelease === null) return -1;
  return left.prerelease.localeCompare(right.prerelease, 'en');
}

/** 候选版本是否比当前版本更新 */
export function isNewer(candidate: string, current: string): boolean {
  return compareVersions(candidate, current) > 0;
}

function toAsset(raw: unknown): ReleaseAsset | null {
  if (!raw || typeof raw !== 'object') return null;
  const item = raw as Record<string, unknown>;
  if (typeof item.name !== 'string' || typeof item.browser_download_url !== 'string') return null;
  return {
    name: item.name,
    size: typeof item.size === 'number' ? item.size : 0,
    url: item.browser_download_url,
    digest: typeof item.digest === 'string' && item.digest ? item.digest : null
  };
}

/** 从资产列表里挑出便携版 exe，优先与版本号完全匹配的那个 */
export function pickManagerAsset(assets: ReleaseAsset[], latestVersion: string): ReleaseAsset | null {
  const candidates = assets.filter((asset) => MANAGER_ASSET_PATTERN.test(asset.name));
  if (candidates.length === 0) return null;
  const version = normalizeVersion(latestVersion);
  const exact = candidates.find((asset) => asset.name.includes(version));
  return exact ?? candidates[0];
}

/** 从资产列表里挑出内置插件 DLL */
export function pickBuiltinAssets(assets: ReleaseAsset[]): ReleaseAsset[] {
  return assets.filter((asset) => BUILTIN_ASSET_NAMES.includes(asset.name));
}

/**
 * 把 GitHub Releases API 的响应转换成本地模型。
 * 仓库还没有任何 Release 时返回 null，由调用方给出“尚未发布”的提示。
 */
export function parseReleasePayload(payload: unknown, currentVersion: string): UpdateInfo | null {
  if (!payload || typeof payload !== 'object') return null;
  const release = payload as Record<string, unknown>;
  const tag = typeof release.tag_name === 'string' ? release.tag_name : null;
  if (!tag) return null;

  const latestVersion = normalizeVersion(tag);
  const assets = Array.isArray(release.assets)
    ? release.assets.map(toAsset).filter((asset): asset is ReleaseAsset => asset !== null)
    : [];

  return {
    repo: RELEASE_REPO,
    releaseUrl:
      typeof release.html_url === 'string'
        ? release.html_url
        : `https://github.com/${RELEASE_REPO}/releases`,
    currentVersion: normalizeVersion(currentVersion),
    latestVersion,
    hasUpdate: isNewer(latestVersion, currentVersion),
    publishedAt: typeof release.published_at === 'string' ? release.published_at : null,
    notes: typeof release.body === 'string' ? release.body.slice(0, 4000) : null,
    managerAsset: pickManagerAsset(assets, latestVersion),
    builtinAssets: pickBuiltinAssets(assets),
    checkedAt: Date.now()
  };
}

/** 便携版产物名，与 electron-builder.yml 的 artifactName 保持一致 */
export const MANAGER_ARTIFACT_PREFIX = 'Bazaar Plugin Manager';

/** 便携版 exe 在 Release 中的固定文件名 */
export function managerAssetName(version: string): string {
  return `${MANAGER_ARTIFACT_PREFIX}-${normalizeVersion(version)}-portable.exe`;
}

/** Release 资产的下载地址（命名约定） */
export function conventionAssetUrl(version: string, fileName: string): string {
  return `https://github.com/${RELEASE_REPO}/releases/download/v${normalizeVersion(version)}/${encodeURIComponent(fileName)}`;
}

/**
 * GitHub API 限流或不可用时，按发布命名约定拼出更新信息。
 * 尺寸与摘要未知，下载后会由实际文件校验。
 */
export function buildConventionUpdateInfo(tagVersion: string, currentVersion: string): UpdateInfo {
  const latestVersion = normalizeVersion(tagVersion);
  const managerName = managerAssetName(latestVersion);
  return {
    repo: RELEASE_REPO,
    releaseUrl: `https://github.com/${RELEASE_REPO}/releases/tag/v${latestVersion}`,
    currentVersion: normalizeVersion(currentVersion),
    latestVersion,
    hasUpdate: isNewer(latestVersion, currentVersion),
    publishedAt: null,
    notes: null,
    managerAsset: {
      name: managerName,
      size: 0,
      url: conventionAssetUrl(latestVersion, managerName),
      digest: null
    },
    builtinAssets: BUILTIN_ASSET_NAMES.map((name) => ({
      name,
      size: 0,
      url: conventionAssetUrl(latestVersion, name),
      digest: null
    })),
    checkedAt: Date.now()
  };
}

/** 校验 `sha256:<hex>` 形式的资产摘要；没有摘要时返回 null 表示跳过校验 */
export function expectedSha256(digest: string | null | undefined): string | null {
  if (typeof digest !== 'string') return null;
  const match = /^sha256:([0-9a-f]{64})$/i.exec(digest.trim());
  return match ? match[1].toLowerCase() : null;
}
