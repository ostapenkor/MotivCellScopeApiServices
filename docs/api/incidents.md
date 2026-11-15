# Инциденты

Описание реализации `main` на коммите `db7f93001de5e57b710cad7a03cb716e706a8a0d`. Базовый путь — `/api/incidents`; контроллер целиком защищён `[Authorize]`. Чтение доступно любому аутентифицированному пользователю, без проверки конкретной роли. POST/PUT/DELETE дополнительно требуют `operator` или `admin`. См. [IncidentsController.cs:13–24][controller]. Общие ошибки привязки, Bearer-аутентификации и необработанные ошибки описаны в [общих правилах](auth-reference-health.md#общие-правила).

## GET /api/incidents

Возвращает все инциденты, **начавшиеся** за указанный период, и имя автора из `users`. Тела запроса нет. Источник: [IncidentsController.cs:26–97][list].

| Query-параметр | Тип | Обязательность / умолчание |
|---|---|---|
| `from` | `DateTime?`, распознаваемая ASP.NET Core дата/время | Обязателен в логике метода; умолчания нет. |
| `to` | `DateTime?` | Обязателен в логике метода; умолчания нет. |

Пример: `/api/incidents?from=2026-11-01T00:00:00Z&to=2026-11-15T00:00:00Z`. Значения приводятся через `ToUniversalTime()`. Проверка требует `from <= to`, равные границы допустимы. Предикат **`BeginTime >= fromUtc && BeginTime <= toUtc`** включает обе границы. Проверки пересечения длительности с периодом нет: начавшийся раньше инцидент не попадёт в список, даже если он ещё открыт.

Строгая проверка наличия `Z`/смещения отсутствует: вход — `DateTime`, не строковый валидатор ISO. При передаче `+HH:MM` в URL знак `+` следует кодировать как `%2B`. Параметры пагинации, ID-фильтры, категория, важность, статус, поиск и сортировка в сигнатуре отсутствуют и не применяются. Возвращаются все строки, сортировка только по `BeginTime` по убыванию; порядок при одинаковом времени не задан. Периода по умолчанию «30 дней» нет.

`200 OK`, точная форма с одной условной записью:

```json
{
  "total": 1,
  "items": [
    {
      "incidentId": 42,
      "pw": "PW-42",
      "directionCode": "DIR",
      "techCenterCode": "TC",
      "priorityCode": "1",
      "networkTypeCode": "LTE",
      "equipmentTypeCode": "EQ",
      "alarmTypeCode": "ALARM",
      "networkElementType": "bts",
      "networkElementName": "Базовая станция 42",
      "servElem": "Базовая станция 42",
      "numbRbs": "2",
      "descr": "Отключение питания",
      "beginTime": "2026-11-14T08:00:00Z",
      "endTime": "2026-11-14T09:00:00Z",
      "akb": "01:00:00",
      "dgu": null,
      "lenght": "0 days, 01:00:00",
      "prostoy": "00:30:00",
      "commonProstoy": "0 days, 01:00:00",
      "alarmWho": "Дежурный",
      "descNotice": "Питание восстановлено",
      "adminDistrict": "Район",
      "severity": "high",
      "category": "power",
      "createdBy": "operator",
      "createdAt": "2026-11-14T09:01:00Z"
    }
  ]
}
```

`total` — число возвращённых элементов, `incidentId` — `ulong`. `beginTime` и `createdAt` — строки фиксированного формата `yyyy-MM-ddTHH:mm:ssZ`; `endTime` — такая строка либо null. `severity`, `category` — строки. Остальные поля элемента — строки либо null, включая `createdBy`, если пользователя нет в LEFT JOIN. Никаких `page`, `pageSize`, `from`, `to` в конверте нет. Пустой результат — `{"total":0,"items":[]}`.

Особенность списка: `akb`, `dgu`, `prostoy` формируются стандартным `TimeSpan.ToString()`, поэтому для длительности больше суток возможна запись `2.00:00:00`. Это отличается от формата detail/POST/PUT/CSV ниже. `lenght` и `commonProstoy` читаются сохранёнными, без пересчёта. Типы источника: [VIncidentsFull.cs:8–64][view].

Ошибки метода:

| Код | Условие | Тело |
|---|---|---|
| 400 | Нет хотя бы одной даты | `{"message":"Параметры 'from' и 'to' обязательны и должны быть в формате ISO 8601."}` |
| 400 | `from > to` | `{"message":"Дата начала 'from' не может быть больше даты окончания 'to'."}` |
| 400 | Неразбираемая дата | Стандартный validation problem от `[ApiController]`. |
| 401 | Нет валидного JWT | Ответ middleware. |
| 500 | Необработанная ошибка БД/выполнения | Собственная стабильная форма не задана. |

## GET /api/incidents/{id}

Параметр пути `id` обязателен, тип `ulong` (`0…18446744073709551615`). Отдельной проверки `id > 0` нет. Тела и query-параметров нет. Выбирает сущность `incidents` с INNER JOIN на `v_incidents_full`. Источник: [IncidentsController.cs:99–163][detail].

`200 OK`, точная форма **детального объекта**; ту же форму возвращают успешные POST и PUT:

```json
{
  "incidentId": 42,
  "pw": "PW-42",
  "directionId": 1,
  "directionCode": "DIR",
  "techCenterId": 1,
  "techCenterCode": "TC",
  "priorityId": 1,
  "priorityCode": "1",
  "networkTypeId": 1,
  "networkTypeCode": "LTE",
  "equipmentTypeId": 1,
  "equipmentTypeCode": "EQ",
  "alarmTypeId": 1,
  "alarmTypeCode": "ALARM",
  "networkElementId": 42,
  "networkElementName": "Базовая станция 42",
  "networkElementType": "bts",
  "servElem": "Базовая станция 42",
  "numbRbs": "2",
  "descr": "Отключение питания",
  "beginTime": "2026-11-14T08:00:00Z",
  "endTime": "2026-11-14T09:00:00Z",
  "akb": "01:00:00",
  "dgu": null,
  "lenght": "0 days, 01:00:00",
  "prostoy": "00:30:00",
  "commonProstoy": "0 days, 01:00:00",
  "alarmWho": "Дежурный",
  "descNotice": "Питание восстановлено",
  "adminDistrict": "Район",
  "regionId": 1,
  "severity": "high",
  "category": "power",
  "createdByUserId": 2,
  "createdAt": "2026-11-14T09:01:00Z",
  "updatedAt": "2026-11-14T09:01:00Z"
}
```

`incidentId` — число `ulong`; nullable `directionId`, `techCenterId`, `priorityId`, `networkTypeId`, `equipmentTypeId`, `alarmTypeId`, `regionId` — `ushort`; nullable `networkElementId`, `createdByUserId` — `uint`. Время начала/создания/обновления — строки с суффиксом `Z`, до секунд; `endTime` nullable. Строки `severity`, `category` не nullable; остальные строковые поля nullable. См. [Incident.cs:8–62][entity].

Detail не возвращает `createdBy` с именем, имена справочников, `status`, числовые секунды длительности или простоя. `akb`/`dgu`/`prostoy` форматируются helper-методом как **суммарные часы:минуты:секунды**, например `48:00:00`, без обрезания на 24 часа. См. [IncidentsController.cs:717–722][time-format]. Остальные сохранённые длительности не пересчитываются. Даты форматируются с литералом `Z`, без отдельного преобразования `ToUniversalTime()` при чтении; запись POST/PUT предварительно приводит входные даты к UTC.

Ошибки: `404 {"error":"not_found"}`, если результат JOIN не найден; некорректный/отрицательный/слишком большой `id` — стандартный `400`; общие `401` и возможный необработанный `500`.

## Общий JSON-вход POST и PUT

Методы используют один `CreateIncidentRequest`, не сущность БД. Тело JSON необходимо для model binding. Перечень полей ниже полный. Источник: [IncidentsController.cs:744–768][request].

| Поле | Тип | При пропуске / обработка |
|---|---|---|
| `pw` | string или null | null; явная проверка длины ≤450. |
| `directionId` | ushort или null | null. |
| `techCenterId` | ushort или null | null. |
| `priorityId` | ushort или null | null. |
| `networkTypeId` | ushort или null | null. |
| `networkElementId` | uint или null | null; если указан, существование проверяется отдельно. |
| `servElem` | string или null | null; может быть заменён `full_name` сетевого элемента. |
| `numbRbs` | string или null | null; отдельной проверки числа/сектора/диапазона нет. |
| `equipmentTypeId` | ushort или null | null. |
| `alarmTypeId` | ushort или null | null. |
| `descr` | string или null | null. |
| `beginTime` | JSON-строка даты/времени → DateTime | В DTO не nullable, но нет `[Required]`: при **пропуске** остаётся `DateTime.MinValue`. Явный null/неразбираемая дата — ошибка привязки. |
| `endTime` | строка даты/времени или null | null: длительность при записи считается до текущего UTC. |
| `akb` | string или null | null; парсер `TimeSpan`, см. ниже. |
| `dgu` | string или null | null; тот же парсер. |
| `prostoy` | string или null | null; тот же парсер. |
| `alarmWho` | string или null | null. |
| `descNotice` | string или null | null. |
| `adminDistrict` | string или null | null. |
| `regionId` | ushort или null | null. |
| `severity` | string или null | null/пустая/whitespace → `medium`; иначе `ToLower()` без Trim. |
| `category` | string или null | null/пустая/whitespace → `other`; иначе `ToLower()` без Trim. |

Числовые типы ограничивают представимый диапазон при привязке: ushort `0…65535`, uint `0…4294967295`. Отдельного запрета нуля и проверки всех FK до сохранения нет. Для nullable-полей пропуск и null эквивалентны. Для `beginTime` пропуск не гарантирует аккуратный `400`: нижняя граница даты в методах не проверяется, дальнейшее сохранение может завершиться ошибкой БД. Клиенту следует явно передавать дату начала.

Пример тела:

```json
{
  "pw":"PW-42",
  "directionId":1,
  "techCenterId":1,
  "priorityId":1,
  "networkTypeId":1,
  "networkElementId":42,
  "servElem":"Имя из формы",
  "numbRbs":"2",
  "equipmentTypeId":1,
  "alarmTypeId":1,
  "descr":"Отключение питания",
  "beginTime":"2026-11-14T08:00:00Z",
  "endTime":"2026-11-14T09:00:00Z",
  "akb":"01:00:00",
  "dgu":null,
  "prostoy":"00:30:00",
  "alarmWho":"Дежурный",
  "descNotice":"Питание восстановлено",
  "adminDistrict":"Район",
  "regionId":1,
  "severity":"high",
  "category":"power"
}
```

### Проверки и расчёты при записи

Проверки POST: [IncidentsController.cs:173–236][create-validation]; PUT: [357–418][update-validation].

1. `pw`, если не null, не длиннее 450 символов.
2. `beginTime.ToUniversalTime()` не позднее `UtcNow + 1 минута`.
3. `endTime`, если задан, после преобразования в UTC не раньше начала. Равенство разрешено; отдельного запрета будущего окончания нет.
4. Категория после нормализации: `power`, `transport`, `hardware`, `software`, `planned`, `client`, `external`, `other`.
5. Важность после нормализации: `low`, `medium`, `high`, `critical`. Окружающие пробелы непустого значения не удаляются, поэтому `" high "` не проходит.
6. `akb`/`dgu`/`prostoy`: null допускается. Для не-null результат парсинга должен быть не null. Пустая или whitespace-строка даёт `400`. Парсер сначала разбирает три целых компонента через `new TimeSpan(hours, minutes, seconds)`, затем пробует `TimeSpan.TryParse`. Это **не строгий HH:MM:SS-валидатор**: допускаются стандартные альтернативные формы, отрицательные интервалы и нормализация переполненных минут/секунд. Отдельной проверки `0…838:59:59` нет; ошибки переполнения конструктора не перехватываются. Источник: [IncidentsController.cs:696–714][time-parse].
7. Если задан `networkElementId`, запись должна существовать. Если её `FullName` непустое, оно заменяет `servElem`; иначе сохраняется переданный `servElem`. Согласованность направления/ТЦ/области с элементом не проверяется. Остальные FK не проверяются отдельными запросами.

Для остальных текстовых полей самостоятельных проверок длины нет. Ограничения схемы/FK действуют при сохранении, однако ошибки БД не превращаются здесь в гарантированные `400`/`409`.

Расчёт в обоих методах:

```text
effectiveEnd = endTimeUtc ?? DateTime.UtcNow
duration = max(effectiveEnd - beginTimeUtc, TimeSpan.Zero)
formatted = "{duration.Days} days, {duration.Hours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
lenght = formatted
commonProstoy = formatted
```

Именно `lenght` (такое написание в коде) и `commonProstoy` получают **одинаковую строку**. Значения `prostoy` и `numbRbs` в расчёте не участвуют; отдельной обработки секторов/неизвестного количества нет. Например, начало `08:00`, конец `09:00`, простой `00:30:00` и `numbRbs="10"` всё равно дадут `commonProstoy="0 days, 01:00:00"`. Для открытой аварии значение фиксируется во время POST/PUT; GET и экспорт не обновляют его по текущему времени. Источники: [238–281][create-calculation], [420–452][update-calculation].

### POST /api/incidents

Доступ: только `operator,admin`. Создаёт инцидент из описанного JSON. Автор — `ClaimTypes.NameIdentifier`, разобранный как uint; при неудаче сохраняется null. Отдельной проверки существования автора в `users` метод не делает. `createdAt` и `updatedAt` задаются `UtcNow`; производные поля/автор не являются входными свойствами DTO. Источник: [IncidentsController.cs:169–340][create].

Успех: **`201 Created`**, `Location` на GET созданного ID и полный [детальный объект](#get-apiincidentsid) в теле. Данные после сохранения перечитываются через JOIN с представлением. После первого `SaveChanges()` вызывается аудит `create_incident`, `entityType="incident"`, `entityId` созданного инцидента. Аудит не включён в общую транзакцию с записью инцидента.

Ошибки: `400 {"error":"validation_failed","field":"<имя>"}` для перечисленных проверок; `field` может быть `pw`, `beginTime`, `endTime`, `category`, `severity`, `akb`, `dgu`, `prostoy`, `networkElementId`. Также стандартные `400`/`415` привязки тела, `401`, `403` при иной роли, возможный необработанный `500`. Собственного `409` нет.

### PUT /api/incidents/{id}

Доступ: только `operator,admin`. `id` обязателен, `ulong`; тело и проверки как у POST. Сначала ищет существующую сущность, затем проверяет поля. Источник: [IncidentsController.cs:346–511][update].

Это **полная перезапись полей DTO**, не PATCH: отсутствующие nullable-поля становятся null, пропущенные category/severity получают `other`/`medium`, пропущенный beginTime сохраняет значение по умолчанию DTO. `createdByUserId` и `createdAt` не изменяются. `updatedAt` задаётся `UtcNow`. Производные длительности пересчитываются по указанному выше правилу.

Успех: **`200 OK`** с полным [детальным объектом](#get-apiincidentsid). Аудит `update_incident` записывается после сохранения отдельно.

Ошибки: `404 {"error":"not_found"}` для отсутствующего ID; `400 validation_failed` с теми же полями, что POST; стандартный `400` для неверного ID/JSON/типов; `401`, `403`, возможный необработанный `500`. Параметра `expectedUpdatedAt`, блокировки строки и специальной проверки конфликта версий нет; `409` метод не формирует.

## DELETE /api/incidents/{id}

Доступ: только `operator,admin`. Обязателен путь `id: ulong`; тела и query-параметров нет. Источник: [IncidentsController.cs:517–556][delete].

Перед удалением выбирает все аномалии с `ExplainedByIncident == id`, сбрасывает эту связь в null и меняет их статус **`explained` → `new`**; остальные статусы сохраняет. Удаление инцидента и изменения аномалий сохраняются одним `SaveChanges` (в одной транзакции EF Core). Аудит `delete_incident` вызывается после него отдельно.

Успех: **`204 No Content`**, пустое тело. Ошибки: `404 {"error":"not_found"}`; стандартный `400` при неверном ID; `401`, `403`, возможный необработанный `500`. Проверки версии/`expectedUpdatedAt` нет.

## GET /api/incidents/export

Доступ: любой аутентифицированный пользователь, в том числе `engineer`. Тело отсутствует. **Оба** query-параметра `from: DateTime?` и `to: DateTime?` обязательны в логике метода, без умолчаний. Приведение к UTC, допустимое равенство границ, фильтр `BeginTime >= fromUtc && BeginTime <= toUtc`, порядок по началу по убыванию и ошибки периода полностью совпадают с [GET списка](#get-apiincidents). Источник: [IncidentsController.cs:563–640][csv].

`200 OK` возвращает файл:

- `Content-Type: text/csv; charset=utf-8`.
- Имя `incidents_{fromUtc:yyyy-MM-dd}_{toUtc:yyyy-MM-dd}.csv`, например `incidents_2026-11-01_2026-11-15.csv`.
- UTF-8. **BOM фактически не добавлен:** `new UTF8Encoding(true).GetBytes(...)` кодирует текст, но код не добавляет `GetPreamble()` к массиву байтов. Комментарий в исходнике о BOM не соответствует этому вызову; [экспорт reports](reports.md#get-apireportsexport) добавляет preamble явно.
- Разделитель `;`; переводы строк — `StringBuilder.AppendLine`, зависят от платформы.
- Все выбранные строки, без пагинации.

Точный заголовок, **23 столбца**:

```csv
Incident ID;PW;Направление;ТЦ;Приоритет;Тип сети;Сетевой элемент;Кол-во БС;Тип оборуд.;Тип аварии;Описание;Начало;Окончание;АКБ;ДГУ;Длительность;Простой;Общий простой;Уведомление;Комментарий;Район;Severity;Category
```

| № | Значение |
|---|---|
| 1 | `IncidentId` |
| 2 | `Pw` |
| 3 | `DirectionCode` |
| 4 | `TechCenterCode` |
| 5 | `PriorityCode` |
| 6 | `NetworkTypeCode` |
| 7 | Непустой `ServElem`, иначе `NetworkElementName` |
| 8 | `NumbRbs` |
| 9 | `EquipmentTypeCode` |
| 10 | `AlarmTypeCode` |
| 11 | `Descr` |
| 12 | `BeginTime` в `yyyy-MM-dd HH:mm` |
| 13 | `EndTime` в том же формате, либо пусто |
| 14 | `Akb` через суммарные часы `FormatTimeSpan`, либо пусто |
| 15 | `Dgu` в том же формате, либо пусто |
| 16 | Сохранённое `Lenght` |
| 17 | `Prostoy` через `FormatTimeSpan`, либо пусто |
| 18 | Сохранённое `CommonProstoy` |
| 19 | `AlarmWho` |
| 20 | `DescNotice` |
| 21 | `AdminDistrict` |
| 22 | `Severity` |
| 23 | `Category` |

Пример строки с null АКБ/ДГУ/окончанием:

```csv
42;PW-42;DIR;TC;1;LTE;Базовая станция 42;2;EQ;ALARM;Отключение питания;2026-11-14 08:00;;;;0 days, 01:00:00;00:30:00;0 days, 01:00:00;Дежурный;Комментарий;Район;high;power
```

`EscapeCsv` применяется к текстовым ячейкам: null/пустое значение → пустая ячейка; если есть `;`, `"`, LF или CR, значение окружается двойными кавычками, внутренние кавычки удваиваются. Остальные значения не заключаются в кавычки. Заголовок не заключён в кавычки. Нейтрализации формул (`=`, `+`, `-`, `@`) нет. Источник: [IncidentsController.cs:727–738][csv-escape].

Перед ответом вызывается аудит `export_incidents`, `entityType="incident"`, `entityId=null`. Ошибки: два собственных `400 {message}` для периода, стандартный `400` привязки дат, `401`, возможный необработанный `500`. `403` по конкретной роли не предусмотрен.

## GET /api/incidents/{id}/anomalies

Доступ: любой аутентифицированный пользователь. Обязателен `id: ulong`; тела и query-параметров нет. Проверяет существование инцидента, затем возвращает все связанные `ExplainedByIncident == id` аномалии с LEFT JOIN к сотам и сетевым элементам. Порядок — `TsFrom` по убыванию; фильтра по статусу, типу или severity нет. Источник: [IncidentsController.cs:646–689][anomalies].

`200 OK`, ответ — **массив**, без total/items:

```json
[
  {
    "anomalyId": 8,
    "cellId": 7,
    "btsCode": "BTS-42",
    "sector": "A",
    "anomalyType": "threshold",
    "kpiCode": "cssr_pct",
    "severity": "warning",
    "tsFrom": "2026-11-14T08:00:00Z",
    "tsTo": "2026-11-14T09:00:00Z",
    "observedValue": 80.0,
    "expectedValue": 95.0,
    "status": "explained"
  }
]
```

`anomalyId` — `ulong`, `cellId` — nullable uint; `btsCode`, `sector`, `kpiCode` — строки либо null; `observedValue`, `expectedValue` — decimal либо null; `anomalyType`, `severity`, `status` — строки; `tsFrom`, `tsTo` — строки формата `yyyy-MM-ddTHH:mm:ssZ`. `score`, `detectedAt`, `explainedByIncident` в JSON не входят, хотя присутствуют в модели. Типы: [Anomaly.cs:8–32][anomaly-model]. Без связей возвращается `[]`.

Ошибки: `404 {"error":"not_found"}` при отсутствии самого инцидента; стандартный `400` для неверного ID; `401`, возможный необработанный `500`.

## Аудит и покрытие

Аудит записи/удаления/экспорта выполняется после основной операции; ошибки сервиса логируются и подавляются. Успех основной операции не подтверждает наличие аудита. Источник: [AuditService.cs:53–122][audit]. В методах чтения списка/detail/anomalies отдельного вызова аудита нет.

Описаны все **7** маршрутов `IncidentsController`. В текущей реализации нет алиаса `/api/accidents`, общих фильтров/пагинации, интервала `[from,to)`, поиска, optimistic concurrency или единого `{error,message,field}` для всех ошибок. Эти отличия от предложенного ранее контракта приведены для корректного использования существующего API; код не изменялся.

[controller]: https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/IncidentsController.cs#L13-L24
[list]: https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/IncidentsController.cs#L26-L97
[detail]: https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/IncidentsController.cs#L99-L163
[create-validation]: https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/IncidentsController.cs#L173-L236
[update-validation]: https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/IncidentsController.cs#L357-L418
[create-calculation]: https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/IncidentsController.cs#L238-L281
[update-calculation]: https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/IncidentsController.cs#L420-L452
[create]: https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/IncidentsController.cs#L169-L340
[update]: https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/IncidentsController.cs#L346-L511
[delete]: https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/IncidentsController.cs#L517-L556
[csv]: https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/IncidentsController.cs#L563-L640
[anomalies]: https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/IncidentsController.cs#L646-L689
[time-parse]: https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/IncidentsController.cs#L696-L714
[time-format]: https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/IncidentsController.cs#L717-L722
[csv-escape]: https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/IncidentsController.cs#L727-L738
[request]: https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Controllers/IncidentsController.cs#L744-L768
[entity]: https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Models/Incident.cs#L8-L62
[view]: https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Models/VIncidentsFull.cs#L8-L64
[anomaly-model]: https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Models/Anomaly.cs#L8-L32
[audit]: https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Services/AuditService.cs#L53-L122
