import json
import pathlib
import tempfile
import unittest

from tools import native_replay_ledger as ledger


class LedgerFixtureTests(unittest.TestCase):
    def normalize_ability_event(self, abilities, effect_id='', action='FlyingStart', enchantment=None):
        with tempfile.TemporaryDirectory() as folder:
            path = pathlib.Path(folder) / 'templates.jsonl'
            template = {'Abilities': abilities}
            if enchantment is not None:
                template = {'Abilities': {}, 'Enchantments': {'Turbo': {'Abilities': abilities}}}
            path.write_text(json.dumps({'id': 'bird-template', 'template': template}) + '\n', encoding='utf8')
            builder = ledger.LedgerBuilder('unused', ledger.DEFAULT_CONTRACT, None, False,
                                           ledger.load_ability_targets(path))
        builder.instances['enemy:bird'] = {'instanceId': 'bird', 'templateId': 'bird-template',
                                           'side': 'enemy', 'enchantment': enchantment}
        builder.side_by_instance['bird'] = 'enemy'
        command = builder.contract['enums']['EActionCommandType'][action]
        return builder.normalize_event([5, ['native-context', effect_id, command, None,
                                            ['bird'], ['bird'], [1, [['neighbor']]]]],
                                       'combat', 100, 5.0)

    @staticmethod
    def blank_id_ability(action='TActionCardFlyingStart', target='TTargetCardRandom'):
        return {'Id': '', 'Trigger': {'$type': 'TTriggerOnCardFired'},
                'Action': {'$type': action, 'Target': {'$type': target}}}

    def test_empty_internal_ability_id_recovers_observed_random_flight(self):
        # Caracara stores the ability under key 0, but both its internal Id and
        # the native EffectExecuted.EffectId are empty. Keep raw evidence intact.
        event = self.normalize_ability_event({'0': self.blank_id_ability()})
        self.assertEqual('', event['fields']['EffectId'])
        self.assertEqual('0', event.get('resolvedAbilityId'))
        self.assertEqual('TActionCardFlyingStart', event.get('actionType'))
        self.assertEqual('TTriggerOnCardFired', event.get('triggerType'))
        self.assertTrue(event['randomTarget'])
        tape = ledger.build_replay_random_tape({'replay': {'sha256': 'original'},
                                               'combat': {'events': [event]}})
        # The engine also identifies native item abilities by their internal Id.
        # Resolving metadata must not replace that empty execution identity.
        self.assertEqual([{'frame': 100, 'source': 'bird', 'ability': '',
                           'kind': 'fly', 'targets': ['neighbor']}], tape['targets'])

    def test_internal_id_resolution_also_supports_selected_enchantment(self):
        event = self.normalize_ability_event({'e1': self.blank_id_ability()}, enchantment='Turbo')
        self.assertEqual('e1', event.get('resolvedAbilityId'))
        self.assertTrue(event['randomTarget'])

    def test_nonempty_internal_id_alias_is_not_an_unknown_runtime_ability(self):
        ability = dict(self.blank_id_ability(), Id='native-flight')
        event = self.normalize_ability_event({'0': ability}, effect_id='native-flight')
        self.assertEqual('native-flight', event['fields']['EffectId'])
        self.assertEqual('0', event.get('resolvedAbilityId'))
        self.assertTrue(event['randomTarget'])
        self.assertFalse(event['runtimeAbility'])

    def test_internal_id_resolution_rejects_ambiguity_and_incompatible_action(self):
        for abilities in ({'0': self.blank_id_ability(), '1': self.blank_id_ability()},
                          {'0': self.blank_id_ability(action='TActionCardSlow')},
                          {'0': {'Action': self.blank_id_ability()['Action']}}):
            event = self.normalize_ability_event(abilities)
            self.assertNotIn('resolvedAbilityId', event)
            self.assertFalse(event['randomTarget'])
            self.assertIsNone(event['targetType'])

    def test_dictionary_key_resolution_precedes_internal_id_alias(self):
        abilities = {'0': self.blank_id_ability(target='TTargetCardSelf'),
                     'different': dict(self.blank_id_ability(), Id='0')}
        event = self.normalize_ability_event(abilities, effect_id='0')
        self.assertEqual('TTargetCardSelf', event['targetType'])
        self.assertFalse(event['randomTarget'])
        self.assertNotIn('resolvedAbilityId', event)

    def regen_builder(self, amounts, changed_attribute=False):
        builder = ledger.LedgerBuilder('unused', ledger.DEFAULT_CONTRACT, None, False)
        damage_type = builder.contract['enums']['EDamageType']['Regen']
        health_type = builder.contract['enums']['EPlayerHealthChangeType']['Health']
        attr = builder.contract['enums']['EPlayerAttributeType']['HealthRegen']
        frames = []
        for amount in amounts:
            updates = {attr: [attr, 10, 24]} if changed_attribute else {}
            raw = [False, [[damage_type, health_type, amount, False, False]], updates, None]
            frames.append([[], raw, None, {}])
        builder.combat = [frames]
        return builder

    def test_constant_regen_adjustments_restore_unreported_opening_regen(self):
        # b1621: no HealthRegen transitions, but every native Regen packet is
        # nominal 24. Do not silently simulate the starting regeneration as 0.
        builder = self.regen_builder([24, 24, 24])
        builder.recover_opening_player_attributes()
        self.assertEqual(24, builder.open_attrs['player']['HealthRegen'])
        self.assertIn('stableRegenAdjustment', builder.attr_sources['player']['HealthRegen'])

    def test_regen_transition_takes_precedence_over_later_tick_amount(self):
        builder = self.regen_builder([24, 24], changed_attribute=True)
        builder.recover_opening_player_attributes()
        self.assertEqual(10, builder.open_attrs['player']['HealthRegen'])
        self.assertIn('firstPrevious', builder.attr_sources['player']['HealthRegen'])

    def test_inconsistent_or_unobserved_regen_remains_unknown(self):
        for amounts in ([2, 3], [24], []):
            builder = self.regen_builder(amounts)
            builder.recover_opening_player_attributes()
            self.assertNotIn('HealthRegen', builder.open_attrs['player'])

    def test_absent_optional_event_categories_are_coverage_notes(self):
        builder = ledger.LedgerBuilder('unused', ledger.DEFAULT_CONTRACT, None, False)
        builder.replay_doc = {'BattleId': 'test'}
        builder.note_sample_limits()
        self.assertEqual(['sample.random'], [u['scope'] for u in builder.unknowns])
        self.assertEqual({'sample.transform', 'sample.transformRevert', 'sample.passive', 'sample.activation'},
                         {u['scope'] for u in builder.coverage_notes})

    def test_unavailable_skills_stay_unknown(self):
        builder = ledger.LedgerBuilder('unused', ledger.DEFAULT_CONTRACT, None, False)
        blob = {'status': 'Unavailable', 'source': 'OpeningMessage', 'items': []}
        builder.snapshot = {key: dict(blob) for key in ('player_hand', 'opponent_hand',
                                                     'player_skills', 'opponent_skills')}
        builder.snapshot_info = {'playerSkills': dict(blob)}
        builder.merge_snapshot()
        builder.note_missing_opening()
        self.assertTrue(any(u['scope'] == 'opening.skills' for u in builder.unknowns))
        self.assertTrue(any(u['scope'] == 'opening.playerSkills' for u in builder.unknowns))

    def test_captured_empty_skills_are_complete_opening_evidence(self):
        builder = ledger.LedgerBuilder('unused', ledger.DEFAULT_CONTRACT, None, False)
        blob = {'status': 'CapturedEmpty', 'source': 'OpeningMessage', 'items': []}
        builder.snapshot = {key: dict(blob) for key in ('player_hand', 'opponent_hand',
                                                     'player_skills', 'opponent_skills')}
        builder.snapshot_info = {'playerSkills': dict(blob), 'opponentSkills': dict(blob)}
        builder.merge_snapshot()
        builder.note_missing_opening()
        self.assertFalse(any(u['scope'] in ('opening.skills', 'opening.playerSkills')
                             for u in builder.unknowns))
        builder.snapshot_info = {}
        builder.note_missing_opening()
        self.assertTrue(any(u['scope'] == 'opening.playerSkills' for u in builder.unknowns))

    def test_positional_transform_has_no_donor_random_choice(self):
        action = {'$type': 'TActionCardTransform', 'SpawnContext': {
            'SelectionMethod': 'Random', 'Groups': [{'SelectionMethod': 'Random', 'Filters': [
                {'$type': 'TSpawnFilterTarget', 'Target': {'$type': 'TTargetCardPositional',
                                                       'TargetMode': 'LeftCard'}}]}]}}
        metadata = ledger.ability_metadata_from({'0': {'Action': action}})
        self.assertFalse(metadata['0']['randomTransform'])
        action['SpawnContext']['Groups'][0]['Filters'] = [{'$type': 'TSpawnFilterQuery'}]
        self.assertTrue(ledger.ability_metadata_from({'0': {'Action': action}})['0']['randomTransform'])

    def test_skill_runtime_updates_do_not_create_board_instances_or_mutate_opening(self):
        builder = ledger.LedgerBuilder('unused', ledger.DEFAULT_CONTRACT, None, False)
        builder.add_skill('player', {'instanceId': 'skill-a', 'templateId': 'template-a',
                                   'attributes': {'Custom_1': 3}, 'source': 'snapshot'})
        attr = builder.contract['enums']['ECardAttributeType']['Custom_1']
        names = [f['name'] for f in builder.contract['dtos']['CombatSimCardUpdate']['fields']]
        raw = [{'Attributes': {attr: [attr, 3, 2]}}.get(name) for name in names]
        builder.process_card_update('skill-a', raw, 40, 2.0)
        self.assertEqual({}, builder.instances)
        self.assertEqual(3, builder.skills['player'][0]['attributes']['Custom_1'])
        self.assertEqual('skill-a', builder.card_updates[0]['instanceId'])
        self.assertEqual(2, builder.card_updates[0]['current'])

    def test_first_player_attribute_transition_recovers_opening_previous_value(self):
        builder = ledger.LedgerBuilder('unused', ledger.DEFAULT_CONTRACT, None, False)
        attr = builder.contract['enums']['EPlayerAttributeType']['Shield']
        names = [f['name'] for f in builder.contract['dtos']['CombatSimPlayerUpdate']['fields']]
        raw = [{'Attributes': {attr: [attr, 7, 18]}}.get(name) for name in names]
        builder.combat = [[ [[], None, None, {}], [[], raw, None, {}] ]]
        builder.recover_opening_player_attributes()
        self.assertEqual(7, builder.open_attrs['player']['Shield'])
        self.assertEqual('replay.frame1.firstPrevious', builder.attr_sources['player']['Shield'])
        self.assertNotIn('HealthMax', builder.open_attrs['player'])
        self.assertNotIn('RageMax', builder.open_attrs['player'])

    def test_opening_instance_requires_opening_phase(self):
        self.assertTrue(ledger.LedgerBuilder.is_opening_instance(
            {"born": {"phase": "opening", "time": 0.0}}))
        self.assertFalse(ledger.LedgerBuilder.is_opening_instance(
            {"born": {"phase": "combat", "time": 8.0}}))
        self.assertFalse(ledger.LedgerBuilder.is_opening_instance(
            {"born": {"phase": "despawn", "time": 21.3}}))

    def test_capture_opening_freezes_attributes_before_combat_mutation(self):
        builder = object.__new__(ledger.LedgerBuilder)
        builder.instances = {
            "player:hand": {
                "instanceId": "hand", "key": "player:hand", "side": "player",
                "templateId": "T1", "templateSource": "bpp.battle_snapshots",
                "name": "Hand", "tier": "Bronze", "enchantment": None,
                "size": "Small", "section": "Hand", "socket": 0,
                "attributes": {"Cooldown": 6000, "CooldownMax": 6000},
                "attributeSources": {"Cooldown": "bpp.battle_snapshots"},
                "tags": [], "hiddenTags": [], "heroes": [], "state": "Alive",
                "born": {"phase": "opening", "time": 0.0},
            },
        }
        builder.opening_instances = {"player": {}, "enemy": {}}
        builder.capture_opening()
        record = builder.opening_instances["player"]["hand"]
        self.assertEqual(6000, record["attributes"]["Cooldown"])
        builder.instances["player:hand"]["attributes"]["Cooldown"] = 0
        self.assertEqual(6000, record["attributes"]["Cooldown"])
        self.assertEqual("bpp.battle_snapshots", record["attributeSources"]["Cooldown"])

    def test_engine_fixture_excludes_stash_but_keeps_hand(self):
        with tempfile.TemporaryDirectory() as folder:
            root = pathlib.Path(folder)
            templates = root / "templates.jsonl"
            catalog = root / "cards.json"
            templates.write_text("", encoding="utf-8")
            catalog.write_text("[]", encoding="utf-8")
            document = {
                "replay": {"battleId": "test", "sha256": "replay", "frameSeconds": 0.05,
                           "duration": 1.0},
                "contract": {"sha256": "contract"},
                "opening": {
                    "player": {
                        "hero": "Dooley", "health": 100, "healthMax": 100, "shield": 0,
                        "healthRegen": 0, "rageMax": 100, "enragedDurationMax": 5000,
                        "items": [
                            {"instanceId": "hand", "templateId": "T1", "tier": "Bronze",
                             "size": "Small", "socket": 0, "section": "Hand",
                             "enchantment": None, "attributes": {}},
                            {"instanceId": "stash", "templateId": "T2", "tier": "Bronze",
                             "size": "Small", "socket": 1, "section": "Stash",
                             "enchantment": None, "attributes": {}},
                        ],
                        "skills": [], "attributes": {"HealthMax": 100, "Gold": 17, "Custom_4": 8},
                    },
                    "enemy": {
                        "hero": "Pygmalien", "health": 100, "healthMax": 100, "shield": 0,
                        "healthRegen": 0, "rageMax": 100, "enragedDurationMax": 5000,
                        "items": [], "skills": [],
                    },
                },
            }
            fixture = ledger.build_engine_fixture(document, templates, catalog)
            self.assertEqual(1, len(fixture["items"]))
            self.assertEqual("hand", fixture["items"][0]["instanceId"])
            self.assertEqual("stash", fixture["excludedItems"][0]["instanceId"])
            self.assertEqual("Stash", fixture["excludedItems"][0]["reason"])
            self.assertEqual({"HealthMax": 100, "Gold": 17, "Custom_4": 8}, fixture['player']['attributes'])
            self.assertTrue(fixture['player']['attributesAreLive'])

    def test_contract_version_guard(self):
        with tempfile.TemporaryDirectory() as folder:
            path = pathlib.Path(folder) / "contract.json"
            path.write_text(json.dumps({"contractVersion": 2}), encoding="utf-8")
            with self.assertRaises(SystemExit):
                ledger.load_contract(path)

    def test_skill_snapshot_and_spawn_are_merged_once(self):
        builder = object.__new__(ledger.LedgerBuilder)
        builder.skills = {"player": [], "enemy": []}
        builder.side_by_instance = {}
        snapshot = {"instanceId": "skl_x", "templateId": "T1", "name": "Lash Out",
                    "tier": "Bronze", "enchantment": None,
                    "attributes": {"PoisonApplyAmount": 3}, "source": "snapshot"}
        spawn = {"instanceId": "skl_x", "templateId": None, "name": None, "tier": None,
                 "attributes": {}, "source": "spawn"}
        builder.add_skill("enemy", snapshot)
        builder.add_skill("enemy", spawn)
        self.assertEqual(1, len(builder.skills["enemy"]))
        skill = builder.skills["enemy"][0]
        self.assertEqual("T1", skill["templateId"])
        self.assertEqual(3, skill["attributes"]["PoisonApplyAmount"])
        self.assertEqual("enemy", builder.side_by_instance["skl_x"])

    def test_ability_targets_include_enchantments_and_auras(self):
        with tempfile.TemporaryDirectory() as folder:
            path = pathlib.Path(folder) / "templates.jsonl"
            row = {
                "id": "T1",
                "template": {
                    "Abilities": {"0": {"Action": {"Target": {"$type": "TTargetCardSelf"}}}},
                    "Auras": {"a1": {"Action": {"$type": "TAuraActionCardModifyAttribute"}}},
                    "Enchantments": {
                        "Turbo": {
                            "Abilities": {"e1": {"Action": {"Target": {"$type": "TTargetCardRandom"}}}},
                            "Auras": {"e2": {"Action": {"$type": "TAuraActionCardModifyAttribute"}}},
                        }
                    },
                },
            }
            path.write_text(json.dumps(row) + "\n", encoding="utf-8")
            targets = ledger.load_ability_targets(path)
            self.assertEqual("TTargetCardSelf", targets["T1"]["abilities"]["0"])
            self.assertIn("a1", targets["T1"]["auras"])
            self.assertEqual("TTargetCardRandom",
                             targets["T1"]["enchantments"]["Turbo"]["e1"])
            self.assertIn("e2", targets["T1"]["enchantmentAuras"]["Turbo"])


if __name__ == "__main__":
    unittest.main()


class ArchivedSkillSelectionTests(unittest.TestCase):
    def test_unique_opening_threshold_selects_archived_rule_without_combat_results(self):
        current = {'Tiers': {'Gold': {'Attributes': {'Custom_1': 5, 'Custom_2': 5}}}}
        old = {'Id': 'skill', 'Version': '5.0.0',
               'Tiers': {'Gold': {'Attributes': {'Custom_1': 4, 'Custom_2': 4}}}}
        row = dict(templateId='skill', version='5.0.0', template=old)
        skill = dict(templateId='skill', tier='Gold', attributes={'Custom_1': 4, 'Custom_2': 4})
        self.assertEqual(old, ledger.compatible_archived_skill(skill, current, '5.0.0', [row]))
        for captured in ({'Custom_1': 5, 'Custom_2': 5}, {'Custom_1': 4, 'Custom_2': 8}, {}):
            skill['attributes'] = captured
            self.assertIsNone(ledger.compatible_archived_skill(skill, current, '5.0.0', [row]))
        skill['attributes'] = {'Custom_1': 4, 'Custom_2': 4}
        self.assertIsNone(ledger.compatible_archived_skill(skill, current, '6.0.0', [row]))
        self.assertIsNone(ledger.compatible_archived_skill(skill, current, '5.0.0', [row, row]))
