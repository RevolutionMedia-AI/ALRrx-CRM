import { useAuth } from '../context/AuthContext';
import { Navigate } from 'react-router-dom';
import { Cancel01Icon, Logout01Icon } from 'hugeicons-react';

export default function AccessDeniedPage() {
  const { user, loading, logout } = useAuth();

  if (loading) return null;
  if (!user) return <Navigate to="/login" replace />;
  if (user.status === 'Active') return <Navigate to="/" replace />;
  if (user.status === 'Pending') return <Navigate to="/pending-approval" replace />;

  const isSuspended = user.status === 'Suspended';

  return (
    <div className="min-h-[calc(100dvh-4rem)] flex items-center justify-center px-4">
      <div className="max-w-md w-full bg-pure-surface dark:bg-gray-900 border border-whisper-border dark:border-gray-800 rounded-xl shadow-card p-8 text-center">
        <div className={`w-16 h-16 mx-auto mb-4 rounded-full flex items-center justify-center ${
          isSuspended ? 'bg-amber-warmth/10' : 'bg-deep-rose/10'
        }`}>
          <Cancel01Icon size={32} className={isSuspended ? 'text-amber-warmth' : 'text-deep-rose'} />
        </div>
        <h1 className="font-display-hero text-2xl text-primary dark:text-white mb-2">
          {isSuspended ? 'Cuenta suspendida' : 'Acceso denegado'}
        </h1>
        <p className="text-secondary dark:text-gray-400 text-sm leading-relaxed mb-6">
          {isSuspended
            ? 'Tu cuenta ha sido suspendida temporalmente. Contacta al administrador para más información.'
            : 'Tu solicitud de acceso fue rechazada. Si crees que es un error, contacta al administrador.'}
        </p>
        <div className="text-xs text-muted-slate dark:text-gray-500 font-metadata-mono mb-6">
          {user.email}
        </div>
        <button
          onClick={logout}
          className="inline-flex items-center gap-2 px-4 py-2 rounded-lg border border-whisper-border dark:border-gray-700 text-secondary dark:text-gray-300 text-sm font-medium hover:bg-card-icon-bg dark:hover:bg-gray-800 transition-colors"
        >
          <Logout01Icon size={16} />
          Cerrar sesión
        </button>
      </div>
    </div>
  );
}
