import { useState, useEffect, useRef, useCallback } from 'react';

export const DISPOSITION_LABELS: Record<string, string> = {
  SALE: 'Sale Completed',
  SALES: 'Sales Completed',
  HNGUP: 'Hung Up',
  A: 'Answered',
  NI: 'Not Interested',
  NTINS: 'Not Interested',
  N: 'No Contact',
  NSLBO: 'No Sale - Bad Offer / Bad Outcome',
  B: 'Busy',
  LNBSY: 'Line Busy',
  ALRPR: 'Already Purchased',
  DNC: 'Do Not Call',
  DAIR: 'Do Not Call - Air',
  DDAIR: 'Do Not Call - Air',
  CALLBK: 'Callback',
  NSLWC: 'No Sale - Wrong Customer',
  NSALE: 'No Sale',
  NSLIC: 'No Sale - Insufficient Contact',
  NSLMC: 'No Sale - Maybe Customer / More Consideration',
  ANSMN: 'Answering Machine',
  ANSM: 'Answering Machine',
  NSLPO: 'No Sale - Price Objection',
  NSLNI: 'No Sale - Not Interested',
  NTQLFY: 'Not Qualified',
  ITST: 'In Test',
  NP: 'Not Present',
  NTAVL: 'Not Available',
  INCALL: 'In Call',
  DROP: 'Dropped',
  NA: 'No Answer',
  VM: 'Voicemail',
  DC: 'Disconnected',
  PDROP: 'Predictive Drop',
  ADC: 'Abandoned During Call',
  AFTHRS: 'After Hours',
  DECLINED: 'Declined',
  APPT: 'Appointment Set',
  FTF: 'Failed To Find',
  HOURS: 'Outside Hours',
  I: 'Invalid Number',
  LTMG: 'Left Message',
  MAXI: 'Max Attempts',
  NEW: 'New Lead',
  PENDING: 'Pending',
  QUEUE: 'In Queue',
  READY: 'Ready',
  SHH: 'Scheduled Hangup',
  SXFER: 'Sent To Transfer',
  TIMEOT: 'Timed Out',
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
  const [flippedCodes, setFlippedCodes] = useState<Set<string>>(new Set());
  const containerRef = useRef<HTMLDivElement>(null);
  const itemRefs = useRef<Record<string, HTMLDivElement | null>>({});

  const measurePositions = useCallback(() => {
    if (!containerRef.current) return;
    const containerRect = containerRef.current.getBoundingClientRect();
    const newFlipped = new Set<string>();
    data.forEach((d) => {
      const el = itemRefs.current[d.name];
      if (el) {
        const rect = el.getBoundingClientRect();
        const distanceFromRight = containerRect.right - rect.right;
        if (distanceFromRight < 220) {
          newFlipped.add(d.name);
        }
      }
    });
    setFlippedCodes(newFlipped);
  }, [data]);

  useEffect(() => {
    measurePositions();
    const observer = new ResizeObserver(measurePositions);
    if (containerRef.current) {
      observer.observe(containerRef.current);
    }
    window.addEventListener('resize', measurePositions);
    return () => {
      observer.disconnect();
      window.removeEventListener('resize', measurePositions);
    };
  }, [measurePositions]);

  if (!data || data.length === 0) return null;

  const grandTotal = total ?? data.reduce((sum, d) => sum + d.value, 0);

  return (
    <div ref={containerRef} className="flex flex-wrap gap-2">
      {data.map((d) => {
        const pct = grandTotal > 0 ? ((d.value / grandTotal) * 100).toFixed(1) : '0.0';
        const isOpen = openCode === d.name;
        const isFlipped = flippedCodes.has(d.name);
        return (
          <div key={d.name} className="relative" ref={(el) => { itemRefs.current[d.name] = el; }}>
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
              <div
                className={`absolute z-20 top-full mt-1.5 px-2.5 py-1.5 bg-pure-surface dark:bg-gray-800 border border-whisper-border dark:border-gray-600 rounded-md shadow-lg text-xs whitespace-nowrap pointer-events-none ${
                  isFlipped ? 'right-0' : 'left-0'
                }`}
              >
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
