export function downloadCSV(csv: string, filename: string) {
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}

function escapeCSV(val: unknown): string {
  const s = String(val ?? '');
  if (s.includes(',') || s.includes('"') || s.includes('\n')) {
    return `"${s.replace(/"/g, '""')}"`;
  }
  return s;
}

interface SectionData {
  name: string;
  columns: string[];
  rows: Record<string, unknown>[];
}

export function exportSectionCSV(section: SectionData): void {
  const header = section.columns.map(escapeCSV).join(',');
  const body = section.rows.map(r =>
    section.columns.map(c => escapeCSV(r[c] ?? '')).join(',')
  ).join('\n');
  const csv = `${header}\n${body}`;
  downloadCSV(csv, `${section.name.replace(/\s+/g, '_')}.csv`);
}

export function exportCombinedCSV(sections: SectionData[]): void {
  const parts: string[] = [];
  for (const section of sections) {
    if (section.rows.length === 0) continue;
    parts.push(`=== ${section.name} ===`);
    parts.push(section.columns.map(escapeCSV).join(','));
    for (const row of section.rows) {
      parts.push(section.columns.map(c => escapeCSV(row[c] ?? '')).join(','));
    }
    parts.push('');
  }
  const csv = parts.join('\n');
  downloadCSV(csv, `ALRrx_Operations_Report.csv`);
}
