import { useState } from 'react';
import type { Bootstrap, BuiltinPluginId, PluginInfo } from '@shared/types';
import { Badge, Button, Card, EmptyState } from '../components';
import { formatBytes, formatDate } from '../format';

interface PluginsPageProps {
  bootstrap: Bootstrap;
  busy: string | null;
  onInstallBuiltin: (id: BuiltinPluginId) => void;
  onInstallZip: () => void;
  onInstallUrl: (url: string) => Promise<boolean>;
  onUninstall: (plugin: PluginInfo) => void;
  onOpenPath: (path: string) => void;
  onRefresh: () => void;
}

const BUILTIN_BADGE: Record<BuiltinPluginId, string> = {
  actionaxis: 'AX',
  livecardsdump: 'LC'
};

export default function PluginsPage({
  bootstrap,
  busy,
  onInstallBuiltin,
  onInstallZip,
  onInstallUrl,
  onUninstall,
  onOpenPath,
  onRefresh
}: PluginsPageProps) {
  const [showUrl, setShowUrl] = useState(false);
  const [url, setUrl] = useState('');
  const [submitting, setSubmitting] = useState(false);

  const { game, plugins, builtin } = bootstrap;
  const pluginsDir = game.bepinex?.pluginsDir ?? null;
  const ready = Boolean(game.found && game.bepinex?.installed);
  const disabled = Boolean(busy) || !ready;

  const submitUrl = async () => {
    const value = url.trim();
    if (!value || submitting) return;
    setSubmitting(true);
    const ok = await onInstallUrl(value);
    setSubmitting(false);
    if (ok) {
      setUrl('');
      setShowUrl(false);
    }
  };

  return (
    <div className="page-stack">
      <div className="toolbar">
        <div>
          <p className="muted">
            {pluginsDir ? (
              <>
                安装位置 <code>{pluginsDir}</code>
              </>
            ) : (
              '尚未检测到 BepInEx 插件目录'
            )}
          </p>
        </div>
        <div className="toolbar-actions">
          <Button variant="primary" onClick={onInstallZip} disabled={disabled}>
            安装本地 ZIP
          </Button>
          <Button onClick={() => setShowUrl((value) => !value)} disabled={disabled}>
            从 URL 安装
          </Button>
          <Button variant="subtle" onClick={onRefresh} busy={busy === '刷新插件列表'}>
            刷新
          </Button>
        </div>
      </div>

      {showUrl ? (
        <Card className="url-form">
          <form
            onSubmit={(event) => {
              event.preventDefault();
              void submitUrl();
            }}
          >
            <label htmlFor="plugin-url">插件下载地址</label>
            <div className="url-row">
              <input
                id="plugin-url"
                type="url"
                placeholder="https://example.com/MyPlugin.zip"
                value={url}
                onChange={(event) => setUrl(event.target.value)}
                autoFocus
              />
              <Button type="submit" variant="primary" busy={submitting} disabled={!url.trim() || disabled}>
                下载并安装
              </Button>
            </div>
            <p className="muted small">
              支持 `.zip` 插件包与单个 `.dll` 直链，最大 200 MB。压缩包内可以是 `BepInEx/plugins/...` 结构，
              也可以是单个插件文件夹。
            </p>
          </form>
        </Card>
      ) : null}

      {!ready ? (
        <Card className="card-alert">
          <strong>{game.found ? '需要先安装 BepInEx' : '还没有定位到游戏目录'}</strong>
          <p className="muted">
            {game.found
              ? '把 BepInEx 5.x 解压到游戏根目录后再回来安装插件。管理器只负责 BepInEx/plugins 下的文件。'
              : '前往「设置」手动选择包含 TheBazaar.exe 的游戏目录。'}
          </p>
        </Card>
      ) : null}

      <section className="section-block">
        <div className="section-heading">
          <h3>内置插件</h3>
          <span className="muted small">来自本仓库的构建产物，可一键部署或覆盖更新</span>
        </div>
        <div className="builtin-grid">
          {builtin.map((item) => (
            <Card key={item.id} className={`builtin-card${item.installed ? ' installed' : ''}`}>
              <div className="builtin-top">
                <span className="builtin-mark">{BUILTIN_BADGE[item.id]}</span>
                <div className="builtin-title">
                  <h4>{item.name}</h4>
                  <span className="muted small">v{item.version}</span>
                </div>
                {item.installed ? <Badge tone="green">已安装</Badge> : <Badge>未安装</Badge>}
              </div>
              <p className="muted builtin-copy">{item.description}</p>
              <div className="builtin-meta">
                <span>{item.sourceNote}</span>
                {item.installed ? (
                  <span>
                    {item.files} 个文件 · {formatBytes(item.sizeBytes)}
                  </span>
                ) : (
                  <span>{item.available ? '构建产物就绪' : '缺少构建产物'}</span>
                )}
              </div>
              <div className="builtin-actions">
                <Button
                  variant={item.installed ? 'ghost' : 'primary'}
                  disabled={!ready || !item.available}
                  busy={busy === `安装内置插件：${item.name}`}
                  onClick={() => onInstallBuiltin(item.id)}
                >
                  {item.installed ? '重新部署' : '安装'}
                </Button>
                {!item.available ? (
                  <span className="muted small">
                    先运行构建脚本生成 {item.id === 'actionaxis' ? 'ActionAxis' : 'LiveCardsDump'} DLL
                  </span>
                ) : null}
              </div>
            </Card>
          ))}
        </div>
      </section>

      <section className="section-block">
        <div className="section-heading">
          <h3>已安装插件</h3>
          <span className="muted small">
            共 {plugins.length} 个 · 卸载会先备份到管理器数据目录
          </span>
        </div>

        {plugins.length === 0 ? (
          <EmptyState
            title="BepInEx/plugins 还是空的"
            description="可以部署上面的内置插件，或安装一个本地 ZIP 插件包。"
            action={
              <Button variant="primary" onClick={onInstallZip} disabled={disabled}>
                安装本地 ZIP
              </Button>
            }
          />
        ) : (
          <div className="plugin-list">
            {plugins.map((plugin) => (
              <Card key={plugin.id} className="plugin-row">
                <div className="plugin-main">
                  <div className="plugin-name">
                    <h4>{plugin.name}</h4>
                    {plugin.knownId ? <Badge tone="gold">已识别</Badge> : <Badge>外部插件</Badge>}
                  </div>
                  <p className="muted small">{plugin.description}</p>
                  <div className="plugin-meta">
                    <span>{plugin.files.length} 个文件</span>
                    <span>{formatBytes(plugin.sizeBytes)}</span>
                    <span>更新于 {formatDate(plugin.modifiedAt)}</span>
                    {plugin.dirs.length > 0 ? <span>目录：{plugin.dirs.join('、')}</span> : null}
                  </div>
                </div>
                <div className="plugin-actions">
                  {pluginsDir ? (
                    <Button small variant="subtle" onClick={() => onOpenPath(pluginsDir)}>
                      打开位置
                    </Button>
                  ) : null}
                  <Button
                    small
                    variant="danger"
                    disabled={Boolean(busy) || game.running}
                    onClick={() => onUninstall(plugin)}
                  >
                    卸载
                  </Button>
                </div>
              </Card>
            ))}
          </div>
        )}
      </section>
    </div>
  );
}
