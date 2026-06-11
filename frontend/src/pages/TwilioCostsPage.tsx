import { useEffect, useState, useCallback } from 'react';
import {
  AreaChart,
  Area,
  BarChart,
  Bar,
  XAxis,
  YAxis,
  Tooltip,
  ResponsiveContainer,
  CartesianGrid,
} from 'recharts';
import { twilioApi, type TwilioSummary, type TwilioCall, type TwilioDailyCost } from '../services/twilioApi';

type Period = 'today' | 'week' | 'month';

export default function TwilioCostsPage() {
  const [period, setPeriod] = useState<Period>('today');
  const [summary, setSummary] = useState<TwilioSummary | null>(null);
  const [calls, setCalls] = useState<TwilioCall[]>([]);
  const [daily, setDaily] = useState<TwilioDailyCost[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [autoRefresh, setAutoRefresh] = useState(true);
  const [lastRefresh, setLastRefresh] = useState<Date>(new Date());

  const loadData = useCallback(async () => {
    try {
      setError(null);
      const [sum, recent, dailyData] = await Promise.all([
        twilioApi.getSummary(period),
        twilioApi.getRecentCalls(20),
        twilioApi.getDailyCosts(30),
      ]);
      setSummary(sum);
      setCalls(recent);
      setDaily(dailyData);
      setLastRefresh(new Date());
    } catch (err: any) {
      const msg = err?.response?.data?.error || err?.message || 'Error cargando datos de Twilio';
      setError(msg);
    } finally {
      setLoading(false);
    }
  }, [period]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  useEffect(() => {
    if (!autoRefresh) return;
    const id = setInterval(loadData, 30_000);
    return () => clearInterval(id);
  }, [autoRefresh, loadData]);

  const fmt = (n: number) =>
    n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 4 });

  const fmtDuration = (s: number) => {
    const m = Math.floor(s / 60);
    const sec = s % 60;
    return `${m}:${sec.toString().padStart(2, '0')}`;
  };

  if (loading && !summary) {
    return (
      <div className="p-8 flex items-center justify-center min-h-screen">
        <div className="text-gray-500">Cargando costos de Twilio...</div>
      </div>
    );
  }

  return (
    <div className="p-6 max-w-7xl mx-auto">
      <div className="flex items-center justify-between mb-6 flex-wrap gap-3">
        <div>
          <h1 className="text-3xl font-bold text-gray-900 dark:text-white">Twilio · Costos en tiempo real</h1>
          <p className="text-sm text-gray-500 mt-1">
            Última actualización: {lastRefresh.toLocaleTimeString()}
            {autoRefresh && <span className="ml-2 text-green-600">● Auto-refresh 30s</span>}
          </p>
        </div>
        <div className="flex gap-2 items-center flex-wrap">
          {(['today', 'week', 'month'] as Period[]).map((p) => (
            <button
              key={p}
              onClick={() => setPeriod(p)}
              className={`px-4 py-2 rounded-lg text-sm font-medium transition ${
                period === p
                  ? 'bg-indigo-600 text-white'
                  : 'bg-gray-100 dark:bg-gray-800 text-gray-700 dark:text-gray-300 hover:bg-gray-200'
              }`}
            >
              {p === 'today' ? 'Hoy' : p === 'week' ? 'Semana' : 'Mes'}
            </button>
          ))}
          <button
            onClick={() => setAutoRefresh(!autoRefresh)}
            className={`px-3 py-2 rounded-lg text-sm ${
              autoRefresh ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-500'
            }`}
          >
            {autoRefresh ? '⏸ Pausar' : '▶ Reanudar'}
          </button>
          <button
            onClick={loadData}
            className="px-3 py-2 bg-indigo-100 text-indigo-700 rounded-lg text-sm hover:bg-indigo-200"
          >
            ↻ Refrescar
          </button>
        </div>
      </div>

      {error && (
        <div className="mb-4 p-4 bg-red-50 border border-red-200 text-red-700 rounded-lg">
          {error}
        </div>
      )}

      {summary && (
        <div className="grid grid-cols-1 md:grid-cols-4 gap-4 mb-6">
          <div className="bg-gradient-to-br from-indigo-500 to-indigo-600 text-white p-5 rounded-xl shadow">
            <div className="text-sm opacity-80">Costo total</div>
            <div className="text-3xl font-bold mt-2">${fmt(summary.totalCost)}</div>
            <div className="text-xs opacity-70 mt-1">{summary.currency}</div>
          </div>
          <div className="bg-white dark:bg-gray-800 p-5 rounded-xl shadow">
            <div className="text-sm text-gray-500">Llamadas</div>
            <div className="text-3xl font-bold mt-2 text-gray-900 dark:text-white">
              {summary.totalCalls}
            </div>
            <div className="text-xs text-gray-400 mt-1">
              {summary.inboundCalls} ent. / {summary.outboundCalls} sal.
            </div>
          </div>
          <div className="bg-white dark:bg-gray-800 p-5 rounded-xl shadow">
            <div className="text-sm text-gray-500">Minutos totales</div>
            <div className="text-3xl font-bold mt-2 text-gray-900 dark:text-white">
              {summary.totalMinutes}
            </div>
            <div className="text-xs text-gray-400 mt-1">
              ~{summary.totalCalls > 0 ? Math.round(summary.totalMinutes / summary.totalCalls) : 0}{' '}
              min/llamada
            </div>
          </div>
          <div className="bg-white dark:bg-gray-800 p-5 rounded-xl shadow">
            <div className="text-sm text-gray-500">Costo por minuto</div>
            <div className="text-3xl font-bold mt-2 text-gray-900 dark:text-white">
              ${summary.totalMinutes > 0 ? fmt(summary.totalCost / summary.totalMinutes) : '0.00'}
            </div>
            <div className="text-xs text-gray-400 mt-1">promedio</div>
          </div>
        </div>
      )}

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 mb-6">
        <div className="bg-white dark:bg-gray-800 p-5 rounded-xl shadow">
          <h2 className="text-lg font-semibold mb-4 text-gray-900 dark:text-white">
            Costo diario (últimos 30 días)
          </h2>
          <ResponsiveContainer width="100%" height={250}>
            <AreaChart data={daily}>
              <defs>
                <linearGradient id="costGradient" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stopColor="#4f46e5" stopOpacity={0.6} />
                  <stop offset="100%" stopColor="#4f46e5" stopOpacity={0} />
                </linearGradient>
              </defs>
              <CartesianGrid strokeDasharray="3 3" stroke="#e5e7eb" />
              <XAxis
                dataKey="date"
                tick={{ fontSize: 11 }}
                tickFormatter={(d) =>
                  new Date(d).toLocaleDateString('es', { day: '2-digit', month: 'short' })
                }
              />
              <YAxis tick={{ fontSize: 11 }} tickFormatter={(v) => `$${v.toFixed(2)}`} />
              <Tooltip formatter={(v: number) => `$${v.toFixed(4)}`} />
              <Area type="monotone" dataKey="cost" stroke="#4f46e5" fill="url(#costGradient)" />
            </AreaChart>
          </ResponsiveContainer>
        </div>

        <div className="bg-white dark:bg-gray-800 p-5 rounded-xl shadow">
          <h2 className="text-lg font-semibold mb-4 text-gray-900 dark:text-white">
            Llamadas por día
          </h2>
          <ResponsiveContainer width="100%" height={250}>
            <BarChart data={daily}>
              <CartesianGrid strokeDasharray="3 3" stroke="#e5e7eb" />
              <XAxis
                dataKey="date"
                tick={{ fontSize: 11 }}
                tickFormatter={(d) =>
                  new Date(d).toLocaleDateString('es', { day: '2-digit', month: 'short' })
                }
              />
              <YAxis tick={{ fontSize: 11 }} />
              <Tooltip />
              <Bar dataKey="callCount" fill="#10b981" />
            </BarChart>
          </ResponsiveContainer>
        </div>
      </div>

      <div className="bg-white dark:bg-gray-800 rounded-xl shadow overflow-hidden">
        <div className="px-5 py-4 border-b border-gray-200 dark:border-gray-700">
          <h2 className="text-lg font-semibold text-gray-900 dark:text-white">Últimas llamadas</h2>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="bg-gray-50 dark:bg-gray-900">
              <tr>
                <th className="px-4 py-3 text-left text-gray-600">Fecha</th>
                <th className="px-4 py-3 text-left text-gray-600">Dirección</th>
                <th className="px-4 py-3 text-left text-gray-600">From</th>
                <th className="px-4 py-3 text-left text-gray-600">To</th>
                <th className="px-4 py-3 text-left text-gray-600">Estado</th>
                <th className="px-4 py-3 text-right text-gray-600">Duración</th>
                <th className="px-4 py-3 text-right text-gray-600">Costo</th>
              </tr>
            </thead>
            <tbody>
              {calls.map((c) => (
                <tr
                  key={c.sid}
                  className="border-t border-gray-100 dark:border-gray-700 hover:bg-gray-50 dark:hover:bg-gray-900"
                >
                  <td className="px-4 py-3 text-gray-700 dark:text-gray-300">
                    {new Date(c.startTime).toLocaleString('es', {
                      hour: '2-digit',
                      minute: '2-digit',
                      day: '2-digit',
                      month: 'short',
                    })}
                  </td>
                  <td className="px-4 py-3">
                    <span
                      className={`px-2 py-1 rounded-full text-xs font-medium ${
                        c.direction === 'inbound'
                          ? 'bg-blue-100 text-blue-700'
                          : 'bg-purple-100 text-purple-700'
                      }`}
                    >
                      {c.direction === 'inbound' ? '↙ Entrante' : '↗ Saliente'}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-gray-700 dark:text-gray-300 font-mono text-xs">
                    {c.from}
                  </td>
                  <td className="px-4 py-3 text-gray-700 dark:text-gray-300 font-mono text-xs">
                    {c.to}
                  </td>
                  <td className="px-4 py-3">
                    <span
                      className={`px-2 py-1 rounded text-xs ${
                        c.status === 'completed'
                          ? 'bg-green-100 text-green-700'
                          : c.status === 'no-answer'
                          ? 'bg-yellow-100 text-yellow-700'
                          : 'bg-gray-100 text-gray-700'
                      }`}
                    >
                      {c.status}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-right text-gray-700 dark:text-gray-300">
                    {fmtDuration(c.durationSeconds)}
                  </td>
                  <td className="px-4 py-3 text-right font-semibold text-gray-900 dark:text-white">
                    ${fmt(c.cost)} <span className="text-xs text-gray-400">{c.currency}</span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
