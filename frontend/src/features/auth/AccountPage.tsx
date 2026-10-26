import { Link } from 'react-router-dom'
import { useAuth } from './useAuth'
import { TelegramLinkPanel } from './TelegramLinkPanel'

export function AccountPage() {
  const { user } = useAuth()

  return (
    <section className="dashboard-shell">
      <section className="welcome-card">
        <div><p className="muted">Ваш email</p><h2>{user?.email}</h2></div>
        <div className="currency-badge">{user?.baseCurrency}</div>
      </section>
      <TelegramLinkPanel />
      <section className="transactions-list-card"><h2>Доступ к аккаунту</h2>
        <p className="dashboard-note">Для смены пароля отправим ссылку на ваш email.</p>
        <Link className="ghost-button link-button" to="/forgot-password">Сменить пароль</Link>
      </section>
    </section>
  )
}
