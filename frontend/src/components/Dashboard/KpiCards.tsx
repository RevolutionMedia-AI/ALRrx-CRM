import type { MetricCardDto } from '../../types';

interface Props {
  metrics: MetricCardDto[];
}

export default function KpiCards({ metrics }: Props) {
  if (metrics.length === 0) {
    return (
      <div className="kpi-empty">
        No metrics available for this period
      </div>
    );
  }

  return (
    <div className="kpi-grid">
      {metrics.map((m, i) => (
        <div
          key={i}
          className="kpi-card"
          style={{
            ['--card-accent' as string]: m.color || '#0d9488',
          }}
        >
          <div className="kpi-label">{m.label}</div>
          <div className="kpi-value">{m.value}</div>
          {m.trend && (
            <div className="kpi-trend">
              <span className={Number(m.trend) >= 0 ? 'trend-up' : 'trend-down'}>
                {Number(m.trend) >= 0 ? '+' : ''}{m.trend}%
              </span>
            </div>
          )}
        </div>
      ))}
    </div>
  );
}
