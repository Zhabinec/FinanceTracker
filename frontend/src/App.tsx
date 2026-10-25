import { Navigate, Route, Routes, useLocation } from 'react-router-dom'
import { useEffect } from 'react'
import { preventRepeatedPaste } from './shared/pasteShortcut'
import { AccountPage } from './features/auth/AccountPage'
import { AccountsPage } from './features/accounts/AccountsPage'
import { DashboardPage } from './features/dashboard/DashboardPage'
import { CategoriesPage } from './features/categories/CategoriesPage'
import { LoginPage } from './features/auth/LoginPage'
import { PasswordRecoveryPage } from './features/auth/PasswordRecoveryPage'
import { EmailVerificationPage } from './features/auth/EmailVerificationPage'
import { RegisterPage } from './features/auth/RegisterPage'
import { TransactionsPage } from './features/transactions/TransactionsPage'
import { useAuth } from './features/auth/useAuth'
import './App.css'
import { AppShell } from './shared/AppShell'

function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const { status } = useAuth()

  if (status === 'loading') return <div className="app-loader">Восстанавливаем сессию…</div>
  if (status === 'anonymous') return <Navigate to="/login" replace />
  return children
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
