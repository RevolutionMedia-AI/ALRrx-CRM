import { Component, type ErrorInfo, type ReactNode } from 'react';

interface ErrorBoundaryProps {
  children: ReactNode;
}

interface ErrorBoundaryState {
  hasError: boolean;
  error: Error | null;
}

/**
 * Top-level error boundary. Catches any uncaught render-time error from
 * descendants and shows a recovery UI instead of the default React
 * behaviour, which is to unmount the whole tree and leave the user
 * staring at a blank page.
 *
 * The "Reload" button is the escape hatch — it forces a hard refresh
 * so the React tree is rebuilt from scratch. The optional "Go to login"
 * button is the safe fallback when the error is in the auth path.
 */
export default class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { hasError: false, error: null };

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // Log to the browser console so the developer can see the stack.
    // In production this would go to Sentry / Datadog / etc.
    // eslint-disable-next-line no-console
    console.error('[ErrorBoundary] Uncaught error:', error, info.componentStack);
  }

  handleReload = () => {
    window.location.reload();
  };

  handleGoToLogin = () => {
    try {
      localStorage.removeItem('auth_token');
      localStorage.removeItem('google_access_token');
    } catch {
      // ignore — storage may be disabled
    }
    window.location.assign('/login');
  };

  render() {
    if (!this.state.hasError) return this.props.children;

    return (
      <div className="min-h-screen flex items-center justify-center bg-canvas-white dark:bg-gray-950 px-4">
        <div className="max-w-md w-full bg-pure-surface dark:bg-gray-900 border border-whisper-border dark:border-gray-800 rounded-xl shadow-card p-8 text-center">
          <div className="w-16 h-16 mx-auto mb-4 rounded-full bg-deep-rose/10 flex items-center justify-center">
            <span
              className="material-symbols-outlined text-deep-rose"
              style={{ fontSize: '32px' }}
            >
              error_outline
            </span>
          </div>
          <h1 className="font-display-hero text-2xl text-primary dark:text-white mb-2">
            Something went wrong
          </h1>
          <p className="text-secondary dark:text-gray-400 text-sm leading-relaxed mb-2">
            The page hit an unexpected error and could not be rendered. Your session and
            data are safe — reloading will rebuild the interface.
          </p>
          {this.state.error && (
            <pre className="text-left text-xs text-muted-slate dark:text-gray-500 bg-surface-container-low dark:bg-gray-800 rounded p-3 mb-6 overflow-auto max-h-32 font-mono">
              {this.state.error.message}
            </pre>
          )}
          <div className="flex gap-2 justify-center">
            <button
              onClick={this.handleReload}
              className="inline-flex items-center gap-2 px-4 py-2 rounded-lg bg-primary text-white text-sm font-medium hover:scale-[0.98] transition-transform shadow-sm"
            >
              <span className="material-symbols-outlined text-base">refresh</span>
              Reload
            </button>
            <button
              onClick={this.handleGoToLogin}
              className="inline-flex items-center gap-2 px-4 py-2 rounded-lg border border-whisper-border dark:border-gray-700 text-secondary dark:text-gray-300 text-sm font-medium hover:bg-card-icon-bg dark:hover:bg-gray-800 transition-colors"
            >
              Sign out and go to login
            </button>
          </div>
        </div>
      </div>
    );
  }
}
