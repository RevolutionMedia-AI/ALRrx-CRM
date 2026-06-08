import { BrowserRouter, Routes, Route, Navigate, useLocation } from 'react-router-dom';
import { AuthProvider, useAuth } from './context/AuthContext';
import { ThemeProvider } from './context/ThemeContext';
import { SliceAuthProvider, useSliceAuth } from './slice/context/SliceAuthContext';
import LoginPage from './pages/LoginPage';
import DashboardPage from './pages/DashboardPage';
import AnalyticsPage from './pages/AnalyticsPage';
import RealTimePage from './pages/RealTimePage';
import UsersPage from './pages/UsersPage';
import VicidialFormPage from './pages/VicidialFormPage';
import AppSelectorPage from './pages/AppSelectorPage';
import AppLayout from './components/Layout/AppLayout';
import SliceLoginPage from './slice/pages/SliceLoginPage';
import SliceShopOverviewPage from './slice/pages/SliceShopOverviewPage';
import SliceAgentOverviewPage from './slice/pages/SliceAgentOverviewPage';
import SliceFileUploadPage from './slice/pages/SliceFileUploadPage';
import SlicePlaceholderPage from './slice/pages/SlicePlaceholderPage';
import SliceLayout from './slice/components/SliceLayout';
import AppChooserPage from './pages/AppChooserPage';
import { shouldShowAppChooser } from './utils/appChooser';
import type { ReactNode } from 'react';

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
  if (!user || user.role !== 'Admin') return <Navigate to="/" replace />;
  return <AppLayout>{children}</AppLayout>;
}

function SliceProtectedRoute({ children }: { children: ReactNode }) {
  const { user, loading } = useSliceAuth();
  if (loading) return <LoadingScreen />;
  if (!user) {
    const location = useLocation();
    return <Navigate to={`/slice/login?redirect=${encodeURIComponent(location.pathname)}`} replace />;
  }
  return <SliceLayout>{children}</SliceLayout>;
}

function ChooseRoute() {
  const alrrx = useAuth();
  const slice = useSliceAuth();
  if (alrrx.loading || slice.loading) return <LoadingScreen />;
  const email = alrrx.user?.email ?? slice.user?.email;
  if (!email) {
    return <Navigate to="/login" replace />;
  }
  if (!shouldShowAppChooser(email)) {
    return <Navigate to={slice.user ? '/slice' : '/'} replace />;
  }
  return <AppChooserPage />;
}

function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/choose" element={<ChooseRoute />} />

      <Route path="/slice/login" element={<SliceLoginPage />} />

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
            <SlicePlaceholderPage
              title="POD Overview"
              description="Aggregated call-center metrics by POD."
              icon="dashboard"
            />
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
            <SlicePlaceholderPage
              title="History & Audit"
              description="Past reports and edit audit log."
              icon="history"
            />
          </SliceProtectedRoute>
        }
      />

      <Route path="/form_sale" element={<VicidialFormPage />} />
      <Route path="/altrx" element={<Navigate to="/" replace />} />
      <Route path="/" element={<AppSelectorPage />} />
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
      <Route path="*" element={<Navigate to="/" replace />} />
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
