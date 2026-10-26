# Размещение и обслуживание

Подготовлена конфигурация одного сервера: PostgreSQL, API и nginx с frontend.
Микросервисов, брокеров и новых библиотек нет. Наружу открыты только 80/443;
PostgreSQL и API доступны внутри сети Compose. Mailpit остаётся только локально.
Публикация ещё не выполнена: нужны выбранный сервер, домен, доверенный TLS-сертификат
и доступ к почтовому провайдеру. Эти ресурсы не покупаются автоматически.

## Перед первым запуском

1. Направить DNS домена на сервер. Разрешить входящие 80/443 и административный
   доступ; не открывать PostgreSQL или порт API.
2. Скопировать `deploy/production.env.example` в `.env.production` в корне проекта.
   Заполнить отдельные случайные JWT-ключ (минимум 32 байта) и пароль БД.
   Для пароля БД использовать случайную hex-строку, чтобы исключить разделители
   connection string. Не использовать локальные секреты или тестовые значения.
3. Задать `PUBLIC_URL=https://ваш-домен` без завершающего слеша и уникальный
   `APP_VERSION` для каждого релиза. Переменные окружения контейнера доступны
   администратору хоста; ограничить доступ к Docker и файлу `.env.production`.
4. Получить доверенный сертификат для домена у хостинга или ACME-клиентом.
   В `TLS_CERT_DIR` должны быть `fullchain.pem` и `privkey.pem`. Приватный ключ
   не помещать в Git. Настроить автоматическое продление сертификата средствами
   хостинга/ACME; после обновления файлов выполнить перезагрузку nginx.
5. В Resend подтвердить домен отправителя и предусмотренные провайдером DNS-записи.
   Установить `EMAIL_FROM` и отдельный `EMAIL_API_KEY`. Код отправки уже поддерживает
   HTTP API Resend. Доставка во внешние ящики требует реальной настройки провайдера;
   локальные Mailpit-проверки её не подтверждают.

Команды выполняются на сервере из корня checkout. Не смешивать локальный
`compose.yaml` с production-файлом: это два отдельных проекта и тома данных.

```bash
docker compose --env-file .env.production -f compose.production.yaml config --quiet
docker compose --env-file .env.production -f compose.production.yaml build api
docker compose --env-file .env.production -f compose.production.yaml build frontend
docker compose --env-file .env.production -f compose.production.yaml up -d --no-build
docker compose --env-file .env.production -f compose.production.yaml ps
```

Сборки намеренно выполняются по очереди. Сервис `migrate` применяет EF migrations
перед API. Внутренний HTTP не публикуется; HTTPS завершается в nginx, а refresh
cookie имеет `Secure=true`. HTTP перенаправляет на HTTPS, кроме endpoint health
для проверки контейнера. Приложение не использует forwarded-заголовки как
доказательство личности. Авторизация работает через проверенный JWT.

После запуска проверить `https://домен/health`, регистрацию на настоящий email,
подтверждение, вход, перезагрузку страницы, восстановление пароля и одну
тестовую финансовую операцию. Проверить флаг Secure у refresh cookie.
Внешняя отправка писем и валидность сертификата проверяются на выбранном домене.

## Резервное копирование и проверка восстановления

Нужен backup до каждого обновления и регулярные копии на отдельное хранилище.
Назначить периодичность, срок хранения и ответственного; файл в том же Docker
volume не переживёт потерю сервера. Дампы содержат персональные данные:
ограничить доступ, шифровать хранилище резервных копий и контролировать срок хранения.

Для Windows/PowerShell из корня репозитория:

```powershell
./scripts/backup-db.ps1
./scripts/check-db-restore.ps1 -BackupFile artifacts/backups/ИМЯ.dump
# Для production Docker context/хоста с PowerShell:
./scripts/backup-db.ps1 -ComposeFile compose.production.yaml -EnvFile .env.production
./scripts/check-db-restore.ps1 -ComposeFile compose.production.yaml -EnvFile .env.production -BackupFile ПУТЬ.dump
```

Первый скрипт делает `pg_dump --format=custom`, копирует файл на хост и печатает
SHA256. Второй создаёт новую БД `finance_restore_<uuid>`, выполняет `pg_restore`
и читает число пользователей, операций и миграций. Основная БД не изменяется.
Проверочная БД сохраняется для осмотра; после осмотра её можно отдельно удалить,
перепроверив точное имя. Нельзя удалять или пересоздавать `finance_tracker`
для обычной проверки backup.

На Linux PowerShell не требуется; эквивалент создания дампа:

```bash
mkdir -p backups
chmod 700 backups
umask 077
docker compose --env-file .env.production -f compose.production.yaml exec -T postgres pg_dump -U finance -d finance_tracker -Fc > backups/finance-RELEASE.dump
sha256sum backups/finance-RELEASE.dump
```

Наличие файла не доказывает восстановимость. Периодически восстанавливать
его в новую базу и проверять данные и миграции. Реальное аварийное переключение
на восстановленную БД выполнять при остановленных записях и после проверки backup;
это отдельная административная операция, не действие скрипта проверки.

## Обновление

1. Сохранить номер текущего релиза и Git commit. Проверить CI/локальные тесты.
2. Сделать backup и проверить восстановление. Сохранить копию за пределами сервера.
3. Переключиться на нужный Git commit, задать новый `APP_VERSION`.
4. Последовательно собрать `api` и `frontend` командами выше.
5. Остановить `frontend` и `api` на время миграции и смены версии.
6. Выполнить миграции, затем запустить сервисы:

```bash
docker compose --env-file .env.production -f compose.production.yaml stop frontend api
docker compose --env-file .env.production -f compose.production.yaml up --no-build --force-recreate migrate
# Продолжать только после успешного завершения migrate.
docker compose --env-file .env.production -f compose.production.yaml up -d --no-build api frontend
```

Проверить health, вход и запись операции. Хранить образы предыдущего релиза до
подтверждения результата. Откат API возможен только при совместимой схеме;
не выполнять автоматически `database update` назад или восстановление поверх
рабочей БД. Возврат дампа может потерять операции после момента backup.
Для текущих двух миграций добавляются новые таблицы, старые данные не переписываются.

## Что проверено локально

Обе Compose-конфигурации проходят `docker compose config --quiet`. Nginx прошёл
`nginx -t` существующим локальным образом с временным тестовым сертификатом.
Скрипты backup/restore выполнены с локальной PostgreSQL; восстановление прошло
в отдельную базу. Это не проверка публичного домена, действительного TLS,
внешней доставки писем, мониторинга или расписания backup на будущем сервере.

Для собеседования: образ — версия приложения, volume — данные; релиз и схема
БД меняются согласованно; backup проверяется восстановлением; TLS защищает
передачу, а ownership и проверки Domain защищают правила внутри приложения.
