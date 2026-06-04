import { useEffect, useMemo, useState } from 'react';
import { useAuth } from '../../context/AuthContext';
import { listAllVicidialSales, updateVicidialSale, type VicidialSaleUpdatePayload } from '../../services/vicidialFormApi';
import { BUNDLE_OPTIONS, type BundleOption, type VicidialSaleDto } from '../../types';
import { extractErrorMessage } from '../../utils/extractErrorMessage';

type Period = 'Today' | 'Week' | 'Month' | 'All' | 'Custom';
type PagePeriod = 'Today' | 'Week' | 'Month' | 'Custom';

interface VicidialSalesSectionProps {
  refreshKey?: number;
  pagePeriod?: PagePeriod;
  pageCustomStart?: string;
  pageCustomEnd?: string;
}

const ALLOWED_EDIT_EMAILS = new Set<string>([
  'jessica.duarte@revolutionmedia.ai',
  'silverio.arellano@revolutionmedia.ai',
  'kevin.escalante@revolutionmedia.ai',
]);

function getPeriodRange(period: Period, customStart: string, customEnd: string): { from: string; to: string } {
  const now = new Date();
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  if (period === 'Today') {
    const end = new Date(today.getTime() + 24 * 60 * 60 * 1000);
    return { from: today.toISOString(), to: end.toISOString() };
  }
  if (period === 'Week') {
    const dayOfWeek = today.getDay();
    const start = new Date(today.getTime() - dayOfWeek * 24 * 60 * 60 * 1000);
    const end = new Date(today.getTime() + 24 * 60 * 60 * 1000);
    return { from: start.toISOString(), to: end.toISOString() };
  }
  if (period === 'Month') {
    const start = new Date(now.getFullYear(), now.getMonth(), 1);
    const end = new Date(now.getFullYear(), now.getMonth() + 1, 1);
    return { from: start.toISOString(), to: end.toISOString() };
  }
  if (period === 'All') {
    return { from: '', to: '' };
  }
  return {
    from: new Date(`${customStart}T00:00:00`).toISOString(),
    to: new Date(`${customEnd}T23:59:59`).toISOString(),
  };
}

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat('en-US', {
    style: 'currency', currency: 'USD', minimumFractionDigits: 2, maximumFractionDigits: 2,
  }).format(amount);
}

function formatDate(iso: string): string {
  if (!iso) return '--';
  try {
    return new Date(iso).toLocaleDateString('en-US', { month: 'short', day: '2-digit', year: 'numeric' });
  } catch {
    return '--';
  }
}

function toLocalDateTimeInputValue(iso: string): string {
  if (!iso) return '';
  try {
    const d = new Date(iso);
    const tzOffset = d.getTimezoneOffset() * 60000;
    return new Date(d.getTime() - tzOffset).toISOString().slice(0, 16);
  } catch {
    return '';
  }
}

export default function VicidialSalesSection({ refreshKey = 0, pagePeriod, pageCustomStart, pageCustomEnd }: VicidialSalesSectionProps) {
  const { user } = useAuth();
  const canEdit = !!user && ALLOWED_EDIT_EMAILS.has(user.email);

  const [period, setPeriod] = useState<Period>('All');
  const [customStart, setCustomStart] = useState(() => {
    const d = new Date();
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  });
  const [customEnd, setCustomEnd] = useState(() => {
    const d = new Date();
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  });
  const [sales, setSales] = useState<VicidialSaleDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [repFilter, setRepFilter] = useState<string>('all');
  const [refreshNonce, setRefreshNonce] = useState(0);
  const [followPage, setFollowPage] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [editForm, setEditForm] = useState<VicidialSaleUpdatePayload>({});
  const [savingEdit, setSavingEdit] = useState(false);
  const [editError, setEditError] = useState<string | null>(null);

  useEffect(() => {
    if (!followPage || !pagePeriod) return;
    setPeriod(pagePeriod);
  }, [followPage, pagePeriod]);

  useEffect(() => {
    if (followPage && pageCustomStart) setCustomStart(pageCustomStart);
  }, [followPage, pageCustomStart]);

  useEffect(() => {
    if (followPage && pageCustomEnd) setCustomEnd(pageCustomEnd);
  }, [followPage, pageCustomEnd]);

  const range = useMemo(() => getPeriodRange(period, customStart, customEnd), [period, customStart, customEnd]);

  const loadSales = async (cancelled: { value: boolean }) => {
    setLoading(true);
    setError(null);
    try {
      const data = await listAllVicidialSales(range.from || undefined, range.to || undefined, 500);
      if (!cancelled.value) setSales(data);
    } catch (err: unknown) {
      if (!cancelled.value) {
        setError(extractErrorMessage(err, 'Could not load Vicidial sales'));
      }
    } finally {
      if (!cancelled.value) setLoading(false);
    }
  };

  useEffect(() => {
    const cancelled = { value: false };
    loadSales(cancelled);
    return () => { cancelled.value = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [range.from, range.to, refreshKey, refreshNonce]);

  const totalCount = sales.length;
  const totalAmount = sales.reduce((sum, s) => sum + Number(s.amount), 0);
  const uniqueAgents = new Set(sales.map((s) => s.salesRep)).size;

  const reps = useMemo(() => {
    const set = new Set(sales.map((s) => s.salesRep).filter(Boolean));
    return Array.from(set).sort((a, b) => a.localeCompare(b));
  }, [sales]);

  const filteredSales = useMemo(() => {
    if (repFilter === 'all') return sales;
    return sales.filter((s) => s.salesRep === repFilter);
  }, [sales, repFilter]);

  const startEdit = (sale: VicidialSaleDto) => {
    setEditingId(sale.id);
    setEditForm({
      saleDate: sale.saleDate,
      clientPhone: sale.clientPhone,
      clientName: sale.clientName,
      clientEmail: sale.clientEmail,
      bundle: sale.bundle,
      amount: sale.amount,
    });
    setEditError(null);
  };

  const cancelEdit = () => {
    setEditingId(null);
    setEditForm({});
    setEditError(null);
  };

  const saveEdit = async () => {
    if (!editingId || !user) return;
    setSavingEdit(true);
    setEditError(null);
    try {
      await updateVicidialSale(editingId, { ...editForm, editorEmail: user.email });
      cancelEdit();
      setRefreshNonce((n) => n + 1);
    } catch (err: unknown) {
      setEditError(extractErrorMessage(err, 'Could not update the sale'));
    } finally {
      setSavingEdit(false);
    }
  };

  return (
    <section
      className="bg-pure-surface dark:bg-gray-900 border border-card-border dark:border-gray-700 rounded-lg shadow-card"
      style={{
        opacity: 0,
        transform: 'translateY(12px)',
        animation: 'fadeSlideIn 0.6s cubic-bezier(0.16, 1, 0.3, 1) forwards',
      }}
    >
      <header className="p-6 border-b border-whisper-border dark:border-gray-700 flex flex-col gap-3">
        <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-3">
          <div>
            <h3 className="font-bold text-lg text-primary dark:text-gray-100">Vicidial Form Sales</h3>
            <p className="text-xs text-secondary dark:text-gray-400 mt-0.5">
              All sales submitted via the standalone form
              {canEdit && <span className="ml-2 text-emerald-signal font-semibold">• You can edit rows</span>}
            </p>
          </div>
          <div className="flex items-center gap-2 flex-wrap">
            {reps.length > 0 && (
              <div className="flex items-center gap-2">
                <label className="text-xs text-secondary dark:text-gray-400">Agent:</label>
                <select
                  value={repFilter}
                  onChange={(e) => setRepFilter(e.target.value)}
                  className="text-xs px-2 py-1 border border-whisper-border dark:border-gray-700 rounded bg-pure-surface dark:bg-gray-800 text-primary dark:text-gray-100 focus:border-electric-blue focus:outline-none"
                >
                  <option value="all">All agents ({reps.length})</option>
                  {reps.map((r) => (
                    <option key={r} value={r}>{r}</option>
                  ))}
                </select>
              </div>
            )}
            <button
              onClick={() => setRefreshNonce((n) => n + 1)}
              disabled={loading}
              className="text-xs px-2.5 py-1 border border-whisper-border dark:border-gray-700 rounded text-secondary dark:text-gray-300 hover:text-primary dark:hover:text-gray-100 hover:bg-surface-container-low dark:hover:bg-gray-800 transition-colors flex items-center gap-1.5 disabled:opacity-50 disabled:cursor-not-allowed"
              title="Refresh sales"
            >
              <span className={`material-symbols-outlined text-sm ${loading ? 'animate-spin' : ''}`}>sync</span>
              <span>Refresh</span>
            </button>
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <span
            className={`text-[10px] font-metadata-mono uppercase tracking-wider px-2 py-0.5 rounded ${
              followPage
                ? 'bg-electric-blue/10 text-electric-blue'
                : 'bg-amber-warmth/15 text-amber-warmth'
            }`}
            title={followPage ? `Mirroring page period: ${pagePeriod ?? '—'}` : 'Independent of page period'}
          >
            {followPage ? `Following page: ${pagePeriod ?? '—'}` : 'Independent'}
          </span>
          <button
            onClick={() => {
              if (followPage) {
                setFollowPage(false);
                return;
              }
              if (pagePeriod) setPeriod(pagePeriod);
              if (pageCustomStart) setCustomStart(pageCustomStart);
              if (pageCustomEnd) setCustomEnd(pageCustomEnd);
              setFollowPage(true);
            }}
            disabled={!pagePeriod}
            className={`text-[10px] font-metadata-mono uppercase tracking-wider px-2.5 py-1 rounded border transition-colors flex items-center gap-1 ${
              followPage
                ? 'border-electric-blue/40 bg-electric-blue/5 text-electric-blue'
                : 'border-whisper-border text-secondary hover:text-primary hover:border-electric-blue/40'
            } disabled:opacity-50 disabled:cursor-not-allowed`}
            title={followPage ? 'Stop mirroring the page period' : 'Mirror the page period selector'}
          >
            <span className="material-symbols-outlined text-[12px]">{followPage ? 'link_off' : 'link'}</span>
            {followPage ? 'Unlink from page' : 'Follow page period'}
          </button>
          <div className="bg-surface-container-low border border-whisper-border rounded flex text-xs overflow-hidden">
            {(['Today', 'Week', 'Month', 'All', 'Custom'] as Period[]).map((p) => (
              <button
                key={p}
                onClick={() => { setFollowPage(false); setPeriod(p); }}
                className={`px-3 py-1 border-r border-whisper-border last:border-r-0 transition-colors ${
                  period === p
                    ? 'bg-pure-surface text-primary font-medium'
                    : 'text-secondary hover:bg-surface-container'
                }`}
              >
                {p}
              </button>
            ))}
          </div>
          {period === 'Custom' && (
            <div className="flex gap-2 items-center bg-surface-container-low border border-whisper-border rounded px-3 py-1">
              <input
                type="date"
                value={customStart}
                onChange={(e) => setCustomStart(e.target.value)}
                className="text-xs text-primary bg-transparent border-none outline-none w-[120px]"
              />
              <span className="text-muted-slate text-xs">to</span>
              <input
                type="date"
                value={customEnd}
                onChange={(e) => setCustomEnd(e.target.value)}
                className="text-xs text-primary bg-transparent border-none outline-none w-[120px]"
              />
            </div>
          )}
        </div>
      </header>

      <div className="grid grid-cols-1 sm:grid-cols-3 gap-px bg-whisper-border dark:bg-gray-700 border-b border-whisper-border dark:border-gray-700">
        <KpiTile label="Total Sales" value={loading ? '--' : totalCount.toString()} valueColor="var(--card-value-emerald)" />
        <KpiTile label="Total Amount" value={loading ? '--' : formatCurrency(totalAmount)} valueColor="var(--card-value-emerald)" />
        <KpiTile label="Unique Agents" value={loading ? '--' : uniqueAgents.toString()} valueColor="var(--card-value-dark)" />
      </div>

      {error ? (
        <div className="p-6 text-deep-rose text-sm">{error}</div>
      ) : loading ? (
        <div className="p-6 space-y-3 animate-pulse">
          {[1, 2, 3].map((i) => <div key={i} className="h-9 bg-surface-container dark:bg-gray-800 rounded" />)}
        </div>
      ) : filteredSales.length === 0 ? (
        <div className="p-12 text-sm text-muted-slate text-center">
          {repFilter === 'all'
            ? (period === 'All' ? 'No Vicidial sales recorded yet' : 'No Vicidial sales recorded for this period')
            : `No sales by "${repFilter}" in this period`}
        </div>
      ) : (
        <div className="overflow-x-auto max-h-[600px] overflow-y-auto scrollbar-thin">
          <table className="w-full text-left text-sm border-collapse">
            <thead className="text-xs uppercase tracking-wider text-secondary dark:text-gray-400 font-metadata-mono bg-surface-container-low dark:bg-gray-800 sticky top-0">
              <tr>
                <th className="p-3 font-medium">Date</th>
                <th className="p-3 font-medium">Agent</th>
                <th className="p-3 font-medium">Client</th>
                <th className="p-3 font-medium">Email</th>
                <th className="p-3 font-medium">Bundle</th>
                <th className="p-3 font-medium text-right">Amount</th>
                {canEdit && <th className="p-3 font-medium text-right">Actions</th>}
              </tr>
            </thead>
            <tbody>
              {filteredSales.map((s) => {
                const isEditing = editingId === s.id;
                return (
                  <tr key={s.id} className="border-b border-whisper-border dark:border-gray-700 hover:bg-surface-container-lowest dark:hover:bg-gray-800/50">
                    <td className="p-3 font-metadata-mono text-primary dark:text-gray-200 whitespace-nowrap">
                      {isEditing ? (
                        <input
                          type="datetime-local"
                          value={toLocalDateTimeInputValue(editForm.saleDate ?? s.saleDate)}
                          onChange={(e) => setEditForm((f) => ({ ...f, saleDate: new Date(e.target.value).toISOString() }))}
                          className="w-[180px] px-2 py-1 text-xs border border-whisper-border dark:border-gray-700 rounded bg-pure-surface dark:bg-gray-800 text-primary"
                        />
                      ) : formatDate(s.saleDate)}
                    </td>
                    <td className="p-3 text-primary dark:text-gray-200 font-medium whitespace-nowrap">{s.salesRep}</td>
                    <td className="p-3 text-primary dark:text-gray-200">
                      {isEditing ? (
                        <div className="flex flex-col gap-1">
                          <input
                            type="text"
                            value={editForm.clientName ?? s.clientName}
                            onChange={(e) => setEditForm((f) => ({ ...f, clientName: e.target.value }))}
                            placeholder="Name"
                            className="w-[180px] px-2 py-1 text-xs border border-whisper-border dark:border-gray-700 rounded bg-pure-surface dark:bg-gray-800 text-primary"
                          />
                          <input
                            type="tel"
                            value={editForm.clientPhone ?? s.clientPhone}
                            onChange={(e) => setEditForm((f) => ({ ...f, clientPhone: e.target.value }))}
                            placeholder="Phone"
                            className="w-[180px] px-2 py-1 text-xs border border-whisper-border dark:border-gray-700 rounded bg-pure-surface dark:bg-gray-800 text-primary"
                          />
                        </div>
                      ) : (
                        <>
                          <div className="font-medium">{s.clientName}</div>
                          <div className="text-[11px] text-secondary dark:text-gray-400 font-metadata-mono">{s.clientPhone}</div>
                        </>
                      )}
                    </td>
                    <td className="p-3 text-secondary dark:text-gray-400 font-metadata-mono text-xs">
                      {isEditing ? (
                        <input
                          type="email"
                          value={editForm.clientEmail ?? s.clientEmail}
                          onChange={(e) => setEditForm((f) => ({ ...f, clientEmail: e.target.value }))}
                          placeholder="email@example.com"
                          className="w-[200px] px-2 py-1 text-xs border border-whisper-border dark:border-gray-700 rounded bg-pure-surface dark:bg-gray-800 text-primary"
                        />
                      ) : s.clientEmail}
                    </td>
                    <td className="p-3">
                      {isEditing ? (
                        <select
                          value={editForm.bundle ?? s.bundle}
                          onChange={(e) => setEditForm((f) => ({ ...f, bundle: e.target.value as BundleOption }))}
                          className="w-[180px] px-2 py-1 text-xs border border-whisper-border dark:border-gray-700 rounded bg-pure-surface dark:bg-gray-800 text-primary"
                        >
                          {BUNDLE_OPTIONS.map((b) => (
                            <option key={b} value={b}>{b}</option>
                          ))}
                        </select>
                      ) : (
                        <span className="px-2 py-1 bg-surface-container dark:bg-gray-800 rounded text-[11px] text-primary dark:text-gray-200 font-medium border border-whisper-border dark:border-gray-700 whitespace-nowrap">
                          {s.bundle}
                        </span>
                      )}
                    </td>
                    <td className="p-3 font-bold text-emerald-signal font-metadata-mono text-right whitespace-nowrap">
                      {isEditing ? (
                        <input
                          type="number"
                          step="0.01"
                          min="0.01"
                          value={editForm.amount ?? s.amount}
                          onChange={(e) => setEditForm((f) => ({ ...f, amount: parseFloat(e.target.value) }))}
                          className="w-[100px] px-2 py-1 text-xs text-right border border-whisper-border dark:border-gray-700 rounded bg-pure-surface dark:bg-gray-800 text-primary font-metadata-mono"
                        />
                      ) : formatCurrency(s.amount)}
                    </td>
                    {canEdit && (
                      <td className="p-3 text-right whitespace-nowrap">
                        {isEditing ? (
                          <div className="flex flex-col gap-1 items-end">
                            {editError && <span className="text-[10px] text-deep-rose">{editError}</span>}
                            <div className="flex gap-1 justify-end">
                              <button
                                onClick={cancelEdit}
                                disabled={savingEdit}
                                className="px-2 py-1 text-[11px] border border-whisper-border dark:border-gray-700 rounded text-secondary hover:text-primary disabled:opacity-50"
                              >
                                Cancel
                              </button>
                              <button
                                onClick={saveEdit}
                                disabled={savingEdit}
                                className="px-2 py-1 text-[11px] bg-emerald-signal text-white rounded font-medium hover:opacity-90 disabled:opacity-50 flex items-center gap-1"
                              >
                                {savingEdit && <span className="material-symbols-outlined text-[12px] animate-spin">progress_activity</span>}
                                Save
                              </button>
                            </div>
                          </div>
                        ) : (
                          <button
                            onClick={() => startEdit(s)}
                            className="px-2 py-1 text-[11px] border border-whisper-border dark:border-gray-700 rounded text-secondary hover:text-electric-blue hover:border-electric-blue/40 flex items-center gap-1 ml-auto"
                            title="Edit sale"
                          >
                            <span className="material-symbols-outlined text-[12px]">edit</span>
                            Edit
                          </button>
                        )}
                      </td>
                    )}
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

function KpiTile({ label, value, valueColor }: { label: string; value: string; valueColor: string }) {
  return (
    <div className="bg-pure-surface dark:bg-gray-900 p-6">
      <p className="text-card-label text-[12px] font-medium uppercase tracking-wider">{label}</p>
      <p className="text-[1.8rem] font-bold mt-1 leading-none" style={{ color: valueColor }}>{value}</p>
    </div>
  );
}
