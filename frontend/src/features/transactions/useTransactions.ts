import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as transactionApi from './transactionApi'

export type TransactionAction =
  | { kind: 'update'; transactionId: string; input: transactionApi.TransactionInput }
  | { kind: 'delete'; transactionId: string; version: string }

const transactionsQueryKey = ['transactions'] as const

export function useTransactions(query: transactionApi.TransactionQuery) {
  const queryClient = useQueryClient()
  const transactionsQuery = useQuery({
    queryKey: [...transactionsQueryKey, query],
    queryFn: ({ signal }) => transactionApi.getTransactions(query, signal),
  })

  const actionMutation = useMutation({
    mutationFn: async (action: TransactionAction) => {
      if (action.kind === 'update') {
        await transactionApi.updateTransaction(action.transactionId, action.input)
        return
      }
      await transactionApi.deleteTransaction(action.transactionId, action.version)
    },
    onSuccess: () => Promise.all([
      queryClient.invalidateQueries({ queryKey: transactionsQueryKey }),
      queryClient.invalidateQueries({ queryKey: ['dashboard'] }),
      queryClient.invalidateQueries({ queryKey: ['accounts'] }),
      queryClient.invalidateQueries({ queryKey: ['budgets'] }),
    ]),
    onError: () => queryClient.invalidateQueries({ queryKey: transactionsQueryKey }),
  })

  return {
    transactionsQuery,
    actionMutation,
    busyTransactionId: actionMutation.isPending
      ? actionMutation.variables?.transactionId
      : undefined,
  }
}
