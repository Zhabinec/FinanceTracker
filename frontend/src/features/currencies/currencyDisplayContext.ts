import { createContext, useContext } from 'react'

export type DisplayCurrency = 'RUB' | 'USD' | 'EUR'
export type CurrencyDisplay = {
  currency: DisplayCurrency; setCurrency: (currency: DisplayCurrency) => void
  money: (amount: string, sourceCurrency: string) => string
  rateDate?: string; pending: boolean; error: boolean; retry: () => void
}
export const CurrencyDisplayContext = createContext<CurrencyDisplay | null>(null)
export function useCurrencyDisplay() {
  const value = useContext(CurrencyDisplayContext)
  if (!value) throw new Error('CurrencyDisplayProvider is missing')
  return value
}
