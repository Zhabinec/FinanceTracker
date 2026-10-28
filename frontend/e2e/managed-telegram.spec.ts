import { test, expect } from '@playwright/test'
import { financialApp } from './fixtures/financialApp'
import { newRecoveryCode, keyFromCode, encryptDocument } from '../src/features/legacyFinance/crypto.ts'
import { parseDocument } from '../src/features/legacyFinance/model.ts'

test('Telegram opens new and returning finances without receiving a financial key', async ({ page }) => {
  const app = await financialApp(page)
  await page.goto('/')
  await expect(page.getByRole('navigation', { name: 'Основная навигация' })).toBeVisible()
  await page.goto('/account')
  await expect(page.getByText('Записи зашифрованы. Доступ к истории', { exact: false })).toBeVisible()
  await expect(page.getByLabel('Код восстановления', { exact: true })).toHaveCount(0)
  await page.reload()
  await expect(page.getByRole('navigation', { name: 'Основная навигация' })).toBeVisible()
  expect(app.paths.filter(path => path === '/api/finance/open')).toHaveLength(3)
  expect(app.paths.some(path => /private-finance|finance\/legacy|\/key/.test(path))).toBe(false)
  expect(app.paths).not.toContain('/api/users/me')
})

test('a lost open response can be retried explicitly', async ({ page }) => {
  await financialApp(page, { failFirstOpen: true })
  await page.goto('/')
  await expect(page.getByRole('alert')).toBeVisible()
  await page.getByRole('button', { name: 'Повторить', exact: true }).click()
  await expect(page.getByRole('navigation', { name: 'Основная навигация' })).toBeVisible()
})

test('legacy keys are sent only after consent and successful local decryption', async ({ page }) => {
  const app = await financialApp(page)
  const code = await newRecoveryCode(), key = await keyFromCode(code), vaultId = crypto.randomUUID()
  const document = parseDocument({ format: 1, baseCurrency: 'RUB', accounts: [{ id: app.owner, name: 'Основной счёт', currency: 'RUB', openingBalance: '0.00', isArchived: false, version: crypto.randomUUID() }], categories: [], transactions: [], transfers: [], budgets: [], requests: [], imports: [] })
  const vault = { id: vaultId, revision: 1, mutationId: crypto.randomUUID(), format: 1, isActive: true, updatedAtUtc: new Date().toISOString(), ...await encryptDocument(document, key, app.owner, vaultId, 1) }
  let enrolled = false
  const enrollments: unknown[] = []
  await page.route('**/api/finance/open', route => route.fulfill({ json: { ready: enrolled, requiresKey: !enrolled, baseCurrency: 'RUB', revision: 0 } }))
  await page.route('**/api/finance/legacy', route => route.fulfill({ json: { vault } }))
  await page.route('**/api/finance/legacy/enroll', route => { enrollments.push(route.request().postDataJSON()); enrolled = true; return route.fulfill({ status: 204 }) })
  await page.goto('/account')
  const input = page.getByLabel('Прежний код восстановления', { exact: true }), submit = page.getByRole('button', { name: 'Подключить историю', exact: true })
  await input.fill(code)
  await expect(submit).toBeDisabled()
  expect(enrollments).toHaveLength(0)
  await page.getByRole('checkbox', { name: 'Разрешаю сервису хранить защищённый финансовый ключ', exact: false }).check()
  await input.fill(await newRecoveryCode()); await submit.click()
  await expect(page.getByRole('alert')).toBeVisible(); expect(enrollments).toHaveLength(0)
  await input.fill(code); await submit.click()
  await expect(page.getByRole('navigation', { name: 'Основная навигация' })).toBeVisible()
  expect(enrollments).toHaveLength(1)
  expect(await page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }))).not.toContain(code)
})

test('ordinary browser offers only the Telegram entry and no financial data', async ({ page }) => {
  const app = await financialApp(page, { telegram: false })
  await page.goto('/login/password')
  await expect(page.getByRole('link', { name: 'Открыть в Telegram', exact: true })).toHaveAttribute('href', 'https://t.me/PetProjectFinanceBot')
  await expect(page.getByLabel('Пароль', { exact: true })).toHaveCount(0)
  expect(app.paths.some(path => path.startsWith('/api/finance/'))).toBe(false)
})
