# Bazaar 插件管理器

本地运行的《大巴扎》(The Bazaar) BepInEx 插件管理器。使用 Electron + React + TypeScript，参考 BazaarPlusPlus 安装器的使用方式，把「游戏目录、插件、本地数据」放在一个桌面应用里管理。

## 功能

- **启动自动检测游戏目录**：读取 Steam 注册表、所有 Steam 库的 `libraryfolders.vdf`、`appmanifest_1617400.acf`，并扫描各磁盘的常见 Steam 安装位置；找不到时可手动选择包含 `TheBazaar.exe` 的目录。
- **插件识别**：扫描 `BepInEx/plugins`，把 BPP 的主 DLL / 依赖 DLL / 版本文件归为一个插件，识别 Bazaar Action Axis（DLL + 数据目录）与 LiveCardsDump，其余目录或 DLL 作为外部插件展示。
- **安装插件**：
  - 一键部署本仓库内置的 Action Axis 与 LiveCardsDump（含 cards.json、skills.json、卡图）。
  - 安装本地 ZIP：支持 `BepInEx/plugins/...` 结构、单插件文件夹，或根目录 DLL。
  - 从 URL 安装：支持 `.zip` 与单个 `.dll` 直链，最大 200 MB。
- **卸载插件**：先备份到管理器数据目录，再从 `BepInEx/plugins` 删除。
- **数据清理**：按组列出 BPP 数据、行动轴运行数据与卡图缓存、LiveCardsDump 输出、BepInEx 配置；清理时统一移入 Windows 回收站。
- **启动游戏**：优先通过 Steam 启动，失败时直接运行 `TheBazaar.exe`。

## 快速开始

要求 Node.js 18+，Windows。

```bash
cd plugin-manager
npm install
npm run dev
```

首屏会自动检测游戏目录并读取插件，不需要先配置。

### 打包 portable exe

```bash
npm run package
# 产物：release/Bazaar Plugin Manager-0.1.0-portable.exe
```

打包时会把这些文件作为内置资源带入安装包：

| 来源 | 用途 |
|---|---|
| `../tools/actionaxis_plugin/build/ActionAxisPlugin.dll`、`ActionAxisEngine.dll` | 内置 Action Axis |
| `../tools/livecards_plugin/LiveCardsDump.dll` | 内置 LiveCardsDump |
| `../src/data/cards.json`、`../data/skills.json` | 行动轴卡牌与技能数据 |
| `../captures/card_icons/*.png` | 行动轴初始卡图（已有文件不覆盖） |

构建产物缺失时，对应的「安装」按钮会显示为不可用，但应用本身仍可管理已安装插件和数据。

## 工作方式

### 游戏目录检测顺序

1. 管理器保存的上次路径（`config.json`）。
2. `HKCU\Software\Valve\Steam` 的 `SteamPath`，以及 `HKLM` 下的 `InstallPath`。
3. 每个 Steam 库的 `steamapps/libraryfolders.vdf`。
4. `<库>/steamapps/appmanifest_1617400.acf` 中的 `installdir`。
5. 所有磁盘的 `SteamLibrary`、`Program Files (x86)\Steam`、`Games\SteamLibrary` 等常见路径。

检测到有效目录后会写回 `config.json`，下次启动优先使用。

### 插件安装与备份

- 只写入 `BepInEx/plugins`，不修改游戏本体与 `BepInEx/core`。
- 覆盖任何已有文件前，先备份到：
  `%APPDATA%/bazaar-plugin-manager/backups/<时间戳>/`
- 游戏运行中时拒绝安装、卸载和清理游戏目录内的数据。
- ZIP 解压由应用自己完成：拒绝绝对路径与 `..` 穿越，限制 20000 个条目、解压后 500 MB。

### 数据清理范围

| 分组 | 内容 |
|---|---|
| BPP 数据 | 游戏目录下 `BazaarPlusPlusV*`、`%APPDATA%/BazaarPlusPlusInstaller`、`%APPDATA%/com.bazaarplusplus.bppinstaller` |
| 行动轴数据 | `BepInEx/plugins/BazaarActionAxis` 下的 `last_*`、`combats/`、`icons/` |
| LiveCardsDump 输出 | 仓库 `captures/live_cards_bpp.json`、`live_cards_bpp.error.txt`、`live_loadout.json` |
| BepInEx 配置 | `BepInEx/config` 中 BPP / ActionAxis / LiveCardsDump 相关 `.cfg` |

清理统一使用 `shell.trashItem` 移入 Windows 回收站，不直接永久删除。

## 开发

```bash
npm run dev        # electron-vite 开发模式
npm run build      # 生产构建
npm run typecheck  # 主进程与渲染层类型检查
npm test           # Vitest（20 个测试）
npm run package    # electron-builder portable 打包
```

目录：

```
src/main/          Electron 主进程：检测、插件、数据、IPC
src/preload/       contextBridge 白名单
src/renderer/      React 界面
src/shared/        共享类型与已知插件清单
```

### 安全边界

- `contextIsolation: true`、`nodeIntegration: false`，渲染层只能调用 preload 暴露的 IPC。
- 卸载/清理的路径全部来自主进程重新扫描的结果，不接受渲染层传入的任意路径。
- 打开目录只允许游戏目录、管理器数据目录、`%APPDATA%` 与本仓库路径。
- 除用户主动输入的 URL 安装外，不发起网络请求。

## 限制

- 只面向 Windows / Steam 版《The Bazaar》；没有实现 BepInEx 本体的安装，只管理 `BepInEx/plugins`。
- 不解析 DLL 元数据，插件名称来自已知清单或文件名。
- 外部插件的识别以目录或单个 DLL 为单位；同一个目录里混合多个插件时无法拆分。
- 数据清理按固定规则扫描，不包含 BPP 在游戏目录外可能产生的其他文件。
- 不包含任何游戏程序集或游戏数据本体。
