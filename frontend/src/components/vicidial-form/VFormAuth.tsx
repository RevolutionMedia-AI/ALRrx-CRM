import { useState } from 'react';
import { authenticateVicidialForm, setVicidialToken } from '../../services/vicidialFormApi';
import RevolutionLogo from '../../images/RevolutionLogo.png';

interface VFormAuthProps {
  onAuthenticated: (formName: string) => void;
}

export default function VFormAuth({ onAuthenticated }: VFormAuthProps) {
  const [key, setKey] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!key.trim()) {
      setError('Ingresa la clave del formulario');
      return;
    }
    setLoading(true);
    setError(null);
    try {
      const res = await authenticateVicidialForm(key.trim());
      setVicidialToken(res.token, res.expiresAt);
      onAuthenticated(res.formName);
    } catch (err: unknown) {
      const msg = err && typeof err === 'object' && 'response' in err
        ? (err as { response?: { data?: { error?: string } } }).response?.data?.error
        : undefined;
      setError(msg ?? 'Clave inválida o no se pudo autenticar');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="min-h-[calc(100dvh-4rem)] flex items-center justify-center px-4">
      <form
        onSubmit={handleSubmit}
        className="w-full max-w-md bg-pure-surface dark:bg-gray-900 border border-whisper-border dark:border-gray-700 rounded-2xl shadow-card p-8"
      >
        <div className="flex flex-col items-center gap-3 mb-6">
          <img src={RevolutionLogo} alt="RevolutionMedia" className="h-12 w-12" />
          <h2 className="text-xl font-bold text-primary dark:text-gray-100">ALTRX Sales Form</h2>
          <p className="text-sm text-secondary dark:text-gray-400 text-center">
            Ingresa la clave del formulario para registrar ventas.
          </p>
        </div>

        <label className="block text-sm font-medium text-primary dark:text-gray-200 mb-1.5">
          Clave del formulario
        </label>
        <input
          type="password"
          value={key}
          onChange={(e) => setKey(e.target.value)}
          placeholder="••••••••••"
          autoFocus
          className="w-full px-3 py-2.5 text-sm border border-whisper-border dark:border-gray-700 rounded-lg bg-pure-surface dark:bg-gray-800 text-primary dark:text-gray-100 focus:border-electric-blue focus:outline-none focus:ring-2 focus:ring-electric-blue/20"
        />

        {error && (
          <div className="mt-3 bg-deep-rose/10 border border-deep-rose/20 rounded-lg p-3 text-deep-rose text-sm">
            {error}
          </div>
        )}

        <button
          type="submit"
          disabled={loading}
          className="mt-5 w-full bg-emerald-signal hover:bg-emerald-signal/90 text-white font-medium py-2.5 rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed flex items-center justify-center gap-2"
        >
          {loading ? (
            <>
              <span className="material-symbols-outlined text-[20px] animate-spin">progress_activity</span>
              <span>Verificando...</span>
            </>
          ) : (
            <>
              <span className="material-symbols-outlined text-[20px]">login</span>
              <span>Acceder</span>
            </>
          )}
        </button>
      </form>
    </div>
  );
}
