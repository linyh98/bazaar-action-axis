import type { BattleResult } from '../engine/types';

export interface TotalsPanelProps {
  totals: BattleResult['totals'];
  endTime: number;
}

export function TotalsPanel({ totals, endTime }: TotalsPanelProps) {
  const row = (label: string, side: BattleResult['totals']['player']) => (
    <tr>
      <th scope="row">{label}</th>
      <td>{side.damage}</td>
      <td>{side.effectiveDamage}</td>
      <td>{side.heal}</td>
      <td>{side.shield}</td>
      <td>{(side.effectiveDamage / Math.max(endTime, 1)).toFixed(1)}</td>
    </tr>
  );

  return (
    <div className="panel">
      <table className="totals-table">
        <thead>
          <tr>
            <th>阵营</th>
            <th>总伤害</th>
            <th>有效伤害</th>
            <th>治疗</th>
            <th>护盾</th>
            <th>DPS</th>
          </tr>
        </thead>
        <tbody>
          {row('我方', totals.player)}
          {row('敌方', totals.enemy)}
        </tbody>
      </table>
    </div>
  );
}
