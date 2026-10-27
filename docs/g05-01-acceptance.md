# G05-01 验收记录：动态倍率、敌方目标、人物光环与持续冷却缩减

- 日期：2026-10-24 初版；2026-10-25 全量扩展
- Owner / Reviewer：本轮执行者（G05）／待复核
- 基线：`main` 共享工作树，HEAD `6eb032a` 加未提交并发改动
- 状态：**IMPLEMENTED**（G05-01 四种子 + 2026-10-25 全量 786 模板；映射与回放值级校准通过，逐事件 comparator／Gate C 未运行，不能标 VERIFIED）
- 范围：初版四种子 Passive Power `22e88b23…`、Rate Limiter `b607f75b…`、Force Field `85f59c88…`、Spiky Shield `8ec1c3f9…`；2026-10-25 扩展为 `docs/native-rule-task-index.csv` 中 `primary_group=G05` 的 **786/786** 个模板。
- 约束：本轮未提交、未部署。

## 0. 2026-10-25 复核：关于“7 个 G05 失败”

- 该结论来自 `tools/actionaxis_plugin/build/g07_green_full.txt`（2026-10-24 22:30）的中间态日志。实际是 **6 条 G05 + 1 条 `CardMapper`**，不是 7 条全 G05。
- 22:30 的 G05 断言根因：
  1. `DynamicHeroAuraTogglesWithoutStacking` 的 `11950 vs 11900`：当时 `Effects.ApplyDamage` 尚未应用 `InternalHero.AuraPercentDamageReduction`，所以 50% 人物减伤没有生效；22:36 已补上。
  2. Spiky `5 vs 0`：旧测试直接向 `config.PlayerSkills` 注入 `CardDef`，该中间态没有把光环纳入运行时集合；不是引擎把 5% 扣了两次。
  3. Spiky `1001 vs 962`：旧测试只使用 Silver 模板 `ShieldApplyAmount=40`，没有把回放实时值 79 写入；962 = 922 + 40。现已使用真实 `SkillMapper` 路径并写入 79。
- 当前复核：
  - 删除 `build/EngineTests.exe` 后重新执行 `bash tools/actionaxis_plugin/compile_tests.sh`，退出码 0，`PASS G05DynamicAura`／`OK`（日志 `tmp_g05/clean_build_rerun.txt`）。
  - 单独编译当前 engine + `G05DynamicAuraTests` 的隔离 harness 也输出 `PASS G05DynamicAura`／`OK`。
  - 22:39、22:47、00:03 的整包复跑均为 `PASS G05DynamicAura`／`OK`；22:30 的 7 项已不再是当前阻塞。
- 若其他分支仍复现这 7 项，优先核对是否缺少 `Effects.cs` 的 `AuraPercentDamageReduction` 减法，以及是否仍在用旧测试注入方式；不要通过放宽断言来消除失败。

## 1. 原生证据

- 数据源 `GameData.db.zip` SHA256：`FDFD3087E6A23EDA2C9F53BE68B012006CC949D03E8666DD51AD65ABC71B61E5`
- 种子模板：`tools/actionaxis_plugin/tests/fixtures/native_g05_seeds.json` SHA256 `45C05F4A70B92F007A67B9F8A86E466057F5AC70A1BB930519A7609037CF1E9D`
- 回放校准值：`tools/actionaxis_plugin/tests/fixtures/native_g05_replay_calibration.json` SHA256 `14065A47D0E7619A18C0C61EB69B2A51435BA0E720E27BD883F1DBE39B35A8C3`

| 回放 | SHA256 | 关键观察 |
| --- | --- | --- |
| `ea043fb8292c4faaa76262aebeb60c39` | `3813cd2e7902ea96c937fa67b0ec07fee721b38721ce549b9f0e971f07cf7b22` | Force Field Gold：6.45s `DamageAmount=553=base 11+Shield 542`；6.5s `425=11+414`。6.4s 被排除，因为第二笔护盾在光环刷新后才到账。 |
| `bb30d9e9d76c426b89d28e4fc9ea35d3` | `698fba023f6973967a31ef894ade108e5c2b50836cc48674aacdcdae64fb5ac7` | Spiky Shield：开场 `CooldownMax=5699`、`PercentCooldownReduction=5`；4.0s `6000→5699`；4.85s 就绪、4.9s 出手。4.0s 另有独立 50ms 直接冷却效果，不能把 `2000→1649` 全归因于 5%。 |
| `bce53fafda924b60b6ade61877e37255` | `35f483e6c3dce2c9d7949968019049c97741592ee0b345326f9c439f66bf5653` | 含 Passive Power，但在 Stash 且无光环事件；不作为倍率证明。 |
| Rate Limiter | — | 315 份本地 BPP 回放中未找到该模板。 |

以上是回放导出值与模板值，不是完整开场棋盘重建，也不是逐事件一致结论。本机回放 Player 开场为空、缺少完整 opening sidecar，无法把整张棋盘注入 `BattleConfig`。

## 2. 五步完成内容

1. **类型识别**：`TAuraActionCardModifyAttribute` 的 `Multiply` 允许 `TReferenceValueCardAttribute` 等运行时引用；固定零倍率仍拒绝。`TAuraActionPlayerModifyAttribute` 新增 `AdditiveMultiply` 与 `PercentDamageReduction` 校验；未知类型／属性／目标继续抛异常。
2. **目标／主体**：动态与静态光环复用 `SectionSupported` / `SectionSideMatches`，支持 `SelfHand`、`SelfBoard`、`SelfHandAndStash`、`SelfStash`、`OpponentHand`、`OpponentBoard`、`OpponentHandAndStash`、`OpponentStash`、`AllHands`；Rate Limiter 的 `OpponentBoard` 已进入运行时目标集。人物光环支持 Self/Opponent，目标条件仍 fail-closed。
3. **结算**：`NativeDynamicAuras.ApplyHeroAuras` 新增可撤销人物派生层；记录 `InternalHero.AuraMaxHp`、`AuraRegen`、`AuraRageMax`、`AuraEnragedDurationMs`、`AuraPercentDamageReduction`，每次 Refresh 从“当前值 − 已计入光环”重算。`Effects.ApplyDamage` 对非自伤应用人物百分比减伤。持续 CDR 仍只走 `AuraCooldownFlat/Percent`，不写回 `LiveAttributes`。
4. **排空**：保留 `Active()` 每次判定条件；光环来源 `Destroyed` 后不再进入 `All()`。`Initialize` 的实时反推只处理卡牌光环，避免把人物光环误当卡牌属性剥离。
5. **测试**：新增 `G05DynamicAuraTests` 并在 `Program.cs` 注册；真实模板夹具不使用裁掉前提的简化卡。

## 3. 改动文件

- `tools/actionaxis_plugin/engine/NativeDynamicAuras.cs`
- `tools/actionaxis_plugin/engine/NativeCardAbilityMapper.cs`
- `tools/actionaxis_plugin/engine/SkillMapper.cs`
- `tools/actionaxis_plugin/engine/Effects.cs`
- `tools/actionaxis_plugin/engine/BattleContext.cs`
- `tools/actionaxis_plugin/tests/G05DynamicAuraTests.cs`
- `tools/actionaxis_plugin/tests/Program.cs`
- `tools/actionaxis_plugin/tests/fixtures/native_g05_seeds.json`
- `tools/actionaxis_plugin/tests/fixtures/native_g05_replay_calibration.json`
- `docs/native-rule-coverage.md`（§4.3、§6.3、§7.2 G05 与日志）

共享工作树还含其它组未提交改动，未纳入本单。

## 4. 新增测试

- `PassivePowerMultipliesZeroCooldownCardsWithoutCompounding`：Weapon 被动 20→25，Burn 4→5，1s 主动武器保持 20；`CooldownMax` 运行时进出 0 五次仍为 25/20，不累乘；实时快照 25 先反推 20 再只应用一次。
- `RateLimiterMultipliesBothBoardsAndDrivesCharge`：自身 Board 4000→2000，敌方 Board 2000→1000；实际充能读取 2s 而非 4s。
- `ForceFieldAddsCurrentShieldWithoutCompounding`：实时 553 反推 base 11；护盾 414→Damage 425；Gold 模板 5.0s 出手，先加 60 盾后以 613 伤害结算。
- `SpikyShieldLiveSnapshotIsNotCountedTwice`：实时 5699＋5% 保持 5699；5.7s 首次出手；922 盾＋79 盾后伤害 1001。
- `DynamicHeroAuraTogglesWithoutStacking`：Forest Cloak 静态 MaxHp 20% 为 12000；暴怒 50% 减伤只作用一次，4 次开关仍 12000/11950。
- `DynamicHeroHealthMaxTogglesWithoutStacking`：动态 HealthMax +1000 开关 4 次仍 1000/2000。
- `CooldownAuraChangesActualActivationTime`：4s 卡在 50% CDR 下首次 2.0s、第二次 4.0s；HP 条件开关六次，`CooldownMax`/剩余进度回到 4/3。

## 5. 验证结果

| 命令 | 结果 |
| --- | --- |
| `bash tools/actionaxis_plugin/compile_tests.sh` | 退出码 0，全部套件 `OK`，`PASS G05DynamicAura` |
| `npm.cmd --prefix d:\巴扎plus test -- --run` | 退出码 0，37 文件／102 测试通过 |
| `bash tools/build_actionaxis_plugin.sh --compile-only` | 退出码 0，仅既有 Unity `CS0618` 警告 |
| `git diff --check` | 退出码 0（仅 CRLF 提示） |

构建产物（本机，未部署）：

- `ActionAxisEngine.dll` SHA256 `69A6A3A3C4CD7E608A27F9FA56393EDB9C109B8C90D606A80BC34AACDAB3EE95`（22:47 shared build）
- `ActionAxisPlugin.dll` SHA256 `F1EFF68A426D118038675BBF19A8E9406E78D79ADD6167C0D96516C36389C958`（22:47 shared build）

矩阵（`build/native_mapping_report.tsv`，387,678 行，SHA256 `9A4677179EC13E4A7EE84844B0D95AC3AC54849EDCF86C258E972212581C9057`）：

- item mapped/unsupported：338414 / 34324
- skill simulated/live-applied/inactive/out-of-combat/unsupported：8142 / 740 / 4510 / 384 / 1164
- Passive Power：18 simulated / 12 inactive（稀疏品质）
- Rate Limiter：12 simulated / 18 inactive
- Force Field：336 mapped
- Spiky Shield：420 mapped

相对本单前报告，Passive Power／Rate Limiter 的 declared 行由 `unsupported` 变为 `simulated`。报告 schema 与历史 126,772 行不同，不能直接比较总通过率。

## 6. 原生对账边界

- 仅值级校准，不是逐事件对账；matched/missing/mismatch 与 UNKNOWN 数量未计算，Diff 结论为 `UNKNOWN`。
- Force Field 的 Add 语义、Spiky Shield 的有效上限与出手帧已用上列回放值核对。
- 未把“映射通过”写成“原生一致”。

## 7. 剩余风险与阻塞

- Rate Limiter 无本地回放；其“目标为整侧 Board”与 tooltip“最右物品”的差异仍按模板数据实现，语义需下一条真实回放确认。
- Passive Power 的引用倍率只证明模板与可逆层；回放没有可观察出手/光环事件。固定零倍率已拒绝；动态引用值若运行时变成 0/负数，当前没有单独的 fail-closed 分支，需在获得此类数据后补测试。
- 持续 CDR 的 Flat+Percent 组合、100% 缩减反推与取整仍未被原生证明；Spiky 回放只覆盖 5% 百分比与一段已在进行中的冷却。
- 动态人物光环的实时快照若在开场已含同一光环，当前只从人物当前值反推已计入量，未像卡牌一样有独立 seed 表；需要真实含 Forest Cloak 的开场回放验证。
- `PercentDamageReduction` 目前对非自伤直伤与 DoT 都按百分比缩减；原生是否只对直伤生效未知，交 G07/G10 复核。
- 共享工作树仍含其它组未提交改动；本单不能代表整包 Gate A/C 已全部通过。

## 8. 下一条可执行动作

G12 把开场 `Cooldown`、护盾、Haste 与 Player 侧卡表注入重建器后，优先对 `bb30…` 的 4.0/4.85/4.9 帧和 `ea043…` 的 6.4/6.45 帧做逐事件比较；再寻找含 Rate Limiter／Passive Power 可观察光环的本地回放。

## 9. 2026-10-25 全量扩展：786 个 G05 模板

### 9.1 结论

- 全量矩阵：`primary_group=G05` 的 **786** 个模板，**786 fully ok / 0 partial / 0 blocked**；G05 相关报告行 165,672。
- 剩余 `inactive`：2,682 行为“该品质没有原生技能效果”，52 行为“当前棋盘不满足光环条件”；无 `unsupported`。
- 本单仍为 **IMPLEMENTED**，不是 VERIFIED：没有逐事件 comparator／Gate C，也没有游戏内 Gate D。
- 数据文件：`tools/actionaxis_plugin/tests/fixtures/native_g05_full.json`（39 个完整模板，SHA256 `2159F95A2E8E4370A4496FBC980399756B77EA335793CF171729B7D041684DAF`）。

### 9.2 本轮新增实现

| 规则族 | 实现 |
| --- | --- |
| 实时人物属性 | `BattleConfig.PlayerAttributes/EnemyAttributes` → `InternalHero.Attributes`；`ApplyHeroAttributes` 复制全部原生属性，不再只取 Rage/Regen；`PlayerAttributeValueSupported` 支持 `Custom_*`、`Level`、`CritChance`、`RerollCostModifier`、`Income/Experience/Prestige` 等。 |
| 人物派生层 | 新增 `InternalHero.AuraCritChance`；`NativeDynamicAuras.Refresh` 从“当前值 − 已计入光环”重算 MaxHp/Regen/RageMax/EnragedDuration/减伤/暴击；`TickEnrage` 结束时立即 Refresh，光环不延迟一帧撤销。 |
| 自引用与数值 | 自引用拒绝收窄为“值读取来源且光环目标包含来源”；Santoku 读自身写左卡、Giant Sub 读其他 Food、Fire Claw 读其他卡 Burn、Subscraper 读自身售价均放行。动态光环值修正确认 `Subtract`，Shuriken 的 `Ammo-1` → Multicast 生效。 |
| 技能/区域 | `AbsolutePlayerSkills` 进入 `SectionSupported`、前提计数与运行时目标集（Ritual Dagger）；`TTargetPlayer Both` 映射为 `bothHeroes`，效果层对双英雄展开（Nitro 双向 Burn）。 |
| 玩家技能光环 | 纯模板棋盘现场结算静态人物/卡牌光环；实时快照已含时保持 `live-applied`，不重复叠加；经济类 `Gold/Income/Experience/Prestige/RerollCostModifier/BuyPrice` 标记 `out-of-combat` 并跳过；同时含静态+动态光环的技能不再整卡拒绝（First Strike/Inexorable）。 |
| 数据缺口处理 | 缺失 `AuraId`（Buddy System/Fairy Haze/Quick Defenses 的 1/2）跳过缺失项、保留其余战斗光环并告警；Nanobot Orange 基础模板缺 `DamageAmount` 时，由自身光环先补原生缺省 0。两项仍登记为 G00 DATA，但不再阻塞 G05。 |

### 9.3 真实回放值级校准

- 语料：`D:\巴扎plus\CombatReplays` 共 200 份 `.mpack.gz`，196 份含 G05 种子；完整证据归档到 [g05-02-replay-calibration.md](g05-02-replay-calibration.md)。
- 方法：`captures/diagnostics/read_bpp_replay.py` + `tools/native_replay_ledger.py --no-snapshot`；数值来自 `combat.healthChanges`、人物属性更新与 `combat.cardUpdates`（50 ms 帧）。事件本身（code 5/7/action 10）不带数值。
- 代表性校准：

| 模板 | 回放 | 观察值 | 校准规则 |
| --- | --- | --- | --- |
| Heavy Mettle | `ab8439ff…` | Lv13 Silver：20 → 46（两次） | 物品 Shield += 2 × Level |
| Fire Claw | `a94eda88…` | 550 = 10 + 100% × (534 + 6) | 基础 + Custom_0% × 其他卡 Burn |
| Healthy Lifestyle | `4ce4c775…` / `0ce4c330…` | 30 = 6×5；36 = 6×6 | Gold：每个非 Weapon +6 Regen |
| Waters of Infinity | `ab8439ff…` / `c61d5b0a…` / `67af77d9…` | 50 / 70 / 30；HRegen 111/180/68 | Silver：10 × 非 Weapon 数 |
| Giant Sub | `a5726eb4…` | 62/4/4/4 与 158/10/10/10 按当前值结算 | 引用型属性持续重读并原样施加 |
| Nitro | `286fbe9e…` | 双方 CombatantId 0/1 各 Burn 4 | BurnApplyAmount 对称作用于双方 |
| Cash Cannon | `a9dc7a52…` | C3=167 → DamageAmount 334，实际造成 334 | 经济引用 ×2 |
| Oinkment | `a9dc7a52…` | HealAmount 167 → 实际治疗 167 | 经济引用按预计算值，不读实时 Gold |
| Philosopher's Stone | `d5feb78a…` | 26 → 54（+4/周期），t=4.55 施加 +26 | 每次激活 +1，转化 Reagent 再 +3 |
| Iron Sharpens Iron | `921ccdd8…` | Marlon 75 = 15 + 3×20 | 最左 Weapon += Custom_0 × 武器计数（Custom_4） |

- 边界：`--no-snapshot` 丢弃 recorder 侧开场，所有卡片证据来自对手棋盘；11 个关注模板无回放；4 份回放不含 G05 种子。Iron Sharpens Iron 回放卡面 `Custom_0=3` 与当前 Gold 模板值 4 有 1 点差异，本轮只按公式实现，不把该样本当作精确 tier 证据。Cash Cannon 的 `+50` 基底与 `a94eda88` 的 Δ42 未解释。以上均为值级校准，不是逐事件一致。

### 9.4 测试

- `bash tools/actionaxis_plugin/compile_tests.sh` 输出 `PASS G05DynamicAura`、`PASS G05FullAura`、`OK`（04:30 及 G05 隔离 harness）。
- `G05FullAuraTests` 覆盖 18 个场景，均断言实际属性/事件/时间，不只断言“不抛异常”：
  - `CashCannonReadsPlayerCustom3`：C3=167 → Damage 334；
  - `HeavyMettleReadsPlayerLevel`：Lv13 → ShieldApply 26；
  - `WildStrengthAddsHeroCrit`：人物暴击 +5；
  - `PlayerShieldConditionMovesCooldown`：有盾 2s/无盾 4s；
  - `RitualDaggerCountsAbsolutePlayerSkills`：有技能 7s/无技能 8s；
  - `HealthyLifestyleCountsNonWeaponCards`：6×5 → Regen 30；
  - `WatersOfInfinityCountsNonWeaponCards`：10×3 → Regen 30；
  - `FireClawAggregatesOtherBurn`：10+534+6 → Burn 550；
  - `NanobotOrangeBuildsDamageFromOwnAura`：15×4 Friend → Damage 60；
  - `GiantSubCopiesOtherFoodValues`：Shield/Regen/Burn 62/4/4；
  - `ShurikenTurnsAmmoIntoMulticast`：Ammo 3 → 额外 2 次；
  - `SantokuCopiesCritToLeftCard`：自身 Crit 10 → 左卡 Crit 10；
  - `GrowthSpurtScalesPlayerMaxHp`：1000 → 1100；
  - `SwoleUsesCardDamageAggregate`：手牌 Damage 20+30 → MaxHp 1050；
  - `IronSharpensIronUsesPlayerCustom4`：15 + 20×4 → Damage 95；
  - `EconomyAurasAreOutOfCombat`：Arbitrage/Open for Business 不进 `PlayerSkills`；
  - `MixedStaticDynamicSkillsMap`：First Strike/Inexorable 为 `simulated` 且有运行时光环；
  - `NitroBurnsBothPlayers`：双方都收到 Burn 事件。
- `G05DynamicAuraTests` 原有的倍率可逆、实时快照反推、人物光环开关、持续 CDR 时序断言继续通过。

### 9.5 验收命令与结果

| 命令 | 结果 |
| --- | --- |
| `bash tools/actionaxis_plugin/compile_tests.sh` | 04:30 整包 `OK`（39 套件）；G05 两套 `PASS` |
| `npm.cmd --prefix d:\巴扎plus test -- --run` | 退出码 0，37 文件／102 测试通过 |
| `bash tools/build_actionaxis_plugin.sh --compile-only` | 退出码 0，仅既有 Unity `CS0618` 警告 |
| `EngineTests.exe --coverage … native_mapping_report.tsv` | 退出码 0，387,678 行 |
| `python tools/rule_inventory.py --mapping-report …` | item unsupported 1,344；skill unsupported 54（全库，G05 为 0） |
| `git diff --check` | 退出码 0（仅 CRLF 提示） |

构建产物（本机，未部署；并发组可能再次覆盖）：

- `ActionAxisEngine.dll` SHA256 `C6ECE5E9CBCE1AAE54C6E5ABE692F124D4145A51E4E86BF5E3AE15515711797C`
- `ActionAxisPlugin.dll` SHA256 `42F0A412942D0463351D2F61B3FD6FCE7CDBFE992C2151885AC773D77823FE6E`
- `EngineTests.exe` SHA256 `C3A2D9AB5E33BE0FE705C6BC6D374E60ECDCDCA94EA14CD983A609AE0D097A24`

矩阵哈希：

- `native_mapping_report.tsv`：`A703634EFA956EC1DCD54D2BA9FAC61936619E4B68B846DA8375A4F144D1A1BE`（04:31 共享树复跑后；本单快照 `tmp_g05/native_mapping_report_g05_final.tsv` 同哈希，G05 仍 786/0/0）
- `native_mapping_templates.jsonl`：`ED9EBC2695A4FF242F1DEFAC161FCA145C3E4FEDD8FCC75D8D34236C140E4494`
- 说明：`build/native_mapping_report.tsv` 是共享产物，其他组复跑会改写哈希；上表为本单最后一次生成/校验的版本，G05 计数在 04:30 与 04:31 两版均为 786/0/0。

### 9.6 共享工作树并发状态

- 04:30 整包为 `OK`。此后共享树中的其他组继续改动测试；最终复跑 G05 两套仍 `PASS`，整包出现 2 条非 G05 断言失败：
  - `G08TransformTests.GreenScreenCopiesLeftMediumWithEnchant:318`：期望 `3172cda6…`，实际 `62c10400…`；属 G08 在途改动。
  - `NativeRuntimeRulesTests.AllG04PrimaryTemplatesSmokeRuntime:1171`：`Friend of the Sea (Skill)` 为 `live-applied` 但不在 `ctx.Skills`；与既有 `SkillsTests` 对 `live-applied` 不进入 `PlayerSkills` 的断言冲突，属 G04 新增冒烟断言。
- 本单未修改、未回退他组测试或共享文件；不能把整包结果全部归因于 G05。

### 9.7 剩余风险与阻塞

- **Gate C 未运行**：没有逐事件 comparator；matched/missing/mismatch 与 UNKNOWN 未计算，Diff 结论仍是 `UNKNOWN`。Gate D 未执行（未获部署授权）。
- **DATA**：Buddy System／Fairy Haze／Quick Defenses 的 `AuraId 1/2` 定义缺失；Nanobot Orange 基础模板缺 `DamageAmount`。当前已按“保留其余部分 + 可见告警”处理，解除需 G00 在新 DB 中补定义。
- **回放覆盖**：Monocle、Lifting、Wild Strength、Growth Spurt、Swole、Venomous Vitality、Shuriken、Santoku、Subscraper、First Strike、Grill 在 200 份样本中未出现；无 Ammo→Multicast 证据。
- **未解释值**：Cash Cannon `+50` 基底与 a94 `Δ42`；Refrigerator 成长驱动；Giant Sub “最高 Food”身份；Waters 非 Weapon 计数未直接序列化。
- 共享工作树仍含其它组未提交改动；未提交、未部署。

### 9.8 本轮文件

- `tools/actionaxis_plugin/engine/Types.cs`
- `tools/actionaxis_plugin/engine/BattleContext.cs`
- `tools/actionaxis_plugin/engine/Battle.cs`
- `tools/actionaxis_plugin/engine/MonsterOpponentMapper.cs`
- `tools/actionaxis_plugin/engine/SkillMapper.cs`
- `tools/actionaxis_plugin/engine/NativeDynamicAuras.cs`
- `tools/actionaxis_plugin/engine/Effects.cs`
- `tools/actionaxis_plugin/engine/NativeCardAbilityMapper.cs`
- `tools/actionaxis_plugin/engine/LiveBoard.cs`
- `tools/actionaxis_plugin/tests/G05FullAuraTests.cs`
- `tools/actionaxis_plugin/tests/G05DynamicAuraTests.cs`
- `tools/actionaxis_plugin/tests/Program.cs`
- `tools/actionaxis_plugin/tests/NativeCoverageRunner.cs`
- `tools/actionaxis_plugin/tests/fixtures/native_g05_full.json`
- `tools/actionaxis_plugin/tests/NativeRuleExpansionTests.cs`、`NativeRuntimeRulesTests.cs`（仅把因 G05 新支持而失效的 fail-closed 断言改为真正未知值/区域）
- `docs/g05-01-acceptance.md`、`docs/g05-02-replay-calibration.md`、`docs/native-rule-coverage.md`
