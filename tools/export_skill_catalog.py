"""Export exact skill definitions and cached Chinese translations from The Bazaar.

No tooltip parsing or effect approximation occurs here. The complete native skill
template is retained so the simulator can explicitly reject unsupported behavior.
"""
from __future__ import annotations

import argparse
from contextlib import closing
import json
import sqlite3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_DB = (
    Path.home()
    / "AppData/LocalLow/Tempo Storm/The Bazaar/prod/cache/GameData.db"
)
DEFAULT_OUT = ROOT / "data/skills.json"


def _read_only_connection(path: Path) -> sqlite3.Connection:
    return sqlite3.connect(path.resolve().as_uri() + "?mode=ro", uri=True)


def read_translations(path: Path | None) -> dict[str, str]:
    """Read a downloaded SQLite locale; a missing locale has an English fallback."""
    if path is None or not path.is_file():
        return {}
    with closing(_read_only_connection(path)) as connection:
        return dict(connection.execute("SELECT hash, text FROM translation"))


def export_skills(db_path: Path, translations_path: Path | None = None) -> list[dict]:
    translations = read_translations(translations_path)
    skills = []
    with closing(_read_only_connection(db_path)) as connection:
        for row_id, raw in connection.execute("SELECT Id, Data FROM cards ORDER BY Id"):
            template = json.loads(raw)
            if template.get("$type") != "TCardSkill":
                continue
            localization = template.get("Localization") or {}
            title = localization.get("Title") or {}
            title_key = title.get("Key") or template.get("TranslationKey")
            tooltips = []
            for index, tooltip in enumerate(localization.get("Tooltips") or []):
                content = tooltip.get("Content") or {}
                tooltips.append(
                    {
                        "index": index,
                        "text": content.get("Text") or "",
                        "textCn": translations.get(content.get("Key")),
                        "tooltipType": tooltip.get("TooltipType"),
                    }
                )
            skills.append(
                {
                    "templateId": str(template.get("Id") or row_id).lower(),
                    "name": title.get("Text") or template.get("InternalName") or row_id,
                    "nameCn": translations.get(title_key),
                    "version": template.get("Version"),
                    "tooltips": tooltips,
                    "template": template,
                }
            )
    return sorted(skills, key=lambda skill: skill["templateId"])


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--db", type=Path, default=DEFAULT_DB)
    parser.add_argument(
        "--translations", type=Path,
        help="SQLite locale file; defaults to translations/zh-CN.bytes beside --db",
    )
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT)
    args = parser.parse_args()
    locale = args.translations or args.db.parent / "translations/zh-CN.bytes"
    skills = export_skills(args.db, locale)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(skills, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    tiers = sum(len(skill["template"].get("Tiers") or {}) for skill in skills)
    translated = sum(bool(skill["nameCn"]) for skill in skills)
    print(f"skills: {len(skills)}, tiers: {tiers}, Chinese titles: {translated}")
    print(f"source: {args.db.resolve()}")
    print(f"output: {args.out.resolve()}")


if __name__ == "__main__":
    main()
