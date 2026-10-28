# Render + Neon

## Текущий контур

| Компонент | Настройка |
|---|---|
| Приложение | https://finance-tracker-zhabinec.onrender.com/ |
| Telegram | https://t.me/PetProjectFinanceBot → Открыть |
| Frontend и API | Один Render Web Service finance-tracker-zhabinec |
| Dockerfile | deploy/render.Dockerfile |
| Ветка публикации | codex/platform-telegram |
| База | Neon, direct PostgreSQL endpoint |
| Регион | Frankfurt для приложения и БД |
| Обновления | Ручной деплой после CI |
| Вход | Telegram и Google; email-регистрация выключена |
| Почта | Resend с тестовым отправителем, только email владельца |

Используются планы Free. Доступность, лимиты и условия проверяйте в панелях провайдеров. После простоя Render может запускаться с задержкой; это тестовое размещение без гарантии постоянной доступности.

Локальные и облачные базы независимы. Аккаунты и данные между ними автоматически не переносятся.

## Схема

deploy/render.Dockerfile собирает React в wwwroot ASP.NET Core API. Контейнер слушает 8080, frontend и API имеют общий origin. PostgreSQL расположен в Neon. HTTPS завершается на Render; refresh cookies имеют Secure.

Hosting__ServeFrontend=true установлен Dockerfile. Hosting__UseRenderExternalUrl=true берёт публичный URL из RENDER_EXTERNAL_URL. SPA index имеет no-cache, хешированные assets кешируются; отсутствующие API и файлы возвращают 404.

## Создание окружения

1. Создайте отдельный PostgreSQL-проект Neon в регионе рядом с Render.
2. Создайте Docker Web Service Render из репозитория: контекст — корень, Dockerfile — deploy/render.Dockerfile.
3. Выберите codex/platform-telegram, health /health, внутренний порт 8080.
4. Заполните Environment ниже.
5. Запустите deploy, дождитесь Live и проверьте SHA.
6. Подключите URL к кнопке Telegram и пройдите реальный вход.

render.yaml описывает Blueprint с free и autoDeployTrigger=checksPass. Текущий сервис подключён как Public Git Repository и обновляется вручную; файл Blueprint сам его настройки не меняет.

## Environment

| Ключ | Значение |
|---|---|
| ASPNETCORE_ENVIRONMENT | Production |
| Http__RedirectToHttps | false, внешний TLS обслуживает Render |
| Auth__SecureCookies | true |
| Hosting__UseRenderExternalUrl | true |
| Database__MigrateOnStartup | true |
| ConnectionStrings__Database | Npgsql connection string Neon |
| Jwt__SigningKey | Отдельный случайный ключ минимум 32 байта |
| Telegram__BotToken / Telegram__BotUsername | Токен и username без @ |
| Google__ClientId / Google__ClientSecret | Серверный OAuth-клиент |
| Google__RedirectUri | https://PUBLIC_HOST/api/auth/google/callback |
| Email__Provider | Resend |
| Email__Endpoint | https://api.resend.com/emails |
| Email__From / Email__ApiKey | Разрешённый отправитель и sending key |

Email-регистрация при Resend по умолчанию выключена. Auth__EmailRegistrationEnabled=false фиксирует это явно.

Не сохраняйте заполненные значения в Git, переписке, логах и скриншотах. При обычном релизе не генерируйте заново JWT-ключ и не меняйте БД/бота.

### Neon

Нужен Npgsql connection string, а не вставленная без преобразования PostgreSQL URI:

~~~text
Host=NEON_DIRECT_HOST;Port=5432;Database=DB_NAME;Username=DB_USER;Password=DB_PASSWORD;SSL Mode=VerifyFull;Maximum Pool Size=10;Timeout=30;Multiplexing=false
~~~

Используйте direct hostname без -pooler. Session advisory locks приватного переноса несовместимы с transaction pooling и Multiplexing; такие конфигурации отклоняются.

### Миграции

Для единственного сервиса Database__MigrateOnStartup=true применяет EF migrations до запуска. Ошибка миграции останавливает старт, а не пересоздаёт БД.

Перед изменением схемы сохраните backup и проверьте восстановление. Старый образ можно вернуть только при совместимой схеме. Downgrade nullable email запрещён при существовании Telegram-only пользователей.

## Обновление

1. Подготовить изменения в отдельной ветке и дождаться CI.
2. Объединить с main и перенести проверенную версию в codex/platform-telegram обычным merge.
3. Проверить CI публикуемого SHA.
4. Выбрать **Manual Deploy → Clear build cache & deploy**.
5. Дождаться Live и сверить SHA.
6. Проверить /health, вход и нужные пользовательские сценарии. Для frontend проверить новые assets.
7. Заново открыть Mini App для загрузки нового HTML.

Документационные изменения не требуют пересборки запущенного приложения. Push сам по себе текущий сервис не обновляет.

## Почта

Telegram/Google создают аккаунт без Resend-письма. Тестовый onboarding@resend.dev не подходит для произвольных адресатов; публичная email-регистрация закрыта, прежние почтовые маршруты сохранены.

Для полноценной почты:

1. Добавить собственный домен/поддомен в Resend.
2. Внести именно выданные Resend DNS-записи и дождаться подтверждения.
3. Настроить Email__From; не заменять существующие MX/SPF без проверки.
4. Перезапустить сервис и проверить confirmation/reset на внешнем адресе и в журнале доставки.
5. Включать Auth__EmailRegistrationEnabled только если нужен этот способ регистрации.

Домен отправителя не требует переносить сайт с onrender.com и не даёт почтовому провайдеру финансовый ключ.

## Эксплуатация

/health проверяет API и БД, но не Google callback, меню бота, доставку писем или расшифровку.

Следите за ошибками, квотами БД, размерами vault и резервными копиями. Дамп Neon создаётся PostgreSQL-инструментами или средствами провайдера; backup-db.ps1 к Neon напрямую не подключается.

[Обслуживание и backup](deployment.md), [безопасность](security.md).

Справочники: [Render Blueprint](https://render.com/docs/blueprint-spec), [Render Environment](https://render.com/docs/environment-variables), [Neon](https://neon.com/docs/introduction), [Npgsql TLS](https://www.npgsql.org/doc/security.html).
