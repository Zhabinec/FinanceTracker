import { http } from '../../api/http'
import { decryptDocument, encryptDocument, downloadJson, backupEnvelope, keyFromCode } from './crypto'
import { saveDevicePin } from './devicePin'
import { parseDocument, type FinancialDocument, type VaultEnvelope, type VaultWrite } from './model'

type OpenSession = { userId: string; key: CryptoKey; head: VaultEnvelope; document: FinancialDocument; managed: boolean }
type PendingOperation = { identity: string; result: unknown }
let current: OpenSession | null = null
let operation: PendingOperation | null = null
let generation = 0
let queue: Promise<unknown> = Promise.resolve()
let loading: Promise<void> | null = null
const listeners = new Set<() => void>()
let snapshot = { unlocked: false, userId: '', vaultId: '', baseCurrency: 'RUB', revision: 0, pending: false, managed: false }
const storageKey = (userId: string) => `finance.private.pending.${userId}`
export const subscribeVault = (listener: () => void) => { listeners.add(listener); return () => { listeners.delete(listener) } }
export const vaultSnapshot = () => snapshot
function emit() {
  snapshot = { unlocked: Boolean(current), userId: current?.userId ?? '', vaultId: current?.head.id ?? '', baseCurrency: current?.document.baseCurrency ?? 'RUB', revision: current?.head.revision ?? 0, pending: Boolean(current && readPending(current.userId)), managed: current?.managed ?? false }
  listeners.forEach(listener => listener())
}
function readPending(userId: string): VaultWrite | null {
  try {
    const text = sessionStorage.getItem(storageKey(userId))
    if (!text) return null
    const data = JSON.parse(text) as VaultWrite
    if (data.format !== 1 || !Number.isSafeInteger(data.expectedRevision) || typeof data.ciphertext !== 'string' || data.ciphertext.length > 2_666_688 || typeof data.nonce !== 'string') throw new Error()
    return data
  } catch { throw new Error('Не удалось прочитать несохранённую зашифрованную копию. Не очищайте данные вкладки.') }
}
function clearPending(userId: string) { sessionStorage.removeItem(storageKey(userId)) }
export function keepEncryptedPending(userId: string, input: VaultWrite) { sessionStorage.setItem(storageKey(userId), JSON.stringify(input)) }
export function lockVault() { generation++; current = null; operation = null; loading = null; emit() }
export function openVault(userId: string, key: CryptoKey, head: VaultEnvelope, document: FinancialDocument, managed = false) {
  const pending = readPending(userId)
  if (pending?.id === head.id && pending.mutationId === head.mutationId) clearPending(userId)
  generation++; current = { userId, key, head, document, managed }; operation = null
  emit()
}
function requireSession() { if (!current) throw new Error('Сначала разблокируйте финансовое хранилище.'); return current }
export function markVaultManaged() { requireSession().managed = true; emit() }
function ensureCurrent(session: OpenSession, epoch: number) { if (current !== session || generation !== epoch) throw new Error('Хранилище заблокировано. Повторите вход.') }
export async function configureDevicePin(code: string, pin: string) {
  const session = requireSession(), epoch = generation
  // The unlocked screen alone is insufficient to replace a PIN: require the recovery key.
  const key = await keyFromCode(code)
  await decryptDocument(session.head, key, session.userId)
  ensureCurrent(session, epoch)
  await saveDevicePin(session.userId, session.head.id, code, pin)
  ensureCurrent(session, epoch)
}
export function financialDocument() { return requireSession().document }
export async function readEncryptedBackup(value: unknown) {
  const session = requireSession()
  return decryptDocument(backupEnvelope(value, session.userId, session.head.id), session.key, session.userId)
}
export async function restoreFinancialBackup(document: FinancialDocument) {
  const scheduled = requireSession(), epoch = generation
  const work = queue.then(async () => {
    ensureCurrent(scheduled, epoch)
    const { data } = await http.get<{ vault: VaultEnvelope | null }>('/private-finance')
    ensureCurrent(scheduled, epoch)
    const head = data.vault
    if (!head?.isActive || head.id !== scheduled.head.id || head.revision < scheduled.head.revision) throw new Error('Версия сервера изменилась. Не удалось безопасно восстановить копию.')
    const restored = parseDocument(document)
    const encrypted = await encryptDocument(restored, scheduled.key, scheduled.userId, head.id, head.revision + 1)
    ensureCurrent(scheduled, epoch)
    const input: VaultWrite = { id: head.id, expectedRevision: head.revision, mutationId: crypto.randomUUID(), format: 1, ...encrypted }
    sessionStorage.setItem(storageKey(scheduled.userId), JSON.stringify(input))
    operation = null; emit()
    await sendPending(scheduled, epoch, input)
  })
  queue = work.catch(() => undefined)
  return work
}
export async function refreshVault() {
  loading ??= (async () => {
    const session = requireSession(), epoch = generation
    const { data } = await http.get<{ vault: VaultEnvelope | null }>('/private-finance')
    ensureCurrent(session, epoch)
    const head = data.vault
    if (!head?.isActive || head.id !== session.head.id || head.revision < session.head.revision) throw new Error('Сервер вернул другую или более старую версию. История не изменена.')
    if (head.revision === session.head.revision) {
      if (head.ciphertext !== session.head.ciphertext || head.nonce !== session.head.nonce) throw new Error('Версия хранилища повреждена.')
      return
    }
    const document = await decryptDocument(head, session.key, session.userId)
    ensureCurrent(session, epoch)
    session.head = head; session.document = document
    if (readPending(session.userId)?.mutationId === head.mutationId) clearPending(session.userId)
    emit()
  })().finally(() => { loading = null })
  return loading
}
async function sendPending(session: OpenSession, epoch: number, input: VaultWrite) {
  if (input.id !== session.head.id) throw new Error('Несохранённая копия относится к другому хранилищу. Скачайте её отдельно.')
  const draft = await decryptDocument({ ...input, revision: input.expectedRevision + 1, isActive: true, updatedAtUtc: '', format: 1 }, session.key, session.userId)
  const { data } = await http.put<VaultEnvelope>('/private-finance', input)
  ensureCurrent(session, epoch)
  if (data.mutationId !== input.mutationId || data.revision !== input.expectedRevision + 1 || data.ciphertext !== input.ciphertext || data.nonce !== input.nonce) throw new Error('Не удалось подтвердить сохранённую версию.')
  session.head = data; session.document = draft; clearPending(session.userId); emit()
}
export async function retryPendingSave() {
  const scheduled = requireSession(), scheduledEpoch = generation
  const work = queue.then(async () => {
    ensureCurrent(scheduled, scheduledEpoch)
    const session = requireSession(), epoch = generation, pending = readPending(session.userId)
    if (pending) await sendPending(session, epoch, pending)
  })
  queue = work.catch(() => undefined)
  return work
}
export async function loadServerVersion() {
  const scheduled = requireSession(), epoch = generation
  const work = queue.then(async () => {
    ensureCurrent(scheduled, epoch)
    const { data } = await http.get<{ vault: VaultEnvelope | null }>('/private-finance')
    const head = data.vault
    if (!head?.isActive || head.id !== scheduled.head.id || head.revision < scheduled.head.revision) throw new Error('Не удалось проверить серверную версию.')
    const document = await decryptDocument(head, scheduled.key, scheduled.userId)
    ensureCurrent(scheduled, epoch)
    scheduled.head = head; scheduled.document = document
    clearPending(scheduled.userId); operation = null; emit()
  })
  queue = work.catch(() => undefined)
  return work
}
export async function mutateFinancial<T>(identity: string, change: (draft: FinancialDocument) => T | Promise<T>): Promise<T> {
  const scheduled = requireSession(), scheduledEpoch = generation
  const work = queue.then(async () => {
    ensureCurrent(scheduled, scheduledEpoch)
    const session = requireSession(), epoch = generation
    const pending = readPending(session.userId)
    if (pending) {
      if (operation?.identity !== identity) throw new Error('Есть неподтверждённое сохранение. Повторите его или скачайте зашифрованную копию в профиле.')
      await sendPending(session, epoch, pending)
      const result = operation.result as T; operation = null; return result
    }
    // A lost response may have been acknowledged by a later refresh.
    if (operation?.identity === identity) { const result = operation.result as T; operation = null; return result }
    operation = null
    await refreshVault()
    ensureCurrent(session, epoch)
    const draft = structuredClone(session.document)
    const result = await change(draft)
    const encrypted = await encryptDocument(draft, session.key, session.userId, session.head.id, session.head.revision + 1)
    ensureCurrent(session, epoch)
    const input: VaultWrite = { id: session.head.id, expectedRevision: session.head.revision, mutationId: crypto.randomUUID(), format: 1, ...encrypted }
    // Only ciphertext survives reload, never the key, operation text, or decrypted draft.
    sessionStorage.setItem(storageKey(session.userId), JSON.stringify(input))
    operation = { identity, result }; emit()
    await sendPending(session, epoch, input)
    operation = null
    return result
  })
  queue = work.catch(() => undefined)
  return work
}
export function exportEncryptedBackup() {
  const session = requireSession(), pending = readPending(session.userId)
  return downloadJson(`finance-private-${new Date().toISOString().slice(0, 10)}.json`, {
    format: 'FinanceTrackerEncryptedBackup', version: 1, userId: session.userId,
    vault: session.head, pending: pending ? { ...pending, revision: pending.expectedRevision + 1, isActive: true, updatedAtUtc: '' } : null,
  })
}
