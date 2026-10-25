import { test } from 'node:test'
import assert from 'node:assert/strict'
import { transferSchema } from '../src/features/accounts/transferValidation.ts'

test('transfers require distinct accounts and preserve decimal amounts', () => {
  const input = { sourceAccountId: 'card', targetAccountId: 'cash', amount: '25,01', description: ' Наличные ' }
  assert.deepEqual(transferSchema.parse(input), { ...input, description: 'Наличные' })
  assert.equal(transferSchema.safeParse({ ...input, targetAccountId: 'card' }).success, false)
  for (const amount of ['0', '-1', '1.001', '10000000000000000']) assert.equal(transferSchema.safeParse({ ...input, amount }).success, false)
})
