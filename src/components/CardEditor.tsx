import { useState } from 'react';
import type { CardDef, Effect, Size, Target, Tier } from '../engine/types';

export interface CardEditorProps {
  initial?: CardDef;
  onSave: (card: CardDef) => void;
  onCancel: () => void;
}

interface EffectDraft {
  kind: Effect['kind'];
  amount: number;
  seconds: number;
  count: number;
  chance: number;
  target: string;
}

const DEFAULT_EFFECT: EffectDraft = {
  kind: 'damage',
  amount: 10,
  seconds: 1,
  count: 1,
  chance: 0.2,
  target: 'enemyHero',
};

const EFFECT_KINDS: Effect['kind'][] = [
  'damage',
  'heal',
  'shield',
  'burn',
  'poison',
  'regen',
  'haste',
  'slow',
  'freeze',
  'charge',
  'multicast',
  'crit',
  'lifesteal',
  'reload',
  'ammo',
];

const TARGETS = [
  ['enemyHero', '敌方英雄'],
  ['selfHero', '己方英雄'],
  ['randomEnemyItem', '随机敌方物品'],
  ['randomSelfItem', '随机己方物品'],
  ['adjacentSelf', '相邻己方物品'],
  ['allEnemyItems', '全部敌方物品'],
  ['allSelfItems', '全部己方物品'],
] as const;

function targetFor(value: string): Target {
  switch (value) {
    case 'selfHero':
      return { kind: 'selfHero' };
    case 'randomSelfItem':
      return { kind: 'randomSelfItem' };
    case 'randomEnemyItem':
      return { kind: 'randomEnemyItem' };
    case 'adjacentSelf':
      return { kind: 'adjacentSelf' };
    case 'allSelfItems':
      return { kind: 'allSelfItems' };
    case 'allEnemyItems':
      return { kind: 'allEnemyItems' };
    default:
      return { kind: 'enemyHero' };
  }
}

function draftToEffect(draft: EffectDraft): Effect {
  switch (draft.kind) {
    case 'damage':
    case 'heal':
    case 'shield':
    case 'burn':
    case 'poison':
    case 'regen':
      return {
        kind: draft.kind,
        amount: { type: 'const', value: draft.amount },
        target: targetFor(draft.target),
      } as Effect;
    case 'haste':
    case 'slow':
    case 'freeze':
    case 'charge':
      return {
        kind: draft.kind,
        seconds: draft.seconds,
        count: draft.count,
        target: targetFor(draft.target),
      } as Effect;
    case 'multicast':
      return { kind: 'multicast', count: draft.count };
    case 'crit':
      return { kind: 'crit', chance: draft.chance };
    case 'lifesteal':
      return { kind: 'lifesteal', percent: draft.amount };
    case 'reload':
      return {
        kind: 'reload',
        amount: draft.amount,
        count: draft.count,
        target: targetFor(draft.target),
      };
    case 'ammo':
      return { kind: 'ammo', amount: draft.amount };
  }
}

export function CardEditor({ initial, onSave, onCancel }: CardEditorProps) {
  const [name, setName] = useState(initial?.name ?? '');
  const [nameCn, setNameCn] = useState(initial?.nameCn ?? '');
  const [size, setSize] = useState<Size>(initial?.size ?? 'Small');
  const [tier, setTier] = useState<Tier>(initial?.tier ?? 'Bronze');
  const [cooldown, setCooldown] = useState(String(initial?.cooldown ?? 4));
  const [ammoMax, setAmmoMax] = useState(
    initial?.ammoMax == null ? '' : String(initial.ammoMax),
  );
  const [tags, setTags] = useState((initial?.tags ?? []).join(', '));
  const [image, setImage] = useState(initial?.image ?? '');
  const [effects, setEffects] = useState<EffectDraft[]>([]);
  const [error, setError] = useState<string | null>(null);

  const updateEffect = (index: number, patch: Partial<EffectDraft>) => {
    setEffects((prev) =>
      prev.map((effect, i) => (i === index ? { ...effect, ...patch } : effect)),
    );
  };

  const save = () => {
    if (!name.trim()) {
      setError('名称不能为空');
      return;
    }
    const cd = Number(cooldown);
    if (!Number.isFinite(cd) || cd < 0.1) {
      setError('冷却必须 ≥ 0.1 秒');
      return;
    }
    if (effects.length === 0) {
      setError('至少添加一个效果');
      return;
    }
    setError(null);

    const card: CardDef = {
      id: initial?.id ?? `custom-${Math.random().toString(36).slice(2, 10)}`,
      name: name.trim(),
      nameCn: nameCn.trim() || undefined,
      image: image.trim() || undefined,
      size,
      tier,
      cooldown: cd,
      effects: effects.map(draftToEffect),
      triggers: [],
      tags: tags
        .split(',')
        .map((tag) => tag.trim())
        .filter(Boolean),
      ammoMax: ammoMax.trim() ? Number(ammoMax) : undefined,
      parseStatus: 'structured',
      rawTooltips: [],
      dataSource: 'custom',
    };
    onSave(card);
  };

  return (
    <div className="panel card-editor">
      <h3>{initial ? '编辑卡牌' : '新建卡牌'}</h3>

      <div className="editor-grid">
        <label>
          名称
          <input aria-label="名称" value={name} onChange={(e) => setName(e.target.value)} />
        </label>
        <label>
          中文名
          <input
            aria-label="中文名"
            value={nameCn}
            onChange={(e) => setNameCn(e.target.value)}
          />
        </label>
        <label>
          尺寸
          <select
            aria-label="尺寸"
            value={size}
            onChange={(e) => setSize(e.target.value as Size)}
          >
            <option value="Small">小</option>
            <option value="Medium">中</option>
            <option value="Large">大</option>
          </select>
        </label>
        <label>
          品阶
          <select
            aria-label="品阶"
            value={tier}
            onChange={(e) => setTier(e.target.value as Tier)}
          >
            <option value="Bronze">青铜</option>
            <option value="Silver">白银</option>
            <option value="Gold">黄金</option>
            <option value="Diamond">钻石</option>
            <option value="Legendary">传奇</option>
          </select>
        </label>
        <label>
          冷却（秒）
          <input
            aria-label="冷却（秒）"
            type="number"
            step="0.1"
            value={cooldown}
            onChange={(e) => setCooldown(e.target.value)}
          />
        </label>
        <label>
          弹药上限
          <input
            aria-label="弹药上限"
            type="number"
            value={ammoMax}
            onChange={(e) => setAmmoMax(e.target.value)}
          />
        </label>
        <label>
          标签
          <input aria-label="标签" value={tags} onChange={(e) => setTags(e.target.value)} />
        </label>
        <label>
          图片 URL
          <input aria-label="图片 URL" value={image} onChange={(e) => setImage(e.target.value)} />
        </label>
      </div>

      <div className="editor-effects">
        <button type="button" onClick={() => setEffects((prev) => [...prev, { ...DEFAULT_EFFECT }])}>
          添加效果
        </button>

        {effects.map((effect, index) => (
          <div key={index} className="effect-row">
            <select
              aria-label={`效果 ${index + 1} 类型`}
              value={effect.kind}
              onChange={(e) => updateEffect(index, { kind: e.target.value as Effect['kind'] })}
            >
              {EFFECT_KINDS.map((kind) => (
                <option key={kind} value={kind}>
                  {kind}
                </option>
              ))}
            </select>
            <input
              aria-label={`效果 ${index + 1} 数值`}
              type="number"
              value={effect.amount}
              onChange={(e) => updateEffect(index, { amount: Number(e.target.value) })}
            />
            <input
              aria-label={`效果 ${index + 1} 秒`}
              type="number"
              step="0.1"
              value={effect.seconds}
              onChange={(e) => updateEffect(index, { seconds: Number(e.target.value) })}
            />
            <input
              aria-label={`效果 ${index + 1} 次数`}
              type="number"
              value={effect.count}
              onChange={(e) => updateEffect(index, { count: Number(e.target.value) })}
            />
            <select
              aria-label={`效果 ${index + 1} 目标`}
              value={effect.target}
              onChange={(e) => updateEffect(index, { target: e.target.value })}
            >
              {TARGETS.map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
            <button
              type="button"
              aria-label={`移除效果 ${index + 1}`}
              onClick={() => setEffects((prev) => prev.filter((_, i) => i !== index))}
            >
              ×
            </button>
          </div>
        ))}
      </div>

      {error && <p className="error">{error}</p>}

      <div className="editor-actions">
        <button type="button" onClick={save}>
          保存
        </button>
        <button type="button" onClick={onCancel}>
          取消
        </button>
      </div>
    </div>
  );
}
