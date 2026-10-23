import { useMemo, useState } from 'react';
import type { Bootstrap, DataGroup, DataTarget } from '@shared/types';
import { Badge, Button, Card, EmptyState } from '../components';
import { formatBytes, formatCount } from '../format';

interface DataPageProps {
  bootstrap: Bootstrap;
  busy: string | null;
  onClean: (ids: string[]) => void;
  onOpenPath: (path: string) => void;
  onRefresh: () => void;
}

const GROUPS: { id: DataGroup; title: string; description: string }[] = [
  {
    id: 'bpp',
    title: 'BazaarPlusPlus 数据',
    description: 'BPP 的对局历史、回放数据库与官方安装器状态。删除后 BPP 会重新建立空数据。'
  },
  {
    id: 'actionaxis',
    title: '行动轴数据',
    description: '游戏内行动轴保存的快照、技能 / 怪物报告与卡图缓存，都会在下次使用时重新生成。'
  },
  {
    id: 'livecards',
    title: 'LiveCardsDump 输出',
    description: '插件以及导入脚本写出的卡牌快照 JSON，删除后游戏运行时会自动重建。'
  },
  {
    id: 'bepinex',
    title: 'BepInEx 配置',
    description: '本仓库插件与 BPP 写入 BepInEx/config 的 cfg 配置。删除后插件恢复默认设置。'
  }
];

function groupTargets(targets: DataTarget[], group: DataGroup): DataTarget[] {
  return targets.filter((target) => target.group === group);
}

export default function DataPage({ bootstrap, busy, onClean, onOpenPath, onRefresh }: DataPageProps) {
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const { dataTargets, game } = bootstrap;

  const validIds = useMemo(() => new Set(dataTargets.map((target) => target.id)), [dataTargets]);
  const effective = useMemo(
    () => new Set([...selected].filter((id) => validIds.has(id))),
    [selected, validIds]
  );
  const selectedTargets = dataTargets.filter((target) => effective.has(target.id));
  const selectedBytes = selectedTargets.reduce((sum, target) => sum + target.sizeBytes, 0);
  const totalBytes = dataTargets.reduce((sum, target) => sum + target.sizeBytes, 0);

  const toggle = (id: string) => {
    setSelected((current) => {
      const next = new Set([...current].filter((item) => validIds.has(item)));
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const toggleGroup = (group: DataGroup, checked: boolean) => {
    const ids = groupTargets(dataTargets, group)
      .filter((target) => !(game.running && target.requiresGameStopped))
      .map((target) => target.id);
    setSelected((current) => {
      const next = new Set([...current].filter((item) => validIds.has(item)));
      for (const id of ids) {
        if (checked) next.add(id);
        else next.delete(id);
      }
      return next;
    });
  };

  return (
    <div className="page-stack data-page">
      <div className="toolbar">
        <div>
          <p className="muted">
            共 {dataTargets.length} 个目标 · {formatBytes(totalBytes)}。清理会把内容移入回收站，可在 Windows
            中恢复。
          </p>
        </div>
        <div className="toolbar-actions">
          <Button variant="subtle" onClick={onRefresh} busy={busy === '刷新数据列表'}>
            重新扫描
          </Button>
        </div>
      </div>

      {game.running ? (
        <Card className="card-warning">
          <strong>游戏正在运行</strong>
          <p className="muted">
            游戏目录内的数据（BPP 数据、行动轴快照与卡图）已被禁用，关闭游戏后刷新即可清理。配置文件和
            LiveCardsDump 输出仍可清理。
          </p>
        </Card>
      ) : null}

      {dataTargets.length === 0 ? (
        <EmptyState
          title="没有发现可清理的数据"
          description="当前没有 BPP 数据目录、行动轴快照或插件配置。安装并使用插件后再回来看看。"
          action={
            <Button variant="subtle" onClick={onRefresh}>
              重新扫描
            </Button>
          }
        />
      ) : (
        GROUPS.map((group) => {
          const targets = groupTargets(dataTargets, group.id);
          if (targets.length === 0) return null;
          const groupBytes = targets.reduce((sum, target) => sum + target.sizeBytes, 0);
          const selectable = targets.filter((target) => !(game.running && target.requiresGameStopped));
          const allSelected =
            selectable.length > 0 && selectable.every((target) => effective.has(target.id));

          return (
            <Card key={group.id} className="data-group">
              <div className="data-group-header">
                <div>
                  <h3>{group.title}</h3>
                  <p className="muted small">{group.description}</p>
                </div>
                <div className="data-group-meta">
                  <span className="muted small">
                    {targets.length} 项 · {formatBytes(groupBytes)}
                  </span>
                  <Button
                    small
                    variant="subtle"
                    disabled={selectable.length === 0}
                    onClick={() => toggleGroup(group.id, !allSelected)}
                  >
                    {allSelected ? '取消全选' : '全选'}
                  </Button>
                </div>
              </div>

              <div className="data-list">
                {targets.map((target) => {
                  const disabled = Boolean(busy) || (game.running && target.requiresGameStopped);
                  const checked = effective.has(target.id);
                  return (
                    <label key={target.id} className={`data-row${disabled ? ' disabled' : ''}`}>
                      <input
                        type="checkbox"
                        checked={checked}
                        disabled={disabled}
                        onChange={() => toggle(target.id)}
                      />
                      <div className="data-row-main">
                        <div className="data-row-title">
                          <strong>{target.name}</strong>
                          {target.requiresGameStopped ? <Badge>需关闭游戏</Badge> : null}
                        </div>
                        <p className="muted small">{target.description}</p>
                        <button
                          type="button"
                          className="path-link"
                          title={target.path}
                          onClick={(event) => {
                            event.preventDefault();
                            onOpenPath(target.path);
                          }}
                        >
                          {target.path}
                        </button>
                      </div>
                      <div className="data-row-size">
                        <strong>{formatBytes(target.sizeBytes)}</strong>
                        <span className="muted small">
                          {target.kind === 'dir' ? `${formatCount(target.itemCount)} 项` : '单个文件'}
                        </span>
                      </div>
                    </label>
                  );
                })}
              </div>
            </Card>
          );
        })
      )}

      <div className="selection-bar">
        <div>
          <strong>已选 {selectedTargets.length} 项</strong>
          <span className="muted"> · {formatBytes(selectedBytes)}</span>
        </div>
        <div className="selection-actions">
          <Button variant="subtle" disabled={selectedTargets.length === 0} onClick={() => setSelected(new Set())}>
            清空选择
          </Button>
          <Button
            variant="danger"
            disabled={selectedTargets.length === 0 || Boolean(busy)}
            onClick={() => onClean(selectedTargets.map((target) => target.id))}
          >
            移入回收站
          </Button>
        </div>
      </div>
    </div>
  );
}
