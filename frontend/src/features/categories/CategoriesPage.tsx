import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { Link } from 'react-router-dom'
import { getApiError } from '../../api/apiError'
import { useAuth } from '../auth/useAuth'
import * as categoryApi from './categoryApi'
import type { Category, CategoryType } from './categoryApi'
import { categorySchema, type CategoryForm } from './validation'

type CategoryAction =
  | { kind: 'rename'; categoryId: string; name: string }
  | { kind: 'deactivate'; categoryId: string }
  | { kind: 'restore'; categoryId: string }

function CategoryRow({
  category,
  busy,
  onAction,
}: {
  category: Category
  busy: boolean
  onAction: (action: CategoryAction) => void
}) {
  const [editing, setEditing] = useState(false)
  const [name, setName] = useState(category.name)
  const [nameError, setNameError] = useState('')

  const submitRename = () => {
    const parsed = categorySchema.shape.name.safeParse(name)
    if (!parsed.success) {
      setNameError(parsed.error.issues[0]?.message ?? 'Проверьте название.')
      return
    }

    setNameError('')
    setEditing(false)
    onAction({ kind: 'rename', categoryId: category.id, name: parsed.data })
  }

  return (
    <li className={`category-row ${category.isActive ? '' : 'inactive'}`}>
      <div className="category-row-main">
        <span className={`category-dot ${category.type.toLowerCase()}`} />
        {editing ? (
          <div className="rename-editor">
            <input
              aria-label="Новое название"
              autoFocus
              value={name}
              maxLength={100}
              onChange={(event) => setName(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter') submitRename()
                if (event.key === 'Escape') {
                  setName(category.name)
                  setNameError('')
                  setEditing(false)
                }
              }}
            />
            {nameError && <span className="field-error">{nameError}</span>}
          </div>
        ) : (
          <div>
            <strong>{category.name}</strong>
            {!category.isActive && <span className="status-label">Неактивна</span>}
          </div>
        )}
      </div>
      <div className="category-actions">
        {editing ? (
          <>
            <button className="text-button" disabled={busy} onClick={submitRename}>Сохранить</button>
            <button className="text-button muted-action" onClick={() => {
              setName(category.name)
              setNameError('')
              setEditing(false)
            }}>Отмена</button>
          </>
        ) : (
          <>
            <button className="text-button" disabled={busy} onClick={() => setEditing(true)}>Изменить</button>
            {category.isActive ? (
              <button className="text-button danger-action" disabled={busy}
                onClick={() => onAction({ kind: 'deactivate', categoryId: category.id })}>
                Отключить
              </button>
            ) : (
              <button className="text-button" disabled={busy}
                onClick={() => onAction({ kind: 'restore', categoryId: category.id })}>
                Восстановить
              </button>
            )}
          </>
        )}
      </div>
    </li>
  )
}

function CategoryColumn({
  title,
  hint,
  type,
  categories,
  busyId,
  onAction,
}: {
  title: string
  hint: string
  type: CategoryType
  categories: Category[]
  busyId?: string
  onAction: (action: CategoryAction) => void
}) {
  const visible = categories.filter((category) => category.type === type)

  return (
    <section className="category-column">
      <div className="category-column-heading">
        <div><p className="eyebrow">{hint}</p><h2>{title}</h2></div>
        <span className="count-badge">{visible.filter((category) => category.isActive).length}</span>
      </div>
      {visible.length === 0 ? (
        <p className="category-empty">Пока ни одной категории.</p>
      ) : (
        <ul className="category-list">
          {visible.map((category) => (
            <CategoryRow key={category.id} category={category} busy={busyId === category.id} onAction={onAction} />
          ))}
        </ul>
      )}
    </section>
  )
}

export function CategoriesPage() {
  const { user, signOut } = useAuth()
  const queryClient = useQueryClient()
  const [serverError, setServerError] = useState('')
  const [actionError, setActionError] = useState('')
  const [leaving, setLeaving] = useState(false)
  const { register, handleSubmit, reset, setError, formState: { errors } } = useForm<CategoryForm>({
    defaultValues: { name: '', type: 'Expense' },
  })

  const categoriesQuery = useQuery({
    queryKey: ['categories'],
    queryFn: categoryApi.getCategories,
  })

  const createMutation = useMutation({
    mutationFn: (values: CategoryForm) => categoryApi.createCategory(values.name, values.type),
    onSuccess: async () => {
      reset({ name: '', type: 'Expense' })
      await queryClient.invalidateQueries({ queryKey: ['categories'] })
    },
  })

  const actionMutation = useMutation({
    mutationFn: (action: CategoryAction) => {
      if (action.kind === 'rename')
        return categoryApi.renameCategory(action.categoryId, action.name)
      if (action.kind === 'deactivate')
        return categoryApi.deactivateCategory(action.categoryId)
      return categoryApi.restoreCategory(action.categoryId)
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['categories'] }),
  })

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
    } catch (error) {
      setServerError(getApiError(error, 'Не удалось создать категорию.'))
    }
  })

  const runAction = async (action: CategoryAction) => {
    setActionError('')
    try {
      await actionMutation.mutateAsync(action)
    } catch (error) {
      setActionError(getApiError(error, 'Не удалось изменить категорию.'))
    }
  }

  const categories = categoriesQuery.data ?? []
  const busyCategoryId = actionMutation.isPending
    ? actionMutation.variables?.categoryId
    : undefined

  return (
    <main className="dashboard-shell categories-page">
      <header className="app-header">
        <div><p className="eyebrow">Finance Tracker</p><h1>Категории</h1><p className="header-user">{user?.email}</p></div>
        <nav className="header-actions">
          <Link className="ghost-button link-button" to="/">Аккаунт</Link>
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
            busyId={busyCategoryId} onAction={(action) => { void runAction(action) }} />
          <CategoryColumn title="Расходы" hint="Деньги уходят" type="Expense" categories={categories}
            busyId={busyCategoryId} onAction={(action) => { void runAction(action) }} />
        </div>
      )}
    </main>
  )
}
