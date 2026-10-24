"""Export a normalized per-frame event ledger from a local BPP replay.

The replay payload keeps native combat frames as
``[[events], ..., {instanceId: [instanceId, previous, current]}]`` with 50 ms
frames. This tool flattens them into {frame, time, code, ability, action,
sources, targets, extra} records so the simulator can be compared event by
event instead of only through the reduced health fixture.

Usage:
    python tools/native_replay_ledger.py <replay.payload.mpack.gz> [--json out.json]
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "captures" / "diagnostics"))
from read_bpp_replay import read_replay  # noqa: E402

FRAME_SECONDS = 0.05


def combat_frames(replay: dict) -> list:
    combat = replay.get("CombatMessageBytes")
    if not isinstance(combat, list) or not combat or not combat[0]:
        raise SystemExit("回放缺少 CombatMessageBytes")
    frames = combat[0][0]
    if not isinstance(frames, list):
        raise SystemExit("CombatMessageBytes 中没有帧数组")
    return frames


def normalize_event(raw) -> dict:
    """Event tuple is [code, payload]; payload order mirrors the native DTO."""
    if not isinstance(raw, list) or not raw:
        return {"code": None, "raw": raw}
    code = raw[0]
    payload = raw[1] if len(raw) > 1 else None
    record = {"code": code, "raw": payload}
    if isinstance(payload, list) and len(payload) >= 6:
        record["ability"] = payload[0]
        record["effect"] = payload[1]
        record["action"] = payload[2]
        record["value"] = payload[3]
        record["sources"] = payload[4]
        record["targets"] = payload[5]
        if len(payload) > 6:
            record["extra"] = payload[6]
    return record


def build_ledger(replay: dict) -> dict:
    frames = combat_frames(replay)
    events = []
    cooldowns = []
    for index, frame in enumerate(frames):
        if not isinstance(frame, list):
            continue
        frame_events = frame[0] if frame and isinstance(frame[0], list) else []
        for raw in frame_events:
            event = normalize_event(raw)
            event["frame"] = index
            event["time"] = round(index * FRAME_SECONDS, 4)
            events.append(event)
        if len(frame) > 3 and isinstance(frame[3], dict):
            for instance, change in frame[3].items():
                cooldowns.append({"frame": index, "time": round(index * FRAME_SECONDS, 4),
                                  "instance": instance, "change": change})
    return {
        "battleId": replay.get("BattleId"),
        "version": replay.get("Version"),
        "frameSeconds": FRAME_SECONDS,
        "frameCount": len(frames),
        "eventCount": len(events),
        "events": events,
        "cooldownChanges": cooldowns,
    }


def main() -> int:
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    replay = read_replay(sys.argv[1])
    ledger = build_ledger(replay)
    if "--json" in sys.argv:
        target = Path(sys.argv[sys.argv.index("--json") + 1])
        target.write_text(json.dumps(ledger, ensure_ascii=False, indent=1), encoding="utf-8")
        print(f"写入 {target}：{ledger['frameCount']} 帧 / {ledger['eventCount']} 事件")
        return 0
    print(f"battleId={ledger['battleId']} frames={ledger['frameCount']} events={ledger['eventCount']}")
    for event in ledger["events"][:20]:
        print(event)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
