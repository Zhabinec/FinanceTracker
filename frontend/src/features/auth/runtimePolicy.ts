import axios from 'axios'
import { http } from '../../api/http'
import type { RuntimeOptions } from './runtimeOptions'

let request: Promise<RuntimeOptions> | null = null
// Shared by bootstrap and React StrictMode. Readiness retries run only while opening
// the app; this is not a timer that keeps the free backend alive in the background.
export function loadRuntimePolicy(): Promise<RuntimeOptions> {
  request ??= fetchPolicy().catch(error => { request = null; throw error })
  return request
}
async function fetchPolicy(): Promise<RuntimeOptions> {
  const deadline = Date.now() + 90_000
  for (let attempt = 0; ; attempt++) {
    try {
      const { data } = await http.get('/auth/options', { timeout: Math.min(10_000, Math.max(1, deadline - Date.now())) })
      // Render may return an HTML wake-up page with HTTP 200. Never interpret it
      // as a valid policy or render its contents inside the application.
      if (!data || typeof data !== 'object' || typeof data.emailRegistrationEnabled !== 'boolean')
        throw new Error('Backend is starting')
      return { telegramOnly: data.telegramOnly === true, managedFinancialKeys: data.managedFinancialKeys === true }
    } catch (error) {
      const status = axios.isAxiosError(error) ? error.response?.status : undefined
      if ((status !== undefined && ![408, 429, 502, 503, 504].includes(status)) ||
        navigator.onLine === false || Date.now() >= deadline) throw error
      await new Promise(resolve => setTimeout(resolve, Math.min(1000 + attempt * 500, 4000, deadline - Date.now())))
    }
  }
}
