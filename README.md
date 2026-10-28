# Finance Tracker

[![CI](https://github.com/Zhabinec/FinanceTracker/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Zhabinec/FinanceTracker/actions)

Личный финансовый трекер для Telegram и браузера: быстрый ввод расходов, бюджеты и наглядная история. Финансовые записи шифруются на устройстве — сервер синхронизирует зашифрованный документ.

**[Открыть в Telegram](https://t.me/PetProjectFinanceBot)** · **[Попробовать в браузере](https://finance-tracker-zhabinec.onrender.com/)**

В боте нажмите **Открыть → Начать в Telegram**. В обычном браузере доступен Google. Демо работает на Render Free + Neon Free; после простоя первый запуск может занять около минуты.

![История операций на компьютере — демонстрационные данные](docs/assets/history-desktop.png)

## Что умеет

- **Быстрый ввод:** нажать категорию, ввести название и сумму. Дата подставляется автоматически.
- **История:** фильтры, сортировка, редактирование и удаление доходов и расходов.
- **Обзор:** итоги периода и круговая диаграмма расходов по категориям; без расходов диаграмма скрыта.
- **Бюджеты:** месячные лимиты по категориям с расчётом остатка и превышения.
- **Счета:** начальные остатки, архивирование, переводы и их отмена.
- **Импорт:** CSV/TSV, сопоставление колонок, предпросмотр и защита от повторной загрузки.
- **Валюты:** отображение RUB/USD/EUR по курсу без изменения исходных сумм.
- **Приватность:** клиентское шифрование, синхронизация между устройствами и зашифрованные резервные копии.

Интерфейс адаптирован для телефона и компьютера: крупные области нажатия, мобильная навигация, управление клавиатурой и анимации с поддержкой уменьшения движения.

## Стек

| Часть | Технологии |
|---|---|
| Backend | C#, ASP.NET Core / .NET 10, EF Core, PostgreSQL 17 |
| Frontend | React, TypeScript, Vite, TanStack Query, React Hook Form, Zod |
| Вход | Telegram Mini App, Google OpenID Connect, JWT и refresh-сессии |
| Шифрование | Web Crypto, AES-256-GCM, PBKDF2-SHA256 для локального PIN |
| Проверки | xUnit, PostgreSQL Testcontainers, Node test runner, Playwright, Gitleaks |
| Размещение | Docker, nginx, GitHub Actions, Render и Neon |

## Архитектура

Один слоистый backend и один React-клиент для двух платформ. Telegram открывает тот же сайт внутри Mini App; отдельного сервера бота и polling-процесса нет.

~~~mermaid
flowchart LR
    T["Telegram Mini App"] --> UI["React-клиент"]
    B["Браузер"] --> UI
    UI --> L["Финансовая модель<br/>BigInt + AES-GCM"]
    UI --> A["ASP.NET Core API"]
    L -->|"Зашифрованный документ"| A
    A --> U["Application"]
    U --> D["Domain"]
    U --> I["Infrastructure"]
    I --> DB[("PostgreSQL")]
~~~

| Каталог | Ответственность |
|---|---|
| [Finance.Api](backend/src/Finance.Api) | HTTP, аутентификация, лимиты, ошибки и health |
| [Finance.Application](backend/src/Finance.Application) | Серверные сценарии и интерфейсы хранения |
| [Finance.Domain](backend/src/Finance.Domain) | Серверные сущности и инварианты без EF Core и ASP.NET Core |
| [Finance.Infrastructure](backend/src/Finance.Infrastructure) | PostgreSQL, миграции и внешние адаптеры |
| [privateFinance](frontend/src/features/privateFinance) | Клиентский ledger, криптография, PIN и синхронизация |

Обычный серверный запрос проходит controller → Application handler → специализированный store → PostgreSQL. Контроллеры отвечают за HTTP, а схема меняется через EF Core migrations.

У приватных финансов другая граница доверия: форма → клиентский ledger → шифрование → API хранилища. Расчёты, фильтры и отчёты выполняются на клиенте; сервер проверяет владельца, размер документа и ревизию, но не может проверить арифметику внутри шифротекста.

Совместимый серверный финансовый API сохраняется для старых аккаунтов до переноса. Перенос включает экспорт → сохранение ciphertext → проверку расшифровки → активацию и удаление открытых строк в транзакции. Старые финансовые endpoints блокируются уже на стадии переноса.

## Технические решения

**Точные деньги.** Приватный ledger считает через BigInt-копейки, суммы передаются строками. Серверный финансовый код использует decimal и numeric(18,2). Результат периода — доходы минус расходы; остатки учитывают также начальный баланс и переводы.

**Конкурентные изменения.** Версии записей защищают редактирование, requestId — повторное сохранение, хеш CSV — повторный импорт. У хранилища отдельные expectedRevision и mutationId: два устройства не могут молча перезаписать одну версию. При конфликте доступны повтор, зашифрованная копия и явный возврат к версии сервера.

**Проверенный вход.** Telegram initData проверяется по подписи и сроку, Google использует серверный Authorization Code + PKCE со state/nonce. Совпадение email не объединяет аккаунты. Refresh-токены вращаются и хранятся на сервере хешированными; access JWT дополнительно проверяется по security stamp и версии Telegram-привязки.

**Защита данных.** Для каждой записи документа используется случайный nonce; AES-GCM связывает ciphertext с пользователем, хранилищем и ревизией. Код восстановления содержит случайный 256-битный ключ. Сервер не получает этот код и PIN. Локальный PIN из шести цифр шифрует код на доверенном устройстве через PBKDF2-SHA256 с 600 000 итераций.

**Целостность.** Категории и счета принадлежат пользователю; неактивные категории и архивные счета недоступны для новых операций. Переводы между счетами одной валюты не увеличивают доходы/расходы. Импорт применяется целиком после предпросмотра. Календарные даты используют Europe/Moscow, техническое время — UTC.

## Запуск локально

### Через Docker

Нужны Docker Engine с Linux containers, Compose v2 и PowerShell 7. Выполните из корня репозитория:

~~~powershell
pwsh -NoProfile -File scripts/init-local.ps1
docker compose build api
docker compose build frontend
docker compose up -d --no-build
docker compose ps -a
~~~

Скрипт создаёт .env со случайным JWT-ключом, не перезаписывая существующий файл. Порядок запуска: PostgreSQL → миграции → API и frontend.

| Адрес | Назначение |
|---|---|
| http://localhost:5180 | Приложение |
| http://localhost:5180/register | Локальная регистрация через Mailpit |
| http://localhost:5180/login/password | Вход по email и паролю |
| http://localhost:8025 | Тестовые письма |
| http://localhost:5180/health | Проверка API и соединения с БД |

Зарегистрируйтесь, подтвердите адрес по письму в Mailpit и войдите по паролю. Затем сохраните код восстановления и при желании настройте PIN. Mailpit не отправляет письма наружу.

PostgreSQL хранит данные в именованном томе; обычная остановка сохраняет их. Для диагностики: docker compose logs --tail 100 migrate api frontend. Для остановки: docker compose stop. Не удаляйте том для обычного обновления.

<details>
<summary><strong>Разработка из исходников</strong></summary>

Нужны .NET SDK 10 и Node.js 24. База и почта:

~~~powershell
pwsh -NoProfile -File scripts/init-local.ps1
docker compose up -d postgres mailpit
~~~

Если полный стек уже работает, остановите его api/frontend. В том же терминале настройте API:

~~~powershell
$financeKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
dotnet user-secrets set 'Jwt:SigningKey' $financeKey --project backend/src/Finance.Api
dotnet user-secrets set 'Http:RedirectToHttps' 'false' --project backend/src/Finance.Api
dotnet user-secrets set 'Auth:FrontendUrl' 'http://localhost:5173' --project backend/src/Finance.Api
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet tool restore
dotnet restore
dotnet ef database update --project backend/src/Finance.Infrastructure --startup-project backend/src/Finance.Api
dotnet run --project backend/src/Finance.Api --launch-profile http
~~~

В другом терминале:

~~~powershell
cd frontend
npm ci
npm run dev
~~~

Frontend: http://localhost:5173. API: http://localhost:5280. Vite проксирует /api. Development-БД — localhost:5433 с локальными настройками из appsettings.Development.json. OpenAPI: http://localhost:5280/openapi/v1.json.

Для первого локального аккаунта используйте /register, Mailpit и /login/password. .env читает Compose; запуск из исходников использует User Secrets или переменные окружения.

</details>

## Подключение провайдеров и размещение

Секреты задаются на backend через User Secrets или Environment хостинга, а не в frontend и Git.

| Настройки | Назначение |
|---|---|
| ConnectionStrings__Database, Jwt__SigningKey | PostgreSQL и подпись JWT; ключ минимум 32 байта |
| Telegram__BotToken, Telegram__BotUsername | Вход через собственного бота; username без @ |
| Google__ClientId, Google__ClientSecret, Google__RedirectUri | Серверный Google OAuth-клиент |
| Auth__SecureCookies=true | Публичные refresh cookies по HTTPS |
| Email__Provider, Email__Endpoint, Email__From, Email__ApiKey | Mailpit для разработки либо Resend |

Для локального Telegram-токена есть [configure-telegram.ps1](scripts/configure-telegram.ps1). Он проверяет бота и сохраняет User Secrets; меню и публичный URL настраиваются отдельно в BotFather. Для Docker используются TELEGRAM_BOT_TOKEN и TELEGRAM_BOT_USERNAME в .env.

Google-клиент создаётся как Web application с точным callback: https://PUBLIC_HOST/api/auth/google/callback. В Telegram WebView Google-кнопка скрыта.

Текущий контур: frontend и API в одном Render-контейнере из [deploy/render.Dockerfile](deploy/render.Dockerfile), PostgreSQL в Neon. Нужен **direct endpoint**, без transaction pooler и Multiplexing: перенос хранилища использует session advisory locks. Секреты задаются в Render Environment; Database__MigrateOnStartup=true включает миграции, Hosting__UseRenderExternalUrl=true — публичный URL Render.

main — стабильная версия; codex/platform-telegram — источник текущего Render-сервиса. Релиз публикуется после CI через Manual Deploy с проверкой SHA. [render.yaml](render.yaml) содержит Blueprint, [compose.production.yaml](compose.production.yaml) — альтернативу для собственного хоста с TLS и отдельными секретами.

<details>
<summary><strong>Почта и резервные копии</strong></summary>

Публичная email-регистрация отключена: с Resend по умолчанию она недоступна, а с Mailpit доступна для разработки. Auth__EmailRegistrationEnabled позволяет включить её явно. Тестовый Resend отправляет письма только владельцу своего аккаунта; Telegram/Google не требуют этих писем.

Существующий парольный вход сохраняется на /login/password. Полноценная внешняя почта требует подтверждённого домена отправителя в Resend. Сброс пароля аккаунта не восстанавливает финансовый ключ.

Пользовательская зашифрованная копия экспортируется в приложении; восстановление требует того же аккаунта, хранилища и кода. Новый счёт получает базовую валюту хранилища, переключатель RUB/USD/EUR меняет только отображение.

Для локального/production Compose есть [backup-db.ps1](scripts/backup-db.ps1) и [check-db-restore.ps1](scripts/check-db-restore.ps1). Второй восстанавливает дамп в отдельную БД. Для внешней Neon нужны PostgreSQL-инструменты или средства провайдера. Серверный backup не содержит пользовательский финансовый ключ; храните копии отдельно и проверяйте восстановление.

</details>

## Проверки

Из корня, последовательно:

~~~powershell
dotnet build -m:1 -p:UseSharedCompilation=false
dotnet test --no-build -m:1
~~~

Integration-тесты создают отдельный PostgreSQL через Testcontainers и требуют работающий Docker. Они не используют базу приложения.

~~~powershell
cd frontend
npm ci
npm test
npm run lint
npm run build
npx playwright install chromium
npm run test:e2e
~~~

Полный E2E ожидает локальный стек на http://localhost:5180, создаёт тестовые аккаунты и письма. Не направляйте его на рабочую БД. Для другого тестового origin задайте E2E_BASE_URL. Часть UI-сценариев полностью перехватывает API и использует синтетические зашифрованные данные.

[CI](.github/workflows/ci.yml) проверяет секреты, backend, frontend и Chromium в двух вариантах размещения: Compose и combined Render image. Это проверка образа, а не автоматический деплой сервиса. Реальный Google-вход, Telegram на устройстве и восстановление с кодом также требуют ручной приёмки.

Для локальной защиты коммитов включите git config --local core.hooksPath scripts/git-hooks. Нужен Gitleaks в PATH или в artifacts/security/gitleaks/gitleaks.exe; hook и CI маскируют найденные секреты.

## Границы проекта

- Нет банковских интеграций, платежей, обмена валют, прямого XLSX-импорта и полноценного offline-first режима.
- Отдельные Telegram- и Google-аккаунты не объединяются автоматически; подключение Google к Telegram-only аккаунту в интерфейсе не реализовано.
- Вход доказывает доступ к аккаунту, но на новом устройстве для расшифровки нужен код. Потеря кода и всех настроенных устройств не исправляется сбросом пароля.
- PIN удобен на личном устройстве, но украденный профиль браузера позволяет перебирать его офлайн. Шифрование не защищает от XSS, подмены клиентского кода и компрометации разблокированного устройства.
- Старые данные приватны только после переноса; прежние backup и WAL не шифруются задним числом. Сервер видит аккаунты и метаданные.
- Размер открытого финансового JSON ограничен 2 000 000 байт; синхронизация требует связи. Бесплатное демо не гарантирует постоянную доступность.
