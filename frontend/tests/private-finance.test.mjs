import { test } from 'node:test'
import assert from 'node:assert/strict'
import { newRecoveryCode, keyFromCode, encryptDocument, decryptDocument, fromBase64, toBase64 } from '../src/features/privateFinance/crypto.ts'
import { parseDocument } from '../src/features/privateFinance/model.ts'
import { executeFinance, cents, decimal, csvRows } from '../src/features/privateFinance/ledger.ts'

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
test('private ledger preserves maximum money precision and deleted request tombstones', async () => {
  const data = fixture(), requestId = crypto.randomUUID()
  const input = { requestId, accountId: data.accounts[0].id, categoryId: data.categories[0].id, type: 'Expense', amount: '9999999999999999.99', description: 'Приватная запись' }
  const created = await executeFinance(data, 'post', '/transactions', input)
  assert.equal((await executeFinance(data, 'get', '/accounts'))[0].balance, '-9999999999999999.99')
  assert.equal((await executeFinance(data, 'post', '/transactions', input)).id, created.id)
  await executeFinance(data, 'delete', `/transactions/${created.id}`, {}, { version: created.version })
  await assert.rejects(executeFinance(data, 'post', '/transactions', input), /уже удалена/)
  assert.equal(data.transactions.length, 0)
  assert.equal(decimal(cents('0.01') + cents('9999999999999999.98')), '9999999999999999.99')
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
test('private transfers, category budgets and version conflicts follow financial invariants', async () => {
  const data = fixture(), source = data.accounts[0]
  const target = await executeFinance(data, 'post', '/accounts', { name: 'Наличные', openingBalance: '10.00' })
  const transfer = await executeFinance(data, 'post', '/transfers', { requestId: crypto.randomUUID(), sourceAccountId: source.id, targetAccountId: target.id, amount: '25.01', description: null })
  assert.equal((await executeFinance(data, 'get', '/accounts')).find(a => a.id === target.id).balance, '35.01')
  await executeFinance(data, 'post', `/transfers/${transfer.id}/cancel`, { version: transfer.version })
  await executeFinance(data, 'post', `/transfers/${transfer.id}/cancel`, { version: transfer.version })
  assert.equal((await executeFinance(data, 'get', '/accounts')).find(a => a.id === target.id).balance, '10.00')
  await executeFinance(data, 'post', '/budgets', { month: '2026-10-01', categoryId: data.categories[0].id, limit: '20.00' })
  const transaction = await executeFinance(data, 'post', '/transactions', { accountId: source.id, categoryId: data.categories[0].id, type: 'Expense', amount: '25.01', occurredOn: '2026-10-31' })
  assert.equal((await executeFinance(data, 'get', '/budgets', {}, { month: '2026-10-01' }))[0].overspent, '5.01')
  assert.equal((await executeFinance(data, 'get', '/dashboard', {}, { from: '2026-10-01', to: '2026-10-31' })).expense, '25.01')
  await assert.rejects(executeFinance(data, 'delete', `/transactions/${transaction.id}`, {}, { version: crypto.randomUUID() }), /уже изменена/)
  await executeFinance(data, 'post', `/categories/${data.categories[0].id}/deactivate`, { version: data.categories[0].version })
  await assert.rejects(executeFinance(data, 'post', '/transactions', { categoryId: data.categories[0].id, accountId: source.id, type: 'Expense', amount: '1.00' }), /неактивна/)
  assert.equal(data.transactions.length, 1)
  const oldVersion = data.categories[0].version
  await executeFinance(data, 'patch', `/categories/${data.categories[0].id}`, { name: 'Еда', version: oldVersion })
  await assert.rejects(executeFinance(data, 'patch', `/categories/${data.categories[0].id}`, { name: 'Устаревшее изменение', version: oldVersion }), /уже изменена/)
  assert.equal(data.categories[0].name, 'Еда')
})
test('private CSV import is atomic, preserves quoted fields, and prevents repeat imports', async () => {
  const data = fixture()
  const content = 'Сумма;Категория;Дата;Описание\n25,01;Продукты;2026-10-27;"Чипсы; сыр"\n'
  const input = { content, delimiter: ';', accountId: data.accounts[0].id, defaultType: 'Expense', mapping: { amount: 0, category: 1, date: 2, description: 3, type: null } }
  assert.equal(csvRows(content, ';')[1][3], 'Чипсы; сыр')
  const invalid = { ...input, content: content + '0;Продукты;bad;Ошибка\n' }
  assert.equal((await executeFinance(data, 'post', '/imports/csv/preview', invalid)).canApply, false)
  await assert.rejects(executeFinance(data, 'post', '/imports/csv/apply', invalid))
  assert.equal(data.transactions.length, 0)
  assert.equal((await executeFinance(data, 'post', '/imports/csv/apply', input)).count, 1)
  assert.equal(data.transactions[0].description, 'Чипсы; сыр')
  await assert.rejects(executeFinance(data, 'post', '/imports/csv/apply', input), /уже импортирован/)
  await assert.rejects(executeFinance(data, 'post', '/imports/csv/apply', { ...input, content: '\uFEFF\uFEFF' + content.replaceAll('\n', '\r\n') }), /уже импортирован/)
  assert.equal(data.transactions.length, 1)
})
