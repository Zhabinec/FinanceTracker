# Finance Tracker frontend

React + TypeScript + Vite. Функции: auth, categories, transactions, dashboard.
Полная инструкция запуска, настройки API, Docker и тестов — в [корневом README](../README.md).

```powershell
npm ci
npm run dev
```

Vite: http://localhost:5173. Прокси /api: http://localhost:5280.

```powershell
npm test
npm run lint
npm run build
npx playwright install chromium
npm run test:e2e
```

E2E ожидает запущенное приложение на http://localhost:5180. Другой адрес задаётся через E2E_BASE_URL.
Отчёты и скриншоты сохраняются в playwright-report и test-results и исключены из Git.
Деньги принимаются как строки; не преобразовывайте их в JavaScript number.
Календарные даты отображаются без преобразования в часовой пояс браузера.
