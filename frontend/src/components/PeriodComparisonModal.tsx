import { useState } from 'react';
import { exportPeriodComparisonExcel } from '../services/api';
import type { TimeFilterDto } from '../types';

interface PeriodComparisonModalProps {
  isOpen: boolean;
  onClose: () => void;
}

export default function PeriodComparisonModal({ isOpen, onClose }: PeriodComparisonModalProps) {
  const [period1Start, setPeriod1Start] = useState(() => {
    const d = new Date();
    d.setDate(d.getDate() - 7);
    return d.toISOString().split('T')[0];
  });
  const [period1End, setPeriod1End] = useState(() => {
    const d = new Date();
    d.setDate(d.getDate() - 1);
    return d.toISOString().split('T')[0];
  });
  const [period2Start, setPeriod2Start] = useState(() => {
    const d = new Date();
    return d.toISOString().split('T')[0];
  });
  const [period2End, setPeriod2End] = useState(() => {
    const d = new Date();
    return d.toISOString().split('T')[0];
  });
  const [exporting, setExporting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!isOpen) return null;

  const handleExport = async () => {
    setExporting(true);
    setError(null);
    try {
      const filter1: TimeFilterDto = {
        period: 'Custom',
        customStart: `${period1Start}T00:00:00`,
        customEnd: `${period1End}T23:59:59`,
      };
      const filter2: TimeFilterDto = {
        period: 'Custom',
        customStart: `${period2Start}T00:00:00`,
        customEnd: `${period2End}T23:59:59`,
      };
      const blob = await exportPeriodComparisonExcel(filter1, filter2);
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `ALTRX_Period_Comparison_${new Date().toISOString().split('T')[0]}.xlsx`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
      onClose();
    } catch (err) {
      setError('Failed to generate comparison report');
    } finally {
      setExporting(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-sm">
      <div className="bg-pure-surface dark:bg-gray-900 border border-whisper-border dark:border-gray-700 rounded-2xl shadow-2xl w-full max-w-lg mx-4 overflow-hidden">
        <div className="flex items-center justify-between p-6 border-b border-whisper-border dark:border-gray-700">
          <div className="flex items-center gap-3">
            <span className="material-symbols-outlined text-2xl text-primary">compare_arrows</span>
            <h2 className="text-xl font-bold text-primary">Period Comparison</h2>
          </div>
          <button
            onClick={onClose}
            className="p-2 hover:bg-surface-container-low rounded-lg transition-colors"
          >
            <span className="material-symbols-outlined text-secondary">close</span>
          </button>
        </div>

        <div className="p-6 space-y-6">
          <div>
            <h3 className="text-sm font-semibold text-primary mb-3 uppercase tracking-wider">Period 1 (Baseline)</h3>
            <div className="flex gap-3 items-center">
              <div className="flex-1">
                <label className="text-xs text-secondary mb-1 block">Start Date</label>
                <input
                  type="date"
                  value={period1Start}
                  onChange={(e) => setPeriod1Start(e.target.value)}
                  className="w-full px-3 py-2 text-sm border border-whisper-border rounded-lg bg-surface-container-low text-primary focus:border-electric-blue focus:outline-none"
                />
              </div>
              <span className="text-secondary mt-5">to</span>
              <div className="flex-1">
                <label className="text-xs text-secondary mb-1 block">End Date</label>
                <input
                  type="date"
                  value={period1End}
                  onChange={(e) => setPeriod1End(e.target.value)}
                  className="w-full px-3 py-2 text-sm border border-whisper-border rounded-lg bg-surface-container-low text-primary focus:border-electric-blue focus:outline-none"
                />
              </div>
            </div>
          </div>

          <div>
            <h3 className="text-sm font-semibold text-primary mb-3 uppercase tracking-wider">Period 2 (Comparison)</h3>
            <div className="flex gap-3 items-center">
              <div className="flex-1">
                <label className="text-xs text-secondary mb-1 block">Start Date</label>
                <input
                  type="date"
                  value={period2Start}
                  onChange={(e) => setPeriod2Start(e.target.value)}
                  className="w-full px-3 py-2 text-sm border border-whisper-border rounded-lg bg-surface-container-low text-primary focus:border-electric-blue focus:outline-none"
                />
              </div>
              <span className="text-secondary mt-5">to</span>
              <div className="flex-1">
                <label className="text-xs text-secondary mb-1 block">End Date</label>
                <input
                  type="date"
                  value={period2End}
                  onChange={(e) => setPeriod2End(e.target.value)}
                  className="w-full px-3 py-2 text-sm border border-whisper-border rounded-lg bg-surface-container-low text-primary focus:border-electric-blue focus:outline-none"
                />
              </div>
            </div>
          </div>

          {error && (
            <div className="bg-deep-rose/10 border border-deep-rose/20 rounded-lg p-3 text-deep-rose text-sm">
              {error}
            </div>
          )}
        </div>

        <div className="flex justify-end gap-3 p-6 border-t border-whisper-border bg-surface-container-low">
          <button
            onClick={onClose}
            className="px-4 py-2 text-sm font-medium text-primary border border-whisper-border rounded-lg hover:bg-surface-container transition-colors"
          >
            Cancel
          </button>
          <button
            onClick={handleExport}
            disabled={exporting}
            className="px-6 py-2 text-sm font-medium text-white bg-emerald-signal rounded-lg hover:scale-[0.98] transition-transform disabled:opacity-50 flex items-center gap-2"
          >
            <span className="material-symbols-outlined text-sm">table_chart</span>
            {exporting ? 'Generating...' : 'Export to Excel'}
          </button>
        </div>
      </div>
    </div>
  );
}