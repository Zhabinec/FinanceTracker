import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { useLocation, useNavigate } from 'react-router-dom'
import { Link } from 'react-router-dom'
import { PasswordInput } from '../../shared/PasswordInput'
import { AuthLayout } from './AuthLayout'
import { useAuth } from './useAuth'
import { getApiError } from '../../api/apiError'
import { loginSchema, type LoginForm } from './validation'
import { isTelegram, telegramLaunchError } from '../../platform/telegram'
import { GoogleSignIn } from './GoogleSignIn'

export function LoginPage() {
  const { register, handleSubmit, setError, formState: { errors, isSubmitting } } = useForm<LoginForm>({
    defaultValues: { email: (useLocation().state as { email?: string } | null)?.email ?? '', password: '' },
  })
  const { signIn, signInWithTelegram, restoreError } = useAuth()
  const navigate = useNavigate()
  const [serverError, setServerError] = useState('')
  const [telegramBusy, setTelegramBusy] = useState(false)
  const registered = Boolean((useLocation().state as { registered?: boolean } | null)?.registered)
  const googleResult = new URLSearchParams(useLocation().search).get('google')

  const submit = handleSubmit(async (values) => {
    setServerError('')
    const parsed = loginSchema.safeParse(values)
    if (!parsed.success) {
      for (const issue of parsed.error.issues)
        setError(issue.path[0] as keyof LoginForm, { message: issue.message })
      return
    }
    try {
      await signIn(parsed.data.email, parsed.data.password)
      navigate('/', { replace: true })
    } catch (error) {
      setServerError(getApiError(error, 'Не удалось войти. Попробуйте ещё раз.'))
    }
  })

  const emailSignIn = <>
    <form onSubmit={submit} noValidate>
      <label>Email<input type="email" autoComplete="email" {...register('email')} /></label>
      {errors.email && <span className="field-error">{errors.email.message}</span>}
      <PasswordInput label="Пароль" autoComplete="current-password" {...register('password')} />
      {errors.password && <span className="field-error">{errors.password.message}</span>}
      <button className={isTelegram() ? 'ghost-button' : 'primary-button'} disabled={isSubmitting || telegramBusy || Boolean(telegramLaunchError())}>{isSubmitting ? 'Входим…' : isTelegram() ? 'Привязать аккаунт' : 'Войти'}</button>
    </form>
    <p><Link to="/forgot-password">Забыли пароль?</Link></p>
    <p><Link to="/verify-email">Подтвердить email или повторить отправку письма</Link></p>
  </>

  return (
    <AuthLayout title={isTelegram() ? 'Вход через Telegram' : 'С возвращением'} subtitle={isTelegram() ? 'Начните без почты и пароля или подключите прежний аккаунт.' : 'Введите данные, чтобы продолжить.'}
      alternateText="Ещё нет аккаунта?" alternateLink="/register" alternateLabel={isTelegram() ? '' : 'Создать'}>
      {registered && <div className="notice success">Аккаунт создан. Подтвердите email по ссылке из письма.</div>}
      {googleResult && <p className="notice error" role="alert">{googleResult === 'link_required'
        ? 'Для подключения Google сначала войдите в существующий аккаунт, затем откройте «Аккаунт» → «Подключить Google».'
        : 'Не удалось войти через Google. Попробуйте ещё раз.'}</p>}
      <GoogleSignIn />
      {isTelegram() && <button className="primary-button" disabled={telegramBusy || isSubmitting || Boolean(telegramLaunchError())} onClick={async () => {
        setTelegramBusy(true); setServerError('')
        try { await signInWithTelegram(); navigate('/', { replace: true }) }
        catch (error) { setServerError(getApiError(error, 'Не удалось войти через Telegram. Попробуйте ещё раз.')) }
        finally { setTelegramBusy(false) }
      }}>{telegramBusy ? 'Входим…' : 'Начать в Telegram'}</button>}
      {serverError && <div className="notice error">{serverError}</div>}
      {restoreError && <div className="notice error" role="alert">{restoreError}
        <button type="button" className="ghost-button" onClick={() => location.reload()}>Повторить вход</button></div>}
      {isTelegram() ? <details className="panel-disclosure"><summary>Подключить существующий аккаунт</summary>
        <p className="muted">Войдите в прежний аккаунт, чтобы открыть ту же историю.</p>{emailSignIn}
      </details> : emailSignIn}
    </AuthLayout>
  )
}
