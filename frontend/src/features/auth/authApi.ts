import axios from 'axios'
import { http, refreshAccessToken, revokeTelegramSession, setAccessToken, setTelegramRefreshToken } from '../../api/http'
import { isTelegram, telegramInitData, telegramLaunchError } from '../../platform/telegram'

export type CurrentUser = {
  id: string
  email: string | null
  registeredAtUtc: string
  baseCurrency: string
}

type TokenResponse = { accessToken: string; expiresAtUtc: string; refreshToken?: string; user?: CurrentUser }
let restoreRequest: Promise<CurrentUser> | null = null
let generation = 0
const ensureCurrent = (epoch: number) => { if (epoch !== generation) throw new axios.CanceledError('Аккаунт изменился.') }
export class TelegramLinkRequired extends Error {}

export async function login(email: string, password: string) {
  const epoch = ++generation
  if (telegramLaunchError()) throw new Error(telegramLaunchError())
  const { data } = await http.post<TokenResponse>(isTelegram() ? '/auth/telegram/link' : '/auth/login',
    { email, password, ...(isTelegram() ? { initData: telegramInitData() } : {}) })
  ensureCurrent(epoch)
  if (isTelegram()) setTelegramRefreshToken(data.refreshToken ?? null)
  setAccessToken(data.accessToken)
  const user = data.user ?? await getCurrentUser(); ensureCurrent(epoch); return user
}

export async function register(email: string, password: string) {
  await http.post('/auth/register', { email, password })
}

export async function registerTelegram() {
  const epoch = ++generation
  if (!isTelegram() || telegramLaunchError()) throw new Error(telegramLaunchError() || 'Откройте приложение через Telegram.')
  const { data } = await http.post<TokenResponse>('/auth/telegram/register', { initData: telegramInitData() })
  ensureCurrent(epoch)
  setTelegramRefreshToken(data.refreshToken ?? null)
  setAccessToken(data.accessToken)
  const user = data.user ?? await getCurrentUser(); ensureCurrent(epoch); return user
}

export function restoreSession() {
  restoreRequest ??= (async () => {
    const epoch = ++generation
    if (isTelegram()) {
      if (telegramLaunchError()) throw new Error(telegramLaunchError())
      const { data } = await axios.post<{ linkRequired: boolean; session: TokenResponse | null }>('/api/auth/telegram/login', { initData: telegramInitData() })
      ensureCurrent(epoch)
      if (!data.session) throw new TelegramLinkRequired('Link required')
      setTelegramRefreshToken(data.session.refreshToken ?? null)
      setAccessToken(data.session.accessToken)
      if (data.session.user) return data.session.user
    } else await refreshAccessToken()
    const user = await getCurrentUser(); ensureCurrent(epoch); return user
  })()
    .finally(() => { restoreRequest = null })

  return restoreRequest
}

export async function getCurrentUser() {
  const { data } = await http.get<CurrentUser>('/users/me')
  return data
}

export async function logout() {
  const epoch = ++generation
  try { if (isTelegram()) await revokeTelegramSession(); else await http.post('/auth/logout') }
  finally { if (epoch === generation) { setAccessToken(null); setTelegramRefreshToken(null) } }
}
