import { test } from 'node:test'
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { stripTypeScriptTypes } from 'node:module'
import axios from 'axios'

// Load the real interceptors with Node's TypeScript stripping and resolved imports.
const source = stripTypeScriptTypes(await readFile(new URL('../src/api/http.ts', import.meta.url), 'utf8'))
  .replaceAll("'axios'", JSON.stringify(import.meta.resolve('axios')))
  .replaceAll("'../platform/telegram'", JSON.stringify(new URL('../src/platform/telegram.ts', import.meta.url).href))
const api = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`)

for (const status of [200, 401]) test(`a delayed ${status} financial response cannot enter another session or refresh as its owner`, async () => {
  let finish, started
  const waiting = new Promise(resolve => { started = resolve })
  api.setSessionOwner('first'); api.setAccessToken('first-token')
  const request = api.http.get('/finance/transactions', { adapter: config => {
    started()
    return new Promise((resolve, reject) => { finish = () => {
      const response = { config, status, statusText: '', headers: {}, data: { secret: 'first-owner' } }
      if (status === 401) reject(new axios.AxiosError('expired', 'ERR_BAD_REQUEST', config, null, response))
      else resolve(response)
    } })
  } })
  await waiting
  api.setSessionOwner('second'); api.setAccessToken('second-token'); finish()
  await assert.rejects(request, error => axios.isCancel(error))
  const next = await api.http.get('/finance/accounts', { adapter: async config => ({ config, status: 200, headers: {}, data: config.headers.Authorization }) })
  assert.equal(next.data, 'Bearer second-token')
})

test('a delayed refresh cannot replace the new session token', async t => {
  let finish
  const original = axios.defaults.adapter
  t.after(() => { axios.defaults.adapter = original })
  axios.defaults.adapter = config => new Promise(resolve => { finish = () => resolve({ config, status: 200, headers: {}, data: { accessToken: 'old-refresh-token' } }) })
  api.setSessionOwner('old'); api.setAccessToken('old-token')
  const refresh = api.refreshAccessToken()
  api.setSessionOwner('new'); api.setAccessToken('new-token'); finish()
  await assert.rejects(refresh, error => axios.isCancel(error))
  const next = await api.http.get('/finance/accounts', { adapter: async config => ({ config, status: 200, headers: {}, data: config.headers.Authorization }) })
  assert.equal(next.data, 'Bearer new-token')
})
