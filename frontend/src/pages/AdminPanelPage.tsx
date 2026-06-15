import { useEffect, useState, useCallback } from 'react';
import {
  listUsers,
  approveUser,
  rejectUser,
  suspendUser,
  reactivateUser,
  changeUserRole,
  setUserPlatformAccess,
  getRoles,
  getUserDetail,
  type AdminUserDto,
  type RoleDto,
  type AuditLogEntry,
} from '../services/adminApi';
import type { PlatformAccess, UserStatus } from '../services/authApi';
import {
  CheckmarkCircle01Icon,
  Cancel01Icon,
  PauseIcon,
  PlayIcon,
  Key01Icon,
  Search01Icon,
  Loading03Icon,
  Globe02Icon,
  UserIcon,
  Shield01Icon,
  ShieldUserIcon,
} from 'hugeicons-react';

type Tab = 'Pending' | 'Active' | 'Suspended' | 'Rejected';
type ActionModal = 'approve' | 'reject' | 'suspend' | 'changeRole' | 'setAccess' | 'detail';

const PLATFORM_ACCESS_OPTIONS: { value: PlatformAccess; label: string; description: string }[] = [
  { value: 'Both',  label: 'Both (ALTRX + Slice)',  description: 'Access to both platforms' },
  { value: 'Altrx', label: 'ALTRX only',           description: 'Pharmacy CRM only' },
  { value: 'Slice', label: 'Slice only',           description: 'Pizza shop ops only' },
  { value: 'None',  label: 'No access',            description: 'Block from both platforms' },
];

const STATUS_COLORS: Record<UserStatus, { bg: string; text: string; label: string }> = {
  Pending:   { bg: 'bg-amber-warmth/10',  text: 'text-amber-warmth',  label: 'Pending' },
  Active:    { bg: 'bg-emerald-signal/10', text: 'text-emerald-signal', label: 'Active' },
  Suspended: { bg: 'bg-deep-rose/10',     text: 'text-deep-rose',     label: 'Suspended' },
  Rejected:  { bg: 'bg-muted-slate/10',   text: 'text-muted-slate',   label: 'Rejected' },
};

const ROLE_ICONS: Record<string, React.ComponentType<{ size?: number; className?: string }>> = {
  Admin: Shield01Icon,
  Supervisor: ShieldUserIcon,
  Employee: UserIcon,
  VicidialEditor: Key01Icon,
};

export default function AdminPanelPage() {
  const [tab, setTab] = useState<Tab>('Pending');
  const [search, setSearch] = useState('');
  const [users, setUsers] = useState<AdminUserDto[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize] = useState(20);
  const [roles, setRoles] = useState<RoleDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [actionTarget, setActionTarget] = useState<AdminUserDto | null>(null);
  const [actionModal, setActionModal] = useState<ActionModal | null>(null);
  const [actionReason, setActionReason] = useState('');
  const [actionRoleId, setActionRoleId] = useState<number>(0);
  const [actionPlatformAccess, setActionPlatformAccess] = useState<PlatformAccess>('None');
  const [actionBusy, setActionBusy] = useState(false);
  const [actionMsg, setActionMsg] = useState<{ kind: 'ok' | 'err'; text: string } | null>(null);

  const [detailAudit, setDetailAudit] = useState<AuditLogEntry[]>([]);
  const [detailLoading, setDetailLoading] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const result = await listUsers({ status: tab, search, page, pageSize });
      setUsers(result.items);
      setTotal(result.total);
    } catch (e: unknown) {
      setError((e as { response?: { data?: { error?: string } } })?.response?.data?.error ?? 'Error loading users');
    } finally {
      setLoading(false);
    }
  }, [tab, search, page, pageSize]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => { setPage(1); }, [tab, search]);

  useEffect(() => {
    getRoles().then(setRoles).catch(() => setRoles([]));
  }, []);

  const openAction = (u: AdminUserDto, modal: ActionModal) => {
    setActionTarget(u);
    setActionModal(modal);
    setActionReason('');
    setActionRoleId(u.roleId);
    setActionPlatformAccess(u.platformAccess);
    setActionMsg(null);
  };

  const closeAction = () => {
    setActionTarget(null);
    setActionModal(null);
    setActionReason('');
    setActionMsg(null);
  };

  const submitAction = async () => {
    if (!actionTarget) return;
    setActionBusy(true);
    setActionMsg(null);
    let emailFailed = false;
    try {
      if (actionModal === 'approve') {
        const res = await approveUser(actionTarget.id, actionRoleId);
        emailFailed = !res.emailSent;
        setActionMsg({
          kind: res.emailSent ? 'ok' : 'err',
          text: res.emailSent
            ? 'User approved — notification email sent'
            : `User approved — but email FAILED: ${res.emailError ?? 'unknown'}. The user will not know unless you contact them manually.`,
        });
      } else if (actionModal === 'reject') {
        if (!actionReason.trim()) { setActionBusy(false); setActionMsg({ kind: 'err', text: 'Reason required' }); return; }
        const res = await rejectUser(actionTarget.id, actionReason.trim());
        emailFailed = !res.emailSent;
        setActionMsg({
          kind: res.emailSent ? 'ok' : 'err',
          text: res.emailSent
            ? 'User rejected — notification email sent'
            : `User rejected — but email FAILED: ${res.emailError ?? 'unknown'}. The user will not know.`,
        });
      } else if (actionModal === 'suspend') {
        if (!actionReason.trim()) { setActionBusy(false); setActionMsg({ kind: 'err', text: 'Reason required' }); return; }
        const res = await suspendUser(actionTarget.id, actionReason.trim());
        emailFailed = !res.emailSent;
        setActionMsg({
          kind: res.emailSent ? 'ok' : 'err',
          text: res.emailSent
            ? 'User suspendido — notification email sent'
            : `User suspendido — but email FAILED: ${res.emailError ?? 'unknown'}. The user will not know.`,
        });
      } else if (actionModal === 'changeRole') {
        await changeUserRole(actionTarget.id, actionRoleId);
        setActionMsg({ kind: 'ok', text: 'Role updated' });
      } else if (actionModal === 'setAccess') {
        await setUserPlatformAccess(actionTarget.id, actionPlatformAccess);
        setActionMsg({
          kind: 'ok',
          text: `Platform access set to ${actionPlatformAccess}. User will be asked to re-login.`,
        });
      } else if (actionModal === 'detail') {
        setDetailLoading(true);
        const detail = await getUserDetail(actionTarget.id);
        setDetailAudit(detail.audit);
        setDetailLoading(false);
        return;
      }
      await load();
      // Don't auto-close if email failed — admin must see and act on the warning
      if (!emailFailed) {
        setTimeout(closeAction, 1500);
      }
    } catch (e: unknown) {
      setActionMsg({ kind: 'err', text: (e as { response?: { data?: { error?: string } } })?.response?.data?.error ?? 'Action failed' });
    } finally {
      setActionBusy(false);
    }
  };

  const reactivate = async (u: AdminUserDto) => {
    try {
      await reactivateUser(u.id);
      await load();
    } catch {/* ignore */}
  };

  const totalPages = Math.max(1, Math.ceil(total / pageSize));

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between border-b border-whisper-border dark:border-gray-700 pb-4 flex-wrap gap-3">
        <div>
          <h1 className="font-headline-lg text-headline-lg text-primary dark:text-white tracking-tight">Admin Panel</h1>
          <p className="text-sm text-secondary dark:text-gray-400 mt-1">Manage user access, roles, and permissions.</p>
        </div>
      </div>

      <div className="flex items-center gap-2 flex-wrap">
        {(['Pending', 'Active', 'Suspended', 'Rejected'] as Tab[]).map((t) => (
          <button
            key={t}
            onClick={() => setTab(t)}
            className={
              tab === t
                ? 'px-4 py-2 rounded-lg bg-electric-blue text-white font-medium shadow-sm text-sm'
                : 'px-4 py-2 rounded-lg bg-pure-surface dark:bg-gray-800 text-secondary dark:text-gray-300 hover:bg-card-icon-bg dark:hover:bg-gray-700 font-medium border border-whisper-border dark:border-gray-700 text-sm'
            }
          >
            {t}
          </button>
        ))}
        <div className="ml-auto relative">
          <Search01Icon size={16} className="absolute left-3 top-1/2 -translate-y-1/2 text-muted-slate pointer-events-none" />
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search by email or name..."
            className="pl-9 pr-3 py-2 rounded-lg border border-whisper-border dark:border-gray-700 bg-pure-surface dark:bg-gray-800 text-primary dark:text-gray-100 text-sm outline-none focus:border-electric-blue w-64"
          />
        </div>
      </div>

      {error && (
        <div className="rounded-lg border border-deep-rose/40 bg-deep-rose/10 text-deep-rose px-4 py-3 text-sm">
          {error}
        </div>
      )}

      <div className="bg-pure-surface dark:bg-gray-900 border border-whisper-border dark:border-gray-800 rounded-xl shadow-card overflow-hidden">
        <table className="w-full text-left border-collapse">
          <thead>
            <tr className="bg-card-icon-bg/50 dark:bg-gray-800/50 border-b border-whisper-border dark:border-gray-800 text-xs uppercase tracking-wider text-secondary dark:text-gray-400 font-metadata-mono">
              <th className="p-4 font-medium">User</th>
              <th className="p-4 font-medium">Role</th>
              <th className="p-4 font-medium">Status</th>
              <th className="p-4 font-medium">Platform</th>
              <th className="p-4 font-medium">Last login</th>
              <th className="p-4 font-medium text-right">Actions</th>
            </tr>
          </thead>
          <tbody className="text-sm">
            {loading ? (
              <tr>
                <td colSpan={6} className="p-8 text-center text-secondary dark:text-gray-400">
                  <Loading03Icon size={20} className="inline animate-spin mr-2" />
                  Loading...
                </td>
              </tr>
            ) : users.length === 0 ? (
              <tr>
                <td colSpan={6} className="p-8 text-center text-muted-slate dark:text-gray-500 text-sm">
                  No {tab.toLowerCase()} users.
                </td>
              </tr>
            ) : users.map(u => {
              const status = STATUS_COLORS[u.status];
              const RoleIcon = ROLE_ICONS[u.role] ?? UserIcon;
              return (
                <tr key={u.id} className="border-b border-whisper-border dark:border-gray-800 hover:bg-card-icon-bg/30 dark:hover:bg-gray-800/30 transition-colors">
                  <td className="p-4">
                    <div className="font-medium text-primary dark:text-gray-100">{u.fullName}</div>
                    <div className="text-xs text-muted-slate dark:text-gray-500">{u.email}</div>
                  </td>
                  <td className="p-4">
                    <span className="inline-flex items-center gap-1.5 px-2 py-1 rounded text-xs font-medium border border-whisper-border dark:border-gray-700 bg-card-icon-bg dark:bg-gray-800 text-primary dark:text-gray-200">
                      <RoleIcon size={12} />
                      {u.role}
                    </span>
                  </td>
                  <td className="p-4">
                    <span className={`inline-block px-2 py-1 rounded text-xs font-medium ${status.bg} ${status.text}`}>
                      {status.label}
                    </span>
                  </td>
                  <td className="p-4">
                    <span className="inline-flex items-center gap-1 px-2 py-1 rounded text-xs font-medium border border-whisper-border dark:border-gray-700 bg-card-icon-bg dark:bg-gray-800 text-primary dark:text-gray-200">
                      <Globe02Icon size={12} />
                      {u.platformAccess}
                    </span>
                  </td>
                  <td className="p-4 text-secondary dark:text-gray-400 text-xs font-metadata-mono">
                    {u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleString() : '—'}
                  </td>
                  <td className="p-4">
                    <div className="flex items-center gap-1 justify-end flex-wrap">
                      {u.status === 'Pending' && (
                        <>
                          <button onClick={() => openAction(u, 'approve')} className="inline-flex items-center gap-1 px-2 py-1 rounded text-xs font-medium bg-emerald-signal/10 text-emerald-signal border border-emerald-signal/20 hover:bg-emerald-signal/20">
                            <CheckmarkCircle01Icon size={12} /> Approve
                          </button>
                          <button onClick={() => openAction(u, 'reject')} className="inline-flex items-center gap-1 px-2 py-1 rounded text-xs font-medium bg-deep-rose/10 text-deep-rose border border-deep-rose/20 hover:bg-deep-rose/20">
                            <Cancel01Icon size={12} /> Reject
                          </button>
                        </>
                      )}
                      {u.status === 'Active' && (
                        <>
                          <button onClick={() => openAction(u, 'setAccess')} className="inline-flex items-center gap-1 px-2 py-1 rounded text-xs font-medium border border-whisper-border dark:border-gray-700 text-primary dark:text-gray-200 hover:bg-card-icon-bg dark:hover:bg-gray-800" title="Set platform access">
                            <Globe02Icon size={12} /> Access
                          </button>
                          <button onClick={() => openAction(u, 'changeRole')} className="inline-flex items-center gap-1 px-2 py-1 rounded text-xs font-medium border border-whisper-border dark:border-gray-700 text-primary dark:text-gray-200 hover:bg-card-icon-bg dark:hover:bg-gray-800">
                            <Shield01Icon size={12} /> Change role
                          </button>
                          <button onClick={() => openAction(u, 'suspend')} className="inline-flex items-center gap-1 px-2 py-1 rounded text-xs font-medium bg-amber-warmth/10 text-amber-warmth border border-amber-warmth/20 hover:bg-amber-warmth/20">
                            <PauseIcon size={12} /> Suspend
                          </button>
                        </>
                      )}
                      {(u.status === 'Suspended' || u.status === 'Rejected') && (
                        <button onClick={() => reactivate(u)} className="inline-flex items-center gap-1 px-2 py-1 rounded text-xs font-medium bg-emerald-signal/10 text-emerald-signal border border-emerald-signal/20 hover:bg-emerald-signal/20">
                          <PlayIcon size={12} /> Reactivate
                        </button>
                      )}
                      <button onClick={() => openAction(u, 'detail')} className="inline-flex items-center gap-1 px-2 py-1 rounded text-xs font-medium border border-whisper-border dark:border-gray-700 text-secondary dark:text-gray-300 hover:bg-card-icon-bg dark:hover:bg-gray-800">
                        <UserIcon size={12} /> Detail
                      </button>
                    </div>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
        {totalPages > 1 && (
          <div className="flex items-center justify-between px-4 py-3 border-t border-whisper-border dark:border-gray-800 text-sm text-secondary dark:text-gray-400">
            <span>Page {page} of {totalPages} ({total} total)</span>
            <div className="flex gap-2">
              <button onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page === 1} className="px-3 py-1 rounded border border-whisper-border dark:border-gray-700 disabled:opacity-50">Prev</button>
              <button onClick={() => setPage(p => Math.min(totalPages, p + 1))} disabled={page === totalPages} className="px-3 py-1 rounded border border-whisper-border dark:border-gray-700 disabled:opacity-50">Next</button>
            </div>
          </div>
        )}
      </div>

      {actionModal && actionTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/50 backdrop-blur-sm" onClick={closeAction}>
          <div className="bg-pure-surface dark:bg-gray-900 rounded-xl shadow-card border border-whisper-border dark:border-gray-700 p-6 max-w-lg w-full" onClick={(e) => e.stopPropagation()}>
            <h3 className="font-display-hero text-lg text-primary dark:text-white mb-1">
              {actionModal === 'approve' && `Approve ${actionTarget.fullName}`}
              {actionModal === 'reject' && `Reject ${actionTarget.fullName}`}
              {actionModal === 'suspend' && `Suspend ${actionTarget.fullName}`}
              {actionModal === 'changeRole' && `Change role of ${actionTarget.fullName}`}
              {actionModal === 'setAccess' && `Platform access for ${actionTarget.fullName}`}
              {actionModal === 'detail' && `User detail: ${actionTarget.fullName}`}
            </h3>
            <p className="text-xs text-muted-slate dark:text-gray-500 mb-4">{actionTarget.email}</p>

            {actionModal === 'approve' && (
              <div className="mb-4">
                <label className="block text-sm text-secondary mb-1">Assign role</label>
                <select
                  value={actionRoleId}
                  onChange={(e) => setActionRoleId(Number(e.target.value))}
                  className="w-full px-3 py-2 rounded-lg border border-whisper-border dark:border-gray-700 bg-pure-surface dark:bg-gray-800 text-primary dark:text-gray-100 text-sm outline-none focus:border-electric-blue"
                >
                  {roles.map(r => (
                    <option key={r.id} value={r.id}>{r.name}</option>
                  ))}
                </select>
              </div>
            )}

            {actionModal === 'changeRole' && (
              <div className="mb-4">
                <label className="block text-sm text-secondary mb-1">New role</label>
                <select
                  value={actionRoleId}
                  onChange={(e) => setActionRoleId(Number(e.target.value))}
                  className="w-full px-3 py-2 rounded-lg border border-whisper-border dark:border-gray-700 bg-pure-surface dark:bg-gray-800 text-primary dark:text-gray-100 text-sm outline-none focus:border-electric-blue"
                >
                  {roles.map(r => (
                    <option key={r.id} value={r.id}>{r.name}</option>
                  ))}
                </select>
              </div>
            )}

            {actionModal === 'setAccess' && (
              <div className="mb-4 space-y-2">
                <p className="text-xs text-muted-slate dark:text-gray-500 mb-2">
                  Currently: <span className="font-medium text-primary dark:text-gray-200">{actionTarget.platformAccess}</span>.
                  Changing this will revoke the user's session and require them to re-login.
                </p>
                {PLATFORM_ACCESS_OPTIONS.map((opt) => (
                  <label
                    key={opt.value}
                    className={`flex items-start gap-3 p-3 rounded-lg border cursor-pointer transition-colors ${
                      actionPlatformAccess === opt.value
                        ? 'border-electric-blue bg-electric-blue/5'
                        : 'border-whisper-border dark:border-gray-700 hover:bg-card-icon-bg dark:hover:bg-gray-800'
                    }`}
                  >
                    <input
                      type="radio"
                      name="platformAccess"
                      value={opt.value}
                      checked={actionPlatformAccess === opt.value}
                      onChange={(e) => setActionPlatformAccess(e.target.value as PlatformAccess)}
                      className="mt-1"
                    />
                    <div>
                      <div className="text-sm font-medium text-primary dark:text-gray-100">{opt.label}</div>
                      <div className="text-xs text-muted-slate dark:text-gray-500">{opt.description}</div>
                    </div>
                  </label>
                ))}
              </div>
            )}

            {(actionModal === 'reject' || actionModal === 'suspend') && (
              <div className="mb-4">
                <label className="block text-sm text-secondary mb-1">Reason <span className="text-deep-rose">*</span></label>
                <textarea
                  value={actionReason}
                  onChange={(e) => setActionReason(e.target.value)}
                  rows={3}
                  placeholder="Provide a clear reason — this is sent to the user via email"
                  className="w-full px-3 py-2 rounded-lg border border-whisper-border dark:border-gray-700 bg-pure-surface dark:bg-gray-800 text-primary dark:text-gray-100 text-sm outline-none focus:border-electric-blue"
                />
              </div>
            )}

            {actionModal === 'detail' && (
              <div className="mb-4 max-h-80 overflow-y-auto">
                {detailLoading ? (
                  <p className="text-sm text-secondary dark:text-gray-400"><Loading03Icon size={16} className="inline animate-spin mr-2" />Loading audit...</p>
                ) : detailAudit.length === 0 ? (
                  <p className="text-sm text-muted-slate dark:text-gray-500">No audit entries for this user.</p>
                ) : (
                  <ul className="space-y-2 text-xs">
                    {detailAudit.map(a => (
                      <li key={a.id} className="border border-whisper-border dark:border-gray-700 rounded p-2">
                        <div className="flex items-center justify-between">
                          <span className="font-mono font-medium text-primary dark:text-gray-100">{a.action}</span>
                          <span className="text-muted-slate dark:text-gray-500">{new Date(a.createdAt).toLocaleString()}</span>
                        </div>
                        {a.oldValue && <div className="text-muted-slate dark:text-gray-500">from: {a.oldValue}</div>}
                        {a.newValue && <div className="text-muted-slate dark:text-gray-500">to: {a.newValue}</div>}
                        {a.reason && <div className="text-muted-slate dark:text-gray-500">reason: {a.reason}</div>}
                        {a.performedBy && <div className="text-muted-slate dark:text-gray-500">by user #{a.performedBy}</div>}
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            )}

            {actionMsg && (
              <div className={`mb-4 px-3 py-2 rounded text-sm ${
                actionMsg.kind === 'ok'
                  ? 'bg-emerald-signal/10 text-emerald-signal'
                  : 'bg-deep-rose/10 text-deep-rose'
              }`}>
                {actionMsg.text}
              </div>
            )}

            <div className="flex items-center justify-end gap-2">
              <button onClick={closeAction} className="px-4 py-2 rounded-lg text-sm font-medium text-secondary dark:text-gray-300 hover:bg-card-icon-bg dark:hover:bg-gray-800 transition-colors">
                Cancel
              </button>
              {actionModal !== 'detail' && (
                <button
                  onClick={submitAction}
                  disabled={actionBusy}
                  className="px-4 py-2 rounded-lg text-sm font-medium bg-electric-blue text-white hover:scale-[0.98] transition-transform shadow-sm disabled:opacity-50"
                >
                  {actionBusy ? 'Processing...' : 'Confirm'}
                </button>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
