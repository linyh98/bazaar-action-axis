#!/usr/bin/env python3
"""Batch-audit the ActionAxis engine against a folder of BPP combat replays.

For every ``*.payload.mpack.gz`` in the replay folder the tool runs the full
G12 pipeline:

1. ``native_replay_ledger`` decodes the replay into the native event ledger
   and an engine fixture (opening board -> CardMapper input);
2. ``EngineTests.exe --ledger`` simulates the fixture into a simulated ledger;
3. ``native_replay_compare`` diffs both ledgers per 50 ms frame;
4. this tool classifies every diff row by the side and template of the
   instance that caused it, and aggregates across battles.

Without the recorder's BPP ``OpeningMessage`` sidecar database the replay only
carries one full board (combatant 1: board, skills, live attributes) plus both
health curves.  Those battles run in "one-sided" mode: the known board is
simulated against a hollowed-out opponent (HP inflated so the fight is not
cut short), and only diffs attributable to the known side are treated as
verifiable findings.  Health/outcome channels and the opponent's own cards
cannot be verified there and are reported as skipped instead.

Usage:
    python tools/replay_batch_audit.py --replays <dir> --out <dir> \
        [--db bazaarplusplus.db] [--battle <battleId>]... [--limit N] \
        [--workers 4] [--artifacts report|all|none] [--window-frames N]

Outputs ``<out>/summary.md`` and ``<out>/summary.json`` plus per-battle
artifacts under ``<out>/<battleId>/``.
"""
from __future__ import annotations

import argparse
import contextlib
import io
import json
import os
import pathlib
import re
import shutil
import subprocess
import sys
import time
from collections import Counter, defaultdict

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools"))

import native_replay_compare as compare  # noqa: E402
import native_replay_ledger as ledger  # noqa: E402

DEFAULT_CONTRACT = ROOT / "tools" / "native_replay_contract_v1.json"
DEFAULT_TEMPLATES = ROOT / "tools" / "actionaxis_plugin" / "build" / "native_mapping_templates.jsonl"
DEFAULT_CATALOG = ROOT / "src" / "data" / "cards.json"
DEFAULT_ENGINE_EXE = ROOT / "tools" / "actionaxis_plugin" / "build" / "EngineTests.exe"

# Attributes whose per-frame curves depend heavily on what the opponent's
# board does to this item (freeze/slow/haste/charge).  In one-sided mode the
# opponent board is unknown, so a mismatch on these is expected noise; they
# are still counted, but flagged instead of treated as a bug signal.
INTERFERENCE_ATTRIBUTES = {"Cooldown", "Haste", "Slow", "Freeze", "Charge"}

HP_INFLATION = 20  # one-sided mode: hollow opponent HP so the sim spans the replay
HP_INFLATION_FLOOR = 2000


def load_template_names(catalog_path, templates_path=None):
    """templateId -> (name, kind) merged from the simulator catalog and the
    raw mapping table (which also knows skills missing from the catalog)."""
    names = {}
    if templates_path:
        try:
            with open(templates_path, encoding="utf-8") as handle:
                for line in handle:
                    line = line.strip()
                    if not line:
                        continue
                    row = json.loads(line)
                    template = row.get("template") or {}
                    if row.get("id") and template.get("InternalName"):
                        kind = "技能" if template.get("Type") == "Skill" else "卡牌"
                        names[row["id"]] = (template["InternalName"], kind)
        except OSError:
            pass
    try:
        rows = json.loads(pathlib.Path(catalog_path).read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return names
    for row in rows:
        base = row.get("baseItemId") or row.get("id")
        if not base:
            continue
        label = row.get("nameCn") or row.get("name")
        if label:
            names[base] = (label, names.get(base, (None, "卡牌"))[1])
    return names


def load_mapped_template_ids(templates_path):
    ids = set()
    try:
        with open(templates_path, encoding="utf-8") as handle:
            for line in handle:
                line = line.strip()
                if not line:
                    continue
                row = json.loads(line)
                if row.get("id"):
                    ids.add(row["id"])
    except OSError:
        pass
    return ids


class IdentityResolver:
    """instanceId -> (side, rootTemplateId) from the native ledger."""

    def __init__(self, native):
        self.by_instance = {}
        instances = (native.get("identity") or {}).get("instances") or {}
        for item in instances.values():
            root = item
            seen = set()
            parent = item.get("parent")
            while parent and isinstance(parent, str) and parent in seen:
                seen.add(parent)
                root = instances.get(parent) or root
                parent = (instances.get(parent) or {}).get("parent")
            template = item.get("templateId")
            generations = root.get("generations") or []
            if generations:
                template = (generations[0] or {}).get("templateId") or template
            self.by_instance[str(item.get("instanceId"))] = (
                item.get("side"), template)
        for side in ("player", "enemy"):
            for skill in ((native.get("opening") or {}).get(side) or {}).get("skills") or []:
                iid = skill.get("instanceId")
                if not iid or not skill.get("templateId"):
                    continue
                key = str(iid)
                existing = self.by_instance.get(key)
                # Instances ensured mid-combat carry a blank template; the
                # opening skill record knows the real one.
                if existing is None or not existing[1]:
                    self.by_instance[key] = (side, skill["templateId"])

    @staticmethod
    def _candidates(raw_id):
        text = str(raw_id)
        yield text
        if "|" in text:
            yield text.split("|")[-1]  # canonical 'side|slot|gN|template' tail
        if ":" in text:
            yield text.split(":", 1)[1]  # sim identity 'side:instanceId'

    def lookup(self, raw_id):
        if raw_id is None:
            return None, None
        for candidate in self._candidates(raw_id):
            entry = self.by_instance.get(candidate)
            if entry:
                return entry
        return None, None

    def side_of(self, raw_id):
        return self.lookup(raw_id)[0]

    def template_of(self, raw_id):
        return self.lookup(raw_id)[1]


def parse_canonical(value):
    """'enemy|3|g0|<templateId>' -> (side, templateId); tolerant to junk."""
    if not value:
        return None, None
    parts = str(value).split("|")
    side = parts[0] if parts and parts[0] in ("player", "enemy", "unknown") else None
    template = parts[-1] if side and len(parts) >= 4 and parts[-1] != "?" else None
    return side, template


# Attributes the simulated ledger exports per card snapshot (mirrors the
# engine's CaptureItemAttributes: statuses, the SnapshotAttributes list and
# runtime AttributeDeltas keys like Custom_N — skills included since they are
# snapshotted too).  Missing native rows on these are real gaps, not
# export-symmetry artifacts.  Aura records are likewise not exported.
SIM_CARD_ATTRIBUTES = {
    "Cooldown", "CooldownMax", "Ammo", "AmmoMax", "Haste", "Slow", "Freeze",
    "Flying", "Multicast", "DamageAmount", "HealAmount", "ShieldApplyAmount",
    "BurnApplyAmount", "PoisonApplyAmount", "RegenApplyAmount", "RageApplyAmount",
    "CritChance", "DamageCrit", "HealCrit", "ShieldCrit", "BurnCrit", "PoisonCrit",
    "Lifesteal", "LifestealPercentage", "FlatCooldownReduction",
    "PercentCooldownReduction", "PercentChargeReduction", "PercentHasteReduction",
    "PercentSlowReduction", "PercentFreezeReduction", "ChargeAmount", "ChargeTargets",
    "HasteAmount", "HasteTargets", "SlowAmount", "SlowTargets", "FreezeAmount",
    "FreezeTargets", "FlyingTargets", "EnchantTargets", "ReloadAmount", "ReloadTargets",
    "BurnRemoveAmount", "PoisonRemoveAmount", "DestroyImmunity", "Heated", "Chilled",
    "SellPrice", "BuyPrice", "Custom_0", "Custom_1", "Custom_2", "Custom_3",
    "Custom_4", "Custom_5",
}
SIM_EFFECT_KINDS = {"auraApplied", "auraRemoved"}


def classify_row(row, resolver, one_sided, known_side):
    """Return (subject_side, subject_template, verifiable, interference_suspect)."""
    channel = row.get("channel")
    native = row.get("native") or {}
    simulated = row.get("simulated") or {}
    side = template = None
    if channel == "effects":
        # The comparator's ref string can erase the instance id ('?' tails),
        # so resolve from the raw ledger event first, then fall back.
        raw_source = (native.get("raw") or {}).get("source") or \
            (simulated.get("raw") or {}).get("source")
        if raw_source:
            side, template = resolver.lookup(raw_source)
        if side is None and template is None:
            source = native.get("source") or simulated.get("source")
            # An unresolved-but-present source belongs to the unrecorded board
            # or to an ambient effect; both stay unattributed instead of being
            # pinned onto the known side via the target.
            if source:
                side, template = parse_canonical(source)
                if template is None:
                    _, template = resolver.lookup(source)
                if side is None:
                    side = resolver.side_of(source)
    elif channel == "cards":
        side, template = parse_canonical(native.get("canonical") or simulated.get("canonical"))
        if template is None or side in (None, "unknown"):
            raw_id = (native.get("raw") or {}).get("instanceId") or \
                (simulated.get("raw") or {}).get("instanceId")
            resolved_side, resolved_template = resolver.lookup(raw_id)
            template = template or resolved_template
            if side in (None, "unknown"):
                side = resolved_side
    elif channel == "identity":
        side, template = parse_canonical(native.get("canonical") or simulated.get("canonical"))
        if template is None and side == "unknown":
            side = None
    elif channel == "health":
        side = native.get("side") or simulated.get("side")
    elif channel == "outcome":
        side = None
    attribute = native.get("attribute") or simulated.get("attribute")
    interference = channel == "cards" and attribute in INTERFERENCE_ATTRIBUTES
    if one_sided and channel in ("health", "outcome"):
        return side, template, False, interference
    if side in (None, "unknown"):
        return side, template, False, interference
    verifiable = side == known_side or not one_sided
    return side, template, verifiable, interference


def build_fixture(ledger_doc, templates_path, catalog_path, mapped_ids):
    """Engine fixture, with cards absent from the mapping table excluded up
    front: the C# exporter hard-fails on them, and their effects are a
    coverage gap rather than an engine bug."""
    fixture = ledger.build_engine_fixture(ledger_doc, pathlib.Path(templates_path),
                                          pathlib.Path(catalog_path))
    excluded = fixture.get("excluded") or []
    for kind in ("items", "skills"):
        kept = []
        for row in fixture.get(kind) or []:
            if row.get("templateId") not in mapped_ids:
                excluded.append({"side": row.get("side"), "instanceId": row.get("instanceId"),
                                 "templateId": row.get("templateId"), "reason": "TemplateNotMapped"})
                continue
            kept.append(row)
        fixture[kind] = kept
    fixture["excluded"] = excluded
    return fixture


def one_sided_transform(fixture):
    """Hollow out the unknown side so the simulation spans the whole replay."""
    # build_fixture emits a flat items/skills list (rows carry .side); the
    # player dict only holds hero attributes.
    had_board = any(row.get("side") == "player"
                    for row in (fixture.get("items") or []) + (fixture.get("skills") or []))
    if had_board:
        return False
    player = fixture["player"]
    hp = player.get("startHp") or 0
    inflated = max(hp * HP_INFLATION, HP_INFLATION_FLOOR)
    player["startHp"] = inflated
    player["maxHp"] = inflated
    # The known board is simulated in isolation: run exactly the replay window,
    # otherwise frames past the native duration would surface as fake extras.
    if fixture.get("nativeDuration"):
        fixture["maxTime"] = fixture["nativeDuration"]
    fixture["mode"] = "one-sided"
    return True


def process_one(task):
    """Worker: ledger -> fixture -> simulate -> compare -> classify one replay."""
    (replay_path, out_dir, contract_path, templates_path, catalog_path,
     engine_exe, db_path, artifacts, window_frames, mapped_ids,
     ability_targets) = task
    out_dir = pathlib.Path(out_dir)
    battle_dir = out_dir / pathlib.Path(replay_path).name.split(".")[0]
    battle_dir.mkdir(parents=True, exist_ok=True)
    result = {"replay": str(replay_path), "battleId": None, "error": None,
              "mode": None, "verdict": None, "counts": {}, "warnings": [],
              "diffs": [], "frames": None, "duration": None,
              "items": {}, "skills": {}, "knownSide": "enemy"}
    try:
        builder = ledger.LedgerBuilder(str(replay_path), str(contract_path),
                                       db_path, db_path is not None,
                                       ability_targets=ability_targets)
        native = builder.build()
        result["battleId"] = (native.get("replay") or {}).get("battleId")
        result["frames"] = (native.get("replay") or {}).get("frameCount")
        result["duration"] = (native.get("replay") or {}).get("duration")
        result["complete"] = native.get("complete")
        result["unknownCount"] = len(native.get("unknowns") or [])
        opening = native.get("opening") or {}
        result["items"] = {side: len((opening.get(side) or {}).get("items") or [])
                           for side in ("player", "enemy")}
        result["skills"] = {side: len((opening.get(side) or {}).get("skills") or [])
                            for side in ("player", "enemy")}
        with contextlib.redirect_stdout(io.StringIO()):
            fixture = build_fixture(native, templates_path, catalog_path, mapped_ids)
        result["unmapped"] = sorted({row["templateId"] for row in fixture.get("excluded") or []
                                     if row.get("reason") == "TemplateNotMapped" and row.get("templateId")})
        one_sided = one_sided_transform(fixture)
        result["mode"] = "one-sided" if one_sided else "two-sided"
        fixture_path = battle_dir / "fixture.json"
        fixture_path.write_text(json.dumps(fixture, ensure_ascii=False, indent=1) + "\n",
                                encoding="utf-8")
        native_path = battle_dir / "native.json"
        sim_path = battle_dir / "sim.json"
        report_path = battle_dir / "report.json"
        native_path.write_text(json.dumps(native, ensure_ascii=False, indent=1) + "\n",
                               encoding="utf-8")
        proc = subprocess.run(
            [str(engine_exe), "--ledger", str(fixture_path), str(templates_path),
             str(catalog_path), str(sim_path)],
            capture_output=True, text=True, encoding="utf-8", errors="replace",
            cwd=str(ROOT), timeout=300)
        if proc.returncode != 0 or not sim_path.exists():
            message = ((proc.stderr or "") + (proc.stdout or "")).strip()
            result["error"] = "EngineTests failed: %s" % message[-400:]
            return result
        sim = json.loads(sim_path.read_text(encoding="utf-8"))
        result["warnings"] = list(sim.get("warnings") or [])
        argv = [str(native_path), str(sim_path), "--json", str(report_path)]
        if window_frames:
            argv += ["--window-frames", str(window_frames)]
        with contextlib.redirect_stdout(io.StringIO()):
            compare.main(argv)
        report = json.loads(report_path.read_text(encoding="utf-8"))
        if artifacts == "none":
            report_path.unlink(missing_ok=True)
            fixture_path.unlink(missing_ok=True)
        if artifacts in ("report", "none"):
            native_path.unlink(missing_ok=True)
            sim_path.unlink(missing_ok=True)

        resolver = IdentityResolver(native)
        known_side = result["knownSide"]
        findings = {}  # template -> compact per-battle finding, merged by aggregate()
        verdict_counts = Counter()
        classified = []
        for row in report.get("diffs") or []:
            side, template, verifiable, interference = classify_row(
                row, resolver, one_sided, known_side)
            attribute = (row.get("native") or {}).get("attribute") or \
                (row.get("simulated") or {}).get("attribute")
            kind = row.get("kind") or (row.get("native") or {}).get("kind")
            if artifacts in ("diffs", "all"):
                classified.append({
                    "channel": row.get("channel"), "status": row.get("status"),
                    "frame": row.get("frame"), "note": row.get("note"),
                    "side": side, "template": template,
                    "attribute": attribute, "kind": kind,
                    "verifiable": verifiable,
                    "interferenceSuspect": interference,
                })
            if not verifiable:
                verdict_counts["skipped"] += 1
                continue
            status = row.get("status")
            if one_sided and kind == "death":
                # Either side dying depends on the unrecorded board's output.
                verdict_counts["skipped"] += 1
                continue
            if status == "missing" and (
                    (row.get("channel") == "cards" and attribute not in SIM_CARD_ATTRIBUTES)
                    or (row.get("channel") == "effects" and kind in SIM_EFFECT_KINDS)):
                # The simulated ledger does not export this record at all.
                verdict_counts["structural"] += 1
                continue
            if interference and one_sided and status in ("value-mismatch", "missing", "extra"):
                # The replay curve reflects freeze/slow/haste interventions from
                # the unrecorded board; in one-sided mode any divergence on
                # these attributes is noise, whatever its row shape.
                verdict_counts["interference"] += 1
            else:
                verdict_counts[status or "other"] += 1
            if result.get("unmapped") and template in result["unmapped"]:
                continue
            if not template:
                template = "(unknown-instance)" if side == known_side else "(unattributed)"
            entry = findings.setdefault(template, {"byKind": Counter(), "onset": [], "notes": []})
            entry["byKind"][(row.get("channel"), row.get("status"), kind, attribute)] += 1
            if interference and row.get("status") == "value-mismatch" and row.get("frame") is not None:
                entry["onset"].append(row["frame"])
            if len(entry["notes"]) < 3 and row.get("note"):
                entry["notes"].append({"frame": row.get("frame"), "note": row["note"]})
        result["findings"] = {
            template: {
                "byKind": [[key, count] for key, count in entry["byKind"].most_common()],
                "onset": entry["onset"],
                "notes": entry["notes"],
            } for template, entry in findings.items()}
        result["counts"] = dict(verdict_counts)
        result["diffs"] = classified if artifacts in ("diffs", "all") else []
        channel_summary = report.get("channels") or {}
        result["channels"] = channel_summary
        hard = sum(count for status, count in verdict_counts.items()
                   if status in ("missing", "extra", "value-mismatch", "time-mismatch"))
        if result.get("complete") is False:
            result["verdict"] = "UNKNOWN"
        elif hard == 0:
            result["verdict"] = "MATCH"
        else:
            result["verdict"] = "DIFF"
        if artifacts in ("diffs", "all") and classified:
            (battle_dir / "diffs.json").write_text(
                json.dumps(classified, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    except SystemExit as error:
        result["error"] = "ledger/fixture failed: %s" % error
    except Exception as error:  # noqa: BLE001 - keep the batch running
        result["error"] = "%s: %s" % (type(error).__name__, error)
    return result


def aggregate(results, template_names):
    """Merge per-battle pre-aggregated findings into a per-template table."""
    template_stats = {}
    battle_rows = []
    warning_patterns = Counter()
    for result in results:
        battle_id = result.get("battleId") or pathlib.Path(result["replay"]).name
        row = {
            "battleId": battle_id, "mode": result.get("mode"),
            "verdict": result.get("verdict") or ("ERROR" if result.get("error") else "?"),
            "frames": result.get("frames"), "duration": result.get("duration"),
            "items": result.get("items"), "skills": result.get("skills"),
            "counts": result.get("counts", {}),
            "unmapped": result.get("unmapped") or [],
        }
        if result.get("error"):
            row["error"] = result["error"]
        battle_rows.append(row)
        for warning in result.get("warnings") or []:
            pattern = re.sub(r"0x[0-9a-fA-F]+", "0x?", warning)
            pattern = re.sub(r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", "<guid>", pattern)
            warning_patterns[pattern] += 1
        for template, finding in (result.get("findings") or {}).items():
            label, kind = template_names.get(template, (None, None))
            stats = template_stats.setdefault(template, {
                "template": template, "name": label, "kind": kind, "battles": set(),
                "byKind": Counter(), "interference": Counter(),
                "onsetFrames": [], "notes": [],
            })
            stats["battles"].add(battle_id)
            for key, count in finding.get("byKind") or []:
                channel, status, kind, attribute = key
                stats["byKind"][key] += count
                if channel == "cards" and attribute in INTERFERENCE_ATTRIBUTES \
                        and status == "value-mismatch":
                    stats["interference"][key] += count
            frames = finding.get("onset") or []
            stats["onsetFrames"].extend(frames)
            notes = finding.get("notes") or []
            if notes:
                stats["notes"].append({"battleId": battle_id, "notes": notes[:2]})
    for stats in template_stats.values():
        stats["battles"] = len(stats["battles"])
        stats["onsetFrames"] = sorted(stats["onsetFrames"])
        stats["byKind"] = [[" ".join(str(p) for p in key), count]
                           for key, count in stats["byKind"].most_common()]
        stats["interference"] = [[" ".join(str(p) for p in key), count]
                                 for key, count in stats["interference"].most_common()]
    ranked = sorted(template_stats.values(),
                    key=lambda stats: (-stats["battles"],
                                       -sum(count for _, count in stats["byKind"])))
    return {"templates": ranked, "battles": battle_rows,
            "warningPatterns": warning_patterns.most_common()}


def median(values):
    if not values:
        return None
    ordered = sorted(values)
    mid = len(ordered) // 2
    return ordered[mid] if len(ordered) % 2 else (ordered[mid - 1] + ordered[mid]) / 2


def write_markdown(path, aggregation, results, one_sided_total, args):
    template_names = load_template_names(args.catalog, args.templates)
    battle_rows = aggregation["battles"]
    verdicts = Counter(row["verdict"] for row in battle_rows)
    lines = []
    lines.append("# 行动轴引擎 × 回放批量审计报告\n")
    lines.append("- 回放目录: `%s`" % args.replays)
    lines.append("- 场次: %d（成功 %d，错误 %d）" % (
        len(battle_rows), sum(1 for r in battle_rows if r["verdict"] != "ERROR"),
        sum(1 for r in battle_rows if r["verdict"] == "ERROR")))
    lines.append("- 判定分布: %s" % ", ".join("%s=%d" % pair for pair in sorted(verdicts.items())))
    lines.append("- 单侧验证场次: %d / %d" % (one_sided_total, len(battle_rows)))
    if one_sided_total:
        lines.append("  - 这些回放没有 OpeningMessage 侧车数据库，只能完整还原**已记录对手**（combatant 1）的"
                     "阵容/技能/属性。单侧模式下：血量曲线与胜负通道不可验证（已跳过）；"
                     "冷却/急速/缓慢/冰冻等受对手干预的曲线差异标记为 `interference`；"
                     "其余差异按出手卡牌归因。")
    lines.append("")
    lines.append("## 最可疑的卡牌 / 技能\n")
    lines.append("按受影响场次排序（仅统计可验证差异）。`onset中位帧` 是冷却类价值不匹配最早出现的帧，"
                 "帧号越小说明越像代码问题（开局即错），帧号大且随场次分散更像对手干预造成的漂移。\n")
    lines.append("| 类型 | 卡牌/技能 | 模板 | 场次 | 差异明细（通道 状态 类别 属性 = 次数） | onset中位帧 |")
    lines.append("|---|---|---|---:|---|---:|")
    for stats in aggregation["templates"][:40]:
        label = stats["name"] or (stats["template"][:8] if len(stats["template"]) > 20
                                  else stats["template"])
        detail = "; ".join("{0} = {1}".format(key, count) for key, count in stats["byKind"][:6])
        onset = median(stats["onsetFrames"])
        lines.append("| %s | %s | `%s` | %d | %s | %s |" % (
            stats.get("kind") or "?", label, stats["template"][:8], stats["battles"], detail,
            "-" if onset is None else int(onset)))
    lines.append("")
    lines.append("## 模板覆盖缺口（未进入映射表，无法验证其效果）\n")
    unmapped_totals = Counter()
    for result in results:
        for template in result.get("unmapped") or []:
            unmapped_totals[template] += 1
    if unmapped_totals:
        lines.append("| 模板 | 出现场次 |")
        lines.append("|---|---:|")
        for template, count in unmapped_totals.most_common(30):
            label = template_names.get(template, (None, None))[0] or template
            lines.append("| %s (`%s`) | %d |" % (label, template[:8], count))
    else:
        lines.append("无。")
    lines.append("")
    lines.append("## 模拟器映射警告（按模式聚合）\n")
    if aggregation["warningPatterns"]:
        lines.append("| 警告 | 出现场次 |")
        lines.append("|---|---:|")
        for pattern, count in aggregation["warningPatterns"][:30]:
            lines.append("| %s | %d |" % (pattern.replace("|", "\\|"), count))
    else:
        lines.append("无。")
    lines.append("")
    lines.append("## 每场结果\n")
    lines.append("| battleId | 模式 | 判定 | 帧 | 可验证差异 | 干预/结构 | 跳过 |")
    lines.append("|---|---|---|---:|---:|---:|---:|")
    for row in battle_rows:
        counts = row.get("counts") or {}
        verifiable = sum(count for status, count in counts.items()
                         if status in ("missing", "extra", "value-mismatch", "time-mismatch"))
        soft = counts.get("interference", 0) + counts.get("structural", 0)
        skipped = counts.get("skipped", 0)
        error = " ⚠️ %s" % row["error"] if row.get("error") else ""
        lines.append("| %s | %s | %s%s | %s | %d | %d | %d |" % (
            row["battleId"], row.get("mode") or "-", row["verdict"], error,
            row.get("frames") or "-", verifiable, soft, skipped))
    pathlib.Path(path).write_text("\n".join(lines) + "\n", encoding="utf-8")


def main(argv=None):
    parser = argparse.ArgumentParser(description="Batch-audit engine vs BPP replays")
    parser.add_argument("--replays", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--db", default=None, help="BPP bazaarplusplus.db path (sidecar)")
    parser.add_argument("--contract", default=str(DEFAULT_CONTRACT))
    parser.add_argument("--templates", default=str(DEFAULT_TEMPLATES))
    parser.add_argument("--catalog", default=str(DEFAULT_CATALOG))
    parser.add_argument("--engine-exe", default=str(DEFAULT_ENGINE_EXE))
    parser.add_argument("--battle", action="append", default=[], help="limit to battleId(s)")
    parser.add_argument("--limit", type=int, default=None)
    parser.add_argument("--workers", type=int, default=os.cpu_count() or 4)
    parser.add_argument("--artifacts", choices=("report", "diffs", "all", "none"), default="report",
                        help="report: comparator report per battle; diffs: + classified rows; "
                             "all: + native/fixture/sim; none: summary only")
    parser.add_argument("--window-frames", type=int, default=0)
    args = parser.parse_args(argv)

    replay_dir = pathlib.Path(args.replays)
    out_dir = pathlib.Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)
    replays = sorted(replay_dir.rglob("*.payload.mpack.gz"))
    if args.battle:
        wanted = set(args.battle)
        replays = [p for p in replays if p.name.split(".")[0] in wanted]
    if args.limit:
        replays = replays[:args.limit]
    if not replays:
        print("no replays found in %s" % replay_dir)
        return 2
    if not pathlib.Path(args.engine_exe).exists():
        print("EngineTests.exe missing at %s; run tools/actionaxis_plugin/compile_tests.sh first"
              % args.engine_exe)
        return 2

    template_names = load_template_names(args.catalog, args.templates)
    mapped_ids = load_mapped_template_ids(args.templates)
    ability_targets = ledger.load_ability_targets(args.templates)
    # Partial crit alignment defeats the comparator's critDivergence/critCascade
    # absorption (mixed crit patterns lose the clean 2x signature), so the tape
    # ships crit bits but the audit pipeline defaults them off; targets, random
    # enchants and same-frame ready order stay on (2026-10-26, 211-replay A/B).
    os.environ.setdefault("RR_DISABLE", "criticals")
    tasks = [(str(path), str(out_dir), args.contract, args.templates, args.catalog,
              args.engine_exe, args.db, args.artifacts, args.window_frames, mapped_ids,
              ability_targets)
             for path in replays]
    started = time.time()
    if args.workers <= 1:
        results = []
        for index, task in enumerate(tasks, 1):
            result = process_one(task)
            results.append(result)
            status = result.get("verdict") or result.get("error") or "?"
            print("[%d/%d] %s -> %s" % (index, len(tasks),
                                        pathlib.Path(task[0]).name.split(".")[0][:8], status),
                  flush=True)
    else:
        from concurrent.futures import ProcessPoolExecutor, as_completed
        results = []
        with ProcessPoolExecutor(max_workers=args.workers) as pool:
            futures = {pool.submit(process_one, task): task for task in tasks}
            for index, future in enumerate(as_completed(futures), 1):
                result = future.result()
                results.append(result)
                status = result.get("verdict") or result.get("error") or "?"
                print("[%d/%d] %s -> %s" % (index, len(tasks),
                                            pathlib.Path(futures[future][0]).name.split(".")[0][:8],
                                            status), flush=True)
    print("processed %d replays in %.1fs" % (len(results), time.time() - started))

    aggregation = aggregate(results, template_names)
    one_sided_total = sum(1 for row in aggregation["battles"] if row.get("mode") == "one-sided")
    summary_json = {
        "args": {key: str(value) for key, value in vars(args).items()},
        "battles": aggregation["battles"],
        "templates": aggregation["templates"],
        "warningPatterns": aggregation["warningPatterns"],
        "oneSidedBattles": one_sided_total,
    }
    (out_dir / "summary.json").write_text(
        json.dumps(summary_json, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    write_markdown(out_dir / "summary.md", aggregation, results, one_sided_total, args)
    print("summary -> %s" % (out_dir / "summary.md"))
    verdicts = Counter(row["verdict"] for row in aggregation["battles"])
    print("verdicts: %s" % ", ".join("%s=%d" % pair for pair in sorted(verdicts.items())))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
