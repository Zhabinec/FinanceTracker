import { z } from 'zod'

export const loginSchema = z.object({
  email: z.email('Введите корректный email.'),
  password: z.string().min(1, 'Введите пароль.'),
})

export const registerSchema = loginSchema.extend({
  password: z.string().min(8, 'Пароль должен содержать минимум 8 символов.'),
  baseCurrency: z.enum(['RUB', 'USD', 'EUR']),
})

export type LoginForm = z.infer<typeof loginSchema>
export type RegisterForm = z.infer<typeof registerSchema>
