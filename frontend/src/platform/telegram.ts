type BackButton = { show(): void; hide(): void; onClick(callback: () => void): void; offClick(callback: () => void): void }
type TelegramWebApp = { initData: string; ready(): void; expand(): void; BackButton?: BackButton }
declare global { interface Window { Telegram?: { WebApp?: TelegramWebApp } } }

let launchError = ''
let telegramLaunch = false
export const isTelegram = () => telegramLaunch
export const telegramInitData = () => window.Telegram?.WebApp?.initData ?? ''
export const telegramLaunchError = () => launchError
export const telegramBackButton = () => window.Telegram?.WebApp?.BackButton

export async function initializeTelegram() {
  const launch = new URLSearchParams(location.hash.slice(1))
  const query = new URLSearchParams(location.search)
  telegramLaunch = Boolean(window.Telegram?.WebApp?.initData || launch.has('tgWebAppData') || query.has('tgWebAppData'))
  // Keep only the platform marker across SPA navigation/reload; the SDK owns launch parameters.
  try {
    if (telegramLaunch) sessionStorage.setItem('finance.telegram', 'true')
    else telegramLaunch = sessionStorage.getItem('finance.telegram') === 'true'
  } catch { /* Storage may be disabled by the host. Initial launches still work. */ }
  if (!telegramLaunch) return
  try {
    if (!window.Telegram?.WebApp) {
      await new Promise<void>((resolve, reject) => {
        const script = document.createElement('script')
        const timer = window.setTimeout(() => { script.remove(); reject(new Error('timeout')) }, 10000)
        script.src = 'https://telegram.org/js/telegram-web-app.js'
        script.onload = () => { clearTimeout(timer); resolve() }
        script.onerror = () => { clearTimeout(timer); reject(new Error('network')) }
        document.head.append(script)
      })
    }
    const app = window.Telegram?.WebApp
    if (!app?.initData) throw new Error('missing launch data')
    app.ready()
    app.expand()
    document.documentElement.classList.add('telegram-app')
  } catch {
    launchError = 'Не удалось открыть Telegram. Закройте приложение и откройте его снова через бота.'
  }
}
