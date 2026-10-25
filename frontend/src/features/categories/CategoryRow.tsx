import { useEffect, useId, useRef, useState } from 'react'
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
  const [menuOpen, setMenuOpen] = useState(false)
  const menuRef = useRef<HTMLDivElement>(null)
  const menuButton = useRef<HTMLButtonElement>(null)
  const menuId = useId()

  useEffect(() => {
    if (!menuOpen) return
    const dismiss = (event: PointerEvent | FocusEvent) => {
      if (event.target instanceof Node && !menuRef.current?.contains(event.target)) setMenuOpen(false)
    }
    const escape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') { setMenuOpen(false); menuButton.current?.focus() }
    }
    document.addEventListener('pointerdown', dismiss)
    document.addEventListener('focusin', dismiss)
    document.addEventListener('keydown', escape)
    return () => {
      document.removeEventListener('pointerdown', dismiss)
      document.removeEventListener('focusin', dismiss)
      document.removeEventListener('keydown', escape)
    }
  }, [menuOpen])

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
    <li className={`category-row category-tile ${category.isActive ? '' : 'inactive'} ${menuOpen ? 'menu-open' : ''} ${editing ? 'is-editing' : ''}`}>
      {!editing && category.isActive && <button type="button" className="category-tile-hitbox" disabled={busy}
        aria-label={`Добавить ${category.type === 'Expense' ? 'расход' : 'доход'}: ${category.name}`}
        onClick={() => { setMenuOpen(false); onQuickAdd(category) }} />}
      {editing ? <div className="rename-editor">
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
          <div className="category-rename-actions">
            <button type="button" className="text-button" disabled={busy}
              onClick={() => { void submitRename() }}>Сохранить</button>
            <button type="button" className="text-button muted-action"
              disabled={busy} onClick={cancelEditing}>Отмена</button>
          </div>
        </div> : <>
          <div className="category-tile-content" aria-hidden={category.isActive ? true : undefined}>
            <span className={`category-tile-icon ${category.type.toLowerCase()}`}>{category.type === 'Expense' ? '−' : '+'}</span>
            <strong>{category.name}</strong>
            <span className="status-label">{!category.isActive ? 'Неактивна' : category.type === 'Expense' ? 'Добавить расход' : 'Добавить доход'}</span>
          </div>
          <div className="category-tile-menu" ref={menuRef}>
            <button type="button" className="category-menu-toggle" ref={menuButton} disabled={busy}
              aria-label={`Настройки категории ${category.name}`} aria-expanded={menuOpen} aria-controls={menuId}
              onClick={() => setMenuOpen(previous => !previous)}>⋯</button>
            {menuOpen && <div className="category-menu-panel" id={menuId} role="group" aria-label="Действия с категорией">
            <button type="button" disabled={busy}
              onClick={() => { setName(category.name); setNameError(''); setMenuOpen(false); setEditing(true) }}>Переименовать</button>
            {category.isActive ? (
              <button type="button" className="danger-action" disabled={busy}
                onClick={() => { setMenuOpen(false); void onAction({ kind: 'deactivate', categoryId: category.id }) }}>
                Отключить
              </button>
            ) : (
              <button type="button" disabled={busy}
                onClick={() => { setMenuOpen(false); void onAction({ kind: 'restore', categoryId: category.id }) }}>
                Восстановить
              </button>
            )}
            </div>}
          </div>
        </>}
    </li>
  )
}
