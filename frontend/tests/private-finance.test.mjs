import { test } from 'node:test'
import assert from 'node:assert/strict'
import { newRecoveryCode, keyFromCode, encryptDocument, decryptDocument, fromBase64, toBase64 } from '../src/features/legacyFinance/crypto.ts'
import { parseDocument } from '../src/features/legacyFinance/model.ts'

function fixture() {
  const accountId = crypto.randomUUID(), categoryId = crypto.randomUUID()
  return parseDocument({ format: 1, baseCurrency: 'RUB', accounts: [{ id: accountId, name: 'Основной счёт', currency: 'RUB', openingBalance: '0.00', isArchived: false, version: crypto.randomUUID() }],
    categories: [{ id: categoryId, name: 'Продукты', type: 'Expense', isActive: true }], transactions: [], transfers: [], budgets: [], requests: [], imports: [] })
}
test('private AES-GCM authenticates ciphertext, nonce, owner, vault id and revision; key is not extractable', async () => {
  const data = fixture(), user = crypto.randomUUID(), id = crypto.randomUUID(), code = await newRecoveryCode(), key = await keyFromCode(code)
  const encrypted = await encryptDocument(data, key, user, id, 1)
  const envelope = { ...encrypted, id, revision: 1, mutationId: crypto.randomUUID(), format: 1, isActive: true, updatedAtUtc: '' }
  assert.deepEqual(await decryptDocument(envelope, key, user), data)
  assert.notEqual((await encryptDocument(data, key, user, id, 1)).nonce, encrypted.nonce)
  const damaged = fromBase64(encrypted.ciphertext); damaged[0] ^= 1
  for (const [changed, owner] of [
    [{ ...envelope, ciphertext: toBase64(damaged) }, user], [{ ...envelope, nonce: toBase64(crypto.getRandomValues(new Uint8Array(12))) }, user],
    [{ ...envelope, id: crypto.randomUUID() }, user], [{ ...envelope, revision: 2 }, user], [envelope, crypto.randomUUID()],
  ]) await assert.rejects(decryptDocument(changed, key, owner))
  await assert.rejects(decryptDocument(envelope, await keyFromCode(await newRecoveryCode()), user))
  await assert.rejects(crypto.subtle.exportKey('raw', key))
  await assert.rejects(keyFromCode(code.slice(0, -2)))
  assert.throws(() => parseDocument({ ...data, transactions: [{ id: crypto.randomUUID() }] }))
})
test('authenticated malformed plaintext is rejected without exposing its contents in an error', async () => {
  const user = crypto.randomUUID(), id = crypto.randomUUID(), key = await keyFromCode(await newRecoveryCode())
  const nonce = crypto.getRandomValues(new Uint8Array(12)), marker = 'SECRET-INVALID-FINANCIAL-CONTENT'
  const encrypted = await crypto.subtle.encrypt({ name: 'AES-GCM', iv: nonce, additionalData: new TextEncoder().encode(`FinanceTracker:private:v1:${user}:${id}:1`), tagLength: 128 }, key, new TextEncoder().encode(marker))
  await assert.rejects(decryptDocument({ id, revision: 1, mutationId: crypto.randomUUID(), nonce: toBase64(nonce), ciphertext: toBase64(new Uint8Array(encrypted)), format: 1, isActive: true, updatedAtUtc: '' }, key, user), error => {
    assert.doesNotMatch(error.message, /SECRET/)
    assert.match(error.message, /повреждена/)
    return true
  })
})
