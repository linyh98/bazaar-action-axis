/** 游戏与 BepInEx 状态 */
export interface BepInExState {
  installed: boolean;
  version: string | null;
  pluginsDir: string;
  configDir: string;
}

export type GameSource = 'saved' | 'steam' | 'manual' | 'scan';

export interface GameState {
  found: boolean;
  path: string | null;
  exePath: string | null;
  source: GameSource | null;
  bepinex: BepInExState | null;
  running: boolean;
  steamAppId: string;
}

export type BuiltinPluginId = 'actionaxis' | 'livecardsdump';

/** BepInEx/plugins 中识别出的一个插件 */
export interface PluginInfo {
  id: string;
  name: string;
  /** 已知插件标识；未知插件为 null */
  knownId: string | null;
  description: string;
  kind: 'known' | 'folder' | 'file';
  /** 相对 BepInEx/plugins 的文件列表 */
  files: string[];
  /** 相对 BepInEx/plugins 的目录列表 */
  dirs: string[];
  sizeBytes: number;
  modifiedAt: number;
}

/** 管理器内置、可一键部署的插件 */
export interface BuiltinPluginInfo {
  id: BuiltinPluginId;
  name: string;
  description: string;
  version: string;
  installed: boolean;
  /** 本机是否存在可部署的构建产物 */
  available: boolean;
  sourceNote: string;
  files: number;
  sizeBytes: number;
}

export type DataGroup = 'bpp' | 'actionaxis' | 'livecards' | 'bepinex';

/** 一个可清理的本地数据目标 */
export interface DataTarget {
  id: string;
  group: DataGroup;
  name: string;
  description: string;
  /** 主要路径，用于展示和打开 */
  path: string;
  /** 实际删除的路径集合（可能多个） */
  paths: string[];
  kind: 'dir' | 'file';
  exists: boolean;
  sizeBytes: number;
  itemCount: number;
  /** 删除时要求游戏未运行 */
  requiresGameStopped: boolean;
}

export interface AppConfig {
  gamePath: string | null;
  autoDetect: boolean;
}

export interface Bootstrap {
  config: AppConfig;
  game: GameState;
  plugins: PluginInfo[];
  builtin: BuiltinPluginInfo[];
  dataTargets: DataTarget[];
  userDataDir: string;
  appVersion: string;
  platform: string;
}

export interface ActionResult {
  ok: boolean;
  message: string;
  detail?: string;
  /** 操作完成后刷新的完整状态 */
  bootstrap?: Bootstrap;
}

export interface CleanResult extends ActionResult {
  trashed: string[];
  failed: { path: string; message: string }[];
}

/** preload 暴露给渲染进程的接口 */
export interface ManagerApi {
  bootstrap: () => Promise<Bootstrap>;
  detectGame: () => Promise<Bootstrap>;
  chooseGamePath: () => Promise<Bootstrap | null>;
  launchGame: () => Promise<ActionResult>;
  installBuiltin: (id: BuiltinPluginId) => Promise<ActionResult>;
  installFromZip: () => Promise<ActionResult>;
  installFromUrl: (url: string) => Promise<ActionResult>;
  uninstallPlugin: (id: string) => Promise<ActionResult>;
  refreshData: () => Promise<DataTarget[]>;
  cleanData: (ids: string[]) => Promise<CleanResult>;
  openPath: (path: string) => Promise<string>;
  openUserData: () => Promise<void>;
  openExternal: (url: string) => Promise<void>;
  updateConfig: (partial: Partial<AppConfig>) => Promise<Bootstrap>;
  copyToClipboard: (text: string) => Promise<void>;
}
