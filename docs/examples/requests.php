<?php
// Учебные функции для main db7f930. Этот файл сам не вызывает API.
// PHP 8 + расширение curl. Это пример клиента, а не новый сайт/контроллер.
declare(strict_types=1);

function apiRequest(
    string $baseUrl,
    string $method,
    string $path,
    ?string $token = null,
    ?array $body = null,
    array $query = []
): array {
    $url = rtrim($baseUrl, '/') . $path;
    if ($query !== []) {
        $url .= '?' . http_build_query($query, '', '&', PHP_QUERY_RFC3986);
    }

    $headers = ['Accept: application/json'];
    if ($token !== null) $headers[] = 'Authorization: Bearer ' . $token;
    if ($body !== null) $headers[] = 'Content-Type: application/json';

    $curl = curl_init($url);
    if ($curl === false) throw new RuntimeException('Не удалось создать cURL-запрос.');

    try {
        $options = [
            CURLOPT_CUSTOMREQUEST => $method,
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_HTTPHEADER => $headers,
            CURLOPT_CONNECTTIMEOUT => 5,
            CURLOPT_TIMEOUT => 30,
            CURLOPT_FOLLOWLOCATION => false
        ];
        if ($body !== null) {
            $options[CURLOPT_POSTFIELDS] = json_encode($body, JSON_UNESCAPED_UNICODE | JSON_THROW_ON_ERROR);
        }
        curl_setopt_array($curl, $options);
        $response = curl_exec($curl);
        if ($response === false) {
            // В браузер не нужно выводить внутренние адреса или детали cURL.
            throw new RuntimeException('API недоступен или соединение прервалось.');
        }
        $status = (int) curl_getinfo($curl, CURLINFO_RESPONSE_CODE);
        $contentType = (string) curl_getinfo($curl, CURLINFO_CONTENT_TYPE);
    } finally {
        curl_close($curl);
    }

    if ($status < 200 || $status >= 300) {
        $error = json_decode($response, true);
        $message = is_array($error)
            ? ($error['message'] ?? $error['error'] ?? $error['title'] ?? 'Ошибка API')
            : 'API вернул ответ без JSON';
        // Код доступен через $exception->getCode(): например, 401 или 403.
        throw new RuntimeException('HTTP ' . $status . ': ' . $message, $status);
    }

    return ['status' => $status, 'contentType' => $contentType, 'body' => $response];
}

function apiJson(
    string $baseUrl,
    string $method,
    string $path,
    ?string $token = null,
    ?array $body = null,
    array $query = []
): ?array {
    $response = apiRequest($baseUrl, $method, $path, $token, $body, $query);
    if ($response['status'] === 204) return null;
    return json_decode($response['body'], true, 512, JSON_THROW_ON_ERROR);
}

function apiLogin(string $baseUrl, string $username, string $password): array {
    return apiJson($baseUrl, 'POST', '/api/auth/login', null, [
        'username' => $username,
        'password' => $password
    ]);
}

function apiIncidents(string $baseUrl, string $token, string $from, string $to): array {
    return apiJson($baseUrl, 'GET', '/api/incidents', $token, null, ['from' => $from, 'to' => $to]);
}

function apiCreateIncident(string $baseUrl, string $token, array $input): array {
    return apiJson($baseUrl, 'POST', '/api/incidents', $token, $input);
}

function apiUpdateIncident(string $baseUrl, string $token, int $id, array $input): array {
    return apiJson($baseUrl, 'PUT', '/api/incidents/' . $id, $token, $input);
}

function apiDeleteIncident(string $baseUrl, string $token, int $id): void {
    apiRequest($baseUrl, 'DELETE', '/api/incidents/' . $id, $token);
}

function apiIncidentsCsv(string $baseUrl, string $token, string $from, string $to): string {
    return apiRequest($baseUrl, 'GET', '/api/incidents/export', $token, null, [
        'from' => $from,
        'to' => $to
    ])['body'];
}
