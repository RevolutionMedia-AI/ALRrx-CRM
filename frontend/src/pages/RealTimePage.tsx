import { useState } from 'react';

export default function RealTimePage() {
  const [connected] = useState(false);

  return (
    <>
      <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4">
        <div>
          <h1 className="font-headline-lg text-headline-lg font-bold text-on-surface">Real-Time Report</h1>
          <p className="text-secondary text-sm mt-1">Live agent status and active call monitoring</p>
        </div>
        <div className="flex items-center gap-3">
          <span
            className={
              connected
                ? 'flex items-center gap-1.5 px-3 py-1.5 rounded-full text-sm font-medium text-emerald-signal bg-emerald-signal/10'
                : 'flex items-center gap-1.5 px-3 py-1.5 rounded-full text-sm font-medium text-deep-rose bg-deep-rose/10'
            }
          >
            <span className={`w-2 h-2 rounded-full ${connected ? 'bg-emerald-signal' : 'bg-deep-rose'}`} />
            {connected ? 'Connected' : 'Disconnected'}
          </span>
        </div>
      </div>

      <div className="flex flex-col items-center justify-center py-24 text-center">
        <span className="material-symbols-outlined text-6xl text-muted-slate mb-4" style={{ fontVariationSettings: "'FILL' 1" }}>
          pulse_alert
        </span>
        <h2 className="font-headline-md text-headline-md font-semibold text-on-surface mb-2">Real-Time Monitoring Coming Soon</h2>
        <p className="text-secondary text-sm max-w-md">
          Live agent availability, active call tracking, and real-time metrics will be displayed here via SignalR.
        </p>
      </div>
    </>
  );
}
