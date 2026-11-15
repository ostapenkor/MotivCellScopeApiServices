<?php
require __DIR__ . '/../src/bootstrap.php';

if (!empty($_SESSION['token'])) {
    header('Location: index.php');
    exit;
}

$error = '';
if (isset($_GET['expired'])) {
    $error = 'Сессия истекла, войдите снова.';
}

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    $username = trim((string) ($_POST['username'] ?? ''));
    $password = (string) ($_POST['password'] ?? '');

    if ($username === '' || $password === '') {
        $error = 'Заполните все поля!';
    } else {
        $res = $api->request('POST', '/api/auth/login', [
            'username' => $username,
            'password' => $password,
        ]);
        if ($res['status'] === 200 && is_array($res['body'])) {
            session_regenerate_id(true);
            $_SESSION['token']      = $res['body']['token'];
            $_SESSION['username']   = $res['body']['username'];
            $_SESSION['role']       = $res['body']['role'];
            $_SESSION['expires_ts'] = strtotime($res['body']['expiresAt']) ?: (time() + 2 * 3600);
            header('Location: index.php');
            exit;
        }
        $error = ($res['status'] === 401) ? 'Неверный логин или пароль' : apiErrorText($res);
    }
}
?>
<!DOCTYPE html>
<html lang="ru">
<head>
    <meta charset="UTF-8">
    <title>Вход - Мотив</title>
    <link rel="stylesheet" href="assets/style.css">
</head>
<body class="login-body">
    <div class="login-card">
        <div class="login-logo">📡 Мотив</div>
        <div class="login-sub">Вход в систему отчетности</div>

        <?php if ($error): ?>
            <div class="login-error" style="display:block;"><?= e($error) ?></div>
        <?php endif; ?>

        <form method="post" action="login.php">
            <label for="username">Логин</label>
            <input type="text" id="username" name="username" placeholder="Введите логин..."
                   value="<?= e($_POST['username'] ?? '') ?>" autocomplete="username" required>

            <label for="password">Пароль</label>
            <input type="password" id="password" name="password" placeholder="••••••••"
                   autocomplete="current-password" required>

            <button type="submit" class="btn-login">Войти в систему</button>
        </form>
    </div>
</body>
</html>
