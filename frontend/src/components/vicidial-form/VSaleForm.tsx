import { useState, useEffect } from 'react';
import { submitVicidialSale, getActiveAltrxAgents } from '../../services/vicidialFormApi';
import { BUNDLE_OPTIONS, type BundleOption, type VicidialSaleRequest, type ActiveAltrxAgentDto } from '../../types';
import { extractErrorMessage } from '../../utils/extractErrorMessage';

interface VSaleFormProps {
  salesRep: string;
  onSalesRepChange: (value: string) => void;
  onSubmitted: () => void;
}

function getTodayLocalDateTime(): string {
  const d = new Date();
  const tzOffset = d.getTimezoneOffset() * 60000;
  return new Date(d.getTime() - tzOffset).toISOString().slice(0, 16);
}

export default function VSaleForm({ salesRep, onSalesRepChange, onSubmitted }: VSaleFormProps) {
  const [saleDate, setSaleDate] = useState(getTodayLocalDateTime());
  const [clientPhone, setClientPhone] = useState('');
  const [clientName, setClientName] = useState('');
  const [clientEmail, setClientEmail] = useState('');
  const [bundle, setBundle] = useState<BundleOption>('GLP-1 1 Month');
  const [amount, setAmount] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [activeAgents, setActiveAgents] = useState<ActiveAltrxAgentDto[]>([]);
  const [agentsLoading, setAgentsLoading] = useState(true);
  const [agentsError, setAgentsError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    const load = async () => {
      try {
        setAgentsLoading(true);
        const agents = await getActiveAltrxAgents();
        if (!cancelled) {
          setActiveAgents(agents);
          setAgentsError(null);
        }
      } catch (err: unknown) {
        if (!cancelled) {
          setAgentsError('Could not load active agents from Vicidial');
          setActiveAgents([]);
        }
      } finally {
        if (!cancelled) setAgentsLoading(false);
      }
    };
    load();
    return () => { cancelled = true; };
  }, []);

  useEffect(() => {
    if (salesRep) {
      setError(null);
    }
  }, [salesRep]);

  const reset = () => {
    setClientPhone('');
    setClientName('');
    setClientEmail('');
    setBundle('GLP-1 1 Month');
    setAmount('');
    setSaleDate(getTodayLocalDateTime());
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setSuccess(null);

    if (!salesRep.trim()) return setError('Agent name is required');
    if (!clientName.trim()) return setError('Client name is required');
    if (!clientPhone.trim()) return setError('Client phone is required');
    if (!clientEmail.trim()) return setError('Client email is required');
    if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(clientEmail.trim())) return setError('Client email is invalid');
    const amountNum = Number(amount);
    if (!amount || isNaN(amountNum) || amountNum <= 0) return setError('Amount must be greater than 0');

    const payload: VicidialSaleRequest = {
      salesRep: salesRep.trim(),
      saleDate: new Date(saleDate).toISOString(),
      clientPhone: clientPhone.trim(),
      clientName: clientName.trim(),
      clientEmail: clientEmail.trim().toLowerCase(),
      bundle,
      amount: amountNum,
    };

    setSubmitting(true);
    try {
      const res = await submitVicidialSale(payload);
      setSuccess(`Sale #${res.id} registered successfully`);
      reset();
      onSubmitted();
    } catch (err: unknown) {
      setError(extractErrorMessage(err, 'Could not register the sale'));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <form
      onSubmit={handleSubmit}
      className="bg-pure-surface dark:bg-gray-900 border border-whisper-border dark:border-gray-700 rounded-2xl shadow-card p-6"
    >
      <div className="flex items-center gap-2 mb-4">
        <span className="material-symbols-outlined text-electric-blue">person_add</span>
        <h2 className="text-lg font-bold text-primary dark:text-gray-100">Register Sale</h2>
      </div>

      <div className="space-y-5">
        <fieldset className="border border-whisper-border dark:border-gray-700 rounded-xl p-4">
          <legend className="text-[11px] font-semibold uppercase tracking-wider text-secondary dark:text-gray-400 px-2">
            Agent Information
          </legend>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <Field label="Agent Name" required>
              {agentsLoading ? (
                <div className="h-[38px] w-full bg-surface-container rounded-lg animate-pulse" />
              ) : agentsError ? (
                <div className="space-y-1">
                  <div className="w-full px-3 py-2 text-sm border border-deep-rose/40 rounded-lg bg-deep-rose/5 text-deep-rose">
                    {agentsError}
                  </div>
                  <button
                    type="button"
                    onClick={() => { setAgentsError(null); setAgentsLoading(true); getActiveAltrxAgents().then(setActiveAgents).catch(() => setAgentsError('Could not load active agents from Vicidial')).finally(() => setAgentsLoading(false)); }}
                    className="text-[11px] text-electric-blue hover:underline"
                  >
                    Retry
                  </button>
                </div>
              ) : (
                <select
                  value={salesRep}
                  onChange={(e) => onSalesRepChange(e.target.value)}
                  className="w-full px-3 py-2 text-sm border border-whisper-border dark:border-gray-700 rounded-lg bg-pure-surface dark:bg-gray-800 text-primary dark:text-gray-100 focus:border-electric-blue focus:outline-none"
                >
                  <option value="">— Select an active ALTRX agent —</option>
                  {activeAgents.map((a) => (
                    <option key={a.user} value={a.name}>
                      {a.name} ({a.user})
                    </option>
                  ))}
                </select>
              )}
            </Field>
            <Field label="Sale Date" required>
              <input
                type="datetime-local"
                value={saleDate}
                onChange={(e) => setSaleDate(e.target.value)}
                className="w-full px-3 py-2 text-sm border border-whisper-border dark:border-gray-700 rounded-lg bg-pure-surface dark:bg-gray-800 text-primary dark:text-gray-100 focus:border-electric-blue focus:outline-none"
              />
            </Field>
          </div>
        </fieldset>

        <fieldset className="border border-whisper-border dark:border-gray-700 rounded-xl p-4">
          <legend className="text-[11px] font-semibold uppercase tracking-wider text-secondary dark:text-gray-400 px-2">
            Client Information
          </legend>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <Field label="Client Phone" required>
              <input
                type="tel"
                value={clientPhone}
                onChange={(e) => setClientPhone(e.target.value)}
                placeholder="+1 555 123 4567"
                className="w-full px-3 py-2 text-sm border border-whisper-border dark:border-gray-700 rounded-lg bg-pure-surface dark:bg-gray-800 text-primary dark:text-gray-100 focus:border-electric-blue focus:outline-none"
              />
            </Field>
            <Field label="Client Name" required>
              <input
                type="text"
                value={clientName}
                onChange={(e) => setClientName(e.target.value)}
                placeholder="Full name"
                className="w-full px-3 py-2 text-sm border border-whisper-border dark:border-gray-700 rounded-lg bg-pure-surface dark:bg-gray-800 text-primary dark:text-gray-100 focus:border-electric-blue focus:outline-none"
              />
            </Field>
            <Field label="Client Email" required>
              <input
                type="email"
                value={clientEmail}
                onChange={(e) => setClientEmail(e.target.value)}
                placeholder="client@email.com"
                className="w-full px-3 py-2 text-sm border border-whisper-border dark:border-gray-700 rounded-lg bg-pure-surface dark:bg-gray-800 text-primary dark:text-gray-100 focus:border-electric-blue focus:outline-none"
              />
            </Field>
            <Field label="Selected Bundle" required>
              <select
                value={bundle}
                onChange={(e) => setBundle(e.target.value as BundleOption)}
                className="w-full px-3 py-2 text-sm border border-whisper-border dark:border-gray-700 rounded-lg bg-pure-surface dark:bg-gray-800 text-primary dark:text-gray-100 focus:border-electric-blue focus:outline-none"
              >
                {BUNDLE_OPTIONS.map((b) => (
                  <option key={b} value={b}>{b}</option>
                ))}
              </select>
            </Field>
            <Field label="Total Amount (USD)" required full>
              <div className="relative">
                <span className="absolute left-3 top-1/2 -translate-y-1/2 text-secondary dark:text-gray-400 text-sm">$</span>
                <input
                  type="number"
                  step="0.01"
                  min="0.01"
                  value={amount}
                  onChange={(e) => setAmount(e.target.value)}
                  placeholder="498.00"
                  className="w-full pl-7 pr-3 py-2 text-sm border border-whisper-border dark:border-gray-700 rounded-lg bg-pure-surface dark:bg-gray-800 text-primary dark:text-gray-100 focus:border-electric-blue focus:outline-none font-metadata-mono"
                />
              </div>
            </Field>
          </div>
        </fieldset>

        {error && (
          <div className="bg-deep-rose/10 border border-deep-rose/20 rounded-lg p-3 text-deep-rose text-sm flex items-center gap-2">
            <span className="material-symbols-outlined text-[18px]">error</span>
            <span>{error}</span>
          </div>
        )}
        {success && (
          <div className="bg-emerald-signal/10 border border-emerald-signal/30 rounded-lg p-3 text-emerald-signal text-sm flex items-center gap-2">
            <span className="material-symbols-outlined text-[18px]">check_circle</span>
            <span>{success}</span>
          </div>
        )}

        <div className="flex justify-end pt-2">
          <button
            type="submit"
            disabled={submitting}
            className="bg-emerald-signal hover:bg-emerald-signal/90 text-white font-medium px-6 py-2.5 rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2 shadow-sm"
          >
            {submitting ? (
              <>
                <span className="material-symbols-outlined text-[20px] animate-spin">progress_activity</span>
                <span>Registering...</span>
              </>
            ) : (
              <>
                <span className="material-symbols-outlined text-[20px]">save</span>
                <span>Register Sale</span>
              </>
            )}
          </button>
        </div>
      </div>
    </form>
  );
}

function Field({
  label, required, children, full = false,
}: { label: string; required?: boolean; children: React.ReactNode; full?: boolean }) {
  return (
    <div className={full ? 'sm:col-span-2' : ''}>
      <label className="block text-xs font-medium text-secondary dark:text-gray-400 mb-1">
        {label} {required && <span className="text-deep-rose">*</span>}
      </label>
      {children}
    </div>
  );
}
