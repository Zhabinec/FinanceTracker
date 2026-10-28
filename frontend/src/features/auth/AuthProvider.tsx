import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import * as authApi from './authApi'
import type { CurrentUser } from './authApi'
import { AuthContext, type AuthContextValue, type AuthStatus } from './authContext'
import { setSessionExpiredHandler, setSessionOwner } from '../../api/http'
import { isTelegram, telegramLaunchError } from '../../platform/telegram'
import { getApiError } from '../../api/apiError'

export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<AuthStatus>('loading')
  const [user, setUser] = useState<CurrentUser | null>(null)
  const [restoreError, setRestoreError] = useState('')
  const queryClient = useQueryClient()
  const operation = useRef(0)

  useEffect(() => {
    setSessionExpiredHandler(() => {
      operation.current++; setSessionOwner(null)
      queryClient.clear()
      setUser(null)
      setStatus('anonymous')
    })

    return () => setSessionExpiredHandler(null)
  }, [queryClient])

  useEffect(() => {
    let active = true
    const epoch = ++operation.current
    authApi.restoreSession()
      .then((currentUser) => {
        if (!active || epoch !== operation.current) return
        setSessionOwner(currentUser.id)
        setUser(currentUser)
        setStatus('authenticated')
      })
      .catch((error: unknown) => {
        if (!active || epoch !== operation.current) return
        if (isTelegram() && !(error instanceof authApi.TelegramLinkRequired))
          setRestoreError(telegramLaunchError() || getApiError(error, 'Не удалось войти через Telegram. Попробуйте ещё раз.'))
        setStatus('anonymous')
      })
    return () => { active = false }
  }, [])

  const value = useMemo<AuthContextValue>(() => ({
    status,
    user,
    restoreError,
    async signInWithTelegram() {
      const epoch = ++operation.current
      setSessionOwner(null)
      queryClient.clear()
      const currentUser = await authApi.registerTelegram()
      if (epoch !== operation.current) return
      setSessionOwner(currentUser.id)
      setUser(currentUser)
      setRestoreError('')
      setStatus('authenticated')
    },
    async signIn(email, password) {
      const epoch = ++operation.current
      setSessionOwner(null); queryClient.clear()
      const currentUser = await authApi.login(email, password)
      if (epoch !== operation.current) return
      setSessionOwner(currentUser.id)
      setUser(currentUser)
      setRestoreError('')
      setStatus('authenticated')
    },
    async signOut() {
      const epoch = ++operation.current
      setSessionOwner(null)
      queryClient.clear()
      setUser(null)
      setRestoreError('')
      setStatus('anonymous')
      try {
        await authApi.logout()
      } catch {
        // Local logout must still complete when the API cannot revoke the session.
      } finally {
        if (epoch === operation.current) {
          setSessionOwner(null); queryClient.clear(); setUser(null); setRestoreError(''); setStatus('anonymous')
        }
      }
    },
  }), [queryClient, status, user, restoreError])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
