# G02 原生回放关键路径证据（2026-10-25）

本文件只记录**回放中直接观察到的事件**与对应回归，不把整份比较器的 DIFF 当作 G02 通过。
开场冷却水位仍未注入重建配置（G12 负责），因此这里只使用**相对帧、事件顺序、持续时间、施放次数**，不使用绝对冷却起点。

## 样本

| 回放 | 时长 | 玩家 | 敌方 | 本单用到的卡 |
| --- | --- | --- | --- | --- |
| `9bbabba4a216484d83d068a149811259` | 29.05s | Dooley | Stelle | Launcher Core、Cog(Icy)、Drowsdrop |
| `5682f3cdc669456ca0757bea0a58942f` | 15.05s | Dooley | Stelle | Launcher Core、Cog(Icy)、Drowsdrop |
| `e8e2fc61218442d4983d928f6ee9f8a4` | 11.95s | Dooley | Karnok | Waterskin(Shiny)、Haste 叠层、Utility Belt→弹药装填 |

回放文件位于本机 `The Bazaar/BazaarPlusPlusV5/CombatReplays/`，解码使用 `tools/native_replay_ledger.py --snapshot-db`。
原始 replay SHA256：

- `9bbabba4`：`cfb0032ea20e3e703c6a84b7bf998d7e35722997945edcded9f57cf836ff424b`
- `5682f3cd`：`41dacfe726518a42c835928060c2d4812d46535c9d4cca425b4d394edadd62c7`
- `e8e2fc61`：`68a623322b4482ebce65ff783beddb027fccca0114d554bcdd1b1c1f53aaf924`

## 1. Launcher Core 被动：被使用卡生效后的下一帧

原生事件（`CardCharge` 的 `source` 是 Core，`triggerSource` 是伙伴/飞行卡）：

| 回放 | 伙伴/飞行卡生效 | Core 被动充能 | 差值 |
| --- | --- | --- | --- |
| `9bbabba4` | Firefly `PlayerBurnApply` frame 102 / 5.10s | frame 103 / 5.15s | +1 帧（50ms） |
| `5682f3cd` | Firefly `PlayerBurnApply` frame 101 / 5.05s | frame 102 / 5.10s | +1 帧（50ms） |
| `5682f3cd` | Spider `PlayerPoisonApply` frame 120 / 6.00s | frame 121 / 6.05s | +1 帧（50ms） |

结论：数据库类型虽然是 `TTriggerOnBeforeItemUsed`，**运行时表现是下一帧**；此前按类型名改成同帧 `onBeforeUse` 会与回放差一帧。G02 校准保留 `onUse`，回归 `LauncherCorePassiveChargesOnNextFrame` 断言 `friendUse + 0.05s`。

同一样本还确认：

- Core 自己使用不会给自己充能（`SourceOther`）。
- Core 的主动在 `9bbabba4` 的 5.05/8.05/10.45/… 出现，和伙伴使用是不同事件。
- `§4.1` 中“BeforeItemUsed 使用前同步结算”的表述与这份回放不一致，应由 G03 复核；本单只校准 Core。

## 2. Shiny 的隐含 Multicast 基底

`e8e2fc61` 的 Waterskin（`3c9438a9-78a6-4cb3-b1d8-a6410693a879`，Bronze，Shiny）开场实时属性：

- `Multicast = 2`
- `CooldownMax = 4000`
- `ChargeAmount = 1000`

原生事件：

- frame 80 / 4.00s：`CardCharge` 左卡
- frame 85 / 4.25s：`CardCharge` 左卡
- frame 160 / 8.00s、frame 165 / 8.25s：再次两发

结论：没有显式 `Multicast` 字段的 Shiny 卡，实时值应为 2，两次施放间隔 250ms；不能把 Shiny 的 +1 再从 0 起算，也不能在实时值 2 上再叠成 3。回归 `LiveShinyMulticastUsesBaseOne` 使用完整 Waterskin 模板 + 实时属性 + Shiny，断言 `MulticastBonus=1` 且两次 `charge` 间隔 250ms。

## 3. 状态持续时间、叠层与到期

`9bbabba4` 的卡属性逐帧更新：

- Haste：frame 80 / 4.00s `0 → 2000ms`，每帧减 50ms，frame 120 / 6.00s 到 0。
- Slow：frame 100 / 5.00s `0 → 1000ms`，frame 120 / 6.00s 到 0。
- Freeze：frame 80 / 4.00s `0 → 500ms`，frame 90 / 4.50s 到 0。

`e8e2fc61` 的 Haste 叠层示例：

- frame 73 / 3.65s：`950 → 1900ms`
- frame 75 / 3.75s：`950 → 2900ms`
- frame 104 / 5.20s：`1500 → 2450ms`

结论：时长按 50ms 帧递减，到期帧按正常冷却速率推进；多次 Haste 是加法叠层，不是取最大值或永久保留。已有 `NativeDurationsStackAndFlyingReducesDebuffs`、`NativeStatusesExpireBeforeCooldownAdvance` 覆盖；本文件只补原生帧证据。

## 4. 装填与弹药

`e8e2fc61` 的敌方 `vS77YIg`（Silver Obsidian，AmmoMax=4）：

- frame 100 / 5.00s：`CardReload`，Ammo `2 → 4`（装满，不是只加 1）
- frame 190 / 9.50s：`CardReload`，Ammo `3 → 4`（加到上限）
- 其它弹药消耗：frame 59 `4→3`、frame 99 `3→2`、frame 164 `4→3`、frame 198 `4→3`

结论：空值/99 的装填是“装到上限”，不是固定加 1；每次施放消耗 1 发。Drowsdrop 的 `Multicast=1 + AmmoMax=1` 与 Shiny 的两次施放仍受弹药逐发限制，已有 `DrowsdropReloadRestoresAmmoAndSlowDelaysEnemy`、`DrowsdropShinyConsumesAmmoPerCast`。

## 5. 与整份比较器的关系

整份 `native_replay_compare.py` 仍然是 `DIFF`。G12 已加入 `G12OpeningCooldown` 路径（开场 `attributes.Cooldown` → `StartCooldown`），但 `9bbabba4` 的比较仍有大量 effects/cards/health 差异；需要 G12 与各规则族按关键帧继续分派。本文件的证据不依赖绝对冷却起点，因此可用于验证上述**相对行为**；但不能据此宣称 Gate C 全绿。

G12 修复开场冷却注入后，应重跑同一批样本，并把剩余差异按 Charge/Haste/Slow/Freeze/Reload/冷却分派；本文件不替代 G12 的比较器结论。
