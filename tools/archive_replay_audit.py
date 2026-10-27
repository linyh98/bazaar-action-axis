"""Archive audited replays into 核对正确/ 核对失败/ 无法使用/ with a cause report.

Categorization (by data completeness first, then verdict):
  - one-sided (only one board recorded in the replay/sidecar)  -> 无法使用/
    regardless of the partial-check verdict; the report notes which passed.
  - two-sided MATCH -> 核对正确/
  - two-sided DIFF  -> 核对失败/
Idempotent: scans the replay root and all three folders for the files.
"""
from __future__ import annotations

import collections
import json
import pathlib
import shutil

ROOT = pathlib.Path(__file__).resolve().parents[1]
REPLAYS = ROOT / "CombatReplays_1"
AUDIT = ROOT / "CombatReplays_1_audit"
SIM_CARD_ATTRIBUTES = {"Cooldown", "Ammo"}
SIM_EFFECT_KINDS = {"auraApplied", "auraRemoved"}
INTERFERENCE = {"Cooldown", "Haste", "Slow", "Freeze", "Charge"}


def hard_rows(path):
    rows = json.load(open(path, encoding="utf-8"))
    out = []
    for row in rows:
        if not row.get("verifiable"):
            continue
        status, attr, kind = row["status"], row.get("attribute"), row.get("kind")
        if kind == "death":
            continue
        if status == "missing" and (
                (row["channel"] == "cards" and attr not in SIM_CARD_ATTRIBUTES)
                or (row["channel"] == "effects" and kind in SIM_EFFECT_KINDS)):
            continue
        if row["channel"] == "cards" and attr in INTERFERENCE \
                and status in ("value-mismatch", "missing", "extra"):
            continue
        out.append(row)
    return out


def main():
    summary = json.load(open(AUDIT / "summary.json", encoding="utf-8"))
    ok_dir = REPLAYS / "核对正确"
    bad_dir = REPLAYS / "核对失败"
    unusable_dir = REPLAYS / "无法使用"
    for d in (ok_dir, bad_dir, unusable_dir):
        d.mkdir(exist_ok=True)

    matches = [r for r in summary["battles"] if r["verdict"] == "MATCH"]
    diffs = [r for r in summary["battles"] if r["verdict"] == "DIFF"]
    errors = [r for r in summary["battles"] if r["verdict"] not in ("MATCH", "DIFF")]
    one_sided = [r for r in summary["battles"] if r["mode"] == "one-sided"]
    two_sided = [r for r in summary["battles"] if r["mode"] == "two-sided"]

    lines = ["# CombatReplays_1 核对结果", ""]
    lines.append("- 总场次 %d：双侧验证 %d 场，单侧验证（仅一方卡牌）%d 场" %
                 (len(summary["battles"]), len(two_sided), len(one_sided)))
    lines.append("- 双测判定：MATCH %d，DIFF %d，ERROR %d" % (
        sum(1 for r in two_sided if r["verdict"] == "MATCH"),
        sum(1 for r in two_sided if r["verdict"] == "DIFF"),
        sum(1 for r in two_sided if r["verdict"] not in ("MATCH", "DIFF"))))
    lines.append("- 单侧场次全部归入 无法使用/（数据不全，仅验证过已记录方的部分通道；"
                 "其中 %d 场连部分验证也未通过）" %
                 sum(1 for r in one_sided if r["verdict"] != "MATCH"))
    lines.append("")

    lines.append("## 核对正确（%d 场，双侧全通道验证通过）" %
                 sum(1 for r in two_sided if r["verdict"] == "MATCH"))
    for r in two_sided:
        if r["verdict"] == "MATCH":
            lines.append("- %s" % r["battleId"])

    lines.append("")
    lines.append("## 核对失败（%d 场，双侧验证仍有硬差异）" %
                 sum(1 for r in two_sided if r["verdict"] == "DIFF"))
    lines.append("")
    lines.append("| 对局 | 硬差异 | 主要差异卡牌 |")
    lines.append("|---|---:|---|")
    for r in sorted(two_sided, key=lambda x: x["battleId"]):
        if r["verdict"] != "DIFF":
            continue
        bid = r["battleId"]
        dpath = AUDIT / bid / "diffs.json"
        hard = hard_rows(dpath) if dpath.exists() else []
        tmpl = collections.Counter((h.get("template") or "?")[:8] for h in hard)
        top = "；".join("%s(%d)" % (t, c) for t, c in tmpl.most_common(3)) or "-"
        lines.append("| %s | %d | %s |" % (bid, len(hard), top))

    lines.append("")
    lines.append("## 无法使用（%d 场，回放只有一方卡牌）" % len(one_sided))
    lines.append("")
    lines.append("| 对局 | 部分验证结果 | 硬差异 | 主要差异卡牌 |")
    lines.append("|---|---|---:|---|")
    for r in sorted(one_sided, key=lambda x: x["battleId"]):
        bid = r["battleId"]
        dpath = AUDIT / bid / "diffs.json"
        hard = hard_rows(dpath) if dpath.exists() else []
        tmpl = collections.Counter((h.get("template") or "?")[:8] for h in hard)
        top = "；".join("%s(%d)" % (t, c) for t, c in tmpl.most_common(3)) or "-"
        lines.append("| %s | %s | %d | %s |" % (bid, r["verdict"], len(hard), top))

    lines += ["", "## 说明", "",
              "- 硬差异 = 排除对手干预噪声（单侧的冷却/急速/缓慢/冰冻曲线）、结构导出缺口、"
              "死亡事件与随机目标 UNVERIFIED 后仍未对上的通道行。",
              "- 核对失败场次的差异来源：回放不含随机种子，随机附魔/随机目标/随机变形与暴击分支"
              "无法复现（可改为提示对齐：让引擎直接采用回放里已记录的随机结果）；"
              "新版本卡牌逐卡原生规则长尾；血量通道对早期分叉级联敏感。",
              "- 无法使用场次并非“验证错误”：只是回放未记录玩家侧阵容，无法做双侧全通道核对。"]
    (AUDIT / "核对结果.md").write_text("\n".join(lines) + "\n", encoding="utf-8")

    moved = collections.Counter()
    for r in summary["battles"]:
        bid = r["battleId"]
        name = "%s.payload.mpack.gz" % bid
        src = None
        for folder in (REPLAYS, ok_dir, bad_dir, unusable_dir):
            p = folder / name
            if p.exists():
                src = p
                break
        if src is None:
            continue
        if r["mode"] == "one-sided":
            dst = unusable_dir
        elif r["verdict"] == "MATCH":
            dst = ok_dir
        else:
            dst = bad_dir
        if src.parent != dst:
            shutil.move(str(src), str(dst / name))
        moved[dst.name] += 1
    print("moved: %s; report -> %s" % (dict(moved), AUDIT / "核对结果.md"))


if __name__ == "__main__":
    main()
