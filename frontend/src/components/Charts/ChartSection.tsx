import {
  BarChart,
  Bar,
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  Legend,
  ResponsiveContainer,
  PieChart,
  Pie,
  Cell,
} from 'recharts';
import type { ChartDataDto } from '../../types';

interface Props {
  charts: ChartDataDto[];
}

const ACCENT = '#0d9488';
const ACCENT_MID = '#5eead4';
const COLORS = [ACCENT, '#06b6d4', '#10b981', '#f59e0b', '#ef4444', '#8b5cf6'];

function CustomTooltip({ active, payload, label }: any) {
  if (!active || !payload?.length) return null;
  return (
    <div style={{
      background: 'white',
      border: '1px solid #e4e7ed',
      borderRadius: 8,
      padding: '10px 14px',
      boxShadow: '0 4px 12px rgba(13,18,36,0.08)',
      fontSize: 13,
    }}>
      <div style={{ fontWeight: 600, marginBottom: 4, color: '#14171f' }}>{label}</div>
      {payload.map((p: any, i: number) => (
        <div key={i} style={{ color: p.color, fontFamily: "'JetBrains Mono', monospace" }}>
          {p.name}: {p.value}
        </div>
      ))}
    </div>
  );
}

function renderChart(chart: ChartDataDto, idx: number) {
  const data = chart.labels.map((label, i) => {
    const point: Record<string, string | number> = { name: label };
    chart.series.forEach((s) => {
      point[s.name] = s.data[i] ?? 0;
    });
    return point;
  });

  const commonProps = {
    margin: { top: 8, right: 8, bottom: 8, left: 8 },
  };

  switch (chart.chartType) {
    case 'line':
      return (
        <LineChart data={data} {...commonProps}>
          <CartesianGrid strokeDasharray="3 3" stroke="#f0f1f5" />
          <XAxis dataKey="name" fontSize={12} tick={{ fill: '#6b7485' }} axisLine={false} tickLine={false} />
          <YAxis fontSize={12} tick={{ fill: '#6b7485' }} axisLine={false} tickLine={false} />
          <Tooltip content={<CustomTooltip />} />
          <Legend wrapperStyle={{ fontSize: 12, color: '#6b7485' }} />
          {chart.series.map((s, i) => (
            <Line
              key={s.name}
              type="monotone"
              dataKey={s.name}
              stroke={COLORS[i % COLORS.length]}
              strokeWidth={2}
              dot={false}
              activeDot={{ r: 4, fill: ACCENT }}
            />
          ))}
        </LineChart>
      );

    case 'pie':
      return (
        <PieChart {...commonProps}>
          <Pie
            data={data}
            dataKey={chart.series[0]?.name || 'value'}
            nameKey="name"
            cx="50%"
            cy="50%"
            innerRadius={60}
            outerRadius={110}
            paddingAngle={3}
            strokeWidth={0}
          >
            {data.map((_, i) => (
              <Cell key={i} fill={COLORS[i % COLORS.length]} />
            ))}
          </Pie>
          <Tooltip content={<CustomTooltip />} />
          <Legend wrapperStyle={{ fontSize: 12, color: '#6b7485' }} />
        </PieChart>
      );

    default:
      return (
        <BarChart data={data} {...commonProps}>
          <CartesianGrid strokeDasharray="3 3" stroke="#f0f1f5" />
          <XAxis dataKey="name" fontSize={12} tick={{ fill: '#6b7485' }} axisLine={false} tickLine={false} />
          <YAxis fontSize={12} tick={{ fill: '#6b7485' }} axisLine={false} tickLine={false} />
          <Tooltip content={<CustomTooltip />} />
          <Legend wrapperStyle={{ fontSize: 12, color: '#6b7485' }} />
          {chart.series.map((s, i) => (
            <Bar
              key={s.name}
              dataKey={s.name}
              fill={COLORS[i % COLORS.length]}
              radius={[4, 4, 0, 0]}
              maxBarSize={48}
            />
          ))}
        </BarChart>
      );
  }
}

export default function ChartSection({ charts }: Props) {
  if (charts.length === 0) return null;

  return (
    <div className="chart-grid">
      {charts.map((chart, i) => (
        <div key={i} className="chart-card">
          <h3 className="chart-title">{chart.title}</h3>
          <ResponsiveContainer width="100%" height={300}>
            {renderChart(chart, i)}
          </ResponsiveContainer>
        </div>
      ))}
    </div>
  );
}
