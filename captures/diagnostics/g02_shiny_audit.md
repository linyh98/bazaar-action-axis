# G02 Shiny 隐含 Multicast 全量审计（2026-10-25）

数据源：`tools/actionaxis_plugin/build/native_mapping_templates.jsonl`（5.0.0 原生模板）。
审计器：`tmp_g02/AuditShiny2.exe`（当前 engine；纯模板 + Shiny，单卡映射/InitBattle）。

## 数量

- 带 Shiny 附魔的物品：1074。
- Shiny 定义**没有** `Multicast` 光环的物品：大部分是“把复制品附魔为 Shiny”的特殊卡；本审计不计。
- Shiny 定义**有** `Multicast Add 1`、且基础品质没有显式 `Multicast` 字段的物品：54。
  - 其中有正冷却、自身/邻居 `Multicast` 光环可观察的活跃物品：41。
  - 其余 13 个是纯被动或只给邻居上光环；单卡审计没有邻居，不能据此判失败。

## 结果

41 个活跃物品中：

- 40 个在 `InitBattle` 后 `MulticastBonus = 1`，符合“隐含基底 1 + Shiny 1 = 2 次”。
- 1 个例外：**Hacksaw** `d50bb808-14f8-4554-950d-d0630899ba85`，仍是 0。
  - 原因不是 Multicast 基底，而是它的 `TActionCardTransform`＋`TSpawnContextQuery` 在 G08 变形映射中失败，整卡 partial，Shiny 光环没有落地。
  - Hacksaw 的主责是 G08，不是 G02；G02 只记录该关联风险。
- 另有两个关联卡同样因整卡 mapping 失败丢失 Shiny +1：`Blender` `0b13735f-…`（G08 变形）、`[DEBUG] Peacewrought` `f4a7f5d3-…`（G09 生成动作）。三者都不在 G02 主责范围，不能靠改 Multicast 基底修复。

## 原生回放抽样

- `e8e2fc61` 的 Waterskin（Shiny，无显式 Multicast）：开场 `Multicast=2`；4.00/4.25s 两次充能，8.00/8.25s 两次充能。
- 被动 Shiny 卡（Radar Module、Rapid Injection System）在回放中的实时 `Multicast=1`，但它们不产生施放，不影响出手时间；本审计不要求它们为 2。

## 残留基底 bug（已修）

子代理审计发现：`NativeDynamicAuras.Initialize` 在实时属性缺 `Multicast` 时先补隐含基底 1，随后“实时值反推”又把这 1 减成 0，导致 Dryad/Combat Core 在 `AttributesAreLive=true` 且快照无 `Multicast` 时只施放 1 次。

修复：`Initialize` 记录 `seededMulticast[item] = seed.ContainsKey("Multicast")`；反推循环遇到 `Multicast` 且 seed 原本没有该键时跳过，不再减隐含基底。回归 `LiveShinyWithoutSnapshotMulticastKeepsBase`：实时 Dryad Shiny 无 `Multicast` 快照仍为 `MulticastBonus=1`、5.00/5.25 两次施放。

## 结论

- G02 的“无显式 Multicast 的 Shiny 卡从 1 起算”修复对 40/41 个可观察活跃卡成立。
- 剩余 Hacksaw 是 G08 变形能力缺口，不能靠改 Multicast 基底解决。
- 邻居型 Shiny（Grill、Attachment Set、Streaming Setup 等）需要真实邻居才能触发，单卡审计不作为失败；后续 G07 可在多卡棋盘补行为回归。
