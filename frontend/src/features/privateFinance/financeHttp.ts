import { AxiosError, AxiosHeaders } from 'axios'
import { executeFinance, FinanceError } from './ledger'
import { financialDocument, mutateFinancial, refreshVault, vaultSnapshot } from './session'

type Options = { params?: unknown; signal?: AbortSignal }
async function request<T>(method: string, url: string, data: unknown, options?: Options): Promise<{ data: T }> {
  try {
    const owner = vaultSnapshot().userId
    options?.signal?.throwIfAborted()
    const read = method === 'get' || url === '/imports/csv/preview'
    let result: unknown
    if (read) {
      await refreshVault(); options?.signal?.throwIfAborted()
      if (!owner || vaultSnapshot().userId !== owner) throw new Error('Аккаунт изменился. Повторите запрос.')
      result = await executeFinance(financialDocument(), method, url, data, options?.params, vaultSnapshot().userId)
    } else result = await mutateFinancial(JSON.stringify([method, url, data, options?.params]), draft => executeFinance(draft, method, url, data, options?.params, vaultSnapshot().userId))
    return { data: structuredClone(result) as T }
  } catch (error) {
    if (error instanceof AxiosError || (error instanceof DOMException && error.name === 'AbortError')) throw error
    const title = error instanceof Error ? error.message : 'Не удалось обработать финансовую запись.'
    const config = { url, headers: new AxiosHeaders() }
    throw new AxiosError(title, undefined, config, undefined, { config, data: { title }, status: error instanceof FinanceError ? error.status : 409, statusText: '', headers: {} })
  }
}
export const financeHttp = {
  get: <T = unknown>(url: string, options?: Options) => request<T>('get', url, undefined, options),
  post: <T = unknown>(url: string, data?: unknown) => request<T>('post', url, data),
  put: <T = unknown>(url: string, data?: unknown) => request<T>('put', url, data),
  patch: <T = unknown>(url: string, data?: unknown) => request<T>('patch', url, data),
  delete: <T = unknown>(url: string, options?: Options) => request<T>('delete', url, undefined, options),
}
