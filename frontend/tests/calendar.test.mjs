import { test } from 'node:test'
import assert from 'node:assert/strict'
import { parseCalendarValue, displayCalendarValue, daysInMonth, shiftMonth, maskCalendarInput } from '../src/shared/calendar.ts'

test('calendar accepts Russian and ISO input, rejects impossible dates rather than normalizing them', () => {
  assert.equal(maskCalendarInput('29022024', 'date'), '29.02.2024')
  assert.equal(maskCalendarInput('022024', 'month'), '02.2024')
  assert.equal(maskCalendarInput('2024-02-29', 'date'), '2024-02-29')
  assert.equal(maskCalendarInput('29.02.2024x', 'date'), '29.02.2024x')
  assert.equal(parseCalendarValue('29.02.2024', 'date'), '2024-02-29')
  assert.equal(parseCalendarValue('2026-10-01', 'date'), '2026-10-01')
  for (const value of ['29.02.2025', '31.04.2026', '00.01.2026', '2026-13-01', '01.01.1899', '01.01.9999', '15.10.'])
    assert.equal(parseCalendarValue(value, 'date'), null, value)
  assert.equal(parseCalendarValue('', 'date'), '')
  assert.equal(displayCalendarValue('2026-10-15', 'date'), '15.10.2026')
})
test('calendar month changes handle year boundaries, leap centuries and supported limits', () => {
  assert.equal(daysInMonth('1900-02'), 28)
  assert.equal(daysInMonth('2000-02'), 29)
  assert.equal(shiftMonth('2026-12', 1), '2027-01')
  assert.equal(shiftMonth('2026-01', -1), '2025-12')
  assert.equal(shiftMonth('1900-01', -1), null)
  assert.equal(shiftMonth('9998-12', 1), null)
  assert.equal(parseCalendarValue('02.2024', 'month'), '2024-02')
  assert.equal(parseCalendarValue('2024-00', 'month'), null)
  assert.equal(displayCalendarValue('2024-02', 'month'), '02.2024')
})
