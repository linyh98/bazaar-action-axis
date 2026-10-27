import json
import contextlib
import io
import pathlib
import tempfile
import unittest

from tools import native_replay_compare as compare


def identity_instance(side, instance_id, slot, template, generations, parent=None, removed=None):
    return {
        "key": "%s:%s" % (side, instance_id),
        "side": side,
        "instanceId": instance_id,
        "socket": slot,
        "templateId": template,
        "parent": parent,
        "born": generations[0].get("born") if generations else None,
        "removed": removed,
        "generations": generations,
    }


def native_ledger(instances, events=None, health=None, cards=None, winner="player", unknowns=None):
    return {
        "ledgerVersion": 1,
        "complete": True,
        "replay": {"battleId": "test", "sha256": "native"},
        "contract": {"contractVersion": 1},
        "identity": {"instances": instances, "lineage": []},
        "combat": {
            "events": events or [],
            "healthChanges": health or [],
            "cardUpdates": cards or [],
            "outcome": {"winnerSide": winner},
        },
        "unknowns": unknowns or [],
    }


def sim_ledger(instances, events=None, health=None, cards=None, winner="player", unknowns=None):
    return {
        "ledgerVersion": 1,
        "kind": "simulated",
        "complete": True,
        "source": {"fixtureSha256": "fixture"},
        "identity": {"instances": instances, "lineage": []},
        "combat": {
            "events": events or [],
            "healthChanges": health or [],
            "cardUpdates": cards or [],
            "outcome": {"winnerSide": winner},
        },
        "unknowns": unknowns or [],
    }


def native_item(side, instance_id, slot, template, born=0.0, removed=None, generation=0, parent=None):
    generation_record = {
        "generation": generation,
        "templateId": template,
        "born": {"phase": "opening" if born == 0 else "combat", "time": born},
        "removed": None if removed is None else {"phase": "combat", "time": removed},
    }
    return identity_instance(side, instance_id, slot, template, [generation_record],
                             parent=parent, removed=removed)


def sim_item(side, instance_id, slot, template, born=0.0, removed=None):
    return {
        "key": "%s:%s" % (side, instance_id),
        "side": side,
        "instanceId": instance_id,
        "templateId": template,
        "slot": slot,
        "born": born,
        "removed": removed,
        "states": [{"time": born, "templateId": template, "slot": slot}],
    }


def native_effect(action, source, target, frame=10, source_side="player"):
    return {
        "codeName": "CombatSimEventEffectExecuted",
        "action": action,
        "source": source,
        "sourceSide": source_side,
        "target": {"type": "card", "ids": [target]},
        "frame": frame,
        "time": frame * 0.05,
    }


def sim_effect(kind, source, target, frame=10, source_side="player"):
    return {
        "kind": kind,
        "source": source,
        "sourceSide": source_side,
        "target": target,
        "targetSide": "player",
        "frame": frame,
        "time": frame * 0.05,
        "value": 1,
    }


class ComparatorTests(unittest.TestCase):
    def test_lethal_burn_decay_remains_a_real_health_difference(self):
        # b57a7c8840314494bd4391969b983606 frame 360: HP 3 -> -19,
        # Burn 22 -> 21. The simulator clamps HP to zero but kept Burn 22.
        native = native_ledger({}, health=[{'frame': 360, 'time': 18.0, 'side': 'player',
            'dead': False, 'vitals': {'Health': -19, 'Burn': 21}}])
        simulated = sim_ledger({}, health=[{'frame': 360, 'time': 18.0, 'side': 'player',
            'dead': True, 'hp': 0, 'burn': 22}])
        report = compare.compare_health(compare.read_health(native, False),
            compare.read_health(simulated, True), 0, observed_decisions=True,
            death_frame=360, dying_side='player')
        self.assertEqual(1, len(report['diffs']))
        self.assertEqual('value-mismatch', report['diffs'][0]['status'])
        self.assertIn('burn 21.0 vs 22.0', report['diffs'][0]['note'])
        self.assertNotIn('hp ', report['diffs'][0]['note'])
        self.assertNotIn('dead ', report['diffs'][0]['note'])
        self.assertEqual(0, report['unverifiedReasons'].get('deathFrame', 0))

    def test_death_frame_still_compares_other_observable_vitals(self):
        for attribute, field in (('Shield', 'shield'), ('Poison', 'poison'),
                                 ('HealthRegen', 'regen'), ('HealthMax', 'hpMax')):
            with self.subTest(attribute=attribute):
                native = native_ledger({}, health=[{'frame': 360, 'side': 'player',
                    'dead': False, 'vitals': {'Health': -19, attribute: 4}}])
                simulated = sim_ledger({}, health=[{'frame': 360, 'side': 'player',
                    'dead': True, 'hp': 0, field: 5}])
                report = compare.compare_health(compare.read_health(native, False),
                    compare.read_health(simulated, True), 0, observed_decisions=True,
                    death_frame=360, dying_side='player')
                self.assertEqual(1, len(report['diffs']))
                self.assertIn(field, report['diffs'][0]['note'])

    def test_negative_and_zero_hp_compare_equal_without_death_exemption(self):
        native = native_ledger({}, health=[{'frame': 360, 'side': 'player',
            'dead': False, 'vitals': {'Health': -19, 'Burn': 21}}])
        simulated = sim_ledger({}, health=[{'frame': 360, 'side': 'player',
            'dead': True, 'hp': 0, 'burn': 21}])
        report = compare.compare_health(compare.read_health(native, False),
            compare.read_health(simulated, True), 0, observed_decisions=True,
            death_frame=360, dying_side='player')
        self.assertEqual([], report['diffs'])
        self.assertEqual(0, report['unverified'])
        self.assertEqual(1, report['matched'])

    def test_simulated_status_crit_is_only_waived_with_engine_crit_event(self):
        native = native_ledger({}, health=[
            {'frame': 70, 'side': 'player', 'vitals': {'Health': 1000, 'Burn': 0}},
            {'frame': 80, 'side': 'player', 'vitals': {'Health': 1000, 'Burn': 12}}])
        simulated = sim_ledger({}, health=[
            {'frame': 70, 'side': 'player', 'hp': 1000, 'burn': 0},
            {'frame': 80, 'side': 'player', 'hp': 1000, 'burn': 24}])
        rows_native = compare.read_health(native, False)
        rows_simulated = compare.read_health(simulated, True)
        waived = compare.compare_health(rows_native, rows_simulated, 0,
            sim_crit_frames={(80, 'player')})
        self.assertEqual([], waived['diffs'])
        self.assertEqual(1, waived['unverifiedReasons'].get('critDivergence', 0))
        # Without the engine's own crit event this is a real doubled apply.
        guarded = compare.compare_health(rows_native, rows_simulated, 0,
            sim_crit_frames=set())
        self.assertEqual(1, len(guarded['diffs']))
        self.assertEqual('value-mismatch', guarded['diffs'][0]['status'])

    def test_missing_lethal_health_row_is_not_waived(self):
        native = native_ledger({}, health=[{'frame': 360, 'side': 'player',
            'vitals': {'Health': -19, 'Burn': 21}}])
        report = compare.compare_health(compare.read_health(native, False), {}, 0,
            observed_decisions=True, death_frame=360, dying_side='player')
        self.assertEqual('missing', report['diffs'][0]['status'])
        self.assertEqual(0, report['unverified'])
        # A dot tick can corroborate HP zero, but cannot corroborate the
        # omitted row's Burn/Shield/Poison state at death.
        report = compare.compare_health(compare.read_health(native, False), {}, 0,
            dot_tick_hp_after={(360, 'player'): 0}, observed_decisions=True,
            death_frame=360, dying_side='player')
        self.assertEqual('missing', report['diffs'][0]['status'])
        self.assertEqual(0, report['unverified'])

    def test_coverage_notes_do_not_mean_missing_input(self):
        native = native_ledger({}, unknowns=[{'scope': 'sample.transform', 'detail': 'no transform'}])
        report = compare.compare_documents(native, sim_ledger({}))
        self.assertEqual('MATCH', report['verdict'])
        self.assertEqual('sample.transform', report['coverageNotes'][0]['detail']['scope'])

    def test_missing_seed_requires_complete_random_audit_and_opening_stays_unknown(self):
        native = native_ledger({}, unknowns=[{'scope': 'sample.random', 'detail': 'no seed'}])
        simulated = sim_ledger({})
        audit = {'mode': 'observed-decisions', 'replaySha256': 'native'}
        for channel in ('target', 'critical', 'order', 'enchant', 'transform'):
            audit.update({channel + 'Records': 0, channel + 'Applied': 0, channel + 'Fallbacks': 0})
        simulated['replayRandom'] = audit
        self.assertEqual('UNKNOWN', compare.compare_documents(native, simulated)['verdict'])
        controlled = compare.compare_documents(native, simulated, observed_decisions=True)
        self.assertEqual('MATCH', controlled['verdict'])
        self.assertTrue(controlled['randomDecisionsControlled'])
        for patch in ({'enchantFallbacks': 1}, {'invalidChoices': 1}, {'orderRecords': 1},
                      {'replaySha256': 'wrong-sha'}):
            simulated['replayRandom'] = dict(audit, **patch)
            self.assertEqual('UNKNOWN', compare.compare_documents(native, simulated, observed_decisions=True)['verdict'])
        simulated['replayRandom'] = audit
        native['unknowns'].append({'scope': 'opening.attribute.player', 'detail': 'HealthMax absent'})
        self.assertEqual('UNKNOWN', compare.compare_documents(native, simulated, observed_decisions=True)['verdict'])

    def test_random_audit_cannot_resolve_ambiguous_or_missing_decisions(self):
        native = native_ledger({}, unknowns=[{'scope': 'sample.random', 'detail': 'no seed'}], events=[
            {'frame': 20, 'source': iid, 'action': 'PlayerDamage', 'target': {'sides': ['enemy']}}
            for iid in ('a', 'b')], health=[{'frame': 20, 'side': 'enemy', 'healthAdjustments': [
                {'damageType': 'Damage', 'attributeChanged': 'Health', 'isCrit': value}
                for value in (False, True)]}])
        simulated = sim_ledger({})
        audit = {'mode': 'observed-decisions', 'replaySha256': 'native'}
        for channel in ('target', 'critical', 'order', 'enchant', 'transform'):
            audit.update({channel + 'Records': 0, channel + 'Applied': 0, channel + 'Fallbacks': 0})
        simulated['replayRandom'] = audit
        report = compare.compare_documents(native, simulated, observed_decisions=True)
        self.assertFalse(report['randomDecisionsControlled'])
        self.assertTrue(any(u['detail'].get('scope') == 'sample.random' for u in report['unknowns']))

    def run_compare(self, native, simulated, window=0):
        with tempfile.TemporaryDirectory() as folder:
            left = pathlib.Path(folder) / "native.json"
            right = pathlib.Path(folder) / "sim.json"
            output = pathlib.Path(folder) / "diff.json"
            left.write_text(json.dumps(native), encoding="utf-8")
            right.write_text(json.dumps(simulated), encoding="utf-8")
            with contextlib.redirect_stdout(io.StringIO()):
                code = compare.main([str(left), str(right), "--json", str(output),
                                     "--window-frames", str(window)])
            return code, json.loads(output.read_text(encoding="utf-8"))

    def test_matching_ledgers_pass(self):
        native = native_ledger(
            {"player:a": native_item("player", "a", 0, "A")},
            events=[native_effect("PlayerDamage", "a", "a")],
            health=[{"frame": 10, "time": 0.5, "side": "player",
                     "vitals": {"Health": 90}, "dead": False}],
            cards=[{"frame": 10, "time": 0.5, "instanceId": "a", "side": "player",
                    "attribute": "Cooldown", "previous": 1000, "current": 950}],
        )
        simulated = sim_ledger(
            {"player:a": sim_item("player", "a", 0, "A")},
            events=[sim_effect("damage", "a", "a")],
            health=[{"frame": 10, "time": 0.5, "side": "player", "hp": 90, "dead": False}],
            cards=[{"frame": 10, "time": 0.5, "instanceId": "a", "side": "player",
                    "attribute": "Cooldown", "previous": 1000, "current": 950}],
        )
        code, report = self.run_compare(native, simulated)
        self.assertEqual(0, code)
        self.assertEqual("MATCH", report["verdict"])
        self.assertEqual(1, report["channels"]["effects"]["matched"])
        self.assertEqual(1, report["channels"]["health"]["matched"])
        self.assertEqual(1, report["channels"]["cards"]["matched"])

    def test_missing_effect_is_a_diff(self):
        native = native_ledger(
            {"player:a": native_item("player", "a", 0, "A")},
            events=[native_effect("PlayerDamage", "a", "a")],
        )
        simulated = sim_ledger({"player:a": sim_item("player", "a", 0, "A")}, events=[])
        code, report = self.run_compare(native, simulated)
        self.assertEqual(1, code)
        self.assertEqual("DIFF", report["verdict"])
        self.assertEqual(1, report["channels"]["effects"]["missing"])

    def test_transform_lineage_aligns_native_child_to_sim_generation(self):
        native = native_ledger({
            "player:a": native_item("player", "a", 0, "A", removed=2.0),
            "player:b": native_item("player", "b", 0, "B", born=2.0, generation=1, parent="player:a"),
        })
        simulated = sim_ledger({
            "player:a": {
                "key": "player:a", "side": "player", "instanceId": "a", "templateId": "A",
                "slot": 0, "born": 0.0, "removed": None,
                "states": [{"time": 0.0, "templateId": "A", "slot": 0},
                           {"time": 2.0, "templateId": "B", "slot": 0}],
            },
        })
        code, report = self.run_compare(native, simulated)
        self.assertEqual(0, code)
        self.assertEqual("MATCH", report["verdict"])
        self.assertEqual(2, report["channels"]["identity"]["matched"])

    def test_health_value_mismatch_is_reported(self):
        native = native_ledger(
            {"player:a": native_item("player", "a", 0, "A")},
            health=[{"frame": 10, "time": 0.5, "side": "player",
                     "vitals": {"Health": 90}, "dead": False}],
        )
        simulated = sim_ledger(
            {"player:a": sim_item("player", "a", 0, "A")},
            health=[{"frame": 10, "time": 0.5, "side": "player", "hp": 91, "dead": False}],
        )
        code, report = self.run_compare(native, simulated)
        self.assertEqual(1, code)
        self.assertEqual("DIFF", report["verdict"])
        self.assertEqual(1, report["channels"]["health"]["valueMismatch"])

    def test_time_window_is_explicit(self):
        native = native_ledger(
            {"player:a": native_item("player", "a", 0, "A")},
            events=[native_effect("PlayerDamage", "a", "a", frame=10)],
        )
        simulated = sim_ledger(
            {"player:a": sim_item("player", "a", 0, "A")},
            events=[sim_effect("damage", "a", "a", frame=11)],
        )
        code, report = self.run_compare(native, simulated)
        self.assertEqual(1, code)
        self.assertEqual(1, report["channels"]["effects"]["missing"])
        code, report = self.run_compare(native, simulated, window=1)
        self.assertEqual(1, code)
        self.assertEqual(1, report["channels"]["effects"]["timeMismatch"])

    def test_runtime_ability_is_unverified_not_missing(self):
        effect = native_effect("CardModifyAttribute", "a", "a", frame=10)
        effect["runtimeAbility"] = True
        native = native_ledger({"player:a": native_item("player", "a", 0, "A")},
                               events=[effect])
        simulated = sim_ledger({"player:a": sim_item("player", "a", 0, "A")})
        code, report = self.run_compare(native, simulated)
        self.assertEqual(2, code)
        self.assertEqual(1, report["channels"]["effects"]["unverified"])
        self.assertEqual(0, report["channels"]["effects"]["missing"])
        self.assertEqual(1, report["channels"]["effects"]["unverifiedReasons"].get("runtimeAbility", 0))

    def test_aura_refresh_card_update_is_unverified(self):
        effect = native_effect("CardModifyAttribute", "a", "a", frame=10)
        effect["auraRefresh"] = True
        native = native_ledger(
            {"player:a": native_item("player", "a", 0, "A")},
            events=[effect],
            cards=[{"frame": 10, "time": 0.5, "instanceId": "a", "side": "player",
                    "attribute": "ShieldApplyAmount", "previous": 2, "current": 12}],
        )
        simulated = sim_ledger({"player:a": sim_item("player", "a", 0, "A")})
        code, report = self.run_compare(native, simulated)
        self.assertEqual(2, code)
        self.assertEqual(1, report["channels"]["cards"]["unverified"])
        self.assertEqual(0, report["channels"]["cards"]["missing"])
        self.assertEqual(1, report["channels"]["cards"]["unverifiedReasons"].get("auraRefresh", 0))

    def test_random_target_is_unverified_not_missing(self):
        effect = native_effect("CardHaste", "a", "a", frame=10)
        effect["randomTarget"] = True
        native = native_ledger({"player:a": native_item("player", "a", 0, "A")},
                               events=[effect])
        simulated = sim_ledger({"player:a": sim_item("player", "a", 0, "A")},
                               events=[sim_effect("haste", "a", "b", frame=10)])
        code, report = self.run_compare(native, simulated)
        self.assertEqual(2, code)
        self.assertEqual("UNKNOWN", report["verdict"])
        self.assertEqual(1, report["channels"]["effects"]["unverified"])
        self.assertEqual(0, report["channels"]["effects"]["missing"])
        self.assertEqual(0, report["channels"]["effects"]["extra"])

    def test_skill_trigger_is_unverified_not_extra(self):
        native = native_ledger({"player:a": native_item("player", "a", 0, "A")})
        native["opening"] = {"player": {"skills": [{"instanceId": "skl_x"}]},
                             "enemy": {"skills": []}}
        simulated = sim_ledger(
            {"player:a": sim_item("player", "a", 0, "A")},
            events=[sim_effect("skillTrigger", "skill:skl_x", None, frame=0)],
        )
        code, report = self.run_compare(native, simulated)
        self.assertEqual(2, code)
        self.assertEqual(1, report["channels"]["effects"]["unverified"])
        self.assertEqual(0, report["channels"]["effects"]["extra"])
        self.assertEqual(0, report["channels"]["identity"]["extra"])

    def test_random_target_cascade_marks_card_updates_unverified(self):
        effect = native_effect("CardHaste", "a", "a", frame=10)
        effect["randomTarget"] = True
        native = native_ledger(
            {"player:a": native_item("player", "a", 0, "A")},
            events=[effect],
            cards=[{"frame": 10, "time": 0.5, "instanceId": "a", "side": "player",
                    "attribute": "Cooldown", "previous": 1000, "current": 900}],
        )
        simulated = sim_ledger(
            {"player:a": sim_item("player", "a", 0, "A")},
            events=[sim_effect("haste", "a", "b", frame=10)],
            cards=[{"frame": 10, "time": 0.5, "instanceId": "a", "side": "player",
                    "attribute": "Cooldown", "previous": 1000, "current": 800}],
        )
        code, report = self.run_compare(native, simulated)
        self.assertEqual(2, code)
        self.assertEqual(1, report["channels"]["cards"]["unverified"])
        self.assertEqual(0, report["channels"]["cards"]["valueMismatch"])

    def test_regen_tick_heal_duplicate_is_not_an_effect(self):
        simulated = sim_ledger(
            {"player:a": sim_item("player", "a", 0, "A")},
            events=[
                {"kind": "dotTick", "frame": 100, "time": 5.0, "source": "a",
                 "sourceSide": "player", "targetSide": "player", "value": 3,
                 "hpAfter": 90, "note": "regen"},
                {"kind": "heal", "frame": 100, "time": 5.0, "source": "a",
                 "sourceSide": "player", "targetSide": "player", "value": 3,
                 "hpAfter": 90, "note": "regen"},
            ],
        )
        native = native_ledger({"player:a": native_item("player", "a", 0, "A")})
        code, report = self.run_compare(native, simulated)
        self.assertEqual(0, report["channels"]["effects"]["extra"])

    def test_explicit_heal_at_same_regen_hp_is_still_verified(self):
        heal = {'kind': 'heal', 'frame': 100, 'time': 5.0, 'source': 'a',
                'sourceSide': 'player', 'targetSide': 'player', 'value': 0, 'hpAfter': 100}
        tick = dict(heal, kind='dotTick', note='regen')
        native = native_ledger({'player:a': native_item('player', 'a', 0, 'A')})
        simulated = sim_ledger({'player:a': sim_item('player', 'a', 0, 'A')}, events=[tick, heal])
        _, report = self.run_compare(native, simulated)
        self.assertEqual(1, report['channels']['effects']['extra'])

    def test_lifesteal_healing_is_health_change_and_not_player_heal_action(self):
        native = native_ledger({'player:a': native_item('player', 'a', 0, 'A')},
            health=[{'frame': 100, 'time': 5.0, 'side': 'player', 'vitals': {'Health': 90}}])
        simulated = sim_ledger({'player:a': sim_item('player', 'a', 0, 'A')},
            events=[{'kind': 'heal', 'frame': 100, 'time': 5.0, 'source': 'a',
                     'sourceSide': 'player', 'targetSide': 'player', 'value': 5,
                     'hpAfter': 91, 'note': 'lifesteal'}],
            health=[{'frame': 100, 'time': 5.0, 'side': 'player', 'hp': 91}])
        _, report = self.run_compare(native, simulated)
        self.assertEqual(0, report['channels']['effects']['extra'])
        self.assertEqual(1, report['channels']['health']['valueMismatch'])

    def test_random_transform_lineage_is_unverified(self):
        native = native_ledger(
            {"player:a": native_item("player", "a", 0, "A"),
             "player:child": native_item("player", "child", 0, "B", born=1.0, generation=1,
                                         parent="player:a")},
            events=[{"codeName": "CombatSimEventCardTransformed", "frame": 20, "time": 1.0,
                     "originalInstanceId": "a", "randomTarget": True,
                     "transformedCards": [{"instanceId": "child", "side": "player"}]}],
        )
        simulated = sim_ledger(
            {"player:a": sim_item("player", "a", 0, "A"),
             "player:other": sim_item("player", "other", 0, "B", born=1.0)},
            events=[{"kind": "transform", "frame": 20, "time": 1.0, "source": "a",
                     "target": "other", "sourceSide": "player", "targetSide": "player"}],
        )
        code, report = self.run_compare(native, simulated)
        self.assertEqual(2, code)
        self.assertEqual(2, report["channels"]["identity"]["unverified"])
        self.assertEqual(0, report["channels"]["identity"]["missing"])
        self.assertEqual(0, report["channels"]["identity"]["extra"])

    def test_divergent_random_donor_transform_marks_cards_unverified(self):
        # Quicksilver 型变形：目标自身（事件无 randomTarget），供体来自
        # TTargetCardRandom。两侧同帧变形但供体模板不同（无原生种子），
        # 之后该实例的冷却/属性差异应归为 randomCascade 而非确定性错误。
        native = native_ledger(
            {"player:a": native_item("player", "a", 0, "A"),
             "player:child": native_item("player", "child", 0, "X", born=1.0, generation=1,
                                         parent="player:a")},
            events=[{"codeName": "CombatSimEventCardTransformed", "frame": 20, "time": 1.0,
                     "originalInstanceId": "a",
                     "transformedCards": [{"instanceId": "child", "templateId": "X",
                                           "side": "player"}]}],
            cards=[{"frame": 21, "time": 1.05, "instanceId": "child", "side": "player",
                    "attribute": "Cooldown", "previous": 4950, "current": 4900}],
        )
        sim_a = sim_item("player", "a", 0, "A")
        sim_a["states"].append({"time": 1.0, "templateId": "Y", "slot": 0})
        simulated = sim_ledger(
            {"player:a": sim_a},
            events=[{"kind": "transform", "frame": 20, "time": 1.0, "source": "a",
                     "target": "a", "sourceSide": "player", "targetSide": "player"}],
            cards=[{"frame": 21, "time": 1.05, "instanceId": "a", "side": "player",
                    "attribute": "Cooldown", "previous": 3950, "current": 3900}],
        )
        code, report = self.run_compare(native, simulated)
        # 两侧实例经谱系归并到同一键，成对计为一条 unverified。
        self.assertEqual(1, report["channels"]["cards"]["unverified"])
        self.assertEqual(0, report["channels"]["cards"]["valueMismatch"])
        self.assertEqual(0, report["channels"]["cards"]["missing"])
        self.assertEqual(0, report["channels"]["cards"]["extra"])

    def test_matching_donor_transform_stays_deterministic(self):
        # 供体模板一致（打印机/镜子型确定性供体）时不标记，对齐行仍算 matched。
        native = native_ledger(
            {"player:a": native_item("player", "a", 0, "A"),
             "player:child": native_item("player", "child", 0, "X", born=1.0, generation=1,
                                         parent="player:a")},
            events=[{"codeName": "CombatSimEventCardTransformed", "frame": 20, "time": 1.0,
                     "originalInstanceId": "a",
                     "transformedCards": [{"instanceId": "child", "templateId": "X",
                                           "side": "player"}]}],
            cards=[{"frame": 21, "time": 1.05, "instanceId": "child", "side": "player",
                    "attribute": "Cooldown", "previous": 2950, "current": 2900}],
        )
        sim_a = sim_item("player", "a", 0, "A")
        sim_a["states"].append({"time": 1.0, "templateId": "X", "slot": 0})
        simulated = sim_ledger(
            {"player:a": sim_a},
            events=[{"kind": "transform", "frame": 20, "time": 1.0, "source": "a",
                     "target": "a", "sourceSide": "player", "targetSide": "player"}],
            cards=[{"frame": 21, "time": 1.05, "instanceId": "a", "side": "player",
                    "attribute": "Cooldown", "previous": 2950, "current": 2900}],
        )
        code, report = self.run_compare(native, simulated)
        self.assertEqual(1, report["channels"]["cards"]["matched"])


if __name__ == "__main__":
    unittest.main()


class ReplayTransformLineageTests(unittest.TestCase):
    def test_fresh_instances_follow_lineage_without_inventing_ancestor_states(self):
        sim = sim_ledger({
            'player:old': sim_item('player', 'old', 4, 'base', removed=8),
            'player:new': sim_item('player', 'new', 4, 'virus', born=8, removed=14),
            'player:next': sim_item('player', 'next', 4, 'virus', born=14),
        })
        sim['identity']['lineage'] = [
            {'kind': 'transform', 'originalInstanceId': 'old', 'children': [{'instanceId': 'new'}]},
            {'kind': 'transform', 'originalInstanceId': 'new', 'children': [{'instanceId': 'next'}]},
        ]
        index = compare.build_sim_identity(sim)
        self.assertEqual('player|4|g0|base', index.ref('old', 'player', 8))
        self.assertEqual('player|4|g1|base', index.ref('new', 'player', 14))
        self.assertEqual('player|4|g2|base', index.ref('next', 'player', 14))
        sim['combat']['events'] = [dict(kind='transform', source='new', sourceSide='player',
                                      target='next', targetSide='player', frame=280, time=14)]
        events, _ = compare.sim_effect_records(sim, index)
        self.assertEqual('player|4|g1|base', events[0]['source'])
        self.assertEqual('player|4|g2|base', events[0]['target'])
        # Identity normalization must still expose an actual numeric error.
        native = native_ledger({
            'player:old': native_item('player', 'old', 4, 'base', removed=8),
            'player:native-new': native_item('player', 'native-new', 4, 'virus', born=8,
                                             generation=1, parent='player:old'),
        }, cards=[dict(instanceId='native-new', side='player', frame=160, time=8,
                       attribute='PoisonApplyAmount', previous=0, current=16)])
        sim['identity']['instances'].pop('player:next')
        sim['identity']['instances']['player:new']['removed'] = None
        sim['identity']['lineage'] = sim['identity']['lineage'][:1]
        sim['combat']['events'] = []
        sim['combat']['cardUpdates'] = [dict(instanceId='new', frame=160, time=8,
                                             attribute='PoisonApplyAmount', previous=0, current=8)]
        report = compare.compare_documents(native, sim, 0, observed_decisions=True)
        self.assertEqual(1, report['counts']['valueMismatch'])
        self.assertEqual(0, report['counts']['missing'])
