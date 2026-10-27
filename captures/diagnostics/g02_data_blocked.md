# G02 仍受阻物品：缺失能力定义（DATA）

截至 2026-10-25 当前工作树，G02 主责 127 物品中 121 个全映射；剩余 6 个全部是**原生模板引用了不存在的 AbilityId**，不是目标、状态或时序代码可修的缺口。

数据源：`GameData.db.zip` SHA256 `FDFD3087E6A23EDA2C9F53BE68B012006CC949D03E8666DD51AD65ABC71B61E5`；模板来自 `tools/actionaxis_plugin/build/native_mapping_templates.jsonl`。

| 物品 | 模板 ID | 缺失 AbilityId | 已有定义 | 结论 |
| --- | --- | --- | --- | --- |
| Cruise Ship | `3132efea-ee79-4b99-b380-d0df3bf1df88` | `3` | `0,1,2,4` | DATA：不能补造 |
| Marlon | `3f867017-c026-412d-99f7-67ca26092e7a` | `3` | `0,1,2` | DATA：不能补造 |
| Dooltron | `41bc6bab-d7a6-42e4-9815-546de8640d4e` | `1` | `0,2,3` | DATA：不能补造 |
| Snowmobile | `6c0af8cf-273f-43c5-afb5-ef00c87ffcc8` | `3` | `0,1,2` | DATA：不能补造 |
| Cosmic Plumage | `c45a7c22-ae24-4246-bb6c-a83324a8546c` | `3` | `0,1,2` | DATA：不能补造 |
| Teddy | `cbec1d5f-096e-49d0-b736-60091fd3f9aa` | `2` | `0,1` | DATA：不能补造 |

## 为什么不能“修”

这些卡的 `Tiers.*.AbilityIds` 明确引用了缺失的 ID；按主文档 fail-closed 规则：

- 不能猜 `TActionCard*` 类型、数值、目标或持续时间；
- 不能把缺失能力静默跳过并把整卡标为完整；
- 不能用旧版本卡库或相似卡替代。

## 解除条件

1. G00 重新导出/核对原生 DB，确认这 6 个 AbilityId 是否在 5.0.0 数据中确实缺失；
2. 若后续版本补上定义，G02 重新映射并补行为测试；
3. 若确认 5.0.0 永久缺失，G11/G00 以 DATA 归档，G02 不再把这 6 张计入“可实现但未实现”。

当前矩阵把它们保持为 `unsupported / 能力定义缺失`，不计入 G02 已覆盖。
