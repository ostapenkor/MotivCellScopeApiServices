# REST API сайта МОТИВ — фактический контракт ветки kolya-api-sync

Основа: `main` на коммите `aff0a9f`. Схема — корневой `init.sql`, MariaDB 10.1. API на C# выполняет запросы синхронно. DTO ввода/вывода находятся в файлах соответствующих контроллеров. PHP-сайт целиком находится в `php-site/` и обращается к API синхронным cURL.

## 1. Общие правила

- Локальный API: `http://127.0.0.1:8080`; сайт: `http://127.0.0.1:5092`.
- В Docker PHP использует `API_BASE_URL=http://api:8080`; пути ниже уже содержат `/api`.
- JSON — camelCase, кодировка UTF-8. Защищённые запросы: `Authorization: Bearer <JWT>`.
- Роли: `engineer` — чтение и CSV; `operator`, `admin` — также правка инцидентов и новые команды пересчёта мониторинга.
- JWT хранится в PHP-сессии. Браузеру передаётся HttpOnly-cookie с идентификатором сессии. PHP не читает MariaDB напрямую. CORS для связи PHP → API не нужен: это серверный HTTP-запрос.
- Новые методы принимают даты ISO 8601 с секундами и `Z` или смещением: `2026-11-14T08:00:00Z`, `2026-11-14T13:00:00+05:00`. В БД используется UTC. В новых фильтрах `from` включается, `to` исключается. Особенности прежних методов admin описаны отдельно ниже.
- `page` начинается с 1, `pageSize` — 1–200, по умолчанию 50. Страница: `{items,total,page,pageSize,from,to}`. `total` относится ко всей отфильтрованной выборке.
- Основные ошибки: 400 — неверные данные; 401 — нет действительного JWT; 403 — недостаточно прав; 404 — нет объекта; 409 — конфликт версии/данных; 503 — недоступна БД. Ошибки инцидентов и отчётов, включая привязку JSON, содержат `{error,message,field?}`. Общий обработчик привязки остальных контроллеров может добавить `fields`; мониторинг может вернуть `{message}`. Это фактические формы, а не обещание одинакового тела для всех ошибок существующего main.

Авторизация и административные функции принадлежат коллеге. Их контроллеры в этой ветке не изменены. Форма PHP только использует готовые `POST /api/auth/login` и `GET /api/auth/me`. Срок JWT остаётся **2 часа**, как в текущем main; значение 8 часов из обсуждения не внедрялось. Нового серверного logout, создания пользователей и административного аудита эта работа не добавляет.

## 2. Служебные методы и справочники

| Метод | Вход | Что делает и зачем | Выход |
| --- | --- | --- | --- |
| `GET /api/health` | Нет, без JWT | Проверяет соединение с БД; диагностика запуска | 200 `{status,database,version,timestamp}` либо 503 со статусом degraded |
| `GET /api/reference/all` | JWT | Заполняет все списки формы одним HTTP-запросом | `{regions,directions,technicalCenters,priorities,networkTypes,equipmentTypes,alarmTypes,categories,severities}` |
| `GET /api/regions` | JWT | Сохраняет прежний URL списка регионов | Массив `{regionId,code,name,tzOffsetMin}` |
| `GET /api/vendors` | JWT | Объясняет несовместимость: таблица vendors удалена автором новой схемы | 410 `{error:"vendors_removed",message}`; оборудование доступно в reference/all |

В справочниках передаются настоящие FK: `directionId`, `techCenterId`, `priorityId`, `networkTypeId`, `equipmentTypeId`, `alarmTypeId`, `regionId`, а также `code` и `name`. У ТЦ есть `directionId`, у приоритета — `sortOrder`. `categories` и `severities` имеют форму `{code,label}`.

**Не путать:** `priorityId` с кодом `1/2/3/А0/В/B` и `category=power/transport/...` — разные поля новой БД. API не преобразует одно в другое по догадке.

### Существующие методы авторизации и администратора

Эта таблица описывает код коллеги из main. Его действия и DTO не переписывались. Входы ниже — JSON; даты для старых методов также следует явно передавать в UTC с `Z`.

| Метод | Вход / доступ | Что делает и зачем | Выход 200 |
| --- | --- | --- | --- |
| `POST /api/auth/login` | `{username,password}`, без JWT | Проверяет BCrypt, записывает попытку входа в аудит, выдаёт JWT на 2 часа | `{token,expiresAt,username,role}`; неверная пара — 401 `{message}` |
| `GET /api/auth/me` | JWT | Читает ID, имя и роль из токена для текущей сессии сайта | `{userId,username,role}`; недействительный токен — 401 |
| `POST /api/admin/refresh-kpi` | `{from,to}`, JWT любой роли в текущем main | Вызывает sp_refresh_kpi_hourly, считает KPI-строки за период | `{processed,durationMs}` |
| `POST /api/admin/detect-anomalies` | `{hoursBack}`, по умолчанию 720; JWT любой роли | Запускает sp_detect_anomalies, сравнивает число аномалий до/после | `{found,processed,durationMs}` |
| `POST /api/admin/detect-availability` | `{from,to}`, JWT любой роли | Запускает sp_detect_availability_anomalies | `{found,durationMs}` |
| `POST /api/admin/recalculate-all` | `{from,to}`, JWT любой роли | Последовательно пересчитывает KPI, основные аномалии и аномалии доступности | `{kpiRefreshed,anomaliesFound,availabilityFound,durationMs}` |

`found` — прирост количества записей, а не общее число открытых аномалий. `processed` в refresh-kpi — число KPI-строк в периоде, а не обязательно число изменённых строк. Существующий AdminController считает строки по условию `from <= tsHour <= to`; он не реализует все ограничения нового фильтра M. У recalculate-all hoursBack — округлённая вверх длительность периода (при неположительной длительности 720); это не отдельный исторический интервал для детектора. Новые команды `/api/monitoring/...` добавляют валидацию и ограничение operator/admin. У старых admin-маршрутов эти изменения не появляются автоматически.

## 3. Аварии: вход, выход, правила

### 19 полей ТЗ

| Поле JSON | Ввод | Смысл/ограничения |
| --- | --- | --- |
| `pw` | string/null | Номер обращения или ссылка, до 450 символов |
| `directionId` | integer/null | Существующее направление |
| `techCenterId` | integer/null | Существующий ТЦ; согласован с указанным направлением |
| `priorityId` | integer/null | Приоритет из справочника |
| `networkTypeId` | integer/null | Технология/тип сети из справочника |
| `servElem` | string/null | Сетевой элемент, до 450; при выборе networkElementId берётся full_name объекта |
| `numbRbs` | string/null | `"1"`–`"10000"`, `"С"` для сектора либо неизвестно; латинская C нормализуется в С |
| `equipmentTypeId` | integer/null | Тип оборудования |
| `alarmTypeId` | integer/null | Тип аварии |
| `descr` | string/null | Краткое описание, до 450 |
| `beginTime` | ISO string, обязательно | Начало, не в будущем; хранится с точностью до секунды |
| `endTime` | ISO string/null | Окончание между началом и текущим UTC; null означает НВ |
| `akb` | string/null | Длительность `ЧЧ:ММ:СС`, от 00:00:00 до 838:59:59 |
| `dgu` | string/null | Длительность работы генератора с тем же диапазоном |
| `lenght` | Только выход | Расчётная длительность; старое написание сохранено для совместимости |
| `prostoy` | string/null | Ручной перерыв одной БС, тот же диапазон TIME |
| `commonProstoy` | Только выход | Перерыв × число полных БС; неизвестно для сектора/пропусков |
| `alarmWho` | string/null | Кому сообщили, до 450 |
| `descNotice` | string/null | Примечание, до 16000 символов |

Расширения: `adminDistrict` (до 128), `networkElementId`, `regionId`, `severity`, `category`. Категории: `power`, `transport`, `hardware`, `software`, `planned`, `client`, `external`, `other`. Критичность: `low`, `medium`, `high`, `critical`. По умолчанию категория other, критичность medium. Ненулевые ID проверяются по соответствующим справочникам; явно противоречащие связи ТЦ/направления/объекта/региона возвращают 400.

В выходном `IncidentDto` есть все перечисленные поля и их ID, `incidentId`, коды/названия справочников (`directionCode/Name`, `techCenterCode/Name` и т. д.), `networkElementName`, `regionCode/Name`, `status=open|closed`, `createdByUserId`, `createdByUsername`, `createdAt`, `updatedAt`, `durationSeconds`, `commonProstoySeconds`.

Автор берётся из JWT, а не из тела запроса. Переданные клиентом `lenght/commonProstoy` не управляют расчётом. НВ пересчитывается до текущего UTC при чтении. Длительность — количество часов, которое может превышать 24; это не время суток. API читает SQL TIME через TimeSpan, обходя ошибочный для длительностей TimeOnly из сгенерированной EF-модели. Общий простой может быть больше 838 часов, потому что хранится/возвращается строкой и секундами. NULL не подменяется нулём.

### Методы аварий

| Метод | Вход | Что делает / зачем | Выход |
| --- | --- | --- | --- |
| `GET /api/incidents` | Фильтр F, JWT | Таблица аварий и поиск для двух вкладок | Страница IncidentDto |
| `GET /api/incidents/{id}` | Положительный id, JWT | Полная карточка и заполнение формы | IncidentDto; 404 при отсутствии |
| `POST /api/incidents` | IncidentInput, operator/admin | Проверяет поля/FK, рассчитывает итоги, сохраняет событие и аудит транзакцией | 201 IncidentDto + Location |
| `PUT /api/incidents/{id}` | id + IncidentInput, operator/admin | Заменяет редактируемые поля, сохраняет автора создания, пишет аудит | 200 IncidentDto; 404/409 |
| `DELETE /api/incidents/{id}` | id, optional expectedUpdatedAt, operator/admin | Обнуляет связь аномалий с событием, удаляет событие и пишет аудит | 204; 404/409 |
| `GET /api/incidents/export` | Фильтр F, JWT | Все строки выборки в CSV для Excel | text/csv, без ограничения страницей |
| `GET /api/incidents/{id}/anomalies` | id, JWT | Показывает аномалии, объяснённые данной аварией | Массив `{anomalyId,cellId,anomalyType,kpiCode,severity,status,tsFrom,tsTo,observedValue,expectedValue,score,detectedAt,explainedByIncident}`; 404 если нет самой аварии |

**Фильтр F:** `from`, `to` (обе границы или ни одной; по умолчанию последние 30 суток), `directionId`, `techCenterId`, `priorityId`, `networkTypeId`, `equipmentTypeId`, `alarmTypeId`, `regionId`, `category`, `severity`, `status=all|open|closed`, буквальный `search` до 200 символов. Для списка дополнительно `page`, `pageSize`, `sort=begin_time|severity|pw`, `order=asc|desc`. Записи отбираются по пересечению периода: начало раньше to, окончание отсутствует или позже from. Сортировка имеет дополнительный ключ incidentId, чтобы страницы были стабильны.

PUT принимает необязательный `expectedUpdatedAt` из последнего GET. Если запись уже изменена, API возвращает 409 и ничего не перезаписывает. Без этого поля обновление безусловное; это сделано для совместимости. PHP-форма отправляет поле. При DELETE оно передаётся в query. Удаление сохраняет историю аудита и саму аномалию; её прежний status автоматически не переопределяется.

Пример нового события без привязки к реестру:

```json
{
  "pw": "NTT-DEMO-001",
  "servElem": "Учебный сетевой элемент",
  "numbRbs": "3",
  "descr": "Перерыв электропитания",
  "beginTime": "2026-11-14T08:00:00Z",
  "endTime": "2026-11-14T09:00:00Z",
  "akb": "00:30:00",
  "dgu": null,
  "prostoy": "00:20:00",
  "alarmWho": "Дежурный инженер",
  "descNotice": "Учебная запись",
  "severity": "high",
  "category": "power"
}
```

Ответ содержит `lenght="01:00:00"`, `commonProstoy="01:00:00"`, `commonProstoySeconds=3600` и серверные ID/автора/даты. Указанные даты должны уже наступить на момент выполнения примера.

## 4. Отчётность

| Метод | Вход | Что делает / зачем | Выход |
| --- | --- | --- | --- |
| `GET /api/reports/incidents-summary` | F, optional groupBy | Счётчики и диаграммы по той же выборке | `{from,to,total,knownDowntimeSeconds,excludedDowntimeCount,byCategory,bySeverity,byDirection,byTechCenter,byAlarmType,groupBy,groups}` |
| `GET /api/reports/incidents-export` | F, groupBy (по умолчанию direction) | Групповой CSV для отчётности | CSV агрегатов |
| `GET /api/reports/available-periods` | Нет фильтра, JWT | Начальная настройка календаря и выбор месяца | `{from,to,total,months,minBeginTime,maxBeginTime}` |
| `GET /api/reports/events` | F | Прежний URL таблицы | Та же страница инцидентов |
| `GET /api/reports/export` | F | Прежний URL CSV | Детальный CSV инцидентов |

`groupBy`: только `category`, `severity`, `direction`, `techCenter`, `alarmType`. Каждая группа: `{key,name,count,knownDowntimeSeconds,excludedDowntimeCount}`. Счётчики не зависят от page/pageSize. Сумма простоя включает только вычислимые значения, количество исключённых записей видно отдельно. Пропуск справочника образует группу с null, а не исчезает.

В available-periods `minBeginTime/maxBeginTime` — минимум и максимум начал. `from/to` охватывают также окончания и НВ до текущего UTC. `months` — месяцы начал `{year,month,from,to,count}`. Это информация о данных, а не запрет пользователю запросить другой период.

Детальный CSV содержит 23 колонки: 19 полей ТЗ в указанном порядке, затем `adminDistrict`, `region`, `severity`, `category`. Вместо FK выводятся читаемые коды/названия. Групповой CSV: `groupBy;key;name;count;knownDowntimeSeconds;excludedDowntimeCount`. Формат: UTF-8 BOM, `;`, CRLF, кавычки вокруг каждой ячейки, двойные кавычки экранируются. Формульный текст Excel защищён префиксом-апострофом. Фильтр один и тот же для списка, сводки и выгрузки; пагинация на CSV не распространяется.

## 5. Мониторинг

Общий фильтр M: `from/to` вместе или по умолчанию последние 24 часа, `regionId`, `btsId`, `cellId`, `search`, `status`, `type`, `severity`, `kpiCode`, `page/pageSize`. Списки значений фильтра могут передаваться через запятую. Период мониторинга ограничен 366 днями, начинается не раньше 1970 года и не заканчивается позже текущего UTC + 1 минута. Это ограничение вычислений, а не ограничение дат инцидентов.

Типы и статусы зависят от сущности: у элементов `active/degraded/down/maintenance/decommissioned`; у аномалий `new/acknowledged/explained/resolved/false_positive`; критичность аномалий `info/warning/critical`, она не совпадает с критичностью инцидента low/medium/high/critical.

| Метод | Вход | Что делает / зачем | Выход |
| --- | --- | --- | --- |
| `GET /api/monitoring/summary` | M | Карточки сети и качества данных без дублирования БС по числу секторов | Плоские avgCssr/avgDcr/traffic*Gb, btsTotal/cellTotal, openAnomalies/criticalAnomalies, networkStatusCounts/anomalyStatusCounts/severityCounts, coverage и kpis |
| `GET /api/monitoring/network-elements` | M, optional forMap | Реестр всех типов элементов для списка/будущей карты | Страница элементов; forMap=true — до 10000, флаг truncated |
| `GET /api/monitoring/network-elements/{id}` | id + M | Карточка элемента с секторами и связанными событиями | `{networkElement,kpis,sectors,sectorsTotal,sectorsTruncated,anomalies,anomaliesTotal,anomaliesTruncated,incidents,incidentsTotal,incidentsTruncated}` |
| `GET /api/monitoring/bts` | M | Только базовые станции | Страница, как network-elements с ограничением типа bts |
| `GET /api/monitoring/bts/{id}` | id + M | Карточка БС | Как карточка элемента; 404 для элемента другого типа |
| `GET /api/monitoring/cells` | M | Таблица радиосот с рассчитанными KPI | Страница сот с идентификаторами БС/региона/сектора, KPI и coverage |
| `GET /api/monitoring/cells/{id}` | id + M | Карточка радиосоты | Объект с cell, аномалиями и связанными инцидентами; KPI находятся внутри cell.kpis |
| `GET /api/monitoring/cells/{id}/kpi` | id + M | Часовой ряд для графика | `{cellId,items,total,page,pageSize,kpis,from,to}`; строки содержат tsHour и почасовые счётчики/производные KPI |
| `GET /api/monitoring/anomalies` | M | Фильтруемый список аномалий | Страница anomalyId, cellId, btsCode, sector, regionId, tsFrom/To, type/kpiCode/severity/status, observedValue/expectedValue/score, explainedByIncident |
| `GET /api/monitoring/anomalies/{id}` | id | Карточка аномалии и объясняющий её инцидент | `{anomaly,incidents}`; incidents — массив, пустой при отсутствии связи |
| `POST /api/monitoring/detect-anomalies` | `{hoursBack:1..8760}`, operator/admin | Валидирует и вызывает уже существующий пересчёт коллеги | `{found,processed,durationMs}` по контракту существующей процедуры/контроллера |
| `POST /api/monitoring/refresh-kpi` | `{from,to}`, operator/admin | Валидирует период и вызывает существующий расчёт KPI | `{processed,durationMs}` |

Сетевой элемент содержит ID/code/fullName/type/status, регион/ТЦ/направление, числа секторов/аномалий и их максимальную критичность. Точные `lat`, `lon`, `address` по умолчанию **null**, присутствует `coordinatesHidden=true`. Вопрос точности оставлен на обсуждение. Отдельный серверный параметр `Monitoring:ExposeCoordinates` существует, но в compose выставлен false; браузер этим параметром не управляет.

CSSR = 100 × сумма успешных соединений / сумма попыток. DCR = 100 × сумма обрывов / сумма успешных соединений. Это взвешенные отношения, а не среднее процентов. При нулевом знаменателе — null. Трафик в десятичных GB, деление на 10^9. Coverage показывает наблюдавшиеся/ожидаемые/отсутствующие часовые записи и долю покрытия; отсутствие строки не превращается в нормальный нулевой показатель. Пропущенные часы не дорисовываются как реальные измерения.

**Достоверного KPI доступности в новой схеме нет.** `avgAvailability`/`availabilityPct` возвращают null. `trafficActivityPct` — отдельный прокси активности трафика, с `availabilityBasis="traffic_activity_proxy"`. Он не доказывает работоспособность сети. В PHP это подписано раздельно. Числа найденных аномалий в командах пересчёта отражают семантику уже существующего AdminController; новая ветка не переписывает его алгоритм.

## 6. Совместимость URL и границы работы

Все маршруты `/api/incidents...` доступны также под `/api/accidents...`. Сохранены `/api/dashboard/summary`, `/api/bts[/{id}]`, `/api/cells[/{id}[/kpi]]`, `/api/anomalies[/{id}]`, `/api/regions`, `/api/reports/events`, `/api/reports/export`. Их JSON приведён к **новой схеме main**; это сохранение URL, а не обещание всех старых полей удалённых таблиц. `vendors` явно отвечает 410 по описанной причине.

Готовые методы main, оставленные без изменений: `POST /api/auth/login`, `GET /api/auth/me`, `POST /api/admin/refresh-kpi`, `/api/admin/detect-anomalies`, `/api/admin/detect-availability`, `/api/admin/recalculate-all`. Новые `GET /api/admin/audit`, `GET/POST /api/admin/users` из обсуждения не реализованы: это работа коллеги.

Нужно отдельно обсудить: в исходном AdminController стоит только `[Authorize]`, без ограничения роли admin. Мы не меняли этот файл по договорённости. Новые команды мониторинга дополнительно ограничены operator/admin. Внешний доступ к старым административным маршрутам должен оценить их автор.

Новая схема объявляет даты UTC. Старый Excel имел неизвестный часовой пояс; прежний импорт с raw-архивом из KOLYA не переносился в эту новую БД. Перед рабочей загрузкой исторических данных требуется подтверждение времени источника. Точные координаты и промышленное правило геосетки также ещё не согласованы. PHP-карта в этой ветке — честная заглушка; таблицы мониторинга и KPI работают с настоящим API.

## 7. Где искать код и проверять контракт

- `IncidentsController.cs`: запрос, ввод, выход, валидация, SQL, расчёты и CSV рядом.
- `ReportsController.cs`: собственные входные/выходные DTO отчётов рядом; общие запросы инцидентов переиспользуются.
- `MonitoringController.cs`: запросы сети, сот, KPI, аномалий, DTO в этом же файле.
- `ReferenceController.cs`: списки и health, общий синхронный обработчик исключений.
- `Models/`: модели БД, уже сгенерированные коллегой; они не являются дополнительными слоями DTO.
- `php-site/index.php`: контроллер и HTML всех вкладок в одном файле. `ApiClient.php`: только синхронный HTTP.

Swagger работающего API: `http://127.0.0.1:8080/swagger`. Машинный снимок списка методов — `openapi.json` рядом с этим файлом. Фактические проверки описаны в [TESTING.md](TESTING.md).
