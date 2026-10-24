# Bazaar 插件管理器

本地运行的《大巴扎》(The Bazaar) BepInEx 插件管理器。使用 Electron + React + TypeScript，参考 BazaarPlusPlus 安装器的使用方式，把「游戏目录、插件、本地数据」放在一个桌面应用里管理。

## 功能

- **启动自动检测游戏目录**：读取 Steam 注册表、所有 Steam 库的 `libraryfolders.vdf`、`appmanifest_1617400.acf`，并扫描各磁盘的常见 Steam 安装位置；找不到时可手动选择包含 `TheBazaar.exe` 的目录。
- **插件识别**：扫描 `BepInEx/plugins`，把 BPP 的主 DLL / 依赖 DLL / 版本文件归为一个插件，识别 Bazaar Action Axis（DLL + 数据目录）与 LiveCardsDump，其余目录或 DLL 作为外部插件展示。
- **安装插件**：
  - 一键部署本仓库内置的 Action Axis 与 LiveCardsDump（含 cards.json、skills.json；已有卡图时一并部署）。
  - 安装本地 ZIP：支持 `BepInEx/plugins/...` 结构、单插件文件夹，或根目录 DLL。
  - 从 URL 安装：支持 `.zip` 与单个 `.dll` 直链，最大 200 MB。
- **卸载插件**：先备份到管理器数据目录，再从 `BepInEx/plugins` 删除。
- **数据清理**：按组列出 BPP 数据、行动轴运行数据与卡图缓存、LiveCardsDump 输出、BepInEx 配置；清理时统一移入 Windows 回收站。
- **启动游戏**：优先通过 Steam 启动，失败时直接运行 `TheBazaar.exe`。
- **自动更新**：读取 GitHub Releases 的最新版本，一键下载 portable exe，退出时替换并自动重启；内置插件 DLL 也可单独从同一个 Release 更新。

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
# 产物：release/Bazaar Plugin Manager-0.2.0-portable.exe
```

打包时会把这些文件作为内置资源带入安装包：

| 来源 | 用途 |
|---|---|
| `../tools/actionaxis_plugin/build/ActionAxisPlugin.dll`、`ActionAxisEngine.dll` | 内置 Action Axis |
| `../tools/livecards_plugin/LiveCardsDump.dll` | 内置 LiveCardsDump |
| `../src/data/cards.json`、`../data/skills.json` | 行动轴卡牌与技能数据 |
| `../captures/card_icons/*.png` | 行动轴初始卡图（若存在；已有文件不覆盖） |

构建产物缺失时，对应的「安装」按钮会显示为不可用，但应用本身仍可管理已安装插件和数据。

## 更新

更新源是 <https://github.com/linyh98/bazaar-action-axis/releases> 的 latest Release，无需配置 Token（公开仓库）。

| 操作 | 行为 |
|---|---|
| 启动时 | 静默检查一次，有新版本时在侧边栏显示「可更新」徽标 |
| 检查更新 | `GET /repos/linyh98/bazaar-action-axis/releases/latest`，用 `tag_name` 与当前版本比较 |
| API 限流时 | 未登录 API 每个 IP 每小时只有 60 次，共享代理下会 403；此时跟随 `/releases/latest` 的 302 取 tag，再按发布命名约定拼下载地址（GitHub 会把资产名中的空格换成点）；发布页也不可达时最后回退到仓库 `plugin-manager/package.json` 版本号 |
| 下载更新 | 下载匹配 `*-portable.exe` 的资产到 `%APPDATA%/bazaar-plugin-manager/updates/<版本>/`，有 `sha256` 摘要时校验 |
| 重启并更新 | 生成等待退出的 PowerShell 脚本 → 关闭管理器 → 备份当前 exe 为 `.bak` → 替换 → 重新启动 |
| 更新内置插件 DLL | 下载 `ActionAxisPlugin.dll`、`ActionAxisEngine.dll`、`LiveCardsDump.dll` 到 `%APPDATA%/bazaar-plugin-manager/builtin/`，部署时优先使用该副本 |

开发模式下不会替换程序本体，「重启并更新」会直接拒绝，避免把 `electron.exe` 换掉。

### 发布新版本

```bash
cd plugin-manager
node scripts/publish-release.mjs            # 打包 + 创建 Release
node scripts/publish-release.mjs --skip-build --draft
```

脚本会发布 `release/Bazaar Plugin Manager-<version>-portable.exe` 与三个内置插件 DLL，tag 取 `package.json` 的 `version`。需要已登录的 `gh`；走代理时先设置 `HTTPS_PROXY`。

> Release 必须先上传 portable exe，管理器才能自动更新；缺少 exe 时只会在界面上给出提示并附上发布页链接。

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
npm test           # Vitest（29 个测试）
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
- 网络请求只有两类：GitHub Releases 更新检查/下载，以及用户主动输入的 URL 安装；下载都限制体积并校验可选摘要。
- 自动替换只针对便携版 exe，且保留 `.bak` 备份；开发模式禁用。

## 限制

- 只面向 Windows / Steam 版《The Bazaar》；没有实现 BepInEx 本体的安装，只管理 `BepInEx/plugins`。
- 不解析 DLL 元数据，插件名称来自已知清单或文件名。
- 外部插件的识别以目录或单个 DLL 为单位；同一个目录里混合多个插件时无法拆分。
- 数据清理按固定规则扫描，不包含 BPP 在游戏目录外可能产生的其他文件。
- 不包含任何游戏程序集或游戏数据本体。
