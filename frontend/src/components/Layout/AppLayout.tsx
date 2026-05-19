import { useAuth } from '../../context/AuthContext';
import { useNavigate, useLocation } from 'react-router-dom';
import type { ReactNode } from 'react';

export default function AppLayout({ children }: { children: ReactNode }) {
  const { user, logout, isAdmin } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();

  return (
    <div className="app-layout">
      <header className="app-header">
        <div className="app-header-left">
          <h1>ALRrx <span>Operations</span></h1>
          <nav className="app-nav">
            <button
              className={`nav-btn ${location.pathname === '/' ? 'active' : ''}`}
              onClick={() => navigate('/')}
            >
              Dashboard
            </button>
            {isAdmin && (
              <button
                className={`nav-btn ${location.pathname === '/users' ? 'active' : ''}`}
                onClick={() => navigate('/users')}
              >
                Users
              </button>
            )}
          </nav>
        </div>
        <div className="app-header-right">
          <div className="user-menu">
            <span className="user-name">{user?.fullName}</span>
            <span className="user-role">{user?.role}</span>
            <button className="logout-btn" onClick={logout}>Sign out</button>
          </div>
        </div>
      </header>
      <main className="app-main">
        {children}
      </main>
      <footer className="app-footer">
        <span>ALRrx Operations Dashboard</span>
        <span>&copy; {new Date().getFullYear()}</span>
      </footer>
    </div>
  );
}
