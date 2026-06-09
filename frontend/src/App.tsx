import { BrowserRouter, Routes, Route, Navigate, useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import { AuthProvider, useAuth } from './context/AuthContext';
import { ThemeProvider } from './context/ThemeContext';
import { SliceAuthProvider, useSliceAuth } from './slice/context/SliceAuthContext';
import LoginPage from './pages/LoginPage';
import DashboardPage from './pages/DashboardPage';
import AnalyticsPage from './pages/AnalyticsPage';
import RealTimePage from './pages/RealTimePage';
import UsersPage from './pages/UsersPage';
import VicidialFormPage from './pages/VicidialFormPage';
import AppLayout from './components/Layout/AppLayout';
import PlatformPickerModal from './components/PlatformPickerModal';
import { resolveAccess, ROUTES } from './utils/accessControl';
import SliceShopOverviewPage from './slice/pages/SliceShopOverviewPage';
import SliceAgentOverviewPage from './slice/pages/SliceAgentOverviewPage';
import SliceFileUploadPage from './slice/pages/SliceFileUploadPage';
import SlicePlaceholderPage from './slice/pages/SlicePlaceholderPage';
import SlicePodOverviewPage from './slice/pages/SlicePodOverviewPage';
import SliceHistoryAuditPage from './slice/pages/SliceHistoryAuditPage';
import SliceLayout from './slice/components/SliceLayout';
import { useState, useEffect, type ReactNode } from 'react';

function LoadingScreen() {
  return (
    <div className="min-h-screen flex items-center justify-center bg-canvas-white dark:bg-gray-950">
      <div className="flex flex-col items-center gap-3">
        <div className="w-8 h-8 border-2 border-electric-blue border-t-transparent rounded-full animate-spin" />
        <p className="text-secondary dark:text-gray-400 text-sm">Loading...</p>
      </div>
    </div>
  );
}

function ProtectedRoute({ children }: { children: ReactNode }) {
  const { user, loading } = useAuth();
  if (loading) return <LoadingScreen />;
  if (!user) return <Navigate to="/login" replace />;
  return <AppLayout>{children}</AppLayout>;
}

function AdminRoute({ children }: { children: ReactNode }) {
  const { user, loading } = useAuth();
  if (loading) return <LoadingScreen />;
  if (!user || user.role !== 'Admin') return <Navigate to="/login" replace />;
  return <AppLayout>{children}</AppLayout>;
}

function SliceProtectedRoute({ children }: { children: ReactNode }) {
  const { user, loading, rehydrateWithGoogle } = useSliceAuth();
  const { user: altrxUser, loading: altrxLoading } = useAuth();
  const location = useLocation();
  const [rehydrating, setRehydrating] = useState(false);
  const [rehydrateFailed, setRehydrateFailed] = useState(false);

  useEffect(() => {
    if (loading || altrxLoading) return;
    if (user) return;
    if (!altrxUser) return;
    if (rehydrating || rehydrateFailed) return;
    console.log('[SliceProtectedRoute] triggering rehydrateWithGoogle for', altrxUser.email);
    setRehydrating(true);
    rehydrateWithGoogle()
      .then((ok) => {
        if (!ok) setRehydrateFailed(true);
      })
      .finally(() => setRehydrating(false));
  }, [loading, altrxLoading, user, altrxUser, rehydrating, rehydrateFailed, rehydrateWithGoogle]);

  if (loading || altrxLoading) return <LoadingScreen />;

  if (user) return <SliceLayout>{children}</SliceLayout>;

  if (!altrxUser) {
    return <Navigate to="/login" replace />;
  }

  if (rehydrating) return <LoadingScreen />;

  if (rehydrateFailed) {
    return <Navigate to="/login" replace />;
  }

  return <Navigate to={`/select-platform?redirect=${encodeURIComponent(location.pathname)}`} replace />;
}

function PlatformPickerPage() {
  const { user, loading, logout } = useAuth();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const redirect = searchParams.get('redirect');

  if (loading) return <LoadingScreen />;
  if (!user) return <Navigate to="/login" replace />;

  const { group, redirectTo } = resolveAccess(user.email);
  if (group !== 'both' && redirectTo) {
    return <Navigate to={redirectTo} replace />;
  }

  const handleSelect = (platform: 'slice' | 'altrx') => {
    const dest = platform === 'slice' ? ROUTES.slice : ROUTES.altrx;
    const final = redirect ?? dest;
    console.log('[PlatformPicker] navigating to', final);
    navigate(final, { replace: true });
  };

  return (
    <PlatformPickerModal
      userEmail={user.email}
      onSelect={handleSelect}
      onCancel={() => {
        logout();
        navigate('/login', { replace: true });
      }}
    />
  );
}

function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />

      <Route path="/select-platform" element={<PlatformPickerPage />} />

      <Route
        path="/slice"
        element={
          <SliceProtectedRoute>
            <SliceShopOverviewPage />
          </SliceProtectedRoute>
        }
      />
      <Route
        path="/slice/upload"
        element={
          <SliceProtectedRoute>
            <SliceFileUploadPage />
          </SliceProtectedRoute>
        }
      />
      <Route
        path="/slice/pod"
        element={
          <SliceProtectedRoute>
            <SlicePodOverviewPage />
          </SliceProtectedRoute>
        }
      />
      <Route
        path="/slice/agents"
        element={
          <SliceProtectedRoute>
            <SliceAgentOverviewPage />
          </SliceProtectedRoute>
        }
      />
      <Route
        path="/slice/history"
        element={
          <SliceProtectedRoute>
            <SliceHistoryAuditPage />
          </SliceProtectedRoute>
        }
      />

      <Route path="/form_sale" element={<VicidialFormPage />} />
      <Route
        path="/"
        element={
          <ProtectedRoute>
            <DashboardPage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/dashboard"
        element={
          <ProtectedRoute>
            <DashboardPage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/analytics"
        element={
          <ProtectedRoute>
            <AnalyticsPage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/real-time"
        element={
          <ProtectedRoute>
            <RealTimePage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/users"
        element={
          <AdminRoute>
            <UsersPage />
          </AdminRoute>
        }
      />
      <Route path="*" element={<Navigate to="/login" replace />} />
    </Routes>
  );
}

export default function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <SliceAuthProvider>
          <ThemeProvider>
            <div className="grain-overlay" aria-hidden="true" />
            <AppRoutes />
          </ThemeProvider>
        </SliceAuthProvider>
      </AuthProvider>
    </BrowserRouter>
  );
}
