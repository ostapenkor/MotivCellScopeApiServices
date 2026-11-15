<?php

final class ApiClient
{
    public function __construct(private string $base, private int $timeout = 20)
    {
    }

    /**
     * JSON-запрос к REST API.
     * @return array{status:int, body:mixed, error:?string}
     */
    public function request(string $method, string $path, ?array $json = null, ?string $token = null): array
    {
        $headers = ['Accept: application/json'];
        if ($token) {
            $headers[] = 'Authorization: Bearer ' . $token;
        }
        if ($json !== null) {
            $headers[] = 'Content-Type: application/json';
        }

        $ch = curl_init($this->base . $path);
        curl_setopt_array($ch, [
            CURLOPT_CUSTOMREQUEST  => $method,
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_HTTPHEADER     => $headers,
            CURLOPT_POSTFIELDS     => $json !== null ? json_encode($json, JSON_UNESCAPED_UNICODE) : null,
            CURLOPT_TIMEOUT        => $this->timeout,
        ]);
        $raw    = curl_exec($ch);
        $errno  = curl_errno($ch);
        $error  = $errno ? curl_error($ch) : null;
        $status = (int) curl_getinfo($ch, CURLINFO_RESPONSE_CODE);
        curl_close($ch);

        if ($errno) {
            return ['status' => 0, 'body' => null, 'error' => 'API недоступен: ' . $error];
        }

        return ['status' => $status, 'body' => $raw ? json_decode($raw, true) : null, 'error' => null];
    }

    /**
     * Сырой запрос (для CSV-выгрузок).
     * @return array{status:int, raw:?string, contentType:?string, error:?string}
     */
    public function raw(string $path, ?string $token = null): array
    {
        $ch = curl_init($this->base . $path);
        $headers = [];
        if ($token) {
            $headers[] = 'Authorization: Bearer ' . $token;
        }
        curl_setopt_array($ch, [
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_HTTPHEADER     => $headers,
            CURLOPT_TIMEOUT        => $this->timeout,
        ]);
        $raw    = curl_exec($ch);
        $errno  = curl_errno($ch);
        $error  = $errno ? curl_error($ch) : null;
        $status = (int) curl_getinfo($ch, CURLINFO_RESPONSE_CODE);
        $type   = curl_getinfo($ch, CURLINFO_CONTENT_TYPE) ?: null;
        curl_close($ch);

        if ($errno) {
            return ['status' => 0, 'raw' => null, 'contentType' => null, 'error' => 'API недоступен: ' . $error];
        }

        return ['status' => $status, 'raw' => $raw, 'contentType' => $type, 'error' => null];
    }
}
