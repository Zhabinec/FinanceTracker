type Props = { label?: string; kind?: 'rows' | 'cards' | 'overview'; rows?: number }

export function DataSkeleton({ label = 'Загружаем данные…', kind = 'rows', rows = 4 }: Props) {
  const cards = kind === 'cards' || kind === 'overview'
  return <div className={`data-skeleton data-skeleton--${kind}`} role="status" aria-label={label} aria-busy="true">
    <span className="loading-label">{label}</span>
    <div aria-hidden="true">
      {cards && <div className="skeleton-cards">{Array.from({ length: kind === 'overview' ? 3 : 2 }, (_, index) =>
        <div className="skeleton-card" key={index}><span className="skeleton-block skeleton-caption" /><span className="skeleton-block skeleton-value" /><span className="skeleton-block skeleton-detail" /></div>)}</div>}
      {kind !== 'cards' && <div className="skeleton-rows">{Array.from({ length: rows }, (_, index) =>
        <div className="skeleton-row" key={index}><span className="skeleton-block skeleton-icon" /><div><span className="skeleton-block skeleton-caption" /><span className="skeleton-block skeleton-detail" /></div><span className="skeleton-block skeleton-amount" /></div>)}</div>}
    </div>
  </div>
}
