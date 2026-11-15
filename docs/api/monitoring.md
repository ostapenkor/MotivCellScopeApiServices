# API мониторинга

Документ описывает код `main` на коммите `db7f93001de5e57b710cad7a03cb716e706a8a0d`, а не старую ветку `kolya-api-sync`. Значения в JSON ниже иллюстративные; примеры показывают форму ответа, а не результат запроса к работающему серверу.

Базовый путь — `/api/monitoring`. Все методы требуют `Authorization: Bearer <JWT>`. Чтение доступно любому аутентифицированному пользователю; два POST дополнительно требуют роль `operator` или `admin`. Отсутствующий/невалидный JWT даёт `401`, недостаточная роль — `403`; собственного JSON для этих ответов контроллер не задаёт. [Маршрут и общая авторизация](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/MonitoringController.cs#L13-L26), [JWT и middleware](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Program.cs#L76-L111).

## Даты, KPI и ограничения текущего контракта

`DateTime`-параметры преобразуются через `ToUniversalTime()`. Для однозначного времени передавайте ISO 8601 с `Z` или смещением, например `2026-11-14T00:00:00Z`. Контроллер не требует явного смещения отдельной проверкой. `+` в query нужно кодировать как `%2B`; предпочтительнее `URLSearchParams` в JS или `http_build_query` в PHP.

У `summary`, `network-elements` и `cells` даты необязательны **независимо друг от друга**: `to` по умолчанию равен текущему UTC, `from` — вычисленному `to − 24 часа`. Фильтрация KPI использует обе включённые границы (`tsHour >= from && tsHour <= to`), а фильтрация аномалий — попадание **начала** `tsFrom` в этот же диапазон. Пересечение интервалов аномалии с периодом не проверяется. Проверка `from > to` есть только у `summary`; равные границы разрешены. Максимальный период, запрет будущих дат и обязательная пара дат здесь не установлены. [Период summary](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/MonitoringController.cs#L31-L54), [список элементов](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/MonitoringController.cs#L149-L207), [список сот](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/MonitoringController.cs#L360-L425).

Фактические вычисления чтения:

| Поле | Расчёт |
| --- | --- |
| `avgCssr` / `cssr` | Арифметическое среднее сохранённых `cssr_pct`; это не отношение сумм счётчиков. `null` не участвуют в среднем; отсутствие среднего заменяется на `0`. |
| `avgDcr` / `dcr` | Аналогичное среднее `dcr_pct`, с заменой отсутствующего среднего на `0`. |
| `avgAvailability` / агрегированное `availability` | `100 × количество имеющихся KPI-строк с conn_attempt > 0 / количество имеющихся KPI-строк`. При отсутствии строк возвращается **100**. |
| `availability` в почасовом ряду | Целое `1`, если `conn_attempt > 0`, иначе `0`. Это не проценты. |
| `totalTrafficGb` / `trafficGb` | Сумма DL + UL байтов, делённая на `1024³`. Несмотря на имя `Gb`, вычисляются двоичные гигабайты (GiB), не гигабиты и не десятичные GB. |

Эти поля активности не измеряют реальную доступность оборудования. Пропущенные часы не добавляются в знаменатель; API не возвращает ожидаемое число часов или coverage. Поэтому `100` при отсутствии KPI и `0` для отсутствующих CSSR/DCR нельзя использовать как доказательство исправности или простоя. Summary округляет CSSR/DCR/активность до 2 знаков и трафик до 1; остальные ответы с KPI — до 2 знаков. [Формулы summary](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/MonitoringController.cs#L57-L73), [формулы карточки](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/MonitoringController.cs#L292-L319), [почасовой ряд](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/MonitoringController.cs#L490-L507).

Координаты выдаются напрямую из `bts.lat/lon`, адрес — в карточке. В этом контроллере нет `ExposeCoordinates`, `coordinatesHidden` или другого механизма скрытия: `null` означает отсутствие данных, а не применённую политику приватности. [Проекция списка](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/MonitoringController.cs#L232-L253), [проекция карточки](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/MonitoringController.cs#L336-L354).

## GET `/api/monitoring/summary`

Query:

| Параметр | Тип | Обязательность и умолчание |
| --- | --- | --- |
| `from` | `DateTime?` | Нет; вычисленный `to − 24 часа`. |
| `to` | `DateTime?` | Нет; текущий UTC. |
| `regionId` | `ushort?` | Нет; без фильтра региона. |

`200 OK` — объект с точными полями из примера. `period.from/to` — строки `yyyy-MM-ddTHH:mm:ssZ`; все остальные поля — числа. Счётчики BTS и сот относятся к текущему справочнику, а не к историческому состоянию за период. BTS — записи `network_elements` с `element_type='bts'`; `btsProblem` объединяет `down` и `degraded`. `decommissioned` включены в `btsTotal`, но отдельного счётчика нет. Счётчики BTS вычисляются отдельно от сот. [Реализация summary](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/MonitoringController.cs#L75-L143).

Аномалии считаются так:

- `anomaliesOpen`: статус `new` или `acknowledged` **и** нет `explainedByIncident`.
- `anomaliesExplained`: статус `explained` **или** имеется связь с инцидентом.
- `anomaliesCritical`: severity `critical`, статус не `resolved` и не `false_positive`.

Это не взаимоисключающие категории. Без регионального фильтра учитываются и аномалии без соты; с `regionId` нужна связь с элементом этого региона. [Выборка и условия](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/MonitoringController.cs#L100-L122).

Пример: `GET /api/monitoring/summary?from=2026-11-14T00%3A00%3A00Z&to=2026-11-15T00%3A00%3A00Z&regionId=1`

```json
{
  "period": { "from": "2026-11-14T00:00:00Z", "to": "2026-11-15T00:00:00Z" },
  "avgCssr": 97.42,
  "avgDcr": 1.31,
  "avgAvailability": 95.83,
  "totalTrafficGb": 12.4,
  "btsTotal": 5,
  "btsActive": 3,
  "btsProblem": 1,
  "btsMaintenance": 1,
  "cellsTotal": 15,
  "anomaliesOpen": 4,
  "anomaliesExplained": 2,
  "anomaliesCritical": 1
}
```

При `from > to`: `400` с `{"message":"Дата 'from' не может быть больше 'to'."}`. При пустой выборке счётчики, CSSR/DCR и трафик равны `0`, `avgAvailability` равен `100`.

## GET `/api/monitoring/network-elements`

Query: `regionId` (`ushort?`, необязательный), `elementType` (`string?`, необязательный), `from` и `to` (`DateTime?`, умолчания выше). Непустой `elementType` обрезается по краям и переводится в нижний регистр; отдельной проверки допустимых значений нет. Неизвестный тип обычно даёт пустую выборку. Значения схемы: `bts`, `ip_node`, `optical_node`, `dwdm_node`, `rrl_node`, `opts_node`, `sss_node`, `client_channel`, `mgmn_node`. [Параметры и фильтры](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/MonitoringController.cs#L149-L189), [типы элементов](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/init.sql#L57-L85).

`200 OK` — **массив**, без `items`, `total`, пагинации или гарантированной сортировки. `cellsCount` — число всех привязанных сот, независимо от периода. `anomalyCount`, `anomalySeverity`, `worstKpiCode` строятся только по связанным с сотами аномалиям, начавшимся в периоде, кроме статусов `resolved` и `false_positive`. Статус `explained` включён. Приоритет severity: `critical > warning > info`; при равенстве выбор `worstKpiCode` отдельным правилом не закреплён. У элемента без таких аномалий: `anomalyCount=0`, два остальных поля `null`. [Агрегация и весь DTO](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/MonitoringController.cs#L191-L255).

Поля элемента: `id`, `cellsCount`, `anomalyCount` — целые; `regionId`, `techCenterId` — целые или `null`; `lat/lon` — числа или `null`; `code`, `elementType`, `status` — строки; `networkElementCode`, `fullName`, `shortName`, `regionCode`, `techCenterCode`, `directionCode`, `siteType`, `anomalySeverity`, `worstKpiCode` — строки или `null`.

Пример: `GET /api/monitoring/network-elements?regionId=1&elementType=bts`

```json
[
  {
    "id": 1,
    "code": "BTS-001",
    "networkElementCode": "NE-001",
    "fullName": "Пример базовой станции",
    "shortName": "БС-001",
    "elementType": "bts",
    "regionId": 1,
    "regionCode": "REG1",
    "techCenterId": 1,
    "techCenterCode": "TC1",
    "directionCode": "DIR1",
    "status": "active",
    "lat": 55.1,
    "lon": 60.2,
    "siteType": "macro",
    "cellsCount": 3,
    "anomalySeverity": "warning",
    "anomalyCount": 1,
    "worstKpiCode": "cssr"
  }
]
```

При отсутствии совпадений — `[]`. Для обратного диапазона дат специального `400` нет: справочные элементы могут вернуться с нулевыми счётчиками аномалий.

## GET `/api/monitoring/network-elements/{id}`

`id` — обязательный `uint` в пути. Query-параметров у метода нет. Карточка доступна для любого типа элемента, не только BTS. Несуществующий ID даёт `404` с `{"error":"not_found"}`. [Поиск элемента](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/MonitoringController.cs#L260-L284).

`cells` упорядочены по `localCellId`. KPI считаются с `UTC now − 24 часа` без верхней границы. `anomalies` содержит все связанные аномалии, кроме `resolved` и `false_positive`, **без ограничения последними 24 часами**, в порядке `detectedAt` по убыванию. Пагинации обоих массивов нет. Для не-BTS массивы обычно пусты, поля BTS могут быть `null`. [Соты, KPI и аномалии](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/MonitoringController.cs#L285-L354).

`200 OK` — точные поля показаны ниже. `id` — целое; `code`, `elementType`, `status` — строки; географические коды, `fullName`, сведения о восстановлении, `address/siteType` — строки или `null`; `lat/lon` — числа или `null`. `statusSince/expectedRestoreDate`, если заданы, форматируются как `yyyy-MM-ddTHH:mm:ssZ`. У сот числовые KPI и целые ID, строковый `sector`; у аномалий целый `anomalyId`, строковые `type/severity/status`, nullable `kpiCode`.

Пример: `GET /api/monitoring/network-elements/1`

```json
{
  "id": 1,
  "code": "BTS-001",
  "fullName": "Пример базовой станции",
  "elementType": "bts",
  "regionCode": "REG1",
  "techCenterCode": "TC1",
  "directionCode": "DIR1",
  "status": "active",
  "statusSince": "2026-11-10T08:00:00Z",
  "stopReason": null,
  "expectedRestoreDate": null,
  "address": "Пример адреса",
  "lat": 55.1,
  "lon": 60.2,
  "siteType": "macro",
  "cells": [
    { "cellId": 1, "localCellId": 1, "sector": "A", "cssr": 97.42, "dcr": 1.31, "availability": 95.83, "trafficGb": 1.25 }
  ],
  "anomalies": [
    { "anomalyId": 7, "type": "threshold", "kpiCode": "cssr", "severity": "warning", "status": "new" }
  ]
}
```

## GET `/api/monitoring/cells`

| Query | Тип | Умолчание и обработка |
| --- | --- | --- |
| `from`, `to` | `DateTime?` | Необязательны; период по общему правилу выше. |
| `regionId` | `ushort?` | Необязателен; регион родительского элемента. |
| `btsId` | `uint?` | Необязателен; точное совпадение `cells.bts_id`. |
| `page` | `int` | `1`; значения `<1` заменяются на `1`. |
| `pageSize` | `int` | `50`; `<1` заменяется на `50`, `>200` ограничивается `200`. |
| `sort` | `string` | `cssr`; поддержаны `dcr`, `availability`, `trafficgb`, без учёта регистра. Любое другое значение выбирает `cssr`. Пробелы не обрезаются. |
| `order` | `string` | `asc`; только `asc` без учёта регистра означает возрастание. Любое другое значение — убывание. |

`total` — количество сот после фильтров региона/BTS, до пагинации. KPI и аномалии агрегируются за период; для аномалий исключены `resolved` и `false_positive`. Сортировка идёт по уже округлённым значениям в памяти. Дополнительный порядок при равных KPI не задан. [Параметры, агрегации и DTO](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/MonitoringController.cs#L360-L449), [сортировка и пагинация](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/MonitoringController.cs#L450-L469).

`200 OK`: целые `total/page/pageSize`; `items` — массив объектов. В item `cellId/localCellId/anomalyCount` целые, `btsCode/sector` строки, `regionCode/worstSeverity` строки или `null`, остальные поля — числовые KPI. Поля `btsId` в ответе **нет**.

Пример: `GET /api/monitoring/cells?btsId=1&page=1&pageSize=50&sort=dcr&order=desc`

```json
{
  "total": 1,
  "page": 1,
  "pageSize": 50,
  "items": [
    { "cellId": 1, "btsCode": "BTS-001", "localCellId": 1, "sector": "A", "regionCode": "REG1", "cssr": 97.42, "dcr": 1.31, "availability": 95.83, "trafficGb": 1.25, "anomalyCount": 1, "worstSeverity": "warning" }
  ]
}
```

Нет совпадений или страница за концом списка — `items: []`; ошибки диапазона дат собственным `400` не обрабатываются.

## GET `/api/monitoring/cells/{id}/kpi`

`id` — обязательный `uint`; `from` и `to` — обязательные query `DateTime?`. Умолчаний, пагинации и максимального периода нет. Сначала проверяется наличие обеих дат, затем существование соты; `from > to` отдельно не проверяется. Возвращаются существующие строки KPI с обеими включёнными границами, по `tsHour` по возрастанию. Пропуски не заполняются. [Полный метод](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/MonitoringController.cs#L474-L508).

`200 OK` — массив: `tsHour` строка UTC указанного формата; `cssr/dcr/trafficGb` числа; `availability` целое `0` или `1`.

Пример: `GET /api/monitoring/cells/1/kpi?from=2026-11-14T00%3A00%3A00Z&to=2026-11-14T01%3A00%3A00Z`

```json
[
  { "tsHour": "2026-11-14T00:00:00Z", "cssr": 97.42, "dcr": 1.31, "trafficGb": 0.12, "availability": 1 },
  { "tsHour": "2026-11-14T01:00:00Z", "cssr": 0, "dcr": 0, "trafficGb": 0, "availability": 0 }
]
```

Без `from` или `to`: `400`, `{"message":"Параметры 'from' и 'to' обязательны."}`. Нет соты: `404`, `{"error":"not_found"}`. Есть сота, но нет строк или задан обратный период: `[]`.

## POST `/api/monitoring/detect-anomalies`

Требует `operator` или `admin`. Тело JSON:

```json
{ "hoursBack": 192 }
```

`hoursBack` — `int`, при отсутствии свойства в `{}` равен **192**. DTO и метод не ограничивают минимум/максимум, поэтому контракт не обещает `400` для нуля или отрицательных значений. Пустой HTTP body и некорректный JSON не эквивалентны `{}`. [DTO](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/MonitoringController.cs#L595-L603).

Метод синхронно устанавливает command timeout `180` секунд и последовательно вызывает:

1. `sp_detect_anomalies(hoursBack)`.
2. `sp_detect_availability_anomalies(from, to)`, где `from = начало текущего UTC-часа − hoursBack`, `to = начало текущего UTC-часа + 1 час`. Это окно на один час шире `hoursBack`.

`found = max(0, общее количество аномалий после − до)`, а не число каждой новой вставки. `processed` — сумма положительных результатов двух `ExecuteSqlRaw`, если она больше нуля, иначе `found`. SQL-поле `processed_rows` не читается как отдельный DTO. `durationMs` включает обе процедуры и попытки аудита. При успехе вызывается аудит `detect_anomalies` и `detect_availability_anomalies` с `entityType="anomaly"`. [Метод и авторизация](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/MonitoringController.cs#L513-L555).

`200 OK`:

```json
{ "found": 3, "processed": 5, "durationMs": 821 }
```

Все три поля — целые числа. Обёртка не задаёт общей транзакции на две процедуры; если вторая завершится ошибкой, метод не вернёт успешный JSON, а изменения первой не следует считать автоматически отменёнными. Собственного JSON ошибки БД нет.

### Что делают процедуры

`sp_detect_anomalies` берёт начало текущего UTC-часа БД и вычисляет `v_from`. Удаляет аномалии с `ts_from >= v_from`, статусом `new`/`explained` и типом `threshold`/`zscore`/`missing_data`/`logical`, затем создаёт пороговые CSSR/DCR, z-score DL-трафика, группы пропущенных часов и логическое `conn_success > conn_attempt`. Для порогов/z-score/logical в SQL стоит нижняя граница без явной верхней. Поэтому повторный запуск может пересоздать записи и изменить их ID, а `found` отражает только чистый прирост. Дедупликация основана на `dedup_hash` и `INSERT IGNORE`. [Основная процедура](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/init.sql#L1134-L1239), [missing-data и logical](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/init.sql#L1241-L1360).

Проверка missing-data использует общую scratch-таблицу `_sp_tmp_missing`, генерирует максимум 1000 предыдущих часов, исключает `maintenance/decommissioned` и требует предыдущие PM-данные за 7 дней. У API нет отдельного ограничения `hoursBack` до возможностей этого генератора. Автопривязка выбирает минимальный ID пересекающегося по времени инцидента той же BTS; она не равна ручной проверке причины инженером. [Missing-data](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/init.sql#L1241-L1273), [автообъяснение](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/init.sql#L1362-L1381).

`sp_detect_availability_anomalies` вычисляет долю имеющихся KPI-строк, где `conn_attempt > 0` **или** DL/UL-трафик больше нуля, и сравнивает с порогом `availability`. Это отличается от GET-формулы, которая смотрит только на `conn_attempt`. Процедура использует `[from,to)`, пропущенные строки в знаменатель не включает; реального измерения доступности здесь также нет. [Вся процедура](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/init.sql#L1387-L1447).

## POST `/api/monitoring/refresh-kpi`

Требует `operator` или `admin`. Тело:

```json
{ "from": "2026-11-14T00:00:00Z", "to": "2026-11-15T00:00:00Z" }
```

`from/to` — свойства `DateTime` без `[Required]` и без собственной проверки диапазона. Это не nullable query-параметры: отсутствующее в `{}` свойство получает `default(DateTime)` (`0001-01-01`), а не понятную ошибку «поле обязательно». Такая дата может быть отвергнута провайдером/БД. Клиенту нужно передавать обе даты явно. Метод не запрещает обратный, равный, будущий или слишком длинный период. [DTO и метод](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/MonitoringController.cs#L560-L603).

Устанавливает timeout `180` секунд, преобразует даты в UTC, передаёт процедуре строки `yyyy-MM-dd HH:mm:ss` (дробная часть секунд теряется), вызывает `sp_refresh_kpi_hourly`. Процедура агрегирует PM-счётчики по соте/часу, рассчитывает CSSR как `100×success/attempt`, DCR как `100×drop/success` с `NULLIF` при нулевом знаменателе и вставляет/обновляет строки KPI за **`[from,to)`**. Аномалии этот метод не пересчитывает. [SQL-пересчёт](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/init.sql#L1096-L1128).

`processed` — результат `ExecuteSqlRaw`, если он положительный; иначе количество KPI-строк в исходном UTC-диапазоне с **включённым** `to`. Это число может отличаться от числа уникальных вставленных/изменённых строк процедуры. После выполнения вызывается аудит `refresh_kpi`, `entityType="kpi"`. [Подсчёт и ответ](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/MonitoringController.cs#L564-L581).

`200 OK`, оба поля целые:

```json
{ "processed": 720, "durationMs": 184 }
```

## Ошибки и аудит

Помимо явно приведённых `400/404`, `[ApiController]` обрабатывает ошибки привязки дат/чисел, некорректный JSON и отсутствующее обязательное тело. Они не обязаны иметь тот же JSON, что ручные `BadRequest` выше; клиент должен читать HTTP-статус и учитывать `message`, `error` либо стандартный объект ошибки валидации. Контроллер не перехватывает исключения БД и не задаёт единый формат `500`. [Атрибут API](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/MonitoringController.cs#L13-L16), [pipeline без пользовательского обработчика ошибок](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Program.cs#L96-L113).

`AuditService` сохраняет пользователя/роль из JWT, IP, действие и результат; исключение записи лога только журналируется и не отменяет успешный HTTP-ответ операции. Поэтому вызов `_audit.Log` не гарантирует наличие записи в БД. На пути исключения самих процедур эти методы отдельно не вызывают аудит с `success=false`. [Реализация аудита](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Services/AuditService.cs#L53-L122).

## Маршруты и замечания для JS/PHP

| Метод и маршрут | Доступ | Форма успеха |
| --- | --- | --- |
| `GET /api/monitoring/summary` | Любой валидный JWT | Объект summary. |
| `GET /api/monitoring/network-elements` | Любой валидный JWT | Массив без пагинации. |
| `GET /api/monitoring/network-elements/{id}` | Любой валидный JWT | Объект с `cells` и `anomalies`. |
| `GET /api/monitoring/cells` | Любой валидный JWT | `{total,page,pageSize,items}`. |
| `GET /api/monitoring/cells/{id}/kpi` | Любой валидный JWT | Массив; даты обязательны. |
| `POST /api/monitoring/detect-anomalies` | `operator`, `admin` | `{found,processed,durationMs}`. |
| `POST /api/monitoring/refresh-kpi` | `operator`, `admin` | `{processed,durationMs}`. |

В этом коммите не зарегистрированы `/api/dashboard/summary`, `/api/bts`, `/api/cells`, `/api/monitoring/bts`, `GET /api/monitoring/cells/{id}` или `/api/monitoring/anomalies`. Для аномалий используйте [отдельный контракт `/api/anomalies`](anomalies.md); наличие старого пути в комментарии не создаёт alias.

JS/PHP-примеры должны явно выбирать массив или `items`, передавать обе даты для почасового ряда, отправлять POST с `Content-Type: application/json` и проверять HTTP-код до разбора успешного DTO. В JS `fetch` не отклоняет Promise только из-за `400/403/500`; в PHP проверяйте HTTP-код cURL. Не показывайте поля `availability` как подтверждённый uptime, не пересчитывайте `Gb` ещё раз в десятичные GB и не обещайте пользователю скрытия координат.
