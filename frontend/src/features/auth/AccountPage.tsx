import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from './useAuth'

export function AccountPage() {
  const { user, signOut } = useAuth()
  const [leaving, setLeaving] = useState(false)

  return (
    <main className="dashboard-shell">
      <header><div><p className="eyebrow">Finance Tracker</p><h1>Добрый день</h1></div>
        <nav className="header-actions">
          <Link className="ghost-button link-button" to="/">Обзор</Link>
          <Link className="ghost-button link-button" to="/transactions">Операции</Link>
          <Link className="ghost-button link-button" to="/accounts">Счета</Link>
          <Link className="ghost-button link-button" to="/categories">Категории</Link>
          <button className="ghost-button" disabled={leaving} onClick={async () => { setLeaving(true); await signOut() }}>
            {leaving ? 'Выходим…' : 'Выйти'}
          </button>
        </nav></header>
      <section className="welcome-card">
        <div><p className="muted">Аккаунт подключён</p><h2>{user?.email}</h2></div>
        <div className="currency-badge">{user?.baseCurrency}</div>
      </section>
      <section className="empty-state"><span>↗</span><h2>Управляйте движением денег</h2>
        <p>Настройте категории, затем добавляйте доходы и расходы в журнал операций.</p>
        <Link className="primary-button empty-state-link" to="/transactions">Открыть операции</Link>
      </section>
    </main>
  )
}
