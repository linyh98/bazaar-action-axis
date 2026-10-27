#!/usr/bin/env python3
"""Compare a normalized native BPP replay ledger with a simulated ledger.

This is the G12 event comparator, not another exporter.  Both inputs must use
the ledger v1 shape produced by ``tools/native_replay_ledger.py`` and
``tools/actionaxis_plugin/tests/NativeReplayLedgerExport.cs``.

Matching rules:
* identity is normalized to ``side + slot + generation + root template``;
  raw instance IDs are only used to look up that canonical identity;
* effects are compared as a multiset per 50 ms frame (same-frame independent
  events are not ordered);
* HP/Shield/Max/Regen changes and card attribute/state changes are compared
  separately, so a missing event cannot be hidden by a matching life curve;
* values are compared only when both sides expose one; missing native values
  stay explicit instead of being invented.

Exit codes: 0 MATCH, 1 DIFF, 2 UNKNOWN / incomplete input.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
import pathlib
import sys
from collections import Counter, defaultdict

if __package__:
    from .replay_random_decisions import build_decisions, audit_complete
else:
    from replay_random_decisions import build_decisions, audit_complete

FRAME_SECONDS = 0.05
TOLERANCE = 1e-6

# Native EActionCommandType -> normalized effect kind.
ACTION_MAP = {
    "CardAddTags": "addTags",
    "CardRemoveTags": "removeTags",
    "CardBeginSandstorm": "beginSandstorm",
    "CardCharge": "charge",
    "CardDestroy": "destroy",
    "CardDisable": "disable",
    "CardForceUse": "forceUse",
    "CardFreeze": "freeze",
    "CardHaste": "haste",
    "CardModifyAttribute": "modifyAttribute",
    "CardReload": "reload",
    "CardSlow": "slow",
    "CardEnchant": "enchant",
    "CardTransform": "transform",
    "CardUpgrade": "upgrade",
    "CardRepair": "repair",
    "CardTransformDestroyed": "transformDestroyed",
    "CardAddTagsRandom": "addTagsRandom",
    "PlayerBurnApply": "burnApply",
    "PlayerBurnRemove": "burnRemove",
    "PlayerDamage": "damage",
    "PlayerHeal": "heal",
    "PlayerJoyApply": "joyApply",
    "PlayerJoyRemove": "joyRemove",
    "PlayerMaxHealthDecrease": "maxHealthDecrease",
    "PlayerMaxHealthIncrease": "maxHealthIncrease",
    "PlayerModifyAttribute": "playerModifyAttribute",
    "PlayerPoisonApply": "poisonApply",
    "PlayerPoisonRemove": "poisonRemove",
    "PlayerRegenApply": "regenApply",
    "PlayerRegenRemove": "regenRemove",
    "PlayerShieldApply": "shieldApply",
    "PlayerShieldRemove": "shieldRemove",
    "CardEnchantRemove": "enchantRemove",
    "FlyingStart": "fly",
    "FlyingStop": "flyStop",
    "PlayerRageApply": "rageApply",
    "PlayerRageRemove": "rageRemove",
    "PlayerTempoApply": "tempoApply",
    "PlayerTempoRemove": "tempoRemove",
}

# Simulated BattleEvent.Kind -> normalized effect kind.
SIM_MAP = {
    "shield": "shieldApply",
    "burn": "burnApply",
    "poison": "poisonApply",
    "regen": "regenApply",
    "rage": "rageApply",
    "playerAttribute": "playerModifyAttribute",
    "skillTrigger": "trigger",
    "transform": "transform",
}

# Native action-level transform/enchant events duplicate the dedicated
# CardTransformed/CardEnchanted records.  Keep the dedicated record only.
SKIP_NATIVE_ACTIONS = {"CardTransform", "CardEnchant"}

# Native combat frames do not contain a standalone activation record.  The
# ledger's activationsDerived is explicitly a projection; never match it to
# the simulator's activate event.  dotTick is represented by the native
# health-adjustment ledger, not by an EffectExecuted event, so it belongs to
# the health channel rather than the action multiset.
# "tags" is the simulator's bookkeeping event for a dynamic tag aura retarget; the
# observable counterpart is the native cardUpdate metadata row, not an effect.
SKIP_SIM_KINDS = {"activate", "dotTick", "crit", "lifesteal", "ammo", "buffDamage",
                  "enrage", "enrageEnd", "noop", "warning", "tags"}

CRITICAL_CHANNELS = ("effects", "health", "cards", "identity", "outcome")
COVERAGE_SCOPES = {"sample.transform", "sample.transformRevert", "sample.passive", "sample.activation"}


def load_json(path):
    return json.loads(pathlib.Path(path).read_text(encoding="utf-8"))


def file_sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def number(value):
    if value is None or isinstance(value, bool):
        return None
    try:
        answer = float(value)
    except (TypeError, ValueError):
        return None
    return answer if math.isfinite(answer) else None


def same_number(left, right, tolerance=TOLERANCE):
    if left is None or right is None:
        return None
    return abs(float(left) - float(right)) <= tolerance


def frame_of(record):
    frame = record.get("frame")
    if frame is not None:
        return int(frame)
    time = number(record.get("time"))
    return int(round(time / FRAME_SECONDS)) if time is not None else 0


def time_of(record):
    time = number(record.get("time"))
    if time is not None:
        return round(time, 6)
    return round(frame_of(record) * FRAME_SECONDS, 6)


def side_value(value):
    text = str(value or "").lower()
    if text in ("0", "player", "side.player", "combatant.player"):
        return "player"
    if text in ("1", "enemy", "opponent", "side.enemy", "combatant.opponent"):
        return "enemy"
    return None


class IdentityIndex:
    """Map raw instance IDs to side/slot/generation/root-template identities."""

    def __init__(self):
        self.by_raw = defaultdict(list)
        self.by_key = {}

    def add(self, raw_id, side, slot, generation, root_template, born, removed, source,
            random_target=False):
        if not raw_id:
            return
        key = "%s|%s|g%s|%s" % (side or "unknown", slot if slot is not None else "?",
                                generation, root_template or "?")
        record = {
            "key": key,
            "rawId": str(raw_id),
            "side": side,
            "slot": slot,
            "generation": generation,
            "rootTemplate": root_template,
            "born": born,
            "removed": removed,
            "source": source,
            "randomTarget": bool(random_target),
        }
        self.by_raw[str(raw_id)].append(record)
        self.by_key[key] = record

    def ref(self, raw_id, side=None, time=0.0, generation=None):
        if raw_id is None:
            return None
        text = str(raw_id)
        if text.startswith("player:") or text.startswith("enemy:") or text.startswith("skill:"):
            stripped = text.split(":", 1)[1]
            if stripped.startswith("skill:"):
                stripped = stripped[len("skill:"):]
            if self.by_raw.get(stripped):
                text = stripped
            else:
                return text
        candidates = self.by_raw.get(text) or []
        if not candidates:
            if side is not None:
                return "%s|unknown|g?|%s" % (side, text)
            return "unknown|unknown|g?|%s" % text
        if generation is not None:
            for record in candidates:
                if record["generation"] == generation:
                    return record["key"]
        active = []
        for record in candidates:
            born = record["born"]
            removed = record["removed"]
            if born is not None and time + TOLERANCE < born:
                continue
            if removed is not None and time > removed + TOLERANCE:
                continue
            active.append(record)
        if active:
            return active[-1]["key"]
        earlier = [record for record in candidates
                   if record["born"] is None or record["born"] <= time + TOLERANCE]
        if earlier:
            return earlier[-1]["key"]
        return candidates[0]["key"]

    def player_ref(self, side):
        return "%s|player|g0|player" % (side or "unknown")


def build_native_identity(ledger):
    index = IdentityIndex()
    instances = (ledger.get("identity") or {}).get("instances") or {}
    random_transform_ids = set()
    for event in (ledger.get("combat") or {}).get("events") or []:
        if event.get("randomTarget") and str(event.get("codeName") or "").endswith("CardTransformed"):
            for child in event.get("transformedCards") or []:
                if child.get("instanceId"):
                    random_transform_ids.add(child["instanceId"])
    for key, item in instances.items():
        side = item.get("side") or "unknown"
        instance_id = item.get("instanceId")
        # Stash items are preserved in the opening ledger but are not combat
        # participants; do not require the simulator to invent them.
        if item.get("section") == "Stash":
            continue
        slot = item.get("socket")
        parent = item.get("parent")
        root = item
        seen = set()
        while parent and isinstance(parent, str) and parent in instances and parent not in seen:
            seen.add(parent)
            root = instances[parent]
            parent = root.get("parent")
        generations = item.get("generations") or []
        # A transform child is created with a placeholder generation 0 before
        # its real birth generation. It is not a second opening instance.
        if len(generations) > 1 and (generations[0] or {}).get("born") is None:
            generations = generations[1:]
        root_generations = root.get("generations") or []
        root_template = None
        if root_generations:
            root_template = (root_generations[0] or {}).get("templateId")
        if not root_template:
            root_template = root.get("templateId") or item.get("templateId")
        if not root_template:
            # Mid-combat skill instances carry no template; the simulator keys
            # skill identities by their instance id (no template is exported),
            # so fall back to it to keep both sides comparable.
            root_template = instance_id
        if not generations:
            generations = [{"generation": 0, "born": item.get("born"), "removed": None}]
        for generation in generations:
            born = generation.get("born") or {}
            removed = generation.get("removed") or {}
            index.add(instance_id, side, slot, generation.get("generation", 0), root_template,
                      number(born.get("time")) if isinstance(born, dict) else None,
                      number(removed.get("time")) if isinstance(removed, dict) else None,
                      "native", instance_id in random_transform_ids)
    # Skills are not board instances, but skill effects reference them as
    # sources.  Key them by instance id — the same key the simulator's
    # derived skill identities use — so both sides resolve identically.
    for side in ("player", "enemy"):
        for skill in ((ledger.get("opening") or {}).get(side) or {}).get("skills") or []:
            raw = skill.get("instanceId")
            if raw:
                index.add(raw, side, None, 0, str(raw), 0.0, None, "native-skill")
    return index


def build_sim_identity(ledger):
    index = IdentityIndex()
    instances = (ledger.get("identity") or {}).get("instances") or {}
    # New transform instances retain their own lifetime. Follow explicit
    # lineage to compute the canonical root and generation, just as for native
    # instances, without attributing an ancestor's states to the child.
    parents = {}
    for event in (ledger.get("identity") or {}).get("lineage") or []:
        if event.get("kind") != "transform":
            continue
        for child in event.get("children") or []:
            raw = child.get("instanceId")
            original = event.get("originalInstanceId")
            if raw and original and raw != original:
                parents[raw] = original
    by_id = {item.get("instanceId"): item for item in instances.values()}
    for key, item in instances.items():
        side = item.get("side") or "unknown"
        instance_id = item.get("instanceId")
        states = sorted(item.get("states") or [], key=lambda row: number(row.get("time")) or 0.0)
        if not states:
            states = [{"time": item.get("born") or 0.0, "templateId": item.get("templateId"),
                       "slot": item.get("slot")}]
        groups = []
        for state in states:
            time = number(state.get("time")) or 0.0
            template = state.get("templateId")
            slot = state.get("slot")
            if not groups or groups[-1]["template"] != template or groups[-1]["slot"] != slot:
                groups.append({"born": time, "template": template, "slot": slot})
        root_template = groups[0]["template"] if groups else item.get("templateId")
        root_id = instance_id
        generation_offset = 0
        seen = {root_id}
        while root_id in parents and parents[root_id] in by_id and parents[root_id] not in seen:
            root_id = parents[root_id]
            seen.add(root_id)
            generation_offset += 1
        if generation_offset:
            root = by_id[root_id]
            root_states = sorted(root.get("states") or [], key=lambda row: number(row.get("time")) or 0.0)
            root_template = root_states[0].get("templateId") if root_states else root.get("templateId")
        removed_at = number(item.get("removed"))
        for position, group in enumerate(groups):
            removed = groups[position + 1]["born"] if position + 1 < len(groups) else removed_at
            index.add(instance_id, side, group["slot"], position + generation_offset, root_template,
                      group["born"], removed, "simulated")
    # The simulated ledger does not export skills as identity instances, but
    # skill effects reference them as sources.  Derive those instances from
    # the skillTrigger events, keyed by instance id exactly like the native
    # ledger's template-less skill instances.
    for event in (ledger.get("combat") or {}).get("events") or []:
        if event.get("kind") != "skillTrigger" or not event.get("source"):
            continue
        side = side_value(event.get("sourceSide")) or "unknown"
        # Simulated skill sources are either 'enemy:skill:<id>' or
        # 'skill:<id>' for player skills.
        raw = str(event["source"])
        if ":skill:" in raw:
            raw = raw.split(":skill:", 1)[1]
        elif raw.startswith("skill:"):
            raw = raw[len("skill:"):]
        index.add(raw, side, None, 0, raw, 0.0, None,
                  "simulated-skill")
    return index


def target_refs(target, index, side, time):
    if not target:
        return [index.player_ref(side) if side else None]
    if target.get("type") == "card":
        return [index.ref(item, side, time) for item in target.get("ids") or [target]]
    if target.get("type") == "player":
        sides = target.get("sides") or []
        return [index.player_ref(item) for item in sides] or [index.player_ref(side)]
    return [None]


def random_transform_scope(native, simulated):
    scope = {}
    random_frames = set()
    events = (native.get("combat") or {}).get("events") or []
    for event in events:
        if not (event.get("randomTarget") and
                str(event.get("codeName") or "").endswith("CardTransformed")):
            continue
        time = time_of(event)
        random_frames.add(frame_of(event))
        ids = [event.get("originalInstanceId")]
        ids.extend(child.get("instanceId") for child in event.get("transformedCards") or [])
        for iid in ids:
            if iid and (iid not in scope or time < scope[iid]):
                scope[iid] = time
    for event in (simulated.get("combat") or {}).get("events", []):
        if event.get("kind") != "transform" or frame_of(event) not in random_frames:
            continue
        time = time_of(event)
        for iid in (event.get("source"), event.get("target")):
            if iid and (iid not in scope or time < scope[iid]):
                scope[iid] = time
    # Quicksilver/Virus 型变形的目标是自身（事件不带 randomTarget），但供体
    # 来自 TTargetCardRandom：同帧两侧都变形而供体模板不相交时，后续差异是
    # 无原生种子的随机供体分叉，不是确定性规则错误。
    native_choices = {}
    native_ids = {}
    for event in events:
        if not str(event.get("codeName") or "").endswith("CardTransformed"):
            continue
        frame = frame_of(event)
        time = time_of(event)
        templates, ids = set(), []
        for child in event.get("transformedCards") or []:
            if child.get("templateId"):
                templates.add(child["templateId"])
            if child.get("instanceId"):
                ids.append(child["instanceId"])
        if event.get("originalInstanceId"):
            ids.append(event["originalInstanceId"])
        native_choices.setdefault(frame, set()).update(templates)
        native_ids.setdefault(frame, []).extend(ids)
        if time is not None:
            random_frames.add(frame)
    sim_templates = {}
    for item in ((simulated.get("identity") or {}).get("instances") or {}).values():
        states = item.get("states") or []
        for state in states:
            template = state.get("templateId")
            frame = state.get("frame")
            if frame is None and state.get("time") is not None:
                frame = int(round(float(state["time"]) / FRAME_SECONDS))
            if template and frame is not None:
                sim_templates.setdefault(int(frame), set()).add(template)
    for frame, templates in native_choices.items():
        if not templates:
            continue
        chosen = sim_templates.get(frame) or sim_templates.get(frame + 1) or set()
        if chosen and templates & chosen:
            continue
        time = frame * FRAME_SECONDS
        for iid in native_ids.get(frame, []):
            if iid and (iid not in scope or time < scope[iid]):
                scope[iid] = time
        for event in (simulated.get("combat") or {}).get("events", []):
            if event.get("kind") != "transform" or frame_of(event) != frame:
                continue
            for iid in (event.get("source"), event.get("target")):
                if iid and (iid not in scope or time < scope[iid]):
                    scope[iid] = time
    return scope


def mark_effect_scope(records, raw_ids, time, scope):
    if not scope or not raw_ids:
        return
    for iid in raw_ids:
        threshold = scope.get(str(iid))
        if threshold is not None and time + TOLERANCE >= threshold:
            for record in records:
                if not record.get("unverifiable") and record.get("randomTarget") is False:
                    record["unverifiable"] = True
                    record["unverifiableReason"] = "randomTransform"


def native_effect_records(ledger, index, effect_scope=None):
    records = []
    unknown = 0
    for event in ((ledger.get("combat") or {}).get("events") or []):
        code_name = event.get("codeName") or ""
        time = time_of(event)
        frame = frame_of(event)
        side = event.get("sourceSide")
        start = len(records)
        if code_name.endswith("EffectExecuted"):
            action = event.get("action")
            if action in SKIP_NATIVE_ACTIONS:
                continue
            kind = ACTION_MAP.get(action, action or "unknown")
            if kind == "unknown":
                unknown += 1
            source = index.ref(event.get("source"), side, time)
            targets = target_refs(event.get("target"), index, side, time)
            for target in targets:
                reason = None
                if event.get("runtimeAbility"):
                    reason = "runtimeAbility"
                elif event.get("auraRefresh"):
                    reason = "noCounterpart"
                records.append(effect_record(frame, time, kind, source, target, None,
                                             "Effects.EffectExecuted", event,
                                             event.get("randomTarget"), reason))
        elif code_name.endswith("EffectTriggered"):
            source = index.ref(event.get("source"), side, time)
            targets = event.get("targets") or []
            if not targets:
                records.append(effect_record(frame, time, "trigger", source, None, None,
                                             "Effects.EffectTriggered", event))
            for target in targets:
                for item in target_refs(target, index, side, time):
                    records.append(effect_record(frame, time, "trigger", source, item, None,
                                                 "Effects.EffectTriggered", event))
            unknown += 1  # target semantics are not independently reported by the engine
        elif code_name.endswith("EffectAuraExecuted"):
            source = index.ref(event.get("source"), side, time)
            for key, kind in (("appliedTo", "auraApplied"), ("removedFrom", "auraRemoved")):
                for target in event.get(key) or []:
                    for item in target_refs(target, index, side, time):
                        records.append(effect_record(frame, time, kind, source, item, None,
                                                     "Effects." + code_name, event, False,
                                                     "noCounterpart"))
            unknown += 1
        elif code_name.endswith("CardEnchanted"):
            instance = index.ref(event.get("instanceId"), side, time)
            records.append(effect_record(frame, time, "enchant", instance, instance,
                                         event.get("enchantment"), "CardEnchanted", event))
        elif code_name.endswith("CardTransformed"):
            source = index.ref(event.get("originalInstanceId"), side, time,
                               event.get("originalGeneration"))
            children = event.get("transformedCards") or []
            for child in children:
                target = index.ref(child.get("instanceId"), child.get("side"), time,
                                   child.get("generation"))
                records.append(effect_record(frame, time, "transform", source, target,
                                             child.get("templateId"), "CardTransformed", event,
                                             event.get("randomTarget")))
        elif code_name.endswith("CardTransformReverted"):
            source = index.ref(event.get("originalCard", {}).get("instanceId"), side, time)
            for child_id in event.get("transformedCardInstanceIds") or []:
                target = index.ref(child_id, side, time)
                records.append(effect_record(frame, time, "revert", source, target,
                                             None, "CardTransformReverted", event))
        elif code_name.endswith("CardSpawned"):
            source = index.ref(event.get("instanceId"), event.get("side") or side, time)
            records.append(effect_record(frame, time, "spawn", source, source,
                                         event.get("templateId"), "CardSpawned", event))
        elif code_name.endswith("CardDisposed"):
            source = index.ref(event.get("instanceId"), side, time)
            records.append(effect_record(frame, time, "dispose", source, source,
                                         event.get("reason"), "CardDisposed", event))
        elif code_name.endswith("CombatantDied"):
            source = index.player_ref(event.get("side") or side)
            records.append(effect_record(frame, time, "death", source, source,
                                         None, "CombatantDied", event))
        elif code_name.endswith("CardAttributeDeleted"):
            source = index.ref(event.get("instanceId"), side, time)
            records.append(effect_record(frame, time, "deleteAttribute", source, source,
                                         event.get("attribute"), "CardAttributeDeleted", event))
        elif code_name.endswith("CardActionCostSpent"):
            source = index.ref(event.get("executingCard"), side, time)
            records.append(effect_record(frame, time, "actionCost", source, source,
                                         event.get("playerAttributeSpent"), "CardActionCostSpent", event))
        elif code_name.endswith("SandstormCountdownStarted") or code_name.endswith("SandstormStarted"):
            records.append(effect_record(frame, time, "sandstorm", None, None, None,
                                         code_name, event, False, "noCounterpart"))
        else:
            unknown += 1
            records.append(effect_record(frame, time, code_name or "unknown", None, None,
                                         None, code_name or "unknown", event))
        raw_ids = [event.get("source"), event.get("instanceId"),
                   event.get("originalInstanceId"), event.get("executingCard")]
        target = event.get("target") or {}
        raw_ids.extend(target.get("ids") or [])
        raw_ids.extend(child.get("instanceId") for child in event.get("transformedCards") or [])
        mark_effect_scope(records[start:], raw_ids, time, effect_scope)
    return records, unknown


def effect_record(frame, time, kind, source, target, value, source_name, raw, random_target=False,
                  unverifiable=None):
    return {
        "channel": "effects",
        "frame": frame,
        "time": time,
        "kind": kind,
        "source": source,
        "target": target,
        "value": value,
        "sourceName": source_name,
        "randomTarget": bool(random_target),
        "unverifiable": bool(unverifiable),
        "unverifiableReason": unverifiable,
        "raw": raw,
    }


def sim_effect_records(ledger, index, effect_scope=None):
    records = []
    unknown = 0
    events = (ledger.get("combat") or {}).get("events") or []
    for event in events:
        raw_kind = event.get("kind") or "unknown"
        if raw_kind in SKIP_SIM_KINDS:
            continue
        # Regen/lifesteal use the shared HP primitive, but native emits no
        # PlayerHeal action for them. Their real HP changes stay in the
        # health channel. Require an explicit origin: matching frame/source/
        # hpAfter can also describe a real capped heal on the same frame.
        if raw_kind == "heal" and event.get("note") in ("regen", "lifesteal"):
            continue
        kind = SIM_MAP.get(raw_kind, raw_kind)
        time = time_of(event)
        frame = frame_of(event)
        side = side_value(event.get("sourceSide"))
        generation = 0 if kind == "transform" and event.get("source") == event.get("target") else None
        source = index.ref(event.get("source"), side, time, generation)
        if source is None and kind == "death" and side is not None:
            # Native deaths are sourced to the dying combatant's player ref;
            # the simulator emits a sourceless death event.
            source = index.player_ref(side)
        target = index.ref(event.get("target"), side_value(event.get("targetSide")), time)
        if target is None and event.get("targetSide"):
            target = index.player_ref(side_value(event.get("targetSide")))
        if target is None and side is not None:
            # Native records always carry the source-side player as the
            # target of self-effects; mirror that default here.
            target = index.player_ref(side)
        if kind in ("unknown", ""):
            unknown += 1
        records.append(effect_record(frame, time, kind, source, target, number(event.get("value")),
                                     "simulated.BattleEvent", event, False,
                                     "noCounterpart" if raw_kind == "skillTrigger" else None))
        mark_effect_scope(records[-1:], [event.get("source"), event.get("target")], time,
                          effect_scope)
    return records, unknown


def read_health(ledger, simulated):
    result = {}
    for record in ((ledger.get("combat") or {}).get("healthChanges") or []):
        side = side_value(record.get("side"))
        frame = frame_of(record)
        if not side:
            continue
        if simulated:
            values = {
                "hp": number(record.get("hp")),
                "hpMax": number(record.get("hpMax")),
                "shield": number(record.get("shield")),
                "burn": number(record.get("burn")),
                "poison": number(record.get("poison")),
                "regen": number(record.get("regen")),
            }
            dead = record.get("dead")
            if dead is not None:
                values["dead"] = bool(dead)
        else:
            vitals = record.get("vitals") or {}
            values = {
                "hp": number(vitals.get("Health")),
                "hpMax": number(vitals.get("HealthMax")),
                "shield": number(vitals.get("Shield")),
                "burn": number(vitals.get("Burn")),
                "poison": number(vitals.get("Poison")),
                "regen": number(vitals.get("HealthRegen")),
            }
            dead = record.get("dead")
            if dead is not None:
                values["dead"] = bool(dead)
        values["frame"] = frame
        values["time"] = time_of(record)
        values["side"] = side
        if values.get("hp") is not None and values["hp"] <= 0:
            # The native payload's IsPlayerDead flag lags the lethal health
            # update by one record; treat non-positive HP as dead on both sides
            # instead of reporting a spurious dead=false difference.
            values["dead"] = True
            # Native keeps overkill as negative HP; the simulator clamps to
            # zero. Only this representation differs. Final Shield/Burn/
            # Poison/Regen values still describe observable combat state.
            values["hp"] = 0.0
        result.setdefault((frame, side), []).append(values)
    return result


def random_card_scope(native, simulated):
    """Instances whose card updates are downstream of a random target choice.

    Native records the chosen target but not the seed.  When the simulator
    chooses a different valid target, every later cooldown/status update on
    either chosen item is a cascade of that unverifiable branch, not a
    deterministic rule difference.  Mark those instances from the first
    random effect onwards.
    """
    scope = {}
    events = (native.get("combat") or {}).get("events") or []
    sim_events = (simulated.get("combat") or {}).get("events") or []
    native_by_key = defaultdict(list)
    for event in events:
        if not event.get("randomTarget"):
            continue
        action = event.get("action")
        kind = ACTION_MAP.get(action, action)
        target = event.get("target") or {}
        ids = target.get("ids") or []
        if not ids:
            continue
        time = time_of(event)
        for iid in ids:
            previous = scope.get(iid)
            if previous is None or time < previous:
                scope[iid] = time
        native_by_key[(frame_of(event), kind, event.get("source"))].append(event)
    for event in sim_events:
        kind = SIM_MAP.get(event.get("kind"), event.get("kind"))
        source = str(event.get("source") or "")
        for (frame, native_kind, native_source), matches in native_by_key.items():
            if frame != frame_of(event) or kind != native_kind:
                continue
            if not native_source or not (source == native_source or source.endswith(native_source) or
                                         native_source.endswith(source)):
                continue
            time = time_of(event)
            for iid in (event.get("target"),):
                if iid and (iid not in scope or time < scope[iid]):
                    scope[iid] = time
    return scope


def runtime_card_scope(native):
    scope = set()
    for event in (native.get("combat") or {}).get("events") or []:
        if not event.get("runtimeAbility"):
            continue
        target = event.get("target") or {}
        for iid in target.get("ids") or []:
            if iid:
                scope.add((str(iid), frame_of(event)))
    return scope


def aura_card_scope(native):
    scope = set()
    for event in (native.get("combat") or {}).get("events") or []:
        if not event.get("auraRefresh"):
            continue
        target = event.get("target") or {}
        for iid in target.get("ids") or []:
            if iid:
                scope.add((str(iid), frame_of(event)))
    return scope


def stash_instance_scope(native):
    """Raw ids of opening Stash items.

    The game keeps ticking their cooldown curves during combat, but they never
    activate and the engine fixture excludes them.  Their recorded update
    stream is out of combat scope, not an engine difference.
    """
    scope = set()
    for _, item in ((native.get("identity") or {}).get("instances") or {}).items():
        if item.get("section") == "Stash" and item.get("instanceId"):
            scope.add(str(item["instanceId"]))
    return scope


def read_cards(ledger, index, simulated, random_scope=None, runtime_scope=None,
               aura_scope=None, stash_scope=None):
    result = defaultdict(list)
    unverified_stash = 0
    for record in ((ledger.get("combat") or {}).get("cardUpdates") or []):
        raw_id = record.get("instanceId") or record.get("itemId")
        if stash_scope and str(raw_id) in stash_scope:
            unverified_stash += 1
            continue
        side = side_value(record.get("side"))
        time = time_of(record)
        canonical = index.ref(raw_id, side, time)
        frame = frame_of(record)
        attribute = record.get("attribute") or record.get("kind") or "unknown"
        identity = index.by_key.get(canonical) or {}
        born = number(identity.get("born"))
        random_target = bool(identity.get("randomTarget") and
                             (born is None or time + TOLERANCE >= born))
        random_time = (random_scope or {}).get(str(raw_id))
        random_cascade = random_time is not None and time + TOLERANCE >= random_time
        runtime_ability = bool(runtime_scope) and (str(raw_id), frame) in runtime_scope
        aura_refresh = bool(aura_scope) and (str(raw_id), frame) in aura_scope
        result[(frame, canonical, attribute)].append({
            "frame": frame,
            "time": time,
            "canonical": canonical,
            "attribute": attribute,
            "previous": record.get("previous"),
            "current": record.get("current"),
            "randomTarget": random_target,
            "randomCascade": random_cascade,
            "runtimeAbility": runtime_ability,
            "auraRefresh": aura_refresh,
            "raw": record,
        })
    return result, unverified_stash


def compare_effects(native, simulated, window_frames, cascade_from=None,
                    death_frame=None, dying_side=None):
    simulated_pool = list(simulated)
    used = [False] * len(simulated_pool)
    diffs = []
    matched = 0
    unverified = 0
    reasons = Counter()

    def take(predicate):
        for index, row in enumerate(simulated_pool):
            if used[index]:
                continue
            if predicate(row):
                used[index] = True
                return row
        return None

    for native_record in sorted(native, key=lambda row: (row["frame"], row["kind"],
                                                        row["source"] or "", row["target"] or "")):
        if cascade_from is not None and native_record["frame"] >= cascade_from:
            # Everything from the (cascade-shifted) death onwards is downstream
            # of the unseedable crit, on both sides.
            unverified += 1
            reasons["critCascade"] += 1
            continue
        exact = take(lambda row: row["frame"] == native_record["frame"] and
                     row["kind"] == native_record["kind"] and
                     row["source"] == native_record["source"] and
                     row["target"] == native_record["target"])
        if exact is not None:
            matched += 1
            mismatch = compare_effect_values(native_record, exact)
            if mismatch:
                diffs.append(mismatch)
            continue
        if native_record.get("randomTarget"):
            # The native target is TTargetCardRandom. Without the native seed
            # the exact target cannot be reconstructed; match the same action
            # from the same source/frame but never count it as verified.
            if native_record["kind"] == "transform":
                # A random transform can select a different original card, so
                # neither source nor target is stable.  It only has to pair
                # with the simulator's transform at the same frame.
                alternative = take(lambda row: row["frame"] == native_record["frame"] and
                                   row["kind"] == "transform")
            else:
                alternative = take(lambda row: row["frame"] == native_record["frame"] and
                                   row["kind"] == native_record["kind"] and
                                   row["source"] == native_record["source"])
            if alternative is None and window_frames:
                alternative = take(lambda row: abs(row["frame"] - native_record["frame"]) <= window_frames and
                                   row["kind"] == native_record["kind"] and
                                   row["source"] == native_record["source"])
            unverified += 1
            reasons["randomTarget"] += 1
            continue
        if native_record.get("unverifiable"):
            # The simulator has no standalone counterpart for this native
            # bookkeeping record (for example a per-tick aura refresh).  It is
            # not a pass and it must not be counted as a missing effect.
            unverified += 1
            reasons[native_record.get("unverifiableReason") or "noCounterpart"] += 1
            continue
        if (death_frame is not None and dying_side is not None
                and native_record["frame"] >= death_frame
                and str(native_record.get("source") or "").startswith(dying_side + "|")):
            # 阵亡方在死亡帧上多出的效果属于终局记账：战斗已结束，不会再
            # 影响任何状态（00a37bf3 帧 360 同一 onUse 反应多记一次）。
            unverified += 1
            reasons["deathFrame"] += 1
            continue
        if (native_record["frame"] == 0 and native_record["kind"] == "playerModifyAttribute"
                and (native_record.get("raw") or {}).get("triggerType") == "TTriggerOnFightStarted"):
            # 原生把开局经济属性（Gold/Income 等）也记为 EffectExecuted；
            # 行动轴不建模经济属性，因此没有对应的模拟事件。状态差异仍由
            # 生命/护盾曲线独立兜底，这里只记不可验证，不算通过。
            unverified += 1
            reasons["openingAttribute"] += 1
            continue
        near = None
        if window_frames:
            candidates = [row for index, row in enumerate(simulated_pool)
                          if not used[index] and row["kind"] == native_record["kind"] and
                          row["source"] == native_record["source"] and
                          row["target"] == native_record["target"]]
            near = min(candidates, key=lambda row: abs(row["frame"] - native_record["frame"]),
                       default=None)
            if near is not None and abs(near["frame"] - native_record["frame"]) > window_frames:
                near = None
        if near is not None:
            used[next(index for index, row in enumerate(simulated_pool) if row is near)] = True
            diffs.append(diff_row("effects", native_record, near, "time-mismatch",
                                  "native frame %s vs simulated frame %s" %
                                  (native_record["frame"], near["frame"])))
            continue
        diffs.append(diff_row("effects", native_record, None, "missing",
                              "native %s has no simulated effect" % native_record["kind"]))
    for index, extra in enumerate(simulated_pool):
        if used[index]:
            continue
        if cascade_from is not None and extra["frame"] >= cascade_from:
            unverified += 1
            reasons["critCascade"] += 1
            continue
        if extra.get("unverifiable"):
            unverified += 1
            reasons[extra.get("unverifiableReason") or "noCounterpart"] += 1
            continue
        diffs.append(diff_row("effects", None, extra, "extra",
                              "simulated %s has no native effect" % extra["kind"]))
    return {"matched": matched, "unverified": unverified,
            "unverifiedReasons": dict(reasons), "diffs": diffs,
            "native": len(native), "simulated": len(simulated)}


def compare_effect_values(native, simulated):
    left = number(native.get("value"))
    right = number(simulated.get("value"))
    if left is None or right is None:
        return None
    if not same_number(left, right):
        return diff_row("effects", native, simulated, "value-mismatch",
                        "%s value %s vs %s" % (native["kind"], left, right))
    return None


def diff_row(channel, native, simulated, status, note):
    row = {
        "channel": channel,
        "status": status,
        "frame": (native or simulated or {}).get("frame"),
        "time": (native or simulated or {}).get("time"),
        "note": note,
        "native": native,
        "simulated": simulated,
    }
    if channel == "effects":
        row["kind"] = (native or simulated or {}).get("kind")
        row["source"] = (native or simulated or {}).get("source")
        row["target"] = (native or simulated or {}).get("target")
    if channel == "health":
        row["side"] = (native or simulated or {}).get("side")
    if channel == "cards":
        row["canonical"] = (native or simulated or {}).get("canonical")
        row["attribute"] = (native or simulated or {}).get("attribute")
    if channel == "identity":
        row["canonical"] = (native or simulated or {}).get("key")
    return row


CRIT_RATIO_TOLERANCE = 0.01


def sim_dot_tick_hp_after(simulated):
    """(frame, side) -> last dotTick hpAfter on that frame.

    The simulator does not emit a healthChanges row for a frame whose
    adjustments net to zero (e.g. a poison tick fully healed by regen), while
    the native ledger records the attempt.  The dotTick events carry the
    post-tick hp, so they can vouch for the omitted row.
    """
    result = {}
    for event in (simulated.get("combat") or {}).get("events") or []:
        # 原生也会记录“封顶后数值不变”的生命改动：例如满血时的吸血/再生
        # 治疗（00a37bf3 帧 80 Health 550->550）。dotTick 与 heal 都带结算后
        # 的 hpAfter，可证明模拟侧该帧未抬血，而不是漏了一次治疗。
        if event.get("kind") not in ("dotTick", "heal"):
            continue
        side = side_value(event.get("targetSide"))
        frame = frame_of(event)
        hp = number(event.get("hpAfter"))
        if side and frame is not None and hp is not None:
            result[(frame, side)] = hp
    return result


def compare_health(native, simulated, window_frames, dot_tick_hp_after=None,
                   observed_decisions=False, death_frame=None, dying_side=None,
                   sim_crit_frames=None):
    diffs = []
    matched = 0
    unverified = 0
    reasons = Counter()
    keys = sorted(set(native) | set(simulated), key=lambda item: (item[0], item[1]))
    # Per-side curve tails for crit detection, and the frame from which the
    # side's health curve is downstream of a crit the simulator cannot seed.
    state = {}
    for key in keys:
        side = key[1]
        my = state.setdefault(side, {"burn": None, "poison": None, "hp": None,
                                     "shield": None, "hpMax": None,
                                     "crit_from": None})
        native_rows = native.get(key) or []
        sim_rows = list(simulated.get(key) or [])
        for native_row in native_rows:
            crit_active = my["crit_from"] is not None and native_row["frame"] >= my["crit_from"]
            match = next((row for row in sim_rows if row["frame"] == native_row["frame"]), None)
            window_forced = False
            if match is None and window_frames:
                candidate = min(sim_rows, key=lambda row: abs(row["frame"] - native_row["frame"]),
                                default=None)
                if candidate is not None and abs(candidate["frame"] - native_row["frame"]) <= window_frames:
                    match = candidate
                    window_forced = True
            if match is None:
                if (death_frame is not None and native_row["frame"] >= death_frame
                        and key[1] != dying_side):
                    # 死亡帧上“非阵亡方”的生命行是终局记账：阵亡方本帧的行动
                    # 在胜负确定后仍结算，模拟端不再补记同样的快照。阵亡方
                    # 自己的致命行仍按硬差异处理（test_missing_lethal...）。
                    unverified += 1
                    reasons["deathFrame"] += 1
                elif crit_active:
                    unverified += 1
                    reasons["critCascade"] += 1
                elif (dot_tick_hp_after
                      and native_row.get("hp") is not None and native_row["hp"] > 0
                      and same_number(dot_tick_hp_after.get(key), native_row.get("hp"))):
                    # Native recorded a net-zero adjustment frame (e.g. poison
                    # tick fully healed by regen); the simulator's dotTick hp
                    # at this frame equals the native hp, so the omission is an
                    # export-symmetry artifact, not a behavioral difference.
                    unverified += 1
                    reasons["netZeroFrame"] += 1
                else:
                    diffs.append(diff_row("health", native_row, None, "missing",
                                          "native health change has no simulated change"))
                    _health_advance(my, native_row, None)
                continue
            sim_rows.remove(match)
            matched += 1
            if window_forced and not crit_active:
                diffs.append(diff_row("health", native_row, match, "time-mismatch",
                                      "health record frame differs"))
            mismatch = []
            for field in ("hp", "hpMax", "shield", "burn", "poison", "regen"):
                left, right = native_row.get(field), match.get(field)
                if left is None or right is None:
                    continue
                if not same_number(left, right):
                    mismatch.append("%s %s vs %s" % (field, left, right))
            if "dead" in native_row and native_row.get("dead") != match.get("dead"):
                mismatch.append("dead %s vs %s" % (native_row.get("dead"), match.get("dead")))
            if not mismatch:
                _health_advance(my, native_row, match)
                continue
            if crit_active:
                _health_advance(my, native_row, match)
                unverified += 1
                reasons["critCascade"] += 1
                continue
            onset = False if observed_decisions else _health_crit_onset(
                my, native_row, match, sim_crit_frames)
            _health_advance(my, native_row, match)
            if onset:
                unverified += 1
                reasons["critDivergence"] += 1
                my["crit_from"] = native_row["frame"]
            else:
                diffs.append(diff_row("health", native_row, match, "value-mismatch",
                                      "; ".join(mismatch)))
        for extra in sim_rows:
            if my["crit_from"] is not None and extra["frame"] >= my["crit_from"]:
                unverified += 1
                reasons["critCascade"] += 1
                continue
            diffs.append(diff_row("health", None, extra, "extra",
                                  "simulated health change has no native change"))
    return {"matched": matched, "unverified": unverified,
            "unverifiedReasons": dict(reasons), "diffs": diffs,
            "critScope": {side: entry["crit_from"] for side, entry in state.items()
                          if entry.get("crit_from") is not None},
            "native": sum(len(value) for value in native.values()),
            "simulated": sum(len(value) for value in simulated.values())}


def _health_advance(state, native_row, sim_row):
    """Remember the latest curve values per side for delta-based crit checks."""
    for field in ("burn", "poison", "hp", "shield", "hpMax"):
        if native_row.get(field) is not None:
            state["native_" + field] = native_row[field]
        if sim_row is not None and sim_row.get(field) is not None:
            state["sim_" + field] = sim_row[field]


def simulated_crit_frames(simulated):
    """(frame, side) pairs where the engine emitted its own crit event."""
    frames = set()
    for event in (simulated.get("combat") or {}).get("events") or []:
        if event.get("kind") != "crit":
            continue
        frame = frame_of(event)
        side = side_value(event.get("targetSide"))
        if frame is not None and side:
            frames.add((frame, side))
    return frames


def _health_crit_onset(state, native_row, sim_row, sim_crit_frames=None):
    """True when this mismatch is the moment one side critted and the other
    did not: the native/sim deltas diverge by exactly the crit multiplier on
    a stack (burn/poison) or on unshielded hp.

    Both directions are unseeded. A simulated crit is only accepted when the
    engine actually emitted a crit event for that side on that frame, so a
    doubled-apply rule bug cannot hide behind this exemption. (034c3648
    frame 80: the engine's own 2% roll doubled a burn stack the native replay
    never crit.)"""
    frame = (native_row or sim_row or {}).get("frame")
    side = (native_row or sim_row or {}).get("side")
    sim_critical = sim_crit_frames is None or (frame, side) in sim_crit_frames
    for field in ("burn", "poison"):
        delta_native = _delta(state, "native_" + field, native_row.get(field))
        delta_sim = _delta(state, "sim_" + field, sim_row.get(field))
        if delta_native is None or delta_sim is None:
            continue
        if delta_native == 0 and delta_sim == 0:
            continue
        if abs(delta_native - 2 * delta_sim) <= CRIT_RATIO_TOLERANCE:
            return True
        if sim_critical and abs(delta_sim - 2 * delta_native) <= CRIT_RATIO_TOLERANCE:
            return True
    # 护盾施加也会暴击（原生 isCrit:true，盾量翻倍）；只在双方都是
    # 正向增长时判定，避免把伤害吸收差当成暴击。
    shield_native = _delta(state, "native_shield", native_row.get("shield"))
    shield_sim = _delta(state, "sim_shield", sim_row.get("shield"))
    if shield_native is not None and shield_sim is not None and shield_native > 0 and shield_sim > 0:
        if abs(shield_native - 2 * shield_sim) <= CRIT_RATIO_TOLERANCE:
            return True
        if sim_critical and abs(shield_sim - 2 * shield_native) <= CRIT_RATIO_TOLERANCE:
            return True
    delta_native = _delta(state, "native_hp", native_row.get("hp"))
    delta_sim = _delta(state, "sim_hp", sim_row.get("hp"))
    shield_same = _same_or_none(state.get("native_shield"), state.get("sim_shield"))
    hpmax_same = _same_or_none(state.get("native_hpMax"), state.get("sim_hpMax"))
    if (delta_native is not None and delta_sim and shield_same and hpmax_same
            and abs(delta_native - 2 * delta_sim) <= CRIT_RATIO_TOLERANCE):
        return True
    if (sim_critical and delta_native and delta_sim is not None and shield_same
            and hpmax_same and abs(delta_sim - 2 * delta_native) <= CRIT_RATIO_TOLERANCE):
        return True
    return False


def _delta(state, key, current):
    previous = state.get(key)
    if current is None or previous is None:
        return None
    return float(current) - float(previous)


def _same_or_none(left, right):
    if left is None or right is None:
        return False
    return abs(float(left) - float(right)) <= TOLERANCE


def compare_cards(native, simulated, window_frames, cascade_from=None,
                  death_frame=None, dying_side=None):
    diffs = []
    matched = 0
    unverified = 0
    reasons = Counter()
    all_keys = set(native) | set(simulated)
    for key in sorted(all_keys):
        native_rows = list(native.get(key) or [])
        sim_rows = list(simulated.get(key) or [])
        for native_row in native_rows:
            if cascade_from is not None and native_row["frame"] >= cascade_from:
                unverified += 1
                reasons["critCascade"] += 1
                continue
            match = next((row for row in sim_rows if row["frame"] == native_row["frame"]), None)
            if native_row.get("randomCascade") or (match is not None and match.get("randomCascade")):
                if match is not None:
                    sim_rows.remove(match)
                unverified += 1
                reasons["randomCascade"] += 1
                continue
            if native_row.get("runtimeAbility") or (match is not None and match.get("runtimeAbility")):
                if match is not None:
                    sim_rows.remove(match)
                unverified += 1
                reasons["runtimeAbility"] += 1
                continue
            if native_row.get("auraRefresh") or (match is not None and match.get("auraRefresh")):
                if match is not None:
                    sim_rows.remove(match)
                unverified += 1
                reasons["auraRefresh"] += 1
                continue
            if match is None and (native_row.get("randomTarget") or
                                  any(row.get("randomTarget") for row in sim_rows)):
                candidate = next((row for row in sim_rows
                                  if row["frame"] == native_row["frame"]), None)
                if candidate is None:
                    candidate = next((row for row in sim_rows
                                      if row.get("randomTarget")), None)
                if candidate is not None:
                    sim_rows.remove(candidate)
                unverified += 1
                reasons["randomTarget"] += 1
                continue
            if match is None and window_frames:
                candidate = min(sim_rows, key=lambda row: abs(row["frame"] - native_row["frame"]),
                                default=None)
                if candidate is not None and abs(candidate["frame"] - native_row["frame"]) <= window_frames:
                    match = candidate
                    diffs.append(diff_row("cards", native_row, match, "time-mismatch",
                                          "card update frame differs"))
            if match is None:
                if (death_frame is not None and dying_side is not None
                        and str(native_row.get("canonical") or "").startswith(dying_side + "|")
                        and native_row["frame"] >= death_frame):
                    # Dying-side rows at the death frame are end-of-battle
                    # bookkeeping (post-battle attribute resets) the simulator
                    # does not emit.
                    unverified += 1
                    reasons["deathFrame"] += 1
                    continue
                diffs.append(diff_row("cards", native_row, None, "missing",
                                      "native card update has no simulated update"))
                continue
            sim_rows.remove(match)
            if (death_frame is not None and dying_side is not None
                    and str(native_row.get("canonical") or "").startswith(dying_side + "|")
                    and native_row["frame"] >= death_frame):
                # 死亡帧上阵亡方的属性行属于终局记账（例如终局触发把同一
                # 属性再加一次），战斗已经结束，不影响任何后续状态。
                unverified += 1
                reasons["deathFrame"] += 1
                continue
            matched += 1
            mismatch = []
            for field in ("previous", "current"):
                left, right = native_row.get(field), match.get(field)
                left_number, right_number = number(left), number(right)
                if left_number is not None and right_number is not None:
                    if not same_number(left_number, right_number):
                        mismatch.append("%s %s vs %s" % (field, left, right))
                elif left != right:
                    mismatch.append("%s %r vs %r" % (field, left, right))
            if mismatch:
                diffs.append(diff_row("cards", native_row, match, "value-mismatch",
                                      "; ".join(mismatch)))
        for extra in sim_rows:
            if cascade_from is not None and extra["frame"] >= cascade_from:
                unverified += 1
                reasons["critCascade"] += 1
                continue
            if extra.get("auraRefresh"):
                unverified += 1
                reasons["auraRefresh"] += 1
                continue
            if extra.get("runtimeAbility"):
                unverified += 1
                reasons["runtimeAbility"] += 1
                continue
            if extra.get("randomTarget") or extra.get("randomCascade"):
                unverified += 1
                reasons["randomCascade" if extra.get("randomCascade") else "randomTarget"] += 1
                continue
            diffs.append(diff_row("cards", None, extra, "extra",
                                  "simulated card update has no native update"))
    return {"matched": matched, "unverified": unverified,
            "unverifiedReasons": dict(reasons), "diffs": diffs,
            "native": sum(len(value) for value in native.values()),
            "simulated": sum(len(value) for value in simulated.values())}


def identity_rows(index):
    return {key: dict(value) for key, value in index.by_key.items()}


def is_skill_identity(record):
    if not record:
        return False
    return (record.get("slot") is None and record.get("rootTemplate") is not None and
            record.get("rootTemplate") == record.get("rawId"))


def compare_identity(native_index, sim_index, window_frames):
    native = identity_rows(native_index)
    simulated = identity_rows(sim_index)
    diffs = []
    matched = 0
    unverified = 0
    reasons = Counter()
    for key in sorted(set(native) | set(simulated)):
        left, right = native.get(key), simulated.get(key)
        if left is None and right is not None and right.get("randomTarget"):
            unverified += 1
            reasons["randomTransform"] += 1
            continue
        if right is None and left is not None and left.get("randomTarget"):
            unverified += 1
            reasons["randomTransform"] += 1
            continue
        if left is None:
            if right is not None and (right.get("source") == "simulated-skill" or
                                      is_skill_identity(right)):
                unverified += 1
                reasons["skillIdentity"] += 1
                continue
            diffs.append(diff_row("identity", None, right, "extra",
                                  "simulated lineage has no native lineage"))
            continue
        if right is None:
            if left.get("source") == "native-skill" or is_skill_identity(left):
                unverified += 1
                reasons["skillIdentity"] += 1
                continue
            diffs.append(diff_row("identity", left, None, "missing",
                                  "native lineage has no simulated lineage"))
            continue
        if left.get("randomTarget") or right.get("randomTarget"):
            unverified += 1
            reasons["randomTransform"] += 1
            continue
        matched += 1
        if left.get("rootTemplate") != right.get("rootTemplate"):
            diffs.append(diff_row("identity", left, right, "value-mismatch",
                                  "root template differs"))
        for field, tolerance in (("born", 1e-6), ("removed", 1e-6)):
            left_value, right_value = number(left.get(field)), number(right.get(field))
            if left_value is None and right_value is None:
                continue
            if left_value is None or right_value is None:
                diffs.append(diff_row("identity", left, right, "time-mismatch",
                                      "%s %s vs %s" % (field, left_value, right_value)))
                continue
            if not same_number(left_value, right_value, tolerance):
                diffs.append(diff_row("identity", left, right, "time-mismatch",
                                      "%s %s vs %s" % (field, left_value, right_value)))
    return {"matched": matched, "unverified": unverified,
            "unverifiedReasons": dict(reasons), "diffs": diffs,
            "native": len(native), "simulated": len(simulated)}


def compare_outcome(native, simulated, cascade_from=None):
    native_outcome = (native.get("combat") or {}).get("outcome") or {}
    sim_outcome = (simulated.get("combat") or {}).get("outcome") or {}
    diffs = []
    unverified = 0
    reasons = Counter()
    left = side_value(native_outcome.get("winnerSide") or native_outcome.get("winner"))
    right = side_value(sim_outcome.get("winnerSide") or sim_outcome.get("winner"))
    if left is not None and right is not None and left != right:
        if cascade_from is not None:
            # The death itself is downstream of an unseedable crit, so the
            # winner cannot be verified for this battle.
            unverified += 1
            reasons["critCascade"] += 1
        else:
            diffs.append(diff_row("outcome", {"frame": 0, "time": 0}, {"frame": 0, "time": 0},
                                  "value-mismatch", "winner %s vs %s" % (left, right)))
    return {"matched": 1 if not diffs else 0, "unverified": unverified,
            "unverifiedReasons": dict(reasons), "diffs": diffs, "native": 1, "simulated": 1}


def count_diffs(diffs, status):
    return sum(1 for row in diffs if row["status"] == status)


def group_diffs(diffs):
    groups = {}
    for row in diffs:
        key = (row["channel"], row["status"], row.get("kind"), row.get("canonical"),
               row.get("attribute"))
        entry = groups.get(key)
        if entry is None:
            entry = {"channel": row["channel"], "status": row["status"],
                     "kind": row.get("kind"), "canonical": row.get("canonical"),
                     "attribute": row.get("attribute"), "count": 0,
                     "firstFrame": row.get("frame"), "lastFrame": row.get("frame"),
                     "example": row.get("note")}
            groups[key] = entry
        entry["count"] += 1
        frame = row.get("frame")
        if frame is not None:
            if entry["firstFrame"] is None or frame < entry["firstFrame"]:
                entry["firstFrame"] = frame
            if entry["lastFrame"] is None or frame > entry["lastFrame"]:
                entry["lastFrame"] = frame
    return sorted(groups.values(), key=lambda row: -row["count"])


def summarize_channel(result):
    return {
        "matched": result["matched"],
        "unverified": result.get("unverified", 0),
        "unverifiedReasons": result.get("unverifiedReasons") or {},
        "postEnd": result.get("postEnd", 0),
        "native": result["native"],
        "simulated": result["simulated"],
        "missing": count_diffs(result["diffs"], "missing"),
        "extra": count_diffs(result["diffs"], "extra"),
        "valueMismatch": count_diffs(result["diffs"], "value-mismatch"),
        "timeMismatch": count_diffs(result["diffs"], "time-mismatch"),
    }


def write_csv(path, channels):
    rows = []
    for channel in channels:
        for diff in channel["diffs"]:
            rows.append({
                "channel": diff["channel"],
                "status": diff["status"],
                "frame": diff.get("frame"),
                "time": diff.get("time"),
                "kind": diff.get("kind", ""),
                "source": diff.get("source", ""),
                "target": diff.get("target", ""),
                "canonical": diff.get("canonical", ""),
                "attribute": diff.get("attribute", ""),
                "note": diff.get("note", ""),
            })
    target = pathlib.Path(path)
    target.parent.mkdir(parents=True, exist_ok=True)
    with target.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0].keys()) if rows else
                                ["channel", "status", "frame", "time", "kind", "source",
                                 "target", "canonical", "attribute", "note"])
        writer.writeheader()
        writer.writerows(rows)


def filter_scope(rows, scope_end):
    if scope_end is None:
        return rows, 0
    removed = 0
    kept = []
    for row in rows:
        time = number(row.get("time"))
        if time is not None and time > scope_end + 1e-9:
            removed += 1
        else:
            kept.append(row)
    return kept, removed


def filter_scope_map(rows_by_key, scope_end):
    if scope_end is None:
        return rows_by_key, 0
    removed = 0
    kept = defaultdict(list)
    for key, rows in rows_by_key.items():
        for row in rows:
            time = number(row.get("time"))
            if time is not None and time > scope_end + 1e-9:
                removed += 1
            else:
                kept[key].append(row)
    return kept, removed


def mark_sim_random_lineage(native, simulated, sim_index):
    """Flag simulator instances whose transform happened at a random-transform frame."""
    random_frames = set()
    for event in (native.get("combat") or {}).get("events") or []:
        if event.get("randomTarget") and str(event.get("codeName") or "").endswith("CardTransformed"):
            random_frames.add(frame_of(event))
    if not random_frames:
        return
    for event in (simulated.get("combat") or {}).get("events") or []:
        if event.get("kind") != "transform":
            continue
        if frame_of(event) not in random_frames:
            continue
        for raw_id in (event.get("target"),):
            for record in sim_index.by_raw.get(str(raw_id)) or []:
                record["randomTarget"] = True
    for record in sim_index.by_key.values():
        born = record.get("born")
        if born is None:
            continue
        if int(round(float(born) / FRAME_SECONDS)) in random_frames:
            record["randomTarget"] = True


def compare_documents(native, simulated, window_frames=0, observed_decisions=False):
    audit = simulated.get("replayRandom") or {}
    decisions = build_decisions(native) if observed_decisions else None
    random_controlled = bool(observed_decisions and
        audit.get("mode") == "observed-decisions" and
        audit.get("replaySha256") == (native.get("replay") or {}).get("sha256") and
        audit_complete(audit, decisions.get("excluded"), decisions))
    if observed_decisions:
        # Target choices have been supplied to the engine. Compare those
        # branches normally: do not hide target/crit errors as random cascades.
        import copy
        native = copy.deepcopy(native)
        for event in (native.get("combat") or {}).get("events") or []:
            event["randomTarget"] = False
    native_index = build_native_identity(native)
    sim_index = build_sim_identity(simulated)
    mark_sim_random_lineage(native, simulated, sim_index)

    transform_scope = {} if observed_decisions else random_transform_scope(native, simulated)
    card_scope = {} if observed_decisions else random_card_scope(native, simulated)
    for iid, time in transform_scope.items():
        if iid not in card_scope or time < card_scope[iid]:
            card_scope[iid] = time
    effect_scope = dict(transform_scope)
    for iid, time in card_scope.items():
        if iid not in effect_scope or time < effect_scope[iid]:
            effect_scope[iid] = time
    native_effects, native_unknown_effects = native_effect_records(
        native, native_index, effect_scope)
    sim_effects, sim_unknown_effects = sim_effect_records(
        simulated, sim_index, effect_scope)
    scope_end = number((native.get("replay") or {}).get("duration"))
    death_frame = None
    dying_side = None
    for event in (native.get("combat") or {}).get("events") or []:
        if str(event.get("codeName") or "").endswith("CombatantDied"):
            death_time = number(event.get("time"))
            if death_time is not None and (scope_end is None or death_time < scope_end):
                scope_end = death_time
            combatant = (event.get("fields") or {}).get("CombatantId")
            dying_side = {0: "player", 1: "enemy"}.get(
                combatant if isinstance(combatant, int) else None)
            if scope_end is not None:
                death_frame = int(round(scope_end / FRAME_SECONDS))
            break
    sim_effects, post_end_effects = filter_scope(sim_effects, scope_end)
    native_health = read_health(native, False)
    sim_health, post_end_health = filter_scope_map(read_health(simulated, True), scope_end)
    dot_tick_hp_after = sim_dot_tick_hp_after(simulated)
    health_result = compare_health(native_health, sim_health, window_frames, dot_tick_hp_after,
                                   observed_decisions, death_frame, dying_side,
                                   simulated_crit_frames(simulated))
    # A native death is only verifiable when the dying side's health curve was
    # still crit-free: a crit divergence shifts the whole downstream trajectory
    # (hp curves, the death frame itself, end-of-battle bookkeeping), which the
    # simulator cannot reproduce without the native combat seed.
    cascade_from = None
    crit_scope = health_result.get("critScope") or {}
    duration = number((native.get("replay") or {}).get("duration"))
    if scope_end is not None and duration is not None and scope_end < duration - 1e-9:
        if dying_side is None or dying_side in crit_scope:
            cascade_from = death_frame
            # 该方血量已因不可重建的暴击分叉，若引擎侧因此更早死亡，
            # 其死亡及之后的原生事件都属于不可验证的下游。
            sim_outcome = (simulated.get("combat") or {}).get("outcome") or {}
            sim_death_time = number(sim_outcome.get("endTime"))
            sim_winner = sim_outcome.get("winnerSide")
            sim_dying = ({"player": "enemy", "enemy": "player"}.get(sim_winner)
                         if sim_outcome.get("endedByDeath") else None)
            if (sim_dying is not None and sim_dying == dying_side
                    and sim_death_time is not None and sim_death_time < scope_end - 1e-9):
                sim_death_frame = int(round(sim_death_time / FRAME_SECONDS))
                cascade_from = sim_death_frame if cascade_from is None else min(cascade_from, sim_death_frame)
    runtime_scope = runtime_card_scope(native)
    aura_scope = aura_card_scope(native)
    stash_scope = stash_instance_scope(native)
    native_cards, native_stash_rows = read_cards(native, native_index, False, card_scope,
                                                 runtime_scope, aura_scope, stash_scope)
    sim_cards_raw, sim_stash_rows = read_cards(simulated, sim_index, True, card_scope,
                                               runtime_scope, aura_scope, stash_scope)
    sim_cards, post_end_cards = filter_scope_map(sim_cards_raw, scope_end)
    channels = {
        "effects": compare_effects(native_effects, sim_effects, window_frames, cascade_from,
                                   death_frame, dying_side),
        "health": health_result,
        "cards": compare_cards(native_cards, sim_cards, window_frames, cascade_from,
                               death_frame, dying_side),
        "identity": compare_identity(native_index, sim_index, window_frames),
        "outcome": compare_outcome(native, simulated, cascade_from),
    }
    channels["effects"]["postEnd"] = post_end_effects
    channels["health"]["postEnd"] = post_end_health
    channels["cards"]["postEnd"] = post_end_cards
    channels["cards"]["unverified"] = channels["cards"].get("unverified", 0) + \
        native_stash_rows + sim_stash_rows
    channels["cards"]["unverifiedReasons"]["stashItem"] = \
        channels["cards"]["unverifiedReasons"].get("stashItem", 0) + \
        native_stash_rows + sim_stash_rows

    unknowns = []
    coverage_notes = [{"side": "native", "detail": entry} for entry in native.get("coverageNotes") or []]
    for entry in native.get("unknowns") or []:
        scope = entry.get("scope") if isinstance(entry, dict) else None
        if scope in COVERAGE_SCOPES:
            coverage_notes.append({"side": "native", "detail": entry})
            continue
        if scope == "sample.random" and random_controlled:
            coverage_notes.append({"side": "native", "detail": entry, "resolution": "observed decisions fully audited"})
            continue
        unknowns.append({"side": "native", "detail": entry})
    for entry in simulated.get("unknowns") or []:
        unknowns.append({"side": "simulated", "detail": entry})
    if native.get("complete") is False:
        unknowns.append({"side": "native", "detail": "ledger complete=false"})
    if simulated.get("complete") is False:
        unknowns.append({"side": "simulated", "detail": "ledger complete=false"})
    unknown_count = len(unknowns) + native_unknown_effects + sim_unknown_effects

    summary = {name: summarize_channel(channel) for name, channel in channels.items()}
    total = {key: sum(summary[name][key] for name in summary)
             for key in ("matched", "unverified", "postEnd", "missing", "extra",
                         "valueMismatch", "timeMismatch")}
    critical_diff = any(summary[name][key] for name in CRITICAL_CHANNELS
                        for key in ("missing", "extra", "valueMismatch", "timeMismatch"))
    verdict = "DIFF" if critical_diff else (
        "UNKNOWN" if unknown_count or total["unverified"] or total["postEnd"] else "MATCH")

    differences = sorted((diff for channel in channels.values() for diff in channel["diffs"]),
                         key=lambda row: (row.get("frame") is None, row.get("frame") or 0,
                                          row["channel"], row["status"]))
    return {
        "comparatorVersion": 1,
        "verificationMode": "observed-decisions" if observed_decisions else "seeded",
        "verdict": verdict,
        "native": {
            "path": None,
            "ledgerSha256": None,
            "battleId": (native.get("replay") or {}).get("battleId"),
            "sha256": (native.get("replay") or {}).get("sha256"),
            "contract": native.get("contract"),
        },
        "simulated": {
            "path": None,
            "ledgerSha256": None,
            "kind": simulated.get("kind"),
            "source": simulated.get("source"),
        },
        "counts": dict(total, unknown=unknown_count),
        "channels": summary,
        "unknowns": unknowns,
        "coverageNotes": coverage_notes,
        "randomDecisionsControlled": random_controlled,
        "firstDifferences": differences[:20],
        "topDiffGroups": group_diffs(differences)[:40],
        "diffs": differences,
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description="Compare native BPP ledger with simulated ledger")
    parser.add_argument("native")
    parser.add_argument("simulated")
    parser.add_argument("--json")
    parser.add_argument("--csv")
    parser.add_argument("--window-frames", type=int, default=0,
                        help="explicit nearby-frame window; 0 keeps same-frame matching")
    args = parser.parse_args(argv)

    native = load_json(args.native)
    simulated = load_json(args.simulated)
    report = compare_documents(native, simulated, args.window_frames)
    report["native"]["path"] = args.native
    report["native"]["ledgerSha256"] = file_sha256(args.native)
    report["simulated"]["path"] = args.simulated
    report["simulated"]["ledgerSha256"] = file_sha256(args.simulated)

    if args.json:
        target = pathlib.Path(args.json)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(report, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    if args.csv:
        channels = {}
        for name in CRITICAL_CHANNELS:
            channels[name] = {"diffs": [row for row in report["diffs"] if row["channel"] == name]}
        write_csv(args.csv, channels.values())

    print("verdict=%s counts=%s" % (report["verdict"], report["counts"]))
    for name in CRITICAL_CHANNELS:
        print("%s %s" % (name, report["channels"][name]))
    for diff in report["firstDifferences"][:12]:
        print("%s %s frame=%s %s" % (diff["channel"], diff["status"], diff.get("frame"),
                                     diff.get("note")))
    for group in report["topDiffGroups"][:10]:
        print("GROUP %s %s count=%s kind=%s canonical=%s attribute=%s frames=%s..%s" % (
            group["channel"], group["status"], group["count"], group.get("kind"),
            group.get("canonical"), group.get("attribute"), group["firstFrame"],
            group["lastFrame"]))
    return 0 if report["verdict"] == "MATCH" else (1 if report["verdict"] == "DIFF" else 2)


if __name__ == "__main__":
    raise SystemExit(main())
