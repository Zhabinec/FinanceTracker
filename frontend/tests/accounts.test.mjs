import { test } from 'node:test'
import assert from 'node:assert/strict'
import { openingBalanceSchema } from '../src/features/accounts/validation.ts'
import { transactionSchema, toTransactionInput } from '../src/features/transactions/validation.ts'

test('opening balance preserves cents, debt and maximum precision as strings', () => {
  assert.equal(openingBalanceSchema.parse('-9999999999999999,99'), '-9999999999999999.99')
  assert.equal(openingBalanceSchema.parse(' 0,01 '), '0.01')
  for (const value of ['1.001', '10000000000000000', 'NaN', '1e3', ''])
    assert.equal(openingBalanceSchema.safeParse(value).success, false)
})

test('transaction form requires a selected account and sends it unchanged', () => {
  const input = { accountId: 'owned-account-id', categoryId: 'category', amount: '10,01', type: 'Expense', description: '', occurredOn: '2026-10-25' }
  assert.equal(transactionSchema.safeParse({ ...input, accountId: '' }).success, false)
  const parsed = transactionSchema.parse(input)
  assert.equal(toTransactionInput(parsed).accountId, input.accountId)
  assert.equal(toTransactionInput(parsed).amount, '10.01')
})
