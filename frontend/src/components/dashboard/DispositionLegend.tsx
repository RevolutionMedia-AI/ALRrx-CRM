import { useState } from 'react';

export const DISPOSITION_LABELS: Record<string, string> = {
  SALE: 'Sale completed',
  HNGUP: 'Hung up',
  ANSMN: 'Answering machine',
  NTINS: 'Not interested',
  DNC: 'Do not call',
  B: 'Busy',
  NA: 'No answer',
  VM: 'Voicemail',
  DC: 'Disconnected',
  A: 'Answered',
  CALLBK: 'Call back',
  DROP: 'Dropped',
  N: 'No answer (alt)',
  PDROP: 'Predictive drop',
  ADC: 'Abandoned during call',
  AFTHRS: 'After hours',
  DECLINED: 'Declined',
  APPT: 'Appointment set',
  FTF: 'Failed to find',
  HOURS: 'Outside hours',
  I: 'Invalid number',
  LTMG: 'Left message',
  MAXI: 'Max attempts',
  NEW: 'New lead',
  PENDING: 'Pending',
  QUEUE: 'In queue',
  READY: 'Ready',
  SHH: 'Scheduled hangup',
  SXFER: 'Sent to transfer',
  TIMEOT: 'Timed out',
  XFER: 'Transferred',
  Y: 'Confirm',
};

export interface DispositionItem {
  name: string;
  value: number;
  color: string;
}

export function getDispositionDescription(code: string): string {
  if (!code) return 'Unknown';
  const upper = code.toUpperCase();
  return DISPOSITION_LABELS[upper] ?? 'Custom disposition';
}

interface DispositionLegendProps {
  data: DispositionItem[];
  total?: number;
}

export default function DispositionLegend({ data, total }: DispositionLegendProps) {
  const [openCode, setOpenCode] = useState<string | null>(null);

  if (!data || data.length === 0) return null;

  const grandTotal = total ?? data.reduce((sum, d) => sum + d.value, 0);

  return (
    <div className="flex flex-wrap gap-2">
      {data.map((d) => {
        const pct = grandTotal > 0 ? ((d.value / grandTotal) * 100).toFixed(1) : '0.0';
        const isOpen = openCode === d.name;
        return (
          <div key={d.name} className="relative">
            <button
              type="button"
              onClick={() => setOpenCode(isOpen ? null : d.name)}
              onMouseEnter={() => setOpenCode(d.name)}
              onMouseLeave={() => setOpenCode((c) => (c === d.name ? null : c))}
              className={`group inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full border transition-all text-xs font-medium cursor-pointer ${
                isOpen
                  ? 'border-electric-blue bg-electric-blue/10 scale-105'
                  : 'border-whisper-border bg-pure-surface hover:border-electric-blue/40 hover:scale-105'
              }`}
            >
              <span
                className="w-2 h-2 rounded-full shrink-0 ring-1 ring-black/5"
                style={{ backgroundColor: d.color }}
              />
              <span className="text-primary font-metadata-mono uppercase tracking-wider">
                {d.name}
              </span>
              <span className="text-secondary font-metadata-mono">{d.value}</span>
              <span className="text-muted-slate text-[10px]">{pct}%</span>
            </button>
            {isOpen && (
              <div className="absolute z-20 left-1/2 -translate-x-1/2 top-full mt-1.5 px-2.5 py-1.5 bg-pure-surface dark:bg-gray-800 border border-whisper-border dark:border-gray-600 rounded-md shadow-lg text-xs whitespace-nowrap pointer-events-none">
                <p className="font-semibold text-primary">{d.name}</p>
                <p className="text-secondary">{getDispositionDescription(d.name)}</p>
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}
