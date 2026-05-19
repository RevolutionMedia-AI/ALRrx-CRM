import { useState } from 'react';
import type { TimeFilterDto } from '../../types';

interface Props {
  onFilterChange: (filter: TimeFilterDto) => void;
  initialPeriod?: string;
}

const PERIODS = [
  { id: 'LastHour', label: 'Hour' },
  { id: 'Today', label: 'Today' },
  { id: 'ThisWeek', label: 'Week' },
  { id: 'ThisMonth', label: 'Month' },
  { id: 'Custom', label: 'Custom' },
];

export default function SectionFilter({ onFilterChange, initialPeriod = 'Today' }: Props) {
  const [activePeriod, setActivePeriod] = useState(initialPeriod);
  const [customStart, setCustomStart] = useState('');
  const [customEnd, setCustomEnd] = useState('');
  const [showCustom, setShowCustom] = useState(false);

  const handlePeriodClick = (period: string) => {
    setActivePeriod(period);
    if (period !== 'Custom') {
      setShowCustom(false);
      onFilterChange({ period });
    } else {
      setShowCustom(true);
      if (customStart && customEnd) {
        onFilterChange({ period: 'Custom', customStart, customEnd });
      }
    }
  };

  const handleCustomApply = () => {
    if (customStart && customEnd) {
      onFilterChange({ period: 'Custom', customStart, customEnd });
    }
  };

  return (
    <div className="section-filter">
      <div className="sf-periods">
        {PERIODS.map((p) => (
          <button
            key={p.id}
            className={`sf-btn ${activePeriod === p.id ? 'active' : ''}`}
            onClick={() => handlePeriodClick(p.id)}
          >
            {p.label}
          </button>
        ))}
      </div>
      {showCustom && (
        <div className="sf-custom">
          <input
            type="datetime-local"
            value={customStart}
            onChange={(e) => setCustomStart(e.target.value)}
            className="sf-input"
          />
          <span>→</span>
          <input
            type="datetime-local"
            value={customEnd}
            onChange={(e) => setCustomEnd(e.target.value)}
            className="sf-input"
          />
          <button className="sf-apply" onClick={handleCustomApply}>Apply</button>
        </div>
      )}
    </div>
  );
}
