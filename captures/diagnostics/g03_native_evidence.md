# G03 触发来源／受体、次数与帧序原生证据

- 建立日期：2026-10-24
- 数据库源：`D:/Program Files (x86)/Steam/steamapps/common/The Bazaar/TheBazaar_Data/StreamingAssets/GameData.db.zip`
- 数据库 ZIP SHA256：`FDFD3087E6A23EDA2C9F53BE68B012006CC949D03E8666DD51AD65ABC71B61E5`
- 工作区解压库：`vendor/GameData/GameData.db`，SHA256 `2B9C6B3765C72A21A5EBFB7336C13251D09017B87C0E599671B14299800BA092`
- 种子夹具：`tools/actionaxis_plugin/tests/fixtures/native_g03_cards.json`，13 个模板，160135 bytes，SHA256 `C94442288CDECFFA599443FDBBF652B935C1F94CC3672FB6757EBE2C18468268`
- 全量夹具：`tools/actionaxis_plugin/tests/fixtures/native_g03_all.json`，45 个 G03 主责模板，522204 bytes，SHA256 `A0CCC63068CE9743755ACB05ED38B18C0115952A37D5C8FD3A079A00F7746CD9`
- FightStarted 辅助夹具：`tools/actionaxis_plugin/tests/fixtures/native_g03_support.json`，Repair Drone 完整模板，13949 bytes，SHA256 `1D0D3D206FA7551E3289FE4B2076C853876FD15D24EAEF5DF3211B0985FC7F98`
- 程序集：
  - `BazaarGameShared.dll` SHA256 `C51716EA3EC8C43009EF244A8477F88C14D836EFEA565B85459E420223A8947`
  - `BazaarBattleService.dll` SHA256 `E95E614BFF9E60D3E73B89779554E183677F47FE5DDCE7C18262CAE2FFEFE987`
- 边界：以下证据是原生模板、局部工具提示与只读程序集 IL，不是游戏回放；逐事件数值／随机目标仍未对账。

## 1. 卡片规则路径

| 卡 | 模板 ID | 原生路径与已确认语义 |
| --- | --- | --- |
| Hunting Hawk | `0941d7dd-1264-42c5-b334-6cc5d694571b` | `Abilities/1` CardFired→Haste，目标 `TTargetCardRandom/SelfHand`，条件 `CooldownMax>0 AND Flying==0`；`Abilities/2` `TTriggerOnCardPerformedHaste`，`Subject=SelfBoard`、`Target=SelfHand`，动作 `TActionCardFlyingStart` 目标是 `TTargetCardTriggerTarget`（再校验 `CooldownMax>0`）；`Auras/3` 只给 `SelfHand` 中 `CooldownMax>0 AND Flying==1 AND CanCrit` 的卡加 `Custom_0` CritChance。工具提示：“Haste a non-Flying item”“When you Haste an item, it starts Flying”。 |
| Boosted Saucer | `0f96e2e4-2bd8-4fcf-aaaf-e4e0ee5cb627` | `Abilities/2` `OnCardRepaired/OnCardTransformed` 的 `Subject=SelfHand`、`Source=SelfBoard`；`Abilities/3` `TTriggerOnBeforeCardDestroyed` 同样 `Subject=SelfHand`、`Source=SelfBoard`。工具提示：“When you Repair or Transform in combat…”“When you Destroy an item…”。 |
| Wolf Mask | `22c8b252-e36d-4564-9588-06febd4b358d` | `Abilities/0` OnItemUsed，`Subject=AllHands/ExcludeSelf=true`，前置 `Custom_1==0`，Priority Medium；`Abilities/4` 同触发，前置 `Custom_1==0`，Priority Immediate，动作 `Custom_1 += 1`。工具提示：“The first time any item is used each fight, gain Rage”。 |
| Bloodhound | `13989ad3-2122-4d86-a323-bb90fda8360b` | `Abilities/0` `TTriggerOnPlayerAttributeChanged(Health,Loss, Subject=Opponent)`，前置 `Custom_0==0` 与敌方 `Health < HealthMax*0.5`，Priority Medium；`Abilities/1` 同触发，Priority Immediate，`Custom_0 += 1`。工具提示：“The first time an enemy falls below half Health each fight…”。 |
| Frozen Secrets | `1252064a-3bf4-478e-9326-946677ecc8b3` | `Abilities/0` OnItemUsed `SelfHand Relic ExcludeSelf`，前置 `Custom_0 < Custom_1`，Priority Medium，冻结 `OpponentHand CooldownMax>0`；`Abilities/1` 同触发、Priority Immediate，`Custom_0 += 1`；Gold `Custom_1=3`，Diamond `Custom_1=6`。`Auras/9` 的 `InternalName=Tooltip Handling`，未被任何品质 `AuraIds` 引用，只服务工具提示 `{aura.9}`。 |
| Counterstrike | `c5bff6ad-5d13-4cf3-9941-98f3cf913949` | `Abilities/0` OnItemUsed `OpponentHand Weapon`，前置 `Custom_0 < Custom_1`，Priority Medium，充能 `SelfHand CooldownMax>0`；`Abilities/1` 同触发、Priority Immediate，`Custom_0 += 1`；Gold `Custom_1=1`。工具提示：“The first time an enemy uses a Weapon…”。 |
| Retool | `fb409226-bf38-4275-b775-7fab879a1d6f` | `Abilities/0` OnItemUsed `SelfHand Tool`，动作 `TActionCardReload`，目标 `TTargetCardRandom/TargetSection=TriggerSourceNeighbors`，条件 `AmmoMax>0`。工具提示：“When you use a Tool, Reload an adjacent item”。 |
| Propeller Hat | `0e3d2e2c-0f2d-4150-b6d6-d1c688d7427c` | `Abilities/1` `TTriggerOnBeforeItemUsed`，`Subject=SelfHand Flying==1`，动作 Haste `SelfHand CooldownMax>0`。用于验证使用前被动在同一帧、主动效果之前结算。 |

## 2. 优先级与前置快照

- `BazaarGameShared.Domain.Core.Types.EEffectPriority` 字段顺序（dnfile 读取）：
  `Lowest, Low, Medium, High, Highest, Immediate`。
- `BazaarBattleService.dll` 的 `BazaarCombatEffectPriorityDelayObjectListExtensions.ProcessEffectsInOrder` IL 从优先级列表末端向前遍历（`count-1` 递减到 0），与“Immediate 先、Lowest 后”一致。
- 因此同一事件中，`Immediate` 的计数能力会先于 `Medium` 的主效果执行。为了让“每战第一次”类前置不被本次计数提前改写，引擎在 `Triggers.ResolvePayload` 中先对同一张卡的全部触发完成事件／主体／受体／前置判定，再按优先级执行；`G03TriggerRuleTests` 的 Wolf Mask、Bloodhound、Frozen Secrets、Counterstrike 用例锁定该契约。
- 帧序：`onBeforeUse` 在主动效果前同步 drain；普通被动（如 Hunting Hawk 的 `onHaste`）留到下一原生帧（50 ms）。测试断言 Hunting Hawk 的 `haste` 在 `4.0s`、`fly` 在 `4.05s`，Propeller Hat 的 before-use haste 与主动伤害同在 `4.0s` 且顺序在前。

## 3. 45 模板全量结论

- `G03FullCoverageTests.AllG03TemplatesMapOrAreDataBlocked`：45 个模板 × 声明品质 × 双方全部走实际映射入口；29 物品中 5 个模板保留可见数据 warning（4 张缺 AbilityId、Resonance Crystal 缺场外 AuraId 0），其余战斗卡 0 warning；16 技能全部 `simulated`／`inactive`／`out-of-combat`，无 unsupported。矩阵当前为 6690 mapped／1800 mapped-warning／0 unsupported。
- DATA（缺 `AbilityIds` 定义，保留拒绝，不补造）：Fortune Cookie `1a0c645a…`、Sulphur `6693a3f1…`、Shard of Obsidian `8981892b…`、Wild Quillback `dd789cd1…`。
- OUT+DATA：Resonance Crystal `64303ae9…` 的 `Auras` 为空但各品质 `AuraIds` 引用 `"0"`；其唯一主动触发是 CardSold，属于场外，故不构成战斗缺口，但缺失光环定义仍记录给 G00/G11，不能补造。
- 场外：Machine Learning `d1e825ba…` 与 Renovation `ec327a59…` 的 `TTriggerOnCardSelected`／Level／Upgraded 触发归 OUT；`IsNonCombatTrigger` 已接受 `TTriggerOnCardSelected`，因此不再被 `TActionCardUpgrade` 误判为战斗缺口。Advanced Synthetics、Learning Crystal、Pinata、Safe、Reroll Token 的出售/升级/重掷分支同样不进入战斗模拟。
- 机制族：OnItemUsed 的 SelfHand/OpponentHand/AllHands、Tag/Size/AmmoMax、ExcludeSelf；CardPerformed Burn/Poison/Slow/Freeze/Haste；FightStarted（Happy Accident 的 Drone/Vehicle、Experimental Chef 的 Food/Tool 随机附魔）；TriggerOr；TPrerequisiteCardCount/Player、Heated 前提；BeforeItemUsed；BeforeCardDestroyed；Repair/Transform Actor；TriggerSourceNeighbors。每个机制族至少一条真实模板行为断言。
- 两张 `[DEBUG]` 卡（Disenchant、Temporary Enchantment）只做入口映射与 CardFired 触发识别；其 `TActionCardEnchantRemove/Enchant` 具体效果归 G07，未在本轮做行为断言。
- 全量分账：30 张战斗卡有行为断言；8 张纯 OUT（Pinata、Safe、Learning Crystal、Reroll Token、Advanced Synthetics、Book of Secrets、Machine Learning、Renovation）；1 张 OUT+DATA（Resonance Crystal）；4 张 DATA（Fortune Cookie、Sulphur、Shard of Obsidian、Wild Quillback）；2 张 DEBUG 入口/G07。合计 45。

## 4. 回放核对

- 文件：`CombatReplays/CombatReplays/04775112acd94dfaa7e9f03c4fc2995c.payload.mpack.gz`
- SHA256：`63fb461a94a23f1c5fb503297c8b81aa440cb603d099c1da3645db24c24b98b0`
- 敌方 Hunting Hawk 实例 `SYgFX7R`，模板 `0941d7dd-1264-42c5-b334-6cc5d694571b`：
  - frame 80 / 4.0s：`CardHaste`（EffectId 1）目标 `-ga9vyi`；
  - frame 81 / 4.05s：`FlyingStart`（EffectId 2）目标同一 `-ga9vyi`；
  - frame 80 / 4.0s 另有 `PlayerDamage`，说明主动伤害与 haste 同帧、飞行被动在下一帧。
- 该序列与 `HuntingHawkActiveHasteFlightsOnFollowingFrame` 的 4.0→4.05 断言一致；回放只证明这一条关键路径，不能替代全部 45 模板的逐事件对账。
- 已扫描 200 份回放，58 份包含至少一个 G03 主责模板；索引保存在 `tmp_g03/replay_g03_primary_index.json`（辅助文件，未纳入构建）。

## 5. 复现命令（只读）

```powershell
python tmp_g03/evidence_report.py
python tmp_g03/disasm_priority.py
```

上述脚本是本次取证辅助文件，未纳入构建；稳定哈希与结论以本文件为准。
