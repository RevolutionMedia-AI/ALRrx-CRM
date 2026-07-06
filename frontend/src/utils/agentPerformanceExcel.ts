export interface AgentPerformanceExportMeta {
  period: string;
  from?: string;
  to?: string;
}

function parseDurationToSeconds(value: string | number | undefined | null): number {
  if (value == null) return 0;
  const s = String(value).trim();
  if (!s) return 0;
  if (/^\d+$/.test(s)) return Number(s);
  const parts = s.split(':').map((p) => parseInt(p, 10) || 0);
  if (parts.length === 3) return parts[0] * 3600 + parts[1] * 60 + parts[2];
  if (parts.length === 2) return parts[0] * 60 + parts[1];
  return 0;
}

function formatDuration(seconds: number): string {
  if (!seconds && seconds !== 0) return '--:--';
  const m = Math.floor(seconds / 60);
  const s = seconds % 60;
  return `${m}m ${s}s`;
}

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: 'USD',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(amount);
}

export async function exportAgentPerformanceExcel(
  rows: Record<string, unknown>[],
  filename: string,
  meta?: AgentPerformanceExportMeta,
): Promise<void> {
  const ExcelJS = (await import('exceljs')).default;
  const workbook = new ExcelJS.Workbook();
  workbook.creator = 'ALTRX CRM';
  workbook.created = new Date();

  const sheet = workbook.addWorksheet('Agent Performance', {
    views: [{ state: 'frozen', ySplit: 1 }],
  });

  sheet.columns = [
    { header: 'Agent', key: 'agent', width: 26 },
    { header: 'Calls Handled', key: 'calls', width: 16 },
    { header: 'VICI Sales', key: 'viciSales', width: 14 },
    { header: 'Form Sales', key: 'formSales', width: 14 },
    { header: 'Form Revenue', key: 'formRevenue', width: 18, style: { numFmt: '"$"#,##0.00' } },
    { header: 'Contacts', key: 'contacts', width: 12 },
    { header: 'Conversion %', key: 'conv', width: 14 },
    { header: 'AHT', key: 'aht', width: 12 },
  ];

  const headerRow = sheet.getRow(1);
  headerRow.font = { bold: true, color: { argb: 'FFFFFFFF' } };
  headerRow.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF1F2937' } };
  headerRow.alignment = { vertical: 'middle', horizontal: 'left' };
  headerRow.height = 22;

  let totalCalls = 0;
  let totalViciSales = 0;
  let totalFormSales = 0;
  let totalFormRevenue = 0;
  let totalContacts = 0;
  let weightedConvSum = 0;
  let weightedConvDen = 0;
  let totalAhtSeconds = 0;
  let ahtCount = 0;

  rows.forEach((r) => {
    const name = String(r.Name ?? r.User ?? '');
    const calls = Number(r.Calls_Handled ?? 0);
    const viciSales = Number(r.Sales_Made ?? 0);
    const formSales = Number(r.Form_Sales_Count ?? 0);
    const formRevenue = Number(r.Form_Sales_Amount ?? 0);
    const contacts = Number(r.Contacts ?? 0);
    const conv = r.Conversion_Percentage != null ? Number(r.Conversion_Percentage) : null;
    const ahtSec = parseDurationToSeconds(r.AHT as string | number | undefined | null);
    const ahtText = ahtSec > 0 ? formatDuration(ahtSec) : '--:--';

    sheet.addRow({
      agent: name,
      calls,
      viciSales,
      formSales,
      formRevenue,
      contacts,
      conv: conv != null ? Number(conv.toFixed(1)) : 0,
      aht: ahtText,
    });

    totalCalls += calls;
    totalViciSales += viciSales;
    totalFormSales += formSales;
    totalFormRevenue += formRevenue;
    totalContacts += contacts;
    if (conv != null && calls > 0) {
      weightedConvSum += conv * calls;
      weightedConvDen += calls;
    }
    if (ahtSec > 0) {
      totalAhtSeconds += ahtSec;
      ahtCount += 1;
    }
  });

  const totalConv = weightedConvDen > 0 ? Number(((weightedConvSum / weightedConvDen)).toFixed(1)) : 0;
  const avgAht = ahtCount > 0 ? formatDuration(Math.round(totalAhtSeconds / ahtCount)) : '--:--';

  const totalRow = sheet.addRow({
    agent: 'TOTAL',
    calls: totalCalls,
    viciSales: totalViciSales,
    formSales: totalFormSales,
    formRevenue: totalFormRevenue,
    contacts: totalContacts,
    conv: totalConv,
    aht: avgAht,
  });
  totalRow.font = { bold: true };
  totalRow.getCell('agent').alignment = { horizontal: 'right' };
  totalRow.getCell('formRevenue').numFmt = '"$"#,##0.00';

  if (meta) {
    const infoSheet = workbook.addWorksheet('Info');
    infoSheet.columns = [
      { header: 'Field', key: 'field', width: 16 },
      { header: 'Value', key: 'value', width: 40 },
    ];
    const infoHeader = infoSheet.getRow(1);
    infoHeader.font = { bold: true, color: { argb: 'FFFFFFFF' } };
    infoHeader.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF1F2937' } };
    infoHeader.height = 22;

    infoSheet.addRow({ field: 'Generated at', value: new Date().toISOString() });
    infoSheet.addRow({ field: 'Period', value: meta.period });
    if (meta.from) infoSheet.addRow({ field: 'From', value: meta.from });
    if (meta.to) infoSheet.addRow({ field: 'To', value: meta.to });
    infoSheet.addRow({ field: 'Total agents', value: rows.length });
    infoSheet.addRow({ field: 'Total calls', value: totalCalls });
    infoSheet.addRow({ field: 'Total VICI sales', value: totalViciSales });
    infoSheet.addRow({ field: 'Total form sales', value: totalFormSales });
    infoSheet.addRow({ field: 'Total form revenue', value: formatCurrency(totalFormRevenue) });
    infoSheet.addRow({ field: 'Weighted conversion %', value: `${totalConv}%` });
  }

  const buffer = await workbook.xlsx.writeBuffer();
  const blob = new Blob([buffer], {
    type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}