import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { http } from '../../api/http'
import { getApiError } from '../../api/apiError'
import { useAuth } from '../auth/useAuth'
import type { FinanceTransaction } from '../transactions/transactionApi'
import { todayInMoscow } from '../transactions/validation'
import { formatDate } from '../../shared/formatting'
import { useCurrencyDisplay } from '../currencies/currencyDisplayContext'
import { BudgetsPanel } from '../budgets/BudgetsPanel'
import { ExpenseDonut } from './ExpenseDonut'
import { DataSkeleton } from '../../shared/DataSkeleton'
import { Mascot } from '../../shared/Mascot'
import { CalendarField } from '../../shared/CalendarField'

type Dashboard = {
  from: string; to: string; income: string; expense: string; result: string
  categories: { categoryId: string; categoryName: string; type: 'Income' | 'Expense'; amount: string }[]
  recent: FinanceTransaction[]
}

export function DashboardPage() {
  const { user } = useAuth()
  const display = useCurrencyDisplay()
  const today = todayInMoscow()
  const [period, setPeriod] = useState({ from: `${today.slice(0, 7)}-01`, to: today })
  const query = useQuery({
    queryKey: ['dashboard', period],
    queryFn: async ({ signal }) => (await http.get<Dashboard>('/finance/dashboard', { params: period, signal })).data,
  })
  const money = (amount: string) => display.money(amount, user?.baseCurrency ?? 'RUB')
  const data = query.data

  return <section className="dashboard-shell">
    <section className="overview-intro" aria-label="Быстрая запись">
      <div className="overview-intro-copy"><p className="eyebrow">Каждый день — яснее</p>
        <h2>Деньги,<br />по полочкам.</h2>
        <p>Ваши доходы, расходы и планы.</p>
        <Link className="primary-button" to="/categories"><span aria-hidden="true">＋</span> Записать расход</Link>
      </div>
      <div className="overview-art"><span className="mascot-orbit" aria-hidden="true" /><Mascot eager /></div>
    </section>
    <section className="summary-grid dashboard-metrics" aria-busy={query.isFetching}>
      <article className="transactions-list-card"><h2>Доходы</h2>{data ? <strong className="summary-value income data-reveal">{money(data.income)}</strong> : query.isPending ? <span className="skeleton-block skeleton-value" aria-label="Доходы загружаются" /> : <strong className="summary-value">—</strong>}</article>
      <article className="transactions-list-card"><h2>Расходы</h2>{data ? <strong className="summary-value expense data-reveal">{money(data.expense)}</strong> : query.isPending ? <span className="skeleton-block skeleton-value" aria-label="Расходы загружаются" /> : <strong className="summary-value">—</strong>}</article>
      <article className="transactions-list-card"><h2>Результат периода</h2>{data ? <strong className="summary-value data-reveal">{money(data.result)}</strong> : query.isPending ? <span className="skeleton-block skeleton-value" aria-label="Результат загружается" /> : <strong className="summary-value">—</strong>}
        <p>{!data ? 'Ожидаем данные' : data.result.startsWith('-') ? 'Расходы превысили доходы' : data.result === '0.00' ? 'Доходы равны расходам' : 'Доходы превысили расходы'}</p></article>
    </section>
    <details className="panel-disclosure dashboard-period"><summary>Период <span>{formatDate(period.from)} — {formatDate(period.to)}</span></summary>
    <section className="transactions-list-card">
      <div className="transaction-filters">
        <CalendarField label="С даты" value={period.from} onChange={from => setPeriod({ ...period, from })} />
        <CalendarField label="По дату" value={period.to} onChange={to => setPeriod({ ...period, to })} />
        <button className="ghost-button" onClick={() => setPeriod({ from: `${today.slice(0, 7)}-01`, to: today })}>Текущий месяц</button>
      </div>
    </section>
    </details>
    {query.isPending && <DataSkeleton label="Загружаем статистику…" rows={3} />}
    {query.isError && <div className="notice error">{getApiError(query.error, 'Не удалось загрузить статистику.')}</div>}
    <BudgetsPanel />
    {data && <>
      <section className="summary-grid category-summary data-reveal">
        <ExpenseDonut key={`${period.from}:${period.to}`} categories={data.categories} money={money} />
        <article className="transactions-list-card">
          <h2>Доходы по категориям</h2>
          {!data.categories.some((item) => item.type === 'Income') && <p>Операций за период нет.</p>}
          <ul className="summary-list">{data.categories.filter((item) => item.type === 'Income').map((item) =>
            <li key={item.categoryId}><span>{item.categoryName}</span><strong>{money(item.amount)}</strong></li>)}</ul>
        </article>
      </section>
      <section className="transactions-list-card data-reveal"><h2>Последние операции за период</h2>
        {!data.recent.length && <p>Добавьте первую операцию, чтобы увидеть статистику.</p>}
        <ul className="summary-list">{data.recent.map((item) => <li key={item.id}>
          <span>{item.categoryName} · {formatDate(item.occurredOn)}</span><strong>{item.type === 'Income' ? '+' : '−'}{money(item.amount)}</strong>
        </li>)}</ul><Link to="/transactions">Вся история</Link>
      </section>
    </>}
  </section>
}
