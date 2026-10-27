import { test, expect } from '@playwright/test'
import { newRecoveryCode, keyFromCode, encryptDocument } from '../src/features/privateFinance/crypto.ts'
import { parseDocument } from '../src/features/privateFinance/model.ts'

test('expense donut uses private period totals, supports touch and keyboard, currency display and an empty period', async ({ page }) => {
  const owner = crypto.randomUUID(), vaultId = crypto.randomUUID(), account = crypto.randomUUID(), target = crypto.randomUUID()
  const food = crypto.randomUUID(), transport = crypto.randomUUID(), salary = crypto.randomUUID()
  const code = await newRecoveryCode(), key = await keyFromCode(code)
  const document = parseDocument({ format: 1, baseCurrency: 'RUB', accounts: [account, target].map((id, i) => ({ id, name: i ? 'Наличные' : 'Основной счёт', currency: 'RUB', openingBalance: '0.00', isArchived: false, version: crypto.randomUUID() })),
    categories: [{ id: food, name: 'Продукты', type: 'Expense', isActive: true }, { id: transport, name: 'Транспорт', type: 'Expense', isActive: true }, { id: salary, name: 'Зарплата', type: 'Income', isActive: true }],
    transactions: [[food, 'Expense', '75.00', '2026-10-15'], [transport, 'Expense', '25.00', '2026-10-15'], [salary, 'Income', '500.00', '2026-10-15'], [food, 'Expense', '900.00', '2026-09-01']].map(([categoryId, type, amount, occurredOn]) => ({ id: crypto.randomUUID(), accountId: account, categoryId, type, amount, description: null, occurredOn, createdAtUtc: '2026-10-15T12:00:00Z', version: crypto.randomUUID() })),
    transfers: [{ id: crypto.randomUUID(), requestId: crypto.randomUUID(), sourceAccountId: account, targetAccountId: target, currency: 'RUB', amount: '100.00', description: null, occurredOn: '2026-10-15', createdAtUtc: '2026-10-15T12:00:00Z', cancelledAtUtc: null, version: crypto.randomUUID() }], budgets: [], requests: [], imports: [] })
  const head = { id: vaultId, revision: 1, mutationId: crypto.randomUUID(), format: 1, isActive: true, updatedAtUtc: '2026-10-15T12:00:00Z', ...await encryptDocument(document, key, owner, vaultId, 1) }
  const paths: string[] = []
  await page.route('**/api/**', route => {
    const path = new URL(route.request().url()).pathname; paths.push(path)
    if (path === '/api/auth/refresh') return route.fulfill({ json: { accessToken: 'synthetic-chart-test', expiresAtUtc: '2099-01-01T00:00:00Z' } })
    if (path === '/api/users/me') return route.fulfill({ json: { id: owner, email: 'chart@example.test', baseCurrency: 'RUB', registeredAtUtc: '2026-10-01T00:00:00Z' } })
    if (path === '/api/private-finance') return route.fulfill({ json: { vault: head } })
    if (path === '/api/currencies/rates') return route.fulfill({ json: { date: '2026-10-27', source: 'test', rublesPerUnit: { RUB: '1', USD: '100', EUR: '110' } } })
    return route.fulfill({ status: 404, json: {} })
  })
  await page.goto('/')
  await page.getByLabel('Код восстановления', { exact: true }).fill(code)
  await page.getByRole('button', { name: 'Разблокировать', exact: true }).click()
  await page.locator('.dashboard-period > summary').click()
  await page.getByLabel('С даты', { exact: true }).fill('2026-10-01')
  await page.getByLabel('По дату', { exact: true }).fill('2026-10-31')
  const chart = page.getByRole('article', { name: 'Расходы по категориям', exact: true })
  await expect(chart.getByRole('img')).toHaveAccessibleName(/100,00/)
  await expect(chart.getByRole('button', { name: /Продукты/ })).toContainText('75%')
  await expect(chart.getByRole('button', { name: /Транспорт/ })).toContainText('25%')
  await expect(chart.getByText('Зарплата', { exact: true })).toHaveCount(0)
  await chart.getByRole('button', { name: /Продукты/ }).click()
  await expect(chart.getByRole('button', { name: /Продукты/ })).toHaveAttribute('aria-pressed', 'true')
  await expect(chart.locator('.expense-donut-center')).toContainText('75,00')
  await chart.getByRole('button', { name: /Продукты/ }).press('Space')
  await expect(chart.getByRole('button', { name: /Продукты/ })).toHaveAttribute('aria-pressed', 'false')
  await page.setViewportSize({ width: 390, height: 844 })
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  await chart.evaluate(element => element.scrollIntoView({ block: 'center' }))
  await chart.screenshot({ path: 'test-results/expense-chart-card-mobile.png' })
  await page.screenshot({ path: 'test-results/expense-chart-mobile.png', fullPage: true })
  await page.setViewportSize({ width: 1280, height: 900 })
  await chart.scrollIntoViewIfNeeded()
  await page.screenshot({ path: 'test-results/expense-chart-desktop.png', fullPage: true })
  // Currency conversion changes labels, not the underlying expense shares.
  await page.getByRole('combobox', { name: 'Валюта отображения', exact: true }).selectOption('USD')
  await expect(chart.locator('.expense-donut-center')).toContainText('1,00')
  await expect(chart.getByRole('button', { name: /Продукты/ })).toContainText('75%')
  await page.getByLabel('С даты', { exact: true }).fill('2026-11-01')
  await page.getByLabel('По дату', { exact: true }).fill('2026-11-30')
  await expect(chart).toHaveCount(0)
  expect(paths.some(path => path === '/api/dashboard' || path === '/api/transactions')).toBe(false)
})
