import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { useQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router-dom'
import { useAccounts } from '../accounts/accountApi'
import { CurrencySelector } from '../currencies/CurrencySelector'
import { getApiError } from '../../api/apiError'
import { getCategories } from '../categories/categoryApi'
import { useAuth } from '../auth/useAuth'
import { TransactionRow } from './TransactionRow'
import type { TransactionInput, TransactionQuery } from './transactionApi'
import { useTransactions } from './useTransactions'
import { toTransactionInput, todayInMoscow, transactionSchema, type TransactionForm } from './validation'

export function TransactionsPage() {
  const { user } = useAuth()
  const [searchParams] = useSearchParams()
  const accountsQuery = useAccounts()
  const accounts = accountsQuery.data ?? []
  const [serverError, setServerError] = useState('')
  const [query, setQuery] = useState<TransactionQuery>({ page: 1, pageSize: 20, sort: 'dateDesc', accountId: searchParams.get('accountId') || undefined })
  const changeFilter = (patch: Partial<TransactionQuery>) => setQuery((previous) => ({ ...previous, ...patch, page: 1 }))
  const { register, handleSubmit, control, setValue, setError, reset, formState: { errors } } =
    useForm<TransactionForm>({
      defaultValues: {
        accountId: '',
        categoryId: '',
        type: 'Expense',
        amount: '',
        description: '',
        occurredOn: todayInMoscow(),
      },
    })
  const selectedType = useWatch({ control, name: 'type' })
  const categoriesQuery = useQuery({
    queryKey: ['categories'],
    queryFn: getCategories,
  })
  const { transactionsQuery, createMutation, actionMutation, busyTransactionId } =
    useTransactions(query)
  const activeCategories = (categoriesQuery.data ?? []).filter((category) =>
    category.isActive && category.type === selectedType)

  const submit = handleSubmit(async (values) => {
    setServerError('')
    const parsed = transactionSchema.safeParse(values)
    if (!parsed.success) {
      for (const issue of parsed.error.issues)
        setError(issue.path[0] as keyof TransactionForm, { message: issue.message })
      return
    }

    try {
      await createMutation.mutateAsync(toTransactionInput(parsed.data))
      reset({
        accountId: parsed.data.accountId,
        categoryId: '',
        type: parsed.data.type,
        amount: '',
        description: '',
        occurredOn: todayInMoscow(),
      })
    } catch (error) {
      setServerError(getApiError(error, 'Не удалось создать операцию.'))
    }
  })

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
      <CurrencySelector />
      <div className="journal-actions"><Link className="primary-button link-button" to="/categories">Добавить по категории</Link></div>
      <details className="panel-disclosure transaction-entry-disclosure"><summary>Запись с датой</summary>
      <section className="transaction-create-card">
        <div><p className="eyebrow">Новая операция</p><h2>Запишите движение денег</h2>
          <p>Дата считается по Москве, а категория определяет, доход это или расход.</p></div>
        <form className="transaction-create-form" onSubmit={submit} noValidate>
          {serverError && <div className="notice error form-wide">{serverError}</div>}
          <label>Счёт<select {...register('accountId')} disabled={accountsQuery.isPending}>
            <option value="">Выберите счёт</option>
            {accounts.filter(a => !a.isArchived).map(a => <option key={a.id} value={a.id}>{a.name} ({a.currency})</option>)}
          </select>{errors.accountId && <span className="field-error">{errors.accountId.message}</span>}</label>
          <label>Тип<select {...register('type')} onChange={(event) => {
            const type = event.target.value as TransactionForm['type']
            setValue('type', type)
            setValue('categoryId', '')
          }}><option value="Expense">Расход</option><option value="Income">Доход</option></select></label>
          <label>Категория<select {...register('categoryId')} disabled={categoriesQuery.isPending}>
            <option value="">Выберите</option>
            {activeCategories.map((category) =>
              <option key={category.id} value={category.id}>{category.name}</option>)}
          </select>{errors.categoryId && <span className="field-error">{errors.categoryId.message}</span>}</label>
          <label>Сумма ({user?.baseCurrency ?? 'RUB'})<input inputMode="decimal" placeholder="0,00" {...register('amount')} />
            {errors.amount && <span className="field-error">{errors.amount.message}</span>}</label>
          <label>Дата<input type="date" {...register('occurredOn')} />
            {errors.occurredOn && <span className="field-error">{errors.occurredOn.message}</span>}</label>
          <label className="form-wide">Описание<input placeholder="Необязательно" maxLength={500} {...register('description')} />
            {errors.description && <span className="field-error">{errors.description.message}</span>}</label>
          <button className="primary-button form-wide" disabled={createMutation.isPending || !activeCategories.length || !accounts.some(a => !a.isArchived)}>
            {createMutation.isPending ? 'Сохраняем…' : 'Добавить операцию'}
          </button>
          {!categoriesQuery.isPending && !activeCategories.length &&
            <p className="form-hint form-wide">Сначала создайте активную категорию этого типа.</p>}
          {categoriesQuery.isError && <span className="field-error form-wide">
            {getApiError(categoriesQuery.error, 'Не удалось загрузить категории.')}
          </span>}
          {accountsQuery.isError && <span className="field-error form-wide">{getApiError(accountsQuery.error, 'Не удалось загрузить счета.')}</span>}
          {accountsQuery.isSuccess && !accounts.some(a => !a.isArchived) && <p className="form-hint form-wide">Создайте или восстановите счёт на странице «Счета».</p>}
        </form>
      </section>
      </details>

      <section className="transactions-list-card">
        <div className="transactions-heading"><div><p className="eyebrow">История</p><h2>Все операции</h2></div>
      <span className="count-badge">{transactionsQuery.data?.totalCount ?? 0}</span></div>
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
        {transactionsQuery.isPending && <div className="category-loading">Загружаем операции…</div>}
        {transactionsQuery.isError && <div className="notice error">
          {getApiError(transactionsQuery.error, 'Не удалось загрузить операции.')}
        </div>}
        {transactionsQuery.isSuccess && transactionsQuery.data.items.length === 0 &&
          <p className="category-empty">Операций по выбранным условиям нет.</p>}
        {transactionsQuery.isSuccess && transactionsQuery.data.items.length > 0 && (
          <ul className="transaction-list">
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
        <div className="pagination">
          <button className="ghost-button" disabled={query.page <= 1 || transactionsQuery.isFetching}
            onClick={() => setQuery({ ...query, page: query.page - 1 })}>Назад</button>
          <span>Страница {query.page} из {Math.max(1, Math.ceil((transactionsQuery.data?.totalCount ?? 0) / query.pageSize))}</span>
          <button className="ghost-button" disabled={transactionsQuery.isFetching || query.page * query.pageSize >= (transactionsQuery.data?.totalCount ?? 0)}
            onClick={() => setQuery({ ...query, page: query.page + 1 })}>Далее</button>
        </div>
      </section>
    </section>
  )
}
