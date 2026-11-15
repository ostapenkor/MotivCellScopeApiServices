<?php
session_start();

// Если уже вошли — на дашборд
if (!empty($_SESSION['role'])) {
    header('Location: index.php');
    exit;
}

$error = '';
if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    $username = trim($_POST['username'] ?? '');
    $role     = $_POST['role'] ?? '';

    if ($username === '') {
        $error = 'Введите логин';
    } elseif (!in_array($role, ['admin', 'engineer', 'operator'], true)) {
        $error = 'Неверная роль';
    } else {
        // Phase 1: mock-сессия. В Phase 2 здесь будет вызов /api/auth/login
        $_SESSION['username'] = $username;
        $_SESSION['role']     = $role;
        $_SESSION['token']    = 'mock-token-' . bin2hex(random_bytes(8));
        header('Location: index.php');
        exit;
    }
}

$selectedRole = $_POST['role'] ?? 'admin';
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
            <div style="color:#e30613; font-size:13px; margin-bottom:10px; text-align:left;">
                <?= htmlspecialchars($error) ?>
            </div>
        <?php endif; ?>

        <form method="post" action="login.php">
            <label>Логин</label>
            <input type="text" name="username" placeholder="Введите логин..."
                   value="<?= htmlspecialchars($_POST['username'] ?? 'admin') ?>" required>

            <label>Пароль</label>
            <input type="password" name="password" placeholder="••••••••" value="12345">

            <label>Войти как:</label>
            <select name="role">
                <option value="admin"    <?= $selectedRole === 'admin'    ? 'selected' : '' ?>>Администратор</option>
                <option value="engineer" <?= $selectedRole === 'engineer' ? 'selected' : '' ?>>Инженер</option>
                <option value="operator" <?= $selectedRole === 'operator' ? 'selected' : '' ?>>Дежурный</option>
            </select>

            <button type="submit" class="btn-login">Войти в систему</button>
        </form>
    </div>
</body>
</html>