# 大巴扎行动轴原生规则全覆盖 · 多人协作工作文档

> 本文件是范围、任务归属、验收结果和交接进度的**唯一事实来源**。生成清单、CSV、测试与回放是证据附件，不另设竞争性的完成度看板。
>
> 最终目标：覆盖本轮范围内所有物品／技能／附魔的主动、被动、光环和组合效果，包含双方阵营与场上变形；通过逐事件回放比对证明所验规则与原生一致。全生命周期的商店／遭遇／升级／赛季规则是后续范围，不冒称本轮已完成。
>
> 当前结论：**部分实现，尚未全覆盖，也未完成原生一致性验收。** 下方历史勾选和“全部映射”不等于本次验收通过；遇冲突，以本文件当前基线、任务契约和已归档证据为准。

导航：[状态口径](#状态口径) · [五步流程](#五步流程) · [协作分组](#协作分组) · [验收规范](#验收规范) · [进度日志](#进度日志)

- 建立／本次整理日期：2026-10-24
- 核查基线：HEAD `6eb032a0eb65814dd42190fbf6b8941d6e42a621` **加当前未提交工作区**；源码插件版本 `1.5.7`。HEAD 单独不包含本文描述的全部修复。集成人须归档补丁／提交和源码哈希后冻结正式验收基线；源码版本不代表游戏实际加载版本。
- 数据源 SHA256：`GameData.db.zip = FDFD3087E6A23EDA2C9F53BE68B012006CC949D03E8666DD51AD65ABC71B61E5`。
- G00 冻结基线（2026-10-25）：`data/g00_baseline.json = 0731AAC460691B3419A3D840B1BC56FFE245C4134747A89836915AB2D6E2C513`；`native_mapping_report.tsv = 8F8BD975002195D1B5A58DBD7AF045566E9F553BA803D10334AD2AB8D3DB93F7`（387678 行）；`native_monster_report.tsv = 76C8D5D92C7454B05C835C80E651392D6E7BF4F1A64A18275AFB7CBCFCA6F240`；`native-rule-task-index.csv = B72D84F67530AC82FD4C4B1E5B8D4DE9593FDA35D1E9F52004D046E6F7BC23F3`。`python tools/g00_audit.py --check` 复核通过；历史报告 `68C6D4E7…` 仅保留为旧批次记录。
- 规则来源：`<GameDir>/TheBazaar_Data/StreamingAssets/GameData.db.zip`（游戏 5.0 原生模板）
- 覆盖基线：[docs/native-rule-inventory.md](native-rule-inventory.md)（由脚本生成，勿手改）
- 刷新类型清单：`python tools/rule_inventory.py --markdown docs/native-rule-inventory.md`；**不要把 `--markdown` 指向本工作文档**，该选项会覆盖输出文件。
- 单条规则样例：`python tools/rule_inventory.py --sample TActionCardReload`

---

<a id="状态口径"></a>
## 1. 总览、目标与判定口径

### 1.1 三层证据，不能混算完成率

| 层级 | 能证明什么 | 不能证明什么 |
| --- | --- | --- |
| 类型清点 | 数据中出现哪些 `$type`，源码中是否出现同名字符串 | 参数组合、调用链和效果是否正确 |
| 实际映射＋行为测试 | 某完整模板／品质／附魔能进入引擎，给定场景按断言执行 | 所有组合都支持、原生帧序或随机序列完全一致 |
| 原生逐事件对账 | 指定回放及关键路径的时间、实例、目标、数值、状态与原生相符 | 单份回放覆盖了所有卡牌 |

覆盖表中的 ✅ 仅表示已有实现与测试证据，**不是原生验收通过**。任务最终状态使用 §7 的 `VERIFIED`，必须关联 §8 的全部门槛。

| 标识 | 含义 |
| --- | --- |
| ✅ 已实现／待对账 | 限于行内所述组合；已有测试，尚不能声称全覆盖 |
| 🟡 部分 | 参数／场景只支持子集；需列出已测与拒绝分支 |
| ⛔ IMPLEMENTATION | 有数据但尚未实现，不能写成“做不了” |
| ⛔ DATA | 缺已验证必需数据；必须带缺失字段、查证证据及解除条件 |
| ➖ OUT | 本轮明确排除；按具体触发场景判定，不按卡名或动作前缀整类排除 |

### 1.2 已实现的基础与仍需交付的部分

- 基础伤害／治疗／护盾／DoT、常用使用与属性变化触发、运行时数值、部分目标／前提／标签、怒气、生命周期均有实现；不是每种属性、时长与参数都已接入实际效果。
- 已修复：打印机／镜子真实前提拒绝、充能成长仍按开场值、销毁后排队出手、怪物怒气漏接、复制槽位挤压、最终棋盘覆盖历史。对应 `AuditRegressionTests`，原生继承语义仍待对账。
- 已修复：武装核心左右全体＋自身、相邻全体成长、Golden 复制受售价阻断、稀疏品质的暴击条件读取；对应 `PositionalRuleTests`。
- 已补并测试：G03 主责 45 个模板全部纳入全量入口与机制族回归；Hunting Hawk 等种子的指定来源／受体、次数／前提限制、使用前同步与普通被动下一帧均有断言；仅剩 4 个模板缺原生能力定义（Fortune Cookie／Sulphur／Shard of Obsidian／Wild Quillback，DATA/G00/G11）与 Resonance Crystal 缺场外光环 0（OUT+DATA/G00/G11）；Machine Learning／Renovation 的 CardSelected 场外分支已按 OUT 跳过。G01 已补 `AllHands`、`Absolute*` 物品区域、位置与 `XMost` 在主动／被动／光环／数值引用中的同一目标集，并有 `G01TargetConsistencyTests`；2026-10-25 续作已把 G01 主责模板 `unsupported` 清零，新增 `TTargetCardOccupying`／`TCardSocketEffect` 共享契约（`G01SocketEffectTests`）、主动 `Effect.Priority` 结算（Weaponized Core 回放首击 190@3.05s）与 `EnchantmentMapper` 目标复用（`G01FullRulesTests`）。玩家实时槽位仍依赖 BPP 快照已烘焙的 `Heated/Chilled`，Trojan／Wild Quillback 仍缺 AbilityId 定义（DATA）。G05-01 已补动态引用倍率、敌方 Board/Hand 目标、带人物前提的人物派生层与持续冷却校准；2026-10-25 续作已把 `primary_group=G05` 的 786 个模板做到 786/786 无 unsupported／partial（人物属性快照、自引用收窄、Subtract、AbsolutePlayerSkills、Both、经济光环 OUT、混合静态+动态、缺失 AuraId 跳过），详见 [g05-01-acceptance.md](g05-01-acceptance.md)。逐事件对账仍由 G12 处理；代码主动拒绝属于实现缺口，不是数据不存在。
- 现存风险：静态实时属性反推、主动 `Effect.Priority` 已接入但触发器／同帧跨卡顺序仍需回放校准、随机取样序列、部分未知字段的缺省值、复活次数限制、复制继承与 `EnemyHero` 完整性。不得在未验证时给“全部无问题”的结论。
- 逐事件对账已补账本、模拟账本、比较器与全量批跑；开场冷却已按真实快照注入（全量非满冷却 0）。521 份回放中 145 份可比较、376 份因缺侧车/旧模板阻塞，145 份仍为 DIFF。按原生随机事件拆分：仅 2 份纯确定性（仍有 30 missing／9 extra／350 value），143 份含随机分支（402265 条 unverified 单列）；不能把随机分支或阻塞数据算成通过，也不能据此称 G02 已对账通过。

**硬性要求：**

1. 不允许「猜」规则。缺少定义、品质不匹配、附魔缺失时必须保留原值并警告。
2. 不允许为了消除警告而擅自提升品质或补造数值。
3. 新规则先写失败测试，再实现；测试必须断言**事件时间和数值**，不能只断言「不抛异常」。
4. 每次改动跑：
   - `bash tools/actionaxis_plugin/compile_tests.sh`
   - `npm.cmd --prefix d:\巴扎plus test -- --run`
   - `bash tools/build_actionaxis_plugin.sh --compile-only`

---

## 2. 数据规模（2026-10-24 扫描）

| 来源 | 模板数 | 说明 |
| --- | --- | --- |
| 历史清单 items 桶 | 2211 | `classify_table` 将 cards 表内其他模板也归此桶；**不是实际物品数量** |
| 实际 `TCardItem` | 1146 | 原生物品模板；品质与附魔是嵌套定义，不重复计物品 |
| 实际 `TCardSkill` | 498 | 战斗内外技能 |
| monsters | 166 | 三选一怪物与首领 |
| other | 127 | 挑战、赛季、升级等，不参与战斗 |

附魔定义嵌在物品模板的 `Enchantments` 节点里，不单独计模板数；清单中「附魔」列就是这些嵌套规则的计数。

共 **349** 个 `$type` 组合。高频规则：

- 触发器：`TTriggerOnCardFired` 7560、`TTriggerOnItemUsed` 1417、`TTriggerOnCardSelected` 625、`TTriggerOnCardSold` 583
- 动作：`TActionCardModifyAttribute` 1884、`TActionCardHaste` 1059、`TActionCardFreeze` 1035、`TActionCardSlow` 1031、`TActionPlayer*Apply` 各 800+
- 光环：`TAuraActionCardModifyAttribute` 8780、`TAuraActionPlayerModifyAttribute` 62
- 条件：`TCardConditionalAttribute` 3360、`TCardConditionalTag` 1798、`TCardConditionalAnd` 1083、`TCardConditionalHiddenTag` 967

完整的类型×来源矩阵见 [native-rule-inventory.md](native-rule-inventory.md)。

阶段 6b 后的历史字符串扫描计数：触发器 13、动作 14、光环动作 0、前提 1、条件 3、数值 0、时长 0、目标 1。**这只表示类型名是否出现，不代表所有规则及参数组合已经实现或验证。**

### 当前范围与未完成项（按用户最新要求更正）

- **场上道具变形**：本轮修复真实打印机／镜子的左侧前提入口、按占格原位放置副本、使旧形态排队施放失效，并保留出生／变形／离场历史供行动轴查询。打印机／镜子可将来源附魔应用到有对应定义的未附魔供体副本；已有附魔保留。模板测试已通过，复制临时属性、附魔冲突与新实例冷却的原生逐事件一致性仍待校准。
- **持续冷却缩减动态光环必须支持**：已实现，有效冷却 =（CooldownMax − 固定缩减）×（1 − 百分比缩减）；实时快照已含缩减时会先反推基础值，避免重复扣减。公式与取整仍需回放校准。
- **审查修复**：控制成长、排队施放、双方属性、变形历史与人物引用光环已补回归。
- **武装核心位置规则**：`AllLeftCards/AllRightCards + IncludeOrigin` 已同时接入效果、数值引用、触发主体、前提及光环；武装核心基础主动／被动通过银／金／钻／传说、双方阵营测试。包含自身的相邻成长不再随机缩为一张。
- **最新实际映射矩阵（不是字符串扫描）**：1146 个物品、498 个技能，枚举模板声明的品质／附魔并增加物品 Legendary→Diamond 分支、双方阵营，共 387678 个入口组合。**物品入口阻断已清零**；仅 2 个技能模板（`Aggressive Mutations`／`Virulent Research`，空 `TTriggerOnCardFired`）仍 unsupported，归 DATA。46 个物品模板因 56 个缺 `AbilityIds/AuraIds` 定义中的部分引用而标 `mapped-warning`（12846 行实时属性告警），其余战斗部分保留、缺项跳过并告警；附魔告警行为 0。另有 166 个怪物 `Player/Hand/Items/Skills` 读取矩阵：978 个手牌／262 个技能，16 个手牌模板缺失、4 个手牌品质稀疏、62 个技能品质稀疏；3 条 `TCardSocketEffect` 已在读取时应用（Heated／Chilled），不再泛化为“未模拟”。通过映射也不代表所有语义或回放已验证。
- **逐事件回放对账**：账本、比较器、全量批跑已实现（`tools/native_replay_ledger.py`、`tools/native_replay_compare.py`、`tools/native_replay_batch.py`、`EngineTests.exe --ledger/--ledger-batch`）。521 份回放全量扫描：145 份可比较且全部 DIFF，376 份 DATA 阻塞；29 次变形、3699 次沙暴、10880 次随机目标、0 次 `EffectTriggered`。145 份中仅 2 份无随机目标，确定性残余差异见 `deterministic_residual.json`；随机/运行时/无对应分支 402265 条 unverified 单列。`captures/diagnostics/g12/` 记录计数、未知项与复跑命令。
- **沙暴期间纯减伤等与本次查看行动轴无关的效果按用户要求排除**；不能据此把所有沙暴触发的出手、变形或控制规则都视为无关。
- `TTargetCardOccupying` 在 BPP 的战斗归因代码中有使用，之前“不参与战斗”的结论缺少依据；是否涉及当前范围需核实具体槽位规则。
- `TCardConditionalPlayerHero` 已接入 BPP 英雄读取，但类型名存在不等于所有缺数据分支和组合语义已通过原生对账。

---

## 3. 当前实现结构

```
GameData.db.zip / 游戏运行时 → LiveCardEntry / LiveSkillEntry / LiveMonsterEntry
  → CardMapper.Build（内部调用 EnchantmentMapper、实时属性覆盖）
  → MonsterOpponentMapper.Apply（怪物棋盘与敌方 SkillMapper）或 TrainingDummy
  → SkillMapper.Apply（我方技能）
  → NativeCardAbilityMapper.Apply（含原生附魔 Ability/Aura）
  → NativeItemMapper.Apply（8 张卡的特殊校准路径，必须复测避免覆盖通用规则）
  → BattleEngine.Simulate
      → NativeDynamicAuras / ItemAttributes / Effects / Triggers / Transforms
      → 事件＋效果统计＋物品历史＋冷却快照
  → Aggregate / BattleDamageSeries / PanelModel / 插件 UI
```

关键文件：

| 文件 | 职责 |
| --- | --- |
| `engine/SkillMapper.cs` | 触发器/动作/目标/数值/条件的**通用**映射；`NativeSkillSupport` 提供条件判断 |
| `engine/NativeCardAbilityMapper.cs` | 物品 Ability/Aura 映射与静态光环结算 |
| `engine/EnchantmentMapper.cs` | 附魔 Ability 映射（附魔光环依赖实时属性，不重复加算） |
| `engine/Effects.cs` | 动作结算与事件发射（`onHaste`/`onFly`/`onReload`…） |
| `engine/Triggers.cs` | 被动触发队列、来源过滤、次数限制、技能触发 |
| `engine/Battle.cs` | 时间轴、冷却、多重、DoT、死亡 |
| `tests/*.cs` | 规则级回归；`tests/fixtures/*.json` 存真实原生模板 |

<a id="五步流程"></a>
### 新增规则的五步标准流程（每一步都须在交接包中有证据）

开工输入：认领任务＋冻结数据库／源码版本＋导出完整模板；先保存能复现失败的最小棋盘与断言。不得删模板的前提、附魔、Priority 来绕开入口错误。

| 步骤 | 必须改／检查 | 输出与检查点 |
| --- | --- | --- |
| 1. 类型识别表 | `NativeSkillSupport.TriggerEvents`、`SkillMapper.MapAction/MapValue`、各校验器 | 列出 `$type`、属性、操作、持续时间、允许值；未知类型／枚举默认抛异常。`supported_tokens()` 只是检索辅助，禁止改它来制造“支持率” |
| 2. 目标／主体解析 | `MapCardTarget/MapHeroTarget/MapSubject`、`CardConditionSupported/CardMatches`、`PrerequisitesSupported/Match` | 同时实现校验与运行时匹配；区分施动者、触发源、受体、相对阵营、包含自身、数量、最近邻与全侧。数值／目标数／时长保留运行时表达式 |
| 3. `Effects.cs` 结算 case | `ResolveEffectCore/EvaluateNum`；必要时 `Battle/ItemAttributes/NativeDynamicAuras/Transforms` | 应用实际状态、发正确事件、统计真实输出；基底／临时／光环分层，不污染共享 `CardDef`。复制新实例在帧边界加入，原有施放代际失效；纯光环不造无意义 effect case，明确其实际结算入口 |
| 4. `Triggers.cs` 排空语义 | `NativeMatches/PlayerMatches/AttributesMatch`、`DrainEvent/DrainNativeFrame`、技能路径 | 验证 Source/Target/Amount/ChainId/Depth 与次数限制；使用前同步，普通联动依原生下一帧，嵌套量值出栈恢复；只按证据变更顺序，不为通过断言清空队列 |
| 5. `tests` 用例与注册 | 真实 `fixtures`＋独立 `*Tests.cs`＋`tests/Program.cs` | 至少正例、零／边界、敌我隔离、未知参数拒绝、完整调用链、指定帧时间和数值；重复两次证明不累乘；新增测试必须被入口执行。复跑 §8、更新覆盖表并在 §9 追加日志 |

### fail-closed 不变量（强制）

- 未知 `$type`、目标／主体／属性操作／必要字段必须抛明确异常；映射边界捕获、回滚、汇总进 `warnings` 并展示。不得当零值、true、相似规则、默认 Self 或就近品质悄悄通过。
- 组合规则中的任一未支持子规则，不能仅丢该子项后标记整卡准确；未知 `Source/Target/Prerequisites` 也不能忽略。
- 仅对已证明“战斗外”的分支按白名单跳过，并记录 OUT；“数据库不存在某类型”不是自动 OUT。
- 现有物品代码保留旧近似卡＋`partial`／警告；技能一般不加入模拟。这是兼容行为而非精确结果。不得新增“就近降级”，任何仍走旧近似的场景均不能过原生验收；发布前必须向用户明确结果不完整。
- 改动校验器后检查所有调用入口：物品、技能、附魔、怪物、八卡校准路径；对失败回滚，检查两侧生命／再生／怒气配置和卡字典都无部分修改泄漏。

---

## 4. 覆盖状态表

### 4.1 触发器

| 原生类型 | 状态 | 说明 / 待办 |
| --- | --- | --- |
| `TTriggerOnFightStarted` | ✅ | 开战 |
| `TTriggerOnItemUsed` | ✅ | 使用物品 |
| `TTriggerOnCardFired` | ✅ | 物品自身主动能力 |
| `TTriggerOr` | ✅ | 组合触发 |
| `TTriggerOnCardPerformedHaste/Slow/Freeze` | ✅ | 控制类被动 |
| `TTriggerOnCardPerformedBurn/Poison/Shield/Heal/Regen` | ✅ | 状态类被动 |
| `TTriggerOnCardCritted` | ✅ | 暴击 |
| `TTriggerOnBeforeItemUsed` | ✅ | 阶段 1 新增；使用前同步结算 |
| `TTriggerOnCardStartedFlying` / `StartsFlying` | ✅ | 阶段 1 新增；`Source` 非空时仍拒绝 |
| `TTriggerOnCardStoppedFlying` | ✅ | 阶段 1 新增；配合 `TActionCardFlyingStop` |
| `TTriggerOnCardPerformedDamage` | ✅ | 阶段 1 新增；复用 `onDamageDealt`（含 DoT） |
| `TTriggerOnCardPerformedReload` | ✅ | 阶段 1 新增；主体为被装填的卡 |
| `TTriggerOnCardPerformedCharge` | ✅ | 阶段 1 新增 `onCharge`；按原生帧延后一帧 |
| `TTriggerOnCardPerformedOverHeal` | ✅ | 阶段 1 新增；含再生溢出 |
| `TTriggerOnPlayerEnraged` / `EnrageEnded` | ✅ | 阶段 3：怒火达到 RageMax（默认 100）进入暴怒，EnragedDurationMax（默认 5000ms）后退出 |
| `TTriggerOnPlayerAttributeChanged` | ✅ | 阶段 3／G07：支持 Health / HealthMax / HealthRegen / Shield / Rage / Burn / Poison 的 Gain / Loss；受伤、治疗、护盾、吸收、怒气、生命上限、燃烧/中毒都会发事件 |
| `TTriggerOnCardAttributeChanged` | ✅ | 阶段 3：支持 Haste/Slow/Freeze/Flying 的 Gain/Loss |
| `TTriggerOnBeforeCardDestroyed` / `CardPerformedDestruction` | ✅ | 阶段 5：被摧毁卡自己的被动同步结算，观察者按帧延后 |
| `TTriggerOnCardDisabled` / `CardRepaired` | ✅ | 阶段 5：禁用/修理事件 |
| `TTriggerOnPlayerDied` | ✅ | 阶段 5／G09：死亡结算前触发；复活次数由每张卡的前提／计数器决定，不再用“每方一次”代替 |
| `TTriggerOnCardSelected` | ➖ | 选牌/商店，非战斗 |
| `TTriggerOnEncounterCardsDealt/Selected/Entered/Exited` | ➖ | 遭遇界面 |
| `TTriggerOnCardSold/Purchased/Upgraded/Enchanted` | ➖ | 战斗外 |
| `TTriggerOnDayStarted/HourStarted/FightEnded` | ➖ | 战斗外 |
| `TTriggerOnSandstorm` | ⛔ IMPLEMENTATION / DATA | 开始触发与伤害数值拆开；现有 DB 未给出伤害／节奏，继续查运行时与回放；纯减伤可 OUT |
| `TTriggerOnCardTransformed` | ✅ | 阶段 6c：场上复制变形后发 `onCardTransformed`，主体为变形后的卡 |
| `TTriggerOnCardQuestCompleted` / `PlayerLeveledUp` / `RunCompleted` | ➖ | 非战斗 |
| `TTriggerOnPlayerDamaged/Shielded/LifeStealed` | ➖ | 数据库计数为 0 的预留类型 |

### 4.2 动作

| 原生类型 | 状态 | 说明 / 待办 |
| --- | --- | --- |
| `TActionPlayerDamage/Heal/ShieldApply/BurnApply/PoisonApply/RegenApply` | ✅ | Self/Opponent 人物目标；数值在结算时重算 |
| `TActionCardHaste/Slow/Freeze/Charge` | 🟡 | 原生时长和目标数在结算时求值，成长／零目标已测；支持的目标、参数组合仍有边界 |
| `TActionCardModifyAttribute` | ✅ | 阶段 3：任意属性 + Add/Subtract/Multiply/Set + 战斗内任意时长 + 目标数量；`Custom_N` 计数器可直接用 |
| `TActionCardFlyingStart` | ✅ | 物品 Ability 映射（含 TriggerTarget/Source） |
| `TActionCardFlyingStop/Toggle` | ✅ | 阶段 1/5 |
| `TActionCardReload` | ✅ | `Value` 为空表示装满 |
| `TActionPlayerBurnRemove` / `PoisonRemove` | ✅ | 空值=清空 |
| `TActionAnd` | 🟡 | 子动作逐项映射，任一不支持则整张卡回退 |
| `TActionPlayerModifyAttribute` | 🟡 | HealthMax/HealthRegen/RageMax/EnragedDurationMax；其余属性（经验等）属非战斗 |
| `TActionPlayerRageApply` | ✅ | 阶段 3：`RageApplyAmount` 默认值；满怒火自动进入暴怒 |
| `TActionPlayerReviveHeal` | ✅ | 阶段 5／G09：复活治疗（默认 `HealAmount`）；Fiery Rebirth 的满血来自 `HealthMax` 光环，复活次数按每卡 `Custom_0` 前提，不再每方一次 |
| `TActionCardForceUse` | ✅ | 阶段 5：立即执行一次使用（不重置冷却、不消耗弹药） |
| `TActionCardDestroy/Disable/Repair` | 🟡 | 排队出手执行前检查摧毁／禁用与形态代际；保留离场历史；G09 补同帧互毁、禁用不出手、摧毁免疫、修理后恢复；触发时序仍需原生对账 |
| `TActionCardAddTagsList/Random/BySource` | ✅ | 阶段 4：战斗内额外标签，不污染共享卡定义 |
| `TActionCardTransform/TransformDestroyed` | 🟡 | 阶段 6c／G09：场上已有道具复制（打印机/镜子/全息/水银/病毒）；`TransformDestroyed` 对 Abducted Cow→Jerky、Drone Crusher→同品质小型 Drone 实现“摧毁前登记、摧毁前被动全部结算后原位变形”，供体池缺失时告警并正常摧毁；场外卡池变形仍拒绝 |
| `TActionCardUpgrade/Enchant/EnchantRandom/EnchantRemove` | 🟡 / OUT | 战斗外交易分支为 OUT；战斗内 `Enchant/EnchantRandom/EnchantRemove` 已按完整模板实现并回归（SelfHand/OpponentHand/Neighbor、PreventOverride、移除恢复基底、附魔主动/被动/光环）；`XMost`／`AbsoluteHand` 目标未逐卡行为验收；战斗开始时已附魔卡的覆盖/移除仍 fail-closed；`TActionCardUpgrade` 未实现 |
| `TActionCardBeginSandstorm` | ⛔ IMPLEMENTATION / DATA | 开始事件影响出手者必做；默认沙暴数值缺证据时不猜值，见 §6 边界表 |
| `TActionGame*` / `TSpawnContextQuery` | 按场景分组 | 已确认场外商店／掉落为 OUT；场上复制为 G08 必做；无法解析且影响战斗者 DATA／IMPLEMENTATION 待核 |
| `TActionPlayerPortrait*` / `TActionExitReplacementSet` | ➖ | 非战斗 |

### 4.3 光环动作

| 原生类型 | 状态 | 说明 |
| --- | --- | --- |
| `TAuraActionCardModifyAttribute` | 🟡 | 开场与运行时分层；支持 `Add/Subtract/Multiply`、随机/最值/相邻/手牌目标、人物与时效状态条件、`FlyingTargets`/`EnchantTargets`、`DamageCrit`、缓速/冻结/急速抗性、摧毁免疫、`Heated/Chilled`；零倍率用原生模板基底反推，无法还原时告警。`TTargetCardOccupying` 已接入同槽目标；部分动态条件仍拒绝 |
| `TAuraActionPlayerModifyAttribute` | 🟡 | HealthMax/HealthRegen/RageMax/EnragedDurationMax/PercentDamageReduction；静态 Self 与带人物前提的动态层均支持，目标条件仍 fail-closed；动态人物减伤接入 `ApplyDamage`，回放未覆盖 |
| `TAuraActionCardAddTagsList` | ✅ | 阶段 4：开场静态加标签 |
| `TAuraActionCardAddTagsBySource` | ✅ | 阶段 4：从同侧其他卡复制标签 |

> 动态光环（随生命/暴怒/属性变化实时开关）已接入战斗循环：`NativeDynamicAuras.Refresh` 在每帧和每次属性变化后重建派生层，可逆属性（伤害/护盾/治疗/DoT/暴击率/暴击伤害/吸血/多重/弹药/冷却上限/缓速、冻结与急速抗性/摧毁免疫/售价/买价/`Custom_N`/`Heated`/`Chilled`）支持 `Add`/`Subtract`/`Multiply`。持续冷却缩减作为单独派生量计算有效冷却，不写入 `LiveAttributes`，避免与实时有效值重复扣减。

### 4.4 前提 / 条件 / 数值 / 时长 / 目标

| 家族 | 已支持 | 待支持（优先级） |
| --- | --- | --- |
| 前提 | `TPrerequisiteCardCount`（含 Subject=Self 的计数器及 Self 的 Neighbor/LeftCard/RightCard/AllLeftCards/AllRightCards）、`TPrerequisitePlayer`（Health/Enraged/Rage）、`TPrerequisiteCardAttributeComparator` | `TPrerequisiteRun`（➖ 非战斗） |
| 条件 | 任意属性的 `TCardConditionalAttribute`（含 Haste/Slow/Freeze 时效状态，按当前战斗时刻判定）、Tag、HiddenTag、Size、CanCrit、Type、And/Or、Tier、Id、HasEnchantment、EnchantmentEligible、PlayerHero、AttributeHighest/Lowest（同侧排行）、SizeLargest/Smallest、TriggerSource | `TRunConditionalCurrentDay/Hour`（➖） |
| 数值 | `TFixedValue`、`TReferenceValueCardAttribute`(+Unscaled)、`TReferenceValueCardCount`（运行时）、`TReferenceValuePlayerAttribute`(+Unscaled HealthMax)、`CardTagCount`、`CardAttributeAggregate`、`TRangeValue`、`TReferenceValueAttributeChange`（读取当前触发携带的变化量） | — |
| 时长 | `TDeterminantDuration(UntilEndOfCombat)`、`TCombatDuration` | — |
| 目标 | Self、Section(Self/Opponent/AbsolutePlayer/AbsoluteOpponent 的 Hand/Board/HandAndStash/Stash；`AllHands` 为双方并集)、Random、SelfNeighbors、TriggerSourceNeighbors、Positional(Self/TriggerSource 的 Neighbor/Left/Right/AllLeft/AllRight，含 IncludeOrigin)、TriggerSource、TriggerTarget、PlayerRelative、PlayerAbsolute、Player(Opponent)、XMost(Left/Right；含 Absolute 区域) | `TTargetCardOccupying` 已接入同槽物品目标（要求来源为 `TCardSocketEffect`；普通来源仍 fail-closed，玩家实时快照依赖已烘焙的 `Heated/Chilled`） |

---

## 5. 历史实施阶段（仅说明已做工作，不是当前协作看板）

- [x] **阶段 0：规则清单工具与基线** — `tools/rule_inventory.py`，输出 349 个类型组合
- [x] **阶段 1：战斗事件与常用动作** — BeforeUse、飞行开始/结束、伤害/装填/充能/过量治疗触发；Reload、FlyingStop、Burn/Poison 清除、TActionAnd
- [x] **阶段 2：条件与目标扩展** — Tier、Id、HasEnchantment、同侧最高/最低、XMost（最左/最右）
- [x] **阶段 2b：数值引用（需要运行时上下文）** — 运行时 `NumExpr`：CardAttribute、PlayerAttribute、CardCount、TagCount、CardAttributeAggregate、Range；`TriggerSource` 条件与 `TriggerTarget` 目标
- [x] **阶段 3：玩家属性与怒气** — PlayerModifyAttribute（HealthMax/Regen/RageMax/EnragedDuration）、Rage/RageMax、Enraged 触发、Player/Card AttributeChanged、通用 ModifyAttribute 与 `Custom_N` 计数器
- [x] **阶段 4：光环与标签** — 静态 AddTags 光环、战斗内 AddTagsList/Random/BySource；动态光环在阶段 6 接入
- [x] **阶段 5：战斗内生命周期** — Destroy、Disable、Repair、ForceUse、FlyingToggle、死亡触发与一次复活；Transform/Sandstorm 保留 ⛔
- [ ] **阶段 6：运行时收尾（部分完成）** — 已实现多类动态光环、场上复制与持续冷却缩减；仍存在未支持组合，且尚未完成原生一致性验收
- [x] **阶段 6b：英雄身份读取接线** — BPP snapshot 的 `Hero` 接入；条件语义与缺数据回退还需原生验证
- [x] **阶段 6c：场上变形与持续冷却缩减** — `TActionCardTransform`（打印机/镜子/全息/水银/病毒）、`onCardTransformed`、可逆冷却缩减派生层（含实时值反推）
- [x] **审查修复批次：确定缺陷修复** — 真实前提、控制成长、排队失效、怪物属性、槽位与历史展示；新增完整模板回归（见末尾日志）
- [x] **武装核心与位置规则／映射矩阵** — 完整模板回归，位置目标／主体／光环共用匹配器；实际枚举品质／附魔检查入口，明确记录受阻组合
- [ ] **阶段 7：真实回放对账（未完成）** — 现有完整 BPP 回放包含事件，具备开展条件，不列为可选收尾

### 下一步实现与验证

1. 先从完整 BPP 回放导出帧事件、逐卡冷却变化及初始棋盘，区分游戏版本、实例 ID 与开场状态。
2. 校准持续冷却缩减的有效 CD、叠加及条件开关行为，加入具体出手时间断言。
3. 用实际打印机／镜子回放校准复制后的成长与临时状态继承、附魔冲突、新冷却，以及历史展示；不要把模板测试等同于原生对账。G08 已补 `07f044ee`／`40bcb27a`／`084bd840` 的来源品质、冷却进度与双方 Virus 事实；`ebde2082` 仍有约 810 ms 冷却残差待 G12 隔离，附魔冲突与 D 门禁未完成。
4. 对齐双方初始状态后比较事件时间、来源、目标、数值；随机目标和同帧归因不明确时报告差异或未知，不冒称逐事件完全一致。

---

## 6. 已知限制 / 交接注意

1. **怪物数据必须来自原生模板**：本地 `cards.json` 只是显示定义，不能用它补怪物数值；缺卡时应回退原生 Ability。
2. **附魔没有独立本地库**：读取卡牌模板的 `Enchantments`；离线可用 `--fixture-names` 导出完整定义及 `--mapping-input` 做映射矩阵，不能声称只能统计。
3. **触发来源语义**：`TTriggerOnCardStartedFlying`/`Reload`/`Charge` 的 Subject 是**被影响的卡**，事件 payload 的 `SourceItemId` 必须是被影响的卡，否则 `Subject=Self` 的被动永远不触发。
4. **前置触发必须同步结算**：`onBeforeUse` 在物品效果之前结算，否则加成本回合不生效。
5. **不要重复加算**：附魔/技能的持久光环已反映在实时属性里，`EnchantmentMapper` 只追加 Ability。
6. **fail-closed**：无法确认的规则宁可整张卡回退+警告，也不要做一半后继续叠加。
7. `TActionAnd` 里可能混有非战斗动作（金币、经验），映射时必须逐项判断，不能整组吞掉。
8. **阶段 1 的实现边界**：
   - `onBeforeUse` 使用 `Triggers.DrainEvent` 只结算本事件，不会提前冲刷其他帧事件。
   - `onCharge` 会按原生「被动看下一帧」的约定留一个队列项；测试 `NativePassivesWaitForFollowingFrame` 已更新。
   - `onOverHeal` 对再生溢出也生效；若后续实测发现原生只对主动治疗生效，需在 `ApplyHeal` 增加来源标记。
   - `TActionCardReload` 的空值语义是「装满」，`AmountValue = -1`，不要与普通加弹药混用。
9. **阶段 2 的实现边界**：
   - `EnchantmentName` 由 `CardMapper` 写入，`CardDef.Clone` 复制；手工构造的 `CardDef` 需要自己设置，否则附魔条件恒为 false。
   - 最高/最低按**同侧全部卡**排行，并列都算命中；如果原生实际只比较目标区域，需在 `CardMatches` 增加候选集合参数。
   - 光环映射前会先把整侧模板属性铺进 `LiveAttributes`（实时覆盖优先），否则先映射的卡会把邻居看成 0；改这张表时不要退回到“逐卡刷新”。
   - `XMost` 先按条件过滤再取最左/最右，`ExcludeSelf` 在取最值前生效。
10. **改动优先看数量**：先用 `--sample` 看 3 个真实模板，再决定映射；不要凭类型名猜字段。
11. **阶段 3 的实现边界**：
    - 人物属性默认值来自原生 `Player.Attributes`：`RageMax` 100、`EnragedDurationMax` 5000 ms（怪物模板实测）。
    - 怒火只由 `TActionPlayerRageApply` 获取；达到上限进入暴怒并清零。数据库中没有“受伤涨怒气”的证据，不要自行添加。
    - `HealthMax` 增加时同步抬高当前生命（食物/成长类卡的常见语义）；如实测不符，修改 `Effects(playerAttribute)` 一处即可。
    - 行动数值已改为运行时读取属性；文本解析的非原生卡仍用常量，两者不要混用 `Value` 快照做结算。
12. **阶段 4/5 的实现边界**：
    - 标签分两层：`CardDef.Tags` 是模板/开场光环，`InternalItem.ExtraTags` 是战斗内新增；匹配时两者合并。
    - 摧毁的卡保留在 `ctx.Items`，目标与冷却跳过；仍记录无有效冷却的快照并保留历史视图，拖回离场之前可查看。不要从列表删除，否则触发遍历会失效。
    - `onBeforeDestroy` 同步结算，`onCardDestroyed` 入队按帧延后；复活每方只尝试一次。
    - `ForceUse` 不重置冷却、不消耗弹药；如后续回放显示原生会重置，再单独校准。
13. **阶段 6 的实现边界**：
    - 动态光环已接入：`NativeDynamicAuras.Initialize` 在 `InitBattle` 首次构建，`Refresh` 在每帧与每次 `ResolveEffect` 前后重建派生层；它只写 `InternalItem.AuraAttributes`，**不**改共享 `CardDef.LiveAttributes`。
    - 人物条件（生命/暴怒/怒气）已允许：`Active()` 每帧调 `PrerequisitesMatch`；带人物条件的卡牌光环只走运行时层，不再作为开场固定加成。
    - 持续冷却缩减（`Flat/PercentCooldownReduction`）已接受 Add 操作并参与有效冷却调度；G05 用 Spiky Shield 回放校准了 5% 的 `6000→5699` 有效上限与 4.9s 实际出手，但“先固定后百分比”的 Flat+Percent 组合仍未经原生证据证明。
    - 动态人物光环（`TAuraActionPlayerModifyAttribute` + 人物前提）已改为可撤销派生层：`InternalHero.Aura*` 记录当前已计入增量，`ApplyHeroAuras` 每次从“当前值−已计入光环”重算；HealthMax/HealthRegen/RageMax/EnragedDurationMax/PercentDamageReduction 支持 Add/AdditiveMultiply，目标条件仍 fail-closed。人物减伤尚未用回放逐事件校准。
    - 技能光环现在会写到 `CardDef.NativeRuntimeAuras` 并把技能放进 `Player/EnemySkills`；`ApplyStaticAuras` 结尾会重写 `NativeRuntimeAuras`，设置必须在它之后。
    - 光环数值引用 `TTargetCardSelf` 时直接读来源卡：技能不在 `ctx.Items` 里，走目标集只会得到 0。

---

### 6.1 范围判定与阻塞解除表

2026-10-24 已对 36 个 G11 主责模板逐项完成分诊，结论分四类：**IN**（引擎缺映射或行为）、**DATA**（原生数据／定义缺失）、**OUT**（本轮行动轴战斗内范围外）、**OUT+DATA**（范围外且数据不足）。OUT 只表示本轮不实现，不是永久不可实现；游戏版本、运行时数据或用户范围变化后必须复核。

| 场景 | 本轮归类／负责人 | 证据与解除条件 |
| --- | --- | --- |
| 购买、出售、商店刷新、遭遇选牌、赛季、升级经验、跑图天时 | OUT，G11；见 6.1.3 | 顶层主动能力中 `TTriggerOnCardSold` 117、`TTriggerOnDayStarted` 61、`TTriggerOnCardPurchased` 29、`TTriggerOnFightEnded` 36、`TTriggerOnCardUpgraded` 11、`TTriggerOnHourStarted` 13。按 `WorksIn`、触发器和原生 tooltip 判定；保留完整模板与理由。长期最终目标仍覆盖这些生命周期，不能吞掉同卡战斗能力 |
| 战斗中使用／属性变化／死亡触发的升级或附魔 | IMPLEMENTATION，G07 | 取得完整定义并实现；不得因动作名含 Upgrade/Enchant 就判 OUT。本轮不归 G11 |
| 打印机／镜子等复制场上道具 | IN，G08 | 供体、继承、占格、冷却与事件均需实现、测试、对账；`TSpawnContextQuery` 本身不是场外证据。本轮不归 G11 |
| 已确认从场外随机卡池生成／变形 | OUT+DATA，G11（13 项）；见 6.1.4 | 13 项全部是战斗外触发器，但 `TSpawnContextQuery` 只有 `$type` 与 `Limit`；缺候选集、权重、英雄／稀有度与运行时上下文。用户扩大范围且补足数据后重开，不用本地任意卡库猜候选。G11 过滤矩阵中这 13 项显示 `mapped` 只是因为非战斗触发器被跳过，**不代表卡池语义已实现** |
| 沙暴开始引起加速、充能、变形等 | IMPLEMENTED 2026-10-24，G11/G03；见 6.1.5 | `TActionCardBeginSandstorm` 2 处、`TTriggerOnSandstorm` 3 处、`TRunConditionalSandstorm` 0 处。引擎已映射开始动作与 `onSandstorm` 一次性事件；G11 单测验证 Bottled Tornado 的 A1/A2 仍映射、Windmill 的 HealthMax 只翻倍一次，36 模板矩阵中 3 项 IN 全部 `mapped`。默认伤害／步进仍是 DATA，未与本实现绑死 |
| 默认沙暴伤害／步进参数 | IMPLEMENTED 2026-10-25，G11/G12；见 6.1.5 | 200 份 `CombatReplays` 中 54 份有 20.0s 倒计时、23 份有 30.0s 开始与伤害；23/23 的 tick 间隔与每 tick 伤害吻合（护盾/生命拆分求和）：5 帧×5、4 帧×5、3 帧起，前 10 tick 1 伤，之后每 tick +2。已按该序列实现并加 `G11Sandstorm` 回归 |
| `TTargetCardOccupying`、槽位 Heated／Chilled 效果 | IMPLEMENTED 2026-10-25，G01/G05（+G03/G07）；见 6.1.6 | 2 条 `TCardSocketEffect`、全表 61 个模板引用 Heated／Chilled；G01 已接入同槽目标与 `Heated/Chilled`，静态/动态光环、数值、前提及被动主体/Target 有 `G01SocketEffectTests`；玩家实时生效仍依赖 BPP 快照烘焙属性 |
| 缺 AbilityId/AuraId 定义、品质、附魔 | DATA（入口已可跳过），G00/G11；见 6.1.4 | 全库 56 个模板／64 个唯一缺失引用（按品质展开 164 处）；G11 主责 4 个。36 模板矩阵现为 36/36 入口 `mapped`，其中 4 项带“缺定义已跳过”告警；定义语义仍 DATA。已排除稀疏品质继承与独立定义表；保存原始路径，不升级品质、不造数值 |
| 动态倍率、AllHands、受体过滤、次数限制 | IMPLEMENTATION，对应 G01/G03/G05 | AllHands、Absolute 区域与 XMost 已由 G01 接入主动／被动／光环／数值引用并有模板回归；动态倍率、受体过滤、次数限制仍未完成。不得伪装成 DATA。本轮不归 G11 |
| 混合战斗／场外主动能力 | IMPLEMENTED（入口映射）2026-10-24，G11/G03；见 6.1.2 | 107 张物品／技能同时含战斗与场外主动能力；`TTriggerOnEncounterSelected` 已进白名单，Peacewrought 矩阵 390/390 `mapped`；`[DEBUG] Peacewrought` 仍因调试用战斗内 `TActionGameSpawnCards` 保持例外。经济类动作与“修改经济属性”的光环已改为 no-op，保留同卡战斗分支；读取 Gold/Income 的战斗缩放光环仍归 G05 |
| 战斗内经济动作／经济光环 | OUT（no-op），G11/G03；见 6.1.2 | `Gold/Income/Experience/Prestige` 的 `TActionPlayerModifyAttribute` 不再拒绝整卡；Cash Register Golden 的 Income 光环按跳过处理（90/90 `mapped`）。不追踪跑图货币，也不把经济属性伪装成战斗属性 |
| 空基础模板／内部占位模板 | OUT，G11（16 项）；见 6.1.1、6.1.3 | 16 项基础 `Abilities/Auras` 为空；4 项有通用附魔能力（G07），4 项为仅自引用的内部占位模板。不得把“基础为空”写成“永不存在行为” |
| 索引外 `TCardPlayerEffect` 战斗规则 | IN，G06/G11；见 6.1.7 | `Base Rage Effect` `4472da8a-26a3-4e10-bd9a-e93c2e22f19c` 不在 1810 索引：暴怒时清零 Slow／Freeze，并在暴怒期间给 SelfHand +10% 冷却缩减；引擎只发 `onPlayerEnraged`，没有全局效果 |
| 索引外 `TCardSocketEffect` | 合并到 6.1.6，G01/G05 | 2 条 socket 定义不在 1810 索引；其效果就是槽位 Heated／Chilled，不能因不在索引而漏掉 |

每个 DATA 记录必须有 `模板ID/规则路径/缺字段/数据源哈希/已查位置/下一条取证动作/解除条件`。数据库或游戏版本变化时只重验相关阻塞，旧调查保留不删除。

#### 6.1.1 G11 主责 36 项逐项判定

**证据基线**：数据库 `D:/Program Files (x86)/Steam/steamapps/common/The Bazaar/TheBazaar_Data/StreamingAssets/GameData.db.zip`，SHA256 `FDFD3087E6A23EDA2C9F53BE68B012006CC949D03E8666DD51AD65ABC71B61E5`；索引 `docs/native-rule-task-index.csv` 中 `primary_group=G11` 共 36 条。36/36 均在 DB 中找到完整模板，**没有提取缺口**。下列“主责”指分诊责任人；实际效果归属列在“主责／关联”，G11 不替其他组实现语义。

**A. 原 IN — 引擎缺映射或行为（3 项，2026-10-24 已实现）**

| 模板 ID／名称（类型，品质） | 判定与路径 | 原生证据 | 主责／关联 | 解除条件与下一条动作 |
| --- | --- | --- | --- | --- |
| `028d89ac-b4c4-4d3c-ba31-71b57aa33bed` Potion Distillery（TCardItem，D/G/S） | IMPLEMENTED（入口映射）2026-10-24：A2 仍 OUT；U0／U1 已映射 | `Abilities.2` `TTriggerOnEncounterSelected` → `TActionCardTransform`（左侧 Small 非 Legendary）→ `TSpawnContextQuery Limit=1`；`Auras.0` +`Custom_0` Ammo 到 SelfHandAndStash 的 Potion；`Auras.1` +`Custom_1` PercentCooldownReduction 到 Potion；tooltip 明确“访问商人时变形”，光环为被动 | G11/G03（触发器）＋G01/G05（光环） | 已把 `TTriggerOnEncounterSelected` 纳入 `IsNonCombatTrigger`；G11 单测 `EncounterSelectedIsNonCombatAndKeepsPotionAuras` 验证 A2 跳过、2 条动态光环保留；36 模板矩阵 336/336 `mapped`。A2 的场外变形仍按 OUT 不实现；下一条：范围扩大时按 6.1.4 取卡池 |
| `c8a322c7-a8f9-49e3-90e5-3a348d0fe5fc` Bottled Tornado（TCardItem，D/G/S） | IMPLEMENTED（开始事件）2026-10-24：A0 已映射，A1／A2 保留 | `Abilities.0` CardFired → `TActionCardBeginSandstorm` Self；`Abilities.1` CardFired → `TActionCardSlow` 随机 OpponentHand（CooldownMax>0）；`Abilities.2` CardFired → `TActionCardModifyAttribute` Custom_0 dummy VFX（引擎接受任意 Custom_N） | G11/G03 | 已在 `SkillMapper.MapAction` 增加 `beginSandstorm`；首次开始写 `SandstormStarted`、发 `sandstormStart` 并派发 `onSandstorm`。测试 `SandstormActionMapsAndStartsOnce` 验证 A1/A2 仍在、开始只发生一次；矩阵 336/336 `mapped`。默认沙暴伤害仍 DATA |
| `ff06b63a-15ee-49be-80b1-5fb52b4e1616` Windmill（TCardItem，D/G） | IMPLEMENTED（开始＋OnSandstorm＋HealthMax）2026-10-24 | `Abilities.0` CardFired → BeginSandstorm；`Abilities.1` ItemUsed（SelfHand 其他物品）→ Charge Self；`Abilities.2` OnSandstorm → `TActionPlayerModifyAttribute` HealthMax，`TReferenceValuePlayerAttributeUnscaled` HealthMax ×1.0，Add，UntilEndOfCombat；`Abilities.3` CardFired → Charge SelfHand（CooldownMax>0） | G11/G03（事件）＋G06（HealthMax） | 已映射 `TTriggerOnSandstorm`→`onSandstorm`；测试 `SandstormTriggerDoublesMaxHealthOnce` 验证 Max Health 100→200 且只发生一次；矩阵 252/252 `mapped`。默认伤害／步进仍 DATA |

> 同族但不由 G11 实现的两张卡：Hunker Down A0 与 Leather Jacket A2 的 `TTriggerOnSandstorm` 已能映射；2026-10-25 当前引擎已支持 `PercentDamageReduction`（G06/G07/G10），两张卡的入口矩阵分别为 24 simulated／6 inactive 与 252/252 `mapped`。G11 只认领沙暴触发器，不重复认领减伤行为测试。

**B. DATA — 原生定义或卡池缺失（17 项：4 缺定义＋13 卡池）**

| 模板 ID／名称（类型，品质） | 规则路径（触发→动作，Limit） | 缺字段／已查位置 | 主责／关联 | 解除条件与下一条动作 |
| --- | --- | --- | --- | --- |
| `ef20ec33-7990-41b8-8d8e-47514b503612` Chunk of Lead（TCardItem，B/D/G/S） | DATA（入口 `mapped-warning`）：缺 AbilityId 0 已跳过，保留其余战斗分支；嵌套 `Transform.Abilities.t1` CardFired→SpawnCards Limit 1，`WorksIn=OutOfCombatOnly` | 缺 AbilityId 0；已查 `Abilities`（空）、`Auras`（空）、`Transform.Abilities`、四个品质块；tooltip：“战斗外被变形时获得 Chunk of Gold”；矩阵 390 行 `mapped-warning` | G00/G11；若补齐由 G07／G08 复核 | 缺定义仍按 6.1.4 共用取证流程；不得用 `Transform` 分支冒充 A0。下一条：新版本 DB 重读该路径 |
| `96b9cc27-b148-4d71-ab5d-21a9c3b64998` Mysterious Gift（TCardItem，B/D/G/S） | DATA（入口 `mapped-warning`）：AbilityId 1–5 已跳过；`Abilities.0` CardSold→SpawnCards Limit 1 仍 OUT | 缺 AbilityId 1–5；已查 Abilities 仅 0、Auras 空、四品质块；tooltip：“出售时获得另一个英雄的小型附魔物品”；矩阵 30 行 `mapped-warning` | G00/G11 | 先补定义再判场景；现有 A0 为 OUT。下一条：新版本 DB 或运行时卡池追踪 |
| `3130ec0b-ee54-4bd1-8bf9-706c41066f11` `[DEBUG] Fortune Cookie`（TCardItem，D/G/S） | DATA（入口 `mapped-warning`）：缺 AbilityId 1 已跳过；`Abilities.0` CardSold→SpawnCards Limit 1 仍 OUT | 缺 AbilityId 1；已查 Abilities 仅 0、Auras 空、三品质块；A0 与各附魔均为 CardSold；矩阵 264 行 `mapped-warning` | G00/G11 | 调试卡也按同一规则；不得因 DEBUG 名称跳过。下一条：新版本 DB 重读 |
| `45e83b0f-26a1-468e-a9ca-0075e58af7a0` `[DEBUG] Stove`（TCardSkill，Diamond） | DATA（入口 `mapped-warning`）：缺 AuraId 0 已按“纯场外技能缺光环”跳过；`Abilities.0` CardPurchased→SpawnCards Limit 1 仍 OUT | 缺 AuraId 0；已查 Auras 空、Abilities 仅 0；tooltip：“你的一个槽位被加热”；真实槽位效果见 6.1.6 的 `[Stove] Socket Effect`；矩阵 12 行由 unsupported 转为 inactive，共 30 行 `inactive` | G00/G11；槽位语义关联 G01/G05 | 缺定义与槽位规则分开取证；不得把 socket 行当作本模板的 Aura 0。下一条：新版本 DB 重读 |
| `4c8c35c4-ab16-465c-92be-0113dd99d3df` Cash Register（TCardItem，B/D/G/S） | `Abilities.0` DayStarted→SpawnCards Limit 3；`Enchantments.Shiny.Abilities.e1` 同；`Enchantments.Golden.Auras.e1` 为 `TAuraActionPlayerModifyAttribute Income +3` | `TSpawnContextQuery` 仅 `$type`+`Limit`；缺候选集／权重／英雄／稀有度；见 6.1.4 共用记录。Golden 的 Income 是经济修改，已按 no-op 跳过，90/90 `mapped` | G11（卡池）＋G00 | 池语义保持 OUT+DATA；用户扩大范围且池数据可用后重开。下一条：按 6.1.4 取运行时选择集；Golden 的经济光环不得算作战斗实现 |
| `8a9a98fe-b1bd-4da7-8233-dffd3845aa45` Dumpster Diving（TCardSkill，B/D/G/S） | `Abilities.0` DayStarted→SpawnCards Limit 1 | 同上；tooltip：“每天开始获得任意英雄的 Potion” | G11＋G00 | 同上 |
| `f1633971-bfe8-470e-bb29-111495163cf2` Frontier Living（TCardSkill，B/D/G/S） | `Abilities.0` DayStarted→SpawnCards Limit 1 | 同上；tooltip：“每天开始获得 Rage 物品” | G11＋G00 | 同上 |
| `ac92c118-c073-465c-8d89-abafa0dcb144` Genie Lamp（TCardItem，Diamond） | `Abilities.0` CardSold→DealCards Limit 1 | 同上；tooltip：“出售时进入 genie Rit” | G11＋G00 | 同上 |
| `ee6059b6-f960-4ede-a65d-ad97cf4393b7` Scavenger（TCardSkill，D/G/S） | `Abilities.0` PlayerAttributeChanged（Level,Gain）→SpawnCards Limit 1 | 同上；触发在白名单内，卡池缺失；tooltip：“升级时获得另一个英雄的小型物品” | G11＋G00 | 同上 |
| `f4827638-60ff-4101-a52f-037c04791ee5` Schematics（TCardItem，D/G/S） | `Abilities.0` PlayerAttributeChanged（Level,Gain）→SpawnCards Limit 1 | 同上；tooltip：“升级时获得任意英雄的 Small Tech 物品” | G11＋G00 | 同上 |
| `103cd65b-0b3d-418d-92d8-0dfa5f19d7c4` Shipment（TCardItem，D/G/S） | `Abilities.0` CardSold→SpawnCards Limit 3 | 同上；tooltip：“出售时获得另一个英雄的 3 个 Small 物品” | G11＋G00 | 同上 |
| `7607b1b9-4982-422d-b84a-edd9334c92c3` Skill Voucher（TCardItem，B/D/G/S） | `Abilities.0` CardSold→SpawnCards Limit 1；`Enchantments.Shiny.Abilities.e1` 同；其余附魔为 CardSold 属性修改 | 同上；tooltip：“出售时学习任意英雄的技能” | G11＋G00 | 同上 |
| `546166b0-03bf-42e9-8370-9ad05d1f00c8` Supply Cache（TCardSkill，B/D/G/S） | `Abilities.0` DayStarted→SpawnCards Limit 1 | 同上；tooltip：“每天开始获得 Loot 物品” | G11＋G00 | 同上 |
| `1e48bd28-cd41-49e9-9e22-b44da30ef6be` Thieves Guild Medallion（TCardItem，Diamond） | `Abilities.0` CardSold→DealCards Limit 1 | 同上；tooltip：“出售时进入 Thieves Guild” | G11＋G00 | 同上 |
| `7827d946-0204-4897-b3e4-bd7cec8e3979` Vending Machine（TCardItem，D/G/S） | `Abilities.0` DayStarted→SpawnCards Limit 3 | 同上；tooltip 明确三个具体物品，但仍需原生池／权重核验 | G11＋G00 | 同上 |
| `ba42edfe-a782-4b66-bb71-adfe9a1b87df` `[DEBUG] Mirror version of Magician's Top Hat`（TCardItem，Diamond） | `Abilities.0` CardSold→SpawnCards Limit 1 | 同上；tooltip：“出售时获得最左 Medium/Small 非 Legendary 物品的 Diamond 复制”；复制语义需 G08 复核 | G11＋G00（效果归属 G08） | 同上；不要把 `TSpawnContextQuery` 当作复制供体 |
| `8e731928-fa9e-4ce4-b9b6-f54b600d94e1` `[Debug] Reroll token`（TCardItem，B/D/G/S） | `Abilities.0` CardSold→`TActionGameReroll`；无 `SpawnContext` | 缺当前选择集、重掷池、费用／次数上下文；已查 Abilities、Tiers、tooltip；与 6.1.4 的 `TPrerequisiteRun` 记录交叉 | G11＋G00 | 保持 OUT+DATA；下一条：取商店／遭遇重掷的运行时状态 |

**C. OUT — 本轮战斗内范围外（16 项）**

| 模板 ID／名称（类型，品质） | 原生证据 | OUT 依据 | 主责／关联 | 复核条件 |
| --- | --- | --- | --- | --- |
| `dd438f4e-77ea-4b7a-b59e-ae8906c49da8` Augment Reagents（TCardItem，B/D/G/S） | 基础 Abilities／Auras 空；12 个附魔能力（9 个 CardFired：Slow/Freeze/Haste/Shield/Heal/Poison/Burn/Damage/Regen，3 个光环） | 基础无行为；附魔能力是通用附魔，归 G07，不构成 G11 基础规则 | G11；附魔 G07 | 若基础模板新增战斗触发／动作，重开；不得把通用附魔算作基础能力 |
| `5ded844c-5279-4c30-9198-309fba0b651b` Bag of Jewels（TCardItem，B/D/G/S） | 基础全空；tooltip“Sells for Gold” | 出售经济，战斗内无行为 | G11 | 版本新增战斗分支 |
| `5a9bd869-4614-4edf-941d-5f44e0bb1519` Bar of Gold（TCardItem，B/D/G/S） | 基础全空；tooltip“Sells for Gold” | 同上 | G11 | 同上 |
| `0c8298ad-3001-4631-9bed-df11a7425ced` Chunk of Gold（TCardItem，B/D/G/S） | 基础全空；10 个附魔能力全部 `TTriggerOnCardSold` | 出售经济；附魔分支仍是战斗外 | G11；附魔 G07 | 版本新增战斗分支 |
| `82be7836-c1f8-4f07-b165-493f40d9d21b` Inspired by Jules: Cooler（TCardSkill，Diamond） | 基础全空；tooltip“一个槽位变成 Cooler（Chilled）”；真实效果在 `[Cooler] Socket Effect` | 模板本身是战斗外布槽；槽位效果另见 6.1.6 | G11；槽位 G01/G05 | 若把 socket 效果并入本模板，按 6.1.6 重开 |
| `0d4f9fa5-521c-4e05-8b2b-dc8b185b18eb` Inspired by Jules: Stove（TCardSkill，Diamond） | 基础全空；tooltip“一个槽位变成 Stove（Heated）”；真实效果在 `[Stove] Socket Effect` | 同上 | G11；槽位 G01/G05 | 同上 |
| `1034cd09-ce16-44ad-9a73-ba86a592c9d2` Keen Edge（TCardSkill，Diamond） | 基础全空；无 tooltip、无附魔 | 无原生行为可执行；占位模板 | G11 | 版本新增定义 |
| `c7432844-9341-411b-8a1f-8662d46c181f` Large Package（TCardItem，B/D/G/S） | 基础全空；tooltip 为模板标签 | 无战斗行为 | G11 | 版本新增战斗分支 |
| `35b7a978-275b-48f0-8e3d-9be2391c6a78` Medium Package（TCardItem，B/D/G/S） | 基础全空；tooltip“Farai will return for this” | 同上 | G11 | 同上 |
| `54c4526a-8fd0-4297-84d4-41e99710895e` Ship [OnBuy] effectdata 2（TCardItem，B/D/G/S） | 基础全空；无 tooltip | effectdata 占位，无主动／被动 | G11 | 版本新增定义 |
| `4ddb3806-a5ee-4cfd-985f-19f184580ade` Small Package（TCardItem，B/D/G/S） | 基础全空；tooltip 为模板标签 | 无战斗行为 | G11 | 版本新增战斗分支 |
| `f212afe2-08d1-40e3-978b-762d345bc7e5` Spare Change（TCardItem，B/D/G/S） | 基础全空；tooltip“Sells for Gold” | 出售经济 | G11 | 同上 |
| `599c27bd-2e78-45a6-9390-aeb349a20e29` `[LARGE ITEM TEMPLATE]`（TCardItem，B/D/G/S） | 基础全空；12 个附魔能力；DB 中只有自身引用 | 内部占位模板；未发现其他模板引用；通用附魔归 G07 | G11；附魔 G07 | 若运行时池会实例化它，按实际附魔能力复核 |
| `a031a51e-7b07-4055-adff-a421d2ee8943` `[MEDIUM ITEM TEMPLATE]`（TCardItem，B/D/G/S） | 同上 | 同上 | G11；附魔 G07 | 同上 |
| `edc0d83d-323e-4f3f-af66-ff5fe958387c` `[SKILL TEMPLATE]`（TCardSkill，B/D/G/S） | 基础全空；无附魔能力 | 内部占位模板 | G11 | 若运行时池会实例化它，复核 |
| `273b7c90-a4be-4226-b374-94a7c01f07d9` `[SMALL ITEM TEMPLATE]`（TCardItem，B/D/G/S） | 基础全空；12 个附魔能力；DB 中只有自身引用 | 内部占位模板；通用附魔归 G07 | G11；附魔 G07 | 同上 |

#### 6.1.2 混合战斗／场外规则

**定义**：同一模板既有战斗内主动能力（CardFired、ItemUsed、受击／受控／死亡等），又有战斗外主动能力（CardSold、DayStarted、CardPurchased、升级经验、EncounterSelected、CardSelected、EncounterCardsDealt 等）。只统计 `TCardItem/TCardSkill` 的顶层 `Abilities`，不把附魔单独算作一张卡。

- 共 **107 张**物品／技能是混合卡。加入 `TTriggerOnEncounterSelected` 后，105 张白名单内场外分支 + Peacewrought 均可跳过场外分支；但这只证明“不会回退”，不等于其战斗动作已全部支持。
- `TTriggerOnEncounterSelected` 已加入 `IsNonCombatTrigger`。Potion Distillery 与 Peacewrought 不再整卡回退；Peacewrought 的 390 个矩阵组合全部 `mapped`，其 `TActionPlayerModifyAttribute Gold`（A4）按经济 no-op 跳过，A0/A1 战斗分支保留。
- `[DEBUG] Peacewrought` `f4a7f5d3-73d6-4f08-ab70-6b5ee3309005` 的 A4 是 `TActionGameSpawnCards` 挂在战斗触发器上，属于调试用的战斗内生成；当前引擎明确跳过该 DEBUG 动作，卡的其余部分 390/390 `mapped`，不是 EncounterSelected 阻塞。
- `TTriggerOnEncounterSelected` 共 43 处／19 个模板，顶层主动能力 14 处／8 个模板；`TTriggerOnCardSelected` 共 625 处／467 个模板，顶层主动能力 624 处／466 个模板，但物品／技能只有 3 个模板（Red Envelope、Machine Learning、SkillCard - Speed），且都是纯场外卡，不属于混合战斗缺口。G03 现已把 `TTriggerOnCardSelected`／`EncounterEntered`／`EncounterExited`／`EncounterCardsDealt` 等纳入白名单，G11 不再把它们当战斗缺口。
- 纯场外卡（Potion Distillery、Backroom Dealings、Coupon、Galactic Translator、Rewards Card、Business Card、Compass、Dreampearl、Thieves Guild Discount、Lucky Clover、Red Envelope、Machine Learning、SkillCard - Speed、Renovation 及两张远征票）的场外触发器现由 G03 白名单统一跳过。Potion Distillery 已处理；其余没有战斗内行为；它们的问题是全卡回退的健壮性，不是 G11 战斗规则缺失。

**系统性问题：41 个未支持的人物属性动作，分布在 26 个基础物品／技能模板。** 引擎 `TActionPlayerModifyAttribute` 接受 `HealthMax/HealthRegen/RageMax/EnragedDurationMax/PercentDamageReduction/Rage`；经济属性已走 no-op；其余未知属性仍 fail-closed。分解如下：

| 属性 | 场外触发 | 战斗触发 | 合计 | 处理归属 |
| --- | --- | --- | --- | --- |
| Gold | 10 | 8 | 18 | 场外经济 OUT；战斗内经济动作已 no-op，不再回退整卡（G11/G03） |
| Income | 1 | 1 | 2 | 同上；Cash Register Golden 的经济光环也已跳过 |
| Experience | 3 | 0 | 3 | OUT（升级经济）；动作已 no-op |
| Prestige | 1 | 0 | 1 | OUT（赛季经济）；动作已 no-op |
| PercentDamageReduction | 0 | 16 | 16 | 当前引擎已支持（G06/G07/G10）；Hunker Down／Leather Jacket 入口已 `mapped`（针对性矩阵 24 simulated／6 inactive，Leather Jacket 252/252） |
| Rage | 0 | 1 | 1 | 当前引擎已支持（G06）；G11 不重复实现 |

结论：经济类 24 个动作已改为“跳过该动作、保留同卡其他动作”，并由 `GoldActionOnCombatTriggerIsNoOp` 覆盖 `TActionAnd` 中经济 + 伤害的组合；物品静态光环路径中的 `TAuraActionPlayerModifyAttribute` Gold／Income／Experience／Prestige 修改光环同样跳过（Cash Register Golden 90/90 `mapped`）。战斗类 17 个动作（PercentDamageReduction、Rage）已由 G06／G07／G10 接入，G11 只验证入口，不重复实现其行为测试。另有一类**读取** Gold／Income 的战斗缩放光环（Ring King Gauntlets、Cash Deposits、Commercial Zoning、Gold Garnish、Slingshot Golden）需要真实跑图货币值，归 G05，不应被本次经济修改 no-op 误吞。

#### 6.1.3 OUT 清单与排除依据

本节对应 6.1.1 C 表 16 项，以及 6.1.1 B 表中 13 项“OUT+DATA”的场外部分。

- 16 项 OUT 的共同证据是：基础 `Abilities` 与 `Auras` 均为空，且没有基础触发器；其中 4 项（Augment Reagents、`[SMALL/MEDIUM/LARGE] ITEM TEMPLATE`）存在通用附魔能力，9 个是 CardFired 的 Slow／Freeze／Haste／Shield／Heal／Poison／Burn／Damage／Regen，另 3 个是 Golden／Shiny／Deadly 光环。这些是 G07 的通用附魔语义，不是 G11 基础卡规则；不能因此把整张卡标成 IN。
- 4 个 `[SIZE] ITEM TEMPLATE`／`[SKILL TEMPLATE]` 在 DB 中只被自身引用，未发现其他模板 ID 指向它们；它们是内部占位模板，不是可派单的完整卡。若运行时池会实例化它们，必须按实际附魔能力重开。
- 另外 4 项有明确的场外 tooltip 或动作：Bag of Jewels／Bar of Gold／Spare Change“Sells for Gold”；Inspired by Jules Stove／Cooler“一个槽位变成 Stove／Cooler”，真实效果在 socket 行（6.1.6）。
- 13 项“OUT+DATA”的场外依据来自触发器与 tooltip，而不是 `TSpawnContextQuery` 本身：CardSold 的“出售时获得…”、DayStarted 的“每天开始获得…”、LevelUp 的“升级时获得…”。卡池查询只有 `$type`+`Limit` 是 DATA 证据，不能单独当 OUT 证据。
- OUT 不是永久排除。复核条件统一为：游戏版本变化后模板新增战斗触发／动作；或用户明确把行动轴范围扩大到战斗外经济与卡池模拟。届时按 6.1.4 的 DATA 字段重新取证，不能沿用本轮结论。
- 顶层主动能力的场外触发计数（全库）：CardSold 117、DayStarted 61、FightEnded 36、CardPurchased 29、HourStarted 13、CardUpgraded 11、EncounterSelected 14、EncounterCardsDealt 180、CardSelected 624。数量用于说明范围，不代表每个模板都已人工复核；逐项结论以 6.1.1 为准。

#### 6.1.4 DATA 记录（卡池与缺定义）

**共用记录 P — 场外随机卡池／变形（对应 13 项）**

- 模板 ID：`4c8c35c4-ab16-465c-92be-0113dd99d3df`、`8a9a98fe-b1bd-4da7-8233-dffd3845aa45`、`f1633971-bfe8-470e-bb29-111495163cf2`、`ac92c118-c073-465c-8d89-abafa0dcb144`、`ee6059b6-f960-4ede-a65d-ad97cf4393b7`、`f4827638-60ff-4101-a52f-037c04791ee5`、`103cd65b-0b3d-418d-92d8-0dfa5f19d7c4`、`7607b1b9-4982-422d-b84a-edd9334c92c3`、`546166b0-03bf-42e9-8370-9ad05d1f00c8`、`1e48bd28-cd41-49e9-9e22-b44da30ef6be`、`7827d946-0204-4897-b3e4-bd7cec8e3979`、`ba42edfe-a782-4b66-bb71-adfe9a1b87df`、`8e731928-fa9e-4ce4-b9b6-f54b600d94e1`。
- 规则路径：逐项见 6.1.1 B 表；动作集中在 `TActionGameSpawnCards`（233 处全库／142 处物品技能）、`TActionGameDealCards`（168／5）、`TActionGameReroll`（6／6）。
- 缺字段：`TSpawnContextQuery` 全库 856 个节点（物品／技能 175 个）**全部只有 `$type` 与 `Limit`**；缺候选卡集合、过滤条件、权重、英雄、稀有度、标签、区域与运行时选择上下文。`TActionGameReroll` 没有 `SpawnContext`，还缺当前选择集与重掷池。
- 数据源哈希：`GameData.db.zip` SHA256 `FDFD3087E6A23EDA2C9F53BE68B012006CC949D03E8666DD51AD65ABC71B61E5`。
- 已查位置：`cards` 表全部 2709 行；`challenges` 80、`monsters` 166、`seasons` 15、`level_ups` 30、`collectibles` 8、`game_modes` 1、`tooltips` 1 均未找到可重建的池集合或权重；`docs/native-rule-task-index.csv` 的 1810 条索引；尚未完成 `BazaarCardDealer`／卡池类型的完整 IL 与运行态追踪。
- 下一条取证动作：在带商店／每日开始／出售的运行时样本中记录实际发出的卡牌 ID 序列、当天英雄、商店等级、种子／RNG 状态；或在只读程序集中定位 `BazaarCardDealer` 的池选择方法并记录输入。禁止用本地任意卡库或全卡表猜候选。
- 解除条件：用户明确扩大范围到战斗外卡池，且同版本运行时数据能重建候选集与至少一组权重／过滤；否则保持 OUT+DATA，不实现。
- 2026-10-25 G11 过滤矩阵（36 模板／5574 行）：item `mapped` 4620、`mapped-warning` 684；skill `inactive` 156、`out-of-combat` 114、`unsupported` 0。**36/36 入口均已映射**；684 行告警与 12 行由 unsupported 转入 inactive 的正是 4 项缺定义：Chunk of Lead 390、Mysterious Gift 30、`[DEBUG] Fortune Cookie` 264、`[DEBUG] Stove` 12（合计 696 行，按状态分别计入）。这不是新的卡池解析成功，定义语义仍是 DATA。3 项 IN 已 `mapped`：Potion Distillery 336/336、Bottled Tornado 336/336、Windmill 252/252；Cash Register 90/90。
- 13 项卡池模板在矩阵中显示 `mapped`，只是因为场外触发器被跳过；不得把该状态当作候选集／权重已实现。它们的解除条件仍是上一条。

**共用记录 D — 缺 AbilityId／AuraId 定义（全库 56 模板／64 唯一引用）**

- 全库统计：56 个 `TCardItem/TCardSkill` 模板存在 Tier 引用的 AbilityId／AuraId 在模板内无定义；64 个唯一 `(模板, 类型, ID)`；按品质展开 164 处。G11 主责 4 项：Chunk of Lead（Ability 0）、Mysterious Gift（Ability 1–5）、`[DEBUG] Fortune Cookie`（Ability 1）、`[DEBUG] Stove`（Aura 0）。
- 规则路径：逐项见 6.1.1 B 表；已查 `Abilities`、`Auras`、`Transform.Abilities`、`Enchantments.*.Abilities/Auras`、`Tiers.*` 的 `AbilityIds/AuraIds`。`Tiers` 块只有 ID 列表，没有内联定义；DB 没有独立的 ability／aura 表。
- 已排除：不是稀疏品质继承（同一缺失 ID 在多个品质重复）；不是引擎读取器把对象读空（原始 JSON 中确实不存在该键）；不是 `Enchantments` 覆盖（附魔能力单独列出）。仍未排除运行时代码动态生成定义。
- 数据源哈希：同上 `FDFD3087...`。
- 已查位置：`cards` 表全部 2709 行；`Tiers/Abilities/Auras/Transform/Enchantments` 全路径；`docs/native-rule-task-index.csv`；只读程序集仅用于沙暴取证，尚未核对动态卡定义生成。
- 下一条取证动作：游戏版本更新后重读同一路径；若仍缺失，查 `BazaarCardDealer`／卡牌生成代码是否在运行时补定义，并记录版本与哈希。不得为缺失 ID 编造数值、品质或效果。
- 解除条件：新版本 DB 补齐定义，或运行时追踪证明该 ID 由代码生成且可重建；在此之前 4 项保持 DATA。

#### 6.1.5 沙暴开始与默认伤害拆分

**A. 开始／触发 — IMPLEMENTED 2026-10-24，G03/G11**

- 动作：`TActionCardBeginSandstorm` 2 处——Bottled Tornado `Abilities.0`、Windmill `Abilities.0`，均为 CardFired → Self。
- 触发：`TTriggerOnSandstorm` 3 处——Windmill `Abilities.2`（HealthMax 翻倍）、Hunker Down `369a6704-2727-4cea-be27-01b76c82df45` A0（`TActionPlayerModifyAttribute` PercentDamageReduction）、Leather Jacket `8619fc8b-ceec-4be6-9289-8c7b6f05a631` A2（`TActionCardDisable` Self）。
- `TRunConditionalSandstorm` 0 处使用；`TGameModeConfigSandstorm` 是配置类型，不是卡牌规则。
- 引擎现状（2026-10-25）：`SkillMapper.MapAction` 接受 `TActionCardBeginSandstorm` 并产出 `beginSandstorm`；`NativeSkillSupport.TriggerEvents` 映射 `TTriggerOnSandstorm`→`onSandstorm`；`Effects.StartSandstorm` 统一处理卡牌提前开始与默认 30s 开始，写 `SandstormStarted/SandstormStartedAt`、发 `beginSandstorm` 动作事件并派发全局 `onSandstorm`，重复开始不再派发。`Battle.TickSandstorm` 负责默认倒计时、tick 与伤害。
- 验证：`G11SandstormTests.SandstormActionMapsAndStartsOnce` 验证 Bottled Tornado A0/A1/A2 均映射且卡牌开始只出现一次；`DefaultSandstormMatchesReplayCadence` 验证 20s 倒计时/30s 开始、13 个初始 tick 与 35/38/41/42.5s 锚点；`SandstormTriggerDoublesMaxHealthOnce` 验证 Windmill 的 `playerAttribute HealthMax` 只 +100、最终 MaxHp 200。Hunker Down／Leather Jacket 的 `PercentDamageReduction` 已由当前引擎映射（G06/G07/G10），不再由沙暴触发器阻塞。
- 结论：开始事件、默认倒计时、tick、伤害与 OnSandstorm 触发均已实现并验证；23/23 回放的数值序列已对齐，事件级同帧顺序留给 G12 对账。

**B. 默认伤害／步进 — IMPLEMENTED 2026-10-25，G11/G12**

- 回放证据：`CombatReplays/CombatReplays` 共 200 份 `.payload.mpack.gz`；54 份有 `CombatSimEventSandstormCountdownStarted`（帧 400，20.0s），23 份有 `CombatSimEventSandstormStarted`（帧 600，30.0s）与实际伤害。23/23 的 tick 间隔完全吻合：5 帧×5、4 帧×5、3 帧起；前 10 tick 伤害 1，之后每 tick +2。
- 反推参数：`CountdownStartTime=20.0s`、`CountdownDuration=10.0s`、`BaseTickRate=5` 帧、`TickRateDecrement=1`、`TickRateFloor=3`、`BaseDamage=1`、`AdditiveScalar=2`。锚点：35.0s=41、38.0s=81、41.0s=121、42.5s=141（干净回放 `06158435ccdc4c7a9c631d144da6ad31`）。
- 实现：`BattleConfig` 新增上述 7 个默认值；`Battle.TickSandstorm` 在原生机制下自动倒计时/开始；`Effects.StartSandstorm` 允许卡牌提前开始并抑制默认 30s 重复开始；沙暴伤害按护盾→生命，标记 `dotTick/sandstorm`，不计入玩家输出、不触发 `onDamageDealt/Taken`。当前回放的 `IsDamageReduced` 均为 false，因此本轮不应用 `PercentDamageReduction`；若后续回放证明沙暴可被减伤，再改。
- 对账口径：23 份回放全部逐 tick 吻合；同一帧的护盾/生命拆分必须先求和再比较，否则会把 `2fe6a954` 等误判为“伤害偏低”。逐事件同帧顺序由 G12 继续。
- 解除条件：已满足基础配置；若后续版本改变沙暴参数，按同版本回放重新反推，不沿用本组数字。

#### 6.1.6 槽位占用与 Heated／Chilled

- 原生定义：2 条 `TCardSocketEffect` 行——`c017c0dd-af3c-47e0-b510-3661d9dcd7f2` `[Stove] Socket Effect`、`d5515ae3-b98d-4272-8add-82f441969ffe` `[Cooler] Socket Effect`；均为 Diamond，`AuraIds=['0']`，`Attributes.Custom_0=1`；Aura 为 `TAuraActionCardModifyAttribute`，`AttributeType=Heated/Chilled`，Add `Custom_0`，目标 `TTargetCardOccupying`，`ActiveIn=HandOnly`，`WorksIn=Anywhere`；tooltip 分别为“The item here is Heated／Chilled”。
- 引用规模：全表 43 个模板引用 Heated、25 个引用 Chilled、7 个两者都有（并集 61；仅算 `TCardItem/TCardSkill` 为 42／24／7，并集 59）。其中 `TCardConditionalAttribute` 179 处（Heated 104、Chilled 75），光环修改 10 处（Heated 5、Chilled 5），动作修改 4 处（Heated 3、Chilled 1）。
- 引擎现状：`SkillMapper` 已把 `TTargetCardOccupying` 映射为 `occupyingItem`，`Effects`、`NativeDynamicAuras`、`NativeCardAbilityMapper` 统一命中同槽非插槽物品；`G01SocketEffectTests` 用两条原生 socket 覆盖主动、被动主体/Target、静态/动态光环、计数/属性引用和前提。`LiveMonster` 读取 `Player.Socket.Effects` 并把 Heated/Chilled 写入目标；玩家实时快照已含烘焙的 `Heated/Chilled`。非 `TCardSocketEffect` 来源仍无目标、不退回 Self。
- 归属：G01 负责槽位占用目标与相邻／占位解析，G05 负责动态光环，G03 负责条件与触发，G07 负责 Heated／Chilled 与附魔交互；G11 只负责分诊与证据。`Inspired by Jules: Stove/Cooler` 两张技能模板本身是空壳 OUT；真正要重开的是这两条 socket 定义，不能因为模板空就判“无规则”。
- 解除条件：已实现槽位占用目标集、Heated／Chilled 属性与 Stove／Cooler 端到端回归；剩余是玩家实时 socket effect 的独立回放验证（当前快照只有烘焙属性，没有独立 socket 来源证据）。
- 索引说明：两条 socket 行的 `$type` 是 `TCardSocketEffect`，不满足 `TCardItem/TCardSkill` 索引条件，因此不在 1810 条 CSV 中；G00 应把它们加入规则清单而不是卡牌索引，避免再次“索引外漏项”。

#### 6.1.7 索引外战斗模板与 G11 验收口径

**索引外战斗规则：`Base Rage Effect`**

- `4472da8a-26a3-4e10-bd9a-e93c2e22f19c`，`TCardPlayerEffect`，`Heroes=['Common']`，StartingTier Diamond；`Abilities.0/1` 为 `TTriggerOnPlayerEnraged` → `TActionCardModifyAttribute` Slow／Freeze，Multiply 0，目标 SelfHand；`Auras.2` 为 `TAuraActionCardModifyAttribute` PercentCooldownReduction Add `Custom_0=10`，目标 SelfHand，前提为 Enraged>0。
- 它不是物品／技能，不在 1810 索引；但它是实际战斗规则：暴怒时清空全手 Slow／Freeze，暴怒期间全手 +10% 冷却缩减。引擎目前只发 `onPlayerEnraged` 事件，没有硬编码这两个全局效果，也没有把 `TCardPlayerEffect` 纳入映射。
- 全库 9 条 `TCardPlayerEffect` 中，7 条是 `[PlayerAttributes]` 跑图统计，1 条是暴怒头像 UI（`TActionPlayerPortraitNext/Reset`），只有 `Base Rage Effect` 是战斗规则。归属 G06/G11；解除条件：G06 实现全局暴怒效果并测试“清 Slow／Freeze、+10% 冷却缩减、暴怒结束移除”，G00 决定是否把 `TCardPlayerEffect` 纳入清单。下一条：G06 写失败测试，G11 提供模板夹具。

**G11 本轮验收口径**

- 零“无主模板”：36/36 G11 主责模板均已在 6.1.1 列名，并写明主责／关联与下一条动作；索引外发现的 `Base Rage Effect` 与 2 条 socket 行也已有归属。
- 可复核排除清单：6.1.3 的 16 项 OUT 与 6.1.4 的 13 项 OUT+DATA 均给出原生路径、触发／动作、tooltip 或“基础为空”的证据，并写明复核条件；不是“剩余少量”式概括。
- 沙暴开始与伤害拆分：6.1.5 A/B 均已 IMPLEMENTED；默认伤害来自 200 份回放中的 54 份倒计时、23 份伤害序列，不再标记 DATA。
- 本轮**已实现并验证**：3 项 IN 的入口映射（Potion Distillery、Bottled Tornado、Windmill）、`TTriggerOnEncounterSelected` 白名单、`TActionCardBeginSandstorm`／`TTriggerOnSandstorm` 一次性事件、默认 20s 倒计时/30s 开始/沙暴 tick 与伤害、经济动作与经济修改光环的 no-op。G11 单测、36 模板过滤矩阵与整包测试均通过；**36/36 入口 mapped**，其中 4 项 DATA 带“缺定义已跳过”告警，13 项 OUT+DATA 仍是场外卡池语义未实现，16 项 OUT 仍无战斗内行为。
- 未执行：卡池运行态追踪、逐事件原生沙暴对账、游戏内部署、提交。04:25 整包快照为 `OK`；04:31 当前源码仅 `G08Transform` 并发行失败，G11 只认领 `G11Sandstorm`／`G11Coverage` 与 36 模板矩阵。
- 下一步并行入口：G12 用回放做逐事件沙暴对账；G05 处理读取 Gold／Income 的战斗缩放光环；G01／G05 实现 Occupying 与 Heated／Chilled；G06 实现 Base Rage Effect；G00 复核 56 模板缺定义与卡池运行时数据。以上均为建议顺序，不代表已排期或已授权。

<a id="协作分组"></a>
## 7. 任务拆解与多人协作（N = 13 个交付组）

### 7.1 全量归属规则：不能只处理示例卡

- 全量附件：[native-rule-task-index.csv](native-rule-task-index.csv)。每个 `TCardItem/TCardSkill` 一行，列出**完整模板 ID、主责组、关联组、声明品质、附魔枚举、原生类型**；怪物另归 G00。CSV 只由本文件末尾路由配置生成，不手改完成状态。
- 主责：先按下述具体种子 ID 指派；其余按基础模板的结构特征匹配，按配置 `priority` 决定唯一主责，无匹配者归 G11。附魔定义不抢占基础卡主责，但凡含附魔均关联 G07；同一卡可以参与多个组的行为测试。
- 每条能力再按 `(table, templateId, tier, enchantment, AbilityId/AuraId, JSON路径, side, scene)` 分解成工作单；一种卡的主动、被动和附魔不得只做其中一个便关闭整卡。
- 主责组负责这张卡的完整集成验收；关联组只实现自己规则切片，不重复写另一套结算。G00/G12 是全量横向门禁，不替任何组代签行为正确性。
- 路由是**派单规则，不是效果语义判定器**：命中 JSON 特征仍需区分战斗外分支、失效历史定义和实际品质引用。G11 归属不代表自动排除；必须逐项完成范围判定。
- 初始所有组负责人均为“待认领”。下面列的是最小验收种子，完整工作量以 CSV 的 `primary_group/related_groups` 为准。更新种子或归属只改本文件配置再重生成 CSV，G00 验证零重复、零遗漏。
- 本次索引实际生成 **1810 条唯一模板**：1146 个物品、498 个技能、166 个怪物；43 个种子 ID／名称已核验。主责数量为 G00 166、G01 164、G02 148、G03 45、G04 150、G05 786、G06 136、G07 3、G08 20、G09 94、G10 62、G11 36；G12 横向关联全部。G07 主责少不代表附魔工作少，大量附魔工作在 `related_groups` 中。
- 大组按 `(规则类型＋参数形状)` 再拆 `G05-001` 等子单，每单控制在一个规则契约及其全部受影响模板；不按卡名重复实现相同机制。每单可独立合并，父组只有全部 IN 子单 VERIFIED 才关闭。

查看某组完整卡牌列表（不是只看本文件的例子）：
```powershell
Import-Csv docs/native-rule-task-index.csv | Where-Object { $_.primary_group -eq 'G05' } | Select-Object template_id,name,template_type,tiers,enchantments
Import-Csv docs/native-rule-task-index.csv | Where-Object { ($_.related_groups -split ';') -contains 'G07' } | Select-Object template_id,name,primary_group,enchantments
```

### 7.2 可交付任务组与初始看板

文件缩写：`E/` = `tools/actionaxis_plugin/engine/`，`P/` = `tools/actionaxis_plugin/plugin/`，`T/` = `tools/actionaxis_plugin/tests/`。下列测试名称已存在，新增用例可补入对应文件；新文件须登记 `Program.cs`。

| 组 | 交付主题 | 当前状态 | 前置依赖 | 负责人／审查人 |
| --- | --- | --- | --- | --- |
| G00 | 数据、品质、读取、矩阵与基线冻结 | PARTIAL；2026-10-25 物品入口阻断清零（0 物品／2 技能 DATA），46 物品保留缺定义警告；387678 组合、166 怪物读取矩阵、56／64／164 缺定义与 11 条索引外规则已冻结；原生回放与部署未做 | 无 | OpenCode（本轮）／待复核 |
| G01 | 位置、全体、来源／目标与槽位查询 | IMPLEMENTED（G01 主责 `unsupported=0`）；AllHands/Absolute/位置/XMost/槽位契约与主动优先级已测；整合整包 Gate A 全绿；回放仅敌方侧种子，Gate C 未完成 | G00 | 本轮实现／待复核 |
| G02 | 充能、控制、飞行、弹药联动 | PARTIAL；四种子品质成长／状态／Shiny 多重时序已回归（G02StatusTiming），6 物品因原生能力定义缺失受阻（DATA），21 技能无 unsupported | G00/G01/G03 契约 | Qoder（本轮）／待复核 |
| G03 | 被动过滤、条件、次数、队列与优先级 | PARTIAL；45 个主责模板全量入口＋机制族回归已测，4 张缺能力定义卡＋Resonance Crystal 场外光环缺失保持 DATA/OUT+DATA，CardSelected 场外卡归 OUT；整包与逐卡真实回放未签 | G00/G01 契约 | Qoder（本轮）／待复核 |
| G04 | 数值引用、属性成长、标签与计数器 | IMPLEMENTED；150/150 主责模板映射＋运行时冒烟通过，G04 机制相关 unsupported=0；1 张缺定义告警（Masterpiece），真实回放仅部分卡有证据 | G00/G01 | Qoder（本轮）／待复核 |
| G05 | 动态光环、倍率与持续冷却派生层 | IMPLEMENTED（G05-02 全量：786/786 模板无 unsupported／partial，16 模板回放值级校准；逐事件对账／Gate C 待 G12） | G01/G04/G06 契约 | 本轮执行者／待复核 |
| G06 | 人物属性、怒气、怪物／玩家状态 | PARTIAL；136/136 模板矩阵无 unsupported（7 条原生缺定义仅 warning），G06RageState＋G06FullCoverage 通过；Toxic／Wild Boar 无正回放 | G00/G03/G04 | Qoder（本轮）／待复核 |
| G07 | 附魔、暴击、抗性、免疫 | IMPLEMENTED（切片）；全库附魔层 **0 告警、0 仅附魔阻塞**；34 个 G07 行为用例；最新权威矩阵基础卡仅 5 物品／2 技能硬拒绝，另有 46 模板仅缺定义告警（DATA，None 同样存在） | G00/G02/G04/G05 | Qoder（本轮）／待复核 |
| G08 | 场上复制变形与历史身份 | IMPLEMENTED；8 张 A 类场上变形全部入口＋行为＋测试，9 张 B 类场外池 OUT/fail-closed，20 个原生 G08 变形事件对账；3D Printer 240ms 冷却残差、静态标签光环与游戏内 D 门禁未关闭 | G01/G03/G04/G07 契约 | Qoder（G08 续）／待复核 |
| G09 | 摧毁、禁用、修理、强制使用、复活 | IMPLEMENTED；94 模板全量分诊（83 全映射／4 DATA／3 部分 DATA／4 OUT）；G09 定向与 05:02 官方全量均 OK；05:04 并发 EnchantmentMapper 使共享工作区变红，G09 仍 PASS | G03/G06/G08 代际契约 | Qoder（本轮）／待复核 |
| G10 | 直伤、DoT、护盾、治疗与输出统计 | IMPLEMENTED；双方/过量/多来源、62/62 模板索引回归（MappingWarnings=0）、084bd840 双方原生账本、低层燃烧衰减、来源应用核对与 Shard 回归已测；逐 tick 来源份额待 G12 | G03/G04/G06/G07 契约 | Qoder（G10 续）／待复核 |
| G11 | 未归类、非战斗、缺数据与沙暴取证 | IMPLEMENTED 入口 2026-10-25；36/36 模板 `mapped`（4 项 DATA 带缺定义告警），默认沙暴倒计时/tick/伤害已按 23 份回放实现，`G11Sandstorm`／`G11Coverage` 通过，04:25 整包快照通过；13 项 OUT+DATA 卡池语义、16 项 OUT 仍保持 | G00 | Qoder（本轮）／待复核 |
| G12 | 逐事件对账、UI 历史、构建发布 | IMPLEMENTED（不是 VERIFIED）；521 份全量扫描/145 份可比较均 DIFF；2 份纯确定性仍有 30 missing／9 extra／350 value；143 份含随机分支单列 402265 unverified；frame-0 DoT 已修复；开场非满冷却 0；376 份缺侧车/旧模板阻塞；G12 测试 PASS；最终复跑 C# OK；未部署 | G00，最终依赖 G01–G11 | OpenCode（本轮）／待复核 |

#### G00 — 数据、品质与映射矩阵
- 卡／技能种子：Counterstrike `c5bff6ad-5d13-4cf3-9941-98f3cf913949`；怪物 Wild Boar（**monsters 表**）`5d779101-3fa5-44b7-b885-f8bb9b0f7438`。全体 1146 物品／498 技能／166 怪物均为本组横向输入。
- 类型／字段：`TCardItem/TCardSkill`、怪物 Player/Hand/Skills、`Tiers/AbilityIds/AuraIds/Enchantments`；Legendary 查询规则、稀疏数组、未知 ID。
- 文件：`tools/rule_inventory.py`、`T/NativeCoverageRunner.cs`、`E/CardMapper.cs`、`E/LiveBoard.cs`、`E/LiveMonster.cs`、`E/SkillMapper.cs` 品质段、`P/LiveSkillReader.cs`、`P/LiveCardDetailReader.cs`。
- 交付：全量 ID 不遗漏、完整模板可复现、读取错误携带原始路径；补矩阵盲区（实时属性、技能 Legendary、附魔警告保留、其他英雄／混合棋盘），不能只输出通过率。
- 验收：复用 `NativeSkillTier/NativeLegendaryTier/LiveSkillReader`；同名 Wild Boar 物品不可当怪物；冻结源码／数据库／矩阵哈希。G00 不更改其他组的效果语义。
- 2026-10-25 本轮冻结：物品入口阻断清零（0 物品／2 技能 DATA）；46 物品保留缺定义警告；`data/g00_baseline.json` 已含 387678 组合矩阵、166 怪物读取矩阵、56／64／164 缺定义、9＋2 条索引外规则与源码哈希；`python tools/g00_audit.py --check` 通过。状态仍为 PARTIAL，不代替其他组语义验收，详见 §9 G00-01／G00-02。

#### G01 — 位置、全体目标与槽位
- 种子：Weaponized Core `9131cedf-e5e7-4a23-a90e-2576048419e8`；Gamma Ray `0229faa7-2eec-4746-9134-ade4f3aebe45`；Omega Ray `c1c4f8fc-548d-4ae6-bfca-1c7ea2aca212`；Kusarigama `cae6a187-7e4e-40fb-989b-4b84d59bda24`；Enervating Sigil `2e2815d0-fa7c-45ab-a0f2-65ebe1c815d8`。
- 类型／参数：`TTargetCardPositional/Section/Random/XMost/TriggerSource/TriggerTarget/Occupying`；AllLeftCards/AllRightCards/AllHands/IncludeOrigin/TargetCount。
- 文件：`E/SkillMapper.cs` 目标与前提、`E/Effects.cs` 目标集、`E/NativeDynamicAuras.cs` 和 `E/NativeCardAbilityMapper.cs` 目标集；`T/PositionalRuleTests.cs`。
- 预期：武装核心自身＋全部右侧武器增伤，左侧远近物品均触发充能；最近邻不能越过不合条件的牌；数量明确，零目标无作用，敌我不混；AllHands 和 Occupying 必须统一主动、被动、光环、数值查询。
- 独立交付：目标集合契约＋参数矩阵。4.1s 首次出手是现有固定测试棋盘结果，不得写为任何武装核心的固定出手时间。

#### G02 — 充能、加速／减速／冻结、飞行与弹药
- 种子：Launcher Core `9fafc0a5-6675-4e17-9457-590f13354a39`；Dryad `cda40a45-8703-4347-8f4e-7fd460b2ac57`；Cog `ca77281c-f912-4db3-86ce-64842a4ccdaa`；Drowsdrop `a655fb50-b9e1-41ec-b658-07459014f96c`。
- 类型：`TActionCardCharge/Haste/Slow/Freeze/Reload/FlyingStart/FlyingStop/FlyingToggle`、对应 `TTriggerOnCardPerformed*`、`TTriggerOnItemUsed`。
- 文件：`E/Effects.cs`、`E/NativeItemMapper.cs`、`E/SkillMapper.cs` 数值/动作、`E/Battle.cs`；`T/NativeItemMapperTests.cs`、`T/NativeRuntimeRulesTests.cs`、`T/AuditRegressionTests.cs`。
- 预期：成长后的秒数与目标数在执行时取值；Dryad 暴怒后 1s→2s；自己使用不多触发，飞行与伙伴标签 OR 不重复计数；装满／增弹药分开，多重受弹药限制。
- 独立交付：毫秒到秒转换、状态重叠／到期、受影响卡的事件主体、控制改变实际时间轴的回归；抗性数值接口与 G07 对接。

#### G03 — 触发来源、受体、前提与次数限制
- 种子：Hunting Hawk `0941d7dd-1264-42c5-b334-6cc5d694571b`；Boosted Saucer `0f96e2e4-2bd8-4fcf-aaaf-e4e0ee5cb627`；Wolf Mask `22c8b252-e36d-4564-9588-06febd4b358d`；Bloodhound `13989ad3-2122-4d86-a323-bb90fda8360b`；Frozen Secrets `1252064a-3bf4-478e-9326-946677ecc8b3`。
- 类型：`TTriggerOnItemUsed/OnBeforeItemUsed/OnCardStartedFlying/OnCardPerformed*`、`TPrerequisiteCardCount` 及复合／次数前提；Source、Subject、Target、Priority。
- 文件：`E/SkillMapper.cs` 触发表／主体／前提、`E/Triggers.cs`、`E/BattleContext.cs`、必要的 `E/Battle.cs` 事件调度；`T/SkillsTests.cs`、`T/NativeRuleExpansionTests.cs`。
- 预期：消除现有“指定行动受体”“触发来源限制”“条件或次数”拒绝，精确命中源和受体；首次／N 次／每战次数分离，开场重置；同帧优先级有原生证据。
- 独立交付：事件负载契约与同步／下一帧顺序测试；不靠事件总数抵消重复触发，必须比较来源、目标和具体帧。
- onUse 按 EffectPriority 分帧（2026-10-25，回放 ebde2082 实证）：同一 `TTriggerOnItemUsed` 的双能力中，**Immediate** 优先级的被动与被用物品自身效果**同帧**结算（Quick Ignition/Grease Fire 的 Immediate modifyAttribute 帧 76），Medium 等其余优先级**下一帧**（Haste/Burn 帧 77）；与 170666bf 的 Matchbox Medium 充能（使用+1 帧）一致。实现：`Triggers.DrainImmediateOnUse`（出手管线内 onUse 入队后同帧排空 Immediate 部分）+ `PartitionByPass`（同帧完成全部前提判定，非 Immediate 匹配快照进 `DeferredMatches` 下一帧只执行不重评——Counterstrike 型 “Immediate 计数 +1 不能反噬 Medium 判定” 语义保持）。测试 `G12Determinism`。ebde2082 的帧 76–78 已逐帧对齐，最早分叉后移到帧 79（Boosted Saucer 冷却/充能的下一层机器人板级联）。

#### G04 — 运行时数值、属性、标签与计数器
- 种子：Streaming Setup `2a0624d0-8257-42f9-8e9d-bbe0a5218b44`；Spacescraper `40d8b44a-8e2f-4081-8163-043d76ddbafa`；Geyser `77df36bf-a1d0-4815-9f07-5c330542e76a`；Pauldrons `8666a1de-066f-47c4-9d9b-87eea7d9f25a`。
- 类型：`TReferenceValueCardAttribute/CardAttributeAggregate/PlayerAttribute/AttributeChange/CardTagCount`、`TActionCardModifyAttribute/AddTagsList/AddTagsBySource/AddTagsRandom`、`Custom_N`、Modifier。
- 文件：`E/Types.cs`、`E/SkillMapper.cs`、`E/Effects.cs`、`E/ItemAttributes.cs`、`E/NativeDynamicAuras.cs` 数值入口；`T/NativeRuntimeRulesTests.cs`。
- 预期：其他卡牌与聚合引用不固定成开场数；触发变化量随嵌套正确恢复；计数器驱动前提；标签实时变化；未支持属性不能只存入字典却不改变实际效果。
- 独立交付：统一求值和取整接口＋缺值/负值/零/比例/时效回滚测试。不假定 Unscaled 与普通值等价，不拿随机均值替代随机结果。
- 2026-10-25 全量验收：150/150 G04 主责模板逐个映射并通过运行时冒烟；G04 机制相关 unsupported=0；仅 Masterpiece 缺能力定义告警。G04 定向与整包 `compile_tests.sh` 均 `OK`；真实回放见 §9 G04-02。

#### G05 — 动态光环与持续冷却
- 种子：Passive Power `22e88b23-7497-4cb9-92a9-6d5e6bde6373`；Rate Limiter `b607f75b-e245-4e4a-809e-899fcdd1e6e5`；Force Field `85f59c88-c391-4a03-8c3b-f312518e81de`；Spiky Shield `8ec1c3f9-6220-431b-b3b0-52b3be2e20f5`。
- 类型：`TAuraActionCardModifyAttribute/PlayerModifyAttribute`、`TReferenceValue*`、`TPrerequisitePlayer`；Multiply、Set、FlatCooldownReduction、PercentCooldownReduction。
- 文件：`E/NativeDynamicAuras.cs`、`E/NativeCardAbilityMapper.cs`、`E/ItemAttributes.cs`、`E/Battle.cs`；`T/DynamicAuraTests.cs`、`T/NativeSkillDynamicAuraTests.cs`、`T/G12DeterminismTests.cs`。
- 预期：补引用倍率、敌方目标和动态人物光环；先基础再派生，实时快照只剥离一次；条件反复开关不累乘；光环来源离场后撤销；持续减 CD 改变实际出手而非仅显示。
- 独立交付：可逆派生层／基底契约＋有效 CD 与剩余进度的原生对账。当前“先固定后百分比”的公式是待校准实现，不能写作已证实游戏规则；100% 缩减无法反推时必须告警。
- 逐次叠加光环（2026-10-25，回放 170666bf 实证）：`WorksIn=CombatOnly` 且无前提的 `SellPrice Add` 光环（全库 7 个：Subscraper／Abacus／Heirlooms／Balcony 等）按来源**每次使用**重执行并永久叠加，不进连续派生层、不静态烘焙；同源引用这些被写属性的无前提 Add 光环按“新贡献−旧贡献”增量重算（Heirlooms 光环 1：ShieldApplyAmount += SellPrice×Custom_0，每次开火 +10）。实现：`NativeDynamicAuras.ClassifyUseAuras`（映射期分类到 `CardDef.NativeUseAuras`）+ `ExecuteUseAuras`（出手管线内在自身效果之后、onUse 派发之前执行，逐轮 Refresh 保证引用读到新值）。带前提的条件光环与 Multiply 类 CombatOnly 光环（Spacescraper ×3、Master Salesman ×2、Skyscraper ×2）没有逐次叠加证据，仍在派生层。Idol of Decay 的 q1（每次刷新 Poison +4）是当前模板缺失的运行时效果，属 G00 数据缺口，不能由引擎重建。
- 经济读取光环（2026-10-25 复核）：引擎侧已支持 `TReferenceValuePlayerAttribute(Income/Gold)`（`PlayerAttributeValue` 从 `hero.Attributes` 读取，未提供按 0）；Ring King Gauntlets（Damage += Income×Custom_0）等 5 张卡的阻塞在**数据**——侧车 `battle_snapshots` 只有手牌/技能 JSON，OpeningMessage 开场属性只有 Health，语料 11 场含该卡但均无 Income/Gold 值。live 快照的 DamageAmount 已烘焙开场 Income 贡献且 Initialize 反推不重复扣减，首发出手数值正确；战斗中 Income 增长（其光环 2 每次使用 +Custom_1 Income）不可验证。归 DATA，等 BPP 侧车携带经济属性或运行时样本。

#### G06 — 人物属性、怒气及双方开场状态
- 种子：Warpaint `153dfd6d-7dc8-4386-9703-ea6c99f92d4a`；Vengeful Sigil `05a32402-598c-4bb5-a280-1a84f0ab84fa`；Burning Shield `7931571a-3d32-41e8-bf7b-ca6b4d1b7a3d`；Toxic Shield `a9b441e1-5824-4855-9359-2ab4643d9ff5`；怪物 Wild Boar 见 G00。
- 类型：`TActionPlayerRageApply/ModifyAttribute`、`TTriggerOnPlayerEnraged/EnrageEnded/AttributeChanged`、`TPrerequisitePlayer`；HealthMax/RageMax/RageApplyAmount/EnragedDurationMax。
- 文件：`E/BattleContext.cs`、`E/MonsterOpponentMapper.cs`、`E/SkillMapper.cs`、`E/Effects.cs`、`P/LiveBoardReader.cs`、`P/ActionAxisPlugin.cs`；`T/MonsterOpponentTests.cs`、`T/AuditRegressionTests.cs`。
- 预期：Wild Boar 模板上限 50 不回落默认 100；玩家／怪物上限、再生、英雄身份正确；Shiny 怒气数值光环真正影响后续动作；人物生命变化与普通治疗分开。
- 独立交付：开场配置与事件属性契约，纯模板／实战快照两条路径；不能把战斗中临时怒气直接当新战斗开场。

#### G07 — 附魔、抗性、免疫与暴击
- 种子：Dishwasher `027ef534-d047-4e18-8496-d9fd4773e15d`／Radiant；Security Blanket `02d7a406-a6eb-4b46-8975-1f69b0fde428`／Radiant；Spirit Diffuser `1b79b3e5-5870-48f7-a877-add0721f9ab6`；Gamma Ray／Deadly、Cog／Golden 见 G01/G02。
- 类型：嵌套 `Enchantments.*.Abilities/Auras/Attributes`、`TActionCardEnchant/EnchantRandom/EnchantRemove`、`TCardConditionalCanCrit/HasEnchantment/EnchantmentEligible`；PercentSlowReduction/PercentFreezeReduction/DestroyImmunity/DamageCrit/SellPrice。
- 文件：`E/EnchantmentMapper.cs`、`E/NativeCardAbilityMapper.cs`、`E/NativeItemMapper.cs`、`E/Effects.cs`、`E/Transforms.cs` 对接；`T/EnchantmentMapperTests.cs`、`T/NativeCanCritTests.cs`。
- 预期：主动、被动与光环都读所选附魔；免疫确实拦截相应效果，战斗中附魔生效／移除／冲突可解释；售价光环不阻断复制，同时保留售价被其他效果引用的值。
- 独立交付：每个实际附魔枚举（不是所有卡随意组合不存在的附魔）完整入口＋行为；校准八卡不能清空附魔被动后仍标完整。已有免疫原值捕获不等于免疫实现。

#### G08 — 场上复制变形
- 种子：3D Printer `723880db-8a97-4833-a9a1-80c75cde4c17`；Mirror `3172cda6-0ebd-4663-a6d6-f72a3d7c4b2c`；Hologram Projector `1d354dcf-8736-4bab-9dc1-7d9054d6c4d4`；Quicksilver `4fb9fa4a-6250-4132-a7cb-9a4b874b0bdc`；Virus `020a0ec0-21e6-41af-899f-063573ba9ca5`；Green Screen `62c10400-c215-4edb-a410-5f253b8b863a`；Blender `0b13735f-7382-460d-a054-710e1a2a3799`；Grabinator `bd46ceff-a181-49f2-ac60-a769a062cb6d`。
- 类型：`TActionCardTransform`、`TSpawnContextQuery`、`TTriggerOnCardTransformed`、相关位置／品质前提。
- 文件：`E/Transforms.cs`、`E/Battle.cs`、`E/BattleContext.cs`、`E/NativeCardAbilityMapper.cs`、`E/SkillMapper.cs`、`E/Effects.cs`、`E/Types.cs`、`P/LiveMonsterReader.cs`、`P/ActionAxisPlugin.cs`；`T/TransformTests.cs`、`T/AuditRegressionTests.cs`、`T/PositionalRuleTests.cs`、`T/G08TransformTests.cs`（新增）、`tests/fixtures/native_g08_transforms.json`（live DB 真实模板）。
- 预期：8 张 A 类场上变形全部有入口＋行为；打印机左侧 Small 三副本使用原三格，镜子取紧邻 Medium（live 不排除 Legendary），Hologram/Quicksilver 排除同一模板的所有副本（Quicksilver 另排除 Legendary），Blender 固定 Slushee 供体，Grabinator 跨侧复制敌方最左 Medium；多个副本不同实例 ID，事件保留原来源，旧队列代际失效；成长／附魔／临时状态继承以原生证据定。
- 原生对账（2026-10-25）：live DB `GameData.db` 的 `SpawnContext.Groups` 是**场上供体筛选**，不是场外卡池；已按 `TSpawnFilterTarget/IdList`、`Sequential/Random`、`IgnoreTierTable/AllowDuplicates/IgnoreHero/InheritTier/InheritEnchantment` 解析，未知字段继续 fail-closed。200 份 `CombatReplays` + 5 份本地样本全部 `Version=1` 可解析；25 个 code=9 事件中 **20 个 G08 事件**，覆盖 Mirror 4、Hologram 2、Quicksilver 2、3D Printer 1、Virus 11（新副本）。关键事实：`07f044ee` Gold Mirror→Gold The Big One；`40bcb27a` Gold Hologram→Gold Gamma Ray（供体 Silver，Poison 4→6）；`ebde2082` Diamond 3D Printer→3 份 Diamond Event Poster（槽 3/4/5、ChargeTargets=2）；`86f51187` Gold Quicksilver→Gold Scalpel（供体 Diamond，Custom_0 20 vs 25，CD 4950/5000）；`921ccdd8`/`ab8439ff` Mirror→Legendary Blazehowl Warden/Dragon Heart；`82fb1bec` 单局 6 次 Virus 感染。证据脚本 `tmp_g08/g08_reconcile.py` 与输出 `tmp_g08/g08_reconciliation.json`（20 条，0 failures），明细 `tmp_g08/analysis/replay_facts_all.json`。
- 实现：`CopyAtTier` 从供体原生模板按来源品质重建并附魔；`Apply` 不再继承供体实时属性／临时标签，副本冷却=新 CDMax−被变形卡已流逝进度；`FindDonor` 支持左侧/随机/同模板排除/敌方最左 Medium；`FindFixedDonor` 从 `NativeTemplates`（插件 `GetCardById`）或 `SpawnPool` 取固定模板；`ApplyFixed` 处理 Blender→Slushee。`Green Screen` 先随机附魔自身，副本强制使用来源附魔；`Grabinator` 支持 `TTriggerOnBeforeCardDestroyed` + 跨侧供体。
- 场外卡池：`Alembic`、`Assembly Line`、`Catalyst`、`Echo Crystal`、`Fairy Statue`、`Hacksaw`、`Potion Distillery`、`Potion Potion`、`Recycling Bin` 仍归 G11 OUT；`Hacksaw/Potion Potion/Recycling Bin` 在战斗入口 fail-closed，避免把随机池猜成固定模板。`TCardEncounterStep`／DEBUG 的 21 个 D 类不在战斗 G08。
- 独立交付：真实前提到新实例完整链路＋前后时间轴历史。8 张 A 类均有测试；Virus 已不再因 `TTargetCardRandom/OpponentHand` 前提整卡拒绝。
- 2026-10-25 晚复核（P1 逐事件比较，`replay_batch_audit.py` + 侧车 DB）：
  - **084bd840 → MATCH（可验证差异 0）**：v9 记录的 “Virus 变形目标顺序（原生 slot4、模拟器 slot2）DIFF” 已不存在。
  - **86f51187 → DIFF（Quicksilver 战）**：根因重新归因为**随机供体分叉**，不是副本冷却公式错误。帧 60 Quicksilver（自身目标变形、事件无 randomTarget）两侧同帧变形但供体不同（原生选 CDMax 5000 的 Scalpel，模拟选 CDMax 4000 的卡）；原生副本 4950/5000 = 供体 CDMax − 50ms 自开火起的流逝，与引擎现行公式一致（打印机 ebde2082 帧 61 副本 2950/3000 ✓、Mirror 07f044ee 13950/14000 ✓）。比较器已补判定：同帧两侧都变形而供体模板集合不相交 → 该实例谱系自变形帧起标 `randomTransform`（`random_transform_scope` 扩展 + transform_scope 合并进 card_scope），130 帧冷却差异正确归为不可验证；新增 2 个单元测试（供体分叉标 unverified／供体一致保持确定性）。全量 321 场判定不变（314/7）、0 回归，2 场大幅重分类（9370ffbd 1388→678、bdb6dc55 883→503）。
  - **ebde2082 → DIFF（3D Printer 战）**：当前最早分叉在帧 76–77——`mXeoDWB` 使用后，原生技能被动（W795pRs/CM5LU1I）的 modifyAttribute **同帧**（76）结算、burn/haste 下一帧（77），模拟全部 +1 帧；随后全板（燃烧炸弹/机器人组/护目镜/摇杆/卡尺/飞碟，onset≈150）级联漂移。这属于 G03/G06 的技能被动调度域（同帧 vs +1 帧按效果类型/优先级区分），非 G08 实例模型问题；240ms 副本残差项在更早分叉未解前无法独立观测。打印机变形本身（帧 61、副本 2950/3000）与引擎一致。
- 剩余风险：副本静态“加标签”光环（Event Poster 相邻变 Dragon）未在真实棋盘重放；供体附魔与来源附魔冲突的 “if able” 语义仍缺回放；游戏内重启后的 UI／拖回历史 D 门禁未做。不能把本组标为 VERIFIED。

#### G09 — 生命周期、强制施放与复活
- 种子：Abducted Cow `03f90f0b-088f-453d-bbf7-ebb5e777afe2`；Drone Crusher `3c7807ef-0993-4b33-8a34-441f7cde0666`；Fiery Rebirth `09c59ebf-d2f9-4862-9c30-396b9ddfdd89`。
- 类型：`TActionCardTransformDestroyed/Destroy/Disable/Repair/ForceUse`、`TTriggerOnBeforeCardDestroyed/OnCardPerformedDestruction/OnPlayerDied`、`TActionPlayerReviveHeal`。
- 文件：`E/Battle.cs`、`E/Effects.cs`、`E/Triggers.cs`、`E/Transforms.cs`；`T/NativeRuntimeRulesTests.cs`、`T/AuditRegressionTests.cs`。
- 预期：明确 TransformDestroyed 的真实主体和时点，不能按英文名猜“变形卡被毁”；已离场／禁用卡不继续排队，多重、弹药和强制使用相互一致；复活次数按规则，不用全局一次假设替代所有技能。
- 独立交付：生命周期事件序列与实例代际，包含同帧相互摧毁、前置回血、复活后再死、嵌套 ForceUse 的安全上限。场外生成部分由 G11 排除，不连带抹去战斗分支。

#### G10 — 伤害、DoT、护盾、回复与输出归因
- 种子：Trained Spider `014d9c98-e823-443c-98a3-6367ab81c956`；Soldering Gun `666f4084-48cc-4558-b678-a46ba18c1bf2`；Force Field 见 G05；其他基础卡默认由本组兜住行为验收。
- 类型：`TActionPlayerDamage/Heal/ShieldApply/BurnApply/PoisonApply/RegenApply`、`TTriggerOnCardPerformedDamage/Heal/OverHeal`；暴击、吸血、减伤、过量、真实命中。
- 文件：`E/Effects.cs`、`E/Battle.cs`、`E/BattleDamageSeries.cs`、`E/Aggregate.cs`、`E/NativeCombatReplayData.cs`；`T/DummyDamageTests.cs`、`T/NativeMechanicsTests.cs`、`T/NativeCombatReplayTests.cs`、`T/G10DamageLedgerTests.cs`、`T/G10TaskIndexTests.cs`；夹具 `T/fixtures/native_g10_templates.json`（62 张 G10 主责模板）、`T/fixtures/native_g10_replay.json`（真实回放 084bd840 敌方观测侧）、`T/fixtures/native_g10_replay_player.json`（同回放玩家侧）、`T/fixtures/native_g10_shard.json`（G03/G10 关联卡 Shard of Obsidian）。
- 预期：我方实际输出只计真实命中 HP/盾，不把治疗／生命上限变化当伤害；理论输出、有效输出、净失血分列；DoT 时间／衰减／归因、暴击与吸血按当前原生版本。
- 独立交付：生命与护盾账本守恒、不跨阵营归因、多来源状态归因的对账；木桩与怪物路径不同但规则口径一致；`primary_group=G10` 的 62 张模板全部进入索引回归，真实回放只做字段级对账，不把无来源字段的原生总量猜给某张卡。

#### G11 — 余项、范围排除与缺数据取证
- 2026-10-25 已完成 36 个主责模板的入口验收：见 §6.1.1–6.1.7 与 G11-03。结论为 3 项 IN、4 项 DATA（入口 mapped-warning，定义语义仍缺）、16 项 OUT、13 项 OUT+DATA；36/36 入口 `mapped`，`G11Sandstorm`／`G11Coverage` 与整包测试通过；未部署、未提交。
- 种子：Potion Distillery `028d89ac-b4c4-4d3c-ba31-71b57aa33bed`；Windmill `ff06b63a-15ee-49be-80b1-5fb52b4e1616`；Bottled Tornado `c8a322c7-a8f9-49e3-90e5-3a348d0fe5fc`。
- 类型：`TActionGame*`、`TTriggerOnEncounter*/OnCardSold/OnCardPurchased/OnDayStarted/OnHourStarted`、`TPrerequisiteRun`、`TRunConditional*`、沙暴、未归类类型。
- 文件：`tools/rule_inventory.py`、`E/SkillMapper.cs` 的 `IsNonCombatTrigger`／`MapAction`、`E/Effects.cs`、`E/Battle.cs`、`E/BattleContext.cs`、`E/Types.cs`、`E/NativeCardAbilityMapper.cs`、`T/G11SandstormTests.cs`、`T/G11CoverageTests.cs`、`T/fixtures/native_g11_sandstorm.json`、`T/fixtures/native_g11_all.json`、`T/Program.cs`、诊断证据；沙暴执行与 G03/G10 接口协调。
- 预期：每个遗漏都有具名主责；非战斗分支列 OUT 依据，缺数据列 DATA 清单与下一步动作；混合卡保留战斗分支；沙暴开始与默认伤害分开。
- 独立交付：零“无主模板”、36/36 入口可映射、可复核排除清单、沙暴开始与默认伤害实现。不能用 `TSpawnContextQuery` 或“剩余少量”概括所有卡池情况，也不能把矩阵 `mapped` 当成卡池语义已实现。

#### G12 — 真实回放对账、UI 与发布门禁
- 回归种子：武装核心、打印机、Dryad、Trained Spider（具体 ID 见上）；全集均须被证据索引覆盖。实战种子 `9bbabba4a216484d83d068a149811259` **不保证包含这些卡**，必须逐条声明它实际验证什么。
- 类型：原生帧 Events、CardUpdates、人物 HealthAdjustments、变形 lineage；内部 `BattleEvent/ItemSnapshot/BattleItemView/EffectStat` 与全部规则族。
- 文件：`tools/native_replay_ledger.py`、`captures/diagnostics/read_bpp_replay.py`、`T/NativeCombatReplayTests.cs`、待新增的比较器／对账用例、`P/NativeCombatReplayReader.cs`、`P/Ui/ActionAxisPanel.cs`、`P/Ui/CardTile.cs`、构建脚本。
- 预期：开场重建、版本化动作码、实例映射和逐事件差异报告可复跑；旧形态／新副本／离场时刻按播放头显示，统计不丢历史；明确未验证区间。
- 独立交付：先交账本格式和一个最小比较用例，允许其他组并行接入；最终交所有门禁证据、同一套 DLL 哈希、加载版本和游戏内验收。不要把导出器成功当比较成功。

### 7.3 并行边界、依赖与文件锁

- 独立交付指“各组有明确输入／输出／测试包”，**不意味着共享核心文件可以同时修改**。每人使用独立分支／worktree；共享目录中只有当前文件锁持有人可写，其他人先交模板、失败测试和设计契约。
- 集成人唯一负责 `Types.cs`、共享 `SkillMapper.cs/Effects.cs/Triggers.cs/Battle.cs` 的合并排期，`tests/Program.cs` 注册、生成 CSV／矩阵汇总和两个发布 DLL。组员先写本组测试，再按已批准的小补丁接口集成；禁止全文格式化抢占其他组差异。
- 先行链：G00 冻结→G12 账本契约（立即开始）＋G01/G03/G04 接口→G02/G05/G06/G07→G08/G09→G10/G12 联合验收。这是集成顺序；调查、夹具与失败测试可并行，不能等全实现后才开始原生取证。
- 每个接口变更声明字段含义、单位、空值、默认值、阵营、实例身份与过期时点；涉及两个组必须双方审查，不能把测试通过作为接口批准。
- 不并发运行同一目录的 `compile_tests.sh`、矩阵检查和 DLL 构建；它们共享 `build/EngineTests.exe`／DLL，曾出现锁冲突。矩阵工具不会运行普通回归套件，二者都要跑。
- 只提交认领范围文件，不能用 `git add -A` 纳入他人的插件管理器／UI 改动。提交与部署须有用户授权；文档任务本身不授权任何发布操作。

当前占用表（初始未分配，不编造人名）：

| 工作单 | 主责组 | Owner／Reviewer | 分支或工作区 | 锁定文件／符号 | 基线／状态／下一步 |
| --- | --- | --- | --- | --- | --- |
| 待认领 | G00–G12 | —／— | — | 无 | 按组领取，先登记后编辑 |

状态机：`UNTRIAGED → READY → IN_PROGRESS → IMPLEMENTED → VERIFIED`；阻塞为 `BLOCKED(DATA/DEPENDENCY)`，排除为 `OUT`。`IMPLEMENTED` 只表示代码＋回归／构建通过；原生证据不足不能升 `VERIFIED`。每次工作停止必须更新下一步、失败命令和锁释放状态，不以“做了一半”结束记录。

<a id="验收规范"></a>
## 8. 验收规范与可直接执行的命令

### 8.1 四道门禁

| 门禁 | 要求 | 必须归档 |
| --- | --- | --- |
| A：规则与回归 | 完整模板入口＋最小行为正反例，C# 全套 `OK`，前端基线 102 项全绿；新增测试不能减少旧项 | 测试名称、注册位置、命令、退出码、摘要；报错不能通过过滤日志隐藏 |
| B：编译与交付物 | 完整 Unity 插件 `--compile-only` 成功；发布时两 DLL 来自同次构建且部署前后 SHA256 一致 | 源码基线、编译器／游戏／插件版本、DLL 源/目标哈希，已知警告；编译成功不等于部署 |
| C：原生一致性 | 每组至少一条对应规则的真实事件关键路径；发布基线至少一份完整本机 BPP 回放完成逐事件比对；所有新增规则族必须有覆盖证据 | 原回放哈希、模板哈希、输入配置、实例表、规范化原生账本、模拟账本、差异 JSON/CSV、命令与判定 |
| D：用户可见功能 | 游戏重启后实际加载该版本；玩家／怪物、规则警告、拖回变形前、出生后、离场后、冷却水位与输出一致 | 加载日志、操作步骤、截图或录屏路径、验收人；无运行环境记 BLOCKED，不写通过 |

完成定义：每个 IN 工作单五步链完整，A/B/C 通过；影响 UI 的单元还需 D。全部 IN 单元 VERIFIED 且 OUT/DATA 均有证据与批准，才能称“本轮范围完成”。仍有 IN 的 DATA 阻塞时只能发布带明确限制的部分版本，不能称全覆盖。

### 8.2 本机标准命令（PowerShell）

从仓库根 `d:\巴扎plus` 运行；路径不同替换 `$root`。PowerShell 不使用 `&&`，每条命令失败立即停；`npm.cmd` 避开执行策略拦截。系统 csc 为 .NET Framework 编译器，不使用 C#7 局部函数等未支持语法。

```powershell
$root = 'd:\巴扎plus'
$bash = 'C:\Program Files\Git\bin\bash.exe'
& $bash "$root/tools/actionaxis_plugin/compile_tests.sh"
if ($LASTEXITCODE -ne 0) { throw 'C# 回归失败' }
npm.cmd --prefix $root test -- --run
if ($LASTEXITCODE -ne 0) { throw '前端回归失败' }
& $bash "$root/tools/build_actionaxis_plugin.sh" --compile-only
if ($LASTEXITCODE -ne 0) { throw '完整插件编译失败' }
git -C $root diff --check
if ($LASTEXITCODE -ne 0) { throw '补丁空白检查失败' }
```

记录 stdout/stderr 与退出码；Unity 已存在的过时 API warning 可登记，但新增 warning 要解释。C# 回归不包含全部 Unity UI，编译不能省略。

矩阵／分组复跑（仓库根）：

```powershell
python tools/rule_inventory.py --mapping-input tools/actionaxis_plugin/build/native_mapping_templates.jsonl
if ($LASTEXITCODE -ne 0) { throw '原生模板导出失败' }
python tools/rule_inventory.py --monster-input tools/actionaxis_plugin/build/native_monsters.jsonl
if ($LASTEXITCODE -ne 0) { throw '怪物模板导出失败' }
python tools/rule_inventory.py --monster-cards tools/actionaxis_plugin/build/native_monster_cards.jsonl
if ($LASTEXITCODE -ne 0) { throw '怪物卡牌定义导出失败' }
& tools/actionaxis_plugin/build/EngineTests.exe --coverage tools/actionaxis_plugin/build/native_mapping_templates.jsonl tools/actionaxis_plugin/build/native_mapping_report.tsv
if ($LASTEXITCODE -ne 0) { throw '矩阵检查进程失败' }
& tools/actionaxis_plugin/build/EngineTests.exe --monsters tools/actionaxis_plugin/build/native_monsters.jsonl tools/actionaxis_plugin/build/native_monster_cards.jsonl tools/actionaxis_plugin/build/native_monster_report.tsv
if ($LASTEXITCODE -ne 0) { throw '怪物读取矩阵失败' }
python tools/rule_inventory.py --mapping-report tools/actionaxis_plugin/build/native_mapping_report.tsv
if ($LASTEXITCODE -ne 0) { throw '矩阵汇总失败' }
python tools/rule_inventory.py --work-index docs/native-rule-task-index.csv
if ($LASTEXITCODE -ne 0) { throw '派单索引验证失败' }
python -m unittest tools.test_rule_workplan -v
if ($LASTEXITCODE -ne 0) { throw '派单工具回归失败' }
python -m unittest tools.test_g00_audit -v
if ($LASTEXITCODE -ne 0) { throw 'G00 审计回归失败' }
python tools/g00_audit.py --check
if ($LASTEXITCODE -ne 0) { throw 'G00 冻结基线不一致' }
```

`native_mapping_report.tsv`／`native_monster_report.tsv` 位于共享 `build/`；复跑前确认没有其它矩阵进程同时写入，否则会读到半成品。`g00_audit.py --check` 比较数据库、索引、夹具、矩阵、怪物读取与源码哈希，不重新执行效果语义。

注意：`NativeCoverageRunner` 有 unsupported 仍可能返回 0。验收看**逐行差异**而非仅退出码；本组 IN 拒绝必须归零且已通过组合不能回退，OUT／DATA 单独统计。每行只暴露最先阻断原因，修一处后必须再跑。

可用工具与尚缺工具：`--fixture-names/--sample/--mapping-input/--monster-input/--monster-cards/--mapping-report`、`EngineTests.exe --coverage/--monsters` 与 `tools/g00_audit.py` 已有；`--work-index` 只生成派单索引；原生事件自动比较 CLI 已由 G12 建立（`tools/native_replay_compare.py`，输入为原生账本与 `EngineTests.exe --ledger` 模拟账本）。它只给差异，不给“全部通过”；当前两份样本均为 DIFF。

### 8.3 部署与 DLL 一致性（仅获授权且游戏已退出时）

```powershell
$game = 'D:\Program Files (x86)\Steam\steamapps\common\The Bazaar'
if (Get-Process TheBazaar -ErrorAction SilentlyContinue) { throw '先正常退出游戏；不强杀进程' }
$env:GAME_DIR = $game
& $bash "$root/tools/build_actionaxis_plugin.sh" --deploy
if ($LASTEXITCODE -ne 0) { throw '部署失败' }
foreach ($name in @('ActionAxisEngine.dll', 'ActionAxisPlugin.dll')) {
    $sourceHash = (Get-FileHash "$root/tools/actionaxis_plugin/build/$name" -Algorithm SHA256).Hash
    $targetHash = (Get-FileHash "$game/BepInEx/plugins/$name" -Algorithm SHA256).Hash
    if ($sourceHash -ne $targetHash) { throw "DLL 不一致：$name" }
    "$name SHA256=$sourceHash"
}
```

`--deploy` 会重新编译，故部署哈希取**部署这次构建**的源文件；绝不拼接旧 Engine 与新 Plugin。重启后核对 BepInEx 加载版本，不能只看目录中的版本号。未部署记录 `DEPLOY=未执行`，不是“用户重启即可生效”。

### 8.4 逐事件对账：最低证据与比较规则

1. **选取回放**：本机已确认存在 `<GameDir>/BazaarPlusPlusV5/CombatReplays/9bbabba4a216484d83d068a149811259.payload.mpack.gz`，此前解码为 581 帧／338 事件。记录真实路径、文件 SHA256、游戏版本与回放版本，不公开账号、令牌等隐私。
2. **导出**：下例输出位于已存在的 build 目录，不覆盖原回放。`tools/native_replay_ledger.py` 按 `tools/native_replay_contract_v1.json` 的事件码／DTO 字段名解析，不再按 tuple 长度猜字段；契约版本或回放 `Version` 不在支持列表时退出码 2，未知字段保留为 UNKNOWN。

```powershell
$root = 'd:\巴扎plus'
$game = 'D:\Program Files (x86)\Steam\steamapps\common\The Bazaar'
$replay = "$game/BazaarPlusPlusV5/CombatReplays/9bbabba4a216484d83d068a149811259.payload.mpack.gz"
Get-FileHash $replay -Algorithm SHA256
python "$root/tools/native_replay_ledger.py" $replay --json "$root/tools/actionaxis_plugin/build/native_mapping_ledger.json"
if ($LASTEXITCODE -ne 0) { throw '回放解码失败' }
```

3. **重建输入**：双方开场物品实例 ID／模板 ID／槽位／品质／附魔／已计算属性、技能、人物 HP/盾/再生/怒气上限、规则版本及随机条件；缺失字段必须 UNKNOWN，不能拿当前战斗结束后的棋盘替开场。
4. **身份对齐**：规范化 `side + slot + generation + opening root template`，保留变形父子谱系、出生／离场时刻；原始 instanceId 只用于查表，不能只用模板 ID 匹配多个副本，也不能把 `Stash` 物品当成参战卡。
5. **比较记录**：`frame/time, phase, source, target, ability/effect, kind, value, HP/Shield, cooldown, state`。至少断言首两次主动、一次被动和该任务边界（控制改变出手、状态切换、变形或伤害归因）。使用的回放没有该行为时换样本，不造覆盖。
6. **容差**：双方统一到 50ms 原生帧，确定性事件默认同帧；整数数值／取整后 HP/盾应完全相同；仅浮点表示误差允许 `1e-6`。确有“准备帧→执行帧”差异时先归一语义再比较，不用全局 ±1 帧掩盖 bug。同帧仅互不依赖的事件可按多重集比较，数量不能丢失。
7. **随机性**：可重建种子／选取顺序则严格比对；不可重建时标该分支 UNVERIFIED，另取确定性关键路径样本。目标集合合法性／分布测试不是逐事件一致；不能强行排序或改种子追到相同输出。
8. **判定报告**：输出 matched/missing/extra/value-mismatch/time-mismatch/unknown 各数量、首个差异的前后至少两帧、复跑命令；关键路径出现未知或差异即不通过。已知差异清单要有 Owner，不计为 matched。

```powershell
$root = 'd:\巴扎plus'
python "$root/tools/native_replay_ledger.py" $replay --json "$root/tmp_g12/ledger.json" --engine-fixture "$root/tmp_g12/fixture.json"
if ($LASTEXITCODE -ne 0) { throw '原生账本导出失败' }
& "$root/tools/actionaxis_plugin/build/EngineTests.exe" --ledger "$root/tmp_g12/fixture.json" `
  "$root/tools/actionaxis_plugin/build/native_mapping_templates.jsonl" "$root/src/data/cards.json" "$root/tmp_g12/simulated.json"
if ($LASTEXITCODE -ne 0) { throw '模拟账本导出失败' }
python "$root/tools/native_replay_compare.py" "$root/tmp_g12/ledger.json" "$root/tmp_g12/simulated.json" `
  --json "$root/tmp_g12/diff.json" --csv "$root/tmp_g12/diff.csv"
if ($LASTEXITCODE -ne 0) { throw '逐事件对账未通过（0=MATCH，1=DIFF，2=UNKNOWN）' }
```

`EngineTests.exe --ledger` 是测试构建中的只读导出入口，不部署、不写游戏目录。比较器分别对 effects、health、cards、identity、outcome 五个通道计数；`unknown` 包含账本 unknown、模拟假设和契约外事件；`unverified` 是原生 `TTargetCardRandom` 等无法重建的随机目标分支。`--window-frames` 只能显式用于验证“准备帧→执行帧”语义，默认 0 帧，不能用它掩盖时间差。

全量复跑用 `python tools/native_replay_batch.py --out tmp_g12/batch_full`；它扫描游戏目录与 `D:\巴扎plus\CombatReplays`，对缺侧车/旧模板逐份标 BLOCKED，不把阻塞算 matched。开场审计用 `coverage_all.json` 的 `partialOpeningCooldown`，必须为 0。

`T/fixtures/native_replay_health.json` 明确是 **Health-only projection**：可做 HP／盾／回复／过量等账本关键路径回归，不能证明主动使用、触发目标、冷却或变形一致；更不能代替完整逐事件验收。`ParityTests` 中前后端模拟器互相比对同样不等于游戏原生对账。单份真实回放只是最低门槛，不足以覆盖全部卡牌；最终证据表须逐规则族列回放与帧区间。

### 8.5 必填交接模板

以下模板复制到本文件 §9 末尾；不得只写“全绿／完成”。实现、测试、文档、部署、原生对账分开写未执行项。

```text
### YYYY-MM-DD HH:mm · Gxx-子单编号 · 标题
- Owner / Reviewer / 分支 / 基线提交或补丁哈希：
- 状态：IN_PROGRESS / IMPLEMENTED / VERIFIED / BLOCKED / OUT
- 范围：table + 模板ID + 名称 + 品质 + 附魔 + Ability/Aura路径 + 阵营/场景
- 原生证据：数据库/回放版本与SHA256；JSON路径；帧区间；已明确的语义
- 完成内容：五步分别对应什么；共享接口影响哪些关联组
- 改动文件：精确路径；未包含其他人的哪些工作区改动
- 新增测试：方法名、夹具路径、Program.cs注册名、失败→修复后的断言
- 验证：命令 / 退出码 / 核心结果 / 日志路径；未跑项和原因
- 原生对账：样本及区间 / 比较器版本 / 差异数量 / UNKNOWN / 证据路径
- 构建部署：版本 / 构建源DLL SHA256 / 目标DLL SHA256 / 加载版本；未部署写明
- 剩余风险与阻塞：具体规则、Owner、缺失字段、解除条件
- 下一条可执行动作：命令或文件/符号，预期结果；锁释放：是/否
```

旧日志只追加更正，不改写成事后全部完成。每次中断需把失败命令／待验证假设落盘；后来者基于同一哈希可复用已验事实，只需核查变更范围，不重新扫全部源码。

<a id="进度日志"></a>
## 9. 进度日志（历史记录，当前结论以 §1、§7、§8 为准）

> 每批完成后追加：日期 / 范围 / 改动文件 / 新增测试 / 验证结果 / 剩余风险。现有旧日志不因本次文档整理自动升级为 VERIFIED。

### 2026-10-24 · 阶段 0：规则清单基线

- 新增 `tools/rule_inventory.py`：扫描 `GameData.db.zip`，按 items / skills / enchantments / monsters 统计全部 `$type`；支持 `--schema`、`--sample <TYPE>`、`--markdown`。
- 生成 `docs/native-rule-inventory.md`（349 个类型组合），`data/rule_inventory.json` 作为可 diff 基线。
- 结论：物品/技能/附魔共用的核心是 10 个触发器、10 个动作、2 个光环动作；缺口集中在目标、条件、数值引用和生命周期动作。
- 验证：`python tools/rule_inventory.py --markdown docs/native-rule-inventory.md` 正常输出。

### 2026-10-24 · 阶段 1：战斗事件与常用动作

- 新增触发器映射（`SkillMapper`/`NativeSkillSupport` 共用）：`TTriggerOnBeforeItemUsed`、`TTriggerOnCardStartedFlying`、`TTriggerOnCardStartsFlying`、`TTriggerOnCardStoppedFlying`、`TTriggerOnCardPerformedDamage`、`TTriggerOnCardPerformedReload`、`TTriggerOnCardPerformedCharge`、`TTriggerOnCardPerformedOverHeal`。
- 新增动作：`TActionCardReload`（空值=装满）、`TActionCardFlyingStop`、`TActionPlayerBurnRemove`、`TActionPlayerPoisonRemove`、`TActionAnd`（复合，`Effect.Effects`）。
- 新增事件：`onBeforeUse`、`onFlyStop`、`onOverHeal`、`onCharge`；`onFly`/`onReload`/`onCharge` 的 payload 来源改为被影响的卡（`Subject=Self` 才能命中）。
- 目标：`TTargetCardPositional` 支持 Left/Right，并支持 `Origin=TriggerSource` 的左右/相邻目标；`TTargetCardSection` 放开 SelfBoard/OpponentBoard。
- 触发队列：新增 `Triggers.DrainEvent`，`onBeforeUse` 不会提前执行其他帧事件。
- 测试：新增 `tests/NativeRuleExpansionTests.cs`（6 组：BeforeUse、StartedFlying、Reload/FlyingStop、Cleanse/Composite、TriggerSource 位置、fail-closed），并更新 `NativePassivesWaitForFollowingFrame` 的 `onCharge` 队列预期。
- 验证：`compile_tests.sh` → `OK`；`npm test -- --run` → 37 文件 / 102 测试全绿；`build_actionaxis_plugin.sh --compile-only` 通过。
- 覆盖变化：脚本判定的「映射代码未出现的关键类型」从 64 降到 51（触发器/动作共 13 个）。
- 剩余风险：`Source` 字段非空的飞行触发、`Absolute*` 区域、`onOverHeal` 对再生的语义、`TActionAnd` 混合非战斗动作仍会整卡回退。

### 2026-10-24 · 阶段 2：条件与目标扩展

- 条件：新增 `TCardConditionalTier`、`TCardConditionalId`、`TCardConditionalHasEnchantment`、`TCardConditionalAttributeHighest`、`TCardConditionalAttributeLowest`，支持 `IsNot`；`ConditionSupported` 与 `CardMatches` 同步扩展。
- 附魔：`CardDef` 新增 `EnchantmentName`（`Clone` 复制），`CardMapper.Build` 从 `LiveCardEntry.Enchantment` 写入，`None`/空归一为 null。
- 排行：`CardMatches` 增加可选 `BattleContext`；最高/最低只在同侧比较，并列都算命中；`AttributeValue` 优先读 `LiveAttributes`，再回退 `Cooldown`。
- 目标：`SkillMapper.MapCardTarget` 支持 `TTargetCardXMost` 的 LeftMost/RightMost；`Effects` 新增 `TakeMost`，先按条件过滤、再处理 `ExcludeSelf`，最后按槽位取最左/最右。
- 光环：`NativeCardAbilityMapper` 在映射前把整侧模板属性铺进 `LiveAttributes`（实时覆盖优先），避免先映射的卡把尚未映射的邻居看成 0；`StaticTargetMatches` 改用已存在的 `ctx.Items`，不再重复 `InitBattle`。
- 测试：新增 `tests/NativeConditionTargetTests.cs`（6 组：Tier/Id/附魔、同侧排行、CardMapper 附魔名、XMost、静态光环排行、fail-closed）。
- 验证：`compile_tests.sh` → `OK`；`npm test -- --run` → 37 文件 / 102 测试全绿；`build_actionaxis_plugin.sh --compile-only` 通过。
- 覆盖变化：condition 未映射 8、value 8、duration 1、target 4；触发器/动作关键缺口仍为 51（属阶段 3/5）。
- 剩余：数值引用（`PlayerAttribute`/`CardTagCount`/`CardAttributeAggregate`）、`TriggerSource` 条件、`TriggerTarget` 目标、`Absolute*` 区域；已单独列入阶段 2b。

### 2026-10-24 · 阶段 2b/3/4/5：运行时数值、玩家属性/怒气、属性变化、标签与生命周期

- 运行时数值：`NumExpr` 新增 `cardAttribute`/`playerAttribute`/`tagCount`/`aggregate`/`cardCount`/`range`；`SkillMapper.MapValue` 代替映射期常量，`Effects.EvaluateNum` 结算时重算并支持 Modifier 链。伤害/治疗/护盾/DoT/充能全部受影响。
- 目标与条件：`TCardConditionalTriggerSource`、`SizeLargest/Smallest`、`CanCrit`、`HiddenTag`、任意属性名；`TTargetCardTriggerTarget`、`SelfNeighbors`、`TTargetPlayerAbsolute`/`TTargetPlayer(Opponent)`、`HandAndStash/Stash` 区域。`ResolveEffect` 新增 `triggerTargetItemId` 参数。
- 玩家属性与怒气：`InternalHero` 新增 `Rage/RageMax/EnragedDurationMs/EnragedUntil`；默认 100 / 5000 ms（来自原生怪物 `Player.Attributes`）。`TActionPlayerRageApply`、`TActionPlayerModifyAttribute`（HealthMax/HealthRegen/RageMax/EnragedDurationMax）、`TryEnrage`、`onPlayerEnraged/EnrageEnded`。
- 属性变化触发：受伤/治疗/护盾/吸收发 `onPlayerAttributeChanged`（Health/Shield，Gain/Loss）；慢速/冻结/加速/飞行发 `onCardAttributeChanged`。`TTriggerOnPlayerAttributeChanged`、`TTriggerOnCardAttributeChanged` 支持方向过滤。
- 通用属性修改：`TActionCardModifyAttribute` 支持任意属性 + Add/Subtract/Multiply/Set + `TCombatDuration`/整场；冷却/弹药/多重/暴击/吸血有专门处理；`ItemAttributes` 维护运行时偏移与到期回退。主动能力的 `Prerequisites` 改为出手时由 `Effect.Prerequisites` 判定（计数器/相邻数量），不再整张卡放弃。
- 标签：`CardDef.HiddenTags` 与 `InternalItem.ExtraTags`；`TActionCardAddTagsList/Random/BySource`、静态 `TAuraActionCardAddTagsList/BySource`。
- 生命周期：Destroy/Disable/Repair/ForceUse/FlyingToggle；`onBeforeDestroy` 同步、`onCardDestroyed` 延后；`onPlayerDied` 可触发一次 `TActionPlayerReviveHeal`。摧毁卡保留占位但从目标/快照/冷却中排除。
- 测试：新增 `tests/NativeRuntimeRulesTests.cs`（14 组：运行时伤害成长、半血伤害、TriggerSource 排除、TriggerTarget、受伤阈值、卡属性变化+怒气、暴怒进入/退出、计数器门控、限时属性到期、摧毁/禁用/修理、ForceUse/飞行切换、标签传播、死亡复活、fail-closed）。`TestHarness` 失败时输出调用位置。
- 验证：`compile_tests.sh` → `OK`（含旧有 20+ 组与新增组）；`npm test -- --run` → 37 文件 / 102 测试全绿；`build_actionaxis_plugin.sh --deploy` 通过，插件版本 1.5.0，两个 DLL 哈希与部署目录一致。
- 覆盖变化：未映射关键类型降至 触发器 13 / 动作 14 / 光环 0 / 前提 1；其中几乎全部为战斗外规则，剩余战斗内缺口为沙暴、战斗内变形、动态光环、`AttributeChange` 数值、`TTargetCardOccupying`。
- 剩余风险：动态光环（随生命/暴怒实时开关）仍未实现，会保留警告；`ForceUse` 不重置冷却、`HealthMax` 同步抬高当前生命两个语义需回放校准；`TRangeValue` 使用本引擎 RNG，无法与原生序列一致。

### 2026-10-24 · 阶段 6：动态光环、变化量数值与怒气事件

- 动态光环：`NativeDynamicAuras.Validate` 放开人物前提（`TPrerequisitePlayer`），`Active()` 每帧用 `PrerequisitesMatch` 重判；`ApplyStaticAuras` 对带人物条件的卡牌光环只登记运行时层、不再静态加算。
- 技能光环：`SkillMapper.Map` 不再因动态光环整条技能回退；能通过 `NativeDynamicAuras.Validate` 的卡牌光环写入 `CardDef.NativeRuntimeAuras` 并把技能加入对应 Skills；与静态光环/触发共存时仍保持原 fail-closed 边界。
- 数值：`TReferenceValueAttributeChange` → `NumExpr(attributeChange)`，读 `BattleContext.TriggerAmount`（由 `Triggers.ResolvePayload` 用栈维护）；无触发来源时回退 `DefaultValue`。
- 事件：`TActionPlayerRageApply` 现在会发 `onPlayerAttributeChanged(Rage, Gain)`，`TTriggerOnPlayerAttributeChanged` 同步支持 `Rage`（Geyser / Pauldrons / Angry Balloon Bot 依赖）。
- 修复：技能光环的 `TTargetCardSelf` 属性引用不再走目标集（技能不在 `ctx.Items`，否则恒为 0）。
- 清单：`rule_inventory.py` 补上 `TFixed`/`TRange`/`TCombat`/`TPlayer` 前缀，误报消失；数值/时长未映射降为 0。
- 测试：`NativeRuntimeRules` 新增 3 组（变化量数值、人物条件光环开关、技能光环读自身属性）；`DynamicAura` 与 `NativeSkillTier` 两组原有测试正式接入 `tests/Program.cs`（之前未注册）。
- 验证：`compile_tests.sh` → `OK`（23 组）；`npm test -- --run` → 37 文件 / 102 测试全绿；`build_actionaxis_plugin.sh --deploy` 通过，插件版本 1.5.1，两个 DLL 哈希与部署目录一致。
- 剩余风险：玩家技能光环与实时属性同时存在时，若开场条件不成立则无法区分实时属性里是否已含该光环（与既有卡牌光环同一限制）；变形/沙暴/英雄身份/槽位目标保持 fail-closed 并已写明数据缺口。

### 2026-10-24 · 阶段 6b：英雄身份条件与测试接线

- `TCardConditionalPlayerHero`：`CardDef.Heroes` 从原生模板读取（`Clone` 复制）；`BattleConfig` 新增 `PlayerHero/EnemyHero`；`Common` 视为全英雄通用（算本方）。
- 插件：`BoardReadResult.PlayerHero` 从 BPP `LiveCardSnapshotSet.Hero` 反射读取，`ActionAxisPlugin` 写入 `config.PlayerHero`；旧版 BPP 或木桩场景为空。
- 容错：英雄身份或卡牌 `Heroes` 缺失时按“本方英雄”处理（宁可少算“另一个英雄”的加成），并在战斗警告里提示一次；对手侧使用 `EnemyHero`，未提供时同样降级。
- 测试：`NativeConditionTarget` 新增英雄条件组（本方/外来/Common/未知）；`NativeCanCrit`、`NativeLegendaryTier`、`NativeSkillDynamicAura` 三组测试接入 `tests/Program.cs`（之前未注册）。
- 验证：`compile_tests.sh` → `OK`（26 组，Parity 内含 17 个夹具场景）；`npm test -- --run` → 37 文件 / 102 测试全绿；`build_actionaxis_plugin.sh --deploy` 通过，插件版本 1.5.2。
- 覆盖变化：条件未映射 3（全部为非战斗：附魔资格、跑图天数/小时）；**战斗内条件已全部映射**。

### 2026-10-24 · 范围澄清与此前结论更正（仅核查，未新增规则实现）

- 上述历史日志中的“全部映射”是字符串扫描结论，不是语义全覆盖；“做不了”“只缺数据”“BPP 回放只有生命曲线”等表述撤回。
- 用户明确：场上道具复制变形、持续冷却缩减对出手时间的影响属于必做；场外卡池变形、沙暴期间纯减伤等无关效果不在本次范围。
- 原生数据库核查：3D 打印机当前说明为 `Transform into 3 copies of the Small item to the left`，主动规则有 `Limit=3`、`Target=Self`、左侧 Small 前提。之前把所有 `TSpawnContextQuery` 归为场外随机抽取是错误推断。
- 冷却证据：BPP `UpgradePreviewValueProjection.TryResolveEffectiveCooldowns` 明确读取有效 `CooldownMax` 并避免重复套用百分比缩减；本引擎当前主动拒绝持续缩减光环，需继续实现。
- 回放证据：用已有 `captures/diagnostics/read_bpp_replay.py` 实际解码本机 V5 回放 `9bbabba4a216484d83d068a149811259.payload.mpack.gz`，得到 581 帧和 338 条帧事件；第 120 帧含 3 条事件。BPP `CombatImpactProjector` 已读取 `Source/TriggerSource/EffectId/ActionType/Target`，变形读取器也读取新旧实例关系。
- 本次只核查原因并更正交接范围，没有声称变形、持续缩减或逐事件对账已经完成。

### 2026-10-24 · 阶段 6c：场上复制变形与可逆冷却缩减

- 变形：`SkillMapper.MapAction` 新增 `TActionCardTransform`（只允许 `UntilEndOfCombat`、只允许 `SpawnContext.Limit`）；`NativeCardAbilityMapper.AddAbility` 对未校准供体整卡回退；新增 `engine/Transforms.cs`。
- 供体规则（原生模板未携带引用，按卡牌身份校准）：3D Printer → 左侧 Small 的 N 份副本；Mirror → 左侧 Medium 非传说；Hologram Projector / Quicksilver → 自持另一张 Small 非传说；Virus → 目标变 Virus 本身。
- 战斗接入：目标原位替换为供体卡并重算冷却/弹药/多重/暴击/吸血；额外副本按槽位插入，经 `BattleContext.PendingItems` 在帧边界追加，避免遍历 `ctx.Items` 时修改集合；变形后发 `onCardTransformed`。
- 冷却缩减：`NativeDynamicAuras.Validate` 接受 `Flat/PercentCooldownReduction`（仅 Add）；`Refresh` 单独累计两个派生量；`Synchronize` 按 `（CooldownMax − flat）×（1 − percent）` 计算有效冷却并同步进度；`Initialize` 对实时快照先反推基础值，避免重复扣减。
- 测试：新增 `tests/TransformTests.cs`（打印机复制 3 份 + 未校准供体 fail-closed）；`NativeRuntimeRules` 新增冷却缩减开关与实时值反推两组。
- 验证：`compile_tests.sh` → `OK`；`npm test -- --run` → 37 文件 / 102 测试全绿；`build_actionaxis_plugin.sh --deploy` 通过，插件版本 1.5.3。
- 剩余风险：变形后的新实例冷却、附魔继承、槽位挤压与原生是否一致尚无回放证据；冷却缩减的取整/叠加顺序（先固定后百分比）为当前假设，需用回放校准。
- 回放工具：新增 `tools/native_replay_ledger.py`（已在本机样本上验证：581 帧 / 338 事件 / 逐卡冷却变化）；逐事件对比尚未接入测试。

### 2026-10-24 · 审查修复批次：真实入口与运行时结算

- 前提：`SkillMapper` 同时放开并正确计算 `LeftCard/RightCard`，先确定邻居再匹配条件，不能越过不符合条件的紧邻卡；打印机不再因真实前提整卡回退。
- 成长：`Effect.DurationValue/CountValue` 记录表达式，`Effects` 结算时读取；控制时长单位仍为毫秒，执行时换为秒。零目标不会强制命中一张；Reload 显式数值也按当前属性计算。`NativeItemMapper` 的校准路径保留动态数值，避免发射核心等退回常量。
- 调度：排队施放记录 `Generation`，执行前复查毁坏／禁用／变形；旧形态的后续子效果和多重施放不能由新卡继续执行。强制使用不再在遍历内刷新新增卡集合。
- 人物：怪物模板与玩家读取结果的 `RageMax/EnragedDurationMax/HealthRegen` 接入；预验证、失败回滚同时恢复人物属性，避免试映射的光环修改泄漏。

### 2026-10-24 · 审查修复批次：变形、附魔、光环与历史

- 槽位：按卡牌实际占格放置副本，不再按副本数量挤动右侧卡；空间不足时保留旧卡并告警。
- 复制：从供体基础属性和运行时偏移准备新定义，避免再次复制整层光环；来源事件保留变形前的卡牌 ID。打印机／镜子附魔在供体未附魔且存在对应定义时重新映射；映射失败不提交半成品。
- 历史：`BattleItemView.States`、`BornAt/RemovedAt` 保存每个实例的形态与槽位；结果保留离场实例，`TileModel.At` 按播放时刻取卡牌／附魔／位置；出生前和离场后不显示。累计数据保持按实例统计，不切断变形前已产生的输出。
- 光环：新增人物 `Shield/Burn/Poison` 取值，技能也进入派生属性参与集合，`Target=Self` 可命中技能来源。原生实时技能属性与纯模板属性分开标记。最后一个光环来源变形消失后，仍重建基础层而非留下旧加成。

### 2026-10-24 · 审查修复批次：验证与交接

- 新增 `AuditRegressionTests.cs`，11 个回归场景，使用 `native_audit_regressions.json` 的 8 个完整原生模板；没有裁掉真实前提或附魔。覆盖打印机／镜子入口与错误邻居、复制后的伤害／附魔／槽位、控制成长与零目标、Dryad／发射核心、排队失效、Wild Boar、双方人物引用光环和技能护盾实际触发、离场历史与水位。
- 模板可重复导出：`python tools/rule_inventory.py --fixture-names "3D Printer" "Mirror" "Dryad" "monsters:Wild Boar" "Force Field" "Spiky Shield" "Burning Shield" "Toxic Shield" --out tools/actionaxis_plugin/tests/fixtures/native_audit_regressions.json`。同名须用表名前缀消歧。
- 验证：C# 全部通过；前端 37 文件／102 项通过；完整插件 `--compile-only` 构建通过（已有 Unity 过时 API 警告）。本轮未部署、未提交。
- 尚未完成：真实回放逐事件自动对账；所有物品×品质×附魔组合矩阵；动态人物属性光环、未支持参数组合；变形临时属性与附魔冲突、持续冷却缩减的原生一致性；Unity 游戏内历史界面的交互验收。以上不能标为“全部完成”。

### 2026-10-24 · 武装核心：位置规则与其他卡牌检查

- 根因：武装核心主动增伤用 `AllRightCards + IncludeOrigin=true`，被动用 `AllLeftCards`。此前只支持相邻／单侧最近一张，类型名存在却未实现参数组合；不能只删除拒绝分支。
- 实现：`Target` 保存原点／位置模式／包含自身；`NativeSkillSupport.PositionalMatch` 统一匹配左右全体与最近邻，排除敌方与离场物品。效果、卡数／聚合值、被动主体、前提、静态及动态光环均接入。未知模式／原点仍拒绝。技能效果同时保留 `TriggerTarget`，不再丢失触发目标。
- 测试：新增 `PositionalRuleTests.cs` 八组。武装核心连续增伤只作用自身和右侧武器，左右距离无关；左侧任意物品能充能，自身／右侧／对手不会触发；固定金品质阵容中每 2 秒使用左侧物品，武装核心首次实际出手为 4.1s。覆盖四种品质、双方、包含原点、TriggerSource、位置计数与光环一致性。
- 子代理只读审查另指出两处问题，均已修复并测试：Gamma Ray／Omega Ray／Kusarigama 无显式数量的相邻成长应作用于全部符合条件物品；Golden 打印机→Cog、Golden 镜子→Launcher Core 因 SellPrice 光环被拒而无法复制。现在正确保留售价派生属性，不吞警告。
- 实际矩阵又发现低品质行缺失导致 `CanCrit` 访问 Null 的问题；已跳过缺失行并保留 Legendary→Diamond 查询，Deadly Gamma Ray 四品质回归通过。修复前矩阵有 485 个物品至少一个组合受阻；修复后 401 个，不能把这个减少量解释为语义全覆盖。
- 验证：C# 全部 `OK`、前端 37 文件／102 项通过、完整插件 `--compile-only` 构建通过（已有 Unity 过时 API 警告）。未提交；检测到游戏仍运行，本轮未覆盖游戏目录或重启游戏，当前进程不会自动获得新逻辑。

### 2026-10-24 · 实际映射矩阵基线与复跑

输入是游戏压缩数据库中 `TCardItem/TCardSkill` 的完整模板，非类型字符串统计。每个组合单独建立纯模板棋盘（待测物品槽位 3，同阵营测试物品在 0、8），固定 Dooley 身份／生命；技能同样检查双方入口。包含非战斗定义，不穷举英雄、棋盘、战斗状态，也不执行所有组合的完整战斗。

| 检查项 | 结果 |
| --- | ---: |
| 物品模板 | 1146 |
| 技能模板 | 498 |
| 品质／附魔／阵营入口组合 | 126772 |
| 物品组合：映射通过 / 受阻 | 95966 / 28280 |
| 技能组合：simulated / live-applied / inactive / out-of-combat / unsupported | 1324 / 178 / 28 / 86 / 910 |
| 至少一个组合受阻的物品 / 技能 | 401 / 198 |
| 仅无附魔，至少一个品质或阵营受阻的物品 | 264 |

仍不完整的具体例子：
- Hunting Hawk：指定行动受体的被动过滤未支持。
- Passive Power、Rate Limiter：动态倍率光环仍拒绝。
- Radiant Dishwasher／Security Blanket 等：部分抗性光环属性组合未支持。
- Warpaint／Vengeful Sigil 的 Shiny：怒气数值光环已由 G06 映射并测试；Radiant 的 `PercentFreezeReduction` 仍属 G07。
- Wolf Mask：`AllHands` 触发区域；Enervating Sigil：`AllHands` 目标区域。
- Streaming Setup／Spacescraper：G04 已实现逐目标运行时引用与聚合光环并加行为测试（见 §9 的 G04 日志）；本列表其余数字仍是历史矩阵基线，需复跑后更新。
- Virus：仍有前提组合受阻，因此之前“供体规则实现”不代表真实模板整体可模拟。

上述列表不是全部。逐项原因在 `tools/actionaxis_plugin/build/native_mapping_report.tsv`（可重建、未入库），每行带模板 ID、名称、品质、附魔、阵营、状态与原因。不要把 `mapped/simulated` 当作无缺陷或原生对账通过，附魔免疫、动作优先级等仍需行为验证。

复跑步骤：
1. `python tools/rule_inventory.py --mapping-input tools/actionaxis_plugin/build/native_mapping_templates.jsonl`
2. `bash tools/actionaxis_plugin/compile_tests.sh`
3. `tools/actionaxis_plugin/build/EngineTests.exe --coverage tools/actionaxis_plugin/build/native_mapping_templates.jsonl tools/actionaxis_plugin/build/native_mapping_report.tsv`
4. `python tools/rule_inventory.py --mapping-report tools/actionaxis_plugin/build/native_mapping_report.tsv`

矩阵通过筛选当前公开映射入口，首个拒绝会遮住同卡后续缺口；修复一项后需重新扫描，不应据此承诺“只剩所列项目”。

### 2026-10-24 · DOC-01 · 多人协作文档基线
- 范围：本文件重新定义当前基线、五步流程、13 个可交付组、四道验收门槛、交接协议与范围排除；保留所有旧阶段日志。
- 改动文件：`docs/native-rule-coverage.md`；派单证据附件 `docs/native-rule-task-index.csv`；`tools/rule_inventory.py` 只增加从本文件配置生成索引的文档工具入口；`tools/test_rule_workplan.py` 验证生成规则，不改引擎语义。
- 校核：具体种子 ID／名称／模板类型从当前压缩数据库读取；矩阵只复核既有报告，报告不是本次重新运行的行为测试。
- 索引生成结果：1810 条唯一记录＝1146 个物品＋498 个技能＋166 个怪物；43 个种子 ID／名称与原生数据库一致。索引检查主责唯一、关联组合法，附魔只参与关联路由而不抢占基础卡主责。
- 工具测试：`python -m unittest discover -s d:/巴扎plus/tools -p test_rule_workplan.py -t d:/巴扎plus -v` → 退出码 0，5 项通过。验证确定性输出、无漏项、种子优先、缺失／重名拒绝、已有文件保护、tooltip 不污染类型。首次运行暴露测试 SQLite 连接退出事务后未关闭，导致 Windows 临时目录清理锁冲突；已显式关闭连接后复跑通过。
- 文档检查：`git diff --check -- docs/native-rule-coverage.md tools/rule_inventory.py` 通过；13 组契约、导航锚点、已有脚本参数及测试路径已核对。再次生成 CSV 得到相同类型／主责数量；生成器不设置 IMPLEMENTED／VERIFIED。
- 未执行：本次不新增规则实现、不部署、不提交，不声称本次重新运行 C#／前端／Unity 验收。
- 剩余：各组仍待认领，401／198 的历史受阻基线需在冻结新代码后复跑；真实对账尚未完成。下一步由 G00 认领冻结源码与数据，再由 G12 先交账本比较契约。

### 2026-10-24 19:20 · G06-01 · 双方开场属性、英雄身份、怒气与暴怒事件

- Owner / Reviewer / 分支 / 基线提交或补丁哈希：Qoder（G06 本次会话）／未指定／`main`／`6eb032a` + 共享工作区未提交改动；本轮不提交。
- 状态：IMPLEMENTED（非 VERIFIED；未做真实回放／游戏内对账，未部署）
- 范围：
  - 物品 Warpaint `153dfd6d-7dc8-4386-9703-ea6c99f92d4a`：全部品质／附魔／双方入口 112 行；重点 Gold+Shiny：`Tiers.Gold.Attributes.RageApplyAmount=10`，`Enchantments.Shiny.Auras.e1` 以 `TAuraActionCardModifyAttribute/Multiply=2` 作用于自身 → 20；`Abilities.0` 由左侧物品使用触发 `TActionPlayerRageApply`。
  - 物品 Vengeful Sigil `05a32402-598c-4bb5-a280-1a84f0ab84fa`：同样 112 行；Gold+Shiny 为 12；`Abilities.0` 由对手手牌物品使用触发怒气；`Auras.1` 在 `Enraged>0` 时给 SelfHand 的 Weapon +100 Lifesteal。
  - 技能 Burning Shield `7931571a-3d32-41e8-bf7b-ca6b4d1b7a3d`、Toxic Shield `a9b441e1-5824-4855-9359-2ab4643d9ff5`：3 品质 × 双方，矩阵状态 `simulated`；`TTriggerOnPlayerAttributeChanged(Health,Loss)` + 首次低于 50% HealthMax 前提 → `TActionPlayerShieldApply`；`Auras.2` 把对手实时 Burn／Poison × `Custom_1` 加到自身 `ShieldApplyAmount`。
  - 怪物 Wild Boar `5d779101-3fa5-44b7-b885-f8bb9b0f7438`（G00 种子，本轮补 G06 行为验证）：`Player.Attributes` 为 `RageMax=50`、`EnragedDurationMax=5000`、`HealthMax=650`；Feeding Frenzy Silver `RageApplyAmount=15`，`TTriggerOnItemUsed`（Food）→ 怒气；`TTriggerOnPlayerEnraged` 后 Wild Boar 物品对 HiddenTag=Shield 的 4 张手牌各 +`Custom_0(40)` ShieldApplyAmount，Feeding Frenzy 对 Food 施加 Haste。
  - 未覆盖：Radiant 附魔（`PercentFreezeReduction`，G07）；Wild Boar 模板没有英雄字段，`EnemyHero` 保持 null。
- 原生证据：`D:/Program Files (x86)/Steam/steamapps/common/The Bazaar/TheBazaar_Data/StreamingAssets/GameData.db.zip` SHA256 `fdfd3087e6a23eda2c9f53be68b012006cc949d03e8666dd51ad65abc71b61e5`；`tools/actionaxis_plugin/tests/fixtures/native_audit_regressions.json`（SHA256 `3362dd0e9bb017cc1234e7e25aa771b2afabb120292af883489da8014d0edf29`）与 `native_wild_boar_monster.json`（SHA256 `3482ffe3a6f58c7e4d59614ee1379575302c7c5e3f68e6bb82cdb209e2617f77`）。证据是原生模板与模拟帧，不是游戏回放。
- 完成内容（五步）：
  1. 类型识别：沿用 `TActionPlayerRageApply`、`TAuraActionCardModifyAttribute`、`TTriggerOnPlayerEnraged`、`TActionPlayerShieldApply`、`TReferenceValuePlayerAttribute(Burn/Poison)` 的现有映射；未放宽未知类型。
  2. 目标／主体：左侧物品、OpponentHand、SelfHand(Weapon)、Self、HiddenTag=Shield 与双方阵营均按原生模板解析；未改写目标绕过入口。
  3. 结算：在 `NativeDynamicAuras.Validate` runtime 白名单、`NativeCardAbilityMapper.AuraAttribute`、`EnchantmentMapper.InheritedAttribute` 补 `RageApplyAmount`；纯模板走静态 ×2 再 Refresh，实时快照已含 ×2 时不再静态重乘；`RageApplyAmount`（单次怒气动作数值）与 `RageMax`（英雄上限）分离。
  4. 排空：Wild Boar 在 4.05／5.05／7.05／8.05 各 +15 怒气，8.05 达到 50 触发 enrage（5000ms）；`onPlayerEnraged` 按原生队列在下一帧 8.1 结算，4 张 Shield 手牌各 +40 ShieldApplyAmount；未改队列顺序。
  5. 测试：新增 7 个 `AuditRegression` 用例，使用真实模板夹具，注册在 `tools/actionaxis_plugin/tests/Program.cs:38`。
- 改动文件（仅 G06 直接改动；共享工作区还含其它组改动，均未提交）：
  - `tools/actionaxis_plugin/engine/NativeDynamicAuras.cs`（白名单行；同文件另有其它组并发改动）
  - `tools/actionaxis_plugin/engine/NativeCardAbilityMapper.cs`（白名单行；同文件另有其它组并发改动）
  - `tools/actionaxis_plugin/engine/EnchantmentMapper.cs`（继承白名单行）
  - `tools/actionaxis_plugin/tests/AuditRegressionTests.cs`（共享未跟踪文件，追加 7 个 G06 用例与 3 个辅助函数）
  - `tools/actionaxis_plugin/tests/fixtures/native_audit_regressions.json`
  - `tools/actionaxis_plugin/tests/fixtures/native_wild_boar_monster.json`
  - `docs/native-rule-coverage.md`（本条日志）
  - 未纳入：`Types.cs`、`BattleDamageSeries.cs`、`DummyDamageTests.cs`、`NativeDynamicAuras.cs` 中其它组的并发改动；`tools/actionaxis_plugin/build/g06_warpaint_sigil.json` 为临时取证文件，不入库。
- 新增测试（`AuditRegressionTests.Run`）：
  - `WarpaintShinyScalesRageGain`：Gold+Shiny → 20；5 次左侧使用 → 5 个 20 怒气事件（t=1..5），t=5 暴怒、`EnragedUntil=10`。
  - `LiveRageSnapshotIsNotScaledTwice`：`AttributesAreLive=true` 且快照已为 20 → 仍为 20，单次使用只发 20。
  - `VengefulSigilShinyAndEnragedLifesteal`：Gold+Shiny → 12；敌方物品触发 1 次怒气；Enraged 时 Weapon Lifesteal 100，清除后回 0。
  - `WildBoarRageAndEnrageEvents`：`EnemyRageMax=50`、`EnemyEnragedDurationMs=5000`；4 次 15 怒气；8.05 暴怒；8.1 四个 +40 ShieldApplyAmount。
  - `OpeningAttributesIgnoreTemporaryState`：玩家与敌方各跑一遍；Health／Shield／Burn／Poison／Rage／Enraged 不进入开场，仅 RageMax／EnragedDurationMax／HealthRegen 生效。
  - `HealthMaxGrowthIsNotHealing`：HealthMax +50 只抬上限与当前 Hp，不增加 `Totals.Heal`、不发 heal；随后 +20 治疗才计入。
  - `HeroIdentityFollowsBothSides`：玩家 Vanessa／敌方 Pygmalien 各自解析本方英雄卡，不串阵营。
  - 既有 `HeroValueAurasFollowBothSides` 继续覆盖 Burning／Toxic Shield 双方实时 Burn／Poison → ShieldApplyAmount 与半血触发。
- 验证：
  - 19:21 复跑 `bash tools/actionaxis_plugin/compile_tests.sh`：源码编译通过，`PASS AuditRegression`（包含本轮全部 7 个 G06 用例，含双方开场属性与 `HeroIdentityFollowsBothSides`）；整包退出码 1，合计 48 个断言失败，集中在其它组：`MonsterOpponent`（`NativeMonsterFightEndToEnd` 的警告计数期望 2、实际 0，并因空列表越界；源于并发改动 `CardMapper.cs` 对原生模板跳过旧附魔映射）、`G00Coverage`（附魔警告可见性与品质/阵营覆盖）、`G10DamageLedger`（伤害、燃烧归属与暴击分离）。G06 无失败。
  - 对 19:20 构建的 `EngineTests.exe` 单独调用 `AuditRegressionTests.Run`：退出码 0，输出 `G06 AuditRegression PASS`；另用 19:14 同版本程序集验证双方英雄身份解析通过。
  - `npm.cmd --prefix d:\巴扎plus test -- --run`：退出码 0，37 文件／102 测试通过（本轮未改前端）。
  - `bash tools/build_actionaxis_plugin.sh --compile-only`：首次 CS1061（`DamageOptions.Shares`，`Effects.cs:353`）失败；立即复跑退出码 0，仅既有 Unity 过时 API 警告；产物 `build/ActionAxisPlugin.dll` SHA256 `24865f2b2ccbaa38988364af3e7513d150b0b06f0a84ed824c7232abaf5c0012`、`ActionAxisEngine.dll` SHA256 `369e8f04a695b27dee0abd28d82dbec43a997224f1424f6a074bb55bbd3f67b4`。未部署。
  - 矩阵：`EngineTests.exe --coverage ...` 退出码 0；`python tools/rule_inventory.py --mapping-report ...` 退出码 0。物品 1146／技能 498／组合 126772；item mapped/unsupported 96044/28202；skill simulated/live-applied/inactive/out-of-combat/unsupported 1324/178/28/86/910；至少一个入口受阻 401/198（与基线一致）。相对历史基线 item mapped/unsupported 95966/28280，本轮为 96044/28202（映射 +78、受阻 -78；其中 Warpaint／Sigil 的 Shiny 各 8 行，另含其它组并发改动）。Warpaint 与 Sigil 各 104 mapped／8 unsupported（8 行全部是 Radiant 的 `PercentFreezeReduction`，属 G07）；Shiny 行全部 mapped；报告内 `RageApplyAmount` 拒绝数为 0；Burning／Toxic Shield 各 6 行 `simulated`。
  - `git diff --check`：退出码 0。
  - 当前共享工作区限制：19:15–19:17 其它组并发修改 `Types.cs`／`BattleDamageSeries.cs`／`NativeDynamicAuras.cs`／`DummyDamageTests.cs` 时曾连续两次编译失败（`BattleDamageSnapshot.Damage` 缺失、`NativeSkillSupport.Empty(string)` 参数不匹配）；随后恢复编译。整包仍因其它组的 `MonsterOpponent`／`G00Coverage`／`G10DamageLedger` 断言失败而不能标记全绿。
- 原生对账：未执行。无游戏回放样本、无比较器版本、差异数 UNKNOWN；本轮只有模板级模拟与矩阵验证，不能声称逐事件一致。
- 构建部署：构建版本与 DLL SHA256 如上；未写入游戏目录、未加载新 DLL，`DEPLOY=未执行`。
- 剩余风险与阻塞：
  - Warpaint／Sigil 的 Radiant 仍因 `PercentFreezeReduction` 受阻（G07）。
  - `RageApplyAmount` 只证明静态／动态光环与快照去重路径，未证明游戏内怒气动作的全部条件、倍率与优先级。
  - 暴怒被动的下一帧结算时序基于现有原生队列实现，需真实回放确认。
  - 双方英雄身份测试已执行通过；但敌方英雄缺省时引擎按“本方”处理并告警，尚未在真实怪物数据上验证。
  - 工作区包含其它组大量未提交改动；本文件与测试文件为共享修改，不能把本轮日志视为整份工作区已通过。`MonsterOpponent`／`G00Coverage`／`G10DamageLedger` 的 48 个失败属其它组，需要各自修复或更正断言，不能算作 G06 通过。
- 下一条可执行动作：复跑 `bash tools/actionaxis_plugin/compile_tests.sh`，确认整包除上述三个其它组外无新增失败；再由 G12 决定是否授权部署与真实回放对账。锁释放：是（G06 本轮不持有独占锁；未提交）。

### 2026-10-24 19:26 · G02 · 充能、加速／减速、冻结、飞行、装填与多重施放联动

- Owner / Reviewer / 分支 / 基线提交：Qoder（G02）／待复核／main／基线 `6eb032a`；工作区含其它组并发未提交改动，本单只对下列文件负责。
- 状态：IMPLEMENTED（G02 本单）；全局 A 门禁 BLOCKED(DEPENDENCY: 并发 G03 编译中断、G10DamageLedger 在途 30 断言)。
- 范围：`docs/native-rule-task-index.csv` 中 `primary_group=G02` 的 148 条（127 物品＋21 技能）；四种子 Launcher Core `9fafc0a5…`、Dryad `cda40a45…`、Cog `ca77281c…`、Drowsdrop `a655fb50…`；覆盖 Bronze/Silver/Gold/Diamond 与全部附魔的矩阵行；行为断言限于文档给定棋盘，不外推到其它卡。
- 原生证据：`tools/actionaxis_plugin/tests/fixtures/native_kyver_cards.json`（Core／Drowsdrop／Cog 模板）SHA256 `a7bd371cbdacdd0b3ded3f6b5beaec2384f6ba6dbaa11644c46be6d100db2f50`；`tools/actionaxis_plugin/tests/fixtures/native_audit_regressions.json`（Dryad 模板）SHA256 `3362dd0e9bb017cc1234e7e25aa771b2afabb120292af883489da8014d0edf29`；数据源为 5.0.0 原生模板字段 `CooldownMax/HasteAmount/HasteTargets/ChargeAmount/ChargeTargets/SlowAmount/ReloadAmount/AmmoMax/FlyingTargets`。帧语义：Dt=0.05s，首次出手在 t=C 而非 C-0.05；被动触发下一帧排空；多重间隔 250ms；状态在帧推进前到期；飞行抗性为 `Max(50, reduction)%`。无真实回放样本。
- 完成内容：五步对应如下。① 审计四种子与 G02 随机目标路径：`HasCooldownOnly` 之前按静态 `Card.Passive/Cooldown` 过滤，而原生条件 `TCardConditionalAttribute` 是 `CooldownMax > 0` 且运行时可被成长修改。② 最小修复：随机目标过滤改为运行时 `item.CooldownMax <= 0`。③ 新增真实模板时间轴回归（见下）。④ 门禁与矩阵复跑。⑤ 本条日志。确认的语义：成长后的秒数／目标数在执行时绑定；Core 自己使用不触发自身“友方或飞行”被动，OR 不重复计数；Drowsdrop 在自身被减速时装填；弹药耗尽时多重不能再出手。共享接口：`E/Effects.cs` 随机目标集，影响 G01/G03/G05/G07 的目标选择路径。
- 改动文件：
  - `tools/actionaxis_plugin/engine/Effects.cs`（本单仅 1 行：随机目标 `HasCooldownOnly` 改用运行时 `item.CooldownMax`；同文件另有其它组并发改动，未纳入本单）
  - `tools/actionaxis_plugin/tests/G02StatusTimingTests.cs`（新增）
  - `tools/actionaxis_plugin/tests/Program.cs`（第 41 行注册 `G02StatusTiming`）
  - `docs/native-rule-coverage.md`（§7.2 G02 状态行与本条日志）
  - `tools/actionaxis_plugin/build/native_mapping_report.tsv`（矩阵再生成产物，19:23 门禁重跑；非手工编辑）
  - 未纳入：`Types.cs`、`Triggers.cs`、`Transforms.cs` 等其它组并发改动；`native_mapping_templates.jsonl` 等生成中间物；未提交、未部署。
- 新增测试（`G02StatusTimingTests.Run`，夹具同上）：
  - `CogGrowthMovesNeighborActivation`：HasteAmount 1s→2s 后 t=4.0 的 haste Value=2，10s 邻居 10.0→8.05；HasteTargets−1 后无 haste 且仍 10.0；邻居 CooldownMax 运行时减到 0 后被目标过滤排除。
  - `DryadRageGrowthMovesItsOwnActivation`：充能 1s→2s，Dryad 首次出手 5.0→4.1，事件值同步为 2。
  - `LauncherCoreFlightsChargeAlliesButNotSelf`：Core 出手 [6.0,12.0]（自己使用不充能自己），两个 16s 飞行盟友各获 +1s 后于 14.0 出手；t=6.0 两个 fly、两个 charge 均指向两名盟友。
  - `DrowsdropReloadRestoresAmmoAndSlowDelaysEnemy`：自身减速 t=5.0，敌方 6.0→6.5；t=6.05 reload=1 指向自己，药水 [5.0,10.5]；无友方减速时只出手一次（弹药 0 阻断）。
  - `FreezeAndFlyingResistanceMoveActivation`：冻结 1s→3.0，目标飞行时抗性使冻结 0.5s→2.5，无冻结→2.0。
- 验证：
  - 19:20 `bash tools/actionaxis_plugin/compile_tests.sh`：编译通过，`PASS G02StatusTiming`；整包退出码 1，唯一失败 `FAIL G10DamageLedger`（30 断言，属 G10 在途）。该次输出仅在会话终端，无持久化日志文件。
  - 19:23 与 19:27 复跑同一命令：编译失败于并发 G03 正在写入的 `InternalItem.NativeTriggerCounts`／`NativeSkillRule.MaxActivations` 未定义（引用已进 `Triggers.cs`，字段尚未落到 `BattleContext.cs`／`SkillMapper.cs`），非 G02 改动导致；待并发写入完成后重跑。
  - `npm.cmd --prefix d:\巴扎plus test -- --run`：退出码 0，37 文件／102 测试通过（本轮未改前端）。
  - `bash tools/build_actionaxis_plugin.sh --compile-only`：退出码 0，仅既有 Unity CS0618 警告；`ActionAxisEngine.dll` SHA256 `5b252c6e54a9d6a45df67d98f7b42ff5418510a927ea82e9b4d673de5bc15f70`，`ActionAxisPlugin.dll` SHA256 `e11512a3a2f230723785ad8bd5ef04c6edd1f9abb324e502a234fa618bb39d60`。
  - `git diff --check`：退出码 0。
  - 矩阵：`EngineTests.exe --coverage` 与 `rule_inventory.py --mapping-report` 退出码 0，当前报告 387,678 行（新 schema 增加 `tier_source/scenario/attributes_live/hero`，与历史 126,772 行不可直接比较），报告 SHA256 `a2514832c0ab0008eab5a66a5f10fc4f94f479880806d386a5e154549b144644`。G02 127 物品：114 全映射、2 部分（Stopwatch 18/252 行、Spark Plug 30/420 行因动态光环属性受阻）、11 全阻；21 技能：18 有 `simulated` 行、3 不支持。四种子映射行数：Launcher Core 336/336，Dryad／Cog／Drowsdrop 各 420/420。
  - `rule_inventory.py --work-index`：退出码 0，1810 唯一模板，G02 148 条；`python -m unittest tools.test_rule_workplan -v`：5/5 OK。
- 原生对账：未执行。无 G02 真实回放、无比较器版本（G12 尚未提供），差异数量 UNKNOWN；本轮只有模板级模拟与矩阵验证，不能声称逐事件一致。
- 构建部署：构建时间 19:20，DLL 哈希如上；未写入游戏目录、未重载、未重启，`DEPLOY=未执行`。
- 剩余风险与阻塞：
  - 13 物品未全映射：Stopwatch（不可逆持续属性光环，G05）、Spark Plug（PercentHasteReduction，G07）、Poke（CritChance 变化，G07）、Laboratory（TActionCardEnchantRandom，G07）、Cruise Ship／Dooltron／Snowmobile／Cosmic Plumage／Teddy（能力定义缺失，G00/G11）、Marlon（DamageCrit 光环＋能力定义缺失，G07/G00）、Bellelista／Hunter's Blind／Monitor Lizard（指定行动受体被动，G03）。
  - 3 技能不支持：Anything to Win（条件或次数限制，G03）、Equivalent Exchange（TTriggerOr，G03）、Retool（TriggerSourceNeighbors，G03）。
  - 本单证明的是四个种子棋盘的实际时间轴；其它 G02 卡的同一机制在不同棋盘、品质与附魔组合下仍可能分支，矩阵 `mapped` 不等于行为正确。
  - 全局 Gate A 因并发编译中断与 G10 的 30 断言不能标记全绿；Gate C（真实回放）与 Gate D（部署后可见功能）均未执行。
- 下一条可执行动作：待并发 `NativeTriggerCounts` 写入完成、编译恢复后复跑 `bash tools/actionaxis_plugin/compile_tests.sh`；预期 `PASS G02StatusTiming` 且除 G10 外无新增失败，G10 归零后再记录 Gate A 全绿。锁释放：是（未提交、未部署）。

### 2026-10-24 20:05 · G11-01 · 36 项未归类规则分诊与沙暴／卡池／混合卡取证

> 本条是分诊快照。3 项 IN 与经济 no-op 已在 G11-02 实现并验证；下方“未实现”描述保留为当时状态，不是当前结论。

- Owner / Reviewer / 分支 / 基线提交或补丁哈希：Qoder（G11 本次会话）／未指定／`main`／`6eb032a` + 共享工作区未提交改动；本轮不提交。
- 状态：TRIAGED（非 IMPLEMENTED／VERIFIED；36/36 有具名主责，未改引擎、未加测试、未构建、未部署）
- 范围：`docs/native-rule-task-index.csv` 中 `primary_group=G11` 的 36 个模板（逐项见 §6.1.1）；重点沙暴开始 vs 默认伤害、场外卡池、混合战斗／场外规则；另记录索引外 2 条 `TCardSocketEffect` 与 1 条 `TCardPlayerEffect` 战斗规则。OUT 只表示本轮行动轴战斗内范围外，不是永久不可实现。
- 原生证据：`D:/Program Files (x86)/Steam/steamapps/common/The Bazaar/TheBazaar_Data/StreamingAssets/GameData.db.zip` SHA256 `FDFD3087E6A23EDA2C9F53BE68B012006CC949D03E8666DD51AD65ABC71B61E5`；索引 1810 条中 G11 主责 36 条，36/36 在 DB 找到完整模板。程序集：`BazaarGameShared.dll` `C51716EA3EC8C43009EF244A84767F88C14D836EFEA565B85459E420223A8947`、`BazaarBattleService.dll` `E95E614BFF9E60D3E73B89779554E183677F47FE5DDCE7C18262CAE2FFEFE987`、`Assembly-CSharp.dll` `F39C59E4FE45766EC6FB392107E75CD6C90BB810AFD68134643B2332D1C005E5`。所有证据是 DB 模板、只读程序集与静态计数，**不是游戏回放或原生逐事件对账**。
- 完成内容（五步）：
  1. 类型识别：36 项全部解析基础 `Abilities/Auras`、`Tiers`、`Enchantments`、`Transform`、`WorkIn/ActiveIn`；确认基础顶层触发器／动作集合，并保留未知类型作为拒绝点。索引外另查 `TCardSocketEffect` 2 条与 `TCardPlayerEffect` 9 条。
  2. 范围判定：按触发器、`WorksIn`、tooltip 与动作路径分成 IN 3（Potion Distillery、Bottled Tornado、Windmill）、DATA 4（Chunk of Lead、Mysterious Gift、`[DEBUG] Fortune Cookie`、`[DEBUG] Stove`）、OUT 16（基础为空／内部占位／出售经济）、OUT+DATA 13（场外卡池／重掷）。混合卡 107 张，只有 Peacewrought 两张因 `TTriggerOnEncounterSelected` 未进白名单而整卡受阻。
  3. 结算／数据取证：`TSpawnContextQuery` 全库 856 个节点（物品／技能 175 个）全部只有 `$type`+`Limit`；`TActionGameSpawnCards/DealCards/Reroll` 全库 233/168/6、物品技能 142/5/6；缺定义全库 56 模板／64 唯一引用（按品质展开 164 处）。沙暴：`TActionCardBeginSandstorm` 2、`TTriggerOnSandstorm` 3、`TRunConditionalSandstorm` 0；170/170 `TCardEncounterCombat` 开启沙暴但 DB 无数字配置；程序集有 7 个 `Int32` 配置字段与 `TakeSandstormDamage`，无客户端默认值。
  4. 归属与解除条件：41 个未支持 `TActionPlayerModifyAttribute` 动作分布在 26 个基础物品／技能模板，其中经济类 24（Gold 18、Income 2、Experience 3、Prestige 1）应跳过／no-op，战斗类 17（PercentDamageReduction 16、Rage 1）为实现缺口；逐项主责、关联、下一条动作见 §6.1.1。
  5. 文档：新增 §6.1.1–6.1.7（36 项表、混合卡、OUT 清单、DATA 共用记录、沙暴拆分、槽位／Heated-Chilled、索引外模板与验收口径）；更新 §6.1 判定表、§7.2 G11 看板与 §7.3 前的 G11 说明。未改 CSV、引擎、测试或构建脚本。
- 改动文件：仅 `docs/native-rule-coverage.md`（G11 新增段落与本条日志）。共享工作区中仍有其它组未提交改动；本文件的 `git diff` 也包含其它组此前写入的日志，不把它们算作 G11 改动。
- 新增测试：无。没有改引擎或测试夹具，因此本轮没有新增方法名、夹具路径或 `Program.cs` 注册。
- 验证：
  - DB 解析：36/36 G11 模板找到；基础触发器／动作、Tier 引用、附魔分支与 tooltip 逐项提取；沙暴 170/170、卡池 856/175、缺定义 56/64/164、混合卡 107/2 的计数均用独立脚本复核。
  - 程序集：三个 SHA256 与上述值一致；`TGameModeConfigSandstorm` 的 7 个字段类型、`BazaarGameRules` 属性与 `BazaarCardDealer.TakeSandstormDamage` 签名只读核实。
  - `git diff --check -- docs/native-rule-coverage.md`：退出码 0。
  - 文档覆盖检查：36 个 G11 模板 ID 在文档中出现数 36/36，缺失 0；§6.1.1–6.1.7 各存在 1 处。
  - 未执行 `compile_tests.sh`、前端测试、矩阵生成与 DLL 构建：本轮没有代码改动，不能用旧构建冒充本轮验证；也未运行游戏内验收。
- 原生对账：未执行。无沙暴回放、无卡池运行时样本、无比较器版本，差异数量与 UNKNOWN 均未量化；本轮不能声称逐事件一致或规则已实现。
- 构建部署：未构建、未部署、未加载新 DLL；`DEPLOY=未执行`。
- 剩余风险与阻塞：
  - 3 项 IN 当时未实现（G11-02 已实现并验证）：Potion Distillery 的 EncounterSelected 白名单；Bottled Tornado／Windmill 的 BeginSandstorm 与 OnSandstorm；Windmill A2 的 HealthMax 翻倍需在触发器实现后验证。
  - 13 项 OUT+DATA 的卡池候选集／权重／运行时上下文缺失；4 项缺 AbilityId／AuraId 定义。均不能用本地卡表猜候选或编造数值。
  - 沙暴默认伤害／步进参数缺失；开始事件可先实现，但不能把两者绑死或写成永久不可实现。
  - 2 条 socket 行不在 1810 索引；Heated／Chilled 与 `TTargetCardOccupying` 完全未实现（G01/G05/G03/G07）。
  - `Base Rage Effect` 不在索引；引擎只发 `onPlayerEnraged`，没有清 Slow／Freeze 与 +10% 冷却缩减（G06/G11）。
  - 41 个未支持人物属性动作中，经济类 24 个尚未改为“跳过动作、保留整卡”，仍会造成整卡回退；战斗类 17 个分别在 G07/G10/G06。
  - 共享工作区含其它组在途改动；G11 没有独占锁，不能据此判断整包编译或测试状态。
- 下一条可执行动作：
  1. G03：把 `TTriggerOnEncounterSelected` 加入 `SkillMapper.IsNonCombatTrigger`（`SkillMapper.cs:440`），并加“Potion Distillery A2 跳过、U0/U1 生效”测试；预期 `NativeRuntimeRulesTests` 通过。
  2. G11/G03：在 `SkillMapper.MapAction` 加 `TActionCardBeginSandstorm`，在 `NativeSkillSupport.TriggerEvents`（`SkillMapper.cs:903`）加 `TTriggerOnSandstorm`，改 `NativeRuntimeRulesTests.cs:819` 的 fail-closed 断言；预期 Bottled Tornado A1/A2 与 Windmill A1–A3 不再被首个拒绝遮住。
  3. G01/G05：实现 `TTargetCardOccupying` 与 Heated／Chilled，先写 Stove／Cooler 失败测试，再替换 `SkillMapper.cs:702/1123`、`NativeCardAbilityMapper.cs:500`、`NativeDynamicAuras.cs:57` 的拒绝分支。
  4. G06：为 `Base Rage Effect` 写全局暴怒失败测试（清 Slow／Freeze、+10% PercentCooldownReduction、暴怒结束移除）。
  5. G12：取一段含沙暴的真实回放，按 6.1.5 B 记录倒计时与伤害时间序列，先交配置／回放证据再决定 `BaseDamage/TickRate`。
  锁释放：是（本轮未持有独占锁；未提交、未部署）。

### 2026-10-24 20:17 · G10-LEDGER · 直伤、DoT、护盾、治疗、暴击、吸血与累计输出归因
- Owner / Reviewer / 分支 / 基线：Qoder（本轮）／待集成人复核／main 共享工作区（未提交）／`6eb032a`。
- 状态：IMPLEMENTED（G10 回归、整包测试与编译通过；未部署）。
- 范围：`cards` 表真实模板——Blow Torch `302d710c-…`、Soldering Gun `666f4084-…`、Trained Spider `014d9c98-…`、Bandages `09132149-…`、Walter Cooler `1b518cd8-…`、Rune Axe `cd651487-…`（Bronze/Silver 按用例）；Chocolate Bar `1a3d4cde-…` 仅作无战斗被动的邻位隔离。覆盖双方棋盘、木桩／普通敌人、单／多来源 DoT、过量、护盾 Prevented、治疗过量、生命上限成长、暴击与吸血。
- 原生证据：模板取自 `vendor/GameData/GameData.db` 的导出 `tools/actionaxis_plugin/build/native_mapping_templates.jsonl`；回放用真实夹具 `tools/actionaxis_plugin/tests/fixtures/native_replay_health.json`（581 帧／29s）与 `native_replay_overheal.json`。未修改原生帧读入语义，只在 `NativeCombatReplayTests` 增加账本恒等式断言。
- 完成内容：① `BattleDamageSnapshot/Series` 分列理论 `Damage`、真实 `OutputDamage`、有效 HP `EffectiveDamage`、`ShieldAbsorbed`、`PreventedDamage`、`OverkillDamage`、`NominalHealing/Healing/Overheal`、`NetHpLoss`、`OtherHpChange`、`SelfDamage`，`SettledDamage=OutputDamage+OverkillDamage`；② `Effects` 的暴击只放大直伤与吸血基数，吸血按实际 HP 损失（护盾吸收不计），自伤不进总输出，DoT 按各自来源分池；③ `Battle` 的燃烧半盾规则产出 `Prevented` 而非输出，中毒穿盾，燃烧衰减／中毒不衰减，生命上限变化只推动 HP；④ `Aggregate` 仅按事件份额归因到来源卡，按受击方生成序列，排除自伤与跨阵营；⑤ 多来源同一 tick 只结算一次，再按池贡献比例拆分，过量／过量治疗单列。
- 改动文件：`engine/Effects.cs`、`engine/Battle.cs`、`engine/BattleContext.cs`、`engine/BattleDamageSeries.cs`、`engine/Aggregate.cs`、`engine/Types.cs`、`engine/StatusPools.cs`（新增）；`tests/G10DamageLedgerTests.cs`（新增）、`tests/NativeCombatReplayTests.cs`、`tests/Program.cs`；`tests/fixtures/native_g10_damage.json`（新增，真实模板导出）；本文件。共享文件同时含其它组并发改动，本日志只认领 G10 差异。
- 新增测试：`Program.cs` 注册 `G10DamageLedger`；`G10DamageLedgerTests` 八组——`MultiSourceBurnSplitsOneTickByOwner`（4:1 池首跳 5=4+1、次跳 4=3.2+0.8，总 9）、`PoisonFollowsNativeCadenceWithoutDecay`（7/8/9s 各 1）、`HealingAndShieldNeverCountAsDamage`、`MaxHealthGrowthIsNeitherDamageNorHealing`、`DirectDamageLifestealAndCritStaySeparate`（15 与暴击 30，吸血同步）、`BothSidesKeepIndependentLedgers`、`DummyAndNormalEnemyUseTheSameOutputRules`、`OverkillAndPreventedStayOutOfOutput`（4 伤打 2 HP → 输出 2／过量 2；4 伤打 1 盾 → 输出 6/7、Prevented 1）。失败→修复：空槽被引擎视作相邻导致 Soldering Gun 多吃 Tool 光环 Multicast（用真实无战斗被动 Chocolate Bar 隔位）；账本恒等式漏减 `OverkillDamage`（4 vs 2）；Shard of Obsidian 原生模板 `AbilityIds=["0","1"]` 但 `Abilities` 仅 `0`，映射 fail-closed，改用完整 Rune Axe 模板做直伤／吸血／暴击断言。
- 验证：`bash tools/actionaxis_plugin/compile_tests.sh` → 退出码 0，`OK`；`PASS G10DamageLedger`、`PASS NativeCombatReplay`，整包全部套件通过。`npm.cmd test -- --run` → 退出码 0，37 文件／102 测试全绿。`bash tools/build_actionaxis_plugin.sh --compile-only` → 退出码 0，仅既有 Unity CS0618 警告。`git diff --check` → 退出码 0（仅 CRLF 提示）。中途曾因其它组并发编辑出现 `MonsterOpponent`／`NativeRuntimeRules`／`G00Data`／`AllHandsRules` 失败，均已被各自修复；最终整包以本轮末尾复跑为准。未跑：游戏内部署与 UI 视觉验收（按约束不擅自部署）。
- 原生对账：`native_replay_health` 在 6s／15.3s／末帧核对 `NetHpLoss = TotalDamage − ShieldAbsorbed − OverkillDamage − Healing − OtherHpChange`；6s 为 TotalDamage 13、ShieldAbsorbed 7、净失血 4，15.3s 为 341−48−0−6=287；末帧 TotalDamage 1822、Overkill 5、HP 0，`TotalDamageKnown=true`。`native_replay_overheal` 在 5s／10s 核对名义 54/110、实收 20/25、Regen 4/9，治疗不进输出。模拟器用同一组恒等式断言（含 Overkill/Prevented/OtherHpChange/SelfDamage）；差异数 0，无 UNKNOWN 帧参与结论。
- 构建部署：`ActionAxisEngine.dll` SHA256 `00b4021064a05d46f97df0f0585969a8d3607fbfd7f87f05d2fb192d00fd8c49`；`ActionAxisPlugin.dll` SHA256 `f0bdd63a1a561660e084b3e4e701306b70ecd1a4f9943cf34fc21988b9953e3c`。仅编译，未覆盖游戏目录。
- 剩余风险与阻塞：没有同时包含上述 G10 卡的真实回放，当前是账本字段级对账而非逐事件对账，事件级差异留给 G12；原生回放不记录伤害来源，多来源 DoT 的比例拆分为本引擎推导，无法用现有回放直接证明来源份额；Shard of Obsidian 的陈旧 `AbilityId` 属数据缺陷（G00/G11），该卡仍 fail-closed；模拟器净失血/过量的 UI 展示仍由 G12 统一。
- 下一条可执行动作：由集成人复核本轮差异并决定是否授权部署；G12 提供含 G10 种子的完整回放后，复跑 `bash tools/actionaxis_plugin/compile_tests.sh` 并追加逐事件对账。锁释放：是（未提交、未部署）。

### 2026-10-24 22:25 · G10-CONT · 任务索引 62 模板回归与真实回放原生账本对账
- Owner / Reviewer / 分支 / 基线：Qoder（G10 续）／待集成人复核／main 共享工作区（未提交）／`6eb032a`。
- 状态：IMPLEMENTED（G10DamageLedger、G10TaskIndex、NativeCombatReplay 通过；整包受并发 G09Lifecycle／G07Enchantment 在途失败影响，未标记全绿；未部署）。
- 范围：在 20:17 G10-LEDGER 基础上补两块证据：① `docs/native-rule-task-index.csv` 中 `primary_group=G10` 的 62 张模板全量回归；② 真实 BPP 回放 `084bd840e98c4b81a681cb8d30c7b72f`（玩家 Dooley，Trained Spider Silver + Soldering Gun Bronze/Toxic，敌方 Pygmalien，426 帧／21.25s）敌方观测侧的原生账本对账。此前“没有同时包含 G10 卡的真实回放”的结论由本轮修正。
- 新增：`tests/G10TaskIndexTests.cs`（注册 `G10TaskIndex`）——解析 CSV 并核对 62/62 的 id/name/table；递归枚举模板内六类 `TActionPlayer*`（含附魔 `Attributes` 上下文），291 个动作全部通过 `SkillMapper.MapAction` 且六种 kind 均出现；逐卡单卡模拟（双方 800/1000 HP、7 盾），62 张卡 `MappingWarnings=0`，对双方执行账本恒等式，并按来源侧/目标侧分别核对累计输出与治疗归因。夹具 `tests/fixtures/native_g10_templates.json`（62 模板，SHA256 `A379658CCA05775B99F58A757265917B49D9A43DDEFD2E930B76EAB28EC150C2`）。`NativeCombatReplayTests` 新增 `ReadsRealG10ReplayLedger`、`EveryKnownNativeFrameKeepsLedgerIdentity`；夹具 `tests/fixtures/native_g10_replay.json`（426 帧，SHA256 `1A9EB69B65CB05B91CE58238B8FABDADAD7AF1B3A1E1B30B1FF352C0E75C49A0`，源回放 SHA256 `A2096BCC1F8EB31CDB274E2948C65271F08542BB989A986EBFE683C23ED06C08`）。
- 夹具来源：模板夹具按 CSV 的 62 个 ID 从 `build/native_mapping_templates.jsonl` 连接；回放夹具先用 `tools/native_replay_ledger.py` 生成 ledger，再投影敌方 `HealthAdjustments`（不引入原生未记录的来源字段）。两者均只读，不改变游戏数据。
- 原生账本：6.0s 敌方 Poison 4，TotalDamage 4、ShieldAbsorbed 0、NetHpLoss 4；12.5s Burn 打 150 盾只消耗 1 点，TotalDamage 95（Poison 94 + Burn 1）、ShieldAbsorbed 1、NetHpLoss 94；末帧 TotalDamage 323（Poison 322 + Burn 1）、ShieldAbsorbed 1、Overkill 0、Healing 0、OtherHpChange 0、NetHpLoss 322，恒等式 `NetHpLoss = TotalDamage − ShieldAbsorbed − Overkill − Healing − OtherHpChange` 成立。三个原生夹具共 1208 帧逐帧检查，未知调整与治疗未知均为 0；该字段级对账不把原生伤害归因到卡，旧回放没有来源字段，多来源 DoT 份额仍由模拟器按池推导。
- 修复／失败→通过：首版索引扫描只用品质属性，Narwhal 的 Fiery/Mossy 动作位于附魔 `Attributes`，因 `BurnApplyAmount` 缺失失败；改为按模板层级合并 `Attributes` 后 62 卡／291 动作通过。无引擎行为改动；G10 现有账本口径已满足本轮断言。
- 验证：按 `compile_tests.sh` 同一源集用 csc 全量编译 → 退出码 0；`PASS G10DamageLedger`、`PASS G10TaskIndex`、`PASS NativeCombatReplay`。`npm.cmd test -- --run` → 退出码 0，37 文件／102 测试全绿；`git diff --check` → 退出码 0（仅 CRLF 提示）。整包退出码 1，失败集中在并发 `G09Lifecycle` 与 `G07Enchantment`（本轮末尾各若干断言），不属 G10 本轮范围，不能用整包失败反推 G10 失败。
- 构建部署：仅测试编译，未构建或覆盖 DLL，未部署；无提交。
- 剩余风险与阻塞：真实回放只记录敌方观测侧；玩家侧原生对账缺少 `HealthMax` 开场字段，未伪造。事件级与来源级对账仍待 G12；模拟器 UI 的净失血／过量展示仍由 G12 统一。`G10TaskIndexTests` 的逐卡模拟忽略非 G10 映射告警，只证明 G10 动作入口与账本恒等式，不证明每张卡的完整战斗语义。
- 下一条可执行动作：G12 用同一 `084bd840` 回放重建模拟器开场并运行 `tools/native_replay_compare.py`，补玩家侧与逐事件来源；集成人复核后决定是否授权部署。锁释放：是（未提交、未部署）。

### 2026-10-24 22:13 · G11-02 · 沙暴开始／OnSandstorm、EncounterSelected 白名单与经济 no-op

> 本条是 2026-10-24 的入口映射快照。默认沙暴伤害、36/36 入口验收与整包绿灯已在 G11-03 补齐；当前状态以 G11-03 为准。

- Owner / Reviewer / 分支 / 基线提交或补丁哈希：Qoder（G11 本次会话）／未指定／`main`／`6eb032a` + 共享工作区未提交改动；本轮不提交、不部署。
- 状态：IMPLEMENTED（G11 切片）＋ VERIFIED（G11 单测、36 模板过滤矩阵、针对性混合卡矩阵）；未做原生回放对账，未做整包 UI／部署验收。
- 范围：36 个 G11 主责模板中的 3 项 IN；`TTriggerOnEncounterSelected` 混合卡；`Gold/Income/Experience/Prestige` 经济动作与经济修改光环；沙暴默认伤害／步进保持 DATA；13 项场外卡池与 4 项缺定义保持 DATA；16 项空模板保持 OUT。
- 原生证据：`GameData.db.zip` SHA256 `FDFD3087E6A23EDA2C9F53BE68B012006CC949D03E8666DD51AD65ABC71B61E5`；程序集哈希沿用 G11-01。证据是 DB 模板与只读程序集，不是回放。
- 完成内容：
  1. `SkillMapper.MapAction` 新增 `TActionCardBeginSandstorm` → `beginSandstorm`；`Effects` 首次开始时写 `SandstormStarted/SandstormStartedAt`、发 `sandstormStart`、派发 `onSandstorm`，重复开始不再派发。没有实现默认伤害／tick。
  2. `NativeSkillSupport.TriggerEvents` 新增 `TTriggerOnSandstorm` → `onSandstorm`；Windmill A2 的 `playerAttribute HealthMax` 已验证只 +100 一次。
  3. `SkillMapper.IsNonCombatTrigger` 新增 `TTriggerOnEncounterSelected`；Potion Distillery A2 跳过、U0/U1 两条动态光环保留；Peacewrought A2 跳过、A4 Gold 按 no-op、A0/A1 战斗分支保留。
  4. `TActionPlayerModifyAttribute` 的 `Gold/Income/Experience/Prestige` 返回 `noop`，不校验经济数值、不回退整卡；`NativeCardAbilityMapper.ApplyStaticAuras` 对同类经济修改光环跳过。`TActionAnd` 中经济 + 伤害仍保留伤害子动作。
  5. 新增真实模板夹具与 G11 测试；未改 CSV、未改构建脚本。
- 改动文件（只认领 G11 差异；共享文件同时含其它组并发改动）：`engine/SkillMapper.cs`、`engine/Effects.cs`、`engine/BattleContext.cs`、`engine/NativeCardAbilityMapper.cs`、`tests/NativeRuntimeRulesTests.cs`、`tests/Program.cs`、`tests/G11SandstormTests.cs`（新增）、`tests/fixtures/native_g11_sandstorm.json`（新增）、本文件。
- 新增测试：`G11SandstormTests` 六组——`SandstormActionMapsAndStartsOnce`（Bottled Tornado A0/A1/A2 均映射、`sandstormStart` 一次、无伪造沙暴伤害）、`SandstormTriggerDoublesMaxHealthOnce`（Windmill 100→200 且只一次）、`EncounterSelectedIsNonCombatAndKeepsPotionAuras`（336/336 入口、2 条光环保留、CardSelected 仍非白名单）、`PeacewroughtMapsWithoutEconomicRollback`（A0/A1/A4 映射）、`CashRegisterGoldenEconomicAuraIsSkipped`（Income 光环跳过、90/90）、`GoldActionOnCombatTriggerIsNoOp`（经济 + 伤害复合动作保留伤害）。
- 验证：
  - G11 单测（当前引擎，使用只编译 engine／TestHarness／G11SandstormTests 的最小入口，避免并发测试文件）→ `PASS G11Sandstorm` / `OK`。
  - 36 模板过滤矩阵（`docs/native-rule-task-index.csv` 的 G11 行过滤后跑 `EngineTests.exe --coverage`）：5574 行；item `mapped` 4620／`unsupported` 684；skill `inactive` 144／`out-of-combat` 114／`unsupported` 12；附魔告警 0、实时属性告警 0。684 + 12 = 4 项缺定义：Chunk of Lead 390、Mysterious Gift 30、`[DEBUG] Fortune Cookie` 264、`[DEBUG] Stove` 12。3 项 IN：Potion Distillery 336/336、Bottled Tornado 336/336、Windmill 252/252；Cash Register 90/90。
  - 针对性混合卡矩阵：Peacewrought 390/390 `mapped`；`[DEBUG] Peacewrought` 390/390 `unsupported`（`TActionGameSpawnCards` 调试例外）；Leather Jacket 252/252 `unsupported`（`PercentDamageReduction` 光环，G07/G10）；Hunker Down 24 `unsupported`（`PercentDamageReduction` 动作，G06）＋6 `inactive`。
  - 全量矩阵快照（当前共享引擎）：387,678 行；item `mapped` 334,508／`unsupported` 38,230；skill `simulated` 7137／`live-applied` 701／`inactive` 4510／`out-of-combat` 384／`unsupported` 2208；附魔告警 780、实时属性告警 0。该快照含其它组并发引擎改动，只作行数与 G11 过滤结果复核，不宣称全库差异都归 G11。
  - 整包测试（多次快照）：一次可编译快照中 `G11Sandstorm` PASS，另有 `CardMapper`（`EnchantmentMapperTests` 旧 key）、`NativeRuleExpansion`（`TTriggerOnCardStartedFlying` 的 `AbsolutePlayerHand` 不再抛异常）、`NativeRuntimeRules`（Seafaring／Vat 新用例）、`G07Enchantment`（Deadly 暴击／Golden 夹具）、`G01TargetConsistency`（Absolute 区域目标数）失败；随后 `G09LifecycleTests.cs` 又处于并发编辑中，整包暂时不可编译。上述失败均不在 G11 改动路径，由共享工作区并发组处理；不能把“整包未绿／暂不可编译”归因于 G11。G11 另用独立最小入口复跑通过。
- 36 项状态口径：3 项 IN 入口映射 IMPLEMENTED（Bottled Tornado、Windmill、Potion Distillery）；Cash Register 的 Golden Income 光环已跳过、入口 90/90 `mapped`，但其池语义仍是 OUT+DATA；DATA 4（Chunk of Lead、Mysterious Gift、`[DEBUG] Fortune Cookie`、`[DEBUG] Stove`）；OUT 16（§6.1.1 C 表）；OUT+DATA 13（§6.1.4 P 表）。不要把“入口 mapped”与“规则语义实现”混为一谈。
- 原生对账：未执行。没有含沙暴的完整回放，没有卡池运行时样本，没有比较器版本；默认 `BaseDamage/BaseTickRate/AdditiveScalar` 等仍 UNKNOWN。
- 构建部署：未构建 DLL、未部署、未提交；`DEPLOY=未执行`。
- 剩余风险与阻塞：
  - 4 项 DATA 缺定义：G00/G11；解除条件是新版本 DB 补齐或运行时追踪证明动态生成；矩阵拒绝数已精确到 4 项。
  - 13 项 OUT+DATA 场外卡池：G11/G00；解除条件是用户扩大范围且同版本运行时数据能重建候选集与权重；矩阵 `mapped` 不代表实现。
  - 沙暴默认伤害／步进：G11/G12；解除条件是回放或服务端配置；下一步取倒计时与伤害时间序列。
  - `PercentDamageReduction`（Hunker Down、Leather Jacket 等）：G06/G07/G10；解除条件是实现战斗减伤属性，不能算作沙暴触发器缺口。
  - `[DEBUG] Peacewrought` 的战斗内 `TActionGameSpawnCards`：G11/G03；保持 DEBUG 例外，除非明确要支持调试生成。
  - 16 项 OUT 与 2 条 socket／Heated-Chilled：G11／G01/G05；版本变化或范围扩大时复核。
  - 沙暴 `onSandstorm` 目前按现有 `TriggerQueue` 先进先出结算；与原生同帧优先级／帧序尚未对账，留给 G12 回放验证。
  - 读取 Gold／Income 的战斗缩放光环（Ring King Gauntlets、Cash Deposits、Commercial Zoning、Gold Garnish、Slingshot Golden）：G05；需要真实跑图货币值，不属于本轮经济修改 no-op。
  - `Base Rage Effect`：G06；`TTriggerOnCardSelected`／`TTriggerOnEncounterExited` 等纯场外触发器：G11/G03 的健壮性 OUT，不是战斗规则。
- 下一条可执行动作：
  1. G12：取一段含沙暴的真实回放，记录倒计时开始、首次伤害、每次伤害与 tick 间隔，补 6.1.5 B。
  2. G06/G07/G10：实现 `PercentDamageReduction` 与 Rage，复跑 Hunker Down／Leather Jacket 矩阵。
  3. G00/G11：取商店／DayStarted／CardSold 的运行时卡牌序列，或定位 `BazaarCardDealer` 池选择方法；在此之前不猜候选。
  4. G00：新版本 DB 重读 4 项缺 AbilityId／AuraId 的模板路径。
  5. 集成人：由并发组分别修复 `CardMapper`、`NativeRuleExpansion`、`NativeRuntimeRules`、`G07Enchantment`、`G01TargetConsistency` 的失败与 `G09LifecycleTests` 的在途编辑后复跑整包；G11 只认领 `G11Sandstorm` 与 36 模板矩阵。
- 锁释放：是（未提交、未部署；未持有独占锁）。

### 2026-10-24 · G06-02 · 双方开场属性、英雄身份、怒气与暴怒事件的纯模板／实时分层复核

- Owner / Reviewer / 分支 / 基线提交或补丁哈希：Qoder（G06 本轮续作）／待复核／`main`／`6eb032a` + 共享工作区未提交改动；本轮不提交、不部署。
- 状态：IMPLEMENTED（G06 定向测试通过；整包仍被其它组并发改动阻塞，非 VERIFIED；无真实回放／游戏内对账）。
- 范围：`docs/native-rule-task-index.csv` 中 G06 主责的四个种子 Warpaint `153dfd6d…`、Vengeful Sigil `05a32402…`、Burning Shield `7931571a…`、Toxic Shield `a9b441e1…`，以及同名怪物／物品 Wild Boar（怪物 `5d779101…` 为 G00 种子，物品 `e01ac7ab…` 为 G06 主责）。重点不是只补“能映射”，而是分别验证：
  - 纯模板 `AttributesAreLive=false`：静态光环在映射期结算一次，战斗初始化不得重乘。
  - 实时快照 `AttributesAreLive=true`：快照已含 Shiny 等实时倍率时先反推再重算，不能二次放大。
  - 战斗内临时状态：Rage／Enraged／Shield／Burn／Poison／`Custom_0` 只存在于当前 `BattleContext`，不能进入下一场开场。
- 原生证据：
  - 游戏包 `GameData.db.zip` SHA256 `fdfd3087e6a23eda2c9f53be68b012006cc949d03e8666dd51ad65abc71b61e5`；解压出的 `GameData.db` SHA256 `2b9c6b3765c72a21a5ebfb7336c13251d09017b87c0e599671b14299800ba092`；`vendor/GameData/GameData.db` 与解压库哈希一致。
  - 新夹具 `tools/actionaxis_plugin/tests/fixtures/native_g06_rage.json`（11 个完整模板）：SHA256 `2ed190310a936342056e30c6c91b68b4a719b8be670b91ba63316c4c1cdd83a5`。由 `python tools/rule_inventory.py --db vendor/GameData/GameData.db --fixture-names "cards:Warpaint" "cards:Vengeful Sigil" "cards:Burning Shield" "cards:Toxic Shield" "cards:Wild Boar" "cards:Feeding Frenzy" "cards:Wildroot Amulet" "cards:Blueberry Pie" "cards:Basket" "cards:Truffles" "monsters:Wild Boar" --out tools/actionaxis_plugin/tests/fixtures/native_g06_rage.json` 生成；含怪物棋盘引用的全部 5 张物品与 Feeding Frenzy，避免测试只取部分模板。
  - 任务索引 `docs/native-rule-task-index.csv` SHA256 `b72d84f67530ac82fd4c4b1e5b8d4de9593fda35d1e9f52004d046e6f7bc23f3`，G06 主责 136 条。新增 `tools/g06_native_verify.py` 逐条核对夹具 ID、主责组、原生模板与数据库完全一致；输出 `g06_primary 136`、`fixture_templates 11`、`OK G06 native verification`，退出码 0。
- 完成内容（五步）：
  1. 类型识别：四个种子模板的 `TCardItem／TCardSkill`、Tier 属性、Ability／Aura 引用逐字段核对；Wild Boar 怪物 `Player.Attributes` 为 `HealthMax=650`、`RageMax=50`、`EnragedDurationMax=5000`，Feeding Frenzy Silver 的 `RageApplyAmount=15`。
  2. 目标／主体：Warpaint 左侧物品、Vengeful Sigil 对手手牌、Burning／Toxic Shield 自身半血、Wild Boar 的 SelfHand HiddenTag=Shield，均在双方阵营分别跑；新增英雄身份从 `Run.Opponent.Hero` 读取，怪物战保持 null，不再拿玩家英雄顶替。
  3. 结算／数据：新增 `G06RageStateTests`，覆盖 Warpaint 三品质×有无 Shiny；Vengeful Sigil 三品质×Shiny、敌方物品触发、Weapon Lifesteal 随暴怒开关；Burning／Toxic Shield 半血以上不触发、首次跌破半血触发一次、第二次不重复、下一场重新可触发；Wild Boar 纯模板开场、4 次 15 怒气、8.05 暴怒、8.1 四张 Shield 卡各 +40、13.05 暴怒结束；Wild Boar 物品自身 `Custom_0=40` 的暴怒增益只在本场有效。
  4. 排空：保留原生下一帧队列时序；暴怒结束由 `TickEnrage` 在 13.05 发出 `enrageEnd`，Sigil 的 Weapon Lifesteal 在结束后由动态光环层撤销。`Base Rage Effect` 仍未实现（见剩余风险），本轮没有把它伪装成已覆盖。
  5. 测试：`G06RageState` 9 个测试方法（含两个 Shield 共用方法各跑双方、双方开场属性与纯模板／实时分层），`Program.cs` 注册 `G06RageState`；`LiveSkillReaderTests` 增加 `Run.Opponent.Hero` 的读取、快照 JSON 的 `enemyHero` 与清理断言。
- 跨组修复（需 G03／集成人复核）：
  - 现象：G03 并发改动把同一张卡内的触发器按 `Priority` 排序后，Burning／Toxic Shield 与 Wolf Mask 的“首次”计数在判定阶段先看到 `Custom_0=1`，主效果被跳过；修复前 `PASS G06RageState` 曾变为失败，`G03TriggerRuleTests.WolfMaskFirstUseEachFight` 也失败。
  - 处理：在 `Triggers.ResolvePayload` 内按“同一张卡先完成全部前置判定，再按现有优先级执行效果”收集触发；不删除优先级排序。这样计数能力即使先执行，主效果的前置仍按事件开始时的 `Custom_0` 判定。修复后 `PASS G06RageState`、`PASS G03TriggerRules`、`PASS AuditRegression`。
  - 代价／风险：若原生对同卡后续能力采用惰性前置判定，本改动会让“后置能力依赖前一个能力结果”的组合多触发；这是共享 `Triggers.cs` 语义变更，必须由 G03／集成人用原生回放复核。未提交。
- 改动文件（仅 G06 直接改动；共享工作区还含其它组未提交改动）：
  - `tools/actionaxis_plugin/tests/G06RageStateTests.cs`（新增）
  - `tools/actionaxis_plugin/tests/fixtures/native_g06_rage.json`（新增，11 模板）
  - `tools/g06_native_verify.py`（新增，夹具／索引／数据库一致性验证）
  - `tools/actionaxis_plugin/tests/Program.cs`（注册 `G06RageState`）
  - `tools/actionaxis_plugin/engine/LiveBoard.cs`（新增 `BoardReadResult.EnemyHero`；同文件另有其它组字段）
  - `tools/actionaxis_plugin/plugin/LiveSkillReader.cs`（读取 `Run.Opponent.Hero`、清理与调试快照）
  - `tools/actionaxis_plugin/plugin/ActionAxisPlugin.cs`（写入 `mapped.Config.EnemyHero`；同文件另有其它组改动）
  - `tools/actionaxis_plugin/tests/LiveSkillReaderTests.cs`（Opponent 假对象与断言）
  - `tools/actionaxis_plugin/engine/Triggers.cs`（同卡前置快照；跨组，见上）
  - `docs/native-rule-coverage.md`（§7.2 G06 状态行、§6 的 Shiny 说明与本日志）
  - 未纳入：其它组对 `Types.cs`、`Battle.cs`、`Effects.cs`、`G08TransformTests.cs` 等的并发改动；`build/g06_harness.cs`、`build/g06_harness.exe`、各日志仅为本轮证据，不入库。
- 验证：
  - 会话开始（本轮首次）`bash tools/actionaxis_plugin/compile_tests.sh`：退出码 0，全部套件 `OK`，作为本轮改动前基线。
  - 当前 `bash tools/actionaxis_plugin/compile_tests.sh`：编译通过；`PASS LiveSkillReader`、`PASS G06RageState`、`PASS AuditRegression`、`PASS G03TriggerRules`。整包退出码 1，最终复跑合计 4 个断言失败，全部在 `G09Lifecycle`（`AbductedCowTransformsInsteadOfDying` 与 `DroneCrusherTransformsTriggerTargetNotItself`）；这是 G09 在途改动，G06 断言全部通过。共享工作区会继续变化，不能用本结果声称整包通过。
  - 定向 `tools/actionaxis_plugin/build/g06_harness.exe`（`LiveSkillReaderTests` + `G06RageStateTests`）：输出 `OK`，退出码 0；不依赖其它组的失败套件。
  - `bash tools/build_actionaxis_plugin.sh --compile-only`：退出码 0，仅既有 Unity `CS0618` 过时 API 警告；`ActionAxisEngine.dll` SHA256 `b108a90a248d49c89b635f49c3f334ea2df97b2bb9a8aabba411556aac6d8fc5`，`ActionAxisPlugin.dll` SHA256 `6e04c6a4e23bdc4c1af6d68fab3dd12dab274ef55353e3098ca2e46cf8aa07f1`。未写入游戏目录。
  - `python tools/g06_native_verify.py`：退出码 0，输出见上；夹具 11/11 与数据库一致，四个 G06 种子及 Wild Boar 物品主责 G06，怪物 G00。
  - `python -m unittest discover -s tools -p test_rule_workplan.py -t . -v`：5/5 `OK`，退出码 0（工作索引生成规则未被本次改动破坏）。
  - `git diff --check`：退出码 0（仅 CRLF 提示）。
  - 前端未复跑：本轮未改前端；上次基线为 37 文件／102 测试通过，不能把旧结果当作本轮验证。
- 原生对账：未执行。无 G06 真实回放样本、无比较器版本；本轮是“原生模板级 + 模拟事件级”验证，不是逐事件回放一致。`enrage`／`enrageEnd` 的 8.05／13.05 与 8.1 的下一帧盾值来自模拟帧，需 G12 用真实回放确认。
- 构建部署：仅 `--compile-only`；未复制 DLL、未重载游戏、未部署，`DEPLOY=未执行`。当前 DLL 含其它组并发改动，不能当作 G06 独立发布物。
- 剩余风险与阻塞：
  - `Triggers.cs` 的同卡前置快照是跨组语义改动，必须由 G03／集成人复核；若原生按惰性前置结算，需退回并改用更窄的计数机制。
  - `EnemyHero` 来自 `Run.Opponent.Hero`；`LiveMonsterReader` 已注明 `Run.Opponent` 可能是上一场遗留，怪物战应为 null，真实 PvP 快照尚未验证。英雄缺失时 `CardMatches` 的兜底仍会把未知身份当作本方，需以真实对局确认。
  - `Base Rage Effect`（`TCardPlayerEffect`，不在 1810 索引）仍未实现：暴怒时清 Slow／Freeze、暴怒期间 +10% `PercentCooldownReduction`、暴怒结束移除；这是 G06/G11 的已知缺口，本轮未改。
  - Warpaint／Sigil 的 Radiant `PercentFreezeReduction` 仍属 G07；Wild Boar 怪物无英雄字段，`EnemyHero=null` 是预期。
  - 整包仍有其它组 4 个断言失败（最终复跑，全部在 `G09Lifecycle`）；共享工作区不能标记 Gate A 全绿。无真实回放，Gate C 未完成；未部署，Gate D 未执行。
- 下一条可执行动作：由集成人先复核 `Triggers.cs` 的同卡前置快照与 `EnemyHero` 读取；待其它组失败套件归零后复跑 `bash tools/actionaxis_plugin/compile_tests.sh`，再由 G12 取含 Warpaint／Sigil／Burning Shield／Wild Boar 的真实回放做逐事件对账。锁释放：是（未持有独占锁；未提交、未部署）。

### 2026-10-24 · G08-01 · 场上复制变形原生对账与回归

- Owner / Reviewer / 分支 / 基线提交或补丁哈希：Qoder（G08 本次续作）／未指定／`main`／`6eb032a` + 共享工作区未提交改动；本轮不提交、不部署。
- 状态：IMPLEMENTED（G08 切片）；原生回放事实对账已补，`G08Transform` 隔离回归通过；不是 VERIFIED（3D Printer 冷却残差、附魔冲突与游戏内 D 门禁未关闭）。
- 范围：3D Printer／Mirror／Hologram Projector／Quicksilver／Virus 的真实模板前提、供体、属性／附魔继承、占格、新实例冷却、旧队列失效、前后历史。场外随机卡池仍 OUT，归 G11。
- 原生证据：live DB `C:\Users\42552\AppData\LocalLow\Tempo Storm\The Bazaar\prod\cache\GameData.db`；回放 `tmp_g08/replays/07f044ee…`（Mirror）、`40bcb27a…`（Hologram）、`ebde2082…`（3D Printer）、`084bd840…`（Virus）。对账脚本 `tmp_g08/g08_reconcile.py`，输出 `tmp_g08/g08_reconciliation.json`（6 条变形记录，0 failures）。
- 补充独立他人回放（2026-10-25 核查 `D:\巴扎plus\CombatReplays`）：200 份 `.payload.mpack.gz` 全部可解析，`Version=1`；14 份含变形。G08 相关样本：Quicksilver `86f5118700d5…`（199 帧；Scalpel 副本为 Gold、供体 Scalpel 为 Diamond，新冷却 4950/5000）、Quicksilver `395a1e4d7b79…`、Mirror `921ccdd8f48f…`（Blazehowl Warden 7950/8000）与 `ab8439ff38b3…`（Dragon Heart 9450/9500）、Hologram `7b0a2ae8515a…`（Fiber Optics）、Virus `82fb1bec8e2f…`／`9298c50baf5b…`（Silver Virus，CDMax 8000）。这批没有 3D Printer，810 ms 冷却残差仍缺样本；回放只本地读取，未提交。
- 完成内容：
  1. `SkillMapper.MapAction` 的 `TActionCardTransform` 不再把 `SpawnContext.Groups/Behaviors` 一律当场外卡池拒绝；只接受已取证的 `TSpawnFilterTarget/TSpawnFilterIdList`、`Sequential/Random`、`IgnoreTierTable/AllowDuplicates/IgnoreHero/InheritTier`，未知字段继续 fail-closed。`TTargetCardRandom` 与 `OpponentHand/OpponentBoard` 的卡数前提同时接入 `PrerequisitesSupported/Match`。
  2. `NativeCardAbilityMapper.CopyAtTier` 从供体原生模板按**来源卡品质**重建复制品；镜像 Gold→The Big One Gold，Hologram Gold→Gamma Ray Gold（Poison 4→6），Quicksilver Silver→Tiered Small Silver，3D Printer Diamond→Diamond Event Poster（ChargeTargets=2）。副本不再继承供体的实时伤害、临时标签或临时属性。
  3. `NativeTransform.Apply` 按被变形卡已流逝的冷却进度计算新实例：`newCooldown = newCDMax − (oldCDMax − oldCooldown)`；Mirror 原生 2950/3000→13950/14000，Virus 目标 3950/6000→5950/8000、1950/6000→3950/8000 与公式一致。新实例仍走 `Generation++`，旧排队施放在 `ExecuteNativeCasts` 代际检查处失效。
  4. 槽位：副本按实际占格连续放置，不挤动右侧卡；空间不足保留旧卡并告警。历史：`RecordItemState` 保留旧形态与新副本出生时刻；`G08Transform` 验证旧卡 `At(0.5)` 与副本 `At(1)`。
  5. 附魔：来源附魔只对未附魔供体生效；供体已有附魔时保留供体附魔，按原生 tooltip 的 “if able” 解释；映射失败回滚并告警。
- 新增／修改文件：`engine/Transforms.cs`、`engine/SkillMapper.cs`、`engine/NativeCardAbilityMapper.cs`、`tests/G08TransformTests.cs`（新增）、`tests/Program.cs`、`tests/fixtures/native_g08_transforms.json`（新增，8 个 live DB 模板，含 Event Poster，SHA256 `A8D169A37FD038DBDBE5557D6B7575C58AC018802507BE32508D37FC5D49C9ED`）、本文件。共享文件中含其它组并发改动，未认领。
- 新增测试（`G08TransformTests.Run`）：`MirrorCopyUsesSourceTierAndElapsedCooldown`、`HologramCopyUsesSourceTier`、`QuicksilverCopyUsesSourceTier`、`PrinterCopiesUseSourceTierAndKeepSlots`、`PrinterCopiesRealEventPosterAtSourceTier`、`VirusCopiesUseSourceTierOnBothSides`。覆盖真实模板入口、错误供体不继承、来源品质、已流逝冷却、三副本槽位与实例 ID、Event Poster Diamond（ChargeTargets 2）、双方 Virus、旧／新历史。
- 验证：
  - 隔离编译当前引擎 + `G08TransformTests`：`PASS G08Transform` / `OK`（`tmp_g08/g08_only.log`）；快照生成脚本 `tmp_g08/make_snapshot.py`、`tmp_g08/compile_snapshot.sh`。快照仅为绕过并发文件中的 CS0136 同名局部变量做了不改语义的改名，G08 代码未改。
  - 整包快照（在并发 G09 文件尚未完成前）：`PASS Transform`、`PASS G08Transform`、`PASS AuditRegression`、`PASS G12UiHistory`；整包退出码 1，失败集中在并发组 `CardMapper`（EnchantmentMapper 旧 key）、`NativeRuleExpansion`（G01 Absolute 区域）、`G09Lifecycle`、`G07Enchantment`，均非 G08。随后 G09 测试源码被并发编辑为未完成状态，最终整包无法编译；不能用该状态反推 G08 失败。
  - `python tmp_g08/g08_reconcile.py` → `transform records: 6 / failures: 0`。
  - `npm.cmd --prefix D:\巴扎plus test -- --run` → 37 文件 / 102 项全绿；本轮无前端改动，仅作为回归门禁。
  - 未构建 DLL、未部署、未提交。
- 剩余风险与阻塞：
  - 3D Printer 回放 `ebde2082` 新副本观测为 2190/3000，比“保留已流逝进度”公式多约 810 ms；该样本同帧有其它 Tool/冷却效果，尚不能归因，未写成已验证规则。需要同版本回放隔离该效果后再补。
  - 供体已有附魔与来源附魔冲突的“if able”语义尚无回放样本；当前保留供体附魔，不能标为原生确认。
  - 复制品的静态“加标签”光环（例如 Event Poster 相邻物品变 Dragon）仍由临时单卡映射，未在真实棋盘重放；3D Printer 复制 Event Poster 的这一分支未验证，不能当成完整属性继承。
  - Mirror 的 Legendary 排除、Hologram 的 Legendary 排除来自 tooltip/现有校准；live `SpawnContext` 只写 Size/Id，仍需一条 Legendary 边界回放确认。
  - 游戏内重启、UI 拖回变形前／出生后／离场后 D 门禁未做。
- 下一条可执行动作：G12 用 `07f044ee`／`ebde2082` 重建同一开场棋盘，做逐事件镜像比较并输出 810 ms 差异归因；G07 补附魔冲突回放；集成人待并发编辑稳定后复跑整包。锁释放：是（未提交、未部署）。

### 2026-10-25 · G08-02 · A 类 8 张全量、B 类 OUT 与 20 个原生变形事件

- Owner / Reviewer / 分支 / 基线：Qoder（G08 续作）／未指定／`main`／`6eb032a` + 共享工作区未提交改动；本轮不提交、不部署。
- 状态：IMPLEMENTED（G08 全量 A 类）；仍不是 VERIFIED。
- 全量盘点：live DB 38 个 `TActionCardTransform` 模板；A=8（3D Printer、Mirror、Hologram、Quicksilver、Virus、Green Screen、Blender、Grabinator），B=9 场外随机池 OUT，C=0，D=21（EncounterStep/DEBUG/非战斗）。G09 的 TransformDestroyed 2 个和顶层 Transform 14 个单列。
- 新增实现：
  1. `Green Screen`：随机附魔自身后复制左侧 Medium，副本强制使用来源附魔。
  2. `Blender`：相邻非 Legendary Small 变为固定 Slushee；`FindFixedDonor` 从 `BattleConfig.NativeTemplates`（插件 `LiveMonsterReader.LoadCardTemplate` → `GetCardById`）或 `SpawnPool` 读取原生模板，缺失时 fail-closed。
  3. `Grabinator`：`TTriggerOnBeforeCardDestroyed` + 跨侧敌方最左 Medium 供体；测试覆盖触发事件与直接效果两条路径。
  4. Mirror 按 live `SpawnContext` 不再额外排除 Legendary；Hologram/Quicksilver 排除同一模板的所有副本，Quicksilver 另排除 Legendary。
  5. `SkillMapper` 接受 `TSpawnFilterTarget` 的 `TTargetCardXMost`、`SelfNeighbors` 与 `TSpawnBehaviorInheritEnchantment`；未知字段仍拒绝。
- 测试：`G08TransformTests` 现有 2D/多牌型用例覆盖 8 张 A 类：Mirror 来源品质/冷却、Hologram 来源品质、Quicksilver 来源品质、3D Printer 三副本/Event Poster Diamond、Virus 双方、Green Screen 附魔复制、Blender 固定 Slushee、Grabinator 直接效果与 onBeforeDestroy 触发、Mirror Legendary、Hologram/Quicksilver 同模板排除；`OffFieldTransformPoolsStayFailClosed` 验证 Hacksaw/Potion Potion/Recycling Bin 仍拒绝随机池。夹具 `native_g08_transforms.json` 12 个 live 模板，SHA256 `840048C20BBA3C0D0B9B7DBCCFE071BA86D51FFA8ECD104A11DEB6AEE6F48849`。
- 原生对账：`D:\巴扎plus\CombatReplays` 200 个 payload + 本地 5 个 JSON 全部 Version=1；25 个 code=9 事件中 20 个 G08，`g08_reconcile.py` 输出 20 条 / 0 failures；新增 3D Printer（本地唯一）、Mirror Legendary 2 例、Quicksilver Scalpel、Virus 连续 6 次感染等证据，明细 `tmp_g08/analysis/replay_facts_all.json`。
- 验证：隔离编译当前引擎 + `G08TransformTests` → `PASS G08Transform` / `OK`（`tmp_g08/g08_only.log`，为绕过并发文件中的 CS0136/未声明 source 做了快照内局部改名，G08 代码未改）。整包在一次快照中可编译：`PASS Transform`、`PASS G08Transform`、`PASS G08PoolTransform`、`PASS AuditRegression`、`PASS G12UiHistory`；退出码 1 的 17 个断言集中在并发组 `G09Lifecycle`、`AllHandsRules`、`G01TargetConsistency`、`G01SocketEffect`，不是 G08。最终重试时 `G01TargetConsistency` 被并发编辑为未完成，整包再次无法编译；不把该状态归因于 G08。临时目录完整 Engine+Plugin 编译 → exit 0，仅既有 Unity CS0618 警告。`npm.cmd test -- --run` → 37 文件 / 102 项全绿。
- 剩余风险：3D Printer 回放副本 2190/3000 与旧 after-frame 2950/4000 的 elapsed 1050 相差 240ms，未归因；副本静态加标签光环（Event Poster→Dragon）未在真实棋盘重放；供体附魔冲突缺回放；D 门禁未做。B 类 9 张继续 OUT，不能把 `mapped` 当实现。
- 下一条：G12 用 `ebde2082`/`86f51187` 重建开场棋盘做逐事件比较并解释 240ms；G07 补附魔冲突回放；集成人待并发编辑稳定后复跑整包。锁释放：是（未提交、未部署）。

### 2026-10-24 22:40 · G00-01 · 完整模板、稀疏品质、附魔、双方与怪物读取矩阵、缺定义与基线冻结
- Owner / Reviewer / 分支 / 基线提交或补丁哈希：OpenCode（本轮）／待集成人复核／`main` 共享工作区（未提交）／`6eb032a` + 当前工作区。
- 状态：IMPLEMENTED（G00Data、G00Coverage、G00 审计与 `--check` 通过；整包 C# 全部套件 `OK`；未标记 VERIFIED：无 G00 专属原生回放、未部署；未提交）。
- 范围：
  - `cards` 表 1146 个 `TCardItem`、498 个 `TCardSkill` 的全量完整模板入口；品质声明、Legendary→Diamond 查询、13 种附魔、双方阵营、`template/live/mixed` 三场景。
  - `monsters` 表 166 个 `TMonster` 的 `Player/Attributes/Hand/Items/Skills/Socket.Effects` 读取矩阵；怪物手牌 978 条、技能 262 条。
  - 56 个模板的 `Tiers.*.AbilityIds/AuraIds` 缺定义；9 条 `TCardPlayerEffect` 与 2 条 `TCardSocketEffect` 索引外规则。
  - 不改变任何 Ability/Aura、触发器、目标、数值、伤害或 UI 语义；G00 只做数据读取、入口枚举、证据冻结。
- 原生证据：
  - 数据库 `GameData.db.zip` SHA256 `FDFD3087E6A23EDA2C9F53BE68B012006CC949D03E8666DD51AD65ABC71B61E5`；索引 `docs/native-rule-task-index.csv` SHA256 `B72D84F67530AC82FD4C4B1E5B8D4DE9593FDA35D1E9F52004D046E6F7BC23F3`（1810 行，0 重复／0 缺失）。
  - 完整模板输入 `native_mapping_templates.jsonl` SHA256 `ED9EBC2695A4FF242F1DEFAC161FCA145C3E4FEDD8FCC75D8D34236C140E4494`（1644 行）；怪物输入 `native_monsters.jsonl` SHA256 `59138A23BE6A71DE88CC057A4D3A71C564E834F908611B16EFBC671610A4EE34`（166 行）。
  - 映射矩阵 `native_mapping_report.tsv` SHA256 `9A4677179EC13E4A7EE84844B0D95AC3AC54849EDCF86C258E972212581C9057`（387678 行）；怪物读取矩阵 `native_monster_report.tsv` SHA256 `67C6B59865F22DFCF6665E4A3802826E496CACC8A131A6BE62F29F71C9012EED`。
  - 夹具 `tests/fixtures/native_g00_templates.json` SHA256 `3292A5A766C2D543177261FA78265E4CE383273E6047421C53B6DFC612DA164A`（11 个完整模板，逐字节与 DB 一致）。
  - 冻结基线 `data/g00_baseline.json` SHA256 `4148EB8EE23A75C49D13B359E9FA7F6BD3216058BB1FA88A151C6B1E7F4D5DEF`（schema 2）；逐行证据、缺定义 164 条原始路径、怪物 166 模板布局和源码哈希均在其中。
  - 缺定义：56 个模板／64 个唯一 `(模板, 字段, ID)` 引用／按品质展开 164 处；缺失 ID 仅为 `0`–`7`。已排除稀疏品质继承与独立定义表；不升级品质、不造数值。
  - 怪物读取缺口：16 条手牌模板缺失（`Player.Hand.Items[...]` 原始路径已保留，含 `c7b72f4f-3bff-46f4-9c94-55243ad6703b` 与 Art Test Monster 两条）；4 条手牌品质稀疏（PVE_Karnok Bronze 引用 Silver 起始物品）；62 条技能品质稀疏（怪物实例品质低于技能声明的最低品质）；3 条 `TCardInstanceSocketEffect`；0 条缺失技能模板／缺失附魔。
  - 索引外：`TCardPlayerEffect` 9 条、`TCardSocketEffect` 2 条已记录在 `out_of_index.rows`；未擅自加入 1810 卡牌索引。
- 完成内容：
  - 完整模板：DB → JSONL → C# 映射入口全链路；1644 个物品／技能逐字节一致，夹具 11 个逐字节一致，索引 1810 条零重复零遗漏。
  - 稀疏品质：物品只枚举声明品质并补 Legendary→Diamond 别名；技能枚举 5 个标准品质，未声明品质标 `inactive-sparse` 且必须为 `inactive`；冻结 `tier_layout` 三项 SHA256，成员变化不能只靠计数掩盖。
  - 附魔：以每个模板实际声明的附魔为准，枚举「无附魔＋全部声明附魔」；保留 `附魔未应用` 告警文本，矩阵新增 `enchantment_status` 列及一致性断言（`none/applied/warning/blocked-base`），告警不能被过滤掉。
  - 双方读取：物品与技能同时跑 Player／Enemy；`status_by_side` 冻结两侧各 169207 个 mapped／各状态计数，`mixed` 场景强制 Mak（玩家）／Dooley（敌人）；怪物作为敌方侧独立矩阵，逐条保留 `Player.Hand.Items[i]`／`Player.Skills[i]` 路径。
  - 基线：`g00_audit.py` 新增缺定义扫描、怪物布局、索引外规则、矩阵双方状态、阻断／告警 ID 列表、品质布局摘要；`--check` 与冻结 JSON 逐字段比较。
- 改动文件：
  - `tools/g00_audit.py`（扩展审计；未改引擎）
  - `tools/rule_inventory.py`（新增 `--monster-input`；未改既有 `--mapping-input/--work-index` 语义）
  - `tools/test_g00_audit.py`（新增离线回归）
  - `tools/actionaxis_plugin/tests/MonsterCoverageRunner.cs`（新增只读怪物矩阵）
  - `tools/actionaxis_plugin/tests/G00DataTests.cs`（新增怪物矩阵用例）
  - `tools/actionaxis_plugin/tests/Program.cs`（仅新增 `--monsters` 分支；未改其它组注册）
  - `data/g00_baseline.json`（重新冻结）
  - `docs/native-rule-coverage.md`（本日志、G00 状态行、§1.2 矩阵数字与头部基线）
  - `docs/native-rule-task-index.csv`（按文档路由重新生成，SHA256 未变）
  - 未包含其他组的 `engine/*`、`plugin/*`、UI、测试夹具与构建 DLL 改动；`build/*.log` 仅为本轮证据，不入库。
- 新增测试：
  - C# `G00DataTests.MonsterCoverageMatrixKeepsPathsAndTierSources`：1 个合成怪物＋3 张完整卡模板；断言 7 行矩阵、缺失手牌／技能路径、`undeclared-sparse`、缺失附魔、`resolved/missing/warning` 分类与汇总计数。首次运行因测试预期把「缺失技能」也算作稀疏品质而失败（期望 2、实际 1），修正为只统计有模板但品质未声明的条目后通过。
  - Python `tools/test_g00_audit.py` 6 项：缺定义按品质展开、已定义引用不误报、怪物缺失模板／稀疏品质／缺失附魔路径、矩阵双方状态与阻断 ID、干净怪物报告、缺怪物行时报错。首次运行暴露审计对「缺失模板且无附魔」的 `enchantment_status` 与 socket 告警路径假设错误（审计期望 17 处不一致），改为与 `MonsterTemplateExtractor` 的实际路径语义一致后通过。
  - 注册：`Program.cs` 新增 `--monsters 怪物.jsonl 卡牌.jsonl 输出.tsv`；既有 `G00Data`、`G00Coverage` 注册未变。
- 验证：
  - `bash tools/actionaxis_plugin/compile_tests.sh` → 退出码 0，整包全部套件 `OK`（含 `PASS G00Data`、`PASS G00Coverage`）；证据 `tools/actionaxis_plugin/build/g00_compile_final6.log`。
  - `EngineTests.exe --coverage ...` → 退出码 0；387678 行；item mapped/unsupported `338414/34324`，skill simulated/live-applied/inactive/out-of-combat/unsupported `8142/740/4510/384/1164`；至少一个入口受阻物品 125／技能 65；无附魔时受阻物品 114；附魔告警行 420；实时属性告警行 0。
  - `EngineTests.exe --monsters ...` → 退出码 0；166 怪物／978 手牌／262 技能；17 个怪物带读取告警；缺失模板 16；未声明品质 66（手牌 4＋技能 62）；缺失附魔 0。
  - `python tools/rule_inventory.py --mapping-input ...` → 1644；`--monster-input ...` → 166；`--mapping-report ...` → 退出码 0；`--work-index ...` → 1810 条、43 个种子、G00 主责 166，SHA256 未变。
  - `python -m unittest tools.test_g00_audit -v` → 6/6 `OK`；`python -m unittest tools.test_rule_workplan -v` → 5/5 `OK`。
  - `python tools/g00_audit.py --output data/g00_baseline.json` → 写入成功；`python tools/g00_audit.py --check` → `基线核对：OK`、`G00 审计通过`。
  - `npm.cmd --prefix d:\巴扎plus test -- --run` → 退出码 0，37 文件／102 测试全绿。
  - `bash tools/build_actionaxis_plugin.sh --compile-only` → 退出码 0，仅既有 Unity `CS0618` 警告；`ActionAxisEngine.dll` SHA256 `3F2020EB7D1548313AAD0C29EC9DE260C5EB2BBCE0A36FCD520DA47905A0DDE8`，`ActionAxisPlugin.dll` SHA256 `3E04B426239A967214C7DF86BADD2F9355C48FD08CF774A1521625CED182E0E9`。
  - `git diff --check` → 退出码 0（仅 CRLF 提示）。
- 原生对账：未执行。G00 没有专属真实回放；G12 比较器已存在（9bbabba4／084bd840 已跑出 DIFF），但本轮没有用它做 G00 样本的对账。G00 只证明完整模板可进入映射入口、读取矩阵与数据库一致，不证明逐事件数值／帧序。差异数、UNKNOWN、帧区间均不适用；证据路径 `tools/actionaxis_plugin/build/native_monster_report.tsv` 与 `data/g00_baseline.json`。
- 构建部署：仅 `--compile-only`；未复制 DLL、未重载游戏、未部署，`DEPLOY=未执行`。上述 DLL 含共享工作区其它组并发改动，不是 G00 独立发布物；哈希取自本轮构建结束时刻，共享 `build/` 随后可能被其它组重建。
- 剩余风险与阻塞：
  - 387678 个入口组合中仍有 125/65 个模板至少一个组合受阻；`mapped` 只代表入口映射，不代表效果语义或原生一致。
  - 56 个模板缺 `AbilityIds/AuraIds` 定义（DATA，G00/G11）；16 条怪物手牌模板缺失（DATA）；4＋62 条怪物品质稀疏、3 条插槽效果未执行；均保留原始路径，未补造数据。
  - 9 条 `TCardPlayerEffect`（含 `Base Rage Effect`）与 2 条 `TCardSocketEffect` 不在 1810 卡牌索引，已单独记录；实际效果仍归 G06/G01/G05/G11。
  - 整包 C# 当前全部套件通过；G00 仍不能代替原生回放或其它组语义验收。共享工作区会继续变化，任何 G00 `source_sha256` 覆盖文件或引擎文件变化都会使 `--check` 失效，必须重跑并重新冻结。
  - `tools/actionaxis_plugin/build/native_mapping_report.tsv` 是共享矩阵产物；本轮一次 `--check` 在其它进程同时写入时读到半成品并抛 `TypeError`，复跑通过。复跑前必须确认没有并发矩阵进程，否则应先重生成再冻结。
  - 未部署，Gate D 未执行；无真实回放，Gate C 未完成。
- 下一条可执行动作：
  1. 整包 C# 已在 `tools/actionaxis_plugin/build/g00_compile_final6.log` 达到 `OK`；集成人复核后 G00 可从 IMPLEMENTED 进入待复核。若共享工作区继续改动，先复跑该命令确认仍为 `OK`。
  2. 其它组若改动 `g00_audit.py` 的 `SOURCE_FILES` 所列文件，重新执行 `python tools/rule_inventory.py --mapping-input ...`、`--monster-input ...`、`EngineTests.exe --coverage ...`、`EngineTests.exe --monsters ...`、`python tools/g00_audit.py --output data/g00_baseline.json`，再 `--check` 并把新哈希写入本文件。
  3. G11/G00 在新版本 DB 或运行时卡池可用时复核 56 个缺定义与 16 条怪物缺模板；不得用相似 ID 或提升品质补齐。
  4. G12 取含 G00 种子的真实回放做逐事件对账；G00 矩阵不能替代比较器结论。
  锁释放：是（未持有独占锁；未提交、未部署）。

### 2026-10-24 · G07-01 · 附魔主动/被动/光环、战斗内附魔、暴击伤害、抗性与免疫

- Owner / Reviewer / 分支 / 基线提交或补丁哈希：Qoder（G07 本次续作）／未指定／`main`／`6eb032a` + 共享工作区未提交改动；本轮不提交、不部署。
- 状态：IMPLEMENTED（G07 切片）；隔离 G07 回归与整包 `G07Enchantment` 均通过，整包当前 7 个失败来自并发组 G05DynamicAura（见验证）。不是 VERIFIED：无 G07 真实回放逐事件对账，未做游戏内 D 门禁。
- 范围：
  - G07 主责种子 Dishwasher `027ef534-d047-4e18-8496-d9fd4773e15d`、Security Blanket `02d7a406-a6eb-4b46-8975-1f69b0fde428`、Spirit Diffuser `1b79b3e5-5870-48f7-a877-add0721f9ab6`。
  - 关联重点卡：Gamma Ray `0229faa7-2eec-4746-9134-ade4f3aebe45`（Radiant/Deadly/Golden）、Cog `ca77281c-f912-4db3-86ce-64842a4ccdaa`（校准路径）、Letter Opener `2989cfa7-86d3-40f7-9946-e417a25d9535`（Deadly `DamageCrit`）、Tinfoil Hat `19885245-2c82-4bf1-91fb-b73a3731923f`（Deadly 被动）、Drowsdrop `a655fb50-b9e1-41ec-b658-07459014f96c`（校准 + Heavy 光环）、Eye of the Colossus `3373c95c-6910-4414-8134-abf2ff1c90fa`（真实 `SellPrice` + Golden）。
  - 覆盖渠道：嵌套 `Enchantments.*.Abilities/Auras/Attributes`、`TActionCardEnchant/EnchantRandom/EnchantRemove`、`TCardConditionalCanCrit/HasEnchantment/EnchantmentEligible`、`DamageCrit`、`PercentSlowReduction`/`PercentFreezeReduction`/`DestroyImmunity`、`SellPrice`。
- 原生证据：`D:/Program Files (x86)/Steam/steamapps/common/The Bazaar/TheBazaar_Data/StreamingAssets/GameData.db.zip` SHA256 `FDFD3087E6A23EDA2C9F53BE68B012006CC949D03E8666DD51AD65ABC71B61E5`；测试夹具 `tools/actionaxis_plugin/tests/fixtures/native_g07_enchantments.json`（10 个完整模板）SHA256 `D503CE6D95EA0C49CD2D84BA49607ECA49D68B930DD5D2DBA063BD3CD241DE4B`；矩阵报告 `tools/actionaxis_plugin/build/native_mapping_report.tsv` SHA256 `E061684FB4EE60AE0F8AED30A2177CE3757EBD99DD07EED42717AB54BA426F85`。证据是原生模板、矩阵与模拟帧，不是游戏回放。
- 完成内容（五步）：
  1. 类型识别：
     - `DamageCrit` 进入 `NativeDynamicAuras` 可逆属性白名单、`NativeCardAbilityMapper.AuraAttribute` 与 `EnchantmentMapper.InheritedAttribute`；不再以“动态光环属性待支持”拒绝 Deadly。
     - Radiant 的 `PercentSlowReduction=100`、`PercentFreezeReduction=100`、`DestroyImmunity=1` 进入实际结算字段；摧毁免疫由 `DestroyItem` 产出 `destroyBlocked`。
     - `EnchantmentMapper` 的旧文本路径现在也接受上述抗性/免疫属性，并把直接附魔属性写入 `LiveAttributes` 与抗性字段，不再只做诊断保存。
     - `EnchantmentMapper` 不再只支持 `TTriggerOnCardFired`：战斗内附魔被动经 `NativeSkillSupport.MapTriggers` 映射，场外触发器按白名单跳过，未知前置条件仍 fail-closed。
     - `TCardConditionalEnchantmentEligible` 已纳入 G07 行为断言（已附魔非同种附魔、无对应定义均拒绝）。
  2. 目标／主体：
     - Spirit Diffuser 的 `SelfHand`/`OpponentHand + ExcludeSelf + HasEnchantment`、Security Blanket 的 `Positional Neighbor`、Spirit Diffuser Radiant 的 `SelfHand`、Deadly 的 `SelfHand + CanCrit/Tag` 均由真实模板进入映射。
     - 不使用合成目标绕过入口；矩阵对 9 个重点模板的双方、全部品质/场景与 13 个附魔组合均为 `mapped`。
  3. 结算：
     - 新增 `DamageSource.DamageCrit`；G07-01 当时按 tooltip 推为 `基础 CritMultiplier × (1 + DamageCrit/100)`，G07-03 回放更正为 `基础 CritMultiplier + DamageCrit/100`：DC=0 为 2×、DC=100 为 3×。Letter Opener Gold + Deadly 的真实模板断言为 40 基础伤害 → 120（3×），治疗/护盾暴击仍只用基础倍率；Deadly 光环通过固定种子实际触发 25→50 暴击。
     - Radiant 直接属性使 2s Slow/Freeze 的实际时长为 0；`DestroyImmunity` 拦截摧毁并记录阻止事件。Security Blanket 的相邻光环把邻居 2s 控制减半，Spirit Diffuser 的 `SelfHand` 光环覆盖全部手牌。
     - Golden 的 `SellPrice` ×2 在真实 Eye of the Colossus 上为 32→64；战斗内 Golden 移除后恢复 32。
     - `NativeEnchant.Apply` 在卡牌战斗开始时没有光环基底时，从 `NativeAuraSeedAttributes`/`LiveAttributes` 建立基准；修复战斗内附魔后 Multicast/SellPrice/CritChance 等基础值被当作 0 的问题。`Remove` 恢复施加前的附魔与光环基底。
     - 校准路径 `NativeItemMapper` 只替换非附魔基础触发器/效果；Drowsdrop + Heavy 的 SlowAmount 1000→2000、Drowsdrop + Golden 的售价 5→10、Cog + Deadly 的相邻 +20% CritChance、Drowsdrop 的 `FromEnchantment` 被动触发均在校准后实际执行。
  4. 排空：
     - 战斗中附魔被动（Tinfoil Hat Deadly 的 `TTriggerOnItemUsed`）在敌人使用物品后实际把 +5% CritChance 加到本方可暴击手牌；未改变既有原生帧延后语义。
     - Spirit Diffuser 的 `TTriggerOnFightStarted` 在开局执行 `EnchantRandom`，产出 `enchant` 事件并把所选附魔的主动/被动/光环层实际附加到目标。
     - `PreventOverride` 阻止覆盖战斗开始时已有附魔；`EnchantRemove` 对战斗开始时附魔仍 fail-closed 并告警，未伪造未附魔基底。
  5. 测试：新增 21 个 `G07EnchantmentTests` 用例并使用真实夹具，注册于 `tools/actionaxis_plugin/tests/Program.cs` 的 `G07Enchantment`。
- 改动文件（G07 直接改动；共享文件同时含其它组并发改动，本单不认领其它差异）：
  - `tools/actionaxis_plugin/engine/Effects.cs`：`DamageSource.DamageCrit`、伤害暴击倍率、`SourceOf` 读取运行时 DamageCrit；未改治疗/护盾暴击路径。
  - `tools/actionaxis_plugin/engine/NativeDynamicAuras.cs`：`DamageCrit` 运行时白名单。
  - `tools/actionaxis_plugin/engine/NativeCardAbilityMapper.cs`：`DamageCrit` 光环属性。
  - `tools/actionaxis_plugin/engine/NativeEnchant.cs`：战斗内附魔基础属性快照与恢复。
  - `tools/actionaxis_plugin/engine/EnchantmentMapper.cs`：抗性/免疫实际字段、直接属性、战斗内附魔被动映射、HiddenTags/NativeEnchantment、`FromEnchantment`。
  - `tools/actionaxis_plugin/tests/G07EnchantmentTests.cs`（新增）、`tools/actionaxis_plugin/tests/fixtures/native_g07_enchantments.json`（新增）、`tools/actionaxis_plugin/tests/Program.cs`（注册）。
  - `docs/native-rule-coverage.md`（§4 状态与 §7.2 G07 看板、本条日志）。
  - 未纳入：其它组的并发文件差异、`build/` 下的临时探针与生成物；未提交、未部署。
- 新增测试（`G07EnchantmentTests.Run`）：
  - `RadiantDirectAttributesBlockControlAndDestroy`（双方；实际 Slow/Freeze 时长与 `destroyBlocked`）
  - `RadiantNeighborAuraHalvesControl`（相邻 50%，远端 0）
  - `RadiantHandAuraReachesEveryCard`（SelfHand 全手牌 50%）
  - `DeadlyDamageCritAddsToCritMultiplier`（Letter Opener Gold + Deadly，G07-03 回放后更正为 40→120）
  - `DeadlyAuraDrivesActualCritRoll`（Deadly 光环 + 固定种子，25→50 与 crit 事件）
  - `DeadlyAuraOnlyMatchesCanCritTargets`（Ray/CanCrit 过滤）
  - `EnchantmentEligibilityFiltersCombatTargets`（EnchantmentEligible 同种/异种/缺定义）
  - `GoldenAuraScalesSellPrice`（Eye of the Colossus 32→64）
  - `CalibrationKeepsEnchantmentAura`（Drowsdrop + Heavy，SlowAmount 2s 实际出手）
  - `CalibrationKeepsGoldenSellPriceAura`（Drowsdrop 校准后 Golden 售价 5→10）
  - `CalibrationKeepsEnchantmentActive`（Cog + Deadly，相邻 +20% CritChance）
  - `CalibrationPreservesEnchantmentPassiveTrigger`（Drowsdrop 校准重建基础触发器后，`FromEnchantment` 的被动触发仍执行）
  - `InCombatEnchantRadiantAppliesAndRemoves`
  - `InCombatEnchantGoldenKeepsBaseSellPrice`
  - `InCombatEnchantShinyKeepsBaseMulticast`
  - `InCombatEnchantDeadlyAddsToBaseCrit`
  - `InCombatPassiveEnchantActuallyTriggers`（Tinfoil Hat Deadly 被动）
  - `SpiritDiffuserFightStartEnchantIsExecuted`（开局随机附魔实际附加）
  - `PreventOverrideBlocksInCombatEnchant`
  - `LegacyRadiantEnchantmentExecutesMechanics`（旧文本路径也实际抗性/免疫）
  - `LegacyPassiveEnchantmentMapsTrigger`（旧文本路径附魔被动）
- 验证：
  - 首次 RED：`bash tools/actionaxis_plugin/compile_tests.sh` 在当时的 `G07EnchantmentTests.DeadlyDamageCritDoublesCriticalDamage`（现名 `DeadlyDamageCritAddsToCritMultiplier`）明确报 `动态光环属性待支持 DamageCrit`；完整日志 `tools/actionaxis_plugin/build/g07_red.txt`。
  - 修复后隔离运行当前引擎 + `G07EnchantmentTests`：`PASS G07Enchantment` / `OK`。
   - 整包复跑 `bash tools/actionaxis_plugin/compile_tests.sh`：源码编译通过，`PASS G07Enchantment`；该 22:30 快照整包退出码 1，7 个失败全部在 `G05DynamicAuraTests`（Spiky 5 vs 0／1001 vs 962；动态人物光环 11950 vs 11900，4 处），不是 G07。日志 `tools/actionaxis_plugin/build/g07_green_full.txt`。**该 7 项是 22:30 的中间态；G05 已在 22:45 修复，22:47 与 00:03 整包复跑均为 `PASS G05DynamicAura`／`OK`，见 G05 日志与 [g05-01-acceptance.md](g05-01-acceptance.md)。**
  - `npm.cmd --prefix d:\巴扎plus test -- --run`：退出码 0，37 文件／102 测试全绿（本轮未改前端）；日志 `build/g07_npm.txt`。
  - `bash tools/build_actionaxis_plugin.sh --compile-only`：退出码 0，仅既有 Unity CS0618 过时 API 警告；`ActionAxisEngine.dll` SHA256 `26F36794FA7BFD16FDA1F831118DF5B6677CAEE56E72C9242911A752029F3B29`，`ActionAxisPlugin.dll` SHA256 `83952DD277D2E7D0D1066DCFAB68BD2E9CA6060BC1D660AE2E85F5CC78B8F6E0`；日志 `build/g07_build.txt`。该哈希对应本次构建快照；共享工作区随后仍有其它组并发改动，正式集成前必须冻结并重算。未部署。
  - 矩阵：`python tools/rule_inventory.py --mapping-input ...` → 1644 模板；`EngineTests.exe --coverage ...` → 退出码 0，387678 行；`python tools/rule_inventory.py --mapping-report ...` → 退出码 0；`python tools/rule_inventory.py --work-index docs/native-rule-task-index.csv` → 1810 模板，G07 主责 3；`python -m unittest tools.test_rule_workplan -v` → 5/5 OK。重点 9 模板共 2772 行全部 `mapped`，含 Radiant/Deadly/Golden 与双方/全品质/三场景；全库仍有 438 附魔告警行、35584 条 item unsupported 行。矩阵通过不等于行为或原生对账通过。
  - `git diff --check`：退出码 0（仅 CRLF 提示）。
  - 未跑：G07 真实 BPP 回放、比较器版本、游戏内 UI／加载验收。
- 原生对账：G07-01 当时未执行；G07-03 已用 200 份本机 BPP 回放补充关键路径证据，见下条日志。`DamageCrit` 公式已被回放更正为 `2 + DC/100`，仍不是完整逐事件对账。
- 构建部署：构建版本／DLL SHA256 如上；未写入游戏目录、未重载，`DEPLOY=未执行`。
- 剩余风险与阻塞：
  - `DamageCrit` 公式与暴击事件值未经真实回放校准；不同卡的 `DamageCrit` 叠加顺序仍可能不同。
  - `TCardConditionalEnchantmentEligible` 当前把“未附魔或已是同种附魔”都视为可选目标；这是既有实现的保守语义，尚无回放证据证明原生允许同种附魔再次入选。
  - `EnchantmentMapper` 旧文本路径仍不重建非自身/动态附魔光环；有原生模板的卡走 `NativeCardAbilityMapper`，无模板旧卡仍只能近似，不能标完整。
  - 战斗开始时已附魔卡的战斗内覆盖/移除继续 fail-closed（无法还原未附魔基底），需要回放证据决定是否允许“覆盖后恢复到开场附魔”。
  - Dishwasher 的 Radiant 条件依赖 Heated；本轮只验证其模板全组合 `mapped`，未执行 Heated 条件行为（G01/G05 的槽位语义）。
  - 全库 438 附魔告警行仍包含 `Chilled`、`BurnRemoveAmount`、自引用光环、动态倍率、`PercentDamageReduction`、能力/光环定义缺失等，分别归 G01/G05/G00/G10；未在本轮消除。
  - 22:30 时整包 Gate A 曾被并发 `G05DynamicAuraTests` 的 7 个断言阻塞；G05 已于 22:45 修复，22:47 与 00:03 整包复跑 `OK`，该项阻塞已解除。不是 G07 失败。
  - 无 G07 真实回放与游戏内 D 门禁，状态保持 IMPLEMENTED，不升 VERIFIED。
- 下一条可执行动作：
  1. G12／G07 取包含 Letter Opener + Deadly（`DamageCrit`）、Radiant 抗性/免疫、Spirit Diffuser 开局附魔的真实回放，按帧核对暴击值、控制时长、摧毁阻止与附魔事件。
  2. G05 处理剩余 438 条告警中的动态光环属性（`Chilled`、`BurnRemoveAmount`、自引用、动态倍率）并与 G07 复核附魔光环。
  3. G05 的 7 个断言已于 22:45 修复；保留此历史待办仅用于说明 22:30 快照，当前不再阻塞 Gate A。G07 可用同一 DLL 哈希复核。
  锁释放：是（未提交、未部署）。

### 2026-10-24 22:30 · G03-TRIGGER · 指定来源／受体、次数前提与帧序

- Owner / Reviewer / 分支 / 基线：Qoder（G03 本轮）／待集成人复核／main 共享工作区（未提交）／`6eb032a` + 并发未提交改动。
- 状态：IMPLEMENTED（45 个 G03 主责模板全量入口＋机制族回归通过；4 张缺能力定义卡保持 DATA，Resonance Crystal 缺场外光环 0 记 OUT+DATA；未部署）。
- 范围：`primary_group=G03` 全部 45 个模板（29 物品＋16 技能），不再只测种子。种子与缺口：Hunting Hawk `0941d7dd…`、Boosted Saucer `0f96e2e4…`、Wolf Mask `22c8b252…`、Bloodhound `13989ad3…`、Frozen Secrets `1252064a…`、Counterstrike `c5bff6ad…`、Retool `fb409226…`、Propeller Hat `0e3d2e2c…`；Fortune Cookie `1a0c645a…`、Sulphur `6693a3f1…`、Shard of Obsidian `8981892b…`、Wild Quillback `dd789cd1…` 缺能力定义，保留 DATA。Machine Learning `d1e825ba…` 与 Renovation `ec327a59…` 的 `TTriggerOnCardSelected`/Level 触发是场外选牌/升级，改按 OUT 跳过，不由 `TActionCardUpgrade` 误判为战斗缺口。完整模板均取自 `cards` 表，不改品质／附魔／Priority／Prerequisites 绕过入口。
- 原生证据：`GameData.db.zip` SHA256 `FDFD3087E6A23EDA2C9F53BE68B012006CC949D03E8666DD51AD65ABC71B61E5`；工作区解压库 SHA256 `2B9C6B3765C72A21A5EBFB7336C13251D09017B87C0E599671B14299800BA092`；种子夹具 `tests/fixtures/native_g03_cards.json`（13 模板／160135 bytes）SHA256 `C94442288CDECFFA599443FDBBF652B935C1F94CC3672FB6757EBE2C18468268`；全量夹具 `tests/fixtures/native_g03_all.json`（45 模板／522204 bytes）SHA256 `A0CCC63068CE9743755ACB05ED38B18C0115952A37D5C8FD3A079A00F7746CD9`。逐卡路径、工具提示与程序集证据见 `captures/diagnostics/g03_native_evidence.md`：`BazaarGameShared.dll` SHA256 `C51716EA…8947`、`BazaarBattleService.dll` SHA256 `E95E614B…E987`；`EEffectPriority` 枚举为 `Lowest,Low,Medium,High,Highest,Immediate`，`ProcessEffectsInOrder` IL 从高序号向低序号遍历，确认 Immediate 先结算。回放核对：`CombatReplays/CombatReplays/04775112acd94dfaa7e9f03c4fc2995c.payload.mpack.gz`（SHA256 `63fb461a94a23f1c5fb503297c8b81aa440cb603d099c1da3645db24c24b98b0`）中敌方 Hunting Hawk 在 frame 80／4.0s 执行 CardHaste，frame 81／4.05s 才对同一受体 FlyingStart，和模板回归的 4.0→4.05 一致。没有逐卡完整回放，未把模板测试冒称全部逐事件对账。
- 完成内容（五步）：
  1. 类型识别：全量枚举 45 模板的 `TTriggerOnItemUsed/CardFired/Or/FightStarted/CardPerformed*`、`TPrerequisiteCardCount/Player/Run`、`TAction*`；`TTriggerOnCardSelected` 归入场外白名单，不再让 Machine Learning／Renovation 因场外升级动作被误判为战斗 unsupported；新增 `TriggerSourceNeighbors` 目标识别；未知类型仍抛异常。
  2. 目标／主体：Hunting Hawk 的 `Subject=SelfBoard`、`Target=SelfHand` 与动作 `TriggerTarget` 分别校验来源阵营、受体阵营和受体运行时条件；Boosted Saucer 的 `Trigger.Source` 映射到 Actor 并做同侧过滤；Retool 以“被使用的 Tool”为邻接原点，而不是施法技能自身；其余 21 个 OnItemUsed 卡按 SelfHand/OpponentHand/AllHands、Tag/Size/AmmoMax 条件统一匹配。
  3. 结算：`Effects` 的两种目标路径都支持 `randomTriggerSourceAdjacent`，先取触发源相邻集合，再由 `AmmoMax>0` 等条件过滤、取样；不丢弃未满足条件的目标。
  4. 排空：共用工作区的 `Triggers.ResolvePayload` 已先完成同卡全部触发的前置快照、再按 Immediate→Lowest 执行；本轮以 Wolf Mask／Bloodhound／Frozen Secrets／Counterstrike 的“每战第一次”用例锁定该语义。`onBeforeUse` 在主动效果前同步结算；普通 `onHaste` 被动留到下一帧，并由回放 frame 80/81 佐证。
  5. 测试：新增 `G03TriggerRuleTests`（种子/帧序）与 `G03FullCoverageTests`（45 模板入口、非战斗分类、OnItemUsed 过滤、Performed 事件、TriggerOr 去重），并在 `tests/Program.cs` 注册；夹具为完整原生模板，不手改条件。
- 改动文件：
  - `tools/actionaxis_plugin/engine/SkillMapper.cs`：`MapCardTarget` 增加 `TriggerSourceNeighbors` → `triggerSourceAdjacent/randomTriggerSourceAdjacent`；`IsNonCombatTrigger` 增加 `TTriggerOnCardSelected`（场外选牌）。
  - `tools/actionaxis_plugin/engine/Effects.cs`：`ItemsForTarget/NativeItemsForTarget` 支持 `randomTriggerSourceAdjacent` 的邻接取样。
  - `tools/actionaxis_plugin/tests/G03TriggerRuleTests.cs`（新增）、`tests/G03FullCoverageTests.cs`（新增）、`tests/fixtures/native_g03_cards.json` + `tests/fixtures/native_g03_all.json` + `tests/fixtures/native_g03_support.json`（新增，完整模板）、`tests/Program.cs`（注册 `G03TriggerRules`／`G03FullCoverage`）。
  - `tools/actionaxis_plugin/tests/G11SandstormTests.cs`：把 CardSelected 的旧断言从“非白名单”改为“非战斗”，与 G03 全量分类一致。
  - `captures/diagnostics/g03_native_evidence.md`（新增，原生证据）、本文件（本条日志）。
  - 未纳入：共享工作区其它组的 `Triggers.cs` 两阶段判定、优先级排序、G01 区域解析、G06/G09/G02/G05 在途改动；本日志不替其签收。
- 新增测试（`G03TriggerRuleTests.Run`）：
  - `HuntingHawkSourceAndRecipientStayExact`：同侧来源/受体只飞被 haste 的卡；敌源、敌受体、运行时 `CooldownMax=0` 均不飞，旁观卡不受影响。
  - `HuntingHawkActiveHasteFlightsOnFollowingFrame`：主动 haste 在 4.0s，`onHaste` 被动在 4.05s 才让同一张受体起飞；4.0s 无提前 fly。
  - `HuntingHawkAuraFiltersFlyingAndCanCrit`：仅飞行、可暴击、有冷却的卡获得 +Custom_0 暴击；非飞行或不可暴击为 0。
  - `BoostedSaucerActorFiltersRepairAndDestroy`：本方 Actor 的 Repair 使 DamageAmount 150→300；敌方 Actor 不改；本方 Destroy 充能 4s，敌方不充能。
  - `WolfMaskFirstUseEachFight`：自身使用不计；敌方首次使用 +30 Rage；第二次不再加；新战斗重置为一次。
  - `BloodhoundFirstBelowHalfEachFight`：高于半血不触发；首次低于半血给两张有冷却友方各 +1s；第二次不再给；新战斗重置。
  - `FrozenSecretsFirstNRelicUses`：Gold 前 3 次 Relic 使用各冻结一次，第 4 次不冻结但计数到 4；非 Relic 不计数；新战斗从 0 开始。
  - `CounterstrikeFirstEnemyWeaponUse`：敌方非 Weapon 不触发；首个 Weapon 给两张友方各 +1s；第二个 Weapon 不再给；新战斗重置。
  - `RetoolReloadsTriggerSourceNeighborWithAmmo`：只装填被使用 Tool 的相邻、`AmmoMax>0` 卡；非相邻有弹药卡保持 1，无弹药上限邻居不被选中。
  - `PropellerHatBeforeUseResolvesOnSameFrame`：飞行道具使用前被动在同一 4.0s 先 haste，随后主动伤害 10；4.05s 无重复 haste。
  - `AllG03TemplatesMapOrAreDataBlocked`：45 个主责模板 × 声明品质 × 双方逐项映射；4 张缺能力定义卡必须留 warning，场外卡（含 Resonance Crystal 的光环缺失）允许 warning，其余战斗卡 0 warning，技能不得 unsupported。
  - `NonCombatCardSelectionIsOutOfCombat`：Machine Learning／Renovation 的 CardSelected/Level/Upgraded 触发全部归 OUT，不因 `TActionCardUpgrade` 拒绝整卡。
  - `PistolSwordRequiresAmmoSource`：SelfHand `AmmoMax>0` 才造成 15 伤害；普通物品、敌方物品不触发。
  - `CaltropsRequiresOpponentSource`：仅敌方使用物品造成 1 伤害，本方使用不触发。
  - `AttachmentSetToolCombination`：Tool 非 Weapon 造成 50 伤害；Tool+Weapon 改为 50 护盾；普通物品两者都不触发。
  - `SkillTagAndSizeFilters`：Relax Bro（Small）、Pyromania（Large）、Underwater Wonder（Aquatic）、Toxic／Healthy Friendship（Friend）只对匹配 Tag/Size 的物品触发，数值分别 3／20／2／2／2。
  - `PerformedActionTriggers`：Sauna/Heat Lover（Burn）、Poison Tyrant/Ouroboros（Poison）、Aged Cask/Tropical Island/Proboscis（Slow）、Lightning Rod（Freeze/Slow）按模板数值触发。
  - `TriggerOrDoesNotDoubleCount`：Lightning Rod 的 Freeze 和 Slow 各只结算一次伤害，不因 OR 重复计数。
  - `FightStartedEnchantsMatchingTagsOnly`：Happy Accident 只给 Drone／Vehicle、Experimental Chef 只给 Food／Tool 各附魔一次，普通物品不被选中。
  - `RemainingCombatCards`：Tinfoil Hat 仅敌方使用给护盾、Spiked Collar 仅自己 Friend 造成伤害、Unwavering 仅自己使用给护盾；Myrrh/Hemlock 主动 Regen/Poison 数值；Pizza 仅 Heated 时 Burn，未 Heated 只 Regen。
- 验证：
  - 22:01 G03 改动前基线 `bash tools/actionaxis_plugin/compile_tests.sh` → 退出码 0，全部套件 `OK`。
  - 22:18 仅编译 engine + `TestHarness` + `G03TriggerRuleTests`：先 RED（Wolf Mask 计数、Hawk 受体条件、Retool 未支持），修复后 `PASS G03TriggerRules`／`OK`。
  - 23:05 加入 `G03FullCoverageTests` 与 45 模板夹具后，G03 独立编译先 RED（治疗满血记 0、Lightning Rod 误按技能映射、CardSelected 未归场外），修复后 `PASS G03TriggerRules`／`PASS G03FullCoverage`／`OK`。CardSelected 断言用“先还原旧白名单→24 个断言失败→恢复”验证过 RED→GREEN。
  - 01:18 官方 `bash tools/actionaxis_plugin/compile_tests.sh`：编译通过，`PASS G03TriggerRules`、`PASS G03FullCoverage`；整包退出码 1，10 个失败集中在其它组的 `SkillsTests`／`NativeRuleExpansionTests`／`NativeSkillDynamicAuraTests`／`NativeRuntimeRulesTests`（静态光环/技能状态回归）。此前 23:15 的整包运行失败来自 G06FullCoverage／G11Sandstorm；G11 的 CardSelected 断言已按新契约修正。G03 未参与这些失败。
  - 22:23 `npm.cmd --prefix d:\巴扎plus test -- --run` → 退出码 0，37 文件／102 测试全绿，日志 `build_g03_npm.log`。
  - 01:30 复跑 `bash tools/build_actionaxis_plugin.sh --compile-only` → 退出码 0，仅既有 Unity CS0618 警告；`ActionAxisEngine.dll` SHA256 `6826E7CBB2B9A703F2BB85E7BD8B571275F2EAD12F81B7E571B558476EB15E8F`，`ActionAxisPlugin.dll` SHA256 `B12B45353C084322A5E367ADA290E4583D636CC995ED15DA44FD2D29D06C87DC`。
  - `git diff --check` → 退出码 0（仅 CRLF 提示）。官方整包当前因上述其它组 7 个断言不能签 OK；G03 定向套件在最终共享源码上再次 `PASS`。
  - 映射：全量夹具 45 模板逐品质/双方入口通过；G03 主责 45 模板（29 物品＋16 技能）8970 行，6690 mapped／1800 mapped-warning／234 simulated／186 inactive／60 out-of-combat／0 unsupported；5 个模板带可见数据警告：Fortune Cookie／Sulphur／Shard of Obsidian／Wild Quillback（缺 AbilityId，DATA）与 Resonance Crystal（缺 AuraId 0，但全部触发为 CardSold 场外，OUT+DATA）。Machine Learning／Renovation 已归 out-of-combat，技能 unsupported 为 0。报告 `tools/actionaxis_plugin/build/native_mapping_g03_group.tsv` SHA256 `61C94E00E972820DB2454AD674B90F19AC609FB6189DDF0BE64B23433B26A088`，种子报告 `native_mapping_g03_seed.tsv` SHA256 `4110788957686BD8E9800EFCDC9713438D7F688E1C706EE29ABA5EFE3A347EEB`。
- 原生对账：已做一条关键路径回放核对，未做全 45 模板逐事件对账。`04775112acd94dfaa7e9f03c4fc2995c.payload.mpack.gz`（SHA256 `63fb461a94a23f1c5fb503297c8b81aa440cb603d099c1da3645db24c24b98b0`）frame 80／4.0s 敌方 Hunting Hawk `CardHaste`，frame 81／4.05s 才对同一目标 `FlyingStart`，与模拟断言一致。已扫描 200 份回放，58 份含 G03 主责模板；其余卡尚未逐事件比对，差异数／UNKNOWN 仍为 UNKNOWN。
- 构建部署：仅 `--compile-only`；DLL 含共享工作区其它组改动，不是 G03 独立发布物；未写入游戏目录、未重载、未重启，`DEPLOY=未执行`。
- 剩余风险与阻塞：
  - `SelfHand/SelfBoard` 在当前模拟器只按阵营区分，尚未模拟真实手牌／棋盘分区；Hunting Hawk 的“来源在棋盘、受体在手牌”只验证到阵营与具体实例，区域语义仍需 G01 契约。
  - 两阶段前置快照与 Immediate→Lowest 顺序已由程序集 IL 与种子用例支持，但未用真实回放校准同帧多卡、嵌套触发与随机取样。
  - Frozen Secrets 的 `Auras/9` 未被品质引用，本轮未把它当运行时加成；其它 235 条 `Tooltip Handling` 光环的运行时语义未逐卡验证。
  - Machine Learning／Renovation 已按 CardSelected/Level/Upgraded 归 OUT，不再由 `TActionCardUpgrade` 阻塞；其场外升级效果仍不在本轮行动轴范围。剩余 4 个缺 AbilityId 模板 Fortune Cookie／Sulphur／Shard of Obsidian／Wild Quillback 归 G00/G11；Resonance Crystal 缺 AuraId 0 但全部触发为 CardSold，记 OUT+DATA，不能补造定义。
  - 两张 `[DEBUG]` 卡（Disenchant、Temporary Enchantment）只验证入口与 CardFired 触发识别；其 Enchant/EnchantRemove 行为归 G07。整包 A 门禁仍被其它组 10 个静态光环/技能状态断言阻塞；不能把 G03 全量模板绿色写成整包 VERIFIED。
  - 本轮改动 `SkillMapper.cs`／`Program.cs` 命中 G00 `SOURCE_FILES`，`data/g00_baseline.json` 需由 G00 owner 重跑 `tools/g00_audit.py` 后更新；未擅自覆盖其并发基线。22:38 `python tools/g00_audit.py --check` 返回 FAIL：矩阵模板集合与数据库不一致、矩阵行数／重复校验失败、当前结果与冻结基线不一致；属 G00 在途矩阵，G03 不代签。
- 下一条可执行动作：待并发 G06/G10 恢复整包编译后复跑 `bash tools/actionaxis_plugin/compile_tests.sh`，预期 `PASS G03TriggerRules`／`PASS G03FullCoverage` 且无新增 G03 失败；G12 用已定位的 58 份含 G03 模板回放继续做逐事件对账，先用 `04775112…` 的 frame 80/81 复核 Hunting Hawk，再扩到 Wolf Mask/Counterstrike。锁释放：是（未持有独占锁；未提交、未部署）。

### 2026-10-24 22:30 · G04-01 · 运行时数值引用、跨卡聚合、成长、标签与计数器

- Owner / Reviewer / 分支 / 基线：Qoder（G04 本次会话）／待复核／`main` 共享工作区（未提交）／`6eb032a` + 共享未提交改动。
- 状态：IMPLEMENTED（G04 机制与回归）；非 VERIFIED（未做真实回放／游戏内对账，未部署）。后续 G04-02 已全量收口：150/150 主责模板映射与运行时冒烟通过，G04 unsupported=0；本条“9 张硬拒绝 / Custom_N fail-closed”已被取代。
- 范围：`docs/native-rule-task-index.csv` 中 `primary_group=G04` 的 150 个模板；四个种子 Streaming Setup `2a0624d0…`、Spacescraper `40d8b44a…`、Geyser `77df36bf…`、Pauldrons `8666a1de…`；新增关联／主责卡 Seafaring `d9bcaba0…`、Ancient Technology (Skill) `ba317deb…`、Cauldron `ad02d867…`、Skyscraper `fc01f3c3…`、Vat of Acid `437100de…`、Atlas Stone `c39eed44…`、Burst of Flame `ee178ba8…`（关联卡在 CSV 中均含 G04 `related_groups`）。覆盖 `TReferenceValueCardAttribute`、`TReferenceValueCardAttributeUnscaled`、`TReferenceValueCardAttributeAggregate`、`TReferenceValueCardTagCount`、`TReferenceValuePlayerAttribute`、`TReferenceValueAttributeChange`、`TRangeValue`、`TActionCardModifyAttribute`、`TAuraActionCardAddTagsList/BySource`、`Custom_N` 与 Modifier。
- 原生证据：`D:/Program Files (x86)/Steam/steamapps/common/The Bazaar/TheBazaar_Data/StreamingAssets/GameData.db.zip` SHA256 `FDFD3087E6A23EDA2C9F53BE68B012006CC949D03E8666DD51AD65ABC71B61E5`；四种子夹具 `tools/actionaxis_plugin/tests/fixtures/native_g04_cards.json` SHA256 `0079FA6D8DC986756755A21A4DF6CD02D840A912BD6624D52249928C0B23668F`；关联卡夹具 `native_g04_related.json` SHA256 `BD40EDC05E12C613098BA1599D03408EC53BE5FE81B98650C0FB0D0CD5F3B269`。证据均为原生模板与模拟帧，不是游戏回放。
- 完成内容（五步）：
  1. 类型识别：确认四种子只走运行时求值入口；`TActionCardModifyAttribute` 的数值在映射期保存 `NumExpr`，结算期由 `Effects.EvaluateNum` 重算，未固化为开场常量。
  2. 目标／聚合：Streaming Setup 的 `Target:null` 逐目标读当前卡售价；Spacescraper/Skyscraper 的 `CardAttributeAggregate` 每次 Refresh 重新对当前手牌求和；Cauldron/Vat of Acid 的 `CardTagCount` 读取当前去重标签。
  3. 结算／回滚：Geyser/Pauldrons 的 `TReferenceValueAttributeChange` 由 `BattleContext` 触发量栈恢复；Pauldrons 暴怒倍率在暴怒结束时撤销；限时属性到期后 Streaming Setup 的引用与光环值同时回退。
  4. 标签：修复 `NativeCardAbilityMapper.ApplyStaticAuras` 把 `TAuraActionCardAddTagsList/BySource` 当成“未知光环操作”整条拒绝的顺序错误；`SkillMapper` 对玩家侧静态标签光环按 `live-applied` 处理并实际加标签，不再把 Seafaring／Ancient Technology／Friend of the Sea 标成 unsupported。
  5. 失败关闭：`TReferenceValuePlayerAttribute` 只接受已实现的人物属性；`Custom_3/4/9`、`Income`、`Gold`、`Level` 等无战斗内来源的值不再静默按 0 结算，而是整条拒绝（Orange Julian／Pyg's Gym 因此转为 unsupported，待 G00/G06 提供人物自定义属性来源）。
- 改动文件：
  - `tools/actionaxis_plugin/engine/NativeCardAbilityMapper.cs`（标签光环操作校验顺序；同文件另有其它组并发改动）
  - `tools/actionaxis_plugin/engine/SkillMapper.cs`（玩家侧静态标签光环；未支持人物属性 fail-closed；同文件另有其它组并发改动）
  - `tools/actionaxis_plugin/tests/NativeRuntimeRulesTests.cs`（新增 9 组 G04 回归；既有四种子用例保留）
  - `tools/actionaxis_plugin/tests/fixtures/native_g04_related.json`（新增，真实模板导出）
  - `docs/native-rule-coverage.md`（本条日志与 §7.2 G04 状态）
  - 未纳入：其它组并发修改的 `Types.cs`、`Effects.cs`、`Triggers.cs`、`Transforms.cs`、`G05DynamicAuraTests.cs` 等；未提交、未部署。
- 新增测试（`NativeRuntimeRulesTests.Run`）：
  - `SeafaringAuraTagsOnlySmallItems`：Diamond Seafaring 对 Small 手牌加 Aquatic，Medium 不加；状态 `live-applied`。
  - `AncientTechnologyAuraTagsRelicsOnly`：Diamond Ancient Technology 只给 Relic 卡加 Tech。
  - `CauldronAuraTagsReachTagCount`：BySource 标签复制成功；Gold Cauldron 的 SelfHand 去重标签 4 个 × Custom_0 4 = Burn/Poison 16；追加 Fire 标签后 20，证明 TagCount 随实时标签重算。
  - `SkyscraperAggregatesLiveSellPrice`：Gold Skyscraper 初始 2×(10+5)=30；把一张手牌售价 +7 后 Refresh 为 44，实际伤害事件也是 44。
  - `VatOfAcidLiveTagsScaleBurn`：Vat 无可见标签时 TagCount=0；加 Food/Potion 后 2×6=12；重复标签不重复计数；ForceUse 的 Burn/Poison 均为 12。
  - `AtlasStoneUnscaledGrowthRepeatsBase`：Bronze Atlas Stone 连续两次出手伤害 20→40，属性 20→40→60；Unscaled 不反馈运行时偏移。
  - `BurstOfFlameFirstHalfHealthLossOnly`：Gold Burst of Flame 在敌方第一次跌破半血时 Burn 20，并令 Custom_0=1；第二次跌破半血不再 Burn，Custom_0=2。
  - `UnsupportedPlayerAttributeValueFailsClosed`：`Custom_3` 人物数值引用抛 `FormatException`。
  - `RangeValueStaysWithinBounds`：`TRangeValue` 100 次求值均在 [3,5] 整数区间且出现多个值。
  - 既有四种子继续通过：`StreamingSetupReadsPerTargetSellPrice`、`StreamingSetupLiveTagAndTimedRollback`、`SpacescraperAggregatesLiveSellPrice`、`GeyserGrowthRestoresAfterNestedTrigger`、`PauldronsEnrageMultiplierRollsBack`。
- 验证：
  - G04 定向：`tmp_g04_verify/run_g04.sh`（仅 engine + `NativeRuntimeRulesTests` + `TestHarness`）→ `PASS NativeRuntimeRules`、`OK`。
  - 整包：`bash tools/actionaxis_plugin/compile_tests.sh` → `PASS NativeRuntimeRules`，整包退出码 1；最后一次复跑为 7 个断言失败，集中在 CardMapper／NativeCardAbilityMapperTests（1）与 G05DynamicAura（6），均为其它组在途改动；G04 无用例失败。共享工作区并发修改期间曾出现编译中断与失败数波动，数字以最终复跑为准。
  - 前端：`npm.cmd test -- --run` → 37 文件／102 测试全绿。
  - 构建：`bash tools/build_actionaxis_plugin.sh --compile-only` → 退出码 0，仅既有 Unity CS0618 警告；`ActionAxisEngine.dll` SHA256 `9D8F9D92472A618696E40B4CE3CAB59CDD0E6511D905BA2270FD197EF884F111`，`ActionAxisPlugin.dll` SHA256 `EC0390057910A279232BF5EFEA9FDAD055EEA717128BE1017A3DE8D3F4688669`。未部署。
  - 矩阵：`EngineTests.exe --coverage`（等价定向 CoverageHarness）→ 387678 行；G04 主责 150 模板共 35580 行组合：mapped 33126、simulated 1062、live-applied 18、inactive 378、out-of-combat 48、unsupported 948。四种子均 mapped：Streaming Setup 312、Spacescraper 234、Geyser 336、Pauldrons 336。
  - 关联卡矩阵：Cauldron 336 mapped（修复前 336 unsupported）；Seafaring／Ancient Technology／Friend of the Sea 各 30 行＝6 live-applied＋6 simulated＋18 inactive，0 unsupported（修复前各 12 unsupported）；Skyscraper 234 mapped、Vat of Acid 234 mapped、Atlas Stone 420 mapped、Burst of Flame 30 simulated。
  - `git diff --check -- <本轮文件>` → 退出码 0（仅 CRLF 提示）。
- 原生对账：未执行。无 G04 真实回放、无比较器版本，差异数 UNKNOWN；本轮只有模板级模拟与矩阵验证，不能声称逐事件一致。
- 剩余风险与阻塞：
  - G04 主责仍有 9 张硬拒绝：Building Crescendo（技能缺少光环定义）、Dreampearl（TTriggerOnEncounterExited，场外）、Masterpiece／Toxic Flame（能力定义缺失）、Showcase（动态光环目标区域）、SkillCard - Speed（TTriggerOnCardSelected）、Stout Fire（人物 Burn 变化触发）、Orange Julian／Pyg's Gym（人物 Custom_3/4 无战斗内来源）。分别归 G00/G03/G05/G06/G11。
  - 静态标签光环在映射/开场加上后没有来源级撤销；来源离场或条件失效时标签不会自动移除。Cauldron/Seafaring 只证明开场与运行时 TagCount 重算，未证明可逆生命周期（G05/G08 接口）。
  - `TReferenceValueCardAttributeAggregate` 的缺值回退按“每个目标各自 DefaultValue”实现；现有卡 DefaultValue 均为 0，缺值/负值/比例的完整原生语义仍需回放校准。
  - `TRangeValue` 使用引擎 RNG，不能与原生随机序列逐次一致；“随机均值”未用于替代随机结果。
  - 未支持人物属性现改为 fail-closed，Orange Julian／Pyg's Gym 因此从 mapped 降为 unsupported；这是为避免“只存字典/静默 0”。若 G00/G06 后续提供人物自定义属性快照，应恢复这两张卡并加真实模板测试。
  - 共享工作区仍含其它组未提交改动；整包 8 个失败属其它组，不能把本条日志视为整份工作区已通过。
- 下一条可执行动作：G00/G06 提供人物 `Custom_N` 战斗内来源后恢复 Orange Julian/Pyg's Gym；G05 为标签光环增加来源级撤销；G12 取含 Streaming Setup／Spacescraper／Geyser／Pauldrons 的真实回放，按帧核对改动量、聚合与标签；由集成人复核本轮差异并决定是否授权提交／部署。锁释放：是（未提交、未部署）。

### 2026-10-24 22:26 · G09-01 · 摧毁前变形、禁用/修理、强制使用与按规则复活

- Owner / Reviewer / 分支 / 基线提交或补丁哈希：Qoder（G09 本次会话）／未指定／`main`／`6eb032a` + 共享工作区未提交改动；本轮不提交、不部署。
- 状态：IMPLEMENTED（A 门禁整包 C# 回归与 B 门禁完整插件编译通过；C 原生逐事件对账、D 游戏内可见验收未执行，未部署）。不能写成 VERIFIED。
- 范围：`docs/native-rule-task-index.csv` 中 `primary_group=G09` 的 94 个模板；本轮行为种子为 Abducted Cow `03f90f0b-088f-453d-bbf7-ebb5e777afe2`、Drone Crusher `3c7807ef-0993-4b33-8a34-441f7cde0666`、Fiery Rebirth `09c59ebf-d2f9-4862-9c30-396b9ddfdd89`，并用 Repair Drone、Rocket Drone、Jerky 等完整模板验证供体池/修理路径。品质覆盖 Silver/Gold/Legendary/Diamond；未声称 94 个模板全部行为通过。
- 原生证据：`GameData.db.zip` SHA256 `FDFD3087E6A23EDA2C9F53BE68B012006CC949D03E8666DD51AD65ABC71B61E5`；完整模板夹具 `tools/actionaxis_plugin/tests/fixtures/native_g09_cards.json` SHA256 `7C56D387333B053F3C0F43E84093C44FF68A53BB1843DB660836D3B10C8C2182`。关键语义来自模板字段与原生 tooltip：
  - Abducted Cow `Abilities.3`：`TTriggerOnBeforeCardDestroyed`（Subject Self）→ `TActionCardTransformDestroyed` Target Self，SpawnContext Limit 2；tooltip “When this is Destroyed, Charge your items … and transform this into 2 Jerky”。
  - Drone Crusher `Abilities.0/1/2`：`TTriggerOnBeforeCardDestroyed`（Subject SelfHand，按 Small/Medium/Large）→ `TActionCardTransformDestroyed` Target `TTargetCardTriggerTarget`，Limit 1/2/3；Source=SelfBoard。tooltip “When you destroy one of your items, transform it into a small Drone from any Hero”。这证明真实主体是**被摧毁的 TriggerTarget**，不是 Drone Crusher 自己，也不是“变形卡被毁”。
  - Fiery Rebirth `Abilities.2/3`：`TTriggerOnPlayerDied` → `TActionPlayerReviveHeal`（前提 Custom_0==0，Priority Immediate）与 `TActionCardModifyAttribute Custom_0 +1`（Priority Low）；`Auras.7` 把 `HealthMax` 写入 `HealAmount`。复活次数是**每卡计数器**，不是全局每方一次。
  - 数据缺口：`TActionCardTransformDestroyed` 的 `SpawnContext` 仍只有 `$type`+`Limit`，没有候选集/权重；供体按卡牌身份与 tooltip 校准，缺失时告警并继续正常摧毁。
- 完成内容（五步）：
  1. 类型识别：`SkillMapper.MapAction` 新增 `TActionCardTransformDestroyed`（拒绝内嵌 Abilities、非 UntilEndOfCombat 时长、缺失/越界 Limit）；`NativeCardAbilityMapper.ValidateTransforms` 接受已校准的摧毁变形，未知卡仍 fail-closed。
  2. 目标／主体：沿用 `TTriggerOnBeforeCardDestroyed` 的 Subject/Actor 映射；Drone Crusher 的 Source=SelfBoard 通过 ActorSide 过滤，敌方摧毁不会触发“When you destroy”；Target 解析为 TriggerTarget。
  3. 结算：新增 `transformDestroyed` effect 与 `NativeTransform.DestroyDonorRule/FindDestroyDonor/ApplyDestroyed`。供体池显式放在 `BattleConfig.SpawnPool`；Abducted Cow→同品质 Jerky，Drone Crusher→同品质 Small+Drone。摧毁前只登记供体，待 `onBeforeDestroy` 全部结算后才原位变形，因此充能/售价仍读原卡；变形成功后 `DestroyItem` 取消 destroy 事件但保留“行动者执行过摧毁”的观察事件，Generation 递增使旧排队失效。
  4. 排空：`TriggerDef.Priority` 从原生 `Priority` 映射；同一张卡的触发按 Immediate→Highest→High→Medium→Low→Lowest 稳定排序（`Triggers.Ordered`），用于 Drone Crusher Immediate 售价累计先于变形、Fiery Rebirth 复活先于计数器。
  5. 测试／文档：新增 `tests/G09LifecycleTests.cs`（11 组）并注册 `Program.cs` 的 `G09Lifecycle`；更新 §4.1/§4.2/§7.2 状态与本日志。
- 改动文件（仅 G09 直接改动；共享工作区还含其它组改动，均未提交）：
  - `tools/actionaxis_plugin/engine/Types.cs`（`BattleConfig.SpawnPool`、`TriggerDef.Priority`）
  - `tools/actionaxis_plugin/engine/BattleContext.cs`（`InternalItem.Destroying`、待执行摧毁变形字段；移除全局 `PlayerReviveUsed/EnemyReviveUsed`）
  - `tools/actionaxis_plugin/engine/Battle.cs`（死亡不再每方一次；ForceUse 深度上限告警）
  - `tools/actionaxis_plugin/engine/Effects.cs`（`transformDestroyed`、摧毁重入保护、变形后取消 destroy、ForceUse 跳过禁用目标）
  - `tools/actionaxis_plugin/engine/Transforms.cs`（摧毁供体校准/选择/原位变形；沿用 G08 的按品质复制与冷却参数）
  - `tools/actionaxis_plugin/engine/Triggers.cs`（同卡触发优先级排序）
  - `tools/actionaxis_plugin/engine/SkillMapper.cs`（TransformDestroyed 映射；技能 Priority）
  - `tools/actionaxis_plugin/engine/NativeCardAbilityMapper.cs`（ValidateTransforms/被动 Priority；另修复并发 G08 `CopyAtTier` 的编译类型错误与 `FindItem` 变量遮蔽，属共享工作区解阻）
  - `tools/actionaxis_plugin/plugin/ActionAxisPlugin.cs`（把 `_catalog.Cards` 填入 SpawnPool；本地卡库近似会在变形时显式告警）
  - `tools/actionaxis_plugin/tests/G09LifecycleTests.cs`（新增）、`tools/actionaxis_plugin/tests/Program.cs`（注册）
  - 未纳入：其它组在 `Program.cs` 注册的测试、`NativeCardAbilityMapper.cs` 的 G08/G01 逻辑、`Types.cs` 的 G05 人物减伤字段；未提交、未部署。
- 新增测试（`G09LifecycleTests.Run`，夹具 `native_g09_cards.json`）：
  - `AbductedCowTransformsInsteadOfDying`：Silver 牛摧毁后原位变 Jerky + 排队第二只；其他物品被充能 3s；无 destroy 事件；Generation=1；两只 Jerky 冷却 3s。
  - `DroneCrusherTransformsTriggerTargetNotItself`：Drone Crusher 不动，TriggerTarget 变 Rocket Drone；Custom_1=7、BurnApplyAmount=35（Immediate 先于变形）；原位 slot 3。
  - `DroneCrusherIgnoresEnemyDestruction`：敌方行动者摧毁同一目标时正常离场，不触发“When you destroy”。
  - `DroneCrusherMissingDonorKeepsDestruction`：只有 Gold 供体、Diamond 目标时告警并继续正常摧毁（fail-closed）。
  - `FieryRebirthHealsToFullOnce`：HealthMax 光环使 HealAmount=100；第一次死亡满血复活，第二次死亡结束；单个 revive 事件 t≈1。
  - `TwoReviveSkillsDoNotShareGlobalOnce`：两张 Fiery Rebirth 各自在第一次死亡触发，第二次都拒绝；证明不是全局每方一次。
  - `EnemyFieryRebirthRevives`：敌方技能同样按 self 侧死亡触发。
  - `ForceUseKeepsCooldownAndAmmo`：强制使用一次，不重置冷却、不消耗弹药、不展开 Multicast。
  - `ForceUseSkipsDisabledAndStopsAtDepth`：禁用目标无 forceUse 事件/伤害；自嵌套在 MaxTriggerDepth 停止并告警。
  - `DisableRepairAndDestroyImmunity`：禁用不出手、修理后恢复、DestroyImmunity 拦截并只发 destroyBlocked。
  - `MutualDestructionResolvesOnce`：同帧互相摧毁两张各只离场一次，不递归到事件上限。
- 验证：
  - 定向运行（engine + TestHarness + G09Lifecycle，临时 `tmp_g09/G09Check.exe`）：编译退出码 0，`PASS G09Lifecycle`，`OK`，运行退出码 0。该构建只证明 G09 定向套件，不是官方整包。
  - 官方 `bash tools/actionaxis_plugin/compile_tests.sh`（22:36 共享工作区）：退出码 0，整包 `OK`；`PASS G09Lifecycle`，G02StatusTiming／G05DynamicAura／PositionalRules／G01TargetConsistency 等并发套件均恢复全绿。日志外置：`C:\Users\42552\AppData\Local\Temp\opencode\g09_compile12.log`（临时，不入库）。
  - `npm.cmd --prefix d:\巴扎plus test -- --run`：退出码 0，37 文件／102 测试全绿。
  - `bash tools/build_actionaxis_plugin.sh --compile-only`：退出码 0，仅既有 Unity CS0618 警告；最终共享源码构建产物 `ActionAxisEngine.dll` SHA256 `F21E0BA694B67F225C33B3C0F43D30F64B47C36401D5391DE762564971BB9D99`、`ActionAxisPlugin.dll` SHA256 `B9FB848BDCCAE50994D11713D5F345BE842767EC03FFE281C9BE93F9BA46855F`。未部署。
  - `git diff --check`：退出码 0（仅 CRLF 提示）。
  - 未执行：`EngineTests.exe --coverage` 全量矩阵、真实回放逐事件比较、游戏内加载。
- 原生对账：未执行。没有包含 Abducted Cow／Drone Crusher／Fiery Rebirth 的 BPP 回放样本；G12 比较器尚未提供；差异数／UNKNOWN 未量化。模板级行为与 tooltip/触发时点一致，但不能写成逐事件一致。
- 构建部署：仅 `--compile-only`；DLL 含共享工作区其它组改动，不是 G09 独立发布物；游戏目录未写入、未重载，`DEPLOY=未执行`。
- 剩余风险与阻塞：
  - TransformDestroyed 的供体候选集在 DB 中缺失；Abducted Cow→Jerky 由 tooltip 校准，Drone Crusher 的小型 Drone 池来自 Small+Drone 过滤。生产插件用本地 `cards.json` 池时，生成卡可能只有本地近似规则并已显式告警；需 G00/G12 提供同版本原生候选池。
  - “变形成功后仍向行动者发 onCardDestroyed 观察事件、但不发 destroy 事件”是本次按 tooltip/触发时点做出的语义裁决；没有回放证据，需 G12 校准。
  - 变形空间不足时按现有 G08 规则保留原卡并继续正常摧毁；原生是否会把多余副本放入其他空槽未验证。
  - `SelfHand/SelfBoard` 在模拟器仍只按阵营区分；Drone Crusher 的手牌主体与棋盘来源未做真实分区校准（G01 契约）。
  - Drone Crusher 的 `Custom_1 * Custom_2` 动态光环、Abducted Cow 的充能/控制附魔依赖 G05/G04/G07 的实时层；本轮只验证种子路径。
  - Fiery Rebirth 的“满血”依赖健康上限光环；HealthMax 在战斗中变化后复活是否仍按当前上限，需回放校准。
  - A/B 门禁已在 22:36 共享源码上通过；C/D 仍未执行，不能把模板／回归结果当作原生逐事件对账或游戏内验收通过。
- 下一条可执行动作：并发组恢复整包绿色后复跑 `bash tools/actionaxis_plugin/compile_tests.sh`，预期 `PASS G09Lifecycle` 且无新增 G09 失败；G12 取包含上述三张卡的真实回放，按 §8.4 记录 destroy/transform/revive 的帧、实例与数值。锁释放：是（未持有独占锁；未提交、未部署）。

### 2026-10-24 22:35 · G12-01 · 真实回放账本、比较器、UI 历史与只读发布核对

- Owner / Reviewer / 分支 / 基线提交或补丁哈希：G12 / 待复核 / 当前共享工作树 / HEAD `6eb032a` + 未提交并发改动；本轮未提交。
- 状态：IMPLEMENTED（不是 VERIFIED；逐事件对账为 DIFF，部署未执行）。
- 范围：`9bbabba4a216484d83d068a149811259`（581 帧／338 事件，基础账本）与 `084bd840e98c4b81a681cb8d30c7b72f`（426 帧／68 事件，3 次变形＋3 次回退）。两份样本都不保证包含全部回归种子；本日志只声明它们实际覆盖的事件。
- 原生证据：回放 SHA256 `cfb0032e…424b`（帧 0–580／29.05s）、`a2096bcc…6c08`（帧 0–425／21.30s；变形帧 160／280／320）；`BazaarGameShared.dll` `c51716ea…a8947`；契约 `tools/native_replay_contract_v1.json`；`GameData 5.0.0`。证据目录 `captures/diagnostics/g12/`。
- 完成内容：
  - 开场重建：只把 `born.phase=opening` 的实例写入开场；修复回退时覆盖 `born` 的问题；BPP 快照中的 `Stash` 物品保留在账本但排除出引擎夹具（`section=Stash`、`inCombat=false`）；帧 0 不再重复写 `cardUpdates`；开局缺失的玩家 `HealthMax/Shield/HealthRegen/RageMax/EnragedDurationMax` 继续标 UNKNOWN，不拿战后值替代。
  - 事件码解析：按契约 `payloadVersions=[1]`、`combatEvents/gameEvents` 的 DTO 字段名绑定；契约外事件码／字段数不一致退出码 2 并写 UNKNOWN；不再按 tuple 长度猜字段。
  - 身份／变形谱系：规范化 `side + slot + generation + opening root template`；保留出生／离场／父子关系；`build_native_identity` 跳过变形子实例的占位 generation 0，避免覆盖原实例。
  - 逐事件比较器：`tools/native_replay_compare.py` 独立比较 effects／health／cards／identity／outcome；同帧多重集匹配，默认不许时间漂移；分别输出 matched/missing/extra/value-mismatch/time-mismatch/unknown、首差与前 40 个重复组。
  - 模拟账本：`EngineTests.exe --ledger` 从开场夹具调用同一 C# 引擎，导出事件、生命变化、卡牌冷却/弹药、实例历史与 lineage；不写游戏目录。
  - UI 历史：新增播放头模型测试，覆盖旧形态→新形态、变形帧切换、离场后隐藏、向前拖回仍保留旧状态。
  - 发布门禁：新增只读 `tools/check_actionaxis_release.py`，比较源码 DLL/已安装 DLL 的 SHA256 与 BepInEx 日志中的 engine MVID；不部署。
- 改动文件：`tools/native_replay_ledger.py`、`tools/native_replay_contract_v1.json`、`tools/native_replay_compare.py`、`tools/test_native_replay_ledger.py`、`tools/test_native_replay_compare.py`、`tools/check_actionaxis_release.py`、`tools/actionaxis_plugin/tests/NativeReplayLedgerExport.cs`、`tools/actionaxis_plugin/tests/G12UiHistoryTests.cs`、`tools/actionaxis_plugin/tests/Program.cs`、`tools/actionaxis_plugin/engine/CardMapper.cs`、`tools/actionaxis_plugin/engine/LiveBoard.cs`、`docs/native-rule-coverage.md`、`captures/diagnostics/g12/*`。未纳入 G01–G11 的并发改动；未使用 `git add -A`。
- 新增测试：
  - `test_matching_ledgers_pass`、`test_missing_effect_is_a_diff`、`test_transform_lineage_aligns_native_child_to_sim_generation`、`test_health_value_mismatch_is_reported`、`test_time_window_is_explicit`（`tools/test_native_replay_compare.py`，5/5）。
  - `test_opening_instance_requires_opening_phase`、`test_engine_fixture_excludes_stash_but_keeps_hand`、`test_contract_version_guard`（`tools/test_native_replay_ledger.py`，3/3）。
  - `G12UiHistoryTests.SwitchesFormAtTransformFrame`、`HidesRemovedItemWithoutLosingHistory`；`Program.cs` 注册 `G12UiHistory`，本轮 `PASS`。
- 验证：
  - `python -m unittest tools.test_native_replay_ledger tools.test_native_replay_compare -q` → 8/8 OK；与 `tools.test_rule_workplan` 合跑 13/13 OK。
  - `npm.cmd test -- --run` → 37 文件／102 测试全绿。
  - `bash tools/actionaxis_plugin/compile_tests.sh` → 退出码 0，全部套件 `OK`，含 `PASS G12UiHistory`；这是本轮 G12 收口时的全量 C# 结果。
  - `EngineTests.exe --ledger` 已从全量测试构建实际跑通两份夹具，输出与独立 G12 导出器一致；比较器计数见下。
  - `bash tools/build_actionaxis_plugin.sh --compile-only` → 退出码 0；仅既有 Unity `CS0618` 警告。
  - `git diff --check` → 退出码 0（仅 CRLF 提示）。
- 原生对账：
  - `9bbabba4`：DIFF；matched 5385／missing 984／extra 396／value-mismatch 3940／time-mismatch 0／unknown 18。账本 SHA256 `3324b1d0…7d3`，模拟账本 `a5120aae…38b2`。
  - `084bd840`：DIFF；matched 2900／missing 632／extra 327／value-mismatch 199／time-mismatch 7／unknown 30。账本 SHA256 `bc842f94…6c7`，模拟账本 `43a0a452…35b65`。
  - 关键差异：原生 `084bd840` 在 slot 4/7/2 的 8/14/16s 变形，当前引擎在 slot 2/4 的 8/16s 变形且无回退；`9bbabba4` 的 Charge、Haste/Freeze、冷却与生命/护盾账本仍有差异。matched 只表示该条记录对齐，不表示规则行为正确。
  - 已知未知：玩家开局 Max/Shield/Regen/Rage/EnragedDuration 缺失；随机种子不可重建；两份样本均无 `CombatSimEventEffectTriggered`；原生帧没有独立主动施放事件；`activationsDerived` 只是投影。详见 `captures/diagnostics/g12/README.md`。
- 构建部署：`--compile-only` 源 DLL SHA256 `2d514cc1…51f6`（Engine）／`7d73371b…926a`（Plugin），MVID `5b8f9149-…`／`a0c13528-…`；已安装 DLL `ac2f2fa6…c8d4`／`8c939512…351c`，MVID `6bc37297-d7e3-4a7f-8f49-c0b987f176d6`／`da2066d8-…`，哈希不同；BepInEx 日志加载 engine MVID 与已安装 Engine 一致、与本次构建不同。游戏进程正在运行，`DEPLOY=未执行`，Gate D 未执行。
- 剩余风险与阻塞：
  - 全量 C# 套件本轮已 `OK`；共享工作树仍可能被并发改动，发布前必须按同一提交复跑，不能把本轮结果外推。
  - 逐事件对账未通过：变形谱系顺序、Charge、Haste/Freeze、冷却与生命/护盾仍有差异；需要各规则族 Owner 逐条认领，不能靠放宽容差或改随机种子追平。
  - 两个样本都没有被动 `EffectTriggered`，也没有沙暴伤害序列；G12 仍需补样本。
  - UI 只做了模型级测试，未重启游戏、未录屏；发布 DLL 与已安装 DLL 不一致。
  - `Stash` 排除是依据 `section` 的原生语义；若某种战斗模式仍使用 Stash 卡，需另取证据，不能全局假定。
- 下一条可执行动作：在集成冻结后复跑 `bash tools/actionaxis_plugin/compile_tests.sh`、`EngineTests.exe --ledger` 与 `python tools/native_replay_compare.py`，确认计数未漂移；另取含 `EffectTriggered`／沙暴的完整回放。部署与游戏内 Gate D 必须另获用户授权，先正常退出游戏。锁释放：是（未提交、未部署）。

### 2026-10-24 22:47 · G01-01 · AllHands、Absolute 区域、位置与槽位目标契约一致性

- Owner / Reviewer / 分支 / 基线：本轮实现（G01）／待复核／当前共享工作树／HEAD `6eb032a` + 未提交并发改动；本轮未提交。
- 状态：IMPLEMENTED（G01 切片）；整包 Gate A 22:47 通过；未 VERIFIED、未部署。
- 范围：`primary_group=G01` 的 164 个模板（129 物品／35 技能）；种子 Weaponized Core、Gamma Ray、Omega Ray、Kusarigama、Enervating Sigil；专项覆盖 AllHands 27 个模板、`AbsolutePlayerHand/AbsoluteOpponentHand`（含 HandAndStash/Board/Stash）、`TTargetCardXMost`、位置目标及 `TTargetCardOccupying` 的槽位边界。
- 原生证据：数据库 SHA256 `FDFD3087E6A23EDA2C9F53BE68B012006CC949D03E8666DD51AD65ABC71B61E5`；`native_all_hands.json` SHA256 `351B6B5EEDF211B29921B4CDD8A6BE13766010B2F84A6546564A26B42FCC285E`；`native_weaponized_core.json` SHA256 `3F0F2A668567D96145C89BFFC3DE0CE5F7F17C64E6A3B7C48298B9EEB92AA567`；`native_audit_regressions.json` SHA256 `3362DD0E9BB017CC1234E7E25AA771B2AFABB120292AF883489DA8014D0EDF29`。槽位证据仍是 `[Stove] Socket Effect` `c017c0dd-af3c-47e0-b510-3661d9dcd7f2` 与 `[Cooler] Socket Effect` `d5515ae3-b98d-4272-8add-82f441969ffe`。以上是原生模板与模拟帧，不是游戏回放。
- 完成内容（五步）：
  1. 类型识别：`AllHands` 作为双方并集；新增 `AbsolutePlayer*`／`AbsoluteOpponent*` 物品区域识别；`TTargetCardXMost` 支持 Self/Enemy/Absolute 区域；`TTargetCardOccupying` 明确只由 `TCardSocketEffect` 提供，普通卡入口继续拒绝。
  2. 目标／主体：`NativeSkillSupport.RelativeSection/AbsoluteSection/SectionSupported/SectionSideMatches/SectionOwner/OwnerMatches` 成为共享判定；接入 `MapCardTarget`、`MapSubject`、Trigger 的 Target/Source、前提计数、`Effects.NativeItemsForTarget/ItemsForTarget`、`NativeDynamicAuras` 与 `NativeCardAbilityMapper`。相对 `Opponent*` 光环现在按拥有者翻转到对侧，不再误加己方。
  3. 结算：静态与动态 XMost 光环使用同一目标集；数值引用对最左/最右取下 1 张，对 AllHands/左右全体/最近邻保留完整集合；未改伤害、状态或队列顺序。
  4. 排空：被动的 Subject/Target 阵营使用同一 owner 判定；AllHands 仍包含敌方使用，Absolute 不随拥有者翻转；未改下一帧语义。
  5. 测试：新增 `tests/G01TargetConsistencyTests.cs` 九组——`AllHandsPathsAgree`、`AllHandsDynamicAuraAgrees`、`AbsoluteSectionsUseFixedSide`、`AbsoluteSectionsWorkForEnemyOwners`、`AbsoluteXMostUsesFixedSide`、`XMostAuraUsesSameTargetSet`、`PositionalPathsAgree`、`NeighborDoesNotCrossIneligibleCard`、`OccupyingFailsClosedEverywhere`；`PositionalRuleTests` 与 `AllHandsRuleTests` 继续保留武装核心、相邻成长和双方 AllHands 回归。
- 改动文件：`engine/SkillMapper.cs`、`engine/Effects.cs`、`engine/NativeDynamicAuras.cs`、`engine/NativeCardAbilityMapper.cs`、`engine/Triggers.cs`；`tests/G01TargetConsistencyTests.cs`（新增）、`tests/NativeRuleExpansionTests.cs`（把已支持的 `AbsolutePlayerHand` 失败断言改为仍未支持的 `AbsolutePlayerSkills`）、`tests/Program.cs`；本文件。共享工作树还含其它组并发改动，本日志只认领上述 G01 差异。
- 验证：
  - 定向回归：`G01TargetConsistency`、`PositionalRules`、`AllHandsRules`、`NativeRuleExpansion` 全部 `PASS`；`G01TargetConsistency` 覆盖 AllHands 主动/被动/静态与动态光环/计数/标签/聚合、Absolute 固定侧、XMost、位置 TriggerSource、最近邻不跨条件、Occupying 全入口 fail-closed。
  - `bash tools/actionaxis_plugin/compile_tests.sh`：G01 相关全部 `PASS`；22:47 复跑整包退出码 0，`OK`，含 CardMapper、G05DynamicAura、G07、G08、G09、G12 全部既有套件。此前一次因并发 G05/CardMapper 在途断言失败，已由各自修复，不能把中间失败算作 G01 回归。
  - `npm.cmd --prefix d:\巴扎plus test -- --run`：37 文件／102 测试全绿。
  - `bash tools/build_actionaxis_plugin.sh --compile-only`：退出码 0，仅既有 Unity `CS0618` 警告；最终产物 `ActionAxisEngine.dll` SHA256 `45AD4B6F5CD20B37A632C7F43FC72FB648B976FC5716E67E213C35918C2F3185`，`ActionAxisPlugin.dll` SHA256 `E98307482C8857EAC8759EA2E3F96A0AEE29B81D215DBE91A9240A890ABA29B3`。
  - 矩阵（`native_mapping_templates.jsonl`，同一构建）：物品 1146／技能 498；至少一个组合受阻 125 物品／65 技能（最终复跑 `tmp_g01/g01_mapping_report_final.tsv`）。G01 主责 `unsupported` 模板由 7 降到 6；`Cherished Keepsake` 的 XMost 光环已解除。剩余 6 个：Adaptive Ordinance／Product Showcase 条件或次数、Frozen Horror／Meat 触发条件或次数、Hypergreens `SellPrice`、Iceberg 不可逆光环。
  - `git diff --check`：退出码 0（仅 CRLF 提示）。
- 原生对账：未执行 G01 逐事件回放；本轮只有模板级模拟与矩阵，Gate C 仍是 UNKNOWN，不能称原生一致。
- 构建部署：仅 `--compile-only`；未写入游戏目录、未重载、未重启，`DEPLOY=未执行`。
- 剩余风险与阻塞：
  - `TTargetCardOccupying` 仍 fail-closed；Heated／Chilled、槽位占用状态与 `TCardSocketEffect` 注入未实现，需 G05/G03/G07 接续，不能用普通卡目标冒充。（后续 G01-02 已实现共享模板契约与同槽解析，历史记录保留）
  - AllHands 仍有非本契约阻塞：Fairy Haze 的时效状态条件光环（G05）、Fire Hose 的 `BurnRemoveAmount` 动态光环（G05）、Trojan／Wild Quillback 缺能力定义（G00）。
  - `AbsolutePlayerSkills` 与技能作为卡牌目标未纳入本轮物品区域；`EnchantmentMapper` 仍使用较窄的 SelfHand/OpponentHand 目标解析，附魔主动/光环的 AllHands／Absolute 需 G07 复核。
  - XMost 静态/动态光环按 1 张处理且没有独立 `TargetCount` 证据；数值引用的 XMost 也只取 1 张，均需真实回放校准。随机目标的数量语义未在本轮改动。
  - 整包 Gate A 已在 22:47 全绿，但共享工作树仍含其它组未提交改动，不能据此声称 G01 原生一致；未跑 Gate D 游戏内验收。
- 下一条可执行动作：集成冻结后复跑 `bash tools/actionaxis_plugin/compile_tests.sh`，确认 G01 无新增失败；G05/G03/G07 接续 Occupying 与 Heated／Chilled；G12 取包含 Enervating Sigil／Weaponized Core／Absolute 区域卡的真实回放做逐事件对账。锁释放：是（未持有独占锁；未提交、未部署）。

### 2026-10-24 22:40 · G02-02 · 四种子品质成长／状态／Shiny 多重时序与原生触发口径

- Owner / Reviewer / 分支 / 基线：Qoder（G02 本次会话）／待复核／当前共享工作树／HEAD `6eb032a` + 未提交并发改动；本轮不提交。
- 状态：IMPLEMENTED（G02-02）；G02 定向回归通过；整包 C# 曾在追加用例后全部 `OK`，当前共享工作树最新复跑仍 `PASS G02StatusTiming`，但并发 G04/G08 断言失败；Gate C 为 DIFF；未部署。
- 范围：`docs/native-rule-task-index.csv` 中 `primary_group=G02` 的 148 条（127 物品＋21 技能）；四种子 Launcher Core `9fafc0a5…`、Dryad `cda40a45…`、Cog `ca77281c…`、Drowsdrop `a655fb50…`；品质 Bronze/Silver/Gold/Diamond 与四种子 Shiny/Heavy/Turbo 附魔路径；行为断言限于给定棋盘，不外推。
- 原生证据：
  - 数据源 SHA256 `FDFD3087E6A23EDA2C9F53BE68B012006CC949D03E8666DD51AD65ABC71B61E5`。
  - 新 fixture `tools/actionaxis_plugin/tests/fixtures/native_g02_seeds.json` SHA256 `7C6174EEDF9E52B2AFAE0537CC90EF09A6C91655BF46E84E92AB4F53BB6D1694`；四个完整模板与 `build/native_mapping_templates.jsonl` 逐字段一致。
  - 新 fixture `tools/actionaxis_plugin/tests/fixtures/native_g02_shiny_evidence.json` SHA256 `A0FCDCED645A3D8E6E8387F573C637AE385382754C50113522196DC6CBBDE000`；Waterskin 完整模板，来自同一批原生模板。
  - 新 fixture `tools/actionaxis_plugin/tests/fixtures/native_g02_resolved_blockers.json` SHA256 `1EE7C7C4A1F90B76C17FC3AD0E46641DEA33E4E3F88A05E56F27DB4803901305`；Stopwatch／Spark Plug／Poke 完整模板。
  - Shiny 全量审计：`captures/diagnostics/g02_shiny_audit.md`；1074 张 Shiny 物品中 54 张的 Shiny 定义带 `Multicast Add 1` 且基础无显式 Multicast，其中 41 张可观察活跃卡 40 张按 `1+1=2`，唯一例外 Hacksaw 属 G08 变形缺口。
  - 回放 `9bbabba4a216484d83d068a149811259`（29.05s，SHA256 `cfb0032e…f424b`）与 `5682f3cdc669456ca0757bea0a58942f`（15.05s，SHA256 `41dacfe7…62c7`）：Firefly 生效 5.10/5.05s 后，Core 被动充能分别落在 5.15/5.10s（+1 帧）；Spider 生效 6.00s 后落在 6.05s。回放 `e8e2fc61218442d4983d928f6ee9f8a4`（11.95s，SHA256 `68a62332…f924`）：Waterskin/Shiny 开场 `Multicast=2`，4.00/4.25s 与 8.00/8.25s 各两次充能；Haste 2000ms 从 4.00s 递减到 6.00s；Reload 在 5.00s 把 Ammo 2→4。
  - 关键修正：`native_kyver_cards.json` 中 Launcher Core `Abilities/2` 仍写作 `TTriggerOnItemUsed`，DB/CSV 实际为 `TTriggerOnBeforeItemUsed`；但回放显示运行时仍是**下一帧**充能，不能按类型名改成同帧。G02 校准保持 `onUse`，以回放为准；`§4.1` 的 BeforeItemUsed 同帧表述交 G03 复核。
  - 其它口径：Dryad Shiny 为 `TAuraActionCardModifyAttribute Multicast Add 1`；Drowsdrop `Multicast=1, AmmoMax=1, ReloadAmount=99`；Launcher Core `FlyingTargets=2/3/4`；Cog `HasteAmount=1/2/3/4s`；Dryad `CooldownMax=7/6/5/4s`。以上含原生模板与回放事件；G12 已加入开场冷却路径，整份比较器仍有其它差异。
- 完成内容（五步）：
  1. 类型识别：审计四种子基础 Abilities/Auras 与 Shiny/Heavy/Turbo 附魔；确认 `TTriggerOnBeforeItemUsed`、`TActionCardCharge/Haste/Slow/Freeze/FlyingStart/Reload`、`Multicast`、`AmmoMax`；未放宽未知类型。
  2. 目标／主体：Core 被动按 `SelfHand ExcludeSelf + OR(Friend,Flying)` 在 onUse 事件检查来源；Cog 只选相邻且运行时 `CooldownMax>0`；Drowsdrop 只选敌方有冷却卡；Dryad 充能自身。目标数在执行时读 `FlyingTargets/HasteTargets/SlowTargets`。
  3. 结算：回放 `9bbabba4`／`5682f3cd` 显示 Core 被动充能在伙伴/飞行卡生效后的下一帧（+50ms），因此校准保持 `onUse`；曾按模板类型改成同帧 `onBeforeUse`，已由回放否定并回退。修复 Shiny 的隐含 Multicast 基底：无显式 `Multicast` 字段的卡按 `1 + 静态多重` 计，再叠加 +1，避免 Dryad Shiny `0+1=1` 次，也避免吞掉旧卡静态多重；实时快照缺少 `Multicast` 时，`Initialize` 的继承光环反推跳过该属性，避免把隐含基底 1 再减成 0；静态光环与动态 `Refresh` 同口径。
  4. 排空：Core 自己使用仍被 `SourceOther` 排除；OR 条件只计一次；伙伴使用后的下一帧 Core 充能进入队列；未改全局队列顺序。
  5. 测试：`G02StatusTiming` 新增 `LauncherCorePassiveChargesOnNextFrame`、`DryadShinyCastsTwice`、`LiveShinyMulticastUsesBaseOne`、`LiveShinyWithoutSnapshotMulticastKeepsBase`、`CogShinyHastesTwicePerActivation`、`DrowsdropShinyConsumesAmmoPerCast`、`LauncherCoreShinyChargesAlliesTwice`、`RuntimeZeroCooldownIsNotAnEnchantmentTarget`、`ResolvedBlockerTimingTests`、`SeedTierGrowthMovesActivation`、`SeedStatusChangesMoveActivation`；原四种子用例保留。`ResolvedBlockerTimingTests` 覆盖 Spark Plug Radiant 的 Haste 2s→1s、Stopwatch Deadly 的敌方 CritChance 50→20、Poke 获得 CritChance 后自充能 1s。共享接口：`NativeDynamicAuras` Multicast 基底与 `NativeCardAbilityMapper` 静态光环基底，影响 G05/G07 的 Shiny 卡。
- 改动文件（仅列本单新增／修改；共享工作树还含其它组改动）：
  - `tools/actionaxis_plugin/engine/NativeDynamicAuras.cs`（Initialize/Refresh 隐含 Multicast 基底为 `1+静态多重`）
  - `tools/actionaxis_plugin/engine/NativeCardAbilityMapper.cs`（静态 Multicast 光环基底改用 `BaseMulticast`，并写入附魔前基底）
  - `tools/actionaxis_plugin/tests/G02StatusTimingTests.cs`（新增 11 组用例并读取完整模板 fixture）
  - `tools/actionaxis_plugin/tests/fixtures/native_g02_seeds.json`（新增完整四模板）
  - `tools/actionaxis_plugin/tests/fixtures/native_g02_shiny_evidence.json`（新增 Waterskin 完整模板）
  - `tools/actionaxis_plugin/tests/fixtures/native_g02_resolved_blockers.json`（新增 Stopwatch／Spark Plug／Poke 完整模板）
  - `captures/diagnostics/g02_native_replay_evidence.md`（回放关键帧证据）
  - `captures/diagnostics/g02_shiny_audit.md`（250 张无显式 Multicast 的 Shiny 审计与 Hacksaw/G08 例外）
  - `captures/diagnostics/g02_data_blocked.md`（6 个缺失能力定义的数据阻塞清单）
  - `docs/native-rule-coverage.md`（§7.2 G02 状态行与本条日志）
  - 未纳入：`NativeItemMapper.cs`／`NativeItemMapperTests.cs`／`AuditRegressionTests.cs` 的临时 `onBeforeUse` 改动已按回放回退，净改动为 0；其它组并发改动、`build/*` 生成物；未提交、未部署。
- 验证：
  - 定向：`tmp_g02/G02Harness`（engine + TestHarness + G02StatusTiming）编译退出码 0，输出 `G02 PASS`；新增与保留用例全部通过，包含回放反证后的 `LauncherCorePassiveChargesOnNextFrame` 与 Waterskin 实时 Shiny 的 `LiveShinyMulticastUsesBaseOne`。
  - 全量手工构建（避开并发占用的 `build/EngineTests.exe`）：该次中间态编译退出码 0，`G02StatusTiming`、`AuditRegression`、`NativeItemMapper`、`G07Enchantment`、`DynamicAura` 均 `PASS`；随后标准全量复跑已全绿。
  - `bash tools/actionaxis_plugin/compile_tests.sh`：22:32 退出码 1，`PASS G02StatusTiming`，唯一失败 `CardMapper`；22:38 曾因并发编译锁 `EngineTests.exe` 报 CS0016；22:43 复跑退出码 0；追加 Shiny/阻塞解除/实时缺 Multicast 用例后，曾取得一次全量 `OK`（含 `PASS G02StatusTiming`、`PASS G12OpeningCooldown`）。当前最新复跑退出码 1，`PASS G02StatusTiming`，失败为本单外的 `NativeRuntimeRules`（G04 `On The Prowl`）与 `G08Transform`（`GreenScreenCopiesLeftMediumWithEnchant` 模板期望 3172cda6 vs 62c10400），G02 无失败。
  - `npm.cmd --prefix d:\巴扎plus test -- --run`：退出码 0，37 文件／102 测试全绿。
  - `bash tools/build_actionaxis_plugin.sh --compile-only`：退出码 0，仅既有 Unity `CS0618` 警告；本单最后一次构建 `ActionAxisEngine.dll` SHA256 `39C7FF51C11C4B7F7A1DEF19B8FEAE51900C70D7FD8131B8649FEB6FD7627032`，`ActionAxisPlugin.dll` SHA256 `B4E3EE35ED97A9B1F689CFD6E809874B04F18ED315FFB620EA41F7DDFF368632`（共享工作树其它组后续重建会改变 DLL 哈希）。
  - 矩阵（同一批 `build/native_mapping_templates.jsonl`，最终报告 `tmp_g02/native_mapping_report_final4.tsv`，387,678 行，SHA256 `F910E9EFDCB90E7FAADF45D7E051A4F90066B4C0525BC5913C949AF58553DF69`）：G02 127 物品中 121 全映射、0 部分、6 全阻（Cruise Ship、Marlon、Dooltron、Snowmobile、Cosmic Plumage、Teddy，均为原生 AbilityId 缺失）；21 技能均有 simulated/inactive 行、无 unsupported。四种子：Launcher Core 336/336，Dryad／Cog／Drowsdrop 各 420/420 mapped。Stopwatch／Spark Plug／Poke 已由并发组修复并纳入 `ResolvedBlockerTimingTests`。
  - `python tools/rule_inventory.py --work-index docs/native-rule-task-index.csv`：退出码 0，1810 唯一模板，G02 148 条；`python -m unittest tools.test_rule_workplan -v`：5/5 OK。
  - `git diff --check`：退出码 0（仅 CRLF 提示）。
- 原生对账：
  - 整份比较器（当前引擎、G12 夹具）：`python tools/native_replay_compare.py tmp_g12/ledger_9bbabba4.json tmp_g02/sim_9bb_g12cooldown.json --json tmp_g02/cmp_9bb_g12cooldown.json --csv tmp_g02/cmp_9bb_g12cooldown.csv` 退出码 1，`verdict=DIFF`；matched 5219／missing 1150／extra 372／value-mismatch 3757／time-mismatch 0／unknown 18；effects matched 62／native 338／simulated 296／missing 276／extra 234。
  - 样本含 Launcher Core、Cog、Drowsdrop，不含 Dryad。Core/Cog/Drowsdrop 的绝对冷却条仍大面积错位。G12 已加入 `G12OpeningCooldown` 路径（开场 `attributes.Cooldown` → `StartCooldown`）且该测试通过；本样本比较器仍为 DIFF，剩余差异需 G12 与各规则族按关键帧分派，G02 不单方面归因。
  - 相对行为证据：`captures/diagnostics/g02_native_replay_evidence.md` 记录 Core 被动 +1 帧、Waterskin/Shiny 两次 250ms 施放、Haste/Slow/Freeze 逐帧到期、Haste 加法叠层、Reload 装到上限。结论：这些关键路径有回放证据；整份 Gate C 仍是 DIFF，不能宣称全绿。
  - 充能 250ms 串行（2026-10-25，回放 170666bf 实证）：同一（充能者,目标）的 `CardCharge` 结算至少间隔 250ms——同帧两次物品使用只立即结算一次（+1 帧），第二次落在 `上次结算+0.25s`（帧 81/86、161/166…）；语料 27,629 次充能同对相邻间隔 1–4 帧为 0 次。同帧跨目标充能（1,257 组）不受影响，multicast 施放天然 250ms 间隔亦不受影响。充能落在已就绪（冷却 0、本帧将重置）的卡上时挂起到重置后的新冷却（`PendingChargeOnReset`）。实现位于 `Effects.TryReserveCharge`/`ApplyCharge` 与 `Battle.ProcessDeferredCharges`；测试 `G12DeterminismTests`（引擎全量 OK）。此前“同帧多次使用只应结算一次/Matchbox 延迟一帧”的表述按此口径收窄为按（充能者,目标）串行。
- 构建部署：仅 `--compile-only`；未写入游戏目录、未重载、未重启，`DEPLOY=未执行`。
- 剩余风险与阻塞：
  - Core 的 DB 类型是 `TTriggerOnBeforeItemUsed`，但回放显示下一帧充能；`§4.1` 的“同帧”表述与回放不一致，需 G03 复核。G02 校准保持 `onUse`，不把类型名当运行时语义。
  - Shiny 隐含基底修复影响所有无显式 Multicast 的 Shiny 卡；本单验证 Dryad／Cog／Drowsdrop／Waterskin，审计 41 张可观察活跃卡 40 张通过；Hacksaw 例外属 G08 变形映射，需 G08 处理；G07 仍需复核其余附魔卡。
  - G02 6 物品因原生能力定义缺失受阻（DATA）：Cruise Ship 缺 Ability `3`、Marlon 缺 `3`、Dooltron 缺 `1`、Snowmobile 缺 `3`、Cosmic Plumage 缺 `3`、Teddy 缺 `2`；不能猜类型或数值，清单见 `captures/diagnostics/g02_data_blocked.md`。Stopwatch／Spark Plug／Poke 已解除。
  - Gate A 当前最新复跑仍被并发 G04/G08 断言阻断；G02 定向 `PASS`；Gate C DIFF；Gate D 未执行（未获部署授权）。
- 下一条可执行动作：G04 `On The Prowl` 与 G08 `GreenScreenCopiesLeftMediumWithEnchant` 并发断言归零后复跑标准 `compile_tests.sh`；G12 在已加入开场 `Cooldown` 路径的基础上复核剩余 DIFF，优先看 Core/Cog/Drowsdrop 的 Charge/Haste/Slow/Reload 关键帧；G07 复核无显式 Multicast 的 Shiny 卡与 Hacksaw/Blender/Peacewrought 的 G08/G09 关联缺口；复核通过后再决定是否提交。锁释放：是（未提交、未部署）。

### 2026-10-24 23:00 · G05-01 · 动态倍率、敌方目标、人物光环与持续冷却缩减

> 完整、可读的逐项记录：[g05-01-acceptance.md](g05-01-acceptance.md)。

- Owner / Reviewer / 分支 / 基线：本轮执行者（G05）／待复核／`main` 共享工作树／HEAD `6eb032a` + 未提交并发改动；本轮不提交、不部署。
- 状态：IMPLEMENTED（G05-01）。Force Field／Spiky Shield 已用真实 BPP 回放值校准；Passive Power 回放无可观察光环事件，Rate Limiter 无本地回放；逐事件 comparator 未运行，不能标 VERIFIED。
- 范围：G05 四种子 Passive Power `22e88b23…`、Rate Limiter `b607f75b…`、Force Field `85f59c88…`、Spiky Shield `8ec1c3f9…`；另以 Forest Cloak `f718a14b…` 校准动态人物光环，以运行时 CooldownMax 进出 0 校准 Passive Power 的可逆倍率。
- 原生证据：
  - 数据源 `GameData.db.zip` SHA256 `FDFD3087E6A23EDA2C9F53BE68B012006CC949D03E8666DD51AD65ABC71B61E5`。
  - `tests/fixtures/native_g05_seeds.json` SHA256 `45C05F4A70B92F007A67B9F8A86E466057F5AC70A1BB930519A7609037CF1E9D`；新增 Forest Cloak／Large Appetites 完整模板。
  - `tests/fixtures/native_g05_replay_calibration.json` SHA256 `14065A47D0E7619A18C0C61EB69B2A51435BA0E720E27BD883F1DBE39B35A8C3`；回放来源与关键值见下。
  - `ea043fb8292c4faaa76262aebeb60c39`（SHA256 `3813cd2e7902ea96c937fa67b0ec07fee721b38721ce549b9f0e971f07cf7b22`）Force Field Gold：6.45s `DamageAmount=553=base 11+Shield 542`，6.5s `425=11+414`；6.4s 被排除，因为第二笔护盾在光环刷新后才到账。
  - `bb30d9e9d76c426b89d28e4fc9ea35d3`（SHA256 `698fba023f6973967a31ef894ade108e5c2b50836cc48674aacdcdae64fb5ac7`）Spiky Shield：开场 `CooldownMax=5699`、`PercentCooldownReduction=5`；4.0s `6000→5699`、4.85s 就绪、4.9s 出手；4.0s 另有独立 50ms 直接冷却效果，因此不能把 `2000→1649` 全归因于 5%。
  - `bce53fafda924b60b6ade61877e37255`（SHA256 `35f483e6c3dce2c9d7949968019049c97741592ee0b345326f9c439f66bf5653`）含 Passive Power，但它在 Stash 且无光环事件；本单不把该回放当作倍率证明。Rate Limiter 未在 315 份本地回放中找到。
  - 以上是回放导出值与模板值，不是完整开场棋盘重建，也不是逐事件一致结论。
- 完成内容（五步）：
  1. 类型识别：`TAuraActionCardModifyAttribute` 的 `Multiply` 允许 `TReferenceValueCardAttribute` 等运行时引用；固定零倍率仍拒绝。`TAuraActionPlayerModifyAttribute` 新增 `AdditiveMultiply` 与 `PercentDamageReduction` 校验；未知类型／属性／目标继续抛异常。
  2. 目标／主体：动态与静态光环复用 `SectionSupported`/`SectionSideMatches`，支持 `SelfHand/SelfBoard/SelfHandAndStash/SelfStash/OpponentHand/OpponentBoard/OpponentHandAndStash/OpponentStash/AllHands`；Rate Limiter 的 `OpponentBoard` 已进入运行时目标集。人物光环支持 Self/Opponent，目标条件仍 fail-closed。
  3. 结算：`NativeDynamicAuras.ApplyHeroAuras` 新增可撤销人物派生层；记录 `InternalHero.AuraMaxHp/AuraRegen/AuraRageMax/AuraEnragedDurationMs/AuraPercentDamageReduction`，每次 Refresh 从“当前值−已计入光环”重算。`Effects.ApplyDamage` 对非自伤应用人物百分比减伤。持续 CDR 仍只走 `AuraCooldownFlat/Percent`，不写回 `LiveAttributes`。
  4. 排空：保留 `Active()` 每次判定条件；光环来源 `Destroyed` 后不再进入 `All()`。`Initialize` 的实时反推只处理卡牌光环，避免把人物光环误当卡牌属性剥离。
  5. 测试：新增 `G05DynamicAuraTests`，注册 `Program.cs`，七组行为用例（见下）；真实模板夹具不使用裁掉前提的简化卡。
- 改动文件（仅列 G05 直接改动；共享文件另有其它组并发改动，未纳入本单）：
  - `tools/actionaxis_plugin/engine/NativeDynamicAuras.cs`（动态引用倍率、人物派生层、敌方区域、初始化过滤）
  - `tools/actionaxis_plugin/engine/NativeCardAbilityMapper.cs`（动态人物光环进 runtime、AdditiveMultiply、操作校验）
  - `tools/actionaxis_plugin/engine/SkillMapper.cs`（动态人物技能光环路由；不把静态 HealthRegen 误当动态）
  - `tools/actionaxis_plugin/engine/Effects.cs`（人物百分比减伤）；`engine/BattleContext.cs`（人物光环派生字段）
  - `tools/actionaxis_plugin/tests/G05DynamicAuraTests.cs`、`tests/Program.cs`、`tests/fixtures/native_g05_seeds.json`、`tests/fixtures/native_g05_replay_calibration.json`
  - `docs/native-rule-coverage.md`（§4.3、§6.3、§7.2 G05 与本条日志）
- 新增测试（`G05DynamicAuraTests.Run`）：
  - `PassivePowerMultipliesZeroCooldownCardsWithoutCompounding`：Weapon 被动 20→25，Burn 4→5，1s 主动武器保持 20；CooldownMax 运行时进出 0 五次仍为 25/20，不累乘。
  - `RateLimiterMultipliesBothBoardsAndDrivesCharge`：自身 Board 4000→2000，敌方 Board 2000→1000；实际充能读取 2s 而非 4s。
  - `ForceFieldAddsCurrentShieldWithoutCompounding`：实时 553 反推 base 11；护盾 414→Damage 425；Gold 模板 5.0s 出手，先加 60 盾后以 613 伤害结算。
  - `SpikyShieldLiveSnapshotIsNotCountedTwice`：实时 5699＋5% 保持 5699；5.7s 首次出手；922 盾＋79 盾后伤害 1001。
  - `DynamicHeroAuraTogglesWithoutStacking` / `DynamicHeroHealthMaxTogglesWithoutStacking`：Forest Cloak 静态 MaxHp 20% 为 12000；暴怒 50% 减伤只作用一次，4 次开关仍 12000/11950；动态 HealthMax +1000 开关 4 次仍 1000/2000。
  - `CooldownAuraChangesActualActivationTime`：4s 卡在 50% CDR 下首次 2.0s、第二次 4.0s；HP 条件开关六次，CooldownMax/剩余进度回到 4/3。
- 验证：
  - `bash tools/actionaxis_plugin/compile_tests.sh`：退出码 0，全部套件 `OK`；`PASS G05DynamicAura`。中途曾因并发 G02/G01 在途出现 `CardMapper` 一条失败，修复本单的静态 HealthRegen 路由后整包恢复全绿。
  - `npm.cmd --prefix d:\巴扎plus test -- --run`：退出码 0，37 文件／102 测试通过。
  - `bash tools/build_actionaxis_plugin.sh --compile-only`：退出码 0，仅既有 Unity `CS0618` 警告；`ActionAxisEngine.dll` SHA256 `69A6A3A3C4CD7E608A27F9FA56393EDB9C109B8C90D606A80BC34AACDAB3EE95`，`ActionAxisPlugin.dll` SHA256 `F1EFF68A426D118038675BBF19A8E9406E78D79ADD6167C0D96516C36389C958`（22:47 shared build；并发组可能再次覆盖）。未部署。
  - 矩阵：`rule_inventory.py --mapping-input` 导出 1644 模板；`EngineTests.exe --coverage` 退出码 0，报告 387,678 行，SHA256 `9A4677179EC13E4A7EE84844B0D95AC3AC54849EDCF86C258E972212581C9057`；item mapped/unsupported 338414/34324，skill simulated/live-applied/inactive/out-of-combat/unsupported 8142/740/4510/384/1164。四种子：Passive Power 18 simulated／12 inactive（稀疏品质），Rate Limiter 12 simulated／18 inactive，Force Field 336 mapped，Spiky Shield 420 mapped；相对本单前报告 Passive Power／Rate Limiter 的 declared 行由 unsupported 变为 simulated。报告 schema 与历史 126,772 行不同，不能直接比较总通过率。
  - `git diff --check`：退出码 0（仅 CRLF 提示）。
- 原生对账：仅做值级校准，不是逐事件对账。Force Field 的 Add 语义、Spiky Shield 的有效上限与出手帧已用上列回放值核对；本机回放 Player 开场为空、缺少完整 opening sidecar，无法把整张棋盘注入 `BattleConfig`，matched/missing/mismatch 与 UNKNOWN 数量未计算。Diff 结论：`UNKNOWN`，不得写成 G05 已通过 Gate C。
- 构建部署：仅 `--compile-only`；未写入游戏目录、未重载、未重启，`DEPLOY=未执行`。
- 剩余风险与阻塞：
  - Rate Limiter 无本地回放；其“目标为整侧 Board”与 tooltip“最右物品”的差异仍按模板数据实现，语义需下一条真实回放确认。
  - Passive Power 的引用倍率只证明模板与可逆层；回放没有可观察出手/光环事件。
  - 持续 CDR 的 Flat+Percent 组合、100% 缩减反推与取整仍未被原生证明；Spiky 回放只覆盖 5% 百分比与一段已在进行中的冷却。
  - 动态人物光环的实时快照若在开场已含同一光环，当前只从人物当前值反推已计入量，未像卡牌一样有独立 seed 表；需要真实含 Forest Cloak 的开场回放验证。
  - `PercentDamageReduction` 目前对非自伤直伤与 DoT 都按百分比缩减；原生是否只对直伤生效未知，交 G07/G10 复核。
  - 共享工作树仍含其它组未提交改动；本单不能代表整包 Gate A/C 已全部通过。
- 下一条可执行动作：G12 把开场 `Cooldown`、护盾、Haste 与 Player 侧卡表注入重建器后，优先对 `bb30…` 的 4.0/4.85/4.9 帧和 `ea043…` 的 6.4/6.45 帧做逐事件比较；再寻找含 Rate Limiter／Passive Power 可观察光环的本地回放。锁释放：是（未提交、未部署）。

### 2026-10-25 · G05-02 · 全量 786 模板与回放值级校准

> 完整记录：[g05-01-acceptance.md](g05-01-acceptance.md) §9；回放证据：[g05-02-replay-calibration.md](g05-02-replay-calibration.md)。

- Owner / Reviewer / 分支 / 基线：本轮执行者（G05）／待复核／`main` 共享工作树／`6eb032a` + 未提交并发改动；本轮不提交、不部署。
- 状态：IMPLEMENTED（G05-02）。`primary_group=G05` 的 **786/786** 模板无 `unsupported`／`partial`；剩余 `inactive` 为“该品质无原生效果／当前棋盘条件不满足”。G05 两套测试 PASS。Gate C 逐事件对账与 Gate D 未执行，不能标 VERIFIED。
- 范围：不再只做四种子，全量覆盖 `docs/native-rule-task-index.csv` 的 G05 主责模板。
- 新增实现：实时人物属性快照（`Custom_N/Level/CritChance/RerollCostModifier` 等）；人物派生层新增 `AuraCritChance`，暴怒结束立即 Refresh；自引用收窄（Santoku/Giant Sub/Fire Claw/Subscraper）与 `Subtract`（Shuriken）；`AbsolutePlayerSkills`（Ritual Dagger）；`TTargetPlayer Both`（Nitro）；玩家技能静态/动态光环、经济光环 OUT、混合技能（First Strike/Inexorable）；缺失 AuraId 跳过并保留其余部分（Buddy System/Fairy Haze/Quick Defenses）；Nanobot Orange 缺 `DamageAmount` 由自身光环补 0。
- 矩阵：`native_mapping_report.tsv` 387,678 行，本单最后校验 SHA256 `A703634EFA956EC1DCD54D2BA9FAC61936619E4B68B846DA8375A4F144D1A1BE`（共享产物，他组复跑会改写）；G05 行 165,672，786 fully ok／0 partial／0 blocked；全库 item unsupported 1,344、skill unsupported 54（G05 为 0）。
- 回放：`CombatReplays` 200 份，196 份含 G05 种子；16 个模板取得值级证据（Heavy Mettle、Fire Claw、Healthy Lifestyle、Waters、Giant Sub、Nitro、Cash Cannon、Oinkment、Philosopher's Stone、Iron Sharpens Iron 等）；11 个关注模板无样本。`--no-snapshot` 丢弃 recorder 侧棋盘，仅为值级校准。
- 验证：04:30 `compile_tests.sh` 整包 `OK`（39 套件，含 `PASS G05DynamicAura`／`PASS G05FullAura`）；前端 37 文件／102 测试通过；`--compile-only` 退出码 0；`ActionAxisEngine.dll` SHA256 `C6ECE5E9…`、`ActionAxisPlugin.dll` SHA256 `42F0A412…`。
- 并发说明：04:30 之后共享树中 G04／G08 新增断言造成 2 条非 G05 失败（`AllG04PrimaryTemplatesSmokeRuntime` 的 `live-applied` 技能进入 `ctx.Skills`、`G08TransformTests.GreenScreenCopiesLeftMediumWithEnchant`），G05 两套仍 PASS；未修改他组文件，不把整包结果全部归因于 G05。
- 剩余阻塞：Gate C／D；Buddy System／Fairy Haze／Quick Defenses 与 Nanobot Orange 的 DATA 缺定义（G00）；Cash Cannon `+50` 基底、Iron Sharpens Iron 回放 C0 差异等未解释值。锁释放：是。

### 2026-10-25 · G03-FULL · 45 模板全量入口、机制族与回放关键路径

- Owner / Reviewer / 分支 / 基线：Qoder（G03）／待集成人复核／main 共享工作区（未提交）／`6eb032a` + 并发改动。
- 状态：IMPLEMENTED（G03 全量模板入口与机制族回归通过；分账：30 张战斗卡行为断言、8 张纯 OUT、1 张 OUT+DATA（Resonance Crystal）、4 张缺 AbilityId DATA、2 张 DEBUG 入口/G07；未部署）。
- 范围：`primary_group=G03` 全部 45 个模板（29 物品＋16 技能），不再只做六种子。新增全量夹具 `tests/fixtures/native_g03_all.json`（45 模板／522204 bytes，SHA256 `A0CCC63068CE9743755ACB05ED38B18C0115952A37D5C8FD3A079A00F7746CD9`）。4 个 DATA：Fortune Cookie、Sulphur、Shard of Obsidian、Wild Quillback；Resonance Crystal 缺 `Auras/0`，但全部触发为 CardSold，归属 OUT+DATA。Machine Learning／Renovation 的 CardSelected/Level/Upgraded 分支归 OUT；`IsNonCombatTrigger` 接受 `TTriggerOnCardSelected`，不再让场外 `TActionCardUpgrade` 阻塞整卡。
- 新增测试：
  - `G03FullCoverageTests.AllG03TemplatesMapOrAreDataBlocked`：45×声明品质×双方实际映射；4 张缺 AbilityId 卡必须留 warning，场外卡（含 Resonance Crystal 缺 AuraId 0）允许 warning，其余战斗卡 0 warning，技能无 unsupported。
  - `NonCombatCardSelectionIsOutOfCombat`：Machine Learning／Renovation 全部 out-of-combat/inactive；曾用还原旧白名单验证 RED（24 断言）→ GREEN。
  - `PistolSwordRequiresAmmoSource`、`CaltropsRequiresOpponentSource`、`AttachmentSetToolCombination`、`SkillTagAndSizeFilters`：SelfHand/OpponentHand、Tag/Size/AmmoMax 过滤与数值。
  - `PerformedActionTriggers`、`TriggerOrDoesNotDoubleCount`：Burn/Poison/Slow/Freeze 触发及 OR 去重。
  - `FightStartedEnchantsMatchingTagsOnly`：Happy Accident 的 Drone/Vehicle 与 Experimental Chef 的 Food/Tool 在开场各附魔一次，普通物品不被选中。
  - `RemainingCombatCards`：Tinfoil Hat/Spiked Collar/Unwavering 的来源与 Tag/ExcludeSelf；Myrrh/Hemlock 主动数值；Pizza 的 Heated 前提控制 Burn。
  - 既有 `G03TriggerRuleTests`：Hunting Hawk 4.0/4.05、Wolf Mask/Bloodhound/Frozen Secrets/Counterstrike 次数前提、Retool 邻接装填、Propeller Hat 使用前同步。
- 回放：`CombatReplays/CombatReplays/04775112acd94dfaa7e9f03c4fc2995c.payload.mpack.gz`（SHA256 `63fb461a…98b0`）frame 80/4.0s 敌方 Hunting Hawk CardHaste，frame 81/4.05s 同目标 FlyingStart，与模拟一致；已扫描 200 份回放，58 份含 G03 主责模板，其余逐卡对账未完成。FightStarted 的辅助真实模板 `native_g03_support.json`（Repair Drone）SHA256 `1D0D3D206FA7551E3289FE4B2076C853876FD15D24EAEF5DF3211B0985FC7F98`。
- 映射：45 模板 8970 行，6690 mapped／1800 mapped-warning／234 simulated／186 inactive／60 out-of-combat／0 unsupported；5 个模板保留可见数据警告——4 张缺 AbilityId DATA + Resonance Crystal 缺场外 AuraId 0（OUT+DATA），技能 unsupported 0；`native_mapping_g03_group.tsv` SHA256 `61C94E00E972820DB2454AD674B90F19AC609FB6189DDF0BE64B23433B26A088`。
- 验证：G03 独立编译 `PASS G03TriggerRules`／`PASS G03FullCoverage`／`OK`；官方 `compile_tests.sh` 最近一次编译成功，`PASS G03TriggerRules`／`PASS G03FullCoverage`，整包退出码 1，10 个失败集中在 `SkillsTests`／`NativeRuleExpansionTests`／`NativeSkillDynamicAuraTests`／`NativeRuntimeRulesTests` 的静态光环／技能状态回归（属其它组），G03 未参与；日志 `build_g03_full_coverage.log`。`build_actionaxis_plugin.sh --compile-only` 退出码 0，仅既有 Unity CS0618 警告；`ActionAxisEngine.dll` SHA256 `6826E7CBB2B9A703F2BB85E7BD8B571275F2EAD12F81B7E571B558476EB15E8F`，`ActionAxisPlugin.dll` SHA256 `B12B45353C084322A5E367ADA290E4583D636CC995ED15DA44FD2D29D06C87DC`。未部署，`DEPLOY=未执行`。
- 下一条：并发组恢复整包后复跑 `compile_tests.sh`；G12 继续用 58 份回放做逐事件比较。锁释放：是。

### 2026-10-25 · G07-02 · 全库附魔层清零、运行时目标数与回放初筛

- Owner / Reviewer / 分支 / 基线：Qoder（G07 续作）／未指定／`main` 共享工作树／HEAD `6eb032a` + 未提交并发改动；本轮不提交、不部署。
- 状态：IMPLEMENTED（G07 附魔层）；**全库 1146 物品＋498 技能的附魔层 0 告警、0 个“仅附魔新增阻塞”**。仍非 VERIFIED：最新权威矩阵基础卡尚有 5 物品／2 技能硬拒绝、46 模板仅缺定义告警，且无完整逐事件对账；这些基础失败对 `None` 和所有附魔同时存在，不是附魔新增缺口。
- 范围：完整 `Enchantments.*.Abilities/Auras/Attributes`、`TActionCardEnchant/EnchantRandom/EnchantRemove`、Radiant/Deadly/Golden、`DamageCrit`、抗性/免疫、运行时光环目标、附魔改变基础属性。矩阵覆盖 1644 个完整物品／技能模板、387678 个品质×阵营×场景组合。
- 原生证据：DB `FDFD3087E6A23EDA2C9F53BE68B012006CC949D03E8666DD51AD65ABC71B61E5`；`native_g07_enchantments.json` SHA256 `D503CE6D95EA0C49CD2D84BA49607ECA49D68B930DD5D2DBA063BD3CD241DE4B`；`native_g07_remaining.json` SHA256 `9F73C6B632161657C02E36706114FC32E1F87BC4767017263F2C754473FEF4FB`；矩阵 `native_mapping_report.tsv` SHA256 `A703634EFA956EC1DCD54D2BA9FAC61936619E4B68B846DA8375A4F144D1A1BE`（并发组完成 Blender/Grabinator/缺 Aura 定义修复后的权威快照）。注：共享树在本轮多次重跑矩阵，114/65、100/72、56/16、14/8、8/7、7/6 等均为过期中间计数，以本条 5/2 为准。
- 完成内容：
  1. **可逆 Subtract 光环**：`NativeDynamicAuras.ValidateCardAura/Refresh/Initialize` 与静态映射均支持 `Subtract`；修复 Foul Mushroom、Piggy Bank、Gumball Machine、Stopwatch、Iceberg、Dragon Tooth 等敌方减暴击/自身计数器/售价减免。
  2. **急速抗性**：新增 `PercentHasteReduction` 字段与 `NativeDynamicAuras.HasteResistance`，Spark Plug Radiant 使相邻卡 2s Haste 实际只生效 1s；`Effects` 的 Haste 分支读取运行时抗性。
  3. **运行时目标数**：`FlyingTargets`/`EnchantTargets` 进入光环白名单与 `AuraAttribute`；Hover Tech Shiny 实际飞 5 个目标，Skyspirit Ashes Shiny 实际附魔 3 个目标（基础 2＋1）。
  4. **随机/最值光环目标**：新增 `TTargetCardRandom` 光环的稳定目标缓存（`BattleContext.AuraTargets`），Showcase Deadly 只给一个合法目标且多次 Refresh 不换人；`TTargetCardXMost` 光环通过 `Effects.NativeItemsForTarget` 应用到最左/最右卡，Fiber Optics Radiant 实测只给最右卡 50% 抗性。
  5. **附魔改变基础属性**：`ItemAttributes` 读取 Ammo/AmmoMax；`NativeDynamicAuras` 支持 Ammo 数值修正的 `Subtract`；Shuriken 的 Multicast＝当前 Ammo，Santoku 把自身 CritChance 复制给左卡，Money Furnace 的零倍率把 Spare Change 售价置 0。
  6. **Heated/Chilled 与时效条件**：`Heated`/`Chilled` 进入动态光环白名单；`BattleContext.Now` 与 `AttributeValue(Haste/Slow/Freeze)` 支持时效状态条件，Cutlass/Tiny Cutlass 等按当前状态开关。
  7. **战斗内附魔基础值**：上一轮修复 `NativeEnchant` 基底快照后，补 Shiny/Golden/Deadly 的实际属性断言；Dishwasher 先由主动能力加热武器，Radiant 的 Heated 条件光环随后实际给武器 50% 缓速/冻结抗性。
  8. **其他触发/动作**：`Ammo/CritChance/SellPrice/RegenApplyAmount` 的卡牌属性变化触发、`Ammo` 消耗/装填事件、`HealthMax` 人物属性变化事件、`PercentDamageReduction` 限时人物减伤动作、`OutOfCombatOnly` 分支跳过、`TTriggerOnFightStarted CombatType` 支持。
  9. **校准路径**：保留 `FromEnchantment` 被动触发；Drowsdrop 校准重建基础触发器后附魔被动仍执行。
- 改动文件（仅 G07 直接改动；共享文件含并发组改动，未认领其它差异）：
  - `engine/SkillMapper.cs`、`engine/Effects.cs`、`engine/Triggers.cs`、`engine/Battle.cs`、`engine/BattleContext.cs`、`engine/Types.cs`、`engine/ItemAttributes.cs`、`engine/NativeDynamicAuras.cs`、`engine/NativeCardAbilityMapper.cs`、`engine/NativeEnchant.cs`、`engine/EnchantmentMapper.cs`
  - `tests/G07EnchantmentTests.cs`、`tests/Program.cs`、`tests/fixtures/native_g07_enchantments.json`、`tests/fixtures/native_g07_remaining.json`
  - 本文件；未提交、未部署。
- 新增测试（`G07EnchantmentTests` 共 34 个用例）：在 G07-01 的 21 个基础上新增
  `SubtractAurasReduceCritAndCounters`、`RadiantHasteReductionHalvesHaste`、`ShinyFlyingTargetsAddsTargets`、`ShinyEnchantTargetsIncreasesEnchants`、`ShowcaseDeadlyPicksOneTargetStably`、`RadiantXMostAuraTargetsRightmost`、`TrailMarkersCooldownSubtractIncreasesCooldown`、`SlingshotGoldenIncomeReferenceMaps`、`LichenCleanseUsesRuntimeRemoveAmount`、`DishwasherHeatedRadiantAuraExecutes`、`ShurikenUsesRuntimeAmmoForMulticast`、`SantokuCopiesCritToLeftCard`、`MoneyFurnaceZeroMultiplierSetsPriceToZero`。
- 验证：
  - 隔离 G07：`PASS G07Enchantment` / `OK`（34 个用例）。
  - 整包：G07 自己的绿色快照为 67/67、0 FAIL（`build/g07_full_final4.txt`，前一张 `g07_full_final3.txt` 同为 67/67）。随后并发组在 04:29–04:32 修改 `SkillMapper/Effects/Transforms/G04/G08` 测试后，最新 `build/g07_full_final5.txt` 为 65 PASS／2 个套件失败（2 个断言）：`NativeRuntimeRules` 的 Friend of the Sea 未出现在 `ctx.Skills`、`G08Transform` 的 GreenScreen 期望供体 `3172cda6…` 实得 `62c10400…`，均非 G07 套件；`G07Enchantment` 在该快照仍 `PASS`。整包最终全绿需等 G04/G08 组收口后复跑。
  - 矩阵：`python tools/rule_inventory.py --mapping-report ...` → 1644 模板；独立 `G07Coverage.exe --coverage` 与并发组随后重跑（同源码，避开共享 exe 锁）→ 退出码 0，387678 行；权威快照 `item/mapped=358548`、`item/unsupported=1344`、`item/mapped-warning=12846`、`skill/simulated=9045`、`skill/live-applied=755`、`skill/inactive=4522`、`skill/out-of-combat=564`、`skill/unsupported=54`；**附魔告警行=0、仅附魔新增阻塞=0**。5 物品／2 技能硬拒绝、46 模板仅告警，全部是基础卡自身（`None` 同样失败）。
  - `npm.cmd test -- --run`：37 文件／102 测试通过；`build_actionaxis_plugin.sh --compile-only`：退出码 0，`ActionAxisEngine.dll` SHA256 `196C6FF3B44B62BD97693B9E6BCD5F460B36B3920D92028DFA1CCAAB33ED3D23`，`ActionAxisPlugin.dll` SHA256 `B9783DC6EDEAE1F3C67AE467815859C5B972A02F7ABBD6296FB231FE2316E154`。未部署。
  - `git diff --check`：退出码 0（仅 CRLF 提示）。
- 原生回放核对（已回填，详见 G07-03）：`D:\巴扎plus\CombatReplays\CombatReplays` 的 200 份回放，报告 `build/g07_replay_evidence.md`。226 个 DC=0 干净帧为 **2.00×**；3 个 DC=100 干净帧为 **3.00×**（Sawpike `b6ee828...` 224→672／280→840；Runic Great Axe Deadly `86f511...` 290→870）。Letter Opener `843f...` 以逐来源金额拆分为 2×。Radiant SR/FR=100 在 `66481e...`／`286fbe...` 中完全抑制 Slow/Freeze；DestroyImmunity 无 `CardDestroy` 样本，未观测。52 次 `CardEnchant` 均把 `None` 变为附魔，未发现战斗内重附魔/移除。
- 剩余风险与阻塞：
  - 基础卡 5 物品／2 技能仍未映射，另有 46 模板因缺 Ability/Aura 定义只告警（DATA）。具体为 5 个场上变形供体：Hacksaw、Potion Potion、Recycling Bin、[DEBUG] Recycling Bin、[DEBUG] Start of Fight Transform（G08）；2 个技能 `TTriggerOnCardFired`：Aggressive Mutations、Virulent Research（G03/G10）。并发组本轮已修复 Blender、Grabinator、Nanobot Orange、Wild Strength 及 Buddy System/Building Crescendo/Fairy Haze/Quick Defenses 的缺定义路径。它们不是附魔新增失败，但会阻止整卡进入完整模拟。
  - 随机光环目标的稳定选择使用确定性哈希而非原生 RNG；行为可重复，但未证明与原生随机序列一致。
  - `DamageCrit` 的 DC=0/100 已由回放核对为 2×/3×；DC=300（Flamberge）无回放样本。Radiant 50% 缩短为跨样本一致但未隔离；DestroyImmunity、战斗内重附魔/移除在 200 份回放中不可观测，不能写成已验证。`PercentHasteReduction`、零倍率、Heated/Chilled 槽位语义仍需回放/槽位规则继续核对。
  - 无 G07 完整逐事件对账、无游戏内 D 门禁；A/B 门禁当前通过，C/D 仍未完成。
- 下一条可执行动作：
  1. 保留 DC=0/100 回放回归；下一步取 Flamberge（DC=300）或 Deadly-Cutlass 暴击样本，确认 300 是 5×还是其它取整。
  2. G08 处理 Blender/Potion Potion/Hacksaw/Grabinator/Recycling Bin 的变形供体；G00 处理缺失 Ability/Aura 定义；G03/G05 处理剩余动态条件和 Both 人物目标。
  3. 冻结共享树后复跑 Gate A/B，并由 G12 做逐事件对账。
  锁释放：是（未提交、未部署）。

### 2026-10-25 01:09 · G09-02 · 真实回放校准 TransformDestroyed/ForceUse，并完成 94 模板全量分诊

- Owner / Reviewer / 分支 / 基线：Qoder（G09 续作）／待复核／`main`／`6eb032a` + 共享工作区未提交改动；未提交、未部署。
- 状态：IMPLEMENTED（G09 定向回归与 05:02 官方全量 C# 套件通过；G09 94 模板全量分诊完成。05:04 后并发组的 EnchantmentMapper 新测试使共享工作区变红，G09 仍 PASS；真实逐事件对账和游戏内验收未执行，不能写成 C/D 门禁 VERIFIED）。
- 范围：`docs/native-rule-task-index.csv` 中 G09 主责 94 个模板；新增完整夹具 `tools/actionaxis_plugin/tests/fixtures/native_g09_all.json`（94/94，SHA256 `C0350947116CA6835C7156591B94793475EDD9243322F0D7607B58A9B1D3C458`）与 `native_g09_revive.json`（Memento Mori／Luxury Tents／Ancient Locket，SHA256 `0978A59F38A5E070620792008A6A99A342F6C2EBA000E14D5012A3FACDBD84B9`）。
- 原生回放证据：
  - 文件 `CombatReplays/CombatReplays/30c3a4739f204e91981f10e8c6b10a8c.payload.mpack.gz`，SHA256 `e4a6740e9c2c88dd8f1b6d563773033e1f875aeffd41d8a6a0dd3c3823923e3f`；181 帧／9.05s。
  - Drone Crusher `68mrA3q`（模板 `3c7807ef-0993-4b33-8a34-441f7cde0666`）在 5.05／5.55／8.45／8.80s 四次 `CardTransformDestroyed`（action code 43）。每次紧接 `CombatSimEventCardTransformed`（event code 9）。
  - 变换创建**新 instanceId**、同 socket、新卡满冷却；旧实例 state `Transformed`，没有 `destroy` 行动事件。例：
    - 5.05s 目标 `WmLbYJg`（Parts Picker，Diamond）→ 新实例 `itm_ZNlrJcT` **Repair Drone**（Diamond），socket 2，Cooldown 2500/2500。
    - 5.55s `0_0jWwe`（Boom Boom Bot，Bronze）→ `itm_DNzl1W_` **Flame Jet Drone**（Bronze），socket 5，Cooldown 3500/3500。
    - 8.45s `dGMAzLQ`（Ice Bomb，Diamond）→ `itm_kKMPEVI` **Seeker Probe**（Diamond），socket 1，Cooldown 2500/2500。
    - 8.80s `itm_ZNlrJcT`（Repair Drone，Diamond）→ `itm_HQbWzy7` **Tugboat**（Diamond），socket 2，Cooldown 2501/2501。
  - 供体均为 **Small Drone**，tier 与被摧毁卡 tier 相同；目标原模板不限 Drone（Ice Bomb 也可被转化）。
  - 旧实例原生 `ECardState=Transformed(3)`，不是 `Destroyed`；全库没有 `CardDestroy`/`CardDisposed` 用于变换目标。引擎内部用 `Destroyed` 表示离场并抑制 destroy 事件，UI 历史以 `RemovedAt` 表示。
  - 原生顺序是 `CardTransformDestroyed(43)` → `CardTransformed(9)` → 同帧观察者；观察者（FlyingStart）能命中新实例。引擎已让新实例在出生帧进入目标候选集（`PendingItems`），帧边界才加入正式列表。
  - 同回放 5.0s：Drone Crusher 先 `CardDisable` `WmLbYJg`，同帧 `CardForceUse` 仍执行该卡的全部主动效果，且卡保持 Disabled，随后由 Repair Drone 修理。证明 ForceUse 是禁用规则的显式例外。
  - Bear Trap 回放 `e0a373306e4e421f950da0fc68a40674`：f61 / 3.05s ForceUse 时 `Ammo 1→0`，同次打出伤害+减速；其后 24 次 0 弹药 ForceUse 全部空转，冷却持续递减未重置。故 ForceUse 受 Ammo 门控、成功使用消耗 1 弹药，但不重置冷却。
  - Memento Mori 回放 `5a597c6e244545e4a76840f4ce0e1829`（SHA256 `13c6e480b9f9d1fcdec0bbff197109ab38d8e39179cbbea1506741946761f92d`）：11.0s 敌方 HP 536→1、PercentDamageReduction 0→100、记录 Heal +1，符合模板 `TActionPlayerReviveHeal` + `TActionPlayerModifyAttribute(PercentDamageReduction=100, 1s)`，不是全局复活。
  - 全库 200 份回放扫描：Abducted Cow、Fiery Rebirth、Luxury Tents **0 次**；Memento Mori 1 次；Ancient Locket 2 次；Life Preserver、Elixir、Artificial Heart、Eject、Sparring Partner 未出现。没有样本的卡只按模板 + 定向回归校准，不能写成逐事件原生证据。
- 根据回放修正的实现：
  1. `NativeTransform.ApplyDestroyed` 不再复用旧实例：旧实例标记离场并 `RemovedAt`，创建**新实例 ID**（`transform:<eventId>:<cardId>`）、同槽位、供体满冷却；新实例进入 `PendingItems`，但目标选择在本帧把 `PendingItems` 一并纳入（回放 5.05s 的 FlyingStart 已能选中新实例），到帧边界再正式加入。账本证据：新实例自己的 generation 仍从 1 开始；链式变换时旧实例 generation 递增，但新实例 generation 不跟随父级（f176 `itm_HQbWzy7` generation=1）。移除了“变形仍向行动者发 onCardDestroyed”的旧裁决；原生没有 destroy 行动事件。
  2. `BattleEngine.ForceUse` 允许 Disabled 目标（仍拒绝 Destroyed）；0 弹药时按回放空转，成功使用消耗 1 弹药；不重置冷却、不展开 Multicast。
  3. `Triggers.ResolvePayload` 保持“先收集本卡全部匹配触发，再执行效果”，确保 Memento/Fiery Rebirth 的计数器 Increment 不会在本帧抢跑其 `Custom_0==0` 前提；同卡按原生 Priority 稳定排序。
  4. `CardDisable` 现在先落 `Disabled`/disable 事件，再触发 `TTriggerOnBeforeCardDestroyed` 观察者（回放 Drone Crusher 禁用 Parts Picker、Ancient Locket 自禁用）：卡仍进入 Disabled，不产生 destroy；若摧毁前能力把它变成新实例，则按 TransformDestroyed 离场。Drone Crusher 因此可在同一事件链里把刚禁用的 Drone 换成新实例。
  5. 新增 Memento Mori、Luxury Tents、Ancient Locket 真实模板回归：Memento 首次死亡 Heal 1 + 1s 100% 减伤，第二击被减伤、第三击结束；Luxury Tents 按 MaxHealth 比例复活；Ancient Locket 被摧毁或禁用后给 2s 100% 减伤。
  6. `Triggers.DrainEventWhileDead`：死亡结算中的 `onBeforeDestroy` 观察者不再被 `HP<=0` 清空队列。Elixir of Immortality 的首次致死路径是“自禁用 → 摧毁前 → ReviveHeal 满血”，旧逻辑会直接结束。
  7. 技能事件源查找扩展到 `ctx.Skills`：技能自己造成的 `onHeal` / `onDamageDealt` 等事件现在能找到技能实例。Eject, Eject! 的“复活后治疗 → 禁用一张 Vehicle”因此可结算。
  8. `onPlayerDied` 一次性保护：Artificial Heart 与 Sparring Partner 的模板都没有 `Custom_0` 次数前提，但卡面分别写 “first time each fight” / “when you would be defeated”；引擎对这两张卡在同一场战斗只结算一次死亡触发，避免每次致死重复满血/翻倍。Artificial Heart 的模板 `Subject` 写成 `Opponent` 且 `HealAmount=0`，按卡面校准为 Self + 满血；Sparring Partner 的“敌方 +Gold”属于经济效果，按 OUT 跳过，不影响复活。
- G09 94 模板分诊（当前矩阵 `tmp_g09/native_mapping_report_current.tsv`，按 Gold/全部声明 tier、双方、附魔枚举）：
  - **83 个模板**当前所有声明入口为 mapped/simulated/inactive/live-applied（不代表逐事件原生对账通过）。
  - **4 个模板全入口 DATA（缺定义）**：
    - Bear Trap `d6ae8bd6-c9c0-4e80-bcfb-fb60025e6152`：`Tiers.*.AbilityIds` 引用 `"3"`，`Abilities` 只有 0–2；tooltip 只引用 ability.0–2。缺 AbilityId 3。
    - Hydrogen Tank `18100c8e-24db-4568-bd8c-4c8914557e11`：`Tiers.*.AuraIds=["3"]`，`Auras` 为空；tooltip 无 aura 引用。缺 Aura 3。
    - Seeker Probe `d208b6ac-fb07-466c-b895-625405bfc3e7`：`Tiers.*.AuraIds=["3"]`，`Auras` 为空；缺 Aura 3。
    - The Big One `a7910370-4540-4bb7-95cc-5b4b5eeb43ac`：Gold `AuraIds=["2","3","6"]`、Diamond `["2","4","6"]`，`Auras` 只有 3/4/9；缺 Aura 2、6。
  - **3 个模板部分 DATA（部分品质/入口受阻）**：
    - Firecrackers `aadb8722-9ff1-46cc-95e1-893f7180e191`、[DEBUG] Firecrackers：Silver/Gold 已映射，Diamond `AuraIds=["3"]` 但 `Auras` 只有 9，缺 Aura 3。
    - [DEBUG] Repeatable Quest `e19e88e3-9296-4bac-b087-346f1b6d906e`：Silver/Gold 已映射，Diamond `AbilityIds` 含 `"4"`，`Abilities` 只有 0–3，缺 AbilityId 4。
  - **4 个模板 OUT（战斗外／debug-only）**：
    - Crash Site Expedition Ticket `5bd0a69d-420c-4cd0-93ec-07582c688a5b`、Temple Expedition Ticket `77c8ef2d-62c8-4214-993d-648915c93e83`：`TTriggerOr(OnEncounterSelected)` + Destroy Self，Aura 目标 `SelectionSet`、属性 `BuyPrice`；tooltip 为 Day 6/9 远征。OUT。
    - [DEBUG] Peacewrought `f4a7f5d3-73d6-4f08-ab70-6b5ee3309005`：`Abilities.4` 为 `TTriggerOnCardPerformedDestruction` → `TActionGameSpawnCards`（debug-only 战斗内生成）；正常 Peacewrought 已映射。OUT。
    - [DEBUG] Destructible Skill：矩阵为 out-of-combat。OUT。
- 新增测试（`G09LifecycleTests.Run` 现为 27 组，含全量夹具）：
  - `DroneCrusherUsesTargetTierAndFreshCooldown`：同回放的 tier 继承与新实例满冷却。
  - `TransformDestroyedNewInstanceStartsAtGenerationOne`：回放 f176 的链式变换；旧实例 generation=2，新实例自己的 generation 仍为 1。
  - `DroneCrusherMediumAndLargeSpawnLimitCopies`：Medium/Large 目标按 `SpawnContext.Limit` 生成 2/3 个不同 ID 的同品质新实例，全部满冷却。
  - `SameFrameObserverSeesTransformSpawn`：用真实 Leverage Momentum 模板验证 `TTriggerOnBeforeCardDestroyed` 观察者在出生帧选中 `PendingItems` 里的新 Drone（回放 5.05s FlyingStart 同一事实）。
  - `DisableByDroneCrusherTransformsAfterDisableEvent`：先记录 disable 事件，再由摧毁前观察者把目标换成新 Drone；事件顺序与回放 f100→f101 一致。
  - `RepairDroneForceUseRepairsTwoRandomItems`：真实 Diamond Repair Drone 一次强制使用修好两张随机手牌，且不重置冷却（回放 f176）。
  - `ForceUseConsumesAmmoKeepsCooldown`：成功使用扣 1 弹药，0 弹药再使用空转，冷却不变。
  - `MementoMoriSurvivesAtOneAndOnlyOnce`、`LuxuryTentsRevivesForPercentMaxHealth`、`AncientLocketGrantsInvulnerabilityOnDestruction`、`AncientLocketTriggersOnDisable`。
  - `LifePreserverRevivesForFlatHeal`：真实 Bronze Life Preserver 首次致死固定回复 200，第二次结束。
  - `ElixirOfImmortalityDisablesThenRevives`：真实 Legendary Elixir 首次致死自禁用 → 摧毁前 ReviveHeal 满血，第二次结束。
  - `ArtificialHeartHealsFullAndLosesMaxHp`：首次致死清毒/清烧、MaxHealth -10%、按新上限满血，且只触发一次。
  - `EjectEjectRevivesToHalfAndDisablesVehicle`：真实 Diamond 技能按 50% MaxHealth 复活，并触发 onHeal 禁用一张 Vehicle。
  - `SparringPartnerDoublesMaxHealthOnDeath`：真实 Legendary 技能首次致死 MaxHealth +100% 并满血，第二次结束；敌方 +Gold 为 OUT。
  - `AllG09TemplatesAreTriaged`：加载 `native_g09_all.json` 94/94，逐模板逐声明 tier 调用 item/skill 映射，并断言分诊结果固定为 83 全映射／4 全入口 DATA／3 部分 DATA／4 OUT；除这些已知项外任何新增拒绝都失败。
  - 既有 `AbductedCowTransformsInsteadOfDying`／`DroneCrusherTransformsTriggerTargetNotItself` 已改为断言旧实例离场、新实例 ID、原槽位与新冷却。
- 验证：
  - 定向 `engine + TestHarness + G09LifecycleTests`：编译退出码 0，`PASS G09Lifecycle`，`OK`。
  - 用同一份 engine/tests/plugin 源文件独立编译 `tmp_g09/EngineTests_g09.exe`（避开共享 PDB 锁）：编译退出码 0；运行退出码 0，`PASS G09Lifecycle`，整包 `OK`（全部套件通过）。这是一次完整 C# 回归，但输出文件不是官方 `build/EngineTests.exe`。
  - 定向 `engine + TestHarness + G09LifecycleTests`（最终源码）再次编译退出码 0、运行退出码 0，`PASS G09Lifecycle`／`OK`（27 组）。
  - 官方 `bash tools/actionaxis_plugin/compile_tests.sh`（05:02 源码快照）：退出码 0，`PASS G09Lifecycle`，整包 `OK`。
  - 05:05／05:06 复跑官方脚本时，G09 仍 `PASS G09Lifecycle`；整包仅被并发组 05:04 正在改写的 `EnchantmentMapperTests.WarpDriveHeavyUsesSharedAllHandsTarget`（expected 2 effects、got 0）阻断。05:13 起并发组 `G01FullRulesTests.cs` 又处于未落盘状态（`ItemAttributes.Get(CardDef)` 编译错误），全量编译被阻断；两者均与本轮 G09 改动无关。
  - 当前矩阵：94 模板／22764 行；上述 83／4／3／4 分类；报告 `tmp_g09/native_mapping_report_current.tsv`（临时，不入库）。
  - `npm.cmd --prefix d:\巴扎plus test -- --run`：退出码 0，37 文件／102 测试全绿。
  - `bash tools/build_actionaxis_plugin.sh --compile-only`（05:03 G09 最终源码快照）：退出码 0，仅既有 Unity CS0618 警告；`ActionAxisEngine.dll` SHA256 `525902A90C1BBBB37BEA4DA93B105AAF4D42F30C00912073E89E15855F9E0BC1`、`ActionAxisPlugin.dll` SHA256 `E3CEBC9688E8492C00B9D17CF17CE669B91D689C39C0F5AB37F7336976825E7C`。未部署。
  - 未执行：真实逐事件比较、游戏内部署。
- 剩余风险与阻塞：
  - Firecrackers Diamond、Hydrogen Tank、Seeker Probe、The Big One、Bear Trap、[DEBUG] Repeatable Quest 的缺 Aura/Ability 定义仍是 DATA；查证路径已写，禁止补造数值或光环。
  - ForceUse 已按回放验证“禁用仍执行、0 弹药空转、成功使用扣 1 弹药、不重置冷却”；Destroyed 与目标条件变化的拒绝路径仍缺样本。
  - 新实例已通过 `NativeItemsForTarget/PositionalTargets` 纳入 PendingItems，出生帧可被同一事件的观察者选中；但它仍要到帧边界才进入 `ctx.Items` 的正式遍历，极少数直接遍历 `ctx.Items` 的路径仍可能差一帧，需 G12 对账。
  - TransformDestroyed 多副本（Medium/Large 目标生成 2/3 个 Drone）已按 `SpawnContext.Limit` 回归并通过，但仍没有回放样本可逐事件对账；当前要求连续空位，空间不足时保留原物品并告警。
  - CardDisable 帧时序（2026-10-25 深夜修正，ebde2082 帧 79/80 实证）：原生禁用触发的 before-destroy 观察者**下一帧**结算（Boosted Saucer 的充能在禁用后 1 帧落地，与 Drone Crusher 现象同根）。引擎已改为 NativeMechanics 下禁用后不排空队列、`onCardDisabled` 排在 `onBeforeDestroy` 之后由下一帧顺序结算；死亡结算中的“自禁用→摧毁前→复活”链保持同步排空。同时修复禁用持久性：`Synchronize` 此前每帧用派生层 `CooldownDisabled=0` 覆盖 `item.Disabled`，战斗内禁用会被清除、冷却继续走；现以 `CombatDisabled` 持久标志与光环层取或（原生禁用后冷却停走且不再出手）。`G09LifecycleTests` 的 Drone Crusher/Ancient Locket 用例已按“下一帧结算”改写。同一张卡先禁用、后被真正摧毁时是否再次触发 before-destroy 仍需专门回放。
  - Artificial Heart 模板把 `TTriggerOnPlayerDied.Subject` 写成 `Opponent`、`HealAmount=0` 且没有 `Custom_0` 次数前提；Sparring Partner 也没有次数前提。引擎按卡面“首次/被击败时”校准为 Self、满血和每场一次，但没有真实回放样本，属于模板冲突下的语义校准，不是原生逐事件证据。
  - Elixir of Immortality 的“死亡中禁用→摧毁前→复活”已由引擎事件链验证；其 `onFightEnded` 永久摧毁仍依赖战斗结束路径，未在真实回放中出现。
  - 技能自身的 `onHeal/onDamageDealt` 事件源已能解析到 `ctx.Skills`；这只影响技能触发链，仍需 G12 对账确认没有改变既有技能顺序。
  - 整包 A/B/C/D 门禁仍不能标全绿；G09 只有模板级与定向回归，不是原生逐事件 VERIFIED。
- 下一条可执行动作：待并发 G03/G06 恢复整包绿色后复跑 `bash tools/actionaxis_plugin/compile_tests.sh`；G12 用 30c3 回放按 §8.4 对账 5.05/5.55/8.45/8.80s 的 TransformDestroyed，以及 5.0s Disable→ForceUse→Repair 序列。锁释放：是（未提交、未部署）。

### 2026-10-25 01:25 · G10-CONT2 · 玩家侧账本、低层燃烧衰减、Shard 与 200 回放覆盖

- Owner / Reviewer / 分支 / 基线：Qoder（G10 续）／待集成人复核／main 共享工作区（未提交）／`6eb032a`。
- 状态：IMPLEMENTED（G10 三个套件通过；并发组修复后整包 C# 全量复跑 `OK`；未部署）。
- 范围：补齐 20:17/22:25 两轮 G10 遗留：① 同回放玩家观测侧原生账本；② 低层燃烧在无实际伤害时的衰减；③ Shard of Obsidian 的陈旧 `AbilityId` 回归；④ 全量 `CombatReplays` 回放扫描与 G10 卡/动作覆盖。不是只取一张卡或一个技能。
- 完成内容：
  1. 玩家侧账本：新增 `native_g10_replay_player.json` 与 `ReadsRealG10PlayerReplayLedger`。玩家开局 `Health=400`，`HealthMax/Shield/Regen` 在 Spawn/Opening 侧车均缺失，夹具不伪造；4.0s 直伤 30、末帧 TotalDamage 402（直伤 270＋毒 132）、Overkill 2、NetHpLoss 400、HP 0，426 帧逐帧伤害恒等式通过。
  2. 燃烧衰减：`BattleEngine.TickNativeDots` 只有在该 Burn tick 实际改变 HP 或 Shield 后才衰减。证据为 084bd840：1 层 Burn 在 50 盾后持续 6s 不衰减；2 层 Burn 打 1 点盾后衰减为 1。新增 `BurnDoesNotDecayWhenNoDamageWasDealt`；修复后原生对照的 health value-mismatch 从 25 降到 18。
  3. Shard of Obsidian：DB 5.0.0 的 `AbilityIds=["0","1"]` 与 `Abilities={0}` 不一致；当前映射器对缺失 id 采取“保留其余能力＋明确 warning”的策略，新增 `ShardOfObsidianIgnoresStaleAbilityId`，验证只结算 Ability 0 的 5 伤害与 5 吸血，不再整卡 fail-closed。
  4. 全量回放：扫描用户提供的 `CombatReplays/CombatReplays` 共 200 份回放；17 份含 G10 主责卡（16 份该目录＋`084bd840`），覆盖 Trained Spider、Soldering Gun、Blow Torch、Rune Axe、Bandages、Walter Cooler、Shard of Obsidian；六类 `PlayerDamage/Burn/Poison/Shield/Heal/Regen` 动作均出现。摘要 `captures/diagnostics/g10/replay_corpus.json`，校验 `tools/test_g10_replay_corpus.py` 6/6。
  5. 来源归因：`captures/diagnostics/g10/source_attribution_084.json` 把 13 次直伤全部唯一定位到原生 `PlayerDamage` 来源（13/13 verified），22 次 Burn/Poison/Shield 应用定位到来源卡；30 次 DoT 扣血 tick 全部标为 `unavailable-native-no-tick-source`，不把应用来源冒充 tick 份额。校验 `tools/test_g10_source_attribution.py` 4/4。
- 原生证据：玩家夹具 SHA256 `0A84F7B15EC021050A62E3BBEA3F77DCA494D6B60853222B7B42C96488230170`；Shard 夹具 `CA13E27FDF802122D24EF7C5AB30E1347AA84996A8A547F3BDC2A61C7B35A95A`；回放源 SHA256 `A2096BCC1F8EB31CDB274E2948C65271F08542BB989A986EBFE683C23ED06C08`。原生健康调整仍无逐 tick 来源字段；玩家 `HealthMax` 仍 UNKNOWN。
- 改动文件：`engine/Battle.cs`（仅燃烧衰减条件）、`tests/G10DamageLedgerTests.cs`、`tests/NativeCombatReplayTests.cs`、`tests/fixtures/native_g10_replay_player.json`、`tests/fixtures/native_g10_shard.json`、`tools/test_g10_replay_corpus.py`、`tools/test_g10_source_attribution.py`、`captures/diagnostics/g10/replay_corpus.json`、`captures/diagnostics/g10/source_attribution_084.json`、`captures/diagnostics/g10/README.md`、本文件。未认领其它组共享文件差异。
- 验证：G10 子集 `PASS NativeCombatReplay`、`PASS G10DamageLedger`、`PASS G10TaskIndex` → `OK`；并发组修复后按同一源集全量编译并运行 `EngineTests.exe` → 退出码 0，全部套件 `OK`（含 G10 三套件）；`python -m unittest tools.test_native_replay_ledger tools.test_native_replay_compare tools.test_rule_workplan tools.test_g10_replay_corpus tools.test_g10_source_attribution -q` → 25/25；`npm.cmd test -- --run` → 37 文件／102 测试全绿；`git diff --check` → 退出码 0。未构建/覆盖 DLL，未部署，无提交。
- 剩余风险与阻塞：① 原生载荷不提供逐 tick 伤害来源，多来源 DoT 份额只能由模拟器按池推导；应用来源可由 `PlayerBurnApply/PoisonApply` 事件核对，tick 份额不能伪造。② 玩家 `HealthMax/Shield/Regen` 缺失，本轮只对账伤害/过量/净失血，不对账玩家治疗与最大生命。③ `084bd840` 的变形目标顺序/冷却仍与原生不同（Virus 在 8s 先选中 slot2，原生先 slot4），导致后续 Burn/Poison 层数与道具冷却 DIFF；属 G08/G12，不在 G10 账本口径内。④ 56 个模板仍有缺失 AbilityIds/AuraIds，保持 DATA 清单，不能凭空补定义。⑤ 模拟器 UI 已显示净失血/承伤/回复/护盾/过量；本轮未重启游戏做视觉验收。
- 下一条可执行动作：G08/G12 先用同一 084 回放修正 Virus 变形目标顺序与冷却，再复跑 `tools/native_replay_compare.py` 检查 health/effects 通道；G12 用侧车补玩家 `HealthMax` 后再对账玩家治疗。部署仍需单独授权。锁释放：是（未提交、未部署）。

### 2026-10-25 · G07-03 · 200 份真实回放核对：DamageCrit=3×、Radiant 完全抑制、战斗内附魔存在

- Owner / Reviewer / 分支 / 基线：Qoder（G07 续作）／独立回放核对任务／`main` 共享工作树／未提交、未部署。
- 状态：IMPLEMENTED＋关键路径回放核对；不是 VERIFIED（未做完整逐事件比较器，部分样本不可观测）。
- 回放语料：`D:\巴扎plus\CombatReplays\CombatReplays` 全部 200 份 `*.payload.mpack.gz`。报告 `tools/actionaxis_plugin/build/g07_replay_evidence.md`，SHA256 `707F47A5DA649B2ACDA471AFA98BE284D3819041F3B6F5281982B5859ADC3467`。
- 工具限制：官方 `native_replay_ledger.py` 在无 OpeningMessage 侧车时对 113/200 回放的未知 side 开场实例抛 `KeyError: 'unknown'`；核对使用临时补丁副本跳过 unknown side，仓库工具未改。玩家开场卡与 1148 个技能实例仍为 UNKNOWN，敌方开场、事件和 healthChanges 可用。
- DamageCrit 证据与更正：
  - 264 个“单来源、无护盾、无减伤”干净暴击帧中，226 个 DC=0 帧精确为 **2.00×**；3 个 DC=100 帧精确为 **3.00×**。
  - Sawpike `b6ee82840ea74f8a80b70d43c1916e5a`（SHA256 `38eef138...`）实例 `T8DeM8v`：11.05s `224→672`、11.85s `280→840`，均 `isCrit=true`、无护盾/减伤。
  - Runic Great Axe Deadly `86f5118700d543ecbb278fafc813f413`（SHA256 `926e43ad...`）5.95s `290→870`，同样 3×。
  - Letter Opener `843f170995a54b32ad62abf91f48147b` 的 DC=0 暴击以逐来源金额拆分为 2×（52→104、76→152、103→206）。
  - 结论：`critMultiplier = 基础 CritMultiplier + DamageCrit/100`（DC=0→2×，DC=100→3×）；G07-01 的 `2×(1+DC/100)` 推断被回放否定。代码 `Effects.ApplyDamage`、测试 `DeadlyDamageCritAddsToCritMultiplier`（40→120）与文档已更正。
  - `Deadly` 并非统一加 `DamageCrit`：31 个 Deadly 开场物品中 30 个 DC=0；Brass Knuckles Deadly 加 +50 `CritChance`，Cutlass/Runic Deadly 才加 +100 `DamageCrit`。引擎按所选附魔定义逐卡映射，未硬编码 Deadly。
  - Flamberge（DC=300）在 200 份回放中 0 次出现；Cutlass 无暴击样本，DC=300/ Cutlass 交互仍 UNKNOWN。
- Radiant 证据：
  - SR=FR=100 的 Skyterror `cGo7KMN`（`66481e...`）在同 context 其他卡获得 500–1000ms Slow 时没有任何 Slow 更新；The Core `iPpfwZv`（`286fbe...`）被 `CardFreeze` 后没有 Freeze 状态、冷却继续走。**100% 抗性为完全抑制**。
  - 50% 抗性在 `ab8439...` 中表现为 500ms vs 对照 1000ms，方向一致但非隔离样本。
  - 8 个 DI=1 Radiant 实例存在，但 200 份回放中 `CardDestroy` 计数为 0，`DestroyImmunity` 的拦截无法观测；仍由本地行为测试证明，不能写成回放已验证。
- 战斗内附魔：
  - 52 次 `CardEnchant`／52 个 `CombatSimEventCardEnchanted`，分布在 22 场；36 个敌方开场物品、16 个未知 side 实例；所有目标开场均未附魔，最终状态从 `None` 变为具体附魔。
  - 0 次 `CardEnchantRandom`、0 次 `CardEnchantRemove`；0 个已附魔卡被重附魔/移除。**战斗内重附魔/移除语义在该语料中不可观测**，维持 fail-closed。
- 验证：
  - 修正后隔离 `PASS G07Enchantment` / `OK`（34 个用例，含 40→120）；整包绿色快照 `build/g07_full_final4.txt` 为 67/67。并发 G04/G08 后续编辑使最新整包快照 `g07_full_final5.txt` 出现 2 个非 G07 失败（Friend of the Sea skill 查找、GreenScreen 供体 ID），G07 用例仍通过。
  - 回放证据报告与上列 SHA256；未运行完整逐事件比较器，matched/missing/extra/UNKNOWN 未量化。
  - 构建：`ActionAxisEngine.dll` SHA256 `196C6FF3B44B62BD97693B9E6BCD5F460B36B3920D92028DFA1CCAAB33ED3D23`，`ActionAxisPlugin.dll` SHA256 `B9783DC6EDEAE1F3C67AE467815859C5B972A02F7ABBD6296FB231FE2316E154`；未部署。
- 剩余风险：
  - DC=300、Cutlass Deadly 暴击、50% 抗性单样本、DestroyImmunity、战斗内重附魔/移除仍需更多回放。
  - 玩家侧无 OpeningMessage 侧车，无法把玩家卡/技能与敌方模板对到同一开场棋盘。
  - 回放事件 payload 不直接携带附魔类型，附魔类型由最终实例状态推断；不能当作逐事件字段证据。
- 下一条可执行动作：取 Flamberge/Cutlass 暴击样本确认 DC=300；取含 `CardDestroy` 打到 Radiant 的样本确认免疫；G12 用完整开场重建跑 G07 比较器。

### 2026-10-25 · G12-02 · 开场冷却注入修正与 520 份全量对账

- 触发：复核指出“回放夹具里有开场剩余冷却，重建时没有塞进引擎”。核对结果：引擎确实忽略 `Cooldown`；同时发现更深的缺陷——旧夹具的 `opening` 属性读取的是战斗结束后的可变实例，3250/3950 等是最后观测值，不是开场值。
- 修复：
  - `merge_snapshot` 后、战斗帧处理前冻结 `opening_instances`；`serialize_opening_item` 只读冻结值；帧 0 的 `PreviousValue` 只补缺失字段。
  - `LoadoutItem.StartCooldown` + `CardMapper` + `BattleEngine.InitBattle` 注入开场剩余冷却；`StartReady` 仍优先为 0。
  - 新增 `G12OpeningCooldownTests`：映射、`InitBattle` 初始值、StartReady 覆盖。
- 开场数据核对：
  - 365 份 BPP `battle_snapshots` 中 4516 个带冷却物品，`Cooldown < CooldownMax` 为 **0**；开场都是满冷却。
  - 520 份全量账本中 5912 个参战开场物品，`partialOpeningCooldown=0`；407 个无 `Cooldown` 的是无冷却被动/特殊物品。
  - 结论：Core/Cog 的冷却差 **不是漏注入开场冷却**。Core 的飞行目标、Cog/Icy 的加速/冻结目标、Drowsdrop 的减速目标都是 `TTargetCardRandom`；回放载荷没有原生随机种子，只能标 UNVERIFIED，不能算 G02 已对账通过。
- 全量对账：`tools/native_replay_batch.py` 跑 520 份（游戏目录 320 + `D:\巴扎plus\CombatReplays` 200）：
  - 145 份可重建并比较，375 份阻塞：200 份无 `OpeningMessage` 侧车，175 份旧回放模板在当前 `GameData 5.0.0` 不存在。
  - 145 份全部 DIFF；聚合 matched 495613／unverified 2191／postEnd 54967／missing 180925／extra 39884／value-mismatch 202008／time-mismatch 10／unknown 4294。
  - 事件覆盖：177665 条战斗事件；`CardTransformed` 29 次、回退 29 次（17 份样本）；`EffectTriggered` 仍为 0；沙暴事件 3623 次（41 份样本）；`randomTarget` 8347 次。
  - 证据：`captures/diagnostics/g12/batch_summary.json`、`batch_replays.csv`、`batch_top_groups.csv`、`coverage_all.json`。
- 固定样本更新（已含随机目标标记）：
  - `9bbabba4`：matched 5219／unverified 16／postEnd 0／missing 1134／extra 369／value-mismatch 3757／time 0／unknown 18；账本 `92edd0c6…bb89`，模拟账本 `c77cb5ae…8cc2`。
  - `084bd840`：matched 3116／unverified 0／postEnd 36／missing 416／extra 335／value-mismatch 230／time 7／unknown 30；账本 `334ebc4c…ec3`，模拟账本 `93995052…4a90`。
- 验证：
  - `G12OpeningCooldown` PASS、`G12UiHistory` PASS；前端 102 项通过；Python 15 项通过。
  - 全量 C# 当前共享工作树仍有其它组并发断言（最终复跑仅 `G05FullAura` 1 个：`NanobotOrangeBuildsDamageFromOwnAura`），Gate A 不能标全绿；G12 两个测试均 PASS，发布前必须同一提交复跑。
  - `--compile-only` 退出码 0，仅既有 `CS0618` 警告；源 DLL Engine `2bcabcdd…f129`（MVID `b6a1e7de-…`）、Plugin `b30b87c8…6b81`（MVID `0f464148-…`）；已安装 Engine `ac2f2fa6…c8d4`（MVID `6bc37297-…`）、Plugin `8c939512…351c`（MVID `da2066d8-…`）；日志加载的 engine MVID 与已安装一致、与本次构建不同。`DEPLOY=未执行`。
- 剩余风险：145 份比较仍全部 DIFF；随机目标分支只能 UNVERIFIED；175 份旧模板是 DATA 阻塞；200 份无侧车不能重建；被动 `EffectTriggered` 全量仍无样本。已确认的沙暴样本可用于下一步 G11/G12 伤害时间序列核对。
- 下一条可执行动作：先按 `batch_top_groups.csv` 把确定性差异（非 randomTarget）拆给规则族；随机分支另列 UNVERIFIED，不强行追平。沙暴样本从 `coverage_all.json` 的 41 份中选取。部署与游戏内 Gate D 仍需用户另行授权。锁释放：是（未提交、未部署）。

### 2026-10-25 · G11-03 · 默认沙暴回放反推、36/36 入口验收与整包绿灯

- Owner / Reviewer / 分支 / 基线提交或补丁哈希：Qoder（G11 本轮续作）／待复核／`main`／`6eb032a` + 共享工作区未提交改动；本轮不提交、不部署。
- 状态：IMPLEMENTED（入口＋默认沙暴）＋ VERIFIED（G11 单测、36 模板矩阵、针对性混合卡矩阵、04:25 整包快照 `OK`；04:31 当前源码仅 G08 并发行失败）；卡池运行态与逐事件原生对账仍未执行。
- 范围：36 个 G11 主责模板；默认沙暴倒计时/tick/伤害；`TTriggerOnEncounterSelected` 与混合卡；4 项 DATA、13 项 OUT+DATA、16 项 OUT 的最终入口状态；不在本轮实现卡池候选集、G05 读金币/收入光环、G01/G05 槽位与 G06 Base Rage。
- 回放证据：`CombatReplays/CombatReplays` 共 200 份 `.payload.mpack.gz`。54 份出现 `SandstormCountdownStarted`（帧 400，20.0s），23 份出现 `SandstormStarted`（帧 600，30.0s）与实际伤害。23/23 的 tick 间隔与每 tick 伤害完全吻合（把同一帧的护盾/生命拆分求和）：5 帧×5、4 帧×5、3 帧起；前 10 tick 伤害 1，之后每 tick +2。锚点（干净回放 `06158435ccdc4c7a9c631d144da6ad31`）：35.0s=41、38.0s=81、41.0s=121、42.5s=141。
- 反推参数：`CountdownStartTime=20.0s`、`CountdownDuration=10.0s`、`BaseTickRate=5` 帧、`TickRateDecrement=1`、`TickRateFloor=3`、`BaseDamage=1`、`AdditiveScalar=2`。
- 完成内容：
  1. `BattleConfig` 新增 7 个沙暴默认参数；`Battle.TickSandstorm` 在原生机制下自动 20s 倒计时、30s 开始并逐 tick 结算；卡牌 `TActionCardBeginSandstorm` 可提前开始，默认 30s 不再重复开始。
  2. `Effects.StartSandstorm` 统一卡牌开始/默认开始，发 `beginSandstorm` 动作事件并派发全局 `onSandstorm`；`Triggers.GlobalMatches` 允许无来源的全场触发器，带 Subject/Source/Target 过滤的仍 fail-closed。
  3. 沙暴伤害按护盾→生命，标记 `dotTick/sandstorm`；不计入玩家输出、不触发 `onDamageDealt/Taken`，账本计入 `OtherHpChange`。回放 `IsDamageReduced` 均为 false，本轮不应用 `PercentDamageReduction`，保留待后续回放复核。
  4. `SkillMapper` 对“纯场外/空壳技能缺 AuraId”按 DATA 告警跳过，不再回退整卡；有战斗触发器的技能仍 fail-closed。因此 36/36 G11 入口 mapped。
  5. 新增 36 模板真实夹具与全量入口测试；新增默认沙暴回放节奏回归。
- 改动文件（只认领 G11 差异；共享文件同时含其它组并发改动）：`engine/SkillMapper.cs`、`engine/Effects.cs`、`engine/Battle.cs`、`engine/BattleContext.cs`、`engine/Types.cs`、`engine/NativeCardAbilityMapper.cs`、`engine/Triggers.cs`、`tests/G11SandstormTests.cs`、`tests/G11CoverageTests.cs`、`tests/fixtures/native_g11_sandstorm.json`、`tests/fixtures/native_g11_all.json`、`tests/NativeRuntimeRulesTests.cs`、`tests/Program.cs`、本文件。
- 新增测试：`G11SandstormTests` 7 组（沙暴动作、默认节奏、Windmill MaxHp 一次、EncounterSelected、Peacewrought、Cash Register Golden、经济复合动作）；`G11CoverageTests` 3 组（27 个物品、9 个技能、4 个关联卡全量入口）。`Program.cs` 注册 `G11Sandstorm`／`G11Coverage`。
- 验证：
  - 整包：2026-10-25 04:25 快照全部套件 PASS／`OK`；04:31 当前源码可编译，`PASS G11Sandstorm`、`PASS G11Coverage`，唯一失败为 `G08TransformTests.GreenScreenCopiesLeftMediumWithEnchant`（期望 `3172cda6…`，实际 `62c10400…`），不在 G11 路径，由 G08 并发组处理。
  - G11 过滤矩阵（36 模板／5574 行）：item `mapped` 4620、`mapped-warning` 684；skill `inactive` 156、`out-of-combat` 114、`unsupported` 0 → **36/36 入口 mapped**；684 行告警对应 3 个物品缺 AbilityId，12 行由 unsupported 转入 inactive 对应 `[DEBUG] Stove` 缺 AuraId；4 项 DATA 语义仍未实现。
  - 针对性混合卡矩阵：Leather Jacket 252/252 mapped；Peacewrought 390/390 mapped；`[DEBUG] Peacewrought` 390/390 mapped（调试 `TActionGameSpawnCards` 已跳过）；Hunker Down 24 simulated／6 inactive；全部无 unsupported。
- 剩余风险与阻塞：
  - 4 项 DATA 定义：Chunk of Lead（Ability 0）、Mysterious Gift（Ability 1–5）、`[DEBUG] Fortune Cookie`（Ability 1）、`[DEBUG] Stove`（Aura 0）；入口已告警跳过，定义语义仍缺。G00/G11；新版本 DB 或运行时动态生成证据。
  - 13 项 OUT+DATA 场外卡池：入口 mapped 只因场外触发器跳过，候选集/权重/运行时上下文仍缺。G11/G00；用户扩大范围 + 同版本运行时样本。
  - 16 项 OUT：基础为空/出售经济/内部占位；G11；版本变化后复核。
  - `[DEBUG] Peacewrought` 战斗内 `TActionGameSpawnCards` 明确跳过，保持 DEBUG 例外。G11/G03。
  - 读取 Gold/Income 的战斗缩放光环（Ring King Gauntlets、Cash Deposits、Commercial Zoning、Gold Garnish、Slingshot Golden）：G05。
  - Occupying/Heated-Chilled：G01/G05；`Base Rage Effect`：G06。
  - 沙暴数值序列已 23/23 对齐；事件级（同一帧其它效果顺序）仍未逐事件对账，归 G12。
- 下一条可执行动作：
  1. G12 用含沙暴回放做逐事件比较，核对同一帧内其它效果与沙暴伤害的顺序。
  2. G00/G11 新版本 DB 重读 4 项缺定义；若补齐，重跑 `G11Coverage` 并把 mapped-warning 转为 mapped。
  3. G00/G11 采集场外卡池运行时序列；在此之前不猜候选。
  4. G05 处理读取 Gold/Income 的战斗缩放光环；G01/G05 处理槽位；G06 处理 Base Rage。
- 锁释放：是（未提交、未部署；本轮只改共享工作区文件与测试，不持有独占锁）。

### 2026-10-25 · G06-03 · G06 全量 136 模板映射、怒气／暴怒规则与 200 份回放取证

- Owner / Reviewer / 分支 / 基线：Qoder（G06 本轮全量续作）／待复核／`main`／`6eb032a` + 共享工作区未提交改动；本轮不提交、不部署。
- 状态：IMPLEMENTED（G06 全部 136 个主责模板在当前矩阵中 **0 条 unsupported**；G06 定向套件通过；仍非 VERIFIED：7 条原生缺定义只做 warning 跳过（4 条 mapped-warning + 3 条保留主能力的 simulated），Toxic Shield／Wild Boar 没有正回放，未做部署后验收）。
- 范围：`docs/native-rule-task-index.csv` 的 136 条 `primary_group=G06` 模板，全部品质／附魔／双方／template-live-mixed 场景；四个种子 Warpaint／Vengeful Sigil／Burning Shield／Toxic Shield；Wild Boar 物品与怪物（G00 种子、G06 行为）；额外覆盖 Shortened／Extended Rage、PercentDamageReduction、HealthMax 触发、缺定义降级和经济触发 OUT。
- 全量映射结果（`native_mapping_report_g06final.tsv`，35,334 行；摘要 `captures/diagnostics/g06/matrix_summary.json`）：
  - `mapped` 33,228；`mapped-warning` 1,176；`simulated` 460；`inactive` 288；`out-of-combat` 168；`live-applied` 14；`unsupported` **0**。
  - 136/136 模板均至少有一条有效入口；4 条 `mapped-warning` 为原生缺定义：Bushel（缺 aura 3）、Calcinator（Diamond 缺 ability 1）、Outlands Terror（缺 aura 0）、Tea Set（缺 aura 3）。Adrenaline Rush／Feral Alliance／Still Focus 的原生 ability 1 缺失也已跳过，保留 ability 0 并记录 `CardDef.MappingWarnings`；不猜数值。
  - 经济／场外：Healthy Tip（Gold 触发）与 Red Envelope（CardSelected）归 `out-of-combat`；Calcinator／Retort 的 day-start Gold 动作被跳过，保留战斗部分。
- 完成内容（全量规则）：
  1. `TActionPlayerModifyAttribute` 增加 `Rage`（Add/Subtract/Set）和保留 `PercentDamageReduction`；`Effects` 对 Rage 变化发 `onPlayerAttributeChanged` 并可触发暴怒。
  2. `TAuraActionPlayerModifyAttribute` 允许 `Subtract`／`Multiply`；Shortened Rage（−1000 ms）、Extended Rage（+1000 ms）、[DEBUG] Rage Extender 的 EnragedDurationMax 通过运行时人物光环生效；7 份回放确认 4000／6000 ms。
  3. `TTriggerOnPlayerAttributeChanged` 的 `HealthMax` Gain/Loss 可触发；HealthMax 变化继续只推动 HP，不计治疗；Hammock／Tea Set 映射。
  4. `NativeCardAbilityMapper`／`SkillMapper` 遇到原生缺失的 AbilityId/AuraId 时跳过该条并写入 `CardDef.MappingWarnings`，保留同卡其余战斗部分；缺失值不合成。
  5. `CardDef` 增加 `MappingWarnings` 并在 `Clone` 保留；`NativeCoverageRunner` 将缺定义告警归为 mapped-warning，不伪装成 unsupported。
  6. Base Rage Effect（原生 `4472da8a-26a3-4e10-bd9a-e93c2e22f19c`）：暴怒时清本方 Slow／Freeze；暴怒期间本场 `CooldownMax × 0.9`（10% 冷却缩减）；`TickEnrage` 结束时恢复原 `CooldownMax` 并保留冷却进度。G02 Dryad 首次出手期望从 4.1s 调整为 3.7s（该 10% 原生效果的直接后果）。
  7. 保留 G06-02 的同卡前置快照、双方英雄身份 `EnemyHero` 与纯模板／实时快照分层；新增 `G06FullCoverageTests` 对 136 模板跑完整矩阵，`G06RageStateTests` 保留四个种子＋Wild Boar 行为与 Sigil Silver 回放窗口。
- 原生证据：
  - 游戏包 `GameData.db.zip` SHA256 `fdfd3087e6a23eda2c9f53be68b012006cc949d03e8666dd51ad65abc71b61e5`；解压库／`vendor/GameData/GameData.db` SHA256 `2b9c6b3765c72a21a5ebfb7336c13251d09017b87c0e599671b14299800ba092`。
  - 全量夹具 `native_g06_templates.json`（136 模板）SHA256 `f41ef05ea3fee3e1327eb0dd232dcdf8438a385eccaf84a15d5342dd2cda36f4`；焦点夹具 `native_g06_rage.json` SHA256 `2ed190310a936342056e30c6c91b68b4a719b8be670b91ba63316c4c1cdd83a5`；任务索引 SHA256 `b72d84f67530ac82fd4c4b1e5b8d4de9593fda35d1e9f52004d046e6f7bc23f3`。
  - `python tools/g06_native_verify.py`：136/136 全量夹具与 DB/索引一致，焦点 11 模板一致，退出码 0。
  - 回放：`D:\巴扎plus\CombatReplays\CombatReplays` 200 份 payload；21 份含 G06 焦点卡。证据报告 `captures/diagnostics/g06/replay_g06_report.md`／`.json`，缺定义分诊 `missing_definition_triage.md`／`.json`。
    - Vengeful Sigil：`9a1d87b3…`（SHA256 `dd7c0693…`）Silver 每次敌方物品使用 +4 怒气；f120→f220 为 5000 ms 暴怒窗口，暴怒期间 Weapon Lifesteal 生效、结束移除；新增 `VengefulSigilSilverReplayWindow` 按该值回归。
    - Warpaint：`921f0f4a…`（SHA256 `c2fd2dd9…`）Diamond 贡献 12 怒气；另 3 份 Warpaint 在 Stash 中未触发，符合“纯模板在手中才生效、Stash 不参战”。
    - Shortened／Extended Rage：7 份回放的开场 `EnragedDurationMax` 分别为 4000／6000，暴怒窗口同值；`DynamicEnragedDurationAuraChangesWindow` 对应。
    - Burning Shield：唯一出现于 `0f4d1425…`，f330 首次跌破半血但原生 `PlayerShieldApply` 未出现（Burn 可能为 0 或被禁用），**不是正证据**；Toxic Shield、Wild Boar 物品／怪物在 200 份中完全缺席，不能用该语料声称原生通过。
    - PercentDamageReduction：`de491beb…` 显示 50% 减伤随 5000 ms 暴怒窗口开关；HealthMax：`921f0f4a…` 有 7 次 +50 事件；均作为规则方向证据，非 G06 种子卡身份证据。
    - 暴怒语义：32/32 个窗口都在 `Rage = RageMax` 时开始，窗口长度严格等于 `EnragedDuration / 50 ms`；13 个在战斗结束前关闭的窗口把 Rage 归零。未观察到“暴怒期间继续积攒怒气”的正样本，暂不把该行为写成已证实规则。
- 验证：
  - `bash tools/actionaxis_plugin/compile_tests.sh`：最近一次完整可编译且全绿的整包复跑为 `tools/actionaxis_plugin/build/g06_full_final_tests3.log`，退出码 0；`PASS G06FullCoverage`、`PASS G06RageState`、`PASS G02StatusTiming`、`PASS AuditRegression`、`PASS G03TriggerRules`。随后其它组继续写入 `G05FullAuraTests` 等新测试，当前复跑 `g06_full_final_tests5.log` 中 G06 仍全过，但整包退出码 1，仅 2 个失败在其它组：`NativeRuntimeRules`（G04 运行时冒充 Friend of the Sea 缺少 ctx.Skills）与 `G08Transform`（GreenScreen 复制目标不同）。集成前需等其它组落盘后复跑。
  - 定向 `g06_harness.exe`（LiveSkillReader＋G06RageState＋G06FullCoverage）：`OK`，退出码 0。
  - 矩阵：`EngineTests.exe --coverage …` 退出码 0；G06 35,334 行，`unsupported=0`（见上）。
  - `bash tools/build_actionaxis_plugin.sh --compile-only`：退出码 0，仅既有 Unity `CS0618` 警告；收尾构建 `ActionAxisEngine.dll` SHA256 `eeedb320260f2be26972891c3b226f026a8452114c4557fdc09f5ad92d84aefe`，`ActionAxisPlugin.dll` SHA256 `3c8389858985e9fcdbb3a09a60456b0e1794294eb3abf350907958cc3a50cd9c`。未写入游戏目录。
  - `python tools/g06_native_verify.py` 退出码 0；`python -m unittest discover -s tools -p test_rule_workplan.py -t . -v` 5/5 `OK`；`git diff --check` 退出码 0。
  - 前端未复跑：本轮未改前端。
- 原生对账：部分完成。Sigil／Warpaint／Shortened／Extended／PDR／HealthMax 有回放方向证据；Burning Shield 无正样本，Toxic Shield／Wild Boar 无样本；没有逐事件比较器结果，不能标 VERIFIED。
- 构建部署：仅 `--compile-only`；未复制 DLL、未重载、未部署，`DEPLOY=未执行`。
- 剩余风险与阻塞：
  - 7 条原生数据缺陷（Adrenaline Rush／Feral Alliance／Still Focus 缺 ability 1；Bushel／Tea Set 缺 aura 3；Outlands Terror 缺 aura 0；Calcinator Diamond 缺 ability 1）只能跳过并 warning；若后续 DB 修复，需重导夹具并复跑。
  - Base Rage Effect 的 10% 冷却缩减通过直接调整 `CooldownMax` 实现，与动态冷却光环叠加时的精确顺序尚无回放证明；G02 Dryad 测试已按 4.1→3.7 更新，需 G02 复核。
  - 回放只暴露可见的 combatant-1 棋盘；玩家侧隐藏，无模板 ID，无法把 Sigil／Warpaint 的玩家侧事件逐事件对账；无怪物回放。
  - `EnemyHero` 仍来自可能过期的 `Run.Opponent`（G06-02 风险未变）；`Toxic Shield`／`Wild Boar` 的模拟测试不是原生回放验证。
  - 共享工作区仍在并发修改；最近一次全绿整包复跑为 `g06_full_final_tests3.log`，当前 `g06_full_final_tests5.log` 仅剩 G04／G08 两个它组失败；集成/提交前必须再复跑，不能用历史 `OK` 代替。
- 下一条可执行动作：等 G04／G08 它组修复后复跑整包；由集成人复核 Base Rage 3.7s 与 `Triggers.cs` 同卡快照；G12 补一份含 Burning/Toxic Shield 或 Wild Boar 的真实回放；G07 处理 Radiant `PercentFreezeReduction`；确认后按授权范围提交、构建、部署。锁释放：是（未提交、未部署、未持有独占锁）。

### 2026-10-25 04:37 · G04-02 · 全量收口：150/150 主责模板、G04 相关 unsupported 清零

- Owner / Reviewer / 分支 / 基线：Qoder（G04）／待复核／`main` 共享工作区（未提交）／`6eb032a` + 共享未提交改动。
- 状态：IMPLEMENTED（G04 全量映射＋运行时冒烟＋机制行为回归）；非 VERIFIED（未做游戏内 UI／完整逐事件对账，未部署）。
- 范围：`primary_group=G04` 的 150 个模板全部逐个走真实映射入口和一次运行时冒烟；同时复查全库 1393 个含 G04 `related_groups` 的模板，确认 G04 数值／标签／计数器路径没有 unsupported。
- 全量结果：
  - 150/150 G04 主责模板均可映射；`G04AllPrimaryTemplatesMap` 与 `AllG04PrimaryTemplatesSmokeRuntime` 通过。
  - G04 主责 150 模板共 35580 行组合：mapped 33972、simulated 1116、live-applied 18、inactive 378、out-of-combat 78、mapped-warning 18、unsupported 0。
  - G04-related 1393 模板共 340038 行：mapped 314706、simulated 8017、live-applied 733、inactive 3442、out-of-combat 318、mapped-warning 11922、unsupported 900。900 行 unsupported 仅来自 Hacksaw／Recycling Bin（G08 变形供体未校准），不是 G04 数值/标签/计数器路径。
  - 11922 行 mapped-warning 来自 42 个模板，原因均为缺失 Ability/Aura 定义（已跳过并保留其余部分）；其中 G04 主责只有 Masterpiece（18 行），其余属 G00/G11 数据缺口。
  - G04 仍有 48 张卡出现非成功行，但全部是 `inactive`（该品质没有原生效果）、`out-of-combat`（升级/购买等场外）或 `mapped-warning`（Masterpiece 缺能力定义），不是 G04 战斗规则拒绝。
- 代码／测试改动：
  - `SkillMapper`：技能 `AuraIds` 引用缺失定义时记录 `MappingWarnings` 并跳过，不再因单个缺失/过期光环把同卡已完整映射的主动能力判为 unsupported；Building Crescendo 的 Ability 0（使用物品时给可暴击手牌加 Custom_0 CritChance）已实际执行通过。
  - 人物 `Custom_N`：沿用 G05 的全量人物属性快照（`InternalHero.Attributes`），G04 数值引用直接读取；Pyg's Gym／Orange Julian 恢复 mapped。
  - 新增 `tools/actionaxis_plugin/tests/fixtures/native_g04_all.json`（150 个主责模板，SHA256 `8D3835564396CA93809D173A182AD05C2C7DCC3C93ABC245A39F1A9FBC5DF2E6`），用于全量映射与冒烟。
  - `NativeRuntimeRulesTests` 新增：150 卡全量映射、150 卡运行时冒烟、Pyg's Gym 读 Custom_4、Orange Julian 读 Custom_3、Building Crescendo 缺光环仍执行能力。
- 回放证据（`D:\巴扎plus\CombatReplays\CombatReplays`，200 份全部可解码）：
  - Pyg's Gym `37253285d1b04282bfc4653e17857fb3`：5.50s 对 3 把武器各 +276 Damage = 人物 `Custom_4 46` × Gold `Custom_0 6`；与新增测试完全一致。
  - Orange Julian `c160cff834524056bb3082052b225949`：Gold 卡未触发；开场人物 `Custom_3=267`、模板 Custom_0=50，算术为 267×50×0.01=133.5→134；测试按此值验证，但该回放本身未观察到触发。
  - Pauldrons `b069c43c47934d5391510520f119cadf`：Gold+Shielded，`ShieldApplyAmount` 3.80s 起 0→596；没有 SellPrice 变化。用于佐证暴怒/护盾成长路径，未做逐事件归因。
  - Streaming Setup `9c0b866a525f467baabd792e90b70e27`：Silver、Stash、`Custom_0=5`、`SellPrice=4`；0–14.55s 无战斗事件、无属性/标签/售价变化，手牌也没有 Toy/Friend。该回放不能验证逐目标售价引用。
  - Spacescraper、Geyser、Skyscraper、Vat of Acid、Seafaring、Ancient Technology 在 200 份回放中均未出现；不得用不存在的回放声称对账通过。
  - 200 份中 155 份含至少一张 G04 主责卡，覆盖 92 个不同 G04 模板；完整清单在 `tmp_g04_verify/decoded_card_presence.json`。
- 验证：
  - G04 定向 `tmp_g04_verify/run_g04.sh` → `PASS NativeRuntimeRules` / `OK`，包含 150 卡全量映射与运行时冒烟。
  - 整包 `bash tools/actionaxis_plugin/compile_tests.sh` → `OK`，全部套件通过（含 `PASS NativeRuntimeRules`、G00/G01/G02/G03/G05/G06/G07/G08/G09/G10/G11/G12 全量/覆盖套件）。
  - 前端 `npm.cmd test -- --run` → 37 文件／102 测试全绿。
  - `bash tools/build_actionaxis_plugin.sh --compile-only` → 通过（仅既有 Unity CS0618 警告）；`ActionAxisEngine.dll` SHA256 `694E21529FCB5C220284DE28B7D31CB93D3E74EB5162FD197CE98961D9DA3C73`，`ActionAxisPlugin.dll` SHA256 `EF70755237A18DD87CDD374DE9439B11E265C0A0F7F7817299D459C42C9A0A4E`。未部署。
  - 矩阵：`CoverageHarness` → 387678 行；全库 item unsupported 1344（5 张 G08 卡）、skill unsupported 54（2 张 G10 技能）；G04 unsupported 0。
  - `git diff --check` → 退出码 0。
- 剩余风险与阻塞：
  - Masterpiece 18 行 `mapped-warning`：Gold `AbilityIds` 含缺失的 `1`，`Abilities` 只有 `0`；`0` 是场外每小时加值，战斗内无行为。缺定义无法凭空补，保留告警。
  - Building Crescendo 的缺失光环 `1` 按“无效果”跳过；能力 0 已工作，但缺失光环的原生真实语义未知。
  - 回放只对 Pyg's Gym（强证据）和 Pauldrons（数值序列）提供部分证据；Streaming Setup 回放无有效战斗事件，Spacescraper/Geyser 无回放。
  - 未做游戏内 UI 验收、未做完整逐事件比较器对账。
- 下一条可执行动作：G00 复核 Masterpiece/Building Crescendo 缺定义是否可从新版本 DB 补齐；G12 用含 Spacescraper/Geyser 的回放补逐事件对账；集成人复核本轮差异后决定是否授权提交／部署。锁释放：是（未提交、未部署）。

### 2026-10-25 05:00 · G00-02 · 物品入口阻断清零、Nanobot/变形池/插槽效果与数据缺口收口
- Owner / Reviewer / 分支 / 基线提交或补丁哈希：OpenCode（本轮）／待集成人复核／`main` 共享工作区（未提交）／`6eb032a` + 当前工作区。
- 状态：IMPLEMENTED（G00Data、G00Coverage、G08PoolTransform、G00 审计与 `--check` 通过；官方全量 C# 受并发组 G01TargetConsistencyTests 正在编辑影响，尚未取得最终全绿；未部署、未提交）。
- 范围：在 G00-01 基础上处理剩余 137 个 blocked 模板中的代码可修部分：Nanobot Orange 运行时 `DamageAmount`、Potion Potion／Recycling Bin／Hacksaw／[DEBUG] Start of Fight Transform 的池供体、Blender／Grabinator 的固定/跨侧供体（G08 同批）、`[Stove]/[Cooler] Socket Effect` 的 Heated/Chilled 读取；同时复核 56 个缺定义、16 条怪物缺失手牌、4＋62 条稀疏品质和 9＋2 条索引外规则。
- 原生证据：
  - 数据库 SHA256 `FDFD3087E6A23EDA2C9F53BE68B012006CC949D03E8666DD51AD65ABC71B61E5`；索引 `B72D84F67530AC82FD4C4B1E5B8D4DE9593FDA35D1E9F52004D046E6F7BC23F3`；矩阵 `8F8BD975002195D1B5A58DBD7AF045566E9F553BA803D10334AD2AB8D3DB93F7`（387678 行）；怪物报告 `76C8D5D92C7454B05C835C80E651392D6E7BF4F1A64A18275AFB7CBCFCA6F240`；怪物卡牌定义 `0A4203FF85FD60F386366485293928D6ABF5C60655C3C5489B7E28FFBA47ABF9`（1655 行）。
  - 冻结基线 `data/g00_baseline.json = 0731AAC460691B3419A3D840B1BC56FFE245C4134747A89836915AB2D6E2C513`（schema 2），`g00_audit.py --check` 通过。
  - 回放证据：Blender `9370ffbde692493cbf066af4ef37304e` 在 4.0s／8.5s 把相邻 Small 变成 `8307efa3-97b6-4645-a3cb-de4930880ef4`（Slushee，Silver，CooldownMax 6000）；Potion Potion `9899c127c95f41978cb117b18d848b8c` 在 2.0s 变成 2 个 Small Potion（Regeneration Potion／Drowsdrop，Silver，各带 Ammo 1）。两文件位于游戏目录 `BazaarPlusPlusV5/CombatReplays`；仓库 `CombatReplays` 为子集，已抽查 2026/10/24 新样本并检索快照库，未找到其余变形供体的回放。回放只用于供体规则/品质校准，不是逐事件全对账。
  - 缺定义：仍为 56 个模板／64 个唯一引用／按品质展开 164 处；46 个物品在矩阵中保留 `mapped-warning`（12846 行实时属性告警），缺项跳过、其余战斗部分保留。live cache DB（SHA `F4FECD4D…`）对应 53／61／147，但模板 ID 版本不同，不作为官方失败解除证据。
  - 怪物：16 条手牌模板缺失（DATA）；4 条手牌品质稀疏、62 条技能品质稀疏（实例品质低于声明最低品质，保留为数据缺口）；3 条 `TCardInstanceSocketEffect` 已应用 Heated／Chilled，怪物报告 0 处矩阵不一致、15 个怪物仍有缺失手牌告警。
  - 索引外：`TCardPlayerEffect` 9 条（`Base Rage Effect` 已由 G06 实现，其余 8 条为跑图统计/头像 UI）＋`TCardSocketEffect` 2 条（已接入读取）。
- 完成内容：
  1. Nanobot Orange：`TActionPlayerDamage` 无 `ReferenceValue` 且模板期无 `DamageAmount` 时，生成运行时自身属性表达式（模板快照 0），不再整卡拒绝；由 `Auras.1` 在运行时提供每 Friend 伤害。
  2. 变形池：新增 `smallPotionSelf`／`potionTarget`／`smallItemTarget` 供体规则；从显式 `SpawnPool` 取同品质、含 `NativeTemplate` 的候选，排除目标/自身模板；缺失时告警并保留原卡，不使用本地文本近似。Blender／Grabinator 的固定/跨侧规则由 G08 同批完成。
  3. 插槽效果：`MonsterTemplateExtractor` 读取 `TCardInstanceSocketEffect`，对 `TTargetCardOccupying` 的 `Heated/Chilled` 光环按槽位写入对应手牌属性；缺模板/未知目标保留原始路径告警。
  4. 数据缺口：未补造任何数值；56 个缺定义、16 条怪物缺模板、4＋62 条稀疏品质、9＋2 条索引外规则全部保留在基线/文档。
- 改动文件：`engine/LiveMonster.cs`、`engine/SkillMapper.cs`、`engine/Transforms.cs`、`tests/G00DataTests.cs`、`tests/G08PoolTransformTests.cs`（新增）、`tests/Program.cs`、`tools/rule_inventory.py`（新增 `--monster-cards`）、`tools/g00_audit.py`、`tests/fixtures/native_g00_socket_effects.json`（新增）、`tests/fixtures/native_g08_pool_transforms.json`（新增）、`data/g00_baseline.json`、本文件。共享文件中的其它组并发改动未认领。
- 新增测试：`G00DataTests.MonsterSocketEffectsApplyHeatedAndChilled`（Stove/Cooler 真实模板→对应槽位 Heated/Chilled；缺失模板保留路径）；`G08PoolTransformTests.DonorRulesAreCalibrated`／`BlenderTransformsAdjacentSmallIntoSlushee`／`PotionPotionCreatesTwoSmallPotions`（真实模板＋显式 SpawnPool；Slushee Silver 6s、2 个 Silver Small Potion）；`Program.cs` 注册 `G08PoolTransform`。
- 验证：
  - 等价源码集 `G00Only.exe`（引擎＋G00/G08Pool 测试）：`PASS G00Data`、`PASS G00Coverage`、`PASS G08PoolTransform`、`OK`。
  - `G00Coverage.exe --coverage`：387678 行；item mapped 359892／mapped-warning 12846／unsupported 0；skill simulated 9045／live-applied 755／inactive 4522／out-of-combat 564／unsupported 54；blocked 0/2；warned 46/0；附魔告警行 0；实时属性告警行 12846。
  - `G00Coverage.exe --monsters`（使用 `native_monster_cards.jsonl`）：166／978／262；0 处不一致；15 个怪物带缺失手牌告警；缺失模板 16；未声明品质 66（手牌 4＋技能 62）；缺失附魔 0。
  - `python tools/g00_audit.py --output`／`--check`：`G00 审计通过`。
  - 官方 `bash tools/actionaxis_plugin/compile_tests.sh`：最后一次可编译运行中 `PASS G00Data`、`PASS G00Coverage`、`PASS G05DynamicAura`、`PASS G05FullAura`、`PASS G08Transform`、`PASS G08PoolTransform`、`PASS G10DamageLedger`；整包因其它组 21 个断言失败（G09Lifecycle／AllHandsRules／G01TargetConsistency／G01SocketEffect）未全绿。随后 G01TargetConsistencyTests 被并发编辑为不可编译；因此本日志不声称 Gate A 全绿。
  - `npm.cmd test -- --run`：37 文件／102 测试全绿（前端未改）。
  - `git diff --check`：退出码 0（仅 CRLF 提示）。
- 原生对账：未完成逐事件对账。已用 Blender/Potion Potion 两份真实回放校准供体模板/品质/数量；SpawnPool 在插件中仍需完整原生模板注入，否则运行时按 fail-closed 告警保留原卡。G12 现有 9bbabba4／084bd840 仍为 DIFF，不能把矩阵清零当原生一致。
- 构建部署：未重新构建/部署 DLL；`DEPLOY=未执行`。官方全量构建待并发组稳定后复跑。
- 剩余风险与阻塞：
  - 2 个技能模板（Aggressive Mutations／Virulent Research）的空 `TTriggerOnCardFired` 仍为 DATA，缺真实触发时机；不得按 tooltip 猜成 onUse。
  - 56 个模板缺 `AbilityIds/AuraIds` 定义（DATA）；46 个物品只保留部分战斗效果和告警；不得用相似 ID/品质升级补齐。
  - 16 条怪物手牌模板缺失（DATA）；4＋62 条稀疏品质仍需原生语义确认；3 条插槽效果已应用读取，但怪物报告中 16 条缺模板仍告警。
  - 9 条 `TCardPlayerEffect` 中 `Base Rage Effect` 已实现，其余 8 条为跑图统计/头像 UI（OUT）；2 条 socket 已接入读取。
  - 官方全量 C# 当前受并发组 G01TargetConsistencyTests 编辑影响，未取得最终全绿；本组不代签其它组。
- 下一条可执行动作：并发组稳定后复跑 `bash tools/actionaxis_plugin/compile_tests.sh` 与 `npm.cmd test -- --run`；若源码哈希变化，重跑 `--mapping-input`／`--monster-input`／`--monster-cards`／`--coverage`／`--monsters`／`g00_audit.py --output` 并更新本文件。G11/G00 在新版本 DB 复核 56 缺定义与 16 条怪物缺模板；G12 用真实回放做逐事件对账。锁释放：是（未持有独占锁；未提交、未部署）。

### 2026-10-25 05:29 · G01-02 · G01 映射清零、槽位契约、主动优先级与回放核对

- Owner / Reviewer / 分支 / 基线：本轮实现（G01）／待复核／当前共享工作树／HEAD `6eb032a` + 未提交并发改动；本轮未提交。
- 状态：IMPLEMENTED（G01 主责模板 `unsupported=0`；整包 C# `OK`）；不是 VERIFIED：玩家开场回放缺失、比较器整体仍为 DIFF；未部署。
- 范围：G01 主责 164 个模板；此前剩余 7 个 `unsupported`（Adaptive Ordinance、Cherished Keepsake、Frozen Horror、Hypergreens、Iceberg、Meat、Product Showcase）；AllHands 27 模板；`TCardSocketEffect` 2 行；`EnchantmentMapper` 目标复用；真实回放 `CombatReplays/CombatReplays`。
- 完成内容：
  1. **映射清零**：最终矩阵 `tmp_g01/g01_report_final_all.tsv` 中 G01 主责 `unsupported=0`；全局物品 `unsupported=0`，技能仅 2 个 G10 模板（Aggressive Mutations、Virulent Research）。
  2. **Occupying 契约**：`MapCardTarget/MapSubject/Trigger.Target/Trigger.Source/Prerequisite` 统一映射 `Target.Kind=occupyingItem`；`Effects`、`NativeDynamicAuras`、`NativeCardAbilityMapper` 命中同槽非插槽物品；非 `TCardSocketEffect` 来源直接无目标，不退回 Self。新增 `G01SocketEffectTests`，用 `[Stove]/[Cooler] Socket Effect` 覆盖主动、静态/动态光环、计数/属性引用、被动主体/Target、前提和缺上下文 fail-closed。
  3. **主动优先级**：`Effect.Priority` 从原生 `Ability.Priority` 写入；主动、ForceUse 与普通激活按 Immediate/Highest/High/Medium/Low/Lowest 稳定排序。真实回放 `7b0a2ae8…` 的 Weaponized Core 首击由修正前 130 变为 **190 @ 3.05s**，与原生一致；Atlas Stone 的 High 双重增伤也在 Medium 伤害前结算，旧测试期望已按原生优先级更正。
  4. **附魔目标复用**：`EnchantmentMapper.MapAction` 改用 `SkillMapper.MapCardTarget`，Warp Drive Heavy 的 `AllHands` 慢速目标不再整卡回退；`G01FullRulesTests` 覆盖 Frozen Horror Both、Hypergreens SellPrice 邻位触发、Fairy Haze 冻结/减速计数、Fire Hose 半量清除、Weaponized Core 首击顺序。
  5. **AllHands 余项**：Fairy Haze、Fire Hose 已进入映射/行为回归；Trojan、Wild Quillback 仍为 `mapped-warning`（缺 AbilityId 2/3 定义，保留告警，不猜数值）。
- 回放核对：
  - 200 份本机回放中 34 份包含 G01 种子/重点模板；34/34 ledger 生成成功。**玩家开场 0/34 可用**：BPP 快照 DB 有 365 条 battle_snapshots，但 200 个回放 BattleId 均不在其中；玩家侧物品/技能无法重建。8 份可观察敌方侧种子事件。
  - Weaponized Core `7b0a2ae8…`：敌方 Diamond Core 首击 190 @3.05s 已对齐；后续伤害值 250/310/370/430/490/550 与原生序列一致，但 sim 触发时刻仍受缺失模板/玩家侧 UNKNOWN 影响。
  - Kusarigama `6eed…/cc91…`、Enervating Sigil `b4e4…` 等敌方侧事件已导出到 `tmp_g01/replay_evidence/`；完整比较器仍 `DIFF`（matched 1085、missing 4164、extra 434、valueMismatch 814），不能称逐事件一致。
- 验证：
  - `bash tools/actionaxis_plugin/compile_tests.sh` → 退出码 0，**OK**（含 `G01TargetConsistency`、`G01SocketEffect`、`G01FullRules`）；`NativeRuntimeRules.AtlasStoneUnscaledGrowthRepeatsBase` 已按原生 High-before-Medium 优先级改为 40/40、60/60，旧 20/40 是未实现优先级时的近似。
  - `npm.cmd --prefix d:\巴扎plus test -- --run` → 37 文件／102 测试通过。
  - `bash tools/build_actionaxis_plugin.sh --compile-only` → 退出码 0，仅既有 Unity `CS0618` 警告；产物 `ActionAxisEngine.dll` SHA256 `B0DDA5A327ABA2169FD6667AA23F7B4D54194EE8236654155E7C19BD04EB3C16`，`ActionAxisPlugin.dll` SHA256 `6307BD08F35DDB0C4BC91B097367C145B85657A7BC738E8FDBCF14CCE7C4F658`。
- 剩余风险与阻塞：
  - `TCardSocketEffect` 的**玩家实时**入口仍依赖 BPP 快照已烘焙的 `Heated/Chilled` 属性；`LiveMonster` 已读取 `Player.Socket.Effects`，但玩家侧 socket effect 注入没有独立回放证据。模板路径已用合成 socket 卡验证，不能写成所有实战路径已验证。
  - Trojan/Wild Quillback 缺 AbilityId 2/3 定义，保留 `mapped-warning`；不能用对称猜测补能力。其余 56 模板缺定义归 G00/G11。
  - 回放无玩家开场，Gate C 不能完成；比较器目前只证明单个种子价值/首击，不是逐事件一致。
  - 未部署、未重启游戏，Gate D 未执行。
- 下一条可执行动作：G12 解决 `battle_snapshots` 与回放 BattleId 的对应或补齐 OpeningMessage；G00/G11 补 Trojan/Wild 缺定义证据；集成冻结后复跑整包和比较器。锁释放：是（未持有独占锁；未提交、未部署）。

### 2026-10-25 · G12-03 · 全量对账第二轮：随机/确定性分栏与五通道补齐

- 触发：上一轮 145/145 DIFF、聚合 missing 180925／extra 39884；复核要求继续处理剩余问题，不能只做几张卡。
- 修复（全部有回归测试或全量复跑）：
  - **frame-0 DoT tick**：原生在 frame 0 就对开场技能/开场状态施加的 Poison/Burn/Regen tick（85 份回放样本）。模拟器已补上；同步修正 G06 `NativeMechanics` 的时序断言（burn 5 次、poison/regen 在 0/1/2 秒、拆分 shield 断言）。
  - **技能去重**：快照与 `PlayerSkillEquipped` 是同一技能，旧账本追加两次，导致开场技能触发两次（Lash Out 等）。现按 `instanceId` 合并；`build_engine_fixture` 不再把技能实例漏进 `opening.items`。
  - **附魔/光环随机目标**：`load_ability_targets` 读取 `Enchantments` 与 `Auras`；Turbo Bandages 等随机加速/冻结不再标成确定性失败。
  - **健康通道**：只比较 Health/HealthMax/Shield/Burn/Poison/HealthRegen/Dead；`Gold/Custom_N` 不再当成健康记录（此前 145 份里制造数千条假 missing）。
  - **卡牌属性导出**：模拟账本导出 Haste/Slow/Freeze/Flying/DamageAmount/CritChance/CooldownMax/Custom_N 等全部实时属性，不再只导出 Cooldown/Ammo（此前 cards 通道 missing 148362 的主因）。
  - **技能 identity**：模拟账本导出 `config.PlayerSkills/EnemySkills`；`enemy:skill:`/`skill:` 前缀统一；出生时间统一为 0。identity 差异从 667 missing／516 extra／296 time 降到 1／1／3。
  - **regen 重复 heal**：regen tick 的 `dotTick` 与 `heal` 只保留一条；不再产生 `extra heal`。
  - **死亡帧与 postEnd**：以原生 `CombatantDied` 时间为比较终点；之后的模拟记录单列 `postEnd`。
  - **沙暴事件**：`SandstormCountdownStarted/Started` 单列 `noCounterpart`，不再算 unknown/missing。
  - **运行时/旧模板能力**：原生 `EffectId` 不在当前模板 Abilities/Auras/对应附魔内时标 `runtimeAbility`；该效果及同帧卡牌更新单列 unverified，不再算 missing。原生每次光环刷新产生的卡牌更新单列 `auraRefresh`。
  - **随机分支分栏**：`unverifiedReasons` 区分 `randomTarget`、`randomCascade`、`randomTransform`、`runtimeAbility`、`auraRefresh`、`skillIdentity`、`noCounterpart`；随机变形会同时标记后续卡牌/效果/identity，不强行追平。
- 全量结果（521 份；145 份可比较；376 份阻塞）：
  - 145 份全部 DIFF；matched 303166／unverified 402265／postEnd 66637／missing 27191／extra 15055／value-mismatch 41295／time-mismatch 3／unknown 4255。
  - unverified 明细：randomCascade 361937、randomTransform 28880、noCounterpart 5742、randomTarget 3030、runtimeAbility 2265、skillIdentity 343、auraRefresh 68。
  - 按原生随机事件拆分：**2 份纯确定性**（101f6357、170666bf），**143 份含随机分支**。
  - 纯确定性 2 份合计：matched 5572／unverified 52／missing 30／extra 9／value 350；仍非 MATCH。
  - 固定样本：9bbabba4 matched 1393／unverified 5597／missing 49／extra 72／value 178；084bd840 matched 2429／unverified 1441／postEnd 45／missing 3／extra 0／value 18。
  - 事件覆盖：178016 条战斗事件；transform/revert 29/29；`EffectTriggered` 仍为 0；沙暴 3699（41 份）；randomTarget 10880；开场非满冷却 0。
- 确定性剩余根因（不是随机，不能算通过）：
  - **101f6357**（10 missing／1 extra／13 value）：Idol 上存在当前模板没有的运行时/旧模板效果 `q1`，每次刷新给 PoisonApplyAmount +4（7→11→15…）；该效果及同帧卡牌更新已标 `runtimeAbility`。frame-0 DoT 已修复，残余生命差是该运行时效果的下游。**2026-10-25 复跑（`tools/replay_batch_audit.py` + 侧车 DB）：MATCH，可验证差异 0**；q1 仍是数据缺口。
  - **170666bf**（20 missing／8 extra／337 value）：已定位并修复两类确定性规则（2026-10-25）：
    1. **充能 250ms 串行**：同一（充能者,目标）的 `CardCharge` 至少间隔 250ms。语料 27,629 次充能中同对相邻间隔 1–4 帧为 **0** 次、恰 5 帧 **12,930** 次；同帧同对仅 3 例，同帧跨目标 1,257 组不受影响。实现：`Effects.TryReserveCharge`/`DeferredCharges`（`Battle.ProcessDeferredCharges` 每帧结算 `上次+0.25s`）；充能落在已就绪（冷却 0、本帧将重置）的卡上时挂起 `PendingChargeOnReset`，在重置后的新冷却上扣减（帧 161：5000→4000）。
    2. **Heirlooms 逐次叠加**：见 G05 新增条目。修复后 8 次充能帧（81/86/161/166/241/246/321/326）、SellPrice（1→6→11→16→21）与 ShieldApplyAmount（2→12→22→32→42）逐帧对齐；**复跑 MATCH，可验证差异 0**（残余 64 行全部为单侧模式不可验证通道）。
  - 1dbdc 等 143 份的冷却/触发/伤害差异主要是随机目标连锁，已计入 `randomCascade`；不能归责为确定性 G02 错误。
- 证据：`captures/diagnostics/g12/batch_summary.json`、`batch_replays.csv`、`batch_top_groups.csv`、`coverage_all.json`、`opening_audit.json`、`deterministic_residual.json`、两份固定样本账本/模拟账本/差异 CSV。
- 验证：全量 C# `OK`；`G12UiHistory`/`G12OpeningCooldown` PASS；Python 23 项通过；前端 102 项通过；`--compile-only` 退出码 0（源 DLL Engine `c55a5568…6bb80`／Plugin `43839ad8…4a20`，已安装 Engine `7ed0bd7c…`／Plugin `ba261a4e…`，`release_check=MISMATCH`）；**未部署**。145/145 仍 DIFF，不能报告 Gate D。
- 2026-10-25 P0 收敛（本轮）：`TTriggerOnItemUsed` 同帧/延迟语义（充能 250ms 串行 + 就绪重置挂起）与 Heirlooms 型每次激活光环增量已在引擎落地；新增 `G12Determinism` 测试套件（Matchbox 型串行化 + Heirlooms 型叠加，帧级断言 4.05/4.30/重置 3750、SellPrice/Shield 逐次值）；4 个同时间戳合成测试（Weaponized Core、Launcher Core、Aviators、Firefly 邻居充能）按原生串行语义改写。两份纯确定性回放 **101f6357/170666bf 复跑均 MATCH（可验证差异 0）**；全量 C# `OK`；Python 58 项通过。剩余：143 份随机分支仍无原生种子（UNVERIFIED），Idol q1 等数据缺口不变；部署与游戏内 Gate D 仍需用户明确授权。锁释放：是（未提交、未部署）。

## 10. 全量派单路由（本文件维护，CSV 只生成）

`python tools/rule_inventory.py --work-index docs/native-rule-task-index.csv` 读取下列 JSON 注释，输出全量卡牌／技能／怪物索引。基于结构匹配建立主责，不基于“首个警告”分配，以免修一处就无人负责剩余效果。`seeds` 只固定核心验收种子的主责；未列出的全部卡牌仍由特征＋兜底分配。

算法：怪物→G00；具体种子→对应组；其余对**去掉 Enchantments 的基础模板**按优先级取第一个匹配组；无匹配→G11。关联组按完整模板全部匹配，加上 G00/G12。`has_enchantments` 只在完整模板关联判定中生效；关联不等于该组负责整卡。按结构无法可靠判定 IN/OUT，索引统一 `scope=待核实`，Owner 认领后在工作单补证据。

调整路由或种子后重新生成索引，先解决重复／未知种子错误；CSV 含数据库与路由 SHA256，可与基线核对。数据库变化导致数量变化要新增日志解释，不能修改预期数字掩盖漏导。

<!-- RULE_WORK_ROUTING
{
  "version": 1,
  "crosscut": ["G00", "G12"],
  "monster_group": "G00",
  "fallback": "G11",
  "priority": ["G08", "G09", "G06", "G05", "G01", "G02", "G04", "G03", "G07", "G10", "G11"],
  "groups": {
    "G00": {"title": "数据、品质与基线"},
    "G01": {"title": "位置与全体目标", "types": ["TTargetCardPositional", "TTargetCardXMost", "TTargetCardOccupying", "TTargetCardTriggerSource", "TTargetCardTriggerTarget"], "sections": ["AllHands", "AbsolutePlayerHand", "AbsoluteOpponentHand"], "seeds": {"9131cedf-e5e7-4a23-a90e-2576048419e8": "Weaponized Core", "0229faa7-2eec-4746-9134-ade4f3aebe45": "Gamma Ray", "c1c4f8fc-548d-4ae6-bfca-1c7ea2aca212": "Omega Ray", "cae6a187-7e4e-40fb-989b-4b84d59bda24": "Kusarigama", "2e2815d0-fa7c-45ab-a0f2-65ebe1c815d8": "Enervating Sigil"}},
    "G02": {"title": "充能控制与弹药", "types": ["TActionCardCharge", "TActionCardHaste", "TActionCardSlow", "TActionCardFreeze", "TActionCardReload", "TActionCardFlyingStart", "TActionCardFlyingStop", "TActionCardFlyingToggle"], "seeds": {"9fafc0a5-6675-4e17-9457-590f13354a39": "Launcher Core", "cda40a45-8703-4347-8f4e-7fd460b2ac57": "Dryad", "ca77281c-f912-4db3-86ce-64842a4ccdaa": "Cog", "a655fb50-b9e1-41ec-b658-07459014f96c": "Drowsdrop"}},
    "G03": {"title": "触发条件与次数", "type_prefixes": ["TTriggerOnCardPerformed", "TPrerequisite", "TCardConditional"], "types": ["TTriggerOnItemUsed", "TTriggerOnBeforeItemUsed", "TTriggerOnCardStartedFlying", "TTriggerOr", "TTriggerAnd"], "seeds": {"0941d7dd-1264-42c5-b334-6cc5d694571b": "Hunting Hawk", "0f96e2e4-2bd8-4fcf-aaaf-e4e0ee5cb627": "Boosted Saucer", "22c8b252-e36d-4564-9588-06febd4b358d": "Wolf Mask", "13989ad3-2122-4d86-a323-bb90fda8360b": "Bloodhound", "1252064a-3bf4-478e-9326-946677ecc8b3": "Frozen Secrets", "c5bff6ad-5d13-4cf3-9941-98f3cf913949": "Counterstrike"}},
    "G04": {"title": "数值属性与标签", "type_prefixes": ["TReferenceValue", "TActionCardAddTags", "TAuraActionCardAddTags"], "types": ["TActionCardModifyAttribute", "TRangeValue"], "seeds": {"2a0624d0-8257-42f9-8e9d-bbe0a5218b44": "Streaming Setup", "40d8b44a-8e2f-4081-8163-043d76ddbafa": "Spacescraper", "77df36bf-a1d0-4815-9f07-5c330542e76a": "Geyser", "8666a1de-066f-47c4-9d9b-87eea7d9f25a": "Pauldrons"}},
    "G05": {"title": "动态光环与冷却", "types": ["TAuraActionCardModifyAttribute", "TAuraActionPlayerModifyAttribute"], "attributes": ["FlatCooldownReduction", "PercentCooldownReduction", "CooldownMax"], "seeds": {"22e88b23-7497-4cb9-92a9-6d5e6bde6373": "Passive Power", "b607f75b-e245-4e4a-809e-899fcdd1e6e5": "Rate Limiter", "85f59c88-c391-4a03-8c3b-f312518e81de": "Force Field", "8ec1c3f9-6220-431b-b3b0-52b3be2e20f5": "Spiky Shield"}},
    "G06": {"title": "人物状态与怒气", "types": ["TActionPlayerRageApply", "TActionPlayerModifyAttribute", "TTriggerOnPlayerEnraged", "TTriggerOnPlayerEnrageEnded"], "attributes": ["Rage", "RageMax", "RageApplyAmount", "EnragedDurationMax"], "seeds": {"153dfd6d-7dc8-4386-9703-ea6c99f92d4a": "Warpaint", "05a32402-598c-4bb5-a280-1a84f0ab84fa": "Vengeful Sigil", "7931571a-3d32-41e8-bf7b-ca6b4d1b7a3d": "Burning Shield", "a9b441e1-5824-4855-9359-2ab4643d9ff5": "Toxic Shield"}},
    "G07": {"title": "附魔暴击抗性免疫", "has_enchantments": true, "type_prefixes": ["TActionCardEnchant"], "attributes": ["DestroyImmunity", "PercentSlowReduction", "PercentFreezeReduction", "DamageCrit", "CritChance"], "seeds": {"027ef534-d047-4e18-8496-d9fd4773e15d": "Dishwasher", "02d7a406-a6eb-4b46-8975-1f69b0fde428": "Security Blanket", "1b79b3e5-5870-48f7-a877-add0721f9ab6": "Spirit Diffuser"}},
    "G08": {"title": "场上复制变形", "types": ["TActionCardTransform", "TTriggerOnCardTransformed"], "seeds": {"723880db-8a97-4833-a9a1-80c75cde4c17": "3D Printer", "3172cda6-0ebd-4663-a6d6-f72a3d7c4b2c": "Mirror", "1d354dcf-8736-4bab-9dc1-7d9054d6c4d4": "Hologram Projector", "4fb9fa4a-6250-4132-a7cb-9a4b874b0bdc": "Quicksilver", "020a0ec0-21e6-41af-899f-063573ba9ca5": "Virus"}},
    "G09": {"title": "生命周期与复活", "types": ["TActionCardTransformDestroyed", "TActionCardDestroy", "TActionCardDisable", "TActionCardRepair", "TActionCardForceUse", "TActionPlayerReviveHeal", "TTriggerOnPlayerDied", "TTriggerOnBeforeCardDestroyed"], "seeds": {"03f90f0b-088f-453d-bbf7-ebb5e777afe2": "Abducted Cow", "3c7807ef-0993-4b33-8a34-441f7cde0666": "Drone Crusher", "09c59ebf-d2f9-4862-9c30-396b9ddfdd89": "Fiery Rebirth"}},
    "G10": {"title": "输出与生命账本", "types": ["TActionPlayerDamage", "TActionPlayerHeal", "TActionPlayerShieldApply", "TActionPlayerBurnApply", "TActionPlayerPoisonApply", "TActionPlayerRegenApply"], "seeds": {"014d9c98-e823-443c-98a3-6367ab81c956": "Trained Spider", "666f4084-48cc-4558-b678-a46ba18c1bf2": "Soldering Gun"}},
    "G11": {"title": "余项范围与数据阻塞", "type_prefixes": ["TActionGame", "TTriggerOnEncounter", "TRunConditional"], "types": ["TPrerequisiteRun", "TSpawnContextQuery", "TActionCardBeginSandstorm", "TTriggerOnSandstorm", "TTriggerOnCardSold", "TTriggerOnCardPurchased", "TTriggerOnDayStarted", "TTriggerOnHourStarted"], "seeds": {"028d89ac-b4c4-4d3c-ba31-71b57aa33bed": "Potion Distillery", "ff06b63a-15ee-49be-80b1-5fb52b4e1616": "Windmill", "c8a322c7-a8f9-49e3-90e5-3a348d0fe5fc": "Bottled Tornado"}},
    "G12": {"title": "对账UI与发布门禁"}
  }
}
RULE_WORK_ROUTING -->
