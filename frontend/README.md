# Finance Tracker Frontend

Frontend личного финансового трекера на React, TypeScript и Vite.

## Реализовано

- регистрация пользователя;
- вход по email и паролю;
- клиентская валидация форм;
- JWT access token в памяти приложения;
- refresh token в `HttpOnly` cookie;
- автоматическое восстановление сессии;
- защищённый маршрут текущего пользователя;
- выход с отзывом refresh token;
- отображение ошибок backend API.

## Локальный запуск

Сначала запустите профиль `https` проекта `Finance.Api` в Visual Studio.
API должен слушать `https://localhost:7105`.

Затем из папки `frontend` выполните:

```powershell
npm.cmd run dev
```

Frontend будет доступен по адресу `http://localhost:5173`.
Vite проксирует запросы `/api` на локальный ASP.NET Core API.

## Проверка

```powershell
npm.cmd run lint
npm.cmd run build
```

## Следующий этап

Следующий вертикальный срез — управление категориями доходов и расходов:
создание, список, переименование, деактивация и восстановление.
