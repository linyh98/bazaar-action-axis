# 行动轴游戏内插件 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 做一个独立 BepInEx 插件，在游戏内一步读取当前棋盘、用 C# 引擎模拟，并打开带可拖动时间轴与累计角标的行动轴面板。

**Architecture:** 纯 C# 引擎库移植现有 TS 引擎（无 Unity/BPP 依赖）→ 插件层反射读取 BPP 棋盘并映射到 `cards.json` → 聚合器按 0.1s 桶累计事件 → IMGUI 面板渲染时间轴和卡牌网格。正确性由 TS 生成的 golden fixtures 做逐事件 parity 保证。

**Tech Stack:** C# / .NET Framework 4.x（系统 `csc.exe`）、BepInEx 5、Unity IMGUI、Python 3.10 + Pillow（AVIF→PNG）、TypeScript + tsx（fixtures）。

**Spec:** `docs/superpowers/specs/2026-10-23-action-axis-plugin-design.md`

## Global Constraints

- 目标框架：.NET Framework 4.x；编译器固定 `C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe`。
- 不引入 .NET SDK、NuGet、Newtonsoft 或任何新运行时依赖。
- `ActionAxisEngine.dll` 不得引用 UnityEngine / BepInEx / BPP。
- 游戏目录默认 `D:/Program Files (x86)/Steam/steamapps/common/The Bazaar`，可用环境变量 `GAME_DIR` 覆盖。
- 插件数据目录默认 `<GameDir>/BepInEx/plugins/BazaarActionAxis`。
- 模拟参数固定：`dt=0.1`、`maxTime=60`、`seed=1`、`playerMaxHp=100`、`enemyMaxHp=100000`、敌方无卡。
- 统计口径固定为规格第 6.3 节。
- JS 数值语义：取整用 `Math.Floor(x + 0.5)`；时间边界用 `+1e-9`；排序必须稳定。
- 本项目不是 git 仓库：**跳过所有 commit 步骤**；每个任务以测试/验证命令收尾。
- 不修改网页版现有行为；只新增文件与 README 段落。

## Review Focus

- **时间边界**：事件恰好落在 0.1 的整数倍、整秒、60 秒上限时，角标归属必须与 TS 一致（桶边界用 `floor(t/dt + 1e-9)`）。
- **重复卡**：棋盘上同名卡多张时，引擎实例 ID 规则、聚合归属和面板行数不能串。
- **未知 templateId / 缺失品阶**：必须回退最低品阶或显示占位，不能崩溃或静默丢卡。
- **BPP 未加载 / 未开局 / 空棋盘**：面板必须给出可读错误，不能抛异常卡死。
- **缺失图标 / 长中文名 / 非 ASCII 附魔名**：占位和截断要正常，不能越界或乱码。
- **弹药 / 多重 / 冻结边界**：0 弹药、multicast=0、激活时处于冻结、装填溢出等不能多算或少算。

---

### Task 1: C# JSON 解析器与测试骨架

**Files:**
- Create: `tools/actionaxis_plugin/engine/Json.cs`
- Create: `tools/actionaxis_plugin/tests/TestHarness.cs`
- Create: `tools/actionaxis_plugin/tests/JsonTests.cs`
- Create: `tools/actionaxis_plugin/compile_tests.sh`

**Interfaces:**
- Consumes: 无。
- Produces: `JsonValue`（`Kind`、`Count`、`this[int]`、`this[string]`、`AsString()`、`AsDouble()`、`AsInt()`、`AsBool()`、`AsArray()`、`AsObject()`）、`Json.Parse(string)`；测试骨架 `TestHarness.RunAll()`、`Assert.Equal/True/Throws`。

- [ ] **Step 1: 写失败测试**

`tools/actionaxis_plugin/tests/JsonTests.cs`：

```csharp
public static void Run()
{
    var v = Json.Parse("{\"a\":1,\"b\":\"x\\ny\",\"c\":[true,null,2.5],\"d\":{\"e\":\"\\u4e2d\"}}");
    Assert.Equal(1, v["a"].AsInt());
    Assert.Equal("x\ny", v["b"].AsString());
    Assert.Equal(true, v["c"][0].AsBool());
    Assert.Equal(JsonKind.Null, v["c"][1].Kind);
    Assert.Equal(2.5, v["c"][2].AsDouble());
    Assert.Equal("中", v["d"]["e"].AsString());

    var cards = Json.Parse(File.ReadAllText("src/data/cards.json"));
    Assert.Equal(3620, cards.Count);
    Assert.Equal("0efe7d3f-4f18-4667-89a3-ac915a93a9f5@Gold", cards[0]["id"].AsString());
    Assert.Throws<FormatException>(() => Json.Parse("{"));
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `bash tools/actionaxis_plugin/compile_tests.sh`
Expected: 编译失败，提示 `Json` / `JsonKind` 不存在。

- [ ] **Step 3: 实现解析器与测试骨架**

`Json.cs` 支持对象、数组、字符串（`\" \\ \/ \b \f \n \r \t \uXXXX`）、数字、`true/false/null`、空白；数字用 `double` 解析，`AsInt` 用 `(int)Math.Floor` 语义。`TestHarness` 维护 `int failures`，`Assert` 失败时打印文件/断言并让进程退出码为 1。

`compile_tests.sh`：

```bash
#!/usr/bin/env bash
set -euo pipefail
CSC="C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe"
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
mkdir -p "$HERE/build"
"$CSC" -nologo -target:exe -out:"$HERE/build/EngineTests.exe" \
  -r:System.dll -r:System.Core.dll \
  "$HERE"/engine/*.cs "$HERE"/tests/*.cs
cd "$ROOT"
"$HERE/build/EngineTests.exe"
```

- [ ] **Step 4: 运行测试确认通过**

Run: `bash tools/actionaxis_plugin/compile_tests.sh`
Expected: 输出 `OK` 且退出码 0；`cards.json` 解析出 3620 张。

- [ ] **Step 5: 回归网页版**

Run: `npm test -- --run`
Expected: 现有测试全绿（本任务未改 TS）。

---

### Task 2: 引擎类型、规则与 RNG

**Files:**
- Create: `tools/actionaxis_plugin/engine/Types.cs`
- Create: `tools/actionaxis_plugin/engine/Rules.cs`
- Create: `tools/actionaxis_plugin/engine/Rng.cs`
- Create: `tools/actionaxis_plugin/engine/JsMath.cs`
- Create: `tools/actionaxis_plugin/tests/RngTests.cs`
- Create: `tools/actionaxis_plugin/tests/RulesTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `JsonValue`。
- Produces:
  - `CardDef`（`Id/BaseItemId/Name/NameCn/Size/Tier/Cooldown/Effects/Triggers/Tags/AmmoMax/StartReady/Passive/ParseStatus/RawTooltips/DataSource`）
  - `Effect`（`Kind/Amount/Target/Seconds/Count/Chance/Percent`）、`NumExpr`、`Target`、`TriggerDef`、`LoadoutItem`、`Loadout`
  - `BattleEvent`、`EffectStat`、`Totals`、`BattleResult`、`RuleOverrides`、`ResolvedRules`
  - `Rules.Resolve(RuleOverrides)`、`Rng(seed).Next()/Int(max)/Chance(p)`
  - `JsMath.Round(double)`、`JsMath.FloorEps(double)`

- [ ] **Step 1: 写失败测试**

`RngTests.cs`：

```csharp
var r = new Rng(1);
Assert.Near(0.627073940588, r.Next());
Assert.Near(0.002735721180, r.Next());
Assert.Near(0.527447039960, r.Next());
Assert.Near(0.981050967472, r.Next());
Assert.Near(0.968377898214, r.Next());

var r2 = new Rng(1);
Assert.Equal(6, r2.Int(10));
Assert.Equal(0, r2.Int(10));
Assert.Equal(5, r2.Int(10));
Assert.Equal(9, r2.Int(10));

Assert.True(!new Rng(1).Chance(0));
Assert.True(new Rng(1).Chance(1));
```

`RulesTests.cs`：

```csharp
var defaults = Rules.Resolve(new RuleOverrides());
Assert.Near(0.1, defaults.Dt);
Assert.Near(60, defaults.MaxTime);
Assert.Near(2, defaults.HasteMultiplier);
Assert.Near(0.5, defaults.SlowMultiplier);
Assert.Near(2, defaults.CritMultiplier);
Assert.Equal(500, defaults.MaxEventsPerTick);
Assert.Equal(20, defaults.MaxTriggerDepth);
Assert.Equal(10, defaults.MaxSlots);

var invalid = Rules.Resolve(new RuleOverrides { Dt = 0, MaxTime = -1, HasteMultiplier = 0 });
Assert.Near(0.1, invalid.Dt);
Assert.Near(60, invalid.MaxTime);
Assert.Near(2, invalid.HasteMultiplier);

Assert.Near(2.5, JsMath.Round(2.5));
Assert.Near(2.5, JsMath.Round(2.4999999999));
Assert.Near(-2, JsMath.Round(-2.5)); // floor(-2.0) == -2
```

- [ ] **Step 2: 运行测试确认失败**

Run: `bash tools/actionaxis_plugin/compile_tests.sh`
Expected: 编译失败，类型不存在。

- [ ] **Step 3: 实现类型与算法**

RNG 使用 `uint` + `unchecked` 复刻 TS：`a += 0x6d2b79f5; t = (a ^ (a >> 15)) * (1u | a); t = (t + ((t ^ (t >> 7)) * (61u | t))) ^ t; return (t ^ (t >> 14)) / 4294967296.0;`。`Rules.Resolve` 逐项按 `src/engine/rules.ts` 校验并回退默认值。

- [ ] **Step 4: 运行测试确认通过**

Run: `bash tools/actionaxis_plugin/compile_tests.sh`
Expected: `OK`。

---

### Task 3: 卡牌数据加载、映射与实时覆盖

**Files:**
- Create: `tools/actionaxis_plugin/engine/CardData.cs`
- Create: `tools/actionaxis_plugin/plugin/CardCatalog.cs`
- Create: `tools/actionaxis_plugin/plugin/LiveBoard.cs`
- Create: `tools/actionaxis_plugin/tests/CatalogTests.cs`

**Interfaces:**
- Consumes: Task 1 `Json`、Task 2 `CardDef`。
- Produces:
  - `CardDef.FromJson(JsonValue)`、`CardCatalog.Load(string path)`
  - `CardCatalog.Cards`、`CardCatalog.Find(string templateId, string? tier)`
  - `CardDef WithLiveOverrides(double? cooldown, int? ammoMax, int? multicast)`
  - `LiveCardEntry`（`InstanceId/TemplateId/Tier/Order/Socket/Enchantment/Attributes`）
  - `TierRank(string)`（Bronze=0 … Legendary=4，未知=99）

- [ ] **Step 1: 写失败测试**

`CatalogTests.cs`：

```csharp
var catalog = CardCatalog.Load("src/data/cards.json");
Assert.Equal(3620, catalog.Cards.Count);

var exact = catalog.Find("0efe7d3f-4f18-4667-89a3-ac915a93a9f5", "Gold");
Assert.Equal("0efe7d3f-4f18-4667-89a3-ac915a93a9f5@Gold", exact.Id);

var fallback = catalog.Find("0efe7d3f-4f18-4667-89a3-ac915a93a9f5", "NonExistentTier");
Assert.Equal("Bronze", fallback.Tier);

Assert.Equal(null, catalog.Find("", "Gold"));
Assert.Equal(null, catalog.Find("does-not-exist", "Gold"));
Assert.Equal(exact.Id, catalog.Find("0EFE7D3F-4F18-4667-89A3-AC915A93A9F5", "Gold").Id);

var over = exact.WithLiveOverrides(7.0, 3, 2);
Assert.Near(7.0, over.Cooldown);
Assert.Equal(3, over.AmmoMax);
Assert.Equal(2, over.MulticastBonus);
Assert.Near(4.0, exact.Cooldown); // 原对象不变
```

- [ ] **Step 2: 运行测试确认失败**

Run: `bash tools/actionaxis_plugin/compile_tests.sh`
Expected: 编译失败。

- [ ] **Step 3: 实现加载、映射与覆盖**

映射规则逐字移植 `tools/live_cards_map.ts` 的 `findCard`：GUID 小写比较；先精确 `baseItemId+tier`，否则同 `baseItemId` 按 `TierRank` 升序取第一个。`LiveCardEntry` 只做数据载体，反射填充在 Task 6。

- [ ] **Step 4: 运行测试确认通过**

Run: `bash tools/actionaxis_plugin/compile_tests.sh`
Expected: `OK`。

---

### Task 4: 效果结算与战斗主循环（核心 parity）

**Files:**
- Create: `tools/actionaxis_plugin/engine/BattleContext.cs`
- Create: `tools/actionaxis_plugin/engine/Effects.cs`
- Create: `tools/actionaxis_plugin/engine/Triggers.cs`
- Create: `tools/actionaxis_plugin/engine/Battle.cs`
- Create: `tools/actionaxis_plugin/tests/ParityRunner.cs`
- Create: `tools/export_engine_fixtures.ts`
- Create: `tools/actionaxis_plugin/tests/fixtures/engine_fixtures.json`（生成）

**Interfaces:**
- Consumes: Task 2 类型/Rules/Rng、Task 3 `CardDef.FromJson`。
- Produces:
  - `BattleContext`（`Player/Enemy/Items/Events/EffectStats/TriggerQueue/Totals/NextEventId/NextChainId/EventsThisTick/Ended/EndTime/Winner/Warnings`）
  - `Engine.Simulate(BattleConfig config) -> BattleResult`
  - `BattleConfig`（`Cards/Player/Enemy/PlayerMaxHp/EnemyMaxHp/Seed/Rules/MaxTime`）
  - `ParityRunner.Run(string fixturesPath) -> int`（0=通过）

- [ ] **Step 1: 生成 TS golden fixtures**

编写 `tools/export_engine_fixtures.ts`，用内联合成卡牌构造以下场景（每张卡 5 秒冷却、1 张卡起）：

1. `basic-damage`：一张伤害 5 的卡，60 秒。
2. `haste-slow`：一张给自己加速 3 秒、一张给敌方减速 2 秒。
3. `freeze`：一张冻结敌方 2 秒。
4. `charge`：一张充能 3 秒。
5. `multicast-ammo`：multicast=3、ammoMax=2 的卡。
6. `reload`：伤害卡 + 装填 2 的卡。
7. `triggers`：onUse 触发一张伤害卡。
8. `dot`：施加灼烧 4、中毒 3。
9. `crit-lifesteal`：暴击率 1.0、吸血 50%。
10. `on-kill`：onKill 触发伤害。
11. `every-second`：everySecond 触发护盾。
12. `multi-target`：攻击所有敌方物品（敌方放 3 张惰性卡）。
13. `percent-max-hp`：造成目标 10% 最大生命伤害。
14. `duplicate-cards`：玩家放两张相同卡。
15. `unknown-card`：loadout 引用不存在的卡。
16. `empty-board`：双方空卡组。
17. `time-cap`：高血量、无击杀，验证 60 秒上限。

每个场景写入：

```json
{
  "scenarios": [
    {
      "name": "basic-damage",
      "config": { "cards": { "...": {} }, "player": { "items": [] }, "enemy": { "items": [] },
                  "playerMaxHp": 100, "enemyMaxHp": 100, "seed": 1, "rules": {}, "maxTime": 60 },
      "expected": { "events": [], "totals": {}, "winner": "draw", "endTime": 60, "warnings": [] }
    }
  ]
}
```

`expected` 由 TS `simulate` 生成；删除 `snapshots`。命令：

```bash
npx tsx tools/export_engine_fixtures.ts
```

Expected: 生成 fixtures，17 个场景。

- [ ] **Step 2: 写 parity 测试并确认失败**

`ParityRunner.cs`：加载 fixtures，对每个场景调用 `Engine.Simulate`，按顺序比较事件的 `kind/time/side/sourceItemId/targetItemId/value/absorbed/hpAfter/shieldAfter/note`（数值容差 `1e-9`，`time` 容差 `1e-6`），再比较 `totals/winner/endTime/warnings`。首个差异打印场景名、事件序号、期望值、实际值。

Run: `bash tools/actionaxis_plugin/compile_tests.sh`
Expected: 编译失败（引擎不存在）或运行失败（场景全红）。

- [ ] **Step 3: 实现 `Effects` / `Triggers` / `Battle`**

逐函数移植：

- `src/engine/effects.ts`：`evaluateNum`、`applyHeal`、`applyShield`、`applyDamage`、`itemsForTarget`、`criticalAmount`、`resolveEffect`。
- `src/engine/triggers.ts`：`enqueueTrigger`、`drainTriggerQueue`（含 source-scoped 集合、深度/事件数上限）。
- `src/engine/battle.ts`：`initBattle`、`applyStaticEffects`、`advanceCooldowns`、`collectReady`（稳定排序）、`activate`、`checkDeath`、`computeWinner`、`tickDots`、`simulate`。
- 不生成 `snapshots`；`BattleResult` 只含 `Events/EffectStats/Totals/Winner/EndTime/Warnings`。
- 所有 JS `Math.round` 换成 `JsMath.Round`；`Number(x.toFixed(6))` 换成 `JsMath.RoundTo6`；时间比较用 `+1e-9`。
- 事件 ID、chain ID、depth 按 TS 逻辑生成，但 parity 不比较它们。

- [ ] **Step 4: 运行 parity 确认通过**

Run: `bash tools/actionaxis_plugin/compile_tests.sh`
Expected: 17 个场景全部 `PASS`，退出码 0。

- [ ] **Step 5: 回归网页版**

Run: `npm test -- --run`
Expected: 全绿。

---

### Task 5: EffectStats、聚合器与面板数据模型

**Files:**
- Create: `tools/actionaxis_plugin/engine/EffectStats.cs`
- Create: `tools/actionaxis_plugin/engine/Aggregate.cs`
- Create: `tools/actionaxis_plugin/tests/AggregateTests.cs`
- Create: `tools/actionaxis_plugin/plugin/Ui/PanelModel.cs`
- Create: `tools/actionaxis_plugin/tests/PanelModelTests.cs`

**Interfaces:**
- Consumes: Task 4 `BattleResult` / `BattleEvent`。
- Produces:
  - `EffectStat { double Time; string SourceItemId; string SourceCardId; string Kind; double Value; int TargetCount; }`
  - `CardStats`（`ActivateCount/Damage/Heal/Shield/HasteCount/HasteSeconds/FreezeCount/FreezeSeconds/SlowCount/SlowSeconds/ChargeSeconds/BurnStacks/PoisonStacks`）
  - `Aggregator.Build(BattleResult result, IReadOnlyList<string> playerItemIds, double dt) -> Dictionary<string, CardStatsSeries>`
  - `CardStatsSeries.At(double time) -> CardStats`
  - `PanelModel.BadgeLines(CardStats stats) -> IReadOnlyList<BadgeLine>`，`BadgeLine { string Text; string ColorKey; }`
  - `PanelModel.PlayheadFromMouse(float mouseX, float barX, float barWidth, double endTime) -> double`

- [ ] **Step 1: 写失败测试**

`AggregateTests.cs`：

```csharp
var events = new List<BattleEvent> {
  Ev(0.1, "p:0", "activate"),
  Ev(0.1, "p:0", "damage", value: 5),
  Ev(1.0, "p:0", "damage", value: 7),
  Ev(1.0, "p:0", "activate"),
};
var stats = new List<EffectStat> {
  St(0.1, "p:0", "haste", value: 3, targetCount: 2),
  St(2.0, "p:0", "freeze", value: 2, targetCount: 1),
  St(2.0, "p:0", "burn", value: 4, targetCount: 1),
};
var series = Aggregator.Build(new BattleResult { Events = events, EffectStats = stats, EndTime = 60 }, new[] { "p:0" }, 0.1)["p:0"];

var at0 = series.At(0);
Assert.Equal(0, at0.ActivateCount);
var at01 = series.At(0.1);
Assert.Equal(1, at01.ActivateCount);
Assert.Near(5, at01.Damage);
Assert.Equal(1, at01.HasteCount);
Assert.Near(3, at01.HasteSeconds); // 与 targetCount 无关
var at1 = series.At(1.0);
Assert.Equal(2, at1.ActivateCount);
Assert.Near(12, at1.Damage);
Assert.Near(0, at1.FreezeSeconds);
var at2 = series.At(2.0);
Assert.Equal(1, at2.FreezeCount);
Assert.Near(2, at2.FreezeSeconds);
Assert.Near(4, at2.BurnStacks);
```

`PanelModelTests.cs`：

```csharp
var zero = new CardStats();
Assert.Equal(0, PanelModel.BadgeLines(zero).Count);

var stats = new CardStats { ActivateCount = 3, Damage = 42, FreezeCount = 2, FreezeSeconds = 4, Heal = 9 };
var lines = PanelModel.BadgeLines(stats);
Assert.Equal("触发 ×3", lines[0].Text);
Assert.Equal("damage", lines[1].ColorKey);
Assert.Equal("冻结 ×2 · 4s", lines[2].Text);
Assert.Equal("治疗 9", lines[3].Text);

Assert.Near(0, PanelModel.PlayheadFromMouse(-10, 100, 800, 60));
Assert.Near(60, PanelModel.PlayheadFromMouse(2000, 100, 800, 60));
Assert.Near(22.5, PanelModel.PlayheadFromMouse(400, 100, 800, 60));
```

- [ ] **Step 2: 运行测试确认失败**

Run: `bash tools/actionaxis_plugin/compile_tests.sh`
Expected: 编译失败。

- [ ] **Step 3: 实现**

- `EffectStat` 在 `resolveEffect` 的每次结算处追加一次：`damage/heal/shield/burn/poison/regen/haste/slow/freeze/charge/reload`；`Value` 用声明数值（秒/层/量），`TargetCount` 用实际目标数。
- `Aggregator.Build` 预分配 `ticks = (int)Math.Floor(result.EndTime / dt + 1e-9) + 1`，按 `index = Math.Min(ticks-1, (int)Math.Floor(time/dt + 1e-9))` 累加。`activate` 计触发；`damage` 计伤害（`dotTick` 不计入直接伤害）；`heal/shield` 计实际值；控制类从 `EffectStats` 累计次数与秒数；`burn/poison` 累计层数。
- 面板模型：角标顺序固定为 触发、伤害、加速、冻结、减速、护盾、治疗、灼烧、中毒、充能；非零才输出；时间格式 `0.0`。
- `PlayheadFromMouse` 做 clamp 后按 0.1 取整（`Math.Round` 到一位小数）。

- [ ] **Step 4: 运行测试确认通过**

Run: `bash tools/actionaxis_plugin/compile_tests.sh`
Expected: `OK`。

---

### Task 6: BPP 棋盘读取（反射）

**Files:**
- Create: `tools/actionaxis_plugin/plugin/ReflectionUtil.cs`
- Create: `tools/actionaxis_plugin/plugin/LiveBoardReader.cs`
- Modify: `tools/actionaxis_plugin/plugin/LiveBoard.cs`
- Create: `tools/actionaxis_plugin/tests/ReflectionTests.cs`

**Interfaces:**
- Consumes: Task 3 `LiveCardEntry`。
- Produces:
  - `BoardReadResult { bool Ok; string Error; IReadOnlyList<LiveCardEntry> Cards; }`
  - `SnapshotExtractor.FromSnapshot(object? snapshot) -> BoardReadResult`（纯反射，可在测试中用假对象调用）
  - `LiveBoardReader.Read() -> BoardReadResult`（定位 BPP 类型并调用）
  - `ReflectionUtil.GetProp(object?, string)`、`GetEnumerable(object?)`、`EnumText(object?)`、`IntText(object?)`

- [ ] **Step 1: 写失败测试**

`ReflectionTests.cs` 定义假类型：

```csharp
class FakeCard {
  public string InstanceId { get; set; } = "itm_1";
  public string TemplateId { get; set; } = "ABC-123";
  public object Tier { get; set; } = FakeTier.Gold;
  public int Order { get; set; } = 2;
  public object SocketId { get; set; } = FakeSocket.Socket_3;
  public object EnchantmentType { get; set; } = null;
  public Dictionary<object, int> Attributes { get; set; } = new() { { FakeAttr.CooldownMax, 7000 } };
}
class FakeSnapshot { public List<FakeCard> BoardItems { get; set; } = new() { new FakeCard() }; }

var result = SnapshotExtractor.FromSnapshot(new FakeSnapshot());
Assert.True(result.Ok);
Assert.Equal(1, result.Cards.Count);
Assert.Equal("abc-123", result.Cards[0].TemplateId);       // 小写
Assert.Equal("Gold", result.Cards[0].Tier);
Assert.Equal(3, result.Cards[0].Socket);                   // 解析 Socket_3
Assert.Equal(7000, result.Cards[0].Attributes["CooldownMax"]);

Assert.True(!SnapshotExtractor.FromSnapshot(null).Ok);
Assert.True(!SnapshotExtractor.FromSnapshot(new FakeSnapshot { BoardItems = new() }).Ok);
```

- [ ] **Step 2: 运行测试确认失败**

Run: `bash tools/actionaxis_plugin/compile_tests.sh`
Expected: 编译失败。

- [ ] **Step 3: 实现反射读取**

`LiveBoardReader` 在 `AppDomain.CurrentDomain.GetAssemblies()` 中找 `BazaarPlusPlus.GameInterop.LiveCards.LiveCardSnapshotReader`；`Activator.CreateInstance(type, true)`；调用 `Read()`；取 `Snapshot` 属性；交给 `SnapshotExtractor`。属性读取使用 `BindingFlags.Public|NonPublic|Instance`。`SocketId` 字符串形如 `Socket_3`，取尾部数字；解析失败用 `Order`。

错误文案：
- 未找到 BPP 类型：`未找到 BazaarPlusPlus 的 LiveCardSnapshotReader，请确认 BPP 已安装并进入一局游戏`
- `Snapshot` 为空：`BPP 没有返回游戏状态，请先进入一局游戏`
- 棋盘为空：`当前棋盘没有卡牌`

- [ ] **Step 4: 运行测试确认通过**

Run: `bash tools/actionaxis_plugin/compile_tests.sh`
Expected: `OK`。

---

### Task 7: 卡图管线（AVIF → 96×96 PNG）

**Files:**
- Create: `tools/build_card_icons.py`
- Create: `tools/test_build_card_icons.py`

**Interfaces:**
- Consumes: `src/data/cards.json`。
- Produces:
  - `plan_icons(cards) -> list[{"base_item_id": str, "image": str}]`（按 `baseItemId` 去重，只保留有 `image` 的）
  - `icon_filename(base_item_id) -> str`（仅保留 `[A-Za-z0-9-]`，其余替换 `_`，加 `.png`）
  - `convert_to_icon(data: bytes, size=96) -> bytes`（AVIF/PNG/JPEG 输入，输出方形 PNG）
  - CLI：`--limit`、`--concurrency`、`--force`、`--out`、`--dry-run`

- [ ] **Step 1: 写失败测试**

`tools/test_build_card_icons.py`：

```python
def test_plan_icons_dedupes_by_base_item_id():
    cards = [
        {"baseItemId": "a", "image": "u1"},
        {"baseItemId": "a", "image": "u1"},
        {"baseItemId": "b", "image": "u2"},
        {"baseItemId": "c"},
    ]
    assert plan_icons(cards) == [
        {"base_item_id": "a", "image": "u1"},
        {"base_item_id": "b", "image": "u2"},
    ]

def test_icon_filename_sanitizes():
    assert icon_filename("0efe/7d3f@Gold") == "0efe_7d3f_Gold.png"

def test_convert_to_icon_produces_96px_png():
    from PIL import Image
    import io
    src = io.BytesIO()
    Image.new("RGB", (300, 200), "red").save(src, "PNG")
    out = convert_to_icon(src.getvalue(), 96)
    img = Image.open(io.BytesIO(out))
    assert img.format == "PNG"
    assert max(img.size) == 96
```

- [ ] **Step 2: 运行测试确认失败**

Run: `python -m unittest tools.test_build_card_icons -v`
Expected: ImportError（函数不存在）。

- [ ] **Step 3: 实现**

- 下载用 `urllib.request` + User-Agent（复用 `download_images.py` 写法）。
- 转换：`Image.open(BytesIO(data)).convert("RGBA")`，缩放到 `(96,96)` 的方形画布（`ImageOps.fit`），`save(format="PNG", optimize=True)`。
- 并发用 `ThreadPoolExecutor(max_workers=args.concurrency)`。
- 已存在且未 `--force` 时跳过。
- 失败只记录 URL 和原因，不中断整体。
- 输出默认 `captures/card_icons`。

- [ ] **Step 4: 运行单测确认通过**

Run: `python -m unittest tools.test_build_card_icons -v`
Expected: 3 个测试通过。

- [ ] **Step 5: 小样本联网验证**

Run: `python tools/build_card_icons.py --limit 3`
Expected: 下载并生成最多 3 个 PNG；打印成功数；`captures/card_icons` 非空。

---

### Task 8: IMGUI 面板、图标缓存与时间轴

**Files:**
- Create: `tools/actionaxis_plugin/plugin/IconCachePaths.cs`
- Create: `tools/actionaxis_plugin/plugin/Ui/ThemeKeys.cs`
- Create: `tools/actionaxis_plugin/plugin/IconCache.cs`
- Create: `tools/actionaxis_plugin/plugin/Ui/Theme.cs`
- Create: `tools/actionaxis_plugin/plugin/Ui/ActionAxisPanel.cs`
- Create: `tools/actionaxis_plugin/plugin/Ui/TimelineWidget.cs`
- Create: `tools/actionaxis_plugin/plugin/Ui/CardTile.cs`
- Create: `tools/actionaxis_plugin/tests/IconCacheTests.cs`

**Interfaces:**
- Consumes: Task 5 `PanelModel` / `CardStats`；Task 7 图标目录。
- Produces:
  - `IconCachePaths.PathFor(string dir, string baseItemId)`（纯函数，测试用）
  - `ThemeKeys.TierColorKey(string? tier)`（纯函数，测试用）
  - `IconCache(string dir)`、`Texture2D Get(string baseItemId)`（缓存、缺失返回 null）
  - `Theme`（颜色常量、字体初始化、`InitStyles()`）
  - `ActionAxisPanel`（`Open()/Close()/Toggle()/IsOpen`、`SetData(IReadOnlyList<CardTileModel>, double endTime, string status)`）
  - `TimelineWidget.Draw(Rect rect, double endTime, ref double playhead)`

- [ ] **Step 1: 写失败测试**

`IconCacheTests.cs`：

```csharp
Assert.Equal("C:/icons/abc.png", IconCachePaths.PathFor("C:/icons", "abc"));
Assert.Equal("C:/icons/_unknown.png", IconCachePaths.PathFor("C:/icons", ""));
Assert.Equal("C:/icons/a_b_c.png", IconCachePaths.PathFor("C:/icons", "a/b c"));
Assert.Equal("bronze", ThemeKeys.TierColorKey("Bronze"));
Assert.Equal("legendary", ThemeKeys.TierColorKey("Legendary"));
Assert.Equal("bronze", ThemeKeys.TierColorKey("Unknown"));
```

- [ ] **Step 2: 运行测试确认失败**

Run: `bash tools/actionaxis_plugin/compile_tests.sh`
Expected: 编译失败（`IconCachePaths` / `ThemeKeys` 不存在）。

- [ ] **Step 3: 实现面板**

- `ActionAxisOverlay : MonoBehaviour`，`OnGUI`：
  - `GUI.depth = -1000`。
  - `Theme.Scale = Mathf.Clamp(Screen.height / 900f, 0.8f, 1.5f)`。
  - 绘制半透明背板 + 金色描边。
  - 标题栏、刷新/关闭按钮、状态行。
  - `TimelineWidget`：绘制刻度（每 5 秒）、轨道、播放头；鼠标按下/拖动时更新 `playhead`。
  - 卡牌网格：每行 5 张，tile 尺寸按面板宽度计算；图标 `GUI.DrawTexture`，缺失时画品阶色块 + 名称前两字。
  - 角标用 `PanelModel.BadgeLines` 输出，颜色由 `Theme` 映射。
- 字体：`Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei UI","Microsoft YaHei","SimHei"}, 14)`，失败回退 `GUI.skin.font`。
- 所有 `GUIStyle` 在 `InitStyles()` 中构造一次；`richText = true`。
- 面板尺寸：`980×480` 逻辑像素，按 `Theme.Scale` 缩放。

- [ ] **Step 4: 运行纯函数测试确认通过**

Run: `bash tools/actionaxis_plugin/compile_tests.sh`
Expected: `OK`。

- [ ] **Step 5: 静态编译检查**

Run: `bash tools/build_actionaxis_plugin.sh --compile-only`
Expected: 插件 DLL 编译通过（脚本在 Task 10 完成；本步可先用临时 csc 命令检查，见任务说明）。

---

### Task 9: 插件入口、按钮/热键、配置

**Files:**
- Create: `tools/actionaxis_plugin/plugin/ActionAxisPlugin.cs`
- Create: `tools/actionaxis_plugin/plugin/Launcher.cs`
- Create: `tools/actionaxis_plugin/plugin/PluginConfig.cs`
- Create: `tools/actionaxis_plugin/tests/PathConfigTests.cs`

**Interfaces:**
- Consumes: Task 6 `LiveBoardReader`、Task 3 `CardCatalog`、Task 4 `Engine`、Task 5 `Aggregator`、Task 8 `ActionAxisPanel`。
- Produces:
  - `ActionAxisPlugin : BaseUnityPlugin`（GUID `com.bazaarsim.actionaxis`，名称 `Bazaar Action Axis`，版本 `1.0.0`）
  - `PluginConfig { KeyboardShortcut ToggleKey; string DataDir; }`
  - `PathConfig.ResolveDataDir(string pluginPath, string configured) -> string`
  - `Launcher`（屏幕右侧按钮 + 热键切换）

- [ ] **Step 1: 写失败测试**

`PathConfigTests.cs`：

```csharp
Assert.Equal("C:/plugins/BazaarActionAxis", PathConfig.ResolveDataDir("C:/plugins", ""));
Assert.Equal("C:/plugins/BazaarActionAxis", PathConfig.ResolveDataDir("C:/plugins", "BazaarActionAxis"));
Assert.Equal("D:/data", PathConfig.ResolveDataDir("C:/plugins", "D:/data"));
```

- [ ] **Step 2: 运行测试确认失败**

Run: `bash tools/actionaxis_plugin/compile_tests.sh`
Expected: 编译失败（`PathConfig` 不存在）。

- [ ] **Step 3: 实现入口与流程**

`Awake`：

```csharp
Config.Bind("General", "ToggleKey", new KeyboardShortcut(KeyCode.F8), "打开/关闭行动轴");
Config.Bind("General", "DataDir", "BazaarActionAxis", "数据目录（cards.json 与 icons）");
var go = new GameObject("BazaarActionAxisOverlay");
DontDestroyOnLoad(go);
go.AddComponent<ActionAxisOverlay>();
```

`ActionAxisOverlay.Update()`：

- `_config.ToggleKey.Value.IsDown()` → `TogglePanel()`。
- `TogglePanel()`：若面板未打开 → `RefreshAndOpen()`；已打开 → `Close()`。
- `RefreshAndOpen()`：
  1. `_catalog ??= CardCatalog.Load(Path.Combine(dataDir, "cards.json"))`。
  2. `_reader.Read()`。
  3. 如果 `!Ok` → 面板打开并显示错误。
  4. 映射每张卡；未匹配的卡用占位 `CardDef`（名称「未知卡 xxxxxxxx」，cooldown 0）。
  5. 应用实时覆盖。
  6. `Engine.Simulate`。
  7. `Aggregator.Build`。
  8. `Panel.SetData(...)`，`playhead=0`。
- `Launcher`：在屏幕右侧画按钮；点击打开/关闭；按钮位置 `Screen.width - 110*scale, Screen.height * 0.42`。
- 异常一律 `catch` 并在状态行显示，不让异常冒到 Unity。

- [ ] **Step 4: 运行测试确认通过**

Run: `bash tools/actionaxis_plugin/compile_tests.sh`
Expected: `OK`。

---

### Task 10: 构建/部署脚本与文档

**Files:**
- Modify: `tools/build_actionaxis_plugin.sh`（Task 1 已建雏形则扩展）
- Modify: `README.md`（新增「游戏内行动轴插件」一节）

**Interfaces:**
- Consumes: 全部前序任务。
- Produces: `bash tools/build_actionaxis_plugin.sh [--test|--compile-only|--deploy]`。

- [ ] **Step 1: 写脚本失败用例**

先手动验证脚本当前不支持 `--test`：

Run: `bash tools/build_actionaxis_plugin.sh --test`
Expected: 报错「unknown option」或不存在。

- [ ] **Step 2: 实现脚本**

- 参数：默认编译+部署；`--test` 只编译引擎+测试并运行；`--compile-only` 只编译 DLL。
- 编译命令：

```bash
"$CSC" -nologo -target:library -out:"$BUILD/ActionAxisEngine.dll" \
  -r:System.dll -r:System.Core.dll "$HERE"/engine/*.cs

"$CSC" -nologo -target:library -out:"$BUILD/ActionAxisPlugin.dll" \
  -r:System.dll -r:System.Core.dll \
  -r:"$BUILD/ActionAxisEngine.dll" \
  -r:"$GAME/BepInEx/core/BepInEx.dll" \
  -r:"$GAME/TheBazaar_Data/Managed/UnityEngine.dll" \
  -r:"$GAME/TheBazaar_Data/Managed/UnityEngine.CoreModule.dll" \
  -r:"$GAME/TheBazaar_Data/Managed/UnityEngine.IMGUIModule.dll" \
  -r:"$GAME/TheBazaar_Data/Managed/UnityEngine.TextRenderingModule.dll" \
  -r:"$GAME/TheBazaar_Data/Managed/UnityEngine.ImageConversionModule.dll" \
  -r:"$GAME/TheBazaar_Data/Managed/netstandard.dll" \
  "$HERE"/plugin/*.cs "$HERE"/plugin/Ui/*.cs
```

- 部署：

```bash
mkdir -p "$GAME/BepInEx/plugins/BazaarActionAxis/icons"
cp "$BUILD/ActionAxisEngine.dll" "$BUILD/ActionAxisPlugin.dll" "$GAME/BepInEx/plugins/"
cp "$ROOT/src/data/cards.json" "$GAME/BepInEx/plugins/BazaarActionAxis/"
if [ -d "$ROOT/captures/card_icons" ]; then cp -f "$ROOT"/captures/card_icons/*.png "$GAME/BepInEx/plugins/BazaarActionAxis/icons/"; else echo "warning: 图标未生成，运行 python tools/build_card_icons.py"; fi
```

- 缺少 csc / BepInEx / 游戏目录时打印明确错误并以非 0 退出。
- `--test` 不需要游戏目录。

- [ ] **Step 3: 运行测试**

Run: `bash tools/build_actionaxis_plugin.sh --test`
Expected: 编译引擎+测试并运行，全部 `PASS`。

- [ ] **Step 4: 编译并部署**

Run: `bash tools/build_actionaxis_plugin.sh`
Expected: 打印部署路径；`<GameDir>/BepInEx/plugins/ActionAxisPlugin.dll` 与 `ActionAxisEngine.dll` 存在。

- [ ] **Step 5: 更新 README**

新增一节，包含：功能、热键、构建步骤（图标→编译部署）、数据目录、限制（附魔、假人、输入穿透）、日志排查。链接设计文档与计划。

- [ ] **Step 6: 回归**

Run: `npm test -- --run`
Expected: 全绿。

---

### Task 11: 游戏内冒烟验证

**Files:**
- 无代码改动（发现 bug 则回到对应任务）。

- [ ] **Step 1: 启动游戏**

通过 Steam 启动 The Bazaar（不要直接启动 exe），进入一局有棋盘的 run。

- [ ] **Step 2: 打开面板**

点击屏幕右侧「行动轴」按钮，或按 F8。
Expected: 面板打开，显示当前棋盘卡牌；状态行显示已载入数量；播放头在 0s，所有角标为 0。

- [ ] **Step 3: 拖动时间轴**

拖到 10s、30s、60s。
Expected: 角标递增；触发次数、伤害、加速/冻结秒数与预期方向一致；时间标签同步。

- [ ] **Step 4: 刷新与关闭**

点击「刷新」，Expected: 重新读取棋盘并重置播放头。
点击「×」或按 Esc，Expected: 面板关闭；再次按 F8 可打开。

- [ ] **Step 5: 错误路径**

- 未进入 run 时点击：Expected: 可读错误，不崩溃。
- 临时把 BPP DLL 改名后重启（可选，高风险）：Expected: 提示 BPP 未加载。验证后立即改回。

- [ ] **Step 6: 检查日志**

Run: `tail -50 "<GameDir>/BepInEx/LogOutput.log"`
Expected: 出现 `[ActionAxis]` 初始化与模拟日志；无未捕获异常。

- [ ] **Step 7: 记录结果**

在 `docs/superpowers/plans/2026-10-23-action-axis-plugin.md` 末尾追加「验证记录」，写明日期、游戏版本、BPP 版本、通过项与遗留问题。

---

## Self-Review

**Spec coverage:**

- 独立插件、不改 BPP → Task 9–10。
- 引擎移植完整功能 → Task 4，覆盖 effects/triggers/battle。
- 卡库映射、实时属性覆盖 → Task 3。
- 卡图管线 → Task 7。
- 可拖动时间轴、角标 → Task 5、8。
- 统计口径 → Task 5。
- 正确性 parity → Task 4、5。
- 构建/部署/README → Task 10。
- 游戏内验证 → Task 11。

**Step scan:** 每个任务都有失败测试/验证、实现、通过验证；无 TBD；无空泛步骤。

**Type consistency:** `CardDef.WithLiveOverrides`、`CardStatsSeries.At`、`PanelModel.BadgeLines`、`SnapshotExtractor.FromSnapshot`、`PathConfig.ResolveDataDir` 在前后任务中命名一致。

**Review Focus mapping:**

- 时间边界 → `AggregateTests` 的 0.1/1.0/2.0 断言。
- 重复卡 → parity `duplicate-cards` + 聚合测试按 itemId。
- 未知 templateId / 缺失品阶 → `CatalogTests`。
- BPP 未加载 / 空棋盘 → `ReflectionTests` + Task 11。
- 缺失图标 / 长中文名 / 非 ASCII → `IconCacheTests`、`PanelModelTests`、Task 11。
- 弹药 / 多重 / 冻结边界 → parity `multicast-ammo`、`reload`、`freeze`。

## 实现记录（2026-10-23）

已完成：

- C# 引擎移植（`tools/actionaxis_plugin/engine/*.cs`），17 个 parity 场景全部通过。
- 聚合、面板模型、映射、反射、路径、卡图管线测试通过。
- `ActionAxisPlugin.dll` / `ActionAxisEngine.dll` 编译并部署到游戏插件目录，`cards.json` 已复制。
- 网页版测试 102/102 通过。

实现偏差：

- 引擎入口类名 `Engine` 改为 `BattleEngine`，避免与命名空间 `BazaarActionAxis.Engine` 冲突。
- 公共 API 使用 `List`/`IList`，替代计划中的 `IReadOnlyList`（.NET 4.0 / C# 5 兼容）。
- 卡图 CDN `howbazaar-images.b-cdn.net` 返回 403，图标改用「品阶色块 + 名称首字」占位；`build_card_icons.py` 保留。
- `Texture2D.LoadImage` 改为反射调用：其 netstandard 签名含 `ReadOnlySpan`，csc 4.0 无法绑定。
- `JsMath.Round(2.5)` 的期望值修正为 3（JS `Math.round` 语义）。
- `CardDef.WithLiveOverrides` 第三个参数为 `multicastBonus`，`CardMapper` 按 `Multicast - 1` 传入；实时多重替换静态多重（`HasMulticastOverride`），避免同一张卡被叠加计算。
- 实时 `CooldownMax` 属性存在时即覆盖（含 0），避免已失效/被动卡仍按静态冷却触发。
- 项目不是 git 仓库，未执行 commit。

待办：

- 游戏内手动冒烟（Task 11）。
- 找到可用图源后生成真实卡图。
