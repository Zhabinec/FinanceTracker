import axios, { AxiosError, CanceledError, type InternalAxiosRequestConfig } from 'axios'
import { isTelegram } from '../platform/telegram'

type TokenResponse = { accessToken: string; expiresAtUtc: string; refreshToken?: string }

let accessToken: string | null = null
let refreshRequest: Promise<string> | null = null
let sessionExpiredHandler: (() => void) | null = null
let telegramRefreshToken: string | null = null
let owner: string | null = null
let sessionEpoch = 0
type OwnedRequest = InternalAxiosRequestConfig & { retried?: boolean; sessionEpoch?: number }
export function setSessionOwner(value: string | null) {
  if (owner === value) return
  owner = value; sessionEpoch++; refreshRequest = null
}

export function setTelegramRefreshToken(token: string | null) { telegramRefreshToken = token }

export async function refreshAccessToken() {
  const epoch = sessionEpoch
  const telegram = isTelegram()
  if (telegram && !telegramRefreshToken) throw new Error('Telegram session is unavailable')
  const { data } = await axios.post<TokenResponse>(telegram ? '/api/auth/telegram/refresh' : '/api/auth/refresh',
    telegram ? { refreshToken: telegramRefreshToken } : {}, { withCredentials: !telegram })
  if (epoch !== sessionEpoch) throw new CanceledError('Аккаунт изменился.')
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
  (config as OwnedRequest).sessionEpoch = sessionEpoch
  if (accessToken) config.headers.Authorization = `Bearer ${accessToken}`
  return config
})

http.interceptors.response.use(response => {
  if (!response.config.url?.includes('/auth/') && (response.config as OwnedRequest).sessionEpoch !== sessionEpoch)
    throw new CanceledError('Аккаунт изменился.')
  return response
}, async (error: AxiosError) => {
  const config = error.config as OwnedRequest | undefined
  const isAuthEndpoint = config?.url?.includes('/auth/')
  if (config && !isAuthEndpoint && config.sessionEpoch !== sessionEpoch) return Promise.reject(new CanceledError('Аккаунт изменился.'))

  if (error.response?.status !== 401 || !config || config.retried || isAuthEndpoint)
    return Promise.reject(error)

  config.retried = true
  const epoch = sessionEpoch
  refreshRequest ??= refreshAccessToken()
    .finally(() => { if (epoch === sessionEpoch) refreshRequest = null })

  try {
    const token = await refreshRequest
    if (epoch !== sessionEpoch) throw new CanceledError('Аккаунт изменился.')
    config.headers.Authorization = `Bearer ${token}`
    return http(config)
  } catch (refreshError) {
    if (epoch !== sessionEpoch) return Promise.reject(refreshError)
    setAccessToken(null)
    sessionExpiredHandler?.()
    return Promise.reject(refreshError)
  }
})
