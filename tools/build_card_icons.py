"""Download card art and convert it to 96x96 PNG icons.

Usage:
    python tools/build_card_icons.py --dry-run --limit 5
    python tools/build_card_icons.py
    python tools/build_card_icons.py --force --concurrency 8
"""
from __future__ import annotations

import argparse
import io
import json
import re
import time
import urllib.request
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

from PIL import Image, ImageOps

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_INPUT = ROOT / "src" / "data" / "cards.json"
DEFAULT_OUT = ROOT / "captures" / "card_icons"
USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) BazaarActionAxis/0.1"
UNSAFE = re.compile(r"[^A-Za-z0-9-]")


def plan_icons(cards):
    """Return one {base_item_id, image} entry per unique baseItemId with art."""
    seen = set()
    plan = []
    for card in cards:
        base_item_id = card.get("baseItemId")
        image = card.get("image")
        if not base_item_id or not image or base_item_id in seen:
            continue
        seen.add(base_item_id)
        plan.append({"base_item_id": base_item_id, "image": image})
    return plan


def icon_filename(base_item_id: str) -> str:
    return UNSAFE.sub("_", base_item_id) + ".png"


def convert_to_icon(data: bytes, size: int = 96) -> bytes:
    image = Image.open(io.BytesIO(data)).convert("RGBA")
    icon = ImageOps.fit(image, (size, size), method=Image.LANCZOS)
    out = io.BytesIO()
    icon.save(out, format="PNG", optimize=True)
    return out.getvalue()


def download(url: str, retries: int = 3) -> bytes:
    last_error = None
    for attempt in range(retries):
        try:
            request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
            with urllib.request.urlopen(request, timeout=30) as response:
                return response.read()
        except Exception as error:  # noqa: BLE001 - report and retry
            last_error = error
            if attempt + 1 < retries:
                time.sleep(1 + attempt)
    raise RuntimeError(str(last_error))


def main() -> None:
    parser = argparse.ArgumentParser(description="Build 96x96 card icons")
    parser.add_argument("--input", default=str(DEFAULT_INPUT))
    parser.add_argument("--out", default=str(DEFAULT_OUT))
    parser.add_argument("--limit", type=int, default=None)
    parser.add_argument("--concurrency", type=int, default=8)
    parser.add_argument("--force", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    cards = json.loads(Path(args.input).read_text(encoding="utf-8"))
    plan = plan_icons(cards)
    if args.limit is not None:
        plan = plan[: args.limit]

    if args.dry_run:
        for entry in plan:
            print(entry["image"])
        print(f"planned {len(plan)} icons")
        return

    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)

    def work(entry):
        dest = out_dir / icon_filename(entry["base_item_id"])
        if dest.exists() and not args.force:
            return entry["base_item_id"], "kept"
        try:
            data = download(entry["image"])
            dest.write_bytes(convert_to_icon(data))
            return entry["base_item_id"], "ok"
        except Exception as error:  # noqa: BLE001 - report and continue
            return entry["base_item_id"], f"failed: {error}"

    with ThreadPoolExecutor(max_workers=max(1, args.concurrency)) as executor:
        results = list(executor.map(work, plan))

    ok = sum(1 for _, status in results if status in {"ok", "kept"})
    failed = [(key, status) for key, status in results if status.startswith("failed")]
    print(f"icons ready {ok}/{len(results)} in {out_dir}")
    for key, status in failed[:20]:
        print(f"  {key}: {status}")
    if len(failed) > 20:
        print(f"  ... and {len(failed) - 20} more")


if __name__ == "__main__":
    main()
