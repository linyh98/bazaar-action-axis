# G06 native replay evidence — BPP combat payloads

Read-only scan of **200** compressed BPP combat replay payloads in `D:\巴扎plus\CombatReplays\CombatReplays` (`*.payload.mpack.gz`). Decoded with `captures/diagnostics/read_bpp_replay.py::read_replay`; field/enum names come from `tools/native_replay_contract_v1.json`.

- Time: `time = frameIndex * 0.05 s` (contract `frameSeconds = 0.05`).
- Sides: **combatant 0 = PlayerUpdates / player bucket** (local player; own board hidden in these payloads); **combatant 1 = OpponentUpdates / opponent bucket** (fully visible board). Every spawned card, including every focus card, has `CombatantId = 1`.
- Replays containing at least one focus template: **21 / 200** (`26` focus cards).

## 1. Coverage

| Focus template | Replays | Focus cards | Event-referenced | Passive-only (timer/attr) | Present but idle | Defining mechanic observed |
|---|---:|---:|---:|---:|---:|---:|
| Warpaint | 4 | 4 | 1 | 0 | 3 | 1 observed / 3 missed |
| Vengeful Sigil | 1 | 1 | 1 | 0 | 0 | 1 observed / 0 missed |
| Burning Shield | 1 | 1 | 1 | 0 | 0 | 0 observed / 1 missed |
| Toxic Shield | 0 | 0 | 0 | 0 | 0 | 0 observed / 0 missed |
| Wild Boar (item) | 0 | 0 | 0 | 0 | 0 | 0 observed / 0 missed |
| Wild Boar (monster) | 0 | 0 | 0 | 0 | 0 | 0 observed / 0 missed |
| Feeding Frenzy | 1 | 1 | 0 | 0 | 1 | 0 observed / 1 missed |
| Wrist Blade | 0 | 0 | 0 | 0 | 0 | 0 observed / 0 missed |
| Geyser | 0 | 0 | 0 | 0 | 0 | 0 observed / 0 missed |
| Pauldrons | 1 | 1 | 1 | 0 | 0 | 1 observed / 0 missed |
| Sawpike | 1 | 1 | 1 | 0 | 0 | 1 observed / 0 missed |
| Resistance Bands | 0 | 0 | 0 | 0 | 0 | 0 observed / 0 missed |
| Honey Badger | 5 | 5 | 4 | 0 | 1 | 3 observed / 2 missed |
| Anaconda | 2 | 2 | 1 | 0 | 1 | 1 observed / 0 missed |
| Executioner's Cleaver | 0 | 0 | 0 | 0 | 0 | 0 observed / 0 missed |
| Campfire | 1 | 1 | 0 | 0 | 1 | 0 observed / 0 missed |
| Shortened Rage | 4 | 4 | 0 | 3 | 1 | 3 observed / 0 missed |
| Extended Rage | 5 | 5 | 0 | 4 | 1 | 4 observed / 0 missed |

**Never present in any of the 200 payloads:** `Toxic Shield`, `Wild Boar (item)`, `Wild Boar (monster)`, `Wrist Blade`, `Geyser`, `Resistance Bands`, `Executioner's Cleaver`.

Notes: *event-referenced* = the instance appears as an event source/trigger/target, receives an aura, or has a card attribute delta. *passive-only* = no direct event reference, but the card's defining mechanic is visible through an owner attribute/timer side effect (e.g. the 4000/6000 ms enrage timer). *present but idle* = neither. A passive stat card can be relevant without emitting events; the last column checks the card's actual G06 signature (rage apply, first-below-half shield, enrage timer).

## 2. Best samples

### (a) Warpaint / Vengeful Sigil rage gain and enrage window

**`9a1d87b3aecb4b688544efb4f309a941.payload.mpack.gz`** — 291 frames / 14.55 s

- Path: `D:\巴扎plus\CombatReplays\CombatReplays\9a1d87b3aecb4b688544efb4f309a941.payload.mpack.gz`
- SHA256: `dd7c0693ef0a7d4f02441136f91d14774a29350a06b672d05965f9a18afc2ddb`
- Cards: Vengeful Sigil [combatant 1/Opponent] Silver/no enchant, Hand socket 0 (event-exercised, defining=True)
- Why: Vengeful Sigil (Silver, Hand socket 0) is the source of 22 PlayerRageApply events; isolated repeats move the owner's rage by exactly +4, matching this card's RageApplyAmount 4 (9 isolated +4 steps, e.g. f81/f86/f91). The owner reaches 100 rage at f120 and enrages for exactly 100 frames (5.0 s, EnragedDuration 5000 = opening EnragedDurationMax); the enraged-only Lifesteal aura is applied at f120 and removed at f220.
  - Vengeful Sigil: observed -> when an opponent item is used -> PlayerRageApply; while enraged -> Lifesteal aura on own Weapons
  - Vengeful Sigil PlayerRageApply: f81 +4 (card RageApplyAmount=4, 1 concurrent), f86 +4 (card RageApplyAmount=4, 1 concurrent), f91 +4 (card RageApplyAmount=4, 1 concurrent)
  - enrage opponent: f120→f220 (5.00 s window, 5000 ms timer); f246→combat end (open, 5000 ms timer)
  - Caveat: The first owner-side rage jump (0→27 at f61) is an aggregate of three PlayerRageApply events (not all Sigil); Sigil's own +4 steps start at f81.
  - Caveat: Combatant-1 (visible board) is the card owner; the replay's 'Player' bucket is the hidden local board.

**`921f0f4a20fe4643a19d716192138451.payload.mpack.gz`** — 201 frames / 10.05 s

- Path: `D:\巴扎plus\CombatReplays\CombatReplays\921f0f4a20fe4643a19d716192138451.payload.mpack.gz`
- SHA256: `c2fd2dd96e4efa26cd366211a9c9f8ab4211015ecce9e5eb69b03a49856df2a9`
- Cards: Warpaint [combatant 1/Opponent] Diamond/no enchant, Hand socket 2 (event-exercised, defining=True); Extended Rage [combatant 1/Opponent] Diamond/no enchant, skill socket None (present-but-unexercised, defining=False)
- Why: Warpaint (Diamond, Hand socket 2, RageApplyAmount 12) is the source of a PlayerRageApply at f57; the owner's rage moves 0→20 that frame from two concurrent rage events, of which Warpaint contributes 12. Rage caps at 52, so no enrage window occurs and the Extended Rage card in the same replay is unexercised.
  - Warpaint: observed -> when the item to the left is used -> PlayerRageApply (RageApplyAmount)
  - Warpaint PlayerRageApply: f57 +20 (card RageApplyAmount=12, 2 concurrent)
  - HealthMax player: f73 7900→7950, f103 7950→8000, f113 8000→8050, f133 8050→8100, … (7 changes)
  - first below half opponent: f160 (3919/9025 HP, shield None)
  - Caveat: Aggregated frame: two PlayerRageApply events land in the same frame, so the +20 cannot be split from the attribute update alone; the per-event split comes from the event list (Warpaint 12 + another source 8).

Vengeful Sigil event payloads (`CombatSimEventEffectExecuted`, code 5, Source = `MEoBc4T`):

- f81: `["3QT", "0", 47, null, ["MEoBc4T"], ["itm_0OVBF1-"], [2, [1]]]`
- f86: `["Ogy", "0", 47, null, ["MEoBc4T"], ["itm_4d5n219"], [2, [1]]]`
- f91: `["MAv", "0", 47, null, ["MEoBc4T"], ["itm_8oHvZrL"], [2, [1]]]`

Warpaint event payload (Source = `ufw9qoN`; TriggerSource = the left-hand item `t94C0Uo`):

- f57: `["-5p", "0", 47, null, ["ufw9qoN"], ["t94C0Uo"], [2, [1]]]`

**Negative / partial (present but defining mechanic idle):**

- `440fbfe6ed054859a356baaa9d79e7f7.payload.mpack.gz` — Warpaint **present-but-unexercised** (Stash socket 0, tier Gold, RageApplyAmount 10): in Stash (section=Stash), never referenced by any combat event. Owner-side rage gains in this replay come from other sources and are not attributable to the card.
- `67af77d96dd949828ff74ee57a39c9a8.payload.mpack.gz` — Warpaint **present-but-unexercised** (Stash socket 2, tier Silver, RageApplyAmount 8): in Stash (section=Stash), never referenced by any combat event. Owner-side rage gains in this replay come from other sources and are not attributable to the card.
- `a093d76658fd4d9582e11bb85aee01d3.payload.mpack.gz` — Warpaint **present-but-unexercised** (Stash socket 4, tier Gold, RageApplyAmount 10): in Stash (section=Stash), never referenced by any combat event. Owner-side rage gains in this replay come from other sources and are not attributable to the card.

### (b) Burning Shield / Toxic Shield first-below-half shield

**`0f4d1425b68942bdabc110aa2b31d0f8.payload.mpack.gz`** — 371 frames / 18.55 s

- Path: `D:\巴扎plus\CombatReplays\CombatReplays\0f4d1425b68942bdabc110aa2b31d0f8.payload.mpack.gz`
- SHA256: `09a8bcf159a5d2bb92ae65d66bb2e9c87d42d45976c36cfd1d7308c28083f7d3`
- Cards: Burning Shield [combatant 1/Opponent] Silver/no enchant, skill socket None (event-exercised, defining=False)
- Why: Only Burning Shield occurrence in the corpus. The skill is exercised as an aura value recalculation (19 CardModifyAttribute events), and its owner first drops below half health at f330 (1243/2635), but the defining first-below-half PlayerShieldApply never fires; the +40 shield at f332 comes from Blueberry Pie (30da8c8f), not from Burning Shield. Toxic Shield never appears.
  - Burning Shield: NOT observed -> first Health loss below 50% HealthMax -> PlayerShieldApply (Custom_1 x Burn on enemy)
  - first below half opponent: f330 (1243/2635 HP, shield 0)
  - Caveat: Present-but-defining-mechanic-missed sample, not a positive validation.
  - Caveat: Possible causes not distinguishable from the payload: zero-value shield suppressed, or the skill was disabled; there is no CardDisable event for the instance in the trace.

**Verdict:** no positive first-below-half sample exists. Burning Shield appears once and does not fire its ability; Toxic Shield is absent from all 200 payloads. Do not treat this corpus as validation for the half-health shield trigger.

### (c) Wild Boar

Neither `e01ac7ab-1ad5-4235-81b4-44d057878953` (item) nor `5d779101-3fa5-44b7-b885-f8bb9b0f7438` (monster) appears in spawn events (including nested code-10 spawn payloads) or mid-combat transform events (code 9/10) of any payload; despawn card entries carry no template id, so they cannot add coverage. **Wild Boar rage/enrage has zero native replay evidence in this corpus.** Feeding Frenzy (the rage-on-food card used by the Wild Boar seed) appears once (`208b18ce…`) but is present-but-unexercised: no rage change and no event sourced by it in that fight.

### (d) HealthMax trigger and PercentDamageReduction

**`de491beb92ff4426a41af7c3b459489e.payload.mpack.gz`** — 401 frames / 20.05 s

- Path: `D:\巴扎plus\CombatReplays\CombatReplays\de491beb92ff4426a41af7c3b459489e.payload.mpack.gz`
- SHA256: `430a772eaa23025ffe6524585d17129956093188d5cd4d0a370404771dbc03a5`
- Why: Player-side PercentDamageReduction goes 0→50 at f72 together with Rage 69→100 and Enraged 0→1, and back to 0 at f172 when Enraged 1→0; a second identical window runs f248→348. This is the cleanest native evidence that PercentDamageReduction is a timed player attribute tied to the enrage window.
  - enrage player: f72→f172 (5.00 s window, 5000 ms timer); f248→f348 (5.00 s window, 5000 ms timer)
  - enrage opponent: f181→f261 (4.00 s window, 4000 ms timer); f311→f391 (4.00 s window, 4000 ms timer)
  - PDR player: f72 0→50, f172 50→0, f248 0→50, f348 50→0
  - first below half opponent: f100 (3129/7894 HP, shield None)
  - Caveat: The PDR source instance is on the hidden combatant-0 board (itm_3D8NdGi aura at f72, itm_eqhvflV at f251); the item identity cannot be resolved from the payload.
  - Caveat: Shortened Rage is present on the opposing side and its owner enrages with a 4000 ms timer.

**`921f0f4a20fe4643a19d716192138451.payload.mpack.gz`** — 201 frames / 10.05 s

- Path: `D:\巴扎plus\CombatReplays\CombatReplays\921f0f4a20fe4643a19d716192138451.payload.mpack.gz`
- SHA256: `c2fd2dd96e4efa26cd366211a9c9f8ab4211015ecce9e5eb69b03a49856df2a9`
- Why: Seven PlayerModifyAttribute (action 31) events from hidden skill skl_Gr7wIyD raise the player's HealthMax and Health by +50 each (7900→8250, f73…f193), giving a clean per-event HealthMax trigger trace.
  - Warpaint: observed -> when the item to the left is used -> PlayerRageApply (RageApplyAmount)
  - Warpaint PlayerRageApply: f57 +20 (card RageApplyAmount=12, 2 concurrent)
  - HealthMax player: f73 7900→7950, f103 7950→8000, f113 8000→8050, f133 8050→8100, … (7 changes)
  - first below half opponent: f160 (3919/9025 HP, shield None)
  - Caveat: Source skl_Gr7wIyD and trigger itm_Jdwcrbz are hidden-board instances; no focus card is involved.
  - Caveat: Same replay is the only positive Warpaint sample, so it doubles as a HealthMax sample.

**`70e269ac2ac54ca9bd90d20dded2fe9b.payload.mpack.gz`** — 321 frames / 16.05 s

- Path: `D:\巴扎plus\CombatReplays\CombatReplays\70e269ac2ac54ca9bd90d20dded2fe9b.payload.mpack.gz`
- SHA256: `d907c38b5bef5829fe9856605ba3b8e9b262e703d2f7973af500644a0ddd190d`
- Why: Owner-side HealthMax changes via action 31: 3230→3403 (+173, Brass Knuckles), 3403→5403 (+2000, Karnok rage skill), then +192 and +198; the same side spawns with EnragedDurationMax 6000 (5000 + Custom_0 1000) and enrages at f101 with a 6000 ms timer, matching Extended Rage.
  - enrage opponent: f101→f221 (6.00 s window, 6000 ms timer)
  - HealthMax opponent: f100 3230→3403, f102 3403→5403, f171 5403→5595, f271 5595→5793
  - first below half opponent: f261 (2779/5595 HP, shield 0)
  - Caveat: HealthMax +2000 event is triggered by the Karnok's Rage skill, not by Extended Rage.

Additional PDR sample `cc91f8f6c33346f2bf8d8fa4f9ba636d.payload.mpack.gz`: PDR 0→50 at the player enrage start (f251) and HealthMax 12267→17172 in the same frame; the PDR source is a hidden-board aura.

### Bonus: EnragedDurationMax modifiers

| Replay | Card present | Owner enrage start | Opening EnragedDurationMax (spawn) | Enrage timer at start | Result |
|---|---|---:|---:|---:|---|
| `70e269ac2ac54ca9bd90d20dded2fe9b.payload.mpack.gz` | Extended Rage | f101 | 6000 ms | 6000 ms | matches |
| `88925590d85146cba38c920d638e9a0b.payload.mpack.gz` | Shortened Rage | f198 | 4000 ms | 4000 ms | matches |
| `8b4d72c9fe8640a1a46ee06f3d8596c2.payload.mpack.gz` | Extended Rage | f166 | 6000 ms | 6000 ms | matches |
| `e0a373306e4e421f950da0fc68a40674.payload.mpack.gz` | Extended Rage | f123 | 6000 ms | 6000 ms | matches |
| `fd8b5f00181548889b0bdcf2ebdf8d95.payload.mpack.gz` | Extended Rage | f124 | 6000 ms | 6000 ms | matches |
| `de491beb92ff4426a41af7c3b459489e.payload.mpack.gz` | Shortened Rage | f181 | 4000 ms | 4000 ms | matches |
| `feb3a214a51c47adb459fe4e0d2c8a01.payload.mpack.gz` | Shortened Rage | f200 | 4000 ms | 4000 ms | matches |

Pattern: Extended Rage reports an opening `EnragedDurationMax` of **6000** (5000 default + Custom_0 1000) and Shortened Rage **4000** (5000 − 1000), already in the spawn snapshot; the enrage timer then starts at the same 6000/4000 ms and no 38 (`EnragedDurationMax`) attribute delta is emitted during combat. This is direct native evidence for the dynamic player-aura path being folded into the opening attributes, not a mid-combat write to the base attribute.

### Observed G06 semantics across these replays

| Observation | Count | Example |
|---|---:|---|
| Enrage starts exactly at Rage = RageMax | 32 / 32 windows | `9a1d87b3` f120 (82→100, Enraged 0→1) |
| Enrage window length = EnragedDuration / 50 ms | 20 / 20 closed windows | `9a1d87b3` f120→f220 = 100 frames = 5000 ms |
| Rage gain strictly inside an Enraged window | 0 / 32 windows | none observed |
| Rage drops at window end (100 → lower) | 20 / 20 closed windows | `9a1d87b3` f220 (100→0) |
| … of those, clean 100→0 reset | 13 windows | same-frame Rage gain lands after the reset in the other 7 |
| Opponent opening RageMax = 100 | 21 / 21 replays | every focus replay |
| Player (combatant 0) opening RageMax reported | 0 / 21 replays | hidden side |

Opening `EnragedDurationMax` per window: 4000 ms × 5, 5000 ms × 15, 6000 ms × 4, unreported (hidden player side) × 8

Consequence for the G06 implementation: the enrage timer is a countdown of `EnragedDuration` ticks (50 ms/frame), the hero cannot bank rage while enraged, and the window end drops Rage back to 0 (a gain landing in the same frame is applied after the reset, leaving a small residue). The timer value is the **opening** `EnragedDurationMax` (possibly modified by a dynamic aura), not a value written during combat. When the window is still open at combat end (12 of 32 windows), `TTriggerOnPlayerEnrageEnded` effects such as Honey Badger's −1000 FlatCooldownReduction are never emitted, so those replays cannot validate enrage-end rules.

## 3. All focus replays

Every focus card in this table is `CombatantId = 1` (the fully visible replay `Opponent` board). Combatant-0 items are hidden and appear only as `itm_*`/`skl_*` ids, so no focus card is owned by the local `Player` side in these 200 payloads.

| File | SHA256 (16) | Frames | Duration | Focus cards (tier/ench, section/socket) | Verdict | Key evidence |
|---|---|---:|---:|---|---|---|
| `0f4d1425b68942bdabc110aa2b31d0f8.payload.mpack.gz` | `09a8bcf159a5d2bb` | 371 | 18.55 s | Burning Shield Silver/-, skill/- | Burning Shield:event/def-miss | Burning Shield: NOT observed -> first Health loss below 50% HealthMax -> PlayerShieldApply (Custom_1 x Burn on enemy); first below half opponent: f330 (1243/2635 HP, shield 0) |
| `208b18cedda64a8a903ad40b5b99ea18.payload.mpack.gz` | `7800582ab59a626c` | 351 | 17.55 s | Feeding Frenzy Silver/-, skill/- | Feeding Frenzy:idle/def-miss | Feeding Frenzy: NOT observed -> Food used -> PlayerRageApply (RageApplyAmount) + Haste to Food; first below half opponent: f310 (400/2180 HP, shield 0) |
| `261da36ec93545879995b0a8f19f4279.payload.mpack.gz` | `33a29768d7de3fea` | 561 | 28.05 s | Honey Badger Silver/-, Hand/4 | Honey Badger:event | Honey Badger: observed -> on Rage gain -> Charge self 1 s; on EnrageEnded -> +1000 ChargeAmount and -1000 FlatCooldownReduction until end of combat; enrage player: f187→f287 (5.00 s window, 5000 ms timer); f455→f555 (5.00 s window, 5000 ms timer); enrage opponent: f203→f303 (5.00 s window, 5000 ms timer); f470→combat end (open, 5000 ms timer) |
| `2f357c3e90664de4bd0557b18a92dff7.payload.mpack.gz` | `b0581104934921d2` | 219 | 10.95 s | Honey Badger Silver/-, Hand/3 | Honey Badger:event/def-miss | Honey Badger: NOT observed -> on Rage gain -> Charge self 1 s; on EnrageEnded -> +1000 ChargeAmount and -1000 FlatCooldownReduction until end of combat; enrage opponent: f178→combat end (open, 5000 ms timer) |
| `440fbfe6ed054859a356baaa9d79e7f7.payload.mpack.gz` | `467bad36079e99c3` | 281 | 14.05 s | Warpaint Gold/-, Stash/0 | Warpaint:idle/def-miss | Warpaint: NOT observed -> when the item to the left is used -> PlayerRageApply (RageApplyAmount); enrage player: f147→f197 (2.50 s window, 2500 ms timer); f253→combat end (open, 2500 ms timer); first below half opponent: f240 (1080/2160 HP, shield 0) |
| `67af77d96dd949828ff74ee57a39c9a8.payload.mpack.gz` | `c172314870a8e082` | 201 | 10.05 s | Warpaint Silver/-, Stash/2 | Warpaint:idle/def-miss | Warpaint: NOT observed -> when the item to the left is used -> PlayerRageApply (RageApplyAmount); first below half opponent: f160 (1835/5660 HP, shield 1023) |
| `70e269ac2ac54ca9bd90d20dded2fe9b.payload.mpack.gz` | `d907c38b5bef5829` | 321 | 16.05 s | Extended Rage Diamond/-, skill/- | Extended Rage:passive | enrage opponent: f101→f221 (6.00 s window, 6000 ms timer); HealthMax opponent: f100 3230→3403, f102 3403→5403, f171 5403→5595, f271 5595→5793; first below half opponent: f261 (2779/5595 HP, shield 0) |
| `88925590d85146cba38c920d638e9a0b.payload.mpack.gz` | `c88d50e6560e6f8d` | 281 | 14.05 s | Shortened Rage Diamond/-, skill/- | Shortened Rage:passive | enrage opponent: f198→f278 (4.00 s window, 4000 ms timer); first below half opponent: f220 (1601/4458 HP, shield 0) |
| `8b4d72c9fe8640a1a46ee06f3d8596c2.payload.mpack.gz` | `f60a6e5ff2927caa` | 301 | 15.05 s | Extended Rage Diamond/-, skill/- | Extended Rage:passive | enrage opponent: f166→f286 (6.00 s window, 6000 ms timer); HealthMax opponent: f81 2839→2889, f86 2889→2939, f91 2939→2989, f96 2989→3039; first below half opponent: f260 (1135/3039 HP, shield 899) |
| `921f0f4a20fe4643a19d716192138451.payload.mpack.gz` | `c2fd2dd96e4efa26` | 201 | 10.05 s | Warpaint Diamond/-, Hand/2; Extended Rage Diamond/-, skill/- | Warpaint:event; Extended Rage:idle | Warpaint: observed -> when the item to the left is used -> PlayerRageApply (RageApplyAmount); Warpaint PlayerRageApply: f57 +20 (card RageApplyAmount=12, 2 concurrent); HealthMax player: f73 7900→7950, f103 7950→8000, f113 8000→8050, f133 8050→8100, … (7 changes) |
| `9a1d87b3aecb4b688544efb4f309a941.payload.mpack.gz` | `dd7c0693ef0a7d4f` | 291 | 14.55 s | Vengeful Sigil Silver/-, Hand/0 | Vengeful Sigil:event | Vengeful Sigil: observed -> when an opponent item is used -> PlayerRageApply; while enraged -> Lifesteal aura on own Weapons; Vengeful Sigil PlayerRageApply: f81 +4 (card RageApplyAmount=4, 1 concurrent), f86 +4 (card RageApplyAmount=4, 1 concurrent), f91 +4 (card RageApplyAmount=4, 1 concurrent); enrage opponent: f120→f220 (5.00 s window, 5000 ms timer); f246→combat end (open, 5000 ms timer) |
| `a093d76658fd4d9582e11bb85aee01d3.payload.mpack.gz` | `d0da785ce7e4e064` | 755 | 37.75 s | Warpaint Gold/-, Stash/4 | Warpaint:idle/def-miss | Warpaint: NOT observed -> when the item to the left is used -> PlayerRageApply (RageApplyAmount); enrage opponent: f193→f293 (5.00 s window, 5000 ms timer); f343→f443 (5.00 s window, 5000 ms timer); f508→f608 (5.00 s window, 5000 ms timer); f677→combat end (open, 5000 ms timer); first below half opponent: f720 (854/1733 HP, shield None) |
| `b069c43c47934d5391510520f119cadf.payload.mpack.gz` | `4957706678a0888d` | 259 | 12.95 s | Pauldrons Gold/Shielded, Hand/4; Anaconda Gold/-, Stash/1; Shortened Rage Diamond/-, skill/- | Pauldrons:event; Anaconda:idle; Shortened Rage:idle | Pauldrons PlayerShieldApply: f118 shield 0→172, f178 shield 0→366, f224 shield 69→1097; first below half opponent: f199 (2704/5486 HP, shield 0) |
| `b6ee82840ea74f8a80b70d43c1916e5a.payload.mpack.gz` | `38eef1384414382d` | 238 | 11.90 s | Sawpike Gold/-, Hand/7 | Sawpike:event | enrage opponent: f221→combat end (open, 5000 ms timer); first below half opponent: f220 (875/2100 HP, shield None) |
| `c95fb3867014486a82cb04932094448a.payload.mpack.gz` | `9d12aa51b5933704` | 281 | 14.05 s | Anaconda Diamond/Shielded, Hand/0 | Anaconda:event | Anaconda PlayerShieldApply: f114 shield 35→131, f160 shield 2749→2964, f165 shield 2964→3179; enrage opponent: f129→f229 (5.00 s window, 5000 ms timer); f251→combat end (open, 5000 ms timer); first below half opponent: f220 (3583/7600 HP, shield 6979) |
| `cc91f8f6c33346f2bf8d8fa4f9ba636d.payload.mpack.gz` | `8050cb2c1dcf7032` | 290 | 14.50 s | Honey Badger Gold/-, Stash/6 | Honey Badger:idle/def-miss | Honey Badger: NOT observed -> on Rage gain -> Charge self 1 s; on EnrageEnded -> +1000 ChargeAmount and -1000 FlatCooldownReduction until end of combat; enrage player: f251→combat end (open, 4000 ms timer); enrage opponent: f215→combat end (open, 5000 ms timer) |
| `de491beb92ff4426a41af7c3b459489e.payload.mpack.gz` | `430a772eaa23025f` | 401 | 20.05 s | Shortened Rage Diamond/-, skill/- | Shortened Rage:passive | enrage player: f72→f172 (5.00 s window, 5000 ms timer); f248→f348 (5.00 s window, 5000 ms timer); enrage opponent: f181→f261 (4.00 s window, 4000 ms timer); f311→f391 (4.00 s window, 4000 ms timer); PDR player: f72 0→50, f172 50→0, f248 0→50, f348 50→0 |
| `e0a373306e4e421f950da0fc68a40674.payload.mpack.gz` | `9c2892e9f89b51d5` | 221 | 11.05 s | Extended Rage Diamond/-, skill/- | Extended Rage:passive | enrage opponent: f123→combat end (open, 6000 ms timer); first below half opponent: f180 (1854/4940 HP, shield 0) |
| `f8f527c6172f44d5ba54fff3bef8f49e.payload.mpack.gz` | `827f72b1b106e213` | 462 | 23.10 s | Honey Badger Silver/-, Hand/5 | Honey Badger:event | Honey Badger: observed -> on Rage gain -> Charge self 1 s; on EnrageEnded -> +1000 ChargeAmount and -1000 FlatCooldownReduction until end of combat; enrage player: f261→f361 (5.00 s window, 5000 ms timer); enrage opponent: f161→f261 (5.00 s window, 5000 ms timer); f338→f438 (5.00 s window, 5000 ms timer) |
| `fd8b5f00181548889b0bdcf2ebdf8d95.payload.mpack.gz` | `be8ba34f417c4bd5` | 219 | 10.95 s | Extended Rage Diamond/-, skill/- | Extended Rage:passive | enrage opponent: f124→combat end (open, 6000 ms timer) |
| `feb3a214a51c47adb459fe4e0d2c8a01.payload.mpack.gz` | `d38b5da0382d4748` | 421 | 21.05 s | Honey Badger Gold/Fiery, Hand/3; Campfire Gold/-, Stash/0; Shortened Rage Diamond/-, skill/- | Honey Badger:event; Campfire:idle; Shortened Rage:passive | Honey Badger: observed -> on Rage gain -> Charge self 1 s; on EnrageEnded -> +1000 ChargeAmount and -1000 FlatCooldownReduction until end of combat; enrage opponent: f200→f280 (4.00 s window, 4000 ms timer); f375→combat end (open, 4000 ms timer); first below half opponent: f340 (1444/3255 HP, shield 0) |

## 4. Present-but-unexercised focus cards

- `208b18cedda64a8a903ad40b5b99ea18.payload.mpack.gz` — Feeding Frenzy `3qngowr` (skill, tier Silver): no combat event references this instance and no card attribute delta was reported
- `440fbfe6ed054859a356baaa9d79e7f7.payload.mpack.gz` — Warpaint `ChDrFlE` (Stash, tier Gold): in Stash (section=Stash), never referenced by any combat event
- `67af77d96dd949828ff74ee57a39c9a8.payload.mpack.gz` — Warpaint `_w7zqTb` (Stash, tier Silver): in Stash (section=Stash), never referenced by any combat event
- `921f0f4a20fe4643a19d716192138451.payload.mpack.gz` — Extended Rage `pzKJsp0` (skill, tier Diamond): no combat event references this instance and no card attribute delta was reported
- `a093d76658fd4d9582e11bb85aee01d3.payload.mpack.gz` — Warpaint `c7Z2wtD` (Stash, tier Gold): in Stash (section=Stash), never referenced by any combat event
- `b069c43c47934d5391510520f119cadf.payload.mpack.gz` — Anaconda `W_VnMab` (Stash, tier Gold): in Stash (section=Stash), never referenced by any combat event
- `b069c43c47934d5391510520f119cadf.payload.mpack.gz` — Shortened Rage `LwS_2Mu` (skill, tier Diamond): no combat event references this instance and no card attribute delta was reported
- `cc91f8f6c33346f2bf8d8fa4f9ba636d.payload.mpack.gz` — Honey Badger `rlg56wd` (Stash, tier Gold): in Stash (section=Stash), never referenced by any combat event
- `feb3a214a51c47adb459fe4e0d2c8a01.payload.mpack.gz` — Campfire `ulyA_e0` (Stash, tier Gold): in Stash (section=Stash), never referenced by any combat event

### Passive-only cards (no event reference, but timer/attribute evidence present)

- `70e269ac2ac54ca9bd90d20dded2fe9b.payload.mpack.gz` — Extended Rage `dz6CjLc`: no direct event reference; the defining mechanic is visible only as an owner attribute/timer side effect; owner enrage timer(s): f101→f221 (6.00 s window, 6000 ms timer)
- `88925590d85146cba38c920d638e9a0b.payload.mpack.gz` — Shortened Rage `mIUmiqx`: no direct event reference; the defining mechanic is visible only as an owner attribute/timer side effect; owner enrage timer(s): f198→f278 (4.00 s window, 4000 ms timer)
- `8b4d72c9fe8640a1a46ee06f3d8596c2.payload.mpack.gz` — Extended Rage `592CtYO`: no direct event reference; the defining mechanic is visible only as an owner attribute/timer side effect; owner enrage timer(s): f166→f286 (6.00 s window, 6000 ms timer)
- `de491beb92ff4426a41af7c3b459489e.payload.mpack.gz` — Shortened Rage `dSovu0Z`: no direct event reference; the defining mechanic is visible only as an owner attribute/timer side effect; owner enrage timer(s): f181→f261 (4.00 s window, 4000 ms timer); f311→f391 (4.00 s window, 4000 ms timer)
- `e0a373306e4e421f950da0fc68a40674.payload.mpack.gz` — Extended Rage `Qbpc1qM`: no direct event reference; the defining mechanic is visible only as an owner attribute/timer side effect; owner enrage timer(s): f123→combat end (open, 6000 ms timer)
- `fd8b5f00181548889b0bdcf2ebdf8d95.payload.mpack.gz` — Extended Rage `7QV3So5`: no direct event reference; the defining mechanic is visible only as an owner attribute/timer side effect; owner enrage timer(s): f124→combat end (open, 6000 ms timer)
- `feb3a214a51c47adb459fe4e0d2c8a01.payload.mpack.gz` — Shortened Rage `qtOsnn4`: no direct event reference; the defining mechanic is visible only as an owner attribute/timer side effect; owner enrage timer(s): f200→f280 (4.00 s window, 4000 ms timer); f375→combat end (open, 4000 ms timer)

## 5. Caveats

1. **Side naming.** Every spawned card in these payloads carries `CombatantId = 1`, i.e. the fully visible board is the replay's `Opponent` side. The `Player` side (combatant 0) only exposes health and partial attributes; its items appear as `itm_*`/`skl_*` ids with no template mapping. Rage/PDR/HealthMax events on the player bucket are real but cannot be attributed to a specific template.
2. **HealthMax for combatant 0** is usually missing at spawn, so first-below-half cannot be derived for the player side unless a HealthMax delta is reported. This is why most `firstBelowHalf.player` values are null.
3. **Event targets are not always the modified card.** For `PlayerModifyAttribute`/`CardModifyAttribute` the payload's `Target` is often the trigger/reference card, while the actually modified instance is the `Source` (verified against card attribute deltas, e.g. f88 of `9a1d87b3`: source `itm_4d5n219` gains DamageAmount 10→14 while the target is another item). The report keeps raw payloads so this can be re-checked.
4. **Burning Shield** is a partial negative: its aura recalculation events are present, but the defining half-health `PlayerShieldApply` never appears even though health crosses 50% at f330. A zero-value effect being suppressed or a silent disable are both possible; the trace does not distinguish them.
5. **No template mapping** exists in the payload for `itm_*`/`skl_*` instance ids; they are hidden-board cards. Any claim about those cards' identities would be speculation.
6. **Wild Boar / Toxic Shield / Wrist Blade / Geyser / Resistance Bands / Executioner's Cleaver** are absent from all 200 payloads; do not cite this corpus for those templates.
7. Replays are PvP and short (10–38 s); no monster encounter replay is included, so the Wild Boar monster seed cannot be validated natively here.

## 6. Reproduction

```
python tmp_g06_full/analyze_g06.py     # rebuild replay_g06_report.json
python tmp_g06_full/build_report.py    # rebuild this Markdown + bestSamples
```

Machine-readable data: `tmp_g06_full/replay_g06_report.json` (21 per-replay records with full attribute timelines, focus-card usage, relevant events, raw sample payloads, and mechanic evidence).
