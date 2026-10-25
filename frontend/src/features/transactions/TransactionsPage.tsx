import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { getApiError } from '../../api/apiError'
import { getCategories } from '../categories/categoryApi'
import { useAuth } from '../auth/useAuth'
import { TransactionRow } from './TransactionRow'
import type { TransactionInput } from './transactionApi'
import { useTransactions } from './useTransactions'
import { toTransactionInput, todayInMoscow, transactionSchema, type TransactionForm } from './validation'

export function TransactionsPage() {
  const { user, signOut } = useAuth()
  const [serverError, setServerError] = useState('')
  const [leaving, setLeaving] = useState(false)
  const { register, handleSubmit, control, setValue, setError, reset, formState: { errors } } =
    useForm<TransactionForm>({
      defaultValues: {
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
    useTransactions()
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

  const remove = async (transactionId: string) => {
    await actionMutation.mutateAsync({ kind: 'delete', transactionId })
  }

  const categories = categoriesQuery.data ?? []

  return (
    <main className="dashboard-shell transactions-page">
      <header className="app-header">
        <div><p className="eyebrow">Finance Tracker</p><h1>Операции</h1><p className="header-user">{user?.email}</p></div>
        <nav className="header-actions">
          <Link className="ghost-button link-button" to="/">Аккаунт</Link>
          <Link className="ghost-button link-button" to="/categories">Категории</Link>
          <button className="ghost-button" disabled={leaving} onClick={async () => {
            setLeaving(true)
            await signOut()
          }}>{leaving ? 'Выходим…' : 'Выйти'}</button>
        </nav>
      </header>

      <section className="transaction-create-card">
        <div><p className="eyebrow">Новая операция</p><h2>Запишите движение денег</h2>
          <p>Дата считается по Москве, а категория определяет, доход это или расход.</p></div>
        <form className="transaction-create-form" onSubmit={submit} noValidate>
          {serverError && <div className="notice error form-wide">{serverError}</div>}
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
          <label>Сумма<input inputMode="decimal" placeholder="0,00" {...register('amount')} />
            {errors.amount && <span className="field-error">{errors.amount.message}</span>}</label>
          <label>Дата<input type="date" {...register('occurredOn')} />
            {errors.occurredOn && <span className="field-error">{errors.occurredOn.message}</span>}</label>
          <label className="form-wide">Описание<input placeholder="Необязательно" maxLength={500} {...register('description')} />
            {errors.description && <span className="field-error">{errors.description.message}</span>}</label>
          <button className="primary-button form-wide" disabled={createMutation.isPending || !activeCategories.length}>
            {createMutation.isPending ? 'Сохраняем…' : 'Добавить операцию'}
          </button>
          {!categoriesQuery.isPending && !activeCategories.length &&
            <p className="form-hint form-wide">Сначала создайте активную категорию этого типа.</p>}
          {categoriesQuery.isError && <span className="field-error form-wide">
            {getApiError(categoriesQuery.error, 'Не удалось загрузить категории.')}
          </span>}
        </form>
      </section>

      <section className="transactions-list-card">
        <div className="transactions-heading"><div><p className="eyebrow">История</p><h2>Все операции</h2></div>
          <span className="count-badge">{transactionsQuery.data?.length ?? 0}</span></div>
        {transactionsQuery.isPending && <div className="category-loading">Загружаем операции…</div>}
        {transactionsQuery.isError && <div className="notice error">
          {getApiError(transactionsQuery.error, 'Не удалось загрузить операции.')}
        </div>}
        {transactionsQuery.isSuccess && transactionsQuery.data.length === 0 &&
          <p className="category-empty">Операций пока нет.</p>}
        {transactionsQuery.isSuccess && transactionsQuery.data.length > 0 && (
          <ul className="transaction-list">
            {transactionsQuery.data.map((transaction) => (
              <TransactionRow
                key={transaction.id}
                transaction={transaction}
                categories={categories}
                currency={user?.baseCurrency ?? 'RUB'}
                busy={busyTransactionId === transaction.id}
                onUpdate={update}
                onDelete={remove}
              />
            ))}
          </ul>
        )}
      </section>
    </main>
  )
}
