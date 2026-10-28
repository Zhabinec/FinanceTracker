import { useEffect, useRef, useState, useSyncExternalStore, type ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { http } from '../../api/http'
import { getApiError } from '../../api/apiError'
import { PasswordInput } from '../../shared/PasswordInput'
import { useAuth } from '../auth/useAuth'
import { AuthContext } from '../auth/authContext'
import { TelegramLinkPanel } from '../auth/TelegramLinkPanel'
import { decryptDocument, encryptDocument, newRecoveryCode, keyFromCode, backupEnvelope } from './crypto'
import { parseDocument, type FinancialDocument, type VaultEnvelope } from './model'
import { exportEncryptedBackup, lockVault, openVault, retryPendingSave, subscribeVault, vaultSnapshot, loadServerVersion, keepEncryptedPending } from './session'
import { hasDevicePin, removeDevicePin, saveDevicePin, unlockWithDevicePin } from './devicePin'
import { PinFields } from './PinFields'
import { RecoveryCodeCard } from './RecoveryCodeCard'
import { useRuntimeOptions } from '../auth/runtimeOptions'
import { isTelegram } from '../../platform/telegram'
import { legacyLaunchUrl } from '../../platform/legacyLaunch'

type Setup = { key: CryptoKey; code: string; document: FinancialDocument; fingerprint: string; id: string }
export function PrivateFinanceGate({ children }: { children: ReactNode }) {
  const auth = useAuth(), userId = auth.user!.id, client = useQueryClient()
  const options = useRuntimeOptions()
  const managedAvailable = options.managedFinancialKeys && isTelegram()
  const session = useSyncExternalStore(subscribeVault, vaultSnapshot)
  const [head, setHead] = useState<VaultEnvelope | null | undefined>()
  const [setup, setSetup] = useState<Setup | null>(null)
  const [code, setCode] = useState(''), [saved, setSaved] = useState(false)
  const [busy, setBusy] = useState(false), [error, setError] = useState('')
  const [backup, setBackup] = useState<unknown>(null), [restoreConfirmed, setRestoreConfirmed] = useState(false)
  const [accountOpen, setAccountOpen] = useState(false)
  const [pinMode, setPinMode] = useState(false), [pinAvailable, setPinAvailable] = useState(false)
  const [pin, setPin] = useState(''), [repeatPin, setRepeatPin] = useState(''), [enablePin, setEnablePin] = useState(false)
  const lifecycle = useRef(0)
  const ensureActive = (epoch: number) => { if (lifecycle.current !== epoch) throw new Error('Аккаунт изменился. Повторите вход.') }
  useEffect(() => {
    const effectEpoch = ++lifecycle.current
    let active = true
    lockVault()
    const vaultRequest = http.get<{ vault: VaultEnvelope | null }>('/private-finance')
    const keyRequest = managedAvailable ? http.get<{ code: string | null }>('/private-finance/key') : Promise.resolve(null)
    Promise.all([vaultRequest, keyRequest]).then(async ([{ data }, access]) => {
      if (!active) return
      if (managedAvailable && data.vault) {
        if (access?.data.code) {
          const key = await keyFromCode(access.data.code)
          const document = await decryptDocument(data.vault, key, userId)
          if (!active) return
          if (data.vault.isActive) finish(key, data.vault, document, effectEpoch, true)
          else { const result = await activate(data.vault, key, undefined, effectEpoch); if (active) finish(key, result.head, result.document, effectEpoch, true) }
          return
        }
        const previousOrigin = legacyLaunchUrl(import.meta.env.VITE_LEGACY_APP_ORIGIN, location.origin, location.hash, location.search)
        if (previousOrigin) { location.replace(previousOrigin); return }
      }
      setHead(data.vault)
    })
      .catch(e => { if (active) setError(getApiError(e, 'Не удалось загрузить хранилище.')) })
    return () => {
      active = false; lifecycle.current = effectEpoch + 1; lockVault()
      client.removeQueries({ predicate: query => ['accounts', 'transfers', 'categories', 'transactions', 'dashboard', 'budgets'].includes(String(query.queryKey[0])) })
    }
    // Initial load is scoped to the authenticated identity and immutable runtime policy.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [userId, client, managedAvailable])
  useEffect(() => {
    let active = true
    if (head?.isActive && !session.unlocked) void hasDevicePin(userId, head.id).then(available => {
      if (active) { setPinAvailable(available); setPinMode(available) }
    }).catch(() => { if (active) { setPinAvailable(false); setPinMode(false) } })
    return () => { active = false }
  }, [head?.id, head?.isActive, session.unlocked, userId])
  useEffect(() => {
    if (session.unlocked) void client.invalidateQueries({ predicate: query => ['accounts', 'transfers', 'categories', 'transactions', 'dashboard', 'budgets'].includes(String(query.queryKey[0])) })
  }, [session.revision, session.unlocked, client])
  const message = (e: unknown) => e instanceof Error && !('isAxiosError' in e) ? e.message : getApiError(e, 'Не удалось открыть хранилище.')
  const prepare = async () => {
    const epoch = lifecycle.current
    setBusy(true); setError('')
    try {
      const { data } = await http.get<{ document: string; fingerprint: string }>('/private-finance/legacy')
      const generated = await newRecoveryCode()
      const key = await keyFromCode(generated)
      ensureActive(epoch)
      setSetup({ key, code: generated, document: parseDocument(JSON.parse(data.document)), fingerprint: data.fingerprint, id: crypto.randomUUID() })
      setSaved(false)
    } catch (e) { setError(message(e)) }
    finally { setBusy(false) }
  }
  async function activate(envelope: VaultEnvelope, key: CryptoKey, expected: FinancialDocument | undefined, epoch: number) {
    // Download and decrypt what was actually persisted, before requesting removal of legacy rows.
    const { data } = await http.get<{ vault: VaultEnvelope | null }>('/private-finance')
    ensureActive(epoch)
    if (!data.vault || data.vault.id !== envelope.id || data.vault.revision !== envelope.revision) throw new Error('Перенос изменился. Обновите страницу.')
    const document = await decryptDocument(data.vault, key, userId)
    const original = expected ?? parseDocument(JSON.parse((await http.get<{ document: string }>('/private-finance/legacy')).data.document))
    ensureActive(epoch)
    if (JSON.stringify(document) !== JSON.stringify(original)) throw new Error('Проверка копии не прошла. Открытая история сохранена.')
    const result = await http.post<VaultEnvelope>('/private-finance/activate', { id: envelope.id, revision: envelope.revision })
    ensureActive(epoch)
    if (!result.data.isActive || result.data.ciphertext !== data.vault.ciphertext || result.data.nonce !== data.vault.nonce) throw new Error('Не удалось проверить завершение переноса.')
    return { head: result.data, document }
  }
  function finish(key: CryptoKey, envelope: VaultEnvelope, document: FinancialDocument, epoch: number, managed = false) {
    ensureActive(epoch)
    // The managed path no longer needs a weak local PIN-wrapped copy of the key.
    // Storage denial must not prevent an otherwise verified Telegram session from opening.
    if (managed) void removeDevicePin(userId, envelope.id).catch(() => undefined)
    client.clear(); openVault(userId, key, envelope, document, managed); setHead(envelope); setCode(''); setSetup(null); setError('')
    setBackup(null); setRestoreConfirmed(false); setAccountOpen(false); setSaved(false)
    setPin(''); setRepeatPin(''); setEnablePin(false)
  }
  const savePin = async (recoveryCode: string, vaultId: string) => {
    if (!enablePin) return
    if (!/^\d{6}$/.test(pin) || pin !== repeatPin) throw new Error('Введите одинаковый PIN из 6 цифр в обоих полях.')
    await saveDevicePin(userId, vaultId, recoveryCode, pin)
  }
  const startManaged = async () => {
    if (busy) return
    const epoch = lifecycle.current
    setBusy(true); setError('')
    try {
      const { data: envelope } = await http.post<VaultEnvelope>('/private-finance/key/start')
      ensureActive(epoch)
      const { data } = await http.get<{ code: string | null }>('/private-finance/key')
      ensureActive(epoch)
      if (!data.code) throw new Error('Не удалось получить доступ к финансам. Повторите открытие.')
      const key = await keyFromCode(data.code)
      const document = await decryptDocument(envelope, key, userId)
      finish(key, envelope, document, epoch, true)
    } catch (e) { if (lifecycle.current === epoch) setError(message(e)) }
    finally { if (lifecycle.current === epoch) setBusy(false) }
  }
  const create = async () => {
    if (!setup || !saved || busy) return
    const epoch = lifecycle.current
    setBusy(true); setError('')
    try {
      if (enablePin && (!/^\d{6}$/.test(pin) || pin !== repeatPin)) throw new Error('Введите одинаковый PIN из 6 цифр в обоих полях.')
      const encrypted = await encryptDocument(setup.document, setup.key, userId, setup.id, 1)
      ensureActive(epoch)
      const { data } = await http.post<VaultEnvelope>('/private-finance/stage', { id: setup.id, mutationId: crypto.randomUUID(), format: 1, legacyFingerprint: setup.fingerprint, ...encrypted })
      ensureActive(epoch)
      const result = await activate(data, setup.key, setup.document, epoch)
      await savePin(setup.code, result.head.id)
      finish(setup.key, result.head, result.document, epoch)
    } catch (e) {
      if (lifecycle.current !== epoch) return
      setError(message(e)); setCode(setup.code)
      // A timed-out stage/activation can have committed. Keep the saved code and recover from server status.
      try { const result = await http.get<{ vault: VaultEnvelope | null }>('/private-finance'); setHead(result.data.vault); if (result.data.vault) setSetup(null) } catch { /* Keep setup and its code for another attempt. */ }
    } finally { setBusy(false) }
  }
  const unlock = async () => {
    if (!head || busy) return
    const epoch = lifecycle.current
    setBusy(true); setError('')
    try {
      const recoveryCode = pinMode ? await unlockWithDevicePin(userId, head.id, pin) : code
      const key = await keyFromCode(recoveryCode)
      ensureActive(epoch)
      if (head.isActive) {
        // Re-fetch after a manual lock: another device may have saved a newer revision.
        const remote = (await http.get<{ vault: VaultEnvelope | null }>('/private-finance')).data.vault
        if (!remote?.isActive || remote.id !== head.id || remote.revision < head.revision) throw new Error('Версия хранилища изменилась. Обновите страницу.')
        const document = await decryptDocument(remote, key, userId)
        ensureActive(epoch)
        if (!pinMode) await savePin(recoveryCode, remote.id)
        finish(key, remote, document, epoch)
      }
      else { const result = await activate(head, key, undefined, epoch); finish(key, result.head, result.document, epoch) }
    } catch (e) {
      if (lifecycle.current !== epoch) return
      setError(message(e)); setPin(''); setRepeatPin('')
      if (pinMode) {
        const available = await hasDevicePin(userId, head.id).catch(() => false)
        if (lifecycle.current === epoch) { setPinAvailable(available); if (!available) setPinMode(false) }
      }
    }
    finally { if (lifecycle.current === epoch) setBusy(false) }
  }
  const cancel = async () => {
    if (!head || head.isActive || !window.confirm('Отменить незавершённый перенос? Исходная история останется на сервере.')) return
    setBusy(true)
    try { await http.delete('/private-finance/stage', { params: { id: head.id, revision: head.revision } }); setHead(null); setCode(''); setError('') }
    catch (e) { setError(message(e)) } finally { setBusy(false) }
  }
  const restoreLocked = async () => {
    if (!head?.isActive || !backup || !restoreConfirmed || !code || busy) return
    const epoch = lifecycle.current
    setBusy(true); setError('')
    try {
      const key = await keyFromCode(code)
      const document = await decryptDocument(backupEnvelope(backup, userId, head.id), key, userId)
      ensureActive(epoch)
      const { data } = await http.get<{ vault: VaultEnvelope | null }>('/private-finance')
      const remote = data.vault
      if (!remote?.isActive || remote.id !== head.id || remote.revision < head.revision || !Number.isSafeInteger(remote.revision)) throw new Error('Не удалось проверить текущую версию сервера.')
      const encrypted = await encryptDocument(document, key, userId, head.id, remote.revision + 1)
      ensureActive(epoch)
      const input = { id: head.id, expectedRevision: remote.revision, mutationId: crypto.randomUUID(), format: 1, ...encrypted }
      keepEncryptedPending(userId, input)
      const result = await http.put<VaultEnvelope>('/private-finance', input)
      if (result.data.mutationId !== input.mutationId || result.data.revision !== remote.revision + 1 || result.data.ciphertext !== input.ciphertext || result.data.nonce !== input.nonce) throw new Error('Сохранение копии не подтверждено.')
      finish(key, result.data, document, epoch); setBackup(null); setRestoreConfirmed(false)
    } catch (e) { if (lifecycle.current === epoch) setError(message(e)) }
    finally { if (lifecycle.current === epoch) setBusy(false) }
  }
  if (session.unlocked && session.userId === userId) return <AuthContext.Provider value={{ ...auth, user: { ...auth.user!, baseCurrency: session.baseCurrency } }}>
    {session.pending && <div className="notice error private-pending" role="alert">Сохранение не подтверждено. Повторите его или скачайте копию перед закрытием вкладки.
      <button className="ghost-button" disabled={busy} onClick={async () => { setBusy(true); try { await retryPendingSave(); await client.invalidateQueries() } catch (e) { setError(message(e)) } finally { setBusy(false) } }}>Повторить сохранение</button>
      <button className="ghost-button" disabled={busy} onClick={async () => { setBusy(true); setError(''); try { await exportEncryptedBackup() } catch (e) { setError(message(e)) } finally { setBusy(false) } }}>Скачать копию</button>
      <button className="ghost-button" disabled={busy} onClick={async () => {
        if (!window.confirm('Вернуться к версии на сервере? Несохранённые изменения будут отброшены. Уже сохранённые записи останутся.')) return
        setBusy(true)
        try { await loadServerVersion(); setError(''); await client.invalidateQueries() } catch (e) { setError(message(e)) } finally { setBusy(false) }
      }}>Вернуться к версии на сервере</button>{error && <p>{error}</p>}</div>}
    {children}
  </AuthContext.Provider>
  return <main className="private-gate" data-vault-user={userId}><section className="transactions-list-card">
    <h1>{head?.isActive ? 'Разблокировать финансы' : head ? 'Завершить защиту записей' : setup ? 'Сохраните код восстановления' : managedAvailable ? 'Ваши финансы в Telegram' : 'Защитим ваши финансы'}</h1>
    {error && <p className="notice error" role="alert">{error}</p>}
    {head === undefined ? <><p role="status">Загружаем…</p>{error && <button className="ghost-button" onClick={() => location.reload()}>Повторить</button>}</>
      : head ? <form onSubmit={e => { e.preventDefault(); void unlock() }}>
        {pinMode ? <>
          <PasswordInput label="PIN" inputMode="numeric" pattern="[0-9]{6}" autoComplete="off" maxLength={6} value={pin} onChange={e => setPin(e.target.value)} />
          <button className="text-button" type="button" disabled={busy} onClick={() => { setPinMode(false); setPin(''); setError('') }}>Забыл PIN — ввести код восстановления</button>
        </> : <>
          <PasswordInput label="Код восстановления" autoComplete="off" value={code} onChange={e => setCode(e.target.value)} maxLength={100} />
          {head.isActive && <>
            {pinAvailable && <button className="text-button" type="button" disabled={busy} onClick={() => { setPinMode(true); setCode(''); setError('') }}>Войти по PIN</button>}
            <label className="private-consent"><input type="checkbox" checked={enablePin} onChange={e => setEnablePin(e.target.checked)} />Настроить PIN на этом устройстве</label>
            {enablePin && <PinFields pin={pin} repeat={repeatPin} onPin={setPin} onRepeat={setRepeatPin} />}
          </>}
        </>}
        {!head.isActive && <p>Ваши записи сохранены. Введите код, чтобы проверить доступ и закончить настройку.</p>}
        <button className="primary-button" disabled={busy || (pinMode ? pin.length !== 6 : !code)}>{busy ? 'Проверяем…' : head.isActive ? 'Разблокировать' : 'Завершить настройку'}</button>
        {head.isActive && !pinMode && <details className="panel-disclosure"><summary>Не удаётся открыть? Восстановить файл</summary>
          <label>Зашифрованный файл для восстановления<input type="file" accept="application/json,.json" disabled={busy} onChange={async e => {
            const file = e.target.files?.[0]; setBackup(null); setRestoreConfirmed(false); setError('')
            if (!file) return
            if (file.size > 6_000_000) { setError('Файл слишком большой.'); return }
            try { const value: unknown = JSON.parse(await file.text()); backupEnvelope(value, userId, head.id); setBackup(value) }
            catch { setError('Выберите зашифрованную копию этого аккаунта и хранилища.') }
          }} /></label>
          {Boolean(backup) && <><label className="private-consent"><input type="checkbox" checked={restoreConfirmed} onChange={e => setRestoreConfirmed(e.target.checked)} />Заменить историю на сервере и несохранённые изменения выбранной копией.</label>
            <button className="primary-button" type="button" disabled={busy || !restoreConfirmed || !code} onClick={() => void restoreLocked()}>Восстановить из файла</button></>}
        </details>}
        {!head.isActive && <button className="ghost-button" type="button" disabled={busy} onClick={() => void cancel()}>Отменить перенос</button>}
      </form>
      : setup ? <>
        <p>Это запасной ключ к вашим записям: для нового устройства или если забудете PIN. Сохраните его в менеджере паролей и никому не отправляйте.</p>
        <h2>Быстрый вход</h2>
        <label className="private-consent"><input type="checkbox" checked={enablePin} onChange={e => setEnablePin(e.target.checked)} />Настроить PIN на этом устройстве</label>
        {enablePin && <PinFields pin={pin} repeat={repeatPin} onPin={setPin} onRepeat={setRepeatPin} />}
        <RecoveryCodeCard key={setup.id} code={setup.code} />
        <p className="muted">Без кода и доступа к вашим устройствам восстановить записи не получится.</p>
        <label className="private-consent"><input type="checkbox" checked={saved} onChange={e => setSaved(e.target.checked)} />Я сохранил код восстановления</label>
        <p className="muted">Продолжая, вы переносите записи в защищённое хранилище. Сами записи сохранятся.</p>
        <details className="panel-disclosure"><summary>Что будет с прежними записями?</summary><p>Сначала проверим защищённую копию. Только после этого удалим прежнюю незашифрованную версию. Если проверка не пройдёт, записи останутся как были.</p></details>
        <button className="primary-button" disabled={busy || !saved} onClick={() => void create()}>{busy ? 'Подготавливаем…' : 'Начать пользоваться'}</button>
      </> : managedAvailable ? <>
        <p>История будет доступна через ваш Telegram-аккаунт на ваших устройствах. Записи и ключ хранятся на сервере в зашифрованном виде.</p>
        <button className="primary-button" disabled={busy} onClick={() => void startManaged()}>{busy ? 'Открываем…' : 'Начать пользоваться'}</button>
      </> : <>
        <p>Сохраните код восстановления один раз. Дальше на своём устройстве можно входить по короткому PIN.</p>
        <button className="primary-button" disabled={busy} onClick={() => { setEnablePin(true); void prepare() }}>{busy ? 'Подготавливаем…' : 'Настроить вход по PIN'}</button>
        <button className="text-button" disabled={busy} onClick={() => { setEnablePin(false); void prepare() }}>Только код восстановления</button>
      </>}
    <details className="panel-disclosure" onToggle={e => setAccountOpen(e.currentTarget.open)}><summary>Доступ к аккаунту</summary>
      {accountOpen && <><p>{auth.user?.email ?? 'Telegram'}</p>{auth.user?.email && !options.telegramOnly && <Link to="/forgot-password">Сменить пароль</Link>}<TelegramLinkPanel /></>}
    </details>
    <button className="text-button" type="button" disabled={busy} onClick={() => void auth.signOut()}>Выйти из аккаунта</button>
  </section></main>
}
