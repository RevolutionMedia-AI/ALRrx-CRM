import { useState } from 'react';
import { exportReport } from '../../services/api';
import type { QueryDefinitionDto, TimeFilterDto } from '../../types';

interface Props {
  queries: QueryDefinitionDto[];
  currentFilter: TimeFilterDto;
}

export default function ExportPanel({ queries, currentFilter }: Props) {
  const [selectedQuery, setSelectedQuery] = useState('');
  const [format, setFormat] = useState('excel');
  const [loading, setLoading] = useState(false);

  const handleExport = async () => {
    if (!selectedQuery) return;
    setLoading(true);
    try {
      const blob = await exportReport({
        reportId: selectedQuery,
        format,
        timeFilter: currentFilter,
      });
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `report.${format}`;
      a.click();
      window.URL.revokeObjectURL(url);
    } catch (err) {
      console.error('Export failed', err);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="export-panel">
      <h3>Export</h3>
      <div className="export-controls">
        <select value={selectedQuery} onChange={(e) => setSelectedQuery(e.target.value)}>
          <option value="">Select report...</option>
          {queries.map((q) => (
            <option key={q.id} value={q.id}>
              {q.name}
            </option>
          ))}
        </select>
        <select value={format} onChange={(e) => setFormat(e.target.value)}>
          <option value="excel">Excel</option>
          <option value="csv">CSV</option>
          <option value="pdf">PDF</option>
        </select>
        <button onClick={handleExport} disabled={!selectedQuery || loading}>
          {loading ? 'Exporting...' : 'Export'}
        </button>
      </div>
    </div>
  );
}
