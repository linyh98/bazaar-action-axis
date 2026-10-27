# G05 回放值级校准记录（2026-10-25）

> 本文件由 `tmp_g05/replay_calibration_report.md` 归档而来，供验收长期保留。语料：`D:\巴扎plus\CombatReplays` 下 200 份 `.mpack.gz`；提取脚本：`tmp_g05/scan_g05_corpus.py`、`tools/native_replay_ledger.py`。以下为原始报告正文。

# G05 native-replay calibration (value level)

Decoders: `captures/diagnostics/read_bpp_replay.py` for raw MessagePack/LZ4, and
`python tools/native_replay_ledger.py <replay> --json tmp_g05/ev_*.json --no-snapshot` for ledgers.
Replay file names are `<battleId>.payload.mpack.gz` (the `battleId` given below is the file stem).
Code-5 `EffectExecuted`, code-7 `EffectAuraExecuted` and action-10 `CardModifyAttribute` carry no numeric value;
applied numbers below come from `combat.healthChanges` / player-attribute updates and `combat.cardUpdates` (50 ms frames).
`--no-snapshot` drops the OpeningMessage sidecar, so the **recorder's own board/hero/level is not in the payload**;
every card below is an opponent (enemy) card. Template tooltips in `data/items_raw.json` / `data/skills.json`
were used only to name the rule, never as replay evidence.
Coverage of the 27 requested templates: **16 present / 11 absent** across the 200 replays. All observed
priority cards had `Ammo 0`, so there is no Ammo→Multicast evidence (Shuriken is absent).
Absent (zero occurrences): Monocle, Lifting, Wild Strength, Growth Spurt, Swole, Venomous Vitality,
Shuriken, Santoku, Subscraper, First Strike, Grill.

---
### 1. `ab8439ff38b34a2eabb92ecd5d65f14b` — Refrigerator / Heavy Mettle / Waters / Ice Swan / Froyo Cart
sha256 `75e14531db11748a42500a77ebb162aaeedea5d2212f6864d5ead15197955765`, 10.05 s, winner=player.
`P` Health 7628. `E` Jules Lv13, HMax 5725, HRegen **111**, Shield/Burn/Poison 0, Crit 0, Custom_3 261 / _4 8 / _6 5 / _8 4.
- **Refrigerator** Silver `c65IbxC`: ShieldApplyAmount **58**, RegenApplyAmount **4**, Chilled 1, CDmax 6000, no growth in 10 s. Every activation (t=3.05, 3.30, 3.85, 4.10 … 9.80; triggers `FtAgGQd/rwcEL55/yiV0D0w/ePVTdX0/53Zp5DX`) gives exactly `Shield +58` (0→58→116→174…) and `HealthRegen +4` (117→121, 127→131, 157→161…).
- **Heavy Mettle** Silver `Vso0ef4` (Custom_0=2): Slushee Diamond base Shield 20 → **46**; Strawberries Silver base 20 → **46** ⇒ **+26 = 2×Lv13**. Sorbet Diamond 60 → 146 (=2×60+26; extra +60 from a second effect).
- **Waters of Infinity** Silver `l7VYnpz`: RegenApplyAmount **50** (Custom_0/1=10), included in HRegen 111; enemy Regen heal tick at t=2.0 = **111**. **Ice Swan** Diamond `qMi3Uad`: FreezeAmount 1000, FreezeTargets 10, Chilled 1 → 7 cards each `Freeze 0→500` at t=6.0. **Froyo Cart** Diamond `V5NGln8` is in **Stash** (FreezeAmount 1000 / FreezeTargets 1): zero events.
- Read: auras act on live card attributes; `HealthRegen` is the per-second heal amount and RegenApplyAmount stacks into it. NOT proven: tier scaling, Ice Swan's 4-target cap (7 froze), Froyo/Chilled adjacency (stash item inert).
### 2. `a9dc7a52d49a4e77a3143efda62b46ed` — Cash Cannon + Oinkment
sha256 `5d7251df89b92d677df75f930cbf6c2d0c846783412fad42557c907e7ca04203`, 18.1 s, winner=player.
`P` Health 2340, final HealthMax 2640. `E` Pygmalien Lv8, HMax 3493, HRegen 26, Gold **12**, Custom_3 **167** / _4 17 / _6 5 / _8 4.
- **Cash Cannon** Silver `nnebari`: DamageAmount **334**, CDmax 9000, Multicast 1; deals exactly `PlayerDamage 334` at t=9.0 (CombatantId 0).
- **Oinkment** Silver `AYBUcjY`: HealAmount **167**, CDmax 6000; `PlayerHeal 167` at t=6.5 and 13.5 (self). No Burn/Poison existed to cleanse.
- Read: `HealAmount` is an economy-scaled precomputed value (**167 = 1×Custom_3**), not live Gold (12); activation applies it verbatim. Cash Cannon 334 = 2×167. NOT proven: the "+50 base" (334 also = 50+2×142; gold-gain deltas happen in shop and never appear), cleanse, or P's six +50 MaxHP bumps (P board invisible).
### 3. `a94eda884bf847df9b826b1f7ba9e2d1` — Cash Cannon + Fire Claw
sha256 `943bee43cad79ab0e426785425841e5956ffeb7cdda8184c73c6e86f2ba327fb`, 10.65 s, winner=enemy.
`P` Health 4929. `E` Pygmalien Lv12, HMax 5750, HRegen 6, Gold 90, Custom_3 **1153** / _4 57 / _6 2 / _8 2.
- **Cash Cannon** Diamond `M7SlHeM`: DamageAmount **2348**, ShieldApplyAmount **2359**, Crit 7, CDmax 7000; deals exactly 2348 at t=6.5 and 10.6, self-applies 2359 Shield at 6.5.
- **Fire Claw** Diamond `XYcYuHk`: BurnApplyAmount **550**, Custom_0 100, Crit 21; other Burn items Money Furnace 534 + Salt 6 ⇒ **550 = 10 + 100%×(534+6)** exactly.
- Read: Fire Claw reference formula confirmed to the unit. Cash Cannon 2348 ≈ 2×Custom_3 (2306, Δ42), so the economy counter is close but not identical here. NOT proven: +50 base, the 2359 vs 2348 shield offset (enchant/aura).
### 4. `4ce4c7756bf8434cbaca2a4063b8fb04` — Healthy Lifestyle + Regenerative + Scorchpepper
sha256 `433fb965b4b67ffb9b836f398d26b7f69ab372379703ea41f2a20a8db7e5a3bd`, 15.05 s, winner=player.
`P` Health 9135. `E` Jules Lv18, HMax 10740, HRegen **152**, Custom_3 437 / _4 17 / _6 7 / _8 7.
- **Healthy Lifestyle** Gold `Wf_i2lL`: RegenApplyAmount **30**, Custom_0/1=6; hand has 7 items, 2 Weapons ⇒ **30 = 6×5** non-Weapon.
- **Regenerative** Gold `hGQv3yu`: RegenApplyAmount **40** (flat, no updates). **Scorchpepper** Diamond `Mr44JzP`: BurnApplyAmount **8** (template value), Crit 39.
- Cross-check: Bread Knife Diamond DamageAmount **304 = 2×152** (HRegen) ⇒ the three G05 regen sources are additive and feed card formulas. NOT proven: the 42-point residual (other skills), Scorchpepper's applied Burn (multi-source frames), Cleanse/Heated.
### 5. `921ccdd8f48f4b2dafbc490a216ca427` — Iron Sharpens Iron + Regenerative
sha256 `54468ad2636bce59f51d208721b262981414ec12331f169a3780f10f25725f35`, 9.85 s, winner=enemy.
`P` Health 4131. `E` Vanessa Lv11, HMax 4310, HRegen **34**, Custom_3 160 / _4 20 / _6 6 / _8 6.
- **Iron Sharpens Iron** Gold `U6oVPga`: Custom_0=3. Leftmost weapon Marlon Gold DamageAmount **75**; Marlon base Damage is 15 at every tier ⇒ **+60 = 3×20** weapons bought.
- **Regenerative** Silver `gYuo47U`: RegenApplyAmount **20**; enemy Regen heal = 34/s = 20 + 14 residual.
- Read: supports "leftmost Weapon += Custom_0 × weapons-bought" and flat Regenerative. NOT proven: N=20 independently (weapons-bought is not serialized), targeting beyond socket-0.
### 6. `d5feb78abe764d54b7156bd3f2368517` — Philosopher's Stone
sha256 `274179c838ca12b030577f0aa10f1dbc4448d5e470c732b25e9dc7c09f2653e2`, 37.75 s, winner=player.
`P` Health 1040. `E` Mak Lv5, HMax 1000, HRegen 0, Custom_3 56 / _4 2 / _6 0 / _8 0.
- **Philosopher's Stone** Silver `PSgcj0h`: opening RegenApplyAmount **26**, CDmax 5000; grows **+4 at t=5,10,15,20,25,30,35.5 → 54**.
- Each activation applies the live value: at t=4.55 enemy HealthRegen 3→29 = **+26**; subsequent PlayerRegenApply events follow the 30/34/38… values.
- Read: +4 = +1 "Gain 1 Regen for the fight" (Silver) +3 "when you transform a Reagent", matching Mak's per-activation transform. NOT proven: which steps had a transform; events carry no amount.
### 7. `a5726eb4da1848cf87c80cf150b89afa` — Giant Sub
sha256 `f4291bf4c9ecfb9548c593f1cb9d11a146212412ea79c32cf881fe1cc4443eaa`, 50.8 s, winner=player.
`P` Health 8399. `E` Jules Lv12, HMax 4895, HRegen 18, Custom_3 220 / _4 6 / _6 5 / _8 4.
- **Giant Sub** Diamond `Er9K4Hm`: opening Shield **30**, Regen/Burn/Poison **2**, Heated 1, Custom_0 50, CDmax 7000; steady **+32 Shield / +2 Regen / +2 Burn / +2 Poison** per step → **1598 / 100 / 100 / 100** at 50.65 s.
- Activation t=6.10 applies live values exactly: `Shield +62`, `HealthRegen +4`, player `Burn +4`, `Poison +4`; t=7.15: `+158`, `+10`, `+10`, `+10`.
- Read: reference-valued card (`Shield/Regen/Burn = highest Shield/Regen/Burn Food`) is re-evaluated continuously and applied from current attributes. NOT proven: the "highest Food" identity (steps are 2× the observed Cake Batter +16), Heated gating.
### 8. `286fbe9e1837427a93f7fdd7a8f72e81` — Nitro
sha256 `9f63f41929ec5cd2909e4267083940fc077a68a2a67382f0f6014c2b018decc7`, 8.9 s, winner=enemy.
`P` Health 5500. `E` Dooley Lv13, HMax 6284, HRegen 6, Custom_3 225 / _4 23 / _6 6 / _8 5.
- **Nitro** Gold `ZJKBEZw`: BurnApplyAmount **4**, ChargeAmount 1000/1, CDmax 3000, Crit 4.
- At t=3.0 two `PlayerBurnApply` events fire, CombatantId **0 and 1**, each Burn 0→**4**; repeats at 4.7, 6.0, 6.8, 8.1; each activation also `CardCharge`s a random item.
- Read: both-player Burn = BurnApplyAmount verbatim and symmetric. NOT proven: charge randomness/targeting, Burn crit.
### 9. `e4b1e6dd96784b93bc6c58694bbd67fe` — Inexorable
sha256 `7a3a1ae103829eb473a968259c1cc620f66d8f05e7b9d4509702bc26cd8a3b0a`, 16.55 s, winner=enemy.
`P` Health 750. `E` Vanessa Lv4, HMax 750, HRegen 0, Custom_3 33 / _4 11 / _6 0 / _8 0.
- **Inexorable** Silver `Ubex78Y`: Custom_0=2, Custom_1=**4** (Silver template C1=2; +2 start-of-fight self bonus visible here).
- All 9 hand items show `CritChance 2`, except Yeti Crab `0`.
- Read: items get +Custom_0 = +2% Crit. NOT proven: why Yeti Crab stayed 0 (excluded/overwritten), or any per-use decay (not part of Inexorable).
### 10. `c61d5b0a9afc4c548f8d0fc9559ca5bd` — Waters of Infinity + Regenerative (2nd sample)
sha256 `ab813837c010a49bd6976fa00252832ee6cb10c8fc00812747002c695ed463d5`, 16.55 s, winner=enemy.
`P` Health 7227. `E` Mak Lv13, HMax 6131, HRegen **180**, Custom_3 395 / _4 8 / _6 5 / _8 4.
- **Waters** Silver `RQcBJsW`: RegenApplyAmount **70** (Custom_0=10); 7 hand items, **0 Weapons** ⇒ 10×7. **Regenerative** Silver `S0iikXS`: **20**.
- Sum 90 of HRegen 180; enemy Regen heal at t=2.0 is exactly **180** (= HRegen).
- Read: clean confirmation of `Custom_0 × non-Weapon hand cards` and additive stacking. NOT proven: the 90 residual (other item/skill regen).
### 11. `67af77d96dd949828ff74ee57a39c9a8` — Waters of Infinity (3rd sample) + Regenerative
sha256 `c172314870a8e0822c37fadf192402ec6c4b7dffbedcef9f5b594a3a935bf64f`, 10.05 s, winner=player.
`P` Health 6841. `E` Stelle Lv13, HMax 5660, HRegen **68**, Custom_3 267 / _4 13 / _6 6 / _8 6.
- **Waters** Silver `dpm6ZuE`: RegenApplyAmount **30**; **Regenerative** Silver `XvvjHKL`: **20**.
- Enemy Regen heal is 68 every second (t=0…4), i.e. exactly HRegen; 30+20 = 50 of 68.
- Read: third Silver Waters value, 10×N with N=3. Caveat: board serializes 6 items with only Battle Drone tagged Weapon, so N=3 requires two of three unknown-template items to be Weapons — count basis not proven.
### 12. `0ce4c330b9b64bb9a589651fedfcf88a` — Healthy Lifestyle + Scorchpepper (2nd sample)
sha256 `f32c34a65d11de17814794fd419f6f796e8d6b73c8c915605a7e11f7c55ddfc1`, 19.55 s, winner=enemy.
`P` Health 3259. `E` Jules Lv9, HMax 2625, HRegen **40**, Custom_3 144 / _4 1 / _6 5 / _8 5.
- **Healthy Lifestyle** Gold `GRYLqwv`: **36** = 6×6 (6 hand items, 0 Weapons); enemy Regen heal 40/s (36 + 4 residual).
- **Scorchpepper** Silver `rY5Vy-Y`: BurnApplyAmount **4** (template Silver), in **Stash**, CDmax 5000, zero events.
- Read: second independent `6 × non-Weapon count` confirmation; stash Scorchpepper inert; static tier value correct. NOT proven: applied Burn.
### 13. `0802443b51dd4271bad85fcf3012ccea` — Refrigerator (growing variant)
sha256 `d72e273537491b89429158c61026915d22f528ae7997da53dc26e7653e2ae524`, 17.05 s, winner=player.
`P` Health 1059. `E` Jules Lv5, HMax 1000, HRegen 0, Custom_3 65 / _4 9 / _6 2 / _8 2.
- **Refrigerator** Silver `ReWyQVs`: opening **20 / 2** → **50 / 5** (t=5.0) → **80 / 8** (10.5) → **110 / 11** (15.5); +30 ShieldApplyAmount / +3 RegenApplyAmount per ~5.5 s cycle.
- Activations apply the post-growth value: 5.05 Shield 0→50, HRegen 0→5; 10.55 Shield 340→420 (+80), HRegen 25→33 (+8); three activations per cycle.
- Read: applied amounts are read from the live attribute at activation, not cached at spawn. NOT proven: the growth driver, nor why replay #1's Refrigerator stayed 58/4.

---

| Template | Replay | Key observed | G05 rule calibrated | Confidence / limits |
|---|---|---|---|---|
| Cash Cannon | a9dc7a52 / a94eda88 | 334 dealt @9.0 (C3=167); 2348 dealt @6.5/10.6 (C3=1153) | economy ref ×2, precomputed DamageAmount | med; +50 base unresolved, a94 Δ42 |
| Oinkment | a9dc7a52 | HealAmount 167 → Heal 167 @6.5/13.5 | 1×economy ref, not live Gold | high on value; cleanse untested |
| Refrigerator | ab8439ff / 0802443b | 58/4 constant ×10; 20/2→50/5→80/8→110/11, applies 50, 80… | apply live Shield/RegenApplyAmount | high on deltas; growth driver unknown |
| Ice Swan | ab8439ff | FreezeAmount 1000, 7 targets, Freeze 0→500 | FreezeAmount @ targets | med; 500 ms display, no 4-cap |
| Froyo Cart | ab8439ff | Stash, Freeze 1000/1, 0 events | stash items do not act | med; single sample |
| Heavy Mettle | ab8439ff | 20→46 twice at Lv13 | item Shield += 2×Level (Silver) | high (+26 exact); Sorbet doubled |
| Waters of Infinity | ab8439ff / c61d / 67af | 50 / 70 / 30; HRegen 111/180/68 | +Regen = 10×non-Weapon | med-high; ab8439 50 vs 70 predicted (Loot excluded) |
| Healthy Lifestyle | 4ce4c775 / 0ce4c330 | 30 = 6×5; 36 = 6×6 | +6 Regen per non-Weapon (Gold) | high |
| Regenerative | 4ce4c775 / 921ccdd8 / c61d | 40 / 20 / 20, no updates | flat +Regen Silver/Gold | high |
| Philosopher's Stone | d5feb78a | 26→54 (+4 @5 s); +26 applied @4.55 | +1/activation +3/Reagent transform | med-high; transform inferred |
| Giant Sub | a5726eb4 | 30/2/2/2→1598/100/100/100; applies 62/4/4/4, 158/10/10/10 | apply live ref-valued attrs | high on apply; "highest Food" identity unproven |
| Fire Claw | a94eda88 | 550 = 10 + 534 + 6 | 10 + Custom_0% × other Burn | high (exact) |
| Nitro | 286fbe9e | Burn 4 on both players @3.0, 4.7, 6.0… | BurnApplyAmount to both combatants | high |
| Inexorable | e4b1e6dd | 8/9 items Crit 2; skill C1 2→4 | items +2% Crit (Silver) | med; Yeti Crab anomaly |
| Iron Sharpens Iron | 921ccdd8 | Marlon 75 = 15 + 3×20; C0=3 | leftmost Weapon += 3/weapon bought | med; N inferred |
| Scorchpepper | 4ce4c775 / 0ce4c330 | Burn 8 (Diamond) / 4 (Silver) | static tier value | low-med; applied burn not isolated |

## What this evidence does NOT establish
- No full event parity: code-5 events carry no value field; applied values are reconstructed from 50 ms
  snapshots and simultaneous sources can merge in one frame.
- No recorder-side board (`--no-snapshot` drops the sidecar): P hero/level/items/skills are invisible, so the
  11 absences mean "not in the 200 sampled opponent boards", not "never in game".
- One sample for most templates; no real tier ladder except Regenerative Silver/Gold, so tier scaling is not calibrated.
- Waters' non-Weapon count and Cash Cannon's gold counter are not directly serialized; Giant Sub's "highest
  Food" source and Refrigerator's growth driver remain under-determined.
- Cooldown arithmetic is not claimed except where the attribute itself moved (Giant Sub CDmax 7000→6299,
  PercentCooldownReduction 0→10 at t=12.35).
