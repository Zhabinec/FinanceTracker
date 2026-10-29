interface Window {
  financeStartup?: {
    policyRequest: Promise<{ status: number; data: unknown }>
    telegramReady?: Promise<void>
  }
}
