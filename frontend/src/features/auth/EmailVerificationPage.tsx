import { useState, type FormEvent } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { AuthLayout } from './AuthLayout'
import { http } from '../../api/http'
import { getApiError } from '../../api/apiError'
import { LocalMailHint } from './LocalMailHint'

export function EmailVerificationPage({ confirm = false }: { confirm?: boolean }) {
  const location = useLocation()
  const [email, setEmail] = useState((location.state as { email?: string } | null)?.email ?? '')
  const [done, setDone] = useState(false)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const token = new URLSearchParams(location.hash.slice(1)).get('token') ?? ''
  const registered = Boolean((location.state as { registered?: boolean } | null)?.registered)

  const submit = async (event: FormEvent) => {
    event.preventDefault(); setBusy(true); setError('')
    try {
      await http.post(confirm ? '/auth/confirm-email' : '/auth/resend-confirmation', confirm ? { token } : { email })
      setDone(true)
      if (confirm) window.history.replaceState(null, '', '/confirm-email')
    } catch (failure) { setError(getApiError(failure, 'Не удалось выполнить запрос. Повторите позже.')) }
    finally { setBusy(false) }
  }

  return <AuthLayout title={confirm ? 'Подтверждение email' : 'Проверьте почту'}
    subtitle={confirm ? 'Подтвердите адрес, чтобы войти в аккаунт.' : 'Для входа нужно подтвердить, что email принадлежит вам.'}
    alternateText="Уже подтвердили адрес?" alternateLink="/login" alternateLabel="Войти">
    {registered && <p className="notice success">Аккаунт создан для {email}. Откройте письмо и подтвердите email.</p>}
    {error && <div className="notice error" role="alert">{error} <Link to="/verify-email">Запросить новое письмо</Link></div>}
    {done ? <p className="notice success" role="status">{confirm ? 'Email подтверждён. Теперь можно войти.' : 'Если аккаунт существует и email ещё не подтверждён, письмо будет отправлено.'}</p> :
      <form onSubmit={submit}>
        {!confirm && <label>Email<input type="email" autoComplete="email" required maxLength={320} value={email} onChange={(event) => setEmail(event.target.value)} /></label>}
        {confirm && !token && <p>Откройте ссылку из письма или <Link to="/verify-email">запросите новое письмо</Link>.</p>}
        <button className="primary-button" disabled={busy || (confirm && !token)}>{busy ? 'Отправляем…' : confirm ? 'Подтвердить email' : 'Повторно отправить письмо'}</button>
      </form>}
    <LocalMailHint />
  </AuthLayout>
}
