import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { http } from '../../api/http'
import { getApiError } from '../../api/apiError'
import { saveLocalFile } from '../../platform/localFiles'
import { isTelegram, telegramCanDownloadFile, telegramDownloadFile } from '../../platform/telegram'

type Preview = { transactions: number; accounts: number; categories: number; expectedRevision: number }
export function FinancialBackupControls() {
  const client = useQueryClient()
  const [prepared, setPrepared] = useState<{ content?: string; url?: string; fileName: string; expiresAtUtc?: string } | null>(null), [backup, setBackup] = useState<unknown>(null), [preview, setPreview] = useState<Preview | null>(null)
  const [confirmed, setConfirmed] = useState(false), [busy, setBusy] = useState(false), [error, setError] = useState(''), [notice, setNotice] = useState('')
  return <section className="transactions-list-card"><h2>Приватность</h2>
    <p>Записи зашифрованы. Доступ к истории — через ваш Telegram-аккаунт.</p>
    {error && <p className="notice error" role="alert">{error}</p>}{notice && <p role="status">{notice}</p>}
    <button className="ghost-button" disabled={busy} onClick={async () => {
      setBusy(true); setError(''); setNotice('')
      try {
        if (isTelegram() && telegramCanDownloadFile()) {
          const { data } = await http.post<{ url: string; fileName: string; expiresAtUtc: string }>('/finance/backups/download-ticket')
          setPrepared({ ...data, url: new URL(data.url, location.origin).href })
        } else {
          const { data } = await http.post('/finance/backups'); setPrepared({ content: JSON.stringify(data), fileName: `finance-${new Date().toISOString().slice(0, 10)}.json` })
        }
        setNotice('Копия подготовлена. Сохраните её на устройстве.')
      }
      catch (failure) { setError(getApiError(failure, 'Не удалось подготовить копию.')) } finally { setBusy(false) }
    }}>Подготовить зашифрованную копию</button>
    {prepared && <button className="primary-button" disabled={busy} onClick={async () => {
      setError('')
      try {
        if (prepared.expiresAtUtc && Date.parse(prepared.expiresAtUtc) <= Date.now()) { setPrepared(null); throw new Error('Срок ссылки истёк. Подготовьте копию ещё раз.') }
        const accepted = prepared.url ? await telegramDownloadFile(prepared.url, prepared.fileName)
          : await saveLocalFile(prepared.fileName, prepared.content!, 'application/json', isTelegram()) !== 'cancelled'
        if (accepted) { setNotice('Запрос на сохранение принят. Проверьте загрузки устройства.'); setPrepared(null) }
      }
      catch (failure) { setError(failure instanceof Error ? failure.message : 'Не удалось сохранить файл.') }
    }}>Сохранить копию</button>}
    <details className="panel-disclosure"><summary>Восстановить копию</summary>
      <label>Зашифрованный файл<input type="file" accept="application/json,.json" disabled={busy} onChange={async e => {
        const file = e.target.files?.[0]; setError(''); setNotice(''); setBackup(null); setPreview(null); setConfirmed(false)
        if (!file) return
        if (file.size > 23_000_000) { setError('Файл слишком большой.'); return }
        setBusy(true)
        try { const value: unknown = JSON.parse(await file.text()); const { data } = await http.post<Preview>('/finance/backups/preview', { backup: value }); setBackup(value); setPreview(data) }
        catch (failure) { setError(getApiError(failure, 'Не удалось проверить копию.')) } finally { setBusy(false) }
      }} /></label>
      {preview && <><p>В копии: {preview.transactions} операций, {preview.accounts} счетов.</p>
        <label className="private-consent"><input type="checkbox" checked={confirmed} onChange={e => setConfirmed(e.target.checked)} />Заменить текущую историю выбранной копией.</label>
        <button className="primary-button" disabled={busy || !confirmed} onClick={async () => {
          setBusy(true); setError('')
          try { await http.post('/finance/backups/restore', { backup, expectedRevision: preview.expectedRevision }); setPreview(null); setBackup(null); setPrepared(null); setNotice('Копия восстановлена.'); await client.invalidateQueries() }
          catch (failure) { setPreview(null); setConfirmed(false); setError(getApiError(failure, 'Не удалось восстановить копию. Проверьте файл повторно.')) } finally { setBusy(false) }
        }}>Восстановить выбранную копию</button></>}
    </details>
  </section>
}
