<?php
// Общий bootstrap всех страниц: сессия, конфиг, API-клиент, guard и CSRF.

if (session_status() !== PHP_SESSION_ACTIVE) {
    session_set_cookie_params(['httponly' => true, 'samesite' => 'Lax']);
    session_start();
}

$config = require __DIR__ . '/config.php';
require __DIR__ . '/ApiClient.php';
require __DIR__ . '/helpers.php';

$api = new ApiClient($config['api_base'], (int) ($config['timeout'] ?? 20));

function authToken(): ?string
{
    return $_SESSION['token'] ?? null;
}

function authUser(): array
{
    return [
        'username' => $_SESSION['username'] ?? 'Пользователь',
        'role'     => $_SESSION['role'] ?? null,
    ];
}

function canEdit(): bool
{
    return in_array($_SESSION['role'] ?? null, ['operator', 'admin'], true);
}

function isAdmin(): bool
{
    return ($_SESSION['role'] ?? null) === 'admin';
}

/** Доступ только для вошедших; иначе — на логин. Проверяет и срок токена. */
function requireLogin(): void
{
    $expired = isset($_SESSION['expires_ts']) && time() >= (int) $_SESSION['expires_ts'];
    if (empty($_SESSION['token']) || $expired) {
        session_unset();
        header('Location: login.php' . ($expired ? '?expired=1' : ''));
        exit;
    }
}

/** Вызывать после любого API-ответа: 401 означает протухшую сессию. */
function handleUnauthorized(array $res): void
{
    if (($res['status'] ?? 0) === 401) {
        session_unset();
        header('Location: login.php?expired=1');
        exit;
    }
}

function csrfToken(): string
{
    if (empty($_SESSION['csrf'])) {
        $_SESSION['csrf'] = bin2hex(random_bytes(16));
    }
    return $_SESSION['csrf'];
}

function csrfCheck(): void
{
    if (!hash_equals($_SESSION['csrf'] ?? '', (string) ($_POST['csrf'] ?? ''))) {
        http_response_code(400);
        exit('Недействительный CSRF-токен. Вернитесь назад и попробуйте снова.');
    }
}

function flash(string $type, string $text): void
{
    $_SESSION['flash'] = ['type' => $type, 'text' => $text];
}

function pullFlash(): ?array
{
    $flash = $_SESSION['flash'] ?? null;
    unset($_SESSION['flash']);
    return $flash;
}
