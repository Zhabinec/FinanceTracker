import { test, expect } from '@playwright/test'

test('a slow startup script does not block downloading the React entry', async ({ page }) => {
  let releaseStartup!: () => void
  const startupGate = new Promise<void>(resolve => { releaseStartup = resolve })
  let entryRequested = false
  page.on('request', request => { if (/\/assets\/index-.*\.js/.test(request.url())) entryRequested = true })
  await page.route('**/startup.js', async route => { await startupGate; await route.continue() })
  try {
    await page.goto('/', { waitUntil: 'commit' })
    await expect.poll(() => entryRequested).toBe(true)
    await expect(page.getByRole('heading', { name: 'Finance Tracker', exact: true })).toBeVisible()
  } finally { releaseStartup() }
})

test('a stalled wake-up response is replaced within six seconds without duplicating login', async ({ page }) => {
  let releaseStalled!: () => void
  const stalledGate = new Promise<void>(resolve => { releaseStalled = resolve })
  let policyRequests = 0, logins = 0
  await page.route('https://telegram.org/js/telegram-web-app.js', route => route.fulfill({ contentType: 'application/javascript',
    body: 'window.Telegram={WebApp:{initData:"synthetic-probe",ready(){},expand(){}}}' }))
  await page.route('**/api/**', async route => {
    const path = new URL(route.request().url()).pathname
    if (path === '/api/auth/options') {
      if (++policyRequests === 1) { await stalledGate; await route.abort(); return }
      return route.fulfill({ json: { emailRegistrationEnabled: false, telegramOnly: true, managedFinancialKeys: true } })
    }
    if (path === '/api/auth/telegram/login') { logins++; return route.fulfill({ json: { linkRequired: true, session: null } }) }
    return route.fulfill({ status: 404 })
  })
  try {
    await page.goto('/#tgWebAppData=synthetic-probe', { waitUntil: 'domcontentloaded' })
    await expect(page.getByRole('button', { name: 'Начать в Telegram', exact: true })).toBeVisible({ timeout: 7000 })
    expect(policyRequests).toBe(2)
    expect(logins).toBe(1)
  } finally { releaseStalled() }
})

test('Telegram reveals the HTML shell and wakes the API before the React bundle arrives', async ({ page }) => {
  let releaseBundle!: () => void
  const bundleGate = new Promise<void>(resolve => { releaseBundle = resolve })
  let policyRequests = 0
  await page.route('**/assets/index-*.js', async route => { await bundleGate; await route.continue() })
  await page.route('https://telegram.org/js/telegram-web-app.js', route => route.fulfill({ contentType: 'application/javascript',
    body: 'window.Telegram={WebApp:{initData:"synthetic-early-launch",ready(){document.documentElement.dataset.telegramReady="true"},expand(){}}}' }))
  await page.route('**/api/**', route => {
    const path = new URL(route.request().url()).pathname
    if (path === '/api/auth/options') { policyRequests++; return route.fulfill({ json: { emailRegistrationEnabled: false, telegramOnly: true, managedFinancialKeys: true } }) }
    if (path === '/api/auth/telegram/login') return route.fulfill({ json: { linkRequired: true, session: null } })
    return route.fulfill({ status: 404 })
  })
  try {
    await page.goto('/#tgWebAppData=synthetic-early-launch', { waitUntil: 'commit' })
    await expect(page.locator('html')).toHaveAttribute('data-telegram-ready', 'true')
    await expect(page.getByRole('heading', { name: 'Finance Tracker', exact: true })).toBeVisible()
    await expect.poll(() => policyRequests).toBe(1)
    releaseBundle()
    await expect(page.getByRole('button', { name: 'Начать в Telegram', exact: true })).toBeVisible()
    expect(policyRequests).toBe(1)
  } finally { releaseBundle() }
})

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
