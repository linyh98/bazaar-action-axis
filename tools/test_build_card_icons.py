import io
import unittest

from PIL import Image

from tools.build_card_icons import convert_to_icon, icon_filename, plan_icons


class PlanIconsTest(unittest.TestCase):
    def test_plan_icons_dedupes_by_base_item_id(self):
        cards = [
            {"baseItemId": "b", "image": "u2"},
            {"baseItemId": "a", "image": "u1"},
            {"baseItemId": "a", "image": "u1"},
            {"baseItemId": "c"},
        ]
        self.assertEqual(
            plan_icons(cards),
            [
                {"base_item_id": "b", "image": "u2"},
                {"base_item_id": "a", "image": "u1"},
            ],
        )


class FilenameTest(unittest.TestCase):
    def test_icon_filename_sanitizes(self):
        self.assertEqual(icon_filename("0efe/7d3f@Gold"), "0efe_7d3f_Gold.png")


class ConvertTest(unittest.TestCase):
    def test_convert_to_icon_produces_96px_png(self):
        source = io.BytesIO()
        Image.new("RGB", (300, 200), "red").save(source, "PNG")
        result = convert_to_icon(source.getvalue(), 96)
        image = Image.open(io.BytesIO(result))
        self.assertEqual(image.format, "PNG")
        self.assertEqual(image.size, (96, 96))


if __name__ == "__main__":
    unittest.main()
