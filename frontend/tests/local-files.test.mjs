import { test } from 'node:test'
import assert from 'node:assert/strict'
import { canSaveLocalFile, saveLocalFile } from '../src/platform/localFiles.ts'

function browser(t, navigator, document = undefined) {
  for (const [key, value] of Object.entries({ navigator, document })) {
    const before = Object.getOwnPropertyDescriptor(globalThis, key)
    Object.defineProperty(globalThis, key, { configurable: true, value })
    t.after(() => { if (before) Object.defineProperty(globalThis, key, before); else delete globalThis[key] })
  }
}
test('Telegram saves a local file through the system sheet without a server URL or download link', async t => {
  let shared
  browser(t, { canShare: data => data.files?.length === 1, share: async data => { shared = data } })
  assert.equal(canSaveLocalFile(true), true)
  assert.equal(await saveLocalFile('key.txt', 'test-only-content', 'text/plain', true), 'shared')
  assert.equal(await shared.files[0].text(), 'test-only-content')
  assert.equal(shared.files[0].name, 'key.txt')
  assert.equal(shared.url, undefined)
})
test('Telegram without file sharing reports an actionable error instead of silently clicking a blob', async t => {
  browser(t, {})
  assert.equal(canSaveLocalFile(true), false)
  assert.equal(canSaveLocalFile(false), true)
  await assert.rejects(saveLocalFile('key.txt', 'secret', 'text/plain', true), /обычном браузере/)
})
test('cancelling the system sheet never falls back to another download or reports success', async t => {
  browser(t, { canShare: () => true, share: async () => { throw new DOMException('cancelled', 'AbortError') } })
  assert.equal(await saveLocalFile('key.txt', 'secret', 'text/plain', true), 'cancelled')
})
test('browser download attaches the link before clicking, removes it and gives the host time to read the blob', async t => {
  let attached = false, removed = false, clicked = false, delay
  const link = { style: {}, click() { assert.equal(attached, true); clicked = true }, remove() { removed = true } }
  browser(t, {}, { createElement: () => link, body: { append: () => { attached = true } } })
  t.mock.method(globalThis, 'setTimeout', (_callback, timeout) => { delay = timeout; return 0 })
  t.mock.method(URL, 'createObjectURL', () => 'blob:test-only')
  assert.equal(await saveLocalFile('key.txt', 'content', 'text/plain', false), 'requested')
  assert.equal(link.download, 'key.txt')
  assert.equal(link.href, 'blob:test-only')
  assert.equal(clicked && removed, true)
  assert.equal(delay, 60_000)
})
