import { useState } from 'react';

type Period = 'Today' | 'Week' | 'Month';

export default function AnalyticsPage() {
  const [period] = useState<Period>('Today');

  return (
    <>
      <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4">
        <div>
          <h1 className="font-headline-lg text-headline-lg font-bold text-on-surface">Analytics</h1>
          <p className="text-secondary text-sm mt-1">Deep dive into historical trends and agent performance</p>
        </div>
        <div className="flex items-center gap-3">
          {(['Today', 'Week', 'Month'] as Period[]).map((p) => (
            <button
              key={p}
              className={
                period === p
                  ? 'px-4 py-1.5 rounded font-medium text-sm bg-primary text-on-primary shadow-sm'
                  : 'px-4 py-1.5 rounded font-medium text-sm text-secondary hover:text-primary transition-colors border border-whisper-border'
              }
            >
              {p}
            </button>
          ))}
        </div>
      </div>

      <div className="flex flex-col items-center justify-center py-24 text-center">
        <span className="material-symbols-outlined text-6xl text-muted-slate mb-4" style={{ fontVariationSettings: "'FILL' 1" }}>
          bar_chart
        </span>
        <h2 className="font-headline-md text-headline-md font-semibold text-on-surface mb-2">Analytics Coming Soon</h2>
        <p className="text-secondary text-sm max-w-md">
          Deep historical analysis, trend comparisons, and exportable reports will be available here.
        </p>
      </div>
    </>
  );
}
