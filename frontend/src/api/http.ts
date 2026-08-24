import axios, { AxiosError, type InternalAxiosRequestConfig } from 'axios'

type TokenResponse = { accessToken: string; expiresAtUtc: string }

let accessToken: string | null = null
let refreshRequest: Promise<string> | null = null

export const http = axios.create({ baseURL: '/api', withCredentials: true })

export function setAccessToken(token: string | null) {
  accessToken = token
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
  refreshRequest ??= axios.post<TokenResponse>('/api/auth/refresh', {}, { withCredentials: true })
    .then(({ data }) => {
      setAccessToken(data.accessToken)
      return data.accessToken
    })
    .finally(() => { refreshRequest = null })

  const token = await refreshRequest
  config.headers.Authorization = `Bearer ${token}`
  return http(config)
})
