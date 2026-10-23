# 《大巴扎》行动轴模拟器 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 构建一个本地 Web 模拟器：双方卡组按冷却轴推进战斗，可拖动时间轴查看任意时刻状态、总伤害/治疗/护盾，点击卡牌查看触发结算链。

**Architecture:** React 18 + Vite + TypeScript。核心是一个不依赖 React 的纯 TS 引擎（`src/engine/`），输入 `CardDef` 与双方卡组，输出事件日志、逐 tick 快照和总量。数据由两个来源合并：游戏原始 SQLite（116 件结构化物品，86 件可按 GUID 对上）优先，图鉴 tooltip 解析兜底。UI 只消费引擎输出，不做战斗计算。

**Tech Stack:** React 18.3、TypeScript 5.6、Vite 5.4、Vitest 2.1、@testing-library/react、tsx、Python 3.12（仅离线导入/下载脚本）。

**Spec:** `docs/superpowers/specs/2026-10-22-bazaar-action-axis-simulator-design.md`

## Global Constraints

- 引擎代码（`src/engine/**`）禁止 import React 或任何 DOM API。
- 所有规则数值来自 `DEFAULT_RULES`；引擎里不得出现散落的魔法数字。`dt=0.1`、`maxTime=60`、`maxSlots=10`、双方默认生命 100。
- 冷却单位统一为秒；游戏库的毫秒字段在导入时除以 1000。
- 卡牌 ID：图鉴/游戏库为 `${guid}@${tier}`，自定义卡为 `custom-${crypto.randomUUID()}`。
- 中文 UI 文案；代码标识符用英文。
- `vendor/` 是第三方参考仓库，仅供数据导入和规则核对；不参与应用构建，不从中执行任何脚本。若初始化 git，必须把 `vendor/` 加入 `.gitignore`。
- 项目当前不是 git 仓库；下文的“提交”步骤仅在用户明确要求 git 时执行。
- 测试命令统一用 `npm test -- --run <path>`；Python 用 `python -m unittest <path> -v`。

## Review Focus

- **空卡组 / 只有一方有卡**：模拟应返回有效结果，不崩溃；没有卡的一方只承受时间流逝或对方伤害。
- **同 tick 多张卡同时就绪**：排序必须确定（我方左→右，再对方左→右），不得重复激活。
- **触发自我循环**（A 触发 A）：必须被深度上限截断并产生 warning，不能卡死。
- **无法解析的 tooltip**：仍要生成卡牌，`parseStatus='text'`，`rawTooltips` 保留，UI 不崩。
- **localStorage 损坏或旧版本数据**：回退到空数据并把原值备份到 `.bak`，应用仍能启动。

---

### Task 1: 项目脚手架

**Files:**
- Create: `package.json`, `tsconfig.json`, `vite.config.ts`, `index.html`, `src/main.tsx`, `src/App.tsx`, `src/styles.css`, `src/engine/__tests__/smoke.test.tsx`

**Interfaces:**
- Consumes: 无
- Produces: `App` 默认导出；`npm run dev/build/test` 可用

- [ ] **Step 1: Write the failing smoke test**

```tsx
// src/engine/__tests__/smoke.test.tsx
import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import App from '../../App';

describe('App', () => {
  it('renders the simulator title', () => {
    render(<App />);
    expect(screen.getByText('大巴扎行动轴模拟器')).toBeTruthy();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test -- --run src/engine/__tests__/smoke.test.tsx`
Expected: FAIL（找不到 `../../App`）

- [ ] **Step 3: Create scaffold**

`package.json` 依赖：`react@^18.3.1`、`react-dom@^18.3.1`；devDependencies：`typescript@^5.6`、`vite@^5.4`、`@vitejs/plugin-react@^4.3`、`vitest@^2.1`、`jsdom@^25`、`@testing-library/react@^16`、`@testing-library/jest-dom@^6`、`tsx@^4.19`、`@types/react`、`@types/react-dom`。scripts：`dev: vite`、`build: tsc -b && vite build`、`test: vitest`、`cards: tsx tools/build_cards.ts`。

`vite.config.ts` 配置 React 插件与 `test: { environment: 'jsdom', globals: true }`。`tsconfig.json` 开 `strict`、`jsx: react-jsx`、`moduleResolution: bundler`。`App.tsx` 渲染 `<h1>大巴扎行动轴模拟器</h1>`。

- [ ] **Step 4: Install and run tests**

Run: `npm install && npm test -- --run src/engine/__tests__/smoke.test.tsx`
Expected: PASS

- [ ] **Step 5: Verify build**

Run: `npm run build`
Expected: 退出码 0

- [ ] **Step 6: Commit (optional, only if git initialized)**

```bash
git add package.json package-lock.json tsconfig.json vite.config.ts index.html src
git commit -m "chore: scaffold vite react typescript app"
```

---

### Task 2: 核心类型、规则表、RNG

**Files:**
- Create: `src/engine/types.ts`, `src/engine/rules.ts`, `src/engine/rng.ts`
- Test: `src/engine/__tests__/rules.test.ts`, `src/engine/__tests__/rng.test.ts`

**Interfaces:**
- Consumes: 无
- Produces:
  - `CardDef`, `Effect`, `Target`, `NumExpr`, `TriggerDef`, `TriggerEvent`, `Loadout`, `LoadoutItem`, `BattleConfig`, `RuleOverrides`, `ItemState`, `HeroState`, `ItemSnapshot`, `Snapshot`, `BattleEvent`, `BattleResult`, `Totals`（全部 `export`）
  - `DEFAULT_RULES: ResolvedRules`
  - `resolveRules(overrides?: RuleOverrides): ResolvedRules`
  - `createRng(seed: number): Rng`，`Rng` 有 `next(): number`、`int(maxExclusive: number): number`、`chance(p: number): boolean`

- [ ] **Step 1: Write the failing rules/RNG tests**

```ts
// src/engine/__tests__/rules.test.ts
import { describe, it, expect } from 'vitest';
import { DEFAULT_RULES, resolveRules } from '../rules';

describe('rules', () => {
  it('uses the spec defaults', () => {
    expect(resolveRules()).toEqual(DEFAULT_RULES);
    expect(DEFAULT_RULES.dt).toBe(0.1);
    expect(DEFAULT_RULES.maxTime).toBe(60);
    expect(DEFAULT_RULES.hasteMultiplier).toBe(2);
    expect(DEFAULT_RULES.slowMultiplier).toBe(0.5);
    expect(DEFAULT_RULES.maxSlots).toBe(10);
    expect(DEFAULT_RULES.burnDecay).toBe(1);
    expect(DEFAULT_RULES.poisonDecay).toBe(0);
    expect(DEFAULT_RULES.sandstorm).toBeNull();
  });
  it('applies overrides', () => {
    expect(resolveRules({ dt: 0.2 }).dt).toBe(0.2);
  });
});
```

```ts
// src/engine/__tests__/rng.test.ts
import { describe, it, expect } from 'vitest';
import { createRng } from '../rng';

describe('rng', () => {
  it('is deterministic for the same seed', () => {
    const a = createRng(42), b = createRng(42);
    expect([a.next(), a.next(), a.next()]).toEqual([b.next(), b.next(), b.next()]);
  });
  it('chance bounds are exact', () => {
    expect(createRng(1).chance(0)).toBe(false);
    expect(createRng(1).chance(1)).toBe(true);
  });
  it('int stays in range', () => {
    const rng = createRng(7);
    for (let i = 0; i < 100; i++) {
      const n = rng.int(5);
      expect(n).toBeGreaterThanOrEqual(0);
      expect(n).toBeLessThan(5);
    }
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npm test -- --run src/engine/__tests__/rules.test.ts src/engine/__tests__/rng.test.ts`
Expected: FAIL（模块不存在）

- [ ] **Step 3: Implement types, rules, rng**

`types.ts` 按规格第 5 节定义全部类型。`rules.ts` 定义 `ResolvedRules = Required<Omit<RuleOverrides,'sandstorm'>> & { sandstorm: RuleOverrides['sandstorm'] | null }`、`DEFAULT_RULES`、`resolveRules`（展开覆盖值）。`rng.ts` 用 mulberry32；`chance(p)` 返回 `next() < p`；`int(n)` 返回 `Math.floor(next()*n)`。

- [ ] **Step 4: Run tests to verify they pass**

Run: `npm test -- --run src/engine/__tests__/rules.test.ts src/engine/__tests__/rng.test.ts`
Expected: PASS

- [ ] **Step 5: Commit (optional)**

```bash
git add src/engine/types.ts src/engine/rules.ts src/engine/rng.ts src/engine/__tests__/rules.test.ts src/engine/__tests__/rng.test.ts
git commit -m "feat: add engine types, rule table, seeded rng"
```

---

### Task 3: 游戏原始库导入器

**Files:**
- Create: `tools/import_game_db.py`, `tools/test_import_game_db.py`, `data/game_cards_structured.json`（脚本产物）
- Input: `vendor/BazaarPlusPlus/bazaarplusplus-mod/tests/CombatImpact.Corpus/corpus/GameData.cards.db`

**Interfaces:**
- Consumes: SQLite `cards(Id TEXT, Data BLOB)`，`Data` 是 UTF-8 JSON
- Produces: `data/game_cards_structured.json`，每条：
  ```ts
  {
    guid: string; internalName: string; size: 'Small'|'Medium'|'Large';
    tier: 'Bronze'|'Silver'|'Gold'|'Diamond'|'Legendary';
    cooldown: number; effects: object[]; triggers: object[];
    tags: string[]; multicastBonus: number;
    parseStatus: 'structured'|'partial'; dataSource: 'game-db';
    rawAbilities: object;
  }
  ```

- [ ] **Step 1: Write the failing importer tests**

```python
# tools/test_import_game_db.py
import unittest
from pathlib import Path
from import_game_db import convert_cards

class ImportGameDbTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        db = Path(r"D:\bazaar-action-axis\vendor\BazaarPlusPlus\bazaarplusplus-mod\tests\CombatImpact.Corpus\corpus\GameData.cards.db")
        cls.cards = convert_cards(db)

    def test_all_items_are_converted(self):
        self.assertEqual(len(self.cards), 116)

    def test_narwhal_bronze(self):
        c = next(x for x in self.cards if x["internalName"] == "Narwhal" and x["tier"] == "Bronze")
        self.assertEqual(c["cooldown"], 3)
        self.assertEqual(c["multicastBonus"], 0)
        dmg = next(e for e in c["effects"] if e["kind"] == "damage")
        self.assertEqual(dmg["amount"], {"type": "const", "value": 5})
        self.assertEqual(dmg["target"], {"kind": "enemyHero"})

    def test_hunting_hawk_haste(self):
        c = next(x for x in self.cards if x["internalName"] == "Hunting Hawk" and x["tier"] == "Bronze")
        h = next(e for e in c["effects"] if e["kind"] == "haste")
        self.assertEqual(h["seconds"], 2)
        self.assertEqual(h["count"], 1)

    def test_gavel_freeze(self):
        c = next(x for x in self.cards if x["internalName"] == "Gavel" and x["tier"] == "Bronze")
        f = next(e for e in c["effects"] if e["kind"] == "freeze")
        self.assertEqual(f["seconds"], 1)

if __name__ == "__main__":
    unittest.main()
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `python -m unittest tools.test_import_game_db -v`
Expected: FAIL（`import_game_db` 不存在）

- [ ] **Step 3: Implement `convert_cards(db_path: Path) -> list[dict]`**

只处理 `$type == "TCardItem"`。对每个 `Tiers[tier]`：
- `cooldown = Attributes["CooldownMax"] / 1000`
- `multicastBonus = max(0, Attributes.get("Multicast", 1) - 1)`
- 遍历 `AbilityIds`，从顶层 `Abilities` 取对应能力；按规格 9.1 的动作/数值/目标/触发器映射表生成 `effects`/`triggers`。
- 无法映射的动作、触发器、目标：把整条能力放入 `rawAbilities`，`parseStatus="partial"`；全部可映射则为 `"structured"`。
- `TActionAnd` 递归展开 `Actions`。
- 数值属性缺失时，该效果跳过并标记 `partial`。
- 输出按 `(internalName, tier)` 排序，写 `data/game_cards_structured.json`，控制台打印总数与 structured/partial 计数。

- [ ] **Step 4: Run tests to verify they pass**

Run: `python -m unittest tools.test_import_game_db -v`
Expected: PASS

- [ ] **Step 5: Generate the data file**

Run: `python tools/import_game_db.py`
Expected: 打印 `116`, 生成 `data/game_cards_structured.json`

- [ ] **Step 6: Commit (optional)**

```bash
git add tools/import_game_db.py tools/test_import_game_db.py data/game_cards_structured.json
git commit -m "feat: import structured card data from game db"
```

---

### Task 4: 图鉴 L1/L2 解析器

**Files:**
- Create: `src/engine/parse.ts`
- Test: `src/engine/__tests__/parse.test.ts`

**Interfaces:**
- Consumes: `Effect`, `TriggerDef`, `NumExpr`, `Target`
- Produces: `parseTooltips(tooltips: string[]): ParseResult`，其中
  ```ts
  interface ParseResult {
    cooldown: number | null;
    effects: Effect[];
    triggers: TriggerDef[];
    ammoMax?: number;
    parseStatus: 'structured' | 'partial' | 'text';
    unparsed: string[];
  }
  ```

- [ ] **Step 1: Write the failing parser tests**

```ts
// src/engine/__tests__/parse.test.ts
import { describe, it, expect } from 'vitest';
import { parseTooltips } from '../parse';

const dmg = (v: number) => ({ kind: 'damage', amount: { type: 'const', value: v }, target: { kind: 'enemyHero' } });

describe('parseTooltips L1/L2', () => {
  it('parses cooldown and damage', () => {
    const r = parseTooltips(['Cooldown 4 seconds', 'Deal 10 Damage']);
    expect(r.cooldown).toBe(4);
    expect(r.effects).toEqual([dmg(10)]);
    expect(r.parseStatus).toBe('structured');
  });
  it('parses percent max health damage', () => {
    const r = parseTooltips(['Cooldown 12 seconds', "Deal Damage equal to 20% of an enemy's Max Health"]);
    expect(r.effects[0]).toEqual({ kind: 'damage', amount: { type: 'percentMaxHp', value: 20 }, target: { kind: 'enemyHero' } });
  });
  it('parses heal and shield with default self target', () => {
    const r = parseTooltips(['Heal 10', 'Shield 6']);
    expect(r.effects).toEqual([
      { kind: 'heal', amount: { type: 'const', value: 10 }, target: { kind: 'selfHero' } },
      { kind: 'shield', amount: { type: 'const', value: 6 }, target: { kind: 'selfHero' } },
    ]);
  });
  it('parses burn and poison with enemy target', () => {
    const r = parseTooltips(['Burn 3', 'Poison 2']);
    expect(r.effects[0]).toEqual({ kind: 'burn', amount: { type: 'const', value: 3 }, target: { kind: 'enemyHero' } });
    expect(r.effects[1]).toEqual({ kind: 'poison', amount: { type: 'const', value: 2 }, target: { kind: 'enemyHero' } });
  });
  it('parses haste/slow/freeze/charge', () => {
    const r = parseTooltips([
      'Haste 2 item(s) for 1 second(s)',
      'Slow 1 item(s) for 3 second(s)',
      'Freeze an item for 1.5 second(s)',
      'Charge an adjacent Relic 3 second(s)',
    ]);
    expect(r.effects).toEqual([
      { kind: 'haste', seconds: 1, count: 2, target: { kind: 'randomSelfItem' } },
      { kind: 'slow', seconds: 3, count: 1, target: { kind: 'randomEnemyItem' } },
      { kind: 'freeze', seconds: 1.5, count: 1, target: { kind: 'randomEnemyItem' } },
      { kind: 'charge', seconds: 3, count: 1, target: { kind: 'adjacentSelf' } },
    ]);
  });
  it('parses multicast, crit, ammo, reload', () => {
    const r = parseTooltips(['Multicast 2', 'Crit Chance 20%', 'Ammo 2', 'Reload adjacent items']);
    expect(r.effects).toContainEqual({ kind: 'multicast', count: 2 });
    expect(r.effects).toContainEqual({ kind: 'crit', chance: 0.2 });
    expect(r.ammoMax).toBe(2);
    expect(r.effects).toContainEqual({ kind: 'reload', amount: 1, target: { kind: 'adjacentSelf' } });
  });
  it('keeps unknown lines as unparsed', () => {
    const r = parseTooltips(['Cooldown 4 seconds', 'When you sneeze, do a backflip']);
    expect(r.parseStatus).toBe('partial');
    expect(r.unparsed).toEqual(['When you sneeze, do a backflip']);
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npm test -- --run src/engine/__tests__/parse.test.ts`
Expected: FAIL（模块不存在）

- [ ] **Step 3: Implement `parseTooltips`**

按规格 9.2 的 L1/L2 正则逐行匹配。大小写不敏感；`s`/`second(s)`/`seconds` 都接受。`Multicast 2` 生成 `{kind:'multicast', count:2}`（引擎里表示额外施放次数）。`Ammo N` 设置 `ammoMax=N`。解析状态：有 cooldown 且无 unparsed 且有至少一个效果/触发 → `structured`；有任一解析成功 → `partial`；否则 `text`。

- [ ] **Step 4: Run tests to verify they pass**

Run: `npm test -- --run src/engine/__tests__/parse.test.ts`
Expected: PASS

- [ ] **Step 5: Commit (optional)**

```bash
git add src/engine/parse.ts src/engine/__tests__/parse.test.ts
git commit -m "feat: parse codex tooltips into effects"
```

---

### Task 5: L3 触发器解析 + 构建合并脚本

**Files:**
- Modify: `src/engine/parse.ts`
- Create: `tools/build_cards.ts`
- Test: `src/engine/__tests__/parse_triggers.test.ts`, `src/engine/__tests__/cards_data.test.ts`
- Output: `src/data/cards.json`, `src/data/parse_report.json`

**Interfaces:**
- Consumes: `parseTooltips`（Task 4）、`data/items_raw.json`、`data/game_cards_structured.json`（Task 3）、`CardDef`
- Produces: `cards.json`（`CardDef[]`）、`parse_report.json`

- [ ] **Step 1: Write the failing trigger/build tests**

```ts
// src/engine/__tests__/parse_triggers.test.ts
import { describe, it, expect } from 'vitest';
import { parseTooltips } from '../parse';

describe('parseTooltips L3 triggers', () => {
  it('parses "when you heal"', () => {
    const r = parseTooltips(['Cooldown 4 seconds', 'When you Heal, Shield 5']);
    expect(r.triggers).toEqual([
      { event: 'onHeal', effects: [{ kind: 'shield', amount: { type: 'const', value: 5 }, target: { kind: 'selfHero' } }] },
    ]);
    expect(r.parseStatus).toBe('structured');
  });
  it('parses "at the start of combat"', () => {
    const r = parseTooltips(['At the start of combat, Haste 1 item for 2 seconds']);
    expect(r.triggers[0].event).toBe('atCombatStart');
  });
  it('parses "every N seconds"', () => {
    const r = parseTooltips(['Every 2 seconds, Deal 3 Damage']);
    expect(r.triggers[0]).toMatchObject({ event: 'everySecond', everySeconds: 2 });
  });
  it('leaves unparseable trigger bodies in unparsed', () => {
    const r = parseTooltips(['When you sneeze, do a backflip']);
    expect(r.triggers).toEqual([]);
    expect(r.unparsed).toHaveLength(1);
  });
});
```

```ts
// src/engine/__tests__/cards_data.test.ts
import { describe, it, expect } from 'vitest';
import cards from '../../data/cards.json';
import report from '../../data/parse_report.json';

describe('generated card data', () => {
  it('contains at least 550 structured cards', () => {
    expect((cards as any[]).filter(c => c.parseStatus === 'structured').length).toBeGreaterThanOrEqual(550);
  });
  it('contains at least 80 game-db cards', () => {
    expect((cards as any[]).filter(c => c.dataSource === 'game-db').length).toBeGreaterThanOrEqual(80);
  });
  it('has no duplicate ids', () => {
    const ids = (cards as any[]).map(c => c.id);
    expect(new Set(ids).size).toBe(ids.length);
  });
  it('report has per-source counts', () => {
    expect(report.byDataSource['game-db']).toBeGreaterThanOrEqual(80);
    expect(report.byDataSource.tooltip).toBeGreaterThan(0);
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npm test -- --run src/engine/__tests__/parse_triggers.test.ts src/engine/__tests__/cards_data.test.ts`
Expected: FAIL（L3 未实现 / `cards.json` 不存在）

- [ ] **Step 3: Implement L3 in `parse.ts`**

匹配：
- `^When you (Heal|Shield|Haste|Slow|Freeze|Poison|Burn|Crit|Reload), (.+)$`
- `^When this is (Hasted|Slowed|Frozen), (.+)$`
- `^At the start of combat, (.+)$`
- `^Every ([\d.]+) seconds, (.+)$`

触发体用 L2 解析；解析失败则整条进入 `unparsed`。事件映射：`Heal→onHeal`、`Shield→onShield`、`Haste→onHaste`、`Slow→onSlow`、`Freeze→onFreeze`、`Poison→onPoisonApplied`、`Burn→onBurnApplied`、`Crit→onCrit`、`Reload→onReload`、`Hasted→onHaste`、`Slowed→onSlow`、`Frozen→onFreeze`。

- [ ] **Step 4: Implement `tools/build_cards.ts`**

流程：
1. 读 `data/items_raw.json` 与 `data/game_cards_structured.json`。
2. 对图鉴每个 `itemId@tier` 建卡：先按 `${guid}@${tier}` 查游戏库；命中则用游戏库的 `cooldown/effects/triggers/multicastBonus` 覆盖，`parseStatus` 用游戏库的值，`dataSource='game-db'`；未命中的用 `parseTooltips(tierData.tooltips)`，`dataSource='tooltip'`。
3. 游戏库中未匹配图鉴的条目追加为额外卡，`nameCn` 为空、`image` 为空。
4. `id` 去重；同名不同 GUID 不合并。
5. 写 `cards.json`（带 2 空格缩进）与 `parse_report.json`：
   ```json
   { "total": 0, "byParseStatus": {"structured":0,"partial":0,"text":0},
     "byDataSource": {"game-db":0,"tooltip":0}, "uniqueGuidMatched": 0 }
   ```
6. 控制台打印报告。

- [ ] **Step 5: Generate data and run tests**

Run: `npx tsx tools/build_cards.ts && npm test -- --run src/engine/__tests__/parse_triggers.test.ts src/engine/__tests__/cards_data.test.ts`
Expected: PASS；控制台 `structured >= 550`、`game-db >= 80`

- [ ] **Step 6: Commit (optional)**

```bash
git add src/engine/parse.ts src/engine/__tests__/parse_triggers.test.ts src/engine/__tests__/cards_data.test.ts tools/build_cards.ts src/data/cards.json src/data/parse_report.json
git commit -m "feat: build merged card catalog from game db and codex"
```

---

### Task 6: 战斗初始化、冷却推进、快照

**Files:**
- Create: `src/engine/battle.ts`
- Test: `src/engine/__tests__/battle_tick.test.ts`

**Interfaces:**
- Consumes: `BattleConfig`, `CardDef`, `ResolvedRules`, `createRng`
- Produces:
  - `simulate(config: BattleConfig): BattleResult`
  - `InternalItem`, `InternalHero`, `BattleContext`（导出给 `effects.ts`/`triggers.ts` 和测试）
  - `BattleContext.items: InternalItem[]`，`InternalItem.id` 等于 `CardDef.id`，`side` 为 `'player'|'enemy'`

- [ ] **Step 1: Write the failing tick tests**

```ts
// src/engine/__tests__/battle_tick.test.ts
import { describe, it, expect } from 'vitest';
import { simulate } from '../battle';
import type { CardDef } from '../types';

const card = (id: string, cooldown: number, extra: Partial<CardDef> = {}): CardDef => ({
  id, name: id, size: 'Small', tier: 'Bronze', cooldown,
  effects: [], triggers: [], tags: [], parseStatus: 'structured', rawTooltips: [],
  ...extra,
});

const config = (cards: Record<string, CardDef>, overrides = {}) => ({
  cards,
  player: { items: [{ cardId: 'a', slot: 0 }] },
  enemy: { items: [{ cardId: 'b', slot: 0 }] },
  playerMaxHp: 100, enemyMaxHp: 100, seed: 1, rules: {}, maxTime: 13,
  ...overrides,
});

describe('cooldown tick', () => {
  it('activates on cooldown boundaries', () => {
    const r = simulate(config({ a: card('a', 4), b: card('b', 6) }));
    const times = (id: string) => r.events
      .filter(e => e.kind === 'activate' && e.sourceItemId === id)
      .map(e => +e.time.toFixed(1));
    expect(times('a')).toEqual([4, 8, 12]);
    expect(times('b')).toEqual([6, 12]);
  });

  it('activates player before enemy on the same tick', () => {
    const r = simulate(config({ a: card('a', 12), b: card('b', 12) }));
    const at12 = r.events.filter(e => e.kind === 'activate' && Math.abs(e.time - 12) < 1e-6);
    expect(at12.map(e => e.side)).toEqual(['player', 'enemy']);
  });

  it('snapshots every tick', () => {
    const r = simulate(config({ a: card('a', 4), b: card('b', 6) }));
    expect(r.snapshots).toHaveLength(131);
  });

  it('handles an empty loadout', () => {
    const r = simulate(config({ a: card('a', 4) }, { enemy: { items: [] } }));
    expect(r.events.some(e => e.kind === 'activate' && e.side === 'player')).toBe(true);
    expect(r.winner).toBe('player');
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npm test -- --run src/engine/__tests__/battle_tick.test.ts`
Expected: FAIL（模块不存在）

- [ ] **Step 3: Implement `simulate` core loop**

```ts
// 签名
export function simulate(config: BattleConfig): BattleResult;

// 内部循环（规格 6.1）
for (let step = 0; step * dt <= maxTime + 1e-9; step++) {
  const t = +(step * dt).toFixed(6);
  // expire statuses, advance cooldowns, tick dots, collect ready, sort, activate, drain triggers, check death
  ctx.snapshots.push(snapshot(ctx, t));
  if (ctx.ended) break;
}
```

- `initBattle(config)` 按 `Loadout.items` 顺序创建 `InternalItem`，`cooldown = card.cooldown`；`startReady` 的卡 `cooldown = 0`。
- `advanceCooldowns`：`rate = frozen ? 0 : 1 * (haste ? rules.hasteMultiplier : 1) * (slow ? rules.slowMultiplier : 1)`；`cooldown -= dt * rate`。
- `collectReady` 返回本 tick 开始时 `cooldown <= 0` 的物品，排序 `player` 先、`slot` 升序。
- `activate` 先检查冻结/弹药，再连放 `1 + multicastBonus` 次；每次重置前调用效果结算（Task 7 起接入）。
- `snapshot` 复制双方 `HeroState` 和每个物品的 `itemId/slot/cooldown/cooldownMax/hasteUntil/slowUntil/frozenUntil/ammo`。
- 空卡组：另一方没有物品时，循环照常推进；时间到按生命百分比判定。

- [ ] **Step 4: Run tests to verify they pass**

Run: `npm test -- --run src/engine/__tests__/battle_tick.test.ts`
Expected: PASS

- [ ] **Step 5: Commit (optional)**

```bash
git add src/engine/battle.ts src/engine/__tests__/battle_tick.test.ts
git commit -m "feat: add battle tick loop and snapshots"
```

---

### Task 7: 伤害、治疗、护盾、暴击、吸血

**Files:**
- Create: `src/engine/effects.ts`
- Modify: `src/engine/battle.ts`
- Test: `src/engine/__tests__/effects.test.ts`

**Interfaces:**
- Consumes: `InternalItem`, `InternalHero`, `BattleContext`（Task 6）
- Produces:
  - `evaluateNum(expr: NumExpr, ctx: BattleContext, source: InternalItem): number`
  - `resolveEffect(ctx: BattleContext, effect: Effect, source: InternalItem, time: number, depth: number): void`
  - `applyDamage(ctx, source, targetSide, amount, time, opts?: { isDot?: boolean; depth?: number }): void`

- [ ] **Step 1: Write the failing tests**

```ts
// src/engine/__tests__/effects.test.ts
import { describe, it, expect } from 'vitest';
import { simulate } from '../battle';
import type { CardDef, Effect } from '../types';

const base = (id: string, cooldown: number, effects: Effect[], extra: Partial<CardDef> = {}): CardDef => ({
  id, name: id, size: 'Small', tier: 'Bronze', cooldown,
  effects, triggers: [], tags: [], parseStatus: 'structured', rawTooltips: [], ...extra,
});
const cfg = (cards: Record<string, CardDef>, p: string[], e: string[]) => ({
  cards,
  player: { items: p.map((id, i) => ({ cardId: id, slot: i })) },
  enemy: { items: e.map((id, i) => ({ cardId: id, slot: i })) },
  playerMaxHp: 100, enemyMaxHp: 100, seed: 1, rules: {}, maxTime: 2,
});

describe('damage / heal / shield', () => {
  it('deals damage to the enemy hero', () => {
    const r = simulate(cfg({ d: base('d', 1, [{ kind: 'damage', amount: { type: 'const', value: 10 }, target: { kind: 'enemyHero' } }]) }, ['d'], []));
    expect(r.snapshots.at(-1)!.enemy.hp).toBe(90);
  });

  it('absorbs with shield before health', () => {
    const r = simulate(cfg({
      s: base('s', 0.5, [{ kind: 'shield', amount: { type: 'const', value: 6 }, target: { kind: 'selfHero' } }]),
      d: base('d', 1, [{ kind: 'damage', amount: { type: 'const', value: 10 }, target: { kind: 'enemyHero' } }]),
    }, ['d'], ['s']));
    const hit = r.events.find(e => e.kind === 'damage')!;
    expect(hit.absorbed).toBe(6);
    expect(r.snapshots.at(-1)!.enemy.hp).toBe(96);
  });

  it('heals without exceeding max hp', () => {
    const r = simulate(cfg({ h: base('h', 1, [{ kind: 'heal', amount: { type: 'const', value: 10 }, target: { kind: 'selfHero' } }]) }, ['h'], []));
    expect(r.snapshots.at(-1)!.player.hp).toBe(100);
  });

  it('crit doubles the amount', () => {
    const r = simulate(cfg({ d: base('d', 1, [
      { kind: 'crit', chance: 1 },
      { kind: 'damage', amount: { type: 'const', value: 10 }, target: { kind: 'enemyHero' } },
    ]) }, ['d'], []));
    expect(r.snapshots.at(-1)!.enemy.hp).toBe(80);
  });

  it('lifesteal heals by actual hp damage', () => {
    const r = simulate(cfg({ d: base('d', 1, [
      { kind: 'lifesteal', percent: 1 },
      { kind: 'damage', amount: { type: 'const', value: 10 }, target: { kind: 'enemyHero' } },
    ])}, ['d'], []));
    expect(r.snapshots.at(-1)!.player.hp).toBe(100);
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npm test -- --run src/engine/__tests__/effects.test.ts`
Expected: FAIL（`effects.ts` 不存在或结算未接入）

- [ ] **Step 3: Implement `effects.ts`**

`evaluateNum`：`const` 返回 `value`；`percentMaxHp` 返回 `target.maxHp * value / 100`；`value`/`income` v1 返回 0；`regen` 返回目标 `regen`；`perStack` 返回 `stacks * per`。

`applyDamage`：
1. `amount = Math.max(0, Math.round(amount))`
2. 暴击：`ctx.rng.chance(source.critBonus)` → `amount *= rules.critMultiplier`，记录 `crit` 事件
3. `absorbed = min(target.shield, amount)`；`shield -= absorbed`
4. `hpLoss = min(target.hp, amount - absorbed)`；`target.hp -= hpLoss`
5. 记录 `damage` 事件（`value` 原始值、`absorbed`、`hpAfter`、`shieldAfter`）
6. 累积 `totals[attacker].damage += amount`、`effectiveDamage += hpLoss`
7. 若 `source.lifesteal > 0`，回复 `hpLoss * lifesteal`
8. 触发 `onDamageDealt` / `onDamageTaken`（Task 11 接入；此处先留空调用）

`resolveEffect` 分派 damage/heal/shield/crit/lifesteal；其余 kind 在后续任务实现。

- [ ] **Step 4: Run tests to verify they pass**

Run: `npm test -- --run src/engine/__tests__/effects.test.ts`
Expected: PASS

- [ ] **Step 5: Commit (optional)**

```bash
git add src/engine/effects.ts src/engine/battle.ts src/engine/__tests__/effects.test.ts
git commit -m "feat: resolve damage heal shield crit lifesteal"
```

---

### Task 8: 持续伤害与再生

**Files:**
- Modify: `src/engine/effects.ts`, `src/engine/battle.ts`
- Test: `src/engine/__tests__/dot.test.ts`

**Interfaces:**
- Consumes: `resolveEffect`、`applyDamage`
- Produces: 每整秒触发一次的 `tickDots(ctx, time)`；事件 kind 为 `dotTick`

- [ ] **Step 1: Write the failing tests**

```ts
// src/engine/__tests__/dot.test.ts
import { describe, it, expect } from 'vitest';
import { simulate } from '../battle';
import type { CardDef, Effect } from '../types';

const card = (id: string, cooldown: number, effects: Effect[]): CardDef => ({
  id, name: id, size: 'Small', tier: 'Bronze', cooldown,
  effects, triggers: [], tags: [], parseStatus: 'structured', rawTooltips: [],
});
const cfg = (c: CardDef, maxTime = 3) => ({
  cards: { [c.id]: c },
  player: { items: [{ cardId: c.id, slot: 0 }] },
  enemy: { items: [] }, playerMaxHp: 100, enemyMaxHp: 100, seed: 1, rules: {}, maxTime,
});

describe('damage over time', () => {
  it('burn ticks and decays by 1 each second', () => {
    const r = simulate(cfg(card('b', 0.5, [{ kind: 'burn', amount: { type: 'const', value: 3 }, target: { kind: 'enemyHero' } }]), 3));
    const ticks = r.events.filter(e => e.kind === 'dotTick' && (e as any).note === 'burn');
    expect(ticks.map(e => e.value)).toEqual([3, 2, 1]);
  });

  it('poison ticks without decaying', () => {
    const r = simulate(cfg(card('p', 0.5, [{ kind: 'poison', amount: { type: 'const', value: 3 }, target: { kind: 'enemyHero' } }]), 3));
    const ticks = r.events.filter(e => e.kind === 'dotTick' && (e as any).note === 'poison');
    expect(ticks.map(e => e.value)).toEqual([3, 3, 3]);
  });

  it('regen heals each second', () => {
    const c = card('r', 0.5, [{ kind: 'regen', amount: { type: 'const', value: 2 }, target: { kind: 'selfHero' } }]);
    const r = simulate({ ...cfg(c, 3), playerMaxHp: 100 });
    expect(r.events.filter(e => e.kind === 'dotTick' && (e as any).note === 'regen').length).toBeGreaterThan(0);
  });

  it('burn hits shield first by default', () => {
    const r = simulate(cfg(card('b', 0.5, [{ kind: 'burn', amount: { type: 'const', value: 3 }, target: { kind: 'enemyHero' } }]), 1));
    const tick = r.events.find(e => e.kind === 'dotTick' && (e as any).note === 'burn')!;
    expect(tick.absorbed).toBe(0);
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npm test -- --run src/engine/__tests__/dot.test.ts`
Expected: FAIL（DoT 未实现）

- [ ] **Step 3: Implement**

- `resolveEffect` 的 `burn`/`poison`：给目标英雄加层数；`regen`：给目标英雄加 `regen`。
- 主循环在跨整秒时调用 `tickDots(ctx, t)`：
  - Burn：`applyDamage(..., burn, {isDot:true})`，然后 `burn = max(0, burn - rules.burnDecay)`。
  - Poison：`applyDamage(..., poison, {isDot:true})`，不衰减。
  - Regen：`heal = min(hp+regen, maxHp)`，记录 `dotTick`。
  - 每个 tick 记录一条 `dotTick` 事件，`note` 为 `'burn'|'poison'|'regen'`。
- DoT 默认走护盾（`rules.dotBypassesShield=false`）；为 true 时直接扣血。

- [ ] **Step 4: Run tests to verify they pass**

Run: `npm test -- --run src/engine/__tests__/dot.test.ts`
Expected: PASS

- [ ] **Step 5: Commit (optional)**

```bash
git add src/engine/effects.ts src/engine/battle.ts src/engine/__tests__/dot.test.ts
git commit -m "feat: add burn poison regen ticks"
```

---

### Task 9: 冷却修改：Haste / Slow / Freeze / Charge

**Files:**
- Modify: `src/engine/effects.ts`, `src/engine/battle.ts`
- Test: `src/engine/__tests__/cooldown_modifiers.test.ts`

**Interfaces:**
- Consumes: `InternalItem`, `BattleContext`
- Produces: `resolveEffect` 支持 `haste/slow/freeze/charge`；`CardDef.startReady?: boolean`（若尚未加入类型，本任务补上）

- [ ] **Step 1: Write the failing tests**

```ts
// src/engine/__tests__/cooldown_modifiers.test.ts
import { describe, it, expect } from 'vitest';
import { simulate } from '../battle';
import type { CardDef, Effect } from '../types';

const card = (id: string, cooldown: number, effects: Effect[], startReady = false): CardDef => ({
  id, name: id, size: 'Small', tier: 'Bronze', cooldown,
  effects, triggers: [], tags: [], parseStatus: 'structured', rawTooltips: [], startReady,
});
const setup = (mod: Effect) => {
  const dmg = card('dmg', 4, [{ kind: 'damage', amount: { type: 'const', value: 1 }, target: { kind: 'enemyHero' } }]);
  const modCard = card('mod', 10, [mod], true);
  return {
    cards: { dmg, mod: modCard },
    player: { items: [{ cardId: 'mod', slot: 0 }, { cardId: 'dmg', slot: 1 }] },
    enemy: { items: [] }, playerMaxHp: 100, enemyMaxHp: 100, seed: 1, rules: {}, maxTime: 12,
  };
};
const firstHit = (r: any) => +r.events.find((e: any) => e.kind === 'damage')!.time.toFixed(1);

describe('cooldown modifiers', () => {
  it('haste doubles the rate for its duration', () => {
    expect(firstHit(simulate(setup({ kind: 'haste', seconds: 2, target: { kind: 'selfItem', itemId: 'dmg' } })))).toBe(2);
  });
  it('slow halves the rate for its duration', () => {
    expect(firstHit(simulate(setup({ kind: 'slow', seconds: 2, target: { kind: 'selfItem', itemId: 'dmg' } })))).toBe(5);
  });
  it('freeze pauses the cooldown', () => {
    expect(firstHit(simulate(setup({ kind: 'freeze', seconds: 2, target: { kind: 'selfItem', itemId: 'dmg' } })))).toBe(6);
  });
  it('charge removes seconds immediately', () => {
    expect(firstHit(simulate(setup({ kind: 'charge', seconds: 2, target: { kind: 'selfItem', itemId: 'dmg' } })))).toBe(2);
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npm test -- --run src/engine/__tests__/cooldown_modifiers.test.ts`
Expected: FAIL（冷却修改未实现）

- [ ] **Step 3: Implement**

- `haste`: `item.hasteUntil = Math.max(item.hasteUntil, time + seconds)`；同时 `item.slowUntil` 保持不变（两者相乘）。
- `slow`: 同理写 `slowUntil`。
- `freeze`: 写 `frozenUntil`。
- `charge`: `item.cooldown = Math.max(0, item.cooldown - seconds)`。
- `advanceCooldowns` 的 `rate` 按三项相乘（Task 6 已定义）。
- `startReady` 的卡在初始化时 `cooldown = 0`。

- [ ] **Step 4: Run tests to verify they pass**

Run: `npm test -- --run src/engine/__tests__/cooldown_modifiers.test.ts`
Expected: PASS

- [ ] **Step 5: Commit (optional)**

```bash
git add src/engine/types.ts src/engine/effects.ts src/engine/battle.ts src/engine/__tests__/cooldown_modifiers.test.ts
git commit -m "feat: apply haste slow freeze charge to cooldowns"
```

---

### Task 10: 多重施放、弹药、装填

**Files:**
- Modify: `src/engine/effects.ts`, `src/engine/battle.ts`
- Test: `src/engine/__tests__/multicast_ammo.test.ts`

**Interfaces:**
- Consumes: `InternalItem.ammo`, `InternalItem.card.ammoMax`
- Produces: `activate` 支持连放与弹药消耗；`resolveEffect` 支持 `multicast/ammo/reload`

- [ ] **Step 1: Write the failing tests**

```ts
// src/engine/__tests__/multicast_ammo.test.ts
import { describe, it, expect } from 'vitest';
import { simulate } from '../battle';
import type { CardDef, Effect } from '../types';

const card = (id: string, cooldown: number, effects: Effect[], extra: Partial<CardDef> = {}): CardDef => ({
  id, name: id, size: 'Small', tier: 'Bronze', cooldown,
  effects, triggers: [], tags: [], parseStatus: 'structured', rawTooltips: [], ...extra,
});
const cfg = (cards: Record<string, CardDef>, player: string[], enemy: string[] = [], maxTime = 8) => ({
  cards, player: { items: player.map((cid, i) => ({ cardId: cid, slot: i })) },
  enemy: { items: enemy.map((cid, i) => ({ cardId: cid, slot: i })) },
  playerMaxHp: 100, enemyMaxHp: 100, seed: 1, rules: {}, maxTime,
});

describe('multicast and ammo', () => {
  it('multicast fires the effect multiple times', () => {
    const c = card('m', 1, [
      { kind: 'multicast', count: 2 },
      { kind: 'damage', amount: { type: 'const', value: 10 }, target: { kind: 'enemyHero' } },
    ]);
    const r = simulate(cfg({ m: c }, ['m']));
    expect(r.events.filter(e => e.kind === 'damage').length).toBe(2);
    expect(r.snapshots.at(-1)!.enemy.hp).toBe(80);
  });

  it('ammo blocks activation until reloaded', () => {
    const a = card('a', 1, [{ kind: 'damage', amount: { type: 'const', value: 5 }, target: { kind: 'enemyHero' } }], { ammoMax: 2 });
    const rl = card('rl', 5, [{ kind: 'reload', amount: 1, target: { kind: 'selfItem', itemId: 'a' } }]);
    const r = simulate(cfg({ a, rl }, ['a', 'rl'], [], 8));
    const hits = r.events.filter(e => e.kind === 'damage' && e.sourceItemId === 'a');
    expect(hits.length).toBeGreaterThanOrEqual(3);
    expect(hits.every(h => h.time <= 8)).toBe(true);
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npm test -- --run src/engine/__tests__/multicast_ammo.test.ts`
Expected: FAIL

- [ ] **Step 3: Implement**

- `resolveEffect` 的 `multicast`：`source.multicastBonus += count`。
- `ammo`：`source.card.ammoMax` 已在建卡时确定；`source.ammo = ammoMax` 初始化。若效果是 `ammo`，增加 `ammoMax` 并同步 `ammo`。
- `activate`：`casts = 1 + item.multicastBonus`；每次施放前检查 `ammoMax != null && ammo <= 0`，为 0 则整个激活取消；每次施放后 `ammo--`。连放结束后 `cooldown = card.cooldown`。
- `reload`：目标物品 `ammo = min(ammoMax, ammo + amount)`，触发 `onReload`。

- [ ] **Step 4: Run tests to verify they pass**

Run: `npm test -- --run src/engine/__tests__/multicast_ammo.test.ts`
Expected: PASS

- [ ] **Step 5: Commit (optional)**

```bash
git add src/engine/effects.ts src/engine/battle.ts src/engine/__tests__/multicast_ammo.test.ts
git commit -m "feat: support multicast ammo and reload"
```

---

### Task 11: 触发队列与链式结算

**Files:**
- Create: `src/engine/triggers.ts`
- Modify: `src/engine/effects.ts`, `src/engine/battle.ts`
- Test: `src/engine/__tests__/triggers.test.ts`

**Interfaces:**
- Consumes: `BattleContext`, `TriggerDef`, `TriggerEvent`
- Produces:
  - `enqueueTrigger(ctx: BattleContext, payload: TriggerPayload): void`
  - `drainTriggerQueue(ctx: BattleContext, time: number): void`
  - `TriggerPayload = { event: TriggerEvent; sourceSide: Side; sourceItemId?: string; targetSide?: Side; depth: number }`

- [ ] **Step 1: Write the failing tests**

```ts
// src/engine/__tests__/triggers.test.ts
import { describe, it, expect } from 'vitest';
import { simulate } from '../battle';
import type { CardDef, Effect, TriggerDef } from '../types';

const card = (id: string, cooldown: number, effects: Effect[] = [], triggers: TriggerDef[] = [], extra: Partial<CardDef> = {}): CardDef => ({
  id, name: id, size: 'Small', tier: 'Bronze', cooldown,
  effects, triggers, tags: [], parseStatus: 'structured', rawTooltips: [], ...extra,
});
const cfg = (cards: Record<string, CardDef>, player: string[], enemy: string[] = [], maxTime = 2) => ({
  cards, player: { items: player.map((cid, i) => ({ cardId: cid, slot: i })) },
  enemy: { items: enemy.map((cid, i) => ({ cardId: cid, slot: i })) },
  playerMaxHp: 100, enemyMaxHp: 100, seed: 1, rules: {}, maxTime,
});

describe('triggers', () => {
  it('fires onDamageDealt', () => {
    const a = card('a', 1, [{ kind: 'damage', amount: { type: 'const', value: 10 }, target: { kind: 'enemyHero' } }]);
    const b = card('b', 99, [], [{ event: 'onDamageDealt', effects: [{ kind: 'shield', amount: { type: 'const', value: 5 }, target: { kind: 'selfHero' } }] }], { startReady: true });
    const r = simulate(cfg({ a, b }, ['a', 'b']));
    expect(r.snapshots.at(-1)!.player.shield).toBe(5);
  });

  it('fires atCombatStart once', () => {
    const b = card('b', 99, [], [{ event: 'atCombatStart', effects: [{ kind: 'shield', amount: { type: 'const', value: 7 }, target: { kind: 'selfHero' } }] }], { startReady: true });
    const r = simulate(cfg({ b }, ['b'], [], 1));
    expect(r.events.filter(e => e.kind === 'shield').length).toBe(1);
  });

  it('caps a self-trigger loop', () => {
    const loop = card('l', 1, [], [{ event: 'onDamageDealt', effects: [{ kind: 'damage', amount: { type: 'const', value: 1 }, target: { kind: 'enemyHero' } }] }]);
    const r = simulate(cfg({ l: loop }, ['l'], [], 0.2));
    expect(r.warnings.some(w => w.includes('depth'))).toBe(true);
  });

  it('does not activate newly-charged items in the same tick', () => {
    const charger = card('c', 99, [{ kind: 'charge', seconds: 1, target: { kind: 'selfItem', itemId: 'b' } }], [], { startReady: true });
    const b = card('b', 1, [{ kind: 'damage', amount: { type: 'const', value: 3 }, target: { kind: 'enemyHero' } }]);
    const r = simulate(cfg({ c: charger, b }, ['c', 'b'], [], 0.3));
    const firstB = r.events.find(e => e.kind === 'activate' && e.sourceItemId === 'b')!;
    expect(firstB.time).toBeGreaterThan(0);
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npm test -- --run src/engine/__tests__/triggers.test.ts`
Expected: FAIL

- [ ] **Step 3: Implement**

- `enqueueTrigger` 把 payload 推入 `ctx.triggerQueue`。
- `drainTriggerQueue`：
  ```ts
  while (ctx.triggerQueue.length) {
    const p = ctx.triggerQueue.shift()!;
    if (p.depth > ctx.rules.maxTriggerDepth) { ctx.warnings.push('trigger depth cap reached'); continue; }
    for (const item of ctx.items) {
      for (const tr of item.card.triggers) {
        if (tr.event !== p.event) continue;
        if (tr.filter?.sourceTag && !item.card.tags.includes(tr.filter.sourceTag)) continue;
        for (const eff of tr.effects) resolveEffect(ctx, eff, item, time, p.depth + 1);
      }
    }
  }
  ```
- `applyDamage` 在结算后 `enqueueTrigger({event:'onDamageDealt', sourceSide: attacker, ...})` 和 `onDamageTaken`；heal/shield/burn/poison/haste/slow/freeze/crit/reload 同理。
- 主循环在 `initBattle` 后、第一个 tick 前处理 `atCombatStart`（只触发一次）。
- 每 tick 结束时 `drainTriggerQueue`；达到 `maxEventsPerTick` 时记录 warning 并清空队列。

- [ ] **Step 4: Run tests to verify they pass**

Run: `npm test -- --run src/engine/__tests__/triggers.test.ts`
Expected: PASS

- [ ] **Step 5: Commit (optional)**

```bash
git add src/engine/triggers.ts src/engine/effects.ts src/engine/battle.ts src/engine/__tests__/triggers.test.ts
git commit -m "feat: add trigger queue with chain depth cap"
```

---

### Task 12: 死亡、胜负、总量、确定性

**Files:**
- Modify: `src/engine/battle.ts`
- Test: `src/engine/__tests__/battle_result.test.ts`

**Interfaces:**
- Consumes: `BattleContext`
- Produces: `simulate` 返回完整 `BattleResult`；`checkDeath(ctx, time)`；`computeWinner(ctx)`

- [ ] **Step 1: Write the failing tests**

```ts
// src/engine/__tests__/battle_result.test.ts
import { describe, it, expect } from 'vitest';
import { simulate } from '../battle';
import type { CardDef, Effect } from '../types';

const card = (id: string, cooldown: number, effects: Effect[], extra: Partial<CardDef> = {}): CardDef => ({
  id, name: id, size: 'Small', tier: 'Bronze', cooldown,
  effects, triggers: [], tags: [], parseStatus: 'structured', rawTooltips: [], ...extra,
});
const cfg = (cards: Record<string, CardDef>, player: string[], enemy: string[], maxTime = 60, hp = 100) => ({
  cards, player: { items: player.map((cid, i) => ({ cardId: cid, slot: i })) },
  enemy: { items: enemy.map((cid, i) => ({ cardId: cid, slot: i })) },
  playerMaxHp: hp, enemyMaxHp: hp, seed: 1, rules: {}, maxTime,
});

describe('battle result', () => {
  it('ends immediately on death', () => {
    const d = card('d', 1, [{ kind: 'damage', amount: { type: 'const', value: 10 }, target: { kind: 'enemyHero' } }]);
    const r = simulate(cfg({ d }, ['d'], [], 60, 10));
    expect(r.winner).toBe('player');
    expect(r.endTime).toBe(1);
    expect(r.events.some(e => e.kind === 'death')).toBe(true);
  });

  it('decides a timeout by hp percentage', () => {
    const d = card('d', 1, [{ kind: 'damage', amount: { type: 'const', value: 5 }, target: { kind: 'enemyHero' } }]);
    const r = simulate(cfg({ d }, ['d'], [], 2, 100));
    expect(r.winner).toBe('player');
    expect(r.endTime).toBe(2);
  });

  it('totals match event sums', () => {
    const d = card('d', 1, [{ kind: 'damage', amount: { type: 'const', value: 10 }, target: { kind: 'enemyHero' } }]);
    const r = simulate(cfg({ d }, ['d'], [], 3));
    const sum = r.events.filter(e => e.kind === 'damage').reduce((a, e) => a + (e.value ?? 0), 0);
    expect(r.totals.player.damage).toBe(sum);
  });

  it('is deterministic for the same seed', () => {
    const d = card('d', 1, [{ kind: 'damage', amount: { type: 'const', value: 10 }, target: { kind: 'enemyHero' } }]);
    const a = simulate(cfg({ d }, ['d'], [], 5));
    const b = simulate(cfg({ d }, ['d'], [], 5));
    expect(JSON.stringify(a.events)).toBe(JSON.stringify(b.events));
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npm test -- --run src/engine/__tests__/battle_result.test.ts`
Expected: FAIL（胜负/总量未实现）

- [ ] **Step 3: Implement**

- `checkDeath`：`hp <= 0` 时设 `ended=true`、`winner` 为对方、`endTime=t`，记录 `death` 事件；不再处理后续事件。
- 循环结束未死亡：比较 `player.hp/player.maxHp` 与 `enemy.hp/enemy.maxHp`，高者胜，相等 `draw`。
- `totals` 在效果结算时累加；`effectiveDamage` 只加实际生命伤害。
- `result.warnings = ctx.warnings`。

- [ ] **Step 4: Run tests to verify they pass**

Run: `npm test -- --run src/engine/__tests__/battle_result.test.ts`
Expected: PASS

- [ ] **Step 5: Commit (optional)**

```bash
git add src/engine/battle.ts src/engine/__tests__/battle_result.test.ts
git commit -m "feat: add death winner totals and determinism"
```

---

### Task 13: 时间轴组件

**Files:**
- Create: `src/components/Timeline.tsx`, `src/components/CardLane.tsx`, `src/components/CardChip.tsx`
- Test: `src/components/__tests__/Timeline.test.tsx`

**Interfaces:**
- Consumes: `BattleResult`, `CardDef`
- Produces:
  ```ts
  interface TimelineProps {
    result: BattleResult;
    cards: Record<string, CardDef>;
    playhead: number;
    onPlayheadChange: (t: number) => void;
    selectedEventId: number | null;
    onSelectEvent: (id: number) => void;
    selectedItemId: string | null;
    onSelectItem: (id: string) => void;
  }
  ```

- [ ] **Step 1: Write the failing tests**

```tsx
// src/components/__tests__/Timeline.test.tsx
import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import { Timeline } from '../Timeline';
import type { BattleResult, CardDef } from '../../engine/types';

const card = (id: string, cooldown: number): CardDef => ({
  id, name: id, size: 'Small', tier: 'Bronze', cooldown,
  effects: [], triggers: [], tags: [], parseStatus: 'structured', rawTooltips: [],
});
const result = {
  events: [
    { id: 1, time: 1, side: 'player', sourceItemId: 'a', kind: 'activate', chainId: 1, depth: 0 },
    { id: 2, time: 2, side: 'enemy', sourceItemId: 'b', kind: 'activate', chainId: 2, depth: 0 },
  ],
  snapshots: [{ time: 0, player: { hp: 100, maxHp: 100, shield: 0, burn: 0, poison: 0, regen: 0, items: [{ itemId: 'a', slot: 0, cooldown: 1, cooldownMax: 1, hasteUntil: 0, slowUntil: 0, frozenUntil: 0, ammo: 0 }] }, enemy: { hp: 100, maxHp: 100, shield: 0, burn: 0, poison: 0, regen: 0, items: [{ itemId: 'b', slot: 0, cooldown: 2, cooldownMax: 2, hasteUntil: 0, slowUntil: 0, frozenUntil: 0, ammo: 0 }] } }],
  totals: { player: { damage: 0, effectiveDamage: 0, heal: 0, shield: 0 }, enemy: { damage: 0, effectiveDamage: 0, heal: 0, shield: 0 } },
  winner: 'draw', endTime: 60, warnings: [],
} as BattleResult;

describe('Timeline', () => {
  it('renders four lanes for two items', () => {
    render(<Timeline result={result} cards={{ a: card('a', 1), b: card('b', 2) }} playhead={0} onPlayheadChange={() => {}} selectedEventId={null} onSelectEvent={() => {}} selectedItemId={null} onSelectItem={() => {}} />);
    expect(screen.getAllByTestId('timeline-lane')).toHaveLength(4);
  });

  it('reports playhead changes', () => {
    const onChange = vi.fn();
    render(<Timeline result={result} cards={{ a: card('a', 1), b: card('b', 2) }} playhead={0} onPlayheadChange={onChange} selectedEventId={null} onSelectEvent={() => {}} selectedItemId={null} onSelectItem={() => {}} />);
    fireEvent.change(screen.getByLabelText('时间轴播放头'), { target: { value: '30' } });
    expect(onChange).toHaveBeenCalledWith(30);
  });

  it('selects a marker', () => {
    const onSelect = vi.fn();
    render(<Timeline result={result} cards={{ a: card('a', 1), b: card('b', 2) }} playhead={0} onPlayheadChange={() => {}} selectedEventId={null} onSelectEvent={onSelect} selectedItemId={null} onSelectItem={() => {}} />);
    fireEvent.click(screen.getByTestId('event-marker-1'));
    expect(onSelect).toHaveBeenCalledWith(1);
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npm test -- --run src/components/__tests__/Timeline.test.tsx`
Expected: FAIL（组件不存在）

- [ ] **Step 3: Implement**

- `Timeline` 渲染一个 `<input type="range" aria-label="时间轴播放头" min={0} max={result.endTime} step={0.1} value={playhead} onChange={e => onPlayheadChange(+e.target.value)} />`。
- 下方按 `snapshots[0]` 的物品顺序渲染泳道：我方英雄、我方每个物品、敌方英雄、敌方每个物品；每条 `data-testid="timeline-lane"`。
- 每条物品泳道内，按 `time / endTime * 100%` 定位该物品的事件标记；标记 `data-testid={`event-marker-${event.id}`}`，点击调用 `onSelectEvent`。
- `CardChip` 显示名称、品阶、当前冷却（取 `playhead` 对应快照）、选中态；点击调用 `onSelectItem`。
- 英雄泳道显示当前 hp/shield/burn/poison。

- [ ] **Step 4: Run tests to verify they pass**

Run: `npm test -- --run src/components/__tests__/Timeline.test.tsx`
Expected: PASS

- [ ] **Step 5: Commit (optional)**

```bash
git add src/components/Timeline.tsx src/components/CardLane.tsx src/components/CardChip.tsx src/components/__tests__/Timeline.test.tsx
git commit -m "feat: add draggable timeline with lanes and markers"
```

---

### Task 14: 总量面板与触发详情

**Files:**
- Create: `src/components/TotalsPanel.tsx`, `src/components/DetailPanel.tsx`
- Test: `src/components/__tests__/TotalsPanel.test.tsx`, `src/components/__tests__/DetailPanel.test.tsx`

**Interfaces:**
- Consumes: `BattleResult['totals']`, `BattleEvent`, `CardDef`
- Produces:
  ```ts
  interface TotalsPanelProps { totals: BattleResult['totals']; endTime: number; }
  interface DetailPanelProps {
    events: BattleEvent[]; cards: Record<string, CardDef>;
    selectedEventId: number | null; selectedItemId: string | null;
  }
  ```

- [ ] **Step 1: Write the failing tests**

```tsx
// src/components/__tests__/TotalsPanel.test.tsx
import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import { TotalsPanel } from '../TotalsPanel';

describe('TotalsPanel', () => {
  it('shows both sides and dps', () => {
    render(<TotalsPanel endTime={10} totals={{
      player: { damage: 100, effectiveDamage: 80, heal: 20, shield: 30 },
      enemy: { damage: 50, effectiveDamage: 40, heal: 10, shield: 5 },
    }} />);
    expect(screen.getByText('总伤害')).toBeTruthy();
    expect(screen.getByText('100')).toBeTruthy();
    expect(screen.getByText('8.0')).toBeTruthy(); // 80 / 10
  });
});
```

```tsx
// src/components/__tests__/DetailPanel.test.tsx
import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import { DetailPanel } from '../DetailPanel';
import type { BattleEvent } from '../../engine/types';

const e = (id: number, chainId: number, kind: any = 'damage', sourceItemId = 'a'): BattleEvent => ({
  id, time: id, side: 'player', sourceItemId, kind, chainId, depth: 0, value: id * 10,
});

describe('DetailPanel', () => {
  it('shows the full chain of the selected event', () => {
    render(<DetailPanel events={[e(1, 7), e(2, 7), e(3, 8)]} cards={{}} selectedEventId={1} selectedItemId={null} />);
    expect(screen.getByText(/事件 #1/)).toBeTruthy();
    expect(screen.getByText(/事件 #2/)).toBeTruthy();
    expect(screen.queryByText(/事件 #3/)).toBeNull();
  });

  it('filters by selected item', () => {
    render(<DetailPanel events={[e(1, 7, 'damage', 'a'), e(2, 8, 'damage', 'b')]} cards={{}} selectedEventId={null} selectedItemId="a" />);
    expect(screen.getByText(/事件 #1/)).toBeTruthy();
    expect(screen.queryByText(/事件 #2/)).toBeNull();
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npm test -- --run src/components/__tests__/TotalsPanel.test.tsx src/components/__tests__/DetailPanel.test.tsx`
Expected: FAIL

- [ ] **Step 3: Implement**

- `TotalsPanel`：两行（我方/敌方），列：总伤害、有效伤害、治疗、护盾、DPS（`effectiveDamage / max(endTime, 1)`，保留 1 位）。
- `DetailPanel`：若 `selectedEventId` 非空，取该事件的 `chainId`，展示同 `chainId` 的所有事件（按 id 升序），每条显示 `事件 #id`、时间、来源卡、类型、数值、护盾吸收、剩余生命/护盾。若 `selectedItemId` 非空，展示该物品的所有事件。
- 两者都不做计算，只渲染引擎输出。

- [ ] **Step 4: Run tests to verify they pass**

Run: `npm test -- --run src/components/__tests__/TotalsPanel.test.tsx src/components/__tests__/DetailPanel.test.tsx`
Expected: PASS

- [ ] **Step 5: Commit (optional)**

```bash
git add src/components/TotalsPanel.tsx src/components/DetailPanel.tsx src/components/__tests__/TotalsPanel.test.tsx src/components/__tests__/DetailPanel.test.tsx
git commit -m "feat: add totals and trigger detail panels"
```

---

### Task 15: 卡组编排与图鉴导入

**Files:**
- Create: `src/components/LoadoutEditor.tsx`, `src/components/ImportDialog.tsx`
- Test: `src/components/__tests__/LoadoutEditor.test.tsx`

**Interfaces:**
- Consumes: `Loadout`, `CardDef`
- Produces:
  ```ts
  interface LoadoutEditorProps {
    side: Side; loadout: Loadout; cards: Record<string, CardDef>;
    maxSlots: number; onChange: (l: Loadout) => void; onOpenImport: (side: Side) => void;
  }
  interface ImportDialogProps {
    open: boolean; cards: Record<string, CardDef>;
    onPick: (cardId: string) => void; onClose: () => void;
  }
  ```

- [ ] **Step 1: Write the failing tests**

```tsx
// src/components/__tests__/LoadoutEditor.test.tsx
import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import { LoadoutEditor } from '../LoadoutEditor';
import { ImportDialog } from '../ImportDialog';
import type { CardDef } from '../../engine/types';

const c = (id: string, name: string, nameCn?: string): CardDef => ({
  id, name, nameCn, size: 'Small', tier: 'Bronze', cooldown: 4,
  effects: [], triggers: [], tags: [], parseStatus: 'structured', rawTooltips: [],
});

describe('LoadoutEditor', () => {
  it('removes an item', () => {
    const onChange = vi.fn();
    render(<LoadoutEditor side="player" loadout={{ items: [{ cardId: 'a', slot: 0 }] }} cards={{ a: c('a', 'A') }} maxSlots={10} onChange={onChange} onOpenImport={() => {}} />);
    fireEvent.click(screen.getByLabelText('移除 A'));
    expect(onChange).toHaveBeenCalledWith({ items: [] });
  });
});

describe('ImportDialog', () => {
  it('filters by chinese name and picks', () => {
    const onPick = vi.fn();
    render(<ImportDialog open cards={{ a: c('a', 'Ambergris', '龙涎香'), b: c('b', 'Anchor', '船锚') }} onPick={onPick} onClose={() => {}} />);
    fireEvent.change(screen.getByPlaceholderText('搜索卡牌'), { target: { value: '龙涎' } });
    expect(screen.getByText('龙涎香')).toBeTruthy();
    expect(screen.queryByText('船锚')).toBeNull();
    fireEvent.click(screen.getByText('龙涎香'));
    expect(onPick).toHaveBeenCalledWith('a');
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npm test -- --run src/components/__tests__/LoadoutEditor.test.tsx`
Expected: FAIL

- [ ] **Step 3: Implement**

- `LoadoutEditor`：按 `slot` 排序显示卡牌 chip、名称、品阶、冷却；每张有 `aria-label={`移除 ${name}`}` 按钮；顶部“添加卡牌”按钮调用 `onOpenImport(side)`；达到 `maxSlots` 时禁用添加并提示。
- `ImportDialog`：`<input placeholder="搜索卡牌" />`，按 `name`/`nameCn` 大小写不敏感过滤；列表显示图片（失败占位）、名称、品阶、`parseStatus` 徽章；点击条目调用 `onPick(card.id)`；关闭按钮调用 `onClose`。

- [ ] **Step 4: Run tests to verify they pass**

Run: `npm test -- --run src/components/__tests__/LoadoutEditor.test.tsx`
Expected: PASS

- [ ] **Step 5: Commit (optional)**

```bash
git add src/components/LoadoutEditor.tsx src/components/ImportDialog.tsx src/components/__tests__/LoadoutEditor.test.tsx
git commit -m "feat: add loadout editor and card importer"
```

---

### Task 16: 自定义卡牌编辑器与持久化

**Files:**
- Create: `src/components/CardEditor.tsx`, `src/state/storage.ts`
- Test: `src/components/__tests__/CardEditor.test.tsx`, `src/state/__tests__/storage.test.ts`

**Interfaces:**
- Consumes: `CardDef`, `Effect`, `TriggerDef`
- Produces:
  ```ts
  interface CardEditorProps { initial?: CardDef; onSave: (c: CardDef) => void; onCancel: () => void; }
  export function readJson<T>(key: string, fallback: T): T;
  export function writeJson(key: string, value: unknown): void;
  export function loadCustomCards(): CardDef[];
  export function saveCustomCards(cards: CardDef[]): void;
  export function loadLoadouts(): { player: Loadout; enemy: Loadout } | null;
  export function saveLoadouts(player: Loadout, enemy: Loadout): void;
  export function loadRules(): RuleOverrides;
  export function saveRules(rules: RuleOverrides): void;
  ```

- [ ] **Step 1: Write the failing tests**

```tsx
// src/components/__tests__/CardEditor.test.tsx
import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import { CardEditor } from '../CardEditor';

describe('CardEditor', () => {
  it('rejects an empty name', () => {
    const onSave = vi.fn();
    render(<CardEditor onSave={onSave} onCancel={() => {}} />);
    fireEvent.change(screen.getByLabelText('冷却（秒）'), { target: { value: '4' } });
    fireEvent.click(screen.getByText('保存'));
    expect(screen.getByText('名称不能为空')).toBeTruthy();
    expect(onSave).not.toHaveBeenCalled();
  });

  it('rejects cooldown below 0.1', () => {
    render(<CardEditor onSave={() => {}} onCancel={() => {}} />);
    fireEvent.change(screen.getByLabelText('名称'), { target: { value: '测试卡' } });
    fireEvent.change(screen.getByLabelText('冷却（秒）'), { target: { value: '0' } });
    fireEvent.click(screen.getByText('保存'));
    expect(screen.getByText('冷却必须 ≥ 0.1 秒')).toBeTruthy();
  });

  it('saves a valid custom card', () => {
    const onSave = vi.fn();
    render(<CardEditor onSave={onSave} onCancel={() => {}} />);
    fireEvent.change(screen.getByLabelText('名称'), { target: { value: '测试卡' } });
    fireEvent.change(screen.getByLabelText('冷却（秒）'), { target: { value: '4' } });
    fireEvent.click(screen.getByText('添加效果'));
    fireEvent.click(screen.getByText('保存'));
    expect(onSave).toHaveBeenCalledTimes(1);
    expect(onSave.mock.calls[0][0].id).toMatch(/^custom-/);
  });
});
```

```ts
// src/state/__tests__/storage.test.ts
import { describe, it, expect, beforeEach } from 'vitest';
import { readJson, loadCustomCards } from '../storage';

describe('storage', () => {
  beforeEach(() => localStorage.clear());

  it('falls back and backs up corrupt json', () => {
    localStorage.setItem('bazaar.customCards.v1', '{oops');
    expect(loadCustomCards()).toEqual([]);
    expect(localStorage.getItem('bazaar.customCards.v1.bak')).toBe('{oops');
  });

  it('reads valid json', () => {
    localStorage.setItem('x', '{"a":1}');
    expect(readJson('x', { a: 0 })).toEqual({ a: 1 });
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npm test -- --run src/components/__tests__/CardEditor.test.tsx src/state/__tests__/storage.test.ts`
Expected: FAIL

- [ ] **Step 3: Implement**

- `CardEditor` 字段：名称、中文名、尺寸、品阶、冷却、弹药上限、标签、图片 URL；效果行（类型/数值/目标/持续/次数/概率）；触发行（事件/来源标签/效果）。`aria-label` 与测试一致。
- 校验：名称非空、冷却 ≥ 0.1、数值 ≥ 0、百分比 0–100、至少一个效果或触发。错误文案与测试一致。
- 保存时 `id = initial?.id ?? 'custom-' + crypto.randomUUID()`，`parseStatus='structured'`。
- `storage.ts`：`readJson` 解析失败时把原值写入 `${key}.bak`，返回 `fallback`；`writeJson` 直接 `JSON.stringify`。`loadCustomCards` 用 `readJson(KEYS.customCards, [])`，并过滤非数组。

- [ ] **Step 4: Run tests to verify they pass**

Run: `npm test -- --run src/components/__tests__/CardEditor.test.tsx src/state/__tests__/storage.test.ts`
Expected: PASS

- [ ] **Step 5: Commit (optional)**

```bash
git add src/components/CardEditor.tsx src/state/storage.ts src/components/__tests__/CardEditor.test.tsx src/state/__tests__/storage.test.ts
git commit -m "feat: add custom card editor and storage"
```

---

### Task 17: App 集成与播放控制

**Files:**
- Create: `src/state/useSimulator.ts`
- Modify: `src/App.tsx`, `src/styles.css`
- Test: `src/state/__tests__/useSimulator.test.tsx`, `src/__tests__/App.test.tsx`

**Interfaces:**
- Consumes: `simulate`, `cards.json`, `storage`
- Produces: `useSimulator(): SimulatorApi`（字段见规格与下方）

- [ ] **Step 1: Write the failing tests**

```tsx
// src/state/__tests__/useSimulator.test.tsx
import { renderHook, act } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import { useSimulator } from '../useSimulator';

describe('useSimulator', () => {
  it('runs a battle and advances the playhead', () => {
    const { result } = renderHook(() => useSimulator());
    act(() => result.current.run());
    expect(result.current.result).not.toBeNull();
    act(() => result.current.setPlayhead(5));
    expect(result.current.playhead).toBe(5);
    act(() => result.current.play());
    act(() => result.current.advance(1));
    expect(result.current.playhead).toBeGreaterThan(5);
  });
});
```

```tsx
// src/__tests__/App.test.tsx
import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import App from '../App';

describe('App', () => {
  it('runs a simulation from the button', () => {
    render(<App />);
    fireEvent.click(screen.getByText('模拟'));
    expect(screen.getByText('总伤害')).toBeTruthy();
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npm test -- --run src/state/__tests__/useSimulator.test.tsx src/__tests__/App.test.tsx`
Expected: FAIL

- [ ] **Step 3: Implement**

- `useSimulator` 初始卡组：从 `cards.json` 选前 3 张 `structured` 我方卡和前 3 张敌方卡（仅用于首屏可玩；可被用户替换）。
- `run()`：`simulate({ cards, player, enemy, playerMaxHp: 100, enemyMaxHp: 100, seed, rules, maxTime: rules.maxTime ?? 60 })`。
- `advance(dt)`：`playing` 时 `playhead = min(playhead + dt * speed, result.endTime)`；到终点自动 `playing=false`。
- `play()` 仅置 `playing=true`；实际推进由 `App` 的 `requestAnimationFrame` 调用 `advance`。
- `App.tsx` 布局：顶部标题 + 双方卡组 + 模拟按钮；中部 `TotalsPanel`；`Timeline`；`DetailPanel`；`LoadoutEditor`/`ImportDialog`/`CardEditor` 用状态切换。
- `styles.css` 用深色主题，时间轴泳道用 CSS grid；移动端纵向堆叠。

- [ ] **Step 4: Run tests to verify they pass**

Run: `npm test -- --run src/state/__tests__/useSimulator.test.tsx src/__tests__/App.test.tsx`
Expected: PASS

- [ ] **Step 5: Manual verification**

Run: `npm run dev`，浏览器打开：
1. 点击“模拟”，出现时间轴和总量。
2. 拖动播放头，血量/护盾变化。
3. 点击卡牌和触发点，详情面板出现。
4. 导入一张图鉴卡，重新模拟。

- [ ] **Step 6: Commit (optional)**

```bash
git add src/state/useSimulator.ts src/App.tsx src/styles.css src/state/__tests__/useSimulator.test.tsx src/__tests__/App.test.tsx
git commit -m "feat: wire simulator ui and playback"
```

---

### Task 18: 图片下载脚本与 README

**Files:**
- Create: `tools/download_images.py`, `README.md`
- Test: `tools/test_download_images.py`

**Interfaces:**
- Consumes: `data/items_raw.json`
- Produces: `public/images/{guid}.avif`（可选）；`--dry-run`/`--limit`/`--concurrency`

- [ ] **Step 1: Write the failing test**

```python
# tools/test_download_images.py
import unittest
from download_images import plan_downloads

class PlanTest(unittest.TestCase):
    def test_dry_run_plans_without_writing(self):
        rows = [{"id": "a", "image": "https://x/a.avif"}, {"id": "b", "image": "https://x/b.avif"}]
        plan = plan_downloads(rows, limit=1)
        self.assertEqual(len(plan), 1)
        self.assertEqual(plan[0]["id"], "a")

if __name__ == "__main__":
    unittest.main()
```

- [ ] **Step 2: Run test to verify it fails**

Run: `python -m unittest tools.test_download_images -v`
Expected: FAIL

- [ ] **Step 3: Implement**

`plan_downloads(items, limit=None)` 去重、过滤空 `image`、按顺序截断。`main()` 支持 `--input data/items_raw.json --out public/images --limit N --concurrency 8 --dry-run`；`--dry-run` 只打印计划；实际下载用 `urllib.request` + 3 次重试 + 跳过已存在文件。

- [ ] **Step 4: Run test and dry-run**

Run: `python -m unittest tools.test_download_images -v && python tools/download_images.py --dry-run --limit 5`
Expected: PASS；打印 5 个 URL，不创建文件

- [ ] **Step 5: Write README**

包含：项目简介、数据来源与许可（bazaarwinner / BazaarPlusPlus MIT / The Bazaar 版权）、安装、`npm run cards` 生成数据、`npm run dev`、`npm test`、可选图片下载、规则设置说明、已知限制（游戏库 116 件、Sandstorm 公式未确认、条件触发覆盖有限）。

- [ ] **Step 6: Final verification**

Run: `npm test -- --run && npm run build`
Expected: 全部 PASS，构建成功

- [ ] **Step 7: Commit (optional)**

```bash
git add tools/download_images.py tools/test_download_images.py README.md
git commit -m "docs: add readme and optional image downloader"
```

---

## Plan Self-Review

- **Spec coverage:** 规格 1–14 节均有对应任务：数据源/Task 3+5，规则/Task 2+6–12，引擎/Task 6–12，时间轴/Task 13，总量与详情/Task 14，编辑器与导入/Task 15–16，持久化/Task 16，错误处理贯穿 Task 11/16，测试/Task 2–18，验收/Task 17–18。
- **Type consistency:** `CardDef.id`、`InternalItem.id`、`BattleEvent.sourceItemId` 统一使用同一个卡牌 ID；`Effect.kind` 与 `resolveEffect` 分派一致；`TriggerEvent` 包含 `onRegen`；`BattleConfig.cards` 在 Task 6 定义、Task 7+ 使用。
- **Review Focus:** 空卡组 → Task 6；同 tick 排序 → Task 6；触发循环 → Task 11；解析失败 → Task 4/5；localStorage 损坏 → Task 16。
- **Proportion:** 计划以签名、测试断言和映射表为主，未抄写引擎实现；最复杂的 `resolveEffect` 与触发器只给出分派规则和边界，不逐行转录。
