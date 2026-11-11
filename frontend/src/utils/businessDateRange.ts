const BUSINESS_TZ = 'America/Tijuana';

type BusinessPeriod = 'Today' | 'Week' | 'Month' | 'Custom' | 'All';

function dateString(year: number, month: number, day: number): string {
  return `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
}

function addDays(date: string, days: number): string {
  const [year, month, day] = date.split('-').map(Number);
  const result = new Date(Date.UTC(year, month - 1, day + days));
  return dateString(result.getUTCFullYear(), result.getUTCMonth() + 1, result.getUTCDate());
}

export function getBusinessDateRange(
  period: BusinessPeriod,
  customStart?: string,
  customEnd?: string,
): { from?: string; to?: string } {
  if (period === 'All') return {};
  if (period === 'Custom') {
    return customStart && customEnd
      ? { from: `${customStart} 00:00:00`, to: `${customEnd} 23:59:59` }
      : {};
  }

  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: BUSINESS_TZ,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(new Date());
  const get = (type: string) => parts.find((part) => part.type === type)?.value ?? '00';
  const today = dateString(Number(get('year')), Number(get('month')), Number(get('day')));
  const tomorrow = addDays(today, 1);

  if (period === 'Today') return { from: `${today} 00:00:00`, to: `${tomorrow} 00:00:00` };
  if (period === 'Week') {
    const [year, month, day] = today.split('-').map(Number);
    const weekday = new Date(Date.UTC(year, month - 1, day)).getUTCDay();
    const monday = addDays(today, -((weekday + 6) % 7));
    return { from: `${monday} 00:00:00`, to: `${tomorrow} 00:00:00` };
  }

  const [year, month] = today.split('-').map(Number);
  const monthStart = dateString(year, month, 1);
  const nextMonth = dateString(month === 12 ? year + 1 : year, month === 12 ? 1 : month + 1, 1);
  return { from: `${monthStart} 00:00:00`, to: `${nextMonth} 00:00:00` };
}
