import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as transactionApi from './transactionApi'

export type TransactionAction =
  | { kind: 'update'; transactionId: string; input: transactionApi.TransactionInput }
  | { kind: 'delete'; transactionId: string }

const transactionsQueryKey = ['transactions'] as const

export function useTransactions() {
  const queryClient = useQueryClient()
  const transactionsQuery = useQuery({
    queryKey: transactionsQueryKey,
    queryFn: transactionApi.getTransactions,
  })

  const createMutation = useMutation({
    mutationFn: transactionApi.createTransaction,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: transactionsQueryKey }),
  })

  const actionMutation = useMutation({
    mutationFn: async (action: TransactionAction) => {
      if (action.kind === 'update') {
        await transactionApi.updateTransaction(action.transactionId, action.input)
        return
      }
      await transactionApi.deleteTransaction(action.transactionId)
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: transactionsQueryKey }),
  })

  return {
    transactionsQuery,
    createMutation,
    actionMutation,
    busyTransactionId: actionMutation.isPending
      ? actionMutation.variables?.transactionId
      : undefined,
  }
}
