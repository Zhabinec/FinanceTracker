# Finance Tracker

Учебное full-stack приложение для личного учёта доходов и расходов. Backend — ASP.NET Core / .NET 10, PostgreSQL и EF Core; frontend — React, TypeScript, Vite и TanStack Query.

## Возможности MVP

- Регистрация, вход, JWT, ротация refresh token и выход.
- Собственные категории доходов и расходов: создание, переименование, деактивация и восстановление.
- Создание, редактирование и удаление операций с проверкой владельца и версии записи.
- Фильтры по периоду, типу и категории; сортировка по дате и сумме; серверная пагинация.
- Dashboard: доходы, расходы, результат периода, распределение по категориям и последние операции.
- HTTP-ошибки, идентификатор запроса и структурированные серверные логи.
- Unit-, PostgreSQL integration- и браузерные тесты; конфигурация GitHub Actions.

Результат периода — доходы минус расходы. Отдельных счетов, начальных остатков, банковских интеграций и конвертации валют нет. Границы MVP определены в [PROJECT.md](PROJECT.md).

## Быстрый запуск в Docker

Нужен работающий Docker Engine с Linux containers. Выполните из корня репозитория:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/init-local.ps1
docker compose up -d --build
docker compose ps
```

Скрипт создаёт игнорируемый Git файл `.env` со случайным JWT-ключом; существующий файл не перезаписывается. Можно вместо скрипта скопировать `.env.example` в `.env` и установить собственный случайный ключ длиной не меньше 32 байт.

Откройте **http://localhost:5180** и зарегистрируйте аккаунт. Демонстрационные пользователи не создаются. Проверка готовности всей цепочки: **http://localhost:5180/health**. Если порт занят, задайте `FRONTEND_PORT` в `.env` и повторите запуск.

Порядок запуска: PostgreSQL становится healthy → сервис `migrate` применяет EF Core migrations и завершается с кодом 0 → запускаются API и nginx. `docker compose ps -a` показывает в том числе завершившийся сервис миграций.

```powershell
docker compose logs --tail 100 api migrate
docker compose stop
docker compose start
```

Данные хранятся в именованном томе `finance_postgres_data`. Обычная остановка контейнеров сохраняет их. После обновления кода используйте `docker compose up -d --build`, чтобы применились миграции и новые образы.

Compose предназначен для **локальной демонстрации**: порты привязаны к loopback, PostgreSQL использует пароль разработки, frontend работает по HTTP. Для внешнего развёртывания нужны HTTPS на доверенном reverse proxy, `Auth__SecureCookies=true`, отдельные секреты БД/JWT и резервное копирование. Не публикуйте локальную конфигурацию напрямую в интернет.

## Локальная разработка

Нужны .NET SDK 10, Node.js 24, PowerShell 7 и PostgreSQL 17 (либо Docker).

1. Создайте `.env`, как описано выше, и запустите БД: `docker compose up -d postgres`.
2. Настройте ключ API через user-secrets:

```powershell
$financeKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
dotnet user-secrets set 'Jwt:SigningKey' $financeKey --project backend/src/Finance.Api
```

3. Примените миграции и запустите API:

```powershell
dotnet tool restore
dotnet ef database update --project backend/src/Finance.Infrastructure --startup-project backend/src/Finance.Api
dotnet run --project backend/src/Finance.Api --launch-profile http
```

4. В другом терминале:

```powershell
cd frontend
npm ci
npm run dev
```

Frontend: http://localhost:5173. Vite проксирует `/api` на http://localhost:5280. Для одновременной работы с Docker-версией остановите её `api` и `frontend`, чтобы не путать окружения. База разработки настроена на порт 5433 в `appsettings.Development.json`.

OpenAPI в Development: http://localhost:5280/openapi/v1.json. JWT-ключ и production connection string не входят в репозиторий. `.env` читает Docker Compose; при `dotnet run` используйте user-secrets либо переменные окружения.

## Проверки

```powershell
dotnet build
dotnet test
docker compose config --quiet
cd frontend
npm ci
npm test
npm run lint
npm run build
npx playwright install chromium
npm run test:e2e
```

Интеграционные тесты требуют Docker и создают отдельный временный PostgreSQL-контейнер. Они не используют БД приложения. E2E требует запущенного полного приложения на http://localhost:5180; для другого адреса задайте `E2E_BASE_URL`.

На Windows с ограниченным доступом к compiler server используйте `dotnet build -m:1 -p:UseSharedCompilation=false`; затем можно отдельно запустить `dotnet test --no-build`. Не запускайте две сборки одного checkout одновременно.

GitHub Actions запускает backend build/tests, frontend tests/lint/build и Docker + Chromium сценарий. Облачный запуск workflow появится после push в GitHub; локальные проверки не являются результатом GitHub Actions.

## Структура и контракт

| Слой | Ответственность |
|---|---|
| Finance.Api | HTTP, аутентификация, cookies, DI и обработка исключений |
| Finance.Application | Сценарии, DTO, валидация и интерфейсы хранения |
| Finance.Domain | Бизнес-инварианты без EF Core и ASP.NET Core |
| Finance.Infrastructure | EF Core, PostgreSQL и миграции |
| frontend/src/features | auth, categories, transactions, dashboard |

Основные endpoints:

- `POST /api/auth/register`, `/login`, `/refresh`, `/logout`; `GET /api/users/me`.
- `GET/POST /api/categories`; `PATCH /api/categories/{id}`; `POST /api/categories/{id}/deactivate` и `/restore`.
- `GET/POST /api/transactions`; `PUT /api/transactions/{id}`; `DELETE /api/transactions/{id}?version={version}`.
- `GET /api/dashboard?from=2026-10-01&to=2026-10-31`.

Список операций принимает `from`, `to` (обе даты включительно), `categoryId`, `type=Income|Expense`, `sort=dateDesc|dateAsc|amountDesc|amountAsc`, `page` и `pageSize` (1–100). Ответ: `{ items, totalCount, page, pageSize }`. Поддерживаются даты операций 1900–9998.

Суммы в ответах — строки, например `"123.45"`, чтобы сохранить точность в JavaScript. В БД — `numeric(18,2)`, в C# — `decimal`. `occurredOn` — календарная дата по Москве; техническое время хранится в UTC. Для изменения передайте `version`; устаревшая версия даёт `409`, после чего нужно перечитать запись. Удаление также требует версии.

Access token хранится в памяти браузера, refresh token — в HttpOnly cookie. В БД хранятся хеши refresh- и reset-токенов. Обычный выход отзывает refresh token; сброс пароля меняет security stamp и немедленно отзывает все старые access- и refresh-токены.

## Восстановление пароля

На странице входа доступна ссылка «Забыли пароль?». Локальный Compose доставляет письма в Mailpit: http://localhost:8025. Это тестовый почтовый ящик, внешним адресатам письма не отправляются. Ссылка действует 30 минут, используется один раз и передаёт токен во fragment URL, чтобы он не попадал в HTTP access logs.

Endpoints: `POST /api/auth/forgot-password` (`email`) и `POST /api/auth/reset-password` (`token`, `password`). Запрос восстановления возвращает одинаковое сообщение для известного и неизвестного email и ограничен по частоте. При недоступном почтовом сервисе сервер пишет безопасную ошибку доставки, а клиент по-прежнему получает общее сообщение.

Внешняя почта: настройте `Auth__FrontendUrl` на публичный HTTPS URL, `Email__Provider=Resend`, `Email__Endpoint=https://api.resend.com/emails`, `Email__From` на подтверждённый адрес/домен и `Email__ApiKey` через секреты окружения. Адаптер использует HTTP API без SDK; фактическая внешняя доставка требует аккаунта и настройки домена у провайдера. [API Resend](https://resend.com/docs/api-reference/emails/send-email).

Разбор решений и путь запроса: [docs/architecture.md](docs/architecture.md).
