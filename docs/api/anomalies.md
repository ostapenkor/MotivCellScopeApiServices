# API аномалий

Контракт описан по `main`, коммит `db7f93001de5e57b710cad7a03cb716e706a8a0d`. JSON-примеры содержат условные данные и точные имена полей ответа.

Реальный базовый путь — **`/api/anomalies`**: он получается из `[Route("api/[controller]")]`. Все методы требуют Bearer JWT. GET не ограничены конкретной ролью; POST explain и PATCH status разрешены только `operator`/`admin`. Комментарии файла упоминают `/api/monitoring/anomalies`, но соответствующих атрибутов маршрута нет: такого alias этот контроллер не регистрирует. [Базовый маршрут](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/AnomaliesController.cs#L11-L33), [авторизация изменений](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/AnomaliesController.cs#L228-L262).

## GET `/api/anomalies`

| Query | Тип | Обязательность и умолчание |
| --- | --- | --- |
| `from` | `DateTime?` | Обязателен. |
| `to` | `DateTime?` | Обязателен. |
| `severity` | `string?` | Нет; точное сравнение с полем БД. |
| `status` | `string?` | Нет; точное сравнение. |
| `type` | `string?` | Нет; сравнивается с `anomaly_type`. |
| `kpiCode` | `string?` | Нет; сравнивается с `kpi_code`. |
| `regionId` | `ushort?` | Нет; регион связанного сетевого элемента. |
| `btsId` | `uint?` | Нет; BTS связанной соты. |
| `page` | `int` | `1`; `<1` заменяется на `1`. |
| `pageSize` | `int` | `50`; `<1` заменяется на `50`, `>200` ограничивается `200`. |

`from/to` преобразуются через `ToUniversalTime()`. Передавайте ISO 8601 с `Z`/смещением; отдельной проверки наличия смещения нет. Условие — **`tsFrom >= from && tsFrom <= to`**, а не пересечение интервалов. Аномалия, которая началась раньше `from`, не попадёт в результат, даже если ещё продолжалась в выбранный период. Равные границы разрешены, ограничения длины периода и будущих дат не установлены. Проверка `from > to` выполняется над привязанными параметрами до явного `ToUniversalTime()`. [Параметры и границы](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/AnomaliesController.cs#L32-L72).

Непустые строковые фильтры передаются в сравнение без trim, lowercase, CSV-разбора и whitelist. Пробельная строка игнорируется. Это одиночные значения, не список: `status=new,acknowledged` не означает два статуса. Фактическая чувствительность сравнения к регистру зависит от collation БД; клиенту следует использовать канонические строки. Неизвестное значение не вызывает отдельный `400`, а обычно даёт пустую выборку. `regionId/btsId` исключают записи без нужной связи. [Фильтры](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/AnomaliesController.cs#L82-L111).

Значения схемы:

- `severity`: `info`, `warning`, `critical`.
- `status`: `new`, `acknowledged`, `explained`, `resolved`, `false_positive`.
- `type`: `threshold`, `zscore`, `missing_data`, `logical`, `cross_source`.
- `kpiCode` — nullable строка, не ENUM; штатные процедуры создают `cssr`, `dcr`, `traffic_dl`, `availability`, а для missing-data — `null`. Контракт списка не ограничивает прочие сохранённые строки.

[Схема anomalies](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/init.sql#L160-L181), [создание z-score](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/init.sql#L1203-L1217), [missing-data](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/init.sql#L1298-L1313), [availability](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/init.sql#L1392-L1405).

`200 OK` — `{total,page,pageSize,items}`. Первые три поля — целые; `total` считается после фильтров, до пагинации. Сортировка только по `tsFrom` по убыванию; дополнительный порядок при одинаковом времени не задан. Без совпадений или за последней страницей `items=[]`. [Подсчёт и пагинация](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/AnomaliesController.cs#L113-L155).

Точные поля `items[]`:

| Поля | JSON-тип / смысл |
| --- | --- |
| `anomalyId` | Целое, исходный C# `ulong`. |
| `detectedAt`, `tsFrom`, `tsTo` | Строки `yyyy-MM-ddTHH:mm:ssZ`, без дробных секунд. |
| `cellId`, `btsId`, `regionId` | Целое или `null`. |
| `btsCode`, `btsFullName`, `sector`, `regionCode` | Строка или `null`; связи LEFT JOIN могут отсутствовать. |
| `type`, `severity`, `status` | Строки. |
| `kpiCode` | Строка или `null`. |
| `observedValue`, `expectedValue`, `score` | Число или `null`; в моделях nullable `decimal`. |
| `incidentId` | Целое `ulong` или `null`; значение `explained_by_incident`. |
| `incidentPw`, `incidentCategory` | Строка или `null`, сведения связанного инцидента. |

[Полная проекция DTO](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/AnomaliesController.cs#L123-L147).

Пример: `GET /api/anomalies?from=2026-11-14T00%3A00%3A00Z&to=2026-11-15T00%3A00%3A00Z&severity=warning&status=new&page=1&pageSize=50`

```json
{
  "total": 1,
  "page": 1,
  "pageSize": 50,
  "items": [
    {
      "anomalyId": 7,
      "detectedAt": "2026-11-14T09:05:00Z",
      "cellId": 1,
      "btsId": 1,
      "btsCode": "BTS-001",
      "btsFullName": "Пример базовой станции",
      "sector": "A",
      "regionId": 1,
      "regionCode": "REG1",
      "tsFrom": "2026-11-14T08:00:00Z",
      "tsTo": "2026-11-14T09:00:00Z",
      "type": "threshold",
      "kpiCode": "cssr",
      "severity": "warning",
      "observedValue": 92.5,
      "expectedValue": 95,
      "score": 2.5,
      "status": "new",
      "incidentId": null,
      "incidentPw": null,
      "incidentCategory": null
    }
  ]
}
```

Ошибки этого метода:

| Статус | JSON | Условие |
| --- | --- | --- |
| `400` | `{"message":"Параметры 'from' и 'to' обязательны и должны быть в формате ISO 8601."}` | Хотя бы одна дата отсутствует. |
| `400` | `{"message":"Дата начала 'from' не может быть больше даты окончания 'to'."}` | `from > to`. |

## GET `/api/anomalies/{id}`

`id` — обязательный `ulong` в пути; query/body не предусмотрены. `200 OK` — одна карточка. Общие с item поля: `anomalyId`, `detectedAt`, `cellId`, `btsId`, `btsCode`, `btsFullName`, `sector`, `regionId`, `regionCode`, `tsFrom`, `tsTo`, `type`, `kpiCode`, `severity`, `observedValue`, `expectedValue`, `score`, `status` с теми же типами. Вместо плоских `incidentId/incidentPw/incidentCategory` возвращается **`incident`**: `null` или объект с точными полями `incidentId` (целое), `pw` (строка/null), `descr` (строка/null), `category` (строка), `beginTime` (строка времени). Поля `explainedByIncident` и `dedupHash` в карточке не возвращаются. [Поиск и полный ответ](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/AnomaliesController.cs#L162-L221).

Пример: `GET /api/anomalies/7`

```json
{
  "anomalyId": 7,
  "detectedAt": "2026-11-14T09:05:00Z",
  "cellId": 1,
  "btsId": 1,
  "btsCode": "BTS-001",
  "btsFullName": "Пример базовой станции",
  "sector": "A",
  "regionId": 1,
  "regionCode": "REG1",
  "tsFrom": "2026-11-14T08:00:00Z",
  "tsTo": "2026-11-14T09:00:00Z",
  "type": "threshold",
  "kpiCode": "cssr",
  "severity": "warning",
  "observedValue": 92.5,
  "expectedValue": 95,
  "score": 2.5,
  "status": "explained",
  "incident": {
    "incidentId": 12,
    "pw": "PW-12",
    "descr": "Пример описания",
    "category": "power",
    "beginTime": "2026-11-14T07:30:00Z"
  }
}
```

Несуществующий ID: `404`, `{"error":"not_found"}`. Если связи с инцидентом нет, `incident=null`, независимо от значения строкового `status`.

## POST `/api/anomalies/{id}/explain`

Доступ: только `operator`/`admin`. `id` — обязательный `ulong`. Тело JSON:

```json
{ "incidentId": 12 }
```

`incidentId` — `ulong`; у свойства нет `[Required]`, при отсутствии в `{}` оно равно `0`. Метод сначала ищет аномалию, затем проверяет существование инцидента. Проверок совпадения BTS/региона, пересечения времени, текущего статуса аномалии или запрета повторной привязки нет. Существующую связь можно заменить другой. При успехе устанавливает связь и **всегда** `status="explained"`, сохраняет изменения и вызывает аудит `explain_anomaly`, `entityType="anomaly"`, `entityId=id`. [Метод](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/AnomaliesController.cs#L228-L254), [DTO](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/AnomaliesController.cs#L294-L301).

`200 OK`:

```json
{ "anomalyId": 7, "explainedByIncident": 12, "status": "explained" }
```

Оба ID — целые, status — строка. Ошибки:

| Статус | JSON | Условие |
| --- | --- | --- |
| `404` | `{"error":"anomaly_not_found"}` | Аномалия не найдена. |
| `400` | `{"error":"validation_failed","field":"incidentId","message":"Указанный инцидент не существует."}` | Не найден инцидент, включая обычный случай `incidentId=0`. |

## PATCH `/api/anomalies/{id}/status`

Доступ: только `operator`/`admin`. `id` — обязательный `ulong`. Тело:

```json
{ "status": "acknowledged" }
```

`status` — строка; в DTO по умолчанию `""`. Метод применяет `Trim().ToLower()`, затем разрешает только `new`, `acknowledged`, `explained`, `resolved`, `false_positive`. Проверка статуса происходит до поиска аномалии. Матрицы разрешённых переходов и проверки конкурентной версии нет. `new` и `false_positive` очищают `explainedByIncident`; остальные значения сохраняют текущую связь. Поэтому `explained` можно установить без инцидента, а `resolved/acknowledged` могут сохранить связь. [Весь метод](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/AnomaliesController.cs#L260-L288).

Сохраняет изменения, вызывает аудит `update_anomaly_status`, `entityType="anomaly"`, `entityId=id`. `200 OK`:

```json
{ "anomalyId": 7, "status": "acknowledged", "explainedByIncident": 12 }
```

`anomalyId` — целое, `status` — строка, `explainedByIncident` — целое или `null`.

| Статус | JSON | Условие |
| --- | --- | --- |
| `400` | `{"error":"validation_failed","field":"status"}` | Пустой/неизвестный статус. |
| `404` | `{"error":"not_found"}` | Валидный статус, но аномалия не найдена. |

## Общие ошибки, аудит и пересчёт

Без валидного JWT — `401`; для изменений с неподходящей ролью — `403`. Собственного JSON этих ошибок нет. `[ApiController]` может вернуть `400` при неразбираемых датах/ID, некорректном JSON или отсутствующем body; это отдельный формат ошибки, не обязательно `{error,field}`. Собственного общего обработчика исключений БД и контрактного JSON `500` здесь нет. [Атрибуты](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/AnomaliesController.cs#L11-L14), [JWT/pipeline](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Program.cs#L76-L111).

GET не записывают аудит. Изменения сохраняются **до** `_audit.Log`; сервис перехватывает ошибку аудита, отсоединяет неудавшуюся запись и пишет ошибку в logger. Успешный ответ изменения не гарантирует успешную запись audit_log. Валидационные отказы этих методов сами по себе не вызывают лог `success=false`. [AuditService](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Services/AuditService.cs#L53-L122).

Этот контроллер не запускает детекторы. Они доступны через [POST мониторинга](monitoring.md#post-apimonitoringdetect-anomalies). SQL-детектор удаляет и пересоздаёт свежие `new/explained` некоторых типов; ручное explain/status не гарантирует неизменность ID при последующем пересчёте. `observedValue/expectedValue/score` зависят от типа проверки: это могут быть проценты, байты, число успешных соединений или z-score; одной общей единицы для всех строк нет. Поле availability в процедуре — proxy активности по попыткам/трафику, не измеренный uptime. [Пересоздание и пороги](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/init.sql#L1134-L1201), [z-score](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/init.sql#L1203-L1239), [logical](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/init.sql#L1344-L1360), [availability](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/init.sql#L1387-L1447).

## Маршруты и замечания для JS/PHP

| Метод и маршрут | Доступ | Успех |
| --- | --- | --- |
| `GET /api/anomalies` | Любой валидный JWT | `200 {total,page,pageSize,items}`; обязательны `from/to`. |
| `GET /api/anomalies/{id}` | Любой валидный JWT | `200`, карточка с `incident`. |
| `POST /api/anomalies/{id}/explain` | `operator`, `admin` | `200 {anomalyId,explainedByIncident,status}`. |
| `PATCH /api/anomalies/{id}/status` | `operator`, `admin` | `200 {anomalyId,status,explainedByIncident}`. |

Для JS/PHP: кодируйте query через стандартный builder; не отправляйте несколько статусов CSV; учитывайте nullable связи. Список использует `incidentId`, карточка — `incident.incidentId`, а ответы изменений — `explainedByIncident`: это разные JSON-формы. PATCH здесь обычный JSON `{"status":"resolved"}`, не JSON Patch. Перед разбором DTO проверяйте HTTP-код; у ошибок встречаются `message`, `error/field` и framework validation response.

`anomalyId/incidentId` в C# — `ulong`, в JSON — числа. JS не сохраняет целые больше `Number.MAX_SAFE_INTEGER`; обычный `response.json()` не устраняет эту проблему. PHP-примеру для больших значений нужен `JSON_BIGINT_AS_STRING` при `json_decode`; при отправке сервер ожидает числовой токен, контракт строковых ID не заявлен. Для небольших демонстрационных ID подходят обычные числа. [Модель ID](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Models/Anomaly.cs#L8-L32).
