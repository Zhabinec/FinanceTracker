export type CategoryTotal = { categoryId: string; categoryName: string; type: 'Income' | 'Expense'; amount: string }
const colors = ['#287660', '#b76138', '#526fa0', '#95714a', '#925d85', '#53818a', '#7a8068']
const decimal = (value: bigint) => `${value / 100n}.${String(value % 100n).padStart(2, '0')}`
function cents(value: string) {
  if (!/^\d+\.\d{2}$/.test(value)) throw new Error('Некорректная сумма расходов.')
  return BigInt(value.replace('.', ''))
}
export function expenseChart(categories: CategoryTotal[]) {
  const expenses = categories.filter(row => row.type === 'Expense').map(row => ({ id: row.categoryId, name: row.categoryName, value: cents(row.amount), count: 1 }))
    .filter(row => row.value > 0n).sort((a, b) => a.value === b.value ? a.id.localeCompare(b.id) : a.value > b.value ? -1 : 1)
  const total = expenses.reduce((sum, row) => sum + row.value, 0n)
  if (!total) return { total: '0.00', slices: [] }
  const rows = expenses.length <= 7 ? expenses : [...expenses.slice(0, 6), {
    id: 'other', name: 'Остальные', value: expenses.slice(6).reduce((sum, row) => sum + row.value, 0n), count: expenses.length - 6,
  }]
  // Only geometry becomes Number. Amounts and percentage division stay exact.
  const parts = rows.map(row => row.value * 1_000_000n / total)
  parts[0] += 1_000_000n - parts.reduce((sum, value) => sum + value, 0n)
  let offset = 0
  const slices = rows.map((row, index) => {
    const tenths = (row.value * 1000n + total / 2n) / total
    const percent = tenths === 0n ? '<0,1%' : `${tenths / 10n}${tenths % 10n ? ',' + tenths % 10n : ''}%`
    const share = Number(parts[index]) / 10_000
    const slice = { id: row.id, name: row.name, amount: decimal(row.value), count: row.count, color: colors[index], percent, share, offset }
    offset += share
    return slice
  })
  return { total: decimal(total), slices }
}
