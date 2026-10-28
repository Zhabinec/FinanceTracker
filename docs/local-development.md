# Локальная разработка

Все команды ниже выполняются из корня клона, кроме блоков с явным переходом в frontend.

## Требования

- .NET SDK 10.
- Node.js 24 и npm.
- Docker Engine с Linux containers и Docker Compose v2.
- PowerShell 7 для приведённых команд и генератора локального JWT-ключа.

Docker нужен для контейнерного запуска и PostgreSQL integration-тестов. Разработка из исходников может использовать самостоятельно установленный PostgreSQL 17.

## Полный контейнерный запуск

~~~powershell
pwsh -NoProfile -File scripts/init-local.ps1
docker compose config --quiet
docker compose build api
docker compose build frontend
docker compose up -d --no-build
docker compose ps -a
~~~

init-local.ps1 создаёт .env со случайным JWT_SIGNING_KEY, если файла ещё нет. Если .env уже существует, убедитесь, что ключ в нём заполнен. Внешние сервисы Google, Telegram и Resend для локального email-сценария не нужны.

Порядок старта: healthy PostgreSQL → успешный migrate → api → frontend. migrate должен завершиться с кодом 0; это одноразовый сервис применения EF Core migrations.

| Компонент | Адрес |
|---|---|
| Web и API через nginx | http://localhost:5180 |
| Регистрация для Mailpit | http://localhost:5180/register |
| Парольный вход | http://localhost:5180/login/password |
| Почта Mailpit | http://localhost:8025 |
| Health API + БД | http://localhost:5180/health |
| PostgreSQL для локальных инструментов | localhost:5433 |

По умолчанию БД называется finance_tracker, пользователь — finance, пароль разработки — finance_dev. Эти значения предназначены только для локального окружения.

Для первого входа зарегистрируйтесь, откройте письмо в Mailpit, подтвердите email и войдите через /login/password. Затем сохраните финансовый код и настройте PIN.

## Запуск API и frontend из исходников

### 1. База и почта

~~~powershell
pwsh -NoProfile -File scripts/init-local.ps1
docker compose up -d postgres mailpit
~~~

Если полный Docker-стек уже запущен, остановите его api и frontend, чтобы не путать версии приложения:

~~~powershell
docker compose stop api frontend
~~~

### 2. Локальные секреты

~~~powershell
$financeKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
dotnet user-secrets set 'Jwt:SigningKey' $financeKey --project backend/src/Finance.Api
dotnet user-secrets set 'Http:RedirectToHttps' 'false' --project backend/src/Finance.Api
dotnet user-secrets set 'Auth:FrontendUrl' 'http://localhost:5173' --project backend/src/Finance.Api
~~~

User Secrets находятся вне репозитория, но не являются зашифрованным хранилищем. Не выводите их содержимое в логи. .env автоматически читается Compose, а не dotnet run.

### 3. Миграции и API

В этом же терминале:

~~~powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet tool restore
dotnet restore
dotnet ef database update --project backend/src/Finance.Infrastructure --startup-project backend/src/Finance.Api
dotnet run --project backend/src/Finance.Api --launch-profile http
~~~

Development-конфигурация подключается к localhost:5433. Если используется другая БД, задайте ConnectionStrings:Database через User Secrets. Не направляйте миграции разработки на публичную базу.

API слушает http://localhost:5280. OpenAPI доступен в Development по http://localhost:5280/openapi/v1.json.

### 4. Frontend

В другом терминале:

~~~powershell
cd frontend
npm ci
npm run dev
~~~

Откройте http://localhost:5173/register для Mailpit-регистрации и /login/password для входа. Vite проксирует /api на localhost:5280; frontend обращается к относительным URL.

Google и Telegram настраиваются по [отдельным инструкциям](configuration.md). Произвольный локальный аккаунт не переносится автоматически в облачный контур.

## Остановка и обновление Docker-версии

~~~powershell
docker compose stop
docker compose start
~~~

После изменения исходников:

~~~powershell
docker compose build api
docker compose build frontend
docker compose up -d --no-build
~~~

Данные находятся в именованном томе PostgreSQL. Не используйте удаление томов для обычного обновления. Перед изменением схемы или данных сделайте [резервную копию](deployment.md).

## Диагностика

| Симптом | Проверка |
|---|---|
| Docker-команда не подключается | Docker Engine запущен, включён режим Linux containers |
| Ошибка JWT_SIGNING_KEY | Заполнен ключ в .env; генератор не перезаписывает существующий файл |
| Ошибка подключения к БД | PostgreSQL healthy, порт 5433 свободен, connection string относится к нужному окружению |
| Не стартует api | Посмотреть завершение migrate и сообщения об обязательной конфигурации |
| Страница не открывается | Проверить FRONTEND_PORT в .env и состояние frontend |
| Письмо не в Gmail | Mailpit не отправляет наружу; открыть localhost:8025 |
| Публичный /register возвращает вход | Email-регистрация выключена; использовать Telegram/Google |
| Старый токен письма не работает | Запросить новую ссылку и использовать письмо из того же окружения |
| 429 | Дождаться времени Retry-After, не повторять запросы циклом |

~~~powershell
docker compose ps -a
docker compose logs --tail 100 migrate api frontend postgres
~~~

Не публикуйте полный лог без проверки на секреты и персональные данные. Проверки кода выполняются [отдельными командами](testing.md); их лучше запускать последовательно.
