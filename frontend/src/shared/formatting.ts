// Money stays a decimal string from the API through rendering and editing.
export function formatMoney(value: string, currency: string) {
  const negative = value.startsWith('-')
  const [whole, fraction = ''] = value.replace(/^-/, '').split('.')
  const integer = BigInt(whole)
  const signed = negative ? (integer === 0n ? -0 : -integer) : integer
  return new Intl.NumberFormat('ru-RU', {
    style: 'currency', currency, minimumFractionDigits: 2, maximumFractionDigits: 2,
  }).formatToParts(signed).map((part) =>
    part.type === 'fraction' ? fraction.padEnd(2, '0') : part.value).join('')
}

// ISO calendar dates do not represent instants and must not use the browser timezone.
export function formatDate(value: string) {
  return value.split('-').reverse().join('.')
}
