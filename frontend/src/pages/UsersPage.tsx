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
    <div className="space-y-6">
      <div className="flex items-center justify-between border-b border-whisper-border pb-4">
        <h1 className="font-headline-lg text-headline-lg text-primary tracking-tight">User Management</h1>
        <button
          onClick={() => setShowForm(!showForm)}
          className="bg-electric-blue text-white px-4 py-2 rounded font-medium text-sm hover:scale-[0.98] transition-transform shadow-sm"
        >
          {showForm ? 'Cancel' : 'Register User'}
        </button>
      </div>

      {msg && (
        <div
          className={`rounded-lg px-4 py-3 text-sm ${
            msg.includes('success') ? 'bg-emerald-signal/10 text-emerald-signal' : 'bg-deep-rose/10 text-deep-rose'
          }`}
        >
          {msg}
        </div>
      )}

      {showForm && (
        <form onSubmit={handleRegister} className="bg-pure-surface dark:bg-gray-900 border border-whisper-border dark:border-gray-800 rounded-xl p-6 space-y-4 shadow-diffused">
          <div>
            <label className="block text-sm text-secondary mb-1">Full Name</label>
            <input
              value={form.fullName}
              onChange={e => setForm(f => ({ ...f, fullName: e.target.value }))}
              required
              className="w-full px-3 py-2 rounded-lg border border-whisper-border bg-surface-container-low text-primary text-sm outline-none focus:border-electric-blue transition-colors"
            />
          </div>
          <div>
            <label className="block text-sm text-secondary mb-1">Email</label>
            <input
              type="email"
              value={form.email}
              onChange={e => setForm(f => ({ ...f, email: e.target.value }))}
              required
              className="w-full px-3 py-2 rounded-lg border border-whisper-border bg-surface-container-low text-primary text-sm outline-none focus:border-electric-blue transition-colors"
            />
          </div>
          <div>
            <label className="block text-sm text-secondary mb-1">Password</label>
            <input
              type="password"
              value={form.password}
              onChange={e => setForm(f => ({ ...f, password: e.target.value }))}
              required
              minLength={6}
              className="w-full px-3 py-2 rounded-lg border border-whisper-border bg-surface-container-low text-primary text-sm outline-none focus:border-electric-blue transition-colors"
            />
          </div>
          <div>
            <label className="block text-sm text-secondary mb-1">Role</label>
            <select
              value={form.role}
              onChange={e => setForm(f => ({ ...f, role: e.target.value }))}
              className="w-full px-3 py-2 rounded-lg border border-whisper-border bg-surface-container-low text-primary text-sm outline-none focus:border-electric-blue transition-colors"
            >
              <option value="Employee">Employee</option>
              <option value="Supervisor">Supervisor</option>
            </select>
          </div>
          <button
            type="submit"
            disabled={busy}
            className="bg-emerald-signal text-white px-4 py-2 rounded font-medium text-sm hover:scale-[0.98] transition-transform shadow-sm disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {busy ? 'Creating...' : 'Create User'}
          </button>
        </form>
      )}

      <div className="bg-pure-surface dark:bg-gray-900 border border-whisper-border dark:border-gray-800 rounded-xl shadow-diffused overflow-hidden">
        <table className="w-full text-left border-collapse">
          <thead>
            <tr className="bg-surface-container-low border-b border-whisper-border text-xs uppercase tracking-wider text-secondary font-metadata-mono">
              <th className="p-4 font-medium">Name</th>
              <th className="p-4 font-medium">Email</th>
              <th className="p-4 font-medium">Role</th>
              <th className="p-4 font-medium">Status</th>
              <th className="p-4 font-medium">Created</th>
            </tr>
          </thead>
          <tbody className="text-sm">
            {users.map(u => (
              <tr key={u.id} className="border-b border-whisper-border hover:bg-surface-container-lowest transition-colors">
                <td className="p-4 font-medium text-primary">{u.fullName}</td>
                <td className="p-4 text-secondary">{u.email}</td>
                <td className="p-4">
                  <span className="px-2 py-1 bg-surface-container rounded text-xs text-primary font-medium border border-whisper-border">{u.role}</span>
                </td>
                <td className="p-4">
                  <button
                    onClick={() => handleToggleActive(u)}
                    className={`px-3 py-1 rounded text-xs font-medium transition-colors border ${
                      u.isActive
                        ? 'bg-emerald-signal/10 text-emerald-signal border-emerald-signal/20'
                        : 'bg-deep-rose/10 text-deep-rose border-deep-rose/20'
                    }`}
                  >
                    {u.isActive ? 'Active' : 'Inactive'}
                  </button>
                </td>
                <td className="p-4 text-secondary font-metadata-mono">{new Date(u.createdAt).toLocaleDateString()}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
