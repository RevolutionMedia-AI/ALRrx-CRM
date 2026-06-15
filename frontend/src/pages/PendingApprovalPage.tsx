import { useEffect, useState } from 'react';
import { useAuth } from '../context/AuthContext';
import { Navigate } from 'react-router-dom';
import { Clock01Icon, Refresh01Icon, Logout01Icon } from 'hugeicons-react';

export default function PendingApprovalPage() {
  const { user, loading, refresh, logout } = useAuth();
  const [refreshing, setRefreshing] = useState(false);
  const [countdown, setCountdown] = useState(30);

  useEffect(() => {
    if (loading) return;
    if (!user) return;
    if (user.status !== 'Pending') return;
    const id = setInterval(() => {
      setCountdown((c) => {
        if (c <= 1) {
          refresh();
          return 30;
        }
        return c - 1;
      });
    }, 1000);
    return () => clearInterval(id);
  }, [loading, user, refresh]);

  const handleRefresh = async () => {
    setRefreshing(true);
    await refresh();
    setRefreshing(false);
    setCountdown(30);
  };

  if (loading) return null;
  if (!user) return <Navigate to="/login" replace />;
  // BUG-4+18 fix: when the admin flips this user to Active, the frontend
  // knows the new status from /api/auth/me, BUT the JWT in localStorage
  // still carries the old `status: Pending` claim. UserStatusMiddleware would
  // then reject every subsequent API call with 403 USER_PENDING, leaving
  // the user stuck on this page. Force a full sign-out so the user gets a
  // fresh JWT on next login.
  if (user.status === 'Active') {
    logout();
    return null;
  }
  if (user.status === 'Rejected' || user.status === 'Suspended') return <Navigate to="/access-denied" replace />;

  return (
    <div className="min-h-[calc(100dvh-4rem)] flex items-center justify-center px-4">
      <div className="max-w-md w-full bg-pure-surface dark:bg-gray-900 border border-whisper-border dark:border-gray-800 rounded-xl shadow-card p-8 text-center">
        <div className="w-16 h-16 mx-auto mb-4 rounded-full bg-amber-warmth/10 flex items-center justify-center">
          <Clock01Icon size={32} className="text-amber-warmth" />
        </div>
        <h1 className="font-display-hero text-2xl text-primary dark:text-white mb-2">
          Cuenta pendiente de aprobación
        </h1>
        <p className="text-secondary dark:text-gray-400 text-sm leading-relaxed mb-6">
          Hola <strong className="text-primary dark:text-gray-200">{user.fullName}</strong>, tu cuenta
          <strong className="text-primary dark:text-gray-200"> {user.email}</strong> está esperando
          la aprobación de un administrador. Te enviaremos un email cuando tu cuenta sea aprobada.
        </p>
        <div className="text-xs text-muted-slate dark:text-gray-500 font-metadata-mono mb-6">
          Próxima verificación automática en {countdown}s
        </div>
        <div className="flex gap-2 justify-center">
          <button
            onClick={handleRefresh}
            disabled={refreshing}
            className="inline-flex items-center gap-2 px-4 py-2 rounded-lg bg-electric-blue text-white text-sm font-medium hover:scale-[0.98] transition-transform shadow-sm disabled:opacity-50"
          >
            <Refresh01Icon size={16} className={refreshing ? 'animate-spin' : ''} />
            {refreshing ? 'Verificando...' : 'Verificar ahora'}
          </button>
          <button
            onClick={logout}
            className="inline-flex items-center gap-2 px-4 py-2 rounded-lg border border-whisper-border dark:border-gray-700 text-secondary dark:text-gray-300 text-sm font-medium hover:bg-card-icon-bg dark:hover:bg-gray-800 transition-colors"
          >
            <Logout01Icon size={16} />
            Cerrar sesión
          </button>
        </div>
      </div>
    </div>
  );
}
