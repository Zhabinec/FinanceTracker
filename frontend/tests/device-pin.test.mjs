import { test } from 'node:test'
import assert from 'node:assert/strict'
import { newRecoveryCode } from '../src/features/legacyFinance/crypto.ts'
import { wrapRecoveryCode, unwrapRecoveryCode } from '../src/features/legacyFinance/devicePin.ts'

test('device PIN encrypts the recovery key, binds it to owner and vault, and authenticates the encrypted value', async () => {
  const user = crypto.randomUUID(), vault = crypto.randomUUID(), code = await newRecoveryCode()
  const wrapped = await wrapRecoveryCode(code, '193847', user, vault)
  assert.equal(await unwrapRecoveryCode(wrapped, '193847', user, vault), code)
  assert.ok(!JSON.stringify(wrapped).includes(code))
  assert.ok(!JSON.stringify(wrapped).includes('193847'))
  const second = await wrapRecoveryCode(code, '193847', user, vault)
  assert.notEqual(wrapped.salt, second.salt)
  assert.notEqual(wrapped.nonce, second.nonce)
  assert.notEqual(wrapped.ciphertext, second.ciphertext)
  await assert.rejects(unwrapRecoveryCode(wrapped, '000000', user, vault))
  await assert.rejects(unwrapRecoveryCode(wrapped, '193847', crypto.randomUUID(), vault))
  await assert.rejects(unwrapRecoveryCode(wrapped, '193847', user, crypto.randomUUID()))
  const tampered = { ...wrapped, ciphertext: (wrapped.ciphertext[0] === 'A' ? 'B' : 'A') + wrapped.ciphertext.slice(1) }
  await assert.rejects(unwrapRecoveryCode(tampered, '193847', user, vault))
})

test('device PIN rejects short, nonnumeric or oversized input and malformed records', async () => {
  const user = crypto.randomUUID(), vault = crypto.randomUUID(), code = await newRecoveryCode()
  for (const pin of ['1234', '1234567', '12a456', '１２３４５６', '12345 '])
    await assert.rejects(wrapRecoveryCode(code, pin, user, vault), /6 цифр/)
  await assert.rejects(wrapRecoveryCode('invalid recovery key', '123456', user, vault))
  await assert.rejects(unwrapRecoveryCode({ format: 2, salt: '', nonce: '', ciphertext: '' }, '123456', user, vault), /повреждена/)
  await assert.rejects(unwrapRecoveryCode({ format: 1, salt: 'A'.repeat(24), nonce: 'A'.repeat(16), ciphertext: 'A'.repeat(161) }, '123456', user, vault), /повреждена/)
})
