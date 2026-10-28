import { test, expect, type Page } from '@playwright/test'
import { newRecoveryCode, keyFromCode, encryptDocument } from '../src/features/privateFinance/crypto.ts'
import { parseDocument } from '../src/features/privateFinance/model.ts'
import { todayInMoscow } from '../src/features/transactions/validation.ts'

async function delayedVault(page: Page) {
  const userId = crypto.randomUUID(), id = crypto.randomUUID(), accountId = crypto.randomUUID(), categoryId = crypto.randomUUID()
  const code = await newRecoveryCode(), key = await keyFromCode(code)
  const document = parseDocument({ format: 1, baseCurrency: 'RUB', accounts: [{ id: accountId, name: 'Основной счёт', currency: 'RUB', openingBalance: '0.00', isArchived: false, version: crypto.randomUUID() }],
    categories: [{ id: categoryId, name: 'Продукты', type: 'Expense', isActive: true }],
    transactions: [{ id: crypto.randomUUID(), accountId, categoryId, type: 'Expense', amount: '25.00', description: 'Чипсы', occurredOn: todayInMoscow(), createdAtUtc: new Date().toISOString(), version: crypto.randomUUID() }], transfers: [], budgets: [], requests: [], imports: [] })
  const head = { id, revision: 1, mutationId: crypto.randomUUID(), format: 1, isActive: true, updatedAtUtc: new Date().toISOString(), ...await encryptDocument(document, key, userId, id, 1) }
  let release!: () => void
  const wait = new Promise<void>(resolve => { release = resolve })
  await page.addInitScript(() => { window.Telegram = { WebApp: { initData: 'synthetic-appearance', ready() {}, expand() {} } } })
  await page.route('**/api/**', async route => {
    const path = new URL(route.request().url()).pathname
    if (path === '/api/auth/options') return route.fulfill({ json: { emailRegistrationEnabled: false, telegramOnly: true, managedFinancialKeys: true } })
    if (path === '/api/auth/telegram/login') return route.fulfill({ json: { linkRequired: false, session: { accessToken: 'synthetic-appearance-session', expiresAtUtc: '2099-01-01T00:00:00Z', user: { id: userId, email: null, baseCurrency: 'RUB', registeredAtUtc: new Date().toISOString() } } } })
    if (path === '/api/private-finance') return route.fulfill({ json: { vault: head } })
    if (path === '/api/private-finance/key') { await wait; return route.fulfill({ json: { code } }) }
    if (path === '/api/currencies/rates') return route.fulfill({ json: { date: todayInMoscow(), source: 'test', rublesPerUnit: { RUB: '1', USD: '100', EUR: '110' } } })
    return route.fulfill({ status: 404, json: {} })
  })
  return release
}

for (const reduced of [false, true]) test(`financial data replaces accessible skeletons and stays usable with reduced motion ${reduced}`, async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.emulateMedia({ reducedMotion: reduced ? 'reduce' : 'no-preference' })
  const release = await delayedVault(page)
  await page.goto('/')
  const skeleton = page.getByRole('status', { name: 'Открываем финансовые данные…', exact: true })
  await expect(skeleton).toBeVisible()
  await expect(skeleton).toHaveAttribute('aria-busy', 'true')
  await expect(page.getByRole('article', { name: 'Расходы по категориям', exact: true })).toHaveCount(0)
  expect(await skeleton.locator('.skeleton-block').first().evaluate(element => getComputedStyle(element, '::after').animationName)).toBe(reduced ? 'none' : 'skeleton-shimmer')
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  if (!reduced) await page.screenshot({ path: 'test-results/data-loading-mobile.png', fullPage: true })
  release()
  await expect(page.getByRole('heading', { name: 'Обзор финансов', exact: true })).toBeVisible()
  await expect(skeleton).toHaveCount(0)
  await expect(page.locator('.dashboard-metrics .expense')).toContainText('25,00')
  await expect(page.locator('.dashboard-metrics .expense')).toHaveCSS('animation-name', reduced ? 'none' : 'data-enter')
  if (!reduced) await page.screenshot({ path: 'test-results/data-ready-mobile.png', fullPage: true })
  await page.getByRole('navigation', { name: 'Основная навигация' }).getByRole('link', { name: 'Категории', exact: true }).click()
  await page.getByRole('button', { name: 'Добавить расход: Продукты', exact: true }).click()
  await expect(page.getByRole('dialog', { name: 'Продукты', exact: true }).getByLabel('Название', { exact: true })).toBeFocused()
})
