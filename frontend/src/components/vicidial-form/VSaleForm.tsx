import { useState, useEffect, useRef } from 'react';
import { useSearchParams } from 'react-router-dom';
import { submitVicidialSale, getActiveAltrxAgents, getVicidialLeadById } from '../../services/vicidialFormApi';
import { BUNDLE_OPTIONS, type BundleOption, type VicidialSaleRequest, type ActiveAltrxAgentDto, type VicidialLeadDto } from '../../types';
import { extractErrorMessage } from '../../utils/extractErrorMessage';
import VLeadBanner, { type LeadLookupState } from './VLeadBanner';

const SALES_REP_STORAGE_KEY = 'vicidial_form_sales_rep';

function getTodayLocalDateTime(): string {
  const d = new Date();
  const tzOffset = d.getTimezoneOffset() * 60000;
  return new Date(d.getTime() - tzOffset).toISOString().slice(0, 16);
}

function combineName(first: string, last: string): string {
  return `${first ?? ''} ${last ?? ''}`.replace(/\s+/g, ' ').trim();
}

export default function VSaleForm() {
  const [searchParams] = useSearchParams();
  const rawLeadId = searchParams.get('lead_id');
  const parsedLeadId = rawLeadId ? Number.parseInt(rawLeadId, 10) : NaN;
  const hasLeadId = !!rawLeadId && rawLeadId.trim() !== '';
  const validLeadId = hasLeadId && Number.isFinite(parsedLeadId) && parsedLeadId > 0 ? parsedLeadId : null;

  const [leadState, setLeadState] = useState<LeadLookupState>(
    hasLeadId && validLeadId == null
      ? { kind: 'invalid', message: `lead_id inválido: "${rawLeadId}". Se esperaba un número entero positivo.` }
      : { kind: 'idle' }
  );
  const resolvedLeadRef = useRef<VicidialLeadDto | null>(null);

  const [agents, setAgents] = useState<ActiveAltrxAgentDto[]>([]);
  const [loadingAgents, setLoadingAgents] = useState(true);
  const [agentsError, setAgentsError] = useState<string | null>(null);
  const [salesRep, setSalesRep] = useState<string>(() => sessionStorage.getItem(SALES_REP_STORAGE_KEY) ?? '');
  const [saleDate, setSaleDate] = useState(getTodayLocalDateTime());
  const [clientPhone, setClientPhone] = useState('');
  const [clientName, setClientName] = useState('');
  const [clientEmail, setClientEmail] = useState('');
  const [bundle, setBundle] = useState<BundleOption>('GLP-1 1 Month');
  const [amount, setAmount] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  const isReadOnly = leadState.kind === 'success';

  useEffect(() => {
    if (validLeadId == null) return;
    let cancelled = false;
    setLeadState({ kind: 'loading', leadId: validLeadId });
    (async () => {
      try {
        const lead = await getVicidialLeadById(validLeadId);
        if (cancelled) return;
        resolvedLeadRef.current = lead;
        setLeadState({ kind: 'success', lead });
        setClientName(combineName(lead.firstName, lead.lastName));
        setClientPhone(lead.phoneNumber ?? '');
        setClientEmail(lead.email ?? '');
        setSaleDate(getTodayLocalDateTime());
      } catch (err: unknown) {
        if (cancelled) return;
        const status = (err as { response?: { status?: number } })?.response?.status;
        const message = extractErrorMessage(err, 'No se pudo consultar VICIdial');
        if (status === 404) {
          setLeadState({ kind: 'not-found', leadId: validLeadId });
        } else if (status === 503) {
          setLeadState({ kind: 'connection-error', message });
        } else if (status === 400) {
          setLeadState({ kind: 'invalid', message });
        } else {
          setLeadState({ kind: 'connection-error', message });
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [validLeadId]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const list = await getActiveAltrxAgents();
        if (cancelled) return;
        setAgents(list);
        if (list.length === 0) {
          setAgentsError('No active ALTRX agents found');
        }
      } catch (err: unknown) {
        if (cancelled) return;
        setAgentsError(extractErrorMessage(err, 'Could not load agents'));
      } finally {
        if (!cancelled) setLoadingAgents(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (salesRep) {
      sessionStorage.setItem(SALES_REP_STORAGE_KEY, salesRep);
    }
  }, [salesRep]);

  const reset = () => {
    setBundle('GLP-1 1 Month');
    setAmount('');
    setSaleDate(getTodayLocalDateTime());
    if (resolvedLeadRef.current) {
      const lead = resolvedLeadRef.current;
      setClientName(combineName(lead.firstName, lead.lastName));
      setClientPhone(lead.phoneNumber ?? '');
      setClientEmail(lead.email ?? '');
    } else {
      setClientPhone('');
      setClientName('');
      setClientEmail('');
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setSuccess(null);

    if (validLeadId == null) {
      return setError('This form requires a VICIdial lead. Please open it from VICIdial with a lead_id in the URL.');
    }
    if (!salesRep.trim()) {
      console.warn('[VSaleForm] Blocked: no sales rep selected');
      return setError('Please select your user from the dropdown above');
    }
    if (!clientName.trim()) return setError('Client name is required');
    if (!clientPhone.trim()) return setError('Client phone is required');
    if (!clientEmail.trim()) return setError('Client email is required');
    if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(clientEmail.trim())) return setError('Client email is invalid');
    const amountNum = Number(amount);
    if (!amount || isNaN(amountNum) || amountNum <= 0) return setError('Amount must be greater than 0');

    const payload: VicidialSaleRequest = {
      ...(validLeadId != null ? { leadId: validLeadId } : {}),
      salesRep: salesRep.trim(),
      saleDate: new Date(saleDate).toISOString(),
      clientPhone: clientPhone.trim(),
      clientName: clientName.trim(),
      clientEmail: clientEmail.trim(),
      bundle,
      amount: amountNum,
    };

    setSubmitting(true);
    try {
      const res = await submitVicidialSale(payload);
      setSuccess(`Sale #${res.id} registered successfully`);
      reset();
    } catch (err: unknown) {
      console.error('[VSaleForm] Submit failed', err);
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

      <VLeadBanner state={leadState} />

      <div className="space-y-5">
        <fieldset className="border border-whisper-border dark:border-gray-700 rounded-xl p-4">
          <legend className="text-[11px] font-semibold uppercase tracking-wider text-secondary dark:text-gray-400 px-2">
            Agent Information
          </legend>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <Field label="Your user" required>
              {loadingAgents ? (
                <div className="w-full h-[38px] px-3 border border-whisper-border dark:border-gray-700 rounded-lg bg-surface-container-low dark:bg-gray-800 text-secondary dark:text-gray-400 text-sm flex items-center">
                  <span className="material-symbols-outlined text-[18px] mr-2 animate-spin">progress_activity</span>
                  Loading agents…
                </div>
              ) : (
                <select
                  value={salesRep}
                  onChange={(e) => setSalesRep(e.target.value)}
                  disabled={!!agentsError}
                  className="w-full px-3 py-2 text-sm border border-whisper-border dark:border-gray-700 rounded-lg bg-pure-surface dark:bg-gray-800 text-primary dark:text-gray-100 focus:border-electric-blue focus:outline-none disabled:opacity-50"
                >
                  <option value="">Select your user</option>
                  {agents.map((a) => (
                    <option key={a.user} value={a.fullName}>
                      {a.fullName} ({a.user})
                    </option>
                  ))}
                </select>
              )}
              {agentsError && (
                <p className="mt-1 text-xs text-deep-rose">{agentsError}</p>
              )}
            </Field>
            <Field label="Sale Date" required>
              <input
                type="datetime-local"
                value={saleDate}
                onChange={(e) => setSaleDate(e.target.value)}
                readOnly={isReadOnly}
                className={`w-full px-3 py-2 text-sm border border-whisper-border dark:border-gray-700 rounded-lg text-primary dark:text-gray-100 focus:border-electric-blue focus:outline-none ${
                  isReadOnly
                    ? 'bg-surface-container-low dark:bg-gray-800 text-secondary dark:text-gray-400 cursor-not-allowed'
                    : 'bg-pure-surface dark:bg-gray-800'
                }`}
              />
            </Field>
          </div>
        </fieldset>

        <fieldset className="border border-whisper-border dark:border-gray-700 rounded-xl p-4">
          <legend className="text-[11px] font-semibold uppercase tracking-wider text-secondary dark:text-gray-400 px-2 flex items-center gap-2">
            Client Information
            {isReadOnly && (
              <span className="text-[10px] font-metadata-mono text-emerald-signal normal-case tracking-normal flex items-center gap-1">
                <span className="material-symbols-outlined text-[12px]">lock</span>
                from VICIdial
              </span>
            )}
          </legend>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <Field label="Client Phone" required>
              <input
                type="tel"
                value={clientPhone}
                onChange={(e) => setClientPhone(e.target.value)}
                readOnly={isReadOnly}
                placeholder={isReadOnly ? '' : '+1 555 123 4567'}
                className={`w-full px-3 py-2 text-sm border border-whisper-border dark:border-gray-700 rounded-lg text-primary dark:text-gray-100 focus:border-electric-blue focus:outline-none ${
                  isReadOnly
                    ? 'bg-surface-container-low dark:bg-gray-800 text-secondary dark:text-gray-400 cursor-not-allowed'
                    : 'bg-pure-surface dark:bg-gray-800'
                }`}
              />
            </Field>
            <Field label="Client Name" required>
              <input
                type="text"
                value={clientName}
                onChange={(e) => setClientName(e.target.value)}
                readOnly={isReadOnly}
                placeholder={isReadOnly ? '' : 'Full name'}
                className={`w-full px-3 py-2 text-sm border border-whisper-border dark:border-gray-700 rounded-lg text-primary dark:text-gray-100 focus:border-electric-blue focus:outline-none ${
                  isReadOnly
                    ? 'bg-surface-container-low dark:bg-gray-800 text-secondary dark:text-gray-400 cursor-not-allowed'
                    : 'bg-pure-surface dark:bg-gray-800'
                }`}
              />
            </Field>
            <Field label="Client Email" required>
              <input
                type="email"
                value={clientEmail}
                onChange={(e) => setClientEmail(e.target.value)}
                readOnly={isReadOnly}
                placeholder={isReadOnly ? '' : 'client@email.com'}
                className={`w-full px-3 py-2 text-sm border border-whisper-border dark:border-gray-700 rounded-lg text-primary dark:text-gray-100 focus:border-electric-blue focus:outline-none ${
                  isReadOnly
                    ? 'bg-surface-container-low dark:bg-gray-800 text-secondary dark:text-gray-400 cursor-not-allowed'
                    : 'bg-pure-surface dark:bg-gray-800'
                }`}
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
                  autoFocus={isReadOnly}
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
          {!salesRep && !loadingAgents && !agentsError && (
            <p className="text-xs text-secondary dark:text-gray-400 mt-2 text-right">
              Select your user above to enable registration
            </p>
          )}
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
