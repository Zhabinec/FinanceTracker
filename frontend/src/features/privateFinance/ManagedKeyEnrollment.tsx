import { useEffect, useState } from 'react'
import { http } from '../../api/http'
import { PasswordInput } from '../../shared/PasswordInput'
import { getApiError } from '../../api/apiError'
import { decryptDocument, keyFromCode } from './crypto'
import { hasDevicePin, removeDevicePin, unlockWithDevicePin } from './devicePin'
import { markVaultManaged, vaultSnapshot } from './session'
import type { VaultEnvelope } from './model'

export function ManagedKeyEnrollment() {
  const { userId, vaultId } = vaultSnapshot()
  const [code, setCode] = useState(''), [pin, setPin] = useState('')
  const [pinAvailable, setPinAvailable] = useState(false), [usePin, setUsePin] = useState(false)
  const [consent, setConsent] = useState(false), [busy, setBusy] = useState(false), [error, setError] = useState('')
  useEffect(() => {
    let active = true
    void hasDevicePin(userId, vaultId).then(value => { if (active) { setPinAvailable(value); setUsePin(value) } }).catch(() => undefined)
    return () => { active = false }
  }, [userId, vaultId])
  return <details className="panel-disclosure"><summary>Вход через Telegram на всех устройствах</summary>
    <p>Прежняя история сохранится. Для открытия без резервного кода сервис сохранит защищённую копию финансового ключа. Это позволит backend расшифровывать записи; прежний режим без доступа сервера изменится только после вашего подтверждения.</p>
    {error && <p className="notice error" role="alert">{error}</p>}
    <form onSubmit={async event => {
      event.preventDefault()
      if (busy || !consent) return
      setBusy(true); setError('')
      try {
        const recovered = usePin ? await unlockWithDevicePin(userId, vaultId, pin) : code
        const key = await keyFromCode(recovered)
        const { data } = await http.get<{ vault: VaultEnvelope | null }>('/private-finance')
        if (!data.vault?.isActive || data.vault.id !== vaultId || vaultSnapshot().userId !== userId) throw new Error('Аккаунт или история изменились. Повторите открытие.')
        await decryptDocument(data.vault, key, userId)
        if (vaultSnapshot().userId !== userId) throw new Error('Аккаунт изменился.')
        await http.post('/private-finance/key', { vaultId, expectedRevision: data.vault.revision, code: recovered, consentVersion: 1 })
        if (vaultSnapshot().userId === userId) {
          markVaultManaged()
          void removeDevicePin(userId, vaultId).catch(() => undefined)
        }
      } catch (failure) { setError(getApiError(failure, failure instanceof Error ? failure.message : 'Не удалось изменить доступ.')) }
      finally { setCode(''); setPin(''); setBusy(false) }
    }}>
      {usePin ? <PasswordInput label="Текущий PIN" autoComplete="off" inputMode="numeric" maxLength={6} value={pin} onChange={event => setPin(event.target.value)} />
        : <PasswordInput label="Код восстановления для переноса" autoComplete="off" maxLength={100} value={code} onChange={event => setCode(event.target.value)} />}
      {pinAvailable && <button type="button" className="text-button" disabled={busy} onClick={() => { setUsePin(value => !value); setCode(''); setPin(''); setError('') }}>{usePin ? 'Использовать резервный код' : 'Использовать PIN этого устройства'}</button>}
      <label className="private-consent"><input type="checkbox" checked={consent} onChange={event => setConsent(event.target.checked)} />Разрешаю сервису хранить защищённую копию ключа для доступа через Telegram.</label>
      <button className="primary-button" disabled={busy || !consent || (usePin ? pin.length !== 6 : !code)}>{busy ? 'Проверяем…' : 'Включить доступ на устройствах'}</button>
    </form>
  </details>
}
