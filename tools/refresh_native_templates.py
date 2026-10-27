"""Refresh native_mapping_templates.jsonl from the live game cache GameData.db.

The engine's card/ability mappers are data-driven: a template present in the
jsonl is simulated (or reported as blocked); a template absent from it is
excluded from fixtures outright.  The vendored corpus snapshot lags the live
game, so newly shipped cards surface as unresolvable templates in replays.

Usage:
    python tools/refresh_native_templates.py [--db PATH] [--out PATH] [--dry-run]
"""
from __future__ import annotations

import argparse
import json
import sqlite3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_DB = Path(r"C:/Users/42552/AppData/LocalLow/Tempo Storm/The Bazaar/prod/cache/GameData.db")
DEFAULT_OUT = ROOT / "tools" / "actionaxis_plugin" / "build" / "native_mapping_templates.jsonl"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--db", default=str(DEFAULT_DB))
    parser.add_argument("--out", default=str(DEFAULT_OUT))
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    connection = sqlite3.connect("file:%s?mode=ro" % Path(args.db).as_posix(), uri=True)
    rows = connection.execute("SELECT Id, Data FROM cards ORDER BY Id").fetchall()
    connection.close()

    old_ids = set()
    if Path(args.out).exists():
        for line in Path(args.out).read_text(encoding="utf-8").splitlines():
            line = line.strip()
            if line:
                old_ids.add(json.loads(line)["id"])

    new_ids = set()
    by_id = {}
    bad = 0
    for cid, data in rows:
        try:
            template = json.loads(data)
        except (TypeError, json.JSONDecodeError):
            bad += 1
            continue
        new_ids.add(cid)
        by_id[cid] = template

    # union: cards retired from the live game stay so older replays keep coverage
    if Path(args.out).exists():
        for line in Path(args.out).read_text(encoding="utf-8").splitlines():
            line = line.strip()
            if not line:
                continue
            row = json.loads(line)
            if row.get("id") and row["id"] not in new_ids:
                new_ids.add(row["id"])
                by_id[row["id"]] = row["template"]

    lines = [json.dumps({"table": "cards", "id": cid, "template": by_id[cid]},
                        ensure_ascii=False, sort_keys=True)
             for cid in sorted(new_ids)]

    added = sorted(new_ids - old_ids)
    removed = sorted(old_ids - new_ids)
    print("cards in live db: %d (unparseable %d)" % (len(rows), bad))
    print("templates: %d -> %d  (+%d / -%d)" % (len(old_ids), len(new_ids), len(added), len(removed)))
    if removed:
        print("REMOVED (present in old jsonl, absent from live db): %s" % ", ".join(removed[:10]))

    if args.dry_run:
        return 0
    Path(args.out).write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("written: %s" % args.out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
