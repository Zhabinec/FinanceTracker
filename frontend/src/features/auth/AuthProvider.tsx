import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import * as authApi from './authApi'
import type { CurrentUser } from './authApi'
import { AuthContext, type AuthContextValue, type AuthStatus } from './authContext'
import { setSessionExpiredHandler } from '../../api/http'
import { isTelegram, telegramLaunchError } from '../../platform/telegram'
import { getApiError } from '../../api/apiError'
import { lockVault } from '../privateFinance/session'

export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<AuthStatus>('loading')
  const [user, setUser] = useState<CurrentUser | null>(null)
  const [restoreError, setRestoreError] = useState('')
  const queryClient = useQueryClient()

  useEffect(() => {
    setSessionExpiredHandler(() => {
      lockVault()
      queryClient.clear()
      setUser(null)
      setStatus('anonymous')
    })

    return () => setSessionExpiredHandler(null)
  }, [queryClient])

  useEffect(() => {
    let active = true
    authApi.restoreSession()
      .then((currentUser) => {
        if (!active) return
        setUser(currentUser)
        setStatus('authenticated')
      })
      .catch((error: unknown) => {
        if (!active) return
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
      lockVault()
      queryClient.clear()
      const currentUser = await authApi.registerTelegram()
      setUser(currentUser)
      setRestoreError('')
      setStatus('authenticated')
    },
    async signIn(email, password) {
      lockVault()
      const currentUser = await authApi.login(email, password)
      setUser(currentUser)
      setRestoreError('')
      setStatus('authenticated')
    },
    async signOut() {
      lockVault()
      queryClient.clear()
      setUser(null)
      setRestoreError('')
      setStatus('anonymous')
      try {
        await authApi.logout()
      } catch {
        // Local logout must still complete when the API cannot revoke the session.
      } finally {
        lockVault()
        queryClient.clear()
        setUser(null)
        setRestoreError('')
        setStatus('anonymous')
      }
    },
  }), [queryClient, status, user, restoreError])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
