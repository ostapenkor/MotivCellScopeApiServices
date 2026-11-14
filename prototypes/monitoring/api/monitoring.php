<?php
declare(strict_types=1);

// Integration stub: this endpoint serves synthetic demo data only.
header('Content-Type: application/json; charset=utf-8');
header('Cache-Control: no-store');
header('X-Content-Type-Options: nosniff');

function respond(int $status, array $body): void
{
    http_response_code($status);
    try {
        echo json_encode($body, JSON_THROW_ON_ERROR | JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
    } catch (JsonException $exception) {
        http_response_code(500);
        echo '{"error":{"code":"JSON_ENCODING_FAILED","message":"Could not encode the response."}}';
    }
    exit;
}

function fail(int $status, string $code, string $message): void
{
    respond($status, ['error' => ['code' => $code, 'message' => $message]]);
}

if (($_SERVER['REQUEST_METHOD'] ?? '') !== 'GET') {
    header('Allow: GET');
    fail(405, 'METHOD_NOT_ALLOWED', 'Use GET.');
}

$mode = getenv('MONITORING_MODE');
if ($mode !== false && $mode !== 'demo') {
    // TODO_AUTH: authenticate the session and authorize every request before production access.
    // TODO_REST: fetch the authorized C# REST API server-side, using a fixed allowlisted URL
    // and credentials from environment variables. Never forward server secrets to the browser.
    // TODO_SERVER: keep BS-to-cell mapping and exact coordinates private; aggregate on the server.
    // No live proxy is implemented here.
    fail(503, 'REST_NOT_CONNECTED', 'TODO: connect authorised C# REST. This stub supports demo mode only.');
}

$action = $_GET['action'] ?? 'cells';
if (!is_string($action) || !in_array($action, ['cells', 'cell'], true)) {
    fail(400, 'INVALID_ACTION', 'Allowed actions: cells, cell.');
}

$cellId = $_GET['id'] ?? null;
if ($cellId !== null && (!is_string($cellId) || preg_match('/\A[A-Za-z0-9][A-Za-z0-9_.:-]{0,127}\z/D', $cellId) !== 1)) {
    fail(400, 'INVALID_ID', 'A cell ID must contain 1 to 128 ASCII letters, digits, underscores, dots, colons or hyphens.');
}
if ($action === 'cell' && $cellId === null) {
    fail(400, 'MISSING_ID', 'The cell action requires an id.');
}

$raw = @file_get_contents(__DIR__ . '/../data/monitoring-demo.json');
if ($raw === false) {
    fail(503, 'DEMO_DATA_UNAVAILABLE', 'Demo data is unavailable.');
}
try {
    $payload = json_decode($raw, true, 512, JSON_THROW_ON_ERROR);
} catch (JsonException $exception) {
    fail(503, 'DEMO_DATA_INVALID', 'Demo data is not valid JSON.');
}
if (!is_array($payload) || ($payload['mode'] ?? null) !== 'demo'
    || !is_string($payload['asOf'] ?? null) || !is_string($payload['gridVersion'] ?? null)
    || !is_array($payload['regions'] ?? null) || !is_array($payload['cells'] ?? null)) {
    fail(503, 'DEMO_DATA_INVALID', 'Demo data has an invalid envelope.');
}

if ($action === 'cells') {
    respond(200, $payload);
}
foreach ($payload['cells'] as $cell) {
    if (is_array($cell) && ($cell['id'] ?? null) === $cellId) {
        respond(200, [
            'mode' => 'demo',
            'asOf' => $payload['asOf'],
            'gridVersion' => $payload['gridVersion'],
            'cell' => $cell,
        ]);
    }
}
fail(404, 'CELL_NOT_FOUND', 'No demo cell has this id.');
