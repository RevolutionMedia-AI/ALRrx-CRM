import { useState } from 'react';
import type { TimeFilterDto } from '../../types';

interface Props {
  onFilterChange: (filter: TimeFilterDto) => void;
}

const PERIODS = [
  { id: 'LastHour', label: 'Last Hour' },
  { id: 'Today', label: 'Today' },
  { id: 'ThisWeek', label: 'This Week' },
  { id: 'ThisMonth', label: 'This Month' },
  { id: 'Custom', label: 'Custom' },
];

export default function TimeFilter({ onFilterChange }: Props) {
  const [activePeriod, setActivePeriod] = useState('Today');
  const [customStart, setCustomStart] = useState('');
  const [customEnd, setCustomEnd] = useState('');

  const handlePeriodClick = (period: string) => {
    setActivePeriod(period);
    onFilterChange({
      period,
      ...(period === 'Custom'
        ? { customStart: customStart || undefined, customEnd: customEnd || undefined }
        : {}),
    });
  };

  const handleCustomApply = () => {
    if (customStart && customEnd) {
      onFilterChange({
        period: 'Custom',
        customStart,
        customEnd,
      });
    }
  };

  return (
    <div className="time-filter">
      <div className="period-buttons">
        {PERIODS.map((p) => (
          <button
            key={p.id}
            className={`period-btn ${activePeriod === p.id ? 'active' : ''}`}
            onClick={() => handlePeriodClick(p.id)}
          >
            {p.label}
          </button>
        ))}
      </div>

      {activePeriod === 'Custom' && (
        <div className="custom-range">
          <label>
            From:
            <input
              type="datetime-local"
              value={customStart}
              onChange={(e) => setCustomStart(e.target.value)}
            />
          </label>
          <label>
            To:
            <input
              type="datetime-local"
              value={customEnd}
              onChange={(e) => setCustomEnd(e.target.value)}
            />
          </label>
          <button className="apply-btn" onClick={handleCustomApply}>
            Apply
          </button>
        </div>
      )}
    </div>
  );
}
