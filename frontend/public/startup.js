// Runs independently of the React bundle so Telegram can reveal the HTML shell early.
(() => {
  const policyController = new AbortController()
  const policyTimeout = setTimeout(() => policyController.abort(), 10000)
  const policyRequest = fetch('/api/auth/options', { signal: policyController.signal, cache: 'no-store', credentials: 'include' })
    .then(async response => ({ status: response.status, data: await response.json().catch(() => null) }))
    .finally(() => clearTimeout(policyTimeout))
  void policyRequest.catch(() => undefined)

  const launch = new URLSearchParams(location.hash.slice(1))
  const query = new URLSearchParams(location.search)
  let inTelegram = Boolean(window.Telegram?.WebApp?.initData || launch.has('tgWebAppData') || query.has('tgWebAppData'))
  try { inTelegram ||= sessionStorage.getItem('finance.telegram') === 'true' } catch { /* Storage is optional. */ }
  let telegramReady
  if (inTelegram) {
    telegramReady = new Promise((resolve, reject) => {
      const reveal = () => {
        try {
          const app = window.Telegram?.WebApp
          if (!app?.initData) throw new Error('Telegram launch is unavailable')
          app.ready(); app.expand(); document.documentElement.classList.add('telegram-app'); resolve()
        } catch (error) { reject(error) }
      }
      if (window.Telegram?.WebApp) { reveal(); return }
      const script = document.createElement('script')
      const timer = setTimeout(() => { script.remove(); reject(new Error('Telegram SDK timeout')) }, 10000)
      script.src = 'https://telegram.org/js/telegram-web-app.js'
      script.onload = () => { clearTimeout(timer); script.onload = script.onerror = null; reveal() }
      script.onerror = () => { clearTimeout(timer); script.remove(); reject(new Error('Telegram SDK unavailable')) }
      document.head.append(script)
    })
    void telegramReady.catch(() => undefined)
  }
  window.financeStartup = { policyRequest, telegramReady }
})()
