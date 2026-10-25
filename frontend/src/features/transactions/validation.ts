import { z } from 'zod'
import type { TransactionInput } from './transactionApi'

const moneyPattern = /^\d+(?:[.,]\d{1,2})?$/

function fitsDatabaseAmount(value: string) {
  const integerPart = value.replace(',', '.').split('.')[0].replace(/^0+/, '') || '0'
  return integerPart.length <= 16
}

export const transactionSchema = z.object({
  accountId: z.string().min(1, 'Выберите счёт.'),
  categoryId: z.string().min(1, 'Выберите категорию.'),
  type: z.enum(['Income', 'Expense']),
  amount: z.string()
    .trim()
    .min(1, 'Введите сумму.')
    .refine((value) => moneyPattern.test(value), 'Введите положительную сумму, не больше двух знаков после запятой.')
    .refine((value) => Number(value.replace(',', '.')) > 0, 'Сумма должна быть больше нуля.')
    .refine(fitsDatabaseAmount, 'Сумма слишком велика.'),
  description: z.string().trim().max(500, 'Не больше 500 символов.'),
  occurredOn: z.string().regex(/^\d{4}-\d{2}-\d{2}$/, 'Выберите дату операции.'),
})

export type TransactionForm = z.infer<typeof transactionSchema>

export function toTransactionInput(form: TransactionForm): TransactionInput {
  return {
    accountId: form.accountId,
    categoryId: form.categoryId,
    type: form.type,
    amount: form.amount.replace(',', '.'),
    description: form.description || null,
    occurredOn: form.occurredOn,
  }
}

export function todayInMoscow() {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Europe/Moscow',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(new Date())
  const part = (type: Intl.DateTimeFormatPartTypes) =>
    parts.find((item) => item.type === type)?.value ?? ''
  return `${part('year')}-${part('month')}-${part('day')}`
}
