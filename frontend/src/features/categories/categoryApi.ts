import { financeHttp as http } from '../privateFinance/financeHttp'

export type CategoryType = 'Income' | 'Expense'

export type Category = {
  id: string
  name: string
  type: CategoryType
  isActive: boolean
  version: string
}

export async function getCategories() {
  const response = await http.get<Category[]>('/categories', {
    params: { includeInactive: true },
  })
  return response.data
}

export async function createCategory(name: string, type: CategoryType) {
  const response = await http.post<Category>('/categories', { name, type })
  return response.data
}

export async function renameCategory(categoryId: string, name: string, version: string) {
  const response = await http.patch<Category>(`/categories/${categoryId}`, { name, version })
  return response.data
}

export async function deactivateCategory(categoryId: string, version: string) {
  const response = await http.post<Category>(`/categories/${categoryId}/deactivate`, { version })
  return response.data
}

export async function restoreCategory(categoryId: string, version: string) {
  const response = await http.post<Category>(`/categories/${categoryId}/restore`, { version })
  return response.data
}
