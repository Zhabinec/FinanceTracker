import { createContext, useContext } from 'react'
export type RuntimeOptions = { telegramOnly: boolean; managedFinancialKeys: boolean }
export const RuntimeOptionsContext = createContext<RuntimeOptions | null>(null)
// Kept outside QueryClient: clearing financial/account caches must not reset runtime policy.
export function useRuntimeOptions() {
  const options = useContext(RuntimeOptionsContext)
  if (!options) throw new Error('RuntimeOptionsProvider is missing')
  return options
}
