import { test } from 'node:test'
import assert from 'node:assert/strict'
import { expenseChart } from '../src/features/dashboard/expenseChart.ts'
const row = (id, amount, type = 'Expense') => ({ categoryId: id, categoryName: id, amount, type })

test('expense chart ignores income and zero expenses, preserves exact totals and shares', () => {
  const chart = expenseChart([row('food', '75.00'), row('transport', '25.00'), row('zero', '0.00'), row('salary', '500.00', 'Income')])
  assert.equal(chart.total, '100.00')
  assert.deepEqual(chart.slices.map(s => [s.id, s.percent, s.share]), [['food', '75%', 75], ['transport', '25%', 25]])
  assert.equal(chart.slices[1].offset, 75)
  assert.deepEqual(expenseChart([row('salary', '500.00', 'Income')]), { total: '0.00', slices: [] })
  const single = expenseChart([row('food', '0.01')]).slices[0]
  assert.equal(single.share, 100)
  assert.equal(single.percent, '100%')
})
test('expense chart keeps maximum money and tiny categories exact without floating-point amounts', () => {
  const chart = expenseChart([row('big', '9999999999999999.99'), row('small', '0.01')])
  assert.equal(chart.total, '10000000000000000.00')
  assert.equal(chart.slices[1].amount, '0.01')
  assert.equal(chart.slices[1].percent, '<0,1%')
  assert.equal(chart.slices.reduce((sum, slice) => sum + slice.share, 0), 100)
})
test('many categories are grouped without losing cents or double-counting them', () => {
  const chart = expenseChart(Array.from({ length: 10 }, (_, i) => row(`cat-${i}`, `${i + 1}.00`)))
  assert.equal(chart.total, '55.00')
  assert.equal(chart.slices.length, 7)
  assert.deepEqual([chart.slices[6].name, chart.slices[6].amount, chart.slices[6].count], ['Остальные', '10.00', 4])
  assert.ok(Math.abs(chart.slices.reduce((sum, s) => sum + s.share, 0) - 100) < 0.000001)
})
