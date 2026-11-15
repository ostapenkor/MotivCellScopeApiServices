# Административный API

Документ соответствует `main` на коммите `db7f93001de5e57b710cad7a03cb716e706a8a0d`. Примеры показывают точные JSON-поля с условными данными.

Базовый путь — `/api/admin`. **Все три метода требуют валидный Bearer JWT и роль `admin`** на уровне класса. Роль `operator` сама по себе доступа не даёт. Отсутствующий/невалидный токен — `401`, валидный токен без нужной роли — `403`; собственный JSON этих ошибок контроллером не задан. [Маршрут и авторизация](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/AdminController.cs#L13-L25), [настройка JWT](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Program.cs#L76-L111).

## GET `/api/admin/audit`

| Query | Тип | Обязательность, умолчание и обработка |
| --- | --- | --- |
| `from` | `DateTime?` | Необязателен; без нижней границы. Если задан: `ts >= from.ToUniversalTime()`. |
| `to` | `DateTime?` | Необязателен; без верхней границы. Если задан: `ts <= to.ToUniversalTime()`. |
| `userId` | `uint?` | Необязателен; точное совпадение, не фильтр по username. |
| `action` | `string?` | Необязателен; непустое значение обрезается и переводится в нижний регистр. |
| `page` | `int` | `1`; `<1` заменяется на `1`. |
| `pageSize` | `int` | `50`; `<1` заменяется на `50`, `>200` ограничивается `200`. |

Даты независимы: разрешена только одна граница. Умолчания «последние 24 часа», проверки `from <= to`, максимального периода и запрета будущих дат нет. Передавайте ISO 8601 с `Z`/смещением, хотя отдельная проверка смещения отсутствует. Для неизвестного `action` нет whitelist-ошибки `400`; обычно будет пустой список. `action` — одно значение, CSV не поддерживается. [Фильтры](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/AdminController.cs#L31-L67).

`200 OK` — `{total,page,pageSize,items}`. `total` после фильтров, до пагинации. Порядок — `ts` по убыванию, дополнительный порядок при одинаковых timestamp не задан. При отсутствии записей/странице за концом `items=[]`. Метод **не вызывает** `_audit.Log("view_audit_log", ...)`, несмотря на наличие этого кода действия в схеме и сервисе. [Чтение и полный ответ](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/AdminController.cs#L69-L98).

Поля:

| Поле | JSON-тип |
| --- | --- |
| `total`, `page`, `pageSize` | Целые. |
| `items[].auditId` | Целое, исходный `ulong`. |
| `ts` | Строка `yyyy-MM-ddTHH:mm:ssZ`, без дробной части секунд. |
| `userId` | Целое или `null`. |
| `username`, `action` | Строки. |
| `role`, `entityType`, `ipAddress` | Строка или `null`. |
| `entityId` | Целое `ulong` или `null`. |
| `success` | Boolean или `null` по nullable-модели; не строка `"true"` и не контрактное число `1`. |

[DTO audit](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/AdminController.cs#L77-L96), [модель](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Models/AuditLog.cs#L8-L26).

Пример: `GET /api/admin/audit?from=2026-11-14T00%3A00%3A00Z&action=create_user&page=1&pageSize=50`

```json
{
  "total": 1,
  "page": 1,
  "pageSize": 50,
  "items": [
    {
      "auditId": 15,
      "ts": "2026-11-14T10:00:00Z",
      "userId": 1,
      "username": "admin",
      "role": "admin",
      "action": "create_user",
      "entityType": "user",
      "entityId": 4,
      "ipAddress": "127.0.0.1",
      "success": true
    }
  ]
}
```

## GET `/api/admin/users`

Query/body отсутствуют. Возвращает всех пользователей, по `userId` по возрастанию; пагинации и фильтров нет. DTO строго состоит из `userId` (целое), `username` (строка), `role` (строка), `createdAt` (строка `yyyy-MM-ddTHH:mm:ssZ`). Хеш пароля и пароль в ответ не включены. [Полный метод](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/AdminController.cs#L104-L121).

`200 OK` — **массив**, без `items`:

```json
[
  { "userId": 1, "username": "admin", "role": "admin", "createdAt": "2026-11-01T00:00:00Z" },
  { "userId": 4, "username": "engineer1", "role": "engineer", "createdAt": "2026-11-14T10:00:00Z" }
]
```

После формирования списка вызывается аудит `view_users`, `entityType="user"`, `entityId=null`, `success=true`. Нет пользователей — `[]`.

## POST `/api/admin/users`

Тело `application/json`:

```json
{ "username": "engineer1", "password": "example-password", "role": "engineer" }
```

| Поле | Тип | Умолчание и фактическая валидация |
| --- | --- | --- |
| `username` | `string` | В DTO `""`. Обязателен по ручной проверке: не whitespace, длина исходной строки не более 64. После проверки обрезаются крайние пробелы. |
| `password` | `string` | В DTO `""`. Не whitespace и минимум 4 символа. Не trim-ится; собственного максимума или требования сложности в контроллере нет. |
| `role` | `string` | В DTO `"engineer"`. Пустое/пробельное значение заменяется на `engineer`; иначе trim + lowercase. Разрешены `engineer`, `operator`, `admin`. |

Пропущенные поля получают умолчания DTO; `{}` затем отклоняется по `username`. Явный JSON `null` для non-nullable string и ошибки типов могут обрабатываться framework-валидацией до метода; это не гарантированно тот же JSON, что ручная ошибка ниже. [Проверки и создание](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/AdminController.cs#L128-L178), [DTO](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/AdminController.cs#L183-L188).

После нормализации проверяет существование username, хеширует пароль BCrypt с work factor `11`, сохраняет нового пользователя с `CreatedAt=DateTime.UtcNow`, затем вызывает аудит `create_user`, `entityType="user"`, `entityId=<новый ID>`, `success=true`. Роль `admin` разрешена к созданию: отдельного запрета создавать других администраторов нет. Логин не приводится к нижнему регистру; сравнение уникальности идёт через БД и её collation. Проверка уникальности и вставка — отдельные операции, поэтому контроллер не гарантирует `409` при любой конкурентной гонке вставки.

`201 Created`, заголовок `Location: /api/admin/users/4`:

```json
{ "userId": 4, "username": "engineer1", "role": "engineer" }
```

В ответе нет `createdAt`, пароля или хеша. При этом **GET `/api/admin/users/{id}` не реализован**: Location показывает идентификатор созданной сущности, но не обещает существующий маршрут её чтения.

Ручные ошибки, в порядке проверок:

| Статус | JSON | Причина |
| --- | --- | --- |
| `400` | `{"error":"validation_failed","field":"username"}` | Пустое имя или исходная длина >64. |
| `400` | `{"error":"validation_failed","field":"password"}` | Пустой пароль или длина <4. |
| `400` | `{"error":"validation_failed","field":"role"}` | Неизвестная роль. |
| `409` | `{"error":"username_already_exists","message":"Пользователь с таким именем уже существует."}` | Найден нормализованный username до вставки. |

## Что означает аудит

`AuditService` принимает только известные действия, нормализуя action через trim/lowercase и замену `-` на `_`. Для текущей схемы это: `login`, `create_user`, `create_incident`, `update_incident`, `delete_incident`, `explain_anomaly`, `update_anomaly_status`, `export_incidents`, `export_report`, `view_audit_log`, `view_users`, `detect_anomalies`, `detect_availability_anomalies`, `refresh_kpi`. Разрешение имени сервисом не означает, что каждый одноимённый HTTP-метод действительно пишет такую запись. [Список сервиса](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Services/AuditService.cs#L13-L30), [ENUM БД](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/init.sql#L321-L341).

Пользователь/роль берутся из claims текущего JWT, username при отсутствии claim — `system`; сервис поддерживает явные override для вызовов вроде login. Username ограничивается 64 символами, role — 16, IP — 45. IP берётся из `HttpContext.Connection.RemoteIpAddress`; API не утверждает, что это внешний адрес клиента за reverse proxy. Записываются `ts`, `userId`, `username`, `role`, `action`, `entityType`, `entityId`, `ipAddress`, `success`. Поля JSON `details` нет, несмотря на оставшийся комментарий в коде. [Заполнение аудита](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Services/AuditService.cs#L53-L114).

Ошибка записи аудита перехватывается, запись отсоединяется от контекста, ошибка попадает в logger. Основная успешная операция при этом не отменяется. Успешный `GET users`/`POST users` не гарантирует наличие лога; ручные валидационные отказы создания пользователя не сопровождаются отдельным `success=false`. [Обработка ошибки аудита](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Services/AuditService.cs#L113-L122).

## Общие ошибки и отсутствующие методы

Ошибки привязки query, несовместимые JSON-типы, некорректный JSON или отсутствующее тело POST могут дать framework `400` благодаря `[ApiController]`; не следует ожидать у каждого `400` поля `error/field`. Контроллер не задаёт единый JSON `500` для ошибок EF/БД/BCrypt. В pipeline нет пользовательского обработчика, который превращал бы все исключения в такой контракт. [Атрибуты класса](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/AdminController.cs#L13-L16), [pipeline](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Program.cs#L96-L113).

Текущий AdminController не вызывает KPI/детектирующие процедуры. Нет `/api/admin/refresh-kpi`, `/api/admin/detect-anomalies`, `/api/admin/detect-availability`, `/api/admin/recalculate-all`, а также изменения/удаления пользователя или его отдельной карточки. Реальные операции пересчёта находятся в [MonitoringController](monitoring.md) и доступны `operator`/`admin`; переносить на них ограничение «только admin» из этого класса нельзя.

## Маршруты и замечания для JS/PHP

| Метод и маршрут | Доступ | Успех |
| --- | --- | --- |
| `GET /api/admin/audit` | Только `admin` | `200 {total,page,pageSize,items}`. |
| `GET /api/admin/users` | Только `admin` | `200`, массив пользователей. |
| `POST /api/admin/users` | Только `admin` | `201 {userId,username,role}` и Location. |

JS/PHP-пример должен передавать JWT, а не username/role отдельными заголовками, и проверять `401/403` по HTTP-коду. Для POST отправляйте `Content-Type: application/json`; значение `password` используйте только в теле запроса, не в URL/логах примера. `GET users` возвращает массив, `GET audit` — `items`; `POST users` успешен с **201**, не только с 200. Для дат используйте query builder, чтобы `+` смещения не превратился в пробел.

`auditId/entityId` имеют исходный тип `ulong` и возвращаются JSON-числами: в JS возможна потеря точности за `Number.MAX_SAFE_INTEGER`. В PHP для декодирования больших ID пригоден `JSON_BIGINT_AS_STRING`. Nullable `success` проверяйте как boolean/null, не через сравнение со строкой. Даты вывода теряют миллисекунды и содержат суффикс `Z`; код форматирует сохранённые значения напрямую, рассчитывая на UTC в БД.
