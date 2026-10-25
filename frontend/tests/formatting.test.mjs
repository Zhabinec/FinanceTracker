import { test } from 'node:test'
import assert from 'node:assert/strict'
import { formatMoney, formatDate } from '../src/shared/formatting.ts'

test('money preserves cents across the entire database range', () => {
  assert.equal(formatMoney('9999999999999999.99', 'RUB').replace(/\s/g, ''), '9999999999999999,99₽')
  assert.equal(formatMoney('-0.01', 'RUB').replace(/\s/g, ''), '-0,01₽')
  assert.equal(formatMoney('123.40', 'RUB').replace(/\s/g, ''), '123,40₽')
})

test('calendar date is independent from the host timezone', () => {
  for (const zone of ['UTC', 'America/Los_Angeles', 'Europe/Moscow', 'Asia/Tokyo']) {
    process.env.TZ = zone
    assert.equal(formatDate('2026-10-25'), '25.10.2026')
  }
})
