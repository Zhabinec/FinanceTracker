import { useState } from 'react'
import { useAuth } from './useAuth'

export function AccountPage() {
  const { user, signOut } = useAuth()
  const [leaving, setLeaving] = useState(false)

  return (
    <main className="dashboard-shell">
      <header><div><p className="eyebrow">Finance Tracker</p><h1>Добрый день</h1></div>
        <button className="ghost-button" disabled={leaving} onClick={async () => { setLeaving(true); await signOut() }}>
          {leaving ? 'Выходим…' : 'Выйти'}
        </button></header>
      <section className="welcome-card">
        <div><p className="muted">Аккаунт подключён</p><h2>{user?.email}</h2></div>
        <div className="currency-badge">{user?.baseCurrency}</div>
      </section>
      <section className="empty-state"><span>↗</span><h2>Авторизация работает</h2>
        <p>Следующим срезом здесь появятся категории доходов и расходов.</p></section>
    </main>
  )
}
