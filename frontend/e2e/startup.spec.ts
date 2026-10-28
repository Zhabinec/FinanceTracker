import { test, expect } from '@playwright/test'

test('HTML startup remains visible while the Telegram SDK loads and API readiness starts in parallel', async ({ page }) => {
  let finishSdk!: () => void
  const sdkGate = new Promise<void>(resolve => { finishSdk = resolve })
  let policyRequests = 0
  await page.route('https://telegram.org/js/telegram-web-app.js', async route => {
    await sdkGate
    await route.fulfill({ contentType: 'application/javascript', body: 'window.Telegram={WebApp:{initData:"synthetic-startup",ready(){},expand(){}}}' })
  })
  await page.route('**/api/**', route => {
    const path = new URL(route.request().url()).pathname
    if (path === '/api/auth/options') { policyRequests++; return route.fulfill({ json: { emailRegistrationEnabled: false, telegramOnly: true, managedFinancialKeys: true } }) }
    if (path === '/api/auth/telegram/login') return route.fulfill({ json: { linkRequired: true, session: null } })
    return route.fulfill({ status: 404 })
  })
  await page.goto('/#tgWebAppData=synthetic-startup', { waitUntil: 'domcontentloaded' })
  await expect(page.getByRole('heading', { name: 'Finance Tracker', exact: true })).toBeVisible()
  await expect.poll(() => policyRequests).toBe(1)
  finishSdk()
  await expect(page.getByRole('button', { name: 'Начать в Telegram', exact: true })).toBeVisible()
  expect(policyRequests).toBe(1)
})

test('a sleeping API HTML page and temporary 503 are retried without accepting a false policy or requiring reload', async ({ page }) => {
  let requests = 0, authRequests = 0
  const externalFonts: string[] = []
  page.on('request', request => { if (/fonts\.(googleapis|gstatic)\.com/.test(request.url())) externalFonts.push(request.url()) })
  await page.route('**/api/**', route => {
    const path = new URL(route.request().url()).pathname
    if (path === '/api/auth/options') {
      requests++
      if (requests === 1) return route.fulfill({ contentType: 'text/html', body: '<html>Render starting up</html>' })
      if (requests === 2) return route.fulfill({ status: 503 })
      return route.fulfill({ json: { emailRegistrationEnabled: false, telegramOnly: true, managedFinancialKeys: true } })
    }
    if (path === '/api/auth/telegram/options') return route.fulfill({ json: { enabled: true, botUsername: 'PetProjectFinanceBot' } })
    authRequests++
    return route.fulfill({ status: 404 })
  })
  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'Finance Tracker', exact: true })).toBeVisible()
  expect(authRequests).toBe(0)
  await expect(page.getByRole('link', { name: 'Открыть в Telegram', exact: true })).toBeVisible()
  expect(requests).toBe(3)
  expect(externalFonts).toEqual([])
})

test('a permanent API configuration error offers an explicit retry instead of spinning forever', async ({ page }) => {
  let ready = false
  await page.route('**/api/**', route => {
    const path = new URL(route.request().url()).pathname
    if (path === '/api/auth/options') return ready
      ? route.fulfill({ json: { emailRegistrationEnabled: false, telegramOnly: true, managedFinancialKeys: true } })
      : route.fulfill({ status: 404 })
    if (path === '/api/auth/telegram/options') return route.fulfill({ json: { enabled: true, botUsername: 'PetProjectFinanceBot' } })
    return route.fulfill({ status: 404 })
  })
  await page.goto('/')
  await expect(page.getByRole('alert')).toHaveText('Не удалось подключиться')
  ready = true
  await page.getByRole('button', { name: 'Повторить', exact: true }).click()
  await expect(page.getByRole('link', { name: 'Открыть в Telegram', exact: true })).toBeVisible()
})
