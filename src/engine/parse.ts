import type { Effect, NumExpr, Target, TriggerDef } from './types';

export interface ParseResult {
  cooldown: number | null;
  effects: Effect[];
  triggers: TriggerDef[];
  ammoMax?: number;
  parseStatus: 'structured' | 'partial' | 'text';
  unparsed: string[];
  ignored: string[];
}

const IGNORABLE_LINE = [
  /^at the start of each (day|hour)/i,
  /^when you (buy|sell|upgrade|reroll|transform|enchant)/i,
  /^when this is (transformed|enchanted)/i,
  /^this has \+?[0-9]+.*\bvalue\b/i,
  /^this has value/i,
  /^adjacent items gain value/i,
  /^your .*gain value/i,
  /^this permanently/i,
  /^destroy this/i,
  /^(golden|heavy|icy|turbo|shielded|restorative|toxic|fiery|heated|chilled|frozen):/i,
];

function isIgnorableLine(line: string): boolean {
  return IGNORABLE_LINE.some((re) => re.test(line));
}

function amountFromText(text: string): NumExpr | null {
  const t = text.trim();
  const fixed = t.match(/^([0-9]+(?:\.[0-9]+)?)$/);
  if (fixed) return { type: 'const', value: Number(fixed[1]) };

  const percent = t.match(
    /^([0-9]+(?:\.[0-9]+)?)%\s+of\s+(?:an?\s+)?(enemy'?s?|enemies'?|your|the\s+enemy'?s?)\s+max\s*health/i,
  );
  if (percent) {
    const value = Number(percent[1]);
    return /your/i.test(percent[2])
      ? { type: 'percentMaxHp', value, basis: 'source' }
      : { type: 'percentMaxHp', value };
  }

  if (/this\s+item'?s?\s+value/i.test(t)) return { type: 'value' };
  if (/your\s+regen/i.test(t)) return { type: 'regen' };
  if (/your\s+income/i.test(t)) return { type: 'income' };
  return null;
}

function parseLine(line: string): Effect | null {
  let m = line.match(/^deal\s+([0-9]+(?:\.[0-9]+)?)\s+damage[.]?$/i);
  if (m) {
    return {
      kind: 'damage',
      amount: { type: 'const', value: Number(m[1]) },
      target: { kind: 'enemyHero' },
    };
  }

  m = line.match(/^deal\s+damage\s+equal\s+to\s+(.+)$/i);
  if (m) {
    const amount = amountFromText(m[1]);
    if (amount) return { kind: 'damage', amount, target: { kind: 'enemyHero' } };
  }

  m = line.match(/^heal\s+([0-9]+(?:\.[0-9]+)?)[.]?$/i);
  if (m) {
    return {
      kind: 'heal',
      amount: { type: 'const', value: Number(m[1]) },
      target: { kind: 'selfHero' },
    };
  }

  m = line.match(/^heal\s+equal\s+to\s+(.+)$/i);
  if (m) {
    const amount = amountFromText(m[1]);
    if (amount) return { kind: 'heal', amount, target: { kind: 'selfHero' } };
  }

  m = line.match(/^shield\s+([0-9]+(?:\.[0-9]+)?)[.]?$/i);
  if (m) {
    return {
      kind: 'shield',
      amount: { type: 'const', value: Number(m[1]) },
      target: { kind: 'selfHero' },
    };
  }

  m = line.match(/^shield\s+equal\s+to\s+(.+)$/i);
  if (m) {
    const amount = amountFromText(m[1]);
    if (amount) return { kind: 'shield', amount, target: { kind: 'selfHero' } };
  }

  m = line.match(/^burn\s+([0-9]+(?:\.[0-9]+)?)[.]?$/i);
  if (m) {
    return {
      kind: 'burn',
      amount: { type: 'const', value: Number(m[1]) },
      target: { kind: 'enemyHero' },
    };
  }

  m = line.match(/^poison\s+([0-9]+(?:\.[0-9]+)?)[.]?$/i);
  if (m) {
    return {
      kind: 'poison',
      amount: { type: 'const', value: Number(m[1]) },
      target: { kind: 'enemyHero' },
    };
  }

  m = line.match(
    /^haste\s+adjacent\s+items?\s+for\s+([0-9]+(?:\.[0-9]+)?)\s+second(?:\(s\)|s)?/i,
  );
  if (m) {
    return { kind: 'haste', seconds: Number(m[1]), count: 1, target: { kind: 'adjacentSelf' } };
  }

  m = line.match(
    /^haste\s+the\s+item\s+to\s+the\s+(left|right)\s+of\s+this\s+for\s+([0-9]+(?:\.[0-9]+)?)\s+second(?:\(s\)|s)?/i,
  );
  if (m) {
    return {
      kind: 'haste',
      seconds: Number(m[2]),
      count: 1,
      target: { kind: m[1].toLowerCase() === 'left' ? 'leftSelf' : 'rightSelf' },
    };
  }

  m = line.match(
    /^charge\s+the\s+item\s+to\s+the\s+(left|right)\s+of\s+this\s+(?:for\s+)?([0-9]+(?:\.[0-9]+)?)\s+second(?:\(s\)|s)?/i,
  );
  if (m) {
    return {
      kind: 'charge',
      seconds: Number(m[2]),
      count: 1,
      target: { kind: m[1].toLowerCase() === 'left' ? 'leftSelf' : 'rightSelf' },
    };
  }

  m = line.match(
    /^slow\s+the\s+item\s+to\s+the\s+(left|right)\s+of\s+this\s+for\s+([0-9]+(?:\.[0-9]+)?)\s+second(?:\(s\)|s)?/i,
  );
  if (m) {
    return {
      kind: 'slow',
      seconds: Number(m[2]),
      count: 1,
      target: { kind: m[1].toLowerCase() === 'left' ? 'leftSelf' : 'rightSelf' },
    };
  }

  m = line.match(
    /^freeze\s+the\s+item\s+to\s+the\s+(left|right)\s+of\s+this\s+for\s+([0-9]+(?:\.[0-9]+)?)\s+second(?:\(s\)|s)?/i,
  );
  if (m) {
    return {
      kind: 'freeze',
      seconds: Number(m[2]),
      count: 1,
      target: { kind: m[1].toLowerCase() === 'left' ? 'leftSelf' : 'rightSelf' },
    };
  }

  m = line.match(/^this\s+has\s+\+([0-9]+)\s+multicast[.]?$/i);
  if (m) return { kind: 'multicast', count: Number(m[1]) + 1 };

  m = line.match(
    /^haste\s+(?:([0-9]+)\s+)?(?:an?\s+)?item(?:\(s\)|s)?\s+for\s+([0-9]+(?:\.[0-9]+)?)\s+second(?:\(s\)|s)?/i,
  );
  if (m) {
    return {
      kind: 'haste',
      seconds: Number(m[2]),
      count: m[1] ? Number(m[1]) : 1,
      target: { kind: 'randomSelfItem' },
    };
  }

  m = line.match(
    /^slow\s+(?:([0-9]+)\s+)?(?:an?\s+)?item(?:\(s\)|s)?\s+for\s+([0-9]+(?:\.[0-9]+)?)\s+second(?:\(s\)|s)?/i,
  );
  if (m) {
    return {
      kind: 'slow',
      seconds: Number(m[2]),
      count: m[1] ? Number(m[1]) : 1,
      target: { kind: 'randomEnemyItem' },
    };
  }

  m = line.match(
    /^freeze\s+(?:([0-9]+)\s+)?(?:an?\s+)?item(?:\(s\)|s)?\s+for\s+([0-9]+(?:\.[0-9]+)?)\s+second(?:\(s\)|s)?/i,
  );
  if (m) {
    return {
      kind: 'freeze',
      seconds: Number(m[2]),
      count: m[1] ? Number(m[1]) : 1,
      target: { kind: 'randomEnemyItem' },
    };
  }

  m = line.match(
    /^charge\s+(?:([0-9]+)\s+)?(?:an?\s+)?(?:adjacent\s+)?(?:[a-z]+\s+)?([0-9]+(?:\.[0-9]+)?)\s+second(?:\(s\)|s)?/i,
  );
  if (m) {
    const target: Target = /adjacent/i.test(line)
      ? { kind: 'adjacentSelf' }
      : { kind: 'randomSelfItem' };
    return {
      kind: 'charge',
      seconds: Number(m[2]),
      count: m[1] ? Number(m[1]) : 1,
      target,
    };
  }

  m = line.match(/^multicast\s+([0-9]+)[.]?$/i);
  if (m) return { kind: 'multicast', count: Number(m[1]) };

  m = line.match(/^crit\s+chance\s+([0-9]+(?:\.[0-9]+)?)%/i);
  if (m) return { kind: 'crit', chance: Number(m[1]) / 100 };

  if (/^lifesteal\b/i.test(line)) {
    return { kind: 'lifesteal', percent: 1 };
  }

  if (/^reload\b/i.test(line)) {
    return {
      kind: 'reload',
      amount: 1,
      target: /adjacent/i.test(line) ? { kind: 'adjacentSelf' } : { kind: 'randomSelfItem' },
    };
  }

  return null;
}

const TRIGGER_EVENTS: Record<string, TriggerDef['event']> = {
  heal: 'onHeal',
  shield: 'onShield',
  haste: 'onHaste',
  slow: 'onSlow',
  freeze: 'onFreeze',
  poison: 'onPoisonApplied',
  burn: 'onBurnApplied',
  crit: 'onCrit',
  reload: 'onReload',
  hasted: 'onHaste',
  slowed: 'onSlow',
  frozen: 'onFreeze',
};

function parseEffectList(text: string): Effect[] | null {
  const parts = text.split(/\s+and\s+/i);
  const effects: Effect[] = [];
  for (const part of parts) {
    const effect = parseLine(part.trim());
    if (!effect) return null;
    effects.push(effect);
  }
  return effects;
}

function parseTrigger(line: string): TriggerDef | null {
  let m = line.match(
    /^when you (heal|shield|haste|slow|freeze|poison|burn|crit|reload),\s*(.+)$/i,
  );
  if (m) {
    const effects = parseEffectList(m[2]);
    if (!effects) return null;
    return { event: TRIGGER_EVENTS[m[1].toLowerCase()], effects };
  }

  m = line.match(/^when this is (hasted|slowed|frozen),\s*(.+)$/i);
  if (m) {
    const effects = parseEffectList(m[2]);
    if (!effects) return null;
    return { event: TRIGGER_EVENTS[m[1].toLowerCase()], filter: { self: true }, effects };
  }

  m = line.match(/^at the start of combat,\s*(.+)$/i);
  if (m) {
    const effects = parseEffectList(m[1]);
    if (!effects) return null;
    return { event: 'atCombatStart', effects };
  }

  m = line.match(/^every ([0-9]+(?:\.[0-9]+)?) seconds?,\s*(.+)$/i);
  if (m) {
    const effects = parseEffectList(m[2]);
    if (!effects) return null;
    return { event: 'everySecond', everySeconds: Number(m[1]), effects };
  }

  return null;
}

export function parseTooltips(tooltips: string[]): ParseResult {
  let cooldown: number | null = null;
  let ammoMax: number | undefined;
  const effects: Effect[] = [];
  const triggers: TriggerDef[] = [];
  const unparsed: string[] = [];
  const ignored: string[] = [];

  for (const raw of tooltips) {
    const line = raw.trim();
    if (!line) continue;

    const cd = line.match(/^cooldown\s+([0-9]+(?:\.[0-9]+)?)\s*(?:second|seconds|s)\b/i);
    if (cd) {
      cooldown = Number(cd[1]);
      continue;
    }

    const ammo = line.match(/^ammo\s+([0-9]+)\b/i);
    if (ammo) {
      ammoMax = Number(ammo[1]);
      continue;
    }

    const regenGain = line.match(
      /^gain\s+([0-9]+(?:\.[0-9]+)?)\s+regen\s+for\s+the\s+fight/i,
    );
    if (regenGain) {
      triggers.push({
        event: 'atCombatStart',
        effects: [
          {
            kind: 'regen',
            amount: { type: 'const', value: Number(regenGain[1]) },
            target: { kind: 'selfHero' },
          },
        ],
      });
      continue;
    }

    const effect = parseLine(line);
    if (effect) {
      effects.push(effect);
      continue;
    }

    const trigger = parseTrigger(line);
    if (trigger) {
      triggers.push(trigger);
      continue;
    }

    if (isIgnorableLine(line)) {
      ignored.push(line);
      continue;
    }

    unparsed.push(line);
  }

  const parsedAnything = cooldown !== null || effects.length > 0 || triggers.length > 0;
  const parseStatus =
    cooldown !== null && unparsed.length === 0 && (effects.length > 0 || triggers.length > 0)
      ? 'structured'
      : parsedAnything
        ? 'partial'
        : 'text';

  return { cooldown, effects, triggers, ammoMax, parseStatus, unparsed, ignored };
}
