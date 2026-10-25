import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { http } from '../../api/http'
import { getApiError } from '../../api/apiError'
import { useAuth } from '../auth/useAuth'
import type { FinanceTransaction } from '../transactions/transactionApi'
import { todayInMoscow } from '../transactions/validation'
import { formatMoney, formatDate } from '../../shared/formatting'

type Dashboard = {
  from: string; to: string; income: string; expense: string; result: string
  categories: { categoryId: string; categoryName: string; type: 'Income' | 'Expense'; amount: string }[]
  recent: FinanceTransaction[]
}

export function DashboardPage() {
  const { user, signOut } = useAuth()
  const today = todayInMoscow()
  const [period, setPeriod] = useState({ from: `${today.slice(0, 7)}-01`, to: today })
  const [leaving, setLeaving] = useState(false)
  const query = useQuery({
    queryKey: ['dashboard', period],
    queryFn: async ({ signal }) => (await http.get<Dashboard>('/dashboard', { params: period, signal })).data,
  })
  const money = (amount: string) => formatMoney(amount, user?.baseCurrency ?? 'RUB')
  const data = query.data

  return <main className="dashboard-shell">
    <header className="app-header"><div><p className="eyebrow">Finance Tracker</p><h1>Обзор финансов</h1><p>{user?.email}</p></div>
      <nav className="header-actions">
        <Link className="ghost-button link-button" to="/transactions">Операции</Link>
        <Link className="ghost-button link-button" to="/categories">Категории</Link>
        <Link className="ghost-button link-button" to="/account">Аккаунт</Link>
        <button className="ghost-button" disabled={leaving} onClick={async () => { setLeaving(true); await signOut() }}>Выйти</button>
      </nav>
    </header>
    <section className="transactions-list-card">
      <h2>Выбранный период</h2>
      <div className="transaction-filters">
        <label>С даты<input type="date" value={period.from} onChange={(event) => setPeriod({ ...period, from: event.target.value })} /></label>
        <label>По дату<input type="date" value={period.to} onChange={(event) => setPeriod({ ...period, to: event.target.value })} /></label>
        <button className="ghost-button" onClick={() => setPeriod({ from: `${today.slice(0, 7)}-01`, to: today })}>Текущий месяц</button>
      </div>
      <p className="muted">Границы периода — по Москве. Результат показывает разницу доходов и расходов за период.</p>
    </section>
    {query.isPending && <p role="status">Загружаем статистику…</p>}
    {query.isError && <div className="notice error">{getApiError(query.error, 'Не удалось загрузить статистику.')}</div>}
    {data && <>
      <section className="summary-grid">
        <article className="transactions-list-card"><h2>Доходы</h2><strong className="summary-value income">{money(data.income)}</strong></article>
        <article className="transactions-list-card"><h2>Расходы</h2><strong className="summary-value expense">{money(data.expense)}</strong></article>
        <article className="transactions-list-card"><h2>Результат периода</h2><strong className="summary-value">{money(data.result)}</strong><p>{data.result.startsWith('-') ? 'Расходы превысили доходы' : data.result === '0.00' ? 'Доходы равны расходам' : 'Доходы превысили расходы'}</p></article>
      </section>
      <section className="summary-grid category-summary">
        {(['Income', 'Expense'] as const).map((type) => <article className="transactions-list-card" key={type}>
          <h2>{type === 'Income' ? 'Доходы по категориям' : 'Расходы по категориям'}</h2>
          {!data.categories.some((item) => item.type === type) && <p>Операций за период нет.</p>}
          <ul className="summary-list">{data.categories.filter((item) => item.type === type).map((item) =>
            <li key={item.categoryId}><span>{item.categoryName}</span><strong>{money(item.amount)}</strong></li>)}</ul>
        </article>)}
      </section>
      <section className="transactions-list-card"><h2>Последние операции за период</h2>
        {!data.recent.length && <p>Добавьте первую операцию, чтобы увидеть статистику.</p>}
        <ul className="summary-list">{data.recent.map((item) => <li key={item.id}>
          <span>{item.categoryName} · {formatDate(item.occurredOn)}</span><strong>{item.type === 'Income' ? '+' : '−'}{money(item.amount)}</strong>
        </li>)}</ul><Link to="/transactions">Открыть журнал операций</Link>
      </section>
    </>}
  </main>
}
