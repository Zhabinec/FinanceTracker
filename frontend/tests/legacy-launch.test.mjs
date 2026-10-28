import { test } from 'node:test'
import assert from 'node:assert/strict'
import { legacyLaunchUrl } from '../src/platform/legacyLaunch.ts'

test('legacy PIN navigation preserves only Telegram launch fields in a fragment, never recovery tokens or query data', () => {
  const url = new URL(legacyLaunchUrl('https://previous.example/app?discard=true', 'https://cdn.example',
    '#tgWebAppData=signed%26user%3D42&tgWebAppVersion=9.0&token=financial-secret', '?email=private&tgWebAppPlatform=android'))
  assert.equal(url.origin, 'https://previous.example')
  assert.equal(url.pathname, '/')
  assert.equal(url.search, '')
  assert.equal(new URLSearchParams(url.hash.slice(1)).get('tgWebAppData'), 'signed&user=42')
  assert.equal(new URLSearchParams(url.hash.slice(1)).get('tgWebAppPlatform'), 'android')
  assert.doesNotMatch(url.href, /financial-secret|private|discard/)
  for (const origin of [undefined, 'http://previous.example', 'https://cdn.example', 'https://user:pass@previous.example'])
    assert.equal(legacyLaunchUrl(origin, 'https://cdn.example', '#tgWebAppData=test', ''), null)
  assert.equal(legacyLaunchUrl('https://previous.example', 'https://cdn.example', '#token=secret', ''), null)
})
