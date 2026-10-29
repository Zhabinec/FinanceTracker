import { test, expect } from '@playwright/test'
import { financialApp } from './fixtures/financialApp'

test('mobile quick entry works when decorative artwork fails and navigation retains touch targets', async ({ page }) => {
  await financialApp(page)
  await page.route('**/images/wallet-buddy.webp', route => route.abort())
  await page.goto('/')
  const navigation = page.getByRole('navigation', { name: 'Основная навигация' })
  for (const width of [320, 390, 768, 1280]) {
    await page.setViewportSize({ width, height: 844 })
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    for (const link of await navigation.getByRole('link').all()) {
      const bounds = await link.boundingBox()
      expect(bounds!.width).toBeGreaterThanOrEqual(44)
      expect(bounds!.height).toBeGreaterThanOrEqual(44)
    }
  }
  await page.setViewportSize({ width: 390, height: 844 })
  await page.getByRole('link', { name: 'Записать расход', exact: true }).click()
  await page.getByRole('button', { name: 'Добавить расход: Продукты', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: 'Продукты', exact: true })
  await expect(dialog.getByLabel('Название', { exact: true })).toBeFocused()
  await page.keyboard.press('Escape')
  await expect(page.getByRole('button', { name: 'Добавить расход: Продукты', exact: true })).toBeFocused()
})
