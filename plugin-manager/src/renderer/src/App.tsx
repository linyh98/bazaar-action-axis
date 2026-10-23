import { useCallback, useEffect, useState } from 'react';
import type {
  ActionResult,
  AppConfig,
  Bootstrap,
  BuiltinPluginId,
  PluginInfo
} from '@shared/types';
import { Badge, Button, Modal, Toast, type ToastTone } from './components';
import { formatBytes, shortPath } from './format';
import OverviewPage from './pages/OverviewPage';
import PluginsPage from './pages/PluginsPage';
import DataPage from './pages/DataPage';
import SettingsPage from './pages/SettingsPage';

type Page = 'overview' | 'plugins' | 'data' | 'settings';

interface ToastState {
  tone: ToastTone;
  message: string;
  detail?: string;
}

interface ConfirmState {
  title: string;
  message: string;
  detail?: string;
  confirmLabel: string;
  danger?: boolean;
  action: () => Promise<void>;
}

const NAV: { id: Page; label: string }[] = [
  { id: 'overview', label: '概览' },
  { id: 'plugins', label: '插件' },
  { id: 'data', label: '数据' },
  { id: 'settings', label: '设置' }
];

const PAGE_META: Record<Page, { title: string; subtitle: string }> = {
  overview: { title: '概览', subtitle: '游戏目录、插件与本地数据状态' },
  plugins: { title: '插件', subtitle: '安装、更新与卸载 BepInEx 插件' },
  data: { title: '本地数据', subtitle: '浏览并安全清理插件产生的数据' },
  settings: { title: '设置', subtitle: '游戏目录、管理器数据与关于' }
};

function errorText(error: unknown): string {
  if (error instanceof Error) return error.message;
  return String(error);
}

function NavIcon({ page }: { page: Page }) {
  const common = {
    width: 18,
    height: 18,
    viewBox: '0 0 24 24',
    fill: 'none',
    stroke: 'currentColor',
    strokeWidth: 1.6,
    strokeLinecap: 'round' as const,
    strokeLinejoin: 'round' as const
  };
  if (page === 'overview') {
    return (
      <svg {...common} aria-hidden>
        <rect x="3" y="3" width="7" height="7" rx="1.5" />
        <rect x="14" y="3" width="7" height="7" rx="1.5" />
        <rect x="3" y="14" width="7" height="7" rx="1.5" />
        <rect x="14" y="14" width="7" height="7" rx="1.5" />
      </svg>
    );
  }
  if (page === 'plugins') {
    return (
      <svg {...common} aria-hidden>
        <path d="M8 3h8v4h4v8h-4v6H8v-6H4V7h4V3z" />
      </svg>
    );
  }
  if (page === 'data') {
    return (
      <svg {...common} aria-hidden>
        <ellipse cx="12" cy="5" rx="8" ry="3" />
        <path d="M4 5v6c0 1.7 3.6 3 8 3s8-1.3 8-3V5" />
        <path d="M4 11v6c0 1.7 3.6 3 8 3s8-1.3 8-3v-6" />
      </svg>
    );
  }
  return (
    <svg {...common} aria-hidden>
      <circle cx="12" cy="12" r="3" />
      <path d="M12 2v3M12 19v3M2 12h3M19 12h3M4.9 4.9l2.1 2.1M17 17l2.1 2.1M19.1 4.9L17 7M7 17l-2.1 2.1" />
    </svg>
  );
}

export default function App() {
  const [bootstrap, setBootstrap] = useState<Bootstrap | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [page, setPage] = useState<Page>('overview');
  const [busy, setBusy] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState | null>(null);
  const [confirm, setConfirm] = useState<ConfirmState | null>(null);

  const refresh = useCallback(async (silent = false) => {
    if (!silent) setLoading(true);
    try {
      const next = await window.bazaarManager.bootstrap();
      setBootstrap(next);
      setError(null);
    } catch (cause) {
      setError(errorText(cause));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  useEffect(() => {
    if (!toast) return;
    const timer = window.setTimeout(() => setToast(null), toast.detail ? 8000 : 4000);
    return () => window.clearTimeout(timer);
  }, [toast]);

  const runAction = async (label: string, action: () => Promise<ActionResult>): Promise<boolean> => {
    setBusy(label);
    try {
      const response = await action();
      if (response.bootstrap) setBootstrap(response.bootstrap);
      setToast({
        tone: response.ok ? 'success' : 'error',
        message: response.message,
        detail: response.detail
      });
      return response.ok;
    } catch (cause) {
      setToast({ tone: 'error', message: errorText(cause) });
      return false;
    } finally {
      setBusy(null);
    }
  };

  const detect = async () => {
    setBusy('重新检测游戏目录');
    try {
      const next = await window.bazaarManager.detectGame();
      setBootstrap(next);
      setToast({
        tone: 'success',
        message: next.game.found ? `已定位游戏目录：${next.game.path}` : '没有找到 The Bazaar，请手动选择目录'
      });
    } catch (cause) {
      setToast({ tone: 'error', message: errorText(cause) });
    } finally {
      setBusy(null);
    }
  };

  const choosePath = async () => {
    setBusy('选择游戏目录');
    try {
      const next = await window.bazaarManager.chooseGamePath();
      if (next) {
        setBootstrap(next);
        setToast({ tone: 'success', message: '游戏目录已更新' });
      }
    } catch (cause) {
      setToast({ tone: 'error', message: errorText(cause) });
    } finally {
      setBusy(null);
    }
  };

  const launch = () => {
    void runAction('启动游戏', () => window.bazaarManager.launchGame());
  };

  const installBuiltin = (id: BuiltinPluginId) => {
    const name = bootstrap?.builtin.find((item) => item.id === id)?.name ?? id;
    void runAction(`安装内置插件：${name}`, () => window.bazaarManager.installBuiltin(id));
  };

  const installZip = () => {
    void runAction('安装本地 ZIP', () => window.bazaarManager.installFromZip());
  };

  const installUrl = async (url: string): Promise<boolean> => {
    return runAction('从 URL 安装插件', () => window.bazaarManager.installFromUrl(url));
  };

  const uninstall = (plugin: PluginInfo) => {
    setConfirm({
      title: `卸载 ${plugin.name}？`,
      message: '插件文件会先备份到管理器数据目录，再从 BepInEx/plugins 中删除。游戏本体和 BepInEx 不会受影响。',
      detail: `${plugin.files.length} 个文件 · ${formatBytes(plugin.sizeBytes)}`,
      confirmLabel: '卸载插件',
      danger: true,
      action: async () => {
        await runAction(`卸载插件：${plugin.name}`, () =>
          window.bazaarManager.uninstallPlugin(plugin.id)
        );
      }
    });
  };

  const clean = (ids: string[]) => {
    const targets = bootstrap?.dataTargets.filter((target) => ids.includes(target.id)) ?? [];
    const bytes = targets.reduce((sum, target) => sum + target.sizeBytes, 0);
    setConfirm({
      title: '把选中的数据移入回收站？',
      message: `将清理 ${targets.length} 项本地数据。内容会进入 Windows 回收站，可以随时从系统回收站恢复。`,
      detail: `${targets.map((target) => target.name).join('、')}\n共 ${formatBytes(bytes)}`,
      confirmLabel: '移入回收站',
      danger: true,
      action: async () => {
        setBusy('正在清理本地数据…');
        try {
          const response = await window.bazaarManager.cleanData(ids);
          if (response.bootstrap) setBootstrap(response.bootstrap);
          else await refresh(true);
          setToast({
            tone: response.ok ? 'success' : 'error',
            message: response.message,
            detail: response.detail
          });
        } catch (cause) {
          setToast({ tone: 'error', message: errorText(cause) });
        } finally {
          setBusy(null);
        }
      }
    });
  };

  const updateConfig = async (patch: Partial<AppConfig>) => {
    setBusy('保存设置');
    try {
      const next = await window.bazaarManager.updateConfig(patch);
      setBootstrap(next);
      setToast({ tone: 'success', message: '设置已保存' });
    } catch (cause) {
      setToast({ tone: 'error', message: errorText(cause) });
    } finally {
      setBusy(null);
    }
  };

  const openPath = async (target: string) => {
    if (!target) {
      setToast({ tone: 'info', message: '路径尚未生成' });
      return;
    }
    try {
      const message = await window.bazaarManager.openPath(target);
      if (message) setToast({ tone: 'error', message });
    } catch (cause) {
      setToast({ tone: 'error', message: errorText(cause) });
    }
  };

  const openUserData = () => {
    void window.bazaarManager.openUserData();
  };

  const copy = (text: string) => {
    void window.bazaarManager.copyToClipboard(text);
    setToast({ tone: 'info', message: '已复制到剪贴板' });
  };

  const meta = PAGE_META[page];
  const game = bootstrap?.game;

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand">
          <span className="brand-mark" aria-hidden>
            <svg viewBox="0 0 32 32" width="26" height="26">
              <path d="M16 2l12 14-12 14L4 16z" fill="none" stroke="currentColor" strokeWidth="1.8" />
              <path d="M16 9l6 7-6 7-6-7z" fill="currentColor" opacity="0.55" />
            </svg>
          </span>
          <div>
            <strong>Bazaar 插件管理器</strong>
            <span className="muted small block">BepInEx · The Bazaar</span>
          </div>
        </div>

        <nav className="nav">
          {NAV.map((item) => (
            <button
              key={item.id}
              className={`nav-item${page === item.id ? ' active' : ''}`}
              onClick={() => setPage(item.id)}
            >
              <NavIcon page={item.id} />
              <span>{item.label}</span>
            </button>
          ))}
        </nav>

        <div className="sidebar-footer">
          <div className={`status-dot ${game?.found ? 'ok' : 'bad'}`} />
          <div className="sidebar-status">
            <strong>{game?.found ? (game.running ? '游戏运行中' : '游戏就绪') : '未找到游戏'}</strong>
            <span className="muted small block" title={game?.path ?? ''}>
              {game?.path ? shortPath(game.path, 30) : '请选择游戏目录'}
            </span>
          </div>
        </div>
        {bootstrap ? <div className="sidebar-version">v{bootstrap.appVersion}</div> : null}
      </aside>

      <div className="main-column">
        <header className="topbar">
          <div>
            <p className="eyebrow">The Bazaar · Manager</p>
            <h1>{meta.title}</h1>
            <p className="muted small">{meta.subtitle}</p>
          </div>
          <div className="topbar-actions">
            {game?.found ? (
              <Badge tone={game.running ? 'green' : 'gold'}>{game.running ? '游戏运行中' : '游戏已就绪'}</Badge>
            ) : (
              <Badge tone="red">未找到游戏</Badge>
            )}
            {game?.bepinex?.installed ? <Badge tone="green">BepInEx</Badge> : <Badge>无 BepInEx</Badge>}
            <Button small variant="subtle" onClick={() => refresh()} busy={loading}>
              刷新状态
            </Button>
          </div>
        </header>

        {busy ? (
          <div className="busy-bar">
            <span className="busy-spinner" aria-hidden />
            {busy}
          </div>
        ) : null}

        <main className="content">
          {loading && !bootstrap ? (
            <div className="loading-panel">
              <span className="busy-spinner" aria-hidden />
              <p>正在检测游戏目录与插件…</p>
            </div>
          ) : null}

          {!loading && error && !bootstrap ? (
            <div className="loading-panel">
              <h2>启动失败</h2>
              <p className="muted">{error}</p>
              <Button variant="primary" onClick={() => refresh()}>
                重试
              </Button>
            </div>
          ) : null}

          {bootstrap ? (
            <>
              {page === 'overview' ? (
                <OverviewPage
                  bootstrap={bootstrap}
                  busy={busy}
                  onDetect={detect}
                  onChoosePath={choosePath}
                  onLaunch={launch}
                  onOpenPath={openPath}
                />
              ) : null}
              {page === 'plugins' ? (
                <PluginsPage
                  bootstrap={bootstrap}
                  busy={busy}
                  onInstallBuiltin={installBuiltin}
                  onInstallZip={installZip}
                  onInstallUrl={installUrl}
                  onUninstall={uninstall}
                  onOpenPath={openPath}
                  onRefresh={() => refresh(true)}
                />
              ) : null}
              {page === 'data' ? (
                <DataPage
                  bootstrap={bootstrap}
                  busy={busy}
                  onClean={clean}
                  onOpenPath={openPath}
                  onRefresh={() => refresh(true)}
                />
              ) : null}
              {page === 'settings' ? (
                <SettingsPage
                  bootstrap={bootstrap}
                  busy={busy}
                  onChoosePath={choosePath}
                  onDetect={detect}
                  onUpdateConfig={updateConfig}
                  onOpenUserData={openUserData}
                  onOpenPath={openPath}
                  onCopy={copy}
                />
              ) : null}
            </>
          ) : null}
        </main>
      </div>

      {toast ? (
        <Toast tone={toast.tone} message={toast.message} detail={toast.detail} onClose={() => setToast(null)} />
      ) : null}

      {confirm ? (
        <Modal
          title={confirm.title}
          onClose={() => setConfirm(null)}
          footer={
            <>
              <Button onClick={() => setConfirm(null)}>取消</Button>
              <Button
                variant={confirm.danger ? 'danger' : 'primary'}
                onClick={() => {
                  const action = confirm.action;
                  setConfirm(null);
                  void action();
                }}
              >
                {confirm.confirmLabel}
              </Button>
            </>
          }
        >
          <p>{confirm.message}</p>
          {confirm.detail ? <pre className="confirm-detail">{confirm.detail}</pre> : null}
        </Modal>
      ) : null}
    </div>
  );
}
