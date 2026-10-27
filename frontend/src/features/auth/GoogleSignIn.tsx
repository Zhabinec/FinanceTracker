import { useEffect, useState } from 'react'
import { http } from '../../api/http'
import { isTelegram } from '../../platform/telegram'

export function GoogleSignIn({ link = false }: { link?: boolean }) {
  const [enabled, setEnabled] = useState(false), [busy, setBusy] = useState(false), [error, setError] = useState('')
  useEffect(() => {
    let active = true
    if (!isTelegram()) void http.get<{ enabled: boolean }>('/auth/google/options')
      .then(({ data }) => { if (active) setEnabled(data.enabled) }).catch(() => undefined)
    return () => { active = false }
  }, [])
  if (!enabled) return null
  return <>
    {error && <p role="alert">{error}</p>}
    <button type="button" className="ghost-button" disabled={busy} onClick={async () => {
      if (!link) { window.location.assign('/api/auth/google/start'); return }
      setBusy(true); setError('')
      try {
        const { data } = await http.post<{ url: string }>('/auth/google/link/start')
        if (data.url !== '/api/auth/google/start?link=true') throw new Error('Invalid redirect')
        window.location.assign(data.url)
      } catch { setError('Не удалось подключить Google. Попробуйте ещё раз.'); setBusy(false) }
    }}>{busy ? 'Подключаем…' : link ? 'Подключить Google' : 'Продолжить с Google'}</button>
  </>
}
