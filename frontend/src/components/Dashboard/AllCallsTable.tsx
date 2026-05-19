import type { ReportDto } from '../../types';
import { useState } from 'react';
import { editRow, deleteRow } from '../../services/dataApi';

interface Props {
  report: ReportDto;
  canEdit?: boolean;
  tableName?: string;
}

const PAGE_SIZE = 50;

export default function AllCallsTable({ report, canEdit, tableName = 'vicidial_log' }: Props) {
  const [page, setPage] = useState(0);
  const [rows, setRows] = useState(report.rows);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editField, setEditField] = useState<string>('');
  const [editValue, setEditValue] = useState('');
  const totalPages = Math.ceil(rows.length / PAGE_SIZE);
  const pageRows = rows.slice(page * PAGE_SIZE, (page + 1) * PAGE_SIZE);

  if (rows.length === 0) {
    return (
      <div className="table-container">
        <h3 className="table-title">All Calls</h3>
        <div className="table-empty">No calls recorded for this period</div>
      </div>
    );
  }

  const handleDelete = async (id: string) => {
    if (!confirm('Delete this call record?')) return;
    try {
      await deleteRow(tableName, Number(id));
      setRows(r => r.filter(row => String(row.lead_id ?? row.Lead_ID) !== id));
    } catch { /* ignore */ }
  };

  const handleEdit = (id: string, field: string, current: string) => {
    setEditingId(id);
    setEditField(field);
    setEditValue(current);
  };

  const handleSaveEdit = async () => {
    try {
      await editRow(tableName, Number(editingId), { [editField]: editValue });
      setRows(r => r.map(row =>
        String(row.lead_id ?? row.Lead_ID) === editingId
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
      <h3 className="table-title">All Calls</h3>
      <div className="table-info">
        {new Date(report.timeRangeStart).toLocaleString()} —{' '}
        {new Date(report.timeRangeEnd).toLocaleString()}
        <span className="table-count">({rows.length} calls)</span>
      </div>
      <div className="table-scroll">
        <table className="report-table report-table-sm">
          <thead>
            <tr>
              <th>Date</th>
              <th>User</th>
              <th>Status</th>
              <th>Lead ID</th>
              <th>Duration</th>
              {canEdit && <th style={{ width: 50 }} />}
            </tr>
          </thead>
          <tbody>
            {pageRows.map((row, i) => {
              const id = String(row.lead_id ?? row.Lead_ID ?? '');
              return (
                <tr key={i}>
                  <td className="cell-date">{new Date(String(row.call_date ?? row.Call_Date ?? '')).toLocaleString()}</td>
                  <td>{String(row.user ?? row.User ?? '')}</td>
                  <td>
                    {editingId === id && editField === 'status' ? (
                      <input className="inline-edit" value={editValue} onChange={e => setEditValue(e.target.value)} onBlur={handleSaveEdit} autoFocus />
                    ) : (
                      <span className="status-badge" onClick={() => canEdit && handleEdit(id, 'status', String(row.status ?? row.Status ?? ''))}>
                        {String(row.status ?? row.Status ?? '')}
                      </span>
                    )}
                  </td>
                  <td className="cell-num">{id}</td>
                  <td className="cell-num">{String(row.length_in_sec ?? row.Length_In_Sec ?? '0')}s</td>
                  {canEdit && (
                    <td className="cell-actions">
                      <button className="action-btn edit" title="Edit" onClick={() => handleEdit(id, 'status', String(row.status ?? row.Status ?? ''))}>
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#059669" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M17 3a2.828 2.828 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5L17 3z"/></svg>
                      </button>
                      <button className="action-btn delete" title="Delete" onClick={() => handleDelete(id)}>
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
      {totalPages > 1 && (
        <div className="pagination">
          <button disabled={page === 0} onClick={() => setPage(p => p - 1)}>Prev</button>
          <span>Page {page + 1} of {totalPages}</span>
          <button disabled={page >= totalPages - 1} onClick={() => setPage(p => p + 1)}>Next</button>
        </div>
      )}
    </div>
  );
}
