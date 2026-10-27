import { test, expect, type Page } from '@playwright/test'
import { readFile } from 'node:fs/promises'

// Only synthetic accounts and intercepted API calls. This UI test runs without
// email delivery or a database and never touches a real user's recovery key.
async function setup(page: Page, telegram: boolean, pin = false) {
  const user = { id: '11111111-1111-4111-8111-111111111111', email: 'recovery-ui@example.test', baseCurrency: 'RUB', registeredAtUtc: '2026-10-27T00:00:00Z' }
  const session = { accessToken: 'synthetic-test-access', refreshToken: 'synthetic-test-refresh', expiresAtUtc: '2099-01-01T00:00:00Z' }
  const document = { format: 1, baseCurrency: 'RUB', accounts: [{ id: '22222222-2222-4222-8222-222222222222', name: 'Основной счёт', currency: 'RUB', openingBalance: '0.00', isArchived: false, version: '33333333-3333-4333-8333-333333333333' }], categories: [], transactions: [], transfers: [], budgets: [], requests: [], imports: [] }
  const bodies: string[] = []
  await page.route('**/api/**', route => {
    const path = new URL(route.request().url()).pathname
    bodies.push(route.request().postData() ?? '')
    if (path === '/api/auth/telegram/login') return route.fulfill({ json: { linkRequired: false, session } })
    if (path === '/api/auth/refresh') return route.fulfill({ json: session })
    if (path === '/api/users/me') return route.fulfill({ json: user })
    if (path === '/api/private-finance') return route.fulfill({ json: { vault: null } })
    if (path === '/api/private-finance/legacy') return route.fulfill({ json: { document: JSON.stringify(document), fingerprint: 'A'.repeat(64) } })
    return route.fulfill({ status: 404, json: {} })
  })
  if (telegram) await page.addInitScript(() => {
    window.Telegram = { WebApp: { initData: 'synthetic-test-launch', ready() {}, expand() {} } }
  })
  await page.goto('/')
  await page.getByRole('button', { name: pin ? 'Настроить вход по PIN' : 'Только код восстановления', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Сохраните код восстановления', exact: true })).toBeVisible()
  return bodies
}

test('quick PIN setup is explicit and cannot finish with an empty PIN or without saving the backup', async ({ page }) => {
  const bodies = await setup(page, false, true)
  const before = bodies.filter(Boolean)
  await expect(page.getByRole('checkbox', { name: 'Настроить PIN на этом устройстве', exact: true })).toBeChecked()
  await expect(page.getByLabel('Новый PIN', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Начать пользоваться', exact: true })).toBeDisabled()
  await page.getByRole('checkbox', { name: 'Я сохранил код восстановления', exact: true }).check()
  await page.getByRole('button', { name: 'Начать пользоваться', exact: true }).click()
  await expect(page.getByRole('alert')).toContainText('одинаковый PIN из 6 цифр')
  await expect(page.getByRole('heading', { name: 'Сохраните код восстановления', exact: true })).toBeVisible()
  expect(bodies.filter(Boolean)).toEqual(before)
})

test('recovery code is hidden by default, copies exactly, and downloads as a readable local file', async ({ page, context }) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write'])
  const bodies = await setup(page, false)
  await expect(page.getByLabel('Новый код восстановления')).toHaveCount(0)
  await page.getByRole('button', { name: 'Скопировать код', exact: true }).click()
  await expect(page.getByRole('status')).toContainText('Код скопирован')
  const code = await page.evaluate(() => navigator.clipboard.readText())
  expect(code).toMatch(/^FT1\.[A-Za-z0-9_-]{43}\.[A-F0-9]{8}$/)
  await expect(page.getByRole('checkbox', { name: 'Я сохранил код восстановления', exact: true })).not.toBeChecked()
  const pending = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Сохранить файл', exact: true }).click()
  const download = await pending
  expect(download.suggestedFilename()).toBe('finance-recovery-code.txt')
  const contents = await readFile((await download.path())!, 'utf8')
  expect(contents).toContain(code)
  expect(contents).toContain('код восстановления')
  expect(contents).not.toContain('userId')
  expect(bodies.join('')).not.toContain(code)
  await page.getByRole('button', { name: 'Показать код', exact: true }).click()
  await expect(page.getByLabel('Новый код восстановления')).toHaveValue(code)
  await page.getByRole('button', { name: 'Скрыть код', exact: true }).click()
  await expect(page.getByLabel('Новый код восстановления')).toHaveCount(0)
})

test('Telegram without file support offers copy and selects the code when clipboard access is denied', async ({ page }) => {
  await page.addInitScript(() => {
    Object.defineProperty(navigator, 'share', { configurable: true, value: undefined })
    Object.defineProperty(navigator, 'canShare', { configurable: true, value: undefined })
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText: async () => { throw new DOMException('blocked', 'NotAllowedError') } } })
  })
  const bodies = await setup(page, true)
  await expect(page.getByRole('button', { name: 'Сохранить файл', exact: true })).toHaveCount(0)
  await page.getByRole('button', { name: 'Скопировать код', exact: true }).click()
  await expect(page.getByRole('alert')).toContainText('Не удалось скопировать автоматически')
  const codeField = page.getByLabel('Новый код восстановления')
  await expect(codeField).toBeVisible()
  expect(await codeField.evaluate((field: HTMLTextAreaElement) => field.selectionStart === 0 && field.selectionEnd === field.value.length)).toBe(true)
  await codeField.press('ArrowRight')
  await page.getByRole('button', { name: 'Скопировать код', exact: true }).click()
  expect(await codeField.evaluate((field: HTMLTextAreaElement) => field.selectionStart === 0 && field.selectionEnd === field.value.length)).toBe(true)
  expect(bodies.join('')).not.toContain(await codeField.inputValue())
  await expect(page.getByRole('checkbox', { name: 'Я сохранил код восстановления', exact: true })).not.toBeChecked()
})

test('Telegram uses the supported system sheet and treats its cancellation as cancellation', async ({ page }) => {
  await page.addInitScript(() => {
    Object.defineProperty(navigator, 'canShare', { configurable: true, value: (data: ShareData) => Boolean(data.files?.length) })
    Object.defineProperty(navigator, 'share', { configurable: true, value: async (data: ShareData) => {
      if (data.url || data.files?.[0].name !== 'finance-recovery-code.txt') throw new Error('Unexpected sharing request')
      sessionStorage.setItem('test-share-used', 'true')
      throw new DOMException('cancelled', 'AbortError')
    } })
  })
  await setup(page, true)
  await page.getByRole('button', { name: 'Сохранить файл', exact: true }).click()
  await expect(page.getByRole('status')).toContainText('Сохранение отменено')
  expect(await page.evaluate(() => sessionStorage.getItem('test-share-used'))).toBe('true')
  await expect(page.getByRole('checkbox', { name: 'Я сохранил код восстановления', exact: true })).not.toBeChecked()
})
