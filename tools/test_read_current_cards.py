import unittest

from tools.read_current_cards import normalize_name, pick_name, plan_slots


class ReadCurrentCardsTest(unittest.TestCase):
    def test_normalize_removes_spaces(self):
        self.assertEqual(normalize_name("龙 心 吊 坠"), "龙心吊坠")

    def test_picks_a_known_name_from_ocr_lines(self):
        known = {"驯化蜘蛛", "炼金梨缶", "焊接枪"}
        lines = ["暴击率", "驯 化 蜘 蛛", "小型 伙伴", "造成 2 剧毒"]
        self.assertEqual(pick_name(lines, known), "驯化蜘蛛")

    def test_picks_an_unknown_title_and_skips_tags_and_effects(self):
        lines = ["小 型 服 饰 巨 龙", "龙 心 吊 坠", "获得 5 护盾", "造成 1 灼烧"]
        self.assertEqual(pick_name(lines, set()), "龙心吊坠")

    def test_scales_slots_to_window_size(self):
        slots = plan_slots(808, 531.5)
        self.assertAlmostEqual(slots[0][0], 280, places=0)
        self.assertAlmostEqual(slots[0][1], 305, places=0)


if __name__ == "__main__":
    unittest.main()
