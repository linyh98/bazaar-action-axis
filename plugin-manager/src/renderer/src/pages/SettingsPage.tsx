import type { AppConfig, Bootstrap } from '@shared/types';
import { Badge, Button, Card, CardHeader, Toggle } from '../components';
import { sourceLabel } from '../format';

interface SettingsPageProps {
  bootstrap: Bootstrap;
  busy: string | null;
  onChoosePath: () => void;
  onDetect: () => void;
  onUpdateConfig: (patch: Partial<AppConfig>) => void;
  onOpenUserData: () => void;
  onOpenPath: (path: string) => void;
  onCopy: (text: string) => void;
}

export default function SettingsPage({
  bootstrap,
  busy,
  onChoosePath,
  onDetect,
  onUpdateConfig,
  onOpenUserData,
  onOpenPath,
  onCopy
}: SettingsPageProps) {
  const { config, game, userDataDir, appVersion, platform } = bootstrap;
  const bepinex = game.bepinex;

  return (
    <div className="page-stack">
      <Card>
        <CardHeader
          title="游戏目录"
          subtitle="启动时会自动从 Steam 注册表、libraryfolders.vdf 和常见目录中定位游戏。"
          actions={
            <div className="inline-actions">
              <Button small onClick={onDetect} busy={busy === '重新检测游戏目录'}>
                重新检测
              </Button>
              <Button small variant="primary" onClick={onChoosePath}>
                选择目录
              </Button>
            </div>
          }
        />
        <div className="settings-path">
          <code>{game.path ?? '尚未选择'}</code>
          {game.path ? (
            <Button small variant="subtle" onClick={() => onOpenPath(game.path as string)}>
              打开
            </Button>
          ) : null}
          {game.path ? (
            <Button small variant="subtle" onClick={() => onCopy(game.path as string)}>
              复制
            </Button>
          ) : null}
        </div>
        <div className="settings-meta">
          <Badge tone={game.found ? 'green' : 'red'}>{game.found ? '目录有效' : '未找到'}</Badge>
          <span className="muted small">来源：{sourceLabel(game.source)}</span>
          <span className="muted small">
            BepInEx：{bepinex?.installed ? `已安装${bepinex.version ? `（v${bepinex.version}）` : ''}` : '未安装'}
          </span>
        </div>
        <div className="settings-divider" />
        <Toggle
          checked={config.autoDetect}
          onChange={(next) => onUpdateConfig({ autoDetect: next })}
          label="启动时自动检测游戏目录"
          description="关闭后仍会优先使用上次记录的路径；手动选择目录不受影响。"
        />
      </Card>

      <Card>
        <CardHeader title="管理器数据" subtitle="配置、卸载备份与日志都保存在这里，不会写入游戏目录。" />
        <div className="settings-path">
          <code>{userDataDir}</code>
          <Button small variant="subtle" onClick={onOpenUserData}>
            打开
          </Button>
          <Button small variant="subtle" onClick={() => onCopy(userDataDir)}>
            复制
          </Button>
        </div>
        <ul className="plain-list">
          <li>
            <code>config.json</code>
            <span className="muted">记录的游戏路径与自动检测开关。</span>
          </li>
          <li>
            <code>backups/</code>
            <span className="muted">卸载插件前自动备份的文件，可随时手动恢复。</span>
          </li>
          <li>
            <code>logs/manager.log</code>
            <span className="muted">安装、卸载与清理操作的本地日志。</span>
          </li>
        </ul>
      </Card>

      <Card>
        <CardHeader title="内置插件来源" subtitle="管理器不会联网检查更新，只部署本机已有的构建产物。" />
        <ul className="plain-list">
          {bootstrap.builtin.map((item) => (
            <li key={item.id}>
              <strong>{item.name}</strong>
              <span className="muted">
                v{item.version} · {item.sourceNote} · {item.available ? '可用' : '缺少构建产物'}
              </span>
            </li>
          ))}
        </ul>
      </Card>

      <Card>
        <CardHeader title="关于" subtitle="本地运行的 BepInEx 插件管理器，不包含任何游戏本体文件。" />
        <dl className="detail-list compact">
          <div>
            <dt>管理器版本</dt>
            <dd>{appVersion}</dd>
          </div>
          <div>
            <dt>运行平台</dt>
            <dd>{platform === 'win32' ? 'Windows' : platform}</dd>
          </div>
          <div>
            <dt>Steam AppID</dt>
            <dd>{game.steamAppId}</dd>
          </div>
          <div>
            <dt>卸载策略</dt>
            <dd>先备份到管理器数据目录，再删除 BepInEx/plugins 中的文件。</dd>
          </div>
          <div>
            <dt>清理策略</dt>
            <dd>数据清理统一移入 Windows 回收站，不直接永久删除。</dd>
          </div>
        </dl>
      </Card>
    </div>
  );
}
