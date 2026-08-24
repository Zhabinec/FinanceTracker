import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import * as authApi from './authApi'
import type { CurrentUser } from './authApi'
import { AuthContext, type AuthContextValue, type AuthStatus } from './authContext'

export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<AuthStatus>('loading')
  const [user, setUser] = useState<CurrentUser | null>(null)
  const queryClient = useQueryClient()

  useEffect(() => {
    let active = true
    authApi.restoreSession()
      .then((currentUser) => {
        if (!active) return
        setUser(currentUser)
        setStatus('authenticated')
      })
      .catch(() => { if (active) setStatus('anonymous') })
    return () => { active = false }
  }, [])

  const value = useMemo<AuthContextValue>(() => ({
    status,
    user,
    async signIn(email, password) {
      const currentUser = await authApi.login(email, password)
      setUser(currentUser)
      setStatus('authenticated')
    },
    async signOut() {
      await authApi.logout()
      queryClient.clear()
      setUser(null)
      setStatus('anonymous')
    },
  }), [queryClient, status, user])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
