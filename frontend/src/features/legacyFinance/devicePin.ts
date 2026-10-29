import { fromBase64, keyFromCode, toBase64 } from './crypto.ts'

// A local convenience lock, not a replacement for the random recovery key.
// A copied browser profile can be attacked offline; the UI counter cannot prevent that.
const ITERATIONS = 600_000
const LIMIT = 5
type WrappedCode = { format: 1; salt: string; nonce: string; ciphertext: string }
type DevicePin = WrappedCode & { id: string; attempts: number }
const idFor = (userId: string, vaultId: string) => `${userId}:${vaultId}`
const aad = (userId: string, vaultId: string) => new TextEncoder().encode(`FinanceTracker:device-pin:v1:${userId}:${vaultId}`)
const buffer = (bytes: Uint8Array) => Uint8Array.from(bytes).buffer
function validatePin(pin: string) {
  if (!/^\d{6}$/.test(pin)) throw new Error('Введите PIN из 6 цифр.')
}
async function pinKey(pin: string, salt: Uint8Array) {
  validatePin(pin)
  const material = await crypto.subtle.importKey('raw', new TextEncoder().encode(pin), 'PBKDF2', false, ['deriveKey'])
  return crypto.subtle.deriveKey({ name: 'PBKDF2', hash: 'SHA-256', salt: buffer(salt), iterations: ITERATIONS }, material,
    { name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt'])
}
export async function wrapRecoveryCode(code: string, pin: string, userId: string, vaultId: string): Promise<WrappedCode> {
  await keyFromCode(code)
  const salt = crypto.getRandomValues(new Uint8Array(16)), nonce = crypto.getRandomValues(new Uint8Array(12))
  const key = await pinKey(pin, salt)
  const ciphertext = await crypto.subtle.encrypt({ name: 'AES-GCM', iv: nonce, additionalData: aad(userId, vaultId) }, key,
    new TextEncoder().encode(code.replace(/\s/g, '')))
  return { format: 1, salt: toBase64(salt), nonce: toBase64(nonce), ciphertext: toBase64(new Uint8Array(ciphertext)) }
}
export async function unwrapRecoveryCode(value: WrappedCode, pin: string, userId: string, vaultId: string) {
  if (value.format !== 1 || typeof value.salt !== 'string' || value.salt.length !== 24 ||
    typeof value.nonce !== 'string' || value.nonce.length !== 16 || typeof value.ciphertext !== 'string' || value.ciphertext.length > 160)
    throw new Error('Настройка PIN повреждена. Используйте код восстановления.')
  const salt = fromBase64(value.salt), nonce = fromBase64(value.nonce)
  if (salt.length !== 16 || nonce.length !== 12) throw new Error('Настройка PIN повреждена. Используйте код восстановления.')
  const key = await pinKey(pin, salt)
  const bytes = await crypto.subtle.decrypt({ name: 'AES-GCM', iv: buffer(nonce), additionalData: aad(userId, vaultId) }, key, buffer(fromBase64(value.ciphertext)))
  const code = new TextDecoder('utf-8', { fatal: true }).decode(bytes)
  await keyFromCode(code)
  return code
}
async function database() {
  return new Promise<IDBDatabase>((resolve, reject) => {
    const request = indexedDB.open('finance-device-unlock', 1)
    request.onupgradeneeded = () => request.result.createObjectStore('pins', { keyPath: 'id' })
    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(new Error('Браузер не разрешил сохранить PIN. Используйте код восстановления.'))
  })
}
async function update<T>(id: string, change: (record: DevicePin | undefined, store: IDBObjectStore) => T): Promise<T> {
  const db = await database()
  try {
    return await new Promise<T>((resolve, reject) => {
      const transaction = db.transaction('pins', 'readwrite'), store = transaction.objectStore('pins')
      let result: T
      const request = store.get(id)
      request.onsuccess = () => {
        try { result = change(request.result as DevicePin | undefined, store) }
        catch (error) { transaction.abort(); reject(error) }
      }
      transaction.oncomplete = () => resolve(result)
      transaction.onerror = transaction.onabort = () => reject(new Error('Не удалось прочитать настройку PIN. Используйте код восстановления.'))
    })
  } finally { db.close() }
}
export async function hasDevicePin(userId: string, vaultId: string) {
  return update(idFor(userId, vaultId), record => Boolean(record && Number.isInteger(record.attempts) && record.attempts >= 0 && record.attempts < LIMIT))
}
export async function saveDevicePin(userId: string, vaultId: string, code: string, pin: string) {
  const wrapped = await wrapRecoveryCode(code, pin, userId, vaultId), id = idFor(userId, vaultId)
  await update(id, (_, store) => { store.put({ ...wrapped, id, attempts: 0 } satisfies DevicePin) })
}
export async function removeDevicePin(userId: string, vaultId: string) {
  await update(idFor(userId, vaultId), (_, store) => { store.delete(idFor(userId, vaultId)) })
}
export async function unlockWithDevicePin(userId: string, vaultId: string, pin: string) {
  validatePin(pin)
  const id = idFor(userId, vaultId)
  // Reserve an attempt atomically before deriving a key, including between tabs.
  const reserved = await update(id, (record, store) => {
    if (!record || !Number.isInteger(record.attempts) || record.attempts < 0 || record.attempts >= LIMIT)
      throw new Error('Введите код восстановления и настройте PIN заново.')
    const next = { ...record, attempts: record.attempts + 1 }; store.put(next); return next
  })
  let code: string
  try { code = await unwrapRecoveryCode(reserved, pin, userId, vaultId) }
  catch {
    const remaining = LIMIT - reserved.attempts
    throw new Error(remaining > 0 ? `Неверный PIN. Осталось попыток: ${remaining}.` : 'Попытки закончились. Введите код восстановления и настройте PIN заново.')
  }
  await update(id, (record, store) => {
    if (!record || record.ciphertext !== reserved.ciphertext || record.nonce !== reserved.nonce)
      throw new Error('Настройка PIN изменилась. Повторите разблокировку.')
    store.put({ ...record, attempts: 0 })
  })
  return code
}
