# MotivCellScope

Система мониторинга сотовой сети: REST API на ASP.NET Core и PHP-сайт поверх него.
API работает с базой данных MariaDB (`motiv_report`): инциденты, аномалии KPI,
отчёты, справочники, пользователи и журнал аудита. Сайт — серверный PHP без
фреймворка: браузер ходит только на сайт, сайт через cURL обращается к API,
JWT хранится только в PHP-сессии и в браузер не попадает.

## Состав репозитория

| Путь | Что это |
|---|---|
| `MotivCellScopeApiServices/` | REST API (.NET 8, ASP.NET Core, EF Core + Pomelo MySQL) |
| `website/` | PHP-сайт (страницы — `public/`, общая логика — `src/`) |
| `docker-compose.yml` | Весь стек: MariaDB + API + PHP-сайт |
| `docker-compose.override.yml` | Настройки запуска из Visual Studio (Development, HTTPS-порт API) |
| `init.sql` | Полная схема базы `motiv_report` (применяется при первом старте БД) |
| `API_DOCUMENTATION.md` | Полная документация REST API: эндпоинты, роли, ошибки, сценарии |
| `UI_REQUIREMENTS.md` | Требования к интерфейсу по макетам дизайнера |
| `DEVELOPMENT_GUIDE.md` | Подробное руководство: VS Code, локальный запуск, Git, секреты |

## Стек

- **API:** .NET 8 / ASP.NET Core, JWT-аутентификация (Bearer), Swagger, BCrypt для паролей
- **БД:** MariaDB 10.1, EF Core (Pomelo.EntityFrameworkCore.MySql)
- **Сайт:** PHP 8.3 + Apache, серверный рендеринг, cURL к API, CSRF-защита форм
- **Инфраструктура:** Docker Compose, секреты — Docker Secrets (файлы `/run/secrets`)

## Быстрый старт (Docker Compose)

### 1. Создайте файл `.env` в корне репозитория

Файл в `.gitignore`, в репозиторий не попадает:

```dotenv
DB_ROOT_PASSWORD=придумайте_стойкий_пароль_root
DB_USER=admin
DB_PASSWORD=придумайте_стойкий_пароль_пользователя
JWT_SECRET_KEY=длинный_случайный_секрет_минимум_32_символа
```

### 2. Поднимите стек

```bash
docker compose up --build
```

### 3. Откройте

| Сервис | Адрес |
|---|---|
| PHP-сайт | http://localhost:8000 |
| REST API | http://localhost:8080 |
| Swagger UI | http://localhost:8080/swagger (кнопка **Authorize** — вставить JWT из `/api/auth/login`) |
| MariaDB | localhost:3306 |

Проверка здоровья API:

```bash
curl http://localhost:8080/api/health
# {"status":"ok","database":"ok","version":"1.0.0","timestamp":"..."}
```

> **Visual Studio:** `docker-compose.override.yml` добавляет API HTTPS-порт 8081
> и окружение Development. Для чистого запуска из терминала используйте основной
> compose-файл — внутри docker-сети API слушает только HTTP
> (`ASPNETCORE_URLS=http://+:8080`), иначе PHP-сайт получит редирект 307 при входе.

## Локальная разработка без Docker для API и сайта

Только база данных в Docker:

```bash
docker compose up -d motiv-db
```

API локально (.NET SDK 8):

```bash
# PowerShell
$env:db_user = "admin"; $env:db_password = "ваш_пароль_из_.env"; $env:jwt_key = "ваш_jwt_секрет_из_.env"
cd MotivCellScopeApiServices
dotnet run --launch-profile http   # http://localhost:5205, Swagger откроется сам
```

PHP-сайт при уже работающем API:

```bash
API_BASE_URL=http://localhost:8080 php -S localhost:8000 -t website/public
```

Подробности (VS Code, launch.json, проверка PHP) — в `DEVELOPMENT_GUIDE.md`
и `website/README.md`.

## Роли и доступ

| Роль | Возможности |
|---|---|
| `engineer` | Только просмотр: дашборд, инциденты, мониторинг, отчёты |
| `operator` | Просмотр + создание/редактирование/удаление инцидентов, объяснение аномалий, пересчёт KPI |
| `admin` | Всё выше + панель администратора: пользователи и журнал аудита |

Роль проверяется дважды: PHP-сайт скрывает недоступные действия, а API
окончательно решает по JWT (`401` — нет/истёк токен, `403` — роль не позволяет).

## API вкратце

Полное описание — в `API_DOCUMENTATION.md`. Основные группы:

| Группа | Базовый путь | Доступ |
|---|---|---|
| Аутентификация | `/api/auth` (`login`, `me`) | login — анонимно, me — любой JWT |
| Инциденты | `/api/incidents` (+ `/{id}`, `/export`, `/{id}/anomalies`) | чтение — все роли; изменения — operator/admin |
| Аномалии | `/api/anomalies` (+ `/{id}`, `/{id}/explain`) | чтение — все роли; explain — operator/admin |
| Мониторинг | `/api/monitoring` (`summary`, `network-elements`, `cells`, `cells/{id}/kpi`, `detect-anomalies`, `refresh-kpi`) | чтение — все роли; пересчёты — operator/admin |
| Отчёты | `/api/reports` (`summary`, `available-periods`, `export`) | все роли |
| Справочники | `/api/reference/all` | все роли |
| Администрирование | `/api/admin` (`users`, `audit`) | только admin |
| Состояние | `/api/health` | анонимно |

Все действия пользователей пишутся в журнал аудита (`AuditService` +
таблица `audit_log`), журнал доступен админу на сайте и через `/api/admin/audit`.

## Страницы сайта

| Страница | Файл | Назначение |
|---|---|---|
| Вход | `website/public/login.php` | Авторизация через `POST /api/auth/login` |
| Дашборд | `website/public/index.php` | Сводка мониторинга и инцидентов |
| Инциденты | `website/public/incidents.php`, `incident.php`, `incident_form.php` | Список, карточка, создание/редактирование |
| Мониторинг | `website/public/monitoring.php` | Аномалии: фильтры, детали, объяснение инцидентом |
| Отчёты | `website/public/reports.php` | Сводки за период, выгрузка CSV |
| Администрирование | `website/public/admin.php` | Пользователи и журнал аудита (только admin) |
| Экспорт CSV | `website/public/export.php` | Проксирование CSV из API без передачи токена браузеру |

## Частые проблемы

- **«Ошибка API (HTTP 307)» при входе на сайте** — API отвечает редиректом на
  HTTPS. В Docker API должен слушать только HTTP внутри сети: в
  `docker-compose.yml` задано `ASPNETCORE_URLS=http://+:8080`. После правок
  compose пересоздайте контейнеры: `docker compose up --build --force-recreate`.
  Для запуска из терминала не подмешивайте override с HTTPS:
  `docker compose -f docker-compose.yml up --build`.
- **«API недоступен»** — проверьте `curl http://localhost:8080/api/health`,
  `docker compose ps` и что в `.env` заданы все четыре секрета.
- **Ошибки подключения к БД** — проверьте, что `DB_USER`/`DB_PASSWORD` в `.env`
  совпадают с секретами, а том `motiv_db_data` не от старой инициализации
  (схема `init.sql` применяется только при первом создании тома).

## Безопасность

- Пароли и JWT-секрет — только в корневом `.env` (в `.gitignore`) и Docker
  Secrets; никогда не коммитьте реальные секреты.
- JWT живёт только в PHP-сессии (cookie `HttpOnly`, `SameSite=Lax`),
  все POST-формы сайта защищены CSRF-токеном, вывод экранируется.
- Если реальные пароли когда-либо попадали в git-историю — смените их.
