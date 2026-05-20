import type { ReactNode } from 'react';
import SectionFilter from '../Filters/SectionFilter';
import type { TimeFilterDto, ReportDto } from '../../types';
import { exportSectionCSV } from '../../utils/csv';

interface Props {
  title: string;
  report?: ReportDto | null;
  children: ReactNode;
  loading?: boolean;
  error?: string | null;
  onFilterChange: (filter: TimeFilterDto) => void;
  onConsultar?: () => void;
  initialPeriod?: string;
}

export default function SectionContainer({
  title,
  report,
  children,
  loading,
  error,
  onFilterChange,
  onConsultar,
  initialPeriod,
}: Props) {
  const hasData = report && report.rows.length > 0;

  const handleCSV = () => {
    if (report) {
      exportSectionCSV({
        name: title,
        columns: report.columns,
        rows: report.rows,
      });
    }
  };

  return (
    <section className="section-container">
      <div className="section-head">
        <h3 className="section-title">{title}</h3>
        <div className="section-actions">
          <SectionFilter onFilterChange={onFilterChange} initialPeriod={initialPeriod} />
          {onConsultar && (
            <button className="csv-btn report-btn" onClick={onConsultar} disabled={loading} title="Consultar">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                <polyline points="14 2 14 8 20 8"/>
                <line x1="16" y1="13" x2="8" y2="13"/>
                <line x1="16" y1="17" x2="8" y2="17"/>
              </svg>
              Consultar
            </button>
          )}
          <button
            className="csv-btn"
            onClick={handleCSV}
            disabled={!hasData}
            title="Download CSV"
          >
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/>
            </svg>
            CSV
          </button>
        </div>
      </div>

      {loading && (
        <div className="section-loading">
          <div className="skeleton-row" />
          <div className="skeleton-row" />
          <div className="skeleton-row" />
        </div>
      )}

      {error && <div className="section-error">{error}</div>}

      {!loading && !error && children}
    </section>
  );
}
