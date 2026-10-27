"""Build a versioned, event-level ledger from a local BPP replay.

The ledger is deliberately stricter than the old health-only projection:

* combat/game event payloads are named from ``native_replay_contract_v1.json``
  (extracted from BazaarGameShared.dll), never from tuple length;
* spawn + BPP ``OpeningMessage`` sidecar reconstruct the opening board;
* every instance is keyed by ``side + instanceId + generation`` and transform
  events keep parent/child links and birth/removal points;
* frame events, health adjustments, cooldown/attribute changes and card state
  changes are exported separately so a per-event comparator can consume them.

Missing information is emitted as UNKNOWN.  The tool never substitutes the
end-of-battle board for the opening and never guesses an unsupported event code.

Usage:
    python tools/native_replay_ledger.py <replay.payload.mpack.gz> \
        [--json out.json] [--contract tools/native_replay_contract_v1.json] \
        [--snapshot-db bazaarplusplus.db] [--no-snapshot] \
        [--engine-fixture out_fixture.json]

Exit codes: 0 ledger complete, 2 replay version/event code not covered by the
contract or an engine fixture could not be built.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import pathlib
import sqlite3
import sys
from collections import Counter, OrderedDict, defaultdict

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "captures" / "diagnostics"))
from read_bpp_replay import read_replay  # noqa: E402

LEDGER_VERSION = 1
DEFAULT_CONTRACT = ROOT / "tools" / "native_replay_contract_v1.json"
DEFAULT_TEMPLATES = ROOT / "tools" / "actionaxis_plugin" / "build" / "native_mapping_templates.jsonl"
DEFAULT_CATALOG = ROOT / "src" / "data" / "cards.json"
SIDES = {0: "player", 1: "enemy"}
PLAYER_ATTRS_REQUIRED = ("Health", "HealthMax", "Shield", "HealthRegen", "RageMax", "EnragedDurationMax")
# CombatSimPlayerUpdate also carries non-vital player attributes (Gold,
# Custom_N).  Those are not health records; comparing them in the health
# channel produced thousands of false "missing" rows at frames 0/1/6.
HEALTH_ATTRIBUTES = {"Health", "HealthMax", "Shield", "Burn", "Poison", "HealthRegen",
                     "IsPlayerDead"}


def sha256_file(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def load_contract(path):
    contract = json.loads(pathlib.Path(path).read_text(encoding="utf-8"))
    if contract.get("contractVersion") != 1:
        raise SystemExit("不支持的契约版本: %r" % contract.get("contractVersion"))
    return contract


def ability_targets_from(container):
    """Return ({abilityId: targetType}, {auraId}) for an Abilities/Auras map."""
    abilities = {}
    auras = set()
    if isinstance(container, dict):
        for ability_id, ability in container.items():
            action = (ability or {}).get("Action") or {}
            target = action.get("Target") or {}
            target_type = target.get("$type") if isinstance(target, dict) else None
            if target_type:
                abilities[str(ability_id)] = target_type
    return abilities, auras


def ability_metadata_from(container):
    """Keep the native trigger/action types needed to identify RNG inputs."""
    answer = {}
    for ability_id, ability in (container or {}).items():
        action = (ability or {}).get("Action") or {}
        trigger = (ability or {}).get("Trigger") or {}
        spawn_context = action.get("SpawnContext") or {}
        filters = [item for group in spawn_context.get("Groups") or []
                   for item in group.get("Filters") or []]
        # A random selection method over a positional/self singleton is
        # deterministic (Mirror/3D Printer); the engine makes no donor roll.
        fixed_donor = bool(filters) and all(
            item.get("$type") == "TSpawnFilterTarget" and
            (item.get("Target") or {}).get("$type") in ("TTargetCardPositional", "TTargetCardSelf")
            for item in filters)
        answer[str(ability_id)] = {
            "triggerType": trigger.get("$type"),
            "actionType": action.get("$type"),
            "randomTransform": bool("Transform" in str(action.get("$type")) and
                                    spawn_context.get("SelectionMethod") == "Random" and not fixed_donor),
        }
    return answer


def ability_internal_ids_from(container):
    """Keep explicit native Ids without replacing canonical dictionary keys."""
    result = defaultdict(list)
    for key, ability in (container or {}).items():
        native_id = (ability or {}).get("Id")
        if isinstance(native_id, str):
            result[native_id].append(str(key))
    return dict(result)


def compatible_ability_action(action_type, command):
    if not action_type or not command:
        return False
    if action_type == "TAction" + command:
        return True
    if command in ("FlyingStart", "FlyingStop"):
        return action_type in ("TActionCard" + command, "TActionCardFlyingToggle")
    return command == "CardEnchant" and action_type == "TActionCardEnchantRandom"


def resolve_internal_ability(definition, enchantment, native_id, command):
    """Resolve a non-key Id only from one explicit, action-compatible definition.

    Caracara's Abilities/0 has Id="" and its native flight event reports that
    empty Id. Multiple aliases remain ambiguous even if their targets happen
    to agree; an event result must never be used to choose a definition.
    """
    candidates = []
    groups = [(definition.get("internalIds") or {}, definition.get("abilities") or {},
               definition.get("metadata") or {})]
    if enchantment:
        groups.append(((definition.get("enchantmentInternalIds") or {}).get(enchantment) or {},
                       (definition.get("enchantments") or {}).get(enchantment) or {},
                       (definition.get("enchantmentMetadata") or {}).get(enchantment) or {}))
    for aliases, targets, metadata in groups:
        for key in aliases.get(native_id) or []:
            candidates.append((key, targets.get(key), metadata.get(key) or {}))
    if len(candidates) != 1 or not compatible_ability_action(candidates[0][2].get("actionType"), command):
        return None
    return candidates[0]


def load_ability_targets(path):
    """Map template -> base ability targets, enchantment targets and aura ids.

    Random-target detection must cover enchantment abilities too: a Turbo
    enchantment can randomly haste any item, and the replay only carries the
    resulting target.  Aura ids identify native aura-refresh records, which
    the simulator models statically and therefore cannot emit per tick.
    """
    targets = {}
    path = pathlib.Path(path)
    if not path.exists():
        return targets
    with path.open(encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if not line:
                continue
            row = json.loads(line)
            template_id = row.get("id")
            template = row.get("template") or {}
            if not template_id or not isinstance(template, dict):
                continue
            abilities, _ = ability_targets_from(template.get("Abilities"))
            auras = set((template.get("Auras") or {}).keys())
            record = {"abilities": abilities, "auras": auras,
                      "metadata": ability_metadata_from(template.get("Abilities")),
                      "internalIds": ability_internal_ids_from(template.get("Abilities")),
                      "enchantmentMetadata": {},
                      "enchantmentInternalIds": {},
                      "enchantments": {}, "enchantmentAuras": {},
                      "known": set(abilities) | set(auras)}
            for name, definition in (template.get("Enchantments") or {}).items():
                definition = definition or {}
                record["enchantmentMetadata"][name] = ability_metadata_from(definition.get("Abilities"))
                record["enchantmentInternalIds"][name] = ability_internal_ids_from(definition.get("Abilities"))
                enchantment_abilities, _ = ability_targets_from(definition.get("Abilities"))
                if enchantment_abilities:
                    record["enchantments"][name] = enchantment_abilities
                enchantment_auras = set((definition.get("Auras") or {}).keys())
                if enchantment_auras:
                    record["enchantmentAuras"][name] = enchantment_auras
            if abilities or auras or record["enchantments"] or record["enchantmentAuras"]:
                targets[template_id] = record
    return targets


def enum_inverse(contract, name):
    return {value: key for key, value in contract["enums"].get(name, {}).items()}


def enum_name(inverse, value):
    if value is None:
        return None
    if isinstance(value, bool):
        return str(value)
    if isinstance(value, (list, tuple)):
        return [enum_name(inverse, item) for item in value]
    try:
        return inverse.get(int(value), str(value))
    except (TypeError, ValueError):
        return str(value)


def raw_json(value):
    """Convert decoded MessagePack containers into JSON-safe structures."""
    if isinstance(value, tuple):
        return [raw_json(item) for item in value]
    if isinstance(value, list):
        return [raw_json(item) for item in value]
    if isinstance(value, dict):
        return {str(key): raw_json(item) for key, item in value.items()}
    if isinstance(value, bytes):
        return {"$bytes": len(value)}
    return value


def instance_id(value):
    if value is None:
        return None
    if isinstance(value, (list, tuple)):
        if len(value) == 1:
            return instance_id(value[0])
        return "|".join(str(instance_id(item)) for item in value if item is not None)
    return str(value)


def instance_ref(value):
    """Decode Nullable<InstanceId>, which is nil or a one-item array."""
    if value is None:
        return None
    if isinstance(value, (list, tuple)):
        ids = [instance_id(item) for item in value if item is not None]
        if len(ids) == 1:
            return ids[0]
        return ids
    return str(value)


def flatten_instance_ids(value):
    answer = []
    if value is None:
        return answer
    if isinstance(value, str):
        return [value]
    if isinstance(value, (list, tuple)):
        for item in value:
            answer.extend(flatten_instance_ids(item))
        return answer
    answer.append(str(value))
    return answer


def decode_effect_target(value):
    if value is None:
        return None
    if not isinstance(value, (list, tuple)) or len(value) != 2:
        return {"type": "unknown", "raw": raw_json(value)}
    tag = int(value[0]) if isinstance(value[0], int) else value[0]
    body = value[1]
    if tag == 1:
        return {"type": "card", "ids": flatten_instance_ids(body)}
    if tag == 2:
        ids = []
        for item in body if isinstance(body, (list, tuple)) else [body]:
            try:
                ids.append(int(item))
            except (TypeError, ValueError):
                ids.append(item)
        return {"type": "player", "combatantIds": ids,
                "sides": [SIDES.get(item, "unknown") for item in ids]}
    return {"type": "unknown", "tag": tag, "raw": raw_json(value)}


def target_side(target):
    if not target:
        return None
    if target.get("type") == "player":
        sides = target.get("sides") or []
        return sides[0] if len(sides) == 1 else None
    if target.get("type") == "card":
        return None
    return None


def decode_time(frame, seconds):
    return round(frame * seconds, 6)


def field_values(entry, payload):
    """Bind a payload to contract field names; keep arity mismatches visible."""
    names = [field["name"] for field in entry.get("fields", [])]
    if payload is None:
        return {}, None
    if isinstance(payload, dict):
        return {name: payload.get(name) for name in names if name in payload}, None
    if not isinstance(payload, (list, tuple)):
        return {}, {"expected": len(names), "actual": type(payload).__name__}
    values = {}
    for index, name in enumerate(names):
        values[name] = payload[index] if index < len(payload) else None
    warning = None
    if len(payload) != len(names):
        warning = {"expected": len(names), "actual": len(payload)}
    return values, warning


class LedgerBuilder:
    def __init__(self, replay_path, contract_path, snapshot_db, use_snapshot=True, ability_targets=None):
        self.replay_path = pathlib.Path(replay_path)
        self.contract_path = pathlib.Path(contract_path)
        self.contract = load_contract(self.contract_path)
        self.snapshot_db = pathlib.Path(snapshot_db) if snapshot_db else None
        self.use_snapshot = use_snapshot
        self.ability_targets = ability_targets if ability_targets is not None else {}
        self.unknowns = []
        self.coverage_notes = []
        self.warnings = []
        self.complete = True
        self.instances = OrderedDict()
        self.skills = {"player": [], "enemy": []}
        self.runtime_skills = {}
        self.opening_instances = {"player": {}, "enemy": {}}
        self.opening_unknown_side = []
        self.side_by_instance = {}
        self.template_by_instance = {}
        self.skill_templates = {}
        self.spawn_events = []
        self.despawn_events = []
        self.lineage = []
        self.frame_events = []
        self.health_changes = []
        self.card_updates = []
        self.actions = Counter()
        self.events_by_code = Counter()
        self.vitals = {"player": {}, "enemy": {}}
        self.open_attrs = {"player": {}, "enemy": {}}
        self.attr_sources = {"player": {}, "enemy": {}}
        self.heroes = {"player": None, "enemy": None}
        self.hero_sources = {}
        self.snapshot = None
        self.snapshot_info = {}
        self.battles_info = {}
        self.replay_doc = None
        self.run = {}
        self.combat = None
        self.despawn_sim = None
        self.frame_count = 0
        self.frame_seconds = float(self.contract.get("frameSeconds", 0.05))
        self.duration = 0.0
        self.combat_outcome = {}

    # ---------- contract helpers ----------

    def combat_enum(self, name):
        return enum_inverse(self.contract, name)

    def game_enum(self, name):
        return enum_inverse(self.contract, name)

    def unknown(self, scope, detail, source=None, impact=None):
        entry = {"scope": scope, "detail": detail}
        if source:
            entry["source"] = source
        if impact:
            entry["impact"] = impact
        if entry not in self.unknowns:
            self.unknowns.append(entry)

    def warning(self, scope, detail, source=None):
        entry = {"scope": scope, "detail": detail}
        if source:
            entry["source"] = source
        if entry not in self.warnings:
            self.warnings.append(entry)

    def coverage(self, scope, detail, source=None):
        entry = {"scope": scope, "detail": detail}
        if source:
            entry["source"] = source
        if entry not in self.coverage_notes:
            self.coverage_notes.append(entry)

    # ---------- envelope and sidecar ----------

    def load(self):
        self.replay_sha256 = sha256_file(self.replay_path)
        self.replay_doc = read_replay(str(self.replay_path))
        version = self.replay_doc.get("Version")
        if version not in self.contract.get("payloadVersions", []):
            self.complete = False
            self.unknown("replay.version", "回放 Version=%r 不在契约 payloadVersions=%r 内" %
                         (version, self.contract.get("payloadVersions")), str(self.replay_path))
        for key in ("SpawnMessageBytes", "CombatMessageBytes", "DespawnMessageBytes"):
            if key not in self.replay_doc:
                self.complete = False
                self.unknown("replay.envelope", "缺少 %s" % key, str(self.replay_path))
        if not self.use_snapshot:
            self.unknown("opening.sidecar", "已按 --no-snapshot 关闭 BPP OpeningMessage 侧车；玩家卡模板/技能将保持 UNKNOWN")
            return
        self.load_snapshot()

    def discover_snapshot_db(self):
        if self.snapshot_db is None:
            candidate = self.replay_path.parent.parent / "bazaarplusplus.db"
            if candidate.exists():
                self.snapshot_db = candidate
        return self.snapshot_db is not None and self.snapshot_db.exists()

    def load_snapshot(self):
        if not self.discover_snapshot_db():
            self.unknown("opening.sidecar", "未找到 bazaarplusplus.db；玩家卡模板 ID/技能无法从回放载荷恢复",
                         str(self.snapshot_db) if self.snapshot_db else None)
            return
        battle_id = self.replay_doc.get("BattleId")
        try:
            connection = sqlite3.connect("file:%s?mode=ro" % self.snapshot_db.as_posix(), uri=True)
            connection.row_factory = sqlite3.Row
            row = connection.execute(
                "select player_hand_json, player_skills_json, opponent_hand_json, opponent_skills_json "
                "from battle_snapshots where battle_id=?", (battle_id,)).fetchone()
            battle = connection.execute("select * from battles where battle_id=?", (battle_id,)).fetchone()
            connection.close()
        except Exception as error:  # sqlite schema/version is external input
            self.warning("opening.sidecar", "读取 BPP 快照失败: %s" % error, str(self.snapshot_db))
            return
        if row is None:
            self.unknown("opening.sidecar", "battle_snapshots 没有 battle_id=%s 的记录" % battle_id,
                         str(self.snapshot_db))
            return
        try:
            self.snapshot = {
                "player_hand": json.loads(row["player_hand_json"]),
                "player_skills": json.loads(row["player_skills_json"]),
                "opponent_hand": json.loads(row["opponent_hand_json"]),
                "opponent_skills": json.loads(row["opponent_skills_json"]),
            }
        except (TypeError, ValueError) as error:
            self.warning("opening.sidecar", "BPP 快照 JSON 解析失败: %s" % error, str(self.snapshot_db))
            return
        self.snapshot_info = {
            "kind": "bpp_battle_snapshots",
            "path": str(self.snapshot_db),
            "battleId": battle_id,
            "provenance": "external sidecar captured from OpeningMessage, not the replay payload",
            "playerHand": self.blob_info(self.snapshot["player_hand"]),
            "playerSkills": self.blob_info(self.snapshot["player_skills"]),
            "opponentHand": self.blob_info(self.snapshot["opponent_hand"]),
            "opponentSkills": self.blob_info(self.snapshot["opponent_skills"]),
        }
        if battle is not None:
            self.battles_info = {
                "source": "bpp.battles",
                "playerHero": battle["player_hero"],
                "opponentHero": battle["opponent_hero"],
                "combatKind": battle["combat_kind"],
                "recordedAtUtc": battle["recorded_at_utc"],
                "result": battle["result"],
            }
            if battle["player_hero"]:
                self.heroes["player"] = battle["player_hero"]
                self.hero_sources["player"] = "bpp.battles.player_hero"
            if battle["opponent_hero"]:
                self.heroes["enemy"] = battle["opponent_hero"]
                self.hero_sources["enemy"] = "bpp.battles.opponent_hero"

    @staticmethod
    def blob_info(blob):
        if not isinstance(blob, dict):
            return {"status": None, "source": None, "items": 0}
        return {"status": blob.get("status"), "source": blob.get("source"),
                "items": len(blob.get("items") or [])}

    # ---------- opening reconstruction ----------

    def sim_parts(self, message, label):
        if not isinstance(message, (list, tuple)) or not message:
            self.complete = False
            self.unknown(label, "消息为空或不是序列")
            return None
        sim = message[0]
        if not isinstance(sim, list) or len(sim) < 7:
            self.complete = False
            self.unknown(label, "GameSim/CombatSim 顶层不是 7/8 字段序列")
            return None
        return sim

    def parse_player_attributes(self, raw, side, source, record_source=True, keep_existing_source=False):
        inverse = enum_inverse(self.contract, "EPlayerAttributeType")
        result = {}
        if not isinstance(raw, dict):
            return result
        for key, value in raw.items():
            name = enum_name(inverse, key)
            try:
                result[name] = int(value)
            except (TypeError, ValueError):
                self.warning("opening.attributes", "非整数玩家属性 %s=%r" % (name, value), source)
                continue
            if record_source and (not keep_existing_source or name not in self.attr_sources[side]):
                self.attr_sources[side][name] = source
        return result

    def parse_card_attributes(self, raw, source, side, instance):
        inverse = enum_inverse(self.contract, "ECardAttributeType")
        result = {}
        if not isinstance(raw, dict):
            return result
        for key, value in raw.items():
            name = enum_name(inverse, key)
            if isinstance(value, (list, tuple)) and len(value) >= 2:
                delta_type = value[0]
                actual = value[1]
                if delta_type == 1:  # EAttributeDeltaType.Delete
                    result[name] = None
                    continue
            else:
                actual = value
            try:
                result[name] = int(actual)
            except (TypeError, ValueError):
                self.warning("opening.cardAttribute", "非整数卡牌属性 %s=%r" % (name, actual), source)
        return result

    def ensure_instance(self, iid, side, source):
        iid = instance_id(iid)
        if not iid:
            return None
        if side not in ("player", "enemy"):
            known = self.side_by_instance.get(iid)
            side = known or "unknown"
        key = "%s:%s" % (side, iid)
        if key not in self.instances:
            self.instances[key] = {
                "key": key,
                "side": side,
                "instanceId": iid,
                "templateId": None,
                "templateSource": None,
                "name": None,
                "type": None,
                "tier": None,
                "enchantment": None,
                "size": None,
                "section": None,
                "socket": None,
                "tags": [],
                "hiddenTags": [],
                "heroes": [],
                "state": None,
                "attributes": {},
                "attributeSources": {},
                "sources": [source],
                "born": None,
                "active": True,
                "generation": 0,
                "generations": [],
                "parent": None,
                "children": [],
            }
            self.instances[key]["generations"].append({
                "generation": 0,
                "templateId": None,
                "born": None,
                "removed": None,
                "reason": None,
            })
            self.side_by_instance[iid] = side
        elif source not in self.instances[key]["sources"]:
            self.instances[key]["sources"].append(source)
        return self.instances[key]

    def instance(self, iid):
        iid = instance_id(iid)
        if not iid:
            return None
        side = self.side_by_instance.get(iid)
        if side:
            return self.instances.get("%s:%s" % (side, iid))
        for item in self.instances.values():
            if item["instanceId"] == iid:
                return item
        return None

    def combat_instance(self, iid, source):
        """Skill updates share the card DTO, but skills are not board items.

        Keep their live state separate from the immutable opening skills.
        Otherwise an update creates an item with no template and reports a
        false missing opening template, while mutating the opening corrupts
        the fixture's initial counters.
        """
        iid = instance_id(iid)
        item = self.instance(iid)
        if item is not None:
            return item
        for side in ("player", "enemy"):
            for skill in self.skills[side]:
                if skill.get("instanceId") != iid:
                    continue
                if iid not in self.runtime_skills:
                    self.runtime_skills[iid] = dict(skill, side=side, sources=[source],
                        attributes=dict(skill.get("attributes") or {}), attributeSources={})
                return self.runtime_skills[iid]
        return self.ensure_instance(iid, self.side_by_instance.get(iid, "unknown"), source)

    def template_for_instance(self, iid):
        item = self.instance(iid)
        if item is not None and item.get("templateId"):
            return item["templateId"]
        iid = instance_id(iid)
        if iid in self.skill_templates:
            return self.skill_templates[iid]
        for side in ("player", "enemy"):
            for skill in self.skills.get(side) or []:
                if skill.get("instanceId") == iid:
                    return skill.get("templateId")
        return None

    def enchantment_for_instance(self, iid):
        item = self.instance(iid)
        return item.get("enchantment") if item is not None else None

    def touch_instance(self, item, source):
        if item is not None and source not in item["sources"]:
            item["sources"].append(source)

    def add_skill(self, side, record):
        """Merge a skill record by instance id.

        The snapshot sidecar and the spawn event both describe the same
        equipped skill.  Appending both applied every combat-start skill twice
        (for example Lash Out poisoned twice), so keep one record and fill only
        fields the snapshot did not provide.
        """
        instance = record.get("instanceId")
        if not instance:
            return None
        for existing in self.skills[side]:
            if existing.get("instanceId") != instance:
                continue
            for name, value in record.items():
                if name in ("attributes", "source"):
                    continue
                if existing.get(name) is None and value is not None:
                    existing[name] = value
            for name, value in (record.get("attributes") or {}).items():
                existing.setdefault("attributes", {}).setdefault(name, value)
            if existing.get("templateId") and not existing.get("templateSource"):
                existing["templateSource"] = existing.get("source")
            return existing
        self.skills[side].append(record)
        self.side_by_instance[instance] = side
        return record

    def merge_snapshot(self):
        if not self.snapshot:
            return
        for side, key in (("player", "player_hand"), ("enemy", "opponent_hand")):
            for raw in self.snapshot[key].get("items") or []:
                iid = raw.get("instance_id")
                if not iid:
                    self.complete = False
                    self.unknown("opening.snapshot", "%s 卡牌缺少 instance_id" % side, "bpp.battle_snapshots")
                    continue
                item = self.ensure_instance(iid, side, "bpp.battle_snapshots.%s" % key)
                if item["born"] is None:
                    item["born"] = {"phase": "opening", "time": 0.0,
                                    "source": "bpp.battle_snapshots"}
                item["templateId"] = raw.get("template_id")
                item["templateSource"] = "bpp.battle_snapshots"
                item["name"] = raw.get("name")
                item["type"] = raw.get("type")
                item["tier"] = raw.get("tier")
                enchant = raw.get("enchant")
                item["enchantment"] = enchant if enchant not in (None, "", "None") else None
                item["size"] = raw.get("size")
                section = raw.get("section")
                if section in ("Hand", "Stash"):
                    item["section"] = section
                item["socket"] = self.parse_socket(raw.get("socket"))
                item["tags"] = list(raw.get("tags") or [])
                for name, value in (raw.get("attributes") or {}).items():
                    try:
                        item["attributes"][name] = int(value)
                    except (TypeError, ValueError):
                        self.warning("opening.cardAttribute", "快照属性 %s=%r 不是整数" % (name, value),
                                     "bpp.battle_snapshots")
                        continue
                    item["attributeSources"][name] = "bpp.battle_snapshots"
                self.touch_instance(item, "bpp.battle_snapshots")
                if side == "player" and not item["templateId"]:
                    self.unknown("opening.playerTemplate", "玩家实例 %s 在快照中也没有 template_id" % iid,
                                 "bpp.battle_snapshots")
        for side, key in (("player", "player_skills"), ("enemy", "opponent_skills")):
            for raw in self.snapshot[key].get("items") or []:
                self.add_skill(side, {
                    "instanceId": raw.get("instance_id"),
                    "templateId": raw.get("template_id"),
                    "name": raw.get("name"),
                    "tier": raw.get("tier"),
                    "enchantment": raw.get("enchant") or None,
                    "attributes": {name: int(value) for name, value in (raw.get("attributes") or {}).items()
                                   if isinstance(value, (int, float))},
                    "source": "bpp.battle_snapshots.%s" % key,
                })
            blob = self.snapshot[key]
            if blob.get("status") not in (None, "Captured", "CapturedEmpty"):
                self.unknown("opening.skills", "%s 状态=%s，不能当作完整开场技能" %
                             (side, blob.get("status")), "bpp.battle_snapshots")

    @staticmethod
    def parse_socket(value):
        if value is None:
            return None
        if isinstance(value, int):
            return value
        text = str(value)
        if text.startswith("Socket_"):
            try:
                return int(text[len("Socket_"):])
            except ValueError:
                return text
        return text

    def spawn_card(self, raw, iid, source):
        fields, warning = field_values(self.contract["dtos"]["SimUpdateCard"], raw)
        if warning:
            self.warning("opening.card", "SimUpdateCard 字段数与契约不一致 %r" % warning, source)
        placement = fields.get("Placement")
        owner = section = socket = None
        if isinstance(placement, (list, tuple)) and len(placement) >= 3:
            owner, socket, section = placement[0], placement[1], placement[2]
        side = SIDES.get(owner)
        item = self.ensure_instance(iid, side, source)
        if item is None:
            return
        if side:
            self.side_by_instance[item["instanceId"]] = side
        if fields.get("Enchantment") is not None:
            item["enchantment"] = enum_name(enum_inverse(self.contract, "EEnchantmentType"), fields["Enchantment"])
        if fields.get("Heroes") is not None:
            item["heroes"] = enum_name(enum_inverse(self.contract, "EHero"), list(fields["Heroes"] or []))
        if fields.get("HiddenTags") is not None:
            item["hiddenTags"] = list(fields["HiddenTags"] or [])
        if fields.get("Tags") is not None:
            item["tags"] = list(fields["Tags"] or [])
        if fields.get("Tier") is not None:
            item["tier"] = enum_name(enum_inverse(self.contract, "ETier"), fields["Tier"])
        if fields.get("Size") is not None:
            item["size"] = enum_name(enum_inverse(self.contract, "ECardSize"), fields["Size"])
        if fields.get("State") is not None:
            item["state"] = enum_name(enum_inverse(self.contract, "ECardState"), fields["State"])
        if section is not None:
            item["section"] = enum_name(enum_inverse(self.contract, "EInventorySection"), section)
        if socket is not None:
            item["socket"] = socket
        attrs = self.parse_card_attributes(fields.get("Attributes"), source, side, iid)
        for name, value in attrs.items():
            item["attributes"][name] = value
            item["attributeSources"][name] = source

    def merge_spawn(self, sim):
        _, player, opponent, cards, run, _, _ = sim[:7]
        self.run = dict(zip([field["name"] for field in self.contract["dtos"]["SimUpdateRun"]["fields"]],
                            run if isinstance(run, (list, tuple)) else []))
        self.open_attrs["player"].update(self.parse_player_attributes(
            player[1] if isinstance(player, list) and len(player) > 1 else None,
            "player", "replay.spawn.player", keep_existing_source=True))
        self.open_attrs["enemy"].update(self.parse_player_attributes(
            opponent[1] if isinstance(opponent, list) and len(opponent) > 1 else None,
            "enemy", "replay.spawn.opponent", keep_existing_source=True))
        self.vitals["player"].update(self.open_attrs["player"])
        self.vitals["enemy"].update(self.open_attrs["enemy"])

        self.spawn_events = sim[0] if isinstance(sim[0], list) else []
        spawn_by_instance = {}
        for raw in self.spawn_events:
            if not isinstance(raw, list) or not raw:
                continue
            code = raw[0]
            payload = raw[1] if len(raw) > 1 else None
            entry = self.contract["gameEvents"].get(str(code))
            if entry is None:
                self.complete = False
                self.unknown("event.game.code", "开场事件码 %r 不在契约中" % code, "SpawnMessage")
                continue
            fields, _ = field_values(entry, payload)
            if code == 8:
                iid = instance_id(fields.get("InstanceId"))
                side = SIDES.get(fields.get("CombatantId"))
                if not side:
                    side = "unknown"
                    self.unknown("opening.spawn", "Spawned 事件缺少 CombatantId: %s" % iid, "SpawnMessage")
                card_type = enum_name(enum_inverse(self.contract, "ECardType"), fields.get("Type"))
                if card_type == "Skill":
                    # Skills ride the CardSpawned channel with a template GUID but
                    # no socket; the PlayerSkillEquipped event only names the owner.
                    self.skill_templates[iid] = fields.get("TemplateId")
                    if side != "unknown":
                        self.side_by_instance[iid] = side
                    continue
                item = self.ensure_instance(iid, side, "replay.spawn.event")
                item["templateId"] = fields.get("TemplateId")
                item["templateSource"] = "replay.spawn.event"
                item["type"] = card_type
                item["section"] = enum_name(enum_inverse(self.contract, "EInventorySection"), fields.get("Section"))
                item["socket"] = fields.get("Socket")
                item["born"] = {"phase": "opening", "time": 0.0, "source": "GameSimEventCardSpawned"}
                self.template_by_instance[iid] = fields.get("TemplateId")
                spawn_by_instance[iid] = item
                self.side_by_instance[iid] = side
            elif code == 22:
                side = SIDES.get(fields.get("CombatantId"))
                if side:
                    hero = enum_name(enum_inverse(self.contract, "EHero"), fields.get("Hero"))
                    self.heroes[side] = hero
                    self.hero_sources[side] = "replay.spawn.GameSimEventPlayerInitialized"
            elif code == 21:
                iid = instance_id(fields.get("InstanceId"))
                side = SIDES.get(fields.get("Owner"))
                if side:
                    self.add_skill(side, {
                        "instanceId": iid,
                        "templateId": None,
                        "name": None,
                        "tier": None,
                        "attributes": {},
                        "source": "replay.spawn.GameSimEventPlayerSkillEquipped",
                    })

        skill_cards = {}
        known_skills = set()
        for side in ("player", "enemy"):
            for skill in self.skills[side]:
                if skill.get("instanceId"):
                    known_skills.add(skill["instanceId"])
        for iid, raw in (cards or {}).items():
            if instance_id(iid) in self.skill_templates or instance_id(iid) in known_skills:
                # Skills are ledgered via self.skills, not as board instances.
                skill_cards[instance_id(iid)] = raw
                continue
            self.spawn_card(raw, iid, "replay.spawn.cards")
            item = self.instance(iid)
            if item and not item.get("templateId"):
                spawned = spawn_by_instance.get(instance_id(iid))
                if spawned and spawned.get("templateId"):
                    item["templateId"] = spawned["templateId"]
                    item["templateSource"] = spawned["templateSource"]

        # Skill templates arrive on the CardSpawned channel, which the game emits
        # after PlayerSkillEquipped; join them once both channels are read.
        for side in ("player", "enemy"):
            for skill in self.skills[side]:
                if skill.get("templateId"):
                    continue
                template = self.skill_templates.get(skill["instanceId"])
                if not template:
                    self.unknown("opening.skillTemplate",
                                 "技能 %s 只有实例 ID，模板需 OpeningMessage 侧车" % skill["instanceId"],
                                 "SpawnMessage")
                    continue
                skill["templateId"] = template
                skill["templateSource"] = "replay.spawn.event"
                raw = skill_cards.get(skill["instanceId"])
                if isinstance(raw, (list, tuple)):
                    fields, _ = field_values(self.contract["dtos"]["SimUpdateCard"], raw)
                    skill["tier"] = enum_name(enum_inverse(self.contract, "ETier"), fields.get("Tier"))
                    skill["enchantment"] = enum_name(
                        enum_inverse(self.contract, "EEnchantmentType"), fields.get("Enchantment"))
                    skill["attributes"] = self.parse_card_attributes(
                        fields.get("Attributes"), "replay.spawn.skills", side, skill["instanceId"])

        for item in self.instances.values():
            if item["born"] is None:
                item["born"] = {"phase": "opening", "time": 0.0, "source": "opening"}
            if item["generations"]:
                generation = item["generations"][0]
                generation["templateId"] = item["templateId"]
                generation["born"] = dict(item["born"])

    def capture_opening(self, only_missing=False):
        """Freeze opening values before any combat frame mutates them."""
        for item in self.instances.values():
            if not self.is_opening_instance(item):
                continue
            side = item["side"]
            if only_missing and item["instanceId"] in self.opening_instances.get(side, {}):
                continue
            record = {
                "instanceId": item["instanceId"],
                "key": item["key"],
                "templateId": item.get("templateId"),
                "templateSource": item.get("templateSource"),
                "name": item.get("name"),
                "tier": item.get("tier"),
                "enchantment": item.get("enchantment"),
                "size": item.get("size"),
                "section": item.get("section"),
                "inCombat": item.get("section") != "Stash",
                "socket": item.get("socket"),
                "attributes": dict(item.get("attributes") or {}),
                "attributeSources": dict(item.get("attributeSources") or {}),
                "tags": list(item.get("tags") or []),
                "hiddenTags": list(item.get("hiddenTags") or []),
                "heroes": list(item.get("heroes") or []),
                "state": item.get("state"),
                "born": dict(item.get("born") or {}),
            }
            if side not in self.opening_instances:
                record["side"] = side
                self.opening_unknown_side.append(record)
                self.unknown("opening.unknownSide",
                             "开场实例 %s 的阵营=%r，已保留在 opening.unknownSide，未计入 player/enemy" %
                             (item["instanceId"], side),
                             (item.get("sources") or ["opening"])[0])
                continue
            self.opening_instances[side][item["instanceId"]] = record

    def fill_opening_from_frame0(self, card_updates):
        """Use frame-0 PreviousValue only for fields the sidecar/spawn omitted."""
        if not isinstance(card_updates, dict):
            return
        for key, raw in card_updates.items():
            iid = instance_id(key)
            side = self.side_by_instance.get(iid)
            record = self.opening_instances.get(side, {}).get(iid)
            if record is None:
                continue
            fields, _ = field_values(self.contract["dtos"]["CombatSimCardUpdate"], raw)
            for name, previous, _current in self.decode_card_attribute_updates(fields.get("Attributes")):
                if record["attributes"].get(name) is not None:
                    continue
                record["attributes"][name] = previous
                record["attributeSources"][name] = "replay.frame0.previous"

    def merge_frame0(self):
        if not self.combat:
            return
        frames = self.combat[0] if isinstance(self.combat, list) and self.combat else []
        if not frames or not isinstance(frames[0], list) or len(frames[0]) < 4:
            return
        card_updates = frames[0][3]
        if not isinstance(card_updates, dict):
            return
        self.fill_opening_from_frame0(card_updates)
        for key, raw in card_updates.items():
            iid = instance_id(key)
            item = self.combat_instance(iid, "replay.frame0")
            fields, warning = field_values(self.contract["dtos"]["CombatSimCardUpdate"], raw)
            if warning:
                self.warning("combat.cardUpdate", "frame0 CardUpdate 字段数不一致 %r" % warning)
            if item.get("templateId") is None:
                self.unknown("opening.cardTemplate", "实例 %s 在回放与开场快照中都没有模板 ID" % iid,
                             "replay+sidecar")
            for name, previous, current in self.decode_card_attribute_updates(fields.get("Attributes")):
                item["attributes"][name] = current
                item["attributeSources"][name] = "replay.frame0"
                self.record_cooldown(iid, item, name, previous, current, 0, 0.0)
                # Frame 0 is recorded once by the main frame loop. Only use
                # this pass to seed opening attributes; do not append a second
                # cardUpdate here.
            self.touch_instance(item, "replay.frame0")

    def recover_opening_player_attributes(self):
        """A field's first transition reports its unchanged opening value.

        Combat updates are deltas. Before the first update of a field its
        PreviousValue is the opening value, even when the first change is
        later than frame zero. Never fill an unobserved field from defaults,
        a closing snapshot, or the opponent's attributes.
        """
        frames = self.combat[0] if isinstance(self.combat, list) and self.combat else []
        inverse = enum_inverse(self.contract, "EPlayerAttributeType")
        seen = {side: set() for side in ("player", "enemy")}
        regen_packets = {side: [] for side in ("player", "enemy")}
        for frame, raw_frame in enumerate(frames):
            if not isinstance(raw_frame, (list, tuple)) or len(raw_frame) < 3:
                continue
            for index, side in ((1, "player"), (2, "enemy")):
                fields, _ = field_values(self.contract["dtos"]["CombatSimPlayerUpdate"], raw_frame[index])
                for raw_adjustment in fields.get("HealthAdjustments") or []:
                    adjustment, _ = field_values(self.contract["dtos"]["CombatSimPlayerHealthAdjustment"], raw_adjustment)
                    if (enum_name(enum_inverse(self.contract, "EDamageType"), adjustment.get("DamageType")) == "Regen"
                            and enum_name(enum_inverse(self.contract, "EPlayerHealthChangeType"), adjustment.get("AttributeChanged")) == "Health"
                            and isinstance(adjustment.get("Amount"), (int, float))
                            and not adjustment.get("IsCrit") and not adjustment.get("IsDamageReduced")):
                        regen_packets[side].append((frame, adjustment["Amount"]))
                for key, raw in (fields.get("Attributes") or {}).items():
                    name = enum_name(inverse, key)
                    if name in seen[side]:
                        continue
                    seen[side].add(name)
                    update, _ = field_values(self.contract["dtos"]["CombatSimPlayerAttributeUpdate"], raw)
                    previous = update.get("PreviousValue")
                    if name in self.open_attrs[side] or not isinstance(previous, (int, float)):
                        continue
                    self.open_attrs[side][name] = previous
                    self.attr_sources[side][name] = "replay.frame%d.firstPrevious" % frame
        for side in ("player", "enemy"):
            packets = regen_packets[side]
            amounts = {amount for _, amount in packets}
            # Regen packets report the nominal pool even when HP is capped
            # (baca5f5b frames 240/260). If the attribute never transitions,
            # repeated identical packets recover its constant opening value.
            # A single/variable packet, or any explicit attribute transition,
            # is insufficient and must retain UNKNOWN instead of guessing.
            if ("HealthRegen" not in self.open_attrs[side] and "HealthRegen" not in seen[side]
                    and len(packets) >= 2 and len(amounts) == 1 and next(iter(amounts)) > 0):
                self.open_attrs[side]["HealthRegen"] = packets[0][1]
                self.attr_sources[side]["HealthRegen"] = "replay.frame%d.stableRegenAdjustment" % packets[0][0]
            self.vitals[side].update(self.open_attrs[side])

    # ---------- events ----------

    def decode_card_attribute_updates(self, raw):
        inverse = enum_inverse(self.contract, "ECardAttributeType")
        answer = []
        if not isinstance(raw, dict):
            return answer
        for key, value in raw.items():
            name = enum_name(inverse, key)
            if isinstance(value, (list, tuple)) and len(value) >= 3:
                previous, current = value[1], value[2]
            elif isinstance(value, (list, tuple)) and len(value) == 2:
                previous, current = value[0], value[1]
            else:
                continue
            answer.append((name, previous, current))
        return answer

    def normalize_event(self, raw, phase, frame=None, time=None, sequence=0):
        if not isinstance(raw, list) or not raw:
            self.complete = False
            self.unknown("event", "事件不是 [code, payload] 序列: %r" % raw, phase)
            return None
        code = raw[0]
        payload = raw[1] if len(raw) > 1 else None
        table = self.contract["combatEvents"] if phase == "combat" else self.contract["gameEvents"]
        entry = table.get(str(code))
        if entry is None:
            self.complete = False
            self.unknown("event.code", "%s 事件码 %r 不在契约中" % (phase, code), "contract")
            return {"code": code, "phase": phase, "frame": frame, "time": time,
                    "sequence": sequence, "unknown": True, "raw": raw_json(payload)}
        fields, arity = field_values(entry, payload)
        event = {
            "code": code,
            "codeName": entry["type"].split(".")[-1],
            "phase": phase,
            "frame": frame,
            "time": time,
            "sequence": sequence,
            "fields": {name: raw_json(value) for name, value in fields.items()},
        }
        if arity:
            event["payloadArity"] = arity
            self.complete = False
            self.unknown("event.payload", "%s 事件码 %s 字段数 %r 与契约 %r 不一致" %
                         (phase, code, arity.get("actual"), arity.get("expected")), entry["type"])
        if phase == "combat":
            self.events_by_code[str(code)] += 1
        if entry["type"].endswith("EffectExecuted"):
            source = instance_ref(fields.get("Source"))
            trigger = instance_ref(fields.get("TriggerSource"))
            target = decode_effect_target(fields.get("Target"))
            action = enum_name(enum_inverse(self.contract, "EActionCommandType"), fields.get("ActionType"))
            event["actionCode"] = fields.get("ActionType")
            event["action"] = action
            event["source"] = source
            event["sourceSide"] = self.side_for(source)
            event["triggerSource"] = trigger
            event["target"] = target
            template_id = self.template_for_instance(source)
            enchantment = self.enchantment_for_instance(source)
            effect_id = str(fields.get("EffectId"))
            definition = self.ability_targets.get(template_id) or {}
            metadata = dict((definition.get("metadata") or {}).get(effect_id) or {})
            target_type = (definition.get("abilities") or {}).get(effect_id)
            aura_refresh = effect_id in (definition.get("auras") or set())
            if enchantment:
                metadata.update(((definition.get("enchantmentMetadata") or {}).get(enchantment) or {}).get(effect_id) or {})
                enchantment_abilities = (definition.get("enchantments") or {}).get(enchantment) or {}
                target_type = enchantment_abilities.get(effect_id, target_type)
                if effect_id in ((definition.get("enchantmentAuras") or {}).get(enchantment) or set()):
                    aura_refresh = True
            resolved_id = None
            # Dictionary keys retain priority. Only a truly unmatched event may
            # fall back to an explicit internal Id, including the empty string.
            if not metadata and target_type is None and not aura_refresh:
                resolved = resolve_internal_ability(definition, enchantment, effect_id, action)
                if resolved is not None:
                    resolved_id, target_type, metadata = resolved
                    event["resolvedAbilityId"] = resolved_id
                    event["abilityResolution"] = "uniqueInternalId"
            event["targetType"] = target_type
            event.update(metadata)
            event["randomTarget"] = bool(target_type and "Random" in target_type)
            event["auraRefresh"] = bool(aura_refresh)
            known_effects = set(definition.get("known") or set())
            if enchantment:
                known_effects.update((definition.get("enchantments") or {}).get(enchantment) or {})
                known_effects.update((definition.get("enchantmentAuras") or {}).get(enchantment) or set())
            event["runtimeAbility"] = bool(resolved_id is None and effect_id and effect_id not in known_effects)
            if phase == "combat":
                self.actions[action] += 1
        elif entry["type"].endswith("EffectTriggered"):
            event["source"] = instance_ref(fields.get("Source"))
            event["sourceSide"] = self.side_for(event["source"])
            event["triggerSource"] = instance_ref(fields.get("TriggerSource"))
            targets = fields.get("Targets")
            event["targets"] = [decode_effect_target(item) for item in targets] if isinstance(targets, (list, tuple)) else []
        elif entry["type"].endswith("EffectAuraExecuted"):
            event["source"] = instance_ref(fields.get("Source"))
            event["sourceSide"] = self.side_for(event["source"])
            event["triggerSource"] = instance_ref(fields.get("TriggerSource"))
            event["appliedTo"] = [decode_effect_target(item) for item in fields.get("AppliedTo") or []]
            event["removedFrom"] = [decode_effect_target(item) for item in fields.get("RemovedFrom") or []]
        elif entry["type"].endswith("CardEnchanted"):
            event["instanceId"] = instance_id(fields.get("InstanceId"))
            event["enchantment"] = enum_name(enum_inverse(self.contract, "EEnchantmentType"),
                                             fields.get("EnchantmentType"))
            event["reverted"] = fields.get("IsReverted")
        elif entry["type"].endswith("CardTransformed"):
            event["originalInstanceId"] = instance_id(fields.get("OriginalInstanceId"))
            event["transformedCards"] = self.decode_transformations(fields.get("TransformedCards"))
            self.apply_transform(event, phase, frame, time)
        elif entry["type"].endswith("CardTransformReverted"):
            event["originalCard"] = self.decode_transformation(fields.get("OriginalCard"))
            event["transformedCardInstanceIds"] = [instance_id(item) for item in
                                                   (fields.get("TransformedCardInstanceIds") or [])]
            self.apply_transform_revert(event, phase, frame, time)
        elif entry["type"].endswith("CardActionCostSpent"):
            event["executingCard"] = instance_id(fields.get("ExecutingCard"))
            event["playerAttributeSpent"] = enum_name(
                enum_inverse(self.contract, "EPlayerAttributeType"), fields.get("PlayerAttributeSpent"))
            event["cardAttributeSpent"] = enum_name(
                enum_inverse(self.contract, "ECardAttributeType"), fields.get("CardAttributeSpent"))
        elif entry["type"].endswith("CardAttributeDeleted"):
            event["instanceId"] = instance_id(fields.get("InstanceId"))
            event["attribute"] = enum_name(enum_inverse(self.contract, "ECardAttributeType"),
                                           fields.get("AttributeType"))
        elif entry["type"].endswith("CardSpawned"):
            event["instanceId"] = instance_id(fields.get("InstanceId"))
            event["templateId"] = fields.get("TemplateId")
            event["side"] = SIDES.get(fields.get("CombatantId"), "unknown")
            event["section"] = enum_name(enum_inverse(self.contract, "EInventorySection"), fields.get("Section"))
            event["socket"] = fields.get("Socket")
        elif entry["type"].endswith("CardDisposed"):
            event["instanceId"] = instance_id(fields.get("InstanceId"))
            event["reason"] = fields.get("Reason")
        elif entry["type"].endswith("CombatantDied"):
            event["combatantId"] = fields.get("CombatantId")
            event["side"] = SIDES.get(fields.get("CombatantId"), "unknown")
        return event

    def side_for(self, iid):
        iid = instance_id(iid)
        if not iid:
            return None
        return self.side_by_instance.get(iid)

    def decode_transformation(self, raw):
        if raw is None:
            return None
        if isinstance(raw, dict):
            fields = raw
        else:
            fields, _ = field_values(self.contract["dtos"]["SimEventCardTransformation"], raw)
        return {
            "instanceId": instance_id(fields.get("InstanceId")),
            "templateId": fields.get("TemplateId"),
            "type": enum_name(enum_inverse(self.contract, "ECardType"), fields.get("Type")),
            "combatantId": fields.get("CombatantId"),
            "side": SIDES.get(fields.get("CombatantId"), "unknown"),
            "section": enum_name(enum_inverse(self.contract, "EInventorySection"), fields.get("Section")),
            "socket": fields.get("Socket"),
        }

    def decode_transformations(self, raw):
        if not isinstance(raw, (list, tuple)):
            return []
        return [self.decode_transformation(item) for item in raw]

    def current_generation(self, item):
        if not item or not item["generations"]:
            return None
        return item["generations"][-1]

    def next_generation(self, item):
        if not item:
            return 0
        return max(generation["generation"] for generation in item["generations"]) + 1

    def close_generation(self, item, time, phase, reason):
        generation = self.current_generation(item)
        if generation and generation.get("removed") is None:
            generation["removed"] = {"phase": phase, "time": time, "reason": reason}
        item["active"] = False

    def open_generation(self, item, generation, template_id, born, parent=None):
        record = {
            "generation": generation,
            "templateId": template_id,
            "born": born,
            "removed": None,
            "reason": None,
        }
        if parent:
            record["parent"] = parent
        item["generations"].append(record)
        item["generation"] = generation
        item["active"] = True
        item["templateId"] = template_id
        if born and item.get("born") is None:
            item["born"] = born
        if parent:
            item["parent"] = parent
        return record

    def apply_transform(self, event, phase, frame, time):
        original_id = event.get("originalInstanceId")
        original = self.instance(original_id)
        children = event.get("transformedCards") or []
        child_side = children[0]["side"] if children else None
        if original is None:
            side = child_side or self.side_by_instance.get(original_id, "unknown")
            original = self.ensure_instance(original_id, side, "lineage")
        parent_generation = self.current_generation(original)
        parent_number = parent_generation["generation"] if parent_generation else 0
        parent_key = original["key"]
        born = {"phase": phase, "frame": frame, "time": time, "source": event["codeName"]}
        self.close_generation(original, time, phase, "transformed")
        if parent_generation is not None:
            event["originalGeneration"] = parent_number
        for child in children:
            iid = child["instanceId"]
            side = child["side"] if child["side"] != "unknown" else original["side"]
            item = self.ensure_instance(iid, side, "replay.transform")
            self.side_by_instance[iid] = side
            item["templateId"] = child["templateId"]
            item["templateSource"] = "replay.transform"
            item["section"] = child["section"]
            item["socket"] = child["socket"]
            item["name"] = None
            generation = self.next_generation(item)
            self.open_generation(item, generation, child["templateId"], dict(born), parent_key)
            original["children"].append({
                "instanceId": iid,
                "key": item["key"],
                "generation": generation,
                "templateId": child["templateId"],
                "phase": phase,
                "frame": frame,
                "time": time,
            })
            child["generation"] = generation
            child["key"] = item["key"]
            child["born"] = dict(born)
        self.lineage.append({"kind": "transform", "phase": phase, "frame": frame, "time": time,
                             "originalInstanceId": original_id, "originalKey": parent_key,
                             "originalGeneration": parent_number,
                             "children": [dict(child) for child in children]})

    def apply_transform_revert(self, event, phase, frame, time):
        original = event.get("originalCard")
        if not original:
            return
        original_id = original.get("instanceId")
        item = self.instance(original_id)
        side = original.get("side") if original.get("side") != "unknown" else None
        if item is None:
            item = self.ensure_instance(original_id, side or "unknown", "lineage.revert")
        if side:
            self.side_by_instance[original_id] = side
        for child_id in event.get("transformedCardInstanceIds") or []:
            child = self.instance(child_id)
            if child is not None:
                self.close_generation(child, time, phase, "transform_reverted")
                child["children"] = child.get("children", [])
        generation = self.next_generation(item)
        born = {"phase": phase, "frame": frame, "time": time, "source": event["codeName"]}
        self.open_generation(item, generation, original.get("templateId"), born,
                             "reverted:" + ",".join(event.get("transformedCardInstanceIds") or []))
        item["templateId"] = original.get("templateId")
        item["section"] = original.get("section")
        item["socket"] = original.get("socket")
        event["restoredGeneration"] = generation
        self.lineage.append({"kind": "revert", "phase": phase, "frame": frame, "time": time,
                             "originalInstanceId": original_id, "restoredGeneration": generation,
                             "transformedCardInstanceIds": event.get("transformedCardInstanceIds") or []})

    # ---------- combat timeline ----------

    def record_cooldown(self, iid, item, name, previous, current, frame, time):
        return None

    def process_health(self, raw, side, frame, time):
        if raw is None:
            return None
        fields, warning = field_values(self.contract["dtos"]["CombatSimPlayerUpdate"], raw)
        if warning:
            self.warning("combat.playerUpdate", "%s 帧 %s 字段数不一致 %r" % (side, frame, warning))
        adjustments = []
        for raw_adjustment in fields.get("HealthAdjustments") or []:
            adj, _ = field_values(self.contract["dtos"]["CombatSimPlayerHealthAdjustment"], raw_adjustment)
            adjustments.append({
                "damageType": enum_name(enum_inverse(self.contract, "EDamageType"), adj.get("DamageType")),
                "attributeChanged": enum_name(enum_inverse(self.contract, "EPlayerHealthChangeType"),
                                              adj.get("AttributeChanged")),
                "amount": adj.get("Amount"),
                "isCrit": adj.get("IsCrit"),
                "isDamageReduced": adj.get("IsDamageReduced"),
            })
        attributes = {}
        raw_attributes = fields.get("Attributes")
        if isinstance(raw_attributes, dict):
            inverse = enum_inverse(self.contract, "EPlayerAttributeType")
            for key, raw_update in raw_attributes.items():
                update, _ = field_values(self.contract["dtos"]["CombatSimPlayerAttributeUpdate"], raw_update)
                name = enum_name(inverse, key if key is not None else update.get("AttributeType"))
                attributes[name] = {"previous": update.get("PreviousValue"),
                                     "current": update.get("CurrentValue")}
                self.vitals[side][name] = update.get("CurrentValue")
        dead = bool(fields.get("IsPlayerDead"))
        if fields.get("IsPlayerDead") is not None:
            self.vitals[side]["IsPlayerDead"] = dead
        change = {
            "frame": frame, "time": time, "side": side, "dead": dead,
            "healthAdjustments": adjustments, "attributes": attributes,
            "vitals": dict(self.vitals[side]),
        }
        relevant = {name: update for name, update in attributes.items()
                    if name in HEALTH_ATTRIBUTES}
        if adjustments or relevant or dead:
            change["attributes"] = relevant
            self.health_changes.append(change)
        return change

    def process_card_update(self, key, raw, frame, time):
        iid = instance_id(key)
        item = self.combat_instance(iid, "replay.cardUpdate")
        if item["side"] == "unknown":
            self.unknown("combat.instanceSide", "帧 %s 的实例 %s 无法判定阵营" % (frame, iid),
                         "CombatSim.CardUpdates")
        fields, warning = field_values(self.contract["dtos"]["CombatSimCardUpdate"], raw)
        if warning:
            self.warning("combat.cardUpdate", "帧 %s CardUpdate 字段数不一致 %r" % (frame, warning))
        changed = []
        for name, previous, current in self.decode_card_attribute_updates(fields.get("Attributes")):
            if previous == current:
                continue
            item["attributes"][name] = current
            item["attributeSources"][name] = "replay.cardUpdate"
            record = {"frame": frame, "time": time, "instanceId": iid, "side": item["side"],
                      "attribute": name, "previous": previous, "current": current}
            self.card_updates.append(record)
            changed.append(record)
        for field_name in ("Enchantment", "Size", "Tier", "State"):
            value = fields.get(field_name)
            if value is None:
                continue
            if field_name == "State":
                state, _ = field_values(self.contract["dtos"]["CombatSimCardStateUpdate"], value)
                previous, current = state.get("PreviousValue"), state.get("CurrentValue")
                if previous == current:
                    continue
                record = {"frame": frame, "time": time, "instanceId": iid, "side": item["side"],
                          "kind": "state", "previous": enum_name(
                              enum_inverse(self.contract, "ECardState"), previous),
                          "current": enum_name(enum_inverse(self.contract, "ECardState"), current)}
                item["state"] = record["current"]
            else:
                enum_table = {"Enchantment": "EEnchantmentType", "Size": "ECardSize", "Tier": "ETier"}[field_name]
                record = {"frame": frame, "time": time, "instanceId": iid, "side": item["side"],
                          "kind": field_name.lower(), "previous": item.get(field_name.lower()),
                          "current": enum_name(enum_inverse(self.contract, enum_table), value)}
                item[field_name.lower()] = record["current"]
            self.card_updates.append(record)
            changed.append(record)
        for field_name in ("Placement", "Tags", "HiddenTags", "Heroes"):
            value = fields.get(field_name)
            if value is None:
                continue
            if field_name == "Placement":
                record = {"frame": frame, "time": time, "instanceId": iid, "side": item["side"],
                          "kind": "placement", "previous": None, "current": raw_json(value)}
                if isinstance(value, (list, tuple)) and len(value) >= 3:
                    item["section"] = enum_name(enum_inverse(self.contract, "EInventorySection"), value[2])
                    item["socket"] = value[1]
            else:
                record = {"frame": frame, "time": time, "instanceId": iid, "side": item["side"],
                          "kind": field_name.lower(), "previous": None, "current": raw_json(value)}
                item[field_name.lower()] = raw_json(value)
            self.card_updates.append(record)
            changed.append(record)
        if changed:
            self.touch_instance(item, "replay.cardUpdate")
        return changed

    # ---------- main build ----------

    def build(self):
        self.load()
        spawn = self.sim_parts(self.replay_doc.get("SpawnMessageBytes"), "SpawnMessage")
        combat = self.sim_parts(self.replay_doc.get("CombatMessageBytes"), "CombatMessage")
        despawn = self.sim_parts(self.replay_doc.get("DespawnMessageBytes"), "DespawnMessage")
        if spawn is None or combat is None or despawn is None:
            return self.finish()
        self.merge_snapshot()
        self.capture_opening()
        self.merge_spawn(spawn)
        self.capture_opening(only_missing=True)
        self.combat = combat
        self.despawn_sim = despawn
        self.recover_opening_player_attributes()
        self.merge_frame0()

        frames = combat[0] if isinstance(combat, list) and combat and isinstance(combat[0], list) else []
        self.frame_count = len(frames)
        self.duration = round(self.frame_count * self.frame_seconds, 6)
        sequence = 0
        for frame, raw_frame in enumerate(frames):
            if not isinstance(raw_frame, list) or len(raw_frame) < 4:
                self.complete = False
                self.unknown("combat.frame", "帧 %s 不是 CombatSimFrame 四字段" % frame)
                continue
            time = decode_time(frame, self.frame_seconds)
            for raw_event in raw_frame[0] or []:
                event = self.normalize_event(raw_event, "combat", frame, time, sequence)
                sequence += 1
                if event is not None:
                    self.frame_events.append(event)
            self.process_health(raw_frame[1], "player", frame, time)
            self.process_health(raw_frame[2], "enemy", frame, time)
            if isinstance(raw_frame[3], dict):
                for key, raw in raw_frame[3].items():
                    self.process_card_update(key, raw, frame, time)

        # Link CardTransformed records to the CardTransform action that produced
        # them.  When that action's target is TTargetCardRandom, the chosen card
        # (and therefore the child lineage) cannot be reconstructed from the
        # replay payload; keep that branch visibly unverified.
        random_transform_targets = set()
        for event in self.frame_events:
            if event.get("action") == "CardTransform" and event.get("randomTarget"):
                target = event.get("target") or {}
                for iid in target.get("ids") or []:
                    random_transform_targets.add((event.get("frame"), iid))
        for event in self.frame_events:
            if str(event.get("codeName") or "").endswith("CardTransformed"):
                if (event.get("frame"), event.get("originalInstanceId")) in random_transform_targets:
                    event["randomTarget"] = True

        # GameSim transform events in the spawn phase were not needed for the
        # opening board, but they are part of the lineage and event ledger.
        for raw in self.spawn_events:
            if isinstance(raw, list) and raw and raw[0] in (25, 26):
                event = self.normalize_event(raw, "opening", None, 0.0, sequence)
                sequence += 1
                if event is not None:
                    self.lineage_event = event

        despawn_events = despawn[0] if isinstance(despawn[0], list) else []
        for raw in despawn_events:
            event = self.normalize_event(raw, "despawn", None, self.duration, sequence)
            sequence += 1
            if event is not None:
                self.despawn_events.append(event)
        self.merge_despawn(despawn)
        self.note_outcome()
        return self.finish()

    def merge_despawn(self, sim):
        if not isinstance(sim, list) or len(sim) < 7:
            return
        _, player, opponent, cards, _, _, _ = sim[:7]
        self.despawn_attrs = {
            "player": self.parse_player_attributes(
                player[1] if isinstance(player, list) and len(player) > 1 else None,
                "player", "replay.despawn.player", record_source=False),
            "enemy": self.parse_player_attributes(
                opponent[1] if isinstance(opponent, list) and len(opponent) > 1 else None,
                "enemy", "replay.despawn.opponent", record_source=False),
        }
        self.despawn_cards = []
        if isinstance(cards, dict):
            for iid, raw in cards.items():
                fields, _ = field_values(self.contract["dtos"]["SimUpdateCard"], raw)
                item = self.instance(iid)
                self.despawn_cards.append({
                    "instanceId": instance_id(iid),
                    "side": item["side"] if item else self.side_by_instance.get(instance_id(iid), "unknown"),
                    "attributes": self.parse_card_attributes(fields.get("Attributes"), "replay.despawn.cards",
                                                              item["side"] if item else None, iid),
                    "enchantment": enum_name(enum_inverse(self.contract, "EEnchantmentType"),
                                             fields.get("Enchantment")),
                    "tier": enum_name(enum_inverse(self.contract, "ETier"), fields.get("Tier")),
                    "state": enum_name(enum_inverse(self.contract, "ECardState"), fields.get("State")),
                })

    def note_outcome(self):
        if not isinstance(self.combat, list):
            return
        fields, _ = field_values(self.contract["dtos"]["CombatSim"], self.combat)
        self.combat_outcome = {
            "winner": enum_name(enum_inverse(self.contract, "ECombatantId"), fields.get("Winner")),
            "loser": enum_name(enum_inverse(self.contract, "ECombatantId"), fields.get("Loser")),
            "winnerSide": SIDES.get(fields.get("Winner")),
            "loserSide": SIDES.get(fields.get("Loser")),
        }

    def finish(self):
        self.note_missing_opening()
        self.note_sample_limits()
        for item in self.instances.values():
            if item["generations"]:
                current = self.current_generation(item)
                if current and not current.get("templateId"):
                    current["templateId"] = item.get("templateId")
                if current and current.get("born") is None:
                    current["born"] = item.get("born")
        ledger = {
            "ledgerVersion": LEDGER_VERSION,
            "generator": "tools/native_replay_ledger.py",
            "complete": self.complete,
            "contract": {
                "path": str(self.contract_path),
                "sha256": sha256_file(self.contract_path),
                "contractVersion": self.contract.get("contractVersion"),
                "assemblySha256": self.contract.get("assemblySha256"),
                "gameDataSha256": self.contract.get("gameDataSha256"),
                "payloadVersions": self.contract.get("payloadVersions"),
            },
            "replay": {
                "path": str(self.replay_path),
                "sha256": self.replay_sha256,
                "battleId": self.replay_doc.get("BattleId"),
                "version": self.replay_doc.get("Version"),
                "gameDataVersion": self.run.get("DataVersion"),
                "frameSeconds": self.frame_seconds,
                "frameCount": self.frame_count,
                "duration": self.duration,
            },
            "provenance": {
                "replay": "gzip MessagePack/LZ4 payload: SpawnMessageBytes + CombatMessageBytes + DespawnMessageBytes",
                "openingSidecar": self.snapshot_info or None,
                "battles": self.battles_info or None,
            },
            "opening": {
                "run": self.run,
                "player": self.side_opening("player"),
                "enemy": self.side_opening("enemy"),
                "unknownSide": list(self.opening_unknown_side),
                "unknown": list(self.unknowns),
            },
            "identity": {
                "keyFormat": "side + ':' + instanceId",
                "instances": {key: self.serialize_instance(item) for key, item in self.instances.items()},
                "lineage": self.lineage,
            },
            "combat": {
                "frames": self.frame_count,
                "duration": self.duration,
                "eventsByCode": dict(sorted(self.events_by_code.items(), key=lambda pair: int(pair[0]))),
                "actions": dict(sorted(self.actions.items())),
                "events": self.frame_events,
                "healthChanges": self.health_changes,
                "cardUpdates": self.card_updates,
                "activationsDerived": self.derived_activations(),
                "outcome": self.combat_outcome,
            },
            "despawn": {
                "events": self.despawn_events,
                "cards": getattr(self, "despawn_cards", []),
                "attributes": getattr(self, "despawn_attrs", {}),
            },
            "warnings": self.warnings,
            "coverageNotes": self.coverage_notes,
            "unknowns": self.unknowns,
        }
        return ledger

    def side_opening(self, side):
        attributes = dict(self.open_attrs[side])
        unknown = [name for name in PLAYER_ATTRS_REQUIRED if name not in attributes]
        if side == "player":
            unknown.append("randomSeed")
        return {
            "side": side,
            "combatantId": 0 if side == "player" else 1,
            "hero": self.heroes[side],
            "heroSource": self.hero_sources.get(side),
            "attributes": attributes,
            "attributeSources": dict(self.attr_sources[side]),
            "unknownAttributes": unknown,
            "health": attributes.get("Health"),
            "healthMax": attributes.get("HealthMax"),
            "shield": attributes.get("Shield"),
            "healthRegen": attributes.get("HealthRegen"),
            "rageMax": attributes.get("RageMax"),
            "enragedDurationMax": attributes.get("EnragedDurationMax"),
            "items": [self.serialize_opening_item(record)
                      for record in self.opening_instances[side].values()],
            "skills": self.skills[side],
        }

    @staticmethod
    def is_opening_instance(item):
        born = item.get("born") or {}
        return born.get("phase") == "opening"

    def serialize_opening_item(self, record):
        return {
            "instanceId": record["instanceId"],
            "key": record["key"],
            "templateId": record.get("templateId"),
            "templateSource": record.get("templateSource"),
            "name": record.get("name"),
            "tier": record.get("tier"),
            "enchantment": record.get("enchantment"),
            "size": record.get("size"),
            "section": record.get("section"),
            "inCombat": record.get("inCombat", record.get("section") != "Stash"),
            "socket": record.get("socket"),
            "attributes": dict(record.get("attributes") or {}),
            "attributeSources": dict(record.get("attributeSources") or {}),
            "tags": list(record.get("tags") or []),
            "hiddenTags": list(record.get("hiddenTags") or []),
            "heroes": list(record.get("heroes") or []),
            "state": record.get("state"),
            "born": record.get("born"),
        }

    def serialize_instance(self, item):
        return {
            "key": item["key"],
            "side": item["side"],
            "instanceId": item["instanceId"],
            "templateId": item.get("templateId"),
            "templateSource": item.get("templateSource"),
            "generation": item.get("generation", 0),
            "active": item.get("active", True),
            "born": item.get("born"),
            "parent": item.get("parent"),
            "children": item.get("children") or [],
            "generations": item.get("generations") or [],
            "sources": item.get("sources") or [],
            "attributes": dict(item.get("attributes") or {}),
            "attributeSources": dict(item.get("attributeSources") or {}),
            "tier": item.get("tier"),
            "enchantment": item.get("enchantment"),
            "size": item.get("size"),
            "section": item.get("section"),
            "socket": item.get("socket"),
            "state": item.get("state"),
        }

    def derived_activations(self):
        groups = OrderedDict()
        for event in self.frame_events:
            if event.get("codeName", "").endswith("EffectExecuted"):
                key = (event.get("source"), event.get("frame"))
                group = groups.get(key)
                if group is None:
                    group = {
                        "kind": "effectBatchDerived",
                        "derived": "grouped by EffectExecuted source + frame; not a native activation event",
                        "source": event.get("source"),
                        "side": event.get("sourceSide"),
                        "frame": event.get("frame"),
                        "time": event.get("time"),
                        "actions": [],
                        "targets": [],
                        "executionContexts": [],
                    }
                    groups[key] = group
                action = event.get("action")
                if action and action not in group["actions"]:
                    group["actions"].append(action)
                target = event.get("target")
                if target and target not in group["targets"]:
                    group["targets"].append(target)
                context = (event.get("fields") or {}).get("ExecutionContextId")
                if context and context not in group["executionContexts"]:
                    group["executionContexts"].append(context)
        return list(groups.values())

    def note_missing_opening(self):
        for side in ("player", "enemy"):
            for name in PLAYER_ATTRS_REQUIRED:
                if name not in self.open_attrs[side]:
                    self.unknown("opening.attribute.%s" % side, "回放 SpawnMessage 与侧车均未提供 %s" % name,
                                 "replay/opening")
        for item in self.instances.values():
            if item["side"] == "player" and not item.get("templateId"):
                self.unknown("opening.playerTemplate", "玩家实例 %s 没有模板 ID" % item["instanceId"],
                             "replay/opening")
            if item["side"] == "enemy" and not item.get("templateId"):
                self.unknown("opening.enemyTemplate", "敌方实例 %s 没有模板 ID" % item["instanceId"],
                             "replay/opening")
        skill_capture = (self.snapshot_info or {}).get("playerSkills") or {}
        captured_empty = skill_capture.get("status") == "CapturedEmpty"
        if not self.skills["player"] and not captured_empty:
            self.unknown("opening.playerSkills", "没有玩家技能数据（回放载荷不携带，需 OpeningMessage 侧车）")
        if not self.snapshot_info:
            self.unknown("opening.provenance", "没有 OpeningMessage 侧车；玩家模板/技能/部分属性为 UNKNOWN")

    def note_sample_limits(self):
        battle_id = self.replay_doc.get("BattleId") or "replay"
        transform_codes = [event for event in self.frame_events if event.get("codeName", "").endswith("CardTransformed")]
        if not transform_codes:
            self.coverage("sample.transform", "该回放没有战斗内 CardTransformed 事件；变形谱系路径未被本样本覆盖",
                         battle_id)
        if not any(event.get("codeName", "").endswith("CardTransformReverted") for event in self.despawn_events):
            self.coverage("sample.transformRevert", "该回放没有 CardTransformReverted 事件；回退路径未被本样本覆盖",
                         battle_id)
        self.unknown("sample.random", "回放载荷没有可重建的随机种子/选取顺序；随机分支只能标为 UNVERIFIED",
                     "replay payload")
        if not any(event.get("codeName", "").endswith("EffectTriggered") for event in self.frame_events):
            self.coverage("sample.passive", "该回放战斗帧没有 EffectTriggered 事件；被动触发目标无法逐事件核对",
                         battle_id)
        self.coverage("sample.activation", "原生战斗帧没有独立的主动施放事件；activationsDerived 仅为 EffectExecuted 按来源/帧分组")


_TEMPLATES_BY_ID = {}


def templates_by_id(templates_path):
    """templateId -> template dict from the mapping jsonl (cached per path)."""
    key = str(templates_path)
    if key not in _TEMPLATES_BY_ID:
        table = {}
        try:
            with open(templates_path, encoding="utf-8") as handle:
                for line in handle:
                    line = line.strip()
                    if not line:
                        continue
                    row = json.loads(line)
                    if row.get("id"):
                        table[row["id"]] = row.get("template") or {}
        except OSError:
            pass
        _TEMPLATES_BY_ID[key] = table
    return _TEMPLATES_BY_ID[key]


def quest_reward_abilities(template, attributes):
    """Ability ids granted before combat by completed quests.

    Quest counters (Quest_1, Quest_2, ...) ride on the card's live opening
    attributes; an entry whose counter already reached its Target completed
    while shopping and its reward entered combat with the card.  Only combat
    abilities are surfaced here — stat-aura rewards (Multicast, cooldown) are
    already baked into the live attributes the fixture imports, re-applying
    them would double count.
    """
    granted = []
    for quest in template.get("Quests") or []:
        for entry in quest.get("Entries") or []:
            try:
                target = int(entry.get("Target"))
            except (TypeError, ValueError):
                continue
            counter = (attributes or {}).get(entry.get("AttributeType"))
            if not isinstance(counter, (int, float)) or counter < target:
                continue
            for ability_id, ability in ((entry.get("Reward") or {}).get("Abilities") or {}).items():
                if isinstance(ability, dict) and ability.get("Id") == ability_id:
                    granted.append(ability_id)
    return granted


def build_replay_random_tape(ledger):
    """Use the same explicit-decision decoder for every fixture entry point.

    Only explicit isCrit flags, target ids, firing order, enchantment names,
    and random donor template ids are inputs. Effect amounts and health
    deltas must never be used to guess a critical result.
    """
    if __package__:
        from .replay_random_decisions import build_decisions
    else:
        from replay_random_decisions import build_decisions
    result = build_decisions(ledger)
    disabled = set((os.environ.get("RR_DISABLE") or "").split(","))
    for setting, key in (("orders", "order"), ("criticals", "criticals")):
        if setting in disabled:
            result[key] = []
    return result


def skill_tier_attributes(template, tier):
    attributes = {}
    for name in ('Bronze', 'Silver', 'Gold', 'Diamond'):
        attributes.update(((template.get('Tiers') or {}).get(name) or {}).get('Attributes') or {})
        if name == ('Diamond' if tier == 'Legendary' else tier):
            break
    return attributes


def compatible_archived_skill(skill, current, version, archived):
    """Resolve a same-version revision only from captured opening thresholds.

    A game data version may cover multiple patches. Do not select from combat
    effects or later counters: require a unique archived definition whose
    captured non-counter Custom_1/2 thresholds agree, while the current
    definition contradicts at least one of those same fields.
    """
    if not version or not current:
        return None
    captured = skill.get('attributes') or {}
    keys = [key for key in ('Custom_1', 'Custom_2')
            if isinstance(captured.get(key), (int, float))]
    if not keys:
        return None
    tier = skill.get('tier') or 'Bronze'
    current_attrs = skill_tier_attributes(current, tier)
    if not any(key in current_attrs and current_attrs[key] != captured[key] for key in keys):
        return None
    candidates = []
    for row in archived:
        template = row.get('template') or {}
        if (row.get('templateId') != skill.get('templateId') or row.get('version') != version or
                template.get('Version') != version or template.get('Id') != skill.get('templateId')):
            continue
        attrs = skill_tier_attributes(template, tier)
        if all(key in attrs and attrs[key] == captured[key] for key in keys):
            candidates.append(template)
    return candidates[0] if len(candidates) == 1 else None


def build_engine_fixture(ledger, templates_path, catalog_path, template_sha=None, catalog_sha=None,
                         template_ids=None, skill_catalog_path=None):
    opening = ledger["opening"]
    templates = templates_by_id(templates_path)
    items = []
    missing = []
    excluded = []
    for side in ("player", "enemy"):
        for item in opening[side]["items"]:
            template_id = item.get("templateId")
            if item.get("section") == "Stash":
                excluded.append({"side": side, "instanceId": item.get("instanceId"),
                                 "templateId": template_id, "reason": "Stash"})
                continue
            if not template_id:
                missing.append("%s:%s" % (side, item.get("instanceId")))
            elif template_ids is not None and template_id not in template_ids:
                missing.append("%s:%s(%s)" % (side, item.get("instanceId"), template_id))
            row = {
                "side": side,
                "instanceId": item.get("instanceId"),
                "templateId": template_id,
                "tier": item.get("tier") or "Bronze",
                "size": item.get("size"),
                "slot": item.get("socket") if isinstance(item.get("socket"), int) else 0,
                "section": item.get("section"),
                "enchantment": item.get("enchantment"),
                "attributes": item.get("attributes") or {},
            }
            quest_ids = quest_reward_abilities(templates.get(template_id) or {}, item.get("attributes"))
            if quest_ids:
                row["questAbilities"] = quest_ids
            items.append(row)
    skills = []
    version = ledger['replay'].get('gameDataVersion')
    archive_path = pathlib.Path(skill_catalog_path) if skill_catalog_path else ROOT / 'data/skills.json'
    archived_skills = json.loads(archive_path.read_text(encoding='utf-8')) if version and archive_path.exists() else []
    for side in ("player", "enemy"):
        for skill in opening[side].get("skills") or []:
            if not skill.get("templateId"):
                # Without the BPP OpeningMessage sidecar a skill has no template;
                # the simulator cannot map it, so exclude it and say so.
                excluded.append({"side": side, "instanceId": skill.get("instanceId"),
                                 "templateId": None, "reason": "SkillTemplateUnknown"})
                continue
            if template_ids is not None and skill.get("templateId") not in template_ids:
                excluded.append({"side": side, "instanceId": skill.get("instanceId"),
                                 "templateId": skill.get("templateId"),
                                 "reason": "SkillTemplateMissing"})
                continue
            row = {
                "side": side,
                "instanceId": skill.get("instanceId"),
                "templateId": skill.get("templateId"),
                "tier": skill.get("tier") or "Bronze",
                "attributes": skill.get("attributes") or {},
            }
            archived = compatible_archived_skill(skill, templates.get(skill.get('templateId')), version, archived_skills)
            if archived is not None:
                row['template'] = archived
                row['templateSource'] = {'path': str(archive_path), 'sha256': sha256_file(archive_path),
                                         'version': version, 'selection': 'unique-opening-threshold-match'}
            skills.append(row)
    if missing:
        raise SystemExit("无法生成引擎夹具，缺少模板 ID: %s" % ", ".join(missing))
    player = opening["player"]
    enemy = opening["enemy"]
    return {
        "fixtureVersion": 1,
        "battleId": ledger["replay"]["battleId"],
        "gameDataVersion": version,
        "replaySha256": ledger["replay"]["sha256"],
        "contractSha256": ledger["contract"]["sha256"],
        "templatePath": str(templates_path),
        "templateSha256": template_sha or sha256_file(templates_path),
        "catalogPath": str(catalog_path),
        "catalogSha256": catalog_sha or sha256_file(catalog_path),
        "frameSeconds": ledger["replay"]["frameSeconds"],
        "nativeDuration": ledger["replay"]["duration"],
        "maxTime": max(30.0, ledger["replay"]["duration"]),
        "dt": ledger["replay"]["frameSeconds"],
        "player": {
            "hero": player.get("hero"),
            "attributes": dict(player.get("attributes") or {}),
            "attributesAreLive": True,
            "maxHp": player.get("healthMax"),
            "startHp": player.get("health"),
            "startShield": player.get("shield"),
            "startRegen": player.get("healthRegen"),
            "rageMax": player.get("rageMax"),
            "enragedDurationMax": player.get("enragedDurationMax"),
        },
        "enemy": {
            "hero": enemy.get("hero"),
            "attributes": dict(enemy.get("attributes") or {}),
            "attributesAreLive": True,
            "maxHp": enemy.get("healthMax"),
            "startHp": enemy.get("health"),
            "startShield": enemy.get("shield"),
            "startRegen": enemy.get("healthRegen"),
            "rageMax": enemy.get("rageMax"),
            "enragedDurationMax": enemy.get("enragedDurationMax"),
        },
        "items": items,
        "skills": skills,
        "excludedItems": excluded,
        "replayRandom": build_replay_random_tape(ledger),
    }


def summarize(ledger):
    opening = ledger["opening"]
    print("battleId=%s version=%s frames=%s duration=%ss sha256=%s" % (
        ledger["replay"]["battleId"], ledger["replay"]["version"],
        ledger["replay"]["frameCount"], ledger["replay"]["duration"],
        ledger["replay"]["sha256"]))
    print("contract=%s assembly=%s" % (
        ledger["contract"]["contractVersion"], ledger["contract"]["assemblySha256"][:16]))
    print("opening player %s/%s items=%s skills=%s; enemy %s/%s items=%s skills=%s" % (
        opening["player"]["hero"], opening["player"]["health"],
        len(opening["player"]["items"]), len(opening["player"]["skills"]),
        opening["enemy"]["hero"], opening["enemy"]["health"],
        len(opening["enemy"]["items"]), len(opening["enemy"]["skills"])))
    print("events=%s actions=%s complete=%s unknown=%s warnings=%s" % (
        sum(ledger["combat"]["eventsByCode"].values()), ledger["combat"]["actions"],
        ledger["complete"], len(ledger["unknowns"]), len(ledger["warnings"])))
    if ledger["unknowns"]:
        for entry in ledger["unknowns"][:12]:
            print("UNKNOWN %s: %s" % (entry["scope"], entry["detail"]))


def main(argv=None):
    parser = argparse.ArgumentParser(description="Build a native BPP replay event ledger")
    parser.add_argument("replay")
    parser.add_argument("--json")
    parser.add_argument("--contract", default=str(DEFAULT_CONTRACT))
    parser.add_argument("--snapshot-db")
    parser.add_argument("--no-snapshot", action="store_true")
    parser.add_argument("--engine-fixture")
    parser.add_argument("--templates", default=str(DEFAULT_TEMPLATES))
    parser.add_argument("--catalog", default=str(DEFAULT_CATALOG))
    args = parser.parse_args(argv)
    builder = LedgerBuilder(args.replay, args.contract, args.snapshot_db, not args.no_snapshot,
                            load_ability_targets(args.templates))
    ledger = builder.build()
    if args.json:
        target = pathlib.Path(args.json)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(ledger, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    summarize(ledger)
    if args.engine_fixture:
        fixture = build_engine_fixture(ledger, pathlib.Path(args.templates), pathlib.Path(args.catalog))
        target = pathlib.Path(args.engine_fixture)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(fixture, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
        print("engine fixture -> %s (%s items, %s skills)" % (target, len(fixture["items"]), len(fixture["skills"])))
    return 0 if ledger["complete"] else 2


if __name__ == "__main__":
    raise SystemExit(main())
