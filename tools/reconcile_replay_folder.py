"""Reconcile a named replay folder, preserving evidence before optional moves.

Run without --move to inspect first. Only the strict comparator's MATCH with
no simulator warnings is accepted. UNKNOWN and missing input stay failures,
with separate reasons; partial/one-sided matches never enter 核对正确.
"""
from __future__ import annotations

import argparse
from collections import Counter
import csv
from datetime import datetime, timezone
import gzip
import json
from pathlib import Path
import sqlite3
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from tools import native_replay_compare as compare
from tools import native_replay_ledger as ledger
from tools.replay_random_decisions import build_decisions, audit_complete


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + '.tmp')
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')
    temporary.replace(path)


def write_gzip(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    with gzip.open(path, 'wt', encoding='utf-8') as handle:
        json.dump(value, handle, ensure_ascii=False, separators=(',', ':'))


def move_replay(source, root, category, expected_sha):
    """Only move an unchanged replay inside the explicitly selected root."""
    root, source = root.resolve(), source.resolve()
    if category not in ('核对正确', '核对失败', '无法使用'):
        raise ValueError('Invalid destination category')
    allowed = {root, root / '核对正确', root / '核对失败', root / '无法使用'}
    if source.parent not in allowed or not source.name.endswith('.payload.mpack.gz'):
        raise ValueError('Replay source is outside the selected folder')
    if ledger.sha256_file(source) != expected_sha:
        raise ValueError('Replay changed after inventory: ' + source.name)
    target_dir = root / category
    target_dir.mkdir(exist_ok=True)
    if target_dir.resolve() != target_dir:
        raise ValueError('Destination resolves outside the selected folder')
    target = target_dir / source.name
    if source == target:
        return target
    if target.exists():
        raise FileExistsError('Will not overwrite: ' + str(target))
    source.rename(target)
    if ledger.sha256_file(target) != expected_sha:
        raise ValueError('Post-move hash mismatch: ' + str(target))
    return target


def accepts(report, warnings):
    return report.get('verdict') == 'MATCH' and not warnings


def decisions_complete(audit, excluded):
    """Every supplied RNG input must run on its recorded frame and be valid."""
    return audit_complete(audit, excluded)


def missing_fixture_data(excluded, warnings):
    missing = [row for row in excluded if row.get('reason') in ('SkillTemplateMissing', 'SkillTemplateUnknown')]
    if missing:
        return 'MISSING_TEMPLATE', '缺少技能原生模板：' + '；'.join(
            '%s:%s (%s)' % (row.get('side'), row.get('instanceId'), row.get('templateId')) for row in missing)
    tiers = [warning for warning in warnings if '品质定义不完整' in warning]
    if tiers:
        return 'MISSING_TIER', '所需品质的原生规则数据不完整：' + '；'.join(tiers)
    return None


def random_differences(native, simulated):
    index = {}
    for event in simulated.get('combat', {}).get('events', []):
        key = (event.get('frame'), event.get('source'), compare.SIM_MAP.get(event.get('kind'), event.get('kind')))
        index.setdefault(key, set()).add(event.get('target'))
    differences = []
    for event in native.get('combat', {}).get('events', []):
        if not event.get('randomTarget'):
            continue
        kind = compare.ACTION_MAP.get(event.get('action'))
        key = (event.get('frame'), event.get('source'), kind)
        expected = set((event.get('target') or {}).get('ids') or [])
        actual = index.get(key, set()) - {None}
        if expected and actual and expected != actual:
            differences.append({'frame': event['frame'], 'time': event.get('time'), 'kind': kind,
                                'source': event.get('source'), 'nativeTargets': sorted(expected),
                                'simulatedTargets': sorted(actual)})
    return differences


def process(entry, args, out, targets, template_ids, inputs):
    source = Path(entry['location'])
    battle_id = entry['battleId']
    result = dict(entry, category='核对失败', status='ERROR', reasons=[], inputs=inputs)
    native_path = out / 'native' / (battle_id + '.json.gz')
    try:
        if ledger.sha256_file(source) != entry['sha256']:
            raise ValueError('Input changed since inventory')
        builder = ledger.LedgerBuilder(source, args.contract, out / 'opening.snapshot.db', True, targets)
        native = builder.build()
        write_gzip(native_path, native)
        result.update(frames=native['replay']['frameCount'], duration=native['replay']['duration'],
                      dataVersion=native['replay'].get('gameDataVersion'),
                      openingUnknowns=native.get('unknowns', []), nativeLedger=str(native_path))
        if not builder.snapshot_info:
            result.update(category='无法使用', status='MISSING_OPENING', reasons=['缺少该场 OpeningMessage 开场快照；回放载荷不能恢复完整玩家卡牌和技能。'])
            return result
        if not native.get('complete'):
            result.update(category='无法使用', status='INCOMPLETE_REPLAY', reasons=['原生回放含未支持或不完整的消息，不能验证整场。'])
            return result
        try:
            fixture = ledger.build_engine_fixture(native, args.templates, args.catalog,
                inputs['templates'], inputs['catalog'], template_ids)
        except SystemExit as error:
            result.update(category='无法使用', status='MISSING_TEMPLATE', reasons=[str(error)])
            return result
        fixture_path = out / 'fixtures' / (battle_id + '.json')
        if args.controlled_random:
            fixture['replayRandom'] = build_decisions(native)
        write_json(fixture_path, fixture)
        result['excludedItems'] = fixture.get('excludedItems', [])
        unavailable = missing_fixture_data(result['excludedItems'], [])
        if unavailable:
            result.update(category='无法使用', status=unavailable[0], reasons=[unavailable[1]])
            return result
        sim_path = out / 'simulated' / (battle_id + '.sim.json')
        sim_path.parent.mkdir(parents=True, exist_ok=True)
        command = [str(args.engine), '--ledger', str(fixture_path), str(args.templates), str(args.catalog), str(sim_path)]
        proc = subprocess.run(command, cwd=ROOT, capture_output=True, encoding='utf-8', errors='replace', timeout=120)
        result['simulatorExitCode'] = proc.returncode
        if proc.returncode != 0:
            result.update(status='SIMULATION_ERROR', reasons=[(proc.stderr + proc.stdout)[-2000:]])
            return result
        simulated = json.loads(sim_path.read_text(encoding='utf-8'))
        write_gzip(sim_path.with_suffix('.json.gz'), simulated)
        sim_path.unlink()  # generated intermediate only; original replay is untouched
        report = compare.compare_documents(native, simulated, 0, observed_decisions=args.controlled_random)
        write_gzip(out / 'diffs' / (battle_id + '.json.gz'), report)
        warnings = simulated.get('warnings') or []
        random_choices = random_differences(native, simulated)
        result.update(status=report['verdict'], counts=report['counts'], channels=report['channels'],
                      warnings=warnings, simulatorUnknowns=simulated.get('unknowns'),
                      firstDifferences=report['firstDifferences'], topDiffGroups=report['topDiffGroups'],
                      randomChoiceDifferences=random_choices[:20], randomChoiceDifferenceCount=len(random_choices))
        if args.controlled_random:
            result['replayRandom'] = simulated.get('replayRandom')
            result['excludedDecisions'] = fixture['replayRandom']['excluded']
        unavailable = missing_fixture_data(result['excludedItems'], warnings)
        if unavailable:
            result.update(category='无法使用', comparisonVerdict=report['verdict'], status=unavailable[0], reasons=[unavailable[1]])
            return result
        audit = result.get('replayRandom') or {}
        decisions_verified = not args.controlled_random or (
            audit_complete(audit, result.get('excludedDecisions'), fixture['replayRandom']) and
            audit.get('replaySha256') == native['replay']['sha256'])
        if accepts(report, warnings) and decisions_verified:
            result['category'] = '核对正确'
        else:
            if args.controlled_random and not decisions_verified:
                result['reasons'].append('部分随机决策缺失、归属不明确、在引擎中不合法或没有在原帧执行，保留为未通过；详见 replayRandom/excludedDecisions。')
            if random_choices:
                first = random_choices[0]
                result['reasons'].append('随机目标与回放不同，首次在 %.2fs；回放未提供可重建的随机种子，未通过修改输出强行对齐。' % first['time'])
            if warnings:
                result['reasons'].append('模拟器存在规则未完整支持等警告，详情见 warnings。')
            counts = report['counts']
            if any(counts.get(key) for key in ('missing', 'extra', 'valueMismatch', 'timeMismatch')):
                result['reasons'].append('修复并复测后仍存在逐帧事件/数值差异，详见 firstDifferences、topDiffGroups 和完整差异账本。')
            if counts.get('unknown') or counts.get('unverified') or counts.get('postEnd') or result['excludedItems']:
                result['reasons'].append('存在缺失开局属性、不可验证分支或排除项，无法证明整场一致。')
    except (Exception, SystemExit) as error:
        result.update(status='ERROR', reasons=[type(error).__name__ + ': ' + str(error)])
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder', type=Path)
    parser.add_argument('--templates', type=Path, required=True)
    parser.add_argument('--snapshot-db', type=Path, required=True)
    parser.add_argument('--catalog', type=Path, default=ROOT / 'src/data/cards.json')
    parser.add_argument('--engine', type=Path, default=ROOT / 'tools/actionaxis_plugin/build/EngineTests.exe')
    parser.add_argument('--contract', type=Path, default=ROOT / 'tools/native_replay_contract_v1.json')
    parser.add_argument('--move', action='store_true', help='Move replays only after every result is persisted')
    parser.add_argument('--controlled-random', action='store_true')
    parser.add_argument('--run-name', default='', help='Separate evidence folder below 核对记录')
    args = parser.parse_args(argv)
    for name in ('folder', 'templates', 'snapshot_db', 'catalog', 'engine', 'contract'):
        setattr(args, name, getattr(args, name).resolve())
    root = args.folder
    record_root = root / '核对记录'
    if args.run_name and any(c not in 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-' for c in args.run_name):
        raise ValueError('Invalid run name')
    out = record_root / args.run_name if args.run_name else record_root
    if out.exists() and out.resolve() != out:
        raise ValueError('Audit output must remain inside the selected folder')
    out.mkdir(parents=True, exist_ok=True)
    manifest_path = record_root / 'manifest.json'
    if manifest_path.exists():
        manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
        if manifest['folder'] != str(root):
            raise ValueError('Existing inventory belongs to another folder')
    else:
        files = sorted(root.glob('*.payload.mpack.gz'), key=lambda p: p.name)
        manifest = {'folder': str(root), 'ordering': 'filename ascending',
                    'createdAt': datetime.now(timezone.utc).isoformat(), 'entries': [
                        {'order': i, 'battleId': p.name.split('.')[0], 'name': p.name,
                         'originalPath': str(p), 'location': str(p), 'sha256': ledger.sha256_file(p)}
                        for i, p in enumerate(files, 1)]}
        write_json(manifest_path, manifest)
    frozen_db = out / 'opening.snapshot.db'
    if not frozen_db.exists():
        source_db = sqlite3.connect('file:' + args.snapshot_db.as_posix() + '?mode=ro', uri=True)
        target_db = sqlite3.connect(frozen_db)
        try:
            source_db.backup(target_db)
        finally:
            source_db.close()
            target_db.close()
    inputs = {name: ledger.sha256_file(getattr(args, name)) for name in ('engine', 'templates', 'catalog', 'contract')}
    inputs['snapshotDb'] = ledger.sha256_file(frozen_db)
    write_json(out / 'inputs.json', {'paths': {name: str(getattr(args, name)) for name in ('engine', 'templates', 'catalog', 'contract')}, 'sha256': inputs,
        'verificationPolicy': {'controlledRandom': args.controlled_random,
                               'moveRequested': args.move,
                               'strictMatchRequired': True}})
    template_ids = {json.loads(line)['id'] for line in args.templates.read_text(encoding='utf-8').splitlines() if line.strip()}
    targets = ledger.load_ability_targets(args.templates)
    results = []
    for entry in manifest['entries']:
        result = process(entry, args, out, targets, template_ids, inputs)
        results.append(result)
        write_json(out / 'results' / (entry['battleId'] + '.json'), result)
        print('%d/%d %s %s' % (entry['order'], len(manifest['entries']), entry['battleId'], result['status']), flush=True)
    for name in ('engine', 'templates', 'catalog', 'contract'):
        if inputs[name] != ledger.sha256_file(getattr(args, name)):
            raise ValueError('Verification input changed during run: ' + name)
    summary = {'total': len(results), 'fullSimulations': sum('counts' in result for result in results),
               'verificationMode': 'observed-decisions' if args.controlled_random else 'seeded',
               'categories': dict(Counter(r['category'] for r in results)),
               'statuses': dict(Counter(r['status'] for r in results)), 'moved': False, 'inputs': inputs}
    write_json(out / 'summary.json', summary)
    if args.move:
        for entry, result in zip(manifest['entries'], results):
            target = move_replay(Path(entry['location']), root, result['category'], entry['sha256'])
            entry['location'] = result['location'] = str(target)
            write_json(out / 'results' / (entry['battleId'] + '.json'), result)
            write_json(manifest_path, manifest)
        summary['moved'] = True
        write_json(out / 'summary.json', summary)
    with (out / 'results.csv').open('w', encoding='utf-8-sig', newline='') as handle:
        writer = csv.writer(handle)
        writer.writerow(['顺序', '对局ID', '分类', '核对状态', '原因', '文件位置', 'SHA256'])
        for result in results:
            writer.writerow([result['order'], result['battleId'], result['category'], result['status'],
                             '；'.join(result['reasons']), result['location'], result['sha256']])
    lines = ['# CombatReplays_4 核对记录', '',
             '按文件名升序逐场处理。完整输入且严格逐帧核对通过才进入“核对正确”；缺少开场数据或原生模板进入“无法使用”；仍有差异进入“核对失败”。', '',
             '总计 %d 场；完成双边推演对比 %d 场；核对正确 %d 场；核对失败 %d 场；无法使用 %d 场。' % (summary['total'], summary['fullSimulations'], summary['categories'].get('核对正确', 0), summary['categories'].get('核对失败', 0), summary['categories'].get('无法使用', 0)), '',
             '## 核对口径', '',
             '- 固定本次引擎、模板、卡库、契约和开场数据库；具体路径与哈希见 inputs.json。',
             '- 胜负相同不代表逐帧一致；UNKNOWN 也不归入严格通过。',
             '- 本工具只记录本次实测结果，不自动宣称任何代码修复已经完成。', '',
             '未扩大帧容差，未以原生战斗结果覆盖模拟器结果，未把单侧或随机不可验证项算成成功。完整 JSON 结果在 results/，原生与推演账本在 native/、simulated/，逐事件差异在 diffs/；文件移动前后均核验 SHA256。', '',
             '| 顺序 | 对局 | 状态 | 原因 |', '| --- | --- | --- | --- |']
    for result in results:
        reasons = '；'.join(result['reasons']).replace('|', '/')
        lines.append('| %d | %s | %s | %s |' % (result['order'], result['battleId'], result['status'], reasons))
    (out / '核对汇总.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')
    print(json.dumps(summary, ensure_ascii=False), flush=True)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
