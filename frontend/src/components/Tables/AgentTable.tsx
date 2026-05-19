import type { ReportDto } from '../../types';
import { useState } from 'react';
import { editRow, deleteRow } from '../../services/dataApi';

interface Props {
  report: ReportDto;
  canEdit?: boolean;
  tableName?: string;
}

export default function AgentTable({ report, canEdit, tableName = 'vicidial_log' }: Props) {
  const [rows, setRows] = useState(report.rows);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editField, setEditField] = useState<string>('');
  const [editValue, setEditValue] = useState('');

  if (rows.length === 0) {
    return (
      <div className="table-container">
        <h3 className="table-title">Agent Performance</h3>
        <div className="table-empty">No agent data available for this period</div>
      </div>
    );
  }

  const handleDelete = async (id: string) => {
    if (!confirm('Delete this row?')) return;
    try {
      await deleteRow(tableName, Number(id));
      setRows(r => r.filter(row => String(row.User ?? row.user) !== id));
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
        String(row.User ?? row.user) === editingId
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
      <h3 className="table-title">Agent Performance</h3>
      <div className="table-info">
        {new Date(report.timeRangeStart).toLocaleString()} —{' '}
        {new Date(report.timeRangeEnd).toLocaleString()}
      </div>
      <div className="table-scroll">
        <table className="report-table">
          <thead>
            <tr>
              <th>User</th>
              <th>Name</th>
              <th>Calls Handled</th>
              <th>Sales</th>
              <th>Contacts</th>
              <th>Conversion</th>
              <th>AHT</th>
              {canEdit && <th style={{ width: 80 }}>Actions</th>}
            </tr>
          </thead>
          <tbody>
            {rows.map((row, i) => {
              const id = String(row.User ?? row.user ?? i);
              return (
                <tr key={i}>
                  <td>{String(row.User ?? row.user ?? '')}</td>
                  <td>{String(row.Name ?? row.name ?? '')}</td>
                  <td className="cell-num">{String(row.Calls_Handled ?? '0')}</td>
                  <td className="cell-num">
                    {editingId === id && editField === 'Sales_Made' ? (
                      <input className="inline-edit" value={editValue} onChange={e => setEditValue(e.target.value)} onBlur={handleSaveEdit} autoFocus />
                    ) : (
                      <span onClick={() => handleEdit(id, 'Sales_Made', String(row.Sales_Made ?? '0'))}>
                        {String(row.Sales_Made ?? '0')}
                      </span>
                    )}
                  </td>
                  <td className="cell-num">{String(row.Contacts ?? '0')}</td>
                  <td className="cell-num">{String(row.Conversion_Percentage ?? '0')}%</td>
                  <td className="cell-num">{String(row.AHT ?? '')}</td>
                  {canEdit && (
                    <td className="cell-actions">
                      <button className="action-btn edit" title="Edit" onClick={() => handleEdit(id, 'Sales_Made', String(row.Sales_Made ?? '0'))}>
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
    </div>
  );
}
