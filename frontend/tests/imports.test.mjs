import { test } from 'node:test'
import assert from 'node:assert/strict'
import { csvHeaders } from '../src/features/imports/csvHeaders.ts'

test('CSV column selection handles BOM, quoted delimiters, escapes and newlines', () => {
  assert.deepEqual(csvHeaders('\uFEFF"Дата;день";"Сумма ""руб""";Категория\r\n1;2;3', ';'), ['Дата;день', 'Сумма "руб"', 'Категория'])
  assert.deepEqual(csvHeaders('"First\ncolumn",Second\n1,2', ','), ['First\ncolumn', 'Second'])
  assert.deepEqual(csvHeaders('Дата\tСумма\n1\t2', '\t'), ['Дата', 'Сумма'])
})
