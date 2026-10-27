"""Import the plugin's self-contained validation folder into the strict replay harness.

python tools/import_validation_replay.py <capture.validation.json.gz> --out <folder>
Optional --engine <EngineTests.exe> also runs simulation and strict comparison.
No source files are moved or changed. Missing/ambiguous inputs never become MATCH.
"""
from __future__ import annotations

import argparse
import base64
import gzip
import json
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from tools import native_replay_ledger as ledger
from tools import native_replay_compare as compare
from tools.replay_random_decisions import build_decisions
from captures.diagnostics.read_bpp_replay import unpack


def read_gzip(path):
    with gzip.open(path, 'rt', encoding='utf-8') as handle:
        return json.load(handle)


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')


def identifier(value):
    return value.get('Value') if isinstance(value, dict) else value


def snapshot_card(card):
    return {'instance_id': identifier(card.get('InstanceId')), 'template_id': identifier(card.get('TemplateId')),
            'type': card.get('Type'), 'size': card.get('Size'), 'section': card.get('Section'),
            'socket': card.get('LeftSocketId'), 'tier': card.get('Tier'), 'enchant': card.get('Enchantment'),
            'tags': card.get('Tags') or [], 'attributes': card.get('Attributes') or {}}


def extract_templates(bundle, source):
    """Keep full current-version catalog; opening templates can recover a failed map entry."""
    name = bundle.get('CatalogFile')
    if not name or Path(name).name != name or '/' in name or '\\' in name:
        raise ValueError('Invalid or missing CatalogFile')
    catalog_path = source.parent / name
    catalog = read_gzip(catalog_path)
    if catalog.get('Schema') != 'actionaxis.catalog.v1':
        raise ValueError('Unknown catalog schema')
    templates = {key: value for key, value in (catalog.get('Templates') or {}).items() if isinstance(value, dict)}
    for phase in ('OpeningBeforeDispatch', 'OpeningAfterDispatch'):
        for side in ('Player', 'Opponent'):
            snapshot = (bundle.get(phase) or {}).get(side) or {}
            for card in (snapshot.get('Cards') or []) + (snapshot.get('Skills') or []):
                tid = identifier(card.get('TemplateId'))
                if tid and isinstance(card.get('Template'), dict):
                    templates.setdefault(tid, card['Template'])
    return templates, catalog.get('Errors') or [], catalog_path


class ValidationLedgerBuilder(ledger.LedgerBuilder):
    def __init__(self, source, contract, targets, bundle):
        super().__init__(source, contract, None, True, targets)
        self.bundle = bundle

    def load(self):
        bundle = self.bundle
        if bundle.get('Schema') != 'actionaxis.validation.v1':
            raise ValueError('Unknown validation schema')
        self.replay_sha256 = ledger.sha256_file(self.replay_path)
        expected = self.contract.get('assemblySha256')
        actual = (bundle.get('Environment') or {}).get('SharedAssemblySha256')
        if not actual or actual != expected:
            raise ValueError('Game assembly differs from decoding contract; regenerate contract before importing')
        self.replay_doc = {'Version': 1, 'BattleId': bundle['CaptureId']}
        for key in ('SpawnMessage', 'CombatMessage', 'DespawnMessage'):
            message = bundle.get(key) or {}
            if not message.get('MessagePackBase64'):
                raise ValueError('Missing original wire message: ' + key)
            self.replay_doc[key + 'Bytes'] = unpack(
                base64.b64decode(message['MessagePackBase64'], validate=True))
            # Named JSON failure alone does not invalidate the original wire data.
        if bundle.get('Stage') != 'complete' or bundle.get('CaptureGap'):
            self.complete = False
            self.unknown('capture.gap', bundle.get('CaptureGap') or 'Missing complete stage')
        self.load_snapshot()

    def load_snapshot(self):
        # Never use a late playback board to repair a missing opening.
        snapshot = self.bundle.get('OpeningBeforeDispatch') or {}
        player = snapshot.get('Player') or {}
        if not isinstance(player.get('Cards'), list) or not isinstance(player.get('Skills'), list):
            raise ValueError('Missing pre-dispatch player opening cards/skills')
        relevant_errors = [error for error in snapshot.get('Errors') or []
                           if not error.startswith('Opponent')]
        if relevant_errors:
            self.complete = False
            self.unknown('capture.opening', '; '.join(relevant_errors))
        cards = [snapshot_card(card) for card in player['Cards'] if card.get('Section') in ('Hand', 'Stash')]
        skills = [snapshot_card(card) for card in player['Skills']]
        for card in cards + skills:
            if not card.get('instance_id') or not card.get('template_id') or not card.get('tier'):
                self.complete = False
                self.unknown('capture.identity', 'Missing opening identity/tier: ' + repr(card.get('instance_id')))
        # The opponent is newly introduced in SpawnMessage. The existing native decoder
        # reconstructs its cards/skills/hero from spawn events + authoritative updates.
        self.snapshot = {key: {'status': 'Captured', 'source': 'OpeningMessage', 'items': entries}
                         for key, entries in (('player_hand', cards), ('player_skills', skills),
                                              ('opponent_hand', []), ('opponent_skills', []))}
        self.snapshot_info = {'kind': 'actionaxis_pre_dispatch', 'captureId': self.bundle['CaptureId'],
                              'opponentSource': 'original SpawnMessage, not the previous live opponent'}
        self.heroes['player'] = player.get('Hero')
        self.hero_sources['player'] = 'OpeningBeforeDispatch.Player.Hero'
        for name, value in (player.get('Attributes') or {}).items():
            if isinstance(value, (int, float)):
                self.open_attrs['player'][name] = int(value)
                self.attr_sources['player'][name] = 'OpeningBeforeDispatch.Player.Attributes'

    def note_missing_opening(self):
        super().note_missing_opening()
        # An explicitly captured empty skill list is complete data.
        self.unknowns[:] = [row for row in self.unknowns if row['scope'] != 'opening.playerSkills']


def import_capture(source, out, contract=ledger.DEFAULT_CONTRACT, card_catalog=ledger.DEFAULT_CATALOG, engine=None):
    source, out = Path(source).resolve(), Path(out).resolve()
    out.mkdir(parents=True, exist_ok=True)
    bundle = read_gzip(source)
    templates, errors, native_catalog = extract_templates(bundle, source)
    template_path = out / 'templates.jsonl'
    with template_path.open('w', encoding='utf-8') as handle:
        for tid, template in sorted(templates.items()):
            handle.write(json.dumps({'id': tid, 'template': template}, ensure_ascii=False) + '\n')
    builder = ValidationLedgerBuilder(source, contract, ledger.load_ability_targets(template_path), bundle)
    native = builder.build()
    native['provenance']['replay'] = 'ActionAxis original native MessagePack + frozen prefix snapshot'
    write_json(out / 'native.json', native)
    fixture = ledger.build_engine_fixture(native, template_path, card_catalog, template_ids=set(templates))
    fixture['replayRandom'] = build_decisions(native)
    write_json(out / 'fixture.json', fixture)
    issues = list(errors)
    if not native['complete']:
        issues.append('Native ledger is incomplete')
    for side in ('player', 'enemy'):
        opening = native['opening'][side]
        if not opening['items']:
            issues.append('No opening items: ' + side)
        issues += [side + ': missing ' + name for name in ledger.PLAYER_ATTRS_REQUIRED
                   if name not in opening['attributes']]
        for item in opening['items'] + opening['skills']:
            if not item.get('templateId') or item['templateId'] not in templates or not item.get('tier'):
                issues.append('Missing identity/template/tier: ' + str(item.get('instanceId')))
            elif item['tier'] not in (templates[item['templateId']].get('Tiers') or {}):
                issues.append('Missing tier definition: ' + str(item.get('instanceId')) + ':' + item['tier'])
    if any(row.get('reason') != 'Stash' for row in fixture.get('excludedItems', [])):
        issues.append('Fixture contains excluded inputs')
    if fixture['replayRandom']['excluded']:
        issues.append('Some random choices cannot be attributed unambiguously')
    report = {'status': 'INPUTS_READY' if not issues else 'INPUTS_INCOMPLETE', 'issues': issues,
              'captureSha256': ledger.sha256_file(source), 'catalogSha256': ledger.sha256_file(native_catalog),
              'frames': native['replay']['frameCount'], 'templates': len(templates),
              'randomDecisions': {k: len(fixture['replayRandom'][k]) for k in ('targets', 'criticals', 'excluded')}}
    if engine:
        subprocess.run([str(Path(engine).resolve()), '--ledger', str(out / 'fixture.json'), str(template_path),
                        str(Path(card_catalog).resolve()), str(out / 'simulated.json')], cwd=ROOT, check=True, timeout=120)
        simulated = json.loads((out / 'simulated.json').read_text(encoding='utf-8'))
        comparison = compare.compare_documents(native, simulated, 0, observed_decisions=True)
        write_json(out / 'comparison.json', comparison)
        audit = simulated.get('replayRandom') or {}
        applied = bool(audit) and all(not audit.get(key) for key in ('targetFallbacks', 'criticalFallbacks', 'invalidChoices'))
        applied = applied and audit.get('targetRecords') == audit.get('targetApplied') and audit.get('criticalRecords') == audit.get('criticalApplied')
        passed = not issues and not simulated.get('warnings') and applied and comparison['verdict'] == 'MATCH'
        report.update(status='MATCH' if passed else 'NOT_VERIFIED', comparison=comparison['verdict'], replayRandom=audit,
                      simulatorWarnings=simulated.get('warnings'))
    write_json(out / 'readiness.json', report)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('capture', type=Path)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--contract', type=Path, default=ledger.DEFAULT_CONTRACT)
    parser.add_argument('--catalog', type=Path, default=ledger.DEFAULT_CATALOG)
    parser.add_argument('--engine', type=Path)
    args = parser.parse_args()
    try:
        report = import_capture(args.capture, args.out, args.contract, args.catalog, args.engine)
    except (ValueError, OSError, KeyError, SystemExit, subprocess.SubprocessError) as error:
        args.out.mkdir(parents=True, exist_ok=True)
        report = {'status': 'INPUTS_INCOMPLETE', 'issues': [str(error)]}
        write_json(args.out / 'readiness.json', report)
    print(json.dumps(report, ensure_ascii=False, indent=1))
    return 0 if report['status'] in ('INPUTS_READY', 'MATCH') else 2


if __name__ == '__main__':
    raise SystemExit(main())
