# MotivCellScope REST API — полная документация

Версия API: 1.0.0 · Формат: JSON (кроме CSV-выгрузок) · Кодировка: UTF-8

Документ описывает все эндпоинты сервиса MotivCellScopeApiServices: аутентификацию,
инциденты, аномалии, отчёты, справочники, администрирование (пользователи и журнал
аудита) и проверку состояния сервиса. Содержание сверено с фактической реализацией
контроллеров.

## Содержание

1. [Общие сведения](#1-общие-сведения)
2. [Аутентификация и роли](#2-аутентификация-и-роли)
3. [Общие правила](#3-общие-правила)
4. [Коды ответов и ошибки](#4-коды-ответов-и-ошибки)
5. [Аутентификация `/api/auth`](#5-аутентификация-api-auth)
6. [Инциденты `/api/incidents`](#6-инциденты-api-incidents)
7. [Аномалии `/api/anomalies`](#7-аномалии-api-anomalies)
8. [Отчёты `/api/reports`](#8-отчёты-api-reports)
9. [Справочники `/api/reference`](#9-справочники-api-reference)
10. [Администрирование `/api/admin`](#10-администрирование-api-admin)
11. [Состояние сервиса `/api/health`](#11-состояние-сервиса-api-health)
12. [Журнал аудита: действия](#12-журнал-аудита-действия)
13. [Сквозной сценарий](#13-сквозной-сценарий)
14. [Ограничения и особенности](#14-ограничения-и-особенности)
15. [Сводная таблица эндпоинтов](#15-сводная-таблица-эндпоинтов)

---

## 1. Общие сведения

### 1.1. Базовые адреса

| Окружение | Base URL | Комментарий |
|---|---|---|
| Локально, профиль `http` | `http://localhost:5205` | `MotivCellScopeApiServices/Properties/launchSettings.json` |
| Локально, профиль `https` | `https://localhost:7018` | Также поднят `http://localhost:5205` |
| Docker Compose | `http://localhost:8080` | Проброс `8080:8080` в `docker-compose.yml` |

Ниже используется обозначение `{baseUrl}` — подставьте адрес своего окружения.

### 1.2. Swagger

Только в окружении `Development` доступен Swagger UI: `{baseUrl}/swagger`.
Кнопка **Authorize** в Swagger принимает JWT-токен (см. раздел 2), после чего
защищённые эндпоинты вызываются прямо из браузера. В `Production` Swagger отключён.

### 1.3. Версионирование

Версии в URL нет (`/api/v1/...` отсутствует). Все пути начинаются с `/api`.

---

## 2. Аутентификация и роли

### 2.1. Схема JWT Bearer

1. Клиент отправляет логин/пароль на `POST /api/auth/login`.
2. Сервер сверяет пароль с BCrypt-хешем в таблице `users` и выдаёт JWT (HS256)
   сроком действия **2 часа**.
3. Токен передаётся в каждом запросе:

```http
Authorization: Bearer <token>
```

4. Refresh-токенов и logout нет: через 2 часа вход выполняется повторно,
   выданный токен до истечения срока не отзывается.

Удобно держать токен в переменной окружения:

```bash
TOKEN="eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
curl -H "Authorization: Bearer $TOKEN" "{baseUrl}/api/incidents?from=2026-11-01T00:00:00Z&to=2026-11-30T23:59:59Z"
```

### 2.2. Роли

| Роль | Возможности |
|---|---|
| `engineer` | Чтение: инциденты, аномалии, отчёты, справочники |
| `operator` | Всё от `engineer` + создание/изменение/удаление инцидентов, привязка аномалий и смена их статусов |
| `admin` | Всё от `operator` + пользователи и журнал аудита (`/api/admin/*`) |

### 2.3. Матрица доступа

| Группа | Без токена | engineer | operator | admin |
|---|:---:|:---:|:---:|:---:|
| `POST /api/auth/login` | ✅ | — | — | — |
| `GET /api/health` | ✅ | ✅ | ✅ | ✅ |
| Чтения (инциденты, аномалии, отчёты, справочники) | 401 | ✅ | ✅ | ✅ |
| POST/PUT/DELETE инцидентов | 401 | 403 | ✅ | ✅ |
| POST/PATCH аномалий | 401 | 403 | ✅ | ✅ |
| `/api/admin/*` | 401 | 403 | 403 | ✅ |

### 2.4. Учётные записи

Пользователей создаёт администратор через `POST /api/admin/users`. При первичной
инициализации БД (`init.sql`) создаются пользователи `engineer`, `operator` и
`admin` с соответствующими ролями; их пароли задаются при развёртывании и в эту
документацию не входят. Пароли в открытом виде нигде не хранятся и через API не
возвращаются — только BCrypt-хеши в БД.

---

## 3. Общие правила

| Тема | Правило |
|---|---|
| Формат тел | `Content-Type: application/json` для всех POST/PUT/PATCH |
| Имена полей | JSON в `camelCase` (`incidentId`, `beginTime`, ...) |
| Даты | ISO 8601, рекомендуется UTC с суффиксом `Z`: `2026-11-14T08:30:00Z`. Ответы с датами отдаются в том же формате; значения хранятся в UTC |
| Длительности | Строки `"HH:MM:SS"`, часы могут превышать 24: `"48:15:00"` |
| ID в пути | Целые беззнаковые: инциденты и аномалии — 64-битные, пользователи — 32-битные |
| Пагинация | Только в `GET /api/anomalies` и `GET /api/admin/audit`: `page` (по умолчанию 1), `pageSize` (по умолчанию 50). Значения `page < 1` приводятся к 1, `pageSize < 1` — к 50, `pageSize > 200` — к 200 |
| Сортировки | Инциденты — по `beginTime` ↓; аномалии — по `tsFrom` ↓; аудит — по `ts` ↓ |
| CSV-выгрузки | Разделитель `;`, кодировка UTF-8 с BOM (корректно открывается в Excel) |
| Бизнес-ошибки аудита | Сбой записи в `audit_log` не ломает основной ответ API, ошибка пишется в лог приложения |

Справочные значения (ENUM):

| Поле | Допустимые значения |
|---|---|
| `category` инцидента | `power`, `transport`, `hardware`, `software`, `planned`, `client`, `external`, `other` |
| `severity` инцидента | `low`, `medium`, `high`, `critical` |
| `type` аномалии | `threshold`, `zscore`, `missing_data`, `logical`, `cross_source` |
| `severity` аномалии | `info`, `warning`, `critical` |
| `status` аномалии | `new`, `acknowledged`, `explained`, `resolved`, `false_positive` |
| `role` пользователя | `engineer`, `operator`, `admin` |

---

## 4. Коды ответов и ошибки

| Код | Когда |
|---|---|
| `200 OK` | Успешное чтение или обновление |
| `201 Created` | Создан инцидент или пользователь (у инцидента также заголовок `Location`) |
| `204 No Content` | Инцидент удалён, тело пустое |
| `400 Bad Request` | Ошибка валидации |
| `401 Unauthorized` | Нет/невалиден/истёк токен; неверный логин или пароль |
| `403 Forbidden` | Токен валиден, но роли недостаточно |
| `404 Not Found` | Сущность не найдена |
| `409 Conflict` | Пользователь с таким именем уже существует |
| `503 Service Unavailable` | БД недоступна (только `/api/health`) |

Форматы тел ошибок (единого envelope нет, встречаются три вида):

```json
{ "error": "validation_failed", "field": "pw" }
```

```json
{ "error": "not_found" }
```

```json
{ "message": "Параметры 'from' и 'to' обязательны и должны быть в формате ISO 8601." }
```

Особенности:

- Ответы `401`/`403` от middleware авторизации имеют **пустое тело**
  (стандартное поведение JwtBearer, со статус-кодом и заголовком `WWW-Authenticate`).
- Невалидный JSON в теле запроса или неверные типы полей: `400` в формате
  ProblemDetails (`application/problem+json`) от `[ApiController]`.
- Внутренние сбои БД глобально не перехватываются — возможен `500` со стандартным
  телом ASP.NET Core.

---

## 5. Аутентификация: `/api/auth`

### 5.1. POST /api/auth/login — вход

Доступ: без токена. Успешный и неуспешный вход пишутся в журнал аудита
(`action = "login"`, `success = true/false`).

Запрос:

```json
{ "username": "operator", "password": "********" }
```

| Поле | Тип | Обязательно | Описание |
|---|---|:---:|---|
| `username` | string | да | Логин пользователя из таблицы `users` |
| `password` | string | да | Пароль в открытом виде (сверяется с BCrypt-хешем) |

```bash
curl -X POST "{baseUrl}/api/auth/login" \
  -H "Content-Type: application/json" \
  -d '{"username":"operator","password":"********"}'
```

Ответ `200 OK`:

```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresAt": "2026-11-14T10:30:00Z",
  "username": "operator",
  "role": "operator"
}
```

| Поле | Описание |
|---|---|
| `token` | JWT для заголовка `Authorization: Bearer ...` |
| `expiresAt` | Момент истечения токена (UTC, +2 часа от входа) |
| `username`, `role` | Логин и роль пользователя |

Ответ `401 Unauthorized` (пользователь не найден либо пароль неверен — ответ
намеренно одинаковый):

```json
{ "message": "Неверный логин или пароль" }
```

### 5.2. GET /api/auth/me — текущий пользователь

Доступ: любая роль с валидным токеном. Данные берутся из клеймов токена, БД не
запрашивается.

```bash
curl -H "Authorization: Bearer $TOKEN" "{baseUrl}/api/auth/me"
```

Ответ `200 OK`:

```json
{ "userId": 2, "username": "operator", "role": "operator" }
```

`401` — токен отсутствует/истёк, либо в токене нет обязательных клеймов
(`{ "message": "Некорректные данные токена" }`).

---

## 6. Инциденты: `/api/incidents`

Все эндпоинты требуют токен. Чтение — любая роль; создание, изменение и удаление —
только `operator` и `admin`.

Объект инцидента в детальном виде (ответы GET по id, POST, PUT):

| Поле | Тип | Описание |
|---|---|---|
| `incidentId` | int64 | ID инцидента |
| `pw` | string \| null | Номер PW |
| `directionId` / `directionCode` | int \| null / string \| null | Направление |
| `techCenterId` / `techCenterCode` | int \| null / string \| null | Техцентр |
| `priorityId` / `priorityCode` | int \| null / string \| null | Приоритет |
| `networkTypeId` / `networkTypeCode` | int \| null / string \| null | Тип сети |
| `equipmentTypeId` / `equipmentTypeCode` | int \| null / string \| null | Тип оборудования |
| `alarmTypeId` / `alarmTypeCode` | int \| null / string \| null | Тип аварии |
| `networkElementId` | int \| null | ID сетевого элемента |
| `networkElementName` | string \| null | Имя сетевого элемента |
| `networkElementType` | string \| null | Тип сетевого элемента |
| `servElem` | string \| null | Обслуживаемый элемент; при создании с `networkElementId` подставляется `full_name` элемента |
| `numbRbs` | string \| null | Количество БС |
| `descr` | string \| null | Описание |
| `beginTime` / `endTime` | datetime \| null | Начало/окончание; `endTime = null` — авария продолжается |
| `akb`, `dgu`, `prostoy` | string \| null | Длительности `"HH:MM:SS"` (АКБ, ДГУ, простой) |
| `lenght` | string \| null | Длительность `"D days, HH:MM:SS"`; поле исторически называется именно `lenght` |
| `commonProstoy` | string \| null | Общий простой, тот же формат, что `lenght` |
| `alarmWho` | string \| null | Кто выявил/уведомление |
| `descNotice` | string \| null | Комментарий |
| `adminDistrict` | string \| null | Административный район |
| `regionId` | int \| null | Регион |
| `severity` | string | `low` / `medium` / `high` / `critical` |
| `category` | string | См. перечень категорий в разделе 3 |
| `createdByUserId` | int \| null | Автор записи |
| `createdAt` / `updatedAt` | datetime | Создание/обновление записи (UTC) |

### 6.1. GET /api/incidents — список за период

Параметры (оба обязательны):

| Параметр | Тип | Описание |
|---|---|---|
| `from` | datetime | Начало периода (по `beginTime`, включительно) |
| `to` | datetime | Конец периода (по `beginTime`, включительно) |

Пагинации у списка нет — возвращаются все инциденты периода, сортировка по
`beginTime` по убыванию.

```bash
curl -H "Authorization: Bearer $TOKEN" \
  "{baseUrl}/api/incidents?from=2026-11-01T00:00:00Z&to=2026-11-30T23:59:59Z"
```

Ответ `200 OK`: в элементе списка вместо пар id+code только коды, автор — именем
(`createdBy`), поля `updatedAt` нет.

```json
{
  "total": 1,
  "items": [
    {
      "incidentId": 10,
      "pw": "PW-100",
      "directionCode": "URAL",
      "techCenterCode": "EKB",
      "priorityCode": "P2",
      "networkTypeCode": "LTE",
      "equipmentTypeCode": "BTS",
      "alarmTypeCode": "POWER",
      "networkElementType": "bts",
      "networkElementName": "EKB-001",
      "servElem": "БС EKB-001",
      "numbRbs": "1",
      "descr": "Отключение электропитания",
      "beginTime": "2026-11-14T06:00:00Z",
      "endTime": "2026-11-14T09:30:00Z",
      "akb": "02:00:00",
      "dgu": "01:30:00",
      "lenght": "0 days, 03:30:00",
      "prostoy": "03:30:00",
      "commonProstoy": "0 days, 03:30:00",
      "alarmWho": "NOC",
      "descNotice": null,
      "adminDistrict": "Ленинский",
      "severity": "high",
      "category": "power",
      "createdBy": "operator",
      "createdAt": "2026-11-14T06:05:00Z"
    }
  ]
}
```

Ошибки: `400 { "message": "..." }`, если `from`/`to` не заданы или `from > to`.

### 6.2. GET /api/incidents/{id} — карточка инцидента

Ответ `200 OK` — полный объект из таблицы выше (пары id+code, `createdByUserId`,
`updatedAt`). `404 { "error": "not_found" }` — инцидент не найден.

```bash
curl -H "Authorization: Bearer $TOKEN" "{baseUrl}/api/incidents/10"
```

### 6.3. POST /api/incidents — создание инцидента

Доступ: `operator`, `admin`. Тело — объект со следующими полями (все необязательны,
кроме осмысленного `beginTime`):

| Поле | Тип | Описание / правила |
|---|---|---|
| `pw` | string \| null | До 450 символов, иначе `400`, `field: "pw"` |
| `directionId`, `techCenterId`, `priorityId`, `networkTypeId`, `equipmentTypeId`, `alarmTypeId`, `regionId` | int (16 бит) \| null | ID справочников, значения берите из `GET /api/reference/all` |
| `networkElementId` | int (32 бита) \| null | Должен существовать, иначе `400`, `field: "networkElementId"`. При заданном значении `servElem` перезаписывается `full_name` элемента |
| `servElem`, `numbRbs`, `descr`, `alarmWho`, `descNotice`, `adminDistrict` | string \| null | Текстовые поля |
| `beginTime` | datetime | Начало аварии. Не позже чем «сейчас + 1 минута», иначе `400`, `field: "beginTime"` |
| `endTime` | datetime \| null | Окончание; не раньше `beginTime`, иначе `400`, `field: "endTime"` |
| `akb`, `dgu`, `prostoy` | string \| null | Длительности `"HH:MM:SS"` (часы могут быть > 24: `"48:00:00"`); невалидный формат — `400` с соответствующим `field` |
| `severity` | string | По умолчанию `medium`; список — раздел 3; невалидное — `400`, `field: "severity"` |
| `category` | string | По умолчанию `other`; список — раздел 3; невалидное — `400`, `field: "category"` |

Сервер сам вычисляет `lenght` и `commonProstoy` по паре `beginTime`/`endTime`
(или до текущего времени, если авария не окончена) и пишет событие
`create_incident` в аудит.

Пример:

```bash
curl -X POST "{baseUrl}/api/incidents" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{
    "pw": "PW-100",
    "networkElementId": 101,
    "descr": "Отключение электропитания",
    "beginTime": "2026-11-14T06:00:00Z",
    "endTime": "2026-11-14T09:30:00Z",
    "akb": "02:00:00",
    "severity": "high",
    "category": "power"
  }'
```

Ответ `201 Created` с заголовком `Location: /api/incidents/{id}` и полным
объектом инцидента (формат — начало раздела 6).

### 6.4. PUT /api/incidents/{id} — полное обновление инцидента

Доступ: `operator`, `admin`. Тело и валидация — как в POST (раздел 6.3).

Важно: это **полная замена**. Поля, не переданные в запросе, перезапишутся:
nullable-поля станут `null`, `category` — `other`, `severity` — `medium`.
`createdByUserId` и `createdAt` не изменяются; `lenght`/`commonProstoy`
пересчитываются. Пишется аудит `update_incident`.

Ответы: `200 OK` с полным объектом; `404 { "error": "not_found" }`;
`400 { "error": "validation_failed", "field": "..." }`.

```bash
curl -X PUT "{baseUrl}/api/incidents/10" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{ "descr": "Уточнённое описание", "beginTime": "2026-11-14T06:00:00Z", "endTime": "2026-11-14T10:00:00Z", "severity": "high", "category": "power" }'
```

### 6.5. DELETE /api/incidents/{id} — удаление инцидента

Доступ: `operator`, `admin`. Ответ `204 No Content` (пустое тело);
`404 { "error": "not_found" }`, если инцидента нет.

Побочный эффект: у аномалий, объяснённых этим инцидентом, сбрасывается привязка
(`explainedByIncident = null`), а статус `explained` возвращается в `new`.
Пишется аудит `delete_incident`.

```bash
curl -X DELETE -H "Authorization: Bearer $TOKEN" "{baseUrl}/api/incidents/10"
```

### 6.6. GET /api/incidents/export — выгрузка CSV

Параметры `from`, `to` обязательны (как в 6.1). Ответ — файл CSV
(`text/csv; charset=utf-8`, UTF-8 с BOM, разделитель `;`), имя файла
`incidents_YYYY-MM-DD_YYYY-MM-DD.csv` по границам периода.

Колонки: `Incident ID;PW;Направление;ТЦ;Приоритет;Тип сети;Сетевой элемент;
Кол-во БС;Тип оборуд.;Тип аварии;Описание;Начало;Окончание;АКБ;ДГУ;Длительность;
Простой;Общий простой;Уведомление;Комментарий;Район;Severity;Category`.
Даты в файле — в формате `yyyy-MM-dd HH:mm`.

```bash
curl -H "Authorization: Bearer $TOKEN" -OJ \
  "{baseUrl}/api/incidents/export?from=2026-11-01T00:00:00Z&to=2026-11-30T23:59:59Z"
```

Ошибки: `400 { "message": "..." }` при отсутствии периода или `from > to`.

### 6.7. GET /api/incidents/{id}/anomalies — аномалии инцидента

Все аномалии, объяснённые данным инцидентом, по убыванию `tsFrom`.
`404 { "error": "not_found" }`, если инцидента нет.

Ответ `200 OK` — массив (без обёртки):

```json
[
  {
    "anomalyId": 55,
    "cellId": 9001,
    "btsCode": "EKB-001",
    "sector": "A",
    "anomalyType": "threshold",
    "kpiCode": "cssr",
    "severity": "warning",
    "tsFrom": "2026-11-14T06:00:00Z",
    "tsTo": "2026-11-14T07:00:00Z",
    "observedValue": 82.5000,
    "expectedValue": 95.0000,
    "status": "explained"
  }
]
```

---

## 7. Аномалии: `/api/anomalies`

Все эндпоинты требуют токен. Чтение — любая роль; изменения — только `operator`
и `admin`. Фактический префикс — только `/api/anomalies` (встречающийся в
комментариях к коду префикс `/api/monitoring` отдельным маршрутом не является).

### 7.1. GET /api/anomalies — список аномалий

Параметры:

| Параметр | Тип | Обязательно | Описание |
|---|---|:---:|---|
| `from`, `to` | datetime | да | Период по `tsFrom`, включительно |
| `severity` | string | нет | Точное значение: `info` / `warning` / `critical` |
| `status` | string | нет | Точное значение: `new` / `acknowledged` / `explained` / `resolved` / `false_positive` |
| `type` | string | нет | Точное значение: `threshold` / `zscore` / `missing_data` / `logical` / `cross_source` |
| `kpiCode` | string | нет | Код KPI, например `cssr` |
| `regionId` | int | нет | Регион |
| `btsId` | int | нет | Базовая станция |
| `page`, `pageSize` | int | нет | Пагинация, правила — раздел 3 |

```bash
curl -H "Authorization: Bearer $TOKEN" \
  "{baseUrl}/api/anomalies?from=2026-11-01T00:00:00Z&to=2026-11-30T23:59:59Z&status=new&page=1&pageSize=50"
```

Ответ `200 OK`:

```json
{
  "total": 1,
  "page": 1,
  "pageSize": 50,
  "items": [
    {
      "anomalyId": 55,
      "detectedAt": "2026-11-14T07:05:00Z",
      "cellId": 9001,
      "btsId": 101,
      "btsCode": "EKB-001",
      "btsFullName": "БС EKB-001, Екатеринбург",
      "sector": "A",
      "regionId": 1,
      "regionCode": "SVE",
      "tsFrom": "2026-11-14T06:00:00Z",
      "tsTo": "2026-11-14T07:00:00Z",
      "type": "threshold",
      "kpiCode": "cssr",
      "severity": "warning",
      "observedValue": 82.5000,
      "expectedValue": 95.0000,
      "score": 12.5000,
      "status": "new",
      "incidentId": null,
      "incidentPw": null,
      "incidentCategory": null
    }
  ]
}
```

Ошибки: `400 { "message": "..." }`, если не задан период или `from > to`.

### 7.2. GET /api/anomalies/{id} — карточка аномалии

Ответ `200 OK` — те же поля, что элемент списка, но без
`incidentId/incidentPw/incidentCategory`: вместо них вложенный объект `incident`
(`incidentId`, `pw`, `descr`, `category`, `beginTime`) или `null`, если аномалия
не объяснена. `404 { "error": "not_found" }` — аномалия не найдена.

### 7.3. POST /api/anomalies/{id}/explain — привязка к инциденту

Доступ: `operator`, `admin`. Устанавливает аномалии указанный инцидент и статус
`explained`. Пишется аудит `explain_anomaly`.

Тело: `{ "incidentId": 10 }` — ID существующего инцидента.

```bash
curl -X POST "{baseUrl}/api/anomalies/55/explain" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"incidentId": 10}'
```

Ответ `200 OK`:

```json
{ "anomalyId": 55, "explainedByIncident": 10, "status": "explained" }
```

Ошибки:

- `404 { "error": "anomaly_not_found" }` — нет аномалии;
- `400 { "error": "validation_failed", "field": "incidentId", "message": "Указанный инцидент не существует." }`.

### 7.4. PATCH /api/anomalies/{id}/status — смена статуса

Доступ: `operator`, `admin`. Тело: `{ "status": "acknowledged" }`.
Допустимые статусы (регистронезависимо, с обрезкой пробелов):
`new`, `acknowledged`, `explained`, `resolved`, `false_positive`.

При переходе в `new` или `false_positive` привязка к инциденту сбрасывается
(`explainedByIncident = null`). Пишется аудит `update_anomaly_status`.

```bash
curl -X PATCH "{baseUrl}/api/anomalies/55/status" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"status": "acknowledged"}'
```

Ответ `200 OK`:

```json
{ "anomalyId": 55, "status": "acknowledged", "explainedByIncident": 10 }
```

Ошибки: `400 { "error": "validation_failed", "field": "status" }` — недопустимый
статус; `404 { "error": "not_found" }` — нет аномалии.

---

## 8. Отчёты: `/api/reports`

Все эндпоинты требуют токен (любая роль).

### 8.1. GET /api/reports/summary — сводка за период

Параметры `from`, `to` необязательны: без них считаются все инциденты, границы
применяются по `beginTime`.

```bash
curl -H "Authorization: Bearer $TOKEN" \
  "{baseUrl}/api/reports/summary?from=2026-11-01T00:00:00Z&to=2026-11-30T23:59:59Z"
```

Ответ `200 OK`:

```json
{
  "total": 12,
  "byCategory": [ { "category": "power", "count": 5 }, { "category": "transport", "count": 4 } ],
  "bySeverity": [ { "severity": "high", "count": 3 }, { "severity": "medium", "count": 9 } ]
}
```

### 8.2. GET /api/reports/available-periods — доступный период данных

Возвращает минимальное и максимальное `beginTime` инцидентов — удобно для
настройки календарей на клиенте. Если инцидентов нет, оба поля `null`.

```json
{ "minBeginTime": "2026-10-01T00:00:00", "maxBeginTime": "2026-11-14T06:00:00" }
```

### 8.3. GET /api/reports/export — выгрузка отчёта CSV

Параметры `from`, `to` необязательны. Ответ — файл CSV (`text/csv`, UTF-8 с BOM,
разделитель `;`), имя `report_YYYYMMDD.csv` по текущей дате.
Колонки: `ID;PW;Описание;Начало;Окончание;Длительность;Общий простой;Категория;
Важность`. Сортировка по `beginTime` по убыванию.

```bash
curl -H "Authorization: Bearer $TOKEN" -OJ "{baseUrl}/api/reports/export?from=2026-11-01&to=2026-11-30"
```

---

## 9. Справочники: `/api/reference`

### GET /api/reference/all — все справочники одним запросом

Доступ: любая роль. Используется клиентом, чтобы получить ID и коды для создания
инцидентов одним вызовом.

Ответ `200 OK`:

```json
{
  "regions":          [ { "regionId": 1, "code": "SVE", "name": "Свердловская область" } ],
  "directions":       [ { "directionId": 1, "code": "URAL", "name": "Уральское", "kind": "direction" } ],
  "technicalCenters": [ { "techCenterId": 1, "directionId": 1, "code": "EKB", "name": "Екатеринбург" } ],
  "priorities":       [ { "priorityId": 1, "code": "P1", "name": "Критический", "sortOrder": 1 } ],
  "networkTypes":     [ { "networkTypeId": 1, "code": "LTE", "name": "LTE" } ],
  "equipmentTypes":   [ { "equipmentTypeId": 1, "code": "BTS", "name": "Базовая станция" } ],
  "alarmTypes":       [ { "alarmTypeId": 1, "code": "POWER", "name": "Электропитание" } ],
  "categories":       [ { "code": "power", "label": "Электроснабжение" } ],
  "severities":       [ { "code": "low", "label": "Низкая" } ]
}
```

`categories` — 8 значений из раздела 3 с русскими подписями; `severities` —
`low/medium/high/critical` с подписями «Низкая/Средняя/Высокая/Критическая».

---

## 10. Администрирование: `/api/admin`

Все эндпоинты — только роль `admin`.

### 10.1. GET /api/admin/users — список пользователей

Ответ `200 OK` — массив, **без хешей паролей**:

```json
[
  { "userId": 1, "username": "admin", "role": "admin", "createdAt": "2026-10-01T09:00:00Z" },
  { "userId": 2, "username": "operator", "role": "operator", "createdAt": "2026-10-01T09:00:00Z" }
]
```

### 10.2. POST /api/admin/users — создание пользователя

Тело:

| Поле | Тип | Обязательно | Описание |
|---|---|:---:|---|
| `username` | string | да | Непустой, до 64 символов |
| `password` | string | да | Минимум 4 символа; сохраняется BCrypt-хеш (work factor 11) |
| `role` | string | нет | `engineer` (по умолчанию) / `operator` / `admin`, регистронезависимо |

```bash
curl -X POST "{baseUrl}/api/admin/users" \
  -H "Authorization: Bearer $ADMIN_TOKEN" -H "Content-Type: application/json" \
  -d '{"username":"ivanov", "password":"secret123", "role":"operator"}'
```

Ответ `201 Created` (заголовок `Location: /api/admin/users/{id}`):

```json
{ "userId": 7, "username": "ivanov", "role": "operator" }
```

Ошибки:

- `400 { "error": "validation_failed", "field": "username" | "password" | "role" }`;
- `409 { "error": "username_already_exists", "message": "Пользователь с таким именем уже существует." }`.

Событие пишется в аудит как `create_user` от имени администратора-актора.

### 10.3. GET /api/admin/audit — журнал аудита

Параметры (все необязательны):

| Параметр | Тип | Описание |
|---|---|---|
| `from`, `to` | datetime | Период по `ts`, включительно |
| `userId` | int | Фильтр по автору действия |
| `action` | string | Фильтр по действию (регистронезависимо); значения — раздел 12 |
| `page`, `pageSize` | int | Пагинация, правила — раздел 3 |

```bash
curl -H "Authorization: Bearer $ADMIN_TOKEN" \
  "{baseUrl}/api/admin/audit?action=login&page=1&pageSize=50"
```

Ответ `200 OK`:

```json
{
  "total": 2,
  "page": 1,
  "pageSize": 50,
  "items": [
    {
      "auditId": 101,
      "ts": "2026-11-14T08:29:55Z",
      "userId": 2,
      "username": "operator",
      "role": "operator",
      "action": "login",
      "entityType": "user",
      "entityId": 2,
      "ipAddress": "192.0.2.10",
      "success": true
    }
  ]
}
```

Поле `details` (JSON с деталями действия — например, старый/новый статус
аномалии или параметры выгрузки) присутствует в ответе, если соответствующая
колонка заведена в вашей схеме `audit_log`; в базовой схеме её нет.

---

## 11. Состояние сервиса: `/api/health`

### GET /api/health

Доступ: без токена. Проверяет физическое подключение к MariaDB.

`200 OK`, база доступна:

```json
{ "status": "ok", "database": "ok", "version": "1.0.0", "timestamp": "2026-11-14T08:30:00Z" }
```

`503`, база недоступна:

```json
{ "status": "degraded", "database": "unavailable" }
```

```bash
curl "{baseUrl}/api/health"
```

---

## 12. Журнал аудита: действия

В `audit_log` пишутся следующие действия (`action`). Колонки записи: `ts`,
`userId`/`username`/`role` (актор), `entityType`/`entityId` (объект действия),
`ipAddress`, `success`.

| Action | Когда пишется | entityType |
|---|---|---|
| `login` | Вход: успешный (`success=true`) и неуспешный (`success=false`) | `user` |
| `create_user` | Администратор создал пользователя | `user` |
| `create_incident` | Создан инцидент | `incident` |
| `update_incident` | Изменён инцидент | `incident` |
| `delete_incident` | Удалён инцидент | `incident` |
| `explain_anomaly` | Аномалия привязана к инциденту | `anomaly` |
| `update_anomaly_status` | Вручную изменён статус аномалии | `anomaly` |
| `export_incidents` | Выгрузка CSV инцидентов | `incident` |
| `export_report` | Выгрузка CSV отчёта | `report` |
| `view_audit_log` | Просмотр журнала аудита | `audit_log` |
| `view_users` | Просмотр списка пользователей | `user` |
| `detect_anomalies` | Системный запуск детектора аномалий (актор `system`) | `anomaly` |
| `detect_availability_anomalies` | Системный запуск детектора доступности (актор `system`) | `anomaly` |
| `refresh_kpi` | Системный пересчёт KPI (актор `system`) | `kpi` |

---

## 13. Сквозной сценарий

Полный цикл работы оператора за 8 шагов:

```bash
# 1. Проверка доступности
curl "{baseUrl}/api/health"

# 2. Вход оператором
curl -X POST "{baseUrl}/api/auth/login" -H "Content-Type: application/json" \
  -d '{"username":"operator","password":"********"}'
TOKEN="<токен из ответа>"

# 3. Справочники: узнать допустимые ID/коды
curl -H "Authorization: Bearer $TOKEN" "{baseUrl}/api/reference/all"

# 4. Инциденты за период
curl -H "Authorization: Bearer $TOKEN" \
  "{baseUrl}/api/incidents?from=2026-11-01T00:00:00Z&to=2026-11-30T23:59:59Z"

# 5. Создать инцидент
curl -X POST "{baseUrl}/api/incidents" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"beginTime":"2026-11-14T06:00:00Z","severity":"high","category":"power","descr":"Авария питания"}'

# 6. Нераспознанные аномалии
curl -H "Authorization: Bearer $TOKEN" \
  "{baseUrl}/api/anomalies?from=2026-11-01T00:00:00Z&to=2026-11-30T23:59:59Z&status=new"

# 7. Объяснить аномалию созданным инцидентом (пусть его id = 10, аномалии = 55)
curl -X POST "{baseUrl}/api/anomalies/55/explain" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"incidentId": 10}'

# 8. Сводка и выгрузка
curl -H "Authorization: Bearer $TOKEN" "{baseUrl}/api/reports/summary?from=2026-11-01&to=2026-11-30"
curl -H "Authorization: Bearer $TOKEN" -OJ "{baseUrl}/api/reports/export?from=2026-11-01&to=2026-11-30"
```

Пример на JavaScript (fetch):

```js
const baseUrl = "http://localhost:8080";

async function login(username, password) {
  const res = await fetch(`${baseUrl}/api/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username, password })
  });
  if (!res.ok) throw new Error("Неверный логин или пароль");
  return (await res.json()).token;
}

async function getIncidents(token, from, to) {
  const url = `${baseUrl}/api/incidents?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`;
  const res = await fetch(url, { headers: { Authorization: `Bearer ${token}` } });
  if (res.status === 401) throw new Error("Токен истёк — выполните вход заново");
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return res.json(); // { total, items: [...] }
}
```

---

## 14. Ограничения и особенности

1. **PUT — полная замена.** При обновлении инцидента не переданные поля
   сбрасываются (раздел 6.4); PATCH для инцидентов нет.
2. **`lenght`** — поле в ответах называется именно так (историческая опечатка,
   зафиксированная в контракте и в CSV нет такого столбца — там «Длительность»).
3. **Нет пагинации** у `GET /api/incidents` — на больших периодах ответ может
   быть объёмным; сужайте `from`/`to`.
4. **Префикс `/api/monitoring/...`** из комментариев к коду маршрутом не
   поддерживается — аномалии доступны только как `/api/anomalies/...`.
5. **Даты без указания зоны** (например, `2026-11-01`) трактуются как локальное
   время сервера и приводятся к UTC. Всегда передавайте суффикс `Z`.
6. **Токен живёт 2 часа**, refresh и logout отсутствуют.
7. **Ответы 401/403 без тела** — клиент должен ориентироваться на статус-код.
8. **Формат CSV**: разделитель `;`, даты внутри файлов — `yyyy-MM-dd HH:mm`,
   выгрузка отчётов именует файл по текущей дате, выгрузка инцидентов — по
   границам периода.
9. В `GET /api/incidents` поля `akb`, `dgu`, `prostoy` отдаются строкой
   `TimeSpan` (возможен формат с днями, например `"1.02:00:00"`), а в карточке,
   POST и PUT — нормализованным `"HH:MM:SS"` с суммарными часами.
10. Сбой записи аудита не влияет на ответ клиенту: операция считается успешной,
    ошибка аудита уходит только в лог приложения.

---

## 15. Сводная таблица эндпоинтов

| # | Метод и путь | Доступ | Описание |
|---:|---|---|---|
| 1 | `POST /api/auth/login` | без токена | Вход, получение JWT |
| 2 | `GET /api/auth/me` | любая роль | Текущий пользователь из токена |
| 3 | `GET /api/incidents?from&to` | любая роль | Список инцидентов за период |
| 4 | `GET /api/incidents/{id}` | любая роль | Карточка инцидента |
| 5 | `POST /api/incidents` | operator, admin | Создание инцидента |
| 6 | `PUT /api/incidents/{id}` | operator, admin | Полное обновление инцидента |
| 7 | `DELETE /api/incidents/{id}` | operator, admin | Удаление инцидента (204) |
| 8 | `GET /api/incidents/export?from&to` | любая роль | CSV-выгрузка инцидентов |
| 9 | `GET /api/incidents/{id}/anomalies` | любая роль | Аномалии, объяснённые инцидентом |
| 10 | `GET /api/anomalies?from&to&...` | любая роль | Список аномалий с фильтрами и пагинацией |
| 11 | `GET /api/anomalies/{id}` | любая роль | Карточка аномалии |
| 12 | `POST /api/anomalies/{id}/explain` | operator, admin | Привязка аномалии к инциденту |
| 13 | `PATCH /api/anomalies/{id}/status` | operator, admin | Смена статуса аномалии |
| 14 | `GET /api/reports/summary?from&to` | любая роль | Сводка по категориям и severity |
| 15 | `GET /api/reports/available-periods` | любая роль | Мин./макс. даты инцидентов |
| 16 | `GET /api/reports/export?from&to` | любая роль | CSV-выгрузка отчёта |
| 17 | `GET /api/reference/all` | любая роль | Все справочники одним запросом |
| 18 | `GET /api/admin/users` | admin | Список пользователей |
| 19 | `POST /api/admin/users` | admin | Создание пользователя |
| 20 | `GET /api/admin/audit` | admin | Журнал аудита с фильтрами и пагинацией |
| 21 | `GET /api/health` | без токена | Статус сервиса и БД (200/503) |
