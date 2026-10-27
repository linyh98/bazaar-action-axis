import base64
import gzip
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from tools.import_validation_replay import (ValidationLedgerBuilder, extract_templates, snapshot_card)
from tools.native_replay_ledger import DEFAULT_CONTRACT, load_contract


class ValidationImportTests(unittest.TestCase):
    def test_snapshot_keeps_instance_tier_attributes_and_stash(self):
        raw = {'InstanceId': {'Value': 'item'}, 'TemplateId': 'template', 'Tier': 'Gold',
               'Section': 'Stash', 'Attributes': {'Cooldown': 2350, 'Ammo': 2},
               'Enchantment': 'Heavy', 'LeftSocketId': 'Socket_4'}
        card = snapshot_card(raw)
        self.assertEqual('item', card['instance_id'])
        self.assertEqual('Stash', card['section'])
        self.assertEqual(2350, card['attributes']['Cooldown'])
        self.assertEqual('Heavy', card['enchant'])

    def test_catalog_requires_sibling_file_and_keeps_offboard_templates(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            with gzip.open(root / 'catalog.json.gz', 'wt', encoding='utf-8') as handle:
                json.dump({'Schema': 'actionaxis.catalog.v1', 'Templates': {'offboard': {'Tiers': {'Gold': {}}}}}, handle)
            bundle = {'CatalogFile': 'catalog.json.gz', 'OpeningBeforeDispatch': {'Player': {'Skills': [
                {'TemplateId': 'skill', 'Template': {'Tiers': {'Bronze': {}}}}]}}}
            templates, errors, _ = extract_templates(bundle, root / 'fight.json.gz')
            self.assertEqual({'offboard', 'skill'}, set(templates))
            self.assertEqual([], errors)
            for name in ('../catalog.json.gz', '..\\catalog.json.gz', '/catalog.json.gz'):
                with self.assertRaises(ValueError):
                    extract_templates(dict(bundle, CatalogFile=name), root / 'fight.json.gz')

    def bundle(self):
        message = {'MessagePackBase64': base64.b64encode(b'\x90').decode('ascii')}
        return {'Schema': 'actionaxis.validation.v1', 'CaptureId': 'test', 'Stage': 'complete',
                'Environment': {'SharedAssemblySha256': load_contract(DEFAULT_CONTRACT)['assemblySha256']},
                'SpawnMessage': message, 'CombatMessage': message, 'DespawnMessage': message,
                'OpeningBeforeDispatch': {'Player': {'Hero': 'Vanessa', 'Cards': [], 'Skills': [],
                    'Attributes': {'Health': 777}}},
                'OpeningAfterDispatch': {'Player': {'Cards': [{'InstanceId': 'late'}], 'Skills': []}}}

    def test_pre_dispatch_is_authoritative_and_empty_skills_are_known(self):
        with tempfile.TemporaryDirectory() as folder:
            source = Path(folder) / 'test.json.gz'
            source.write_bytes(b'evidence')
            bundle = self.bundle()
            builder = ValidationLedgerBuilder(source, DEFAULT_CONTRACT, {}, bundle)
            builder.load()
            self.assertEqual([], builder.snapshot['player_hand']['items'])
            self.assertEqual(777, builder.open_attrs['player']['Health'])
            self.assertTrue(builder.snapshot_info)
            builder.note_missing_opening()
            self.assertFalse(any(row['scope'] == 'opening.playerSkills' for row in builder.unknowns))
            bundle.pop('OpeningBeforeDispatch')
            with self.assertRaisesRegex(ValueError, 'pre-dispatch'):
                ValidationLedgerBuilder(source, DEFAULT_CONTRACT, {}, bundle).load()

    def test_changed_game_or_missing_wire_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            source = Path(folder) / 'test.gz'
            source.write_bytes(b'evidence')
            bundle = self.bundle()
            bundle['Environment']['SharedAssemblySha256'] = 'new-build'
            with self.assertRaisesRegex(ValueError, 'assembly differs'):
                ValidationLedgerBuilder(source, DEFAULT_CONTRACT, {}, bundle).load()
            bundle = self.bundle()
            del bundle['CombatMessage']
            with self.assertRaisesRegex(ValueError, 'Missing original wire'):
                ValidationLedgerBuilder(source, DEFAULT_CONTRACT, {}, bundle).load()


if __name__ == '__main__':
    unittest.main()
