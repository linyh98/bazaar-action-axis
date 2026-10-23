import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { isInside, isStrictlyInside, safeArchivePath } from '../fsutil';

describe('safeArchivePath', () => {
  it('保留合法的压缩包相对路径', () => {
    expect(safeArchivePath('BepInEx/plugins/Mod.dll')).toBe('BepInEx/plugins/Mod.dll');
    expect(safeArchivePath('./Mod/Mod.dll')).toBe('Mod/Mod.dll');
    expect(safeArchivePath('Mod//Mod.dll')).toBe('Mod/Mod.dll');
    expect(safeArchivePath('Mod\\Mod.dll')).toBe('Mod/Mod.dll');
  });

  it('拒绝路径穿越与绝对路径', () => {
    expect(safeArchivePath('../evil.dll')).toBeNull();
    expect(safeArchivePath('Mod/../../evil.dll')).toBeNull();
    expect(safeArchivePath('/etc/passwd')).toBeNull();
    expect(safeArchivePath('C:\\Windows\\evil.dll')).toBeNull();
  });
});

describe('路径包含判断', () => {
  const root = path.resolve('D:\\Games\\The Bazaar');

  it('允许根目录自身（isInside）', () => {
    expect(isInside(root, root)).toBe(true);
  });

  it('严格包含时排除自身', () => {
    expect(isStrictlyInside(root, root)).toBe(false);
    expect(isStrictlyInside(root, path.join(root, 'BepInEx', 'plugins'))).toBe(true);
  });

  it('拒绝相邻同前缀目录', () => {
    expect(isStrictlyInside(root, path.resolve('D:\\Games\\The Bazaar2'))).toBe(false);
  });
});
