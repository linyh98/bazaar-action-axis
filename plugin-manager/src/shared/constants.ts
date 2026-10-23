import type { BuiltinPluginId } from './types';

export const STEAM_APP_ID = '1617400';
export const GAME_DIR_NAME = 'The Bazaar';
export const GAME_EXE = 'TheBazaar.exe';

export interface KnownPluginSpec {
  id: string;
  name: string;
  description: string;
  /** 顶层文件名（不含扩展名）匹配 */
  fileStems: string[];
  /** 顶层目录名匹配，整体归属该插件 */
  dirNames: string[];
}

/**
 * BepInEx/plugins 下的已知插件归属规则。
 * BPP 的依赖 DLL（sqlite、ImageSharp 等）一并归到 BPP，避免误判成多个插件。
 */
export const KNOWN_PLUGIN_SPECS: KnownPluginSpec[] = [
  {
    id: 'actionaxis',
    name: 'Bazaar Action Axis',
    description: '游戏内行动轴推演面板（本仓库开发）',
    fileStems: ['ActionAxisPlugin', 'ActionAxisEngine'],
    dirNames: ['BazaarActionAxis']
  },
  {
    id: 'bpp',
    name: 'BazaarPlusPlus',
    description: 'BPP 游戏内模组：图鉴、历史与战斗回放',
    fileStems: [
      'BazaarPlusPlus',
      'BazaarPlusPlus.ModApi',
      'BazaarPlusPlus.Storage',
      'BazaarPlusPlus.Localization',
      'BazaarPlusPlus.history-database',
      'BazaarPlusPlus.version',
      'e_sqlite3',
      'Microsoft.Data.Sqlite',
      'SixLabors.ImageSharp',
      'SQLitePCLRaw.batteries_v2',
      'SQLitePCLRaw.core',
      'SQLitePCLRaw.provider.e_sqlite3',
      'System.Buffers',
      'System.Numerics.Vectors',
      'System.Text.Encoding.CodePages'
    ],
    dirNames: []
  },
  {
    id: 'livecardsdump',
    name: 'LiveCardsDump',
    description: '把当前商店 / 棋盘 / 仓库卡牌导出为 JSON（本仓库开发）',
    fileStems: ['LiveCardsDump'],
    dirNames: []
  }
];

export interface BuiltinPluginDefinition {
  id: BuiltinPluginId;
  name: string;
  description: string;
  version: string;
}

export const BUILTIN_PLUGINS: BuiltinPluginDefinition[] = [
  {
    id: 'actionaxis',
    name: 'Bazaar Action Axis',
    description: '在游戏内按冷却轴推演当前棋盘与怪物，支持时间轴拖动和实战回放对照。',
    version: '1.2.2'
  },
  {
    id: 'livecardsdump',
    name: 'LiveCardsDump',
    description: '每 2 秒导出一次当前商店、棋盘和仓库卡牌快照，供本仓库导入工具使用。',
    version: '1.0.0'
  }
];

/** Windows 上 Steam 可能出现的 App 数据目录 */
export const BPP_INSTALLER_DIRS = ['BazaarPlusPlusInstaller', 'com.bazaarplusplus.bppinstaller'];
