# Finance Tracker frontend

React + TypeScript + Vite. Один клиент для браузера и Telegram Mini App. Разделы: auth, categories, transactions, dashboard, accounts, budgets, imports и privateFinance.

## Запуск

Из frontend:

~~~powershell
npm ci
npm run dev
~~~

Vite: http://localhost:5173, proxy /api: http://localhost:5280. Нужен backend с PostgreSQL. [Полный запуск, секреты и миграции](../docs/local-development.md).

Публичный экран предлагает Telegram/Google. Локальная Mailpit-регистрация — /register, парольный вход — /login/password.

## Проверки

~~~powershell
npm test
npm run lint
npm run build
~~~

dist — production build. npm run preview показывает сборку, но не заменяет API.

[Playwright и CI](../docs/testing.md). playwright-report и test-results игнорируются Git.

## Поток данных

features/privateFinance/financeHttp выполняет финансовые действия через клиентский ledger и сохраняет только зашифрованный документ. Серверные /transactions и /dashboard не читают приватные суммы.

- Деньги — строки и BigInt-копейки.
- Даты — календарные, без смещения timezone браузера.
- Ключ и открытый документ — в памяти сессии.
- PIN хранит зашифрованный код в IndexedDB после явной настройки.
- Pending ciphertext может сохраняться на время жизни вкладки.
- CSS-анимации учитывают prefers-reduced-motion.

[Архитектура](../docs/architecture.md), [формат и границы приватности](../docs/decisions/private-financial-vault.md).
