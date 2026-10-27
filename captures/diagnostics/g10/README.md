# G10 回放覆盖与账本证据（2026-10-25）

本目录只保存只读统计，不包含回放文件、账号信息或部署动作。

## 范围

- 扫描 `CombatReplays/CombatReplays` 共 200 份 BPP 回放。
- 其中 16 份包含 `primary_group=G10` 的卡牌；另加入游戏目录中的
  `084bd840e98c4b81a681cb8d30c7b72f`，合计 17 份 G10 卡样本。
- 覆盖卡牌：Trained Spider、Soldering Gun、Blow Torch、Rune Axe、Bandages、
  Walter Cooler、Shard of Obsidian。
- 六类人物动作在 200 份回放中均出现：PlayerDamage、PlayerBurnApply、
  PlayerPoisonApply、PlayerShieldApply、PlayerHeal、PlayerRegenApply。
- 明细：`replay_corpus.json`；校验：`python -m unittest tools.test_g10_replay_corpus -q`。
- 来源归因明细：`source_attribution_084.json`；校验：`python -m unittest tools.test_g10_source_attribution -q`。

## 已对账

- 玩家侧：`084bd840` 4.0s 直伤 30；末帧 TotalDamage 402（直伤 270 + 毒 132）、
  Overkill 2、NetHpLoss 400、HP 0。玩家 `HealthMax/Shield/Regen` 在 Spawn 和
  Opening 侧车均缺失，未伪造。
- 敌方侧：末帧 TotalDamage 323（Poison 322 + Burn 1）、ShieldAbsorbed 1、
  NetHpLoss 322、HP 78。
- 低层燃烧：1 层 Burn 在 50 盾后不衰减；2 层 Burn 打掉 1 盾后才衰减为 1。

## 来源归因（084bd840）

- 13 次直伤全部能在同帧唯一匹配到 `PlayerDamage` 来源，13/13 标为 `verified`。
- 22 次状态/护盾应用有明确来源，例如 Soldering Gun 施加 Burn/Poison、Trained Spider 施加 Poison。
- 30 次 Poison/Burn 扣血 tick 没有逐 tick 来源字段，全部标为
  `unavailable-native-no-tick-source`，不把应用来源冒充 tick 来源。
- 明细：`source_attribution_084.json`。

## 不能从载荷证明的

- 健康调整没有逐 tick 伤害来源字段。`PlayerBurnApply/PlayerPoisonApply` 可以
  证明“谁施加了状态”，不能证明每个 tick 的份额；多来源 DoT 份额仍是模拟器
  按池推导，不能写成原生已验证。
- 200 份回放都没有 `CombatSimEventEffectTriggered` 被动触发事件；被动目标
  路径不能逐事件对账。
- 200 份回放的 SpawnMessage 都没有玩家 `HealthMax/Shield/Regen`；玩家治疗
  与最大生命变化只能用显式假设，不能用战后值替代。
- `084bd840` 的 Virus 变形目标顺序与当前模拟器不同（原生 8s 先 slot4，模拟器
  先 slot2），导致后续 Burn/Poison 层数和道具冷却仍有 DIFF；归属 G08/G12。
