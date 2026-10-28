import type { Page } from '@playwright/test'

export async function financialApp(page: Page, options: { telegram?: boolean; delayOpen?: boolean; failFirstOpen?: boolean } = {}) {
  const owner = crypto.randomUUID(), category = crypto.randomUUID(), transport = crypto.randomUUID(), salary = crypto.randomUUID()
  const account = owner, paths: string[] = []
  const categories = [{ id: category, name: 'Продукты', type: 'Expense', isActive: true, version: crypto.randomUUID() },
    { id: transport, name: 'Транспорт', type: 'Expense', isActive: true, version: crypto.randomUUID() },
    { id: salary, name: 'Зарплата', type: 'Income', isActive: true, version: crypto.randomUUID() }]
  const transactions = [[category, 'Expense', '75.00', '2026-10-15'], [transport, 'Expense', '25.00', '2026-10-15'],
    [salary, 'Income', '500.00', '2026-10-15'], [category, 'Expense', '900.00', '2026-09-01']].map(([categoryId, type, amount, occurredOn]) => ({
      id: crypto.randomUUID(), accountId: account, categoryId, categoryName: categories.find(x => x.id === categoryId)!.name,
      type, amount, description: 'Пример', occurredOn, createdAtUtc: '2026-10-15T12:00:00Z', version: crypto.randomUUID()
    }))
  const user = { id: owner, email: null, baseCurrency: 'RUB', registeredAtUtc: '2026-10-01T00:00:00Z' }
  const session = { accessToken: 'synthetic-financial-session', refreshToken: 'synthetic-financial-refresh', expiresAtUtc: '2099-01-01T00:00:00Z', user }
  let release!: () => void, opens = 0
  const wait = new Promise<void>(resolve => { release = resolve })
  if (options.telegram !== false) await page.addInitScript(() => { window.Telegram = { WebApp: { initData: 'synthetic-financial-launch', ready() {}, expand() {} } } })
  await page.route('**/api/**', async route => {
    const url = new URL(route.request().url()), path = url.pathname; paths.push(path)
    if (path === '/api/auth/options') return route.fulfill({ json: { emailRegistrationEnabled: false, telegramOnly: true, managedFinancialKeys: true, serverFinancialRecords: true } })
    if (path === '/api/auth/telegram/options') return route.fulfill({ json: { enabled: true, botUsername: 'PetProjectFinanceBot' } })
    if (path === '/api/auth/telegram/login') return route.fulfill({ json: { linkRequired: false, session } })
    if (path === '/api/auth/telegram/register') return route.fulfill({ json: session })
    if (path === '/api/auth/telegram/logout') return route.fulfill({ status: 204 })
    if (path === '/api/auth/telegram/link') return route.fulfill({ json: { linked: true, username: 'synthetic' } })
    if (path === '/api/finance/open') {
      opens++
      if (options.failFirstOpen && opens === 1) return route.fulfill({ status: 503, json: { title: 'Временная ошибка' } })
      if (options.delayOpen) await wait
      return route.fulfill({ json: { ready: true, requiresKey: false, baseCurrency: 'RUB', revision: 0 } }).catch(() => undefined)
    }
    if (path === '/api/finance/categories') return route.fulfill({ json: categories })
    if (path === '/api/finance/accounts') return route.fulfill({ json: [{ id: account, name: 'Основной счёт', currency: 'RUB', openingBalance: '0.00', balance: '-500.00', isArchived: false, version: crypto.randomUUID() }] })
    if (path === '/api/finance/budgets') return route.fulfill({ json: [] })
    if (path === '/api/finance/transfers') return route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 10 } })
    if (path === '/api/finance/transactions') return route.fulfill({ json: { items: transactions, totalCount: 4, page: 1, pageSize: 20 } })
    if (path === '/api/finance/dashboard') {
      const empty = url.searchParams.get('from')?.startsWith('2026-11')
      return route.fulfill({ json: { from: '2026-10-01', to: '2026-10-31', income: empty ? '0.00' : '500.00', expense: empty ? '0.00' : '100.00', result: empty ? '0.00' : '400.00',
        categories: empty ? [] : [{ categoryId: category, categoryName: 'Продукты', type: 'Expense', amount: '75.00' }, { categoryId: transport, categoryName: 'Транспорт', type: 'Expense', amount: '25.00' }, { categoryId: salary, categoryName: 'Зарплата', type: 'Income', amount: '500.00' }], recent: empty ? [] : transactions.slice(0, 3) } })
    }
    if (path === '/api/currencies/rates') return route.fulfill({ json: { date: '2026-10-29', source: 'test', rublesPerUnit: { RUB: '1', USD: '100', EUR: '110' } } })
    return route.fulfill({ status: 404, json: {} })
  })
  return { owner, paths, categories, transactions, release, session }
}
