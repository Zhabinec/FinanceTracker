# Конфигурация

ASP.NET Core читает appsettings, настройки окружения, User Secrets в Development и переменные окружения. В переменной окружения разделитель вложенного ключа — двойное подчёркивание: Auth:FrontendUrl → Auth__FrontendUrl.

Ниже описаны ключи приложения. Наличие переменной в .env не означает, что Compose передаст её контейнеру: она должна быть указана в environment конкретного сервиса.

## Обязательные серверные настройки

| Ключ | Назначение |
|---|---|
| ConnectionStrings__Database | Npgsql connection string PostgreSQL |
| Jwt__SigningKey | Секрет подписи JWT, минимум 32 UTF-8 байта |
| Jwt__Issuer | Издатель; по умолчанию FinanceTracker.Api |
| Jwt__Audience | Получатель; по умолчанию FinanceTracker.Client |
| Jwt__AccessTokenLifetimeMinutes | Срок access token; по умолчанию 15, допустимо 1–60 |
| Auth__RefreshTokenLifetimeDays | Срок refresh-сессии; по умолчанию 30, допустимо 1–90 |

Пример формы connection string без настоящих значений:

~~~text
Host=DB_HOST;Port=5432;Database=DB_NAME;Username=DB_USER;Password=DB_PASSWORD;SSL Mode=VerifyFull;Maximum Pool Size=10;Timeout=30;Multiplexing=false
~~~

Для приватного хранилища требуется direct PostgreSQL endpoint. Neon hostname с -pooler и Npgsql Multiplexing не поддерживаются: session advisory locks должны оставаться на одном физическом соединении.

## HTTP и запуск

| Ключ | Поведение |
|---|---|
| ASPNETCORE_ENVIRONMENT | Development для исходников, Production для хостинга |
| Auth__FrontendUrl | Базовый URL ссылок email; локально явно задайте origin frontend |
| Auth__SecureCookies | Secure для refresh cookie; по умолчанию true вне Development |
| Http__RedirectToHttps | Перенаправление HTTP; false за TLS-прокси и для локального HTTP |
| Database__MigrateOnly | Применить миграции и завершить процесс |
| Database__MigrateOnStartup | Применить миграции перед запуском API |
| Hosting__UseRenderExternalUrl | Брать URL приложения из RENDER_EXTERNAL_URL |
| RENDER_EXTERNAL_URL | Публичный origin, предоставляемый Render |
| Hosting__ServeFrontend | Отдавать frontend из wwwroot; включено в Render Dockerfile |
| ASPNETCORE_HTTP_PORTS | Порт Kestrel; Render Dockerfile задаёт 8080 |

render.yaml также задаёт PORT=8080 как настройку окружения хостинга. Приложение не преобразует произвольное значение PORT в настройку Kestrel: при изменении порта согласуйте ASPNETCORE_HTTP_PORTS и настройки сервиса.

Публичное приложение работает по HTTPS с Secure cookies. Отключение Http__RedirectToHttps за Render/nginx не отключает внешний TLS: его обслуживает прокси.

## Способы входа

| Ключ | Поведение |
|---|---|
| Telegram__BotToken | Секрет Bot API; непустое значение включает Telegram |
| Telegram__BotUsername | Username бота без @; по умолчанию PetProjectFinanceBot |
| Google__ClientId | OAuth client ID |
| Google__ClientSecret | Секрет серверного OAuth-клиента |
| Google__RedirectUri | Точный публичный /api/auth/google/callback |
| Auth__EmailRegistrationEnabled | Явное включение/отключение email-регистрации |

Google включён при заполненных ClientId и ClientSecret; callback проверяется при старте. Кнопка скрыта в Telegram WebView.

Без явного Auth__EmailRegistrationEnabled email-регистрация включена только при Email__Provider=Mailpit. При Resend она выключена. Это проверяется и frontend через /api/auth/options, и сервером перед созданием аккаунта. Настройка не отключает существующий парольный вход.

## Доставка писем

| Ключ | Значение по умолчанию / назначение |
|---|---|
| Email__Provider | Mailpit; поддерживается также Resend |
| Email__Endpoint | http://localhost:8025/api/v1/send для локального API |
| Email__From | finance@example.test; в Resend задаётся разрешённый отправитель |
| Email__ApiKey | Секрет Resend, передаётся только backend |
| Email__PreviewUrl | Ссылка интерфейса на локальный Mailpit, по умолчанию http://localhost:8025 |

В контейнерном Compose endpoint Mailpit — http://mailpit:8025/api/v1/send. Для Resend — https://api.resend.com/emails.

Текущий публичный контур использует тестового отправителя onboarding@resend.dev. Письма другим пользователям недоступны. Это не препятствует Telegram/Google-регистрации. Подключение своего домена описано в [настройке размещения](render-neon.md).

## Ограничения нагрузки

| Ключ | По умолчанию |
|---|---|
| Http__ApiPermitLimit | 600 запросов/мин на процесс |
| Http__ApiConcurrencyLimit | 8 одновременных API-запросов |
| Auth__RegistrationPermitLimit | 20 email/Telegram-регистраций за 15 минут на процесс |
| Auth__LoginPermitLimit | 15 парольных входов/мин на адрес соединения |
| Auth__RecoveryPermitLimit | 10 обращений за 15 минут на маршрут и адрес соединения |
| Telegram__AuthPermitLimit | 30 запросов/мин на маршрут и адрес соединения |

Google auth имеет отдельный фиксированный лимит 30/мин на адрес соединения; /health — 60/мин. Общие лимиты действуют до чтения пользователя из БД. Ответ при превышении — 429 с Retry-After.

Это ограничения одного экземпляра приложения. Непроверенный X-Forwarded-For не используется для выбора адреса клиента. За прокси несколько пользователей могут делить квоту. Подробнее: [безопасность](security.md).

## Docker Compose

Локальный .env использует переменные из [.env.example](../.env.example): JWT_SIGNING_KEY, FRONTEND_PORT, TELEGRAM_BOT_TOKEN и TELEGRAM_BOT_USERNAME. Внутри api/migrate они преобразуются в ключи ASP.NET Core.

Production Compose использует [deploy/production.env.example](../deploy/production.env.example). Основные переменные: APP_VERSION, POSTGRES_PASSWORD, JWT_SIGNING_KEY, PUBLIC_URL, EMAIL_FROM, EMAIL_API_KEY и TLS_CERT_DIR. Telegram-переменные можно добавить в .env.production.

Google и явный флаг email-регистрации в базовые Compose-файлы не проброшены. Для них нужен локальный override, перечисляющий environment api (для нового окружения также migrate, если настройки требуются при его старте). Пример дополнительного файла compose.auth.yaml:

~~~yaml
services:
  api:
    environment:
      Google__ClientId: ${GOOGLE_CLIENT_ID}
      Google__ClientSecret: ${GOOGLE_CLIENT_SECRET}
      Google__RedirectUri: ${GOOGLE_REDIRECT_URI}
      Auth__EmailRegistrationEnabled: "false"
~~~

Применяйте этот файл вместе с выбранной базовой конфигурацией через дополнительный -f. Секреты храните в игнорируемом env-файле. Не меняйте ключ JWT при каждом обычном обновлении.

В Render переменные задаются непосредственно в Environment сервиса; Compose там не используется.
