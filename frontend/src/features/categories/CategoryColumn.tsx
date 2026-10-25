import type { Category, CategoryType } from './categoryApi'
import { CategoryRow } from './CategoryRow'
import type { CategoryAction } from './useCategories'

export function CategoryColumn({
  title,
  hint,
  type,
  categories,
  busyId,
  onAction,
  onQuickAdd,
}: {
  title: string
  hint: string
  type: CategoryType
  categories: Category[]
  busyId?: string
  onAction: (action: CategoryAction) => Promise<boolean>
  onQuickAdd: (category: Category) => void
}) {
  const visible = categories.filter((category) => category.type === type)

  return (
    <section className="category-column">
      <div className="category-column-heading">
        <div><p className="eyebrow">{hint}</p><h2>{title}</h2></div>
        <span className="count-badge">
          {visible.filter((category) => category.isActive).length}
        </span>
      </div>
      {visible.length === 0 ? (
        <p className="category-empty">Пока ни одной категории.</p>
      ) : (
        <ul className="category-list">
          {visible.map((category) => (
            <CategoryRow
              key={category.id}
              category={category}
              busy={busyId === category.id}
              onAction={onAction}
              onQuickAdd={onQuickAdd}
            />
          ))}
        </ul>
      )}
    </section>
  )
}
