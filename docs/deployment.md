# Размещение и обслуживание

Текущий тестовый контур работает на [Render + Neon](render-neon.md). Ниже — альтернативный собственный Docker-хост и обслуживание данных.

## Собственный сервер

Production Compose запускает PostgreSQL, migrate, API и nginx. Наружу публикуются только 80/443; база и API находятся во внутренней сети. Mailpit не входит в этот контур.

Нужны Docker с Linux containers, домен приложения, доверенный TLS-сертификат и доступ к секретам.

### Конфигурация

1. Скопировать deploy/production.env.example в .env.production.
2. Задать APP_VERSION релиза, независимые POSTGRES_PASSWORD и JWT_SIGNING_KEY.
3. Установить PUBLIC_URL на HTTPS-origin без завершающего слеша.
4. В TLS_CERT_DIR положить fullchain.pem и privkey.pem. Настроить продление и перезагрузку nginx после обновления сертификата.
5. Заполнить EMAIL_FROM и EMAIL_API_KEY: базовый Compose требует их; тестовый Resend сохраняет ограничения доставки.
6. Добавить TELEGRAM_BOT_TOKEN и TELEGRAM_BOT_USERNAME.
7. Если нужен Google, передать environment через [Compose override](configuration.md).

Не используйте finance_dev и ключи CI на внешнем хосте. Случайный hex-пароль БД позволяет избежать разделителей connection string. Ограничьте доступ к Docker и env-файлам.

compose.yaml и compose.production.yaml — отдельные проекты с отдельными томами, а не части одной среды.

### Первый запуск

Из корня checkout на сервере:

~~~bash
docker compose --env-file .env.production -f compose.production.yaml config --quiet
docker compose --env-file .env.production -f compose.production.yaml build api
docker compose --env-file .env.production -f compose.production.yaml build frontend
docker compose --env-file .env.production -f compose.production.yaml up -d --no-build
docker compose --env-file .env.production -f compose.production.yaml ps -a
~~~

Сборки последовательные. migrate должен завершиться с кодом 0 до API. HTTPS обслуживает nginx, refresh cookie имеет Secure. PostgreSQL-порт не публикуется.

Проверьте /health, Telegram-вход, сохранение и чтение тестовой операции, Google callback при включённом провайдере, перезагрузку и разблокировку. Внешняя почта проверяется отдельно.

## Backup

Копии хранятся вне основного диска/сервера. Задайте расписание, срок хранения, контроль доступа и шифрование хранилища backup.

Дамп содержит аккаунты, метаданные, ciphertext, а до приватного переноса — открытые финансы. Он не содержит финансовый ключ пользователя.

### Compose

Из корня, PowerShell:

~~~powershell
./scripts/backup-db.ps1
./scripts/check-db-restore.ps1 -BackupFile artifacts/backups/ИМЯ.dump
~~~

Для production:

~~~powershell
./scripts/backup-db.ps1 -ComposeFile compose.production.yaml -EnvFile .env.production
./scripts/check-db-restore.ps1 -ComposeFile compose.production.yaml -EnvFile .env.production -BackupFile ПУТЬ.dump
~~~

Первый скрипт выполняет custom pg_dump, копирует файл на хост и печатает SHA256. Второй создаёт новую finance_restore_UUID, восстанавливает дамп и проверяет пользователей, legacy-операции и миграции. Основная БД не меняется; проверочная остаётся для осмотра.

Для приватных аккаунтов дополнительно проверьте private_financial_vaults и расшифровку собственного тестового хранилища через изолированное приложение. Число строк legacy transactions не доказывает полноту приватной истории.

Linux без PowerShell:

~~~bash
mkdir -p backups
chmod 700 backups
umask 077
docker compose --env-file .env.production -f compose.production.yaml exec -T postgres pg_dump -U finance -d finance_tracker -Fc > backups/finance-RELEASE.dump
sha256sum backups/finance-RELEASE.dump
~~~

Файл и checksum не заменяют тестовое восстановление.

### Neon / внешний PostgreSQL

Compose-скрипты не обслуживают Neon напрямую. Используйте PostgreSQL client tools совместимой версии.

Подключение задаётся через libpq environment PGHOST, PGPORT, PGDATABASE, PGUSER, PGSSLMODE=verify-full и PGSSLROOTCERT с доверенным корневым сертификатом, если он требуется клиенту. Пароль — через защищённый pgpass или секреты среды, не через аргумент команды. Npgsql connection string не является форматом libpq.

~~~bash
pg_dump --format=custom --file=finance-RELEASE.dump
sha256sum finance-RELEASE.dump
~~~

Для проверки выделите отдельную БД и отдельную среду приложения. При разрешённом создании БД:

~~~bash
createdb finance_restore_check
pg_restore --dbname=finance_restore_check --no-owner --no-privileges --exit-on-error finance-RELEASE.dump
~~~

Команды должны подключаться к проверочному серверу/проекту, не перезаписывать рабочую БД. Если имя занято, createdb завершится ошибкой; не удаляйте существующую БД ради продолжения.

## Обновление собственного хоста

1. Зафиксировать текущие commit и APP_VERSION.
2. Проверить CI новой версии.
3. Сделать и проверить backup, сохранить копию вне хоста.
4. Получить нужный commit, задать новый APP_VERSION, последовательно собрать api/frontend.
5. Остановить запись и применить миграции:

~~~bash
docker compose --env-file .env.production -f compose.production.yaml stop frontend api
docker compose --env-file .env.production -f compose.production.yaml up --no-build --force-recreate migrate
~~~

Продолжать только после exit code 0:

~~~bash
docker compose --env-file .env.production -f compose.production.yaml up -d --no-build api frontend
~~~

Проверить health, вход, запись, повторное чтение, версию frontend и логи. Предыдущие образы сохранять до приёмки.

## Откат и аварийное восстановление

Образ и схема БД — разные состояния. Старый образ допустим только при совместимой схеме. Не выполняйте автоматический downgrade или restore поверх рабочей базы.

1. Остановить запись и сохранить текущее состояние БД.
2. Восстановить подходящий backup в отдельную БД.
3. Проверить аккаунты, привязки, миграции и приватные envelope.
4. Подключить изолированное приложение совместимой версии.
5. Проверить разблокировку собственного тестового хранилища.
6. Переключить основной сервис и открыть доступ после проверки.

Restore теряет изменения после момента копии. Ротация JWT-ключа завершает прежние сессии. Downgrade nullable email запрещён при наличии Telegram-only пользователей.

Следите за TLS, подключением, миграциями, 429, квотами, размерами vault и успехом backup. Не публикуйте env и полные запросы с credentials в логах.

Регион сервера не меняет бизнес-календарь Europe/Moscow. [Приёмка релиза](testing.md) дополняет health-check.
