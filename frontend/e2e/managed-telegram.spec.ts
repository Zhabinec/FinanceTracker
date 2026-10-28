import { test, expect, type Page } from '@playwright/test'
import { newRecoveryCode, keyFromCode, encryptDocument } from '../src/features/privateFinance/crypto.ts'
import { parseDocument } from '../src/features/privateFinance/model.ts'

// Synthetic identities only. Real Telegram signatures and database ownership are tested by the API suite.
async function mockApp(page: Page, { existing = false, managed = true, telegram = true, failFirstStart = false } = {}) {
  const owner = crypto.randomUUID(), id = crypto.randomUUID(), code = await newRecoveryCode()
  const key = await keyFromCode(code)
  const document = parseDocument({ format: 1, baseCurrency: 'RUB', accounts: [{ id: crypto.randomUUID(), name: 'Основной счёт', currency: 'RUB', openingBalance: '0.00', isArchived: false, version: crypto.randomUUID() }], categories: [], transactions: [], transfers: [], budgets: [], requests: [], imports: [] })
  const head = { id, revision: 1, mutationId: crypto.randomUUID(), format: 1, isActive: true, updatedAtUtc: new Date().toISOString(), ...await encryptDocument(document, key, owner, id, 1) }
  let created = existing, enrolled = managed && existing, starts = 0
  const enrollments: unknown[] = [], paths: string[] = []
  await page.route('**/api/**', async route => {
    const path = new URL(route.request().url()).pathname; paths.push(path)
    const method = route.request().method()
    if (path === '/api/auth/options') return route.fulfill({ json: { telegramOnly: true, managedFinancialKeys: true } })
    if (path === '/api/auth/telegram/options') return route.fulfill({ json: { enabled: true, botUsername: 'PetProjectFinanceBot' } })
    if (path === '/api/auth/refresh') return route.fulfill({ status: 404, json: {} })
    if (path === '/api/auth/telegram/login') return route.fulfill({ json: { linkRequired: false, session: { accessToken: 'synthetic-managed-session', refreshToken: 'synthetic-refresh', expiresAtUtc: '2099-01-01T00:00:00Z' } } })
    if (path === '/api/users/me') return route.fulfill({ json: { id: owner, email: null, baseCurrency: 'RUB', registeredAtUtc: new Date().toISOString() } })
    if (path === '/api/auth/telegram/link') return route.fulfill({ json: { linked: true, telegramUserId: 123, username: 'synthetic' } })
    if (path === '/api/private-finance/key/start') {
      created = true; enrolled = true; starts++
      if (failFirstStart && starts === 1) return route.fulfill({ status: 503, json: { detail: 'Временная ошибка' } })
      return route.fulfill({ json: head })
    }
    if (path === '/api/private-finance/key') {
      if (method === 'POST') { enrollments.push(route.request().postDataJSON()); enrolled = true; return route.fulfill({ status: 204 }) }
      return route.fulfill({ json: { code: enrolled ? code : null } })
    }
    if (path === '/api/private-finance') return route.fulfill({ json: { vault: created ? head : null } })
    if (path === '/api/currencies/rates') return route.fulfill({ json: { date: '2026-10-28', source: 'test', rublesPerUnit: { RUB: '1', USD: '100', EUR: '110' } } })
    return route.fulfill({ status: 404, json: {} })
  })
  if (telegram) await page.addInitScript(() => {
    window.Telegram = { WebApp: { initData: 'synthetic-launch', ready() {}, expand() {} } }
  })
  return { code, enrollments, paths }
}

test('Telegram opens new and returning finances without a recovery code or local PIN', async ({ page }) => {
  const app = await mockApp(page)
  await page.goto('/')
  await page.getByRole('button', { name: 'Начать пользоваться', exact: true }).click()
  await expect(page.getByRole('navigation', { name: 'Основная навигация' })).toBeVisible()
  await page.goto('/account')
  await expect(page.getByText('Записи зашифрованы. Доступ к истории', { exact: false })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Заблокировать финансы', exact: true })).toHaveCount(0)
  await expect(page.getByLabel('Код восстановления', { exact: true })).toHaveCount(0)
  await page.reload()
  await expect(page.getByRole('navigation', { name: 'Основная навигация' })).toBeVisible()
  expect(app.paths.filter(path => path.endsWith('/key/start'))).toHaveLength(1)
  expect(await page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }))).not.toContain(app.code)
})

test('a committed bootstrap with a lost response recovers on retry without creating another vault', async ({ page }) => {
  await mockApp(page, { failFirstStart: true })
  await page.goto('/')
  await page.getByRole('button', { name: 'Начать пользоваться', exact: true }).click()
  await expect(page.getByRole('alert')).toBeVisible()
  await page.getByRole('button', { name: 'Начать пользоваться', exact: true }).click()
  await expect(page.getByRole('navigation', { name: 'Основная навигация' })).toBeVisible()
})

test('legacy keys are sent only after consent and successful decryption', async ({ page }) => {
  const app = await mockApp(page, { existing: true, managed: false })
  await page.goto('/account')
  await page.getByLabel('Код восстановления', { exact: true }).fill(app.code)
  await page.getByRole('button', { name: 'Разблокировать', exact: true }).click()
  await page.getByText('Вход через Telegram на всех устройствах', { exact: true }).click()
  const submit = page.getByRole('button', { name: 'Включить доступ на устройствах', exact: true })
  await page.getByLabel('Код восстановления для переноса', { exact: true }).fill(app.code)
  await expect(submit).toBeDisabled()
  expect(app.enrollments).toHaveLength(0)
  await page.getByRole('checkbox', { name: 'Разрешаю сервису хранить защищённую копию ключа', exact: false }).check()
  await page.getByLabel('Код восстановления для переноса', { exact: true }).fill(await newRecoveryCode())
  await submit.click()
  await expect(page.getByRole('alert')).toBeVisible()
  expect(app.enrollments).toHaveLength(0)
  await page.getByLabel('Код восстановления для переноса', { exact: true }).fill(app.code)
  await submit.click()
  await expect(page.getByRole('button', { name: 'Заблокировать финансы', exact: true })).toHaveCount(0)
  expect(app.enrollments).toHaveLength(1)
  await page.reload()
  await expect(page.getByRole('navigation', { name: 'Основная навигация' })).toBeVisible()
})

test('ordinary browser offers only the Telegram entry and no financial data', async ({ page }) => {
  const app = await mockApp(page, { telegram: false })
  await page.goto('/login/password')
  await expect(page.getByRole('link', { name: 'Открыть в Telegram', exact: true })).toHaveAttribute('href', 'https://t.me/PetProjectFinanceBot')
  await expect(page.getByLabel('Пароль', { exact: true })).toHaveCount(0)
  await expect(page.getByRole('link', { name: /Google/ })).toHaveCount(0)
  expect(app.paths.some(path => path.startsWith('/api/private-finance'))).toBe(false)
})
