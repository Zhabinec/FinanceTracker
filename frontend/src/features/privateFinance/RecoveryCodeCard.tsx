import { useEffect, useRef, useState } from 'react'
import { isTelegram } from '../../platform/telegram'
import { canSaveLocalFile, saveLocalFile } from '../../platform/localFiles'

export function RecoveryCodeCard({ code }: { code: string }) {
  const [shown, setShown] = useState(false), [selectCode, setSelectCode] = useState(false)
  const [notice, setNotice] = useState(''), [error, setError] = useState(''), [busy, setBusy] = useState(false)
  const field = useRef<HTMLTextAreaElement>(null)
  useEffect(() => {
    if (shown && selectCode) { field.current?.focus(); field.current?.select() }
  }, [shown, selectCode])
  return <div className="recovery-code-card">
    <div className="recovery-code-actions">
      <button className="ghost-button" type="button" disabled={busy} onClick={async () => {
        setNotice(''); setError(''); setBusy(true)
        try {
          if (!navigator.clipboard?.writeText) throw new Error('Clipboard unavailable')
          await navigator.clipboard.writeText(code)
          setNotice('Код скопирован. Сохраните его в менеджере паролей.')
        } catch {
          setShown(true); setSelectCode(true)
          if (field.current) { field.current.focus(); field.current.select() }
          // Selecting text does not mean it reached the clipboard: say so explicitly.
          setError('Не удалось скопировать автоматически. Код выделен ниже — удерживайте его и выберите «Копировать» или нажмите Ctrl+C.')
        } finally { setBusy(false) }
      }}>Скопировать код</button>
      {canSaveLocalFile(isTelegram()) && <button className="ghost-button" type="button" disabled={busy} onClick={async () => {
        setNotice(''); setError(''); setBusy(true)
        try {
          const result = await saveLocalFile('finance-recovery-code.txt', `Finance Tracker — код восстановления\n\n${code}\n\nСохраните этот файл в безопасном месте. Код нужен на новом устройстве или если вы забудете PIN.\nНикому не отправляйте код. Без кода и доступа к устройствам восстановить записи нельзя.\n`, 'text/plain', isTelegram())
          if (result === 'requested') setNotice('Скачивание запрошено. Проверьте загрузки устройства.')
          else if (result === 'shared') setNotice('Файл передан выбранному вами приложению. Убедитесь, что сохранили его.')
          else setNotice('Сохранение отменено. Можно скопировать код.')
        } catch (failure) { setError(failure instanceof Error ? failure.message : 'Не удалось сохранить файл.') }
        finally { setBusy(false) }
      }}>Сохранить файл</button>}
      <button className="text-button" type="button" aria-expanded={shown} onClick={() => { setShown(!shown); setSelectCode(false) }}>{shown ? 'Скрыть код' : 'Показать код'}</button>
    </div>
    {shown && <label>Код восстановления<textarea ref={field} readOnly value={code} autoComplete="off" aria-label="Новый код восстановления" onClick={event => event.currentTarget.select()} /></label>}
    {notice && <p role="status">{notice}</p>}{error && <p className="notice error" role="alert">{error}</p>}
  </div>
}
