#!/usr/bin/env python3
"""Triage CombatReplays_3 replays into 核对正确 / 核对失败 / 无法使用.

无法使用  — one-sided battles: the replay carries only one board, so the
            health/outcome channels cannot be verified at all (data incomplete).
核对正确  — two-sided battles whose verifiable diffs are zero.
核对失败  — two-sided battles that still diverge after the random-outcome
            scripting (crits / random targets / firing order).

Files are moved (not copied); manifests record the verdict basis.
"""
from __future__ import annotations

import argparse
import json
import pathlib
import shutil
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
HARD_STATUSES = ("missing", "extra", "value-mismatch", "time-mismatch")


def battle_reason(art_dir):
    report = art_dir / "report.json"
    if not report.exists():
        return None
    try:
        doc = json.loads(report.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None
    diffs = [row for row in doc.get("diffs") or []
             if row.get("status") in HARD_STATUSES]
    if not diffs:
        return None
    first = diffs[0]
    return "首差异 帧%s %s %s: %s（共 %d 个硬差异）" % (
        first.get("frame"), first.get("channel"), first.get("status"),
        (first.get("note") or "")[:80], len(diffs))


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--summary", nargs="+", required=True,
                        help="one or more batch summary.json files (merged)")
    parser.add_argument("--replays", default=str(ROOT / "CombatReplays_3"))
    parser.add_argument("--artifacts-root", default=None)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args(argv)

    rows = []
    for path in args.summary:
        doc = json.loads(pathlib.Path(path).read_text(encoding="utf-8"))
        rows.extend(doc.get("battles") or [])
    replays = pathlib.Path(args.replays)
    # Files may already sit in a previous triage's subfolders; find them anywhere.
    index = {}
    for existing in replays.rglob("*.payload.mpack.gz"):
        index[existing.name.split(".")[0]] = existing
    ok_dir = replays / "核对正确"
    bad_dir = replays / "核对失败"
    unusable_dir = replays / "无法使用"
    if not args.dry_run:
        for d in (ok_dir, bad_dir, unusable_dir):
            d.mkdir(exist_ok=True)

    counts = {"核对正确": 0, "核对失败": 0, "无法使用": 0}
    missing_files = 0
    bad_rows, unusable_rows = [], []
    for row in rows:
        battle_id = row.get("battleId")
        verdict = row.get("verdict")
        mode = row.get("mode")
        if not battle_id:
            continue
        src = index.get(battle_id)
        if src is None:
            missing_files += 1
            continue
        if mode == "one-sided":
            target = unusable_dir
            counts["无法使用"] += 1
            counts_hard = row.get("counts") or {}
            hard = sum(counts_hard.get(k, 0) for k in HARD_STATUSES)
            unusable_rows.append((battle_id, verdict, "单侧：回放只含一方卡牌，血量/胜负通道不可验证；可验证硬差异 %d" % hard))
        elif verdict == "MATCH":
            target = ok_dir
            counts["核对正确"] += 1
        else:
            target = bad_dir
            counts["核对失败"] += 1
            reason = row.get("error")
            if not reason and args.artifacts_root:
                reason = battle_reason(pathlib.Path(args.artifacts_root) / battle_id)
            if not reason:
                c = row.get("counts") or {}
                hard = sum(c.get(k, 0) for k in HARD_STATUSES)
                reason = "可验证硬差异 %d（%s）" % (
                    hard, "、".join("%s=%s" % (k, c[k]) for k in HARD_STATUSES if c.get(k)))
            bad_rows.append((battle_id, verdict, mode, reason))
        if not args.dry_run:
            shutil.move(str(src), str(target / src.name))

    if not args.dry_run:
        if bad_rows:
            lines = ["# 核对失败原因清单", "",
                     "判定基线：%s" % ", ".join(args.summary), "",
                     "双测模式（双方阵容已知）且随机结果已按回放事实脚本化（暴击/随机目标/同帧点火顺序）后仍存在硬差异。", "",
                     "| battleId | 判定 | 模式 | 原因 |", "|---|---|---|---|"]
            for battle_id, verdict, mode, reason in bad_rows:
                lines.append("| %s | %s | %s | %s |" % (battle_id, verdict, mode or "?", reason))
            (bad_dir / "_核对失败原因.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
        if unusable_rows:
            lines = ["# 无法使用原因清单", "",
                     "这些回放没有对应 OpeningMessage 侧车（bazaarplusplus.db 无该 battle_id），",
                     "回放载荷只含一方完整阵容；血量曲线与胜负通道天然不可验证，按数据不全处理。", "",
                     "| battleId | 判定 | 原因 |", "|---|---|---|"]
            for battle_id, verdict, reason in unusable_rows:
                lines.append("| %s | %s | %s |" % (battle_id, verdict, reason))
            (unusable_dir / "_无法使用原因.md").write_text("\n".join(lines) + "\n", encoding="utf-8")

    print("核对正确: %d, 核对失败: %d, 无法使用: %d, 找不到文件: %d%s" % (
        counts["核对正确"], counts["核对失败"], counts["无法使用"],
        missing_files, "（dry-run）" if args.dry_run else ""))
    return 0


if __name__ == "__main__":
    sys.exit(main())
