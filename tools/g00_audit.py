"""G00 数据、品质、读取与映射矩阵的可复现基线审计。

只读取游戏数据库、派单索引、测试夹具和 C# 矩阵报告；不生成游戏数据，
不修改映射器，也不把组合数当成语义覆盖率。

Usage:
    python tools/g00_audit.py --output data/g00_baseline.json
    python tools/g00_audit.py --check
"""
from __future__ import annotations

import argparse
import collections
import csv
import hashlib
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import rule_inventory

ROOT = Path(__file__).resolve().parents[1]
CANONICAL_ITEMS = ("Bronze", "Silver", "Gold", "Diamond")
SKILL_TIERS = ("Bronze", "Silver", "Gold", "Diamond", "Legendary")
SCENARIOS = ("template", "live", "mixed")
SIDES = ("Player", "Enemy")
MATRIX_HEADER = (
    "kind", "table", "id", "name", "tier", "tier_source", "enchantment", "side", "scenario",
    "attributes_live", "player_hero", "enemy_hero", "status", "enchantment_status",
    "live_attribute_status", "enchantment_warning", "live_attribute_warning", "reason",
)
MONSTER_HEADER = (
    "record", "monster_id", "monster_name", "index", "template_id", "template_name", "kind",
    "tier", "tier_source", "enchantment", "enchantment_status", "socket", "status", "reason",
)
SOURCE_FILES = (
    "tools/rule_inventory.py",
    "tools/g00_audit.py",
    "tools/test_g00_audit.py",
    "tools/actionaxis_plugin/tests/NativeCoverageRunner.cs",
    "tools/actionaxis_plugin/tests/MonsterCoverageRunner.cs",
    "tools/actionaxis_plugin/tests/G00DataTests.cs",
    "tools/actionaxis_plugin/tests/G00CoverageTests.cs",
    "tools/actionaxis_plugin/tests/Program.cs",
    "tools/actionaxis_plugin/engine/CardMapper.cs",
    "tools/actionaxis_plugin/engine/LiveBoard.cs",
    "tools/actionaxis_plugin/engine/LiveMonster.cs",
    "tools/actionaxis_plugin/engine/SkillMapper.cs",
    "tools/actionaxis_plugin/plugin/LiveSkillReader.cs",
    "tools/actionaxis_plugin/plugin/LiveCardDetailReader.cs",
    "tools/actionaxis_plugin/plugin/LiveMonsterReader.cs",
    "tools/actionaxis_plugin/tests/fixtures/native_g00_templates.json",
)
COUNTERSTRIKE = "c5bff6ad-5d13-4cf3-9941-98f3cf913949"
WILD_BOAR_ITEM = "e01ac7ab-1ad5-4235-81b4-44d057878953"
WILD_BOAR_MONSTER = "5d779101-3fa5-44b7-b885-f8bb9b0f7438"
KNOWN_DB_SHA256 = "FDFD3087E6A23EDA2C9F53BE68B012006CC949D03E8666DD51AD65ABC71B61E5"
KNOWN_MISSING_DEFINITIONS = (56, 64, 164)
KNOWN_MONSTER_GAPS = {
    "missing_item_templates": 16,
    "undeclared_item_tiers": 4,
    "missing_item_enchantments": 0,
    "missing_skill_templates": 0,
    "undeclared_skill_tiers": 62,
    "effects": 0,
    "socket_effects": 3,
}
KNOWN_OUT_OF_INDEX = {"TCardPlayerEffect": 9, "TCardSocketEffect": 2}


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def relative(path: Path) -> str:
    return path.resolve().relative_to(ROOT).as_posix()


def load_records(db_path: Path):
    """完整模板按表分类；只保留物品、技能和怪物，重复键立即失败。"""
    records = {}
    counts = collections.Counter()
    duplicates = []
    connection = rule_inventory.open_db(db_path)
    try:
        for table, template_id, template in rule_inventory.iter_rows(connection):
            kind = template.get("$type", "")
            if "monster" in table.lower() or kind == "TMonster":
                group = "monsters"
            elif kind == "TCardItem":
                group = "items"
            elif kind == "TCardSkill":
                group = "skills"
            else:
                continue
            key = (table, template_id)
            if key in records:
                duplicates.append([table, template_id])
                continue
            records[key] = (group, template)
            counts[group] += 1
    finally:
        connection.close()
    return records, counts, sorted(duplicates)


def item_expected(template: dict):
    tiers = template.get("Tiers") or {}
    result = []
    for tier in CANONICAL_ITEMS:
        if tier in tiers:
            result.append((tier, "declared"))
    if "Diamond" in tiers:
        result.append(("Legendary", "legendary-alias"))
    elif "Legendary" in tiers:
        result.append(("Legendary", "declared"))
    if not result:
        starting = template.get("StartingTier") or "Bronze"
        result.append((starting, "missing-tier"))
    enchantments = [""]
    if isinstance(template.get("Enchantments"), dict):
        enchantments.extend(sorted(template["Enchantments"]))
    return result, enchantments


def skill_expected(template: dict):
    tiers = template.get("Tiers") or {}
    result = []
    for tier in SKILL_TIERS:
        if tier == "Legendary":
            result.append((tier, "legendary-alias"))
        else:
            result.append((tier, "declared" if tier in tiers else "inactive-sparse"))
    return result, [""]


def expected_keys(template: dict, skill: bool, sides=SIDES, scenarios=SCENARIOS):
    tiers, enchantments = skill_expected(template) if skill else item_expected(template)
    result = []
    for tier, source in tiers:
        for enchantment in enchantments:
            for side in sides:
                for scenario in scenarios:
                    result.append((tier, source, enchantment or "None", side, scenario))
    return result, tiers


def tier_facts(records):
    item_declared = collections.Counter()
    skill_declared = collections.Counter()
    items = {"with_diamond": 0, "sparse_lower": 0, "explicit_legendary": 0,
             "legendary_differs_from_diamond": 0, "legendary_differs_from_diamond_ids": [],
             "gaps": [], "missing_diamond": []}
    skills = {"sparse": 0, "with_all_canonical": 0, "explicit_legendary": 0,
              "legendary_differs_from_diamond": 0, "legendary_differs_from_diamond_ids": [],
              "missing_all_canonical": []}
    enchantments = collections.Counter()
    items_with_enchantments = 0
    max_enchantments = 0
    for (table, template_id), (group, template) in records.items():
        tiers = template.get("Tiers") or {}
        if group == "items":
            for tier in tiers:
                item_declared[tier] += 1
            if "Diamond" in tiers:
                items["with_diamond"] += 1
            else:
                items["missing_diamond"].append(template_id)
            if "Legendary" in tiers:
                items["explicit_legendary"] += 1
                if tiers.get("Diamond") != tiers.get("Legendary"):
                    items["legendary_differs_from_diamond"] += 1
                    items["legendary_differs_from_diamond_ids"].append(template_id)
            positions = [CANONICAL_ITEMS.index(tier) for tier in CANONICAL_ITEMS if tier in tiers]
            if positions and positions[0] != 0:
                items["sparse_lower"] += 1
            if positions and positions != list(range(positions[0], positions[-1] + 1)):
                items["gaps"].append(template_id)
            enchantment_names = list((template.get("Enchantments") or {}).keys())
            if enchantment_names:
                items_with_enchantments += 1
                max_enchantments = max(max_enchantments, len(enchantment_names))
            for name in enchantment_names:
                enchantments[name] += 1
        elif group == "skills":
            for tier in tiers:
                skill_declared[tier] += 1
            if all(tier in tiers for tier in CANONICAL_ITEMS):
                skills["with_all_canonical"] += 1
            else:
                skills["sparse"] += 1
            if not any(tier in tiers for tier in CANONICAL_ITEMS):
                skills["missing_all_canonical"].append(template_id)
            if "Legendary" in tiers:
                skills["explicit_legendary"] += 1
                if tiers.get("Diamond") != tiers.get("Legendary"):
                    skills["legendary_differs_from_diamond"] += 1
                    skills["legendary_differs_from_diamond_ids"].append(template_id)
    return {
        "items": {
            "declared_tier_counts": dict(sorted(item_declared.items())),
            **items,
            "legendary_differs_from_diamond_ids": sorted(items["legendary_differs_from_diamond_ids"]),
            "gaps": sorted(items["gaps"]),
            "missing_diamond": sorted(items["missing_diamond"]),
        },
        "skills": {
            "declared_tier_counts": dict(sorted(skill_declared.items())),
            **skills,
            "legendary_differs_from_diamond_ids": sorted(skills["legendary_differs_from_diamond_ids"]),
            "missing_all_canonical": sorted(skills["missing_all_canonical"]),
        },
        "enchantments": {
            "names": sorted(enchantments),
            "counts": dict(sorted(enchantments.items())),
            "items_with_enchantments": items_with_enchantments,
            "max_per_item": max_enchantments,
        },
    }, item_expected, skill_expected


def check_index(index_path: Path, records, source_hash: str):
    rows = []
    duplicates = []
    mismatches = []
    missing = []
    seen = set()
    with index_path.open(encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        expected_fields = ("table", "template_id", "name", "template_type", "primary_group",
                           "related_groups", "scope", "tiers", "enchantments", "native_types",
                           "source_sha256", "routing_sha256")
        if tuple(reader.fieldnames or ()) != expected_fields:
            mismatches.append(["header", "unexpected columns"])
        for row in reader:
            rows.append(row)
            key = (row["table"], row["template_id"])
            if key in seen:
                duplicates.append([row["table"], row["template_id"]])
                continue
            seen.add(key)
            record = records.get(key)
            if record is None:
                missing.append([row["table"], row["template_id"]])
                continue
            group, template = record
            expected_table = "monsters" if group == "monsters" else "cards"
            fields = {
                "table": expected_table,
                "name": template.get("InternalName", ""),
                "template_type": template.get("$type", "TMonster"),
                "tiers": ";".join(sorted((template.get("Tiers") or {}).keys())),
                "enchantments": ";".join(sorted((template.get("Enchantments") or {}).keys())),
                "source_sha256": source_hash,
            }
            for field, expected in fields.items():
                if row[field] != expected:
                    mismatches.append([row["table"], row["template_id"], field, row[field], expected])
    known = {key for key in records if records[key][0] in ("items", "skills", "monsters")}
    extra = sorted([list(key) for key in known - seen])
    ids = [row["template_id"] for row in rows]
    result = {
        "sha256": digest(index_path),
        "rows": len(rows),
        "unique_ids": len(set(ids)),
        "duplicates": duplicates,
        "missing_from_index": extra,
        "missing_from_db": missing,
        "mismatches": mismatches,
        "by_type": dict(sorted(collections.Counter(row["template_type"] for row in rows).items())),
        "by_table": dict(sorted(collections.Counter(row["table"] for row in rows).items())),
        "g00_primary": sum(1 for row in rows if row["primary_group"] == "G00"),
    }
    return result


def check_fixture(fixture_path: Path, records):
    fixture = json.loads(fixture_path.read_text(encoding="utf-8"))
    mismatches = []
    for key, entry in sorted(fixture.items()):
        table, template_id, template = entry.get("table"), entry.get("id"), entry.get("template")
        record = records.get((table, template_id))
        if record is None:
            mismatches.append([key, "missing"])
        elif record[1] != template:
            mismatches.append([key, "template-differs"])
    return {
        "sha256": digest(fixture_path),
        "templates": len(fixture),
        "mismatches": mismatches,
    }


def missing_definitions(records):
    """按品质展开 Tiers 引用的 AbilityIds/AuraIds；缺失引用保留原始 JSON 路径。"""
    entries = []
    templates = set()
    references = set()
    kinds = collections.Counter()
    by_type = collections.Counter()
    for (table, template_id), (group, template) in sorted(records.items()):
        kind = template.get("$type", "")
        if kind not in ("TCardItem", "TCardSkill"):
            continue
        tiers = template.get("Tiers") or {}
        if not isinstance(tiers, dict):
            continue
        for tier in sorted(tiers):
            row = tiers[tier]
            if not isinstance(row, dict):
                continue
            for key, definitions in (("AbilityIds", "Abilities"), ("AuraIds", "Auras")):
                ids = row.get(key)
                if not isinstance(ids, list):
                    continue
                defined = template.get(definitions) or {}
                for index, missing_id in enumerate(ids):
                    if missing_id in defined:
                        continue
                    entries.append([table, template_id, str(template.get("InternalName", "")), tier,
                                    key, index, missing_id])
                    templates.add((table, template_id))
                    references.add((template_id, key, missing_id))
                    kinds[key] += 1
                    by_type[kind] += 1
    entries.sort(key=lambda row: (row[0], row[1], row[3], row[4], row[5], str(row[6])))
    return {
        "templates": len(templates),
        "template_ids": sorted(template_id for _, template_id in templates),
        "unique_references": len(references),
        "expanded_references": len(entries),
        "unique_missing_ids": sorted({entry[6] for entry in entries}, key=str),
        "kinds": dict(sorted(kinds.items())),
        "by_type": dict(sorted(by_type.items())),
        "entries": entries,
    }


def socket_number(value, fallback):
    """与 MonsterTemplateExtractor.Socket 相同：数字直读，字符串取末尾数字。"""
    if isinstance(value, bool):
        return fallback
    if isinstance(value, (int, float)):
        return int(value)
    text = str(value or "")
    start = len(text)
    while start > 0 and text[start - 1].isdigit():
        start -= 1
    if start == len(text):
        return fallback
    try:
        return int(text[start:])
    except ValueError:
        return fallback


def monster_layout(records):
    """166 个怪物模板的 Player/Hand/Skills 结构；只做读取审计，不执行效果。"""
    layout = []
    monster_ids = []
    health_missing = []
    missing_item_templates = []
    undeclared_item_tiers = []
    missing_item_enchantments = []
    missing_skill_templates = []
    undeclared_skill_tiers = []
    effects = []
    socket_effects = []
    item_types = collections.Counter()
    skill_types = collections.Counter()
    item_levels = collections.Counter()
    skill_levels = collections.Counter()
    total_items = total_skills = 0
    for (table, template_id), (group, template) in sorted(records.items()):
        if table != "monsters":
            continue
        name = str(template.get("InternalName", ""))
        monster_ids.append(template_id)
        player = template.get("Player") or {}
        attributes = player.get("Attributes") or {}
        health = attributes.get("HealthMax")
        if not isinstance(health, (int, float)) or isinstance(health, bool) or health <= 0:
            health_missing.append([table, template_id, name, health])
        items = ((player.get("Hand") or {}).get("Items") or [])
        for index, item in enumerate(items):
            total_items += 1
            item_id = item.get("TemplateId")
            target = records.get(("cards", item_id))
            target_type = target[1].get("$type", "") if target else "missing"
            item_types[target_type] += 1
            if target is None or target[0] != "items":
                missing_item_templates.append([table, template_id, name, index, item_id])
                continue
            tier = str(item.get("Tier") or "Bronze")
            tier_source = "declared"
            lookup = "Diamond" if tier == "Legendary" else tier
            declared = target[1].get("Tiers") or {}
            if lookup not in declared:
                tier_source = "undeclared-sparse"
                undeclared_item_tiers.append([table, template_id, name, index, item_id, tier,
                                              ";".join(sorted(declared))])
            item_levels[tier_source] += 1
            enchantment = item.get("EnchantmentType")
            if enchantment and enchantment not in (target[1].get("Enchantments") or {}):
                missing_item_enchantments.append([table, template_id, name, index, item_id, enchantment])
        skills = player.get("Skills") or []
        for index, skill in enumerate(skills):
            total_skills += 1
            skill_id = skill.get("TemplateId")
            target = records.get(("cards", skill_id))
            target_type = target[1].get("$type", "") if target else "missing"
            skill_types[target_type] += 1
            if target is None or target[0] != "skills":
                missing_skill_templates.append([table, template_id, name, index, skill_id])
                continue
            tier = str(skill.get("Tier") or "Bronze")
            tier_source = "declared"
            lookup = "Diamond" if tier == "Legendary" else tier
            declared = target[1].get("Tiers") or {}
            if lookup not in declared:
                tier_source = "undeclared-sparse"
                undeclared_skill_tiers.append([table, template_id, name, index, skill_id, tier,
                                               ";".join(sorted(declared))])
            skill_levels[tier_source] += 1
        for index, effect in enumerate(player.get("Effects") or []):
            effects.append([table, template_id, name, "Player.Effects[" + str(index) + "]",
                            str(effect.get("$type", "unknown"))])
        for index, effect in enumerate(((player.get("Socket") or {}).get("Effects") or [])):
            socket_effects.append([table, template_id, name, "Player.Socket.Effects[" + str(index) + "]",
                                   str(effect.get("$type", "unknown"))])
        layout.append([template_id, name,
                       [[item.get("TemplateId"), item.get("Tier"), item.get("EnchantmentType"),
                         socket_number(item.get("SocketId"), index)]
                        for index, item in enumerate(items)],
                       [[skill.get("TemplateId"), skill.get("Tier")] for skill in skills]])
    payload = json.dumps(layout, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    return {
        "monsters": len(monster_ids),
        "monster_ids": monster_ids,
        "health_missing": health_missing,
        "items": total_items,
        "skills": total_skills,
        "item_types": dict(sorted(item_types.items())),
        "skill_types": dict(sorted(skill_types.items())),
        "item_tier_sources": dict(sorted(item_levels.items())),
        "skill_tier_sources": dict(sorted(skill_levels.items())),
        "missing_item_templates": missing_item_templates,
        "undeclared_item_tiers": undeclared_item_tiers,
        "missing_item_enchantments": missing_item_enchantments,
        "missing_skill_templates": missing_skill_templates,
        "undeclared_skill_tiers": undeclared_skill_tiers,
        "effects": effects,
        "socket_effects": socket_effects,
        "warning_entries": len(missing_item_templates) + len(missing_skill_templates) + len(effects),
        "layout_sha256": hashlib.sha256(payload).hexdigest().upper(),
    }


def layout_digest(records, groups):
    """品质／附魔声明布局的稳定摘要；比单纯计数更能发现成员变化。"""
    rows = []
    for (table, template_id), (group, template) in sorted(records.items()):
        if group not in groups:
            continue
        rows.append([group, template_id, sorted((template.get("Tiers") or {}).keys()),
                     sorted((template.get("Enchantments") or {}).keys())])
    payload = json.dumps(rows, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(payload).hexdigest().upper()


def out_of_index_rules(db_path: Path):
    """索引外的规则行不能从“卡牌 1810 条”里消失。"""
    rows = []
    connection = rule_inventory.open_db(db_path)
    try:
        for table, template_id, template in rule_inventory.iter_rows(connection):
            kind = template.get("$type", "")
            if kind not in ("TCardSocketEffect", "TCardPlayerEffect"):
                continue
            rows.append([table, template_id, str(template.get("InternalName", "")), kind])
    finally:
        connection.close()
    rows.sort(key=lambda row: (row[3], row[0], row[1]))
    return {"count": len(rows), "by_type": dict(sorted(collections.Counter(row[3] for row in rows).items())),
            "rows": rows}


def check_raw_templates(path: Path):
    """只校验 JSONL 形状；卡牌定义可包含 TCardSocketEffect/TCardPlayerEffect。"""
    rows = 0
    errors = []
    with path.open(encoding="utf-8") as handle:
        for line in handle:
            if not line.strip():
                continue
            rows += 1
            try:
                row = json.loads(line)
            except ValueError as error:
                errors.append(["json", str(error)])
                continue
            if not all(key in row for key in ("table", "id", "template")):
                errors.append(["shape", row.get("id", "")])
    return {"sha256": digest(path), "rows": rows, "errors": errors}


def check_mapping_input(path: Path, records):
    rows = 0
    mismatches = []
    with path.open(encoding="utf-8") as handle:
        for line in handle:
            if not line.strip():
                continue
            row = json.loads(line)
            rows += 1
            record = records.get((row["table"], row["id"]))
            if record is None:
                mismatches.append([row["table"], row["id"], "missing"])
            elif record[1] != row["template"]:
                mismatches.append([row["table"], row["id"], "template-differs"])
    return {"sha256": digest(path), "rows": rows, "mismatches": mismatches}


def check_monster_report(path: Path, records):
    """核对 C# 怪物读取矩阵：逐怪物、逐手牌／技能，而不是只看总数。"""
    expected = monster_layout(records)
    expected_rows = {}
    expected_monster_reasons = {}
    for (table, template_id), (group, template) in sorted(records.items()):
        if table != "monsters":
            continue
        name = str(template.get("InternalName", ""))
        player = template.get("Player") or {}
        warnings = []
        for index, item in enumerate(((player.get("Hand") or {}).get("Items") or [])):
            item_id = item.get("TemplateId")
            target = records.get(("cards", item_id))
            tier = str(item.get("Tier") or "Bronze")
            enchantment = item.get("EnchantmentType") or "None"
            socket = str(socket_number(item.get("SocketId"), index))
            if target is None or target[0] != "items":
                tier_source = "missing"
                enchantment_status = "none" if enchantment == "None" else "unknown"
                status = "missing"
                template_name = ""
                reason = "Player.Hand.Items[" + str(index) + "].TemplateId=" + str(item_id)
                warnings.append(reason)
            else:
                template_name = str(target[1].get("InternalName", ""))
                lookup = "Diamond" if tier == "Legendary" else tier
                declared = target[1].get("Tiers") or {}
                tier_source = ("legendary-alias" if tier == "Legendary" and lookup in declared else
                               "declared" if lookup in declared else "undeclared-sparse")
                if enchantment == "None":
                    enchantment_status = "none"
                elif enchantment in (target[1].get("Enchantments") or {}):
                    enchantment_status = "declared"
                else:
                    enchantment_status = "missing"
                status = "warning" if tier_source == "undeclared-sparse" or enchantment_status == "missing" \
                    else "resolved"
                reason = ""
                if status == "warning":
                    reason = "Player.Hand.Items[" + str(index) + "] Tier=" + tier + \
                             " declared=" + ";".join(sorted(declared))
            expected_rows[(template_id, "item", str(index))] = {
                "record": "item", "monster_id": template_id, "monster_name": name,
                "index": str(index), "template_id": str(item_id or ""), "template_name": template_name,
                "kind": "item", "tier": tier, "tier_source": tier_source,
                "enchantment": enchantment, "enchantment_status": enchantment_status,
                "socket": socket, "status": status,
            }
        for index, skill in enumerate(player.get("Skills") or []):
            skill_id = skill.get("TemplateId")
            target = records.get(("cards", skill_id))
            tier = str(skill.get("Tier") or "Bronze")
            if target is None or target[0] != "skills":
                tier_source = "missing"
                status = "missing"
                template_name = ""
                reason = "Player.Skills[" + str(index) + "].TemplateId=" + str(skill_id)
                warnings.append(reason)
            else:
                template_name = str(target[1].get("InternalName", ""))
                lookup = "Diamond" if tier == "Legendary" else tier
                declared = target[1].get("Tiers") or {}
                tier_source = ("legendary-alias" if tier == "Legendary" and lookup in declared else
                               "declared" if lookup in declared else "undeclared-sparse")
                status = "warning" if tier_source == "undeclared-sparse" else "resolved"
                reason = "" if status == "resolved" else \
                    "Player.Skills[" + str(index) + "] Tier=" + tier + " declared=" + ";".join(sorted(declared))
            expected_rows[(template_id, "skill", str(index))] = {
                "record": "skill", "monster_id": template_id, "monster_name": name,
                "index": str(index), "template_id": str(skill_id or ""), "template_name": template_name,
                "kind": "skill", "tier": tier, "tier_source": tier_source,
                "enchantment": "", "enchantment_status": "", "socket": "", "status": status,
            }
        for index, effect in enumerate(player.get("Effects") or []):
            warnings.append("Player.Effects[" + str(index) + "] " + str(effect.get("$type", "unknown")))
        expected_rows[(template_id, "monster", "")] = {
            "record": "monster", "monster_id": template_id, "monster_name": name,
            "index": "", "template_id": "", "template_name": "", "kind": "monster",
            "tier": "", "tier_source": "", "enchantment": "", "enchantment_status": "",
            "socket": "", "status": "warning" if warnings else "read",
        }
        expected_monster_reasons[template_id] = warnings

    reported_rows = {}
    duplicates = []
    counts = collections.Counter()
    header = None
    with path.open(encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle, delimiter="\t")
        header = reader.fieldnames
        for row in reader:
            counts[row.get("record", "")] += 1
            key = (row.get("monster_id", ""), row.get("record", ""), row.get("index", ""))
            if key in reported_rows:
                if len(duplicates) < 20:
                    duplicates.append(list(key))
                continue
            reported_rows[key] = row

    mismatches = []
    fields = ("monster_name", "template_id", "template_name", "kind", "tier", "tier_source",
              "enchantment", "enchantment_status", "socket", "status")
    for key, expected_row in sorted(expected_rows.items()):
        reported = reported_rows.get(key)
        if reported is None:
            if len(mismatches) < 50:
                mismatches.append([key[0], key[1], key[2], "missing-row"])
            continue
        for field in fields:
            if reported.get(field, "") != expected_row[field]:
                if len(mismatches) < 50:
                    mismatches.append([key[0], key[1], key[2], field,
                                       reported.get(field, ""), expected_row[field]])
        if expected_row["status"] in ("warning", "missing") and not reported.get("reason", ""):
            if len(mismatches) < 50:
                mismatches.append([key[0], key[1], key[2], "reason", "empty", "nonempty"])
        if expected_row["status"] == "missing":
            expected_path = ("Player.Hand.Items[" if key[1] == "item" else "Player.Skills[") + key[2] + "]"
            if expected_path not in reported.get("reason", ""):
                if len(mismatches) < 50:
                    mismatches.append([key[0], key[1], key[2], "reason-path",
                                       reported.get("reason", ""), expected_path])
    unexpected_rows = []
    for key in sorted(set(reported_rows) - set(expected_rows)):
        if len(unexpected_rows) < 20:
            unexpected_rows.append(list(key))

    reported_monster_ids = {key[0] for key in reported_rows if key[1] == "monster"}
    missing_monsters = sorted(set(expected["monster_ids"]) - reported_monster_ids)
    unexpected_monsters = sorted(reported_monster_ids - set(expected["monster_ids"]))
    monster_reason_missing = []
    for template_id, warnings in sorted(expected_monster_reasons.items()):
        reported = reported_rows.get((template_id, "monster", ""))
        if reported is None:
            continue
        reason = reported.get("reason", "")
        for warning in warnings:
            # 提取路径部分，例如 Player.Hand.Items[0].TemplateId=...；
            # 类型说明可能被截断或本地化，路径不能丢。
            reason_path = warning.split(" ", 1)[0]
            if reason_path not in reason:
                monster_reason_missing.append([template_id, reason_path])
                break

    reported_missing_items = sorted([key[0], key[2], row["template_id"]]
                                    for key, row in reported_rows.items()
                                    if key[1] == "item" and row.get("status") == "missing")
    reported_missing_skills = sorted([key[0], key[2], row["template_id"]]
                                     for key, row in reported_rows.items()
                                     if key[1] == "skill" and row.get("status") == "missing")
    reported_undeclared_items = sorted([key[0], key[2], row["template_id"], row["tier"]]
                                       for key, row in reported_rows.items()
                                       if key[1] == "item" and row.get("tier_source") == "undeclared-sparse")
    reported_undeclared_skills = sorted([key[0], key[2], row["template_id"], row["tier"]]
                                        for key, row in reported_rows.items()
                                        if key[1] == "skill" and row.get("tier_source") == "undeclared-sparse")
    reported_missing_enchantments = sorted([key[0], key[2], row["template_id"], row["enchantment"]]
                                           for key, row in reported_rows.items()
                                           if key[1] == "item" and row.get("enchantment_status") == "missing")

    expected_missing_items = sorted([row[1], str(row[3]), str(row[4])]
                                    for row in expected["missing_item_templates"])
    expected_missing_skills = sorted([row[1], str(row[3]), str(row[4])]
                                     for row in expected["missing_skill_templates"])
    expected_undeclared_items = sorted([row[1], str(row[3]), str(row[4]), row[5]]
                                       for row in expected["undeclared_item_tiers"])
    expected_undeclared_skills = sorted([row[1], str(row[3]), str(row[4]), row[5]]
                                        for row in expected["undeclared_skill_tiers"])
    expected_missing_enchantments = sorted([row[1], str(row[3]), str(row[4]), row[5]]
                                           for row in expected["missing_item_enchantments"])
    layout_checks = (
        ("missing_item_templates", reported_missing_items, expected_missing_items),
        ("missing_skill_templates", reported_missing_skills, expected_missing_skills),
        ("undeclared_item_tiers", reported_undeclared_items, expected_undeclared_items),
        ("undeclared_skill_tiers", reported_undeclared_skills, expected_undeclared_skills),
        ("missing_item_enchantments", reported_missing_enchantments, expected_missing_enchantments),
    )
    layout_mismatches = []
    for name, reported, wanted in layout_checks:
        if reported != wanted:
            layout_mismatches.append([name, reported[:10], wanted[:10]])
    if header is not None and tuple(header) != MONSTER_HEADER:
        mismatches.append(["*", "header", "", "columns", header, list(MONSTER_HEADER)])
    if monster_reason_missing:
        mismatches.append(["*", "monster-reason-paths", "", "missing", monster_reason_missing[:10], ""])
    if layout_mismatches:
        mismatches.append(["*", "layout", "", "mismatch", layout_mismatches, ""])
    if duplicates:
        mismatches.append(["*", "duplicates", "", "count", len(duplicates), 0])
    return {
        "sha256": digest(path),
        "rows": {name: counts[name] for name in ("monster", "item", "skill")},
        "status": dict(sorted(collections.Counter(row.get("status", "") for row in reported_rows.values()).items())),
        "mismatches": mismatches,
        "mismatch_count": len(mismatches),
        "missing_monsters": missing_monsters,
        "unexpected_monsters": unexpected_monsters,
        "unexpected_rows": unexpected_rows,
        "duplicates": duplicates,
        "missing_item_templates": reported_missing_items,
        "missing_skill_templates": reported_missing_skills,
        "undeclared_item_tiers": reported_undeclared_items,
        "undeclared_skill_tiers": reported_undeclared_skills,
        "missing_item_enchantments": reported_missing_enchantments,
    }


def check_matrix(path: Path, records):
    by_id = {}
    for (table, template_id), (group, template) in records.items():
        by_id[template_id] = (table, group, template)
    expected_items = set()
    expected_skills = set()
    item_expected_rows = skill_expected_rows = 0
    for (table, template_id), (group, template) in records.items():
        if group == "items":
            expected_items.add(template_id)
            item_expected_rows += len(expected_keys(template, False)[0])
        elif group == "skills":
            expected_skills.add(template_id)
            skill_expected_rows += len(expected_keys(template, True)[0])

    actual = collections.defaultdict(set)
    statuses = collections.Counter()
    tier_sources = collections.Counter()
    scenarios = collections.Counter()
    side_status = collections.defaultdict(collections.Counter)
    scenario_status = collections.defaultdict(collections.Counter)
    enchantment_statuses = collections.Counter()
    live_statuses = collections.Counter()
    blocked = collections.defaultdict(set)
    warned = collections.defaultdict(set)
    unenchanted_blocked = set()
    ids = {"item": set(), "skill": set()}
    duplicates = []
    duplicate_rows = 0
    row_count = 0
    enchantment_warning_rows = live_attribute_warning_rows = 0
    errors = collections.Counter()
    examples = []

    def note(kind, *values):
        errors[kind] += 1
        if len(examples) < 20:
            examples.append([kind] + [str(value) for value in values])

    with path.open(encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle, delimiter="\t")
        if tuple(reader.fieldnames or ()) != MATRIX_HEADER:
            note("header", reader.fieldnames)
        for row in reader:
            row_count += 1
            kind, template_id = row["kind"], row["id"]
            if kind not in ("item", "skill"):
                note("kind", kind, template_id)
                continue
            record = by_id.get(template_id)
            if record is None:
                note("unknown-template", kind, template_id)
            else:
                table, group, template = record
                expected_group = "items" if kind == "item" else "skills"
                if group != expected_group:
                    note("kind-table", kind, template_id, group)
                if row["table"] != table:
                    note("table", kind, template_id, row["table"], table)
                expected_name = str(template.get("InternalName", "")).replace("\t", " ").replace("\r", " ").replace("\n", " ")
                if row["name"] != expected_name:
                    note("name", kind, template_id, row["name"], expected_name)
            ids[kind].add(template_id)
            cell = (kind, template_id, row["tier"], row["tier_source"], row["enchantment"],
                    row["side"], row["scenario"])
            key = cell[2:]
            if key in actual[(kind, template_id)]:
                duplicate_rows += 1
                if len(duplicates) < 20:
                    duplicates.append(list(cell))
            actual[(kind, template_id)].add(key)
            statuses[(kind, row["status"])] += 1
            tier_sources[row["tier_source"]] += 1
            scenarios[row["scenario"]] += 1
            side_status[row["side"]][row["status"]] += 1
            scenario_status[row["scenario"]][row["status"]] += 1
            enchantment_statuses[row["enchantment_status"]] += 1
            live_statuses[row["live_attribute_status"]] += 1
            if row["enchantment_warning"]:
                enchantment_warning_rows += 1
                if row["enchantment"] == "None":
                    note("warning-without-enchantment", template_id)
            if row["live_attribute_warning"]:
                live_attribute_warning_rows += 1
            if row["status"] in ("unsupported", "exception"):
                blocked[kind].add(template_id)
                if kind == "item" and row["enchantment"] == "None":
                    unenchanted_blocked.add(template_id)
            if row["status"] in ("mapped-warning", "warning"):
                warned[kind].add(template_id)
            enchantment_status = row["enchantment_status"]
            if row["enchantment"] == "None":
                if enchantment_status != "none":
                    note("enchantment-none-status", template_id, enchantment_status)
            elif enchantment_status not in ("applied", "warning", "blocked-base"):
                note("enchantment-status", template_id, row["enchantment"], enchantment_status)
            if enchantment_status == "warning" and not row["enchantment_warning"]:
                note("enchantment-warning-dropped", template_id, row["tier"], row["side"], row["scenario"])
            if enchantment_status == "blocked-base" and row["status"] != "unsupported":
                note("enchantment-blocked-base-status", template_id, row["status"])
            if enchantment_status == "applied" and row["status"] not in ("mapped", "mapped-warning"):
                note("enchantment-applied-status", template_id, row["status"])
            if row["attributes_live"] != ("true" if row["scenario"] != "template" else "false"):
                note("attributes-live", template_id, row["scenario"])
            if row["scenario"] == "template" and row["live_attribute_status"] != "template":
                note("live-status-template", template_id, row["live_attribute_status"])
            if row["scenario"] != "template" and row["live_attribute_status"] == "template":
                note("live-status-missing", template_id, row["scenario"])
            if row["scenario"] == "mixed":
                if row["player_hero"] != "Mak" or row["enemy_hero"] != "Dooley":
                    note("mixed-heroes", template_id, row["player_hero"], row["enemy_hero"])
            elif row["player_hero"] != "Dooley" or row["enemy_hero"] != "Dooley":
                note("fixed-heroes", template_id, row["player_hero"], row["enemy_hero"])
            if row["tier_source"] == "inactive-sparse" and row["status"] != "inactive":
                note("sparse-tier-status", template_id, row["tier"], row["status"])
            if row["tier_source"] == "legendary-alias" and row["tier"] != "Legendary":
                note("legendary-alias-tier", template_id, row["tier"])
            if row["tier"] == "Legendary" and row["tier_source"] not in ("legendary-alias", "declared"):
                note("legendary-source", template_id, row["tier_source"])
            if row["side"] not in SIDES or row["scenario"] not in SCENARIOS:
                note("side-scenario", template_id, row["side"], row["scenario"])

    missing_templates = []
    extra_templates = []
    missing_rows = extra_rows = 0
    missing_examples = []
    expected_total = 0
    for kind, group in (("item", "items"), ("skill", "skills")):
        expected_ids = expected_items if kind == "item" else expected_skills
        actual_ids = ids[kind]
        for template_id in sorted(expected_ids - actual_ids):
            missing_templates.append([kind, template_id])
            missing_examples.append([kind, template_id, "all"])
        for template_id in sorted(actual_ids - expected_ids):
            extra_templates.append([kind, template_id])
        for (table, template_id), (record_group, template) in records.items():
            if record_group != group:
                continue
            expected = set(expected_keys(template, kind == "skill")[0])
            expected_total += len(expected)
            found = actual.get((kind, template_id), set())
            missing = expected - found
            extra = found - expected
            missing_rows += len(missing)
            extra_rows += len(extra)
            for cell in sorted(missing)[:2]:
                missing_examples.append([kind, template_id] + list(cell))
            for cell in sorted(extra)[:2]:
                note("extra-combination", kind, template_id, *cell)
    if row_count != expected_total:
        note("row-count", row_count, expected_total)

    return {
        "sha256": digest(path),
        "rows": row_count,
        "expected_rows": expected_total,
        "templates": {"item": len(ids["item"]), "skill": len(ids["skill"])},
        "status": {"item": {status: count for (kind, status), count in sorted(statuses.items()) if kind == "item"},
                   "skill": {status: count for (kind, status), count in sorted(statuses.items()) if kind == "skill"}},
        "status_by_side": {side: dict(sorted(side_status[side].items())) for side in SIDES},
        "status_by_scenario": {scenario: dict(sorted(scenario_status[scenario].items())) for scenario in SCENARIOS},
        "tier_source": dict(sorted(tier_sources.items())),
        "scenario": dict(sorted(scenarios.items())),
        "enchantment_status": dict(sorted(enchantment_statuses.items())),
        "live_attribute_status": dict(sorted(live_statuses.items())),
        "blocked_templates": {"item": len(blocked["item"]), "skill": len(blocked["skill"])},
        "blocked_ids": {"item": sorted(blocked["item"]), "skill": sorted(blocked["skill"])},
        "warned_templates": {"item": len(warned["item"]), "skill": len(warned["skill"])},
        "warned_ids": {"item": sorted(warned["item"]), "skill": sorted(warned["skill"])},
        "unenchanted_blocked_items": len(unenchanted_blocked),
        "unenchanted_blocked_ids": sorted(unenchanted_blocked),
        "enchantment_warning_rows": enchantment_warning_rows,
        "live_attribute_warning_rows": live_attribute_warning_rows,
        "duplicates": duplicates[:20],
        "duplicate_count": duplicate_rows,
        "missing_templates": missing_templates[:20],
        "missing_template_count": len(missing_templates),
        "extra_templates": extra_templates[:20],
        "extra_template_count": len(extra_templates),
        "missing_row_count": missing_rows,
        "extra_row_count": extra_rows,
        "missing_examples": missing_examples[:20],
        "errors": dict(sorted(errors.items())),
        "error_examples": examples,
    }


def wild_boar_check(records):
    item = records.get(("cards", WILD_BOAR_ITEM))
    monster = records.get(("monsters", WILD_BOAR_MONSTER))
    countersrike = records.get(("cards", COUNTERSTRIKE))
    result = {
        "item_found": item is not None,
        "monster_found": monster is not None,
        "counterstrike_found": countersrike is not None,
        "item_name": item[1].get("InternalName") if item else None,
        "monster_name": monster[1].get("InternalName") if monster else None,
        "counterstrike_type": countersrike[1].get("$type") if countersrike else None,
        "distinct": item is not None and monster is not None and WILD_BOAR_ITEM != WILD_BOAR_MONSTER,
    }
    return result


def build(args):
    db_path = rule_inventory.resolve_db(args.db)
    source_hash = digest(db_path)
    db_file, holder = rule_inventory.materialize(db_path)
    try:
        records, counts, duplicates = load_records(db_file)
        facts, _, _ = tier_facts(records)
        record_ids = [template_id for (_, template_id) in records]
        duplicate_ids = sorted(template_id for template_id, count in
                                collections.Counter(record_ids).items() if count > 1)
        index = check_index(ROOT / args.index, records, source_hash)
        fixture = check_fixture(ROOT / args.fixture, records)
        mapping_input = check_mapping_input(ROOT / args.mapping_input, records) \
            if (ROOT / args.mapping_input).exists() else {"missing": True}
        monster_input = check_mapping_input(ROOT / args.monster_input, records) \
            if (ROOT / args.monster_input).exists() else {"missing": True}
        monster_cards = check_raw_templates(ROOT / args.monster_cards) \
            if (ROOT / args.monster_cards).exists() else {"missing": True}
        matrix = check_matrix(ROOT / args.matrix, records) \
            if (ROOT / args.matrix).exists() else {"missing": True}
        monster_report = check_monster_report(ROOT / args.monster_report, records) \
            if (ROOT / args.monster_report).exists() else {"missing": True}
        definitions = missing_definitions(records)
        monsters = monster_layout(records)
        out_of_index = out_of_index_rules(db_file)
        baseline = {
            "schema": 2,
            "database": {"sha256": source_hash, "items": counts["items"],
                         "skills": counts["skills"], "monsters": counts["monsters"],
                         "unique_ids": len(set(record_ids)), "duplicate_keys": duplicates,
                         "duplicate_ids": duplicate_ids},
            "index": index,
            "tiers": facts,
            "tier_layout": {
                "items_sha256": layout_digest(records, ("items",)),
                "skills_sha256": layout_digest(records, ("skills",)),
                "all_sha256": layout_digest(records, ("items", "skills")),
            },
            "missing_definitions": definitions,
            "monsters": {"layout": monsters, "input": monster_input, "cards": monster_cards,
                         "report": monster_report},
            "out_of_index": out_of_index,
            "wild_boar": wild_boar_check(records),
            "fixture": fixture,
            "mapping_input": mapping_input,
            "matrix": matrix,
            "source_sha256": {path: digest(ROOT / path) for path in SOURCE_FILES},
        }
    finally:
        if holder is not None:
            holder.cleanup()

    failures = []
    if counts["items"] != 1146 or counts["skills"] != 498 or counts["monsters"] != 166:
        failures.append(f"数据数量变化：{dict(counts)}")
    if duplicates:
        failures.append(f"数据库重复模板键：{len(duplicates)}")
    if duplicate_ids:
        failures.append(f"数据库模板 ID 跨表重复：{len(duplicate_ids)}")
    for name, part in (("index", index), ("fixture", fixture), ("mapping_input", mapping_input),
                       ("monster_input", monster_input), ("monster_cards", monster_cards),
                       ("matrix", matrix), ("monster_report", monster_report)):
        if part.get("missing"):
            failures.append(f"{name} 文件缺失")
        elif part.get("mismatches"):
            failures.append(f"{name} 模板不一致：{len(part['mismatches'])}")
        elif part.get("errors"):
            failures.append(f"{name} 逐行解析失败：{len(part['errors'])}")
    if definitions["expanded_references"] != len(definitions["entries"]):
        failures.append("缺定义展开数与明细数量不一致")
    if definitions["unique_references"] != len({(entry[1], entry[4], entry[6])
                                                for entry in definitions["entries"]}):
        failures.append("缺定义唯一引用数与明细不一致")
    if source_hash == KNOWN_DB_SHA256:
        actual = (definitions["templates"], definitions["unique_references"],
                  definitions["expanded_references"])
        if actual != KNOWN_MISSING_DEFINITIONS:
            failures.append(f"缺定义基线与文档记录不一致：{actual} != {KNOWN_MISSING_DEFINITIONS}")
        for field, expected in KNOWN_MONSTER_GAPS.items():
            if len(monsters[field]) != expected:
                failures.append(f"怪物读取基线变化：{field}={len(monsters[field])}，预期 {expected}")
        if out_of_index["by_type"] != KNOWN_OUT_OF_INDEX:
            failures.append(f"索引外规则基线变化：{out_of_index['by_type']}")
    if index["rows"] != 1810 or index["unique_ids"] != 1810:
        failures.append(f"索引数量异常：rows={index['rows']} unique={index['unique_ids']}")
    if index["duplicates"] or index["missing_from_db"] or index["missing_from_index"]:
        failures.append("索引存在重复、缺失或多余项")
    if index["mismatches"]:
        failures.append(f"索引字段不一致：{len(index['mismatches'])}")
    if index["by_type"] != {"TCardItem": 1146, "TCardSkill": 498, "TMonster": 166}:
        failures.append(f"索引类型计数异常：{index['by_type']}")
    if index["g00_primary"] != 166:
        failures.append(f"G00 主责数量异常：{index['g00_primary']}")
    if facts["items"]["missing_diamond"] or facts["items"]["gaps"]:
        failures.append("物品品质不连续或缺少 Diamond")
    if facts["items"]["with_diamond"] != 1146:
        failures.append("并非所有物品都定义 Diamond")
    if facts["skills"]["missing_all_canonical"]:
        failures.append("存在没有任何标准品质的技能")
    if not all(baseline["wild_boar"].get(key) for key in ("item_found", "monster_found",
                                                          "counterstrike_found", "distinct")):
        failures.append("G00 种子模板缺失或同名物品／怪物混淆")
    if not matrix.get("missing"):
        if matrix["missing_template_count"] or matrix["extra_template_count"]:
            failures.append("矩阵模板集合与数据库不一致")
        if matrix["missing_row_count"] or matrix["extra_row_count"] or matrix["duplicate_count"]:
            failures.append("矩阵组合存在缺失、多余或重复")
        if matrix["rows"] != matrix["expected_rows"]:
            failures.append("矩阵行数与期望不一致")
        if matrix["errors"]:
            failures.append(f"矩阵逐行校验失败：{matrix['errors']}")
        if matrix["blocked_templates"]["item"] == 0 and matrix["blocked_templates"]["skill"] == 0:
            failures.append("矩阵未保留任何受阻模板计数")
    if monsters["health_missing"]:
        failures.append(f"怪物缺少有效生命上限：{len(monsters['health_missing'])}")
    if not monster_report.get("missing"):
        if monster_report["mismatch_count"]:
            failures.append(f"怪物读取矩阵有 {monster_report['mismatch_count']} 处不一致")
        if monster_report["missing_monsters"] or monster_report["unexpected_monsters"]:
            failures.append("怪物读取矩阵模板集合与数据库不一致")
        if monster_report["unexpected_rows"] or monster_report["duplicates"]:
            failures.append("怪物读取矩阵存在多余或重复行")
        if monster_report["rows"] != {"monster": monsters["monsters"], "item": monsters["items"],
                                      "skill": monsters["skills"]}:
            failures.append(f"怪物读取矩阵行数与数据库不一致：{monster_report['rows']}")
    return baseline, failures


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--db")
    parser.add_argument("--index", default="docs/native-rule-task-index.csv")
    parser.add_argument("--fixture", default="tools/actionaxis_plugin/tests/fixtures/native_g00_templates.json")
    parser.add_argument("--mapping-input", default="tools/actionaxis_plugin/build/native_mapping_templates.jsonl")
    parser.add_argument("--matrix", default="tools/actionaxis_plugin/build/native_mapping_report.tsv")
    parser.add_argument("--monster-input", default="tools/actionaxis_plugin/build/native_monsters.jsonl")
    parser.add_argument("--monster-cards", default="tools/actionaxis_plugin/build/native_monster_cards.jsonl")
    parser.add_argument("--monster-report", default="tools/actionaxis_plugin/build/native_monster_report.tsv")
    parser.add_argument("--output", default="data/g00_baseline.json")
    parser.add_argument("--check", action="store_true", help="与已冻结的基线逐字段比较")
    args = parser.parse_args()
    baseline, failures = build(args)
    target = ROOT / args.output
    if args.check:
        if not target.exists():
            failures.append(f"基线不存在：{args.output}")
        else:
            frozen = json.loads(target.read_text(encoding="utf-8"))
            if frozen != baseline:
                failures.append("当前结果与冻结基线不一致")
        print(f"基线核对：{'FAIL' if failures else 'OK'}")
    else:
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(baseline, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
                          encoding="utf-8")
        print(f"已写入基线：{args.output}")
    database = baseline["database"]
    print(f"数据库：物品 {database['items']}／技能 {database['skills']}／怪物 {database['monsters']}；"
          f"SHA256={database['sha256']}")
    print(f"索引：{baseline['index']['rows']} 行；G00 主责 {baseline['index']['g00_primary']}；"
          f"SHA256={baseline['index']['sha256']}")
    matrix = baseline["matrix"]
    if not matrix.get("missing"):
        print(f"矩阵：{matrix['rows']} 行；物品 {matrix['templates']['item']}／技能 {matrix['templates']['skill']}；"
              f"受阻 {matrix['blocked_templates']['item']}/{matrix['blocked_templates']['skill']}；"
              f"附魔告警行 {matrix['enchantment_warning_rows']}；"
              f"实时属性告警行 {matrix['live_attribute_warning_rows']}")
        print("矩阵状态：" + json.dumps(matrix["status"], ensure_ascii=False, sort_keys=True))
        print("矩阵双方状态：" + json.dumps(matrix["status_by_side"], ensure_ascii=False, sort_keys=True))
        print(f"矩阵 SHA256={matrix['sha256']}")
    definitions = baseline["missing_definitions"]
    print(f"缺定义：模板 {definitions['templates']}／唯一引用 {definitions['unique_references']}／"
          f"按品质展开 {definitions['expanded_references']}；"
          f"缺失 ID {json.dumps(definitions['unique_missing_ids'], ensure_ascii=False)}")
    monster_layout_facts = baseline["monsters"]["layout"]
    print(f"怪物读取：模板 {monster_layout_facts['monsters']}；手牌 {monster_layout_facts['items']}／"
          f"技能 {monster_layout_facts['skills']}；缺手牌模板 {len(monster_layout_facts['missing_item_templates'])}；"
          f"手牌未声明品质 {len(monster_layout_facts['undeclared_item_tiers'])}；"
          f"技能未声明品质 {len(monster_layout_facts['undeclared_skill_tiers'])}；"
          f"缺技能模板 {len(monster_layout_facts['missing_skill_templates'])}；"
          f"插槽效果 {len(monster_layout_facts['socket_effects'])}")
    report = baseline["monsters"]["report"]
    if not report.get("missing"):
        print(f"怪物读取矩阵：{report['rows']}；不一致 {report['mismatch_count']}；"
              f"SHA256={report['sha256']}")
    print("索引外规则：" + json.dumps(baseline["out_of_index"]["by_type"], ensure_ascii=False, sort_keys=True))
    if failures:
        for failure in failures:
            print("FAIL " + failure)
        return 1
    print("G00 审计通过")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
