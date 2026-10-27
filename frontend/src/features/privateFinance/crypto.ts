import { parseDocument, type FinancialDocument, type VaultEnvelope } from './model.ts'

export const MAX_DOCUMENT_BYTES = 2_000_000
export function toBase64(bytes: Uint8Array) {
  let result = ''
  for (let i = 0; i < bytes.length; i += 32768) result += String.fromCharCode(...bytes.subarray(i, i + 32768))
  return btoa(result)
}
export function fromBase64(value: string) { return Uint8Array.from(atob(value), c => c.charCodeAt(0)) }
const buffer = (bytes: Uint8Array) => Uint8Array.from(bytes).buffer
export async function fingerprint(value: string) {
  return [...new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(value)))].map(b => b.toString(16).padStart(2, '0')).join('').toUpperCase()
}
export async function newRecoveryCode() {
  const raw = crypto.getRandomValues(new Uint8Array(32))
  const encoded = toBase64(raw).replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', '')
  const checksum = (await fingerprint(encoded)).slice(0, 8)
  return `FT1.${encoded}.${checksum}`
}
export async function keyFromCode(input: string) {
  const code = input.replace(/\s/g, '')
  const parts = code.split('.')
  if (parts.length !== 3 || parts[0] !== 'FT1' || !/^[A-Za-z0-9_-]{43}$/.test(parts[1]) ||
    (await fingerprint(parts[1])).slice(0, 8) !== parts[2]) throw new Error('Проверьте код восстановления: он скопирован не полностью или с ошибкой.')
  const raw = fromBase64(parts[1].replaceAll('-', '+').replaceAll('_', '/') + '=')
  return crypto.subtle.importKey('raw', buffer(raw), 'AES-GCM', false, ['encrypt', 'decrypt'])
}
function aad(userId: string, id: string, revision: number) {
  if (!Number.isSafeInteger(revision) || revision < 1) throw new Error('Некорректная ревизия хранилища.')
  return new TextEncoder().encode(`FinanceTracker:private:v1:${userId}:${id}:${revision}`)
}
export async function encryptDocument(document: FinancialDocument, key: CryptoKey, userId: string, id: string, revision: number) {
  const bytes = new TextEncoder().encode(JSON.stringify(parseDocument(document)))
  if (bytes.byteLength > MAX_DOCUMENT_BYTES) throw new Error('Хранилище достигло лимита 2 МБ. Скачайте резервную копию.')
  const nonce = crypto.getRandomValues(new Uint8Array(12))
  const encrypted = await crypto.subtle.encrypt({ name: 'AES-GCM', iv: nonce, additionalData: aad(userId, id, revision), tagLength: 128 }, key, bytes)
  return { nonce: toBase64(nonce), ciphertext: toBase64(new Uint8Array(encrypted)) }
}
export async function decryptDocument(envelope: VaultEnvelope, key: CryptoKey, userId: string) {
  if (!envelope || envelope.format !== 1 || typeof envelope.ciphertext !== 'string' || typeof envelope.nonce !== 'string' || envelope.ciphertext.length > 2_666_688 || envelope.nonce.length !== 16) throw new Error('Неподдерживаемое или повреждённое хранилище.')
  let bytes: ArrayBuffer
  try {
    bytes = await crypto.subtle.decrypt({ name: 'AES-GCM', iv: buffer(fromBase64(envelope.nonce)), additionalData: aad(userId, envelope.id, envelope.revision), tagLength: 128 }, key, buffer(fromBase64(envelope.ciphertext)))
  } catch { throw new Error('Код не подходит к этому хранилищу или зашифрованная копия повреждена. Исходные данные не изменены.') }
  if (bytes.byteLength > MAX_DOCUMENT_BYTES) throw new Error('Хранилище превышает допустимый размер.')
  try { return parseDocument(JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(bytes))) }
  catch { throw new Error('Финансовая копия повреждена или использует неподдерживаемый формат. Исходная история не изменена.') }
}
export function backupEnvelope(value: unknown, userId: string, id: string): VaultEnvelope {
  if (!value || typeof value !== 'object') throw new Error('Некорректный файл резервной копии.')
  const backup = value as { format?: string; version?: number; userId?: string; vault?: VaultEnvelope; pending?: VaultEnvelope }
  const envelope = backup.pending ?? backup.vault
  if (backup.format !== 'FinanceTrackerEncryptedBackup' || backup.version !== 1 || backup.userId !== userId || envelope?.id !== id)
    throw new Error('Выберите зашифрованную копию этого аккаунта и хранилища.')
  return envelope
}
export function downloadJson(name: string, value: unknown) {
  const url = URL.createObjectURL(new Blob([JSON.stringify(value)], { type: 'application/json' }))
  const link = document.createElement('a'); link.href = url; link.download = name; link.click()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
