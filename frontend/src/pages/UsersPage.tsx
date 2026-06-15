import { useEffect, useState } from 'react';
import { getUsers, register, updateUser, type UserInfo, type RegisterRequest } from '../services/authApi';
import { getRoles, type RoleDto } from '../services/adminApi';

export default function UsersPage() {
  const [users, setUsers] = useState<UserInfo[]>([]);
  const [roles, setRoles] = useState<RoleDto[]>([]);
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState<RegisterRequest>({ email: '', fullName: '', roleId: 0 });
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<{ kind: 'ok' | 'err'; text: string } | null>(null);

  const load = async () => {
    try {
      const [usersData, rolesData] = await Promise.all([getUsers(), getRoles()]);
      setUsers(usersData);
      setRoles(rolesData);
      if (rolesData.length > 0 && form.roleId === 0) {
        setForm(f => ({ ...f, roleId: rolesData.find(r => r.name === 'Employee')?.id ?? rolesData[0].id }));
      }
    } catch { /* ignore */ }
  };

  useEffect(() => { load(); }, []);

  const handleRegister = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setMsg(null);
    try {
      await register(form);
      setShowForm(false);
      const employeeRole = roles.find(r => r.name === 'Employee')?.id ?? 0;
      setForm({ email: '', fullName: '', roleId: employeeRole });
      await load();
      setMsg({ kind: 'ok', text: 'User registered successfully' });
    } catch (err: unknown) {
      const e = err as { response?: { data?: { error?: string } } };
      setMsg({ kind: 'err', text: e?.response?.data?.error ?? 'Registration failed' });
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
      <div className="flex items-center justify-between border-b border-whisper-border dark:border-gray-700 pb-4">
        <h1 className="font-headline-lg text-headline-lg text-primary dark:text-white tracking-tight">User Management</h1>
        <button
          onClick={() => setShowForm(!showForm)}
          className="bg-electric-blue text-white px-4 py-2 rounded font-medium text-sm hover:scale-[0.98] transition-transform shadow-sm"
        >
          {showForm ? 'Cancel' : 'Register User'}
        </button>
      </div>

      {msg && (
        <div className={`rounded-lg px-4 py-3 text-sm ${
          msg.kind === 'ok' ? 'bg-emerald-signal/10 text-emerald-signal' : 'bg-deep-rose/10 text-deep-rose'
        }`}>
          {msg.text}
        </div>
      )}

      {showForm && (
        <form onSubmit={handleRegister} className="bg-pure-surface dark:bg-gray-900 border border-whisper-border dark:border-gray-800 rounded-xl p-6 space-y-4 shadow-card">
          <div>
            <label className="block text-sm text-secondary mb-1">Full Name</label>
            <input
              value={form.fullName}
              onChange={e => setForm(f => ({ ...f, fullName: e.target.value }))}
              required
              className="w-full px-3 py-2 rounded-lg border border-whisper-border dark:border-gray-700 bg-pure-surface dark:bg-gray-800 text-primary dark:text-gray-100 text-sm outline-none focus:border-electric-blue transition-colors"
            />
          </div>
          <div>
            <label className="block text-sm text-secondary mb-1">Email</label>
            <input
              type="email"
              value={form.email}
              onChange={e => setForm(f => ({ ...f, email: e.target.value }))}
              required
              className="w-full px-3 py-2 rounded-lg border border-whisper-border dark:border-gray-700 bg-pure-surface dark:bg-gray-800 text-primary dark:text-gray-100 text-sm outline-none focus:border-electric-blue transition-colors"
            />
          </div>
          <div>
            <label className="block text-sm text-secondary mb-1">Role</label>
            <select
              value={form.roleId}
              onChange={e => setForm(f => ({ ...f, roleId: Number(e.target.value) }))}
              className="w-full px-3 py-2 rounded-lg border border-whisper-border dark:border-gray-700 bg-pure-surface dark:bg-gray-800 text-primary dark:text-gray-100 text-sm outline-none focus:border-electric-blue transition-colors"
            >
              {roles.filter(r => r.name !== 'Admin').map(r => (
                <option key={r.id} value={r.id}>{r.name}</option>
              ))}
            </select>
            <p className="text-xs text-muted-slate dark:text-gray-500 mt-1">Admin role can only be assigned by an existing admin via the Admin Panel.</p>
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

      <div className="bg-pure-surface dark:bg-gray-900 border border-whisper-border dark:border-gray-800 rounded-xl shadow-card overflow-hidden">
        <table className="w-full text-left border-collapse">
          <thead>
            <tr className="bg-card-icon-bg/50 dark:bg-gray-800/50 border-b border-whisper-border dark:border-gray-800 text-xs uppercase tracking-wider text-secondary dark:text-gray-400 font-metadata-mono">
              <th className="p-4 font-medium">Name</th>
              <th className="p-4 font-medium">Email</th>
              <th className="p-4 font-medium">Role</th>
              <th className="p-4 font-medium">Status</th>
              <th className="p-4 font-medium">Created</th>
            </tr>
          </thead>
          <tbody className="text-sm">
            {users.map(u => (
              <tr key={u.id} className="border-b border-whisper-border dark:border-gray-800 hover:bg-card-icon-bg/30 dark:hover:bg-gray-800/30 transition-colors">
                <td className="p-4 font-medium text-primary dark:text-gray-100">{u.fullName}</td>
                <td className="p-4 text-secondary dark:text-gray-400">{u.email}</td>
                <td className="p-4">
                  <span className="px-2 py-1 bg-card-icon-bg dark:bg-gray-800 rounded text-xs text-primary dark:text-gray-200 font-medium border border-whisper-border dark:border-gray-700">{u.role}</span>
                </td>
                <td className="p-4">
                  <button
                    onClick={() => handleToggleActive(u)}
                    className={`px-3 py-1 rounded text-xs font-medium transition-colors border ${
                      u.status === 'Active'
                        ? 'bg-emerald-signal/10 text-emerald-signal border-emerald-signal/20'
                        : u.status === 'Pending'
                        ? 'bg-amber-warmth/10 text-amber-warmth border-amber-warmth/20'
                        : 'bg-deep-rose/10 text-deep-rose border-deep-rose/20'
                    }`}
                  >
                    {u.status}
                  </button>
                </td>
                <td className="p-4 text-secondary dark:text-gray-400 font-metadata-mono text-xs">{new Date(u.createdAt).toLocaleDateString()}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
