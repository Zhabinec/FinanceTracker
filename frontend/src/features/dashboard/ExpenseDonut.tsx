import { useState, type CSSProperties } from 'react'
import { expenseChart, type CategoryTotal } from './expenseChart'

export function ExpenseDonut({ categories, money }: { categories: CategoryTotal[]; money: (amount: string) => string }) {
  const chart = expenseChart(categories)
  const [selected, setSelected] = useState<string | null>(null)
  const active = chart.slices.find(slice => slice.id === selected)
  const select = (id: string) => setSelected(previous => previous === id ? null : id)
  if (!chart.slices.length) return null
  return <article className="transactions-list-card expense-chart" aria-label="Расходы по категориям">
    <h2>Расходы по категориям</h2>
    <div className="expense-chart-layout data-reveal">
      <div className="expense-donut">
        <svg viewBox="0 0 112 112" role="img" aria-label={`Круговая диаграмма расходов: ${money(chart.total)}`}>
          <circle cx="56" cy="56" r="40" fill="none" stroke="#e8e9e2" strokeWidth="16" />
          {chart.slices.map(slice => <circle key={slice.id} cx="56" cy="56" r="40" fill="none"
            pathLength="100" stroke={slice.color} strokeWidth={active?.id === slice.id ? 19 : 16}
            strokeDasharray={`${slice.share} ${100 - slice.share}`} strokeDashoffset={-slice.offset} transform="rotate(-90 56 56)"
            opacity={active && active.id !== slice.id ? 0.25 : 1} aria-hidden="true" onClick={() => select(slice.id)} />)}
        </svg>
        <div className="expense-donut-center" aria-live="polite" aria-atomic="true">
          <span>{active?.name ?? 'Всего расходов'}</span>
          <strong>{money(active?.amount ?? chart.total)}</strong>
          {active && <span>{active.percent}</span>}
        </div>
      </div>
      <ul className="expense-chart-legend">{chart.slices.map(slice => <li key={slice.id}>
        <button type="button" className="expense-chart-category" aria-pressed={active?.id === slice.id}
          style={{ '--slice-color': slice.color } as CSSProperties} onClick={() => select(slice.id)}>
          <span className="expense-chart-dot" aria-hidden="true" />
          <span className="expense-chart-name">{slice.name}{slice.count > 1 && <small>{slice.count} категорий</small>}</span>
          <span className="expense-chart-amount"><strong>{money(slice.amount)}</strong><small>{slice.percent}</small></span>
        </button>
      </li>)}</ul>
    </div>
  </article>
}
