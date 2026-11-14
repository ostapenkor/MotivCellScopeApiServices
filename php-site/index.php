<?php
declare(strict_types=1);

require_once __DIR__ . '/ApiClient.php';
date_default_timezone_set('UTC');
ini_set('session.use_strict_mode', '1');
ini_set('session.use_only_cookies', '1');
$secureCookie = getenv('APP_COOKIE_SECURE') === '1' || (!empty($_SERVER['HTTPS']) && $_SERVER['HTTPS'] !== 'off');
session_name('motiv_scope');
session_set_cookie_params(['lifetime' => 0, 'path' => '/', 'secure' => $secureCookie, 'httponly' => true, 'samesite' => 'Lax']);
session_start();
header('Content-Type: text/html; charset=utf-8');
header('Cache-Control: no-store');
header('X-Content-Type-Options: nosniff');
header('Referrer-Policy: same-origin');
header("Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'none'; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'");
$_SESSION['csrf'] ??= bin2hex(random_bytes(32));

function e(mixed $value): string { return htmlspecialchars(is_scalar($value) ? (string) $value : '', ENT_QUOTES | ENT_SUBSTITUTE, 'UTF-8'); }
function input(array $source, string $key, string $default = ''): string { return isset($source[$key]) && is_scalar($source[$key]) ? trim((string) $source[$key]) : $default; }
function href(string $tab, array $params = []): string { return 'index.php?' . http_build_query(['tab' => $tab] + $params, '', '&', PHP_QUERY_RFC3986); }
function redirect(string $tab, array $params = []): never { header('Location: ' . href($tab, $params), true, 303); exit; }
function csrf(): void { echo '<input type="hidden" name="csrf" value="' . e($_SESSION['csrf']) . '">'; }
function textValue(mixed $value, string $fallback = '—'): string { return $value !== null && $value !== '' ? (string) $value : $fallback; }
function numberValue(mixed $value, int $precision = 0): string { return is_numeric($value) ? number_format((float) $value, $precision, ',', ' ') : '—'; }
function dateValue(mixed $value, bool $time = true): string {
    if (!$value || !is_string($value)) { return '—'; }
    try { return (new DateTimeImmutable($value))->setTimezone(new DateTimeZone('UTC'))->format($time ? 'd.m.Y H:i' : 'd.m.Y'); }
    catch (Exception) { return '—'; }
}
function localDateValue(mixed $value): string {
    if (!$value || !is_string($value)) { return ''; }
    try { return (new DateTimeImmutable($value))->setTimezone(new DateTimeZone('UTC'))->format('Y-m-d\TH:i:s'); }
    catch (Exception) { return ''; }
}
function secondsValue(mixed $value): string {
    if (!is_numeric($value)) { return 'Нет данных'; }
    $seconds = max(0, (int) round((float) $value));
    return sprintf('%d:%02d:%02d', intdiv($seconds, 3600), intdiv($seconds % 3600, 60), $seconds % 60);
}
function selected(mixed $left, mixed $right): string { return (string) $left === (string) $right ? ' selected' : ''; }
function labelValue(string $value): string {
    return ['critical' => 'Критическая', 'high' => 'Высокая', 'medium' => 'Средняя', 'low' => 'Низкая', 'minor' => 'Незначительная',
        'warning' => 'Предупреждение', 'power' => 'Электропитание', 'transport' => 'Транспорт', 'equipment' => 'Оборудование',
        'network' => 'Сеть', 'hardware' => 'Оборудование', 'software' => 'ПО', 'client' => 'Обращение клиента', 'external' => 'Внешнее',
        'planned' => 'Плановые работы', 'other' => 'Прочее', 'open' => 'Открыт', 'closed' => 'Закрыт', 'info' => 'Информация',
        'new' => 'Новая', 'acknowledged' => 'Принята', 'explained' => 'Объяснена', 'resolved' => 'Устранена', 'false_positive' => 'Ложная',
        'threshold' => 'Порог', 'zscore' => 'Статистическая', 'missing_data' => 'Пропуск данных', 'logical' => 'Логическая', 'cross_source' => 'Расхождение источников',
        'bts' => 'Базовая станция', 'ip_node' => 'IP-узел', 'optical_node' => 'Оптический узел', 'dwdm_node' => 'DWDM-узел',
        'rrl_node' => 'РРЛ-узел', 'opts_node' => 'ОПТС', 'sss_node' => 'ССС', 'client_channel' => 'Клиентский канал', 'mgmn_node' => 'МГМН',
        'engineer' => 'Инженер', 'operator' => 'Оператор', 'admin' => 'Администратор'][$value] ?? $value;
}
function badge(mixed $value): string {
    $value = (string) ($value ?? '');
    $class = in_array($value, ['critical', 'high', 'open', 'new'], true) ? 'danger' : (in_array($value, ['closed', 'low', 'resolved'], true) ? 'success' : 'neutral');
    return '<span class="badge ' . $class . '">' . e($value === '' ? 'Не указано' : labelValue($value)) . '</span>';
}
function apiRead(ApiClient $api, string $path, array $query, array &$errors, string $label): ?array {
    try { return $api->request('GET', $path, $query); }
    catch (Throwable $error) { $errors[] = $label . ': ' . $error->getMessage(); return null; }
}
function items(?array $data): array {
    if ($data === null) { return []; }
    $rows = $data['items'] ?? $data;
    return is_array($rows) ? array_values(array_filter($rows, 'is_array')) : [];
}
function referenceRows(array $references, string $key): array {
    $rows = $references[$key] ?? [];
    return is_array($rows) ? $rows : [];
}
function referenceId(mixed $row, string $key): string {
    if (!is_array($row)) { return is_scalar($row) ? (string) $row : ''; }
    $idKey = ['regions' => 'regionId', 'directions' => 'directionId', 'technicalCenters' => 'techCenterId', 'priorities' => 'priorityId',
        'networkTypes' => 'networkTypeId', 'equipmentTypes' => 'equipmentTypeId', 'alarmTypes' => 'alarmTypeId'][$key] ?? 'id';
    return (string) ($row[$idKey] ?? $row['id'] ?? $row['value'] ?? $row['code'] ?? '');
}
function referenceName(mixed $row): string {
    if (!is_array($row)) { return labelValue((string) $row); }
    $name = $row['name'] ?? $row['title'] ?? $row['label'] ?? $row['fullName'] ?? $row['code'] ?? $row['value'] ?? '';
    $code = $row['code'] ?? '';
    return $code !== '' && $code !== $name ? $code . ' · ' . $name : (string) $name;
}
function selectField(string $name, string $label, array $rows, string $referenceKey, mixed $value, bool $required = false, string $empty = 'Не указано'): void {
    echo '<label class="field"><span>' . e($label) . ($required ? ' <b aria-hidden="true">*</b>' : '') . '</span><select name="' . e($name) . '"' . ($required ? ' required' : '') . '>';
    echo '<option value="">' . e($empty) . '</option>';
    $found = false;
    foreach ($rows as $row) { $id = referenceId($row, $referenceKey); if ((string) $value === $id) { $found = true; } echo '<option value="' . e($id) . '"' . selected($id, $value) . '>' . e(referenceName($row)) . '</option>'; }
    if (!$found && $value !== null && $value !== '') { echo '<option value="' . e($value) . '" selected>Текущее значение: ' . e($value) . '</option>'; }
    echo '</select></label>';
}
function groupTable(?array $rows, string $title, bool $bars = false): void {
    if ($rows === null) { echo '<section class="panel"><div class="panel-heading"><h2>' . e($title) . '</h2></div><div class="empty compact">Сводка недоступна. Проверьте ответ API выше.</div></section>'; return; }
    echo '<section class="panel"><div class="panel-heading"><h2>' . e($title) . '</h2><span class="muted">' . count($rows) . ' групп</span></div>';
    if (!$rows) { echo '<div class="empty compact">За выбранный период нет данных.</div></section>'; return; }
    $maximum = max(1, ...array_map(static fn(array $row): int => (int) ($row['count'] ?? 0), $rows));
    if ($bars) {
        echo '<div class="bar-list">';
        foreach ($rows as $row) {
            $count = (int) ($row['count'] ?? 0);
            $percent = max(0, min(100, (int) round($count / $maximum * 100)));
            echo '<div class="bar-row"><div><span>' . e(labelValue((string) ($row['name'] ?? $row['key'] ?? 'Не указано'))) . '</span><strong>' . numberValue($count) . '</strong></div><progress value="' . $percent . '" max="100" aria-label="' . e($row['name'] ?? 'Группа') . '"></progress></div>';
        }
        echo '</div>';
    } else {
        echo '<div class="table-wrap"><table><thead><tr><th>Группа</th><th class="numeric">Инциденты</th><th class="numeric">Известный простой</th><th class="numeric">Без расчёта</th></tr></thead><tbody>';
        foreach ($rows as $row) { echo '<tr><td>' . e(labelValue((string) ($row['name'] ?? $row['key'] ?? 'Не указано'))) . '</td><td class="numeric">' . numberValue($row['count'] ?? null) . '</td><td class="numeric mono">' . e(secondsValue($row['knownDowntimeSeconds'] ?? null)) . '</td><td class="numeric">' . numberValue($row['excludedDowntimeCount'] ?? null) . '</td></tr>'; }
        echo '</tbody></table></div>';
    }
    echo '</section>';
}

$tabs = ['dashboard' => 'Дашборд', 'incidents' => 'Журнал инцидентов', 'reports' => 'Отчёты', 'monitoring' => 'Мониторинг'];
$tab = input($_GET, 'tab', 'dashboard');
if (!isset($tabs[$tab])) { $tab = 'dashboard'; }
$errors = [];
$formErrors = [];
$notice = input($_SESSION, 'notice');
unset($_SESSION['notice']);
$action = input($_POST, 'action');
$method = $_SERVER['REQUEST_METHOD'] ?? 'GET';
$loginName = input($_POST, 'username');
$isPost = $method === 'POST';
$csrfValid = !$isPost || (isset($_POST['csrf']) && is_string($_POST['csrf']) && hash_equals($_SESSION['csrf'], $_POST['csrf']));
if (!$csrfValid) { http_response_code(403); $errors[] = 'Срок действия формы истёк. Проверьте данные и отправьте её снова.'; }

if ($isPost && $csrfValid && $action === 'logout') {
    $_SESSION = [];
    session_regenerate_id(true);
    $_SESSION['csrf'] = bin2hex(random_bytes(32));
    $_SESSION['notice'] = 'Вы вышли из системы.';
    redirect('dashboard');
}
$baseUrl = getenv('API_BASE_URL') ?: 'http://api:8080';
try { $api = new ApiClient($baseUrl, $_SESSION['token'] ?? null); }
catch (Throwable $error) { $errors[] = $error->getMessage(); $api = null; }

if ($isPost && $csrfValid && $action === 'login' && $api !== null) {
    if ($loginName === '' || input($_POST, 'password') === '') { $errors[] = 'Введите логин и пароль.'; }
    else {
        try {
            $login = $api->request('POST', '/api/auth/login', [], ['username' => $loginName, 'password' => (string) $_POST['password']]);
            if (empty($login['token']) || !is_string($login['token'])) { throw new RuntimeException('API не вернул токен авторизации.'); }
            session_regenerate_id(true);
            $_SESSION['token'] = $login['token'];
            $_SESSION['user'] = ['username' => (string) ($login['username'] ?? $loginName), 'role' => (string) ($login['role'] ?? '')];
            $_SESSION['csrf'] = bin2hex(random_bytes(32));
            redirect('dashboard');
        } catch (Throwable $error) { $errors[] = $error->getMessage(); }
    }
}

$authenticated = !empty($_SESSION['token']);
$user = $_SESSION['user'] ?? [];
$verifiedUser = false;
if ($authenticated && $api !== null) {
    try {
        $user = $api->request('GET', '/api/auth/me');
        $_SESSION['user'] = $user;
        $verifiedUser = true;
    } catch (ApiException $error) {
        if ($error->status === 401) {
            unset($_SESSION['token'], $_SESSION['user']);
            session_regenerate_id(true);
            $_SESSION['csrf'] = bin2hex(random_bytes(32));
            $authenticated = false;
        }
        $errors[] = $error->getMessage();
    } catch (Throwable $error) { $errors[] = $error->getMessage(); }
}
// Роль управляет только интерфейсом. Доступ к данным проверяет существующий API.
$canWrite = $verifiedUser && in_array($user['role'] ?? '', ['operator', 'admin'], true);
$references = [];
$periods = null;
$summary = null;
$monitoring = null;
$incidents = null;
$anomalies = null;
$networkElements = [];
$networkElementsTotal = null;
$detail = null;
$editId = input($_GET, 'edit');
$detailId = input($_GET, 'id');
$showEditor = $tab === 'incidents' && (input($_GET, 'new') === '1' || preg_match('/^[1-9][0-9]*$/D', $editId));
$form = [];
$filterNames = ['from', 'to', 'regionId', 'directionId', 'techCenterId', 'priorityId', 'networkTypeId', 'equipmentTypeId', 'alarmTypeId', 'category', 'severity', 'status', 'search', 'sort', 'desc', 'size'];
$filterValues = [];
foreach ($filterNames as $key) { $filterValues[$key] = input($_GET, $key); }
$query = [];
$filtersValid = true;
foreach (['from', 'to'] as $key) {
    if ($filterValues[$key] === '') { continue; }
    $date = DateTimeImmutable::createFromFormat('!Y-m-d', $filterValues[$key]);
    if (!$date || $date->format('Y-m-d') !== $filterValues[$key]) { $errors[] = 'Укажите корректную дату периода.'; $filtersValid = false; }
    else { $query[$key] = ($key === 'to' ? $date->modify('+1 day') : $date)->format('Y-m-d\TH:i:s\Z'); }
}
if ($filterValues['from'] !== '' && $filterValues['to'] !== '' && $filterValues['from'] > $filterValues['to']) { $errors[] = 'Начало периода должно быть не позже конца.'; $filtersValid = false; }
foreach (['regionId', 'directionId', 'techCenterId', 'priorityId', 'networkTypeId', 'equipmentTypeId', 'alarmTypeId'] as $key) {
    if ($filterValues[$key] !== '') {
        if (preg_match('/^[1-9][0-9]{0,9}$/D', $filterValues[$key])) { $query[$key] = $filterValues[$key]; }
        else { $errors[] = 'Некорректное значение справочника: ' . $key . '.'; $filtersValid = false; }
    }
}
foreach (['category', 'severity', 'status', 'search'] as $key) { if ($filterValues[$key] !== '') { $query[$key] = substr($filterValues[$key], 0, 200); } }
$page = max(1, min(1000000, (int) input($_GET, 'page', '1')));
$size = in_array((int) $filterValues['size'], [10, 25, 50, 100], true) ? (int) $filterValues['size'] : 25;
$sort = in_array($filterValues['sort'], ['begin_time', 'severity', 'pw'], true) ? $filterValues['sort'] : 'begin_time';
$desc = $filterValues['desc'] !== 'false';
$linkFilters = array_filter($filterValues, static fn(string $value): bool => $value !== '');
$groupOptions = ['category' => 'Категория', 'severity' => 'Критичность', 'direction' => 'Направление', 'techCenter' => 'Технический центр', 'alarmType' => 'Тип аварии'];
$groupBy = input($_GET, 'groupBy', 'category');
if (!isset($groupOptions[$groupBy])) { $groupBy = 'category'; }

if ($authenticated && $api !== null) {
    $references = apiRead($api, '/api/reference/all', [], $errors, 'Справочники') ?? [];
    $periods = apiRead($api, '/api/reports/available-periods', [], $errors, 'Доступный период');

    // Начальная выборка охватывает реальный период БД, включая исторические данные.
    if ($filtersValid) {
        if ($filterValues['from'] === '') { $filterValues['from'] = !empty($periods['from']) ? substr((string) $periods['from'], 0, 10) : gmdate('Y-m-d', strtotime('-29 days')); }
        if ($filterValues['to'] === '') { $filterValues['to'] = !empty($periods['to']) ? substr((string) $periods['to'], 0, 10) : gmdate('Y-m-d'); }
        $fromDate = DateTimeImmutable::createFromFormat('!Y-m-d', $filterValues['from']);
        $toDate = DateTimeImmutable::createFromFormat('!Y-m-d', $filterValues['to']);
        if (!$fromDate || !$toDate || $fromDate > $toDate) { $filtersValid = false; $errors[] = 'Начало периода должно быть не позже конца.'; }
        else { $query['from'] = $fromDate->format('Y-m-d\TH:i:s\Z'); $query['to'] = $toDate->modify('+1 day')->format('Y-m-d\TH:i:s\Z'); }
        $linkFilters = array_filter($filterValues, static fn(string $value): bool => $value !== '');
    }

    if ($isPost && $csrfValid && in_array($action, ['save', 'delete'], true)) {
        $tab = 'incidents';
        $id = input($_POST, 'incidentId');
        if (!$canWrite) { $formErrors[] = 'Изменение инцидентов доступно оператору и администратору при подтверждённой API-сессии.'; }
        if ($id !== '' && !preg_match('/^[1-9][0-9]*$/D', $id)) { $formErrors[] = 'Некорректный номер инцидента.'; }
        if ($action === 'delete' && $id === '') { $formErrors[] = 'Не указан инцидент для удаления.'; }
        if ($action === 'delete' && !$formErrors) {
            try { $api->request('DELETE', '/api/incidents/' . $id); $_SESSION['notice'] = 'Инцидент №' . $id . ' удалён.'; redirect('incidents', $linkFilters); }
            catch (Throwable $error) { $formErrors[] = $error->getMessage(); }
        }
        if ($action === 'save') {
            $showEditor = true;
            $editId = $id;
            $stringFields = ['pw', 'servElem', 'numbRbs', 'descr', 'alarmWho', 'descNotice', 'adminDistrict', 'severity', 'category', 'akb', 'dgu', 'prostoy'];
            $payload = [];
            foreach ($stringFields as $key) { $form[$key] = input($_POST, $key); $payload[$key] = $form[$key] === '' ? null : $form[$key]; }
            foreach (['directionId', 'techCenterId', 'priorityId', 'networkTypeId', 'equipmentTypeId', 'alarmTypeId', 'regionId', 'networkElementId'] as $key) {
                $form[$key] = input($_POST, $key);
                if ($form[$key] !== '' && !preg_match('/^[1-9][0-9]{0,9}$/D', $form[$key])) { $formErrors[] = 'Некорректное значение поля ' . $key . '.'; }
                $payload[$key] = $form[$key] === '' ? null : (int) $form[$key];
            }
            foreach (['beginTime', 'endTime'] as $key) {
                $form[$key] = input($_POST, $key);
                $timestamp = strlen($form[$key]) === 16 ? $form[$key] . ':00' : $form[$key];
                $date = $timestamp === '' ? false : DateTimeImmutable::createFromFormat('!Y-m-d\TH:i:s', $timestamp);
                if ($form[$key] === '' && $key === 'endTime') { $payload[$key] = null; }
                elseif (!$date || $date->format('Y-m-d\TH:i:s') !== $timestamp) { $formErrors[] = 'Укажите корректное время ' . ($key === 'beginTime' ? 'начала' : 'окончания') . '.'; }
                else { $payload[$key] = $date->format('Y-m-d\TH:i:s\Z'); }
            }
            if (!empty($payload['beginTime']) && !empty($payload['endTime']) && $payload['endTime'] < $payload['beginTime']) { $formErrors[] = 'Окончание не может быть раньше начала.'; }
            foreach (['akb' => 'АКБ', 'dgu' => 'ДГУ', 'prostoy' => 'Простой'] as $key => $label) {
                if ($form[$key] !== '' && (!preg_match('/^[0-9]{1,3}:[0-5][0-9]:[0-5][0-9]$/D', $form[$key]) || (int) explode(':', $form[$key])[0] > 838)) { $formErrors[] = $label . ': используйте ЧЧ:ММ:СС от 0:00:00 до 838:59:59.'; }
            }
            if ($form['numbRbs'] !== '' && $form['numbRbs'] !== 'С' && (!ctype_digit($form['numbRbs']) || (int) $form['numbRbs'] < 1 || (int) $form['numbRbs'] > 10000)) { $formErrors[] = 'Количество РБС: целое число от 1 до 10 000, «С» или пустое значение.'; }
            foreach (['category' => 'categories', 'severity' => 'severities'] as $key => $ref) {
                if ($form[$key] === '') { $formErrors[] = ($key === 'category' ? 'Категория' : 'Критичность') . ' обязательна.'; }
                $allowed = array_map(static fn($row): string => referenceId($row, $ref), referenceRows($references, $ref));
                if ($form[$key] !== '' && $allowed && !in_array($form[$key], $allowed, true)) { $formErrors[] = 'Выберите значение ' . $key . ' из справочника.'; }
            }
            $form['updatedAt'] = input($_POST, 'expectedUpdatedAt');
            if ($id !== '' && $form['updatedAt'] !== '') { $payload['expectedUpdatedAt'] = $form['updatedAt']; }
            if (!$formErrors) {
                try {
                    $saved = $api->request($id === '' ? 'POST' : 'PUT', '/api/incidents' . ($id === '' ? '' : '/' . $id), [], $payload);
                    $_SESSION['notice'] = $id === '' ? 'Инцидент создан.' : 'Изменения сохранены.';
                    $savedId = $saved['incidentId'] ?? $id;
                    redirect('incidents', $linkFilters + ($savedId !== '' ? ['id' => $savedId] : []));
                } catch (Throwable $error) { $formErrors[] = $error->getMessage(); }
            }
        }
    }

    if ($filtersValid && input($_GET, 'download') !== '') {
        $download = input($_GET, 'download');
        if (in_array($download, ['details', 'grouped'], true)) {
            try {
                $csv = $api->request('GET', $download === 'details' ? '/api/incidents/export' : '/api/reports/incidents-export', $query + ($download === 'grouped' ? ['groupBy' => $groupBy] : []), null, true);
                header('Content-Type: text/csv; charset=utf-8');
                header('Content-Disposition: attachment; filename="incidents-' . ($download === 'details' ? 'details' : $groupBy) . '-' . gmdate('Y-m-d') . '.csv"');
                echo $csv['body'];
                exit;
            } catch (Throwable $error) { $errors[] = 'Выгрузка: ' . $error->getMessage(); }
        }
    }
    if ($filtersValid && in_array($tab, ['dashboard', 'reports'], true)) { $summary = apiRead($api, '/api/reports/incidents-summary', $query, $errors, 'Сводка инцидентов'); }
    if ($tab === 'dashboard' || $tab === 'monitoring') { $monitoring = apiRead($api, '/api/monitoring/summary', [], $errors, 'Мониторинг'); }
    if ($tab === 'incidents' && $filtersValid) {
        $incidents = apiRead($api, '/api/incidents', $query + ['page' => $page, 'pageSize' => $size, 'sort' => $sort, 'order' => $desc ? 'desc' : 'asc'], $errors, 'Журнал инцидентов');
        if ($showEditor && $canWrite) {
            $networkElements = items(apiRead($api, '/api/monitoring/network-elements', ['forMap' => 'true'], $errors, 'Сетевые элементы'));
            if ($editId !== '' && !$form && preg_match('/^[1-9][0-9]*$/D', $editId)) {
                $form = apiRead($api, '/api/incidents/' . $editId, [], $errors, 'Редактирование инцидента') ?? [];
                foreach (['beginTime', 'endTime'] as $key) { $form[$key] = localDateValue($form[$key] ?? null); }
            }
        }
        if ($detailId !== '' && preg_match('/^[1-9][0-9]*$/D', $detailId)) { $detail = apiRead($api, '/api/incidents/' . $detailId, [], $errors, 'Карточка инцидента'); }
    }
    if ($tab === 'monitoring') {
        $anomalies = apiRead($api, '/api/monitoring/anomalies', ['page' => $page, 'pageSize' => $size], $errors, 'Аномалии');
        $networkList = apiRead($api, '/api/monitoring/network-elements', ['pageSize' => 200], $errors, 'Сетевые элементы');
        $networkElements = items($networkList);
        $networkElementsTotal = $networkList['total'] ?? null;
    }
}
$periodLabel = $filterValues['from'] !== '' || $filterValues['to'] !== ''
    ? ($filterValues['from'] !== '' ? dateValue($filterValues['from'], false) : 'Начало данных') . ' — ' . ($filterValues['to'] !== '' ? dateValue($filterValues['to'], false) : 'Сегодня')
    : 'Весь доступный период';
$role = (string) ($user['role'] ?? '');
$userName = (string) ($user['username'] ?? 'Пользователь');
?>
<!doctype html>
<html lang="ru">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <meta name="color-scheme" content="light">
    <title><?= e($authenticated ? $tabs[$tab] : 'Вход') ?> · МОТИВ / Сеть</title>
    <link rel="stylesheet" href="assets/style.css">
    <script src="assets/ui.js" defer></script>
</head>
<body class="<?= $authenticated ? 'app' : 'login-page' ?>">
<a class="skip-link" href="#main">К содержимому</a>
<?php if (!$authenticated): ?>
<main id="main" class="login-layout">
    <section class="login-brand" aria-label="МОТИВ Сеть">
        <a class="brand" href="index.php"><span class="brand-name">МОТИВ<span class="brand-dot">●</span></span><span class="brand-sub">Центр управления сетью</span></a>
        <div class="login-copy"><span class="eyebrow">Единый рабочий контур</span><h1>Вся сеть.<br>В одном фокусе.</h1><p>Инциденты, отчётность и мониторинг инфраструктуры МОТИВ.</p><div class="login-line" aria-hidden="true"><i></i><i></i><i></i><i></i><i></i><i></i></div></div>
        <span class="login-caption">Данные поступают из MotivCellScope API</span>
    </section>
    <section class="login-form-wrap">
        <div class="login-box"><span class="eyebrow">Рабочее пространство</span><h2>Войти в систему</h2><p class="muted">Используйте учётную запись, выданную администратором.</p>
        <?php if ($notice !== ''): ?><div class="alert success" role="status"><?= e($notice) ?></div><?php endif; ?>
        <?php foreach ($errors as $error): ?><div class="alert error" role="alert"><?= e($error) ?></div><?php endforeach; ?>
        <form method="post" action="index.php" class="login-form" data-busy="Вход…">
            <?php csrf(); ?><input type="hidden" name="action" value="login">
            <label class="field"><span>Логин</span><input name="username" autocomplete="username" value="<?= e($loginName) ?>" required maxlength="128" autofocus></label>
            <label class="field"><span>Пароль</span><input type="password" name="password" autocomplete="current-password" required maxlength="1024"></label>
            <button class="button primary large" type="submit">Войти <span aria-hidden="true">→</span></button>
        </form><p class="login-help">Доступ определяется вашей ролью в API.<br>За восстановлением доступа обратитесь к администратору.</p></div>
        <span class="login-footer">МОТИВ · Инфраструктура под контролем</span>
    </section>
</main>
<?php else: ?>
<header class="topbar">
    <a class="brand" href="<?= e(href('dashboard')) ?>" aria-label="МОТИВ — дашборд"><span class="brand-name">МОТИВ<span class="brand-dot">●</span></span><span class="brand-sub">Центр управления сетью</span></a>
    <div class="user-tools"><span class="live-indicator"><i aria-hidden="true"></i> Серверный режим</span><div class="user-avatar" aria-hidden="true"><?= e(function_exists('mb_substr') ? mb_substr($userName, 0, 1) : strtoupper(substr($userName, 0, 1))) ?></div><div class="user-label"><strong><?= e($userName) ?></strong><span><?= e(labelValue($role)) ?></span></div><form method="post" action="index.php"><?php csrf(); ?><input type="hidden" name="action" value="logout"><button class="button ghost small" type="submit">Выйти</button></form></div>
</header>
<nav class="navigation" aria-label="Основные разделы"><div class="nav-inner">
<?php $navIcons = ['dashboard' => '◫', 'incidents' => '☷', 'reports' => '▤', 'monitoring' => '◎']; foreach ($tabs as $key => $label): ?>
    <a class="nav-link <?= $key === $tab ? 'active' : '' ?>" href="<?= e(href($key)) ?>"<?= $key === $tab ? ' aria-current="page"' : '' ?>><span aria-hidden="true"><?= $navIcons[$key] ?></span><?= e($label) ?></a>
<?php endforeach; ?>
    <span class="nav-context">MotivCellScope <span>/</span> <?= e(gmdate('d.m.Y')) ?></span>
</div></nav>
<main id="main" class="workspace">
    <div class="page-heading"><div><div class="breadcrumb">МОТИВ / Сеть <span>/ <?= e($tabs[$tab]) ?></span></div><h1><?= e($tabs[$tab]) ?></h1><p><?= e(['dashboard' => 'Ключевые показатели и состояние инцидентов', 'incidents' => 'Единый журнал событий и восстановительных работ', 'reports' => 'Сводки по инцидентам и выгрузка исходных данных', 'monitoring' => 'Показатели сети и зарегистрированные аномалии'][$tab]) ?></p></div><div class="page-actions"><?php if ($tab === 'incidents' && $canWrite): ?><a class="button primary" href="<?= e(href('incidents', $linkFilters + ['new' => '1'])) ?>"><span aria-hidden="true">＋</span> Новый инцидент</a><?php else: ?><a class="button secondary" href="<?= e(href($tab, $linkFilters + ['groupBy' => $groupBy])) ?>"><span aria-hidden="true">↻</span> Обновить данные</a><?php endif; ?></div></div>
    <?php if ($notice !== ''): ?><div class="alert success" role="status"><strong>Готово.</strong> <?= e($notice) ?></div><?php endif; ?>
    <?php if ($errors): ?><div class="alert error" role="alert"><strong>Не удалось загрузить часть данных</strong><ul><?php foreach (array_unique($errors) as $error): ?><li><?= e($error) ?></li><?php endforeach; ?></ul></div><?php endif; ?>
    <?php if ($formErrors): ?><div class="alert error" role="alert" id="form-errors" tabindex="-1"><strong>Проверьте форму</strong><ul><?php foreach ($formErrors as $error): ?><li><?= e($error) ?></li><?php endforeach; ?></ul></div><?php endif; ?>

<?php if (in_array($tab, ['dashboard', 'incidents', 'reports'], true)): ?>
    <section class="panel filter-panel" aria-label="Фильтры">
        <form method="get" action="index.php" data-busy="Применение…">
            <input type="hidden" name="tab" value="<?= e($tab) ?>">
            <div class="filter-main"><div class="filter-caption"><span class="filter-icon" aria-hidden="true">≡</span><div><strong>Параметры выборки</strong><span>Даты и время — UTC</span></div></div>
                <label class="field"><span>Период с</span><input type="date" name="from" value="<?= e($filterValues['from']) ?>"></label>
                <label class="field"><span>По включительно</span><input type="date" name="to" value="<?= e($filterValues['to']) ?>"></label>
                <?php selectField('directionId', 'Направление', referenceRows($references, 'directions'), 'directions', $filterValues['directionId'], false, 'Все направления'); ?>
                <?php selectField('severity', 'Критичность', referenceRows($references, 'severities'), 'severities', $filterValues['severity'], false, 'Все уровни'); ?>
                <button class="button primary" type="submit">Применить</button><a class="reset-link" href="<?= e(href($tab)) ?>">Сбросить</a>
            </div>
            <details class="advanced-filters"<?= count(array_filter(array_intersect_key($filterValues, array_flip(['regionId', 'techCenterId', 'priorityId', 'networkTypeId', 'equipmentTypeId', 'alarmTypeId', 'category', 'status', 'search'])))) ? ' open' : '' ?>>
                <summary>Дополнительные фильтры <span aria-hidden="true">⌄</span></summary><div class="form-grid filters-grid">
                    <?php foreach (['regionId' => ['Регион', 'regions'], 'techCenterId' => ['Технический центр', 'technicalCenters'], 'priorityId' => ['Приоритет', 'priorities'], 'networkTypeId' => ['Тип сети', 'networkTypes'], 'equipmentTypeId' => ['Оборудование', 'equipmentTypes'], 'alarmTypeId' => ['Тип аварии', 'alarmTypes'], 'category' => ['Категория', 'categories']] as $name => [$label, $ref]): selectField($name, $label, referenceRows($references, $ref), $ref, $filterValues[$name], false, 'Все'); endforeach; ?>
                    <label class="field"><span>Состояние</span><select name="status"><option value="">Все</option><option value="open"<?= selected('open', $filterValues['status']) ?>>Открытые</option><option value="closed"<?= selected('closed', $filterValues['status']) ?>>Закрытые</option></select></label>
                    <label class="field span-2"><span>Поиск</span><input name="search" value="<?= e($filterValues['search']) ?>" placeholder="Описание, элемент сети, примечание…" maxlength="200" type="search"></label>
                    <?php if ($tab === 'incidents'): ?><label class="field"><span>Сортировка</span><select name="sort"><?php foreach (['begin_time' => 'По началу', 'severity' => 'По критичности', 'pw' => 'По ПВ / PW'] as $key => $label): ?><option value="<?= e($key) ?>"<?= selected($key, $sort) ?>><?= e($label) ?></option><?php endforeach; ?></select></label><label class="field"><span>Порядок</span><select name="desc"><option value="true"<?= selected('true', $desc ? 'true' : 'false') ?>>По убыванию</option><option value="false"<?= selected('false', $desc ? 'true' : 'false') ?>>По возрастанию</option></select></label><label class="field"><span>Строк на странице</span><select name="size"><?php foreach ([10, 25, 50, 100] as $option): ?><option value="<?= $option ?>"<?= selected($option, $size) ?>><?= $option ?></option><?php endforeach; ?></select></label><?php endif; ?>
                    <?php if ($tab === 'reports'): ?><label class="field"><span>Группировка отчёта</span><select name="groupBy"><?php foreach ($groupOptions as $key => $label): ?><option value="<?= e($key) ?>"<?= selected($key, $groupBy) ?>><?= e($label) ?></option><?php endforeach; ?></select></label><?php endif; ?>
                </div>
            </details>
        </form>
        <div class="filter-foot"><span class="period-chip"><i aria-hidden="true"></i><?= e($periodLabel) ?></span><?php if ($periods && !empty($periods['from'])): ?><span>В базе: <?= e(dateValue($periods['from'], false)) ?> — <?= e(dateValue($periods['to'] ?? null, false)) ?></span><?php else: ?><span>Период определяется данными API</span><?php endif; ?></div>
    </section>
<?php endif; ?>

<?php switch ($tab): case 'dashboard': ?>
    <div class="kpi-grid">
        <article class="kpi-card accent-red"><div class="kpi-top"><span>Инциденты</span><span class="kpi-icon" aria-hidden="true">☷</span></div><strong class="kpi-value"><?= numberValue($summary['total'] ?? null) ?></strong><p>За выбранный период</p></article>
        <article class="kpi-card accent-orange"><div class="kpi-top"><span>Известный простой</span><span class="kpi-icon" aria-hidden="true">◷</span></div><strong class="kpi-value time-value"><?= e(secondsValue($summary['knownDowntimeSeconds'] ?? null)) ?></strong><p>Часы : минуты : секунды</p></article>
        <article class="kpi-card accent-purple"><div class="kpi-top"><span>Не включены в расчёт</span><span class="kpi-icon" aria-hidden="true">◇</span></div><strong class="kpi-value"><?= numberValue($summary['excludedDowntimeCount'] ?? null) ?></strong><p>Нет исходных данных простоя</p></article>
        <article class="kpi-card accent-green"><div class="kpi-top"><span>Доступность сети</span><span class="kpi-icon" aria-hidden="true">⌁</span></div><strong class="kpi-value <?= !is_numeric($monitoring['avgAvailability'] ?? null) ? 'text-value' : '' ?>"><?= is_numeric($monitoring['avgAvailability'] ?? null) ? numberValue($monitoring['avgAvailability'], 2) . '%' : 'Нет исходных данных' ?></strong><p>Измеренная доступность в API</p></article>
    </div>
    <div class="two-columns dashboard-columns">
        <?php groupTable($summary['byDirection'] ?? null, 'Инциденты по направлениям', true); ?>
        <?php groupTable($summary['bySeverity'] ?? null, 'Распределение по критичности'); ?>
    </div>
    <section class="panel summary-strip"><div><span class="eyebrow">Мониторинг сети</span><h2>Показатели текущего среза</h2><p class="muted">Показатели мониторинга не зависят от фильтров журнала инцидентов.</p></div><dl><div><dt>CSSR</dt><dd><?= numberValue($monitoring['avgCssr'] ?? null, 2) ?><?= is_numeric($monitoring['avgCssr'] ?? null) ? '%' : '' ?></dd></div><div><dt>DCR</dt><dd><?= numberValue($monitoring['avgDcr'] ?? null, 2) ?><?= is_numeric($monitoring['avgDcr'] ?? null) ? '%' : '' ?></dd></div><div><dt>Трафик</dt><dd><?= numberValue($monitoring['trafficTotalGb'] ?? null, 2) ?> <small>ГБ</small></dd></div><div><dt>Базовые станции</dt><dd><?= numberValue($monitoring['btsTotal'] ?? null) ?></dd></div></dl><a class="button secondary" href="<?= e(href('monitoring')) ?>">Перейти в мониторинг <span aria-hidden="true">→</span></a></section>
    <div class="two-columns"><?php groupTable($summary['byCategory'] ?? null, 'Категории инцидентов'); ?><section class="panel note-panel"><span class="note-symbol" aria-hidden="true">i</span><h2>Как читать данные</h2><p>Сводка включает только записи, соответствующие выбранным фильтрам. Для полного списка откройте журнал.</p><p>Простой с неизвестным значением и записи с количеством РБС «С» не участвуют в расчёте общего простоя. Число таких записей указано отдельно.</p><a class="text-link" href="<?= e(href('incidents', $linkFilters)) ?>">Открыть выборку в журнале <span aria-hidden="true">→</span></a></section></div>
<?php break; case 'incidents': ?>
    <?php if ($showEditor && $canWrite): ?>
    <section class="panel editor-panel" id="incident-editor"><div class="panel-heading"><div><span class="eyebrow"><?= $editId !== '' ? 'Редактирование' : 'Новая запись' ?></span><h2><?= $editId !== '' ? 'Инцидент №' . e($editId) : 'Зарегистрировать инцидент' ?></h2></div><a class="button ghost small" href="<?= e(href('incidents', $linkFilters)) ?>">Закрыть</a></div>
        <form method="post" action="<?= e(href('incidents', $linkFilters)) ?>" data-busy="Сохранение…" class="incident-form">
            <?php csrf(); ?><input type="hidden" name="action" value="save"><input type="hidden" name="incidentId" value="<?= e($editId) ?>"><input type="hidden" name="expectedUpdatedAt" value="<?= e($form['updatedAt'] ?? '') ?>">
            <fieldset><legend><span>01</span> Классификация и привязка</legend><div class="form-grid">
                <?php foreach (['directionId' => ['Направление', 'directions'], 'techCenterId' => ['Технический центр', 'technicalCenters'], 'priorityId' => ['Приоритет', 'priorities'], 'networkTypeId' => ['Тип сети', 'networkTypes'], 'equipmentTypeId' => ['Тип оборудования', 'equipmentTypes'], 'alarmTypeId' => ['Тип аварии', 'alarmTypes'], 'regionId' => ['Регион', 'regions'], 'severity' => ['Критичность', 'severities'], 'category' => ['Категория', 'categories']] as $name => [$label, $ref]): selectField($name, $label, referenceRows($references, $ref), $ref, $form[$name] ?? '', in_array($name, ['severity', 'category'], true)); endforeach; ?>
                <label class="field span-2"><span>Сетевой элемент</span><select name="networkElementId"><option value="">Не указан</option><?php $hasCurrentElement = false; foreach ($networkElements as $element): $elementId = $element['networkElementId'] ?? $element['id'] ?? ''; if ((string) $elementId === (string) ($form['networkElementId'] ?? '')) { $hasCurrentElement = true; } ?><option value="<?= e($elementId) ?>"<?= selected($elementId, $form['networkElementId'] ?? '') ?>><?= e($element['fullName'] ?? $element['name'] ?? $elementId) ?></option><?php endforeach; if (!$hasCurrentElement && !empty($form['networkElementId'])): ?><option value="<?= e($form['networkElementId']) ?>" selected><?= e($form['networkElementName'] ?? 'Элемент №' . $form['networkElementId']) ?></option><?php endif; ?></select><small>Название элемента берётся из справочника API.</small></label>
                <label class="field"><span>Административный район</span><input name="adminDistrict" value="<?= e($form['adminDistrict'] ?? '') ?>" maxlength="128"></label>
            </div></fieldset>
            <fieldset><legend><span>02</span> Событие и время</legend><div class="form-grid">
                <label class="field"><span>ПВ / PW</span><input name="pw" value="<?= e($form['pw'] ?? '') ?>" maxlength="450"></label>
                <label class="field"><span>Обслуживаемый элемент</span><input name="servElem" value="<?= e($form['servElem'] ?? '') ?>" maxlength="450"></label>
                <label class="field"><span>Количество РБС</span><input name="numbRbs" value="<?= e($form['numbRbs'] ?? '') ?>" maxlength="5" pattern="([1-9][0-9]{0,3}|10000|С)" placeholder="Например, 3 или С"><small>Пусто — неизвестно; «С» — сетевое событие.</small></label>
                <label class="field"><span>Начало, UTC <b aria-hidden="true">*</b></span><input type="datetime-local" step="1" name="beginTime" value="<?= e($form['beginTime'] ?? '') ?>" required></label>
                <label class="field"><span>Окончание, UTC</span><input type="datetime-local" step="1" name="endTime" value="<?= e($form['endTime'] ?? '') ?>"><small>Пусто — инцидент открыт.</small></label>
                <label class="field"><span>Кто сообщил</span><input name="alarmWho" value="<?= e($form['alarmWho'] ?? '') ?>" maxlength="450"></label>
                <label class="field full"><span>Описание</span><textarea name="descr" rows="3" maxlength="450" placeholder="Причина, затронутые объекты и выполненные работы"><?= e($form['descr'] ?? '') ?></textarea></label>
            </div></fieldset>
            <fieldset><legend><span>03</span> Длительности и примечание</legend><div class="form-grid">
                <?php foreach (['akb' => 'Работа от АКБ', 'dgu' => 'Работа от ДГУ', 'prostoy' => 'Простой'] as $name => $label): ?><label class="field"><span><?= e($label) ?></span><input name="<?= e($name) ?>" value="<?= e($form[$name] ?? '') ?>" pattern="[0-9]{1,3}:[0-5][0-9]:[0-5][0-9]" placeholder="00:00:00" inputmode="text" maxlength="12"><small>ЧЧ:ММ:СС, до 838:59:59; пусто — неизвестно.</small></label><?php endforeach; ?>
                <label class="field full"><span>Примечание</span><textarea name="descNotice" rows="2" maxlength="16000"><?= e($form['descNotice'] ?? '') ?></textarea></label>
            </div><p class="form-note">Продолжительность и общий простой рассчитывает API. Пустая длительность означает неизвестное значение.</p></fieldset>
            <div class="form-actions"><span class="muted">Поля со <b>*</b> обязательны</span><a class="button secondary" href="<?= e(href('incidents', $linkFilters)) ?>">Отмена</a><button class="button primary" type="submit"><?= $editId !== '' ? 'Сохранить изменения' : 'Создать инцидент' ?></button></div>
        </form>
    </section>
    <?php elseif ($showEditor): ?><div class="alert info">Ваша роль позволяет просматривать инциденты. Создание и редактирование доступны оператору и администратору.</div><?php endif; ?>

    <?php if ($detail !== null): ?>
    <section class="panel detail-panel"><div class="panel-heading"><div><span class="eyebrow">Карточка инцидента</span><h2>№<?= e($detail['incidentId'] ?? $detailId) ?> <span class="heading-badges"><?= badge($detail['status'] ?? (!empty($detail['endTime']) ? 'closed' : 'open')) ?> <?= badge($detail['severity'] ?? '') ?></span></h2></div><div class="inline-actions"><?php if ($canWrite): ?><a class="button secondary small" href="<?= e(href('incidents', $linkFilters + ['edit' => $detail['incidentId'] ?? $detailId])) ?>">Редактировать</a><?php endif; ?><a class="button ghost small" href="<?= e(href('incidents', $linkFilters)) ?>">Закрыть</a></div></div>
        <dl class="detail-grid"><?php foreach (['pw' => 'ПВ / PW', 'directionName' => 'Направление', 'techCenterName' => 'Технический центр', 'priorityName' => 'Приоритет', 'networkTypeName' => 'Тип сети', 'networkElementName' => 'Сетевой элемент', 'servElem' => 'Обслуживаемый элемент', 'numbRbs' => 'Количество РБС', 'equipmentTypeName' => 'Оборудование', 'alarmTypeName' => 'Тип аварии', 'regionName' => 'Регион', 'adminDistrict' => 'Район'] as $key => $label): ?><div><dt><?= e($label) ?></dt><dd><?= e(textValue($detail[$key] ?? null)) ?></dd></div><?php endforeach; ?><div><dt>Категория</dt><dd><?= e(labelValue((string) ($detail['category'] ?? ''))) ?></dd></div><div><dt>Начало, UTC</dt><dd><?= e(dateValue($detail['beginTime'] ?? null)) ?></dd></div><div><dt>Окончание, UTC</dt><dd><?= e(dateValue($detail['endTime'] ?? null)) ?></dd></div><div><dt>Продолжительность</dt><dd class="mono"><?= e(textValue($detail['lenght'] ?? null)) ?></dd></div><?php foreach (['akb' => 'АКБ', 'dgu' => 'ДГУ', 'prostoy' => 'Простой', 'commonProstoy' => 'Общий простой', 'alarmWho' => 'Кто сообщил', 'createdByUsername' => 'Автор'] as $key => $label): ?><div><dt><?= e($label) ?></dt><dd><?= e(textValue($detail[$key] ?? null)) ?></dd></div><?php endforeach; ?><div class="full"><dt>Описание</dt><dd class="preserve-lines"><?= e(textValue($detail['descr'] ?? null)) ?></dd></div><div class="full"><dt>Примечание</dt><dd class="preserve-lines"><?= e(textValue($detail['descNotice'] ?? null)) ?></dd></div></dl>
        <div class="detail-footer"><span>Обновлён: <?= e(dateValue($detail['updatedAt'] ?? null)) ?> UTC</span><?php if ($canWrite): ?><form method="post" action="<?= e(href('incidents', $linkFilters)) ?>" data-confirm="Удалить инцидент №<?= e($detail['incidentId'] ?? $detailId) ?>? Это действие нельзя отменить." data-busy="Удаление…"><?php csrf(); ?><input type="hidden" name="action" value="delete"><input type="hidden" name="incidentId" value="<?= e($detail['incidentId'] ?? $detailId) ?>"><button type="submit" class="button danger-outline small">Удалить инцидент</button></form><?php endif; ?></div>
    </section>
    <?php endif; ?>

    <section class="panel journal-panel"><div class="panel-heading"><div><h2>Журнал инцидентов <span class="count-pill"><?= numberValue($incidents['total'] ?? null) ?></span></h2><span class="muted">Время записей — UTC</span></div><a class="button secondary small" href="<?= e(href('incidents', $linkFilters + ['download' => 'details'])) ?>">↓ Скачать CSV</a></div>
    <?php if ($incidents === null): ?><div class="empty"><span class="empty-icon" aria-hidden="true">!</span><h3>Журнал недоступен</h3><p>Проверьте сообщения выше и повторите запрос.</p></div><?php elseif (!items($incidents)): ?><div class="empty"><span class="empty-icon" aria-hidden="true">☷</span><h3>Инциденты не найдены</h3><p>Измените фильтры или зарегистрируйте новый инцидент.</p></div><?php else: ?>
    <div class="table-wrap"><table class="incident-table"><thead><tr><th scope="col">№ / начало</th><th scope="col">Направление / ТЦ</th><th scope="col">Элемент сети</th><th scope="col">Описание</th><th scope="col">Критичность</th><th scope="col">Простой</th><th scope="col">Состояние</th><th scope="col"><span class="sr-only">Действие</span></th></tr></thead><tbody>
    <?php foreach (items($incidents) as $row): $rowId = (string) ($row['incidentId'] ?? ''); ?>
        <tr><td><a class="record-link" href="<?= e(href('incidents', $linkFilters + ['id' => $rowId])) ?>">#<?= e($rowId) ?></a><small><?= e(dateValue($row['beginTime'] ?? null)) ?></small></td><td><strong><?= e(textValue($row['directionCode'] ?? $row['directionName'] ?? null)) ?></strong><small><?= e(textValue($row['techCenterCode'] ?? $row['techCenterName'] ?? null)) ?></small></td><td><span class="truncate" title="<?= e($row['networkElementName'] ?? '') ?>"><?= e(textValue($row['networkElementName'] ?? $row['servElem'] ?? null)) ?></span><small><?= e(textValue($row['networkTypeCode'] ?? null)) ?> · РБС: <?= e(textValue($row['numbRbs'] ?? null)) ?></small></td><td class="description-cell"><span class="truncate" title="<?= e($row['descr'] ?? '') ?>"><?= e(textValue($row['descr'] ?? null)) ?></span><small><?= e(labelValue((string) ($row['category'] ?? ''))) ?></small></td><td><?= badge($row['severity'] ?? '') ?></td><td class="mono"><?= e(textValue($row['prostoy'] ?? null)) ?><small>Общий: <?= e(textValue($row['commonProstoy'] ?? null)) ?></small></td><td><?= badge($row['status'] ?? (!empty($row['endTime']) ? 'closed' : 'open')) ?></td><td><a class="row-arrow" aria-label="Открыть инцидент <?= e($rowId) ?>" href="<?= e(href('incidents', $linkFilters + ['id' => $rowId])) ?>">↗</a></td></tr>
    <?php endforeach; ?></tbody></table></div>
    <?php $total = (int) ($incidents['total'] ?? 0); $totalPages = max(1, (int) ceil($total / $size)); ?><div class="pagination"><span>Показаны <?= numberValue(min(($page - 1) * $size + 1, $total)) ?>–<?= numberValue(min($page * $size, $total)) ?> из <?= numberValue($total) ?></span><nav aria-label="Страницы журнала"><?php if ($page > 1): ?><a class="button secondary small" href="<?= e(href('incidents', $linkFilters + ['page' => $page - 1])) ?>">← Назад</a><?php endif; ?><strong><?= $page ?> <span>/ <?= $totalPages ?></span></strong><?php if ($page < $totalPages): ?><a class="button secondary small" href="<?= e(href('incidents', $linkFilters + ['page' => $page + 1])) ?>">Далее →</a><?php endif; ?></nav></div>
    <?php endif; ?></section>
    <?php if (!$canWrite): ?><p class="permission-note">Режим просмотра · Создание, изменение и удаление записей доступны оператору и администратору.</p><?php endif; ?>
<?php break; case 'reports': ?>
    <section class="panel report-export"><div class="export-icon" aria-hidden="true">↓</div><div><span class="eyebrow">Данные для дальнейшей работы</span><h2>Экспорт отчёта</h2><p>CSV с учётом выбранного периода и всех фильтров. Группировка: <strong><?= e($groupOptions[$groupBy]) ?></strong>.</p></div><div class="inline-actions"><a class="button secondary" href="<?= e(href('reports', $linkFilters + ['download' => 'details'])) ?>">Детальный CSV</a><a class="button primary" href="<?= e(href('reports', $linkFilters + ['groupBy' => $groupBy, 'download' => 'grouped'])) ?>">Сводный CSV <span aria-hidden="true">↓</span></a></div></section>
    <div class="report-facts"><span>Всего инцидентов <strong><?= numberValue($summary['total'] ?? null) ?></strong></span><span>Известный простой <strong class="mono"><?= e(secondsValue($summary['knownDowntimeSeconds'] ?? null)) ?></strong></span><span>Без исходных данных <strong><?= numberValue($summary['excludedDowntimeCount'] ?? null) ?></strong></span></div>
    <?php $groupKey = ['category' => 'byCategory', 'severity' => 'bySeverity', 'direction' => 'byDirection', 'techCenter' => 'byTechCenter', 'alarmType' => 'byAlarmType'][$groupBy]; groupTable($summary[$groupKey] ?? null, 'Сводка: ' . $groupOptions[$groupBy]); ?>
    <div class="two-columns"><?php groupTable($summary['bySeverity'] ?? null, 'Критичность', true); ?><?php groupTable($summary['byCategory'] ?? null, 'Категории', true); ?></div>
    <p class="permission-note">Значения простоя с неизвестными исходными данными не заменяются нулём. Сводный CSV содержит число исключённых записей по каждой группе.</p>
<?php break; case 'monitoring': ?>
    <div class="monitoring-notice"><span class="status-dot" aria-hidden="true"></span><strong>Текущий срез API</strong><span>Обновление выполняется при открытии страницы или вручную</span><span class="monitoring-time"><?= e(gmdate('d.m.Y H:i')) ?> UTC</span></div>
    <div class="kpi-grid monitoring-kpis">
        <article class="kpi-card accent-red"><div class="kpi-top"><span>Успешность вызовов</span><span class="kpi-icon" aria-hidden="true">↗</span></div><strong class="kpi-value"><?= numberValue($monitoring['avgCssr'] ?? null, 2) ?><?= is_numeric($monitoring['avgCssr'] ?? null) ? '<small>%</small>' : '' ?></strong><p>Средний CSSR</p></article>
        <article class="kpi-card accent-orange"><div class="kpi-top"><span>Обрывы вызовов</span><span class="kpi-icon" aria-hidden="true">⌁</span></div><strong class="kpi-value"><?= numberValue($monitoring['avgDcr'] ?? null, 2) ?><?= is_numeric($monitoring['avgDcr'] ?? null) ? '<small>%</small>' : '' ?></strong><p>Средний DCR</p></article>
        <article class="kpi-card accent-blue"><div class="kpi-top"><span>Передача данных</span><span class="kpi-icon" aria-hidden="true">⇅</span></div><strong class="kpi-value"><?= numberValue($monitoring['trafficTotalGb'] ?? null, 2) ?><small>ГБ</small></strong><p>Суммарный трафик</p></article>
        <article class="kpi-card accent-green"><div class="kpi-top"><span>Доступность сети</span><span class="kpi-icon" aria-hidden="true">◎</span></div><strong class="kpi-value <?= !is_numeric($monitoring['avgAvailability'] ?? null) ? 'text-value' : '' ?>"><?= is_numeric($monitoring['avgAvailability'] ?? null) ? numberValue($monitoring['avgAvailability'], 2) . '%' : 'Нет исходных данных' ?></strong><p>Измеренная доступность</p></article>
    </div>
    <div class="two-columns monitoring-columns"><section class="panel"><div class="panel-heading"><h2>Инфраструктура</h2><span class="muted">По данным API</span></div><dl class="infrastructure-stats"><div><dt>Базовые станции</dt><dd><?= numberValue($monitoring['btsTotal'] ?? null) ?></dd></div><div><dt>Сетевые элементы</dt><dd><?= numberValue($monitoring['networkElementsTotal'] ?? null) ?></dd></div><div><dt>Открытые аномалии</dt><dd><?= numberValue($monitoring['openAnomalies'] ?? null) ?></dd></div><?php if (is_numeric($monitoring['trafficActivityPct'] ?? null)): ?><div><dt>Активность трафика <small>Доля срезов с ненулевым трафиком</small></dt><dd><?= numberValue($monitoring['trafficActivityPct'], 2) ?><small>%</small></dd></div><?php endif; ?></dl><p class="form-note">Активность трафика не является измерением доступности сети.</p></section>
    <section class="panel map-panel"><div class="panel-heading"><h2>Карта сети</h2><span class="badge neutral">Слой не подключён</span></div><div class="map-placeholder"><div class="map-grid" aria-hidden="true"></div><div class="map-state"><span class="map-symbol" aria-hidden="true">◎</span><h3>Географический слой не подключён</h3><p>Точные координаты по умолчанию скрыты. Режим карты и допустимую точность нужно согласовать. Реальные элементы доступны ниже.</p><a class="text-link" href="#network-elements">К сетевым элементам ↓</a></div></div></section></div>
    <section class="panel"><div class="panel-heading"><div><h2>Аномалии сети <span class="count-pill"><?= numberValue($anomalies['total'] ?? null) ?></span></h2><span class="muted">Зарегистрированные события мониторинга</span></div></div>
    <?php if ($anomalies === null): ?><div class="empty compact">Данные об аномалиях недоступны. Проверьте сообщение API выше.</div><?php elseif (!items($anomalies)): ?><div class="empty compact"><h3>Аномалии не найдены</h3><p>API не вернул событий для этой страницы.</p></div><?php else: ?><div class="table-wrap"><table><thead><tr><th>№ / время</th><th>Объект</th><th>Метрика</th><th>Значение</th><th>Критичность</th><th>Состояние</th><th>Тип / инцидент</th></tr></thead><tbody><?php foreach (items($anomalies) as $row): ?><tr><td><strong>#<?= e($row['anomalyId'] ?? $row['id'] ?? '—') ?></strong><small><?= e(dateValue($row['detectedAt'] ?? $row['openedAt'] ?? $row['ts'] ?? null)) ?></small></td><td><?= e(textValue($row['networkElementName'] ?? $row['btsName'] ?? $row['cellName'] ?? $row['entityName'] ?? null)) ?></td><td><?= e(textValue($row['metricName'] ?? $row['metricCode'] ?? $row['kpiCode'] ?? $row['metric'] ?? null)) ?></td><td class="mono"><?= e(textValue($row['actualValue'] ?? $row['observedValue'] ?? $row['value'] ?? null)) ?></td><td><?= badge($row['severity'] ?? '') ?></td><td><?= badge($row['status'] ?? (!empty($row['resolvedAt']) ? 'closed' : 'open')) ?></td><td class="description-cell"><?= e(labelValue((string) ($row['type'] ?? ''))) ?><?php if (!empty($row['explainedByIncident'])): ?><small><a class="record-link" href="<?= e(href('incidents', ['id' => $row['explainedByIncident']])) ?>">Инцидент #<?= e($row['explainedByIncident']) ?></a></small><?php endif; ?></td></tr><?php endforeach; ?></tbody></table></div><?php $anomalyPages = max(1, (int) ceil((int) ($anomalies['total'] ?? 0) / $size)); ?><div class="pagination"><span>Страница <?= $page ?> из <?= $anomalyPages ?></span><nav aria-label="Страницы аномалий"><?php if ($page > 1): ?><a class="button secondary small" href="<?= e(href('monitoring', ['page' => $page - 1, 'size' => $size])) ?>">← Назад</a><?php endif; ?><?php if ($page < $anomalyPages): ?><a class="button secondary small" href="<?= e(href('monitoring', ['page' => $page + 1, 'size' => $size])) ?>">Далее →</a><?php endif; ?></nav></div><?php endif; ?></section>
    <section class="panel" id="network-elements"><div class="panel-heading"><h2>Сетевые элементы</h2><span class="muted">Первые <?= count($networkElements) ?> из <?= numberValue($networkElementsTotal) ?></span></div><?php if (!$networkElements): ?><div class="empty compact">Нет доступных элементов сети.</div><?php else: ?><div class="table-wrap element-list"><table><thead><tr><th>ID</th><th>Название</th><th>Регион</th><th>Тип элемента</th></tr></thead><tbody><?php foreach ($networkElements as $row): ?><tr><td class="mono"><?= e($row['networkElementId'] ?? $row['id'] ?? '—') ?></td><td><strong><?= e(textValue($row['fullName'] ?? $row['name'] ?? null)) ?></strong></td><td><?= e(textValue($row['regionName'] ?? $row['regionCode'] ?? null)) ?></td><td><?= e(labelValue((string) ($row['type'] ?? ''))) ?></td></tr><?php endforeach; ?></tbody></table></div><?php endif; ?></section>
<?php break; endswitch; ?>
<footer class="page-footer"><span>МОТИВ <b>·</b> Центр управления сетью</span><span>Время — UTC <b>·</b> <?= e(gmdate('d.m.Y H:i')) ?></span></footer>
</main>
<?php endif; ?>
</body>
</html>
