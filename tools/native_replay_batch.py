#!/usr/bin/env python3
"""Batch G12 reconciliation over every local BPP replay.

The batch is intentionally data-driven: it does not pick a few cards. It builds
a native ledger for every replay, exports a simulated ledger for every fixture
whose opening is recoverable, runs the same comparator, and writes an aggregate
inventory plus per-replay counts.

Replays without a BPP OpeningMessage sidecar are still ledgered and counted,
but they are marked BLOCKED_NO_SNAPSHOT and are not silently treated as
simulated. Random-target branches remain visible; this script does not widen
tolerances or reorder events to hide them.
"""
from __future__ import annotations

import argparse
import csv
import gzip
import json
import pathlib
import subprocess
import sys
from collections import Counter, defaultdict

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))

from tools import native_replay_compare as compare
from tools import native_replay_ledger as ledger


GAME_REPLAYS = pathlib.Path(r"D:\Program Files (x86)\Steam\steamapps\common\The Bazaar"
                            r"\BazaarPlusPlusV5\CombatReplays")
EXTRA_REPLAYS = pathlib.Path(r"D:\巴扎plus\CombatReplays")
SNAPSHOT_DB = pathlib.Path(r"D:\Program Files (x86)\Steam\steamapps\common\The Bazaar"
                           r"\BazaarPlusPlusV5\bazaarplusplus.db")
TEMPLATES = pathlib.Path("tools/actionaxis_plugin/build/native_mapping_templates.jsonl")
CATALOG = pathlib.Path("src/data/cards.json")
ENGINE_TESTS = pathlib.Path("tools/actionaxis_plugin/build/EngineTests.exe")
CONTRACT = pathlib.Path("tools/native_replay_contract_v1.json")


def discover(*directories):
    found = {}
    for directory in directories:
        if not directory.exists():
            continue
        for path in sorted(directory.rglob("*.payload.mpack.gz")):
            battle_id = path.name.split(".")[0]
            found.setdefault(battle_id, path)
    return found


def write_gzip_json(path, document):
    path.parent.mkdir(parents=True, exist_ok=True)
    with gzip.open(path, "wt", encoding="utf-8") as handle:
        json.dump(document, handle, ensure_ascii=False, separators=(",", ":"))


def read_gzip_json(path):
    with gzip.open(path, "rt", encoding="utf-8") as handle:
        return json.load(handle)


def load_template_ids(path):
    ids = set()
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if not line:
                continue
            row = json.loads(line)
            if row.get("id"):
                ids.add(row["id"])
    return ids


def opening_audit(ledger_doc):
    items = []
    for side in ("player", "enemy"):
        items.extend(ledger_doc.get("opening", {}).get(side, {}).get("items") or [])
    partial = []
    missing = []
    for item in items:
        if item.get("section") == "Stash" or item.get("inCombat") is False:
            continue
        attrs = item.get("attributes") or {}
        cooldown = attrs.get("Cooldown")
        maximum = attrs.get("CooldownMax")
        if not isinstance(cooldown, int) or not isinstance(maximum, int):
            missing.append(item.get("instanceId"))
        elif maximum > 0 and cooldown < maximum:
            partial.append({"instanceId": item.get("instanceId"),
                            "cooldown": cooldown, "cooldownMax": maximum})
    return {"items": len(items), "partialCooldown": len(partial),
            "missingCooldown": len(missing), "partialSamples": partial[:10]}


def main(argv=None):
    parser = argparse.ArgumentParser(description="Batch G12 replay reconciliation")
    parser.add_argument("--game-replays", default=str(GAME_REPLAYS))
    parser.add_argument("--extra-replays", default=str(EXTRA_REPLAYS))
    parser.add_argument("--snapshot-db", default=str(SNAPSHOT_DB))
    parser.add_argument("--out", default="tmp_g12/batch")
    parser.add_argument("--templates", default=str(TEMPLATES))
    parser.add_argument("--catalog", default=str(CATALOG))
    parser.add_argument("--contract", default=str(CONTRACT))
    parser.add_argument("--engine-tests", default=str(ENGINE_TESTS))
    parser.add_argument("--limit", type=int, default=0)
    parser.add_argument("--skip-sim", action="store_true")
    args = parser.parse_args(argv)

    out = pathlib.Path(args.out)
    ledger_dir = out / "ledgers"
    fixture_dir = out / "fixtures"
    sim_dir = out / "simulated"
    for directory in (ledger_dir, fixture_dir, sim_dir):
        directory.mkdir(parents=True, exist_ok=True)

    replays = discover(pathlib.Path(args.game_replays), pathlib.Path(args.extra_replays))
    battle_ids = sorted(replays)
    if args.limit:
        battle_ids = battle_ids[:args.limit]
    template_sha = ledger.sha256_file(args.templates)
    catalog_sha = ledger.sha256_file(args.catalog)
    template_ids = load_template_ids(args.templates)
    ability_targets = ledger.load_ability_targets(args.templates)

    entries = []
    blocked = []
    audit_totals = Counter()
    for index, battle_id in enumerate(battle_ids, 1):
        replay_path = replays[battle_id]
        entry = {"battleId": battle_id, "replayPath": str(replay_path),
                 "status": "READY", "reason": None}
        try:
            builder = ledger.LedgerBuilder(replay_path, args.contract, args.snapshot_db, True,
                                           ability_targets)
            native = builder.build()
            entry["replaySha256"] = builder.replay_sha256
            entry["frames"] = native["replay"]["frameCount"]
            entry["duration"] = native["replay"]["duration"]
            entry["hasSnapshot"] = bool(builder.snapshot_info)
            entry["ledgerComplete"] = bool(native.get("complete"))
            audit = opening_audit(native)
            entry["openingAudit"] = audit
            for key, value in audit.items():
                if isinstance(value, int):
                    audit_totals[key] += value
            write_gzip_json(ledger_dir / (battle_id + ".json.gz"), native)
            if not entry["hasSnapshot"]:
                entry["status"] = "BLOCKED_NO_SNAPSHOT"
                entry["reason"] = "BPP OpeningMessage sidecar absent; player templates/opening values UNKNOWN"
            elif not native.get("complete"):
                entry["status"] = "BLOCKED_LEDGER_INCOMPLETE"
                entry["reason"] = "native ledger complete=false"
            else:
                try:
                    fixture = ledger.build_engine_fixture(
                        native, args.templates, args.catalog, template_sha, catalog_sha,
                        template_ids)
                    fixture_path = fixture_dir / (battle_id + ".json")
                    fixture_path.write_text(json.dumps(fixture, ensure_ascii=False, indent=1) + "\n",
                                            encoding="utf-8")
                    entry["fixturePath"] = str(fixture_path)
                    entry["fixtureItems"] = len(fixture["items"])
                    entry["excludedItems"] = len(fixture.get("excludedItems") or [])
                except SystemExit as error:
                    entry["status"] = "BLOCKED_TEMPLATE"
                    entry["reason"] = str(error)
                except Exception as error:  # malformed external replay
                    entry["status"] = "ERROR"
                    entry["reason"] = "fixture: " + str(error)
        except Exception as error:
            entry["status"] = "ERROR"
            entry["reason"] = "ledger: " + str(error)
        if entry["status"] != "READY":
            blocked.append(entry)
        entries.append(entry)
        if index % 25 == 0:
            print("ledgered %s/%s" % (index, len(battle_ids)))

    fixtures = sorted(str(path) for path in fixture_dir.glob("*.json"))
    sim_status = 0
    if fixtures and not args.skip_sim:
        command = [str(pathlib.Path(args.engine_tests).resolve()), "--ledger-batch",
                   str(fixture_dir.resolve()), str(pathlib.Path(args.templates).resolve()),
                   str(pathlib.Path(args.catalog).resolve()), str(sim_dir.resolve())]
        print("running " + " ".join(command))
        result = subprocess.run(command, capture_output=True)
        sim_status = result.returncode
        if result.stdout:
            print(result.stdout.decode("utf-8", errors="replace").strip())
        if result.stderr:
            print(result.stderr.decode("utf-8", errors="replace").strip(), file=sys.stderr)

    replay_rows = []
    aggregate = Counter()
    channel_totals = defaultdict(Counter)
    group_totals = Counter()
    group_replays = Counter()
    unverified_reasons = Counter()
    compared = 0
    for entry in entries:
        if entry["status"] != "READY":
            continue
        battle_id = entry["battleId"]
        sim_path = sim_dir / (battle_id + ".sim.json")
        if not sim_path.exists():
            entry["status"] = "BLOCKED_SIM_MISSING"
            entry["reason"] = "EngineTests --ledger-batch did not produce output"
            blocked.append(entry)
            continue
        try:
            native = read_gzip_json(ledger_dir / (battle_id + ".json.gz"))
            simulated = json.loads(sim_path.read_text(encoding="utf-8"))
            report = compare.compare_documents(native, simulated, 0)
        except Exception as error:
            entry["status"] = "ERROR_COMPARE"
            entry["reason"] = str(error)
            blocked.append(entry)
            continue
        compared += 1
        entry["verdict"] = report["verdict"]
        entry["counts"] = report["counts"]
        entry["channels"] = report["channels"]
        entry["topDiffGroups"] = report["topDiffGroups"][:20]
        entry["simulatedSource"] = simulated.get("source")
        entry["unknowns"] = report["unknowns"]
        for key, value in report["counts"].items():
            aggregate[key] += value
        for name, channel in report["channels"].items():
            for key, value in channel.items():
                if key == "unverifiedReasons":
                    unverified_reasons.update(value)
                    continue
                channel_totals[name][key] += value
        for group in report["topDiffGroups"]:
            group_key = (group["channel"], group["status"], group.get("kind"),
                         group.get("canonical"), group.get("attribute"))
            group_totals[group_key] += group["count"]
            group_replays[group_key] += 1
        replay_rows.append({
            "battleId": battle_id,
            "verdict": report["verdict"],
            "frames": entry.get("frames"),
            "hasSnapshot": entry.get("hasSnapshot"),
            **{key: report["counts"].get(key, 0) for key in
               ("matched", "unverified", "postEnd", "missing", "extra",
                "valueMismatch", "timeMismatch", "unknown")},
        })

    top_groups = []
    for key, count in group_totals.most_common(100):
        top_groups.append({"channel": key[0], "status": key[1], "kind": key[2],
                           "canonical": key[3], "attribute": key[4],
                           "count": count, "replays": group_replays[key]})
    summary = {
        "generatedBy": "tools/native_replay_batch.py",
        "replaysDiscovered": len(battle_ids),
        "compared": compared,
        "blocked": len(blocked),
        "simExitCode": sim_status,
        "aggregate": dict(aggregate),
        "unverifiedReasons": dict(unverified_reasons),
        "channels": {name: dict(values) for name, values in channel_totals.items()},
        "openingAudit": dict(audit_totals),
        "topGroups": top_groups,
        "blockedReplays": blocked,
        "replays": entries,
    }
    (out / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=1) + "\n",
                                      encoding="utf-8")
    with (out / "replays.csv").open("w", encoding="utf-8", newline="") as handle:
        fields = ["battleId", "verdict", "frames", "hasSnapshot", "matched", "unverified",
                  "postEnd", "missing", "extra", "valueMismatch", "timeMismatch", "unknown"]
        writer = csv.DictWriter(handle, fieldnames=fields)
        writer.writeheader()
        writer.writerows(replay_rows)
    with (out / "top_groups.csv").open("w", encoding="utf-8", newline="") as handle:
        fields = ["channel", "status", "kind", "canonical", "attribute", "count", "replays"]
        writer = csv.DictWriter(handle, fieldnames=fields)
        writer.writeheader()
        writer.writerows(top_groups)

    print("compared=%s blocked=%s aggregate=%s" % (compared, len(blocked), dict(aggregate)))
    print("openingAudit=%s" % dict(audit_totals))
    for group in top_groups[:15]:
        print("GROUP %s %s count=%s replays=%s kind=%s canonical=%s attribute=%s" % (
            group["channel"], group["status"], group["count"], group["replays"],
            group["kind"], group["canonical"], group["attribute"]))
    return 0 if compared else 2


if __name__ == "__main__":
    raise SystemExit(main())
