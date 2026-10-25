import { z } from 'zod'
import { transactionSchema } from '../transactions/validation.ts'

export const transferSchema = z.object({
  sourceAccountId: z.string().min(1, 'Выберите счёт списания.'),
  targetAccountId: z.string().min(1, 'Выберите счёт зачисления.'),
  amount: transactionSchema.shape.amount,
  description: z.string().trim().max(500, 'Не больше 500 символов.'),
}).refine(input => input.sourceAccountId !== input.targetAccountId, { message: 'Выберите два разных счёта.', path: ['targetAccountId'] })
