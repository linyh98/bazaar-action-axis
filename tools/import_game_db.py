"""Convert the game's GameData.cards.db into simulator card definitions.

Usage:
    python tools/import_game_db.py
"""
from __future__ import annotations

import json
import sqlite3
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_DB = (
    ROOT
    / "vendor"
    / "BazaarPlusPlus"
    / "bazaarplusplus-mod"
    / "tests"
    / "CombatImpact.Corpus"
    / "corpus"
    / "GameData.cards.db"
)
DEFAULT_OUT = ROOT / "data" / "game_cards_structured.json"

ACTION_VALUE_ATTR = {
    "TActionPlayerDamage": "DamageAmount",
    "TActionPlayerHeal": "HealAmount",
    "TActionPlayerShieldApply": "ShieldApplyAmount",
    "TActionPlayerBurnApply": "BurnApplyAmount",
    "TActionPlayerPoisonApply": "PoisonApplyAmount",
    "TActionPlayerRegenApply": "RegenApplyAmount",
}

DURATION_ACTION = {
    "TActionCardHaste": ("haste", "HasteAmount", "HasteTargets", "randomSelfItem"),
    "TActionCardSlow": ("slow", "SlowAmount", "SlowTargets", "randomEnemyItem"),
    "TActionCardFreeze": ("freeze", "FreezeAmount", None, "randomEnemyItem"),
    "TActionCardCharge": ("charge", "ChargeAmount", "ChargeTargets", "randomSelfItem"),
}

TRIGGER_EVENT = {
    "TTriggerOnCardFired": "onUse",
    "TTriggerOnFightStarted": "atCombatStart",
    "TTriggerOnCardPerformedHaste": "onHaste",
    "TTriggerOnCardPerformedSlow": "onSlow",
    "TTriggerOnCardPerformedFreeze": "onFreeze",
    "TTriggerOnCardCritted": "onCrit",
    "TTriggerOnCardPerformedHeal": "onHeal",
    "TTriggerOnCardPerformedShield": "onShield",
    "TTriggerOnCardPerformedBurn": "onBurnApplied",
    "TTriggerOnCardPerformedPoison": "onPoisonApplied",
    "TTriggerOnCardPerformedRegen": "onRegen",
    "TTriggerOnCardPerformedReload": "onReload",
}


def _num(value, default=0.0):
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


def map_target(target):
    """Return (Target | None, has_conditions)."""
    if not isinstance(target, dict):
        return None, False
    conditions = target.get("Conditions")
    kind = target.get("$type")
    if kind == "TTargetPlayerRelative":
        mode = str(target.get("TargetMode", "")).lower()
        return ({"kind": "selfHero"} if mode == "self" else {"kind": "enemyHero"}), bool(conditions)
    if kind == "TTargetCardSelf":
        return {"kind": "selfItem", "itemId": ""}, bool(conditions)
    if kind == "TTargetCardRandom":
        section = str(target.get("TargetSection", ""))
        out = "randomSelfItem" if section.startswith("Self") else "randomEnemyItem"
        return {"kind": out}, bool(conditions)
    if kind == "TTargetCardSection":
        section = str(target.get("TargetSection", ""))
        out = "allSelfItems" if section.startswith("Self") else "allEnemyItems"
        return {"kind": out}, bool(conditions)
    if kind == "TTargetCardPositional":
        pos = str(target.get("Position", "")).lower()
        if "left" in pos:
            return {"kind": "leftSelf"}, bool(conditions)
        if "right" in pos:
            return {"kind": "rightSelf"}, bool(conditions)
        return {"kind": "adjacentSelf"}, bool(conditions)
    return None, bool(conditions)


def map_trigger_events(trigger):
    if not isinstance(trigger, dict):
        return []
    kind = trigger.get("$type")
    if kind == "TTriggerOr":
        parts = trigger.get("Triggers") or trigger.get("Conditions") or []
        events = []
        for part in parts:
            sub = map_trigger_events(part)
            if not sub:
                return []
            events.extend(sub)
        return events
    event = TRIGGER_EVENT.get(kind)
    return [event] if event else []


def action_to_effects(action, attrs, card_id):
    """Return (effects, mapped, has_conditions)."""
    if not isinstance(action, dict):
        return [], False, False
    kind = action.get("$type")

    if kind == "TActionAnd":
        effects = []
        mapped = True
        has_conditions = False
        for sub in action.get("Actions") or []:
            sub_effects, sub_mapped, sub_conditions = action_to_effects(sub, attrs, card_id)
            effects.extend(sub_effects)
            mapped = mapped and sub_mapped
            has_conditions = has_conditions or sub_conditions
        return effects, mapped, has_conditions

    if kind in ACTION_VALUE_ATTR:
        target, has_conditions = map_target(action.get("Target"))
        if target is None:
            return [], False, has_conditions
        if target.get("kind") == "selfItem":
            target["itemId"] = card_id
        effect_kind = {
            "TActionPlayerDamage": "damage",
            "TActionPlayerHeal": "heal",
            "TActionPlayerShieldApply": "shield",
            "TActionPlayerBurnApply": "burn",
            "TActionPlayerPoisonApply": "poison",
            "TActionPlayerRegenApply": "regen",
        }[kind]
        amount = _num(attrs.get(ACTION_VALUE_ATTR[kind]))
        return [
            {"kind": effect_kind, "amount": {"type": "const", "value": amount}, "target": target}
        ], True, has_conditions

    if kind in DURATION_ACTION:
        effect_kind, duration_attr, count_attr, default_target = DURATION_ACTION[kind]
        target, has_conditions = map_target(action.get("Target"))
        if target is None:
            target = {"kind": default_target}
        seconds = _num(attrs.get(duration_attr)) / 1000.0
        count = int(_num(attrs.get(count_attr), 1)) if count_attr else 1
        count = max(1, count)
        return [
            {"kind": effect_kind, "seconds": seconds, "target": target, "count": count}
        ], True, has_conditions

    if kind == "TActionCardReload":
        target, has_conditions = map_target(action.get("Target"))
        if target is None:
            target = {"kind": "randomSelfItem"}
        amount = int(_num(attrs.get("ReloadAmount"), 1))
        count = max(1, int(_num(attrs.get("ReloadTargets"), 1)))
        return [
            {"kind": "reload", "amount": amount, "target": target, "count": count}
        ], True, has_conditions

    if kind == "TActionPlayerAmmo":
        amount = int(_num(attrs.get("AmmoMax"), _num(action.get("Value"))))
        return [{"kind": "ammo", "amount": amount}], True, False

    return [], False, False


def convert_item(data):
    guid = data.get("Id", "")
    name = data.get("InternalName") or guid
    size = data.get("Size", "Small")
    tags = list(data.get("Tags") or [])
    abilities = data.get("Abilities") or {}
    auras = data.get("Auras") or {}
    cards = []

    inherited_attrs = {}
    for tier, tier_data in (data.get("Tiers") or {}).items():
        inherited_attrs = {**inherited_attrs, **(tier_data.get("Attributes") or {})}
        attrs = inherited_attrs
        card_id = f"{guid}@{tier}"
        cooldown = _num(attrs.get("CooldownMax")) / 1000.0
        multicast_bonus = max(0, int(_num(attrs.get("Multicast"), 1)) - 1)
        ammo_max = int(_num(attrs.get("AmmoMax"))) if _num(attrs.get("AmmoMax")) > 0 else None
        effects = []
        triggers = []
        unmapped = {}
        partial = False

        for ability_id in tier_data.get("AbilityIds") or []:
            ability = abilities.get(str(ability_id))
            if not isinstance(ability, dict):
                unmapped[str(ability_id)] = None
                partial = True
                continue
            trigger = ability.get("Trigger") or {}
            events = map_trigger_events(trigger)
            action = ability.get("Action") or {}
            action_effects, mapped, has_conditions = action_to_effects(action, attrs, card_id)
            if not events or not mapped:
                unmapped[str(ability_id)] = ability
                partial = True
                continue
            if has_conditions or ability.get("Prerequisites"):
                partial = True
            if events == ["onUse"] and trigger.get("$type") == "TTriggerOnCardFired":
                effects.extend(action_effects)
            else:
                for event in dict.fromkeys(events):
                    triggers.append({"event": event, "effects": action_effects})

        if auras:
            partial = True

        cards.append(
            {
                "guid": guid,
                "internalName": name,
                "size": size,
                "tier": tier,
                "cooldown": round(cooldown, 3),
                "multicastBonus": multicast_bonus,
                "ammoMax": ammo_max,
                "effects": effects,
                "triggers": triggers,
                "tags": tags,
                "parseStatus": "partial" if partial else "structured",
                "dataSource": "game-db",
                "rawAbilities": unmapped,
            }
        )
    return cards


def convert_cards(db_path: Path = DEFAULT_DB):
    con = sqlite3.connect(f"file:{db_path.as_posix()}?mode=ro", uri=True)
    try:
        rows = con.execute("select Id, Data from cards").fetchall()
    finally:
        con.close()

    out = []
    for _, raw in rows:
        data = json.loads(raw)
        if data.get("$type") != "TCardItem":
            continue
        out.extend(convert_item(data))
    out.sort(key=lambda c: (c["internalName"].lower(), c["tier"]))
    return out


def main() -> None:
    import argparse

    parser = argparse.ArgumentParser(description="Convert GameData.cards.db to JSON")
    parser.add_argument("--db", default=str(DEFAULT_DB))
    parser.add_argument("--out", default=str(DEFAULT_OUT))
    args = parser.parse_args()

    out_path = Path(args.out)
    cards = convert_cards(Path(args.db))
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(cards, ensure_ascii=False, indent=2), encoding="utf-8")
    structured = sum(1 for c in cards if c["parseStatus"] == "structured")
    print(f"items: {len({c['guid'] for c in cards})}, entries: {len(cards)}, structured: {structured}, partial: {len(cards) - structured}")
    print(f"output: {out_path}")


if __name__ == "__main__":
    main()
