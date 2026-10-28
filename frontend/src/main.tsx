import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider } from './features/auth/AuthProvider'
import { CurrencyDisplayProvider } from './features/currencies/CurrencyDisplayProvider'
import './index.css'
import App from './App'
import { initializeTelegram } from './platform/telegram'
import { RuntimeOptionsProvider } from './features/auth/RuntimeOptionsProvider'
import { loadRuntimePolicy } from './features/auth/runtimePolicy'

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
})

// Start waking the API while the Telegram SDK loads instead of waiting for it.
void loadRuntimePolicy().catch(() => undefined)
await initializeTelegram()
createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <RuntimeOptionsProvider><AuthProvider><CurrencyDisplayProvider><App /></CurrencyDisplayProvider></AuthProvider></RuntimeOptionsProvider>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
)
