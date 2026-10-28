import { createContext } from 'react'
import type { CurrentUser } from './authApi'

export type AuthStatus = 'loading' | 'authenticated' | 'anonymous'
export type AuthContextValue = {
  status: AuthStatus
  user: CurrentUser | null
  restoreError: string
  signIn(email: string, password: string): Promise<void>
  signInWithTelegram(): Promise<void>
  signOut(): Promise<void>
}

export const AuthContext = createContext<AuthContextValue | null>(null)
