import { Navigate, Route, Routes, useLocation } from 'react-router-dom'
import { lazy, useEffect } from 'react'
import { preventRepeatedPaste } from './shared/pasteShortcut'
import { LoginPage } from './features/auth/LoginPage'
import { PasswordRecoveryPage } from './features/auth/PasswordRecoveryPage'
import { EmailVerificationPage } from './features/auth/EmailVerificationPage'
import { RegisterPage } from './features/auth/RegisterPage'
import { useAuth } from './features/auth/useAuth'
import './App.css'
import './motion.css'
import { AppShell } from './shared/AppShell'
import { PrivateFinanceGate } from './features/privateFinance/PrivateFinanceGate'

const AccountPage = lazy(() => import('./features/auth/AccountPage').then(module => ({ default: module.AccountPage })))
const AccountsPage = lazy(() => import('./features/accounts/AccountsPage').then(module => ({ default: module.AccountsPage })))
const DashboardPage = lazy(() => import('./features/dashboard/DashboardPage').then(module => ({ default: module.DashboardPage })))
const CategoriesPage = lazy(() => import('./features/categories/CategoriesPage').then(module => ({ default: module.CategoriesPage })))
const TransactionsPage = lazy(() => import('./features/transactions/TransactionsPage').then(module => ({ default: module.TransactionsPage })))

function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const { status, user } = useAuth()

  if (status === 'loading') return <div className="app-loader">Восстанавливаем сессию…</div>
  if (status === 'anonymous') return <Navigate to="/login" replace />
  return <PrivateFinanceGate key={user?.id}>{children}</PrivateFinanceGate>
}

export default function App() {
  const { status } = useAuth()
  const location = useLocation()
  useEffect(() => {
    document.addEventListener('keydown', preventRepeatedPaste, true)
    return () => document.removeEventListener('keydown', preventRepeatedPaste, true)
  }, [])

  return (
    <Routes>
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
    </Routes>
  )
}
