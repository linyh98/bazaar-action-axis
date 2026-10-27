#!/usr/bin/env python3
"""Aggregate event/opening coverage from native ledger files (read-only)."""
from __future__ import annotations

import argparse
import gzip
import json
import pathlib
from collections import Counter


def main(argv=None):
    parser = argparse.ArgumentParser(description="Aggregate native replay ledger coverage")
    parser.add_argument("ledgers")
    parser.add_argument("--json", default="tmp_g12/coverage.json")
    args = parser.parse_args(argv)
    folder = pathlib.Path(args.ledgers)
    codes = Counter()
    actions = Counter()
    rows = []
    totals = Counter()
    for path in sorted(folder.glob("*.json.gz")):
        with gzip.open(path, "rt", encoding="utf-8") as handle:
            document = json.load(handle)
        events = (document.get("combat") or {}).get("events") or []
        despawn = (document.get("despawn") or {}).get("events") or []
        all_events = events + despawn
        event_codes = Counter(event.get("codeName") for event in all_events)
        event_actions = Counter(event.get("action") for event in events if event.get("action"))
        codes.update(event_codes)
        actions.update(event_actions)
        transforms = sum(1 for event in events if str(event.get("codeName", "")).endswith("CardTransformed"))
        reverts = sum(1 for event in all_events if str(event.get("codeName", "")).endswith("CardTransformReverted"))
        passive = sum(1 for event in events if str(event.get("codeName", "")).endswith("EffectTriggered"))
        random_targets = sum(1 for event in events if event.get("randomTarget"))
        sandstorm = sum(1 for event in events if "Sandstorm" in str(event.get("action") or "")) + \
            sum(1 for event in (document.get("combat") or {}).get("healthChanges") or []
                for adjustment in event.get("healthAdjustments") or []
                if "Sandstorm" in str(adjustment.get("damageType") or ""))
        items = []
        for side in ("player", "enemy"):
            items.extend((document.get("opening") or {}).get(side, {}).get("items") or [])
        combat_items = [item for item in items if item.get("section") != "Stash"]
        partial = sum(1 for item in combat_items
                      if isinstance(item.get("attributes", {}).get("Cooldown"), int) and
                      isinstance(item.get("attributes", {}).get("CooldownMax"), int) and
                      item["attributes"]["CooldownMax"] > 0 and
                      item["attributes"]["Cooldown"] < item["attributes"]["CooldownMax"])
        missing_cd = sum(1 for item in combat_items
                         if not isinstance(item.get("attributes", {}).get("Cooldown"), int))
        row = {
            "battleId": document.get("replay", {}).get("battleId"),
            "frames": document.get("replay", {}).get("frameCount"),
            "events": len(events),
            "transforms": transforms,
            "reverts": reverts,
            "passiveTriggers": passive,
            "randomTargetEvents": random_targets,
            "sandstormEvents": sandstorm,
            "openingCombatItems": len(combat_items),
            "partialOpeningCooldown": partial,
            "missingOpeningCooldown": missing_cd,
            "unknowns": len(document.get("unknowns") or []),
        }
        rows.append(row)
        for key in ("transforms", "reverts", "passiveTriggers", "randomTargetEvents",
                    "sandstormEvents", "openingCombatItems", "partialOpeningCooldown",
                    "missingOpeningCooldown", "unknowns"):
            totals[key] += row[key]
        totals["replays"] += 1
        totals["events"] += len(events)
    report = {
        "replays": len(rows),
        "totals": dict(totals),
        "eventCodes": dict(codes),
        "actions": dict(actions),
        "samples": {
            "transforms": [row["battleId"] for row in rows if row["transforms"]][:20],
            "passiveTriggers": [row["battleId"] for row in rows if row["passiveTriggers"]][:20],
            "sandstorm": [row["battleId"] for row in rows if row["sandstormEvents"]][:20],
            "randomTargets": [row["battleId"] for row in rows if row["randomTargetEvents"]][:20],
        },
        "rows": rows,
    }
    target = pathlib.Path(args.json)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(report, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    print(json.dumps({key: report[key] for key in ("replays", "totals", "samples")},
                     ensure_ascii=False, indent=1))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
