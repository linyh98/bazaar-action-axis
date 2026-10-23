# 行动轴游戏内插件设计

日期：2026-10-23
状态：已确认，待实现

## 1. 背景与目标

现有行动轴只存在于网页模拟器（`npm run dev`）。玩家在对局中想查看当前棋盘每张卡的触发次数、伤害和控制量时，必须切出游戏、打开网页、点击「识别当前卡」。

目标：做一个独立的 BepInEx 伴生插件，在游戏内一步完成「识别当前棋盘 → 模拟 → 打开行动轴面板」，面板提供可拖动时间轴和每张卡的累计统计角标。

成功标准：

- 游戏内点击「行动轴」按钮或按热键，面板立即打开并显示当前棋盘卡牌。
- 拖动时间轴时，每张卡的角标实时显示 0 到当前时刻的累计值。
- 不需要浏览器、不需要手动启动本地服务。
- 模拟结果与网页版 TS 引擎在相同输入下逐事件一致（由 parity 测试保证）。

## 2. 非目标

- 不修改 BazaarPlusPlus（BPP）本体，不替换其 DLL。
- 不显示敌方卡牌行；敌方用无敌假人代替。
- 不模拟附魔效果和属性加成的具体数值（仅显示附魔名，冷却/弹药/多重使用实时属性）。
- 不拦截游戏鼠标输入（IMGUI 覆盖层限制）。
- 不做回放、播放动画、导出、云同步。

## 3. 方案选型

| 选项 | 结论 | 原因 |
|---|---|---|
| 浏览器页面 | 否 | 用户明确要游戏内面板 |
| 独立插件 + IMGUI 面板 | **采用** | 不改 BPP、可用系统 `csc` 编译、迭代快 |
| 独立插件 + UGUI 面板 | 否 | 视觉更好但代码量约翻倍，离开 Unity 编辑器难迭代 |
| 反射注册进 BPP `OverlayPanelHost` | 后续可选 | 可复用互斥/热键，但依赖 BPP 内部实现 |
| Fork BPP 源码 | 否 | 需要 .NET SDK 10、游戏库和 BPP 构建链，且替换现有 BPP |

模拟计算：**C# 内移植完整引擎**，不使用本地 Node 服务。

## 4. 架构

```
┌─────────────────────────────────────────────────────┐
│ ActionAxisPlugin.dll (BepInEx 插件)                  │
│                                                     │
│  LauncherButton/Hotkey ──► LiveBoardReader          │
│                              │ (BPP 反射)            │
│                              ▼                      │
│                          CardCatalog                │
│                         (cards.json + 映射)          │
│                              │                      │
│                              ▼                      │
│                        ActionAxisEngine.dll         │
│                        (纯 C#，移植 TS 引擎)          │
│                              │                      │
│                              ▼                      │
│                          Aggregator                 │
│                     (0.1s 桶累计统计)                │
│                              │                      │
│                              ▼                      │
│                       ActionAxisPanel (IMGUI)       │
└─────────────────────────────────────────────────────┘
```

### 4.1 `ActionAxisEngine.dll`

纯 C# 类库，不引用 UnityEngine / BepInEx / BPP。逐模块移植：

- `Types.cs`：`CardDef`、`Effect`、`Target`、`NumExpr`、`TriggerDef`、`Loadout`、`BattleEvent`、`BattleResult`、`Totals` 等。
- `Rules.cs`：`DEFAULT_RULES`、`resolveRules`，默认值与 TS 版一致。
- `Rng.cs`：复刻 TS 的 mulberry32 变体，使用 `uint` 运算和 `unchecked`。
- `BattleContext.cs`：内部英雄/物品状态和上下文。
- `Effects.cs`：`evaluateNum`、`applyDamage`、`applyHeal`、`applyShield`、`resolveEffect`。
- `Triggers.cs`：触发队列和结算。
- `Battle.cs`：`initBattle`、冷却推进、激活、DOT、主循环、`simulate`。
- `EffectStats.cs`：引擎额外输出 `EffectStat` 列表，供面板统计控制效果。
- `Aggregate.cs`：把 `events` + `effectStats` 按 0.1 秒桶累计成每张卡的统计序列。
- `Json.cs`：最小 JSON 解析器（对象、数组、字符串含转义、数字、布尔、null）。

与 TS 版的差异（有意为之）：

- 不生成 `snapshots`（面板不需要），只输出 `events`、`effectStats`、`totals`、`winner`、`endTime`、`warnings`。
- 新增 `EffectStat`：每次效果结算记录一次，与目标数量无关。
- 所有 JS 取整使用 `Math.Floor(x + 0.5)` 语义，避免 C# 银行家舍入。
- 排序使用稳定排序（LINQ `OrderBy` 或带索引比较）。

### 4.2 `ActionAxisPlugin.dll`

- `ActionAxisPlugin`：BepInEx 入口，注册配置、创建面板 GameObject。
- `LiveBoardReader`：反射调用 `BazaarPlusPlus.GameInterop.LiveCards.LiveCardSnapshotReader.Read()`，读取 `BoardItems`，提取 `instanceId`、`templateId`、`tier`、`order`、`socket`、`enchantment`、`attributes`。
- `CardCatalog`：启动时解析 `data/cards.json`，建立 `baseItemId → CardDef[]` 索引；实现 `findCard(templateId, tier)`（精确品阶优先，否则最低品阶）。
- `LiveOverrides`：用快照属性覆盖 `cooldown = CooldownMax/1000`（属性存在即覆盖，含 0）、`ammoMax = AmmoMax`（>0 时）、`multicastBonus = Multicast-1`（属性存在即覆盖，替换静态多重而不是叠加）。
- `Aggregator`：调用引擎后生成每张卡的累计序列。
- `IconCache`：从 `icons/<baseItemId>.png` 加载 96×96 纹理，缺失时返回 null。
- `ActionAxisPanel`：IMGUI 面板、时间轴、卡牌网格、按钮、错误状态。
- `Launcher`：屏幕边缘小按钮 + 热键（默认 F8）。

### 4.3 数据与图标管线

- `tools/build_card_icons.py`
  - 读取 `src/data/cards.json`。
  - 按 `baseItemId` 去重（约 1041 张有图）。
  - 下载 AVIF，用 Pillow 转 96×96 PNG，输出 `captures/card_icons/<baseItemId>.png`。
  - 支持 `--limit`、`--concurrency`、`--force`、`--out`。
  - 实测卡图 CDN `howbazaar-images.b-cdn.net` 返回 403、BPP 静态图源 404；图标缺失时插件用品阶色块 + 名称首字占位。管线保留，换可用图源后可直接复用。
- 构建脚本 `tools/build_actionaxis_plugin.sh`
  - 用系统 `csc.exe` 编译 `ActionAxisEngine.dll` 和 `ActionAxisPlugin.dll`。
  - 把 DLL 部署到 `BepInEx/plugins/`，把 `cards.json` 和 `icons/` 部署到 `BepInEx/plugins/BazaarActionAxis/`。
  - 图标缺失时给出提示，不阻塞 DLL 编译。
- 插件数据目录默认 `Paths.PluginPath/BazaarActionAxis`，可在 BepInEx 配置中覆盖。

## 5. 数据流

1. 玩家点击「行动轴」按钮或按热键。
2. 插件读取 BPP 当前 `BoardItems`；失败时面板显示原因。
3. 每张卡映射到 `cards.json` 的卡牌定义；未匹配的卡保留占位。
4. 用实时属性覆盖冷却、弹药上限、多重施放。
5. 构造对局：玩家棋盘 vs 无敌假人（`enemyMaxHp = 100000`，敌方无卡），`dt = 0.1`，`maxTime = 60`，`seed = 1`。
6. 引擎返回事件、效果统计、总量、结束时间、警告。
7. 聚合器按 0.1 秒桶生成每张卡的累计序列。
8. 面板渲染时间轴和卡牌网格；拖动播放头时按 `floor(playhead / 0.1)` 读取累计值。
9. 点击刷新重新执行 2–8，保留面板打开状态并重置播放头到 0。

## 6. UI 规格

### 6.1 入口

- 屏幕右侧边缘一个「行动轴」小按钮，始终显示；点击打开/关闭面板。
- 热键默认 `F8`，可在 BepInEx 配置中修改。
- 按钮不参与游戏输入，尺寸约 `96×36`，按屏幕高度缩放。

### 6.2 面板

- 尺寸约 `980×480`（按 `Screen.height / 900` 缩放，限制 0.8–1.5）。
- 深色半透明背景（`#0D1117`，透明度约 0.96），金色 1px 描边（`#C8A24A`）。
- 标题栏：`行动轴 · 当前棋盘`，右侧「刷新」「×」。
- 状态行：来源/数量/未知卡/警告。
- 时间轴：全宽，0–`endTime` 秒，每 5 秒刻度，播放头可拖动，显示当前秒（保留 1 位小数）。
- 卡牌网格：每行 5 张，最多 2 行；按棋盘 socket 顺序排列。
- 卡牌单元：
  - 96×96 图标，品阶色描边（铜 `#B87333`、银 `#C0C0C0`、金 `#FFD700`、钻 `#B9F2FF`、传说 `#FF6B35`）。
  - 名称（最多两行）、冷却 `CD 4.0s`、附魔名（如有）。
  - 角标只显示非零项：
    - `触发 ×n`（白）
    - `伤害 n`（红）
    - `加速 ×n · Ns`（青）
    - `冻结 ×n · Ns`（蓝）
    - `减速 ×n · Ns`（紫）
    - `护盾 n`（黄）
    - `治疗 n`（绿）
    - `灼烧 n 层`（橙）
    - `中毒 n 层`（深绿）
    - `充能 Ns`（青绿）
- 未识别卡显示品阶色块 + 「未知卡」+ GUID 前 8 位。
- 棋盘为空/BPP 不可用时显示明确提示。
- 字体：`Font.CreateDynamicFontFromOSFont`，优先「Microsoft YaHei UI」，回退「Microsoft YaHei」「SimHei」。
- `GUI.depth = -1000`，画在游戏 UI 之上。
- 面板打开时按 Esc 关闭；按热键再次切换。
- 面板不阻塞游戏输入（已知限制）。

### 6.3 统计口径

| 角标 | 来源 | 口径 |
|---|---|---|
| 触发 ×n | `activate` 事件 | 激活次数；多重施放每段都计一次 |
| 伤害 n | `damage` 事件 | 实际伤害之和（含暴击） |
| 加速/冻结/减速/充能 | `EffectStat` | 效果结算次数 + 声明秒数之和，与目标数量无关 |
| 护盾/治疗 | `shield`/`heal` 事件 | 实际数值之和 |
| 灼烧/中毒 | `EffectStat` | 施加层数之和 |

播放头在 0 秒时所有角标为 0；只显示非零项。

## 7. 正确性保障

### 7.1 TS → C# parity

- 新增 `tools/export_engine_fixtures.ts`，用现有 TS 引擎运行固定场景，导出 golden JSON。
- 场景覆盖：基础伤害、加速/减速/冻结、充能、多重/弹药/装填、触发链、DOT、暴击/吸血、击杀触发、每秒触发、多目标、百分比生命、重复卡、未知卡、空棋盘、60 秒上限。
- C# 控制台 runner `tools/actionaxis_plugin/tests/ParityRunner.cs`：
  - 读取 fixtures。
  - 运行 C# 引擎。
  - 逐事件比较 `kind/time/side/sourceItemId/targetItemId/value/absorbed/hpAfter/shieldAfter/note`，以及 `totals/winner/endTime/warnings`。
  - 不比较 snapshots（C# 不生成）。
  - 失败时打印首个差异和上下文。

### 7.2 其他测试

- 映射：精确品阶、最低品阶回退、大小写、未知 templateId、空 GUID。
- 实时覆盖：`CooldownMax`、`AmmoMax`、`Multicast` 的边界（0、负数、缺失）。
- 聚合：构造事件/EffectStat，验证指定时间点的累计值。
- JSON：cards.json 子集、转义字符、数字、bool、null。
- 手动游戏内冒烟：按钮/热键、刷新、拖动、图标、未知卡、空棋盘、BPP 未加载。

## 8. 构建与部署

```bash
# 1. 生成图标（首次或卡图更新时）
python tools/build_card_icons.py

# 2. 编译并部署
bash tools/build_actionaxis_plugin.sh

# 3. 运行 parity 测试（不部署）
bash tools/build_actionaxis_plugin.sh --test
```

部署结果：

```
<GameDir>/BepInEx/plugins/ActionAxisPlugin.dll
<GameDir>/BepInEx/plugins/ActionAxisEngine.dll
<GameDir>/BepInEx/plugins/BazaarActionAxis/cards.json
<GameDir>/BepInEx/plugins/BazaarActionAxis/icons/*.png
```

重启 The Bazaar 后生效。日志：`BepInEx/LogOutput.log`，插件日志前缀 `[ActionAxis]`。

## 9. 风险与限制

- **引擎移植偏差**：由 parity 测试覆盖；fixtures 更新时需同步审查。
- **BPP 内部 API 变化**：反射读取，BPP 大版本升级可能失效；与现有 `LiveCardsDump` 插件同类风险。
- **附魔**：v1 只显示附魔名，不改变数值。
- **卡牌文本解析质量**：`cards.json` 中约 755 张结构化、其余部分结构化，结果与网页版一致，不额外改进。
- **图标下载**：首次约 1041 张，耗时数分钟；当前 CDN 返回 403，实际使用品阶色块占位。
- **输入穿透**：IMGUI 不拦截游戏点击。
- **敌方假人**：不显示敌方行；敌方物品指向的效果通过 `EffectStat` 计入结算次数。

## 10. 交付阶段

1. 引擎移植 + 单元/parity 测试。
2. 卡库/映射/实时覆盖 + 卡图管线。
3. 面板 UI + 按钮/热键。
4. 部署脚本、README、游戏内手动验证。

每个阶段完成后运行 `npm test -- --run`（不回归网页版）和 C# parity 测试。
