import { useEffect, useState } from 'react'
import { Mascot } from './Mascot'

export function StartupScreen({ error = false, onRetry }: { error?: boolean; onRetry?: () => void }) {
  const [waiting, setWaiting] = useState(false)
  useEffect(() => {
    const timer = setTimeout(() => setWaiting(true), 8000)
    return () => clearTimeout(timer)
  }, [])
  return <main className="startup-screen" aria-busy={!error}>
    <Mascot className="startup-mascot" eager />
    <h1>Finance Tracker</h1>
    <p role={error ? 'alert' : 'status'}>{error ? 'Не удалось подключиться' : waiting ? 'Подключаемся к вашим финансам…' : 'Открываем ваши финансы…'}</p>
    {waiting && !error && <p className="startup-hint">Первый запуск после перерыва может занять около минуты.</p>}
    {!error && <span className="startup-progress" aria-hidden="true" />}
    {error && <><p className="startup-hint">Проверьте интернет и попробуйте ещё раз. Ваши записи сохранены.</p>
      <button className="primary-button" onClick={onRetry}>Повторить</button></>}
  </main>
}
