import { describe, expect, it } from 'vitest';
import { groupPlugins, type TopNode } from '../plugins';

function fileNode(name: string, size = 100): TopNode {
  return {
    name,
    rel: name,
    isDir: false,
    files: [name],
    dirs: [],
    sizeBytes: size,
    mtime: 10,
    hasDll: name.toLowerCase().endsWith('.dll')
  };
}

function dirNode(name: string, files: string[], hasDll: boolean): TopNode {
  return {
    name,
    rel: name,
    isDir: true,
    files,
    dirs: [`${name}/sub`],
    sizeBytes: 500,
    mtime: 20,
    hasDll
  };
}

describe('groupPlugins', () => {
  it('把 BPP 依赖 DLL 归入同一个插件', () => {
    const plugins = groupPlugins([
      fileNode('BazaarPlusPlus.dll', 1000),
      fileNode('BazaarPlusPlus.ModApi.dll', 200),
      fileNode('e_sqlite3.dll', 300),
      fileNode('BazaarPlusPlus.version', 5),
      fileNode('BazaarPlusPlus.history-database.json', 50)
    ]);

    expect(plugins).toHaveLength(1);
    expect(plugins[0].knownId).toBe('bpp');
    expect(plugins[0].files).toHaveLength(5);
    expect(plugins[0].sizeBytes).toBe(1555);
  });

  it('行动轴目录即使没有 DLL 也识别为已知插件', () => {
    const plugins = groupPlugins([
      fileNode('ActionAxisPlugin.dll'),
      dirNode('BazaarActionAxis', ['BazaarActionAxis/cards.json'], false)
    ]);

    const actionAxis = plugins.find((plugin) => plugin.knownId === 'actionaxis');
    expect(actionAxis).toBeDefined();
    expect(actionAxis?.dirs).toEqual(['BazaarActionAxis']);
    expect(actionAxis?.files).toContain('BazaarActionAxis/cards.json');
  });

  it('未知目录插件只保留包含 DLL 的目录', () => {
    const plugins = groupPlugins([
      dirNode('CustomMod', ['CustomMod/CustomMod.dll'], true),
      dirNode('DataOnly', ['DataOnly/readme.txt'], false)
    ]);

    expect(plugins).toHaveLength(1);
    expect(plugins[0].kind).toBe('folder');
    expect(plugins[0].name).toBe('CustomMod');
  });

  it('未知顶层 DLL 单独成为一个插件', () => {
    const plugins = groupPlugins([fileNode('SingleMod.dll'), fileNode('.gitkeep')]);
    expect(plugins).toHaveLength(1);
    expect(plugins[0].id).toBe('file:SingleMod.dll');
  });

  it('已识别插件排在外部插件之前', () => {
    const plugins = groupPlugins([
      fileNode('Unknown.dll'),
      fileNode('LiveCardsDump.dll')
    ]);
    expect(plugins[0].knownId).toBe('livecardsdump');
    expect(plugins[1].knownId).toBeNull();
  });
});
