# 《大巴扎》行动轴战斗模拟器 — 设计规格

- 日期：2026-10-22
- 状态：已确认，待实现计划
- 项目目录：`D:\bazaar-action-axis`

## 1. 目标

做一个本地运行的网页工具，用来推演《大巴扎》(The Bazaar) 的物品组合：

1. 用户可以把双方卡牌排到左右两侧的卡组槽位里。
2. 点击“模拟”后，引擎按冷却轴推进战斗，产出一整条时间轴。
3. 拖动时间轴播放头，可以看到任意时刻双方的血量、护盾、持续伤害和卡牌冷却状态。
4. 看到整场及各时段的总伤害、总治疗、总护盾。
5. 点击任意卡牌或触发点，展开该次触发的详细结算链（来源、目标、原始值、护盾吸收、实际掉血、触发了谁）。
6. 支持导入图鉴物品，也支持完全自定义卡牌。

### 1.1 数据源

1. **游戏原始卡牌库** `vendor/GameData/GameData.db`（从游戏 `StreamingAssets/GameData.db.zip` 解压）：1146 件 `TCardItem`，含结构化能力、触发器、目标、数值属性和毫秒级冷却；其中 1041 件可按 GUID 与图鉴对上。BPP 仓库的裁剪版 `GameData.cards.db`（116 件）作为后备数据源。
2. **大巴扎图鉴** `data/items_raw.json`：1043 件，925 件有品阶数据，提供中英文名、图片、品阶和完整 tooltip 文本。
3. **合并策略**：游戏库优先；图鉴补名称、图片和剩余卡牌；tooltip 解析兜底。

### 非目标（v1 不做）

- 不做联机、排行、账号系统。
- 不追求 1:1 复刻游戏画面。
- 不保证全部 1043 件物品的条件触发都能自动解析；解析不了的以文本形式保留，可手动补。
- 不做技能、英雄被动、附魔的完整模拟；附魔可作为后续扩展。
- 默认不批量下载图片；提供可选脚本，UI 优先本地图，失败回退 CDN。

## 2. 用户与典型场景

- **组合验证**：把 A 卡组和 B 卡组放上去，看 0–60 秒的伤害曲线，判断哪套先手、爆发点在几秒。
- **单卡调试**：自定义一张“每 4 秒造成 10 伤害”的卡，观察它与加速/减速卡的交互。
- **教学/复盘**：拖动时间轴，点开某次触发，解释为什么这一下只掉了 3 血（护盾吸收了 7）。

## 3. 规则基准 v1

所有数值都在 `src/engine/rules.ts` 中集中定义，可在 UI 的“规则设置”里覆盖。

| 机制 | 规则 |
|---|---|
| 时间步长 | `dt = 0.1s`，模拟上限 `60s` |
| 初始冷却 | 物品以基础冷却开始计时；`startReady=false` |
| 冷却推进 | 每 tick 减少 `dt × rate`，`rate` 默认 1 |
| 触发 | 剩余冷却 ≤ 0 时激活，激活后重置为基础冷却 |
| 同时触发排序 | 我方按槽位从左到右，然后对方从左到右 |
| 卡组槽位 | 每方最多 10 个（`maxSlots`） |
| Haste（加速） | 持续 N 秒，`rate × 2`；重复施加刷新持续时间，不叠乘 |
| Slow（减速） | 持续 N 秒，`rate × 0.5`；重复施加刷新持续时间，不叠乘 |
| Freeze（冻结） | 持续 N 秒，`rate = 0`；重复施加刷新持续时间 |
| Charge（充能） | 立即减少 N 秒剩余冷却，最低到 0 |
| 伤害 | 先扣护盾，溢出扣生命 |
| 暴击 | 概率触发，本次伤害/治疗/护盾 ×2 |
| 吸血 | 按实际造成的生命伤害回复；默认不按护盾伤害回复 |
| 灼烧 Burn | 每 1 秒造成等于层数的伤害，然后层数 −1 |
| 中毒 Poison | 每 1 秒造成等于层数的伤害，层数不衰减 |
| 再生 Regen | 每 1 秒回复等于层数的生命 |
| 护盾 | 吸收伤害，持续到战斗结束 |
| 多重施放 | 一次激活连续结算 N 次，每次都可触发 onUse |
| 弹药 | 有弹药的物品每次施放消耗 1 发；0 发时不再激活，等待装填 |
| 装填 | 为目标物品恢复 N 发弹药 |
| 目标默认 | 伤害→敌方英雄；治疗/护盾→己方英雄；加速/充能→己方物品；减速/冻结→敌方物品 |
| 结束 | 任一方生命 ≤ 0 立即结束；60 秒到时按剩余生命百分比判定，相同为平局 |
| Sandstorm（沙尘暴） | 可选，默认关闭；配置 `start/base/ramp`，公式未从仓库确认 |
| 随机 | 用种子 PRNG（mulberry32），同种子同配置结果可复现 |

### 3.1 触发事件

v1 支持的事件类型：

`onUse`、`onHeal`、`onShield`、`onDamageDealt`、`onDamageTaken`、`onBurnApplied`、`onPoisonApplied`、`onHaste`、`onSlow`、`onFreeze`、`onCrit`、`onReload`、`onKill`、`atCombatStart`、`everySecond`。

- 触发以 FIFO 队列处理，支持链式触发。
- 单 tick 事件上限 500；触发链深度上限 20；超限时记录 `warning` 事件并停止该链。
- 同一 tick 内由 Charge/Haste 新变为可用的物品，**下一 tick** 才激活，避免同 tick 无限连锁。

## 4. 技术架构

### 4.1 技术栈

- Vite + React 18 + TypeScript
- 引擎为纯 TS 模块，不依赖 React，可在 Node/Vitest 中直接测试
- 离线构建脚本用 `tsx` 直接运行 TypeScript，与引擎共用解析器和类型
- 样式用原生 CSS（CSS Modules 可选），不引入重型 UI 库
- 时间轴用 SVG + HTML 混合渲染
- 持久化用 localStorage（自定义卡、卡组、规则覆盖）
- 测试用 Vitest + @testing-library/react（仅组件测试）

### 4.2 目录

```
D:\bazaar-action-axis\
├─ data/
│  ├─ items_raw.json               # 已提取的图鉴原始数据
│  └─ game_cards_structured.json   # 游戏 DB 转换产物
├─ tools/
│  ├─ extract_items.py             # 已存在：从 Next.js chunk 提取
│  ├─ import_game_db.py            # 游戏 DB → 结构化 JSON
│  ├─ build_cards.ts               # 解析 + 合并 + 生成 cards.json（tsx 运行）
│  └─ download_images.py           # 可选：下载卡图
├─ src/
│  ├─ engine/
│  │  ├─ types.ts
│  │  ├─ rules.ts
│  │  ├─ rng.ts
│  │  ├─ battle.ts
│  │  ├─ effects.ts
│  │  ├─ triggers.ts
│  │  ├─ parse.ts
│  │  └─ __tests__/
│  ├─ data/
│  │  ├─ cards.json                # 构建产物
│  │  └─ parse_report.json         # 解析覆盖率报告
│  ├─ components/
│  │  ├─ Timeline.tsx
│  │  ├─ CardLane.tsx
│  │  ├─ CardChip.tsx
│  │  ├─ LoadoutEditor.tsx
│  │  ├─ CardEditor.tsx
│  │  ├─ DetailPanel.tsx
│  │  ├─ TotalsPanel.tsx
│  │  ├─ RulesPanel.tsx
│  │  └─ ImportDialog.tsx
│  ├─ state/
│  │  ├─ storage.ts
│  │  └─ simulatorStore.ts
│  ├─ App.tsx
│  ├─ main.tsx
│  └─ styles.css
├─ public/
│  └─ images/                      # 可选下载的卡图
├─ docs/superpowers/specs/
├─ package.json
├─ tsconfig.json
├─ vite.config.ts
└─ README.md
```

## 5. 数据模型

### 5.1 数值表达式

```ts
type NumExpr =
  | { type: 'const'; value: number }
  | { type: 'percentMaxHp'; value: number }   // 20 表示 20%
  | { type: 'value' }                          // 物品 Value，v1 默认 0
  | { type: 'regen' }
  | { type: 'income' }
  | { type: 'perStack'; stack: 'burn' | 'poison'; per: number }
  | { type: 'perTag'; tag: string; per: number };
```

v1 实现 `const`、`percentMaxHp`、`value`、`regen`。其余解析为 `partial`，由用户手动填写固定值。

### 5.2 目标

```ts
type Target =
  | { kind: 'selfHero' }
  | { kind: 'enemyHero' }
  | { kind: 'selfItem'; itemId: string }
  | { kind: 'enemyItem'; itemId: string }
  | { kind: 'randomEnemyItem' }
  | { kind: 'randomSelfItem' }
  | { kind: 'allEnemyItems' }
  | { kind: 'allSelfItems' }
  | { kind: 'adjacentSelf' }
  | { kind: 'leftSelf' }
  | { kind: 'rightSelf' }
  | { kind: 'lowestHpEnemyItem' };
```

### 5.3 效果

```ts
type Effect =
  | { kind: 'damage'; amount: NumExpr; target: Target; crit?: boolean }
  | { kind: 'heal'; amount: NumExpr; target: Target; crit?: boolean }
  | { kind: 'shield'; amount: NumExpr; target: Target; crit?: boolean }
  | { kind: 'burn'; amount: NumExpr; target: Target }
  | { kind: 'poison'; amount: NumExpr; target: Target }
  | { kind: 'regen'; amount: NumExpr; target: Target }
  | { kind: 'haste'; seconds: number; target: Target; count?: number }
  | { kind: 'slow'; seconds: number; target: Target; count?: number }
  | { kind: 'freeze'; seconds: number; target: Target; count?: number }
  | { kind: 'charge'; seconds: number; target: Target; count?: number }
  | { kind: 'multicast'; count: number }
  | { kind: 'crit'; chance: number }
  | { kind: 'lifesteal'; percent: number }
  | { kind: 'reload'; amount: number; target: Target; count?: number }
  | { kind: 'ammo'; amount: number };
```

### 5.4 触发条件

```ts
type TriggerEvent =
  | 'onUse' | 'onHeal' | 'onShield' | 'onDamageDealt' | 'onDamageTaken'
  | 'onBurnApplied' | 'onPoisonApplied' | 'onRegen' | 'onHaste' | 'onSlow' | 'onFreeze'
  | 'onCrit' | 'onReload' | 'onKill' | 'atCombatStart' | 'everySecond';

interface TriggerDef {
  event: TriggerEvent;
  filter?: {
    sourceTag?: string;      // 例如 'Weapon'、'Potion'
    sourceItemId?: string;
    targetSide?: 'self' | 'enemy';
  };
  everySeconds?: number;     // everySecond 用
  effects: Effect[];
}
```

### 5.5 卡牌

```ts
interface CardDef {
  id: string;                    // 图鉴卡: `${itemId}@${tier}`；自定义: `custom-${uuid}`
  baseItemId?: string;           // 图鉴来源
  name: string;
  nameCn?: string;
  image?: string;                // CDN 或 /images/xxx
  size: 'Small' | 'Medium' | 'Large';
  tier: 'Bronze' | 'Silver' | 'Gold' | 'Diamond' | 'Legendary';
  cooldown: number;
  effects: Effect[];
  triggers: TriggerDef[];
  tags: string[];
  ammoMax?: number;
  parseStatus: 'structured' | 'partial' | 'text';
  rawTooltips: string[];
}
```

### 5.6 战斗配置与状态

```ts
interface BattleConfig {
  player: Loadout;
  enemy: Loadout;
  playerMaxHp: number;
  enemyMaxHp: number;
  seed: number;
  rules: RuleOverrides;
  maxTime: number;
}

interface Loadout {
  items: { cardId: string; slot: number }[];
}

interface ItemState {
  card: CardDef;
  slot: number;
  cooldown: number;
  hasteUntil: number;
  slowUntil: number;
  frozenUntil: number;
  multicastBonus: number;
  critBonus: number;
  damageBonus: number;
  ammo: number;
  disabled: boolean;
}

interface HeroState {
  hp: number;
  maxHp: number;
  shield: number;
  burn: number;
  poison: number;
  regen: number;
}

interface BattleEvent {
  id: number;
  time: number;
  side: 'player' | 'enemy';
  sourceItemId?: string;
  kind:
    | 'activate' | 'damage' | 'heal' | 'shield' | 'burn' | 'poison'
    | 'regen' | 'haste' | 'slow' | 'freeze' | 'charge' | 'crit'
    | 'reload' | 'ammo' | 'death' | 'warning' | 'dotTick';
  targetItemId?: string;
  value?: number;
  absorbed?: number;
  hpAfter?: number;
  shieldAfter?: number;
  chainId: number;
  depth: number;
  note?: string;
}

interface ItemSnapshot {
  itemId: string;
  slot: number;
  cooldown: number;
  cooldownMax: number;
  hasteUntil: number;
  slowUntil: number;
  frozenUntil: number;
  ammo: number;
}

interface Snapshot {
  time: number;
  player: HeroState & { items: ItemSnapshot[] };
  enemy: HeroState & { items: ItemSnapshot[] };
}

interface RuleOverrides {
  dt?: number;                       // 默认 0.1
  maxTime?: number;                  // 默认 60
  hasteMultiplier?: number;          // 默认 2
  slowMultiplier?: number;           // 默认 0.5
  startReady?: boolean;              // 默认 false
  burnDecay?: number;                // 默认 1
  poisonDecay?: number;              // 默认 0
  dotBypassesShield?: boolean;       // 默认 false
  critMultiplier?: number;           // 默认 2
  lifestealOnShieldDamage?: boolean; // 默认 false
  activationOrder?: 'player-first-left-to-right'; // v1 唯一实现
  maxEventsPerTick?: number;         // 默认 500
  maxTriggerDepth?: number;          // 默认 20
  maxSlots?: number;                 // 默认 10
  sandstorm?: { start: number; base: number; ramp: number } | null; // 默认 null
}

interface BattleResult {
  events: BattleEvent[];
  snapshots: Snapshot[];
  totals: {
    player: { damage: number; effectiveDamage: number; heal: number; shield: number };
    enemy: { damage: number; effectiveDamage: number; heal: number; shield: number };
  };
  winner: 'player' | 'enemy' | 'draw';
  endTime: number;
  warnings: string[];
}
```

## 6. 引擎算法

### 6.1 主循环

```
simulate(config):
  state = init(config)
  events = []
  snapshots = []
  for t = 0; t <= maxTime; t += dt:
    expireStatuses(state, t)
    advanceCooldowns(state, dt)
    if crossedSecondBoundary(t):
      tickDots(state, t, events)
    ready = collectReady(state)          # 固定本 tick 的列表
    sort(ready, side='player' first, then slot asc)
    for item in ready:
      if battleOver(state): break
      activate(state, item, t, events)
    checkDeath(state, t, events)
    snapshots.push(snapshot(state, t))
    if battleOver(state): break
  return result
```

### 6.2 激活

```
activate(state, item, t, events):
  if item.frozen: return
  if item.ammoMax != null and item.ammo <= 0: return
  casts = 1 + item.multicastBonus
  for i in 0..casts-1:
    if battleOver: break
    emit('activate', item, t)
    for effect in item.card.effects:
      resolveEffect(effect, item, t)
    if item.ammoMax != null: item.ammo -= 1
    emit('onUse', item, t)
    drainTriggerQueue(t)
  item.cooldown = item.card.cooldown
```

### 6.3 效果结算

- `damage`：计算数值 → 暴击判定 → 扣护盾 → 扣生命 → 记录 `absorbed` 与实际伤害 → 触发 `onDamageDealt` / `onDamageTaken`。
- `heal`：恢复生命，不超过最大生命 → 触发 `onHeal`。
- `shield`：增加护盾 → 触发 `onShield`。
- `burn`/`poison`：增加层数 → 触发对应事件。
- `haste`/`slow`/`freeze`：设置 `*Until = t + seconds`，取较大值（刷新不叠乘）→ 触发对应事件。
- `charge`：`cooldown = max(0, cooldown - seconds)`。
- `multicast`：只修改当前物品的 `multicastBonus`。
- `crit`：只修改当前物品的 `critBonus`。
- `reload`：目标物品 `ammo += amount`，不超过 `ammoMax` → 触发 `onReload`。
- `ammo`：增加 `ammoMax`。

### 6.4 结算顺序与回归保护

- 同一 tick 的 ready 列表在 tick 开始时固定；激活过程中新产生的 ready 物品下一 tick 再处理。
- 触发队列按 FIFO，单个事件的所有效果结算完再处理下一个。
- 深度 > 20 或单 tick 事件 > 500 时，记录 `warning` 并清空当前队列。

## 7. 时间轴 UI

### 7.1 布局

```
┌──────────────────────────────────────────────────────────────┐
│ 顶部：卡组A vs 卡组B | 模拟按钮 | 种子 | 规则设置 | 导入/导出 │
├──────────────────────────────────────────────────────────────┤
│ 总量面板：伤害 / 有效伤害 / 治疗 / 护盾 / DPS（双方）        │
├──────────────────────────────────────────────────────────────┤
│ 时间轴（SVG）：0────────────●────────────────────60s          │
│  我方英雄  ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓             │
│  我方物品1 ▒▒▒▒●▒▒▒▒▒▒●▒▒▒▒▒▒●▒▒▒▒▒▒●▒▒▒▒▒▒                 │
│  我方物品2 ▒▒▒▒▒▒▒▒●▒▒▒▒▒▒▒▒●▒▒▒▒▒▒▒▒●▒▒▒▒                 │
│  敌方英雄  ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓             │
│  敌方物品1 ...                                                │
├──────────────────────────────────────────────────────────────┤
│ 详情面板：选中触发的完整结算链                                │
└──────────────────────────────────────────────────────────────┘
```

### 7.2 交互

- **拖动播放头**：点击/拖动时间轴任意位置，播放头跟随；右侧状态取 `snapshots` 中 `time <= playhead` 的最后一帧。
- **播放/暂停/倍速**：`requestAnimationFrame` 推进，倍速 0.5×/1×/2×/4×。
- **点击触发点**：选中该 `BattleEvent`，详情面板按 `chainId` 展示整条链。
- **点击卡牌**：筛选该卡的所有事件，按时间列出。
- **悬停**：显示浮层摘要（时间、来源、目标、数值、剩余血量/护盾）。
- **缩放**：默认 0–60s 全览；可拖选区间放大到 10s 窗口。

### 7.3 总量面板

- 总伤害：所有 `damage` 事件的原始值之和。
- 有效伤害：实际扣生命的部分（不含护盾吸收）。
- 总治疗、总护盾：对应事件之和。
- DPS：有效伤害 / 结束时间。
- 双方各一行；可展开为“按卡牌”明细。

## 8. 卡牌编辑器与导入

### 8.1 编辑器

- 字段：名称（中/英）、尺寸、品阶、基础冷却、弹药上限、标签、图片 URL。
- 效果行：类型下拉 → 数值（固定值/百分比最大生命/Value/Regen）→ 目标 → 持续秒数/次数/概率。
- 触发行：事件类型 → 过滤条件（来源标签/来源卡/目标侧）→ 效果列表。
- 实时校验：冷却 ≥ 0.1；数值 ≥ 0；百分比 0–100；至少一个效果或触发。
- 保存到 `localStorage` 的 `bazaar.customCards.v1`。

### 8.2 图鉴导入

- 搜索框支持中英文名、英雄、尺寸、品阶、标签筛选。
- 卡片列表显示图片、名称、品阶、冷却、解析状态徽章。
- 点击“加入我方/敌方”，按槽位顺序放入。
- 同一物品不同品阶视为不同卡（`itemId@tier`）。

### 8.3 持久化

- `bazaar.customCards.v1`：自定义卡数组。
- `bazaar.loadouts.v1`：双方卡组。
- `bazaar.rules.v1`：规则覆盖。
- 导入/导出为单个 JSON 文件。

## 9. 数据管线

### 9.1 游戏原始库导入

`tools/import_game_db.py` 读取游戏完整 `GameData.db` 的 `cards` 表（`Id`, `Data`），只处理 `$type == "TCardItem"` 的 1146 条记录，输出 `data/game_cards_full.json`；也支持用 `--db`/`--out` 指向 BPP 裁剪库并输出 `data/game_cards_structured.json` 作为后备。

每个品阶从 `Tiers[tier].Attributes` 取值：

- `CooldownMax`（毫秒）→ `cooldown = CooldownMax / 1000`
- `Multicast` → 施放次数；引擎里 `multicastBonus = Multicast - 1`
- 动作数值属性按下表映射（毫秒字段同样除以 1000）。

| 动作 `$type` | 数值属性 | 目标属性 | 我们的效果 |
|---|---|---|---|
| `TActionPlayerDamage` | `DamageAmount` | — | `damage` |
| `TActionPlayerHeal` | `HealAmount` | — | `heal` |
| `TActionPlayerShieldApply` | `ShieldApplyAmount` | — | `shield` |
| `TActionPlayerBurnApply` | `BurnApplyAmount` | — | `burn` |
| `TActionPlayerPoisonApply` | `PoisonApplyAmount` | — | `poison` |
| `TActionPlayerRegenApply` | `RegenApplyAmount` | — | `regen` |
| `TActionCardHaste` | `HasteAmount`（ms） | `HasteTargets` | `haste` |
| `TActionCardSlow` | `SlowAmount`（ms） | `SlowTargets` | `slow` |
| `TActionCardFreeze` | `FreezeAmount`（ms） | — | `freeze` |
| `TActionCardCharge` | `ChargeAmount`（ms） | `ChargeTargets` | `charge` |
| `TActionCardReload` | `ReloadAmount` | `ReloadTargets` | `reload` |
| `TActionAnd` | 子动作 | — | 逐个展开 |
| `TActionCardModifyAttribute` / `TActionPlayerModifyAttribute` | 复杂 | — | v1 标记 `partial`，保留原始 JSON |

触发器映射：

- `TTriggerOnCardFired` → `onUse`
- `TTriggerOnFightStarted` → `atCombatStart`
- `TTriggerOnCardPerformedHaste` → `onHaste`
- `TTriggerOnCardPerformedSlow` → `onSlow`
- `TTriggerOnCardCritted` → `onCrit`
- `TTriggerOnCardPerformedHeal` → `onHeal`
- `TTriggerOnCardPerformedBurn` → `onBurnApplied`
- `TTriggerOnCardPerformedPoison` → `onPoisonApplied`
- `TTriggerOnCardPerformedRegen` → `onRegen`
- `TTriggerOr` → 展开为多个触发器
- 其余（`OnItemUsed`、`OnDayStarted`、`OnPlayerAttributeChanged` 等）→ `partial`，保留原始 JSON

目标映射：

- `TTargetPlayerRelative`：`TargetMode=Self` → `selfHero`，`Opponent` → `enemyHero`
- `TTargetCardSelf` → `selfItem`
- `TTargetCardRandom`：`TargetSection=SelfHand` → `randomSelfItem`，`OpponentHand` → `randomEnemyItem`
- `TTargetCardSection`：`SelfHand/SelfBoard` → `allSelfItems`，`OpponentHand/OpponentBoard` → `allEnemyItems`
- `TTargetCardPositional` → `adjacentSelf` / `leftSelf` / `rightSelf`
- 其余目标类型 → `partial`

### 9.2 图鉴解析分级

`tools/build_cards.ts`（用 `npx tsx` 运行）对每个物品的每个品阶执行：

1. **L1 冷却**：匹配 `^Cooldown ([\d.]+) seconds` → `cooldown`。
2. **L2 主效果**：匹配以下模式并生成 `Effect`：
   - `Deal (\d+) Damage` / `Deal Damage equal to (\d+)% of ... Max Health`
   - `Heal (\d+)` / `Heal equal to ...`
   - `Shield (\d+)` / `Shield equal to ...`
   - `Burn (\d+)` / `Poison (\d+)`
   - `Haste (\d+) item(s) for ([\d.]+) second(s)`
   - `Slow (\d+) item(s) for ([\d.]+) second(s)`
   - `Freeze ... for ([\d.]+) second(s)`
   - `Charge ... ([\d.]+) second(s)`
   - `Multicast (\d+)`
   - `Crit Chance ...%`
   - `Ammo (\d+)` / `Reload ...`
3. **L3 简单触发**：匹配 `When you ...` / `When this ...` / `At the start of combat` / `Every N seconds`，生成 `TriggerDef`。只支持能识别的事件动词；识别不了的整条保留为文本。
4. **L4 文本**：其余全部写入 `rawTooltips`，`parseStatus='text'`，不影响导入。

### 9.3 合并顺序

1. 以 `items_raw.json` 的 `itemId@tier` 为全集。
2. 优先读 `data/game_cards_full.json`（完整库），否则回退 `data/game_cards_structured.json`。命中同 GUID + 同品阶时，使用游戏库的 `cooldown/effects/triggers/ammoMax`。
3. 若游戏库为 `partial` 且图鉴解析为 `structured`，合并图鉴效果/触发，`parseStatus='structured'`，`dataSource='merged'`；游戏库本身结构化时 `dataSource='game-db'`。
4. 未命中游戏库的图鉴卡走 L1/L2/L3，`dataSource='tooltip'`。
5. 游戏库中未匹配图鉴的条目作为额外卡追加，并按 GUID 回查图鉴补 `nameCn`/图片。
6. 输出 `cards.json` 与 `parse_report.json`，按 `dataSource` 分组统计。

### 9.4 输出

- `src/data/cards.json`：每个 `itemId@tier` 一条 `CardDef`。
- `src/data/parse_report.json`：总数、structured/partial/text 数量、按 `dataSource`、英雄和效果的覆盖统计。
- 控制台打印覆盖率，便于后续迭代。

### 9.5 图片

- `tools/download_images.py`：读取 `items_raw.json` 的 `image` 字段，并发 8 下载到 `public/images/{itemId}.avif`，带重试和跳过已存在。
- UI 优先 `/images/{itemId}.avif`，`onError` 回退到 CDN URL，再失败显示占位图。

## 10. 错误处理

- **数据解析失败**：保留原始对象和 `rawTooltips`，标记 `text`，不静默丢弃。
- **未知效果文本**：保留在 `rawTooltips`，UI 显示“需手动配置”。
- **引擎异常**：捕获后返回 `warning` 事件和部分结果，不白屏。
- **无限触发**：深度/事件数上限；超限记录警告。
- **图片加载失败**：本地 → CDN → 占位图三级回退。
- **localStorage 损坏**：`JSON.parse` 失败时备份原值到 `*.bak`，回退到空数据并提示。
- **空卡组**：允许模拟，直接按生命值判定或提示“请至少放入一张卡”。

## 11. 测试策略

### 11.1 引擎单测（Vitest）

- 冷却：4 秒冷却在第 4.0/8.0/12.0 秒触发。
- Haste：持续 2 秒、速率 ×2 时，4 秒冷却在 2.0 秒触发。
- Slow：持续 2 秒、速率 ×0.5 时，4 秒冷却在 5.0 秒触发。
- Freeze：冻结 2 秒，触发时间后移 2 秒。
- Charge：立即减少剩余冷却。
- 护盾：10 伤害打 6 护盾，absorbed=6，hp 掉 4。
- Burn：3 层每 tick 3 伤害，随后变 2 层。
- Poison：3 层持续每 tick 3 伤害，不衰减。
- Multicast：2 次施放产生两组效果。
- 弹药：0 发时不激活，装填后恢复。
- 死亡：HP 归零立即结束，后续事件不产生。
- 确定性：同种子两次模拟事件序列完全一致。

### 11.2 游戏库转换测试（pytest 或 unittest）

- `Narwhal`：`CooldownMax=3000` → `cooldown=3`，`DamageAmount=5` → damage 5，目标敌方英雄。
- `Hunting Hawk`：`HasteAmount=2000` → haste 2 秒，`HasteTargets=1`。
- `Gavel`：`FreezeAmount=1000` → freeze 1 秒，目标为全部武器。
- `Multicast=2` 的卡 → `multicastBonus=1`。
- 116 件全部生成；未匹配的 30 件仍带 `guid` 和 `dataSource='game-db'`。

### 11.3 解析器单测

- 用 `data/items_raw.json` 中真实 tooltip 作为表驱动样本。
- 断言 L1/L2 的字段值；L4 样本断言 `parseStatus='text'` 且 `rawTooltips` 非空。

### 11.4 黄金用例

- 两张自定义卡：A 每 4 秒 10 伤害，B 每 6 秒 5 护盾，跑 20 秒，断言双方总量与结束时间。
- 一套真实图鉴组合（例如 Vanessa 的 Ambergris + Anchor），断言不崩溃、事件数 > 0、总量单调。

### 11.5 UI 验证

- `npm run dev` 手动走查：导入 → 编排 → 模拟 → 拖动 → 点击详情。
- 组件测试覆盖 `Timeline` 的播放头拖动和 `CardEditor` 的校验。

## 12. 里程碑

| 阶段 | 交付物 | 验收 |
|---|---|---|
| M1 数据与引擎 | `build_cards.py`、`cards.json`、`engine/*`、单测 | `npm test` 全绿；解析覆盖率报告生成 |
| M2 时间轴 | `Timeline`、`TotalsPanel`、`DetailPanel`、播放控制 | 能模拟两套卡组、拖动、点开详情 |
| M3 编辑器 | `CardEditor`、`ImportDialog`、localStorage | 能自定义卡、导入图鉴、保存/导出 |
| M4 打磨 | L3 触发覆盖、规则面板、图片脚本、README | 覆盖率提升；规则可改；文档完整 |

## 13. 风险与假设

- **规则细节**：Haste ×2、Slow ×0.5、Burn 每 tick 衰减 1 层属于社区常见理解，但未从官方文档逐条确认。已全部放进 `rules.ts`，可改。
- **条件触发**：自然语言解析不可能 100% 准确；v1 以 L2 为主，L3 逐步覆盖，L4 手动补。
- **Value/Income/Regen 等动态数值**：v1 只实现固定值与百分比最大生命；其余需要用户填固定值。
- **图片版权**：卡图来自图鉴 CDN，仅本地个人使用；对外分发需自行确认授权。
- **社区资料不可达**：Reddit/Fandom 在当前网络下超时，规则以图鉴效果文本 + 游戏原始库 + 战斗语料为准。
- **游戏原始库只有 116 件**，且 `ModifyAttribute`、`SpawnCards`、`Flying` 等复杂动作 v1 只标记 `partial`。
- **Sandstorm 公式缺失**：语料确认其存在，但仓库没有具体数值；v1 默认关闭，只保留配置位。
- **GUID 匹配**：86 件能按 GUID 对上图鉴；未匹配的 30 件按游戏库名称导入，中文名和图片为空。

## 14. 验收标准

1. `npm install && npm run dev` 能在本机打开模拟器。
2. 卡牌库中至少 80 件 `dataSource='game-db'`，且 `structured` 卡牌总数 ≥ 500（完整库并入后当前 755）；能全部导入双方卡组。
3. 点击“模拟”后 1 秒内返回 60 秒时间轴（普通配置）。
4. 拖动播放头，任意时刻的血量/护盾/冷却状态正确更新。
5. 点击任意卡牌，能看到该卡的触发次数、每次时间与数值。
6. 总量面板的总伤害/治疗/护盾与事件日志求和一致。
7. `npm test` 覆盖第 11.1 节全部用例并通过。
8. 自定义一张卡并保存，刷新页面后仍在。
