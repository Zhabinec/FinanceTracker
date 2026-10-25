import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { useLocation, useNavigate } from 'react-router-dom'
import { Link } from 'react-router-dom'
import { PasswordInput } from '../../shared/PasswordInput'
import { AuthLayout } from './AuthLayout'
import { useAuth } from './useAuth'
import { getApiError } from '../../api/apiError'
import { loginSchema, type LoginForm } from './validation'

export function LoginPage() {
  const { register, handleSubmit, setError, formState: { errors, isSubmitting } } = useForm<LoginForm>({
    defaultValues: { email: (useLocation().state as { email?: string } | null)?.email ?? '', password: '' },
  })
  const { signIn } = useAuth()
  const navigate = useNavigate()
  const [serverError, setServerError] = useState('')
  const registered = Boolean((useLocation().state as { registered?: boolean } | null)?.registered)

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

  return (
    <AuthLayout title="С возвращением" subtitle="Введите данные, чтобы продолжить."
      alternateText="Ещё нет аккаунта?" alternateLink="/register" alternateLabel="Создать">
      {registered && <div className="notice success">Аккаунт создан. Подтвердите email по ссылке из письма.</div>}
      {serverError && <div className="notice error">{serverError}</div>}
      <form onSubmit={submit} noValidate>
        <label>Email<input type="email" autoComplete="email" {...register('email')} /></label>
        {errors.email && <span className="field-error">{errors.email.message}</span>}
        <PasswordInput label="Пароль" autoComplete="current-password" {...register('password')} />
        {errors.password && <span className="field-error">{errors.password.message}</span>}
        <button className="primary-button" disabled={isSubmitting}>{isSubmitting ? 'Входим…' : 'Войти'}</button>
      </form>
      <p><Link to="/forgot-password">Забыли пароль?</Link></p>
      <p><Link to="/verify-email">Подтвердить email или повторить отправку письма</Link></p>
    </AuthLayout>
  )
}
