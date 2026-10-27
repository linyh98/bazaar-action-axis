import json
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parent.parent
REPORT = ROOT / "captures" / "diagnostics" / "g10" / "source_attribution_084.json"


class G10SourceAttributionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = json.loads(REPORT.read_text(encoding="utf-8"))

    def test_every_direct_hit_has_one_native_source(self):
        direct = self.data["directHits"]
        self.assertEqual(13, len(direct))
        for hit in direct:
            self.assertEqual("verified", hit["attribution"], hit)
            self.assertEqual(1, len(hit["sources"]), hit)
            self.assertTrue(hit["sourceNames"][0], hit)

    def test_dot_ticks_are_not_fabricated(self):
        ticks = self.data["dotTicks"]
        self.assertEqual(30, len(ticks))
        for tick in ticks:
            self.assertEqual("unavailable-native-no-tick-source", tick["attribution"], tick)
            self.assertEqual([], tick["sources"], tick)

    def test_application_sources_are_present(self):
        apps = self.data["applications"]
        self.assertEqual(22, len(apps))
        keys = {(app["action"], app["sourceName"]) for app in apps}
        self.assertIn(("PlayerBurnApply", "Soldering Gun"), keys)
        self.assertIn(("PlayerPoisonApply", "Trained Spider"), keys)
        self.assertIn(("PlayerPoisonApply", "Aludel"), keys)

    def test_summary_is_consistent(self):
        summary = self.data["summary"]
        self.assertEqual(len(self.data["directHits"]), summary["directHits"])
        self.assertEqual(len(self.data["dotTicks"]), summary["dotTicks"])
        self.assertEqual(len(self.data["applications"]), summary["applications"])
        self.assertEqual(summary["directHits"], summary["verifiedDirectHits"])
        self.assertEqual(summary["dotTicks"], summary["unverifiedDotTicks"])


if __name__ == "__main__":
    unittest.main()
