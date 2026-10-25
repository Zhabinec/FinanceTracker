import { test, expect } from '@playwright/test'

test.use({ timezoneId: 'America/Los_Angeles' })

test('recover password using a delivered one-time email link', async ({ page, request }) => {
  const email = `recovery-${Date.now()}@example.com`
  const original = 'Original-password-123!'
  await request.post('/api/auth/register', { data: { email, password: original, baseCurrency: 'RUB' } })
  await page.goto('/login')
  await page.getByRole('link', { name: 'Забыли пароль?' }).click()
  await page.getByLabel('Email', { exact: true }).fill(email)
  await page.getByRole('button', { name: 'Отправить ссылку' }).click()
  await expect(page.getByRole('status')).toContainText('Если аккаунт существует')
  const mailpit = process.env.E2E_MAILPIT_URL ?? 'http://localhost:8025'
  let messageId = ''
  await expect.poll(async () => {
    const response = await request.get(`${mailpit}/api/v1/search`, { params: { query: `to:${email}` } })
    const result = await response.json()
    messageId = result.messages?.[0]?.ID ?? ''
    return messageId
  }).not.toBe('')
  const message = await (await request.get(`${mailpit}/api/v1/message/${messageId}`)).json()
  const link = message.Text.match(/https?:\/\/[^\s]+\/reset-password#token=[^\s]+/)[0]
  await page.goto(link)
  await page.getByLabel('Новый пароль', { exact: true }).fill('Changed-password-123!')
  await page.getByLabel('Повторите пароль', { exact: true }).fill('Changed-password-123!')
  await page.getByRole('button', { name: 'Сменить пароль' }).click()
  await expect(page.getByRole('status')).toContainText('Пароль изменён')
  await page.getByRole('link', { name: 'Войти', exact: true }).click()
  await page.getByLabel('Email', { exact: true }).fill(email)
  await page.getByLabel('Пароль', { exact: true }).fill('Changed-password-123!')
  await page.getByRole('button', { name: 'Войти', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Обзор финансов' })).toBeVisible()
})

test('register, record expenses, filter history and review finances', async ({ page }) => {
  const email = `e2e-${Date.now()}@example.com`
  const category = `Продукты ${Date.now()}`
  await page.goto('/register')
  await page.getByLabel('Email', { exact: true }).fill(email)
  await page.getByLabel('Пароль', { exact: true }).fill('Browser-test-123!')
  await page.getByRole('button', { name: 'Зарегистрироваться' }).click()
  await expect(page).toHaveURL(/\/login$/)
  await page.getByLabel('Пароль', { exact: true }).fill('Browser-test-123!')
  await page.getByRole('button', { name: 'Войти', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Обзор финансов' })).toBeVisible()
  await page.getByRole('link', { name: 'Категории', exact: true }).click()
  await page.getByLabel('Название').fill(category)
  await page.getByRole('button', { name: 'Добавить', exact: true }).click()
  await expect(page.getByText(category, { exact: true })).toBeVisible()
  await page.getByRole('link', { name: 'Операции', exact: true }).click()
  const create = page.locator('.transaction-create-form')
  await create.getByLabel('Категория').selectOption({ label: category })
  await create.getByLabel('Сумма').fill('123,45')
  await create.getByLabel('Дата').fill('2026-10-25')
  await create.getByRole('button', { name: 'Добавить операцию' }).click()
  await expect(page.locator('.transaction-list')).toContainText('123,45')
  await expect(page.locator('.transaction-list')).toContainText('25.10.2026')
  await page.getByRole('button', { name: 'Изменить', exact: true }).click()
  await page.locator('.transaction-edit-form').getByLabel('Сумма').fill('124,50')
  await page.getByRole('button', { name: 'Сохранить', exact: true }).click()
  await expect(page.locator('.transaction-list')).toContainText('124,50')
  await page.reload()
  await expect(page.getByRole('heading', { name: 'Операции', exact: true })).toBeVisible()
  await page.locator('.transaction-filters').getByLabel('С даты').fill('2026-10-26')
  await expect(page.getByText('Операций по выбранным условиям нет.')).toBeVisible()
  await page.getByRole('button', { name: 'Сбросить' }).click()
  await expect(page.locator('.transaction-list')).toContainText(category)
  await page.goto('/')
  await page.getByLabel('С даты').fill('2026-10-25')
  await page.getByLabel('По дату').fill('2026-10-25')
  await expect(page.locator('.summary-grid').first()).toContainText('124,50')
  await expect(page.locator('.category-summary')).toContainText(category)
  await page.screenshot({ path: 'test-results/dashboard.png', fullPage: true })
  await page.getByRole('link', { name: 'Операции', exact: true }).click()
  page.once('dialog', (dialog) => dialog.accept())
  await page.getByRole('button', { name: 'Удалить', exact: true }).click()
  await expect(page.getByText('Операций по выбранным условиям нет.')).toBeVisible()
  await page.getByRole('button', { name: 'Выйти', exact: true }).click()
  await expect(page).toHaveURL(/\/login$/)
  await page.goto('/transactions')
  await expect(page).toHaveURL(/\/login$/)
})
