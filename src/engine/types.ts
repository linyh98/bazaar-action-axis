export type Size = 'Small' | 'Medium' | 'Large';
export type Tier = 'Bronze' | 'Silver' | 'Gold' | 'Diamond' | 'Legendary';
export type Side = 'player' | 'enemy';

export type NumExpr =
  | { type: 'const'; value: number }
  | { type: 'percentMaxHp'; value: number; basis?: 'target' | 'source' }
  | { type: 'value' }
  | { type: 'regen' }
  | { type: 'income' }
  | { type: 'perStack'; stack: 'burn' | 'poison'; per: number }
  | { type: 'perTag'; tag: string; per: number };

export type Target =
  | { kind: 'selfHero' }
  | { kind: 'enemyHero' }
  | { kind: 'selfItem'; itemId: string }
  | { kind: 'enemyItem'; itemId: string }
  | { kind: 'randomEnemyItem' }
  | { kind: 'randomSelfItem' }
  | { kind: 'allEnemyItems' }
  | { kind: 'allSelfItems' }
  | { kind: 'adjacentSelf' }
  | { kind: 'leftSelf' }
  | { kind: 'rightSelf' }
  | { kind: 'lowestHpEnemyItem' };

export type Effect =
  | { kind: 'damage'; amount: NumExpr; target: Target; crit?: boolean }
  | { kind: 'heal'; amount: NumExpr; target: Target; crit?: boolean }
  | { kind: 'shield'; amount: NumExpr; target: Target; crit?: boolean }
  | { kind: 'burn'; amount: NumExpr; target: Target }
  | { kind: 'poison'; amount: NumExpr; target: Target }
  | { kind: 'regen'; amount: NumExpr; target: Target }
  | { kind: 'haste'; seconds: number; target: Target; count?: number }
  | { kind: 'slow'; seconds: number; target: Target; count?: number }
  | { kind: 'freeze'; seconds: number; target: Target; count?: number }
  | { kind: 'charge'; seconds: number; target: Target; count?: number }
  | { kind: 'multicast'; count: number }
  | { kind: 'crit'; chance: number }
  | { kind: 'lifesteal'; percent: number }
  | { kind: 'reload'; amount: number; target: Target; count?: number }
  | { kind: 'ammo'; amount: number };

export type TriggerEvent =
  | 'onUse'
  | 'onHeal'
  | 'onShield'
  | 'onDamageDealt'
  | 'onDamageTaken'
  | 'onBurnApplied'
  | 'onPoisonApplied'
  | 'onRegen'
  | 'onHaste'
  | 'onSlow'
  | 'onFreeze'
  | 'onCrit'
  | 'onReload'
  | 'onKill'
  | 'atCombatStart'
  | 'everySecond';

export interface TriggerDef {
  event: TriggerEvent;
  filter?: {
    sourceTag?: string;
    sourceItemId?: string;
    targetSide?: Side;
    self?: boolean;
  };
  everySeconds?: number;
  effects: Effect[];
}

export interface TriggerPayload {
  event: TriggerEvent;
  sourceSide: Side;
  sourceItemId?: string;
  targetSide?: Side;
  targetItemId?: string;
  chainId?: number;
  depth: number;
}

export interface CardDef {
  id: string;
  baseItemId?: string;
  name: string;
  nameCn?: string;
  image?: string;
  size: Size;
  tier: Tier;
  cooldown: number;
  effects: Effect[];
  triggers: TriggerDef[];
  tags: string[];
  ammoMax?: number;
  startReady?: boolean;
  passive?: boolean;
  parseStatus: 'structured' | 'partial' | 'text';
  rawTooltips: string[];
  dataSource?: 'game-db' | 'tooltip' | 'custom' | 'merged';
}

export interface LoadoutItem {
  cardId: string;
  slot: number;
}

export interface Loadout {
  items: LoadoutItem[];
}

export interface RuleOverrides {
  dt?: number;
  maxTime?: number;
  hasteMultiplier?: number;
  slowMultiplier?: number;
  startReady?: boolean;
  burnDecay?: number;
  poisonDecay?: number;
  dotBypassesShield?: boolean;
  critMultiplier?: number;
  lifestealOnShieldDamage?: boolean;
  activationOrder?: 'player-first-left-to-right';
  maxEventsPerTick?: number;
  maxTriggerDepth?: number;
  maxSlots?: number;
  sandstorm?: { start: number; base: number; ramp: number } | null;
}

export interface BattleConfig {
  cards: Record<string, CardDef>;
  player: Loadout;
  enemy: Loadout;
  playerMaxHp: number;
  enemyMaxHp: number;
  seed: number;
  rules: RuleOverrides;
  maxTime: number;
}

export interface ItemState {
  card: CardDef;
  slot: number;
  cooldown: number;
  hasteUntil: number;
  slowUntil: number;
  frozenUntil: number;
  multicastBonus: number;
  critBonus: number;
  damageBonus: number;
  lifesteal: number;
  ammo: number;
  disabled: boolean;
}

export interface HeroState {
  hp: number;
  maxHp: number;
  shield: number;
  burn: number;
  poison: number;
  regen: number;
}

export interface ItemSnapshot {
  itemId: string;
  cardId: string;
  slot: number;
  cooldown: number;
  cooldownMax: number;
  hasteUntil: number;
  slowUntil: number;
  frozenUntil: number;
  ammo: number;
}

export interface Snapshot {
  time: number;
  player: HeroState & { items: ItemSnapshot[] };
  enemy: HeroState & { items: ItemSnapshot[] };
}

export type BattleEventKind =
  | 'activate'
  | 'damage'
  | 'heal'
  | 'shield'
  | 'burn'
  | 'poison'
  | 'regen'
  | 'haste'
  | 'slow'
  | 'freeze'
  | 'charge'
  | 'crit'
  | 'reload'
  | 'ammo'
  | 'death'
  | 'warning'
  | 'dotTick';

export interface BattleEvent {
  id: number;
  time: number;
  side: Side;
  sourceItemId?: string;
  sourceCardId?: string;
  kind: BattleEventKind;
  targetSide?: Side;
  targetItemId?: string;
  value?: number;
  absorbed?: number;
  hpAfter?: number;
  shieldAfter?: number;
  chainId: number;
  depth: number;
  note?: string;
}

export interface Totals {
  player: { damage: number; effectiveDamage: number; heal: number; shield: number };
  enemy: { damage: number; effectiveDamage: number; heal: number; shield: number };
}

export interface BattleResult {
  events: BattleEvent[];
  snapshots: Snapshot[];
  totals: Totals;
  winner: 'player' | 'enemy' | 'draw';
  endTime: number;
  warnings: string[];
}
