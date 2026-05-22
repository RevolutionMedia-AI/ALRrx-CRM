export function downloadCSV(csv: string, filename: string) {
  const blob = new Blob(['\uFEFF' + csv], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}

function esc(val: unknown): string {
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
  const header = section.columns.map(esc).join(',');
  const body = section.rows.map(r =>
    section.columns.map(c => esc(r[c] ?? '')).join(',')
  ).join('\n');
  const csv = `${header}\n${body}`;
  downloadCSV(csv, `${section.name.replace(/\s+/g, '_')}.csv`);
}

export function exportCombinedCSV(sections: SectionData[]): void {
  const parts: string[] = [];
  for (const section of sections) {
    if (section.rows.length === 0) continue;
    parts.push(`=== ${section.name} ===`);
    parts.push(section.columns.map(esc).join(','));
    for (const row of section.rows) {
      parts.push(section.columns.map(c => esc(row[c] ?? '')).join(','));
    }
    parts.push('');
  }
  downloadCSV(parts.join('\n'), 'ALRrx_Operations_Report.csv');
}

function formatHHMMSS(seconds: unknown): string {
  const n = Number(seconds);
  if (isNaN(n) || n === 0) return '00:00:00';
  const h = Math.floor(n / 3600);
  const m = Math.floor((n % 3600) / 60);
  const s = Math.floor(n % 60);
  return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
}

function formatPct(val: unknown): string {
  const n = Number(val);
  if (isNaN(n)) return '0.0%';
  return `${n.toFixed(1)}%`;
}

type MetricCardDto = { label: string; value: string; trend?: string };

export function exportDashboardCSV(
  metrics: MetricCardDto[],
  agentReport: SectionData | null,
  dispositions: SectionData | null,
  contactReport: SectionData | null,
  period: string,
): void {
  const ts = new Date().toLocaleString();
  const lines: string[] = [];

  lines.push(`Dashboard ALTRX,${ts}`);
  lines.push('');
  lines.push(['Total Calls', 'Contacts', 'No Contacts', 'Sales Today', 'Avg Handle Time', 'Occupancy', 'Leads Dialed', 'Leads Contacted', 'Contact Rate'].join(','));
  const mv = (label: string) => {
    const m = metrics.find(x => x.label.toLowerCase().includes(label.toLowerCase()));
    return m?.value ?? '--';
  };
  lines.push([mv('Total Calls'), mv('Contacts'), mv('No Contacts'), mv('Sales Today'), mv('Handle Time'), mv('Occupancy'), mv('Leads Dialed'), mv('Leads Contacted'), mv('Contact Rate')].join(','));
  lines.push('');

  if (contactReport && contactReport.rows.length > 0) {
    lines.push('Contact vs No Contact');
    lines.push(['Contacts', 'No Contacts', 'Total Calls', 'Contact Rate'].join(','));
    const cr = contactReport.rows[0];
    const total = Number(cr.Contact ?? 0) + Number(cr.No_Contact ?? 0);
    const rate = total > 0 ? `${((Number(cr.Contact ?? 0) / total) * 100).toFixed(1)}%` : '0.0%';
    lines.push([cr.Contact ?? 0, cr.No_Contact ?? 0, cr.Total_Calls ?? total, rate].join(','));
    lines.push('');
  }

  if (dispositions && dispositions.rows.length > 0) {
    lines.push('Dispositions');
    lines.push(['Disposition', 'Total', 'Percentage'].join(','));
    for (const row of dispositions.rows) {
      lines.push([esc(row.Disposition ?? row.disposition ?? ''), esc(row.Total ?? row.total ?? 0), esc(formatPct(row.Percentage ?? row.percentage ?? 0))].join(','));
    }
    lines.push('');
  }

  if (agentReport && agentReport.rows.length > 0) {
    lines.push('Agent Performance');
    lines.push(['Agent', 'Calls Handled', 'Sales Made', 'Contacts', 'Conversion %', 'Avg Handle Time'].join(','));
    for (const row of agentReport.rows) {
      const ahtRaw = row.AHT ?? row.aht ?? '0';
      let ahtSeconds = 0;
      if (typeof ahtRaw === 'string' && ahtRaw.includes(':')) {
        const parts = String(ahtRaw).split(':').map(Number);
        ahtSeconds = parts.reduce((acc, t) => acc * 60 + (t || 0), 0);
      } else {
        ahtSeconds = Math.round(Number(ahtRaw) * 60);
      }
      lines.push([
        esc(row.Name ?? row.User ?? ''),
        esc(row.Calls_Handled ?? row.calls_handled ?? 0),
        esc(row.Sales_Made ?? row.sales_made ?? 0),
        esc(row.Contacts ?? row.contacts ?? 0),
        esc(formatPct(row.Conversion_Percentage ?? row.conversion_percentage ?? 0)),
        esc(formatHHMMSS(ahtSeconds)),
      ].join(','));
    }
  }

  downloadCSV(lines.join('\n'), `Dashboard_ALTRX_${period}_${new Date().toISOString().split('T')[0]}.csv`);
}

export function exportAnalyticsCSV(
  agentReport: SectionData | null,
  period: string,
  customStart?: string,
  customEnd?: string,
): void {
  const ts = new Date().toLocaleString();
  const rangeLabel = period === 'Custom' ? `${customStart ?? ''} to ${customEnd ?? ''}` : period;
  const lines: string[] = [];

  lines.push(`Analytics ALTRX,Period: ${rangeLabel},${ts}`);
  lines.push('');
  lines.push(['ID Agente', 'Nombre Agente', 'Campaña', 'Talk Time', 'Pause Time', 'Wait Time', 'Wrap Time', 'Total Llamadas', 'Ventas', 'Contactos', 'Conversión %', 'AHT'].join(','));

  if (agentReport && agentReport.rows.length > 0) {
    const sorted = [...agentReport.rows].sort((a, b) => {
      const va = Number(a.Calls_Handled ?? a.calls_handled ?? 0);
      const vb = Number(b.Calls_Handled ?? b.calls_handled ?? 0);
      return vb - va;
    });

    for (const row of sorted) {
      const name = String(row.Name ?? row.User ?? '');
      const user = String(row.User ?? row.user ?? '');
      const campaign = 'ALTRX';
      const calls = Number(row.Calls_Handled ?? row.calls_handled ?? 0);
      const sales = Number(row.Sales_Made ?? row.sales_made ?? 0);
      const contacts = Number(row.Contacts ?? row.contacts ?? 0);
      const convPct = formatPct(row.Conversion_Percentage ?? row.conversion_percentage ?? 0);
      const ahtRaw = row.AHT ?? row.aht ?? '0';
      let ahtSeconds = 0;
      if (typeof ahtRaw === 'string' && String(ahtRaw).includes(':')) {
        const parts = String(ahtRaw).split(':').map(Number);
        ahtSeconds = parts.reduce((acc, t) => acc * 60 + (t || 0), 0);
      } else {
        ahtSeconds = Math.round(Number(ahtRaw) * 60);
      }

      lines.push([
        esc(user),
        esc(name),
        esc(campaign),
        esc(formatHHMMSS(ahtSeconds)),
        esc('00:00:00'),
        esc('00:00:00'),
        esc('00:00:00'),
        esc(calls),
        esc(sales),
        esc(contacts),
        esc(convPct),
        esc(formatHHMMSS(ahtSeconds)),
      ].join(','));
    }
  }

  downloadCSV(lines.join('\n'), `Analytics_ALTRX_${period}_${new Date().toISOString().split('T')[0]}.csv`);
}

export function exportRealTimeCSV(
  staffing: SectionData | null,
  queueData: { campaign: string; callsWaiting: number; maxWait: string; agentsLogged: number },
  period: string,
): void {
  const ts = new Date().toLocaleString();
  const lines: string[] = [];

  lines.push(`Real-Time ALTRX,${ts}`);
  lines.push('');
  lines.push('Estado de la Cola');
  lines.push(['Campaña', 'Llamadas en Espera', 'Tiempo Espera Máx', 'Agentes Logueados'].join(','));
  lines.push([esc(queueData.campaign), esc(queueData.callsWaiting), esc(queueData.maxWait), esc(queueData.agentsLogged)].join(','));
  lines.push('');
  lines.push('');
  lines.push('');

  lines.push('Estado Actual de Agentes');
  lines.push(['Agente', 'Estado Actual', 'Tiempo en Estado', 'Campaña Activa'].join(','));

  if (staffing && staffing.rows.length > 0) {
    const sorted = [...staffing.rows].sort((a, b) => {
      const timeA = String(a.last_update_time ?? a.last_call_time ?? '');
      const timeB = String(b.last_update_time ?? b.last_call_time ?? '');
      if (!timeA && !timeB) return 0;
      if (!timeA) return 1;
      if (!timeB) return -1;
      return new Date(timeA).getTime() - new Date(timeB).getTime();
    });

    for (const row of sorted) {
      const name = String(row.Name ?? row.User ?? '');
      const status = String(row.Status ?? 'OFFLINE').toUpperCase();
      const statusLabel = status === 'READY' ? 'Available' : status === 'INCALL' ? 'On Call' : status === 'QUEUE' ? 'In Queue' : status === 'PAUSED' ? 'Paused' : 'Offline';
      const isActive = status === 'INCALL' || status === 'QUEUE' || status === 'PAUSED';
      const isoTime = isActive ? String(row.last_call_time ?? row.last_update_time ?? '') : String(row.last_update_time ?? '');
      let timeInState = '--:--:--';
      if (isoTime) {
        try {
          const diff = Math.floor((Date.now() - new Date(isoTime).getTime()) / 1000);
          timeInState = formatHHMMSS(diff);
        } catch {
        }
      }
      const campaign = String(row.Supervisor ?? 'ALTRX');

      lines.push([esc(name), esc(statusLabel), esc(timeInState), esc(campaign)].join(','));
    }
  }

  downloadCSV(lines.join('\n'), `RealTime_ALTRX_${new Date().toISOString().split('T')[0]}.csv`);
}