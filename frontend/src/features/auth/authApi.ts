import axios from 'axios'
import { http, refreshAccessToken, revokeTelegramSession, setAccessToken, setTelegramRefreshToken } from '../../api/http'
import { isTelegram, telegramInitData, telegramLaunchError } from '../../platform/telegram'

export type CurrentUser = {
  id: string
  email: string | null
  registeredAtUtc: string
  baseCurrency: string
}

type TokenResponse = { accessToken: string; expiresAtUtc: string; refreshToken?: string }
let restoreRequest: Promise<CurrentUser> | null = null
export class TelegramLinkRequired extends Error {}

export async function login(email: string, password: string) {
  if (telegramLaunchError()) throw new Error(telegramLaunchError())
  const { data } = await http.post<TokenResponse>(isTelegram() ? '/auth/telegram/link' : '/auth/login',
    { email, password, ...(isTelegram() ? { initData: telegramInitData() } : {}) })
  if (isTelegram()) setTelegramRefreshToken(data.refreshToken ?? null)
  setAccessToken(data.accessToken)
  return getCurrentUser()
}

export async function register(email: string, password: string) {
  await http.post('/auth/register', { email, password })
}

export async function registerTelegram() {
  if (!isTelegram() || telegramLaunchError()) throw new Error(telegramLaunchError() || 'Откройте приложение через Telegram.')
  const { data } = await http.post<TokenResponse>('/auth/telegram/register', { initData: telegramInitData() })
  setTelegramRefreshToken(data.refreshToken ?? null)
  setAccessToken(data.accessToken)
  return getCurrentUser()
}

export function restoreSession() {
  restoreRequest ??= (async () => {
    if (isTelegram()) {
      if (telegramLaunchError()) throw new Error(telegramLaunchError())
      const { data } = await axios.post<{ linkRequired: boolean; session: TokenResponse | null }>('/api/auth/telegram/login', { initData: telegramInitData() })
      if (!data.session) throw new TelegramLinkRequired('Link required')
      setTelegramRefreshToken(data.session.refreshToken ?? null)
      setAccessToken(data.session.accessToken)
    } else await refreshAccessToken()
    return getCurrentUser()
  })()
    .finally(() => { restoreRequest = null })

  return restoreRequest
}

export async function getCurrentUser() {
  const { data } = await http.get<CurrentUser>('/users/me')
  return data
}

export async function logout() {
  try { if (isTelegram()) await revokeTelegramSession(); else await http.post('/auth/logout') }
  finally { setAccessToken(null); setTelegramRefreshToken(null) }
}
