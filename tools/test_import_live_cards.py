import unittest

from tools.import_live_cards import find_card


class ImportLiveCardsTest(unittest.TestCase):
    def setUp(self):
        self.cards = [
            {"id": "a@Silver", "baseItemId": "a", "tier": "Silver", "name": "A"},
            {"id": "a@Gold", "baseItemId": "a", "tier": "Gold", "name": "A"},
        ]

    def test_exact_tier_match(self):
        card = find_card(self.cards, "a", "Silver")
        self.assertEqual(card["id"], "a@Silver")

    def test_falls_back_to_any_tier(self):
        card = find_card(self.cards, "a", "Diamond")
        self.assertEqual(card["id"], "a@Silver")

    def test_unknown_template(self):
        self.assertIsNone(find_card(self.cards, "missing", "Silver"))


if __name__ == "__main__":
    unittest.main()
