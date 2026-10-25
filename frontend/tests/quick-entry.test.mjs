import { test } from 'node:test'
import assert from 'node:assert/strict'
import { chooseQuickAccount, quickEntrySchema } from '../src/features/transactions/quickEntry.ts'

test('quick entry requires only a purchase name and positive precise amount', () => {
  assert.deepEqual(quickEntrySchema.parse({ description: ' Чипсы ', amount: '25,01' }), { description: 'Чипсы', amount: '25,01' })
  for (const amount of ['0', '-25', '25.001', 'NaN']) assert.equal(quickEntrySchema.safeParse({ description: 'Чипсы', amount }).success, false)
  assert.equal(quickEntrySchema.safeParse({ description: ' ', amount: '25' }).success, false)
})

test('quick account uses an active preference, primary account, then any active account', () => {
  const primary = { id: 'owner', isArchived: false }
  const preferred = { id: 'card', isArchived: false }
  const archived = { id: 'closed', isArchived: true }
  assert.equal(chooseQuickAccount([preferred, primary, archived], 'card', 'owner'), preferred)
  assert.equal(chooseQuickAccount([preferred, primary, archived], 'closed', 'owner'), primary)
  assert.equal(chooseQuickAccount([preferred, archived], null, 'owner'), preferred)
  assert.equal(chooseQuickAccount([archived], 'closed', 'owner'), undefined)
})
