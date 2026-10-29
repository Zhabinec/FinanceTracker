import { http } from '../../api/http'

export type CategoryType = 'Income' | 'Expense'

export type Category = {
  id: string
  name: string
  type: CategoryType
  isActive: boolean
  version: string
}

export async function getCategories() {
  const response = await http.get<Category[]>('/finance/categories', {
    params: { includeInactive: true },
  })
  return response.data
}

export async function createCategory(name: string, type: CategoryType) {
  const response = await http.post<Category>('/finance/categories', { name, type })
  return response.data
}

export async function renameCategory(categoryId: string, name: string, version: string) {
  const response = await http.patch<Category>(`/finance/categories/${categoryId}`, { name, version })
  return response.data
}

export async function deactivateCategory(categoryId: string, version: string) {
  const response = await http.post<Category>(`/finance/categories/${categoryId}/deactivate`, { version })
  return response.data
}

export async function restoreCategory(categoryId: string, version: string) {
  const response = await http.post<Category>(`/finance/categories/${categoryId}/restore`, { version })
  return response.data
}
