# 原生规则全覆盖 · 工作文档

> 目标：让行动轴引擎读懂游戏原生模板里的**全部物品主动/被动、技能、附魔**规则，而不是只支持一小部分后给出「规则待支持」。
>
> 本文档是交接用的单一事实来源。每完成一批规则，必须在文末「进度日志」追加一条记录，并同步更新下面的覆盖状态表。

- 建立日期：2026-10-24
- 规则来源：`<GameDir>/TheBazaar_Data/StreamingAssets/GameData.db.zip`（游戏 5.0 原生模板）
- 覆盖基线：[docs/native-rule-inventory.md](native-rule-inventory.md)（由脚本生成，勿手改）
- 刷新命令：`python tools/rule_inventory.py --markdown docs/native-rule-inventory.md`
- 单条规则样例：`python tools/rule_inventory.py --sample TActionCardReload`

---

## 1. 判定口径

「支持」指该规则在**战斗推演**中产生与原生语义一致的结果，并且有 C# 测试覆盖。分四档：

| 状态 | 含义 |
| --- | --- |
| ✅ 已支持 | 已映射并通过测试 |
| 🟡 部分 | 只有部分属性/目标/条件组合可用；其余组合会 fail-closed 并给出警告 |
| ⛔ 未支持 | 映射代码会抛出明确原因，面板显示「规则待支持」 |
| ➖ 非战斗 | 购买、升级、掉落、经验等战斗外规则，不影响行动轴；允许保留警告 |

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
| items | 2211 | 含全部品质与附魔定义 |
| skills | 498 | 战斗内外技能 |
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

- **场上道具变形必须支持**：已接入打印机、镜子、全息投影仪、水银、病毒（见阶段 6c）；变成场外卡牌/随机卡池仍不在范围。属性、附魔、槽位与新实例冷却的继承已实现但尚未与真实回放逐事件校准。
- **持续冷却缩减动态光环必须支持**：已实现，有效冷却 =（CooldownMax − 固定缩减）×（1 − 百分比缩减）；实时快照已含缩减时会先反推基础值，避免重复扣减。公式与取整仍需回放校准。
- **逐事件回放对账**：导出已完成，对比未完成。`tools/native_replay_ledger.py` 可把 `.payload.mpack.gz` 展平为帧/时间/能力 id/动作码/来源/目标/冷却变化；下一步是把开场棋盘还原进 `BattleConfig` 并逐事件比对。
- **沙暴期间纯减伤等与本次查看行动轴无关的效果按用户要求排除**；不能据此把所有沙暴触发的出手、变形或控制规则都视为无关。
- `TTargetCardOccupying` 在 BPP 的战斗归因代码中有使用，之前“不参与战斗”的结论缺少依据；是否涉及当前范围需核实具体槽位规则。
- `TCardConditionalPlayerHero` 已接入 BPP 英雄读取，但类型名存在不等于所有缺数据分支和组合语义已通过原生对账。

---

## 3. 当前实现结构

```
GameData.db ──(运行时读取)──> LiveCardEntry / LiveSkillEntry / LiveMonsterEntry
                                   │
              CardMapper.Build ────┤  卡牌基础、品质、实时属性、附魔定义
                                   │
     MonsterOpponentMapper.Apply ──┤  怪物卡组/技能，enemy: 命名空间
                                   │
        SkillMapper.Apply ─────────┤  技能：触发器 + 动作 + 静态光环
 NativeCardAbilityMapper.Apply ────┤  物品：原生 Ability/Aura 覆盖
        NativeItemMapper.Apply ────┤  8 张已校准卡的特殊顺序/过滤
       EnchantmentMapper.Apply ────┤  附魔新增 Ability
                                   ▼
                         BattleEngine.Simulate
                     Effects / Triggers / BattleDamageSeries
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

新增规则的标准步骤：

1. `NativeSkillSupport`：若涉及条件/前提，先扩展 `CardConditionSupported`/`CardMatches`/`PrerequisitesSupported`。
2. `SkillMapper.MapTrigger` / `NativeCardAbilityMapper.MapTriggers`：加事件与 Subject 映射；人物事件用 `PlayerOwner`，属性事件填 `AttributeFilter`/`ChangeType`。
3. `SkillMapper.MapValue`：新数值引用返回**运行时** `NumExpr`（`cardAttribute`/`playerAttribute`/`tagCount`/`aggregate`/`cardCount`/`range`），只把初值写进 `Value` 供面板显示。
4. `SkillMapper.MapAction`：加动作映射（返回 `Effect`，复合动作用 `Effect.Effects`）；主动能力的条件写 `Effect.Prerequisites`，出手时判定。
5. `Effects.ResolveEffect`：实现结算并发事件；属性修改统一走 `ApplyAttributeChange`/`ItemAttributes`，不要直接改共享 `CardDef`。
6. `Triggers`：事件作用域、`AttributesMatch`、玩家/卡牌主体匹配；新事件要同步 `FireOwn`/`DrainWhileDead` 的使用场景。
7. 在 `tests/` 增加至少一个正例 + 一个「不支持时必须 fail-closed」的反例。
8. 更新本文件覆盖表与进度日志。

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
| `TTriggerOnPlayerAttributeChanged` | ✅ | 阶段 3：支持 Health / Shield / Rage 的 Gain / Loss；受伤、治疗、护盾、吸收、怒气都会发事件 |
| `TTriggerOnCardAttributeChanged` | ✅ | 阶段 3：支持 Haste/Slow/Freeze/Flying 的 Gain/Loss |
| `TTriggerOnBeforeCardDestroyed` / `CardPerformedDestruction` | ✅ | 阶段 5：被摧毁卡自己的被动同步结算，观察者按帧延后 |
| `TTriggerOnCardDisabled` / `CardRepaired` | ✅ | 阶段 5：禁用/修理事件 |
| `TTriggerOnPlayerDied` | ✅ | 阶段 5：死亡结算前触发，每方一次复活机会 |
| `TTriggerOnCardSelected` | ➖ | 选牌/商店，非战斗 |
| `TTriggerOnEncounterCardsDealt/Selected/Entered/Exited` | ➖ | 遭遇界面 |
| `TTriggerOnCardSold/Purchased/Upgraded/Enchanted` | ➖ | 战斗外 |
| `TTriggerOnDayStarted/HourStarted/FightEnded` | ➖ | 战斗外 |
| `TTriggerOnSandstorm` | ⛔ | 沙暴未建模：数据库只有 `SandstormEnabled` 开关，没有伤害/步进数值 |
| `TTriggerOnCardTransformed` | ✅ | 阶段 6c：场上复制变形后发 `onCardTransformed`，主体为变形后的卡 |
| `TTriggerOnCardQuestCompleted` / `PlayerLeveledUp` / `RunCompleted` | ➖ | 非战斗 |
| `TTriggerOnPlayerDamaged/Shielded/LifeStealed` | ➖ | 数据库计数为 0 的预留类型 |

### 4.2 动作

| 原生类型 | 状态 | 说明 / 待办 |
| --- | --- | --- |
| `TActionPlayerDamage/Heal/ShieldApply/BurnApply/PoisonApply/RegenApply` | ✅ | Self/Opponent 人物目标；数值在结算时重算 |
| `TActionCardHaste/Slow/Freeze/Charge` | ✅ | 目标/数量受 §4.4 限制 |
| `TActionCardModifyAttribute` | ✅ | 阶段 3：任意属性 + Add/Subtract/Multiply/Set + 战斗内任意时长 + 目标数量；`Custom_N` 计数器可直接用 |
| `TActionCardFlyingStart` | ✅ | 物品 Ability 映射（含 TriggerTarget/Source） |
| `TActionCardFlyingStop/Toggle` | ✅ | 阶段 1/5 |
| `TActionCardReload` | ✅ | `Value` 为空表示装满 |
| `TActionPlayerBurnRemove` / `PoisonRemove` | ✅ | 空值=清空 |
| `TActionAnd` | 🟡 | 子动作逐项映射，任一不支持则整张卡回退 |
| `TActionPlayerModifyAttribute` | 🟡 | HealthMax/HealthRegen/RageMax/EnragedDurationMax；其余属性（经验等）属非战斗 |
| `TActionPlayerRageApply` | ✅ | 阶段 3：`RageApplyAmount` 默认值；满怒火自动进入暴怒 |
| `TActionPlayerReviveHeal` | ✅ | 阶段 5：复活治疗（默认 `HealAmount`） |
| `TActionCardForceUse` | ✅ | 阶段 5：立即执行一次使用（不重置冷却、不消耗弹药） |
| `TActionCardDestroy/Disable/Repair` | ✅ | 阶段 5：移除/禁用/修理；摧毁后的观察者按帧延后 |
| `TActionCardAddTagsList/Random/BySource` | ✅ | 阶段 4：战斗内额外标签，不污染共享卡定义 |
| `TActionCardTransform/TransformDestroyed` | 🟡 | 阶段 6c：场上已有道具复制（打印机/镜子/全息/水银/病毒）；`TransformDestroyed` 与场外卡池变形仍拒绝 |
| `TActionCardUpgrade/Enchant/EnchantRandom/EnchantRemove` | ➖ | 升级/附魔多数在战斗外；战斗内附魔未支持 |
| `TActionCardBeginSandstorm` | ⛔ | 沙暴未建模：没有可推导的沙暴伤害数据 |
| `TActionGame*` / `TSpawnContextQuery` | ➖ | 商店/遭遇/掉落 |
| `TActionPlayerPortrait*` / `TActionExitReplacementSet` | ➖ | 非战斗 |

### 4.3 光环动作

| 原生类型 | 状态 | 说明 |
| --- | --- | --- |
| `TAuraActionCardModifyAttribute` | ✅ | 开场静态 + 战斗内动态两层；人物条件每帧开关；持续冷却缩减可作为可逆派生层 |
| `TAuraActionPlayerModifyAttribute` | 🟡 | HealthMax/HealthRegen/RageMax/EnragedDurationMax，目标限 Self；带人物条件的动态人物光环仍 fail-closed |
| `TAuraActionCardAddTagsList` | ✅ | 阶段 4：开场静态加标签 |
| `TAuraActionCardAddTagsBySource` | ✅ | 阶段 4：从同侧其他卡复制标签 |

> 动态光环（随生命/暴怒/属性变化实时开关）已接入战斗循环：`NativeDynamicAuras.Refresh` 在每帧和每次属性变化后重建派生层，可逆属性（伤害/护盾/治疗/DoT/暴击/吸血/多重/弹药/冷却上限/`Custom_N`）支持 `Add`/`Multiply`。持续冷却缩减作为单独派生量计算有效冷却，不写入 `LiveAttributes`，避免与实时有效值重复扣减。

### 4.4 前提 / 条件 / 数值 / 时长 / 目标

| 家族 | 已支持 | 待支持（优先级） |
| --- | --- | --- |
| 前提 | `TPrerequisiteCardCount`（含 Subject=Self 的计数器）、`TPrerequisitePlayer`（Health/Enraged/Rage）、`TPrerequisiteCardAttributeComparator` | `TPrerequisiteRun`（➖ 非战斗） |
| 条件 | 任意属性的 `TCardConditionalAttribute`、Tag、HiddenTag、Size、CanCrit、Type、And/Or、Tier、Id、HasEnchantment、PlayerHero、AttributeHighest/Lowest（同侧排行）、SizeLargest/Smallest、TriggerSource | `TCardConditionalEnchantmentEligible`（➖）、`TRunConditionalCurrentDay/Hour`（➖） |
| 数值 | `TFixedValue`、`TReferenceValueCardAttribute`(+Unscaled)、`TReferenceValueCardCount`（运行时）、`TReferenceValuePlayerAttribute`(+Unscaled HealthMax)、`CardTagCount`、`CardAttributeAggregate`、`TRangeValue`、`TReferenceValueAttributeChange`（读取当前触发携带的变化量） | — |
| 时长 | `TDeterminantDuration(UntilEndOfCombat)`、`TCombatDuration` | — |
| 目标 | Self、Section(Self/ Opponent 的 Hand/Board/HandAndStash/Stash)、Random、SelfNeighbors、Positional(Self/TriggerSource 的 Neighbor/Left/Right)、TriggerSource、TriggerTarget、PlayerRelative、PlayerAbsolute、Player(Opponent)、XMost(Left/Right) | `TTargetCardOccupying`（槽位效果待核实）、尚未覆盖的 `Absolute*` 区域 |

---

## 5. 阶段计划

- [x] **阶段 0：规则清单工具与基线** — `tools/rule_inventory.py`，输出 349 个类型组合
- [x] **阶段 1：战斗事件与常用动作** — BeforeUse、飞行开始/结束、伤害/装填/充能/过量治疗触发；Reload、FlyingStop、Burn/Poison 清除、TActionAnd
- [x] **阶段 2：条件与目标扩展** — Tier、Id、HasEnchantment、同侧最高/最低、XMost（最左/最右）
- [x] **阶段 2b：数值引用（需要运行时上下文）** — 运行时 `NumExpr`：CardAttribute、PlayerAttribute、CardCount、TagCount、CardAttributeAggregate、Range；`TriggerSource` 条件与 `TriggerTarget` 目标
- [x] **阶段 3：玩家属性与怒气** — PlayerModifyAttribute（HealthMax/Regen/RageMax/EnragedDuration）、Rage/RageMax、Enraged 触发、Player/Card AttributeChanged、通用 ModifyAttribute 与 `Custom_N` 计数器
- [x] **阶段 4：光环与标签** — 静态 AddTags 光环、战斗内 AddTagsList/Random/BySource；动态光环在阶段 6 接入
- [x] **阶段 5：战斗内生命周期** — Destroy、Disable、Repair、ForceUse、FlyingToggle、死亡触发与一次复活；Transform/Sandstorm 保留 ⛔
- [ ] **阶段 6：运行时收尾（部分完成）** — 部分动态光环、变化量数值和怒气事件已接入；场上复制变形、持续冷却缩减仍待实现，不能标记全阶段完成
- [x] **阶段 6b：英雄身份读取接线** — BPP snapshot 的 `Hero` 接入；条件语义与缺数据回退还需原生验证
- [x] **阶段 6c：场上变形与持续冷却缩减** — `TActionCardTransform`（打印机/镜子/全息/水银/病毒）、`onCardTransformed`、可逆冷却缩减派生层（含实时值反推）
- [ ] **阶段 7：真实回放对账（未完成）** — 现有完整 BPP 回放包含事件，具备开展条件，不列为可选收尾

### 下一步实现与验证

1. 先从完整 BPP 回放导出帧事件、逐卡冷却变化及初始棋盘，区分游戏版本、实例 ID 与开场状态。
2. 校准持续冷却缩减的有效 CD、叠加及条件开关行为，加入具体出手时间断言。
3. 实现场上复制变形（打印机为首例）：来源筛选、多个新实例、槽位、复制属性/附魔、旧施放队列失效、新冷却以及行动轴历史展示。
4. 对齐双方初始状态后比较事件时间、来源、目标、数值；随机目标和同帧归因不明确时报告差异或未知，不冒称逐事件完全一致。

---

## 6. 已知限制 / 交接注意

1. **怪物数据必须来自原生模板**：本地 `cards.json` 只是显示定义，不能用它补怪物数值；缺卡时应回退原生 Ability。
2. **附魔没有独立本地库**：只存在于卡牌模板的 `Enchantments` 节点，运行时读取；离线只能用 `rule_inventory.py` 统计。
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
    - 摧毁的卡保留在 `ctx.Items` 但打 `Destroyed` 标记，所有目标/快照/冷却都跳过；不要从列表删除，否则触发遍历会失效。
    - `onBeforeDestroy` 同步结算，`onCardDestroyed` 入队按帧延后；复活每方只尝试一次。
    - `ForceUse` 不重置冷却、不消耗弹药；如后续回放显示原生会重置，再单独校准。
13. **阶段 6 的实现边界**：
    - 动态光环已接入：`NativeDynamicAuras.Initialize` 在 `InitBattle` 首次构建，`Refresh` 在每帧与每次 `ResolveEffect` 前后重建派生层；它只写 `InternalItem.AuraAttributes`，**不**改共享 `CardDef.LiveAttributes`。
    - 人物条件（生命/暴怒/怒气）已允许：`Active()` 每帧调 `PrerequisitesMatch`；带人物条件的卡牌光环只走运行时层，不再作为开场固定加成。
    - 持续冷却缩减（`Flat/PercentCooldownReduction`）当前仍被拒绝，原因是尚未完成有效冷却与进度的分层计算和校准，不是原理上不可撤销。
    - 动态人物光环（`TAuraActionPlayerModifyAttribute` + 人物前提）仍 fail-closed，因为人物属性归 `BattleConfig`/`InternalHero`，没有可撤销的派生层。
    - 技能光环现在会写到 `CardDef.NativeRuntimeAuras` 并把技能放进 `Player/EnemySkills`；`ApplyStaticAuras` 结尾会重写 `NativeRuntimeAuras`，设置必须在它之后。
    - 光环数值引用 `TTargetCardSelf` 时直接读来源卡：技能不在 `ctx.Items` 里，走目标集只会得到 0。

---

## 7. 进度日志

> 每批完成后追加：日期 / 范围 / 改动文件 / 新增测试 / 验证结果 / 剩余风险。

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
