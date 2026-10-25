import { transactionSchema } from './validation.ts'
import type { MoneyAccount } from '../accounts/accountApi'

export const quickEntrySchema = transactionSchema.pick({ amount: true, description: true }).extend({
  description: transactionSchema.shape.description.min(1, 'Введите название операции.'),
})

export function chooseQuickAccount(accounts: MoneyAccount[], preferredId: string | null, userId: string) {
  const active = accounts.filter(account => !account.isArchived)
  return active.find(account => account.id === preferredId) ?? active.find(account => account.id === userId) ?? active[0]
}
