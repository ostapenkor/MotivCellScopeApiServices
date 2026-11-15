<?php
require __DIR__ . '/../src/bootstrap.php';
requireLogin();
require __DIR__ . '/../src/layout.php';

if (!isAdmin()) {
    renderHeader('Админ-панель', 'admin');
    echo '<div class="alert alert-error">403 — доступ только для администратора</div>';
    renderFooter();
    exit;
}

$token = authToken();
$tab = ($_GET['tab'] ?? 'users') === 'audit' ? 'audit' : 'users';

// Создание пользователя
if ($_SERVER['REQUEST_METHOD'] === 'POST' && ($_POST['action'] ?? '') === 'create_user') {
    csrfCheck();
    $res = $api->request('POST', '/api/admin/users', [
        'username' => trim((string) ($_POST['username'] ?? '')),
        'password' => (string) ($_POST['password'] ?? ''),
        'role'     => (string) ($_POST['role'] ?? 'engineer'),
    ], $token);
    handleUnauthorized($res);
    if ($res['status'] === 201) {
        flash('success', 'Пользователь создан');
    } else {
        flash('error', apiErrorText($res));
    }
    header('Location: admin.php?tab=users');
    exit;
}

$users = [];
$audit = ['total' => 0, 'items' => []];
$loadError = null;
if ($tab === 'users') {
    $res = $api->request('GET', '/api/admin/users', null, $token);
    handleUnauthorized($res);
    if ($res['status'] === 200) {
        $users = $res['body'];
    } else {
        $loadError = apiErrorText($res);
    }
} else {
    $page = max(1, (int) ($_GET['page'] ?? 1));
    $qs = http_build_query(array_filter([
        'from' => isset($_GET['from']) && $_GET['from'] !== '' ? toIso($_GET['from']) : null,
        'to' => isset($_GET['to']) && $_GET['to'] !== '' ? toIso($_GET['to']) : null,
        'userId' => $_GET['userId'] ?? null,
        'action' => $_GET['action'] ?? null,
        'page' => $page, 'pageSize' => 50,
    ], fn ($x) => $x !== null && $x !== ''));
    $res = $api->request('GET', '/api/admin/audit?' . $qs, null, $token);
    handleUnauthorized($res);
    if ($res['status'] === 200) {
        $audit = $res['body'];
    } else {
        $loadError = apiErrorText($res);
    }
    $usersRes = $api->request('GET', '/api/admin/users', null, $token);
    handleUnauthorized($usersRes);
    $users = $usersRes['body'] ?? [];
}
$auditActions = ['login', 'create_user', 'create_incident', 'update_incident', 'delete_incident', 'explain_anomaly', 'update_anomaly_status', 'export_incidents', 'export_report', 'view_audit_log', 'view_users', 'detect_anomalies', 'detect_availability_anomalies', 'refresh_kpi'];

renderHeader('Админ-панель', 'admin');
?>
<div class="admin-tabs">
    <a class="admin-tab <?= $tab === 'users' ? 'active' : '' ?>" href="admin.php?tab=users">Пользователи</a>
    <a class="admin-tab <?= $tab === 'audit' ? 'active' : '' ?>" href="admin.php?tab=audit">Журнал аудита</a>
</div>

<?php if ($loadError): ?><div class="alert alert-error"><?= e($loadError) ?></div><?php endif; ?>

<?php if ($tab === 'users'): ?>
<div class="card">
    <div class="card-title">Управление пользователями
        <button class="btn btn-primary" type="button" onclick="document.getElementById('create-user').style.display='block'">+ Создать пользователя</button>
    </div>
    <form id="create-user" method="post" style="display:none;margin-bottom:20px;">
        <input type="hidden" name="csrf" value="<?= e(csrfToken()) ?>">
        <input type="hidden" name="action" value="create_user">
        <div class="form-grid">
            <div class="form-group"><label>Логин</label><input type="text" name="username" maxlength="64" required></div>
            <div class="form-group"><label>Пароль (минимум 4)</label><input type="password" name="password" minlength="4" required></div>
            <div class="form-group"><label>Роль</label><select name="role">
                <option value="engineer">Инженер</option><option value="operator">Дежурный</option><option value="admin">Администратор</option>
            </select></div>
        </div>
        <div style="text-align:right;margin-top:15px;"><button class="btn btn-primary" type="submit">Создать</button></div>
    </form>
    <?php if (!$users): ?>
        <div class="empty-state">Пользователей нет</div>
    <?php else: ?>
    <table>
        <tr><th>Пользователь</th><th>Роль</th><th>Создан</th></tr>
        <?php foreach ($users as $u): ?>
            <tr><td><?= e($u['username']) ?></td><td><?= roleBadge($u['role'] ?? null) ?></td><td><?= e(fmtDate($u['createdAt'] ?? null, 'd.m.Y')) ?></td></tr>
        <?php endforeach; ?>
    </table>
    <?php endif; ?>
    <div style="font-size:12px;color:#8c8c99;margin-top:10px;">Изменение роли, блокировка и удаление пользователей API не поддерживает — доступны только список и создание.</div>
</div>
<?php else: ?>
<div class="card">
    <div class="card-title">Журнал действий</div>
    <form class="filter-form" method="get" style="margin-bottom:15px;">
        <input type="hidden" name="tab" value="audit">
        <div class="form-group"><label>Период с</label><input type="datetime-local" name="from" value="<?= e($_GET['from'] ?? '') ?>"></div>
        <div class="form-group"><label>по</label><input type="datetime-local" name="to" value="<?= e($_GET['to'] ?? '') ?>"></div>
        <div class="form-group"><label>Пользователь</label><select name="userId"><option value="">Все пользователи</option>
            <?php foreach ($users as $u): ?><option value="<?= (int) $u['userId'] ?>" <?= (string) ($_GET['userId'] ?? '') === (string) $u['userId'] ? 'selected' : '' ?>><?= e($u['username']) ?></option><?php endforeach; ?>
        </select></div>
        <div class="form-group"><label>Действие</label><select name="action"><option value="">Все действия</option>
            <?php foreach ($auditActions as $a): ?><option value="<?= $a ?>" <?= (($_GET['action'] ?? '') === $a) ? 'selected' : '' ?>><?= e(auditActionLabel($a)) ?></option><?php endforeach; ?>
        </select></div>
        <button class="btn btn-secondary" type="submit">Показать</button>
    </form>
    <?php if (empty($audit['items'])): ?>
        <div class="empty-state">Записей по заданным условиям нет</div>
    <?php else: ?>
    <table>
        <tr><th>Время</th><th>Пользователь</th><th>Роль</th><th>Действие</th><th>Объект</th><th>IP-адрес</th><th>Результат</th></tr>
        <?php foreach ($audit['items'] as $row): ?>
            <tr>
                <td><?= e(fmtDate($row['ts'] ?? null, 'd.m H:i:s')) ?></td>
                <td><?= e($row['username']) ?></td>
                <td><?= roleBadge($row['role'] ?? null) ?></td>
                <td><?= e(auditActionLabel($row['action'] ?? null)) ?> <span style="color:#8c8c99;">(<?= e($row['action']) ?>)</span></td>
                <td><?= e($row['entityType'] ?? '') ?><?= isset($row['entityId']) ? ' #' . e($row['entityId']) : '' ?></td>
                <td><?= e($row['ipAddress'] ?? '—') ?></td>
                <td><?= !empty($row['success']) ? '<span style="color:#4caf50;">Успех</span>' : '<span style="color:#e30613;">Ошибка</span>' ?></td>
            </tr>
        <?php endforeach; ?>
    </table>
    <div class="pagination">
        <?php $page = max(1, (int) ($_GET['page'] ?? 1)); ?>
        <?php if ($page > 1): ?><a href="?<?= e(http_build_query(array_merge($_GET, ['page' => $page - 1]))) ?>">← Назад</a><?php endif; ?>
        <span>Стр. <?= $page ?> · Всего: <?= (int) $audit['total'] ?></span>
        <?php if ($page * 50 < (int) $audit['total']): ?><a href="?<?= e(http_build_query(array_merge($_GET, ['page' => $page + 1]))) ?>">Вперёд →</a><?php endif; ?>
    </div>
    <?php endif; ?>
</div>
<?php endif; ?>
<?php renderFooter(); ?>
