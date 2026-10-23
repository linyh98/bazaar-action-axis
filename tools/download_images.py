"""Download card art from the codex CDN into public/images.

Usage:
    python tools/download_images.py --dry-run --limit 5
    python tools/download_images.py
"""
from __future__ import annotations

import argparse
import json
import time
import urllib.request
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_INPUT = ROOT / "data" / "items_raw.json"
DEFAULT_OUT = ROOT / "public" / "images"
USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) BazaarSimulator/0.1"


def plan_downloads(items, limit=None):
    """Return unique {id, image} pairs in input order."""
    seen = set()
    plan = []
    for item in items:
        card_id = item.get("id")
        image = item.get("image")
        if not card_id or not image or card_id in seen:
            continue
        seen.add(card_id)
        plan.append({"id": card_id, "image": image})
        if limit is not None and len(plan) >= limit:
            break
    return plan


def download(url: str, dest: Path, retries: int = 3) -> bool:
    for attempt in range(retries):
        try:
            request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
            with urllib.request.urlopen(request, timeout=30) as response:
                dest.write_bytes(response.read())
            return True
        except Exception:
            if attempt + 1 < retries:
                time.sleep(1 + attempt)
    return False


def main() -> None:
    parser = argparse.ArgumentParser(description="Download card art")
    parser.add_argument("--input", default=str(DEFAULT_INPUT))
    parser.add_argument("--out", default=str(DEFAULT_OUT))
    parser.add_argument("--limit", type=int, default=None)
    parser.add_argument("--concurrency", type=int, default=8)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    items = json.loads(Path(args.input).read_text(encoding="utf-8"))
    plan = plan_downloads(items, args.limit)

    if args.dry_run:
        for entry in plan:
            print(entry["image"])
        print(f"planned {len(plan)} downloads")
        return

    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    def work(entry):
        dest = out / f"{entry['id']}.avif"
        if dest.exists():
            return entry["id"], True
        return entry["id"], download(entry["image"], dest)

    with ThreadPoolExecutor(max_workers=max(1, args.concurrency)) as executor:
        results = list(executor.map(work, plan))

    ok = sum(1 for _, success in results if success)
    print(f"downloaded/kept {ok}/{len(results)}")


if __name__ == "__main__":
    main()
