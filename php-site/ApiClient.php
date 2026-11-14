<?php
declare(strict_types=1);

/** Синхронный HTTP-клиент. Адрес сервера задаёт только окружение. */
final class ApiClient
{
    private string $baseUrl;

    public function __construct(string $baseUrl, private readonly ?string $token = null)
    {
        $parts = parse_url($baseUrl);
        if ($parts === false || !in_array($parts['scheme'] ?? '', ['http', 'https'], true)
            || empty($parts['host']) || isset($parts['user']) || isset($parts['pass'])
            || isset($parts['query']) || isset($parts['fragment'])) {
            throw new RuntimeException('Некорректная настройка API_BASE_URL.');
        }
        $this->baseUrl = rtrim($baseUrl, '/');
    }

    public function request(string $method, string $path, array $query = [], ?array $payload = null, bool $csv = false): array
    {
        // Внешний ввод не может подменить хост или произвольный путь запроса.
        $routes = [
            'GET' => '#^/api/(auth/me|reference/all|incidents(?:/[1-9][0-9]*|/export)?|reports/(?:incidents-summary|incidents-export|available-periods)|monitoring/(?:summary|anomalies|network-elements))$#D',
            'POST' => '#^/api/(?:auth/login|incidents)$#D',
            'PUT' => '#^/api/incidents/[1-9][0-9]*$#D',
            'DELETE' => '#^/api/incidents/[1-9][0-9]*$#D',
        ];
        if (!isset($routes[$method]) || !preg_match($routes[$method], $path)) {
            throw new RuntimeException('Этот запрос API не разрешён клиентом.');
        }
        if (!extension_loaded('curl')) {
            throw new RuntimeException('На PHP-сервере отсутствует расширение cURL. Проверьте установку или запустите Docker-образ сайта.');
        }
        $url = $this->baseUrl . $path . ($query ? '?' . http_build_query($query, '', '&', PHP_QUERY_RFC3986) : '');
        $headers = ['Accept: ' . ($csv ? 'text/csv' : 'application/json')];
        if ($this->token !== null && $this->token !== '') {
            $headers[] = 'Authorization: Bearer ' . $this->token;
        }
        $handle = curl_init($url);
        $options = [
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_CUSTOMREQUEST => $method,
            CURLOPT_CONNECTTIMEOUT => 5,
            CURLOPT_TIMEOUT => 30,
            CURLOPT_FOLLOWLOCATION => false,
            CURLOPT_PROTOCOLS => CURLPROTO_HTTP | CURLPROTO_HTTPS,
            CURLOPT_MAXREDIRS => 0,
            CURLOPT_HTTPHEADER => $headers,
        ];
        if ($payload !== null) {
            $options[CURLOPT_POSTFIELDS] = json_encode($payload, JSON_THROW_ON_ERROR | JSON_UNESCAPED_UNICODE);
            $options[CURLOPT_HTTPHEADER][] = 'Content-Type: application/json';
        }
        curl_setopt_array($handle, $options);
        $body = curl_exec($handle);
        $status = (int) curl_getinfo($handle, CURLINFO_RESPONSE_CODE);
        $errno = curl_errno($handle);
        curl_close($handle);
        if ($body === false || $errno !== 0) {
            throw new ApiException($errno === CURLE_OPERATION_TIMEDOUT
                ? 'Сервер API не ответил за 30 секунд. Повторите запрос.'
                : 'Не удалось соединиться с API. Проверьте доступность сервера.', 0);
        }
        $data = json_decode($body, true);
        if ($status < 200 || $status >= 300) {
            $message = match ($status) {
                401 => 'Сессия истекла. Войдите снова.',
                403 => 'API запретил эту операцию для вашей роли.',
                404 => 'Запись или метод API не найден.',
                409 => 'Запись уже изменена другим пользователем. Обновите её перед сохранением.',
                422, 400 => 'API отклонил данные формы.',
                default => 'API временно недоступен. Код ответа: ' . $status . '.',
            };
            if ($status < 500 && is_array($data)) {
                $details = [];
                foreach (is_array($data['errors'] ?? null) ? $data['errors'] : [] as $field => $messages) {
                    if (!is_array($messages)) { $messages = [$messages]; }
                    foreach ($messages as $item) {
                        if (is_string($item)) { $details[] = $field . ': ' . $item; }
                    }
                }
                if ($details) {
                    $message .= ' ' . implode(' ', array_slice($details, 0, 12));
                } elseif (isset($data['message']) && is_string($data['message'])) {
                    $message = $data['message'];
                }
            }
            throw new ApiException($message, $status);
        }
        if ($csv) { return ['body' => $body]; }
        if ($status === 204 || trim($body) === '') { return []; }
        if (!is_array($data)) { throw new ApiException('API вернул ответ в неподдерживаемом формате.', $status); }
        return $data;
    }
}

final class ApiException extends RuntimeException
{
    public function __construct(string $message, public readonly int $status)
    {
        parent::__construct($message);
    }
}
