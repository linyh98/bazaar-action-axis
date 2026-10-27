"""G00 数据审计的离线回归：不读游戏数据库，也不改引擎语义。"""
import csv
import tempfile
import unittest
from pathlib import Path

from tools import g00_audit as audit


def record(group, template):
    return group, template


class MissingDefinitionTests(unittest.TestCase):
    def test_missing_ability_and_aura_paths_are_expanded_per_tier(self):
        item = {
            "$type": "TCardItem",
            "InternalName": "Probe",
            "Tiers": {
                "Silver": {"AbilityIds": ["known", "missing-1"], "AuraIds": ["missing-aura"]},
                "Gold": {"AbilityIds": ["known", "missing-2"]},
            },
            "Abilities": {"known": {}},
            "Auras": {},
        }
        result = audit.missing_definitions({("cards", "probe"): record("items", item)})

        self.assertEqual(1, result["templates"])
        self.assertEqual(3, result["unique_references"])
        self.assertEqual(3, result["expanded_references"])
        self.assertEqual(["missing-1", "missing-2", "missing-aura"], result["unique_missing_ids"])
        self.assertEqual({"AbilityIds": 2, "AuraIds": 1}, result["kinds"])
        self.assertEqual({"TCardItem": 3}, result["by_type"])
        self.assertEqual(
            [
                ["cards", "probe", "Probe", "Gold", "AbilityIds", 1, "missing-2"],
                ["cards", "probe", "Probe", "Silver", "AbilityIds", 1, "missing-1"],
                ["cards", "probe", "Probe", "Silver", "AuraIds", 0, "missing-aura"],
            ],
            result["entries"],
        )

    def test_defined_references_are_not_reported(self):
        item = {
            "$type": "TCardItem",
            "Tiers": {"Diamond": {"AbilityIds": ["a"], "AuraIds": ["b"]}},
            "Abilities": {"a": {}},
            "Auras": {"b": {}},
        }
        result = audit.missing_definitions({("cards", "probe"): record("items", item)})
        self.assertEqual(0, result["templates"])
        self.assertEqual([], result["entries"])


class MonsterLayoutTests(unittest.TestCase):
    def test_layout_reports_missing_templates_sparse_tiers_and_enchantments(self):
        monster = {
            "InternalName": "Monster Probe",
            "Player": {
                "Attributes": {"HealthMax": 100, "Health": 90},
                "Hand": {
                    "Items": [
                        {"TemplateId": "item", "Tier": "Gold", "EnchantmentType": "Toxic", "SocketId": "Socket_1"},
                        {"TemplateId": "missing-item", "Tier": "Gold", "SocketId": "Socket_2"},
                    ]
                },
                "Skills": [
                    {"TemplateId": "skill", "Tier": "Bronze"},
                    {"TemplateId": "missing-skill", "Tier": "Gold"},
                ],
                "Effects": [{"$type": "TEffect"}],
                "Socket": {"Effects": [{"$type": "TSocket"}]},
            },
        }
        item = {"$type": "TCardItem", "InternalName": "Item", "Tiers": {"Silver": {}}, "Enchantments": {}}
        skill = {"$type": "TCardSkill", "InternalName": "Skill", "Tiers": {"Gold": {}}}
        result = audit.monster_layout(
            {
                ("monsters", "monster"): record("monsters", monster),
                ("cards", "item"): record("items", item),
                ("cards", "skill"): record("skills", skill),
            }
        )

        self.assertEqual(1, result["monsters"])
        self.assertEqual(2, result["items"])
        self.assertEqual(2, result["skills"])
        self.assertEqual([], result["health_missing"])
        self.assertEqual([["monsters", "monster", "Monster Probe", 1, "missing-item"]],
                         result["missing_item_templates"])
        self.assertEqual([["monsters", "monster", "Monster Probe", 0, "item", "Gold", "Silver"]],
                         result["undeclared_item_tiers"])
        self.assertEqual([["monsters", "monster", "Monster Probe", 0, "item", "Toxic"]],
                         result["missing_item_enchantments"])
        self.assertEqual([["monsters", "monster", "Monster Probe", 1, "missing-skill"]],
                         result["missing_skill_templates"])
        self.assertEqual([["monsters", "monster", "Monster Probe", 0, "skill", "Bronze", "Gold"]],
                         result["undeclared_skill_tiers"])
        self.assertEqual([["monsters", "monster", "Monster Probe", "Player.Effects[0]", "TEffect"]],
                         result["effects"])
        self.assertEqual([["monsters", "monster", "Monster Probe", "Player.Socket.Effects[0]", "TSocket"]],
                         result["socket_effects"])
        self.assertEqual({"TCardItem": 1, "missing": 1}, result["item_types"])
        self.assertEqual({"TCardSkill": 1, "missing": 1}, result["skill_types"])


class MatrixSummaryTests(unittest.TestCase):
    HEADER = audit.MATRIX_HEADER

    def write_rows(self, path, rows):
        with path.open("w", encoding="utf-8", newline="") as handle:
            writer = csv.DictWriter(handle, fieldnames=self.HEADER, delimiter="\t", lineterminator="\n")
            writer.writeheader()
            writer.writerows(rows)

    def test_side_status_blocked_ids_and_warning_columns_are_kept(self):
        item = {"$type": "TCardItem", "InternalName": "Probe", "Tiers": {"Bronze": {}, "Diamond": {}}}
        records = {("cards", "probe"): record("items", item)}
        rows = []
        for tier, source in (("Bronze", "declared"), ("Diamond", "declared"), ("Legendary", "legendary-alias")):
            for side in ("Player", "Enemy"):
                for scenario in ("template", "live", "mixed"):
                    status = "unsupported" if side == "Player" and scenario == "mixed" else "mapped"
                    rows.append({
                        "kind": "item", "table": "cards", "id": "probe", "name": "Probe",
                        "tier": tier, "tier_source": source, "enchantment": "None", "side": side,
                        "scenario": scenario, "attributes_live": "false" if scenario == "template" else "true",
                        "player_hero": "Mak" if scenario == "mixed" else "Dooley", "enemy_hero": "Dooley",
                        "status": status, "enchantment_status": "none",
                        "live_attribute_status": "template" if scenario == "template" else "live",
                        "enchantment_warning": "", "live_attribute_warning": "",
                        "reason": "probe" if status == "unsupported" else "",
                    })
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "matrix.tsv"
            self.write_rows(path, rows)
            result = audit.check_matrix(path, records)

        self.assertEqual(18, result["rows"])
        self.assertEqual(18, result["expected_rows"])
        self.assertEqual({"item": {"mapped": 15, "unsupported": 3}, "skill": {}}, result["status"])
        self.assertEqual({"Player": {"mapped": 6, "unsupported": 3}, "Enemy": {"mapped": 9}},
                         result["status_by_side"])
        self.assertEqual({"item": ["probe"], "skill": []}, result["blocked_ids"])
        self.assertEqual(["probe"], result["unenchanted_blocked_ids"])
        self.assertEqual({"none": 18}, result["enchantment_status"])
        self.assertEqual({"live": 12, "template": 6}, result["live_attribute_status"])
        self.assertEqual({}, result["errors"])


class MonsterReportTests(unittest.TestCase):
    HEADER = audit.MONSTER_HEADER

    def write_rows(self, path, rows):
        with path.open("w", encoding="utf-8", newline="") as handle:
            writer = csv.DictWriter(handle, fieldnames=self.HEADER, delimiter="\t", lineterminator="\n")
            writer.writeheader()
            writer.writerows(rows)

    def records(self):
        monster = {
            "InternalName": "Monster Probe",
            "Player": {
                "Attributes": {"HealthMax": 100},
                "Hand": {"Items": [{"TemplateId": "item", "Tier": "Gold"}]},
                "Skills": [{"TemplateId": "skill", "Tier": "Diamond"}],
            },
        }
        item = {"$type": "TCardItem", "InternalName": "Item", "Tiers": {"Gold": {}}, "Enchantments": {}}
        skill = {"$type": "TCardSkill", "InternalName": "Skill", "Tiers": {"Diamond": {}}}
        return {
            ("monsters", "monster"): record("monsters", monster),
            ("cards", "item"): record("items", item),
            ("cards", "skill"): record("skills", skill),
        }

    def row(self, **values):
        row = {name: "" for name in self.HEADER}
        row.update(values)
        return row

    def test_clean_report_matches_database_layout(self):
        rows = [
            self.row(record="monster", monster_id="monster", monster_name="Monster Probe", kind="monster",
                     status="read"),
            self.row(record="item", monster_id="monster", monster_name="Monster Probe", index="0",
                     template_id="item", template_name="Item", kind="item", tier="Gold",
                     tier_source="declared", enchantment="None", enchantment_status="none", socket="0",
                     status="resolved"),
            self.row(record="skill", monster_id="monster", monster_name="Monster Probe", index="0",
                     template_id="skill", template_name="Skill", kind="skill", tier="Diamond",
                     tier_source="declared", enchantment="", enchantment_status="", socket="",
                     status="resolved"),
        ]
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "monsters.tsv"
            self.write_rows(path, rows)
            result = audit.check_monster_report(path, self.records())

        self.assertEqual({"monster": 1, "item": 1, "skill": 1}, result["rows"])
        self.assertEqual([], result["mismatches"])
        self.assertEqual([], result["missing_monsters"])

    def test_missing_monster_row_is_a_mismatch(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "monsters.tsv"
            self.write_rows(path, [])
            result = audit.check_monster_report(path, self.records())
        self.assertEqual(["monster"], result["missing_monsters"])


if __name__ == "__main__":
    unittest.main()
