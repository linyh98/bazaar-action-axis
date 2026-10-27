"""Verify the G06 native fixtures against the GameData database and task index.

Usage:
    python tools/g06_native_verify.py [--db vendor/GameData/GameData.db]

Focus fixture export:
    python tools/rule_inventory.py --db vendor/GameData/GameData.db \
      --fixture-names "cards:Warpaint" "cards:Vengeful Sigil" "cards:Burning Shield" \
      "cards:Toxic Shield" "cards:Wild Boar" "cards:Feeding Frenzy" \
      "cards:Wildroot Amulet" "cards:Blueberry Pie" "cards:Basket" "cards:Truffles" \
      "monsters:Wild Boar" --out tools/actionaxis_plugin/tests/fixtures/native_g06_rage.json
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import sqlite3
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FIXTURE = ROOT / "tools/actionaxis_plugin/tests/fixtures/native_g06_rage.json"
FULL_FIXTURE = ROOT / "tools/actionaxis_plugin/tests/fixtures/native_g06_templates.json"
INDEX = ROOT / "docs/native-rule-task-index.csv"
DEFAULT_DB = ROOT / "vendor/GameData/GameData.db"

# Seed rows used by the G06 behavior suite. The monster Wild Boar is a G00 seed
# whose rage/enrage behavior is owned by G06; the item with the same name is G06.
EXPECTED_GROUPS = {
    "cards:Warpaint": "G06",
    "cards:Vengeful Sigil": "G06",
    "cards:Burning Shield": "G06",
    "cards:Toxic Shield": "G06",
    "cards:Wild Boar": "G06",
    "monsters:Wild Boar": "G00",
}


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load_db_templates(db_path: Path) -> dict[tuple[str, str], dict]:
    con = sqlite3.connect("file:" + db_path.as_posix() + "?mode=ro", uri=True)
    try:
        rows: dict[tuple[str, str], dict] = {}
        for table in ("cards", "monsters"):
            for row_id, data in con.execute('select Id, Data from "%s"' % table):
                rows[(table, str(row_id))] = json.loads(data)
        return rows
    finally:
        con.close()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--db", type=Path, default=DEFAULT_DB)
    args = parser.parse_args()

    for path in (FIXTURE, FULL_FIXTURE, INDEX, args.db):
        if not path.exists():
            print("missing input: " + str(path), file=sys.stderr)
            return 1

    fixture = json.loads(FIXTURE.read_text(encoding="utf-8"))
    full_fixture = json.loads(FULL_FIXTURE.read_text(encoding="utf-8"))
    rows = list(csv.DictReader(INDEX.open(encoding="utf-8-sig")))
    by_id = {row["template_id"]: row for row in rows}
    db = load_db_templates(args.db)

    failures = 0
    for key, expected_group in EXPECTED_GROUPS.items():
        if key not in fixture:
            print("MISSING fixture key " + key)
            failures += 1
            continue
        row = fixture[key]
        template_id = row["id"]
        index = by_id.get(template_id)
        if index is None:
            print("MISSING index row " + key + " " + template_id)
            failures += 1
            continue
        if index["primary_group"] != expected_group:
            print("WRONG group " + key + " expected " + expected_group + " got " + index["primary_group"])
            failures += 1
        if db.get((row["table"], template_id)) != row["template"]:
            print("DB mismatch " + key + " " + template_id)
            failures += 1
        print("OK " + key + " " + template_id + " " + index["primary_group"])

    for key, row in fixture.items():
        template_id = row["id"]
        if template_id not in by_id:
            print("MISSING index reference " + key + " " + template_id)
            failures += 1

    # Full G06 task-index fixture: every primary G06 row must be present exactly
    # once and must still equal the native 5.0.0 database template.
    g06_ids = {row["template_id"] for row in rows if row["primary_group"] == "G06"}
    if len(g06_ids) != 136:
        print("WRONG G06 index count " + str(len(g06_ids)))
        failures += 1
    if set(full_fixture) != g06_ids:
        missing = sorted(g06_ids - set(full_fixture))
        extra = sorted(set(full_fixture) - g06_ids)
        print("G06 fixture mismatch missing=" + str(len(missing)) + " extra=" + str(len(extra)))
        failures += 1
    for template_id, row in full_fixture.items():
        index = by_id.get(template_id)
        if index is None or index["primary_group"] != "G06":
            print("G06 fixture not indexed as G06: " + template_id)
            failures += 1
            continue
        if row.get("table") != "cards" or row.get("id") != template_id:
            print("G06 fixture shape mismatch: " + template_id)
            failures += 1
        if db.get(("cards", template_id)) != row.get("template"):
            print("G06 fixture DB mismatch: " + template_id)
            failures += 1

    print("focus_fixture_sha256 " + sha256(FIXTURE))
    print("full_fixture_sha256 " + sha256(FULL_FIXTURE))
    print("db_sha256 " + sha256(args.db))
    print("index_sha256 " + sha256(INDEX))
    print("g06_primary " + str(len(g06_ids)))
    print("g06_full_templates " + str(len(full_fixture)))
    print("focus_templates " + str(len(fixture)))
    if failures:
        print("FAILED " + str(failures))
        return 1
    print("OK G06 native verification")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
