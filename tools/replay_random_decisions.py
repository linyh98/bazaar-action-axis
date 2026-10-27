"""Extract observed random decisions without copying effect values or outcomes."""
from collections import defaultdict
from functools import lru_cache
from itertools import product

ACTION_KIND = {'CardCharge': 'charge', 'CardHaste': 'haste', 'CardSlow': 'slow',
               'CardFreeze': 'freeze', 'FlyingStart': 'fly', 'FlyingStop': 'flyStop',
               'CardDestroy': 'destroy', 'CardReload': 'reload', 'CardRepair': 'repair',
               'CardModifyAttribute': 'modifyAttribute', 'CardForceUse': 'forceUse',
               'CardDisable': 'disable', 'CardTransform': 'transform', 'CardUpgrade': 'upgrade',
               'CardEnchant': 'enchant', 'CardEnchantRandom': 'enchantRandom',
               'CardAddTagsRandom': 'addTagsRandom',
               'PlayerDamage': 'damage', 'PlayerHeal': 'heal', 'PlayerShieldApply': 'shield'}
HEALTH_KIND = {'Damage': 'damage', 'Heal': 'heal', 'Healing': 'heal', 'Shield': 'shield'}
# Burn/Poison applications leave no HP or Shield adjustment, so their critical
# decision is not covered by the isCrit flags below. The target's stack export
# still records previous -> current: the applied stacks are exactly readable
# when one apply lands on that side and the source's live apply amount is known.
STATUS_KIND = {'PlayerBurnApply': ('burn', 'Burn', 'BurnApplyAmount', True),
               'PlayerPoisonApply': ('poison', 'Poison', 'PoisonApplyAmount', False),
               'PlayerRegenApply': ('regen', 'HealthRegen', 'RegenApplyAmount', False)}

PRIORITY_RANK = {'Immediate': 0, 'Highest': 1, 'High': 2, 'Low': 4, 'Lowest': 5}
_TEMPLATE_PRIORITIES = None


def _template_priorities():
    """templateId -> {abilityId: priority rank}, cached per process."""
    global _TEMPLATE_PRIORITIES
    if _TEMPLATE_PRIORITIES is not None:
        return _TEMPLATE_PRIORITIES
    import json
    from pathlib import Path
    path = (Path(__file__).resolve().parents[1] / 'tools' / 'actionaxis_plugin'
            / 'build' / 'native_mapping_templates.jsonl')
    table = {}
    try:
        with open(path, encoding='utf-8') as handle:
            for line in handle:
                line = line.strip()
                if not line:
                    continue
                row = json.loads(line)
                abilities = (row.get('template') or {}).get('Abilities') or {}
                table[row.get('id')] = {
                    ability_id: PRIORITY_RANK.get((ability or {}).get('Priority'), 3)
                    for ability_id, ability in abilities.items()}
    except OSError:
        table = {}
    _TEMPLATE_PRIORITIES = table
    return table


def _instance_templates(native):
    mapping = {}
    for side in ('player', 'enemy'):
        bucket = (native.get('opening') or {}).get(side) or {}
        for row in (bucket.get('items') or []) + (bucket.get('skills') or []):
            if row.get('instanceId') and row.get('templateId'):
                mapping[row['instanceId']] = row['templateId']
    for instance in ((native.get('identity') or {}).get('instances') or {}).values():
        if instance.get('instanceId') and instance.get('templateId'):
            mapping.setdefault(instance['instanceId'], instance['templateId'])
    return mapping


def _activation_order(pairs):
    """Recover activation order from one frame's (source, priority rank) stream.

    Native runs same-frame abilities sorted by (effect priority, activation
    order).  Adjacent sources inside one priority group therefore constrain the
    activation order; a topological sort with first-appearance tie-break
    recovers it.  A cycle or missing template falls back to first appearance.
    """
    first = {}
    for index, (source, _) in enumerate(pairs):
        first.setdefault(source, index)
    sources = list(first)
    if len(sources) <= 1:
        return sources
    groups = defaultdict(list)
    for source, rank in pairs:
        if source not in groups[rank]:
            groups[rank].append(source)
    edges = set()
    indegree = {source: 0 for source in sources}
    for group in groups.values():
        for left, right in zip(group, group[1:]):
            if left != right and (left, right) not in edges:
                edges.add((left, right))
                indegree[right] += 1
    remaining = set(sources)
    ordered = []
    while remaining:
        ready = sorted((source for source in remaining if indegree[source] == 0),
                       key=lambda source: first[source])
        if not ready:  # contradictory priorities: keep the raw first appearance
            return sources
        pick = ready[0]
        remaining.remove(pick)
        ordered.append(pick)
        for left, right in edges:
            if left == pick and right in remaining:
                indegree[right] -= 1
    return ordered


def ordered_damage_critical_flags(events, records):
    """Pair explicit bits only when contiguous hit boundaries are unique.

    One damage action has one Health adjustment and optionally one adjacent
    Shield adjustment, with the same bit on both. Native can serialize HP
    before Shield or Shield before HP. Hits absorbed entirely by Shield have
    one Shield adjustment each. Amounts never identify a boundary.
    """
    sequence = [event.get('sequence') for event in events]
    if (not all(isinstance(value, int) for value in sequence)
            or not all(left < right for left, right in zip(sequence, sequence[1:]))):
        return None
    if (len(records) == len(events)
            and all(attribute == 'Shield' and isinstance(bit, bool)
                    for bit, attribute in records)):
        return [bit for bit, _ in records]
    if (sum(attribute == 'Health' for _, attribute in records) != len(events)
            or any(attribute not in ('Health', 'Shield') for _, attribute in records)):
        return None

    @lru_cache(maxsize=None)
    def partitions(position, hits):
        if hits == len(events):
            return ((),) if position == len(records) else ()
        candidates = []
        for size in (1, 2):
            group = records[position:position + size]
            if len(group) != size:
                continue
            if size == 1:
                valid = group[0][1] == 'Health'
            else:
                valid = ({group[0][1], group[1][1]} == {'Health', 'Shield'}
                         and group[0][0] == group[1][0])
            if not valid:
                continue
            for suffix in partitions(position + size, hits + 1):
                candidates.append((group[0][0],) + suffix)
                if len(candidates) >= 2:
                    return tuple(candidates)  # multiple boundaries stay ambiguous
        return tuple(candidates)

    paths = partitions(0, 0)
    return list(paths[0]) if len(paths) == 1 else None


def ordered_shield_critical_flags(events, records):
    """Shield gains have one unsplit adjustment per observed execution.

    Complete increasing execution sequences and a one-to-one list of Shield
    adjustments let us pair the explicit bits without inspecting amounts.
    Capped/missing gains or any different attribute leave no such proof.
    """
    sequence = [event.get('sequence') for event in events]
    if (len(records) != len(events) or
            not all(isinstance(value, int) for value in sequence) or
            not all(left < right for left, right in zip(sequence, sequence[1:])) or
            any(attribute != 'Shield' or not isinstance(bit, bool) for bit, attribute in records)):
        return None
    return [bit for bit, _ in records]


def audit_complete(audit, excluded, expected=None):
    """Prove that all five input channels were consumed without fallback."""
    if not audit or excluded or audit.get('invalidChoices'):
        return False
    for channel, key in (('target', 'targets'), ('critical', 'criticals'), ('order', 'order'),
                         ('enchant', 'enchantments'), ('transform', 'transforms')):
        if channel + 'Records' not in audit or channel + 'Applied' not in audit:
            return False
        records = audit[channel + 'Records']
        if audit.get(channel + 'Fallbacks') or records != audit[channel + 'Applied']:
            return False
        if expected is not None and records != len(expected.get(key) or []):
            return False
    # Lineage is identity evidence only. A transform must independently
    # produce the observed template/location before later decisions may use
    # that child's native ID; an unbound child leaves the audit incomplete.
    if expected is not None and expected.get('identities'):
        records = audit.get('identityRecords')
        if (records != len(expected['identities']) or
                records != audit.get('identityApplied') or audit.get('identityFallbacks')):
            return False
    return True


def _status_cleanse_outcomes(frame_events, rows, event, side, attribute, previous,
                             current, ticked, nominals, live, updates):
    """Infer a bit only when known healing and every feasible ordering agree.

    Healing's actual HP adjustment can be capped. Its nominal amount comes
    only from the healing item's native attributes and an explicit crit bit.
    No inferred amount is returned to the engine, only the critical decision.
    """
    side_events = [row for row in frame_events
                   if side in ((row.get('target') or {}).get('sides') or [])]
    if any(row.get('action') in (f'Player{attribute}Remove', f'{attribute}Remove')
           for row in side_events):
        return set()
    heals = [row for row in side_events if row.get('action') == 'PlayerHeal']
    adjustments = [adjustment for row in rows
                   for adjustment in row.get('healthAdjustments') or []
                   if adjustment.get('damageType') in ('Heal', 'Healing')]
    if not heals and not adjustments:
        return None  # Caller preserves the existing no-healing calculation.
    flags = [row.get('isCrit') for row in adjustments]
    if (not heals or len(adjustments) != len(heals) or
            any(not isinstance(flag, bool) for flag in flags) or len(set(flags)) != 1):
        return set()
    amounts = []
    for heal in heals:
        source = heal.get('source')
        candidates = [(live.get(source) or {}).get('HealAmount')]
        candidates += [row['current'] for row in updates
                       if row.get('instanceId') == source and row.get('attribute') == 'HealAmount']
        if any(not isinstance(value, (int, float)) or value < 0 for value in candidates):
            return set()
        # Engine/native fixed critical healing is twice the live nominal.
        requested = {int(value * (2 if flags[0] else 1) + 0.5) for value in candidates}
        amounts.append({max(1, value * 0.05) if value > 0 else 0 for value in requested})
    # Exported execution sequence identifies the relative action order. Where
    # absent, consider every insertion of the sole status apply among heals.
    sequences = [row.get('sequence') for row in heals + [event]]
    if all(isinstance(value, int) for value in sequences) and len(set(sequences)) == len(sequences):
        heals = sorted(zip(heals, amounts), key=lambda pair: pair[0]['sequence'])
        amounts = [pair[1] for pair in heals]
        positions = [sum(pair[0]['sequence'] < event['sequence'] for pair in heals)]
    else:
        positions = range(len(heals) + 1)
    outcomes = set()
    for nominal in nominals:
        for critical in (False, True):
            for cleanse_amounts in product(*amounts):
                for position in positions:
                    stack = max(0, previous - ticked)
                    for index in range(len(cleanse_amounts) + 1):
                        if index == position:
                            stack += nominal * (2 if critical else 1)
                        if index < len(cleanse_amounts):
                            # .NET Math.Round and Python round both use ties-to-even.
                            stack = max(0, round(stack - cleanse_amounts[index]))
                    if stack == current:
                        outcomes.add(critical)
    return outcomes


def _joint_status_critical_mask(events, frame_events, rows, side, attribute,
                                amount_attribute, applied, live, updates):
    """Solve a small additive group only when all feasible sums share a mask.

    Nominal attribute candidates may remain ambiguous; none are exported.
    Repeated sources, unknown mutations and groups beyond the explicit work
    bound are deliberately excluded rather than guessed.
    """
    sources = [event.get('source') for event in events]
    if (not 2 <= len(events) <= 6 or not all(sources) or len(set(sources)) != len(sources) or
            not isinstance(applied, (int, float)) or applied <= 0 or int(applied) != applied):
        return None
    side_events = [event for event in frame_events
                   if side in ((event.get('target') or {}).get('sides') or [])]
    if any(event.get('action') in ('PlayerHeal', 'PlayerModifyAttribute',
                                   f'Player{attribute}Remove', f'{attribute}Remove')
           for event in side_events) or any(
               adjustment.get('damageType') in ('Heal', 'Healing')
               for row in rows for adjustment in row.get('healthAdjustments') or []):
        return None
    candidates_by_source = []
    work = 1
    for source in sources:
        candidates = [(live.get(source) or {}).get(amount_attribute)]
        candidates += [row['current'] for row in updates
                       if row.get('instanceId') == source and row.get('attribute') == amount_attribute]
        if any(not isinstance(value, (int, float)) or isinstance(value, bool) or
               value <= 0 or int(value) != value for value in candidates):
            return None
        candidates = set(candidates)
        work *= 2 * len(candidates)
        if len(candidates) > 4 or work > 4096:
            return None
        candidates_by_source.append(candidates)
    paths = {(0, 0)}
    for index, candidates in enumerate(candidates_by_source):
        paths = {(total + nominal * multiplier, mask | (bit << index))
                 for total, mask in paths for nominal in candidates
                 for bit, multiplier in ((0, 1), (1, 2))
                 if total + nominal * multiplier <= applied}
    masks = {mask for total, mask in paths if total == applied}
    if len(masks) != 1:
        return None
    mask = masks.pop()
    return [bool(mask & (1 << index)) for index in range(len(events))]


def status_apply_criticals(native, base):
    """Recover Burn/Poison critical decisions from native stack transitions.

    A DoT apply produces no HP or Shield adjustment, so ``isCrit`` never
    reaches the tape.  The target's stack attribute still records
    ``previous -> current``: with exactly one apply on that side and the
    source item's live apply amount, ``+amount`` means no critical and
    ``+2*amount`` means one (Burn additionally loses one stack to its tick).
    Anything else -- several sources, missing live amount, an unexpected
    delta -- stays ambiguous and is left to the seeded RNG; it is never
    inferred from HP damage or effect output. Known same-frame healing is
    replayed from its live nominal amount and explicit native critical bit;
    capped HP gain is never treated as the nominal heal.
    """
    live = {}
    for side in ('player', 'enemy'):
        for item in ((native.get('opening', {}).get(side, {}).get('items') or []) +
                     (native.get('opening', {}).get(side, {}).get('skills') or [])):
            if item.get('instanceId'):
                live[item['instanceId']] = dict(item.get('attributes') or {})
    updates = defaultdict(list)
    for row in native.get('combat', {}).get('cardUpdates') or []:
        if 'attribute' in row and row.get('instanceId'):
            updates[row['frame']].append(row)
    changes = defaultdict(list)
    for row in native.get('combat', {}).get('healthChanges') or []:
        changes[(row.get('frame'), row.get('side'))].append(row)
    applies = defaultdict(list)
    frame_events = defaultdict(list)
    for event in native.get('combat', {}).get('events') or []:
        frame_events[event.get('frame')].append(event)
        if event.get('action') not in STATUS_KIND:
            continue
        sides = (event.get('target') or {}).get('sides') or []
        if len(sides) == 1 and isinstance(event.get('frame'), int):
            applies[(event['frame'], sides[0], event['action'])].append(event)

    records, excluded = [], []
    update_frames = sorted(updates)
    position = 0
    for frame in sorted({key[0] for key in applies}):
        while position < len(update_frames) and update_frames[position] < frame:
            for row in updates[update_frames[position]]:
                live.setdefault(row['instanceId'], {})[row['attribute']] = row['current']
            position += 1
        for (apply_frame, side, action), events in sorted(applies.items()):
            if apply_frame != frame:
                continue
            kind, attribute, amount_attribute, decays = STATUS_KIND[action]
            rows = changes.get((frame, side)) or []
            states = [row for row in rows
                      if isinstance((row.get('attributes') or {}).get(attribute), dict)]
            if len(states) != 1:
                excluded.append({'frame': frame, 'reason': 'status state not unique',
                                 'targetSide': side, 'action': action})
                continue
            state = states[0]['attributes'][attribute]
            previous, current = state.get('previous'), state.get('current')
            if not isinstance(previous, (int, float)) or not isinstance(current, (int, float)):
                excluded.append({'frame': frame, 'reason': 'status state unreadable',
                                 'targetSide': side, 'action': action})
                continue
            ticked = decays and any(
                adjustment.get('damageType') == attribute
                for row in rows for adjustment in row.get('healthAdjustments') or [])
            # Burn loses max(1, floor(3% of its pre-tick pool)), only when
            # the periodic packet actually moved HP or Shield. The native
            # prior stack and signed adjustment presence establish this input;
            # never substitute a simulated stack or assume a fixed one layer.
            dealt = ticked and any(
                adjustment.get('damageType') == attribute and
                isinstance(adjustment.get('amount'), (int, float)) and adjustment['amount'] < 0
                for row in rows for adjustment in row.get('healthAdjustments') or [])
            decay = min(previous, max(1, int(previous * 0.03))) if dealt else 0
            applied = current - previous + decay
            if len(events) != 1:
                # Native Burn ticks can decay more than one stack. Until the
                # exact decay is independently known, a net delta cannot prove
                # a multi-source critical mask; do not reuse the legacy +1.
                mask = None if ticked else _joint_status_critical_mask(
                    events, frame_events[frame], rows, side, attribute, amount_attribute,
                    applied, live, updates.get(frame, []))
                if mask is None:
                    excluded.append({'frame': frame, 'reason': 'multiple status applies',
                                     'targetSide': side, 'action': action})
                else:
                    records.extend(dict(base(event, kind), targetSide=side, critical=critical)
                                   for event, critical in zip(events, mask))
                continue
            event = events[0]
            # A same-frame card update may be the item's own pre-apply buff
            # (Soldering Gun adds BurnApplyAmount before applying).  Neither
            # ordering is assumed: every value that could have been live at
            # apply time must agree on the critical outcome.
            source = event.get('source')
            candidates = [(live.get(source) or {}).get(amount_attribute)]
            candidates += [row['current'] for row in updates.get(frame, [])
                           if row.get('instanceId') == source and row.get('attribute') == amount_attribute]
            nominals = [nominal for nominal in candidates
                        if isinstance(nominal, (int, float)) and nominal > 0]
            if not nominals:
                excluded.append({'frame': frame, 'reason': 'status apply amount unknown',
                                 'targetSide': side, 'action': action})
                continue
            if kind == 'regen':
                # Healing cleanses Burn/Poison, never HealthRegen. A separate
                # hero-attribute action on this frame can still obscure the
                # additive transition, so do not infer a critical bit there.
                mutated = any(row.get('action') == 'PlayerModifyAttribute' and
                              side in ((row.get('target') or {}).get('sides') or [])
                              for row in frame_events[frame])
                outcomes = set() if mutated else None
            else:
                outcomes = _status_cleanse_outcomes(
                    frame_events[frame], rows, event, side, attribute, previous, current,
                    decay, nominals, live, updates.get(frame, []))
            if outcomes is None:
                outcomes = set()
                for nominal in nominals:
                    if applied == nominal:
                        outcomes.add(False)
                    elif applied == 2 * nominal:
                        outcomes.add(True)
            if len(outcomes) != 1:
                excluded.append({'frame': frame, 'reason': 'ambiguous status critical',
                                 'targetSide': side, 'action': action})
                continue
            critical = outcomes.pop()
            records.append(dict(base(event, kind), targetSide=side, critical=critical))
    return records, excluded


def build_decisions(native):
    skills = {}
    for side in ('player', 'enemy'):
        for skill in native.get('opening', {}).get(side, {}).get('skills', []):
            iid = skill.get('instanceId')
            if iid:
                skills[iid] = ('enemy:' if side == 'enemy' else '') + 'skill:' + iid

    def base(event, kind):
        return {'frame': event['frame'], 'source': skills.get(event.get('source'), event.get('source')),
                'ability': event.get('fields', {}).get('EffectId'), 'kind': kind}

    targets, criticals, excluded = [], [], []
    target_executions = {}
    orders = {}
    enchantments, transforms, identities = [], [], []
    enchant_actions, transform_actions = [], {}
    target_transform_actions = {}
    effects = defaultdict(list)
    adjustments = defaultdict(list)
    unknown_adjustments = set()
    priorities = _template_priorities()
    templates_by_instance = _instance_templates(native)
    for event in native.get('combat', {}).get('events', []):
        kind = ACTION_KIND.get(event.get('action'))
        if event.get('triggerType') == 'TTriggerOnCardFired' and event.get('source'):
            # 记录 (来源, 效果优先级)，供帧末按优先级分组还原激活顺序。
            ability = (event.get('fields') or {}).get('EffectId')
            template_id = templates_by_instance.get(event['source'])
            rank = priorities.get(template_id, {}).get(ability, 3) if template_id else 3
            orders.setdefault(event['frame'], []).append((event['source'], rank))
        if event.get('actionType') == 'TActionCardEnchantRandom':
            enchant_actions.append(event)
        if event.get('randomTransform'):
            context = (event.get('fields') or {}).get('ExecutionContextId')
            if context is not None:
                transform_actions[(event['frame'], context)] = event
        if event.get('action') == 'CardTransform' and event.get('randomTarget'):
            context = (event.get('fields') or {}).get('ExecutionContextId')
            if context is not None:
                target_transform_actions[(event['frame'], context)] = event
        if str(event.get('codeName') or '').endswith('CardEnchanted') and not event.get('reverted'):
            iid = event.get('instanceId')
            # The dedicated row has no source. Attribute it only to an
            # observed preceding random-enchant action on the same target.
            for action in reversed(enchant_actions):
                ids = (action.get('target') or {}).get('ids') or []
                if action['frame'] == event['frame'] and iid in ids:
                    enchantments.append({'frame': event['frame'], 'source': skills.get(action.get('source'), action.get('source')),
                                         'target': iid, 'name': event.get('enchantment')})
                    break
        if str(event.get('codeName') or '').endswith('CardTransformed'):
            context = (event.get('fields') or {}).get('ExecutionContextId')
            action = transform_actions.get((event['frame'], context))
            for index, card in enumerate(event.get('transformedCards') or []):
                if (event.get('originalInstanceId') and card.get('instanceId') and
                        card.get('templateId') and card.get('side') in ('player', 'enemy') and
                        isinstance(card.get('socket'), int)):
                    identities.append({'frame': event['frame'], 'original': event['originalInstanceId'],
                                       'instanceId': card['instanceId'], 'templateId': card['templateId'],
                                       'side': card['side'], 'slot': card['socket'], 'index': index})
                else:
                    excluded.append({'frame': event.get('frame'),
                                     'reason': 'incomplete transform identity'})
            if action is not None:
                templates = [card.get('templateId') for card in event.get('transformedCards') or []]
                if templates and all(templates):
                    transforms.append({'frame': event['frame'], 'source': event.get('originalInstanceId'),
                                       'kind': 'transform', 'targets': templates})
        if event.get('randomTarget'):
            # The dedicated transform event repeats the action's random-target
            # metadata but contains no second random draw. Suppress it only
            # when the same execution actually selected its original card.
            action = target_transform_actions.get((event['frame'],
                      (event.get('fields') or {}).get('ExecutionContextId')))
            if (str(event.get('codeName') or '').endswith('CardTransformed') and
                    action is not None and event.get('originalInstanceId') in
                    ((action.get('target') or {}).get('ids') or [])):
                continue
            ids = (event.get('target') or {}).get('ids') or []
            if kind and ids:
                context = (event.get('fields') or {}).get('ExecutionContextId')
                key = (event['frame'], event.get('source'), kind,
                       (event.get('fields') or {}).get('EffectId'), context)
                # One random action is serialized as one event per affected
                # card. Its execution identity is the boundary of ONE draw;
                # separate casts on the same frame must remain separate.
                choice = target_executions.get(key) if context is not None else None
                if choice is None:
                    choice = dict(base(event, kind), targets=[])
                    targets.append(choice)
                    if context is not None:
                        target_executions[key] = choice
                choice['targets'].extend(iid for iid in ids if iid not in choice['targets'])
            else:
                excluded.append({'frame': event.get('frame'), 'reason': 'unhandled random action', 'action': event.get('action')})
        if kind not in ('damage', 'heal', 'shield'):
            continue
        for side in (event.get('target') or {}).get('sides') or []:
            effects[(event['frame'], side, kind)].append(event)
    for change in native.get('combat', {}).get('healthChanges', []):
        grouped = defaultdict(list)
        for adjustment in change.get('healthAdjustments', []):
            kind = HEALTH_KIND.get(adjustment.get('damageType'))
            if kind and isinstance(adjustment.get('isCrit'), bool):
                grouped[kind].append((adjustment['isCrit'], adjustment.get('attributeChanged')))
            elif kind:
                unknown_adjustments.add((change['frame'], change['side'], kind))
        for kind, flags in grouped.items():
            adjustments[(change['frame'], change['side'], kind)].extend(flags)
    for key, events in effects.items():
        records = adjustments.get(key, [])
        flags = [row[0] for row in records]
        if key[2] == 'shield' and key in unknown_adjustments:
            excluded.append({'frame': key[0], 'reason': 'ambiguous critical sources', 'targetSide': key[1]})
            continue
        if not flags:
            # No HP/shield adjustment means no observable critical decision.
            continue
        distinct_hits = max((sum(row[1] == attribute for row in records)
                             for attribute in ('Health', 'Shield')), default=0)
        ordered_flags = (ordered_damage_critical_flags(events, records) if key[2] == 'damage' else
                         ordered_shield_critical_flags(events, records) if key[2] == 'shield' else None)
        if len(set(flags)) == 1 and (len(events) == 1 or distinct_hits == len(events)):
            # All observed hits have the same bit, so neither source order
            # nor splitting one hit across HP and shield creates ambiguity.
            # Require enough same-attribute rows to cover every hit: one
            # split hit plus an unobserved/capped hit must stay ambiguous.
            paired = [flags[0]] * len(events)
        elif ordered_flags is not None:
            # Complete adjustment boundaries preserve native execution order;
            # a damage hit split across Health/Shield is still one decision.
            paired = ordered_flags
        elif len(events) == 1:
            excluded.append({'frame': key[0], 'reason': 'inconsistent critical flags', 'targetSide': key[1]})
            continue
        else:
            # Split hits, missing adjustments, or unknown execution order do
            # not prove which source owns a mixed critical bit.
            excluded.append({'frame': key[0], 'reason': 'ambiguous critical sources', 'targetSide': key[1]})
            continue
        for event, critical in zip(events, paired):
            criticals.append(dict(base(event, key[2]), targetSide=key[1], critical=critical))
    status_records, status_excluded = status_apply_criticals(native, base)
    criticals.extend(status_records)
    excluded.extend(status_excluded)
    order_rows = []
    for frame, pairs in sorted(orders.items()):
        sources = _activation_order(pairs)
        if len(sources) >= 2:
            order_rows.append({'frame': frame, 'sources': sources})
    return {'schemaVersion': 1, 'replaySha256': native.get('replay', {}).get('sha256'),
            'targets': targets, 'criticals': criticals, 'order': order_rows,
            'enchantments': enchantments, 'transforms': transforms,
            'identities': identities, 'excluded': excluded}
