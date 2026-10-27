import { useEffect, useState, useSyncExternalStore } from 'react'
import { PasswordInput } from '../../shared/PasswordInput'
import { configureDevicePin, subscribeVault, vaultSnapshot } from './session'
import { hasDevicePin, removeDevicePin } from './devicePin'
import { PinFields } from './PinFields'

export function DevicePinControls() {
  const session = useSyncExternalStore(subscribeVault, vaultSnapshot)
  const [enabled, setEnabled] = useState(false), [busy, setBusy] = useState(false)
  const [code, setCode] = useState(''), [pin, setPin] = useState(''), [repeat, setRepeat] = useState('')
  const [error, setError] = useState(''), [notice, setNotice] = useState('')
  useEffect(() => {
    let active = true
    void hasDevicePin(session.userId, session.vaultId).then(value => { if (active) setEnabled(value) }).catch(() => undefined)
    return () => { active = false }
  }, [session.userId, session.vaultId])
  return <details className="panel-disclosure"><summary>PIN на этом устройстве{enabled ? ' · включён' : ''}</summary>
    {error && <p className="notice error" role="alert">{error}</p>}{notice && <p role="status">{notice}</p>}
    <form onSubmit={async event => {
      event.preventDefault()
      if (busy) return
      setError(''); setNotice('')
      if (!/^\d{6}$/.test(pin) || pin !== repeat) { setError('Введите одинаковый PIN из 6 цифр в обоих полях.'); return }
      setBusy(true)
      try { await configureDevicePin(code, pin); setEnabled(true); setCode(''); setPin(''); setRepeat(''); setNotice('PIN сохранён на этом устройстве.') }
      catch (failure) { setError(failure instanceof Error ? failure.message : 'Не удалось сохранить PIN.') }
      finally { setBusy(false) }
    }}>
      <PasswordInput label="Код восстановления для настройки PIN" value={code} onChange={e => setCode(e.target.value)} maxLength={100} autoComplete="off" />
      <PinFields pin={pin} repeat={repeat} onPin={setPin} onRepeat={setRepeat} />
      <button className="primary-button" disabled={busy || !code || !pin || !repeat}>{busy ? 'Сохраняем…' : enabled ? 'Сменить PIN' : 'Включить PIN'}</button>
    </form>
    {enabled && <button className="ghost-button" disabled={busy} onClick={async () => {
      if (!window.confirm('Отключить PIN на этом устройстве? Для следующей разблокировки понадобится код восстановления.')) return
      setBusy(true); setError(''); setNotice('')
      try { await removeDevicePin(session.userId, session.vaultId); setEnabled(false); setNotice('PIN отключён.'); setCode(''); setPin(''); setRepeat('') }
      catch { setError('Не удалось отключить PIN.') }
      finally { setBusy(false) }
    }}>Отключить PIN</button>}
  </details>
}
