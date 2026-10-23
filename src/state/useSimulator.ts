import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { simulate } from '../engine/battle';
import type { BattleResult, CardDef, Loadout, RuleOverrides } from '../engine/types';
import allCards from '../data/cards.json';
import {
  loadCustomCards,
  loadLoadouts,
  loadRules,
  saveCustomCards,
  saveLoadouts,
  saveRules,
} from './storage';

const BUILTIN = allCards as CardDef[];

export interface SimulatorApi {
  cards: Record<string, CardDef>;
  player: Loadout;
  enemy: Loadout;
  rules: RuleOverrides;
  result: BattleResult | null;
  playhead: number;
  playing: boolean;
  speed: number;
  selectedEventId: number | null;
  selectedItemId: string | null;
  setPlayer: (loadout: Loadout) => void;
  setEnemy: (loadout: Loadout) => void;
  setRules: (rules: RuleOverrides) => void;
  run: (overrides?: { player?: Loadout; enemy?: Loadout }) => void;
  setPlayhead: (t: number) => void;
  play: () => void;
  pause: () => void;
  setSpeed: (s: number) => void;
  selectEvent: (id: number | null) => void;
  selectItem: (id: string | null) => void;
  advance: (dt: number) => void;
  addCustomCard: (card: CardDef) => void;
}

function defaultLoadouts(cards: CardDef[]): { player: Loadout; enemy: Loadout } {
  const seen = new Set<string>();
  const usable = cards.filter((card) => {
    if (card.parseStatus !== 'structured' || card.effects.length === 0) return false;
    const key = card.baseItemId ?? card.id;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
  const playerIds = usable.slice(0, 3).map((card) => card.id);
  const enemyIds = usable.slice(3, 6).map((card) => card.id);
  return {
    player: { items: playerIds.map((id, i) => ({ cardId: id, slot: i })) },
    enemy: { items: enemyIds.map((id, i) => ({ cardId: id, slot: i })) },
  };
}

export function useSimulator(): SimulatorApi {
  const [customCards, setCustomCards] = useState<CardDef[]>(() => loadCustomCards());

  const cards = useMemo(() => {
    const map: Record<string, CardDef> = {};
    for (const card of BUILTIN) map[card.id] = card;
    for (const card of customCards) map[card.id] = card;
    return map;
  }, [customCards]);

  const saved = useMemo(() => loadLoadouts(), []);
  const fallback = useMemo(() => defaultLoadouts(BUILTIN), []);
  const [player, setPlayerState] = useState<Loadout>(saved?.player ?? fallback.player);
  const [enemy, setEnemyState] = useState<Loadout>(saved?.enemy ?? fallback.enemy);
  const [rules, setRulesState] = useState<RuleOverrides>(() => loadRules());
  const [result, setResult] = useState<BattleResult | null>(null);
  const [playhead, setPlayheadState] = useState(0);
  const [playing, setPlaying] = useState(false);
  const [speed, setSpeedState] = useState(1);
  const [selectedEventId, setSelectedEventId] = useState<number | null>(null);
  const [selectedItemId, setSelectedItemId] = useState<string | null>(null);

  const resultRef = useRef<BattleResult | null>(null);
  const speedRef = useRef(1);

  useEffect(() => {
    speedRef.current = speed;
  }, [speed]);

  const setPlayer = useCallback(
    (loadout: Loadout) => {
      setPlayerState(loadout);
      saveLoadouts(loadout, enemy);
    },
    [enemy],
  );

  const setEnemy = useCallback(
    (loadout: Loadout) => {
      setEnemyState(loadout);
      saveLoadouts(player, loadout);
    },
    [player],
  );

  const setRules = useCallback((next: RuleOverrides) => {
    setRulesState(next);
    saveRules(next);
  }, []);

  const run = useCallback(
    (overrides?: { player?: Loadout; enemy?: Loadout }) => {
      const nextPlayer = overrides?.player ?? player;
      const nextEnemy = overrides?.enemy ?? enemy;
      const next = simulate({
        cards,
        player: nextPlayer,
        enemy: nextEnemy,
        playerMaxHp: 100,
        enemyMaxHp: 100,
        seed: 1,
        rules,
        maxTime: rules.maxTime ?? 60,
      });
      setPlayerState(nextPlayer);
      setEnemyState(nextEnemy);
      saveLoadouts(nextPlayer, nextEnemy);
      resultRef.current = next;
      setResult(next);
      setPlayheadState(0);
      setPlaying(false);
      setSelectedEventId(null);
      setSelectedItemId(null);
    },
    [cards, player, enemy, rules],
  );

  const setPlayhead = useCallback((t: number) => {
    setPlayheadState(Math.max(0, t));
  }, []);

  const advance = useCallback((dt: number) => {
    setPlayheadState((current) => {
      const end = resultRef.current?.endTime ?? 0;
      if (end <= 0) {
        setPlaying(false);
        return current;
      }
      const next = Math.min(current + dt * speedRef.current, end);
      if (next >= end) setPlaying(false);
      return next;
    });
  }, []);

  const play = useCallback(() => {
    if (resultRef.current) setPlaying(true);
  }, []);

  const pause = useCallback(() => setPlaying(false), []);

  const setSpeed = useCallback((next: number) => {
    setSpeedState(next);
  }, []);

  const selectEvent = useCallback((id: number | null) => {
    setSelectedEventId(id);
    setSelectedItemId(null);
  }, []);

  const selectItem = useCallback((id: string | null) => {
    setSelectedItemId(id);
    setSelectedEventId(null);
  }, []);

  const addCustomCard = useCallback((card: CardDef) => {
    setCustomCards((prev) => {
      const next = [...prev, card];
      saveCustomCards(next);
      return next;
    });
  }, []);

  return {
    cards,
    player,
    enemy,
    rules,
    result,
    playhead,
    playing,
    speed,
    selectedEventId,
    selectedItemId,
    setPlayer,
    setEnemy,
    setRules,
    run,
    setPlayhead,
    play,
    pause,
    setSpeed,
    selectEvent,
    selectItem,
    advance,
    addCustomCard,
  };
}
