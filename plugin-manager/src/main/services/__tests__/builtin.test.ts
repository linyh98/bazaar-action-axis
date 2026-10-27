import { promises as fs } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const mockPaths = vi.hoisted(() => ({ root: '' }));
vi.mock('electron', () => ({
  app: {
    isPackaged: false,
    getPath: () => path.join(mockPaths.root, 'userData'),
    getAppPath: () => path.join(mockPaths.root, 'plugin-manager')
  }
}));

import { getBuiltinFilePlan, getBuiltinPluginInfo } from '../builtin';

function plugin(version: string): Buffer {
  const strings = ['com.bazaarsim.actionaxis', 'Bazaar Action Axis', version].map((value) => {
    const bytes = Buffer.from(value);
    return Buffer.concat([Buffer.from([bytes.length]), bytes]);
  });
  return Buffer.concat([Buffer.from([1, 0]), ...strings, Buffer.from([0, 0])]);
}

describe('builtin source selection and version checks', () => {
  let source: string;
  let pluginsDir: string;

  beforeEach(async () => {
    mockPaths.root = await fs.mkdtemp(path.join(tmpdir(), 'bazaar-builtin-test-'));
    const sourceDir = path.join(mockPaths.root, 'tools/actionaxis_plugin/build');
    source = path.join(sourceDir, 'ActionAxisPlugin.dll');
    pluginsDir = path.join(mockPaths.root, 'plugins');
    await fs.mkdir(sourceDir, { recursive: true });
    await fs.mkdir(pluginsDir);
    await fs.writeFile(source, plugin('1.5.6'));
    await fs.writeFile(path.join(sourceDir, 'ActionAxisEngine.dll'), 'engine');
  });

  afterEach(async () => {
    await fs.rm(mockPaths.root, { recursive: true, force: true });
  });

  it('显示实际来源 DLL 的版本，无法读取时显示未知', async () => {
    expect((await getBuiltinPluginInfo([])).find((info) => info.id === 'actionaxis')?.version).toBe('1.5.6');
    await fs.writeFile(source, 'unknown dll');
    expect((await getBuiltinPluginInfo([])).find((info) => info.id === 'actionaxis')?.version).toBe('未知');
  });

  it('创建部署清单前拒绝旧版来源覆盖新版游戏 DLL', async () => {
    await fs.writeFile(path.join(pluginsDir, 'ActionAxisPlugin.dll'), plugin('1.6.5'));
    await expect(getBuiltinFilePlan('actionaxis', pluginsDir)).rejects.toThrow(/1\.5\.6.*1\.6\.5/);
  });

  it('用户更新副本优先，读取副本版本并允许升级', async () => {
    const overrideDir = path.join(mockPaths.root, 'userData/builtin/actionaxis');
    await fs.mkdir(overrideDir, { recursive: true });
    await fs.writeFile(path.join(overrideDir, 'ActionAxisPlugin.dll'), plugin('1.6.5'));
    await fs.writeFile(path.join(overrideDir, 'ActionAxisEngine.dll'), 'engine');
    await fs.writeFile(path.join(pluginsDir, 'ActionAxisPlugin.dll'), plugin('1.5.6'));
    const plan = await getBuiltinFilePlan('actionaxis', pluginsDir);
    expect(plan.required[0].source).toBe(path.join(overrideDir, 'ActionAxisPlugin.dll'));
    expect((await getBuiltinPluginInfo([])).find((info) => info.id === 'actionaxis')?.version).toBe('1.6.5');
  });

  it('其他内置插件不触发行动轴版本检查', async () => {
    await fs.writeFile(path.join(pluginsDir, 'ActionAxisPlugin.dll'), plugin('1.6.5'));
    const plan = await getBuiltinFilePlan('livecardsdump', pluginsDir);
    expect(plan.required[0].destRel).toBe('LiveCardsDump.dll');
  });
});
