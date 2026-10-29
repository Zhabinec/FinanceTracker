import { http } from '../../api/http'
import type { CategoryType } from '../categories/categoryApi'

export type FinanceTransaction = {
  id: string
  accountId: string
  categoryId: string
  categoryName: string
  type: CategoryType
  amount: string
  description: string | null
  occurredOn: string
  createdAtUtc: string
  version: string
}

export type TransactionInput = {
  requestId?: string
  accountId?: string
  categoryId: string
  type: CategoryType
  amount: string
  description: string | null
  occurredOn?: string
  version?: string
}

export type TransactionQuery = {
  from?: string; to?: string; categoryId?: string; accountId?: string; type?: CategoryType | ''
  sort?: string; page: number; pageSize: number
}
export type TransactionPage = { items: FinanceTransaction[]; totalCount: number; page: number; pageSize: number }

export async function getTransactions(query: TransactionQuery, signal?: AbortSignal) {
  const response = await http.get<TransactionPage>('/finance/transactions', { params: query, signal })
  return response.data
}

export async function createTransaction(input: TransactionInput) {
  const response = await http.post<FinanceTransaction>('/finance/transactions', input)
  return response.data
}

export async function updateTransaction(transactionId: string, input: TransactionInput) {
  const response = await http.put<FinanceTransaction>(`/finance/transactions/${transactionId}`, input)
  return response.data
}

export async function deleteTransaction(transactionId: string, version: string) {
  await http.delete(`/finance/transactions/${transactionId}`, { params: { version } })
}
