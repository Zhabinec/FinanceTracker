import { test, expect, type APIRequestContext } from '@playwright/test'

test.use({ timezoneId: 'America/Los_Angeles' })
const generatedMailIds = new Set<string>()

test.afterEach(async ({ request }) => {
  // Never issue an empty-ID delete: Mailpit interprets that as deleting the entire inbox.
  const ids = [...generatedMailIds]
  generatedMailIds.clear()
  if (!ids.length) return
  const mailpit = process.env.E2E_MAILPIT_URL ?? 'http://localhost:8025'
  expect((await request.delete(`${mailpit}/api/v1/messages`, { data: { IDs: ids } })).ok()).toBe(true)
})

async function accountMail(request: APIRequestContext, email: string, subject: string) {
  const mailpit = process.env.E2E_MAILPIT_URL ?? 'http://localhost:8025'
  let messageId = ''
  await expect.poll(async () => {
    const response = await request.get(`${mailpit}/api/v1/search`, { params: { query: `to:${email}` } })
    const result = await response.json()
    messageId = result.messages?.find((message: { Subject: string }) => message.Subject === subject)?.ID ?? ''
    return messageId
  }).not.toBe('')
  const message = await (await request.get(`${mailpit}/api/v1/message/${messageId}`)).json()
  expect(message.To.some((recipient: { Address: string }) => recipient.Address === email)).toBe(true)
  generatedMailIds.add(messageId)
  return message
}

async function confirmTestAccount(request: APIRequestContext, email: string) {
  const message = await accountMail(request, email, 'Подтверждение email Finance Tracker')
  const token = new URL(message.Text.match(/https?:\/\/[^\s]+\/confirm-email#token=[^\s]+/)[0]).hash.slice('#token='.length)
  expect((await request.post('/api/auth/confirm-email', { data: { token: decodeURIComponent(token) } })).status()).toBe(204)
}

test('recover password using a delivered one-time email link', async ({ page, request }) => {
  const email = `recovery-${Date.now()}@example.com`
  const original = 'Original-password-123!'
  await request.post('/api/auth/register', { data: { email, password: original, baseCurrency: 'RUB' } })
  await confirmTestAccount(request, email)
  await page.goto('/login')
  await page.getByRole('link', { name: 'Забыли пароль?' }).click()
  await page.getByLabel('Email', { exact: true }).fill(email)
  await page.getByRole('button', { name: 'Отправить ссылку' }).click()
  await expect(page.getByRole('status')).toContainText('Если аккаунт существует')
  const message = await accountMail(request, email, 'Восстановление пароля Finance Tracker')
  expect(message.Text).toContain(email)
  const link = message.Text.match(/https?:\/\/[^\s]+\/reset-password#token=[^\s]+/)[0]
  await page.goto('/reset-password#token=old-invalid-token')
  // Same-document fragment navigation must replace the token used by the form.
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

test('native clipboard preserves exact text and replaces selections across forms', async ({ page, context }) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write'])
  for (const [path, label] of [
    ['/login', 'Пароль'], ['/register', 'Пароль'], ['/reset-password#token=clipboard-test', 'Новый пароль'],
  ]) {
    await page.goto(path)
    const input = page.getByLabel(label, { exact: true })
    await page.evaluate(() => navigator.clipboard.writeText('12345678'))
    await input.click()
    await input.press('Control+V')
    await expect(input).toHaveValue('12345678')
    await input.press('Control+A')
    await input.press('Control+V')
    await expect(input).toHaveValue('12345678')
    await input.fill('')
    await page.keyboard.down('Control')
    await page.keyboard.down('v')
    await page.keyboard.down('v')
    await page.keyboard.up('v')
    await page.keyboard.up('Control')
    await expect(input).toHaveValue('12345678')
    await page.getByRole('button', { name: 'Показать пароль', exact: true }).first().click()
    await expect(input).toHaveAttribute('type', 'text')
    await input.press('Control+A')
    await input.press('Control+C')
    await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toBe('12345678')
    await page.evaluate(() => navigator.clipboard.writeText('пароль + <>& 12345678'))
    await input.press('Control+V')
    await expect(input).toHaveValue('пароль + <>& 12345678')
    const longText = 'a'.repeat(140)
    await page.evaluate((text) => navigator.clipboard.writeText(text), longText)
    await input.press('Control+A')
    await input.press('Control+V')
    await expect(input).toHaveValue(longText)
  }
})

test('register, confirm email, record expenses, filter history and review finances', async ({ page, request }) => {
  const email = `e2e-${Date.now()}@example.com`
  const category = `Продукты ${Date.now()}`
  await page.goto('/register')
  await expect(page.getByRole('combobox', { name: 'Базовая валюта' })).toHaveCount(0)
  await page.getByLabel('Email', { exact: true }).fill(email)
  await page.getByLabel('Пароль', { exact: true }).fill('Browser-test-123!')
  await page.getByRole('button', { name: 'Зарегистрироваться' }).click()
  await expect(page).toHaveURL(/\/verify-email$/)
  await expect(page.getByText(`Аккаунт создан для ${email}. Откройте письмо и подтвердите email.`)).toBeVisible()
  const verificationMail = await accountMail(request, email, 'Подтверждение email Finance Tracker')
  expect(verificationMail.Text).toContain(email)
  // Follow the actual HTML email link, preserving the fragment secret.
  await page.setContent(verificationMail.HTML)
  await page.getByRole('link', { name: 'Подтвердить email', exact: true }).click()
  await page.getByRole('button', { name: 'Подтвердить email', exact: true }).click()
  await expect(page.getByRole('status')).toContainText('Email подтверждён')
  await page.getByRole('link', { name: 'Войти', exact: true }).click()
  await page.getByLabel('Email', { exact: true }).fill(email)
  await page.getByLabel('Пароль', { exact: true }).fill('Browser-test-123!')
  await page.getByRole('button', { name: 'Войти', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Обзор финансов' })).toBeVisible()
  const accountName = `Карта ${Date.now()}`
  await page.getByRole('link', { name: 'Счета', exact: true }).click()
  await page.getByLabel('Название', { exact: true }).fill(accountName)
  await page.getByLabel('Начальный остаток', { exact: true }).fill('1000,00')
  await page.getByRole('button', { name: 'Создать счёт', exact: true }).click()
  const accountCard = page.locator('.account-card').filter({ has: page.getByRole('heading', { name: accountName, exact: true }) })
  await expect(accountCard.locator('.summary-value')).toContainText('1 000,00')
  await page.route('**/api/currencies/rates', route => route.fulfill({ json: {
    date: '2026-10-25', source: 'ЦБ РФ', rublesPerUnit: { RUB: '1', USD: '100', EUR: '150' },
  } }))
  await page.getByRole('combobox', { name: 'Валюта отображения', exact: true }).selectOption('USD')
  await expect(accountCard.locator('.summary-value')).toContainText('10,00')
  await expect(accountCard.locator('.summary-value')).toContainText('$')
  await page.reload()
  await expect(page.getByRole('combobox', { name: 'Валюта отображения', exact: true })).toHaveValue('USD')
  await expect(accountCard.locator('.summary-value')).toContainText('10,00')
  await expect(page.locator('.currency-display')).toContainText('25.10.2026')
  await page.screenshot({ path: 'test-results/currency-display.png', fullPage: true })
  await page.getByRole('combobox', { name: 'Валюта отображения', exact: true }).selectOption('RUB')
  await page.getByRole('link', { name: 'Категории', exact: true }).click()
  await page.getByLabel('Название').fill(category)
  await page.getByRole('button', { name: 'Добавить', exact: true }).click()
  await expect(page.getByText(category, { exact: true })).toBeVisible()
  await page.getByRole('link', { name: 'Операции', exact: true }).click()
  const create = page.locator('.transaction-create-form')
  await create.getByRole('combobox', { name: 'Счёт', exact: true }).selectOption({ label: `${accountName} (RUB)` })
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
  await page.getByRole('combobox', { name: 'Валюта отображения', exact: true }).selectOption('USD')
  await expect(page.locator('.transaction-list')).toContainText('1,25')
  await page.getByRole('button', { name: 'Изменить', exact: true }).click()
  await expect(page.locator('.transaction-edit-form').getByLabel('Сумма')).toHaveValue('124.50')
  await page.getByRole('button', { name: 'Отмена', exact: true }).click()
  await page.getByRole('combobox', { name: 'Валюта отображения', exact: true }).selectOption('RUB')
  await page.reload()
  await expect(page.getByRole('heading', { name: 'Операции', exact: true })).toBeVisible()
  await page.locator('.transaction-filters').getByLabel('С даты').fill('2026-10-26')
  await expect(page.getByText('Операций по выбранным условиям нет.')).toBeVisible()
  await page.getByRole('button', { name: 'Сбросить' }).click()
  await expect(page.locator('.transaction-list')).toContainText(category)
  await page.getByRole('link', { name: 'Счета', exact: true }).click()
  await expect(accountCard.locator('.summary-value')).toContainText('875,50')
  await accountCard.getByRole('button', { name: 'В архив', exact: true }).click()
  await expect(accountCard).toHaveCount(0)
  await page.getByLabel('Показать архивные счета').check()
  await expect(accountCard).toContainText('В архиве')
  await accountCard.getByRole('button', { name: 'Восстановить', exact: true }).click()
  await expect(accountCard).toContainText('Активен')
  await page.screenshot({ path: 'test-results/accounts.png', fullPage: true })
  await page.unroute('**/api/currencies/rates')
  await page.route('**/api/currencies/rates', route => route.fulfill({ status: 503, json: { title: 'Курс недоступен' } }))
  await page.getByRole('combobox', { name: 'Валюта отображения', exact: true }).selectOption('USD')
  await page.reload()
  await expect(page.getByRole('alert')).toContainText('Суммы показаны в исходной валюте')
  await expect(accountCard.locator('.summary-value')).toContainText('875,50')
  await expect(accountCard.locator('.summary-value')).not.toContainText('$')
  await page.getByRole('combobox', { name: 'Валюта отображения', exact: true }).selectOption('RUB')
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
  await page.getByRole('link', { name: 'Счета', exact: true }).click()
  await expect(accountCard.locator('.summary-value')).toContainText('1 000,00')
  await page.getByRole('link', { name: 'Операции', exact: true }).click()
  await page.getByRole('button', { name: 'Выйти', exact: true }).click()
  await expect(page).toHaveURL(/\/login$/)
  await page.goto('/transactions')
  await expect(page).toHaveURL(/\/login$/)
})
