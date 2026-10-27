"""Inventory every native rule type used by items, skills, enchantments and monsters.

The C# engine maps native templates ($type driven) into the simulator. This
script scans the game's GameData.db and reports every Trigger / Action / Aura /
Prerequisite / Condition / Value / Duration / Target type with usage counts, so
unsupported rules can be tracked instead of discovered one card at a time.

Usage:
    python tools/rule_inventory.py --db <GameData.db> [--out data/rule_inventory.json]
    python tools/rule_inventory.py --db <GameData.db> --markdown docs/rule-coverage.md
    python tools/rule_inventory.py --schema
"""
from __future__ import annotations

import argparse
import collections
import csv
import hashlib
import json
import sqlite3
import sys
import tempfile
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
GAME_DB = Path(
    "D:/Program Files (x86)/Steam/steamapps/common/The Bazaar"
    "/TheBazaar_Data/StreamingAssets/GameData.db.zip"
)
FALLBACK_DB = (
    ROOT
    / "vendor"
    / "BazaarPlusPlus"
    / "bazaarplusplus-mod"
    / "tests"
    / "CombatImpact.Corpus"
    / "corpus"
    / "GameData.cards.db"
)

# The nearest parent key decides what a $type node means.
ROLE_BY_KEY = {
    "Trigger": "trigger",
    "Triggers": "trigger",
    "Action": "action",
    "Prerequisites": "prerequisite",
    "Conditions": "condition",
    "Duration": "duration",
    "ReferenceValue": "value",
    "Value": "value",
    "TargetCount": "value",
    "Amount": "value",
    "Modifier": "value",
    "Target": "target",
}

# Table rows that are not card templates must not pollute item counts.
SOURCE_ORDER = ("items", "skills", "enchantments", "monsters", "other")


def resolve_db(explicit: str | None) -> Path:
    if explicit:
        return Path(explicit)
    for candidate in (GAME_DB, FALLBACK_DB):
        if candidate.exists():
            return candidate
    raise SystemExit("找不到 GameData.db，请用 --db 指定")


def materialize(db_path: Path) -> tuple[Path, tempfile.TemporaryDirectory | None]:
    """游戏只保留 zip 时，解压到临时目录后扫描。"""
    if db_path.suffix.lower() != ".zip":
        return db_path, None
    holder = tempfile.TemporaryDirectory(prefix="bazaar_rules_")
    with zipfile.ZipFile(db_path) as archive:
        member = next((n for n in archive.namelist() if n.lower().endswith(".db")), None)
        if member is None:
            holder.cleanup()
            raise SystemExit(f"zip 中没有数据库：{db_path}")
        archive.extract(member, holder.name)
        return Path(holder.name) / member, holder


def open_db(path: Path) -> sqlite3.Connection:
    return sqlite3.connect(f"file:{path.as_posix()}?mode=ro", uri=True)


def tables(con: sqlite3.Connection) -> list[str]:
    return [
        row[0]
        for row in con.execute("select name from sqlite_master where type='table'")
    ]


def classify_table(name: str, sample: dict) -> str:
    low = name.lower()
    kind = str(sample.get("$type", ""))
    if "enchant" in low or kind.startswith("TEnchant"):
        return "enchantments"
    if "monster" in low or kind == "TMonster":
        return "monsters"
    if "skill" in low or kind == "TCardSkill":
        return "skills"
    if "card" in low or "item" in low or kind in ("TCardItem", "TCardBase"):
        return "items"
    return "other"


def iter_rows(con: sqlite3.Connection):
    """Yield (table, template_json) for every (Id, Data) shaped table."""
    for name in tables(con):
        columns = [row[1] for row in con.execute(f'pragma table_info("{name}")')]
        if "Data" not in columns:
            continue
        try:
            rows = con.execute(f'select Id, Data from "{name}"').fetchall()
        except sqlite3.Error:
            continue
        for row_id, data in rows:
            if not data:
                continue
            try:
                parsed = json.loads(data)
            except (TypeError, ValueError):
                continue
            if isinstance(parsed, dict):
                yield name, str(row_id), parsed


def role_of(kind: str, parent_role: str) -> str:
    """$type 前缀比父字段更可靠地决定规则角色。"""
    if kind.startswith("TTrigger"):
        return "trigger"
    if kind.startswith("TAura"):
        return "aura_action"
    if kind.startswith("TAction"):
        return "action"
    if kind.startswith("TPrerequisite"):
        return "prerequisite"
    if kind.startswith("TCardConditional") or kind.startswith("TConditional"):
        return "condition"
    if kind.startswith("TReference") or kind.startswith("TFixedValue"):
        return "value"
    if kind.startswith("TDeterminant") or kind.endswith("Duration"):
        return "duration"
    if kind.startswith("TTarget"):
        return "target"
    return parent_role


TEMPLATE_TYPES = {
    "TCardItem",
    "TCardSkill",
    "TMonster",
    "TCardMusicNoteSocketEffect",
    "TCardEncounterEvent",
}


def walk(node, role: str, source: str, table: str, counts, locations):
    if isinstance(node, list):
        for child in node:
            walk(child, role, source, table, counts, locations)
        return
    if not isinstance(node, dict):
        return
    kind = node.get("$type")
    if isinstance(kind, str) and kind:
        next_role = role_of(kind, role)
        if kind not in TEMPLATE_TYPES and next_role != "template":
            counts[(source, next_role, kind)] += 1
            if next_role in ("trigger", "action", "aura_action", "prerequisite"):
                locations.setdefault((source, next_role, kind), []).append(table)
        role = next_role
    for key, child in node.items():
        child_role = ROLE_BY_KEY.get(key, role)
        child_source = "enchantments" if key == "Enchantments" and isinstance(child, dict) else source
        walk(child, child_role, child_source, table, counts, locations)


def scan(db_path: Path):
    con = open_db(db_path)
    try:
        counts = collections.Counter()
        locations: dict[tuple[str, str, str], list[str]] = {}
        totals = collections.Counter()
        for table, row_id, data in iter_rows(con):
            source = classify_table(table, data)
            totals[source] += 1
            walk(data, "template", source, table, counts, locations)
        return counts, locations, totals
    finally:
        con.close()


def supported_tokens() -> set[str]:
    """$types that already appear in the engine mapping code.

    Presence is not proof of semantic support; the coverage document keeps the
    verified status. This only narrows the triage list.
    """
    tokens: set[str] = set()
    for path in (ROOT / "tools" / "actionaxis_plugin" / "engine").glob("*.cs"):
        text = path.read_text(encoding="utf-8", errors="ignore")
        for name in (
            "TTrigger",
            "TAction",
            "TAura",
            "TReference",
            "TPrerequisite",
            "TCardConditional",
            "TDeterminant",
            "TTarget",
            "TEnchant",
            "TFixed",
            "TRange",
            "TCombat",
            "TPlayer",
        ):
            start = 0
            while True:
                index = text.find('"' + name, start)
                if index < 0:
                    break
                end = text.find('"', index + 1)
                tokens.add(text[index + 1 : end])
                start = end
    return tokens


def write_json(counts, totals, out: Path) -> None:
    payload = {
        "totals": dict(totals),
        "rules": [
            {"source": source, "role": role, "type": kind, "count": count}
            for (source, role, kind), count in sorted(counts.items())
        ],
    }
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")


def markdown(counts, totals, known: set[str]) -> str:
    lines = ["# 原生规则类型清单", ""]
    lines.append("用途：列出 `GameData.db` 中物品、技能、附魔、怪物实际使用的全部 `$type`，")
    lines.append("作为「支持所有规则」的覆盖率基线。由 `python tools/rule_inventory.py` 生成。")
    lines.append("")
    lines.append("## 模板数量")
    lines.append("")
    lines.append("| 来源 | 模板数 |")
    lines.append("| --- | --- |")
    for source in SOURCE_ORDER:
        if totals.get(source):
            lines.append(f"| {source} | {totals[source]} |")
    lines.append("")
    for role, title in (
        ("trigger", "触发器"),
        ("action", "动作"),
        ("aura_action", "光环动作"),
        ("prerequisite", "前提条件"),
        ("condition", "卡牌条件"),
        ("value", "数值引用"),
        ("duration", "持续时间"),
        ("target", "目标"),
    ):
        rows = [
            (source, kind, count)
            for (source, kind_role, kind), count in counts.items()
            if kind_role == role
        ]
        if not rows:
            continue
        lines.append(f"## {title}（{role}）")
        lines.append("")
        lines.append("| 类型 | 物品 | 技能 | 附魔 | 怪物 | 映射代码出现 |")
        lines.append("| --- | --- | --- | --- | --- | --- |")
        by_type = collections.defaultdict(lambda: collections.defaultdict(int))
        for source, kind, count in rows:
            by_type[kind][source] += count
        for kind in sorted(by_type, key=lambda k: (-sum(by_type[k].values()), k)):
            cells = [str(by_type[kind].get(s, 0)) for s in SOURCE_ORDER[:4]]
            mark = "是" if kind in known else ""
            lines.append(f"| `{kind}` | " + " | ".join(cells) + f" | {mark} |")
        lines.append("")
    return "\n".join(lines)


def print_schema(db_path: Path) -> None:
    con = open_db(db_path)
    try:
        objects = con.execute("select type, name from sqlite_master").fetchall()
        for kind, name in objects:
            if kind not in ("table", "view"):
                continue
            columns = [row[1] for row in con.execute(f'pragma table_info("{name}")')]
            count = con.execute(f'select count(*) from "{name}"').fetchone()[0]
            print(f"{kind} {name} ({count}): {', '.join(columns)}")
        if "cards" in {name for _, name in objects}:
            row = con.execute("select Id, Data from cards limit 1").fetchone()
            data = row[1]
            size = len(data) if data is not None else 0
            head = repr(data[:160]) if isinstance(data, (bytes, bytearray)) else repr(str(data)[:160])
            print(f"sample cards row: id={row[0]} size={size} head={head}")
    finally:
        con.close()


def find_node(node, kind: str):
    if isinstance(node, list):
        for child in node:
            found = find_node(child, kind)
            if found is not None:
                return found
        return None
    if not isinstance(node, dict):
        return None
    if node.get("$type") == kind:
        return node
    for child in node.values():
        found = find_node(child, kind)
        if found is not None:
            return found
    return None


def print_sample(db_path: Path, kind: str) -> None:
    con = open_db(db_path)
    try:
        shown = 0
        for table, row_id, data in iter_rows(con):
            found = find_node(data, kind)
            if found is None:
                continue
            print(f"# {table} {row_id} {data.get('InternalName', '')}")
            print(json.dumps(found, ensure_ascii=False, indent=2))
            print()
            shown += 1
            if shown >= 3:
                break
        if shown == 0:
            print(f"没有找到 {kind}")
    finally:
        con.close()


def export_fixtures(db_path: Path, names: list[str], target: Path) -> None:
    """按原始名称导出完整模板，不裁掉前提、附魔或能力字段。"""
    con = open_db(db_path)
    try:
        found = {}
        for table, row_id, data in iter_rows(con):
            name = data.get("InternalName")
            key = f"{table}:{name}" if f"{table}:{name}" in names else name
            if key not in names:
                continue
            if key in found:
                raise SystemExit(f"名称不唯一，不能选错测试模板：{name}")
            found[key] = {"table": table, "id": row_id, "template": data}
        missing = set(names) - set(found)
        if missing:
            raise SystemExit(f"测试模板缺失：{sorted(missing)}")
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(found, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(f"已导出 {len(found)} 个完整原生模板：{target}")
    finally:
        con.close()


def export_mapping_input(db_path: Path, target: Path) -> None:
    """逐行导出完整物品与技能，供 C# 真实映射入口检查所有品质／附魔。"""
    con = open_db(db_path)
    try:
        count = 0
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open("w", encoding="utf-8") as output:
            for table, row_id, template in iter_rows(con):
                if template.get("$type") not in ("TCardItem", "TCardSkill"):
                    continue
                output.write(json.dumps({"table": table, "id": row_id, "template": template}, ensure_ascii=False) + "\n")
                count += 1
        print(f"已导出 {count} 个完整物品／技能模板：{target}")
    finally:
        con.close()


def export_monster_cards(db_path: Path, target: Path) -> None:
    """怪物读取所需的卡牌映射：物品、技能、插槽/玩家效果定义。"""
    con = open_db(db_path)
    try:
        count = 0
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open("w", encoding="utf-8") as output:
            for table, row_id, template in iter_rows(con):
                if template.get("$type") not in ("TCardItem", "TCardSkill", "TCardSocketEffect", "TCardPlayerEffect"):
                    continue
                output.write(json.dumps({"table": table, "id": row_id, "template": template},
                                        ensure_ascii=False) + "\n")
                count += 1
        print(f"已导出 {count} 个怪物读取卡牌定义：{target}")
    finally:
        con.close()


def export_monsters(db_path: Path, target: Path) -> None:
    """逐行导出完整怪物模板；读取审计不执行 Ability/Aura。"""
    con = open_db(db_path)
    try:
        count = 0
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open("w", encoding="utf-8") as output:
            for table, row_id, template in iter_rows(con):
                if "monster" not in table.lower() and template.get("$type") != "TMonster":
                    continue
                output.write(json.dumps({"table": table, "id": row_id, "template": template},
                                        ensure_ascii=False) + "\n")
                count += 1
        print(f"已导出 {count} 个完整怪物模板：{target}")
    finally:
        con.close()


def summarize_mapping(path: Path) -> None:
    """汇总入口矩阵；分开硬拒绝、仅告警、无附魔与各入口来源，避免把组合数当卡数。"""
    groups = collections.defaultdict(set)
    examples = collections.defaultdict(set)
    clean = collections.defaultdict(set)
    blocked = collections.defaultdict(set)
    partial = collections.defaultdict(set)
    templates = collections.defaultdict(set)
    statuses = collections.Counter()
    tiers = collections.Counter()
    scenarios = collections.Counter()
    rows = enchantment_warning_rows = live_attribute_warning_rows = 0
    with path.open(encoding="utf-8-sig", newline="") as handle:
        for row in csv.DictReader(handle, delimiter="\t"):
            key, status = row["kind"], row["status"]
            rows += 1
            templates[key].add(row["id"])
            statuses[(key, status)] += 1
            tiers[row.get("tier_source", "")] += 1
            scenarios[row.get("scenario", "")] += 1
            if row.get("enchantment_warning"):
                enchantment_warning_rows += 1
            if row.get("live_attribute_warning"):
                live_attribute_warning_rows += 1
            if row["enchantment"] == "None":
                clean[key].add(row["id"])
                if status in ("unsupported", "exception"):
                    blocked[key].add(row["id"])
            if status == "mapped-warning":
                partial[key].add(row["id"])
            if status not in ("unsupported", "exception"):
                continue
            reason = row["reason"].split(" | ")[0]
            if "（" in reason:
                reason = reason.split("（", 1)[1].rsplit("）", 1)[0]
            label = key + ": " + reason
            groups[label].add(row["id"])
            if len(examples[label]) < 3:
                examples[label].add(row["name"] + "/" + row["enchantment"])
    print(f"矩阵行 {rows}；模板 " + "／".join(f"{kind} {len(ids)}" for kind, ids in sorted(templates.items())))
    for kind in sorted(templates):
        counts = ", ".join(f"{status}={count}" for (status_kind, status), count in sorted(statuses.items())
                           if status_kind == kind)
        print(f"{kind} 状态：{counts}")
    for kind in sorted(clean):
        print(f"{kind} 无附魔模板 {len(clean[kind])}；至少一个品质／阵营／场景硬拒绝 {len(blocked[kind])}；"
              f"至少一个组合仅告警 {len(partial[kind])}")
    print("tier_source：" + ", ".join(f"{name or '空'}={count}" for name, count in sorted(tiers.items())))
    print("scenario：" + ", ".join(f"{name or '空'}={count}" for name, count in sorted(scenarios.items())))
    print(f"附魔告警行={enchantment_warning_rows}；实时属性告警行={live_attribute_warning_rows}")
    for reason in sorted(groups, key=lambda key: (-len(groups[key]), key))[:30]:
        print(f"{len(groups[reason])} 个模板 | {reason} | {', '.join(sorted(examples[reason]))}")


def work_features(node) -> dict[str, set[str]]:
    """只读取规则结构字段，不根据 tooltip 文本臆测支持状态。"""
    found = {key: set() for key in ("types", "attributes", "sections")}

    def visit(value):
        if isinstance(value, list):
            for child in value:
                visit(child)
        elif isinstance(value, dict):
            for field, group in (("$type", "types"), ("AttributeType", "attributes"),
                                 ("Attribute", "attributes"), ("TargetSection", "sections")):
                if isinstance(value.get(field), str):
                    found[group].add(value[field])
            for child in value.values():
                visit(child)

    visit(node)
    return found


def export_work_index(db_path: Path, source_path: Path, target: Path) -> None:
    """工作文档是唯一派单配置源；CSV 不存完成状态，也不代表语义支持。"""
    document = (ROOT / "docs/native-rule-coverage.md").read_text(encoding="utf-8")
    start, end = "<!-- RULE_WORK_ROUTING", "RULE_WORK_ROUTING -->"
    if document.count(start) != 1 or document.count(end) != 1:
        raise SystemExit("工作文档必须包含唯一的 RULE_WORK_ROUTING 配置")
    routing = json.loads(document.split(start, 1)[1].split(end, 1)[0])
    groups = routing["groups"]
    priority = routing["priority"]
    if routing.get("version") != 1 or len(priority) != len(set(priority)):
        raise SystemExit("不支持的路由版本或重复优先级")
    if set(priority + routing["crosscut"] + [routing["fallback"], routing["monster_group"]]) != set(groups):
        raise SystemExit("路由组不完整或存在未知组")
    seeds = {}
    allowed = {"title", "types", "attributes", "sections", "type_prefixes", "has_enchantments", "seeds"}
    for group, spec in groups.items():
        if set(spec) - allowed:
            raise SystemExit(f"{group} 有未知路由字段：{set(spec) - allowed}")
        for template_id, name in spec.get("seeds", {}).items():
            if template_id in seeds:
                raise SystemExit(f"种子主责重复：{template_id}")
            seeds[template_id] = (group, name)

    def matches(spec, features, template):
        return (any(features[key].intersection(spec.get(key, [])) for key in features)
                or any(kind.startswith(prefix) for kind in features["types"] for prefix in spec.get("type_prefixes", []))
                or bool(spec.get("has_enchantments") and template.get("Enchantments")))

    source_hash = hashlib.sha256(source_path.read_bytes()).hexdigest().upper()
    routing_hash = hashlib.sha256(json.dumps(routing, ensure_ascii=False, sort_keys=True).encode("utf-8")).hexdigest().upper()
    rows, seen, found_seeds = [], set(), set()
    con = open_db(db_path)
    try:
        for table, template_id, template in iter_rows(con):
            kind = template.get("$type", "")
            monster = "monster" in table.lower() or kind == "TMonster"
            if not monster and kind not in ("TCardItem", "TCardSkill"):
                continue
            key = (table, template_id)
            if key in seen:
                raise SystemExit(f"重复模板键：{key}")
            seen.add(key)
            base = {key: value for key, value in template.items() if key != "Enchantments"}
            base_features, full_features = work_features(base), work_features(template)
            if template_id in seeds:
                primary, expected_name = seeds[template_id]
                if template.get("InternalName") != expected_name:
                    raise SystemExit(f"种子名称已变更：{template_id}，预期 {expected_name}")
                found_seeds.add(template_id)
            elif monster:
                primary = routing["monster_group"]
            else:
                primary = next((group for group in priority if matches(groups[group], base_features, base)), routing["fallback"])
            related = {group for group, spec in groups.items() if matches(spec, full_features, template)}
            related.update(routing["crosscut"])
            related.add(primary)
            rows.append({"table": table, "template_id": template_id, "name": template.get("InternalName", ""),
                         "template_type": kind or "TMonster", "primary_group": primary,
                         "related_groups": ";".join(sorted(related)), "scope": "待核实",
                         "tiers": ";".join(sorted((template.get("Tiers") or {}).keys())),
                         "enchantments": ";".join(sorted((template.get("Enchantments") or {}).keys())),
                         "native_types": ";".join(sorted(full_features["types"])),
                         "source_sha256": source_hash, "routing_sha256": routing_hash})
    finally:
        con.close()
    if set(seeds) != found_seeds:
        raise SystemExit(f"缺少种子模板：{sorted(set(seeds) - found_seeds)}")
    if not rows:
        raise SystemExit("没有可派单的模板，保留现有索引")
    target.parent.mkdir(parents=True, exist_ok=True)
    with target.open("w", encoding="utf-8-sig", newline="") as output:
        writer = csv.DictWriter(output, fieldnames=list(rows[0]), lineterminator="\n")
        writer.writeheader()
        writer.writerows(sorted(rows, key=lambda row: (row["primary_group"], row["table"], row["template_id"])))
    print(f"已生成 {len(rows)} 个唯一模板的派单索引；种子校验 {len(found_seeds)} 个")
    print("按类型：", dict(collections.Counter(row["template_type"] for row in rows)))
    print("按主责：", dict(sorted(collections.Counter(row["primary_group"] for row in rows).items())))
    print("派单不是支持状态；范围、认领和验收只写工作文档。")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--db")
    parser.add_argument("--out", default="data/rule_inventory.json")
    parser.add_argument("--markdown", help="同时写出一份 Markdown 清单")
    parser.add_argument("--schema", action="store_true", help="只打印数据库表结构")
    parser.add_argument("--sample", help="打印包含指定 $type 的前三个模板片段")
    parser.add_argument("--fixture-names", nargs="+", help="导出指定 InternalName 的完整回归模板，配合 --out")
    parser.add_argument("--mapping-input", help="输出 C# 映射矩阵使用的完整模板 JSONL，不进行类型字符串统计")
    parser.add_argument("--monster-input", help="输出 C# 怪物读取矩阵使用的完整怪物模板 JSONL")
    parser.add_argument("--monster-cards", help="输出怪物读取矩阵使用的物品/技能/插槽效果定义 JSONL")
    parser.add_argument("--mapping-report", help="汇总 C# 实际映射入口报告 TSV")
    parser.add_argument("--work-index", help="按工作文档中的路由配置生成完整派单 CSV")
    args = parser.parse_args()
    if args.mapping_report:
        summarize_mapping(ROOT / args.mapping_report)
        return 0
    source_path = resolve_db(args.db)
    db_path, holder = materialize(source_path)
    try:
        if not db_path.exists():
            raise SystemExit(f"数据库不存在：{db_path}")
        if args.work_index:
            export_work_index(db_path, source_path, ROOT / args.work_index)
            return 0
        if args.mapping_input:
            export_mapping_input(db_path, ROOT / args.mapping_input)
            return 0
        if args.monster_input:
            export_monsters(db_path, ROOT / args.monster_input)
            return 0
        if args.monster_cards:
            export_monster_cards(db_path, ROOT / args.monster_cards)
            return 0
        if args.fixture_names:
            export_fixtures(db_path, args.fixture_names, ROOT / args.out)
            return 0
        if args.schema:
            print_schema(db_path)
            return 0
        if args.sample:
            print_sample(db_path, args.sample)
            return 0
        counts, _locations, totals = scan(db_path)
        write_json(counts, totals, ROOT / args.out)
        known = supported_tokens()
        if args.markdown:
            target = ROOT / args.markdown
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(markdown(counts, totals, known), encoding="utf-8")
        print(f"扫描完成：{db_path}")
        for source in SOURCE_ORDER:
            if totals.get(source):
                print(f"  {source}: {totals[source]} 个模板")
        print(f"  $type 组合: {len(counts)}")
        uncovered = [
            (kind, role)
            for (source, role, kind) in counts
            if kind not in known and role in ("trigger", "action", "aura_action", "prerequisite")
        ]
        print(f"  映射代码未出现的关键类型: {len(set(uncovered))}")
        for kind, role in sorted(set(uncovered))[:40]:
            print(f"    [{role}] {kind}")
        for role in ("trigger", "action", "aura_action", "prerequisite", "condition", "value", "duration", "target"):
            missing = {kind for (source, item_role, kind) in counts if item_role == role and kind not in known}
            if missing:
                print(f"  {role} 未映射类型: {len(missing)}")
        return 0
    finally:
        if holder is not None:
            holder.cleanup()


if __name__ == "__main__":
    sys.exit(main())
