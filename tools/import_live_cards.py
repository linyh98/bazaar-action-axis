"""Map a BPP live-cards dump to simulator card IDs.

Usage:
    python tools/import_live_cards.py
    python tools/import_live_cards.py --dump captures/live_cards_bpp.json
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def find_card(cards, template_id: str, tier: str):
    template_id = (template_id or "").lower()
    for card in cards:
        if (
            (card.get("baseItemId") or "").lower() == template_id
            and card.get("tier") == tier
        ):
            return card
    for card in cards:
        if (card.get("baseItemId") or "").lower() == template_id:
            return card
    return None


def convert(dump, cards):
    result = {"hero": dump.get("hero"), "shop": [], "board": [], "stash": []}
    for section in ("shop", "board", "stash"):
        for raw in dump.get(section, []):
            match = find_card(cards, raw.get("templateId", ""), raw.get("tier", ""))
            result[section].append(
                {
                    "templateId": raw.get("templateId"),
                    "tier": raw.get("tier"),
                    "cardId": match.get("id") if match else None,
                    "name": (
                        (match.get("nameCn") or match.get("name")) if match else None
                    ),
                    "found": match is not None,
                }
            )
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description="Map BPP live cards to simulator IDs")
    parser.add_argument("--dump", default=str(ROOT / "captures" / "live_cards_bpp.json"))
    parser.add_argument("--cards", default=str(ROOT / "src" / "data" / "cards.json"))
    parser.add_argument(
        "--out", default=str(ROOT / "captures" / "live_loadout.json")
    )
    args = parser.parse_args()

    dump = json.loads(Path(args.dump).read_text(encoding="utf-8"))
    cards = json.loads(Path(args.cards).read_text(encoding="utf-8"))
    result = convert(dump, cards)

    out_path = Path(args.out)
    out_path.write_text(
        json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    for section in ("shop", "board", "stash"):
        entries = result[section]
        print(
            f"{section}: {len(entries)} "
            f"({sum(1 for entry in entries if entry['found'])} mapped)"
        )
    print(f"saved {out_path}")


if __name__ == "__main__":
    main()
