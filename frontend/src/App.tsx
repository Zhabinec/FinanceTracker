import { Navigate, Route, Routes } from 'react-router-dom'
import { AccountPage } from './features/auth/AccountPage'
import { DashboardPage } from './features/dashboard/DashboardPage'
import { CategoriesPage } from './features/categories/CategoriesPage'
import { LoginPage } from './features/auth/LoginPage'
import { PasswordRecoveryPage } from './features/auth/PasswordRecoveryPage'
import { RegisterPage } from './features/auth/RegisterPage'
import { TransactionsPage } from './features/transactions/TransactionsPage'
import { useAuth } from './features/auth/useAuth'
import './App.css'

function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const { status } = useAuth()

  if (status === 'loading') return <div className="app-loader">Восстанавливаем сессию…</div>
  if (status === 'anonymous') return <Navigate to="/login" replace />
  return children
}

export default function App() {
  const { status } = useAuth()

  return (
    <Routes>
      <Route path="/login" element={status === 'authenticated' ? <Navigate to="/" replace /> : <LoginPage />} />
      <Route path="/register" element={status === 'authenticated' ? <Navigate to="/" replace /> : <RegisterPage />} />
      <Route path="/forgot-password" element={<PasswordRecoveryPage />} />
      <Route path="/reset-password" element={<PasswordRecoveryPage reset />} />
      <Route path="/" element={<ProtectedRoute><DashboardPage /></ProtectedRoute>} />
      <Route path="/account" element={<ProtectedRoute><AccountPage /></ProtectedRoute>} />
      <Route path="/categories" element={<ProtectedRoute><CategoriesPage /></ProtectedRoute>} />
      <Route path="/transactions" element={<ProtectedRoute><TransactionsPage /></ProtectedRoute>} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
