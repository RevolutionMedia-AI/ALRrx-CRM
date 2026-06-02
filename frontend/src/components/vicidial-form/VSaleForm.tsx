import { useState, useEffect } from 'react';
import { submitVicidialSale } from '../../services/vicidialFormApi';
import { BUNDLE_OPTIONS, type BundleOption, type VicidialSaleRequest } from '../../types';

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

    if (!salesRep.trim()) return setError('Nombre del agente es requerido');
    if (!clientName.trim()) return setError('Nombre del cliente es requerido');
    if (!clientPhone.trim()) return setError('Teléfono del cliente es requerido');
    if (!clientEmail.trim()) return setError('Email del cliente es requerido');
    if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(clientEmail.trim())) return setError('Email del cliente no es válido');
    const amountNum = Number(amount);
    if (!amount || isNaN(amountNum) || amountNum <= 0) return setError('Monto debe ser mayor a 0');

    const payload: VicidialSaleRequest = {
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
      setSuccess(`Venta #${res.id} registrada correctamente`);
      reset();
      onSubmitted();
    } catch (err: unknown) {
      const data = err && typeof err === 'object' && 'response' in err
        ? (err as { response?: { data?: { error?: string } } }).response?.data
        : undefined;
      setError(data?.error ?? 'No se pudo registrar la venta');
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
        <h2 className="text-lg font-bold text-primary dark:text-gray-100">Registrar Venta</h2>
      </div>

      <div className="space-y-5">
        <fieldset className="border border-whisper-border dark:border-gray-700 rounded-xl p-4">
          <legend className="text-[11px] font-semibold uppercase tracking-wider text-secondary dark:text-gray-400 px-2">
            Datos del Agente
          </legend>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <Field label="Nombre del agente" required>
              <input
                type="text"
                value={salesRep}
                onChange={(e) => onSalesRepChange(e.target.value)}
                placeholder="Ej. Juan Pérez"
                className="w-full px-3 py-2 text-sm border border-whisper-border dark:border-gray-700 rounded-lg bg-pure-surface dark:bg-gray-800 text-primary dark:text-gray-100 focus:border-electric-blue focus:outline-none"
              />
            </Field>
            <Field label="Fecha de venta" required>
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
            Datos del Cliente
          </legend>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <Field label="Teléfono del cliente" required>
              <input
                type="tel"
                value={clientPhone}
                onChange={(e) => setClientPhone(e.target.value)}
                placeholder="+1 555 123 4567"
                className="w-full px-3 py-2 text-sm border border-whisper-border dark:border-gray-700 rounded-lg bg-pure-surface dark:bg-gray-800 text-primary dark:text-gray-100 focus:border-electric-blue focus:outline-none"
              />
            </Field>
            <Field label="Nombre del cliente" required>
              <input
                type="text"
                value={clientName}
                onChange={(e) => setClientName(e.target.value)}
                placeholder="Nombre completo"
                className="w-full px-3 py-2 text-sm border border-whisper-border dark:border-gray-700 rounded-lg bg-pure-surface dark:bg-gray-800 text-primary dark:text-gray-100 focus:border-electric-blue focus:outline-none"
              />
            </Field>
            <Field label="Email del cliente" required>
              <input
                type="email"
                value={clientEmail}
                onChange={(e) => setClientEmail(e.target.value)}
                placeholder="cliente@correo.com"
                className="w-full px-3 py-2 text-sm border border-whisper-border dark:border-gray-700 rounded-lg bg-pure-surface dark:bg-gray-800 text-primary dark:text-gray-100 focus:border-electric-blue focus:outline-none"
              />
            </Field>
            <Field label="Bundle seleccionado" required>
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
            <Field label="Monto total (USD)" required full>
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
                <span>Registrando...</span>
              </>
            ) : (
              <>
                <span className="material-symbols-outlined text-[20px]">save</span>
                <span>Registrar Venta</span>
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
