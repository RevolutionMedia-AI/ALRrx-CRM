import { useEffect, useState } from 'react';
import { getUsers, register, updateUser, type UserInfo, type RegisterRequest } from '../services/authApi';

export default function UsersPage() {
  const [users, setUsers] = useState<UserInfo[]>([]);
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState<RegisterRequest>({ email: '', password: '', fullName: '', role: 'Employee' });
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState('');

  const load = async () => {
    try {
      const data = await getUsers();
      setUsers(data);
    } catch { /* ignore */ }
  };

  useEffect(() => { load(); }, []);

  const handleRegister = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setMsg('');
    try {
      await register(form);
      setShowForm(false);
      setForm({ email: '', password: '', fullName: '', role: 'Employee' });
      await load();
      setMsg('User registered successfully');
    } catch (err: any) {
      setMsg(err?.response?.data?.error || 'Registration failed');
    } finally {
      setBusy(false);
    }
  };

  const handleToggleActive = async (user: UserInfo) => {
    try {
      await updateUser(user.id, { isActive: !user.isActive });
      await load();
    } catch { /* ignore */ }
  };

  return (
    <div className="users-page">
      <div className="page-header">
        <h2>User Management</h2>
        <button className="apply-btn" onClick={() => setShowForm(!showForm)}>
          {showForm ? 'Cancel' : 'Register User'}
        </button>
      </div>

      {msg && <div className="login-error" style={{ background: msg.includes('success') ? '#ecfdf5' : '#fef2f2', color: msg.includes('success') ? '#065f46' : '#991b1b' }}>{msg}</div>}

      {showForm && (
        <form onSubmit={handleRegister} className="user-form">
          <div className="login-field"><label>Full Name</label><input value={form.fullName} onChange={e => setForm(f => ({ ...f, fullName: e.target.value }))} required /></div>
          <div className="login-field"><label>Email</label><input type="email" value={form.email} onChange={e => setForm(f => ({ ...f, email: e.target.value }))} required /></div>
          <div className="login-field"><label>Password</label><input type="password" value={form.password} onChange={e => setForm(f => ({ ...f, password: e.target.value }))} required minLength={6} /></div>
          <div className="login-field">
            <label>Role</label>
            <select value={form.role} onChange={e => setForm(f => ({ ...f, role: e.target.value }))}>
              <option value="Employee">Employee</option>
              <option value="Supervisor">Supervisor</option>
            </select>
          </div>
          <button type="submit" className="login-btn" disabled={busy}>{busy ? 'Creating...' : 'Create User'}</button>
        </form>
      )}

      <div className="table-container">
        <table className="report-table">
          <thead>
            <tr>
              <th>Name</th>
              <th>Email</th>
              <th>Role</th>
              <th>Status</th>
              <th>Created</th>
            </tr>
          </thead>
          <tbody>
            {users.map(u => (
              <tr key={u.id}>
                <td>{u.fullName}</td>
                <td>{u.email}</td>
                <td><span className="status-badge">{u.role}</span></td>
                <td>
                  <button
                    className={`toggle-btn ${u.isActive ? 'active' : ''}`}
                    onClick={() => handleToggleActive(u)}
                  >
                    {u.isActive ? 'Active' : 'Inactive'}
                  </button>
                </td>
                <td className="cell-date">{new Date(u.createdAt).toLocaleDateString()}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
