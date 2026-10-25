import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link } from 'react-router-dom'
import { getApiError } from '../../api/apiError'
import { useAuth } from '../auth/useAuth'
import { CategoryColumn } from './CategoryColumn'
import { useCategories, type CategoryAction } from './useCategories'
import { categorySchema, type CategoryForm } from './validation'

export function CategoriesPage() {
  const { user, signOut } = useAuth()
  const [serverError, setServerError] = useState('')
  const [actionError, setActionError] = useState('')
  const [leaving, setLeaving] = useState(false)
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
          <Link className="ghost-button link-button" to="/transactions">Операции</Link>
          <button className="ghost-button" disabled={leaving} onClick={async () => {
            setLeaving(true)
            await signOut()
          }}>{leaving ? 'Выходим…' : 'Выйти'}</button>
        </nav>
      </header>

      <section className="category-create-card">
        <div><p className="eyebrow">Новая категория</p><h2>Добавьте направление денег</h2>
          <p>Тип фиксируется при создании: так будущие операции не смогут поменять смысл задним числом.</p></div>
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

      {actionError && <div className="notice error category-notice">{actionError}</div>}
      {categoriesQuery.isPending && <div className="category-loading">Загружаем категории…</div>}
      {categoriesQuery.isError && <div className="notice error category-notice">
        {getApiError(categoriesQuery.error, 'Не удалось загрузить категории.')}
      </div>}
      {categoriesQuery.isSuccess && (
        <div className="category-grid">
          <CategoryColumn title="Доходы" hint="Деньги приходят" type="Income" categories={categories}
            busyId={busyCategoryId} onAction={runAction} />
          <CategoryColumn title="Расходы" hint="Деньги уходят" type="Expense" categories={categories}
            busyId={busyCategoryId} onAction={runAction} />
        </div>
      )}
    </main>
  )
}
