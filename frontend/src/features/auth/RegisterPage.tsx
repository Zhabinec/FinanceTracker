import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router-dom'
import { AuthLayout } from './AuthLayout'
import { register as registerUser } from './authApi'
import { getApiError } from '../../api/apiError'
import { PasswordInput } from '../../shared/PasswordInput'
import { LocalMailHint } from './LocalMailHint'
import { registerSchema, type RegisterForm } from './validation'
import { GoogleSignIn } from './GoogleSignIn'

export function RegisterPage() {
  const { register, handleSubmit, setError, formState: { errors, isSubmitting } } = useForm<RegisterForm>({
    defaultValues: { email: '', password: '' },
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
      await registerUser(parsed.data.email, parsed.data.password)
      navigate('/verify-email', { replace: true, state: { registered: true, email: parsed.data.email } })
    } catch (error) {
      setServerError(getApiError(error, 'Не удалось создать аккаунт.'))
    }
  })

  return (
    <AuthLayout title="Создать аккаунт" subtitle="Начните вести финансы осознанно."
      alternateText="Уже есть аккаунт?" alternateLink="/login" alternateLabel="Войти">
      {serverError && <div className="notice error">{serverError}</div>}
      <GoogleSignIn />
      <form onSubmit={submit} noValidate>
        <label>Email<input type="email" autoComplete="email" {...register('email')} /></label>
        {errors.email && <span className="field-error">{errors.email.message}</span>}
        <PasswordInput label="Пароль" autoComplete="new-password" {...register('password')} />
        {errors.password && <span className="field-error">{errors.password.message}</span>}
        <button className="primary-button" disabled={isSubmitting}>{isSubmitting ? 'Создаём…' : 'Зарегистрироваться'}</button>
      </form>
      <LocalMailHint />
    </AuthLayout>
  )
}
