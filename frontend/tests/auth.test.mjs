import { test } from 'node:test'
import assert from 'node:assert/strict'
import { AxiosError } from 'axios'
import { loginSchema, registerSchema } from '../src/features/auth/validation.ts'
import { getApiError } from '../src/api/apiError.ts'

test('email may be trimmed but password whitespace and Unicode stay unchanged', () => {
  const password = '  пароль + 12345678  '
  const parsed = registerSchema.parse({ email: ' person@example.com ', password, baseCurrency: 'RUB' })
  assert.equal(parsed.email, 'person@example.com')
  assert.equal(parsed.password, password)
})

test('long passwords are rejected rather than silently truncated', () => {
  for (const schema of [loginSchema, registerSchema]) {
    assert.equal(schema.safeParse({ email: 'person@example.com', password: 'a'.repeat(140), baseCurrency: 'RUB' }).success, false)
  }
})

test('rate limit without a response body still has an actionable message', () => {
  const failure = new AxiosError('Too Many Requests')
  failure.response = { status: 429, data: '' }
  assert.match(getApiError(failure, 'Не удалось выполнить запрос.'), /Слишком много запросов/)
})
