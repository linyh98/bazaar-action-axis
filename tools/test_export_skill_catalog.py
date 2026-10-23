import json
from contextlib import closing
import sqlite3
import tempfile
import unittest
from pathlib import Path

from tools.export_skill_catalog import export_skills


class ExportSkillCatalogTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.db = self.root / "GameData.db"
        self.locale = self.root / "zh-CN.bytes"
        self.skill = {
            "$type": "TCardSkill",
            "Id": "ABC-123",
            "InternalName": "Internal Skill",
            "Version": "test-build",
            "TranslationKey": "old-card-key",
            "Localization": {
                "Title": {"Key": "title-key", "Text": "Skill Title"},
                "Tooltips": [{
                    "Content": {"Key": "tooltip-key", "Text": "Shield {ability.0}"},
                    "TooltipType": "Passive",
                }],
            },
            "Tiers": {
                "Bronze": {"Attributes": {"Custom_0": 10}, "AbilityIds": ["0"]},
                "Silver": {"Attributes": {}, "AbilityIds": ["0"]},
            },
            "Abilities": {"0": {
                "Trigger": {"$type": "TTriggerOnItemUsed", "Subject": {"Conditions": None}},
                "Action": {"$type": "TActionPlayerShieldApply", "ReferenceValue": None},
                "Prerequisites": [{"$type": "TFutureUnknownPrerequisite"}],
            }},
        }
        with closing(sqlite3.connect(self.db)) as connection:
            connection.execute("CREATE TABLE cards (Id TEXT PRIMARY KEY, Data BLOB NOT NULL)")
            connection.executemany("INSERT INTO cards VALUES (?,?)", [
                (self.skill["Id"], json.dumps(self.skill).encode()),
                ("item", json.dumps({"$type": "TCardItem", "Id": "item"}).encode()),
            ])
            connection.commit()
        with closing(sqlite3.connect(self.locale)) as connection:
            connection.execute("CREATE TABLE translation (hash TEXT PRIMARY KEY, text TEXT)")
            connection.executemany("INSERT INTO translation VALUES (?,?)", [
                ("title-key", "技能标题"),
                ("tooltip-key", "获得{ability.0}护盾"),
                ("old-card-key", "过期名称"),
            ])
            connection.commit()

    def test_exports_only_skills_with_exact_native_template(self):
        skills = export_skills(self.db, self.locale)
        self.assertEqual(len(skills), 1)
        self.assertEqual(skills[0]["templateId"], "abc-123")
        self.assertEqual(skills[0]["template"], self.skill)
        # Sparse tiers, unknown prerequisites, and null reference values stay exact.
        self.assertEqual(skills[0]["template"]["Tiers"]["Silver"]["Attributes"], {})

    def test_translation_uses_current_title_key_and_preserves_tooltip_tokens(self):
        skill = export_skills(self.db, self.locale)[0]
        self.assertEqual(skill["name"], "Skill Title")
        self.assertEqual(skill["nameCn"], "技能标题")
        self.assertEqual(skill["tooltips"], [{
            "index": 0, "text": "Shield {ability.0}",
            "textCn": "获得{ability.0}护盾", "tooltipType": "Passive",
        }])

    def test_missing_locale_preserves_english_and_never_changes_source(self):
        before = self.db.read_bytes()
        skill = export_skills(self.db, self.root / "missing.bytes")[0]
        self.assertEqual(skill["name"], "Skill Title")
        self.assertIsNone(skill["nameCn"])
        self.assertIsNone(skill["tooltips"][0]["textCn"])
        self.assertEqual(self.db.read_bytes(), before)

    def test_missing_database_does_not_create_an_empty_database(self):
        missing = self.root / "absent.db"
        with self.assertRaises(sqlite3.OperationalError):
            export_skills(missing)
        self.assertFalse(missing.exists())


if __name__ == "__main__":
    unittest.main()
