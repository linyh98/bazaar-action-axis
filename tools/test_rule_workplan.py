"""工作文档派单索引的离线回归：不改游戏数据库与引擎。"""
import contextlib
import csv
import io
import json
from pathlib import Path
import sqlite3
import tempfile
import unittest
from unittest.mock import patch

from tools import rule_inventory as inv


class WorkIndexTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.db = self.root / "GameData.db"
        self.output = self.root / "index.csv"
        self.config = {
            "version": 1, "crosscut": ["G00", "G12"],
            "monster_group": "G00", "fallback": "G11",
            "priority": ["G01", "G07", "G11"],
            "groups": {
                "G00": {"title": "数据"}, "G12": {"title": "验收"},
                "G01": {"title": "位置", "types": ["TTargetCardPositional"]},
                "G07": {"title": "附魔", "has_enchantments": True},
                "G11": {"title": "余项"},
            },
        }
        with contextlib.closing(sqlite3.connect(self.db)) as con:
            con.execute("create table cards (Id text, Data text)")
            con.execute("create table monsters (Id text, Data text)")
            for key, data in [
                ("position", {"$type": "TCardItem", "InternalName": "Position", "Tiers": {"Gold": {}},
                              "Target": {"$type": "TTargetCardPositional"}}),
                ("enchant", {"$type": "TCardItem", "InternalName": "Enchant", "Enchantments": {
                    "Deadly": {"Target": {"$type": "TTargetCardPositional"}}}}),
                ("skill", {"$type": "TCardSkill", "InternalName": "Skill"}),
                ("encounter", {"$type": "TCardEncounterEvent"}),
            ]:
                con.execute("insert into cards values (?, ?)", (key, json.dumps(data)))
            con.execute("insert into monsters values (?, ?)", ("monster", json.dumps({"InternalName": "Monster"})))
            con.commit()

    def generate(self):
        docs = self.root / "docs"
        docs.mkdir(exist_ok=True)
        (docs / "native-rule-coverage.md").write_text(
            "<!-- RULE_WORK_ROUTING\n" + json.dumps(self.config) + "\nRULE_WORK_ROUTING -->", encoding="utf-8")
        with patch.object(inv, "ROOT", self.root), contextlib.redirect_stdout(io.StringIO()):
            inv.export_work_index(self.db, self.db, self.output)
        with self.output.open(encoding="utf-8-sig", newline="") as handle:
            return {row["template_id"]: row for row in csv.DictReader(handle)}

    def test_all_templates_have_one_owner_and_enchantments_do_not_steal_owner(self):
        rows = self.generate()
        self.assertEqual({"position", "enchant", "skill", "monster"}, set(rows))
        self.assertEqual("G01", rows["position"]["primary_group"])
        self.assertEqual("G11", rows["enchant"]["primary_group"])
        self.assertIn("G07", rows["enchant"]["related_groups"].split(";"))
        self.assertIn("G01", rows["enchant"]["related_groups"].split(";"))
        self.assertEqual("G00", rows["monster"]["primary_group"])
        self.assertTrue(all(row["scope"] == "待核实" for row in rows.values()))
        original = self.output.read_bytes()
        self.generate()
        self.assertEqual(original, self.output.read_bytes())

    def test_seed_overrides_feature_priority(self):
        self.config["groups"]["G07"]["seeds"] = {"position": "Position"}
        self.assertEqual("G07", self.generate()["position"]["primary_group"])

    def test_missing_or_renamed_seed_preserves_previous_file(self):
        self.generate()
        original = self.output.read_bytes()
        for seeds in ({"missing": "Missing"}, {"position": "Wrong Name"}):
            self.config["groups"]["G01"]["seeds"] = seeds
            with self.assertRaises(SystemExit):
                self.generate()
            self.assertEqual(original, self.output.read_bytes())

    def test_duplicate_owner_and_unknown_group_are_rejected(self):
        self.config["groups"]["G01"]["seeds"] = {"position": "Position"}
        self.config["groups"]["G07"]["seeds"] = {"position": "Position"}
        with self.assertRaises(SystemExit):
            self.generate()
        del self.config["groups"]["G07"]["seeds"]
        self.config["priority"].append("missing")
        with self.assertRaises(SystemExit):
            self.generate()

    def test_tooltip_strings_do_not_become_rule_types(self):
        features = inv.work_features({"Tooltip": "TTargetCardPositional", "Value": {
            "$type": "TFixedValue", "AttributeType": "CooldownMax"}})
        self.assertEqual({"TFixedValue"}, features["types"])
        self.assertEqual({"CooldownMax"}, features["attributes"])


if __name__ == "__main__":
    unittest.main()
