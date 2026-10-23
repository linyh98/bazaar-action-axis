import unittest
from pathlib import Path

from tools.import_game_db import convert_cards

ROOT = Path(__file__).resolve().parents[1]
DB = (
    ROOT
    / "vendor"
    / "BazaarPlusPlus"
    / "bazaarplusplus-mod"
    / "tests"
    / "CombatImpact.Corpus"
    / "corpus"
    / "GameData.cards.db"
)


class ImportGameDbTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.cards = convert_cards(DB)

    def test_all_items_are_converted(self):
        self.assertEqual(len({c["guid"] for c in self.cards}), 116)
        self.assertGreater(len(self.cards), 116)

    def test_narwhal_bronze(self):
        c = next(
            x
            for x in self.cards
            if x["internalName"] == "Narwhal" and x["tier"] == "Bronze"
        )
        self.assertEqual(c["cooldown"], 3)
        self.assertEqual(c["multicastBonus"], 0)
        dmg = next(e for e in c["effects"] if e["kind"] == "damage")
        self.assertEqual(dmg["amount"], {"type": "const", "value": 5})
        self.assertEqual(dmg["target"], {"kind": "enemyHero"})

    def test_hunting_hawk_haste(self):
        c = next(
            x
            for x in self.cards
            if x["internalName"] == "Hunting Hawk" and x["tier"] == "Bronze"
        )
        h = next(e for e in c["effects"] if e["kind"] == "haste")
        self.assertEqual(h["seconds"], 2)
        self.assertEqual(h["count"], 1)

    def test_gavel_freeze(self):
        c = next(x for x in self.cards if x["internalName"] == "Gavel")
        f = next(e for e in c["effects"] if e["kind"] == "freeze")
        self.assertEqual(f["seconds"], 1)


if __name__ == "__main__":
    unittest.main()
