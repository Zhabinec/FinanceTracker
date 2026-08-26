import { z } from 'zod'

export const categorySchema = z.object({
  name: z.string().trim().min(1, 'Введите название категории.').max(100, 'Не больше 100 символов.'),
  type: z.enum(['Income', 'Expense']),
})

export type CategoryForm = z.infer<typeof categorySchema>
