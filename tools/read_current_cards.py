"""Read the current The Bazaar shop cards via hover tooltips + Windows OCR.

Usage:
    python tools/read_current_cards.py
    python tools/read_current_cards.py --from-captures .
    python tools/read_current_cards.py --capture-only
"""
from __future__ import annotations

import argparse
import ctypes
import ctypes.wintypes as wt
import json
import re
import subprocess
import time
from datetime import datetime, timezone
from pathlib import Path

from PIL import Image, ImageGrab

ROOT = Path(__file__).resolve().parents[1]
REF_W, REF_H = 1616, 1063
SLOTS = [(560, 610), (640, 630), (742, 630), (866, 630), (1056, 630)]
TOOLTIP_BOXES = [
    (650, 200, 1140, 1050),
    (760, 240, 1240, 1000),
    (850, 100, 1310, 1020),
    (1080, 120, 1550, 1020),
    (1150, 180, 1570, 1050),
]

KEYWORDS = {
    "暴击率",
    "灼烧",
    "护盾",
    "剧毒",
    "减速",
    "多重释放",
    "弹药",
    "装填",
    "回复",
    "再生",
    "获得",
    "造成",
    "如果",
    "相邻",
    "每有",
    "每天",
    "出售",
    "使用",
    "此物品",
    "小型",
    "中型",
    "大型",
    "服饰",
    "工具",
    "药水",
    "伙伴",
    "巨龙",
    "原料",
    "武器",
    "饰品",
}
ENCHANTMENTS = {
    "毒素",
    "黄金",
    "沉重",
    "寒冰",
    "疾速",
    "护盾",
    "回复",
    "炽焰",
    "闪亮",
    "致命",
    "辉煌",
    "黑曜石",
    "长青",
}


def normalize_name(text: str) -> str:
    return re.sub(r"\s+", "", text)


def plan_slots(width: float, height: float):
    return [(x * width / REF_W, y * height / REF_H) for x, y in SLOTS]


def plan_boxes(width: float, height: float):
    return [
        (
            round(left * width / REF_W),
            round(top * height / REF_H),
            round(right * width / REF_W),
            round(bottom * height / REF_H),
        )
        for left, top, right, bottom in TOOLTIP_BOXES
    ]


def _is_title_candidate(line: str) -> bool:
    text = normalize_name(line)
    if not (2 <= len(text) <= 12):
        return False
    if not re.fullmatch(r"[一-鿿]+", text):
        return False
    if text in ENCHANTMENTS:
        return False
    return not any(keyword in text for keyword in KEYWORDS)


def pick_name(lines, known_names) -> str:
    joined = "".join(normalize_name(line) for line in lines)
    for name in sorted(known_names, key=len, reverse=True):
        normalized = normalize_name(name)
        if normalized and normalized in joined:
            return name
    for line in lines:
        if _is_title_candidate(line):
            return normalize_name(line)
    return ""


def load_known_names():
    names = set()
    items = json.loads((ROOT / "data/items_raw.json").read_text(encoding="utf-8"))
    for item in items:
        if item.get("nameCn"):
            names.add(item["nameCn"])
    cards = json.loads((ROOT / "src/data/cards.json").read_text(encoding="utf-8"))
    for card in cards:
        if card.get("nameCn"):
            names.add(card["nameCn"])
    return names


def match_id(name: str):
    items = json.loads((ROOT / "data/items_raw.json").read_text(encoding="utf-8"))
    for item in items:
        if item.get("nameCn") == name:
            return item.get("id")
    return None


class RECT(ctypes.Structure):
    _fields_ = [
        ("left", wt.LONG),
        ("top", wt.LONG),
        ("right", wt.LONG),
        ("bottom", wt.LONG),
    ]


class MOUSEINPUT(ctypes.Structure):
    _fields_ = [
        ("dx", wt.LONG),
        ("dy", wt.LONG),
        ("mouseData", wt.DWORD),
        ("dwFlags", wt.DWORD),
        ("time", wt.DWORD),
        ("dwExtraInfo", ctypes.POINTER(wt.ULONG)),
    ]


class INPUT(ctypes.Structure):
    _fields_ = [("type", wt.DWORD), ("mi", MOUSEINPUT)]


def find_window(title: str = "The Bazaar"):
    user32 = ctypes.windll.user32
    found = []

    def callback(hwnd, _):
        if not user32.IsWindowVisible(hwnd):
            return True
        length = user32.GetWindowTextLengthW(hwnd)
        if length == 0:
            return True
        buffer = ctypes.create_unicode_buffer(length + 1)
        user32.GetWindowTextW(hwnd, buffer, length + 1)
        if title.lower() in buffer.value.lower():
            pid = wt.DWORD()
            user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
            found.append((hwnd, buffer.value, pid.value))
        return True

    user32.EnumWindows(
        ctypes.WINFUNCTYPE(ctypes.c_bool, wt.HWND, wt.LPARAM)(callback), 0
    )
    return found[0] if found else None


def force_foreground(hwnd: int) -> None:
    user32 = ctypes.windll.user32
    user32.ShowWindow(hwnd, 9)
    user32.SetWindowPos(
        hwnd,
        ctypes.c_void_p(-1),
        0,
        0,
        0,
        0,
        0x0001 | 0x0002 | 0x0040,
    )
    user32.keybd_event(0x12, 0, 0, 0)
    user32.keybd_event(0x12, 0, 2, 0)
    user32.BringWindowToTop(hwnd)

    foreground = user32.GetForegroundWindow()
    if foreground and foreground != hwnd:
        foreground_pid = wt.DWORD()
        target_pid = wt.DWORD()
        foreground_thread = user32.GetWindowThreadProcessId(
            foreground, ctypes.byref(foreground_pid)
        )
        target_thread = user32.GetWindowThreadProcessId(
            hwnd, ctypes.byref(target_pid)
        )
        if foreground_thread and target_thread and foreground_thread != target_thread:
            user32.AttachThreadInput(foreground_thread, target_thread, True)
            user32.SetForegroundWindow(hwnd)
            user32.SetActiveWindow(hwnd)
            user32.SetFocus(hwnd)
            user32.AttachThreadInput(foreground_thread, target_thread, False)
            return

    user32.SetForegroundWindow(hwnd)
    user32.SetActiveWindow(hwnd)
    user32.SetFocus(hwnd)


def restore_window(hwnd: int) -> None:
    ctypes.windll.user32.SetWindowPos(
        hwnd,
        ctypes.c_void_p(-2),
        0,
        0,
        0,
        0,
        0x0001 | 0x0002 | 0x0040,
    )


def send_move(x: int, y: int) -> None:
    user32 = ctypes.windll.user32
    screen_w = user32.GetSystemMetrics(0)
    screen_h = user32.GetSystemMetrics(1)
    nx = int(x * 65535 / (screen_w - 1))
    ny = int(y * 65535 / (screen_h - 1))
    user32.SendInput(
        1,
        ctypes.byref(
            INPUT(0, MOUSEINPUT(nx, ny, 0, 0x0001 | 0x8000, 0, None))
        ),
        ctypes.sizeof(INPUT),
    )


def capture_window(hwnd: int, path: Path) -> Image.Image:
    user32 = ctypes.windll.user32
    rect = RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(rect))
    image = ImageGrab.grab(
        bbox=(rect.left, rect.top, rect.right, rect.bottom), all_screens=True
    )
    image.save(path)
    return image


def ocr_image(path: Path, script: Path):
    out_file = path.with_suffix(".txt")
    subprocess.run(
        [
            "powershell",
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            str(script),
            "-Path",
            str(path.resolve()),
            "-OutFile",
            str(out_file.resolve()),
        ],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=60,
    )
    if not out_file.exists():
        return []
    text = out_file.read_text(encoding="utf-8-sig", errors="replace")
    return [line.strip() for line in text.splitlines() if line.strip()]


def result_for(slot: int, lines, known) -> dict:
    name = pick_name(lines, known) if lines else ""
    return {
        "slot": slot,
        "name": name,
        "known": name in known if name else False,
        "matchedId": match_id(name) if name else None,
        "ocrLines": lines,
    }


def read_live(hwnd: int, out_dir: Path, wait: float, ocr_script: Path, capture_only: bool):
    user32 = ctypes.windll.user32
    force_foreground(hwnd)
    time.sleep(1.0)

    rect = RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(rect))
    width = rect.right - rect.left
    height = rect.bottom - rect.top
    ox, oy = rect.left, rect.top

    slots = plan_slots(width, height)
    boxes = plan_boxes(width, height)
    known = load_known_names()
    cards = []

    for index, ((slot_x, slot_y), box) in enumerate(zip(slots, boxes), 1):
        for offset_x, offset_y in [(-60, -40), (-40, -20), (-20, 0), (0, 0)]:
            send_move(ox + int(slot_x) + offset_x, oy + int(slot_y) + offset_y)
            time.sleep(0.25)
        time.sleep(wait)

        full_path = out_dir / f"slot{index}.png"
        image = capture_window(hwnd, full_path)
        crop = image.crop(box)
        crop_path = out_dir / f"slot{index}_tooltip.png"
        crop.save(crop_path)

        lines = [] if capture_only else ocr_image(crop_path, ocr_script)
        card = result_for(index, lines, known)
        card["screenshot"] = str(full_path)
        card["tooltip"] = str(crop_path)
        cards.append(card)

        send_move(ox + 10, oy + 10)
        time.sleep(0.6)

    restore_window(hwnd)
    return cards


def read_from_captures(source: Path, out_dir: Path, ocr_script: Path, capture_only: bool):
    known = load_known_names()
    files = sorted(source.glob("hover_card*.png"))
    if not files:
        files = sorted(source.glob("slot[0-9]*.png"))
    cards = []
    for index, path in enumerate(files, 1):
        image = Image.open(path)
        boxes = plan_boxes(*image.size)
        box = boxes[min(index - 1, len(boxes) - 1)]
        crop = image.crop(box)
        crop_path = out_dir / f"from_{index}_tooltip.png"
        crop.save(crop_path)
        lines = [] if capture_only else ocr_image(crop_path, ocr_script)
        card = result_for(index, lines, known)
        card["screenshot"] = str(path)
        card["tooltip"] = str(crop_path)
        cards.append(card)
    return cards


def main() -> int:
    parser = argparse.ArgumentParser(description="Read current Bazaar shop cards")
    parser.add_argument("--out-dir", default=str(ROOT / "captures" / "live"))
    parser.add_argument("--from-captures", default=None)
    parser.add_argument("--capture-only", action="store_true")
    parser.add_argument("--wait", type=float, default=4.0)
    parser.add_argument("--ocr-script", default=str(ROOT / "tools" / "win_ocr.ps1"))
    args = parser.parse_args()

    out_dir = Path(args.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    ocr_script = Path(args.ocr_script)

    if args.from_captures:
        cards = read_from_captures(
            Path(args.from_captures), out_dir, ocr_script, args.capture_only
        )
        payload = {"mode": "captures", "source": args.from_captures, "cards": cards}
    else:
        window = find_window()
        if not window:
            print("The Bazaar window not found")
            return 1
        hwnd, title, pid = window
        cards = read_live(hwnd, out_dir, args.wait, ocr_script, args.capture_only)
        payload = {
            "mode": "live",
            "window": {"pid": pid, "title": title},
            "capturedAt": datetime.now(timezone.utc).isoformat(),
            "cards": cards,
        }

    result_path = out_dir / "live_cards.json"
    result_path.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    for card in cards:
        print(card["slot"], card["name"] or "(no OCR)", card["matchedId"] or "")
    print(f"saved {result_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
