import { BrowserRouter, Routes, Route, Navigate, useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import { AuthProvider, useAuth } from './context/AuthContext';
import { ThemeProvider } from './context/ThemeContext';
import { SliceAuthProvider, useSliceAuth } from './slice/context/SliceAuthContext';
import ErrorBoundary from './components/ErrorBoundary';
import LoginPage from './pages/LoginPage';
import DashboardPage from './pages/DashboardPage';
import AnalyticsPage from './pages/AnalyticsPage';
import RealTimePage from './pages/RealTimePage';
import UsersPage from './pages/UsersPage';
import VicidialFormPage from './pages/VicidialFormPage';
import TwilioCostsPage from './pages/TwilioCostsPage';
import AdminPanelPage from './pages/AdminPanelPage';
import PendingApprovalPage from './pages/PendingApprovalPage';
import AccessDeniedPage from './pages/AccessDeniedPage';
import NoAccessPage from './pages/NoAccessPage';
import AppLayout from './components/Layout/AppLayout';
import PlatformPickerModal from './components/PlatformPickerModal';
import { resolveAccess, ROUTES } from './utils/accessControl';
import SliceShopOverviewPage from './slice/pages/SliceShopOverviewPage';
import SliceAgentOverviewPage from './slice/pages/SliceAgentOverviewPage';
import SliceFileUploadPage from './slice/pages/SliceFileUploadPage';
import SlicePlaceholderPage from './slice/pages/SlicePlaceholderPage';
import SlicePodOverviewPage from './slice/pages/SlicePodOverviewPage';
import SliceHistoryAuditPage from './slice/pages/SliceHistoryAuditPage';
import SliceReportsPeriodPage from './slice/pages/SliceReportsPeriodPage';
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
  if (user.status === 'Pending') return <Navigate to="/pending-approval" replace />;
  if (user.status === 'Suspended' || user.status === 'Rejected') return <Navigate to="/access-denied" replace />;
  // BUG-14 fix: enforce PlatformAccess at the route layer. A user with
  // PlatformAccess=Slice or None who types /dashboard in the URL must NOT
  // be allowed in just because their status is Active.
  if (user.platformAccess !== 'Altrx' && user.platformAccess !== 'Both') {
    return <Navigate to="/select-platform" replace />;
  }
  return <AppLayout>{children}</AppLayout>;
}

function AdminRoute({ children }: { children: ReactNode }) {
  const { user, loading } = useAuth();
  if (loading) return <LoadingScreen />;
  if (!user) return <Navigate to="/login" replace />;
  if (user.status === 'Pending') return <Navigate to="/pending-approval" replace />;
  if (user.status === 'Suspended' || user.status === 'Rejected') return <Navigate to="/access-denied" replace />;
  if (user.role !== 'Admin') return <Navigate to="/dashboard" replace />;
  return <AppLayout>{children}</AppLayout>;
}

function SliceProtectedRoute({ children }: { children: ReactNode }) {
  const { user, loading, rehydrateWithGoogle } = useSliceAuth();
  const { user: altrxUser, loading: altrxLoading, logout: altrxLogout } = useAuth();
  const location = useLocation();
  const [rehydrating, setRehydrating] = useState(false);
  const [rehydrateFailed, setRehydrateFailed] = useState(false);
  const [rehydratingStarted, setRehydratingStarted] = useState(false);

  useEffect(() => {
    if (loading || altrxLoading) return;
    if (user) return;
    if (!altrxUser) return;
    if (rehydrating || rehydrateFailed || rehydratingStarted) return;
    setRehydratingStarted(true);
    setRehydrating(true);
    rehydrateWithGoogle()
      .then((ok) => {
        if (!ok) setRehydrateFailed(true);
      })
      .finally(() => setRehydrating(false));
  }, [loading, altrxLoading, user, altrxUser, rehydrating, rehydrateFailed, rehydratingStarted, rehydrateWithGoogle]);

  if (loading || altrxLoading) return <LoadingScreen />;

  if (user) return <SliceLayout>{children}</SliceLayout>;

  if (!altrxUser) {
    return <Navigate to="/login" replace />;
  }

  // BUG-14 fix: enforce PlatformAccess on slice routes too. A user with
  // PlatformAccess=Altrx or None must not be able to reach /slice just by
  // typing the URL.
  if (altrxUser.platformAccess !== 'Slice' && altrxUser.platformAccess !== 'Both') {
    return <Navigate to="/select-platform" replace />;
  }

  if (rehydrating) return <LoadingScreen />;

  if (rehydrateFailed) {
    // BUG-2 fix: do NOT redirect to /login. LoginPage auto-redirects back to
    // /slice for users whose platformAccess is 'Slice', which would cause an
    // infinite loop. Show an error page with explicit sign-out instead so the
    // user can escape.
    return (
      <div className="min-h-screen flex items-center justify-center bg-canvas-white dark:bg-gray-950 px-4">
        <div className="max-w-md w-full bg-pure-surface dark:bg-gray-900 border border-whisper-border dark:border-gray-800 rounded-xl shadow-card p-8 text-center">
          <div className="w-16 h-16 mx-auto mb-4 rounded-full bg-deep-rose/10 flex items-center justify-center">
            <span className="material-symbols-outlined text-deep-rose" style={{ fontSize: '32px' }}>error_outline</span>
          </div>
          <h1 className="font-display-hero text-2xl text-primary dark:text-white mb-2">
            Slice access unavailable
          </h1>
          <p className="text-secondary dark:text-gray-400 text-sm leading-relaxed mb-6">
            Your ALTRX session is valid (<strong>{altrxUser.email}</strong>) but we could not
            re-validate it for Slice. This usually means the Slice service is down or your
            access has been revoked.
          </p>
          <div className="flex gap-2 justify-center">
            <button
              onClick={() => { altrxLogout(); }}
              className="inline-flex items-center gap-2 px-4 py-2 rounded-lg bg-primary text-white text-sm font-medium hover:scale-[0.98] transition-transform shadow-sm"
            >
              Sign out and try again
            </button>
          </div>
        </div>
      </div>
    );
  }

  return <Navigate to={`/select-platform?redirect=${encodeURIComponent(location.pathname)}`} replace />;
}

function PlatformPickerPage() {
  const { user, loading, logout, isAdmin } = useAuth();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const redirect = searchParams.get('redirect');

  if (loading) return <LoadingScreen />;
  if (!user) return <Navigate to="/login" replace />;

  const { group, redirectTo } = resolveAccess(user.platformAccess);
  // BUG-13/14 fix: a non-admin user whose PlatformAccess is 'None' has no
  // access to anything. Send them to access-denied instead of showing the
  // picker where they could click Slice/ALTRX and end up somewhere they
  // shouldn't be.
  if (!isAdmin && group === 'none') {
    return <Navigate to="/access-denied" replace />;
  }
  // Admins always see the picker (to access /admin), even if their email is in
  // ALTRX_ONLY or SLICE_ONLY. Non-admins in a single-platform group are still
  // auto-redirected to their platform.
  if (group !== 'both' && !isAdmin && redirectTo) {
    return <Navigate to={redirectTo} replace />;
  }

  const handleSelect = (platform: 'slice' | 'altrx' | 'admin') => {
    if (platform === 'admin') {
      console.log('[PlatformPicker] navigating to /admin');
      navigate('/admin', { replace: true });
      return;
    }
    const dest = platform === 'slice' ? ROUTES.slice : ROUTES.altrx;
    const final = redirect ?? dest;
    console.log('[PlatformPicker] navigating to', final);
    navigate(final, { replace: true });
  };

  return (
    <PlatformPickerModal
      userEmail={user.email}
      isAdmin={isAdmin}
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
      <Route
        path="/slice/reports-period"
        element={
          <SliceProtectedRoute>
            <SliceReportsPeriodPage />
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
      <Route
        path="/admin"
        element={
          <AdminRoute>
            <AdminPanelPage />
          </AdminRoute>
        }
      />
      <Route
        path="/pending-approval"
        element={<PendingApprovalPage />}
      />
      <Route
        path="/access-denied"
        element={<AccessDeniedPage />}
      />
      <Route
        path="/no-access"
        element={<NoAccessPage />}
      />
      <Route
        path="/twilio-costs"
        element={
          <AdminRoute>
            <TwilioCostsPage />
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
      <ErrorBoundary>
        <AuthProvider>
          <SliceAuthProvider>
            <ThemeProvider>
              <div className="grain-overlay" aria-hidden="true" />
              <AppRoutes />
            </ThemeProvider>
          </SliceAuthProvider>
        </AuthProvider>
      </ErrorBoundary>
    </BrowserRouter>
  );
}
