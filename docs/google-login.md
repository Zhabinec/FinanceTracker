# Вход через Google

Google используется как альтернатива Telegram в обычном браузере. Новому пользователю не нужно получать письмо Resend. Внутри Telegram WebView Google-кнопка скрыта.

## Настройка OAuth-клиента

В Google Auth Platform создайте клиент типа Web application и настройте название приложения, контактный адрес и аудиторию.

Для текущего контура точный Authorized redirect URI:

~~~text
https://finance-tracker-zhabinec.onrender.com/api/auth/google/callback
~~~

Сохраните в backend Environment:

| Ключ | Значение |
|---|---|
| Google__ClientId | Client ID созданного клиента |
| Google__ClientSecret | Его серверный секрет |
| Google__RedirectUri | Точный callback выше |

После обновления конфигурации перезапустите/переразверните сервис. GET /api/auth/google/options должен вернуть enabled=true. Отсутствие ID или секрета выключает провайдера; неверный callback при включённом провайдере останавливает старт.

Client Secret не помещается во frontend, Git и screenshots. Браузерный JavaScript SDK Google не используется. Параметры аудитории и доступность аккаунтов проверяйте в своём OAuth-проекте.

Для локальной проверки предпочтителен HTTPS-контур. Backend допускает HTTP callback только на loopback, однако Google external/correlation cookies используют Secure; простой HTTP-запуск не является гарантией прохождения всего OIDC-потока.

## Как проходит вход

~~~text
/login
  → /api/auth/google/start
  → Google: code + PKCE, state, nonce
  → /api/auth/google/callback
  → временная HttpOnly external cookie
  → /api/auth/google/complete
  → аккаунт и обычная refresh cookie
  → SPA восстанавливает access token
~~~

Официальный ASP.NET Core OIDC handler проверяет ответ и ID token. Запрашиваются openid и email. Google access/refresh/id tokens не сохраняются в финансовом хранилище, БД или URL приложения.

Временная cookie живёт пять минут и защищена Data Protection. Если контейнер перезапущен во время входа и ключи временной cookie потеряны, начните вход заново.

## Аккаунт и привязка

Основная идентичность Google — sub. Смена Google email не создаёт новую историю для уже связанного sub.

Новый sub с доступным подтверждённым email создаёт аккаунт, основной счёт и категории. Совпадение email с прежним аккаунтом не даёт автоматического доступа к нему.

Для подключения Google к существующей истории:

1. Войти в прежний аккаунт в обычном браузере.
2. Открыть **Аккаунт → Подключить Google**.
3. Завершить Google-вход в том же браузере.

Авторизованный POST создаёт намерение привязки на пять минут. При завершении сервер повторно проверяет владельца и security stamp. Google, уже связанный с другим пользователем, не перепривязывается.

Привязка Telegram-only аккаунта к браузерному Google-входу в интерфейсе не реализована. Отдельная регистрация Google не переносит его Telegram-историю.

## Финансовый ключ

Google доказывает доступ к аккаунту, но не расшифровывает финансы. На новом устройстве нужен код восстановления, на доверенном — локальный PIN. Сброс Google-пароля не заменяет финансовый код.

## Диагностика

| Симптом | Проверка |
|---|---|
| Кнопки Google нет | Обычный браузер, заполнены оба credentials, enabled=true |
| Ошибка redirect URI | Полное совпадение scheme, host и /api/auth/google/callback |
| Вход прервался после рестарта | Начать поток заново; временные cookies не являются постоянной сессией |
| Нужна привязка | Войти прежним способом и использовать действие в аккаунте |
| Не приходит reset-письмо | Google-вход не исправляет тестовое ограничение Resend |

Проверка открытия страницы выбора аккаунта не заменяет полный вход и callback. Ручная проверка включена в [приёмку релиза](testing.md).

Документация провайдеров: [ASP.NET Core OIDC](https://learn.microsoft.com/aspnet/core/security/authentication/configure-oidc-web-authentication?view=aspnetcore-10.0), [Google OAuth policies](https://developers.google.com/identity/protocols/oauth2/policies).
