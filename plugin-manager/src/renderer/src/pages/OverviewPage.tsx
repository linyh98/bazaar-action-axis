import type { Bootstrap } from '@shared/types';
import { Badge, Button, Card, CardHeader } from '../components';
import { formatBytes, sourceLabel } from '../format';

interface OverviewPageProps {
  bootstrap: Bootstrap;
  busy: string | null;
  onDetect: () => void;
  onChoosePath: () => void;
  onLaunch: () => void;
  onOpenPath: (path: string) => void;
}

export default function OverviewPage({
  bootstrap,
  busy,
  onDetect,
  onChoosePath,
  onLaunch,
  onOpenPath
}: OverviewPageProps) {
  const { game, plugins, dataTargets, builtin } = bootstrap;
  const bepinex = game.bepinex;
  const dataBytes = dataTargets.reduce((sum, target) => sum + target.sizeBytes, 0);
  const knownCount = plugins.filter((plugin) => plugin.knownId).length;
  const builtinReady = builtin.filter((item) => item.installed).length;

  return (
    <div className="page-stack">
      <section className="hero">
        <div>
          <p className="eyebrow">The Bazaar · BepInEx 工具链</p>
          <h2>插件、数据与游戏目录，一处管理。</h2>
          <p className="muted hero-copy">
            启动时自动定位 Steam 版《The Bazaar》，管理 BepInEx 插件的安装与卸载，并把本地数据安全地移入回收站。
          </p>
        </div>
        <div className="hero-actions">
          <Button variant="primary" onClick={onLaunch} disabled={!game.found} busy={busy === '启动游戏'}>
            {game.running ? '游戏运行中' : '启动游戏'}
          </Button>
          <Button variant="ghost" onClick={onDetect} busy={busy === '重新检测游戏目录'}>
            重新检测目录
          </Button>
        </div>
      </section>

      {!game.found ? (
        <Card className="card-alert">
          <CardHeader
            title="还没有找到 The Bazaar"
            subtitle="请确认游戏已通过 Steam 安装，或手动选择包含 TheBazaar.exe 的游戏根目录。"
            actions={
              <Button variant="primary" onClick={onChoosePath}>
                选择游戏目录
              </Button>
            }
          />
          <div className="hint-grid">
            <div>
              <strong>自动检测范围</strong>
              <p className="muted">Steam 注册表、libraryfolders.vdf、所有磁盘的常见 Steam 库位置。</p>
            </div>
            <div>
              <strong>需要什么</strong>
              <p className="muted">游戏根目录下的 TheBazaar.exe，以及已安装的 BepInEx 5.x。</p>
            </div>
          </div>
        </Card>
      ) : null}

      <div className="stat-grid">
        <Card className="stat-card">
          <span className="stat-label">游戏目录</span>
          <strong className={game.found ? 'stat-value ok' : 'stat-value bad'}>
            {game.found ? '已定位' : '未找到'}
          </strong>
          <span className="muted small">{sourceLabel(game.source)}</span>
        </Card>
        <Card className="stat-card">
          <span className="stat-label">BepInEx</span>
          <strong className={bepinex?.installed ? 'stat-value ok' : 'stat-value bad'}>
            {bepinex?.installed ? `v${bepinex.version ?? '5.x'}` : '未安装'}
          </strong>
          <span className="muted small">{bepinex?.installed ? '插件加载器就绪' : '需要 BepInEx 5.x'}</span>
        </Card>
        <Card className="stat-card">
          <span className="stat-label">已安装插件</span>
          <strong className="stat-value">{plugins.length}</strong>
          <span className="muted small">
            {knownCount} 个已识别 · {builtinReady} 个内置插件
          </span>
        </Card>
        <Card className="stat-card">
          <span className="stat-label">可清理数据</span>
          <strong className="stat-value">{formatBytes(dataBytes)}</strong>
          <span className="muted small">{dataTargets.length} 个目标</span>
        </Card>
      </div>

      <div className="two-col">
        <Card>
          <CardHeader
            title="游戏与加载器"
            subtitle="路径来自 Steam 库和本地配置，可在设置中修改。"
            actions={
              <Button small onClick={onDetect} busy={busy === '重新检测游戏目录'}>
                重新检测
              </Button>
            }
          />
          <dl className="detail-list">
            <div>
              <dt>游戏目录</dt>
              <dd className="path-cell">
                {game.path ?? '未检测到'}
                {game.path ? (
                  <Button small variant="subtle" onClick={() => onOpenPath(game.path as string)}>
                    打开
                  </Button>
                ) : null}
              </dd>
            </div>
            <div>
              <dt>游戏状态</dt>
              <dd>
                {game.running ? <Badge tone="green">运行中</Badge> : <Badge>未运行</Badge>}
                {game.found ? <span className="muted small inline-gap">Steam AppID {game.steamAppId}</span> : null}
              </dd>
            </div>
            <div>
              <dt>插件目录</dt>
              <dd className="path-cell">
                {bepinex?.pluginsDir ?? '未检测到 BepInEx'}
                {bepinex?.installed ? (
                  <Button small variant="subtle" onClick={() => onOpenPath(bepinex.pluginsDir)}>
                    打开
                  </Button>
                ) : null}
              </dd>
            </div>
            <div>
              <dt>配置目录</dt>
              <dd className="path-cell">
                {bepinex?.configDir ?? '未检测到 BepInEx'}
                {bepinex?.installed ? (
                  <Button small variant="subtle" onClick={() => onOpenPath(bepinex.configDir)}>
                    打开
                  </Button>
                ) : null}
              </dd>
            </div>
          </dl>
          {!game.found || !bepinex?.installed ? (
            <div className="inline-alert">
              <strong>{game.found ? '缺少 BepInEx' : '未找到游戏目录'}</strong>
              <span className="muted">
                {game.found
                  ? '请先在游戏目录安装 BepInEx 5.x（把压缩包内容解压到游戏根目录），再回到这里刷新。'
                  : '可以先手动选择游戏目录，管理器会自动识别 BepInEx 和插件。'}
              </span>
            </div>
          ) : null}
        </Card>

        <Card>
          <CardHeader
            title="插件概览"
            subtitle="只读取 BepInEx/plugins，不修改游戏本体。"
            actions={
              <Button small variant="subtle" onClick={() => onOpenPath(bepinex?.pluginsDir ?? '')}>
                打开插件目录
              </Button>
            }
          />
          {plugins.length === 0 ? (
            <p className="muted">还没有已安装的插件。前往「插件」页部署内置插件或安装本地压缩包。</p>
          ) : (
            <ul className="plugin-summary">
              {plugins.map((plugin) => (
                <li key={plugin.id}>
                  <div>
                    <strong>{plugin.name}</strong>
                    <span className="muted small block">
                      {plugin.files.length} 个文件 · {formatBytes(plugin.sizeBytes)}
                    </span>
                  </div>
                  {plugin.knownId ? <Badge tone="gold">已识别</Badge> : <Badge>外部</Badge>}
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>
    </div>
  );
}
