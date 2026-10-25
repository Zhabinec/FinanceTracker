import { http } from '../../api/http'
import type { CategoryType } from '../categories/categoryApi'

export type FinanceTransaction = {
  id: string
  categoryId: string
  categoryName: string
  type: CategoryType
  amount: number
  description: string | null
  occurredOn: string
  createdAtUtc: string
}

export type TransactionInput = {
  categoryId: string
  type: CategoryType
  amount: string
  description: string | null
  occurredOn: string
}

export async function getTransactions() {
  const response = await http.get<FinanceTransaction[]>('/transactions')
  return response.data
}

export async function createTransaction(input: TransactionInput) {
  const response = await http.post<FinanceTransaction>('/transactions', input)
  return response.data
}

export async function updateTransaction(transactionId: string, input: TransactionInput) {
  const response = await http.put<FinanceTransaction>(`/transactions/${transactionId}`, input)
  return response.data
}

export async function deleteTransaction(transactionId: string) {
  await http.delete(`/transactions/${transactionId}`)
}
