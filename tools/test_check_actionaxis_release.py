"""Release checks must distinguish matching disk files from a stale process."""
import contextlib
import io
import json
import pathlib
import tempfile
import unittest
from unittest import mock

import check_actionaxis_release as release


class ReleaseCheckTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        root = pathlib.Path(self.temp.name)
        self.game = root / "game"
        self.build = root / "build"
        self.build.mkdir()
        plugins = self.game / "BepInEx" / "plugins"
        plugins.mkdir(parents=True)
        self.log = self.game / "BepInEx" / "LogOutput.log"
        for name in ("ActionAxisEngine.dll", "ActionAxisPlugin.dll"):
            (self.build / name).write_bytes(name.encode())
            (plugins / name).write_bytes(name.encode())

    def run_check(self, installed_version="1.6.5"):
        output = io.StringIO()
        with mock.patch.object(release, "module_version_id", return_value="aaa-bbb"), \
                mock.patch.object(release, "plugin_version", return_value=installed_version), \
                contextlib.redirect_stdout(output):
            status = release.main(["--game-dir", str(self.game), "--build-dir", str(self.build)])
        return status, json.loads(output.getvalue())

    def test_same_engine_with_old_loaded_plugin_is_mismatch(self):
        self.log.write_text(
            "[Info : BepInEx] Loading [Bazaar Action Axis 1.5.6]\n"
            "[Info : Bazaar Action Axis] [ActionAxis] loaded; engine=aaa-bbb\n",
            encoding="utf-8",
        )
        status, report = self.run_check()
        self.assertEqual(status, 1)
        self.assertEqual(report["diskVerdict"], "MATCH")
        self.assertTrue(report["loadedEngineMatchesInstalled"])
        self.assertFalse(report["loadedPluginMatchesInstalled"])
        self.assertEqual(report["runtimeVerdict"], "MISMATCH")
        self.assertEqual(report["verdict"], "MISMATCH")
        for location in ("source", "target"):
            self.assertEqual(report["files"]["ActionAxisPlugin.dll"][location]["pluginVersion"], "1.6.5")

    def test_latest_matching_plugin_log_line_wins(self):
        self.log.write_text(
            "Loading [Bazaar Action Axis 1.5.6]\n"
            "Loading [Other Plugin 9.9.9]\n"
            "[Info : BepInEx] Loading [Bazaar Action Axis 1.6.5]\n",
            encoding="utf-8",
        )
        self.assertEqual(release.loaded_plugin_version(self.log), "1.6.5")

    def test_missing_log_is_unknown_and_disk_check_still_succeeds(self):
        status, report = self.run_check()
        self.assertEqual(status, 0)
        self.assertEqual(report["diskVerdict"], "MATCH")
        self.assertIsNone(report["loadedEngineMatchesInstalled"])
        self.assertIsNone(report["loadedPluginMatchesInstalled"])
        self.assertEqual(report["runtimeVerdict"], "UNKNOWN")
        self.assertEqual(report["verdict"], "UNKNOWN")

    def test_unavailable_plugin_metadata_does_not_verify_runtime(self):
        self.log.write_text(
            "Loading [Bazaar Action Axis 1.6.5]\n"
            "[ActionAxis] loaded; engine=aaa-bbb\n",
            encoding="utf-8",
        )
        status, report = self.run_check(installed_version=None)
        self.assertEqual(status, 0)
        self.assertIsNone(report["loadedPluginMatchesInstalled"])
        self.assertEqual(report["runtimeVerdict"], "UNKNOWN")
        self.assertEqual(report["verdict"], "UNKNOWN")


if __name__ == "__main__":
    unittest.main()
