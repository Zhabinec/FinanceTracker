import { useQuery } from '@tanstack/react-query'
import { financeHttp as http } from '../privateFinance/financeHttp'

export type MoneyAccount = {
  id: string; name: string; currency: string; openingBalance: string; balance: string; isArchived: boolean; version: string
}

export function useAccounts() {
  return useQuery({
    queryKey: ['accounts'],
    queryFn: async ({ signal }) => (await http.get<MoneyAccount[]>('/accounts', { signal })).data,
  })
}
