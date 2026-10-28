import { useEffect, useState } from 'react'
import { http } from '../../api/http'
import { getApiError } from '../../api/apiError'
import { PasswordInput } from '../../shared/PasswordInput'
import { useAuth } from '../auth/useAuth'
import { decryptDocument, keyFromCode } from './crypto'
import { hasDevicePin, removeDevicePin, unlockWithDevicePin } from './devicePin'
import type { VaultEnvelope } from './model'
import { legacyLaunchUrl } from '../../platform/legacyLaunch'

// Compatibility reader only: no financial calculations, mutations or key provisioning in the client.
export function LegacyFinancialUnlock({ onComplete }: { onComplete(): void }) {
  const { user, signOut } = useAuth()
  const [head, setHead] = useState<VaultEnvelope | null>(null)
  const [pinAvailable, setPinAvailable] = useState(false), [usePin, setUsePin] = useState(false)
  const [code, setCode] = useState(''), [pin, setPin] = useState(''), [consent, setConsent] = useState(false)
  const [busy, setBusy] = useState(false), [error, setError] = useState('')
  useEffect(() => {
    const abort = new AbortController()
    const previous = legacyLaunchUrl(import.meta.env.VITE_LEGACY_APP_ORIGIN, location.origin, location.hash, location.search)
    if (previous) { location.replace(previous); return }
    void http.get<{ vault: VaultEnvelope | null }>('/finance/legacy', { signal: abort.signal }).then(async ({ data }) => {
      if (!data.vault?.isActive) throw new Error('Прежнее хранилище недоступно. Исходная история сохранена.')
      const available = await hasDevicePin(user!.id, data.vault.id).catch(() => false)
      if (!abort.signal.aborted) { setHead(data.vault); setPinAvailable(available); setUsePin(available) }
    }).catch(failure => { if (!abort.signal.aborted) setError(getApiError(failure, 'Не удалось проверить прежнюю историю.')) })
    return () => abort.abort()
  }, [user])
  return <main className="private-gate"><section className="transactions-list-card">
    <h1>Подключить прежнюю историю</h1>
    <p>Для этой истории раньше использовался отдельный ключ. Подтвердите его один раз — затем записи будут открываться через Telegram.</p>
    {error && <p className="notice error" role="alert">{error}</p>}
    <form onSubmit={async event => {
      event.preventDefault(); if (busy || !consent || !head || !user) return
      setBusy(true); setError('')
      try {
        const recovered = usePin ? await unlockWithDevicePin(user.id, head.id, pin) : code
        const key = await keyFromCode(recovered)
        await decryptDocument(head, key, user.id)
        await http.post('/finance/legacy/enroll', { vaultId: head.id, expectedRevision: head.revision, code: recovered, consentVersion: 1 })
        await removeDevicePin(user.id, head.id).catch(() => undefined)
        onComplete()
      } catch (failure) { setError(getApiError(failure, failure instanceof Error ? failure.message : 'Не удалось подключить историю.')) }
      finally { setCode(''); setPin(''); setBusy(false) }
    }}>
      {usePin ? <PasswordInput label="Текущий PIN" value={pin} onChange={e => setPin(e.target.value)} inputMode="numeric" maxLength={6} autoComplete="off" />
        : <PasswordInput label="Прежний код восстановления" value={code} onChange={e => setCode(e.target.value)} maxLength={100} autoComplete="off" />}
      {pinAvailable && <button type="button" className="text-button" disabled={busy} onClick={() => { setUsePin(value => !value); setCode(''); setPin('') }}>{usePin ? 'Использовать код восстановления' : 'Использовать PIN этого устройства'}</button>}
      <label className="private-consent"><input type="checkbox" checked={consent} onChange={e => setConsent(e.target.checked)} />Разрешаю сервису хранить защищённый финансовый ключ и обрабатывать записи для входа через Telegram.</label>
      <button className="primary-button" disabled={busy || !head || !consent || (usePin ? pin.length !== 6 : !code)}>{busy ? 'Проверяем…' : 'Подключить историю'}</button>
    </form>
    <button className="text-button" onClick={() => void signOut()}>Выйти из аккаунта</button>
  </section></main>
}
