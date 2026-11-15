<?php
// Общий каркас страниц: шапка с навигацией по ролям и подвал.

function renderHeader(string $pageTitle, string $currentPage): void
{
    $user     = authUser();
    $roleName = ['admin' => 'Администратор', 'engineer' => 'Инженер', 'operator' => 'Дежурный'][$user['role']] ?? $user['role'];
    $tabs = [
        ['page' => 'index',      'file' => 'index.php',      'label' => 'Дашборд',      'admin' => false],
        ['page' => 'incidents',  'file' => 'incidents.php',  'label' => 'Аварии',       'admin' => false],
        ['page' => 'reports',    'file' => 'reports.php',    'label' => 'Отчетность',   'admin' => false],
        ['page' => 'monitoring', 'file' => 'monitoring.php', 'label' => 'Мониторинг',   'admin' => false],
        ['page' => 'admin',      'file' => 'admin.php',      'label' => 'Админ-панель', 'admin' => true],
    ];
    $flash = pullFlash();
    ?>
<!DOCTYPE html>
<html lang="ru">
<head>
    <meta charset="UTF-8">
    <title><?= e($pageTitle) ?> - Мотив</title>
    <link rel="stylesheet" href="assets/style.css">
</head>
<body>
    <header>
        <div class="logo">📡 Мотив</div>
        <div class="tabs">
            <?php foreach ($tabs as $tab): ?>
                <?php if ($tab['admin'] && !isAdmin()) continue; ?>
                <a href="<?= $tab['file'] ?>" class="tab<?= $tab['page'] === $currentPage ? ' active' : '' ?>"><?= e($tab['label']) ?></a>
            <?php endforeach; ?>
        </div>
        <div class="user-info">
            <span><?= e($user['username']) ?> · <span style="color:#fff;font-weight:bold;"><?= e($roleName) ?></span></span>
            <a href="logout.php" class="btn-logout" style="text-decoration:none;display:inline-block;">Выйти</a>
        </div>
    </header>
    <div class="content">
    <?php if ($flash): ?>
        <div class="alert alert-<?= $flash['type'] === 'success' ? 'success' : 'error' ?>"><?= e($flash['text']) ?></div>
    <?php endif; ?>
    <?php
}

function renderFooter(): void
{
    ?>
    </div>
</body>
</html>
    <?php
}
