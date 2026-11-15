# Как обращаться к REST API из JavaScript и PHP

Источник контракта: `main`, коммит [`db7f930`](https://github.com/ostapenkor/MotivCellScopeApiServices/commit/db7f93001de5e57b710cad7a03cb716e706a8a0d), полученный 15 ноября 2026 года. Это примеры использования готового API коллеги. Они не добавляют серверные методы и не запускают запись в БД автоматически.

## 1. Из чего состоит запрос

Пример получения аварий:

```http
GET /api/incidents?from=2026-11-01T00%3A00%3A00Z&to=2026-11-15T23%3A59%3A59Z HTTP/1.1
Host: localhost:8080
Authorization: Bearer ВАШ_JWT
Accept: application/json
```

- **Адрес API** — например `http://localhost:8080`. Реальный адрес берите из запуска коллеги. В main порт 8080 опубликован через Docker Compose. Схема HTTP/HTTPS должна совпадать с настройкой сервера.
- **Метод**: GET читает, POST создаёт или выполняет действие, PUT заменяет данные записи, DELETE удаляет, PATCH меняет статус аномалии.
- **Query** после `?` задаёт фильтры. **Body** содержит JSON для POST/PUT/PATCH.
- **JWT** — строка, которую возвращает вход. Её передают в заголовке `Authorization`, а не в адресе запроса. Пароль сайта и пароль MariaDB — разные вещи.

`POST /api/auth/login` и `GET /api/health` доступны без JWT. Остальные методы требуют входа. Для правки аварий и аномалий нужны operator/admin; `/api/admin/*` доступен только admin. Срок JWT в main — 2 часа. Методов refresh-token и logout в API нет. Выход клиента удаляет его копию токена.

## 2. Где выполняется код и почему важен CORS

**JavaScript браузера** выполняет запрос с адреса открытой страницы. Адреса `http://localhost:5092`, `http://localhost:8080` и `http://127.0.0.1:8080` являются разными origin. Браузер требует разрешение CORS для обращения к другому origin. В текущем `Program.cs` нет настройки `AddCors/UseCors`.

Поэтому приведённый ниже JS можно выполнить в консоли Swagger, открытого **на том же адресе API**. Для отдельного сайта команда должна выбрать один из вариантов интеграции:

1. Браузер обращается к PHP-сайту, PHP вызывает C# API и возвращает HTML либо JSON. CORS между PHP и API не требуется.
2. Веб-сервер публикует сайт и API на одном origin через маршрутизацию.
3. Автор API явно разрешает нужный origin через CORS. В этой задаче мы конфигурацию API не меняем.

`mode: "no-cors"` не даёт читать защищённый JSON и не решает эту задачу. Если запрос делает серверный JavaScript (например Node.js), браузерное ограничение CORS к нему не применяется.

**PHP** выполняется на сервере. Для локального PHP адрес API обычно `http://localhost:8080`. В отдельном контейнере `localhost` указывает на сам контейнер PHP. В той же Docker-сети используйте имя существующего сервиса: `http://motivcellscopeapiservices:8080`. Alias `api` в нынешнем main не объявлен. Не подставляйте адрес API из произвольного пользовательского параметра.

## 3. JavaScript: вход, чтение, запись и CSV

Готовые функции: [examples/requests.js](examples/requests.js). Это обычный JS без библиотек. Скопируйте содержимое в консоль страницы Swagger вашего API. Сам файл только объявляет функции.

JS `fetch` работает через Promise; `async/await` делает последовательность запросов читаемой. Это свойство браузерного клиента, оно не меняет синхронную реализацию C#.

### Войти

```js
// Пароль спросим при выполнении, не записываем его в исходники.
try {
    const user = await login(prompt("Логин"), prompt("Пароль"));
    console.log(user.username, user.role); // JWT здесь не печатается.
} catch (error) {
    console.error(error.message);
}
```

Для пользовательского сайта вместо prompt нужна обычная форма с полем `type="password"`. В примере JWT хранится только в переменной памяти страницы. Обновление страницы его удаляет. Для схемы с PHP храните JWT на PHP-сервере в сессии, как показано ниже.

### Получить аварии, справочники и сводку

```js
const from = "2026-11-01T00:00:00Z";
const to = "2026-11-15T23:59:59Z";
const result = await incidents(from, to);
console.log(result.total);
console.table(result.items);

const dropdowns = await references();
console.table(dropdowns.priorities);
console.log(await reportSummary(from, to));
```

У **списка аварий** обе даты обязательны, обе границы включаются. Отбор идёт по `beginTime`. Он возвращает `{total,items}`, без page/pageSize. У **отчётов** даты необязательны. Не переносите правила пагинации и фильтров с одного метода на другой. Подробности — [справочник API](API.md).

### Создать и изменить учебную запись

Этот фрагмент выполняйте отдельно, когда нужна запись; он требует operator/admin.

```js
const input = {
    pw: "DEMO-001",
    servElem: "Учебный сетевой элемент",
    numbRbs: "1",
    descr: "Учебная запись",
    beginTime: new Date(Date.now() - 60 * 60 * 1000).toISOString(),
    endTime: null,
    severity: "low",
    category: "other"
};

const created = await createIncident(input);
console.log("Создана запись", created.incidentId);

// PUT отправляет полную модель ввода, а не только изменённое поле.
const changed = await updateIncident(created.incidentId, {
    ...input,
    descr: "Обновлённый комментарий"
});
console.log(changed.descr);
```

В рабочей форме заполняйте модель из **GET `/api/incidents/{id}`**, где есть ID справочников. В списке аварий вместо части ID приходят читаемые коды. Незаполненные nullable-поля PUT могут затереть прежние значения. `createdAt`, `createdByUserId`, `lenght`, `commonProstoy` не включайте в модель ввода. Полный перечень — [incidents.md](api/incidents.md).

Удаление выполняется отдельным явным вызовом для выбранной учебной записи:

```js
await deleteIncident(created.incidentId); // Успех: 204, тела JSON нет.
```

### Получить аномалии и изменить связь

```js
const data = await anomalies({ from, to, page: 1, pageSize: 50, status: "new" });
console.table(data.items);

// Только по реальным ID, выбранным пользователем, и с правом operator/admin:
// await explainAnomaly(anomalyId, incidentId);
// await updateAnomalyStatus(anomalyId, "acknowledged");
```

Текущий список аномалий находится в `/api/anomalies`, а не в `/api/monitoring/anomalies`.

### Скачать CSV

```js
await downloadIncidentsCsv(from, to);
```

Это GET с JWT, затем сохранение полученных байтов. Простая ссылка `<a href="/api/incidents/export">` не добавит заголовок Bearer. CSV нужно читать как файл/Blob, а не через `response.json()`.

## 4. PHP: синхронный cURL

Готовые функции: [examples/requests.php](examples/requests.php). Нужны PHP 8 и расширение cURL. Проверка окружения: `php -v`, `php -m`. Файл сам ничего не отправляет.

Минимальное использование из вашей серверной страницы после обработки формы входа:

```php
<?php
session_start();
require __DIR__ . '/examples/requests.php'; // Исправьте путь под свою папку.
$baseUrl = getenv('API_BASE_URL') ?: 'http://localhost:8080';

// Эти две строки используются в обработчике вашей формы входа.
$auth = apiLogin($baseUrl, $usernameFromForm, $passwordFromForm);
session_regenerate_id(true);
$_SESSION['api_token'] = $auth['token'];
$_SESSION['role'] = $auth['role'];
```

`$usernameFromForm` и `$passwordFromForm` — введённые пользователем значения, которые обрабатывает ваш контроллер. Это фрагмент интеграции, а не законченная страница входа. Cookie-сессия и CSRF-проверка форм остаются частью сайта. JWT не выводят в HTML или JavaScript.

### Получить таблицу и безопасно вывести значение

```php
$token = $_SESSION['api_token'] ?? null;
if ($token === null) {
    // Здесь ваш контроллер показывает вход или делает redirect.
    exit('Сначала войдите в систему');
}

try {
    $data = apiIncidents($baseUrl, $token,
        '2026-11-01T00:00:00Z', '2026-11-15T23:59:59Z');
    foreach ($data['items'] as $row) {
        echo '<p>' . htmlspecialchars($row['descr'] ?? '', ENT_QUOTES, 'UTF-8') . '</p>';
    }
} catch (Throwable $error) {
    if ($error->getCode() === 401) unset($_SESSION['api_token']);
    echo htmlspecialchars($error->getMessage(), ENT_QUOTES, 'UTF-8');
}
```

Все обращения выполняются последовательно: `curl_exec` ждёт ответ. Соединение ограничено 5 секундами, весь запрос — 30. Клиент не следует HTTP-редиректам: сразу задайте рабочий конечный URL, включая нужную схему HTTPS.

### Создать, заменить, удалить

```php
$input = [
    'pw' => 'DEMO-002',
    'servElem' => 'Учебный сетевой элемент',
    'numbRbs' => '1',
    'descr' => 'Учебная запись',
    'beginTime' => gmdate('Y-m-d\TH:i:s\Z', time() - 3600),
    'endTime' => null,
    'severity' => 'low',
    'category' => 'other'
];
$created = apiCreateIncident($baseUrl, $token, $input);
$input['descr'] = 'Обновлённый комментарий';
$updated = apiUpdateIncident($baseUrl, $token, $created['incidentId'], $input);

// Вызовите отдельно при необходимости удалить именно эту учебную запись:
// apiDeleteIncident($baseUrl, $token, $created['incidentId']);
```

### Любой другой JSON-метод

```php
$references = apiJson($baseUrl, 'GET', '/api/reference/all', $token);
$summary = apiJson($baseUrl, 'GET', '/api/reports/summary', $token, null, [
    'from' => '2026-11-01T00:00:00Z',
    'to' => '2026-11-15T23:59:59Z'
]);

// POST-связь и PATCH-статус используют тот же клиент:
// apiJson($baseUrl, 'POST', '/api/anomalies/' . $anomalyId . '/explain', $token,
//     ['incidentId' => $incidentId]);
// apiJson($baseUrl, 'PATCH', '/api/anomalies/' . $anomalyId . '/status', $token,
//     ['status' => 'acknowledged']);
```

### Передать CSV браузеру

В обработчике скачивания до любого HTML:

```php
$csv = apiIncidentsCsv($baseUrl, $token,
    '2026-11-01T00:00:00Z', '2026-11-15T23:59:59Z');
header('Content-Type: text/csv; charset=utf-8');
header('Content-Disposition: attachment; filename="incidents.csv"');
echo $csv;
exit;
```

Для краткого CSV отчётов используйте `apiRequest($baseUrl, 'GET', '/api/reports/export', $token, null, $query)['body']`. Это другой набор колонок. Клиент сохраняет байты ответа как есть. Не декодируйте CSV как JSON и не обещайте одинаковое наличие BOM у обеих выгрузок: различие указано в справочнике методов.

## 5. Частые ошибки

| Симптом | Что проверить |
| --- | --- |
| 400 | Тело JSON, типы полей, обязательные from/to у incidents, порядок дат, допустимые category/severity/status |
| 401 | Был ли вход, передан ли `Authorization: Bearer ...`, не истёк ли JWT |
| 403 | Нужна ли для этого действия роль operator/admin либо только admin |
| 404 | Существует ли ID и есть ли такой маршрут именно в main |
| Ошибка CORS в браузере | Совпадает ли origin; используйте PHP-сервер или согласованную настройку API |
| PHP не подключается к localhost в контейнере | Нужен адрес сервиса API в общей сети, а не самого PHP-контейнера |
| Ошибка TLS или редирект | Проверьте правильный HTTP/HTTPS URL и настройку сертификата у автора запуска |
| JSON parse error после DELETE | Успешный DELETE возвращает 204 с пустым телом |

В main нет единой формы ошибок для всех методов. Клиент проверяет HTTP-код, затем читает `message`, `error`, `title` или `errors` при их наличии. Неподхваченное исключение может дать 500; это не доказательство успешного сохранения и не причина автоматически повторять POST.

## Источники синтаксиса

- [MDN: использование Fetch API](https://developer.mozilla.org/en-US/docs/Web/API/Fetch_API/Using_Fetch) — Promise, HTTP-коды, JSON, CORS.
- [PHP: curl_setopt_array](https://www.php.net/manual/en/function.curl-setopt-array.php) — настройка HTTP-запроса.
- [PHP: json_encode](https://www.php.net/manual/en/function.json-encode.php) — JSON тела запроса.
- [Program.cs зафиксированного main](https://github.com/ostapenkor/MotivCellScopeApiServices/blob/db7f93001de5e57b710cad7a03cb716e706a8a0d/MotivCellScopeApiServices/Program.cs) — маршрутизация, JWT, отсутствие настройки CORS.
