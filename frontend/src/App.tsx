import { Navigate, Route, Routes, useLocation } from 'react-router-dom'
import { lazy, Suspense, useEffect } from 'react'
import { preventRepeatedPaste } from './shared/pasteShortcut'
import { StartupScreen } from './shared/StartupScreen'
import { useAuth } from './features/auth/useAuth'
import './App.css'
import './motion.css'
import { AppShell } from './shared/AppShell'
import { FinancialWorkspaceGate } from './features/finance/FinancialWorkspaceGate'
import { useRuntimeOptions } from './features/auth/runtimeOptions'
import { isTelegram } from './platform/telegram'
import { AuthLayout } from './features/auth/AuthLayout'
import { TelegramEntry } from './features/auth/TelegramEntry'

const AccountPage = lazy(() => import('./features/auth/AccountPage').then(module => ({ default: module.AccountPage })))
const LoginPage = lazy(() => import('./features/auth/LoginPage').then(module => ({ default: module.LoginPage })))
const RegisterPage = lazy(() => import('./features/auth/RegisterPage').then(module => ({ default: module.RegisterPage })))
const PasswordRecoveryPage = lazy(() => import('./features/auth/PasswordRecoveryPage').then(module => ({ default: module.PasswordRecoveryPage })))
const EmailVerificationPage = lazy(() => import('./features/auth/EmailVerificationPage').then(module => ({ default: module.EmailVerificationPage })))
const AccountsPage = lazy(() => import('./features/accounts/AccountsPage').then(module => ({ default: module.AccountsPage })))
const DashboardPage = lazy(() => import('./features/dashboard/DashboardPage').then(module => ({ default: module.DashboardPage })))
const CategoriesPage = lazy(() => import('./features/categories/CategoriesPage').then(module => ({ default: module.CategoriesPage })))
const TransactionsPage = lazy(() => import('./features/transactions/TransactionsPage').then(module => ({ default: module.TransactionsPage })))

function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const { status, user } = useAuth()

  if (status === 'loading') return <StartupScreen />
  if (status === 'anonymous') return <Navigate to="/login" replace />
  return <FinancialWorkspaceGate key={user?.id}>{children}</FinancialWorkspaceGate>
}

export default function App() {
  const { status } = useAuth()
  const location = useLocation()
  const { telegramOnly } = useRuntimeOptions()
  useEffect(() => {
    document.addEventListener('keydown', preventRepeatedPaste, true)
    return () => document.removeEventListener('keydown', preventRepeatedPaste, true)
  }, [])

  if (telegramOnly && !isTelegram()) return <AuthLayout title="Откройте Finance Tracker в Telegram"
    subtitle="Ваши записи доступны через ваш Telegram-аккаунт." alternateText="" alternateLink="/login" alternateLabel="">
    <TelegramEntry />
  </AuthLayout>

  if (telegramOnly && ['/register', '/forgot-password', '/reset-password', '/verify-email', '/confirm-email'].includes(location.pathname))
    return <Navigate to="/login" replace />

  return (
    <Suspense fallback={<StartupScreen />}><Routes>
      <Route path="/login" element={status === 'authenticated' ? <Navigate to="/" replace /> : <LoginPage />} />
      <Route path="/login/password" element={status === 'authenticated' ? <Navigate to="/" replace /> : <LoginPage password />} />
      <Route path="/register" element={status === 'authenticated' ? <Navigate to="/" replace /> : <RegisterPage />} />
      <Route path="/forgot-password" element={<PasswordRecoveryPage key="forgot" />} />
      <Route path="/reset-password" element={<PasswordRecoveryPage key={location.key} reset />} />
      <Route path="/verify-email" element={<EmailVerificationPage key="verification" />} />
      <Route path="/confirm-email" element={<EmailVerificationPage key={location.key} confirm />} />
      <Route element={<ProtectedRoute><AppShell /></ProtectedRoute>}>
        <Route path="/" element={<DashboardPage />} />
        <Route path="/account" element={<AccountPage />} />
        <Route path="/accounts" element={<AccountsPage />} />
        <Route path="/categories" element={<CategoriesPage />} />
        <Route path="/transactions" element={<TransactionsPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes></Suspense>
  )
}
