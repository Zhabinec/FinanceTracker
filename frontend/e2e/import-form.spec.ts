import { test, expect } from '@playwright/test'
import { financialApp } from './fixtures/financialApp'

test('CSV upload exposes column mapping and sends the selected description column', async ({ page }) => {
  await financialApp(page)
  await page.route('**/api/finance/imports/csv/preview', async route => {
    expect(route.request().postDataJSON().mapping.description).toBe(3)
    await route.fulfill({ json: { rows: [{ row: 2, date: '2026-10-15', amount: '25.01', category: 'Продукты', type: 'Expense', description: 'Чипсы; сыр', errors: [] }], canApply: true, alreadyImported: false, currency: 'RUB' } })
  })
  await page.goto('/transactions')
  await page.getByText('Импорт из CSV', { exact: true }).click()
  await page.getByLabel('CSV-файл', { exact: true }).setInputFiles({ name: 'finance.csv', mimeType: 'text/csv', buffer: Buffer.from('Дата;Сумма;Категория;Описание\n2026-10-15;25,01;Продукты;"Чипсы; сыр"\n') })
  await page.getByRole('combobox', { name: 'Колонка описания', exact: true }).selectOption('3')
  await page.getByRole('button', { name: 'Предпросмотр CSV', exact: true }).click()
  await expect(page.locator('.import-preview')).toContainText('Чипсы; сыр')
})
