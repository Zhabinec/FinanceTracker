import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useSearchParams } from 'react-router-dom'
import { useAccounts } from '../accounts/accountApi'
import { getApiError } from '../../api/apiError'
import { getCategories } from '../categories/categoryApi'
import { useAuth } from '../auth/useAuth'
import { TransactionRow } from './TransactionRow'
import type { TransactionInput, TransactionQuery } from './transactionApi'
import { useTransactions } from './useTransactions'
import { ImportPanel } from '../imports/ImportPanel'
import { DataSkeleton } from '../../shared/DataSkeleton'

export function TransactionsPage() {
  const { user } = useAuth()
  const [searchParams] = useSearchParams()
  const accountsQuery = useAccounts()
  const accounts = accountsQuery.data ?? []
  const [query, setQuery] = useState<TransactionQuery>({ page: 1, pageSize: 20, sort: 'dateDesc', accountId: searchParams.get('accountId') || undefined })
  const changeFilter = (patch: Partial<TransactionQuery>) => setQuery((previous) => ({ ...previous, ...patch, page: 1 }))
  const categoriesQuery = useQuery({
    queryKey: ['categories'],
    queryFn: getCategories,
  })
  const { transactionsQuery, actionMutation, busyTransactionId } =
    useTransactions(query)
  const update = async (transactionId: string, input: TransactionInput) => {
    await actionMutation.mutateAsync({ kind: 'update', transactionId, input })
  }

  const remove = async (transactionId: string, version: string) => {
    await actionMutation.mutateAsync({ kind: 'delete', transactionId, version })
    if (transactionsQuery.data?.items.length === 1 && query.page > 1)
      setQuery((previous) => ({ ...previous, page: previous.page - 1 }))
  }

  const categories = categoriesQuery.data ?? []

  return (
    <section className="dashboard-shell transactions-page">
      <section className="transactions-list-card">
        <div className="transactions-heading"><h2>Доходы и расходы</h2>
      <span className="count-badge">{transactionsQuery.data?.totalCount ?? '—'}</span></div>
        <details className="filter-disclosure"><summary>Фильтры и сортировка</summary>
        <div className="transaction-filters">
          <label>Счёт<select value={query.accountId ?? ''} onChange={event => changeFilter({ accountId: event.target.value || undefined })}>
            <option value="">Все</option>{accounts.map(a => <option key={a.id} value={a.id}>{a.name}{a.isArchived ? ' (архив)' : ''}</option>)}
          </select></label>
          <label>С даты<input type="date" value={query.from ?? ''} onChange={(event) => changeFilter({ from: event.target.value || undefined })} /></label>
          <label>По дату<input type="date" value={query.to ?? ''} onChange={(event) => changeFilter({ to: event.target.value || undefined })} /></label>
          <label>Тип<select value={query.type ?? ''} onChange={(event) => changeFilter({ type: event.target.value as TransactionQuery['type'] || undefined, categoryId: undefined })}>
            <option value="">Все</option><option value="Income">Доходы</option><option value="Expense">Расходы</option>
          </select></label>
          <label>Категория<select value={query.categoryId ?? ''} onChange={(event) => changeFilter({ categoryId: event.target.value || undefined })}>
            <option value="">Все</option>{categories.filter((category) => !query.type || category.type === query.type).map((category) =>
              <option key={category.id} value={category.id}>{category.name}{category.isActive ? '' : ' (неактивна)'}</option>)}
          </select></label>
          <label>Сортировка<select value={query.sort} onChange={(event) => changeFilter({ sort: event.target.value })}>
            <option value="dateDesc">Сначала новые</option><option value="dateAsc">Сначала старые</option>
            <option value="amountDesc">Сумма по убыванию</option><option value="amountAsc">Сумма по возрастанию</option>
          </select></label>
          <label>На странице<select value={query.pageSize} onChange={(event) => changeFilter({ pageSize: Number(event.target.value) })}>
            <option value={10}>10</option><option value={20}>20</option><option value={50}>50</option><option value={100}>100</option>
          </select></label>
          <button className="ghost-button" onClick={() => setQuery({ page: 1, pageSize: 20, sort: 'dateDesc' })}>Сбросить</button>
        </div>
        </details>
        {transactionsQuery.isPending && <DataSkeleton label="Загружаем операции…" />}
        {transactionsQuery.isError && <div className="notice error">
          {getApiError(transactionsQuery.error, 'Не удалось загрузить операции.')}
        </div>}
        {transactionsQuery.isSuccess && transactionsQuery.data.items.length === 0 &&
          <p className="category-empty">Операций по выбранным условиям нет.</p>}
        {transactionsQuery.isSuccess && transactionsQuery.data.items.length > 0 && (
          <ul className="transaction-list data-reveal">
            {transactionsQuery.data.items.map((transaction) => (
              <TransactionRow
                key={transaction.id}
                transaction={transaction}
                categories={categories}
                accounts={accounts}
                currency={user?.baseCurrency ?? 'RUB'}
                busy={busyTransactionId === transaction.id}
                onUpdate={update}
                onDelete={remove}
              />
            ))}
          </ul>
        )}
        {transactionsQuery.data && transactionsQuery.data.totalCount > query.pageSize && <div className="pagination">
          <button className="ghost-button" disabled={query.page <= 1 || transactionsQuery.isFetching}
            onClick={() => setQuery({ ...query, page: query.page - 1 })}>Назад</button>
          <span>Страница {query.page} из {Math.max(1, Math.ceil((transactionsQuery.data?.totalCount ?? 0) / query.pageSize))}</span>
          <button className="ghost-button" disabled={transactionsQuery.isFetching || query.page * query.pageSize >= (transactionsQuery.data?.totalCount ?? 0)}
            onClick={() => setQuery({ ...query, page: query.page + 1 })}>Далее</button>
        </div>}
      </section>
      <ImportPanel />
    </section>
  )
}
