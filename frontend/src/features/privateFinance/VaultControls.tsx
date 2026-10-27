import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { exportEncryptedBackup, lockVault, readEncryptedBackup, restoreFinancialBackup } from './session'
import type { FinancialDocument } from './model'
import { getApiError } from '../../api/apiError'
import { DevicePinControls } from './DevicePinControls'

export function VaultControls() {
  const client = useQueryClient()
  const [backup, setBackup] = useState<FinancialDocument | null>(null)
  const [confirmed, setConfirmed] = useState(false), [busy, setBusy] = useState(false), [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const message = (e: unknown) => e instanceof Error && !('isAxiosError' in e) ? e.message : getApiError(e, 'Не удалось восстановить копию.')
  return <section className="transactions-list-card"><h2>Приватность</h2>
    <p>Финансы зашифрованы. Код восстановления остаётся у вас.</p>
    {error && <p className="notice error" role="alert">{error}</p>}{notice && <p role="status">{notice}</p>}
    <button className="ghost-button" disabled={busy} onClick={async () => {
      setBusy(true); setError(''); setNotice('')
      try {
        const result = await exportEncryptedBackup()
        if (result === 'requested') setNotice('Скачивание запрошено. Проверьте загрузки устройства.')
        else if (result === 'shared') setNotice('Копия передана выбранному вами приложению.')
      } catch (failure) { setError(message(failure)) } finally { setBusy(false) }
    }}>Скачать зашифрованную копию</button>{' '}
    <button className="ghost-button" onClick={() => { client.clear(); lockVault() }}>Заблокировать финансы</button>
    <DevicePinControls />
    <details className="panel-disclosure"><summary>Восстановить копию</summary>
      <label>Зашифрованный файл<input type="file" accept="application/json,.json" disabled={busy} onChange={async e => {
        const file = e.target.files?.[0]; setError(''); setNotice(''); setBackup(null); setConfirmed(false)
        if (!file) return
        if (file.size > 6_000_000) { setError('Файл слишком большой.'); return }
        setBusy(true)
        try { setBackup(await readEncryptedBackup(JSON.parse(await file.text()))) }
        catch (failure) { setError(message(failure)) }
        finally { setBusy(false) }
      }} /></label>
      {backup && <><p>В выбранной копии: {backup.transactions.length} операций, {backup.accounts.length} счетов. Если файл содержит несохранённую версию, используется она.</p>
        <label className="private-consent"><input type="checkbox" checked={confirmed} onChange={e => setConfirmed(e.target.checked)} />Заменить текущую историю и несохранённые изменения выбранной копией.</label>
        <button className="primary-button" disabled={!confirmed || busy} onClick={async () => {
          setBusy(true); setError('')
          try { await restoreFinancialBackup(backup); setBackup(null); setConfirmed(false); setNotice('Копия восстановлена.'); await client.invalidateQueries() }
          catch (failure) { setError(message(failure)) } finally { setBusy(false) }
        }}>Восстановить выбранную копию</button></>}
    </details>
  </section>
}
