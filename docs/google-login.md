# Google sign-in

Google-вход реализован через официальный ASP.NET Core OpenID Connect handler
10.0.11 (зависимость согласована 28.10.2026). До настройки credentials выключен.

В Google Cloud / Google Auth Platform нужен OAuth client типа Web application.
Для текущего Render-сервиса зарегистрировать точный redirect URI:

`https://finance-tracker-zhabinec.onrender.com/api/auth/google/callback`

Сохранить в Environment Render (не в Git и не в frontend):

- `Google__ClientId`
- `Google__ClientSecret`
- `Google__RedirectUri` — URI выше

Consent screen, аудитория и разрешённые тестовые пользователи настраиваются
в Google Cloud владельцем. Реальный вход не считается проверенным, пока
OAuth client не создан и владелец не прошёл Google-авторизацию.
В тестовом режиме Google могут потребоваться явно добавленные тестировщики.

## Поток

Browser → `/api/auth/google/start` → Google (code + PKCE, state/nonce)
→ `/api/auth/google/callback` (валидация middleware) → краткоживущая HttpOnly
external cookie → `/api/auth/google/complete` → Application → Store transaction
→ наша HttpOnly refresh cookie → SPA → обычное восстановление сессии.
Google access/refresh/id tokens не сохраняются в БД, URL приложения или JS storage.
Ключ финансов Google не получает. Временные cookie защищены Data Protection;
перезапуск Render во время входа может потребовать начать вход заново.

Новый sub + новый email создаёт подтверждённый аккаунт с обычными категориями
и счётом. Пароль — случайное неизвестное пользователю значение, хранится только
hash; позже пароль можно установить через email recovery при работающей почте.
Существующий sub всегда относится к прежнему user id даже при смене email Google.

Совпавший email не объединяет аккаунты. Для привязки пользователь входит обычным
способом, в разделе «Аккаунт» нажимает «Подключить Google». Авторизованный POST
создаёт защищённое намерение на 5 минут; оно переносится в защищённый OIDC state,
а callback повторно проверяет security stamp под блокировкой пользователя.
Уже связанный с другим пользователем Google не перепривязывается.
Уникальные индексы и транзакция защищают повторную регистрацию и создание сессии.

В Telegram WebView Google-кнопка скрыта: Google запрещает встроенные user agents.
Привязка и Google-вход выполняются в обычном браузере. Telegram-вход работает
по подписанному initData отдельно. Подключение OAuth не отменяет расшифровку
финансов: на доверенном устройстве доступен PIN, на новом нужен recovery code.

## Почта

Текущий `onboarding@resend.dev` обслуживает только владельца Resend.
Для других получателей нужен подтверждённый домен и адрес отправителя на нём.
Google с подтверждённым email позволяет создать аккаунт без письма Resend,
но не исправляет отправку писем восстановления пароля.

Основания:
- https://learn.microsoft.com/aspnet/core/security/authentication/configure-oidc-web-authentication?view=aspnetcore-10.0
- https://developers.google.com/identity/protocols/oauth2/policies
