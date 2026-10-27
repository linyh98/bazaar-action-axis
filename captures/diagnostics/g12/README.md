# G12 原生回放对账证据（2026-10-25，v9；P0 收敛附录见文末）

本目录只保存 G12 的只读对账输出，不包含游戏本体、账号信息或部署动作。
生成的 DLL 未复制到游戏目录；`release_check.json` 记录
`deployment=NOT_EXECUTED_READ_ONLY_CHECK`，`verdict=MISMATCH`。

## 本轮修复（相对上一版证据）

1. **开场 frame-0 DoT tick**：原生在 frame 0 就会对开场技能/开场状态施加的
   Poison/Burn/Regen 立即 tick（85 份回放样本）。模拟器已补上这一步，并同步
   修正 G06 `NativeMechanics` 的时序断言。
2. **开场冷却**：`LoadoutItem.StartCooldown` + `CardMapper` + `InitBattle` 注入
   开场剩余冷却；`StartReady` 仍优先为 0。开场值在 `merge_snapshot` 后冻结。
3. **技能去重**：快照与 `PlayerSkillEquipped` 描述同一个技能，旧账本追加两次，
   导致 Lash Out 等开场技能触发两次。现按 `instanceId` 合并。
4. **随机目标识别**：覆盖附魔能力与光环；`TTargetCardRandom` 的飞行/加速/
   减速/冻结/变形不再当成确定性差异。
5. **五通道补齐**：
   - 健康通道只比较 Health/HealthMax/Shield/Burn/Poison/HealthRegen/Dead；
   - 模拟账本导出全部实时卡牌属性；
   - 模拟技能实例加入 identity，技能来源前缀统一；
   - regen 重复 `heal` 不再计入效果通道；
   - 死亡帧后的模拟记录单列 `postEnd`；
   - 沙暴事件单列 `noCounterpart`。
6. **运行时/旧模板能力**：原生 `EffectId` 不在当前模板 Abilities/Auras/
   对应附魔内时标 `runtimeAbility`；这类效果和同帧卡牌更新单列 unverified，
   不再算 missing。原生每次光环刷新产生的卡牌更新也单列 `auraRefresh`。
7. **确定性回放单列**：按原生 `randomTarget` 事件数拆成 2 份纯确定性、
   143 份含随机分支。纯确定性仍有 30 missing／9 extra／350 value。

## 全量回放对账（521 份，不是抽样）

- 发现回放：**521** 份（游戏目录 321 + `D:\巴扎plus\CombatReplays` 200）。
- 可重建并比较：**145** 份。
- 阻塞：**376** 份
  - 200 份来自 `D:\巴扎plus\CombatReplays`，没有 BPP `OpeningMessage` 侧车；
  - 176 份旧回放使用当前 `GameData 5.0.0` 已不存在的模板。
- 145 份全部 `DIFF`，聚合：

| 指标 | 数量 |
| --- | ---: |
| matched | 303166 |
| unverified（随机/运行时/无对应，未通过） | 402265 |
| postEnd（原生结束后模拟记录） | 66637 |
| missing | 27191 |
| extra | 15055 |
| value-mismatch | 41295 |
| time-mismatch | 3 |
| unknown | 4255 |

`unverified` 明细：

| 原因 | 数量 |
| --- | ---: |
| randomCascade（随机目标后续分支） | 361937 |
| randomTransform（随机变形谱系及后续效果） | 28880 |
| noCounterpart（光环/沙暴/触发等无独立模拟事件） | 5742 |
| randomTarget（随机效果本身） | 3030 |
| runtimeAbility（当前模板没有的原生能力） | 2265 |
| skillIdentity（技能模板被排除/未模拟） | 343 |
| auraRefresh（原生每次光环刷新产生的卡牌更新） | 68 |

分栏对比：

| 子集 | 回放 | matched | unverified | missing | extra | value | time |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 纯确定性 | 2 | 5572 | 52 | 30 | 9 | 350 | 0 |
| 含随机分支 | 143 | 297594 | 402213 | 27161 | 15046 | 40945 | 3 |

**结论：不能把这 402265 条 unverified 当成通过。** 随机目标没有可重建的种子；
确定性回放也仍有差异，见下节。

## 确定性回放剩余根因

- `101f6357`（10 missing／1 extra／13 value）：
  - 原生 Idol 上存在当前模板没有的运行时/旧模板效果 `q1`，每次刷新给
    PoisonApplyAmount +4（7→11→15→…）；模拟器无法从静态模板重建。
    该效果及其同帧卡牌更新已标 `runtimeAbility`，不再算 missing。
  - frame-0 DoT tick 已修复；残余生命差是上述运行时效果的下游。
- `170666bf`（20 missing／8 extra／337 value）：
  - Matchbox `TTriggerOnItemUsed` 在同帧多次使用时只结算其中一次/延迟到下一帧；
    模拟器从额外的同帧使用（5fM1k0Q）获得充能，又丢失 native frame 86 的
    延迟充能，导致后续 285 帧冷却偏移。**没有采用“全局每帧只触发一次”的
    修法**：原生语料里有 1252 组同源同帧多次 CardCharge，粗暴合并会改坏
    已经验证的多重触发。
  - Heirlooms 的卡牌更新来自原生每次激活的光环刷新（+10 ShieldApplyAmount／
    +5 SellPrice）；当前静态光环重算不能复现每次增量，已标 `auraRefresh`。
- `1dbdc915` 等 143 份含随机分支：Turbo/Core/Cog 的随机目标不同，
  后续冷却/触发/伤害差异已计入 `randomCascade`，不归责为确定性规则错误。

## 两份固定样本（v5）

| 样本 | 帧数 | 用途 |
| --- | ---: | --- |
| `9bbabba4` | 581 | 随机目标 + Core/Cog/Drowsdrop 连锁 |
| `084bd840` | 426 | 3 次随机变形 + 3 次回退 |

最终计数：

- `9bbabba4`：matched 1393／unverified 5597／missing 49／extra 72／
  value-mismatch 178／time 0／unknown 17。
- `084bd840`：matched 2429／unverified 1441／postEnd 45／missing 3／extra 0／
  value-mismatch 18／time 0／unknown 29。

账本/模拟账本 SHA256：

- `ledger_9bbabba4.json` `b0228d8d5890b11c1c4fb4f302e15d47d4ffa1157282f9456d24738dab047a5f`
- `sim_9bbabba4.json` `9f4f0c50a5b6e93af93beb28a1c894360bc94a771839bf0669b37d28d55b1531`
- `ledger_084bd840.json` `91a26cb4c1a666f30d12e4d5e82eef3b5541e47ec0a5f7dff1936372320fbfdf`
- `sim_084bd840.json` `a5a51240e7c541b21a58fa5e4af4200f96c8e31613ab47b11661774458500a3f`

## 全量事件覆盖（521 份账本，178016 条战斗事件）

- 变形 `CardTransformed` 29 次、回退 29 次，覆盖 17 份回放；
- `CombatSimEventEffectTriggered` 仍为 **0**；被动触发目标路径没有样本；
- 沙暴相关事件 3699 次，覆盖 41 份回放；
- `randomTarget` 事件 **10880** 次（含附魔/光环映射）；
- 开场参战物品 5928（全量账本）；非满冷却 0；
- 409 个无 `Cooldown` 字段的物品是本身无冷却的被动/特殊物品。

## 复跑命令

```powershell
$root = 'd:\巴扎plus'
python "$root/tools/native_replay_batch.py" --out "$root/tmp_g12/batch_full_v9"
python "$root/tools/native_replay_coverage.py" "$root/tmp_g12/batch_full_v9/ledgers" `
  --json "$root/captures/diagnostics/g12/coverage_all.json"
```

单份对账：

```powershell
python "$root/tools/native_replay_compare.py" native.json simulated.json `
  --json diff.json --csv diff.csv
```

## 验证状态（v9 收口）

- 全量 C#：`OK`（含新增 frame-0 DoT 断言）；`G12UiHistory`、`G12OpeningCooldown` PASS。
- Python：`test_native_replay_ledger`、`test_native_replay_compare`、
  `test_rule_workplan` 共 23 项通过。
- 前端：37 文件／102 测试通过。
- 构建：`--compile-only` 退出码 0，仅既有 `CS0618` 警告。本次源 DLL：
  Engine `c55a5568…6bb80`（MVID `b8419f96-…`）、Plugin `43839ad8…4a20`
  （MVID `cc6422d9-…`）；已安装是 Engine `7ed0bd7c…46e85`
  （MVID `1687d746-…`）、Plugin `ba261a4e…8f6df`（MVID `0436a66f-…`）。
  日志加载的 engine MVID 与已安装一致、与本次构建不同。该安装变化不是本轮
  部署动作，`deployment=NOT_EXECUTED_READ_ONLY_CHECK`。
- 未部署；`release_check.json` 的 `MISMATCH` 表示构建 DLL 与已安装 DLL 不同，
  `deployment=NOT_EXECUTED_READ_ONLY_CHECK`。
- **145/145 可比较回放仍为 DIFF；2 份纯确定性回放也未通过。**
  不能报告 Gate D，也不能把随机分支或阻塞数据算成通过。

## P0 确定性收敛（2026-10-25 晚，`tools/replay_batch_audit.py` + 侧车 DB）

上节 v9 记录的两份纯确定性回放根因已在引擎修复并复跑收敛：

1. **充能 250ms 串行**（170666bf / Matchbox）：同一（充能者,目标）的
   `CardCharge` 至少间隔 250ms。全语料 27,629 次充能中同对相邻间隔
   1–4 帧为 **0** 次、恰 5 帧 **12,930** 次；同帧同对仅 3 例；同帧跨目标
   1,257 组与 multicast 施放不受影响。同帧两次使用只立即结算一次（+1 帧），
   第二次落在 `上次结算+0.25s`。充能落在已就绪（冷却 0、本帧将重置）的卡上
   时挂起 `PendingChargeOnReset`，在重置后的新冷却上扣减（帧 161：
   5000→4000）。实现：`Effects.TryReserveCharge`/`ApplyCharge`/
   `DeferredCharges` + `Battle.ProcessDeferredCharges`。
2. **Heirlooms 逐次叠加**（170666bf）：`WorksIn=CombatOnly` 的无前提
   `SellPrice Add` 光环按来源每次使用永久叠加（SellPrice 1→6→11→16→21）；
   引用其被写属性的同源无前提 Add 光环按“新贡献−旧贡献”增量重算
   （ShieldApplyAmount 2→12→22→32→42）。实现：
   `NativeDynamicAuras.ClassifyUseAuras`/`ExecuteUseAuras`。Idol of Decay
   的 q1 仍是模板缺失的运行时数据（runtimeAbility），不能由引擎重建。

复跑结果（`python tools/replay_batch_audit.py --replays <游戏回放目录>
--db <bazaarplusplus.db> --battle 101f6357… --battle 170666bf…`）：

- **101f6357 → MATCH（可验证差异 0）**；残余为 runtimeAbility 数据缺口。
- **170666bf → MATCH（可验证差异 0）**；8 次充能帧、SellPrice/Shield
  逐次值、冷却曲线逐帧对齐；残余 64 行全部为单侧模式不可验证通道
  （health/outcome/玩家侧属性）。

全量 321 份游戏目录回放对比（新引擎 vs 关闭新行为的基线 exe）：
判定 5→7 MATCH、0 降级；聚合 missing 121851→121635、extra 74084→73312、
interference 224310→219960、structural 75758→75246；95 场计数改善、
63 场计数上升（抽查为随机分支级联的类别间重分类，非规则回归，
如 b1621d7b 的 Cooldown value-mismatch 513→312 而 missing 类上升）。

测试：新增 `G12Determinism`（Matchbox 型串行 + Heirlooms 型叠加，帧级断言）；
4 个同时间戳合成测试按串行语义改写（Weaponized Core/Launcher Core/
Aviators/Firefly 邻居）。全量 C# `OK`；Python 58 项通过。未提交、未部署；
Gate C/D 的随机分支（无原生种子）与部署门禁状态不变。

## P1 G08 复核附录（2026-10-25 晚，续 P0 收敛）

- **084bd840 → MATCH**：Virus 变形目标顺序差异已不存在。
- **86f51187（Quicksilver）**：逐事件比较定位到帧 60 自身变形的**随机供体分叉**
  （原生供体 CDMax 5000 vs 模拟 4000；冷却公式本身与全部确定性证据一致：
  原生副本 4950/5000、打印机 2950/3000、Mirror 13950/14000 均为
  供体 CDMax − 流逝）。比较器增强：同帧两侧变形而供体模板集合不相交时，
  该实例谱系自变形帧起标 `randomTransform`（不再计为确定性差异）。
  新增 2 个单元测试；全量 321 场判定 314/7 不变、0 回归，
  2 场大幅重分类（9370ffbd 1388→678、bdb6dc55 883→503）。
- **ebde2082（3D Printer）**：当前最早分叉在帧 76–77——技能被动对使用的
  反应，原生 modifyAttribute 同帧、burn/haste 下一帧，模拟统一 +1 帧，
  随后全板级联（onset≈150）。属 G03/G06 技能被动调度域，非 G08 实例模型；
  v9 记录的 240ms 副本残差在该分叉解决前无法独立观测。打印机变形本身与引擎一致。
- **G05 经济读取光环**：引擎已支持 Income/Gold 读取；Ring King Gauntlets
  在语料 11 场出现，但侧车 OpeningMessage 无 Income/Gold 值（仅 Health），
  归 DATA 阻塞。

## P1 续：onUse Immediate 分帧（2026-10-25 深夜）

ebde2082 最早分叉（帧 76–77 技能被动 +1 帧）的根因是 **EffectPriority 分帧**：
同一 `TTriggerOnItemUsed` 的 Immediate 被动与被用物品自身效果同帧（76），
Medium 动作下一帧（77）。引擎已实现 `Triggers.DrainImmediateOnUse` +
`PartitionByPass`（前提同帧一次判定，非 Immediate 匹配快照下帧执行）。
修复后 ebde2082 帧 76–78 逐帧对齐，最早分叉后移到帧 79（强化飞碟冷却/
充能的机器人板级联下一层），总差异行 3090→2437。全量 321+1 场判定
不变（7 MATCH、0 降级），聚合 missing −377／extra −2115；89 场改善、
25 场随机级联重分类。测试 `G12Determinism` 新增 Immediate 分帧用例。

## P1 续 2：ebde2082 帧 79 层（2026-10-25 深夜）

剥掉 Immediate 分帧后，帧 79 层的两个确定性根因已修复：

1. **禁用的 before-destroy 链下一帧结算**：Fire Bomb 使用后自禁用（帧 79），
   原生 Boosted Saucer 的 onBeforeCardDestroyed 充能在帧 80 落地；引擎此前
   同帧排空导致充能早 1 帧、飞轮提前开火。已改为 NativeMechanics 下禁用后
   队列留待下一帧（onCardDisabled 排后），死亡结算中的复活链保持同步。
2. **禁用持久性**：`NativeDynamicAuras.Synchronize` 每帧用派生层
   CooldownDisabled=0 覆盖 `item.Disabled`，战斗内禁用被清除、冷却继续走
   （原生禁用后冷却停走）。新增 `CombatDisabled` 持久标志与光环层取或；
   repair/变形新副本清除它。

修复后 ebde2082：飞轮充能帧 80 与冷却曲线（5000→1950）逐帧对齐、Fire Bomb
禁用后不再走冷却；总差异行 2437→2354、可验证 1368→1287。剩余残差为随机
haste/freeze/修理目标分叉（无原生种子）。三份确定性样本（101f6357/
170666bf/084bd840）复跑仍 MATCH。
