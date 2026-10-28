import { Link } from 'react-router-dom'
import { useAuth } from './useAuth'
import { TelegramLinkPanel } from './TelegramLinkPanel'
import { VaultControls } from '../privateFinance/VaultControls'
import { GoogleSignIn } from './GoogleSignIn'
import { useQuery } from '@tanstack/react-query'
import { http } from '../../api/http'
import { useRuntimeOptions } from './runtimeOptions'

export function AccountPage() {
  const { user } = useAuth()
  const { telegramOnly } = useRuntimeOptions()
  const delivery = useQuery({ queryKey: ['email-delivery-mode'], enabled: !telegramOnly, queryFn: async () =>
    (await http.get<{ simulated: boolean; restricted?: boolean; previewUrl: string | null }>('/auth/email-delivery')).data,
    staleTime: Infinity })

  return (
    <section className="dashboard-shell">
      <section className="welcome-card">
        <div><p className="muted">Аккаунт</p><h2>{user?.email ?? 'Telegram'}</h2></div>
        <div className="currency-badge">{user?.baseCurrency}</div>
      </section>
      <TelegramLinkPanel />
      <VaultControls />
      {!telegramOnly && <section className="transactions-list-card"><h2>Доступ к аккаунту</h2>
        <GoogleSignIn link />
        {user?.email && delivery.data && !delivery.data.restricted && <><p className="dashboard-note">Для смены пароля отправим ссылку на ваш email.</p>
        <Link className="ghost-button link-button" to="/forgot-password">Сменить пароль</Link></>}
      </section>}
    </section>
  )
}
