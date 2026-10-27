# G06 unsupported-template triage — missing definitions vs engine gaps vs non-combat

Date: 2026-10-25. Scope: the 19 G06-primary templates that have `unsupported` rows in
`tools/actionaxis_plugin/build/native_mapping_report_g06full.tsv` (387,678 rows). The
prompt says 17; the report and the task index actually contain **19** G06-primary
templates with `unsupported` rows. All 19 are covered here.

Inputs inspected (read-only):

- `docs/native-rule-task-index.csv` — 136 `primary_group=G06` rows.
- `tools/actionaxis_plugin/build/native_mapping_report_g06full.tsv` — snapshot
  mtime `2026-10-25 00:56`.
- `tools/actionaxis_plugin/build/native_mapping_templates.jsonl` — complete templates.
- `vendor/GameData/GameData.db` — authoritative native `cards.Data` JSON, used to
  confirm that a missing key is absent in the game data itself, not lost by export.
- `data/game_cards_full.json` — used only as a second witness for the null
  Calcinator Diamond ability.
- `tools/actionaxis_plugin/engine/*.cs` — current uncommitted engine source.

## Important snapshot warning

The named report predates the current uncommitted engine work. Two read-only
rechecks were taken while other processes were still editing the shared workspace:

- report written: **00:56**;
- first recheck: **01:03**, with `EngineTests.exe` built at 01:02;
- latest recheck: **01:25**, with a copied `EngineTests.exe` built at 01:24:36
  (`ActionAxisEngine.dll` 01:22:41).

At 01:03, 7 of the 19 templates were already resolved and Calcinator's report-time
`Custom_9` aura error was gone. At 01:25, a coverage run over the **original 19
templates** produced **zero `unsupported` and zero `exception` rows**: items were
`mapped` or `mapped-warning`, skills were `simulated`/`live-applied`/`inactive`,
and the two economy cards were `out-of-combat`. The four item data defects now
carry explicit "definition missing, skipped, rest of combat retained" warnings.

The table below keeps the report-time cause and the root-cause classification.
The 01:25 recheck means **none of the 19 is currently blocked**; the remaining
issue for four of them is the native data warning, not an engine failure. Do not
treat the in-flight 01:25 state as a release gate: another process was still
editing `SkillMapper.cs` at 01:24:55.

## Verdict

| Bucket | Templates |
|---|---|
| Native data defect (missing definition in `cards.Data`) | Adrenaline Rush, Feral Alliance, Still Focus, Bushel, Tea Set, Outlands Terror, Calcinator (Diamond ability 1) |
| Engine gap open at 01:03, resolved by 01:25 | Extended Rage, Shortened Rage, [DEBUG] Rage Extender, [DEBUG] Replacement Effect |
| Engine gap already fixed at 01:03 | Ghillie Suit, Hammock, Hunker Down, Invulnerability Potion, Retort, Staff of the Moose |
| Non-combat / economy, should be out-of-combat | Healthy Tip (out-of-combat by 01:25), Red Envelope (already out-of-combat at 01:03) |

At 01:25 all 19 templates are unblocked. The four data-defect item templates
(Bushel, Calcinator Diamond rows, Outlands Terror, Tea Set) are `mapped-warning`
with the skipped key named in the warning. The three data-defect skills
(Adrenaline Rush, Feral Alliance, Still Focus) are `simulated`. No `unsupported`
or `exception` row remains in the original 19-template coverage run.

No missing number is proposed anywhere. The seven data-defective templates have
**no definition at all** for the referenced key, so the only safe fix is to skip
the stale reference with a visible warning; inventing a value or borrowing a
sibling card's definition would be guessing.

## Required triage table

| template | id | missing references | affected tiers | missing definition context (nearby internal names / duplicate IDs) | classification (ENGINE_GAP / DATA_DEFECT / NON_COMBAT) | exact minimal fix | risk if skipped |
|---|---|---|---|---|---|---|---|
| [DEBUG] Rage Extender | cc822ee3-0c6c-4ff9-9883-d451a27d433a | none. `AuraIds ["0"]` is present. | Gold, Diamond (+ Legendary alias); player side only | No missing key. Aura 0 is `TAuraActionPlayerModifyAttribute / EnragedDurationMax / Add / Custom_0` (5000 or 10000 ms), no prerequisite. Auras 8 and 9 are `"Tooltip Handling"` and are not referenced by any tier. | ENGINE_GAP (c: unsupported player aura; debug-only card); resolved by 01:25 | In `SkillMapper.Map`, apply a no-prerequisite `TAuraActionPlayerModifyAttribute` `EnragedDurationMax` aura as a static player attribute at battle init via `ApplyStaticAuras` (Add). Do not send it through the dynamic-aura validator. | Debug card only; no live gameplay loss. If left unsupported, its whole enrage-window effect is unmodelled. |
| [DEBUG] Replacement Effect | f3d410a9-0c04-41f4-a650-271277d07fa3 | none. Abilities 0 and 1 both exist. | Gold, Diamond (+ Legendary alias) | No missing key. Ability 0: `TTriggerOnPlayerAttributeChanged Rage Gain -> TActionPlayerRegenApply`, value `TReferenceValueAttributeChange`. Ability 1: same trigger -> `TActionPlayerModifyAttribute Rage / Subtract`, same value. | ENGINE_GAP (a: unsupported active action `Rage`; debug-only card); resolved by 01:25 | Add `"Rage"` to the supported player attributes in `SkillMapper.MapAction`. `Effects` already implements `playerAttribute` `Rage` add/subtract and emits `onPlayerAttributeChanged`, and the trigger already accepts `Rage`. | Debug card only. If mapped as a trigger pair, the net effect is regen-instead-of-rage, but exact replacement timing (suppressing Enrage from the replaced gain) is a deeper feature; note that caveat. |
| Adrenaline Rush | c7fa34d4-3e26-4471-a7a4-7919fdc86aab | `AbilityId "1"`. `Abilities` has only `"0"`; no key `"1"` anywhere in `cards.Data`. | Gold, Diamond (+ Legendary alias) | Ability 0 exists (`InternalName ""`, `TTriggerOnPlayerEnraged -> TActionCardCharge` for Weapons, HandOnly / Anywhere). Tooltip 0 references `ability.0` only. `HiddenTags` are `RageReference`, `Charge`. No same-name template and no card-local duplicate/renamed definition for key 1. | DATA_DEFECT (d: missing definition aborts the card before ability 0 is committed); 01:25 simulated | Skip absent ability 1 with a per-card `MappingWarnings` entry and keep ability 0; optionally delete the stale `"1"` from the two tier arrays. Do not synthesize an ability. | If ability 1 was an undocumented hidden effect it is under-modelled; every tooltip and every non-null definition is preserved. There are no numbers or definition text to recover, so skipping is the only non-guessing option. |
| Bushel | 843ddc82-9d87-4995-b81c-bf31ae45fd30 | `AuraId "3"`. `Auras` is empty. | Bronze, Silver, Gold, Diamond (+ Legendary alias) | Abilities 0/1/2 are `"Bushel 1"`, `"Hammock 2"`, `"Bushel 3"`. Ability 1 is the same HealthMax-on-adjacent-use definition as Hammock ability 1. The `Auras` map is empty. Hammock (`a4b48bb6`) does define aura 3, but it adds the right card's `SellPrice` to `Custom_0`; that is Hammock's scaling and must not be borrowed for Bushel. Tooltips reference abilities 0/1/2 only. | DATA_DEFECT (d: missing aura aborts the card before any ability maps); 01:25 mapped-warning | Skip absent aura 3 with a per-card warning and map abilities 0/1/2; optionally delete `"3"` from `AuraIds`. Do not copy Hammock aura 3. | Only aura 3 is lost; no native tooltip references it and no definition exists. Heal, Max-Health gain, and Shield-charge remain. Substituting Hammock's SellPrice aura would silently change Bushel's Max-Health scaling. |
| Calcinator | 3e3bda1d-81ba-4719-8772-417d8b834e99 | `AbilityId "1"`, **Diamond only**. Report-time failure was aura 1's `TReferenceValuePlayerAttribute Custom_9`, not a missing key. | Report: all tiers (Bronze–Legendary). Residual: Diamond (+ Legendary alias) | Abilities 0/2/3 exist (`"Calcinator 1"`, `"Calcinator 3"`, `"Calcinator 4"`); Diamond's `AbilityIds` uniquely adds `"1"`. `data/game_cards_full.json` has Diamond `rawAbilities["1"] = null`. Retort, the sibling card, never references ability 1. Aura 1 is present. Tooltips reference ability 0, ability 3 and aura 1; none reference ability 1. | DATA_DEFECT (residual); report-time `Custom_9` cause was ENGINE_GAP and is already handled by current `Custom_*` player-reference support; 01:25 mapped-warning on Diamond rows | Skip Diamond `AbilityId "1"` with a warning and keep abilities 0/2/3 plus aura 1. No further `Custom_9` work is needed if the current `PlayerAttributeValueSupported(Custom_*)` path is kept. Do not add a fake ability. | The Diamond key is explicitly null in the native data, so there is no behaviour to lose. Bronze/Silver/Gold already map; Diamond keeps its burn active and all three present abilities. |
| Extended Rage | 8b583fac-46a1-4531-8026-efd57232571a | none. `AuraIds ["0"]` is present. | Diamond (+ Legendary alias); player side only | No missing key. Aura 0: `TAuraActionPlayerModifyAttribute / EnragedDurationMax / Add / Custom_0` (1000 ms), no prerequisite. Auras 8/9 are `"Tooltip Handling"` and unreferenced (pure tooltip). | ENGINE_GAP (c: unsupported player aura); resolved by 01:25 | In `SkillMapper.Map`, map the no-prerequisite player `EnragedDurationMax` aura as a static player attribute at battle init (`ApplyStaticAuras` already supports `EnragedDurationMax` Add/Subtract). Do not require dynamic validation. | The whole card effect (+1 s enrage window) is lost. Enemy-side mapping already simulates it through the static item path. |
| Feral Alliance | feac268f-c3aa-442b-8b14-ba9026156133 | `AbilityId "1"`. `Abilities` has only `"0"`. | Gold, Diamond (+ Legendary alias) | Ability 0 exists (`InternalName ""`, `TTriggerOnPlayerEnraged -> TActionCardCharge` for Friends, HandOnly / Anywhere). Tooltip 0 references `ability.0` only. No card-local duplicate/renamed definition for key 1. | DATA_DEFECT (d: missing definition aborts the card before ability 0 is committed); 01:25 simulated | Skip absent ability 1 with a warning and keep ability 0; optionally remove `"1"` from the tier arrays. Do not synthesize an ability. | Same as Adrenaline Rush: only an undocumented hidden effect could be lost; all tooltips and non-null definitions are preserved. |
| Ghillie Suit | d25ccf21-f789-4e06-8c38-b7f5f47e66af | none. All abilities and the only referenced aura are present. | Silver, Gold, Diamond (+ Legendary alias) | No missing key. Ability 0 uses `TActionPlayerModifyAttribute PercentDamageReduction` with `TCombatDuration 1000 ms`; abilities 1/2 are counter and CritChance. Aura 9 is `"Tooltip Handling"` and is not referenced by `AuraIds` (pure tooltip). | ENGINE_GAP (a: unsupported player attribute `PercentDamageReduction`); **resolved at 01:03** | Keep `PercentDamageReduction` in the supported player-attribute list and honour `TCombatDuration` expiry via the existing `ActionPercentDamageReductionUntil` path. | If unresolved, the first-crit damage-immunity window is lost; the other two abilities still map. |
| Hammock | a4b48bb6-180a-49fd-bb1c-c6beb372b6de | none. All `AbilityIds` and `AuraIds` are present. | Silver, Gold, Diamond (+ Legendary alias) | No missing key. Abilities 0/2 use `TTriggerOnPlayerAttributeChanged HealthMax Gain`; ability 1 is the active Max-Health gain. Auras 2/3 add adjacent `SellPrice` to `Custom_0`. | ENGINE_GAP (b: unsupported player-attribute trigger `HealthMax`); **resolved at 01:03** | Allow `HealthMax` in `MapTrigger` for `TTriggerOnPlayerAttributeChanged` and emit `onPlayerAttributeChanged` on Max-HP gains (already present in current source). | If unresolved, the Max-Health-to-Heal and Max-Health-to-Shield synergy is lost; the item still heals. |
| Healthy Tip | c6a57594-4358-44de-b888-64216df4aa05 | none. Ability 0 is present; aura 9 is unreferenced tooltip handling. | Silver, Gold, Diamond (+ Legendary alias) | No missing key. Ability 0: `TTriggerOnPlayerAttributeChanged Gold Gain -> TActionPlayerModifyAttribute HealthMax Add` with `Duration: null` (permanent). Aura 9 is pure tooltip and `AuraIds` is empty. | NON_COMBAT (e: economy trigger not skipped); resolved by 01:25 (out-of-combat) | Add `Gold` (and the other economy attributes) to the `TTriggerOnPlayerAttributeChanged` non-combat branch of `IsNonCombatTrigger`, so the skill is recorded as `out-of-combat`. Do not simulate permanent run progression. | No combat loss. The permanent out-of-combat Max-HP growth remains unmodelled by design; that is the correct scope boundary. |
| Hunker Down | 369a6704-2727-4cea-be27-01b76c82df45 | none. Ability 0 is present. | Silver, Gold, Diamond (+ Legendary alias) | No missing key. Ability 0: `TTriggerOnSandstorm -> TActionPlayerModifyAttribute PercentDamageReduction / Add / Custom_0 / UntilEndOfCombat`. | ENGINE_GAP (a: unsupported player attribute `PercentDamageReduction`); **resolved at 01:03** | Same `PercentDamageReduction` support as Ghillie Suit, plus `UntilEndOfCombat` duration (already in current `playerAttribute` branch). | The card's only effect (25/50/75 % damage reduction for the rest of the fight) is lost. |
| Invulnerability Potion | 4fdc947b-3646-429e-ba4e-bb693fe44bc2 | none. All three abilities are present; `AuraIds` is empty. | Silver, Gold, Diamond (+ Legendary alias) | No missing key. Abilities 0/1 use `PercentDamageReduction` with `TCombatDuration` (1000/2000 ms) and Shiny-enchantment prerequisites. Ability 2 is a `Custom_8` dummy "ensure item is used". Aura 9 is unreferenced tooltip handling. | ENGINE_GAP (a: unsupported player attribute `PercentDamageReduction` + timed duration); **resolved at 01:03** | Same `PercentDamageReduction` support, including `TCombatDuration` expiry. No data change. | The no-damage window is the card's whole defensive effect; all other definitions are already supported. |
| Outlands Terror | 0660ace1-2d6a-4f7b-bfc2-05e8582d27b9 | `AuraId "0"`. `Auras` has only 1/2/3. | Gold, Diamond (+ Legendary alias) | Auras 1/2/3 exist: `"Outlands Terror 2"` Lifesteal, `"Outlands Terror 2"` FlatCooldownReduction, `"Outlands terror 3"` RageApplyAmount ×2. There is no aura 0 in this template. Tooltips reference aura 2 only; auras 1/2/3 cover both passives. | DATA_DEFECT (d: missing aura aborts the card before the present auras/ability map); 01:25 mapped-warning | Skip absent aura 0 with a warning and map aura 1/2/3 plus ability 0; optionally delete `"0"` from `AuraIds`. Do not invent an aura. | Only the stale aura 0 is lost; Lifesteal, cooldown reduction, double Rage gain and the active damage all remain. |
| Red Envelope | 5ff845f5-6cb5-40b8-957f-8a6ae47c3962 | none. Ability 0 is present. | Bronze, Silver, Gold, Diamond (+ Legendary alias) | No missing key. Ability 0: `TTriggerOnCardSelected -> TActionPlayerModifyAttribute Gold / Add 10`, HandOnly / Anywhere. No auras. | NON_COMBAT (e: buy/upgrade economy trigger); **already out-of-combat at 01:03** | No further change: `TTriggerOnCardSelected` is already in `IsNonCombatTrigger`. | None. Buying/upgrading Gold gain is outside combat and must not trigger the action axis. |
| Retort | c2cc691a-8f35-43be-8951-c65911b08865 | none. Ability 0/2/3 and aura 1 are present. | Bronze, Silver, Gold, Diamond (+ Legendary alias) | No missing key. Ability 0 is Poison; abilities 2/3 are day-start economy (spawn a Chunk of Lead, spend 2 Gold); aura 1 scales `PoisonApplyAmount` by `Player Custom_9 × Card Custom_0`. | ENGINE_GAP (aura value references `Custom_9`); **resolved at 01:03** | Keep accepting `Custom_*` in `TReferenceValuePlayerAttribute` for aura values, with a 0 default when the live snapshot lacks the run stat. No data change. | If unresolved, the Poison scaling aura is lost; the active Poison still maps. The day-start Gold/spawn abilities are correctly out-of-combat. |
| Shortened Rage | 00e1526a-0eb6-49af-93fd-e5e4aea5f05a | none. `AuraIds ["0"]` is present. | Diamond (+ Legendary alias); player side only | No missing key. Aura 0: `TAuraActionPlayerModifyAttribute / EnragedDurationMax / Subtract / Custom_0` (1000 ms), no prerequisite. Auras 8/9 are unreferenced tooltip handling. | ENGINE_GAP (c: unsupported player aura, and `Subtract` rejected by the player-aura validator); resolved by 01:25 | Map the no-prerequisite player `EnragedDurationMax` aura as a static player attribute and allow `Subtract` (the static `ApplyStaticAuras` path already supports both Add and Subtract for `EnragedDurationMax`). | The whole card effect (-1 s enrage window) is lost. Enemy-side mapping already simulates it. |
| Staff of the Moose | c135f23d-b8b4-4aa8-9a11-b288ae05908c | none. All five abilities and auras 6/9 are present. | Gold, Diamond (+ Legendary alias) | No missing key. Ability 0 is Damage; abilities 1/2 use `PercentDamageReduction` on Health loss; ability 4 is Regen; ability 5 is the half-Health counter; aura 6 adds player `HealthRegen` to Weapon Damage; aura 9 is unreferenced tooltip handling. | ENGINE_GAP (a: unsupported player attribute `PercentDamageReduction`); **resolved at 01:03** | Same `PercentDamageReduction` support with `TCombatDuration` expiry. No data change. | The first-below-half-Health immunity is lost; damage, Regen, counter and aura 6 still map. |
| Still Focus | 38eb79ab-a415-4a8b-ab61-5a7370397b59 | `AbilityId "1"`. `Abilities` has only `"0"`. | Gold, Diamond (+ Legendary alias) | Ability 0 exists (`InternalName ""`, `TTriggerOnPlayerEnrageEnded -> TActionCardCharge` for non-Weapons, HandOnly / CombatOnly). Tooltip 0 references `ability.0` only. No card-local duplicate/renamed definition for key 1. | DATA_DEFECT (d: missing definition aborts the card before ability 0 is committed); 01:25 simulated | Skip absent ability 1 with a warning and keep ability 0; optionally remove `"1"` from the tier arrays. Do not synthesize an ability. | Only an undocumented hidden effect could be lost; every tooltip and non-null definition is preserved. |
| Tea Set | 0c501dff-aa3a-4091-9c1e-2df43faef2e1 | `AuraId "3"`. `Auras` is empty. | Silver, Gold, Diamond (+ Legendary alias) | Abilities 1/2 exist (`"Tea Set 2"`, `"Tea Set 3"`); the keys start at 1, there is no ability 0. Hammock ability 2 is also named `"Tea Set 3"` and Hammock defines aura 3 (right-card `SellPrice -> Custom_0`), but that is a family copy-paste and unrelated to Tea Set. Tooltips reference abilities 1/2 only. | DATA_DEFECT (d: missing aura aborts the card before abilities 1/2 map); 01:25 mapped-warning | Skip absent aura 3 with a warning and map abilities 1/2; optionally delete `"3"` from `AuraIds`. Do not copy Hammock aura 3 and do not import the community-DB "Burn 2" value, which is absent from the native 5.0.0 template and attributes. | Only aura 3 is lost. The active Max-Health gain and the Burn-on-Max-Health passive remain. Substituting Hammock's SellPrice aura or a guessed Burn value would change combat numbers without native evidence. |

## Report reason vs 01:03 recheck

Verbatim report reasons are listed here because the report records only the **first**
thrown error; a missing definition can mask later unsupported rules.

| template | report `unsupported` rows | verbatim report reason | 01:03 recheck |
|---|---:|---|---|
| [DEBUG] Rage Extender | 9 | 未加入模拟：尚不支持动态光环、未知光环属性或条件 | unsupported (player), simulated (enemy) |
| [DEBUG] Replacement Effect | 252 | 原生战斗规则未完整模拟（尚不支持人物属性 Rage） | unsupported |
| Adrenaline Rush | 18 | 未加入模拟：技能缺少能力定义 | unsupported |
| Bushel | 420 | 原生战斗规则未完整模拟（光环定义缺失 3） | unsupported |
| Calcinator | 420 | 原生战斗规则未完整模拟（光环人物属性未支持 Custom_9） | 252 mapped, 168 unsupported (Diamond ability 1) |
| Extended Rage | 6 | 未加入模拟：尚不支持动态光环、未知光环属性或条件 | unsupported (player), simulated (enemy) |
| Feral Alliance | 18 | 未加入模拟：技能缺少能力定义 | unsupported |
| Ghillie Suit | 336 | 原生战斗规则未完整模拟（尚不支持人物属性 PercentDamageReduction） | mapped |
| Hammock | 336 | 原生战斗规则未完整模拟（尚不支持人物属性变化 HealthMax） | mapped |
| Healthy Tip | 24 | 未加入模拟：尚不支持人物属性变化 Gold | unsupported |
| Hunker Down | 24 | 未加入模拟：尚不支持人物属性 PercentDamageReduction | simulated |
| Invulnerability Potion | 336 | 原生战斗规则未完整模拟（尚不支持人物属性 PercentDamageReduction） | mapped |
| Outlands Terror | 252 | 原生战斗规则未完整模拟（光环定义缺失 0） | unsupported |
| Red Envelope | 30 | 未加入模拟：尚不支持触发器 TTriggerOnCardSelected | out-of-combat |
| Retort | 420 | 原生战斗规则未完整模拟（光环人物属性未支持 Custom_9） | mapped |
| Shortened Rage | 6 | 未加入模拟：尚不支持动态光环、未知光环属性或条件 | unsupported (player), simulated (enemy) |
| Staff of the Moose | 252 | 原生战斗规则未完整模拟（尚不支持人物属性 PercentDamageReduction） | mapped |
| Still Focus | 18 | 未加入模拟：技能缺少能力定义 | unsupported |
| Tea Set | 336 | 原生战斗规则未完整模拟（光环定义缺失 3） | unsupported |

Total report `unsupported` rows across these 19 templates: **3,513**. `inactive`
rows for skill tiers that the template does not define (for example Bronze/Silver
on Adrenaline Rush) are expected and are not gaps.

## Latest 01:25 status: all 19 unblocked

Read-only run of the copied 01:24:36 binary over the original 19 templates
(3,630 rows). `unsupported` and `exception` are both zero.

| template | 01:25 status | warning / note |
|---|---|---|
| [DEBUG] Rage Extender | simulated / live-applied (player + enemy); inactive lower tiers | none |
| [DEBUG] Replacement Effect | mapped | none |
| Adrenaline Rush | simulated (Gold/Diamond/Legendary); inactive Bronze/Silver | none |
| Bushel | mapped-warning | 光环定义缺失 3（已跳过，保留其余战斗部分） |
| Calcinator | mapped (Bronze/Silver/Gold); mapped-warning (Diamond/Legendary) | 能力定义缺失 1（已跳过，保留其余战斗部分） |
| Extended Rage | simulated / live-applied (player + enemy); inactive lower tiers | none |
| Feral Alliance | simulated (Gold/Diamond/Legendary); inactive Bronze/Silver | none |
| Ghillie Suit | mapped | none |
| Hammock | mapped | none |
| Healthy Tip | out-of-combat; inactive Bronze | economy |
| Hunker Down | simulated; inactive Bronze | none |
| Invulnerability Potion | mapped | none |
| Outlands Terror | mapped-warning | 光环定义缺失 0（已跳过，保留其余战斗部分） |
| Red Envelope | out-of-combat | economy |
| Retort | mapped | none |
| Shortened Rage | simulated / live-applied (player + enemy); inactive lower tiers | none |
| Staff of the Moose | mapped | none |
| Still Focus | simulated (Gold/Diamond/Legendary); inactive Bronze/Silver | none |
| Tea Set | mapped-warning | 光环定义缺失 3（已跳过，保留其余战斗部分） |

Run totals: `item/mapped` 2,184; `item/mapped-warning` 1,176; `skill/simulated`
106; `skill/live-applied` 14; `skill/out-of-combat` 54; `skill/inactive` 96;
`item/unsupported` 0; `skill/unsupported` 0; `exception` 0. The four
`mapped-warning` item templates are exactly the four data-defect items and their
warnings name the skipped key. This independently confirms the recommended
"skip the stale definition, keep the rest, warn visibly" fix.

## Detailed evidence

### 1. The seven missing-definition cases are genuine native data defects

For each missing key, the authoritative `vendor/GameData/GameData.db` `cards.Data`
JSON was read directly. The key is absent from the template's `Abilities`/`Auras`
dictionary; it is not a JSONL-export omission.

| template | map contents | missing key | tiers referencing it | referenced by every declared tier? | second witness |
|---|---|---|---|---|---|
| Adrenaline Rush | Abilities `0`; Auras none | ability `1` | Gold, Diamond | yes (Gold and Diamond) | `data/skills.json` has only ability 0 |
| Feral Alliance | Abilities `0`; Auras none | ability `1` | Gold, Diamond | yes (Gold and Diamond) | same |
| Still Focus | Abilities `0`; Auras none | ability `1` | Gold, Diamond | yes (Gold and Diamond) | same |
| Bushel | Abilities `0/1/2`; Auras none | aura `3` | Bronze, Silver, Gold, Diamond | yes (all four) | DB `Auras` empty |
| Tea Set | Abilities `1/2`; Auras none | aura `3` | Silver, Gold, Diamond | yes (all three) | DB `Auras` empty |
| Outlands Terror | Abilities `0`; Auras `1/2/3` | aura `0` | Gold, Diamond | yes (Gold and Diamond) | DB `Auras` keys 1,2,3 only |
| Calcinator | Abilities `0/2/3`; Auras `1` | ability `1` | Diamond only | **no, Diamond only** | `data/game_cards_full.json` Diamond `rawAbilities["1"] = null` |

Duplicate/renamed-definition checks:

- There is exactly one template per card name in the JSONL; no alternate version
  of Adrenaline Rush, Feral Alliance, Still Focus, Bushel, Tea Set or Outlands
  Terror exists.
- No ability with key `1` and the same trigger/action exists in those templates.
  A full scan of `TTriggerOnPlayerEnraged` / `TTriggerOnPlayerEnrageEnded`
  abilities found the present ability 0 definitions but no renamed twin.
- Bushel and Tea Set are Pygmalien-family copy-paste siblings: Bushel ability 1 is
  named `"Hammock 2"`, Hammock ability 2 is named `"Tea Set 3"`, and Hammock is the
  only card of the three that actually defines aura 3. Hammock aura 3 adds the
  right card's `SellPrice` to `Custom_0`; borrowing it for Bushel or Tea Set would
  alter their Max-Health scaling and is not supported by their tooltips.
- Outlands Terror's three present auras already cover both passive tooltips
  (Lifesteal + cooldown reduction, double Rage gain). There is no candidate aura 0.
- Calcinator's Diamond ability 1 is explicitly `null` in the second witness export.
  Retort, the sibling card, never references ability 1 in any tier.

Conclusion: these are stale tier-array references to definitions that do not exist
in the native data. They are **DATA_DEFECT**, not "engine has not implemented the
effect". The safe engine-side resolution is skip + warning.

### 2. Read-only skip experiment: the missing definition is the only residual blocker

A temporary copy of the 19 templates was made in the OS temp directory (no repo
file was edited). Only the absent `AbilityIds`/`AuraIds` were filtered out of the
tier arrays; nothing else was changed. It was run through the 01:02
`EngineTests.exe --coverage`:

| template | after removing only the absent id |
|---|---|
| Bushel | 420/420 mapped |
| Calcinator | 420/420 mapped (including Diamond) |
| Outlands Terror | 252/252 mapped |
| Tea Set | 336/336 mapped |
| Adrenaline Rush | 18/18 previously-unsupported rows now simulated; 12 expected inactive rows |
| Feral Alliance | 18/18 simulated; 12 inactive |
| Still Focus | 18/18 simulated; 12 inactive |

This proves the missing definition is the residual blocker and that all other
present definitions map. It does not prove the missing definition had no hidden
effect; it proves no other definition depends on it and there is nothing left to
implement from the available data. The 01:25 engine implements exactly this
policy: the four data-defect items are `mapped-warning` and the warnings name the
skipped key, while the three data-defect skills are `simulated`.

The experiment also exposes masked secondary causes at report time:

- **Tea Set** would still have hit the `HealthMax` player-attribute trigger after
  the aura was skipped. That trigger was an engine gap at report time and is
  supported in the current source (see Hammock).
- **Calcinator** was already fixed for `Custom_9` by 01:03; only the Diamond null
  ability remains.
- **Bushel** and **Outlands Terror** map fully once the stale id is removed; their
  present definitions were already supported.
- **Adrenaline Rush / Feral Alliance / Still Focus** map fully once ability 1 is
  skipped; ability 0 is the entire tooltip-described effect.

### 3. The four engine gaps open at 01:03 were real combat effects (resolved by 01:25)

| template | exact unsupported construct | why the 01:03 engine rejected it | change that unblocked it |
|---|---|---|---|
| Extended Rage | Aura 0 `TAuraActionPlayerModifyAttribute` `EnragedDurationMax` Add 1000 ms, no prerequisite | `SkillMapper.Map` routed all player-side `TAuraActionPlayerModifyAttribute` auras through `NativeDynamicAuras.Validate`; the player skill path did not apply the no-prerequisite aura statically, and the player-aura validator was narrower than the static item path. | Apply no-prerequisite player `EnragedDurationMax` auras statically at battle init via `ApplyStaticAuras` (which already supports Add/Subtract and `EnragedDurationMax`). 01:25 status: simulated/live-applied. |
| Shortened Rage | Same, `Subtract` 1000 ms | Same, plus `ValidatePlayerAura` only accepted Add/AdditiveMultiply. | Same static application; `ApplyStaticAuras` already handles Subtract for `EnragedDurationMax`. 01:25 status: simulated/live-applied. |
| [DEBUG] Rage Extender | Same, Add 5000/10000 ms | Same. | Same; debug-only. 01:25 status: simulated/live-applied. |
| [DEBUG] Replacement Effect | Ability 1 `TActionPlayerModifyAttribute` `Rage` Subtract | `SkillMapper.MapAction` supported player-attribute list did not contain `Rage`; it threw `尚不支持人物属性 Rage`. | `"Rage"` added to the supported list. `Effects` already has the `Rage` branch, emits `onPlayerAttributeChanged`, and `MapTrigger` already accepts `Rage`. 01:25 status: mapped. |

For Extended/Shortened/Rage Extender, the enemy side already simulated at 01:03
because the enemy branch fell through to `ApplyStaticAuras`; only the player skill
path threw. That asymmetry was the exact bug. At 01:25 both sides are simulated /
live-applied.

For Replacement Effect, mapping the two abilities as a trigger pair yields the net
"Rage gain becomes Regen" behaviour. The exact replacement ordering (suppressing
Enrage from the replaced gain) remains a semantic caveat, not a reason to skip the
card; the mapping is now accepted.

### 4. The six engine gaps already resolved at 01:03

These no longer reproduced at 01:03, and remain resolved at 01:25. Keep them in
the handover so nobody re-opens them:

| template | report-time gap | current support |
|---|---|---|
| Ghillie Suit | `PercentDamageReduction` player attribute action | supported list + `TCombatDuration` expiry in `Effects` |
| Hammock | `TTriggerOnPlayerAttributeChanged HealthMax` | trigger filter + `EmitPlayerAttribute` on Max-HP gains |
| Hunker Down | `PercentDamageReduction` + `UntilEndOfCombat` | same player-attribute branch |
| Invulnerability Potion | `PercentDamageReduction` + `TCombatDuration` | same |
| Retort | aura value `TReferenceValuePlayerAttribute Custom_9` | `PlayerAttributeValueSupported` accepts `Custom_*`; absent live stat defaults to 0 |
| Staff of the Moose | `PercentDamageReduction` + `TCombatDuration` | same |

Calcinator's report-time `Custom_9` failure is the same fix as Retort; its residual
Diamond null ability is data-defective.

### 5. Non-combat boundaries

- **Healthy Tip** is entirely an economy/progression card: trigger on Gold gain,
  permanent Max-HP increase (`Duration: null`). It should be recorded as
  `out-of-combat`, not simulated. At 01:03 the engine still threw because
  `IsNonCombatTrigger` only treated `Level`/`Experience` as non-combat
  player-attribute changes, not `Gold`; by 01:25 it is `out-of-combat` (the
  Gold/economy branch was added).
- **Red Envelope** is entirely an economy card: trigger on card selected, add Gold.
  `TTriggerOnCardSelected` is already non-combat, so the 01:03 engine records it
  as `out-of-combat` (30 rows) with no further change.
- **Calcinator / Retort day-start abilities** are also non-combat/economy
  (`TTriggerOnDayStarted`: spawn a Chunk of Lead, spend 2 Gold). They are already
  skipped and are not the cause of the unsupported rows. Do not mark the whole
  card out-of-combat: both cards have real combat actives and auras.

## Appendix: referenced ability/aura inventory

`ActiveIn` / `WorksIn` are shown for abilities and auras; auras additionally have
`Trigger: null` (shown as `—`). "Combat" means it changes the battle simulation;
"economy" means out-of-combat progression; "tooltip" means unreferenced
display-only handling.

| template | ref | present? | Trigger | Action | AttributeType | ActiveIn / WorksIn | class |
|---|---|---|---|---|---|---|---|
| Adrenaline Rush | A0 | yes | TTriggerOnPlayerEnraged | TActionCardCharge | ChargeAmount | HandOnly / Anywhere | combat |
| Adrenaline Rush | A1 | **no** | — | — | — | — | unknown stale ref |
| Feral Alliance | A0 | yes | TTriggerOnPlayerEnraged | TActionCardCharge | ChargeAmount | HandOnly / Anywhere | combat |
| Feral Alliance | A1 | **no** | — | — | — | — | unknown stale ref |
| Still Focus | A0 | yes | TTriggerOnPlayerEnrageEnded | TActionCardCharge | ChargeAmount | HandOnly / CombatOnly | combat |
| Still Focus | A1 | **no** | — | — | — | — | unknown stale ref |
| Bushel | A0 | yes | TTriggerOnCardFired | TActionPlayerHeal | HealAmount | HandOnly / Anywhere | combat |
| Bushel | A1 | yes | TTriggerOnItemUsed (neighbor) | TActionPlayerModifyAttribute | HealthMax Add, UntilEndOfCombat | HandOnly / Anywhere | combat |
| Bushel | A2 | yes | TTriggerOnCardPerformedShield | TActionCardCharge | ChargeAmount | HandOnly / Anywhere | combat |
| Bushel | U3 | **no** | — | — | — | — | unknown stale ref |
| Tea Set | A1 | yes | TTriggerOnCardFired | TActionPlayerModifyAttribute | HealthMax Add, UntilEndOfCombat | HandOnly / Anywhere | combat |
| Tea Set | A2 | yes | TTriggerOnPlayerAttributeChanged (HealthMax Gain) | TActionCardModifyAttribute | BurnApplyAmount Add, UntilEndOfCombat | HandOnly / CombatOnly | combat |
| Tea Set | U3 | **no** | — | — | — | — | unknown stale ref |
| Calcinator | A0 | yes | TTriggerOnCardFired | TActionPlayerBurnApply | BurnApplyAmount | HandOnly / Anywhere | combat |
| Calcinator | A1 | **no (Diamond only)** | — | — | — | — | null in second witness |
| Calcinator | A2 | yes | TTriggerOnDayStarted | TActionGameSpawnCards | — (Gold >= 2) | HandAndStash / Anywhere | economy |
| Calcinator | A3 | yes | TTriggerOnDayStarted | TActionPlayerModifyAttribute | Gold Subtract 2 | HandAndStash / Anywhere | economy |
| Calcinator | U1 | yes | — | TAuraActionCardModifyAttribute | BurnApplyAmount Add (Custom_9 × Custom_0) | HandAndStash / Anywhere | combat scaling |
| Retort | A0 | yes | TTriggerOnCardFired | TActionPlayerPoisonApply | PoisonApplyAmount | HandOnly / Anywhere | combat |
| Retort | A2 | yes | TTriggerOnDayStarted | TActionGameSpawnCards | — (Gold >= 2) | HandAndStash / Anywhere | economy |
| Retort | A3 | yes | TTriggerOnDayStarted | TActionPlayerModifyAttribute | Gold Subtract 2 | HandAndStash / Anywhere | economy |
| Retort | U1 | yes | — | TAuraActionCardModifyAttribute | PoisonApplyAmount Add (Custom_9 × Custom_0) | HandAndStash / Anywhere | combat scaling |
| Extended Rage | U0 | yes | — | TAuraActionPlayerModifyAttribute | EnragedDurationMax Add (Custom_0) | HandAndStash / Anywhere | combat |
| Extended Rage | U8 | yes | — | TAuraActionCardModifyAttribute | Custom_2 Add (Custom_1) | HandOnly / Anywhere | tooltip |
| Extended Rage | U9 | yes | — | TAuraActionPlayerModifyAttribute | EnragedDurationMax Add (player value × 0.001) | HandOnly / Anywhere | tooltip |
| Shortened Rage | U0 | yes | — | TAuraActionPlayerModifyAttribute | EnragedDurationMax Subtract (Custom_0) | HandAndStash / Anywhere | combat |
| Shortened Rage | U8 | yes | — | TAuraActionCardModifyAttribute | Custom_2 Add (Custom_1) | HandOnly / Anywhere | tooltip |
| Shortened Rage | U9 | yes | — | TAuraActionPlayerModifyAttribute | EnragedDurationMax Add (player value × 0.001) | HandOnly / Anywhere | tooltip |
| Rage Extender | U0 | yes | — | TAuraActionPlayerModifyAttribute | EnragedDurationMax Add (Custom_0) | HandAndStash / Anywhere | combat, debug |
| Rage Extender | U8 | yes | — | TAuraActionCardModifyAttribute | Custom_2 Add (Custom_1) | HandOnly / Anywhere | tooltip |
| Rage Extender | U9 | yes | — | TAuraActionPlayerModifyAttribute | EnragedDurationMax Add (player value × 0.001) | HandOnly / Anywhere | tooltip |
| Replacement Effect | A0 | yes | TTriggerOnPlayerAttributeChanged (Rage Gain) | TActionPlayerRegenApply | RegenApplyAmount (AttributeChange) | HandOnly / Anywhere | combat, debug |
| Replacement Effect | A1 | yes | TTriggerOnPlayerAttributeChanged (Rage Gain) | TActionPlayerModifyAttribute | Rage Subtract (AttributeChange) | HandOnly / Anywhere | combat, debug |
| Ghillie Suit | A0 | yes | TTriggerOnCardCritted | TActionPlayerModifyAttribute | PercentDamageReduction Add 100, 1000 ms | HandOnly / Anywhere | combat |
| Ghillie Suit | A1 | yes | TTriggerOnCardCritted | TActionCardModifyAttribute | Custom_0 Add 1, UntilEndOfCombat | HandOnly / Anywhere | combat counter |
| Ghillie Suit | A2 | yes | TTriggerOnCardPerformedSlow | TActionCardModifyAttribute | CritChance Add (Custom_2) | HandOnly / Anywhere | combat |
| Ghillie Suit | U9 | yes | — | TAuraActionCardModifyAttribute | Custom_0 Add (Custom_1) | HandOnly / Anywhere | tooltip |
| Hammock | A0 | yes | TTriggerOnPlayerAttributeChanged (HealthMax Gain) | TActionCardModifyAttribute | HealAmount Add (Custom_2) | HandOnly / CombatOnly | combat |
| Hammock | A1 | yes | TTriggerOnCardFired | TActionPlayerModifyAttribute | HealthMax Add (Custom_0) | HandOnly / Anywhere | combat |
| Hammock | A2 | yes | TTriggerOnPlayerAttributeChanged (HealthMax Gain) | TActionCardModifyAttribute | ShieldApplyAmount Add (Custom_1) | HandOnly / CombatOnly | combat |
| Hammock | U2 | yes | — | TAuraActionCardModifyAttribute | Custom_0 Add (left SellPrice) | HandAndStash / Anywhere | combat scaling |
| Hammock | U3 | yes | — | TAuraActionCardModifyAttribute | Custom_0 Add (right SellPrice) | HandAndStash / Anywhere | combat scaling |
| Healthy Tip | A0 | yes | TTriggerOnPlayerAttributeChanged (Gold Gain) | TActionPlayerModifyAttribute | HealthMax Add, permanent | HandAndStash / Anywhere | economy |
| Healthy Tip | U9 | yes | — | TAuraActionCardModifyAttribute | Custom_3 Add (Custom_2) | HandAndStash / Anywhere | tooltip |
| Hunker Down | A0 | yes | TTriggerOnSandstorm | TActionPlayerModifyAttribute | PercentDamageReduction Add (Custom_0), UntilEndOfCombat | HandOnly / Anywhere | combat |
| Invulnerability Potion | A0 | yes | TTriggerOnCardFired | TActionPlayerModifyAttribute | PercentDamageReduction Add 100, 1000 ms | HandOnly / Anywhere | combat |
| Invulnerability Potion | A1 | yes | TTriggerOnCardFired | TActionPlayerModifyAttribute | PercentDamageReduction Add 100, 2000 ms | HandOnly / Anywhere | combat |
| Invulnerability Potion | A2 | yes | TTriggerOnCardFired | TActionCardModifyAttribute | Custom_8 Add 1, UntilEndOfCombat | HandOnly / Anywhere | dummy usage marker |
| Invulnerability Potion | U9 | yes | — | TAuraActionCardModifyAttribute | Custom_1 Add (Custom_0) | HandAndStash / Anywhere | tooltip |
| Outlands Terror | A0 | yes | TTriggerOnCardFired | TActionPlayerDamage | DamageAmount | HandOnly / Anywhere | combat |
| Outlands Terror | U0 | **no** | — | — | — | — | unknown stale ref |
| Outlands Terror | U1 | yes | — | TAuraActionCardModifyAttribute | Lifesteal Add 100 (Enraged > 0) | HandOnly / Anywhere | combat |
| Outlands Terror | U2 | yes | — | TAuraActionCardModifyAttribute | FlatCooldownReduction Add (Custom_0) | HandOnly / Anywhere | combat |
| Outlands Terror | U3 | yes | — | TAuraActionCardModifyAttribute | RageApplyAmount Multiply 2, SelfBoard | HandOnly / Anywhere | combat |
| Red Envelope | A0 | yes | TTriggerOnCardSelected | TActionPlayerModifyAttribute | Gold Add 10 | HandOnly / Anywhere | economy |
| Staff of the Moose | A0 | yes | TTriggerOnCardFired | TActionPlayerDamage | DamageAmount | HandOnly / Anywhere | combat |
| Staff of the Moose | A1 | yes | TTriggerOnPlayerAttributeChanged (Health Loss) | TActionPlayerModifyAttribute | PercentDamageReduction Add 100, 1000 ms | HandOnly / Anywhere | combat |
| Staff of the Moose | A2 | yes | TTriggerOnPlayerAttributeChanged (Health Loss) | TActionPlayerModifyAttribute | PercentDamageReduction Add 100, 2000 ms | HandOnly / Anywhere | combat |
| Staff of the Moose | A4 | yes | TTriggerOnPlayerAttributeChanged (Health Loss) | TActionPlayerRegenApply | RegenApplyAmount, UntilEndOfCombat | HandOnly / Anywhere | combat |
| Staff of the Moose | A5 | yes | TTriggerOnPlayerAttributeChanged (Health Loss) | TActionCardModifyAttribute | Custom_0 Add 1, UntilEndOfCombat | HandOnly / Anywhere | combat counter |
| Staff of the Moose | U6 | yes | — | TAuraActionCardModifyAttribute | DamageAmount Add (player HealthRegen) to Weapons | HandOnly / Anywhere | combat |
| Staff of the Moose | U9 | yes | — | TAuraActionCardModifyAttribute | Custom_2 Add (Custom_1) | HandOnly / Anywhere | tooltip |

All combat abilities use `HandOnly`; the only `HandAndStash` abilities are the economy
ones (Calcinator/Retort day-start, Healthy Tip). Some passive auras are
`HandAndStash` (Calcinator/Retort aura 1 and the enrage-window auras), which the
static aura path accepts. `WorksIn` is `Anywhere` or `CombatOnly`. No referenced
definition is blocked by an area restriction.

## Minimal fix order (no number guessing)

This is the root-cause fix list that was needed. All five items are implemented in
the in-flight 01:24:36 binary: the 01:25 run over the original 19 templates has zero
`unsupported`/`exception`, and `g06_full_green7.log` reports `PASS G06FullCoverage`.
Keep the list as the rationale, not as remaining work.

1. **Skip stale definitions with a per-card warning** in
   `NativeCardAbilityMapper.Map` and `SkillMapper.Map` for absent
   `AbilityIds`/`AuraIds`; keep mapping every present definition. If nothing
   combat-capable remains, mark the card inactive/out-of-combat rather than
   mapped. This unblocks Adrenaline Rush, Feral Alliance, Still Focus, Bushel,
   Tea Set, Outlands Terror and Calcinator's Diamond rows. Do **not** create
   definitions for the absent ability key 1 (Adrenaline/Feral/Still, Calcinator
   Diamond) or the absent aura keys 0 (Outlands) and 3 (Bushel/Tea Set); none
   exists in the native DB.
2. **Apply no-prerequisite player `EnragedDurationMax` skill auras statically**
   (Add for Extended Rage/Rage Extender, Subtract for Shortened Rage), reusing the
   existing `ApplyStaticAuras` path. This unblocks the three rage-window skills.
3. **Add `Rage` to the supported player attributes** in `SkillMapper.MapAction`.
   This unblocks [DEBUG] Replacement Effect. Note the replacement-timing caveat.
4. **Treat `Gold` player-attribute changes as non-combat** in
   `IsNonCombatTrigger`, so Healthy Tip is recorded as out-of-combat. Red Envelope
   already takes the `TTriggerOnCardSelected` non-combat path.
5. **Keep the already-fixed rules**: `PercentDamageReduction` with
   `TCombatDuration`, `HealthMax` attribute-change triggers, and `Custom_*` player
   references for Calcinator/Retort auras. Do not revert them while editing the
   shared workspace.

## Method / limits

- All analysis was read-only with respect to repository source, tests and docs.
  Temporary JSONL/TSV copies and a copied engine binary were written only under
  the OS temp directory.
- The skip experiment is a diagnostic, not a proposed data edit. It used the
  `EngineTests.exe --coverage` runner on a filtered copy of the 19 templates.
- The latest 01:25 check used a **copied** 01:24:36 `EngineTests.exe` +
  01:22:41 `ActionAxisEngine.dll` on the original 19-template JSONL. It produced
  zero `unsupported` and zero `exception` rows. `g06_full_green7.log` (01:24:44)
  independently shows `PASS G06FullCoverage`. This is an observed in-flight build,
  not a release gate.
- The report snapshot is from 00:56; the 01:03 and 01:25 rechecks are
  moving-target observations because another process was editing the shared
  engine. The classification of native data defects is independent of that race:
  it was verified directly against `vendor/GameData/GameData.db`.
- `mapped` / `simulated` in the coverage runner means the mapper accepted every
  referenced rule; it is not a replay-parity or semantic-correctness claim.
- No missing numeric value is proposed. Where a value is quoted (1000 ms, 2000 ms,
  100 %, etc.) it comes from the template JSON or the native DB, not from
  inference.
