import AdmZip from 'adm-zip';
import { promises as fs } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { PluginInfo } from '@shared/types';
import { deployFiles, uninstallPlugin } from '../plugins';
import { installZipArchive } from '../zip';

let root: string;

beforeEach(async () => {
  root = await fs.mkdtemp(path.join(tmpdir(), 'bazaar-manager-test-'));
});

afterEach(async () => {
  await fs.rm(root, { recursive: true, force: true });
});

describe('deployFiles', () => {
  it('复制新文件，覆盖前备份旧文件', async () => {
    const pluginsDir = path.join(root, 'plugins');
    const backupDir = path.join(root, 'backup');
    const source = path.join(root, 'source.dll');
    await fs.mkdir(pluginsDir, { recursive: true });
    await fs.writeFile(source, 'new-content');
    await fs.writeFile(path.join(pluginsDir, 'Mod.dll'), 'old-content');

    const result = await deployFiles(pluginsDir, [{ source, destRel: 'Mod.dll' }], backupDir);

    expect(result.copied).toBe(1);
    expect(await fs.readFile(path.join(pluginsDir, 'Mod.dll'), 'utf8')).toBe('new-content');
    expect(await fs.readFile(path.join(backupDir, 'Mod.dll'), 'utf8')).toBe('old-content');
  });

  it('skipExisting 时保留已有文件', async () => {
    const pluginsDir = path.join(root, 'plugins');
    const source = path.join(root, 'icon.png');
    await fs.mkdir(pluginsDir, { recursive: true });
    await fs.writeFile(source, 'new');
    await fs.writeFile(path.join(pluginsDir, 'icon.png'), 'existing');

    const result = await deployFiles(
      pluginsDir,
      [{ source, destRel: 'icon.png', skipExisting: true }],
      path.join(root, 'backup')
    );

    expect(result.skipped).toBe(1);
    expect(await fs.readFile(path.join(pluginsDir, 'icon.png'), 'utf8')).toBe('existing');
  });

  it('拒绝写出 plugins 目录之外的路径', async () => {
    const pluginsDir = path.join(root, 'plugins');
    const source = path.join(root, 'source.dll');
    await fs.mkdir(pluginsDir, { recursive: true });
    await fs.writeFile(source, 'x');

    await expect(
      deployFiles(pluginsDir, [{ source, destRel: '../evil.dll' }], path.join(root, 'backup'))
    ).rejects.toThrow(/非法/);
  });
});

describe('uninstallPlugin', () => {
  it('备份后删除插件文件与目录', async () => {
    const pluginsDir = path.join(root, 'plugins');
    await fs.mkdir(path.join(pluginsDir, 'ModDir'), { recursive: true });
    await fs.writeFile(path.join(pluginsDir, 'Mod.dll'), 'dll');
    await fs.writeFile(path.join(pluginsDir, 'ModDir', 'data.json'), '{}');

    const plugin: PluginInfo = {
      id: 'folder:ModDir',
      name: 'Mod',
      knownId: null,
      description: '',
      kind: 'folder',
      files: ['Mod.dll', 'ModDir/data.json'],
      dirs: ['ModDir'],
      sizeBytes: 10,
      modifiedAt: 1
    };

    const result = await uninstallPlugin(pluginsDir, plugin, path.join(root, 'backups'));

    expect(result.removed).toBe(2);
    expect(await fs.readdir(pluginsDir)).toEqual([]);
    expect(await fs.readFile(path.join(result.backupDir, 'plugins', 'Mod.dll'), 'utf8')).toBe('dll');
    expect(
      await fs.readFile(path.join(result.backupDir, 'plugins', 'ModDir', 'data.json'), 'utf8')
    ).toBe('{}');
  });
});

describe('zip 安装', () => {
  it('识别 BepInEx/plugins 结构并安装子目录', async () => {
    const zipPath = path.join(root, 'mod.zip');
    const zip = new AdmZip();
    zip.addFile('BepInEx/plugins/MyMod/MyMod.dll', Buffer.from('dll-bytes'));
    zip.addFile('BepInEx/plugins/MyMod/README.txt', Buffer.from('readme'));
    zip.writeZip(zipPath);

    const pluginsDir = path.join(root, 'plugins');
    const result = await installZipArchive(
      zipPath,
      pluginsDir,
      path.join(root, 'work'),
      path.join(root, 'backup')
    );

    expect(result.copied).toBe(2);
    expect(await fs.readFile(path.join(pluginsDir, 'MyMod', 'MyMod.dll'), 'utf8')).toBe('dll-bytes');
  });

  it('单文件夹包整体安装到 plugins/<文件夹名>', async () => {
    const zipPath = path.join(root, 'single.zip');
    const zip = new AdmZip();
    zip.addFile('Single/Plugin.dll', Buffer.from('x'));
    zip.writeZip(zipPath);

    const pluginsDir = path.join(root, 'plugins');
    const result = await installZipArchive(
      zipPath,
      pluginsDir,
      path.join(root, 'work'),
      path.join(root, 'backup')
    );

    expect(result.name).toBe('Single');
    expect(await fs.readFile(path.join(pluginsDir, 'Single', 'Plugin.dll'), 'utf8')).toBe('x');
  });
});
