import json
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parent.parent
CORPUS = ROOT / "captures" / "diagnostics" / "g10" / "replay_corpus.json"

G10_ACTIONS = (
    "PlayerDamage",
    "PlayerBurnApply",
    "PlayerPoisonApply",
    "PlayerShieldApply",
    "PlayerHeal",
    "PlayerRegenApply",
)


class G10ReplayCorpusTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = json.loads(CORPUS.read_text(encoding="utf-8"))

    def test_corpus_scope_is_all_scanned_replays(self):
        self.assertEqual(200, self.data["replayCount"])
        self.assertEqual(17, self.data["g10CardReplayCount"])
        self.assertEqual(17, len(self.data["g10CardReplays"]))

    def test_every_replay_is_complete_and_unique(self):
        ids = set()
        for replay in self.data["g10CardReplays"]:
            self.assertTrue(replay["complete"], replay["file"])
            self.assertGreater(replay["frames"], 0, replay["file"])
            self.assertGreater(replay["duration"], 0, replay["file"])
            self.assertEqual(64, len(replay["sha256"]), replay["file"])
            self.assertTrue(replay["g10Cards"], replay["file"])
            self.assertNotIn(replay["battleId"], ids, replay["file"])
            ids.add(replay["battleId"])

    def test_both_sides_have_native_ledger_values(self):
        for replay in self.data["g10CardReplays"]:
            for side in ("player", "enemy"):
                values = replay["sides"][side]
                self.assertGreaterEqual(values["changes"], 0, replay["file"])
                self.assertGreaterEqual(values["totalDamage"], 0, replay["file"])
                self.assertGreaterEqual(values["shieldAbsorbed"], 0, replay["file"])
                self.assertGreaterEqual(values["overkill"], 0, replay["file"])
                self.assertGreaterEqual(values["healing"], 0, replay["file"])
                self.assertIsNotNone(values["netHpLoss"], replay["file"])

    def test_all_six_g10_action_families_are_covered(self):
        coverage = self.data["actionCoverageAllReplays"]
        for action in G10_ACTIONS:
            self.assertGreater(coverage.get(action, 0), 0, action)

    def test_all_g10_card_seeds_are_represented(self):
        names = set()
        for replay in self.data["g10CardReplays"]:
            for card in replay["g10Cards"]:
                names.add(card["name"])
        for expected in (
            "Trained Spider",
            "Soldering Gun",
            "Blow Torch",
            "Rune Axe",
            "Bandages",
            "Walter Cooler",
            "Shard of Obsidian",
        ):
            self.assertIn(expected, names)

    def test_player_opening_limits_are_explicit(self):
        notes = " ".join(self.data["notes"])
        self.assertIn("HealthMax", notes)
        self.assertIn("no per-tick damage source", notes)


if __name__ == "__main__":
    unittest.main()
