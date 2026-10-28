import { useEffect, useState, type ReactNode } from 'react'
import { RuntimeOptionsContext, type RuntimeOptions } from './runtimeOptions'
import { loadRuntimePolicy } from './runtimePolicy'
import { StartupScreen } from '../../shared/StartupScreen'
export function RuntimeOptionsProvider({ children }: { children: ReactNode }) {
  const [options, setOptions] = useState<RuntimeOptions | null>(null)
  const [error, setError] = useState(false), [attempt, setAttempt] = useState(0)
  useEffect(() => {
    let active = true
    void loadRuntimePolicy().then(data => {
      if (active) { setOptions(data); setError(false) }
    }).catch(() => { if (active) setError(true) })
    return () => { active = false }
  }, [attempt])
  if (!options) return <StartupScreen key={attempt} error={error} onRetry={() => { setError(false); setAttempt(value => value + 1) }} />
  return <RuntimeOptionsContext.Provider value={options}>{children}</RuntimeOptionsContext.Provider>
}
