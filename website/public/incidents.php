<?php
require __DIR__ . '/../src/bootstrap.php';
requireLogin();
require __DIR__ . '/../src/layout.php';

$token = authToken();

// Удаление (POST с карточки/списка)
if ($_SERVER['REQUEST_METHOD'] === 'POST' && ($_POST['action'] ?? '') === 'delete') {
    csrfCheck();
    if (!canEdit()) {
        flash('error', 'Недостаточно прав для удаления.');
    } else {
        $res = $api->request('DELETE', '/api/incidents/' . (int) $_POST['id'], null, $token);
        handleUnauthorized($res);
        if ($res['status'] === 204) {
            flash('success', 'Авария удалена. Связанные аномалии отвязаны.');
        } else {
            flash('error', apiErrorText($res));
        }
    }
    header('Location: incidents.php');
    exit;
}

$from = $_GET['from'] ?? date('Y-m-d\TH:i', strtotime('-30 days'));
$to   = $_GET['to'] ?? date('Y-m-d\TH:i');
$search      = trim((string) ($_GET['q'] ?? ''));
$fDirection  = $_GET['directionCode'] ?? '';
$fSeverity   = $_GET['severity'] ?? '';
$fOpenOnly   = isset($_GET['openOnly']);

$refsRes = $api->request('GET', '/api/reference/all', null, $token);
handleUnauthorized($refsRes);
$refs = $refsRes['body'] ?? [];

$res = $api->request('GET', '/api/incidents?from=' . urlencode((string) toIso($from)) . '&to=' . urlencode((string) toIso($to)), null, $token);
handleUnauthorized($res);
$items = $res['body']['items'] ?? [];
$loadError = ($res['status'] !== 200) ? apiErrorText($res) : null;

// Клиентские фильтры (API умеет только период)
$items = array_values(array_filter($items, function ($i) use ($search, $fDirection, $fSeverity, $fOpenOnly) {
    if ($fOpenOnly && !empty($i['endTime'])) return false;
    if ($fDirection !== '' && ($i['directionCode'] ?? null) !== $fDirection) return false;
    if ($fSeverity !== '' && ($i['severity'] ?? null) !== $fSeverity) return false;
    if ($search !== '') {
        $hay = mb_strtolower(implode(' ', array_filter([$i['pw'] ?? '', $i['descr'] ?? '', $i['servElem'] ?? '', $i['networkElementName'] ?? ''])));
        if (!str_contains($hay, mb_strtolower($search))) return false;
    }
    return true;
}));

renderHeader('Аварии', 'incidents');
?>
<div class="card">
    <div class="table-toolbar">
        <form class="table-filters" method="get">
            <input type="datetime-local" name="from" value="<?= e($from) ?>">
            <span>—</span>
            <input type="datetime-local" name="to" value="<?= e($to) ?>">
            <input type="text" name="q" placeholder="Поиск: PW, описание, элемент" value="<?= e($search) ?>">
            <select name="directionCode">
                <option value="">Все направления</option>
                <?php foreach (($refs['directions'] ?? []) as $d): ?>
                    <option value="<?= e($d['code']) ?>" <?= $fDirection === $d['code'] ? 'selected' : '' ?>><?= e($d['code']) ?></option>
                <?php endforeach; ?>
            </select>
            <select name="severity">
                <option value="">Все критичности</option>
                <?php foreach (['low', 'medium', 'high', 'critical'] as $s): ?>
                    <option value="<?= $s ?>" <?= $fSeverity === $s ? 'selected' : '' ?>><?= $s ?></option>
                <?php endforeach; ?>
            </select>
            <label style="color:#8c8c99;font-size:12px;"><input type="checkbox" name="openOnly" <?= $fOpenOnly ? 'checked' : '' ?>> только открытые</label>
            <button class="btn btn-secondary" type="submit">Показать</button>
        </form>
        <div>
            <?php if (canEdit()): ?><a class="btn btn-primary" style="text-decoration:none;" href="incident_form.php">+ Новая авария</a><?php endif; ?>
            <a class="btn btn-secondary" style="text-decoration:none;" href="export.php?type=incidents&from=<?= e(urlencode($from)) ?>&to=<?= e(urlencode($to)) ?>">📥 Выгрузить в CSV</a>
        </div>
    </div>

    <?php if ($loadError): ?>
        <div class="alert alert-error"><?= e($loadError) ?></div>
    <?php elseif (!$items): ?>
        <div class="empty-state">За выбранный период аварий нет</div>
    <?php else: ?>
    <table>
        <tr><th>NTT/PW</th><th>Направление</th><th>ТЦ</th><th>Severity</th><th>Сетевой элемент</th><th>Начало</th><th>Окончание</th><th>Длительность</th><?php if (canEdit()): ?><th>Действия</th><?php endif; ?></tr>
        <?php foreach ($items as $i): ?>
            <tr onclick="window.location.href='incident.php?id=<?= (int) $i['incidentId'] ?>'">
                <td><?= e($i['pw'] ?? ('#' . $i['incidentId'])) ?></td>
                <td><?= e($i['directionCode'] ?? '—') ?></td>
                <td><?= e($i['techCenterCode'] ?? '—') ?></td>
                <td><?= severityBadge($i['severity'] ?? null) ?></td>
                <td><?= e($i['servElem'] ?? $i['networkElementName'] ?? '—') ?></td>
                <td><?= e(fmtDate($i['beginTime'] ?? null)) ?></td>
                <td><?= empty($i['endTime']) ? '<span style="color:#e30613;">НВ</span>' : e(fmtDate($i['endTime'])) ?></td>
                <td><?= e($i['lenght'] ?? '—') ?></td>
                <?php if (canEdit()): ?>
                    <td onclick="event.stopPropagation();">
                        <a class="btn-edit" style="text-decoration:none;" href="incident_form.php?id=<?= (int) $i['incidentId'] ?>">✏️</a>
                        <form method="post" style="display:inline;" onsubmit="return confirm('Удалить аварию? Связанные аномалии будут отвязаны.');">
                            <input type="hidden" name="csrf" value="<?= e(csrfToken()) ?>">
                            <input type="hidden" name="action" value="delete">
                            <input type="hidden" name="id" value="<?= (int) $i['incidentId'] ?>">
                            <button class="btn-edit" type="submit">🗑</button>
                        </form>
                    </td>
                <?php endif; ?>
            </tr>
        <?php endforeach; ?>
    </table>
    <div style="text-align:right;margin-top:15px;color:#8c8c99;">Всего: <?= count($items) ?></div>
    <?php endif; ?>
</div>
<?php renderFooter(); ?>
