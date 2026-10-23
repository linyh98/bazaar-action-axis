import { describe, expect, it } from 'vitest';
import { parseAppManifest, parseVdfPaths } from '../steam';

describe('parseVdfPaths', () => {
  it('解析多个库路径并去重', () => {
    const vdf = `
      "libraryfolders"
      {
        "0" { "path" "D:\\\\Program Files (x86)\\\\Steam" }
        "1" { "path" "E:\\\\SteamLibrary" }
        "2" { "path" "D:\\\\Program Files (x86)\\\\Steam" }
      }
    `;
    expect(parseVdfPaths(vdf)).toEqual(['D:\\Program Files (x86)\\Steam', 'E:\\SteamLibrary']);
  });

  it('没有 path 时返回空数组', () => {
    expect(parseVdfPaths('"libraryfolders" { }')).toEqual([]);
  });
});

describe('parseAppManifest', () => {
  it('读取 appid、名称与安装目录', () => {
    const acf = `
      "AppState"
      {
        "appid" "1617400"
        "name" "The Bazaar"
        "installdir" "The Bazaar"
      }
    `;
    expect(parseAppManifest(acf)).toEqual({
      appid: '1617400',
      installdir: 'The Bazaar',
      name: 'The Bazaar'
    });
  });

  it('缺少必需字段时返回 null', () => {
    expect(parseAppManifest('"appid" "1617400"')).toBeNull();
  });
});
