import { useState, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { http } from '../../api/http'
import { formatMoney } from '../../shared/formatting'
import { useAuth } from '../auth/useAuth'
import { CurrencyDisplayContext, type DisplayCurrency } from './currencyDisplayContext'
import { convertMoney } from './conversion'

type Rates = { date: string; source: string; rublesPerUnit: Record<string, string> }
const storageKey = 'finance.displayCurrency'
function initialCurrency(): DisplayCurrency {
  try { const stored = localStorage.getItem(storageKey); if (stored === 'USD' || stored === 'EUR') return stored }
  catch { /* Display preferences are optional when browser storage is unavailable. */ }
  return 'RUB'
}

export function CurrencyDisplayProvider({ children }: { children: ReactNode }) {
  const { user, status } = useAuth()
  const [currency, changeCurrency] = useState<DisplayCurrency>(initialCurrency)
  const needsRate = Boolean(user && user.baseCurrency !== currency)
  const query = useQuery({
    queryKey: ['display-exchange-rates'],
    queryFn: async ({ signal }) => (await http.get<Rates>('/currencies/rates', { signal })).data,
    enabled: status === 'authenticated' && needsRate,
    staleTime: 15 * 60 * 1000,
    refetchInterval: needsRate ? 60 * 60 * 1000 : false,
  })
  const sourceSupported = !user || ['RUB', 'USD', 'EUR'].includes(user.baseCurrency)
  // Never relabel unconverted money if rates are loading or unavailable.
  const usableRates = !query.isError && query.data ? query.data : undefined
  const money = (amount: string, sourceCurrency: string) => {
    if (sourceCurrency === currency) return formatMoney(amount, sourceCurrency)
    const sourceRate = usableRates?.rublesPerUnit[sourceCurrency]
    const targetRate = usableRates?.rublesPerUnit[currency]
    return sourceRate && targetRate
      ? `≈ ${formatMoney(convertMoney(amount, sourceRate, targetRate), currency)}`
      : formatMoney(amount, sourceCurrency)
  }
  return <CurrencyDisplayContext.Provider value={{
    currency, setCurrency: next => {
      changeCurrency(next)
      try { localStorage.setItem(storageKey, next) } catch { /* The in-memory preference still works. */ }
    },
    money, rateDate: needsRate && usableRates ? usableRates.date : undefined,
    pending: needsRate && query.isFetching && !usableRates,
    error: needsRate && (query.isError || !sourceSupported), retry: () => { void query.refetch() },
  }}>{children}</CurrencyDisplayContext.Provider>
}
