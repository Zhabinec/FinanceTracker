import { test } from 'node:test'
import assert from 'node:assert/strict'
import { convertMoney } from '../src/features/currencies/conversion.ts'

test('display conversion preserves maximum amounts without floating point', () => {
  assert.equal(convertMoney('9999999999999999.99', '1', '1'), '9999999999999999.99')
  assert.equal(convertMoney('1000.00', '1', '100'), '10.00')
  assert.equal(convertMoney('10.00', '150', '100'), '15.00')
  assert.equal(convertMoney('-0.01', '1', '100'), '0.00')
})

test('only the final converted value is rounded, halves away from zero', () => {
  assert.equal(convertMoney('0.01', '1', '2'), '0.01')
  assert.equal(convertMoney('-0.01', '1', '2'), '-0.01')
  assert.equal(convertMoney('1.00', '1', '3'), '0.33')
  assert.equal(convertMoney('100.01', '1.2345', '3.4567'), '35.72')
  for (const value of ['0', '-1', 'NaN', '1e3']) assert.throws(() => convertMoney('10', '1', value))
})
