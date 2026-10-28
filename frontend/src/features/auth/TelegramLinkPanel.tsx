import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { http } from '../../api/http'
import { getApiError } from '../../api/apiError'
import { isTelegram } from '../../platform/telegram'
import { useAuth } from './useAuth'
import { useRuntimeOptions } from './runtimeOptions'

export function TelegramLinkPanel() {
  const client = useQueryClient()
  const { signOut, user } = useAuth()
  const { telegramOnly } = useRuntimeOptions()
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const link = useQuery({ queryKey: ['telegram-link'], queryFn: async () =>
    (await http.get<{ linked: boolean; version: string | null }>('/auth/telegram/link')).data })
  const options = useQuery({ queryKey: ['telegram-options'], queryFn: async () =>
    (await http.get<{ enabled: boolean; botUsername: string }>('/auth/telegram/options')).data })
  if (!options.data?.enabled && !link.data?.linked) return null
  const unlink = async () => {
    setBusy(true); setError('')
    try {
      await http.delete('/auth/telegram/link', { params: { version: link.data?.version } })
      if (isTelegram()) await signOut()
      else await client.invalidateQueries({ queryKey: ['telegram-link'] })
    } catch (e) { setError(getApiError(e, 'Не удалось отключить Telegram.')) }
    finally { setBusy(false) }
  }
  return <section className="transactions-list-card"><h2>Telegram</h2>
    {link.isError ? <><p role="alert">Не удалось проверить привязку.</p><button className="ghost-button" onClick={() => void link.refetch()}>Повторить</button></>
      : link.isPending ? <p role="status">Загружаем…</p>
      : link.data.linked ? <><p>Аккаунт подключён.</p>{user?.email && !telegramOnly
        ? <button className="ghost-button" disabled={busy} onClick={() => void unlink()}>Отключить Telegram</button>
        : <p className="muted">Telegram — ваш способ входа в этот аккаунт.</p>}</>
      : <a className="ghost-button link-button" href={`https://t.me/${encodeURIComponent(options.data?.botUsername ?? '')}`} target="_blank" rel="noreferrer">Открыть бота</a>}
    {error && <p className="notice error" role="alert">{error}</p>}
  </section>
}
