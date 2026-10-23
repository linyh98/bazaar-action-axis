import unittest

from tools.download_images import plan_downloads


class PlanTest(unittest.TestCase):
    def test_dry_run_plans_without_writing(self):
        rows = [
            {"id": "a", "image": "https://x/a.avif"},
            {"id": "b", "image": "https://x/b.avif"},
        ]
        plan = plan_downloads(rows, limit=1)
        self.assertEqual(len(plan), 1)
        self.assertEqual(plan[0]["id"], "a")


if __name__ == "__main__":
    unittest.main()
