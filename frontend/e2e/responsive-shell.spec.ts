import { test, expect } from '@playwright/test'
import { financialApp } from './fixtures/financialApp'

// Telegram Desktop can use classic scrollbars. Headless Chromium normally hides
// them, which otherwise masks the width change between short and long sections.
test.use({ launchOptions: { ignoreDefaultArgs: ['--hide-scrollbars'] } })

test('shell and currency stay aligned between short and long sections at phone and desktop widths', async ({ page }) => {
  const app = await financialApp(page)
  app.categories[0].name = 'ОченьДлинноеНазваниеКатегории'.repeat(6)
  const navigation = page.getByRole('navigation', { name: 'Основная навигация' })
  for (const width of [320, 390, 550, 768, 1280]) {
    await page.setViewportSize({ width, height: 900 })
    await page.goto('/transactions')
    let baseline: { logout: number; currency: number; left: number; right: number } | undefined
    for (const section of ['История', 'Счета', 'Категории', 'Обзор', 'Аккаунт', 'История']) {
      await navigation.getByRole('link', { name: section, exact: true }).click()
      await expect(page.locator('.data-skeleton')).toHaveCount(0)
      const frame = await page.evaluate(() => {
        const logout = document.querySelector('.app-logout')!.getBoundingClientRect()
        const currency = document.querySelector('.currency-trigger')!.getBoundingClientRect()
        const content = document.querySelector('.app-content')!.getBoundingClientRect()
        return { logout: logout.right, currency: currency.right, left: content.left, right: content.right }
      })
      baseline ??= frame
      for (const key of ['logout', 'currency', 'left', 'right'] as const)
        expect(Math.abs(frame[key] - baseline[key]), `${section}, ${width}px: ${key}`).toBeLessThan(1)
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth), `${section}, ${width}px overflow`).toBe(true)
      if (width === 550 && (section === 'История' || section === 'Категории'))
        await page.screenshot({ path: `test-results/stable-${section === 'История' ? 'history' : 'categories'}-header.png`,
          clip: { x: 0, y: 0, width: 550, height: 260 }, animations: 'disabled' })
    }
  }
})
