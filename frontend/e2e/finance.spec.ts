import { test, expect, type Page, type APIRequestContext } from '@playwright/test'
import { createHmac } from 'node:crypto'
import { todayInMoscow } from '../src/features/transactions/validation.ts'

test.skip(process.env.E2E_TELEGRAM !== 'true', 'Requires the isolated Telegram API test stack')
test.setTimeout(90_000)
function launchData(id: number) {
  const fields = { auth_date: String(Math.floor(Date.now() / 1000)), user: JSON.stringify({ id, first_name: 'CI' }) }
  const secret = createHmac('sha256', 'WebAppData').update('123456789:test-only-synthetic-bot-token-never-a-real-secret').digest()
  const hash = createHmac('sha256', secret).update(Object.entries(fields).map(([key, value]) => `${key}=${value}`).join('\n')).digest('hex')
  return new URLSearchParams({ ...fields, hash }).toString()
}
async function sdk(page: Page, initData: string) {
  await page.addInitScript(launch => {
    const downloads: { url: string; file_name: string }[] = []
    Object.assign(window, { financeDownloads: downloads })
    window.Telegram = { WebApp: { initData: launch, ready() {}, expand() {}, isVersionAtLeast() { return true },
      downloadFile(params, callback) { downloads.push(params); callback?.(true) } } }
  }, initData)
}
async function identity(request: APIRequestContext) {
  const initData = launchData(Date.now() + Math.floor(Math.random() * 1_000_000))
  const result = await request.post('/api/auth/telegram/register', { data: { initData } }); expect(result.ok()).toBe(true)
  const session = await result.json()
  return { initData, headers: { Authorization: `Bearer ${session.accessToken}` }, owner: session.user.id }
}

test('Telegram expense retry, second device, editing, native encrypted backup and restoration use the real server', async ({ page, request, browser }) => {
  const user = await identity(request); await sdk(page, user.initData)
  const paths: string[] = []; page.on('request', r => { if (r.url().includes('/api/')) paths.push(new URL(r.url()).pathname) })
  let loseResponse = true
  await page.route('**/api/finance/transactions', async route => {
    if (route.request().method() === 'POST' && loseResponse) {
      loseResponse = false; const saved = await route.fetch(); expect(saved.ok()).toBe(true); await route.abort('failed')
    } else await route.continue()
  })
  await page.goto('/categories')
  await page.getByRole('button', { name: 'Добавить расход: Продукты', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: 'Продукты', exact: true })
  await dialog.getByLabel('Название', { exact: true }).fill('Чипсы')
  await dialog.getByLabel('Сумма (RUB)', { exact: true }).fill('25,01')
  await dialog.getByRole('button', { name: 'Добавить расход', exact: true }).click()
  await expect(dialog.getByRole('alert')).toBeVisible()
  await dialog.getByRole('button', { name: 'Добавить расход', exact: true }).click()
  await expect(dialog).toHaveCount(0)
  const firstHistory = await (await request.get('/api/finance/transactions', { headers: user.headers })).json()
  expect(firstHistory.totalCount).toBe(1)
  expect(firstHistory.items[0].amount).toBe('25.01')
  const other = await browser.newContext(); const device = await other.newPage(); await sdk(device, user.initData)
  try {
    await device.goto('/')
    await expect(device.locator('.dashboard-metrics .expense')).toContainText('25,01')
    await page.goto('/account')
    await page.getByRole('button', { name: 'Подготовить зашифрованную копию', exact: true }).click()
    await page.getByRole('button', { name: 'Сохранить копию', exact: true }).click()
    const download = await page.evaluate(() => (window as Window & { financeDownloads: { url: string }[] }).financeDownloads[0])
    const response = await request.get(download.url); expect(response.ok()).toBe(true)
    const backup = await response.text(); expect(backup).not.toContain('Чипсы')
    expect((await request.get(download.url)).status()).toBe(410)
    await page.goto('/transactions')
    const row = page.locator('.transaction-row').filter({ hasText: 'Чипсы' })
    await row.getByRole('button', { name: 'Изменить', exact: true }).click()
    await row.getByLabel('Сумма (RUB)', { exact: true }).fill('30')
    await row.getByRole('button', { name: 'Сохранить', exact: true }).click()
    await expect(row).toContainText('30,00')
    await device.reload(); await expect(device.locator('.dashboard-metrics .expense')).toContainText('30,00')
    await page.goto('/account')
    await page.getByText('Восстановить копию', { exact: true }).click()
    await page.getByLabel('Зашифрованный файл', { exact: true }).setInputFiles({ name: 'finance.json', mimeType: 'application/json', buffer: Buffer.from(backup) })
    await page.getByRole('checkbox', { name: 'Заменить текущую историю выбранной копией.', exact: true }).check()
    await page.getByRole('button', { name: 'Восстановить выбранную копию', exact: true }).click()
    await expect(page.getByText('Копия восстановлена.', { exact: true })).toBeVisible()
    await device.reload(); await expect(device.locator('.dashboard-metrics .expense')).toContainText('25,01')
    await page.goto('/transactions'); page.once('dialog', d => d.accept())
    await page.locator('.transaction-row').getByRole('button', { name: 'Удалить', exact: true }).click()
    await expect(page.locator('.transaction-row')).toHaveCount(0)
    expect(paths.some(path => /private-finance|finance\/legacy/.test(path))).toBe(false)
  } finally { await other.close() }
})

test('CSV preview/apply, duplicate protection and monthly budget use server records', async ({ page, request }) => {
  const user = await identity(request); await sdk(page, user.initData)
  await page.goto('/transactions')
  await page.getByText('Импорт из CSV', { exact: true }).click()
  const content = `Дата;Сумма;Категория;Описание\n${todayInMoscow()};25,01;Продукты;"Чипсы; сыр"\n`
  const upload = { name: 'finance.csv', mimeType: 'text/csv', buffer: Buffer.from(content) }
  await page.getByLabel('CSV-файл', { exact: true }).setInputFiles(upload)
  await page.getByLabel('Колонка описания', { exact: true }).selectOption('3')
  await page.getByRole('button', { name: 'Предпросмотр CSV', exact: true }).click()
  await expect(page.locator('.import-preview')).toContainText('Чипсы; сыр')
  await page.getByRole('button', { name: 'Импортировать 1 операций', exact: true }).click()
  await expect(page.getByText('Импортировано операций: 1.', { exact: true })).toBeVisible()
  await expect(page.locator('.transaction-row')).toHaveCount(1)
  await page.getByLabel('CSV-файл', { exact: true }).setInputFiles(upload)
  await page.getByLabel('Колонка описания', { exact: true }).selectOption('3')
  await page.getByRole('button', { name: 'Предпросмотр CSV', exact: true }).click()
  await expect(page.getByRole('alert').filter({ hasText: 'уже импортирован' })).toBeVisible()
  await page.goto('/')
  await page.getByText('Добавить лимит', { exact: true }).click()
  await page.getByLabel('Категория бюджета', { exact: true }).selectOption({ label: 'Продукты' })
  await page.getByLabel('Лимит (RUB)', { exact: true }).fill('20')
  await page.getByRole('button', { name: 'Сохранить лимит', exact: true }).click()
  await expect(page.locator('.budget-card')).toContainText('5,01')
  await expect(page.getByRole('article', { name: 'Расходы по категориям', exact: true })).toBeVisible()
  await page.setViewportSize({ width: 390, height: 844 })
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
})
