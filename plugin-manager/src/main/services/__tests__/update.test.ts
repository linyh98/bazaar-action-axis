import { describe, expect, it } from 'vitest';
import type { ReleaseAsset } from '@shared/types';
import {
  BUILTIN_ASSET_TARGETS,
  buildConventionUpdateInfo,
  compareVersions,
  expectedSha256,
  isNewer,
  managerAssetName,
  normalizeVersion,
  parseReleasePayload,
  pickBuiltinAssets,
  pickManagerAsset
} from '../update-core';

const asset = (name: string, digest: string | null = null): ReleaseAsset => ({
  name,
  size: 1024,
  url: `https://example.com/${name}`,
  digest
});

describe('版本比较', () => {
  it('去掉 v 前缀并按数字段比较', () => {
    expect(normalizeVersion('  v1.2.3 ')).toBe('1.2.3');
    expect(compareVersions('1.2.3', '1.2.2')).toBeGreaterThan(0);
    expect(compareVersions('v1.10.0', '1.9.9')).toBeGreaterThan(0);
    expect(compareVersions('1.0', '1.0.0')).toBe(0);
    expect(compareVersions('2.0.0', '10.0.0')).toBeLessThan(0);
  });

  it('预发布版本低于同号正式版本', () => {
    expect(compareVersions('1.0.0-beta.2', '1.0.0')).toBeLessThan(0);
    expect(compareVersions('1.0.0', '1.0.0-rc.1')).toBeGreaterThan(0);
    expect(isNewer('1.0.0-beta.2', '1.0.0-beta.1')).toBe(true);
  });

  it('无法解析时退回字符串比较，且不抛错', () => {
    expect(typeof compareVersions('latest', '1.0.0')).toBe('number');
    expect(isNewer('1.0.0', '1.0.0')).toBe(false);
  });
});

describe('资产选择', () => {
  it('优先选择版本号匹配的便携版 exe', () => {
    const assets = [
      asset('Bazaar Plugin Manager-0.2.0-portable.exe'),
      asset('Bazaar Plugin Manager-0.3.0-portable.exe'),
      asset('latest.yml')
    ];
    expect(pickManagerAsset(assets, 'v0.3.0')?.name).toBe('Bazaar Plugin Manager-0.3.0-portable.exe');
    expect(pickManagerAsset([asset('latest.yml')], '0.3.0')).toBeNull();
  });

  it('只挑选已知的内置插件 DLL', () => {
    const assets = [asset('ActionAxisPlugin.dll'), asset('LiveCardsDump.dll'), asset('readme.txt')];
    expect(pickBuiltinAssets(assets).map((item) => item.name)).toEqual([
      'ActionAxisPlugin.dll',
      'LiveCardsDump.dll'
    ]);
    expect(Object.keys(BUILTIN_ASSET_TARGETS)).toEqual(
      expect.arrayContaining(['ActionAxisPlugin.dll', 'ActionAxisEngine.dll', 'LiveCardsDump.dll'])
    );
  });
});

describe('parseReleasePayload', () => {
  const payload = {
    tag_name: 'v0.3.0',
    html_url: 'https://github.com/linyh98/bazaar-action-axis/releases/tag/v0.3.0',
    published_at: '2026-10-24T10:00:00Z',
    body: '- 新增自动更新',
    assets: [
      {
        name: 'Bazaar Plugin Manager-0.3.0-portable.exe',
        size: 120,
        browser_download_url: 'https://example.com/app.exe',
        digest: `sha256:${'a'.repeat(64)}`
      },
      {
        name: 'ActionAxisPlugin.dll',
        size: 12,
        browser_download_url: 'https://example.com/ActionAxisPlugin.dll'
      }
    ]
  };

  it('识别新版本并挑出可用资产', () => {
    const info = parseReleasePayload(payload, '0.2.0');
    expect(info).not.toBeNull();
    expect(info?.latestVersion).toBe('0.3.0');
    expect(info?.hasUpdate).toBe(true);
    expect(info?.managerAsset?.url).toBe('https://example.com/app.exe');
    expect(info?.builtinAssets).toHaveLength(1);
    expect(info?.builtinAssets[0].digest).toBeNull();
    expect(info?.notes).toContain('自动更新');
  });

  it('本地版本更高时不提示更新', () => {
    expect(parseReleasePayload(payload, '0.3.0')?.hasUpdate).toBe(false);
    expect(parseReleasePayload(payload, '1.0.0')?.hasUpdate).toBe(false);
  });

  it('缺少 tag 或空响应返回 null', () => {
    expect(parseReleasePayload(null, '0.2.0')).toBeNull();
    expect(parseReleasePayload({}, '0.2.0')).toBeNull();
    expect(parseReleasePayload({ assets: [] }, '0.2.0')).toBeNull();
  });
});

describe('expectedSha256', () => {
  it('只接受 sha256 形式摘要', () => {
    expect(expectedSha256(`sha256:${'B'.repeat(64)}`)).toBe('b'.repeat(64));
    expect(expectedSha256('md5:abc')).toBeNull();
    expect(expectedSha256(null)).toBeNull();
    expect(expectedSha256('sha256:1234')).toBeNull();
  });
});

describe('API 受限时的命名约定回退', () => {
  it('产物名与 electron-builder 的 artifactName 一致', () => {
    expect(managerAssetName('v0.3.0')).toBe('Bazaar Plugin Manager-0.3.0-portable.exe');
  });

  it('按 tag 拼出可直接下载的资产地址', () => {
    const info = buildConventionUpdateInfo('v0.3.0', '0.2.0');
    expect(info.hasUpdate).toBe(true);
    expect(info.managerAsset?.url).toBe(
      'https://github.com/linyh98/bazaar-action-axis/releases/download/v0.3.0/Bazaar%20Plugin%20Manager-0.3.0-portable.exe'
    );
    expect(info.builtinAssets.map((item) => item.name)).toEqual(
      expect.arrayContaining(['ActionAxisPlugin.dll', 'LiveCardsDump.dll'])
    );
    expect(info.releaseUrl).toBe('https://github.com/linyh98/bazaar-action-axis/releases/tag/v0.3.0');
    expect(info.publishedAt).toBeNull();
  });

  it('版本相同时不标记更新', () => {
    expect(buildConventionUpdateInfo('v0.2.0', '0.2.0').hasUpdate).toBe(false);
  });
});
