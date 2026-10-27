import { existsSync } from 'node:fs';
import { promises as fs } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { assertBuiltinActionAxisUpgrade, extractActionAxisVersion, readActionAxisVersion } from '../plugin-version';

function serString(value: string): Buffer {
  const bytes = Buffer.from(value, 'utf8');
  const size = bytes.length;
  const prefix = size < 0x80 ? Buffer.from([size])
    : size < 0x4000 ? Buffer.from([0x80 | (size >> 8), size & 0xff])
      : Buffer.from([0xc0 | (size >> 24), (size >> 16) & 0xff, (size >> 8) & 0xff, size & 0xff]);
  return Buffer.concat([prefix, bytes]);
}

function attribute(version: string, guid = 'com.bazaarsim.actionaxis'): Buffer {
  return Buffer.concat([
    Buffer.from([1, 0]), serString(guid), serString('Bazaar Action Axis'), serString(version), Buffer.from([0, 0])
  ]);
}

describe('BepInPlugin version attribute', () => {
  it('只读取匹配 GUID 的属性，忽略其他版本文本', () => {
    const bytes = Buffer.concat([
      Buffer.from('1.9.9'), attribute('8.0.0', 'other.plugin'), Buffer.from('unrelated'), attribute('1.6.5')
    ]);
    expect(extractActionAxisVersion(bytes)).toBe('1.6.5');
  });

  it.each([128, 16384])('按压缩字节长度读取含 %i 字节预发布后缀的版本', (length) => {
    const version = `1.6.5-${'a'.repeat(length)}`;
    expect(extractActionAxisVersion(attribute(version))).toBe(version);
  });

  it('损坏、截断或无效版本返回未知', () => {
    expect(extractActionAxisVersion(Buffer.from([1, 0, 0x80]))).toBeNull();
    expect(extractActionAxisVersion(Buffer.from([1, 0, 0xc0, 0xff]))).toBeNull();
    expect(extractActionAxisVersion(Buffer.from([1, 0, 0xff]))).toBeNull();
    expect(extractActionAxisVersion(attribute('not-a-version'))).toBeNull();
    expect(extractActionAxisVersion(attribute('1.6.5').subarray(0, -1))).toBeNull();
  });
});

describe('builtin Action Axis downgrade guard', () => {
  let root: string;
  let source: string;
  let installed: string;

  beforeEach(async () => {
    root = await fs.mkdtemp(path.join(tmpdir(), 'bazaar-version-test-'));
    source = path.join(root, 'source.dll');
    installed = path.join(root, 'installed.dll');
    await fs.writeFile(source, attribute('1.6.5'));
  });

  afterEach(async () => {
    await fs.rm(root, { recursive: true, force: true });
  });

  it.each(['1.6.5', '1.5.6'])('允许从 %s 安装到 1.6.5', async (version) => {
    await fs.writeFile(installed, attribute(version));
    await expect(assertBuiltinActionAxisUpgrade(source, installed)).resolves.toBeUndefined();
  });

  it('无已安装 DLL 时允许首次安装', async () => {
    await expect(assertBuiltinActionAxisUpgrade(source, installed)).resolves.toBeUndefined();
  });

  it('拒绝降级并显示源与已装版本，保留现有文件', async () => {
    await fs.writeFile(source, attribute('1.5.6'));
    const newer = attribute('1.6.5');
    await fs.writeFile(installed, newer);
    await expect(assertBuiltinActionAxisUpgrade(source, installed)).rejects.toThrow(/1\.5\.6.*1\.6\.5.*阻止/);
    expect(await fs.readFile(installed)).toEqual(newer);
  });

  it('未知版本不能伪装成当前版本覆盖已有 DLL', async () => {
    await fs.writeFile(installed, Buffer.from('unknown plugin'));
    await expect(assertBuiltinActionAxisUpgrade(source, installed)).rejects.toThrow(/无法读取已安装/);
    await fs.writeFile(source, Buffer.from('unknown source'));
    await expect(assertBuiltinActionAxisUpgrade(source, installed)).rejects.toThrow(/无法读取内置/);
  });
});

// Local release artifacts are optional in a clean source checkout; synthetic blob cases above always run.
const realReleases = [
  ['1.5.6', path.resolve(__dirname, '../../../../../tools/actionaxis_plugin/build/release-ui-1.6.0-20261025-010808/previous-installed/ActionAxisPlugin.dll')],
  ['1.6.5', path.resolve(__dirname, '../../../../../tools/actionaxis_plugin/build/release-ui-1.6.5-dummy-15s/ActionAxisPlugin.dll')]
] as const;

describe('local compiled plugin artifacts', () => {
  for (const [version, filePath] of realReleases) {
    it.skipIf(!existsSync(filePath))(`读取实际 ${version} DLL 的 BepInPlugin 版本`, async () => {
      expect(await readActionAxisVersion(filePath)).toBe(version);
    });
  }
});
