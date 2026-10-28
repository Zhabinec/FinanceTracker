import { lazy, Suspense, useEffect, useState, type ReactNode } from 'react'
import { http } from '../../api/http'
import { getApiError } from '../../api/apiError'
import { useAuth } from '../auth/useAuth'
import { AuthContext } from '../auth/authContext'
import { DataSkeleton } from '../../shared/DataSkeleton'
const LegacyFinancialUnlock = lazy(() => import('../legacyFinance/LegacyFinancialUnlock').then(module => ({ default: module.LegacyFinancialUnlock })))

type Workspace = { ready: boolean; baseCurrency: string | null; requiresKey: boolean }
export function FinancialWorkspaceGate({ children }: { children: ReactNode }) {
  const auth = useAuth()
  const [workspace, setWorkspace] = useState<Workspace | null>(null)
  const [error, setError] = useState(''), [attempt, setAttempt] = useState(0)
  useEffect(() => {
    const abort = new AbortController()
    void http.post<Workspace>('/finance/open', undefined, { signal: abort.signal }).then(({ data }) => {
      if (!abort.signal.aborted) { setWorkspace(data); setError('') }
    }).catch(failure => { if (!abort.signal.aborted) setError(getApiError(failure, 'Не удалось открыть финансы. Повторите попытку.')) })
    return () => abort.abort()
  }, [auth.user?.id, attempt])
  if (workspace?.ready) return <AuthContext.Provider value={{ ...auth, user: auth.user ? { ...auth.user, baseCurrency: workspace.baseCurrency ?? auth.user.baseCurrency } : null }}>{children}</AuthContext.Provider>
  if (workspace?.requiresKey) return <Suspense fallback={<DataSkeleton kind="overview" />}><LegacyFinancialUnlock onComplete={() => { setWorkspace(null); setAttempt(value => value + 1) }} /></Suspense>
  return <main className="private-loading" data-vault-user={auth.user?.id}>
    {error ? <section className="transactions-list-card"><p className="notice error" role="alert">{error}</p><button className="primary-button" onClick={() => { setError(''); setAttempt(value => value + 1) }}>Повторить</button></section>
      : <DataSkeleton kind="overview" label="Открываем финансовые данные…" />}
    <button className="text-button" onClick={() => void auth.signOut()}>Выйти из аккаунта</button>
  </main>
}
