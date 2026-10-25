import { useState, type FormEvent } from 'react'
import { AuthLayout } from './AuthLayout'
import { http, setAccessToken } from '../../api/http'
import { getApiError } from '../../api/apiError'
import { useAuth } from './useAuth'
import { Link, useLocation } from 'react-router-dom'
import { PasswordInput } from '../../shared/PasswordInput'
import { LocalMailHint } from './LocalMailHint'
import axios from 'axios'

export function PasswordRecoveryPage({ reset = false }: { reset?: boolean }) {
  const { signOut } = useAuth()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const location = useLocation()
  const token = new URLSearchParams(location.hash.slice(1)).get('token') ?? ''
  const [busy, setBusy] = useState(false)
  const [done, setDone] = useState(false)
  const [error, setError] = useState('')
  const [linkProblem, setLinkProblem] = useState(false)

  const submit = async (event: FormEvent) => {
    event.preventDefault(); setError(''); setLinkProblem(false)
    if (reset && (password.length < 8 || password.length > 128 || password !== confirmation)) {
      setError('Введите пароль от 8 до 128 символов и повторите его без изменений.'); return
    }
    setBusy(true)
    try {
      if (reset) {
        await http.post('/auth/reset-password', { token, password })
        setAccessToken(null)
        await signOut()
        window.history.replaceState(null, '', '/reset-password')
      } else await http.post('/auth/forgot-password', { email })
      setDone(true)
    } catch (failure) {
      setError(getApiError(failure, 'Не удалось выполнить запрос. Повторите позже.'))
      setLinkProblem(axios.isAxiosError(failure) && failure.response?.status === 400 &&
        String(failure.response.data?.title ?? '').includes('Ссылка'))
    }
    finally { setBusy(false) }
  }

  return <AuthLayout title={reset ? 'Новый пароль' : 'Восстановление доступа'}
    subtitle={reset ? 'Задайте новый пароль для аккаунта.' : 'Отправим ссылку на ваш email.'}
    alternateText="Вернуться ко входу" alternateLink="/login" alternateLabel="Войти">
    {error && <div className="notice error" role="alert">{error}{reset && linkProblem && <p>Ссылка могла истечь или уже использоваться. <Link to="/forgot-password">Запросить новую ссылку</Link></p>}</div>}
    {done ? <div className="notice success" role="status">{reset ? 'Пароль изменён. Войдите с новым паролем.' : 'Если аккаунт существует, письмо с инструкцией будет отправлено.'}</div> :
      <form onSubmit={submit}>
        {reset ? <>
          <PasswordInput label="Новый пароль" autoComplete="new-password" minLength={8} required value={password} onChange={(event) => setPassword(event.target.value)} />
          <PasswordInput label="Повторите пароль" autoComplete="new-password" required value={confirmation} onChange={(event) => setConfirmation(event.target.value)} />
        </> : <label>Email<input type="email" autoComplete="email" maxLength={320} required value={email} onChange={(event) => setEmail(event.target.value)} /></label>}
        <button className="primary-button" disabled={busy || (reset && !token)}>{busy ? 'Отправляем…' : reset ? 'Сменить пароль' : 'Отправить ссылку'}</button>
        {reset && !token && <p>Откройте ссылку из письма восстановления пароля.</p>}
      </form>}
    {!reset && <LocalMailHint />}
  </AuthLayout>
}
