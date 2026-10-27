import { useEffect, useRef, useState, useSyncExternalStore, type ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { http } from '../../api/http'
import { getApiError } from '../../api/apiError'
import { PasswordInput } from '../../shared/PasswordInput'
import { useAuth } from '../auth/useAuth'
import { AuthContext } from '../auth/authContext'
import { TelegramLinkPanel } from '../auth/TelegramLinkPanel'
import { decryptDocument, encryptDocument, newRecoveryCode, keyFromCode, downloadJson, backupEnvelope } from './crypto'
import { parseDocument, type FinancialDocument, type VaultEnvelope } from './model'
import { exportEncryptedBackup, lockVault, openVault, retryPendingSave, subscribeVault, vaultSnapshot, loadServerVersion, keepEncryptedPending } from './session'

type Setup = { key: CryptoKey; code: string; document: FinancialDocument; fingerprint: string; id: string }
export function PrivateFinanceGate({ children }: { children: ReactNode }) {
  const auth = useAuth(), userId = auth.user!.id, client = useQueryClient()
  const session = useSyncExternalStore(subscribeVault, vaultSnapshot)
  const [head, setHead] = useState<VaultEnvelope | null | undefined>()
  const [setup, setSetup] = useState<Setup | null>(null)
  const [code, setCode] = useState(''), [saved, setSaved] = useState(false)
  const [busy, setBusy] = useState(false), [error, setError] = useState('')
  const [backup, setBackup] = useState<unknown>(null), [restoreConfirmed, setRestoreConfirmed] = useState(false)
  const [accountOpen, setAccountOpen] = useState(false)
  const lifecycle = useRef(0)
  const ensureActive = (epoch: number) => { if (lifecycle.current !== epoch) throw new Error('Аккаунт изменился. Повторите вход.') }
  useEffect(() => {
    const effectEpoch = ++lifecycle.current
    let active = true
    lockVault()
    http.get<{ vault: VaultEnvelope | null }>('/private-finance').then(({ data }) => { if (active) setHead(data.vault) })
      .catch(e => { if (active) setError(getApiError(e, 'Не удалось загрузить хранилище.')) })
    return () => {
      active = false; lifecycle.current = effectEpoch + 1; lockVault()
      client.removeQueries({ predicate: query => ['accounts', 'transfers', 'categories', 'transactions', 'dashboard', 'budgets'].includes(String(query.queryKey[0])) })
    }
  }, [userId, client])
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
  const activate = async (envelope: VaultEnvelope, key: CryptoKey, expected: FinancialDocument | undefined, epoch: number) => {
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
  const finish = (key: CryptoKey, envelope: VaultEnvelope, document: FinancialDocument, epoch: number) => {
    ensureActive(epoch)
    client.clear(); openVault(userId, key, envelope, document); setHead(envelope); setCode(''); setSetup(null); setError('')
    setBackup(null); setRestoreConfirmed(false); setAccountOpen(false); setSaved(false)
  }
  const create = async () => {
    if (!setup || !saved || busy) return
    const epoch = lifecycle.current
    setBusy(true); setError('')
    try {
      const encrypted = await encryptDocument(setup.document, setup.key, userId, setup.id, 1)
      ensureActive(epoch)
      const { data } = await http.post<VaultEnvelope>('/private-finance/stage', { id: setup.id, mutationId: crypto.randomUUID(), format: 1, legacyFingerprint: setup.fingerprint, ...encrypted })
      ensureActive(epoch)
      const result = await activate(data, setup.key, setup.document, epoch)
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
      const key = await keyFromCode(code)
      ensureActive(epoch)
      if (head.isActive) finish(key, head, await decryptDocument(head, key, userId), epoch)
      else { const result = await activate(head, key, undefined, epoch); finish(key, result.head, result.document, epoch) }
    } catch (e) { setError(message(e)) }
    finally { setBusy(false) }
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
      <button className="ghost-button" onClick={exportEncryptedBackup}>Скачать копию</button>
      <button className="ghost-button" disabled={busy} onClick={async () => {
        if (!window.confirm('Вернуться к версии на сервере? Несохранённые изменения будут отброшены. Уже сохранённые записи останутся.')) return
        setBusy(true)
        try { await loadServerVersion(); setError(''); await client.invalidateQueries() } catch (e) { setError(message(e)) } finally { setBusy(false) }
      }}>Вернуться к версии на сервере</button>{error && <p>{error}</p>}</div>}
    {children}
  </AuthContext.Provider>
  return <main className="private-gate" data-vault-user={userId}><section className="transactions-list-card">
    <h1>{head?.isActive ? 'Разблокировать финансы' : head ? 'Завершить перенос' : 'Приватное хранилище'}</h1>
    {error && <p className="notice error" role="alert">{error}</p>}
    {head === undefined ? <><p role="status">Загружаем…</p>{error && <button className="ghost-button" onClick={() => location.reload()}>Повторить</button>}</>
      : head ? <form onSubmit={e => { e.preventDefault(); void unlock() }}>
        <p>Введите сохранённый код. Он нужен только на вашем устройстве.</p>
        <PasswordInput label="Код восстановления" autoComplete="off" value={code} onChange={e => setCode(e.target.value)} maxLength={100} />
        {!head.isActive && <p>Зашифрованная копия сохранена. После её проверки открытая история будет удалена из рабочей БД.</p>}
        <button className="primary-button" disabled={busy || !code}>{busy ? 'Проверяем…' : head.isActive ? 'Разблокировать' : 'Проверить и завершить перенос'}</button>
        {head.isActive && <details className="panel-disclosure"><summary>Не удаётся открыть? Восстановить файл</summary>
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
        <p>Сохраните код отдельно и никому не передавайте. Без него мы не сможем восстановить финансы после потери устройств.</p>
        <label>Новый код восстановления<textarea readOnly value={setup.code} autoComplete="off" aria-label="Новый код восстановления" /></label>
        <button className="ghost-button" onClick={() => downloadJson('finance-recovery-key.json', { format: 'FinanceTrackerRecoveryKey', version: 1, userId, vaultId: setup.id, recoveryCode: setup.code })}>Скачать код</button>
        <label className="private-consent"><input type="checkbox" checked={saved} onChange={e => setSaved(e.target.checked)} />Я сохранил код. Разрешаю перенести историю и удалить открытые записи из рабочей БД.</label>
        <button className="primary-button" disabled={busy || !saved} onClick={() => void create()}>{busy ? 'Шифруем и проверяем…' : 'Зашифровать и перенести'}</button>
      </> : <>
        <p>Доходы, расходы и счета будут шифроваться на устройстве. Сервер получит только зашифрованную копию.</p>
        <p>Вход в аккаунт и разблокировка истории — разные действия. Код восстановления серверу не отправляется.</p>
        <button className="primary-button" disabled={busy} onClick={() => void prepare()}>{busy ? 'Подготавливаем…' : 'Создать приватное хранилище'}</button>
      </>}
    <details className="panel-disclosure" onToggle={e => setAccountOpen(e.currentTarget.open)}><summary>Доступ к аккаунту</summary>
      {accountOpen && <><p>{auth.user?.email}</p><Link to="/forgot-password">Сменить пароль</Link><TelegramLinkPanel /></>}
    </details>
    <button className="text-button" type="button" disabled={busy} onClick={() => void auth.signOut()}>Выйти из аккаунта</button>
  </section></main>
}
