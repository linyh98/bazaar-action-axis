import copy
import json
import unittest
from tools.replay_random_decisions import build_decisions
from tools import native_replay_compare as compare


class ReplayRandomDecisionsTests(unittest.TestCase):
    def test_split_targets_of_one_execution_are_one_random_choice(self):
        native = self.native()
        native['combat']['events'] = [
            {'frame': 100, 'source': 'multitool', 'action': 'CardHaste',
             'fields': {'EffectId': '0', 'ExecutionContextId': context},
             'randomTarget': True, 'target': {'ids': [target]}}
            for context, target in [('cast-a', 'left'), ('cast-a', 'right'), ('cast-b', 'other')]]
        result = build_decisions(native)
        self.assertEqual([['left', 'right'], ['other']], [r['targets'] for r in result['targets']])
        self.assertEqual([100, 100], [r['frame'] for r in result['targets']])

    def test_random_target_rows_without_execution_identity_are_not_merged(self):
        native = self.native()
        native['combat']['events'] = [
            {'frame': 100, 'source': 'a', 'action': 'CardHaste', 'fields': {'EffectId': '0'},
             'randomTarget': True, 'target': {'ids': [target]}} for target in ['b', 'c']]
        self.assertEqual([['b'], ['c']], [r['targets'] for r in build_decisions(native)['targets']])

    def native(self):
        return {'replay': {'sha256': 'original-sha'}, 'opening': {}, 'combat': {
            'events': [
                {'frame': 20, 'source': 'a', 'action': 'FlyingStart', 'fields': {'EffectId': '0'},
                 'randomTarget': True, 'target': {'ids': ['b']}},
                {'frame': 20, 'source': 'a', 'action': 'PlayerDamage', 'fields': {'EffectId': '1'},
                 'target': {'sides': ['enemy']}}],
            'healthChanges': [{'frame': 20, 'side': 'enemy', 'healthAdjustments': [
                {'damageType': 'Damage', 'amount': -20, 'attributeChanged': 'Health', 'isCrit': True}]}]}}

    def test_decisions_include_target_and_explicit_critical_without_damage(self):
        result = build_decisions(self.native())
        self.assertEqual(['b'], result['targets'][0]['targets'])
        self.assertTrue(result['criticals'][0]['critical'])
        self.assertEqual('original-sha', result['replaySha256'])
        changed = self.native()
        changed['combat']['healthChanges'][0]['healthAdjustments'][0]['amount'] = -99999
        self.assertEqual(result, build_decisions(changed))

    def test_fixture_decoder_uses_the_same_explicit_decisions(self):
        from tools.native_replay_ledger import build_replay_random_tape
        self.assertEqual(build_decisions(self.native()), build_replay_random_tape(self.native()))

    def test_resolved_metadata_does_not_replace_execution_ability_id(self):
        native = self.native()
        action = native['combat']['events'][0]
        action['fields']['EffectId'] = ''
        action['resolvedAbilityId'] = '0'
        result = build_decisions(native)
        self.assertEqual('', result['targets'][0]['ability'])
        self.assertEqual('', action['fields']['EffectId'])

    def test_ambiguous_critical_sources_are_not_guessed(self):
        native = self.native()
        second = copy.deepcopy(native['combat']['events'][1])
        second['source'] = 'other'
        native['combat']['events'].append(second)
        native['combat']['healthChanges'][0]['healthAdjustments'].append({
            'damageType': 'Damage', 'amount': -15, 'attributeChanged': 'Shield', 'isCrit': False})
        result = build_decisions(native)
        self.assertEqual([], result['criticals'])
        self.assertEqual('ambiguous critical sources', result['excluded'][0]['reason'])

    def test_ordered_unsplit_health_hits_retain_mixed_explicit_flags(self):
        native = self.native()
        damage = native['combat']['events'][1]
        damage['sequence'] = 10
        second = copy.deepcopy(damage)
        second.update(source='other', sequence=12)
        native['combat']['events'].append(second)
        native['combat']['healthChanges'][0]['healthAdjustments'].append({
            'damageType': 'Damage', 'amount': -15, 'attributeChanged': 'Health', 'isCrit': False})
        result = build_decisions(native)
        self.assertEqual([('a', True), ('other', False)],
                         [(r['source'], r['critical']) for r in result['criticals']])
        self.assertEqual([], result['excluded'])
        changed = copy.deepcopy(native)
        for row in changed['combat']['healthChanges'][0]['healthAdjustments']:
            row['amount'] = -999999
        self.assertEqual(result, build_decisions(changed))

        # An unknown execution order still prevents pairing mixed bits.
        for sequence in (None, 9):
            changed = copy.deepcopy(native)
            changed['combat']['events'][-1]['sequence'] = sequence
            self.assertEqual([], build_decisions(changed)['criticals'])
        changed = copy.deepcopy(native)
        changed['combat']['healthChanges'][0]['healthAdjustments'].append({
            'damageType': 'Damage', 'amount': -1, 'attributeChanged': 'Shield', 'isCrit': False})
        self.assertEqual(result['criticals'], build_decisions(changed)['criticals'])

    def split_critical_native(self):
        native = self.native()
        first = copy.deepcopy(native['combat']['events'][1])
        first.update(frame=160, sequence=100, source='gun-a')
        second = copy.deepcopy(first)
        second.update(sequence=101, source='gun-b')
        native['combat']['events'] = [first, second]
        native['combat']['healthChanges'] = [{'frame': 160, 'side': 'enemy', 'healthAdjustments': [
            {'damageType': 'Damage', 'amount': amount, 'attributeChanged': attribute, 'isCrit': critical}
            for amount, attribute, critical in [(-119, 'Health', False), (-21, 'Shield', False),
                                                (-160, 'Health', True)]]}]
        return native

    def shield_only_damage_native(self):
        native = self.native()
        damage = native['combat']['events'][1]
        native['combat']['events'] = []
        for sequence, source in [(62, 'marlon'), (65, 'sharkray'), (68, 'calico')]:
            event = copy.deepcopy(damage)
            event.update(frame=120, sequence=sequence, source=source)
            native['combat']['events'].append(event)
        native['combat']['healthChanges'] = [{'frame': 120, 'side': 'enemy', 'healthAdjustments': [
            {'damageType': 'Damage', 'attributeChanged': 'Shield', 'amount': amount, 'isCrit': critical}
            for amount, critical in [(-120, True), (-25, False), (-90, True)]]}]
        return native

    def test_shield_only_damage_hits_retain_ordered_mixed_critical_bits(self):
        # 0466d70e frame 120: three complete hits absorbed entirely by Shield.
        native = self.shield_only_damage_native()
        result = build_decisions(native)
        self.assertEqual([('marlon', True), ('sharkray', False), ('calico', True)],
                         [(r['source'], r['critical']) for r in result['criticals']])
        self.assertEqual([], result['excluded'])
        for adjustment in native['combat']['healthChanges'][0]['healthAdjustments']:
            adjustment['amount'] = -987654321
        self.assertEqual(result, build_decisions(native))

    def test_shield_only_mixed_damage_criticals_require_complete_execution_order(self):
        for sequence in (None, 65, 61):
            with self.subTest(sequence=sequence):
                native = self.shield_only_damage_native()
                native['combat']['events'][-1]['sequence'] = sequence
                result = build_decisions(native)
                self.assertEqual([], result['criticals'])
                self.assertEqual('ambiguous critical sources', result['excluded'][0]['reason'])

    def test_shield_only_mixed_damage_criticals_reject_missing_adjustments(self):
        native = self.shield_only_damage_native()
        native['combat']['healthChanges'][0]['healthAdjustments'].pop()
        result = build_decisions(native)
        self.assertEqual([], result['criticals'])
        self.assertEqual('ambiguous critical sources', result['excluded'][0]['reason'])

    def test_fourth_battle_mixed_criticals_with_adjacent_shield_split_are_explicit(self):
        # b60fa630 frame 160: gun-a HP false + Shield false, gun-b HP true.
        native = self.split_critical_native()
        result = build_decisions(native)
        self.assertEqual([('gun-a', False), ('gun-b', True)],
                         [(r['source'], r['critical']) for r in result['criticals']])
        self.assertEqual([], result['excluded'])
        changed = copy.deepcopy(native)
        rows = changed['combat']['healthChanges'][0]['healthAdjustments']
        rows[0], rows[1] = rows[1], rows[0]
        self.assertEqual(result, build_decisions(changed))
        for row in rows:
            row['amount'] = 123456789
        self.assertEqual(result, build_decisions(changed))

    def test_split_mixed_criticals_refuse_incomplete_or_inconsistent_boundaries(self):
        cases = []
        native = self.split_critical_native()
        for sequence in (None, 99):
            changed = copy.deepcopy(native)
            changed['combat']['events'][-1]['sequence'] = sequence
            cases.append(changed)
        changed = copy.deepcopy(native)
        changed['combat']['healthChanges'][0]['healthAdjustments'].pop()
        cases.append(changed)  # one split hit cannot reveal the second hit
        changed = copy.deepcopy(native)
        changed['combat']['healthChanges'][0]['healthAdjustments'][0]['isCrit'] = True
        cases.append(changed)  # HP true / Shield false / HP true cannot partition
        changed = copy.deepcopy(native)
        changed['combat']['healthChanges'][0]['healthAdjustments'].append({
            'damageType': 'Damage', 'amount': -1, 'attributeChanged': 'Health', 'isCrit': False})
        cases.append(changed)  # more HP adjustments than executions
        changed = copy.deepcopy(native)
        changed['combat']['healthChanges'][0]['healthAdjustments'].insert(2, {
            'damageType': 'Damage', 'amount': -1, 'attributeChanged': 'Shield', 'isCrit': False})
        cases.append(changed)  # two shield rows cannot belong to one split hit
        changed = copy.deepcopy(native)
        changed['combat']['healthChanges'][0]['healthAdjustments'][1]['attributeChanged'] = 'Unknown'
        cases.append(changed)
        for index, changed in enumerate(cases):
            with self.subTest(case=index):
                result = build_decisions(changed)
                self.assertEqual([], result['criticals'])
                self.assertEqual('ambiguous critical sources', result['excluded'][0]['reason'])

    def test_mixed_shield_split_requires_unique_contiguous_partition(self):
        native = self.split_critical_native()
        middle = copy.deepcopy(native['combat']['events'][0])
        middle.update(sequence=101, source='gun-middle')
        native['combat']['events'][-1]['sequence'] = 102
        native['combat']['events'].insert(1, middle)
        native['combat']['healthChanges'][0]['healthAdjustments'].insert(2, {
            'damageType': 'Damage', 'amount': -1, 'attributeChanged': 'Health', 'isCrit': False})
        # The Shield false row could belong to either preceding or following
        # HP false. Keep the mixed group excluded rather than guessing a split.
        result = build_decisions(native)
        self.assertEqual([], result['criticals'])
        self.assertEqual('ambiguous critical sources', result['excluded'][0]['reason'])

    def test_same_critical_flag_for_multiple_sources_is_unambiguous(self):
        native = self.native()
        second = copy.deepcopy(native['combat']['events'][1])
        second['source'] = 'other'
        native['combat']['events'].append(second)
        native['combat']['healthChanges'][0]['healthAdjustments'].append({
            'damageType': 'Damage', 'amount': -15, 'attributeChanged': 'Shield', 'isCrit': True})
        native['combat']['healthChanges'][0]['healthAdjustments'].append({
            'damageType': 'Damage', 'amount': -5, 'attributeChanged': 'Health', 'isCrit': True})
        result = build_decisions(native)
        self.assertEqual(['a', 'other'], [r['source'] for r in result['criticals']])
        self.assertTrue(all(r['critical'] for r in result['criticals']))
        self.assertEqual([], result['excluded'])

    def mixed_shield_native(self):
        return {'replay': {'sha256': 'shield-replay'}, 'combat': {
            'events': [{'frame': 343, 'sequence': sequence, 'source': source,
                        'action': 'PlayerShieldApply', 'fields': {'EffectId': '0'},
                        'target': {'sides': ['player']}}
                       for sequence, source in [(554, 'wallace'), (557, 'copper')]],
            'healthChanges': [{'frame': 343, 'side': 'player', 'healthAdjustments': [
                {'damageType': 'Shield', 'attributeChanged': 'Shield', 'amount': 20, 'isCrit': bit}
                for bit in (True, False)]}]}}

    def test_mixed_shield_criticals_follow_complete_native_execution_sequence(self):
        native = self.mixed_shield_native()
        result = build_decisions(native)
        self.assertEqual([('wallace', True), ('copper', False)],
                         [(r['source'], r['critical']) for r in result['criticals']])
        self.assertEqual([], result['excluded'])
        # Amounts cannot be used to identify the owning source.
        for row in native['combat']['healthChanges'][0]['healthAdjustments']:
            row['amount'] = 987654321
        self.assertEqual(result, build_decisions(native))

    def test_mixed_shield_criticals_reject_missing_capped_or_unknown_adjustments(self):
        for case in ('missing', 'extra', 'unknown', 'extra_unknown', 'attribute'):
            with self.subTest(case=case):
                native = self.mixed_shield_native()
                rows = native['combat']['healthChanges'][0]['healthAdjustments']
                if case == 'missing':
                    rows.pop()
                elif case == 'extra':
                    rows.append(copy.deepcopy(rows[0]))
                elif case == 'unknown':
                    del rows[0]['isCrit']
                elif case == 'extra_unknown':
                    rows.append(dict(rows[0], isCrit=None))
                else:
                    rows[0]['attributeChanged'] = 'Health'
                result = build_decisions(native)
                self.assertEqual([], result['criticals'])
                self.assertTrue(result['excluded'])

    def test_mixed_shield_criticals_require_strict_complete_sequence(self):
        for sequence in (None, 554, 553):
            with self.subTest(sequence=sequence):
                native = self.mixed_shield_native()
                native['combat']['events'][-1]['sequence'] = sequence
                result = build_decisions(native)
                self.assertEqual([], result['criticals'])
                self.assertTrue(result['excluded'])

    def test_split_hit_does_not_prove_another_capped_hit_critical(self):
        native = self.native()
        second = copy.deepcopy(native['combat']['events'][1])
        second['source'] = 'other'
        native['combat']['events'].append(second)
        native['combat']['healthChanges'][0]['healthAdjustments'].append({
            'damageType': 'Damage', 'amount': -15, 'attributeChanged': 'Shield', 'isCrit': True})
        result = build_decisions(native)
        self.assertEqual([], result['criticals'])
        self.assertEqual('ambiguous critical sources', result['excluded'][0]['reason'])

    def test_order_recovers_activation_from_priority_groups(self):
        # b 的 High 效果在帧首、Medium 效果在帧尾；Medium 组顺序才是真实
        # 激活顺序，因此 b 排在 a 之后（00a37bf3 帧 80/120）。
        native = self.native()
        native['opening'] = {
            'player': {'items': [
                {'instanceId': 'b', 'templateId': 'tpl-b'},
                {'instanceId': 'a', 'templateId': 'tpl-a'}]},
            'enemy': {'items': []}}
        native['combat']['events'] = [
            {'frame': 20, 'source': 'b', 'triggerType': 'TTriggerOnCardFired',
             'action': 'CardModifyAttribute', 'fields': {'EffectId': 'high'}},
            {'frame': 20, 'source': 'a', 'triggerType': 'TTriggerOnCardFired',
             'action': 'CardModifyAttribute', 'fields': {'EffectId': 'medium'}},
            {'frame': 20, 'source': 'b', 'triggerType': 'TTriggerOnCardFired',
             'action': 'CardModifyAttribute', 'fields': {'EffectId': 'medium'}}]
        import tools.replay_random_decisions as replay
        saved = replay._TEMPLATE_PRIORITIES
        replay._TEMPLATE_PRIORITIES = {'tpl-b': {'high': 2, 'medium': 3},
                                       'tpl-a': {'medium': 3}}
        try:
            self.assertEqual([{'frame': 20, 'sources': ['a', 'b']}],
                             build_decisions(native)['order'])
        finally:
            replay._TEMPLATE_PRIORITIES = saved

    def test_order_keeps_first_activation_when_tail_effect_is_low(self):
        # b 的 High 效果在帧首、Low 效果在帧尾；Low 不参与激活顺序，
        # 因此保持 b 在 a 之前（0276c46b 帧 80/240）。
        native = self.native()
        native['opening'] = {
            'player': {'items': [
                {'instanceId': 'b', 'templateId': 'tpl-b'},
                {'instanceId': 'a', 'templateId': 'tpl-a'}]},
            'enemy': {'items': []}}
        native['combat']['events'] = [
            {'frame': 20, 'source': 'b', 'triggerType': 'TTriggerOnCardFired',
             'action': 'CardModifyAttribute', 'fields': {'EffectId': 'high'}},
            {'frame': 20, 'source': 'a', 'triggerType': 'TTriggerOnCardFired',
             'action': 'CardModifyAttribute', 'fields': {'EffectId': 'medium'}},
            {'frame': 20, 'source': 'b', 'triggerType': 'TTriggerOnCardFired',
             'action': 'CardModifyAttribute', 'fields': {'EffectId': 'low'}}]
        import tools.replay_random_decisions as replay
        saved = replay._TEMPLATE_PRIORITIES
        replay._TEMPLATE_PRIORITIES = {'tpl-b': {'high': 2, 'low': 4},
                                       'tpl-a': {'medium': 3}}
        try:
            self.assertEqual([{'frame': 20, 'sources': ['b', 'a']}],
                             build_decisions(native)['order'])
        finally:
            replay._TEMPLATE_PRIORITIES = saved

    def test_order_only_uses_native_on_card_fired_abilities(self):
        native = self.native()
        native['combat']['events'] = [
            {'frame': 20, 'source': source, 'codeName': 'CombatSimEventEffectExecuted',
             'triggerType': trigger, 'action': 'CardModifyAttribute', 'fields': {'EffectId': '0'}}
            for source, trigger in [('b', 'TTriggerOnCardFired'), ('passive', 'TTriggerOnItemUsed'),
                                    ('a', 'TTriggerOnCardFired'), ('b', 'TTriggerOnCardFired')]]
        result = build_decisions(native)
        # 没有模板优先级时退化为首次出现顺序；TTriggerOnItemUsed 源不参与。
        self.assertEqual([{'frame': 20, 'sources': ['b', 'a']}], result['order'])
        self.assertNotIn('amount', json.dumps(result))

    def test_random_enchantment_retains_name_even_for_deterministic_target(self):
        native = self.native()
        native['combat']['events'] = [
            {'frame': 0, 'source': 'a', 'action': 'CardEnchant', 'actionType': 'TActionCardEnchantRandom',
             'fields': {'EffectId': '2'}, 'randomTarget': False, 'target': {'ids': ['b']}},
            {'frame': 0, 'codeName': 'CombatSimEventCardEnchanted', 'instanceId': 'b',
             'enchantment': 'Turbo', 'reverted': False}]
        result = build_decisions(native)
        self.assertEqual([{'frame': 0, 'source': 'a', 'target': 'b', 'name': 'Turbo'}],
                         result['enchantments'])
        self.assertEqual([], result['targets'])

    def status_native(self, previous, current, amount=2, tick=True, action='PlayerBurnApply',
                      update=None, opening_amount=None):
        attribute = 'Burn' if action == 'PlayerBurnApply' else 'Poison'
        amount_attribute = attribute + 'ApplyAmount'
        health = {'frame': 10, 'side': 'enemy',
                  'attributes': {attribute: {'previous': previous, 'current': current}},
                  'healthAdjustments': []}
        if tick:
            health['healthAdjustments'].append({
                'damageType': attribute, 'amount': -previous, 'attributeChanged': 'Health', 'isCrit': False})
        attributes = {amount_attribute: amount if opening_amount is None else opening_amount}
        native = {'replay': {'sha256': 'sha'},
                  'opening': {'player': {'items': [{'instanceId': 'gun', 'attributes': attributes}]},
                              'enemy': {'items': []}},
                  'combat': {'events': [{'frame': 10, 'source': 'gun', 'action': action,
                                         'fields': {'EffectId': '0'}, 'target': {'sides': ['enemy']}}],
                             'healthChanges': [health]}}
        if update is not None:
            native['combat']['cardUpdates'] = [{
                'frame': 10, 'instanceId': 'gun', 'attribute': amount_attribute,
                'previous': update[0], 'current': update[1]}]
        return native

    def test_status_apply_critical_reads_the_native_stack_delta(self):
        # b60fa630 frame 130: Burn 22 -> 23 after the tick means 2 applied, no critical.
        self.assertFalse(build_decisions(self.status_native(5, 6))['criticals'][0]['critical'])
        self.assertTrue(build_decisions(self.status_native(5, 8))['criticals'][0]['critical'])
        result = build_decisions(self.status_native(5, 7))
        self.assertEqual([], result['criticals'])
        self.assertEqual('ambiguous status critical', result['excluded'][0]['reason'])

    def test_large_burn_tick_uses_three_percent_decay_for_critical_inference(self):
        self.assertFalse(build_decisions(self.status_native(400, 398, amount=10))['criticals'][0]['critical'])
        self.assertTrue(build_decisions(self.status_native(400, 408, amount=10))['criticals'][0]['critical'])

    def test_regen_critical_is_independent_of_heal_cleanse(self):
        native = self.status_native(36, 44, amount=4, tick=False, action='PlayerPoisonApply')
        native['opening']['player']['items'][0]['attributes'] = {'RegenApplyAmount': 4}
        event = native['combat']['events'][0]
        event['action'] = 'PlayerRegenApply'
        state = native['combat']['healthChanges'][0]
        state['attributes']['HealthRegen'] = state['attributes'].pop('Poison')
        result = build_decisions(native)
        self.assertEqual('regen', result['criticals'][0]['kind'])
        self.assertTrue(result['criticals'][0]['critical'])
        native['combat']['events'].append(dict(frame=event['frame'], action='PlayerModifyAttribute', target=event['target']))
        self.assertTrue(build_decisions(native)['excluded'])

    def test_status_apply_critical_accepts_a_same_frame_self_buff(self):
        # b60fa630 frame 123: BurnApplyAmount 14 -> 15 lands on the same frame
        # as the 15-stack apply, so only the post-buff value explains it.
        result = build_decisions(self.status_native(2, 16, opening_amount=14, update=(14, 15)))
        self.assertEqual([('gun', False)],
                         [(r['source'], r['critical']) for r in result['criticals']])

    def test_poison_apply_critical_has_no_tick_decay(self):
        result = build_decisions(self.status_native(0, 4, tick=False, action='PlayerPoisonApply'))
        self.assertTrue(result['criticals'][0]['critical'])
        result = build_decisions(self.status_native(0, 2, tick=False, action='PlayerPoisonApply'))
        self.assertFalse(result['criticals'][0]['critical'])

    def healed_status_native(self, previous=342, current=409, nominal=25, actual=25):
        native = self.status_native(previous, current, amount=34, tick=False,
                                    action='PlayerPoisonApply')
        native['opening']['enemy']['items'].append({
            'instanceId': 'butter', 'attributes': {} if nominal is None else {'HealAmount': nominal}})
        native['combat']['events'].append({
            'frame': 10, 'source': 'butter', 'action': 'PlayerHeal',
            'fields': {'EffectId': '0'}, 'target': {'sides': ['enemy']}})
        native['combat']['healthChanges'][0]['healthAdjustments'].append({
            'damageType': 'Heal', 'attributeChanged': 'Health', 'amount': actual, 'isCrit': False})
        return native

    def test_status_critical_accounts_for_same_frame_nominal_heal_cleanse(self):
        result = build_decisions(self.healed_status_native())
        self.assertEqual([True], [r['critical'] for r in result['criticals'] if r['kind'] == 'poison'])
        self.assertEqual([], result['excluded'])

    def test_status_cleanse_uses_nominal_heal_even_when_most_healing_overflows(self):
        # Nominal 100 cleanses five stacks even though only one HP was restored.
        result = build_decisions(self.healed_status_native(current=405, nominal=100, actual=1))
        self.assertEqual([True], [r['critical'] for r in result['criticals'] if r['kind'] == 'poison'])

    def test_status_cleanse_without_nominal_or_explicit_critical_is_not_guessed(self):
        for missing in ('nominal', 'critical'):
            with self.subTest(missing=missing):
                native = self.healed_status_native(nominal=None if missing == 'nominal' else 25)
                if missing == 'critical':
                    del native['combat']['healthChanges'][0]['healthAdjustments'][0]['isCrit']
                result = build_decisions(native)
                self.assertFalse(any(r['kind'] == 'poison' for r in result['criticals']))
                self.assertTrue(result['excluded'])

    def test_status_cleanse_rejects_unattributed_healing_and_other_status_removal(self):
        for unknown in ('heal', 'removal'):
            with self.subTest(unknown=unknown):
                native = self.healed_status_native()
                if unknown == 'heal':
                    native['combat']['events'].pop()
                else:
                    native['combat']['events'].append({
                        'frame': 10, 'source': 'other', 'action': 'PlayerPoisonRemove',
                        'target': {'sides': ['enemy']}})
                result = build_decisions(native)
                self.assertFalse(any(r['kind'] == 'poison' for r in result['criticals']))
                self.assertTrue(result['excluded'])

    def test_status_cleanse_rounds_remaining_stack_to_even_and_keeps_ambiguity(self):
        # 50 nominal healing removes 2.5; round the remaining stack, not 2.5.
        result = build_decisions(self.healed_status_native(previous=343, current=408, nominal=50))
        self.assertEqual([True], [r['critical'] for r in result['criticals'] if r['kind'] == 'poison'])
        # A wash large enough to erase either candidate cannot identify the bit.
        result = build_decisions(self.healed_status_native(previous=0, current=0, nominal=2000))
        self.assertFalse(any(r['kind'] == 'poison' for r in result['criticals']))
        self.assertTrue(result['excluded'])

    def test_status_cleanse_requires_a_unique_status_apply_and_uses_explicit_heal_crit(self):
        native = self.healed_status_native(current=408)
        native['combat']['healthChanges'][0]['healthAdjustments'][0]['isCrit'] = True
        result = build_decisions(native)
        self.assertEqual([True], [r['critical'] for r in result['criticals'] if r['kind'] == 'poison'])
        native['combat']['events'].append(copy.deepcopy(native['combat']['events'][0]))
        result = build_decisions(native)
        self.assertFalse(any(r['kind'] == 'poison' for r in result['criticals']))
        self.assertTrue(any(r['reason'] == 'multiple status applies' for r in result['excluded']))

    def joint_status_native(self, nominals=(22, 25, 25), previous=430, current=526):
        native = self.status_native(previous, current, tick=False, action='PlayerPoisonApply')
        native['opening']['player']['items'] = [
            {'instanceId': f'gun-{index}', 'attributes': {'PoisonApplyAmount': nominal}}
            for index, nominal in enumerate(nominals)]
        native['combat']['events'] = [
            {'frame': 10, 'source': f'gun-{index}', 'action': 'PlayerPoisonApply',
             'fields': {'EffectId': '0'}, 'target': {'sides': ['enemy']}}
            for index in range(len(nominals))]
        native['combat']['cardUpdates'] = [
            {'frame': 10, 'instanceId': f'gun-{index}', 'attribute': 'PoisonApplyAmount',
             'previous': nominal, 'current': nominal + 2}
            for index, nominal in enumerate(nominals)]
        return native

    def test_joint_status_criticals_accept_only_a_unique_mask_across_nominal_candidates(self):
        # 034c3648 frame 343: 44+25+27 or 44+27+25; nominal ambiguity
        # remains, but both solutions identify precisely the same critical bits.
        result = build_decisions(self.joint_status_native())
        self.assertEqual([('gun-0', True), ('gun-1', False), ('gun-2', False)],
                         [(r['source'], r['critical']) for r in result['criticals']])
        self.assertEqual([], result['excluded'])

    def test_joint_burn_with_tick_keeps_unknown_stack_decay_excluded(self):
        native = self.joint_status_native((1, 2), previous=67, current=69)
        for item in native['opening']['player']['items']:
            item['attributes']['BurnApplyAmount'] = item['attributes'].pop('PoisonApplyAmount')
        for event in native['combat']['events']:
            event['action'] = 'PlayerBurnApply'
        native['combat']['cardUpdates'] = []
        change = native['combat']['healthChanges'][0]
        change['attributes']['Burn'] = change['attributes'].pop('Poison')
        change['healthAdjustments'] = [{
            'damageType': 'Burn', 'attributeChanged': 'Health', 'amount': -67, 'isCrit': False}]
        # Actual decay is two stacks here. Adding one to the net delta would
        # manufacture the false/false mask even though the native bit is unknown.
        result = build_decisions(native)
        self.assertEqual([], result['criticals'])
        self.assertTrue(result['excluded'])

    def test_joint_status_criticals_keep_ambiguous_unsupported_or_unbounded_groups_excluded(self):
        for case in ('ambiguous', 'no_solution', 'same_source', 'too_many', 'unknown_nominal',
                     'fractional', 'zero', 'heal', 'remove', 'modify', 'unknown_heal'):
            with self.subTest(case=case):
                native = self.joint_status_native()
                if case == 'ambiguous':
                    native = self.joint_status_native((10, 10), previous=0, current=30)
                    native['combat']['cardUpdates'] = []
                elif case == 'no_solution':
                    native['combat']['healthChanges'][0]['attributes']['Poison']['current'] = 9999
                elif case == 'same_source':
                    native['combat']['events'][-1]['source'] = 'gun-0'
                elif case == 'too_many':
                    native = self.joint_status_native((1,) * 7, previous=0, current=14)
                elif case in ('unknown_nominal', 'fractional', 'zero'):
                    attributes = native['opening']['player']['items'][0]['attributes']
                    if case == 'unknown_nominal':
                        attributes.clear()
                    else:
                        attributes['PoisonApplyAmount'] = 1.5 if case == 'fractional' else 0
                elif case == 'unknown_heal':
                    native['combat']['healthChanges'][0]['healthAdjustments'].append({
                        'damageType': 'Heal', 'attributeChanged': 'Health', 'isCrit': False})
                else:
                    native['combat']['events'].append({
                        'frame': 10, 'source': 'other', 'target': {'sides': ['enemy']},
                        'action': {'heal': 'PlayerHeal', 'remove': 'PlayerPoisonRemove',
                                   'modify': 'PlayerModifyAttribute'}[case]})
                result = build_decisions(native)
                self.assertEqual([], result['criticals'])
                self.assertTrue(result['excluded'])

    def test_status_apply_without_live_amount_is_not_guessed(self):
        native = self.status_native(5, 6)
        native['opening']['player']['items'][0]['attributes'] = {}
        result = build_decisions(native)
        self.assertEqual([], result['criticals'])
        self.assertEqual('status apply amount unknown', result['excluded'][0]['reason'])

    def test_split_health_and_shield_from_one_hit_is_one_critical_decision(self):
        native = self.native()
        native['combat']['healthChanges'][0]['healthAdjustments'].append({
            'damageType': 'Damage', 'amount': -5, 'attributeChanged': 'Shield', 'isCrit': True})
        result = build_decisions(native)
        self.assertEqual(1, len(result['criticals']))

    def test_controlled_targets_are_compared_instead_of_waived(self):
        from tools.test_native_replay_compare import native_ledger, sim_ledger, native_item, sim_item, native_effect, sim_effect
        left_items = {'player:' + iid: native_item('player', iid, slot, iid) for slot, iid in enumerate(('a', 'b', 'c'))}
        right_items = {'player:' + iid: sim_item('player', iid, slot, iid) for slot, iid in enumerate(('a', 'b', 'c'))}
        effect = native_effect('CardHaste', 'a', 'b')
        effect['randomTarget'] = True
        left = native_ledger(left_items, events=[effect])
        right = sim_ledger(right_items, events=[sim_effect('haste', 'a', 'c')])
        result = compare.compare_documents(left, right, observed_decisions=True)
        self.assertEqual('DIFF', result['verdict'])
        self.assertGreater(result['counts']['missing'] + result['counts']['extra'], 0)
        self.assertTrue(left['combat']['events'][0]['randomTarget'])

    def test_controlled_damage_error_is_not_waived_as_critical(self):
        def row(frame, hp):
            return {'frame': frame, 'time': frame * 0.05, 'side': 'enemy', 'hp': hp,
                    'hpMax': 1000, 'shield': 0, 'burn': 0, 'poison': 0, 'regen': 0}
        left = {(0, 'enemy'): [row(0, 1000)], (20, 'enemy'): [row(20, 600)]}
        right = {(0, 'enemy'): [row(0, 1000)], (20, 'enemy'): [row(20, 800)]}
        result = compare.compare_health(left, right, 0, observed_decisions=True)
        self.assertTrue(any(d['status'] == 'value-mismatch' for d in result['diffs']))
        self.assertEqual({}, result['critScope'])


if __name__ == '__main__':
    unittest.main()


class TransformIdentityDecisionTests(unittest.TestCase):
    def test_transform_notification_reuses_target_draw_and_exports_identity_only(self):
        native = {'replay': {'sha256': 'input'}, 'combat': {'events': [
            {'frame': 160, 'codeName': 'CombatSimEventEffectExecuted', 'source': 'virus',
             'action': 'CardTransform', 'randomTarget': True,
             'fields': {'ExecutionContextId': 'cast', 'EffectId': '2'}, 'target': {'ids': ['old']}},
            {'frame': 160, 'codeName': 'CombatSimEventCardTransformed', 'randomTarget': True,
             'fields': {'ExecutionContextId': 'cast'}, 'originalInstanceId': 'old',
             'transformedCards': [{'instanceId': 'new', 'templateId': 'virus', 'side': 'player', 'socket': 4}]},
        ]}}
        decisions = build_decisions(native)
        self.assertEqual(1, len(decisions['targets']))
        self.assertEqual([], decisions['excluded'])
        self.assertEqual([], decisions['transforms'])
        self.assertEqual([dict(frame=160, original='old', instanceId='new', templateId='virus',
                              side='player', slot=4, index=0)], decisions['identities'])
        native['combat']['events'][1]['fields']['ExecutionContextId'] = 'orphan'
        self.assertTrue(build_decisions(native)['excluded'])
