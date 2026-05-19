import type { ReportDto } from '../../types';

interface Props {
  report: ReportDto;
}

export default function ReportTable({ report }: Props) {
  return (
    <div className="table-container">
      <h3 className="table-title">{report.reportName}</h3>
      <div className="table-info">
        {new Date(report.timeRangeStart).toLocaleString()} —{' '}
        {new Date(report.timeRangeEnd).toLocaleString()}
      </div>
      <div className="table-scroll">
        <table className="report-table">
          <thead>
            <tr>
              {report.columns.map((col) => (
                <th key={col}>{col}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {report.rows.map((row, i) => (
              <tr key={i}>
                {report.columns.map((col) => (
                  <td key={col}>{String(row[col] ?? '')}</td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
