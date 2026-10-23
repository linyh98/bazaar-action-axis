import { useEffect, useState } from 'react';
import type { Side } from './engine/types';
import { CardEditor } from './components/CardEditor';
import { DetailPanel } from './components/DetailPanel';
import { ImportDialog } from './components/ImportDialog';
import { LoadoutEditor } from './components/LoadoutEditor';
import { Timeline } from './components/Timeline';
import { TotalsPanel } from './components/TotalsPanel';
import { useSimulator } from './state/useSimulator';

export default function App() {
  const sim = useSimulator();
  const [importSide, setImportSide] = useState<Side | null>(null);
  const [editorOpen, setEditorOpen] = useState(false);
  const [liveStatus, setLiveStatus] = useState<'idle' | 'loading' | 'error'>('idle');
  const [liveMessage, setLiveMessage] = useState('');

  useEffect(() => {
    if (!sim.playing) return;
    let frame = 0;
    let last = performance.now();
    const loop = (now: number) => {
      sim.advance((now - last) / 1000);
      last = now;
      frame = requestAnimationFrame(loop);
    };
    frame = requestAnimationFrame(loop);
    return () => cancelAnimationFrame(frame);
  }, [sim.playing]);

  const pickCard = (cardId: string) => {
    if (!importSide) return;
    const loadout = importSide === 'player' ? sim.player : sim.enemy;
    if (loadout.items.length >= 10) {
      setImportSide(null);
      return;
    }
    const nextSlot =
      loadout.items.reduce((max, item) => Math.max(max, item.slot), -1) + 1;
    const next = {
      items: [...loadout.items, { cardId, slot: nextSlot }],
    };
    if (importSide === 'player') sim.setPlayer(next);
    else sim.setEnemy(next);
    setImportSide(null);
  };

  const recognizeLive = async () => {
    setLiveStatus('loading');
    setLiveMessage('正在识别游戏当前卡牌…（截图 OCR 约 20 秒）');
    try {
      const response = await fetch('/api/live-cards');
      const payload = await response.json();
      if (!response.ok) {
        throw new Error(payload.error ?? '识别失败');
      }

      const entries = (payload.cards ?? []) as Array<{
        cardId: string | null;
        name: string;
        tier: string | null;
        found: boolean;
      }>;
      const found = entries.filter((entry) => entry.cardId);
      const missing = entries.filter((entry) => !entry.cardId);
      if (found.length === 0) {
        throw new Error(
          '没有识别到可用的卡牌。请关闭 Windows 日历/通知浮窗后重试，或重启游戏加载 BPP 插件（更稳定）',
        );
      }

      const items = found.map((entry, index) => ({
        cardId: entry.cardId as string,
        slot: index,
      }));
      sim.run({ player: { items } });

      setLiveStatus('idle');
      const source = payload.source === 'bpp' ? 'BPP 插件' : '截图 OCR';
      const missingText =
        missing.length > 0
          ? `；${missing.length} 张未匹配：${missing.map((entry) => entry.name).join('、')}`
          : '';
      setLiveMessage(`来源：${source}；已载入 ${items.length} 张到行动轴${missingText}`);
    } catch (error) {
      setLiveStatus('error');
      setLiveMessage(
        `识别失败：${error instanceof Error ? error.message : String(error)}`,
      );
    }
  };

  return (
    <div className="app">
      <header className="app-header">
        <h1>大巴扎行动轴模拟器</h1>
        <div className="header-actions">
          <button type="button" onClick={() => sim.run()}>
            模拟
          </button>
          <button
            type="button"
            onClick={recognizeLive}
            disabled={liveStatus === 'loading'}
          >
            {liveStatus === 'loading' ? '识别中…' : '识别当前卡'}
          </button>
          <button type="button" onClick={() => setEditorOpen(true)}>
            新建卡牌
          </button>
        </div>
      </header>

      {liveMessage && (
        <div className={`panel live-status ${liveStatus}`}>{liveMessage}</div>
      )}

      {sim.result && sim.result.warnings.length > 0 && (
        <div className="panel warning-panel">
          <strong>模拟警告</strong>
          <ul>
            {sim.result.warnings.map((warning, index) => (
              <li key={`${index}-${warning}`}>{warning}</li>
            ))}
          </ul>
        </div>
      )}

      <div className="loadouts">
        <LoadoutEditor
          side="player"
          loadout={sim.player}
          cards={sim.cards}
          maxSlots={10}
          onChange={sim.setPlayer}
          onOpenImport={setImportSide}
        />
        <LoadoutEditor
          side="enemy"
          loadout={sim.enemy}
          cards={sim.cards}
          maxSlots={10}
          onChange={sim.setEnemy}
          onOpenImport={setImportSide}
        />
      </div>

      {sim.result && (
        <>
          <TotalsPanel totals={sim.result.totals} endTime={sim.result.endTime} />

          <Timeline
            result={sim.result}
            cards={sim.cards}
            playhead={sim.playhead}
            onPlayheadChange={sim.setPlayhead}
            selectedEventId={sim.selectedEventId}
            onSelectEvent={sim.selectEvent}
            selectedItemId={sim.selectedItemId}
            onSelectItem={sim.selectItem}
          />

          <div className="panel playback">
            <button type="button" onClick={sim.playing ? sim.pause : sim.play}>
              {sim.playing ? '暂停' : '播放'}
            </button>
            <select
              value={sim.speed}
              onChange={(event) => sim.setSpeed(Number(event.target.value))}
            >
              <option value={0.5}>0.5×</option>
              <option value={1}>1×</option>
              <option value={2}>2×</option>
              <option value={4}>4×</option>
            </select>
            <span className="dim">
              {sim.playhead.toFixed(1)}s / {sim.result.endTime.toFixed(1)}s
            </span>
          </div>

          <DetailPanel
            events={sim.result.events}
            cards={sim.cards}
            selectedEventId={sim.selectedEventId}
            selectedItemId={sim.selectedItemId}
          />
        </>
      )}

      <ImportDialog
        open={importSide !== null}
        cards={sim.cards}
        onPick={pickCard}
        onClose={() => setImportSide(null)}
      />

      {editorOpen && (
        <div className="dialog-backdrop">
          <CardEditor
            onSave={(card) => {
              sim.addCustomCard(card);
              setEditorOpen(false);
            }}
            onCancel={() => setEditorOpen(false)}
          />
        </div>
      )}
    </div>
  );
}
