import type { VicidialLeadDto } from '../../types';

export type LeadLookupState =
  | { kind: 'idle' }
  | { kind: 'loading'; leadId: number }
  | { kind: 'success'; lead: VicidialLeadDto }
  | { kind: 'not-found'; leadId: number }
  | { kind: 'connection-error'; message: string }
  | { kind: 'invalid'; message: string };

interface VLeadBannerProps {
  state: LeadLookupState;
}

function MaskPhone(phone: string): string {
  if (!phone) return '—';
  if (phone.length <= 4) return phone;
  return `${phone.slice(0, 3)}••• ${phone.slice(-4)}`;
}

function MaskEmail(email: string): string {
  if (!email) return '—';
  const [user, domain] = email.split('@');
  if (!domain) return email;
  const maskedUser = user.length <= 2 ? user : `${user[0]}${'•'.repeat(Math.max(1, user.length - 2))}${user[user.length - 1]}`;
  return `${maskedUser}@${domain}`;
}

export default function VLeadBanner({ state }: VLeadBannerProps) {
  if (state.kind === 'idle') {
    return (
      <div className="mb-4 px-3 py-2 rounded-lg bg-surface-container-low dark:bg-gray-800 border border-whisper-border dark:border-gray-700 text-xs text-secondary dark:text-gray-400 flex items-center gap-2">
        <span className="material-symbols-outlined text-[16px]">info</span>
        <span>Entrada manual: VICIdial no envió un <code className="font-metadata-mono text-[11px]">lead_id</code>. Complete los datos del cliente.</span>
      </div>
    );
  }

  if (state.kind === 'loading') {
    return (
      <div className="mb-4 px-3 py-2.5 rounded-lg bg-electric-blue/5 border border-electric-blue/20 text-electric-blue text-sm flex items-center gap-2">
        <span className="material-symbols-outlined text-[18px] animate-spin">progress_activity</span>
        <span>Buscando lead <span className="font-metadata-mono font-semibold">#{state.leadId}</span> en VICIdial…</span>
      </div>
    );
  }

  if (state.kind === 'success') {
    const fullName = `${state.lead.firstName} ${state.lead.lastName}`.trim() || '—';
    return (
      <div className="mb-4 px-3 py-2.5 rounded-lg bg-emerald-signal/10 border border-emerald-signal/30 text-emerald-signal text-sm flex items-center gap-2">
        <span className="material-symbols-outlined text-[18px]">link</span>
        <div className="flex-1 flex flex-wrap items-center gap-x-3 gap-y-0.5">
          <span className="font-semibold">VICIdial Lead #{state.lead.leadId}</span>
          <span className="text-primary dark:text-gray-100">{fullName}</span>
          <span className="font-metadata-mono text-xs text-secondary dark:text-gray-400">{MaskPhone(state.lead.phoneNumber)}</span>
          <span className="font-metadata-mono text-xs text-secondary dark:text-gray-400">{MaskEmail(state.lead.email)}</span>
        </div>
      </div>
    );
  }

  if (state.kind === 'not-found') {
    return (
      <div className="mb-4 px-3 py-2.5 rounded-lg bg-deep-rose/10 border border-deep-rose/30 text-deep-rose text-sm flex items-center gap-2">
        <span className="material-symbols-outlined text-[18px]">error</span>
        <div className="flex-1">
          <p className="font-semibold">No se encontró el lead #{state.leadId} en VICIdial.</p>
          <p className="text-xs text-secondary dark:text-gray-400 mt-0.5">Puede continuar y registrar la venta manualmente.</p>
        </div>
      </div>
    );
  }

  if (state.kind === 'connection-error') {
    return (
      <div className="mb-4 px-3 py-2.5 rounded-lg bg-amber-warmth/10 border border-amber-warmth/30 text-amber-warmth text-sm flex items-center gap-2">
        <span className="material-symbols-outlined text-[18px]">wifi_off</span>
        <div className="flex-1">
          <p className="font-semibold">No se pudo conectar con VICIdial.</p>
          <p className="text-xs text-secondary dark:text-gray-400 mt-0.5">{state.message} — puede continuar e ingresar los datos manualmente.</p>
        </div>
      </div>
    );
  }

  return (
    <div className="mb-4 px-3 py-2.5 rounded-lg bg-deep-rose/10 border border-deep-rose/30 text-deep-rose text-sm flex items-center gap-2">
      <span className="material-symbols-outlined text-[18px]">error</span>
      <span className="flex-1">{state.message}</span>
    </div>
  );
}
