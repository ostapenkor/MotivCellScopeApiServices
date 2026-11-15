<?php
// Проксирование CSV-выгрузок API: браузер не ходит в API напрямую (нет CORS,
// токен не должен попадать в клиентский JS).
require __DIR__ . '/../src/bootstrap.php';
requireLogin();

$type = $_GET['type'] ?? '';
$from = trim((string) ($_GET['from'] ?? ''));
$to   = trim((string) ($_GET['to'] ?? ''));
$query = [];
if ($from !== '') $query['from'] = toIso($from);
if ($to !== '')   $query['to'] = toIso($to);
$qs = $query ? ('?' . http_build_query($query)) : '';

if ($type === 'incidents') {
    if (!$query) {
        http_response_code(400);
        exit('Для выгрузки инцидентов нужен период from/to.');
    }
    $path = '/api/incidents/export' . $qs;
    $fallbackName = 'incidents.csv';
} elseif ($type === 'report') {
    $path = '/api/reports/export' . $qs;
    $fallbackName = 'report_' . date('Ymd') . '.csv';
} else {
    http_response_code(400);
    exit('Неизвестный тип выгрузки.');
}

$res = $api->raw($path, authToken());
handleUnauthorized($res);
if ($res['status'] !== 200) {
    http_response_code($res['status'] ?: 502);
    exit('Не удалось сформировать выгрузку: ' . e($res['error'] ?? ('HTTP ' . $res['status'])));
}

header('Content-Type: ' . ($res['contentType'] ?: 'text/csv; charset=utf-8'));
header('Content-Disposition: attachment; filename="' . $fallbackName . '"');
echo $res['raw'];
