# HTTP API

Базовый префикс — /api. Frontend использует тот же origin, что и backend. OpenAPI доступен в Development по /openapi/v1.json; публичная документация API не включена автоматически в Production.

## Авторизация и ошибки

Access JWT передаётся в Authorization: Bearer. Backend проверяет подпись, срок, издателя, аудиторию, security stamp и при наличии Telegram-claim — актуальную версию привязки.

Браузер получает refresh token в HttpOnly cookie finance.refresh_token с Path=/api/auth и SameSite=Strict; на публичном хостинге включён Secure. Telegram получает собственный refresh token в JSON и хранит его в памяти клиента.

Ошибки используют Problem Details. Типовые статусы:

| Статус | Значение |
|---|---|
| 400 | Некорректный ввод или ссылка |
| 401 | Требуется вход либо сессия недействительна |
| 403 | Email не подтверждён или email-регистрация отключена |
| 404 | Ресурс отсутствует или недоступен пользователю |
| 409 | Конфликт версии, повтор с другими данными или закрытый legacy API |
| 429 | Превышен лимит; учитывать Retry-After |

Идентификатор владельца определяется из проверенной сессии, не из userId тела запроса. Секреты и токены не следует записывать в журнал запросов.

## Публичные настройки

| Метод и маршрут | Ответ |
|---|---|
| GET /auth/options | emailRegistrationEnabled |
| GET /auth/telegram/options | enabled, botUsername |
| GET /auth/google/options | enabled |
| GET /auth/email-delivery | simulated, restricted, previewUrl |
| GET /health (без /api) | Готовность API и PostgreSQL |

## Вход через Telegram

Все маршруты ниже имеют префикс /api/auth/telegram.

| Метод | Маршрут | Тело / назначение |
|---|---|---|
| POST | /login | { initData }; возвращает linkRequired и session |
| POST | /register | { initData }; явное создание или сессия уже связанного аккаунта |
| POST | /link | { initData, email, password }; привязка прежнего подтверждённого аккаунта |
| POST | /refresh | { refreshToken }; ротация Telegram-сессии |
| POST | /logout | { refreshToken }; отзыв Telegram-сессии |
| GET | /link | Текущая привязка; требуется access token |
| DELETE | /link?version=UUID | Отключение по версии; требуется access token |

session содержит accessToken, expiresAtUtc и refreshToken. Нельзя подменять проверку исходного initData отправкой Telegram user id. Единственный вход Telegram-only пользователя отключить нельзя.

## Google OIDC

| Метод и маршрут | Назначение |
|---|---|
| GET /auth/google/start | Redirect к Google, Authorization Code + PKCE |
| GET /auth/google/callback | Middleware callback; не произвольный JSON login endpoint |
| GET /auth/google/complete | Завершение через временную защищённую cookie |
| POST /auth/google/link/start | Авторизованное намерение привязать Google; возвращает url |

После входа выдаётся обычная браузерная refresh cookie, затем SPA восстанавливает access token. Google-токены не возвращаются в URL приложения и не сохраняются как финансовый ключ. Подробности: [Google](google-login.md).

## Совместимый парольный вход

| Метод и маршрут | Тело |
|---|---|
| POST /auth/register | { email, password, baseCurrency? } |
| POST /auth/login | { email, password } |
| POST /auth/refresh | Без тела, refresh cookie |
| POST /auth/logout | Без тела, refresh cookie |
| GET /users/me | Текущий пользователь; требуется access token |
| POST /auth/forgot-password | { email } |
| POST /auth/reset-password | { token, password } |
| POST /auth/resend-confirmation | { email } |
| POST /auth/confirm-email | { token } |

Регистрация по email ограничена настройкой Auth:EmailRegistrationEnabled. В текущем Resend-контуре POST /auth/register отклоняется до создания пользователя.

Запрос писем возвращает общее сообщение независимо от существования email. Reset-ссылка действует 30 минут, confirmation — 24 часа; токены одноразовые. В ссылках токен передаётся через fragment, затем явным POST. GET страницы письма сам токен не расходует.

## Приватные финансы: основной транспорт

Все маршруты имеют префикс /api/private-finance и требуют access token.

| Метод | Маршрут | Назначение |
|---|---|---|
| GET | / | { vault: envelope или null } |
| GET | /legacy | Согласованный старый экспорт { document, fingerprint } для переноса |
| POST | /stage | Сохранить pending ciphertext с fingerprint исходной истории |
| POST | /activate | Проверить { id, revision } и завершить перенос |
| DELETE | /stage?id=UUID&revision=N | Отменить pending-перенос |
| PUT | / | Сохранить зашифрованный документ с expectedRevision |

Тело stage:

~~~json
{
  "id": "UUID",
  "mutationId": "UUID",
  "nonce": "BASE64_12_BYTES",
  "ciphertext": "BASE64_CIPHERTEXT_WITH_GCM_TAG",
  "legacyFingerprint": "SHA256_HEX",
  "format": 1
}
~~~

Тело записи:

~~~json
{
  "id": "UUID",
  "expectedRevision": 1,
  "mutationId": "UUID",
  "nonce": "BASE64_12_BYTES",
  "ciphertext": "BASE64_CIPHERTEXT_WITH_GCM_TAG",
  "format": 1
}
~~~

Ответ envelope содержит id, revision, mutationId, nonce, ciphertext, isActive, updatedAtUtc и format. Код восстановления и PIN в запрос не включаются.

Клиент шифрует запись для ожидаемой новой ревизии; AAD связывает владельца, id и revision. Сервер проверяет размер, структуру, владельца и конкуренцию. Точный повтор последней записи идемпотентен; устаревшая expectedRevision даёт 409. Историю нельзя объединять простой перезаписью JSON.

Формат и перенос: [приватное хранилище](decisions/private-financial-vault.md).

## Курсы

GET /api/currencies/rates предоставляет дату и курсы rublesPerUnit. Это данные для отображения; операции обмена валют endpoint не выполняет.

## Legacy финансовый API

Эти маршруты предназначены для аккаунтов до переноса. Как только существует staged или active vault, middleware блокирует их с 409. Для приватного аккаунта frontend financeHttp выполняет соответствующие финансовые действия локально и отправляет только зашифрованный документ.

| Ресурс | Методы |
|---|---|
| /api/accounts | GET, POST; PUT /{id} |
| /api/categories | GET, POST; PATCH /{id}; POST /{id}/deactivate и /restore |
| /api/transactions | GET, POST; PUT /{id}; DELETE /{id}?version=UUID |
| /api/transfers | GET, POST; POST /{id}/cancel |
| /api/budgets | GET, POST; PUT /{id}; DELETE /{id} с версией |
| /api/dashboard | GET с from и to |
| /api/imports/csv/preview и /apply | POST |

Денежные ответы legacy API используют строки, C# decimal и PostgreSQL numeric(18,2). Список операций принимает from/to включительно, accountId, categoryId, type, sort, page и pageSize 1–100. Формат ответа — { items, totalCount, page, pageSize }. Изменение и удаление проверяют version.

Этот API не является способом чтения расшифрованной приватной истории сервером.
