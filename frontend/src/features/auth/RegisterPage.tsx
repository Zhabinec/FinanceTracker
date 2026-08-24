import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router-dom'
import { AuthLayout } from './AuthLayout'
import { register as registerUser } from './authApi'
import { getApiError } from './apiError'
import { registerSchema, type RegisterForm } from './validation'

export function RegisterPage() {
  const { register, handleSubmit, setError, formState: { errors, isSubmitting } } = useForm<RegisterForm>({
    defaultValues: { email: '', password: '', baseCurrency: 'RUB' },
  })
  const navigate = useNavigate()
  const [serverError, setServerError] = useState('')

  const submit = handleSubmit(async (values) => {
    setServerError('')
    const parsed = registerSchema.safeParse(values)
    if (!parsed.success) {
      for (const issue of parsed.error.issues)
        setError(issue.path[0] as keyof RegisterForm, { message: issue.message })
      return
    }
    try {
      await registerUser(parsed.data.email, parsed.data.password, parsed.data.baseCurrency)
      navigate('/login', { replace: true, state: { registered: true, email: parsed.data.email } })
    } catch (error) {
      setServerError(getApiError(error, 'Не удалось создать аккаунт.'))
    }
  })

  return (
    <AuthLayout title="Создать аккаунт" subtitle="Начните вести финансы осознанно."
      alternateText="Уже есть аккаунт?" alternateLink="/login" alternateLabel="Войти">
      {serverError && <div className="notice error">{serverError}</div>}
      <form onSubmit={submit} noValidate>
        <label>Email<input type="email" autoComplete="email" {...register('email')} /></label>
        {errors.email && <span className="field-error">{errors.email.message}</span>}
        <label>Пароль<input type="password" autoComplete="new-password" {...register('password')} /></label>
        {errors.password && <span className="field-error">{errors.password.message}</span>}
        <label>Базовая валюта<select {...register('baseCurrency')}><option>RUB</option><option>USD</option><option>EUR</option></select></label>
        {errors.baseCurrency && <span className="field-error">{errors.baseCurrency.message}</span>}
        <button className="primary-button" disabled={isSubmitting}>{isSubmitting ? 'Создаём…' : 'Зарегистрироваться'}</button>
      </form>
    </AuthLayout>
  )
}
