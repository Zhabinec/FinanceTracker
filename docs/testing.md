# Тестирование

## Быстрые проверки

В корне репозитория, последовательно:

~~~powershell
dotnet build -m:1 -p:UseSharedCompilation=false
dotnet test backend/tests/Finance.UnitTests/Finance.UnitTests.csproj --no-build -m:1
~~~

Unit-тесты не требуют PostgreSQL. После изменений backend полный прогон включает integration-тесты:

~~~powershell
dotnet test --no-build -m:1
~~~

Integration-тесты создают временный PostgreSQL 17 через Testcontainers. Нужен работающий Docker Engine с Linux containers. База приложения и публичная Neon не используются.

В frontend:

~~~powershell
cd frontend
npm ci
npm test
npm run lint
npm run build
~~~

Не запускайте одновременно несколько сборок одного checkout. Для машин с ограниченными ресурсами сохраняйте один worker браузерных тестов и последовательную сборку Docker/.NET.

## Браузерные тесты

Полный сценарий ожидает локальное приложение с Mailpit на http://localhost:5180. Запуск стека описан в [локальной разработке](local-development.md).

~~~powershell
cd frontend
npx playwright install chromium
npm run test:e2e
~~~

Полный finance.spec.ts создаёт тестовые аккаунты, письма и финансовые записи. Используйте отдельное локальное окружение. Не направляйте весь набор на публичный сервис с реальными данными.

Для другого **тестового** origin:

~~~powershell
$env:E2E_BASE_URL = 'http://localhost:5180'
npm run test:e2e
~~~

Сценарии auth-entry.spec.ts, recovery-code.spec.ts и expense-chart.spec.ts перехватывают API и используют синтетические данные. Они проверяют UI без рабочей БД, но требуют доступной сборки frontend. Их можно запустить через лёгкий preview после npm run build:

~~~powershell
npm run preview -- --host 127.0.0.1 --port 5181
~~~

В другом терминале из frontend:

~~~powershell
$env:E2E_BASE_URL = 'http://127.0.0.1:5181'
npx playwright test e2e/auth-entry.spec.ts e2e/recovery-code.spec.ts e2e/expense-chart.spec.ts --workers=1
~~~

Этот прогон не доказывает реальную доставку email или завершение входа у внешнего провайдера.

## Что проверяется

- Владение аккаунтами, категориями, счетами и приватным хранилищем.
- Денежная точность, балансы, переводы, бюджеты и импорт.
- Версии, идемпотентные запросы и конкурентная ротация сессий.
- Подпись и срок Telegram initData, повторная регистрация и отзыв привязки.
- Google OIDC challenge, PKCE/state/nonce и привязка существующей истории.
- Email-подтверждение, одноразовые reset-токены и разделение лимитов.
- Запрет email-регистрации без побочных эффектов.
- AES-GCM, формат документа, PIN, перенос и восстановление копий.
- Вёрстка 320–1280 px, центрирование значков, клавиатура и reduced-motion.

## CI

[GitHub Actions](https://github.com/Zhabinec/FinanceTracker/actions) запускается на push и pull request:

| Job | Содержание |
|---|---|
| secrets | Полная полученная Git-история, Gitleaks с проверкой checksum |
| backend | Restore, Release build, unit и PostgreSQL integration |
| frontend | npm ci, тесты, lint и production build |
| end-to-end (compose) | Полный локальный Compose и Chromium |
| end-to-end (render) | Сборка combined Docker image и Chromium в CI |

Job render проверяет контейнерную схему размещения в CI; он не выполняет деплой реального Render-сервиса. Telegram-сценарии используют синтетический тестовый токен и подписанный запуск, а не настоящий аккаунт.

При падении Playwright сохраняются отчёт и traces; при локальном запуске также доступны скриншоты в frontend/test-results. Эти каталоги игнорируются Git. Отчёты с реальными пользовательскими данными не публикуются.

## Документация и конфигурация

Для изменений только Markdown проверьте внутренние ссылки, пути исходников, команды и отсутствие устаревших утверждений. Для изменений Docker:

~~~powershell
docker compose config --quiet
~~~

Production Compose валидируется с отдельным заполненным env-файлом; config --quiet не запускает контейнеры. Не печатайте раскрытую конфигурацию с секретами.

## Ручная приёмка релиза

Проверяйте нужный commit SHA, а не только зелёную отметку старого workflow.

1. /health подтверждает доступность API и БД.
2. Новый пользователь начинает через настоящий Telegram Mini App.
3. Google-вход завершается в обычном браузере, callback возвращает в приложение.
4. После создания хранилища сохраняется код, работает повторная разблокировка PIN.
5. Операция видна в истории, бюджете и обзоре.
6. На другом устройстве тот же аккаунт и код открывают ту же историю.
7. Резервная копия восстанавливается только в нужное хранилище после подтверждения.
8. Сбой сети и конфликт не уничтожают неподтверждённую историю.

Автоматические имитации провайдеров и успешный health-check не заменяют эту проверку.
