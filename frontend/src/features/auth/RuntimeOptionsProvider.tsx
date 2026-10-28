import { useEffect, useState, type ReactNode } from 'react'
import { http } from '../../api/http'
import { RuntimeOptionsContext, type RuntimeOptions } from './runtimeOptions'
export function RuntimeOptionsProvider({ children }: { children: ReactNode }) {
  const [options, setOptions] = useState<RuntimeOptions | null>(null)
  const [error, setError] = useState(false), [attempt, setAttempt] = useState(0)
  useEffect(() => {
    let active = true
    void http.get<Partial<RuntimeOptions>>('/auth/options').then(({ data }) => {
      if (active) { setOptions({ telegramOnly: data.telegramOnly === true, managedFinancialKeys: data.managedFinancialKeys === true }); setError(false) }
    }).catch(() => { if (active) setError(true) })
    return () => { active = false }
  }, [attempt])
  if (!options) return <main className="private-gate"><p role={error ? 'alert' : 'status'}>{error ? 'Не удалось подключиться к приложению.' : 'Открываем приложение…'}</p>
    {error && <button className="ghost-button" onClick={() => { setError(false); setAttempt(value => value + 1) }}>Повторить</button>}</main>
  return <RuntimeOptionsContext.Provider value={options}>{children}</RuntimeOptionsContext.Provider>
}
