import axios, { AxiosError, type InternalAxiosRequestConfig } from 'axios'
import { isTelegram } from '../platform/telegram'

type TokenResponse = { accessToken: string; expiresAtUtc: string; refreshToken?: string }

let accessToken: string | null = null
let refreshRequest: Promise<string> | null = null
let sessionExpiredHandler: (() => void) | null = null
let telegramRefreshToken: string | null = null

export function setTelegramRefreshToken(token: string | null) { telegramRefreshToken = token }

export async function refreshAccessToken() {
  const telegram = isTelegram()
  if (telegram && !telegramRefreshToken) throw new Error('Telegram session is unavailable')
  const { data } = await axios.post<TokenResponse>(telegram ? '/api/auth/telegram/refresh' : '/api/auth/refresh',
    telegram ? { refreshToken: telegramRefreshToken } : {}, { withCredentials: !telegram })
  if (telegram) telegramRefreshToken = data.refreshToken ?? null
  setAccessToken(data.accessToken)
  return data.accessToken
}

export async function revokeTelegramSession() {
  const refreshToken = telegramRefreshToken
  telegramRefreshToken = null
  if (refreshToken) await axios.post('/api/auth/telegram/logout', { refreshToken })
}

export const http = axios.create({ baseURL: '/api', withCredentials: true })

export function setAccessToken(token: string | null) {
  accessToken = token
}

export function setSessionExpiredHandler(handler: (() => void) | null) {
  sessionExpiredHandler = handler
}

http.interceptors.request.use((config) => {
  if (accessToken) config.headers.Authorization = `Bearer ${accessToken}`
  return config
})

http.interceptors.response.use(undefined, async (error: AxiosError) => {
  const config = error.config as (InternalAxiosRequestConfig & { retried?: boolean }) | undefined
  const isAuthEndpoint = config?.url?.includes('/auth/')

  if (error.response?.status !== 401 || !config || config.retried || isAuthEndpoint)
    return Promise.reject(error)

  config.retried = true
  refreshRequest ??= refreshAccessToken()
    .finally(() => { refreshRequest = null })

  try {
    const token = await refreshRequest
    config.headers.Authorization = `Bearer ${token}`
    return http(config)
  } catch (refreshError) {
    setAccessToken(null)
    sessionExpiredHandler?.()
    return Promise.reject(refreshError)
  }
})
