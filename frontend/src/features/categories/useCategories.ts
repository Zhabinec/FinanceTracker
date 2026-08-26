import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as categoryApi from './categoryApi'
import type { CategoryForm } from './validation'

export type CategoryAction =
  | { kind: 'rename'; categoryId: string; name: string }
  | { kind: 'deactivate'; categoryId: string }
  | { kind: 'restore'; categoryId: string }

const categoriesQueryKey = ['categories'] as const

export function useCategories() {
  const queryClient = useQueryClient()
  const categoriesQuery = useQuery({
    queryKey: categoriesQueryKey,
    queryFn: categoryApi.getCategories,
  })

  const createMutation = useMutation({
    mutationFn: (values: CategoryForm) =>
      categoryApi.createCategory(values.name, values.type),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: categoriesQueryKey }),
  })

  const actionMutation = useMutation({
    mutationFn: (action: CategoryAction) => {
      if (action.kind === 'rename')
        return categoryApi.renameCategory(action.categoryId, action.name)
      if (action.kind === 'deactivate')
        return categoryApi.deactivateCategory(action.categoryId)
      return categoryApi.restoreCategory(action.categoryId)
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: categoriesQueryKey }),
  })

  return {
    categoriesQuery,
    createMutation,
    actionMutation,
    busyCategoryId: actionMutation.isPending
      ? actionMutation.variables?.categoryId
      : undefined,
  }
}
