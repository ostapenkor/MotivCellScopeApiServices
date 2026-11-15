<?php
// Общая шапка + футер для всех защищённых страниц

function renderHeader(string $pageTitle, string $currentPage): void
{
    global $roleName, $isAdmin;

    $tabs = [
        ['page' => 'index',      'file' => 'index.php',      'label' => 'Дашборд',      'admin' => false],
        ['page' => 'incidents',  'file' => 'incidents.php',  'label' => 'Аварии',       'admin' => false],
        ['page' => 'reports',    'file' => 'reports.php',    'label' => 'Отчетность',   'admin' => false],
        ['page' => 'monitoring', 'file' => 'monitoring.php', 'label' => 'Мониторинг',   'admin' => false],
        ['page' => 'admin',      'file' => 'admin.php',      'label' => 'Админ-панель', 'admin' => true],
    ];
    ?>
<!DOCTYPE html>
<html lang="ru">
<head>
    <meta charset="UTF-8">
    <title><?= htmlspecialchars($pageTitle) ?> - Мотив</title>
    <link rel="stylesheet" href="assets/style.css">
</head>
<body>
    <header>
        <div class="logo">📡 Мотив</div>
        <div class="tabs">
            <?php foreach ($tabs as $tab): ?>
                <?php if ($tab['admin'] && !$isAdmin) continue; ?>
                <a href="<?= $tab['file'] ?>" class="tab<?= $tab['page'] === $currentPage ? ' active' : '' ?>"><?= htmlspecialchars($tab['label']) ?></a>
            <?php endforeach; ?>
        </div>
        <div class="user-info">
            <span>Роль: <span style="color: #fff; font-weight: bold;"><?= htmlspecialchars($roleName) ?></span></span>
            <a href="logout.php" class="btn-logout" style="text-decoration:none; display:inline-block;">Выйти</a>
        </div>
    </header>
    <div class="content">
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