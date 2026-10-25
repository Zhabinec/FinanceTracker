import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider } from './features/auth/AuthProvider'
import { CurrencyDisplayProvider } from './features/currencies/CurrencyDisplayProvider'
import './index.css'
import App from './App'

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AuthProvider><CurrencyDisplayProvider><App /></CurrencyDisplayProvider></AuthProvider>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
)
