# REST API: справочник текущего main

Документация описывает код `main` на коммите [`db7f93001de5e57b710cad7a03cb716e706a8a0d`](https://github.com/ostapenkor/MotivCellScopeApiServices/commit/db7f93001de5e57b710cad7a03cb716e706a8a0d), полученном 15 ноября 2026 года. Описаны **28 HTTP-операций в 8 контроллерах**. Комментарии старого обсуждения и реализации других веток не определяют этот контракт.

В каждом разделе указаны входные параметры, назначение, выходные поля, доступ и ответы при ошибках. Примеры JSON показывают структуру; ID и значения учебные. Это документация по исходникам, а не отчёт о запуске текущего main. Код API, SQL и Docker в ветке документации не изменялись.

## Быстрый переход

- [Авторизация, справочники, состояние API](api/auth-reference-health.md).
- [Аварии: поля, CRUD, список, подробный CSV, связанные аномалии](api/incidents.md).
- [Отчётность: сводка, доступные даты, краткий CSV](api/reports.md).
- [Мониторинг: KPI, сетевые элементы, соты, пересчёт](api/monitoring.md).
- [Аномалии: список, карточка, связь с аварией и статус](api/anomalies.md).
- [Администратор: аудит и пользователи](api/admin.md).
- [Запросы из JavaScript и PHP с готовыми примерами](CLIENTS-JS-PHP.md).
- [Текст слайдов, речь и сценарий демонстрации](PRESENTATION.md).

## Общие правила

Пример локального адреса — `http://localhost:8080`. Подставьте адрес фактически запущенного **main**. В main Swagger включён в окружении Development: `/swagger`. Файл проекта `ReportController.cs` содержит класс `ReportsController`, поэтому путь имеет вид `/api/reports/...`.

Для JSON-запроса передавайте `Accept: application/json`, при наличии тела — `Content-Type: application/json`. Ответы сериализуются в camelCase. В защищённом запросе нужен заголовок:

```http
Authorization: Bearer ВАШ_JWT
```

JWT выдаётся входом на 2 часа. Новая выдача токена требует повторного входа; серверных методов refresh/logout в этой версии нет. У чтения стоит `[Authorize]`: нужен действующий токен. У изменений и административных действий дополнительные ограничения указаны в таблице.

Даты рекомендуется передавать ISO 8601 с явно заданным UTC: `2026-11-15T08:00:00Z`. Признак `Z` предотвращает неоднозначность часового пояса у входящего DateTime. У разных методов различаются умолчания, обязательность и границы периода — смотрите их собственное описание. Отсутствующее окончание события передаётся как JSON `null`.

**Списки не имеют общего формата для всех методов.** Например, журнал возвращает `{total,items}` без пагинации, сетевые элементы — массив, а список аномалий — объект с пагинацией. Нельзя отправить универсальный набор фильтров и ожидать, что каждый метод их применит.

Ошибки также различаются: ручные проверки возвращают `message` либо `error`/`field`; ошибки привязки ASP.NET Core могут возвращать ProblemDetails с `title`, `status`, `errors`. 401/403 могут не содержать JSON. Клиент сначала проверяет HTTP-код. Успешный DELETE — 204 без тела; успешный POST аварии — 201 с карточкой.

## Полный список методов

«JWT» означает действительный токен без дополнительного ограничения роли в атрибуте чтения. «Правка» означает operator или admin. «Админ» означает только admin.

| № | Метод | URL | Доступ | Назначение / описание |
| --- | --- | --- | --- | --- |
| 1 | POST | `/api/auth/login` | Без JWT | [Вход и получение токена](api/auth-reference-health.md) |
| 2 | GET | `/api/auth/me` | JWT | [Текущий пользователь](api/auth-reference-health.md) |
| 3 | GET | `/api/health` | Без JWT | [Соединение API с БД](api/auth-reference-health.md) |
| 4 | GET | `/api/reference/all` | JWT | [Справочники формы](api/auth-reference-health.md) |
| 5 | GET | `/api/incidents` | JWT | [Журнал за период](api/incidents.md) |
| 6 | GET | `/api/incidents/{id}` | JWT | [Карточка аварии](api/incidents.md) |
| 7 | POST | `/api/incidents` | Правка | [Создание](api/incidents.md) |
| 8 | PUT | `/api/incidents/{id}` | Правка | [Замена данных](api/incidents.md) |
| 9 | DELETE | `/api/incidents/{id}` | Правка | [Удаление](api/incidents.md) |
| 10 | GET | `/api/incidents/export` | JWT | [Подробный CSV](api/incidents.md) |
| 11 | GET | `/api/incidents/{id}/anomalies` | JWT | [Связанные аномалии](api/incidents.md) |
| 12 | GET | `/api/reports/summary` | JWT | [Сводка аварий](api/reports.md) |
| 13 | GET | `/api/reports/available-periods` | JWT | [Минимальная/максимальная дата](api/reports.md) |
| 14 | GET | `/api/reports/export` | JWT | [Краткий CSV](api/reports.md) |
| 15 | GET | `/api/monitoring/summary` | JWT | [Сводка сети и KPI](api/monitoring.md) |
| 16 | GET | `/api/monitoring/network-elements` | JWT | [Реестр сетевых элементов](api/monitoring.md) |
| 17 | GET | `/api/monitoring/network-elements/{id}` | JWT | [Карточка элемента](api/monitoring.md) |
| 18 | GET | `/api/monitoring/cells` | JWT | [Список сот](api/monitoring.md) |
| 19 | GET | `/api/monitoring/cells/{id}/kpi` | JWT | [Почасовые KPI соты](api/monitoring.md) |
| 20 | POST | `/api/monitoring/detect-anomalies` | Правка | [Пересчёт аномалий](api/monitoring.md) |
| 21 | POST | `/api/monitoring/refresh-kpi` | Правка | [Пересчёт KPI](api/monitoring.md) |
| 22 | GET | `/api/anomalies` | JWT | [Список аномалий](api/anomalies.md) |
| 23 | GET | `/api/anomalies/{id}` | JWT | [Карточка аномалии](api/anomalies.md) |
| 24 | POST | `/api/anomalies/{id}/explain` | Правка | [Связь с аварией](api/anomalies.md) |
| 25 | PATCH | `/api/anomalies/{id}/status` | Правка | [Изменение статуса](api/anomalies.md) |
| 26 | GET | `/api/admin/audit` | Админ | [Журнал действий](api/admin.md) |
| 27 | GET | `/api/admin/users` | Админ | [Список пользователей](api/admin.md) |
| 28 | POST | `/api/admin/users` | Админ | [Создание пользователя](api/admin.md) |

## Соответствие вкладкам сайта

| Экран | Основные методы |
| --- | --- |
| Вход | auth/login, auth/me |
| Аварии | reference/all, incidents, incidents/{id}, incidents/export |
| Отчётность | reports/summary, reports/available-periods, reports/export; подробный CSV — incidents/export |
| Мониторинг | monitoring/summary, network-elements, cells, cells/{id}/kpi; аномалии — отдельная группа /api/anomalies |
| Администрирование | admin/audit, admin/users |

В main нет готового PHP/HTML-сайта. Этот документ показывает, какие методы использовать при подключении интерфейса. В `Program.cs` CORS не настроен: инструкцию для запросов из браузера с другого адреса смотрите в [CLIENTS-JS-PHP.md](CLIENTS-JS-PHP.md).

## Нюансы, которые влияют на интерфейс и защиту

- `PUT /api/incidents/{id}` принимает полную модель ввода. Механизма `expectedUpdatedAt` и защиты 409 от параллельной правки в этом main нет.
- Список аварий и экспорт отбирают по **началу**, обе границы включаются. Из старой незавершённой аварии не следует её попадание в любой более поздний период.
- В main `commonProstoy` при записи копирует длительность события; формула умножения простоя на число БС из PDF этим кодом не реализована. Подробности и входные интервалы — в разделе аварий.
- Подробный и краткий CSV отличаются набором колонок и реализацией кодирования. Названия и поведение приведены в отдельных разделах.
- Точные адреса и координаты мониторинга выдаются как в текущем коде. Старого флага скрытия координат в main нет. Модель геосетки должна решаться отдельно при подключении карты.
- KPI доступности в monitoring — расчётный показатель по ConnAttempt, а не измеренный SLA. При отсутствии данных есть значения по умолчанию; формулы описаны в разделе мониторинга.

Это описание существующей реализации. Замечания не изменяют её поведение. Для проверки конкретного стенда используйте его Swagger и данные; результаты старых проверок других веток не являются проверкой этого коммита.
