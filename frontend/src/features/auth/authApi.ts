import axios from 'axios'
import { http, setAccessToken } from '../../api/http'

export type CurrentUser = {
  id: string
  email: string
  registeredAtUtc: string
  baseCurrency: string
}

type TokenResponse = { accessToken: string; expiresAtUtc: string }
let restoreRequest: Promise<CurrentUser> | null = null

export async function login(email: string, password: string) {
  const { data } = await http.post<TokenResponse>('/auth/login', { email, password })
  setAccessToken(data.accessToken)
  return getCurrentUser()
}

export async function register(email: string, password: string, baseCurrency: string) {
  await http.post('/auth/register', { email, password, baseCurrency })
}

export function restoreSession() {
  restoreRequest ??= axios.post<TokenResponse>('/api/auth/refresh', {}, { withCredentials: true })
    .then(({ data }) => {
      setAccessToken(data.accessToken)
      return getCurrentUser()
    })
    .finally(() => { restoreRequest = null })

  return restoreRequest
}

export async function getCurrentUser() {
  const { data } = await http.get<CurrentUser>('/users/me')
  return data
}

export async function logout() {
  try { await http.post('/auth/logout') }
  finally { setAccessToken(null) }
}
