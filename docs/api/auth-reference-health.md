# Авторизация, справочники и состояние API

Документ описывает код `main` на коммите `db7f93001de5e57b710cad7a03cb716e706a8a0d`. Примеры значений условные; наборы полей соответствуют коду. Связанные разделы: [инциденты](incidents.md), [отчёты](reports.md).

## Общие правила

JSON использует стандартные настройки `AddControllers()`: имена свойств в camelCase, без собственного единого конверта ошибок. Для защищённых маршрутов передавайте `Authorization: Bearer <token>`. JWT проверяется по подписи, issuer, audience и сроку действия. При отсутствии/невалидности токена middleware возвращает `401`; при недостаточной роли — `403`. Эти ответы не получают специального JSON из контроллеров. `[Authorize]` без `Roles` принимает любого аутентифицированного пользователя, а не только заранее перечисленные роли. См. [Program.cs:41–42, 76–90, 108–111][program].

Невалидные JSON, типы значений или привязка параметров могут дать стандартный `400` от `[ApiController]` ещё до вызова метода. Типичная форма такого ответа:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "from": ["The value 'bad-date' is not valid."] },
  "traceId": "<идентификатор запроса>"
}
```

Ключи `errors`, текст и служебные поля зависят от ошибки/ASP.NET Core. Неподдерживаемый тип тела может привести к `415`. Для необработанных ошибок БД/выполнения возможен `500`; стабильное тело такого ответа кодом не задано. В `Program.cs` нет собственного обработчика исключений или замены стандартного model-validation ответа. Включено перенаправление HTTP на HTTPS. См. [Program.cs:41, 96–113][program].

## POST /api/auth/login

Выдаёт JWT по логину/паролю. Аутентификация не требуется. Тело — JSON, других параметров нет. См. [AuthController.cs:33–75][auth-login], [LoginRequest:130–135][login-dto].

| Поле | Тип и значение по умолчанию | Фактическая проверка |
|---|---|---|
| `username` | string, при пропуске `""` | Поиск `users.Username == request.Username`; отдельной проверки длины/непустоты нет. |
| `password` | string, при пропуске `""` | BCrypt-проверка против найденного хеша; отдельной проверки длины/непустоты нет. |

Оба свойства не nullable; явный `null` может быть отклонён стандартной валидацией. Пропущенные свойства сохраняют инициализатор `""`, поэтому пропуск сам по себе не обрабатывается методом как отдельная ошибка обязательности.

```json
{"username":"operator","password":"example-password"}
```

`example-password` — заглушка для примера, а не пароль из init.sql. Используйте пароль учётной записи, созданной автором текущей БД.

`200 OK`:

```json
{
  "token": "<JWT>",
  "expiresAt": "2026-11-15T12:00:00Z",
  "username": "operator",
  "role": "operator"
}
```

Все четыре поля — строки. `expiresAt` берётся из `UtcNow + 2 часа`, форматируется с точностью до секунды. JWT содержит идентификатор, имя, роль пользователя и `jti`; подписывается HMAC SHA-256. Refresh-токен в ответе отсутствует. См. [AuthController.cs:64–74, 103–125][auth-token].

`401 Unauthorized`, если пользователь не найден, хеш пуст или BCrypt вернул false:

```json
{"message":"Неверный логин или пароль"}
```

Для обоих исходов вызывается аудит `login`, `entityType="user"`, `entityId=userId` либо `null`, с `success=true/false` и явными данными попытки входа. Необработанная ошибка самого BCrypt/БД не превращается в этот `401`.

## GET /api/auth/me

Требуется любой валидный JWT, без ограничения роли. Параметров и тела нет. Возвращает данные из claims, повторно пользователя в БД не читает. См. [AuthController.cs:77–100][auth-me].

`200 OK`:

```json
{"userId":2,"username":"operator","role":"operator"}
```

`userId` — число `uint`; `username` — непустая строка; `role` — строка либо `null`, если claim роли отсутствует. При невозможности разобрать ID в `uint` или пустом имени метод возвращает `401`:

```json
{"message":"Некорректные данные токена"}
```

Проверки существования пользователя и положительности ID здесь нет. `401` от JWT middleware может прийти раньше без этого тела.

## GET /api/reference/all

Требуется любой валидный JWT, без ограничения роли. Параметров и тела нет. Возвращает все справочники одним объектом; пагинации и фильтров нет. См. [ReferenceController.cs:10–145][reference].

`200 OK`; пример показывает точный состав свойств каждой записи:

```json
{
  "regions": [{"regionId":1,"code":"REG","name":"Область"}],
  "directions": [{"directionId":1,"code":"DIR","name":"Направление","kind":"direction"}],
  "technicalCenters": [{"techCenterId":1,"directionId":1,"code":"TC","name":"Техцентр"}],
  "priorities": [{"priorityId":1,"code":"1","name":"Приоритет 1","sortOrder":1}],
  "networkTypes": [{"networkTypeId":1,"code":"LTE","name":"LTE"}],
  "equipmentTypes": [{"equipmentTypeId":1,"code":"EQ","name":"Оборудование"}],
  "alarmTypes": [{"alarmTypeId":1,"code":"ALARM","name":"Тип аварии"}],
  "categories": [
    {"code":"power","label":"Электроснабжение"},
    {"code":"transport","label":"Транспорт"},
    {"code":"hardware","label":"Оборудование"},
    {"code":"software","label":"ПО"},
    {"code":"planned","label":"Плановые работы"},
    {"code":"client","label":"Обращение клиента"},
    {"code":"external","label":"Внешнее"},
    {"code":"other","label":"Прочее"}
  ],
  "severities": [
    {"code":"low","label":"Низкая"},
    {"code":"medium","label":"Средняя"},
    {"code":"high","label":"Высокая"},
    {"code":"critical","label":"Критическая"}
  ]
}
```

Все девять свойств — массивы. Первые семь читаются из БД и могут быть пустыми. ID, `directionId` техцентра и `sortOrder` — числа; остальные показанные поля — строки. Первые семь массивов сортируются по собственному ID по возрастанию, кроме `priorities`: он сортируется только по `sortOrder`. `kind` берётся из БД (`direction`/`federal_district` по схеме); категории и важности — статические массивы ровно в приведённом порядке. См. [ReferenceController.cs:26–132][reference].

Специальных `400`/`404` метод не формирует; возможны общие `401` и необработанные ошибки выполнения. Маршрутов вида `/api/reference/regions` в этом контроллере нет; `networkElements` в ответ не включён.

## GET /api/health

Публичная синхронная проверка `_db.Database.CanConnect()`. Параметров и тела нет. См. [HealthController.cs:8–52][health].

При доступной БД — `200 OK`:

```json
{"status":"ok","database":"ok","version":"1.0.0","timestamp":"2026-11-15T10:00:00Z"}
```

Все поля — строки; версия фиксирована в методе, timestamp — текущее UTC с точностью до секунды. Если `CanConnect()` вернул false или выбросил исключение — `503 Service Unavailable`:

```json
{"status":"degraded","database":"unavailable"}
```

В ответе `503` нет `version` и `timestamp`. Метод не проверяет наличие таблиц, представлений, исходных данных или работоспособность остальных маршрутов. Проверка выполняется в уже запущенном приложении: она не гарантирует ответ при ошибке его старта, где используется `ServerVersion.AutoDetect`. См. [Program.cs:33–39][program].

## Аудит и границы контракта

`AuditService` берёт ID, имя, роль из claims (для login применяются явные overrides), добавляет UTC-время и IP запроса; имя обрезает до 64 символов, роль — до 16, IP — до 45. Запись аудита выполняется отдельным `SaveChanges`; исключение логируется и подавляется. Поэтому успешный HTTP-ответ не гарантирует сохранение строки аудита. См. [AuditService.cs:53–122][audit].

Описаны все 4 маршрута этих трёх контроллеров. Здесь документируется реализация: отдельного общего `{error,message,field}`-контракта и отдельных endpoint каждого справочника в ней нет.

[program]: https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Program.cs#L33-L113
[auth-login]: https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/AuthController.cs#L33-L75
[login-dto]: https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/AuthController.cs#L130-L135
[auth-token]: https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/AuthController.cs#L64-L125
[auth-me]: https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/AuthController.cs#L77-L100
[reference]: https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/ReferenceController.cs#L10-L145
[health]: https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/HealthController.cs#L8-L52
[audit]: https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Services/AuditService.cs#L53-L122
