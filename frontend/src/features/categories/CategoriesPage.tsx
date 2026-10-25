import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link } from 'react-router-dom'
import { getApiError } from '../../api/apiError'
import { useAuth } from '../auth/useAuth'
import { CategoryColumn } from './CategoryColumn'
import { useCategories, type CategoryAction } from './useCategories'
import { categorySchema, type CategoryForm } from './validation'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useAccounts } from '../accounts/accountApi'
import type { Category } from './categoryApi'
import { QuickTransactionDialog } from './QuickTransactionDialog'
import { deleteTransaction, type FinanceTransaction } from '../transactions/transactionApi'
import { formatMoney } from '../../shared/formatting'

export function CategoriesPage() {
  const { user, signOut } = useAuth()
  const [serverError, setServerError] = useState('')
  const [actionError, setActionError] = useState('')
  const [leaving, setLeaving] = useState(false)
  const [quickCategory, setQuickCategory] = useState<Category | null>(null)
  const [added, setAdded] = useState<FinanceTransaction | null>(null)
  const [undoError, setUndoError] = useState('')
  const [undone, setUndone] = useState(false)
  const accountsQuery = useAccounts()
  const queryClient = useQueryClient()
  const undoMutation = useMutation({
    mutationFn: (transaction: FinanceTransaction) => deleteTransaction(transaction.id, transaction.version),
    onSuccess: async () => {
      await Promise.all(['transactions', 'dashboard', 'accounts'].map(key => queryClient.invalidateQueries({ queryKey: [key] })))
      setAdded(null); setUndone(true)
    },
  })
  const { register, handleSubmit, reset, setError, formState: { errors } } = useForm<CategoryForm>({
    defaultValues: { name: '', type: 'Expense' },
  })

  const { categoriesQuery, createMutation, actionMutation, busyCategoryId } =
    useCategories()

  const submit = handleSubmit(async (values) => {
    setServerError('')
    const parsed = categorySchema.safeParse(values)
    if (!parsed.success) {
      for (const issue of parsed.error.issues)
        setError(issue.path[0] as keyof CategoryForm, { message: issue.message })
      return
    }

    try {
      await createMutation.mutateAsync(parsed.data)
      reset({ name: '', type: 'Expense' })
    } catch (error) {
      setServerError(getApiError(error, 'Не удалось создать категорию.'))
    }
  })

  const runAction = async (action: CategoryAction) => {
    setActionError('')
    try {
      await actionMutation.mutateAsync(action)
      return true
    } catch (error) {
      setActionError(getApiError(error, 'Не удалось изменить категорию.'))
      return false
    }
  }

  const categories = categoriesQuery.data ?? []

  return (
    <main className="dashboard-shell categories-page">
      <header className="app-header">
        <div><p className="eyebrow">Finance Tracker</p><h1>Категории</h1><p className="header-user">{user?.email}</p></div>
        <nav className="header-actions">
          <Link className="ghost-button link-button" to="/">Обзор</Link>
          <Link className="ghost-button link-button" to="/accounts">Счета</Link>
          <Link className="ghost-button link-button" to="/transactions">Операции</Link>
          <button className="ghost-button" disabled={leaving} onClick={async () => {
            setLeaving(true)
            await signOut()
          }}>{leaving ? 'Выходим…' : 'Выйти'}</button>
        </nav>
      </header>

      <p className="category-quick-hint">Нажмите на категорию, введите название и сумму — готово.</p>
      {added && <div className="notice success quick-entry-notice" role="status">
        Добавлено: {added.description} · {formatMoney(added.amount, user?.baseCurrency ?? 'RUB')}
        <button type="button" className="text-button" disabled={undoMutation.isPending} onClick={async () => {
          setUndoError('')
          try { await undoMutation.mutateAsync(added) }
          catch (error) { setUndoError(getApiError(error, 'Не удалось отменить добавление. Проверьте журнал операций.')) }
        }}>{undoMutation.isPending ? 'Отменяем…' : 'Отменить добавление'}</button>
        <Link to="/transactions">История операций</Link>
      </div>}
      {undone && <div className="notice success" role="status">Добавление отменено.</div>}
      {undoError && <div className="notice error" role="alert">{undoError}</div>}
      <details className="category-create-disclosure"><summary>Создать категорию</summary>
      <section className="category-create-card">
        <div><p className="eyebrow">Новая категория</p><h2>Добавьте направление денег</h2>
          <p>Например, «Продукты» для расходов или «Зарплата» для доходов.</p></div>
        <form className="category-create-form" onSubmit={submit} noValidate>
          {serverError && <div className="notice error form-wide">{serverError}</div>}
          <label>Название<input placeholder="Например, Продукты" maxLength={100} {...register('name')} /></label>
          <label>Тип<select {...register('type')}><option value="Expense">Расход</option><option value="Income">Доход</option></select></label>
          <button className="primary-button" disabled={createMutation.isPending}>
            {createMutation.isPending ? 'Добавляем…' : 'Добавить'}
          </button>
          {errors.name && <span className="field-error form-wide">{errors.name.message}</span>}
          {errors.type && <span className="field-error form-wide">{errors.type.message}</span>}
        </form>
      </section>
      </details>

      {actionError && <div className="notice error category-notice">{actionError}</div>}
      {categoriesQuery.isPending && <div className="category-loading">Загружаем категории…</div>}
      {categoriesQuery.isError && <div className="notice error category-notice">
        {getApiError(categoriesQuery.error, 'Не удалось загрузить категории.')}
      </div>}
      {categoriesQuery.isSuccess && (
        <div className="category-grid">
          <CategoryColumn title="Расходы" hint="Деньги уходят" type="Expense" categories={categories}
            busyId={busyCategoryId} onAction={runAction} onQuickAdd={setQuickCategory} />
          <CategoryColumn title="Доходы" hint="Деньги приходят" type="Income" categories={categories}
            busyId={busyCategoryId} onAction={runAction} onQuickAdd={setQuickCategory} />
        </div>
      )}
      {quickCategory && user && <QuickTransactionDialog key={quickCategory.id} category={quickCategory}
        userId={user.id} accounts={accountsQuery.data ?? []} accountsPending={accountsQuery.isPending} accountsError={accountsQuery.isError}
        onRetryAccounts={() => { void accountsQuery.refetch() }}
        onClose={() => setQuickCategory(null)} onSaved={transaction => {
          setAdded(transaction); setUndone(false); setUndoError(''); setQuickCategory(null)
        }} />}
    </main>
  )
}
