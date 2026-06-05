import { useState, useEffect, useRef } from 'react';
import { useSearchParams } from 'react-router-dom';
import { submitVicidialSale, getVicidialLeadById, getAgentByUser } from '../../services/vicidialFormApi';
import { BUNDLE_OPTIONS, type BundleOption, type VicidialSaleRequest, type VicidialLeadDto } from '../../types';
import { extractErrorMessage } from '../../utils/extractErrorMessage';
import VLeadBanner, { type LeadLookupState } from './VLeadBanner';

function getTodayLocalDateTime(): string {
  const d = new Date();
  const tzOffset = d.getTimezoneOffset() * 60000;
  return new Date(d.getTime() - tzOffset).toISOString().slice(0, 16);
}

function combineName(first: string, last: string): string {
  return `${first ?? ''} ${last ?? ''}`.replace(/\s+/g, ' ').trim();
}

const VICIDIAL_PLACEHOLDER_PATTERN = /^--A--[\w_\-]+--B--$/;

export default function VSaleForm() {
  const [searchParams] = useSearchParams();
  const rawLeadId = searchParams.get('lead_id');
  const parsedLeadId = rawLeadId ? Number.parseInt(rawLeadId, 10) : NaN;
  const hasLeadId = !!rawLeadId && rawLeadId.trim() !== '';
  const validLeadId = hasLeadId && Number.isFinite(parsedLeadId) && parsedLeadId > 0 ? parsedLeadId : null;

  const rawSalesRep = (searchParams.get('salesRep') ?? searchParams.get('full_name') ?? searchParams.get('fullname') ?? '').trim();
  const rawAgentUser = (searchParams.get('user') ?? '').trim();
  const salesRepIsPlaceholder = VICIDIAL_PLACEHOLDER_PATTERN.test(rawSalesRep);
  const agentUserIsPlaceholder = VICIDIAL_PLACEHOLDER_PATTERN.test(rawAgentUser);
  const urlSalesRep = salesRepIsPlaceholder ? '' : rawSalesRep;
  const urlAgentUser = agentUserIsPlaceholder ? '' : rawAgentUser;
  const hasAgentContext = urlSalesRep.length > 0 || urlAgentUser.length > 0;

  const [leadState, setLeadState] = useState<LeadLookupState>(
    hasLeadId && validLeadId == null
      ? { kind: 'invalid', message: `lead_id inválido: "${rawLeadId}". Se esperaba un número entero positivo.` }
      : hasLeadId && !hasAgentContext
        ? { kind: 'invalid', message: 'Esta ventana debe abrirse desde VICIdial (faltan datos del agente en la URL).' }
        : { kind: 'idle' }
  );
  const resolvedLeadRef = useRef<VicidialLeadDto | null>(null);

  const [salesRep, setSalesRep] = useState<string>(urlSalesRep);
  const [agentUser, setAgentUser] = useState<string>(urlAgentUser);
  const [resolvingAgent, setResolvingAgent] = useState<boolean>(!urlSalesRep && urlAgentUser.length > 0);
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
    if (urlSalesRep || !urlAgentUser) return;
    let cancelled = false;
    (async () => {
      try {
        const agent = await getAgentByUser(urlAgentUser);
        if (cancelled) return;
        const resolved = (agent.fullName ?? '').trim() || agent.user;
        setSalesRep(resolved);
        if (agent.user && agent.user !== urlAgentUser) {
          setAgentUser(agent.user);
        }
      } catch (err) {
        if (cancelled) return;
        console.warn('[VSaleForm] Failed to resolve agent from user, falling back to login', err);
        setSalesRep(urlAgentUser);
      } finally {
        if (!cancelled) setResolvingAgent(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [urlSalesRep, urlAgentUser]);

  useEffect(() => {
    if (validLeadId == null) return;
    if (!hasAgentContext) return;
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
  }, [validLeadId, hasAgentContext]);

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
      return setError('Agent (salesRep) not provided by VICIdial. Open this form from a VICIdial session.');
    }
    if (!clientName.trim()) return setError('Client name is required');
    if (!clientPhone.trim()) return setError('Client phone is required');
    if (!clientEmail.trim()) return setError('Client email is required');
    if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(clientEmail.trim())) return setError('Client email is invalid');
    const amountNum = Number(amount);
    if (!amount || isNaN(amountNum) || amountNum <= 0) return setError('Amount must be greater than 0');

    const payload: VicidialSaleRequest = {
      leadId: validLeadId,
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
              <div className="w-full px-3 py-2 text-sm border border-whisper-border dark:border-gray-700 rounded-lg bg-surface-container-low dark:bg-gray-800 text-primary dark:text-gray-100 flex items-center gap-2">
                <span className="material-symbols-outlined text-[16px] text-emerald-signal">verified_user</span>
                {resolvingAgent ? (
                  <span className="flex items-center gap-1.5 text-secondary dark:text-gray-400">
                    <span className="material-symbols-outlined text-[14px] animate-spin">progress_activity</span>
                    <span>Resolving agent…</span>
                  </span>
                ) : (
                  <>
                    <span className="font-medium">{salesRep || agentUser || '—'}</span>
                    {agentUser && (
                      <span className="text-xs text-secondary dark:text-gray-400 font-metadata-mono">({agentUser})</span>
                    )}
                  </>
                )}
                <span className="ml-auto text-[10px] text-emerald-signal uppercase tracking-wider font-semibold flex items-center gap-1">
                  <span className="material-symbols-outlined text-[12px]">lock</span>
                  VICIdial
                </span>
              </div>
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
