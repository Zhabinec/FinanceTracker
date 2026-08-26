import { http } from '../../api/http'

export type CategoryType = 'Income' | 'Expense'

export type Category = {
  id: string
  name: string
  type: CategoryType
  isActive: boolean
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

export async function renameCategory(categoryId: string, name: string) {
  const response = await http.patch<Category>(`/categories/${categoryId}`, { name })
  return response.data
}

export async function deactivateCategory(categoryId: string) {
  const response = await http.post<Category>(`/categories/${categoryId}/deactivate`)
  return response.data
}

export async function restoreCategory(categoryId: string) {
  const response = await http.post<Category>(`/categories/${categoryId}/restore`)
  return response.data
}
