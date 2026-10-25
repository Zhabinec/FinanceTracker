import { useState } from 'react'
import type { Category } from './categoryApi'
import type { CategoryAction } from './useCategories'
import { categorySchema } from './validation'

export function CategoryRow({
  category,
  busy,
  onAction,
  onQuickAdd,
}: {
  category: Category
  busy: boolean
  onAction: (action: CategoryAction) => Promise<boolean>
  onQuickAdd: (category: Category) => void
}) {
  const [editing, setEditing] = useState(false)
  const [name, setName] = useState(category.name)
  const [nameError, setNameError] = useState('')

  const cancelEditing = () => {
    setName(category.name)
    setNameError('')
    setEditing(false)
  }

  const submitRename = async () => {
    const parsed = categorySchema.shape.name.safeParse(name)
    if (!parsed.success) {
      setNameError(parsed.error.issues[0]?.message ?? 'Проверьте название.')
      return
    }

    setNameError('')
    const saved = await onAction({
      kind: 'rename',
      categoryId: category.id,
      name: parsed.data,
    })

    if (saved) setEditing(false)
    else setNameError('Изменение не сохранено.')
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
                if (event.key === 'Enter') void submitRename()
                if (event.key === 'Escape') cancelEditing()
              }}
            />
            {nameError && <span className="field-error">{nameError}</span>}
          </div>
        ) : (
          category.isActive ? <button type="button" className="category-quick-button" disabled={busy}
            aria-label={`Добавить ${category.type === 'Expense' ? 'расход' : 'доход'}: ${category.name}`}
            onClick={() => onQuickAdd(category)}><strong>{category.name}</strong>
            <span className="status-label">+ {category.type === 'Expense' ? 'Расход' : 'Доход'}</span></button> :
          <div><strong>{category.name}</strong><span className="status-label">Неактивна</span></div>
        )}
      </div>
      <div className="category-actions">
        {editing ? (
          <>
            <button type="button" className="text-button" disabled={busy}
              onClick={() => { void submitRename() }}>Сохранить</button>
            <button type="button" className="text-button muted-action"
              disabled={busy} onClick={cancelEditing}>Отмена</button>
          </>
        ) : (
          <>
            <button type="button" className="text-button" disabled={busy}
              onClick={() => setEditing(true)}>Изменить</button>
            {category.isActive ? (
              <button type="button" className="text-button danger-action" disabled={busy}
                onClick={() => { void onAction({ kind: 'deactivate', categoryId: category.id }) }}>
                Отключить
              </button>
            ) : (
              <button type="button" className="text-button" disabled={busy}
                onClick={() => { void onAction({ kind: 'restore', categoryId: category.id }) }}>
                Восстановить
              </button>
            )}
          </>
        )}
      </div>
    </li>
  )
}
