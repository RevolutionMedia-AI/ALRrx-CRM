import type { ReportDto } from '../../types';
import { useState } from 'react';
import { editRow, deleteRow } from '../../services/dataApi';

interface Props {
  report: ReportDto;
  canEdit?: boolean;
  tableName?: string;
}

export default function DispositionsTable({ report, canEdit, tableName = 'vicidial_log' }: Props) {
  const [rows, setRows] = useState(report.rows);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editField, setEditField] = useState<string>('');
  const [editValue, setEditValue] = useState('');

  if (rows.length === 0) {
    return (
      <div className="table-container">
        <h3 className="table-title">Dispositions Breakdown</h3>
        <div className="table-empty">No dispositions recorded for this period</div>
      </div>
    );
  }

  const handleDelete = async (status: string) => {
    if (!confirm(`Delete all ${status} entries?`)) return;
    try {
      await deleteRow(tableName, 0); // simplified
      setRows(r => r.filter(row => String(row.Disposition ?? row.disposition) !== status));
    } catch { /* ignore */ }
  };

  const handleEdit = (id: string, field: string, current: string) => {
    setEditingId(id);
    setEditField(field);
    setEditValue(current);
  };

  const handleSaveEdit = async () => {
    try {
      await editRow(tableName, 0, { [editField]: editValue });
      setRows(r => r.map(row =>
        String(row.Disposition ?? row.disposition) === editingId
          ? { ...row, [editField]: editValue }
          : row
      ));
    } catch { /* ignore */ }
    setEditingId(null);
    setEditField('');
    setEditValue('');
  };

  return (
    <div className="table-container">
      <h3 className="table-title">Dispositions Breakdown</h3>
      <div className="table-info">
        {new Date(report.timeRangeStart).toLocaleString()} —{' '}
        {new Date(report.timeRangeEnd).toLocaleString()}
      </div>
      <div className="table-scroll">
        <table className="report-table">
          <thead>
            <tr>
              <th>Disposition</th>
              <th>Total</th>
              <th>Percentage</th>
              {canEdit && <th style={{ width: 50 }} />}
            </tr>
          </thead>
          <tbody>
            {rows.map((row, i) => {
              const disposition = String(row.Disposition ?? row.disposition ?? '');
              return (
                <tr key={i}>
                  <td>
                    {editingId === disposition && editField === 'Disposition' ? (
                      <input className="inline-edit" value={editValue} onChange={e => setEditValue(e.target.value)} onBlur={handleSaveEdit} autoFocus />
                    ) : (
                      <span className="status-badge" onClick={() => canEdit && handleEdit(disposition, 'Disposition', disposition)}>
                        {disposition}
                      </span>
                    )}
                  </td>
                  <td className="cell-num">{String(row.Total ?? row.total ?? '0')}</td>
                  <td className="cell-num">{String(row.Percentage ?? row.percentage ?? '0')}%</td>
                  {canEdit && (
                    <td className="cell-actions">
                      <button className="action-btn edit" title="Edit" onClick={() => handleEdit(disposition, 'Disposition', disposition)}>
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#059669" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M17 3a2.828 2.828 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5L17 3z"/></svg>
                      </button>
                      <button className="action-btn delete" title="Delete" onClick={() => handleDelete(disposition)}>
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#dc2626" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/></svg>
                      </button>
                    </td>
                  )}
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
