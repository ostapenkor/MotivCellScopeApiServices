<?php
final class ApiClient
{
    public function __construct(private string $base) {}

    /** @return array{status:int, body:mixed} */
    public function request(string $method, string $path, ?array $json = null, ?string $token = null): array
    {
        $ch = curl_init($this->base . $path);
        $headers = ['Accept: application/json'];
        if ($token)   $headers[] = 'Authorization: Bearer ' . $token;
        if ($json !== null) $headers[] = 'Content-Type: application/json';

        curl_setopt_array($ch, [
            CURLOPT_CUSTOMREQUEST  => $method,
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_HTTPHEADER     => $headers,
            CURLOPT_POSTFIELDS     => $json !== null ? json_encode($json) : null,
            CURLOPT_TIMEOUT        => 15,
        ]);
        $raw    = curl_exec($ch);
        $status = curl_getinfo($ch, CURLINFO_RESPONSE_CODE);
        curl_close($ch);

        return ['status' => $status, 'body' => $raw ? json_decode($raw, true) : null];
    }
}