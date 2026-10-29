export type CalendarKind = 'date' | 'month'
export function maskCalendarInput(text: string, kind: CalendarKind) {
  if (!/^[\d.]+$/.test(text)) return text // Preserve ISO paste and malformed input for validation.
  const digits = text.replaceAll('.', '')
  if (digits.length > (kind === 'date' ? 8 : 6)) return text
  return (kind === 'date' ? [digits.slice(0, 2), digits.slice(2, 4), digits.slice(4)]
    : [digits.slice(0, 2), digits.slice(2)]).filter(Boolean).join('.')
}
export const monthNames = ['Январь', 'Февраль', 'Март', 'Апрель', 'Май', 'Июнь', 'Июль', 'Август', 'Сентябрь', 'Октябрь', 'Ноябрь', 'Декабрь']
export function daysInMonth(month: string) {
  const [year, number] = month.split('-').map(Number)
  return new Date(Date.UTC(year, number, 0)).getUTCDate()
}
export function parseCalendarValue(text: string, kind: CalendarKind): string | null {
  if (!text) return ''
  const iso = kind === 'date' && /^\d{2}\.\d{2}\.\d{4}$/.test(text) ? text.split('.').reverse().join('-')
    : kind === 'month' && /^\d{2}\.\d{4}$/.test(text) ? text.split('.').reverse().join('-') : text
  if (!(kind === 'date' ? /^\d{4}-\d{2}-\d{2}$/ : /^\d{4}-\d{2}$/).test(iso)) return null
  const [year, month, day] = iso.split('-').map(Number)
  if (year < 1900 || year > 9998 || month < 1 || month > 12 ||
    (kind === 'date' && (day < 1 || day > daysInMonth(iso.slice(0, 7))))) return null
  return iso
}
export function displayCalendarValue(value: string, kind: CalendarKind) {
  return parseCalendarValue(value, kind) ? value.split('-').reverse().join('.') : ''
}
export function shiftMonth(month: string, delta: number) {
  const [year, number] = month.split('-').map(Number)
  const date = new Date(Date.UTC(year, number - 1 + delta, 1))
  return date.getUTCFullYear() < 1900 || date.getUTCFullYear() > 9998 ? null : date.toISOString().slice(0, 7)
}
