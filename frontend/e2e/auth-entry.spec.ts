import { test, expect, type Page } from '@playwright/test'

async function publicEntry(page: Page, telegram = false) {
  const requests: string[] = []
  await page.route('**/api/**', route => {
    const path = new URL(route.request().url()).pathname
    requests.push(path)
    if (path === '/api/auth/options') return route.fulfill({ json: { emailRegistrationEnabled: false } })
    if (path === '/api/auth/telegram/options') return route.fulfill({ json: { enabled: true, botUsername: 'PetProjectFinanceBot' } })
    if (path === '/api/auth/google/options') return route.fulfill({ json: { enabled: true } })
    if (path === '/api/auth/telegram/login') return route.fulfill({ json: { linkRequired: true, session: null } })
    return route.fulfill({ status: 401, json: {} })
  })
  if (telegram) await page.addInitScript(() => {
    window.Telegram = { WebApp: { initData: 'synthetic-test-launch', ready() {}, expand() {} } }
  })
  return requests
}

test('public browser entry offers Telegram and Google without password registration', async ({ page }) => {
  const requests = await publicEntry(page)
  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto('/login')
  await expect(page.getByRole('link', { name: 'Открыть в Telegram' })).toHaveAttribute('href', 'https://t.me/PetProjectFinanceBot')
  const contrast = await page.getByRole('link', { name: 'Открыть в Telegram' }).evaluate(element => {
    const style = getComputedStyle(element)
    const luminance = (color: string) => {
      const channels = color.match(/[\d.]+/g)!.slice(0, 3).map(value => {
        const channel = Number(value) / 255
        return channel <= .04045 ? channel / 12.92 : ((channel + .055) / 1.055) ** 2.4
      })
      return channels[0] * .2126 + channels[1] * .7152 + channels[2] * .0722
    }
    const foreground = luminance(style.color), background = luminance(style.backgroundColor)
    return (Math.max(foreground, background) + .05) / (Math.min(foreground, background) + .05)
  })
  expect(contrast).toBeGreaterThanOrEqual(4.5)
  await expect(page.getByRole('button', { name: 'Продолжить с Google' })).toBeVisible()
  await expect(page.getByLabel('Email', { exact: true })).toHaveCount(0)
  await expect(page.getByRole('link', { name: /пароль|аккаунт|письм/i })).toHaveCount(0)
  await expect(page.locator('.auth-mascot')).toHaveCSS('opacity', '1')
  await page.screenshot({ path: 'test-results/auth-entry-mobile.png' })
  await page.goto('/register')
  await expect(page).toHaveURL(/\/login$/)
  expect(requests).not.toContain('/api/auth/register')
})

test('Telegram entry only offers explicit Telegram registration', async ({ page }) => {
  const requests = await publicEntry(page, true)
  await page.goto('/login')
  await expect(page.getByRole('button', { name: 'Начать в Telegram' })).toBeVisible()
  await expect(page.getByLabel('Email', { exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Продолжить с Google' })).toHaveCount(0)
  expect(requests).not.toContain('/api/auth/telegram/register')
})

test('existing password entry remains available by its separate address', async ({ page }) => {
  await publicEntry(page)
  await page.goto('/login/password')
  await expect(page.getByLabel('Email', { exact: true })).toBeVisible()
  await expect(page.getByLabel('Пароль', { exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Забыли пароль?' })).toBeVisible()
})
