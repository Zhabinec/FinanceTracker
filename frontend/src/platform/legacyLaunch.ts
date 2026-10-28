// Preserve device-local PIN access for vaults created at the old app origin.
// Only Telegram's launch fields are forwarded, in the fragment (never in the
// HTTP URL/query/referrer); financial recovery/reset tokens are not forwarded.
export function legacyLaunchUrl(origin: string | undefined, currentOrigin: string, hash: string, search: string): string | null {
  if (!origin) return null
  const target = new URL(origin)
  if (target.protocol !== 'https:' || target.username || target.password || target.origin === currentOrigin) return null
  const fragment = new URLSearchParams(hash.replace(/^#/, '')), query = new URLSearchParams(search)
  const launch = new URLSearchParams()
  for (const name of ['tgWebAppData', 'tgWebAppVersion', 'tgWebAppPlatform', 'tgWebAppThemeParams', 'tgWebAppStartParam']) {
    const value = fragment.get(name) ?? query.get(name)
    if (value !== null) launch.set(name, value)
  }
  if (!launch.has('tgWebAppData')) return null
  target.pathname = '/'; target.search = ''; target.hash = launch.toString()
  return target.href
}
