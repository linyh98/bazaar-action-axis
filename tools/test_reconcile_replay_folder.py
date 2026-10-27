import hashlib
from pathlib import Path
import tempfile
import unittest

from tools.reconcile_replay_folder import accepts, move_replay, missing_fixture_data, decisions_complete


class ReconcileReplayFolderTests(unittest.TestCase):
    def test_all_random_decision_channels_must_be_consumed_without_fallback(self):
        clean = {'targetRecords': 2, 'targetApplied': 2, 'criticalRecords': 3, 'criticalApplied': 3,
                 'orderRecords': 1, 'orderApplied': 1, 'enchantRecords': 1, 'enchantApplied': 1,
                 'transformRecords': 1, 'transformApplied': 1}
        self.assertTrue(decisions_complete(clean, []))
        for name in ('target', 'critical', 'order', 'enchant', 'transform'):
            self.assertFalse(decisions_complete(dict(clean, **{name + 'Fallbacks': 1}), []))
            self.assertFalse(decisions_complete(dict(clean, **{name + 'Applied': 0}), []))
        self.assertFalse(decisions_complete(clean, [{'reason': 'ambiguous'}]))
        self.assertFalse(decisions_complete({}, []))
    def test_only_verified_match_is_accepted(self):
        self.assertTrue(accepts({'verdict': 'MATCH'}, []))
        for verdict in ('DIFF', 'UNKNOWN', None):
            self.assertFalse(accepts({'verdict': verdict}, []))
        self.assertFalse(accepts({'verdict': 'MATCH'}, ['unsupported rule']))

    def test_missing_skills_and_tiers_are_unusable_but_stash_is_valid(self):
        self.assertIsNone(missing_fixture_data([{'reason': 'Stash'}], []))
        missing = missing_fixture_data([{'reason': 'SkillTemplateMissing', 'side': 'enemy',
                                       'instanceId': 'skill', 'templateId': 'unknown'}], [])
        self.assertEqual('MISSING_TEMPLATE', missing[0])
        self.assertEqual('MISSING_TIER', missing_fixture_data([], ['卡牌：品质定义不完整'])[0])
        self.assertIsNone(missing_fixture_data([], ['尚不支持某个动作']))

    def test_move_preserves_original_bytes_and_supports_resume(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            source = root / 'battle.payload.mpack.gz'
            payload = b'original replay payload'
            source.write_bytes(payload)
            sha = hashlib.sha256(payload).hexdigest()
            target = move_replay(source, root, '核对失败', sha)
            self.assertFalse(source.exists())
            self.assertEqual(payload, target.read_bytes())
            self.assertEqual(target, move_replay(target, root, '核对失败', sha))

    def test_conflict_does_not_overwrite_either_replay(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            source = root / 'battle.payload.mpack.gz'
            source.write_bytes(b'original')
            (root / '核对正确').mkdir()
            target = root / '核对正确' / source.name
            target.write_bytes(b'existing different file')
            with self.assertRaises(FileExistsError):
                move_replay(source, root, '核对正确', hashlib.sha256(b'original').hexdigest())
            self.assertEqual(b'original', source.read_bytes())
            self.assertEqual(b'existing different file', target.read_bytes())

    def test_missing_data_can_move_between_unusable_and_failed(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            source = root / 'battle.payload.mpack.gz'
            source.write_bytes(b'replay')
            sha = hashlib.sha256(b'replay').hexdigest()
            unused = move_replay(source, root, '无法使用', sha)
            self.assertEqual(root / '无法使用' / source.name, unused)
            restored = move_replay(unused, root, '核对失败', sha)
            self.assertEqual(b'replay', restored.read_bytes())

    def test_changed_input_and_outside_paths_are_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            outer = Path(folder)
            root = outer / 'selected'
            root.mkdir()
            outside = outer / 'battle.payload.mpack.gz'
            outside.write_bytes(b'replay')
            sha = hashlib.sha256(b'replay').hexdigest()
            with self.assertRaises(ValueError):
                move_replay(outside, root, '核对失败', sha)
            source = root / outside.name
            source.write_bytes(b'changed')
            with self.assertRaises(ValueError):
                move_replay(source, root, '核对失败', sha)
            self.assertTrue(source.exists())
            self.assertTrue(outside.exists())


if __name__ == '__main__':
    unittest.main()
