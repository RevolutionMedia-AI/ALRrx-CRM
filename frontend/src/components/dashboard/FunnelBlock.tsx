interface FunnelBlockProps {
  dialed: number | null;
  contacted: number | null;
  sales: number;
  outboundCalls?: number | null;
  inboundCalls?: number | null;
  loading?: boolean;
}

interface StageProps {
  label: string;
  value: number | null;
  loading?: boolean;
  gradient: string;
  ringColor: string;
  textColor: string;
  emptyText?: string;
}

function Stage({ label, value, loading, gradient, ringColor, textColor, emptyText }: StageProps) {
  const isEmpty = value === null || value === 0;
  return (
    <div
      className={`relative flex-1 min-w-0 rounded-xl border ${ringColor} ${gradient} backdrop-blur-sm p-4 sm:p-5 shadow-sm`}
    >
      <p className="text-[10px] sm:text-xs font-semibold text-secondary uppercase tracking-wider mb-2">
        {label}
      </p>
      {loading ? (
        <div className="h-9 w-20 bg-pure-surface/60 rounded animate-pulse" />
      ) : isEmpty ? (
        <div>
          <p className={`text-xl sm:text-2xl font-bold ${textColor} opacity-40`}>0</p>
          {emptyText && (
            <p className="text-[10px] text-muted-slate mt-1 italic">{emptyText}</p>
          )}
        </div>
      ) : (
        <p className={`text-2xl sm:text-3xl font-bold ${textColor} leading-none tracking-tight font-metadata-mono`}>
          {value!.toLocaleString('en-US')}
        </p>
      )}
    </div>
  );
}

function Arrow({ pct, prevValue, nextValue }: { pct: string | null; prevValue: number; nextValue: number }) {
  if (pct === null) {
    return (
      <div className="flex items-center justify-center shrink-0">
        <span className="material-symbols-outlined text-muted-slate text-2xl">chevron_right</span>
      </div>
    );
  }
  const positive = prevValue > 0 && nextValue > 0;
  return (
    <div className="flex flex-col items-center justify-center shrink-0 px-1 sm:px-2">
      <div className="flex items-center gap-1 px-2 py-0.5 rounded-full bg-pure-surface border border-whisper-border">
        <span className={`text-[10px] sm:text-xs font-bold font-metadata-mono ${positive ? 'text-electric-blue' : 'text-muted-slate'}`}>
          {pct}
        </span>
      </div>
      <span className="material-symbols-outlined text-electric-blue/60 text-2xl mt-0.5">arrow_forward</span>
    </div>
  );
}

function safePct(numerator: number, denominator: number): string | null {
  if (!denominator || denominator <= 0) return null;
  return `${((numerator / denominator) * 100).toFixed(1)}%`;
}

function safePctDetailed(numerator: number, denominator: number): string | null {
  if (!denominator || denominator <= 0) return null;
  return `${((numerator / denominator) * 100).toFixed(2)}%`;
}

export default function FunnelBlock({ dialed, contacted, sales, outboundCalls, inboundCalls, loading }: FunnelBlockProps) {
  const allZero = (dialed ?? 0) === 0 && (contacted ?? 0) === 0 && sales === 0;

  return (
    <section className="bg-pure-surface dark:bg-gray-900 border border-card-border dark:border-gray-700 rounded-xl shadow-card overflow-hidden">
      <div className="p-6 border-b border-whisper-border flex items-center justify-between">
        <div>
          <h3 className="font-bold text-lg text-primary">History Call</h3>
          <p className="text-[11px] text-secondary mt-0.5 font-metadata-mono uppercase tracking-wider">
            Dialed → Contacted → Sales
          </p>
        </div>
        <span className="material-symbols-outlined text-electric-blue text-2xl">filter_alt</span>
      </div>
      <div className="p-6 space-y-5">
        {allZero && !loading ? (
          <div className="h-24 rounded-lg border border-dashed border-whisper-border bg-surface-container-low flex flex-col items-center justify-center text-muted-slate text-sm gap-1">
            <span className="material-symbols-outlined text-3xl text-muted-slate/50">phone_disabled</span>
            <p className="font-medium">No calls yet</p>
            <p className="text-[11px]">Click sync to load data</p>
          </div>
        ) : (
          <div className="flex flex-col sm:flex-row items-stretch sm:items-center gap-2 sm:gap-1">
            <Stage
              label="Dialed"
              value={dialed}
              loading={loading}
              gradient="bg-gradient-to-br from-electric-blue/10 via-electric-blue/5 to-transparent"
              ringColor="border-electric-blue/30"
              textColor="text-electric-blue"
            />
            <Arrow
              pct={safePct(contacted ?? 0, dialed ?? 0)}
              prevValue={dialed ?? 0}
              nextValue={contacted ?? 0}
            />
            <Stage
              label="Contacted"
              value={contacted}
              loading={loading}
              gradient="bg-gradient-to-br from-emerald-signal/10 via-emerald-signal/5 to-transparent"
              ringColor="border-emerald-signal/30"
              textColor="text-emerald-signal"
            />
            <Arrow
              pct={safePct(sales, contacted ?? 0)}
              prevValue={contacted ?? 0}
              nextValue={sales}
            />
            <Stage
              label="Sales"
              value={sales}
              loading={loading}
              gradient="bg-gradient-to-br from-amber-warmth/10 via-amber-warmth/5 to-transparent"
              ringColor="border-amber-warmth/30"
              textColor="text-amber-warmth"
              emptyText="No sales yet today"
            />
          </div>
        )}
        {contacted !== null && contacted > 0 && sales > 0 && (
          <div className="pt-4 border-t border-whisper-border flex items-center justify-between text-xs">
            <span className="text-secondary">Overall conversion</span>
            <span className="font-bold text-emerald-signal font-metadata-mono">
              {safePctDetailed(sales, dialed ?? 0)} (dialed → sale)
            </span>
          </div>
        )}

        <div className="pt-4 border-t border-whisper-border">
          <p className="text-[10px] sm:text-xs font-semibold text-secondary uppercase tracking-wider mb-3">
            Call direction
          </p>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <DirectionCard
              kind="outbound"
              label="Outbound Calls"
              value={outboundCalls ?? null}
              loading={loading}
            />
            <DirectionCard
              kind="inbound"
              label="Inbound Calls"
              value={inboundCalls ?? null}
              loading={loading}
            />
          </div>
        </div>
      </div>
    </section>
  );
}

function DirectionCard({
  kind,
  label,
  value,
  loading,
}: {
  kind: 'outbound' | 'inbound';
  label: string;
  value: number | null;
  loading?: boolean;
}) {
  const isOutbound = kind === 'outbound';
  const isEmpty = value === null || value === 0;
  const icon = isOutbound ? 'call_made' : 'call_received';
  const palette = isOutbound
    ? {
        gradient: 'bg-gradient-to-br from-emerald-signal/10 via-emerald-signal/5 to-transparent',
        ringColor: 'border-emerald-signal/30',
        textColor: 'text-emerald-signal',
        iconColor: 'text-emerald-signal',
      }
    : {
        gradient: 'bg-gradient-to-br from-electric-blue/10 via-electric-blue/5 to-transparent',
        ringColor: 'border-electric-blue/30',
        textColor: 'text-electric-blue',
        iconColor: 'text-electric-blue',
      };

  return (
    <div className={`flex items-center gap-3 rounded-xl border ${palette.ringColor} ${palette.gradient} p-4 shadow-sm`}>
      <div className={`w-10 h-10 rounded-lg flex items-center justify-center bg-pure-surface/60 ${palette.iconColor}`}>
        <span className="material-symbols-outlined text-xl">{icon}</span>
      </div>
      <div className="flex-1 min-w-0">
        <p className="text-[10px] sm:text-xs font-semibold text-secondary uppercase tracking-wider">
          {label}
        </p>
        {loading ? (
          <div className="h-7 w-16 bg-pure-surface/60 rounded animate-pulse mt-1" />
        ) : isEmpty ? (
          <p className={`text-xl sm:text-2xl font-bold ${palette.textColor} opacity-40 font-metadata-mono leading-none tracking-tight`}>
            0
          </p>
        ) : (
          <p className={`text-2xl sm:text-3xl font-bold ${palette.textColor} leading-none tracking-tight font-metadata-mono`}>
            {value!.toLocaleString('en-US')}
          </p>
        )}
      </div>
    </div>
  );
}
