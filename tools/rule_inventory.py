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


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--db")
    parser.add_argument("--out", default="data/rule_inventory.json")
    parser.add_argument("--markdown", help="同时写出一份 Markdown 清单")
    parser.add_argument("--schema", action="store_true", help="只打印数据库表结构")
    parser.add_argument("--sample", help="打印包含指定 $type 的前三个模板片段")
    args = parser.parse_args()
    db_path = resolve_db(args.db)
    db_path, holder = materialize(db_path)
    try:
        if not db_path.exists():
            raise SystemExit(f"数据库不存在：{db_path}")
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
