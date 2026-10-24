# 原生规则类型清单

用途：列出 `GameData.db` 中物品、技能、附魔、怪物实际使用的全部 `$type`，
作为「支持所有规则」的覆盖率基线。由 `python tools/rule_inventory.py` 生成。

## 模板数量

| 来源 | 模板数 |
| --- | --- |
| items | 2211 |
| skills | 498 |
| monsters | 166 |
| other | 127 |

## 触发器（trigger）

| 类型 | 物品 | 技能 | 附魔 | 怪物 | 映射代码出现 |
| --- | --- | --- | --- | --- | --- |
| `TTriggerOnCardFired` | 1321 | 3 | 6236 | 0 | 是 |
| `TTriggerOnItemUsed` | 274 | 227 | 916 | 0 | 是 |
| `TTriggerOnCardSelected` | 621 | 4 | 0 | 0 |  |
| `TTriggerOnCardSold` | 137 | 6 | 440 | 0 | 是 |
| `TTriggerOnCardCritted` | 42 | 11 | 183 | 0 | 是 |
| `TTriggerOnPlayerAttributeChanged` | 54 | 75 | 74 | 0 | 是 |
| `TTriggerOnPlayerEnraged` | 47 | 9 | 136 | 0 | 是 |
| `TTriggerOnEncounterCardsDealt` | 180 | 0 | 0 | 0 |  |
| `TTriggerOnCardPerformedSlow` | 62 | 23 | 91 | 0 | 是 |
| `TTriggerOnBeforeCardDestroyed` | 41 | 3 | 71 | 0 | 是 |
| `TTriggerOnCardPerformedFreeze` | 38 | 12 | 61 | 0 | 是 |
| `TTriggerOnDayStarted` | 59 | 3 | 48 | 0 | 是 |
| `TTriggerOnCardAttributeChanged` | 44 | 3 | 54 | 0 | 是 |
| `TTriggerOnFightStarted` | 26 | 17 | 55 | 0 | 是 |
| `TTriggerOnBeforeItemUsed` | 29 | 1 | 65 | 0 | 是 |
| `TTriggerOnCardPerformedBurn` | 38 | 17 | 16 | 0 | 是 |
| `TTriggerOr` | 36 | 9 | 27 | 0 | 是 |
| `TTriggerOnCardPerformedHaste` | 46 | 13 | 11 | 0 | 是 |
| `TTriggerOnCardStartedFlying` | 23 | 2 | 44 | 0 | 是 |
| `TTriggerOnCardStoppedFlying` | 17 | 0 | 48 | 0 | 是 |
| `TTriggerOnCardPurchased` | 42 | 4 | 1 | 0 | 是 |
| `TTriggerOnPlayerDied` | 17 | 9 | 36 | 0 | 是 |
| `TTriggerOnCardPerformedPoison` | 26 | 11 | 13 | 0 | 是 |
| `TTriggerOnEncounterSelected` | 41 | 2 | 0 | 0 |  |
| `TTriggerOnFightEnded` | 35 | 2 | 0 | 0 | 是 |
| `TTriggerOnCardPerformedHeal` | 11 | 5 | 10 | 0 | 是 |
| `TTriggerOnCardPerformedRegen` | 8 | 6 | 13 | 0 | 是 |
| `TTriggerOnCardPerformedDestruction` | 9 | 2 | 9 | 0 | 是 |
| `TTriggerOnCardPerformedShield` | 8 | 5 | 7 | 0 | 是 |
| `TTriggerOnCardUpgraded` | 10 | 4 | 0 | 0 | 是 |
| `TTriggerOnPlayerEnrageEnded` | 7 | 4 | 8 | 0 | 是 |
| `TTriggerOnCardTransformed` | 4 | 0 | 12 | 0 |  |
| `TTriggerOnHourStarted` | 11 | 2 | 1 | 0 | 是 |
| `TTriggerOnCardPerformedDamage` | 2 | 0 | 8 | 0 | 是 |
| `TTriggerOnCardRepaired` | 1 | 0 | 8 | 0 | 是 |
| `TTriggerOnCardPerformedReload` | 4 | 0 | 0 | 0 | 是 |
| `TTriggerOnCardDisabled` | 0 | 3 | 0 | 0 | 是 |
| `TTriggerOnCardEnchanted` | 0 | 0 | 0 | 0 | 是 |
| `TTriggerOnEncounterEntered` | 3 | 0 | 0 | 0 |  |
| `TTriggerOnEncounterExited` | 3 | 0 | 0 | 0 |  |
| `TTriggerOnSandstorm` | 2 | 1 | 0 | 0 |  |
| `TTriggerOnCardPerformedCharge` | 0 | 0 | 0 | 0 | 是 |
| `TTriggerOnCardPerformedOverHeal` | 0 | 2 | 0 | 0 | 是 |
| `TTriggerOnCardQuestCompleted` | 2 | 0 | 0 | 0 |  |
| `TTriggerOnCardStartsFlying` | 1 | 0 | 0 | 0 | 是 |
| `TTriggerOnPlayerDamaged` | 0 | 0 | 0 | 0 |  |
| `TTriggerOnPlayerLeveledUp` | 0 | 0 | 0 | 0 |  |
| `TTriggerOnPlayerLifeStealed` | 0 | 0 | 0 | 0 |  |
| `TTriggerOnPlayerShielded` | 0 | 0 | 0 | 0 |  |
| `TTriggerOnRunCompleted` | 0 | 0 | 0 | 0 |  |

## 动作（action）

| 类型 | 物品 | 技能 | 附魔 | 怪物 | 映射代码出现 |
| --- | --- | --- | --- | --- | --- |
| `TActionCardModifyAttribute` | 835 | 236 | 813 | 0 | 是 |
| `TActionCardHaste` | 107 | 31 | 921 | 0 | 是 |
| `TActionCardFreeze` | 65 | 16 | 954 | 0 | 是 |
| `TActionCardSlow` | 88 | 14 | 929 | 0 | 是 |
| `TActionPlayerPoisonApply` | 74 | 14 | 896 | 0 | 是 |
| `TActionPlayerBurnApply` | 138 | 14 | 825 | 0 | 是 |
| `TActionPlayerShieldApply` | 141 | 8 | 826 | 0 | 是 |
| `TActionPlayerRegenApply` | 65 | 14 | 894 | 0 | 是 |
| `TActionPlayerDamage` | 352 | 5 | 612 | 0 | 是 |
| `TActionPlayerHeal` | 56 | 6 | 904 | 0 | 是 |
| `TSpawnContextQuery` | 380 | 10 | 53 | 0 |  |
| `TActionPlayerModifyAttribute` | 289 | 13 | 7 | 0 | 是 |
| `TActionCardCharge` | 237 | 48 | 0 | 0 | 是 |
| `TActionGameSpawnCards` | 170 | 10 | 53 | 0 |  |
| `TActionGameDealCards` | 168 | 0 | 0 | 0 |  |
| `TActionCardFlyingStart` | 69 | 8 | 0 | 0 | 是 |
| `TActionCardUpgrade` | 61 | 8 | 0 | 0 |  |
| `TActionCardDisable` | 51 | 5 | 0 | 0 | 是 |
| `TActionCardEnchant` | 39 | 1 | 12 | 0 |  |
| `TActionCardTransform` | 38 | 0 | 0 | 0 |  |
| `TActionPlayerRageApply` | 29 | 9 | 0 | 0 | 是 |
| `TActionCardForceUse` | 17 | 3 | 10 | 0 | 是 |
| `TActionCardReload` | 24 | 5 | 0 | 0 | 是 |
| `TActionCardEnchantRandom` | 21 | 5 | 0 | 0 |  |
| `TActionCardAddTagsList` | 18 | 0 | 0 | 0 | 是 |
| `TActionCardDestroy` | 18 | 0 | 0 | 0 | 是 |
| `TActionCardFlyingStop` | 12 | 1 | 0 | 0 | 是 |
| `TActionCardRepair` | 11 | 2 | 0 | 0 | 是 |
| `TActionCardFlyingToggle` | 11 | 0 | 0 | 0 | 是 |
| `TActionPlayerBurnRemove` | 6 | 4 | 0 | 0 | 是 |
| `TActionAnd` | 9 | 0 | 0 | 0 | 是 |
| `TActionExitReplacementSet` | 9 | 0 | 0 | 0 |  |
| `TActionGameReroll` | 6 | 0 | 0 | 0 |  |
| `TActionPlayerPoisonRemove` | 2 | 4 | 0 | 0 | 是 |
| `TActionPlayerReviveHeal` | 4 | 2 | 0 | 0 | 是 |
| `TActionCardTransformDestroyed` | 4 | 0 | 0 | 0 |  |
| `TActionCardAddTagsBySource` | 2 | 0 | 0 | 0 | 是 |
| `TActionCardBeginSandstorm` | 2 | 0 | 0 | 0 |  |
| `TActionCardAddTagsRandom` | 1 | 0 | 0 | 0 | 是 |
| `TActionCardEnchantRemove` | 1 | 0 | 0 | 0 |  |
| `TActionPlayerPortraitNext` | 1 | 0 | 0 | 0 |  |
| `TActionPlayerPortraitReset` | 1 | 0 | 0 | 0 |  |

## 光环动作（aura_action）

| 类型 | 物品 | 技能 | 附魔 | 怪物 | 映射代码出现 |
| --- | --- | --- | --- | --- | --- |
| `TAuraActionCardModifyAttribute` | 674 | 360 | 7746 | 0 | 是 |
| `TAuraActionPlayerModifyAttribute` | 27 | 33 | 2 | 0 | 是 |
| `TAuraActionCardAddTagsList` | 9 | 14 | 1 | 0 | 是 |
| `TAuraActionCardAddTagsBySource` | 5 | 0 | 0 | 0 | 是 |

## 前提条件（prerequisite）

| 类型 | 物品 | 技能 | 附魔 | 怪物 | 映射代码出现 |
| --- | --- | --- | --- | --- | --- |
| `TPrerequisiteCardCount` | 392 | 199 | 183 | 0 | 是 |
| `TPrerequisitePlayer` | 67 | 84 | 70 | 0 | 是 |
| `TPrerequisiteRun` | 49 | 0 | 0 | 0 |  |
| `TPrerequisiteCardAttributeComparator` | 3 | 0 | 0 | 0 | 是 |

## 卡牌条件（condition）

| 类型 | 物品 | 技能 | 附魔 | 怪物 | 映射代码出现 |
| --- | --- | --- | --- | --- | --- |
| `TCardConditionalAttribute` | 764 | 332 | 2264 | 0 | 是 |
| `TCardConditionalTag` | 658 | 347 | 793 | 0 | 是 |
| `TCardConditionalAnd` | 430 | 145 | 508 | 0 | 是 |
| `TCardConditionalHiddenTag` | 201 | 119 | 647 | 0 | 是 |
| `TCardConditionalCanCrit` | 81 | 46 | 409 | 0 | 是 |
| `TPlayerConditionalAttribute` | 65 | 81 | 70 | 0 | 是 |
| `TCardConditionalId` | 206 | 0 | 2 | 0 | 是 |
| `TCardConditionalSize` | 107 | 84 | 11 | 0 | 是 |
| `TCardConditionalOr` | 68 | 12 | 114 | 0 | 是 |
| `TCardConditionalTier` | 102 | 10 | 28 | 0 | 是 |
| `TCardConditionalHasEnchantment` | 74 | 6 | 64 | 0 | 是 |
| `TCardConditionalEnchantmentEligible` | 87 | 0 | 0 | 0 |  |
| `TCardConditionalType` | 33 | 10 | 3 | 0 | 是 |
| `TRunConditionalCurrentDay` | 48 | 0 | 0 | 0 |  |
| `TCardConditionalPlayerHero` | 8 | 1 | 12 | 0 | 是 |
| `TCardConditionalAttributeHighest` | 5 | 11 | 3 | 0 | 是 |
| `TCardConditionalTriggerSource` | 11 | 2 | 0 | 0 | 是 |
| `TCardConditionalAttributeLowest` | 2 | 2 | 5 | 0 | 是 |
| `TCardConditionalSizeLargest` | 2 | 0 | 0 | 0 | 是 |
| `TCardConditionalSizeSmallest` | 1 | 0 | 0 | 0 | 是 |
| `TRunConditionalCurrentHour` | 1 | 0 | 0 | 0 |  |

## 数值引用（value）

| 类型 | 物品 | 技能 | 附魔 | 怪物 | 映射代码出现 |
| --- | --- | --- | --- | --- | --- |
| `TFixedValue` | 3113 | 807 | 10908 | 0 | 是 |
| `TReferenceValueCardAttribute` | 1007 | 512 | 3461 | 0 | 是 |
| `TReferenceValuePlayerAttribute` | 183 | 114 | 177 | 0 | 是 |
| `TReferenceValueCardCount` | 74 | 36 | 19 | 0 | 是 |
| `TRangeValue` | 78 | 0 | 0 | 0 | 是 |
| `TReferenceValueCardTagCount` | 14 | 0 | 7 | 0 | 是 |
| `TReferenceValuePlayerAttributeUnscaled` | 19 | 0 | 0 | 0 | 是 |
| `TReferenceValueCardAttributeUnscaled` | 13 | 1 | 4 | 0 | 是 |
| `TReferenceValueCardAttributeAggregate` | 11 | 3 | 0 | 0 | 是 |
| `TReferenceValueAttributeChange` | 11 | 1 | 0 | 0 | 是 |

## 持续时间（duration）

| 类型 | 物品 | 技能 | 附魔 | 怪物 | 映射代码出现 |
| --- | --- | --- | --- | --- | --- |
| `TDeterminantDuration` | 555 | 251 | 1271 | 0 | 是 |
| `TCombatDuration` | 25 | 0 | 6 | 0 | 是 |

## 目标（target）

| 类型 | 物品 | 技能 | 附魔 | 怪物 | 映射代码出现 |
| --- | --- | --- | --- | --- | --- |
| `TTargetCardSelf` | 2698 | 952 | 11236 | 0 | 是 |
| `TTargetPlayerRelative` | 1275 | 414 | 5474 | 0 | 是 |
| `TTargetCardSection` | 1161 | 669 | 1665 | 0 | 是 |
| `TTargetCardRandom` | 371 | 122 | 2769 | 0 | 是 |
| `TTargetCardPositional` | 411 | 17 | 976 | 0 | 是 |
| `TTargetCardXMost` | 144 | 80 | 472 | 0 | 是 |
| `TTargetPlayerAbsolute` | 475 | 17 | 59 | 0 | 是 |
| `TTargetCardTriggerSource` | 117 | 25 | 42 | 0 | 是 |
| `TTargetCardTriggerTarget` | 12 | 6 | 0 | 0 | 是 |
| `TTargetPlayer` | 3 | 0 | 0 | 0 | 是 |
| `TTargetCardOccupying` | 2 | 0 | 0 | 0 |  |
