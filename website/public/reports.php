<?php
require __DIR__ . '/../src/bootstrap.php';
requireLogin();
require __DIR__ . '/../src/layout.php';

$token = authToken();
$from = $_GET['from'] ?? date('Y-m-d\TH:i', strtotime('-30 days'));
$to   = $_GET['to'] ?? date('Y-m-d\TH:i');
$fDirection = $_GET['directionCode'] ?? '';
$fSeverity  = $_GET['severity'] ?? '';

$periodsRes = $api->request('GET', '/api/reports/available-periods', null, $token);
handleUnauthorized($periodsRes);
$periods = $periodsRes['body'] ?? [];

$qs = http_build_query(array_filter(['from' => toIso($from), 'to' => toIso($to)]));
$summaryRes = $api->request('GET', '/api/reports/summary?' . $qs, null, $token);
handleUnauthorized($summaryRes);
$summary = $summaryRes['body'] ?? [];
$loadError = ($summaryRes['status'] !== 200) ? apiErrorText($summaryRes) : null;

$incRes = $api->request('GET', '/api/incidents?from=' . urlencode((string) toIso($from)) . '&to=' . urlencode((string) toIso($to)), null, $token);
handleUnauthorized($incRes);
$items = $incRes['body']['items'] ?? [];
// Клиентские фильтры детализации (у API отчётов таких параметров нет)
$items = array_values(array_filter($items, function ($i) use ($fDirection, $fSeverity) {
    if ($fDirection !== '' && ($i['directionCode'] ?? null) !== $fDirection) return false;
    if ($fSeverity !== '' && ($i['severity'] ?? null) !== $fSeverity) return false;
    return true;
}));
$page = max(1, (int) ($_GET['page'] ?? 1));
$pageSize = 20;
$totalRows = count($items);
$pageItems = array_slice($items, ($page - 1) * $pageSize, $pageSize);

$refsRes = $api->request('GET', '/api/reference/all', null, $token);
handleUnauthorized($refsRes);
$refs = $refsRes['body'] ?? [];

renderHeader('Отчетность', 'reports');
?>
<div class="card">
    <form class="filter-form" method="get">
        <div class="form-group"><label>Период с</label><input type="datetime-local" name="from" value="<?= e($from) ?>"></div>
        <div class="form-group"><label>по</label><input type="datetime-local" name="to" value="<?= e($to) ?>"></div>
        <div class="form-group"><label>Направление (детализация)</label><select name="directionCode"><option value="">Все направления</option>
            <?php foreach (($refs['directions'] ?? []) as $d): ?><option value="<?= e($d['code']) ?>" <?= $fDirection === $d['code'] ? 'selected' : '' ?>><?= e($d['code']) ?></option><?php endforeach; ?>
        </select></div>
        <div class="form-group"><label>Критичность (детализация)</label><select name="severity"><option value="">Все критичности</option>
            <?php foreach (['low', 'medium', 'high', 'critical'] as $s): ?><option value="<?= $s ?>" <?= $fSeverity === $s ? 'selected' : '' ?>><?= $s ?></option><?php endforeach; ?>
        </select></div>
        <button class="btn btn-secondary" type="submit">Найти</button>
        <a class="btn btn-primary" style="text-decoration:none;" href="export.php?type=report&from=<?= e(urlencode($from)) ?>&to=<?= e(urlencode($to)) ?>">📥 Отчёт в CSV</a>
        <a class="btn btn-secondary" style="text-decoration:none;" href="export.php?type=incidents&from=<?= e(urlencode($from)) ?>&to=<?= e(urlencode($to)) ?>">📥 Инциденты в CSV</a>
    </form>
    <?php if (!empty($periods['minBeginTime'])): ?>
        <div style="font-size:12px;color:#8c8c99;margin-top:10px;">Данные доступны с <?= e(fmtDate($periods['minBeginTime'], 'd.m.Y')) ?> по <?= e(fmtDate($periods['maxBeginTime'], 'd.m.Y')) ?></div>
    <?php endif; ?>
</div>

<?php if ($loadError): ?><div class="alert alert-error"><?= e($loadError) ?></div><?php endif; ?>

<div class="dashboard-grid">
    <div class="kpi-card red"><div class="kpi-title">Всего инцидентов</div><div class="kpi-value"><?= (int) ($summary['total'] ?? 0) ?></div><div class="kpi-sub">За выбранный период</div></div>
    <div class="card">
        <div class="card-title">По категориям</div>
        <?php $maxCat = max(1, ...array_map(fn ($r) => (int) $r['count'], $summary['byCategory'] ?? [0])); ?>
        <?php foreach (($summary['byCategory'] ?? []) as $row): ?>
            <div class="bar-row"><span class="bar-label"><?= e($row['category']) ?></span><span class="bar-track"><span class="bar-fill" style="display:block;width:<?= round(100 * $row['count'] / $maxCat) ?>%"></span></span><b><?= (int) $row['count'] ?></b></div>
        <?php endforeach; ?>
        <?php if (empty($summary['byCategory'])): ?><div class="empty-state">Нет данных</div><?php endif; ?>
    </div>
    <div class="card">
        <div class="card-title">По критичности</div>
        <?php $maxSev = max(1, ...array_map(fn ($r) => (int) $r['count'], $summary['bySeverity'] ?? [0])); ?>
        <?php foreach (($summary['bySeverity'] ?? []) as $row): ?>
            <div class="bar-row"><span class="bar-label"><?= e($row['severity']) ?></span><span class="bar-track"><span class="bar-fill orange" style="display:block;width:<?= round(100 * $row['count'] / $maxSev) ?>%"></span></span><b><?= (int) $row['count'] ?></b></div>
        <?php endforeach; ?>
        <?php if (empty($summary['bySeverity'])): ?><div class="empty-state">Нет данных</div><?php endif; ?>
    </div>
</div>

<div class="card">
    <div class="card-title">Детализация инцидентов</div>
    <?php if (!$pageItems): ?>
        <div class="empty-state">За период нет данных</div>
    <?php else: ?>
    <table>
        <tr><th>NTT/PW</th><th>Направление</th><th>Severity</th><th>Сетевой элемент</th><th>Начало</th><th>Окончание</th></tr>
        <?php foreach ($pageItems as $i): ?>
            <tr onclick="window.location.href='incident.php?id=<?= (int) $i['incidentId'] ?>'">
                <td><?= e($i['pw'] ?? ('#' . $i['incidentId'])) ?></td><td><?= e($i['directionCode'] ?? '—') ?></td>
                <td><?= severityBadge($i['severity'] ?? null) ?></td>
                <td><?= e($i['servElem'] ?? $i['networkElementName'] ?? '—') ?></td>
                <td><?= e(fmtDate($i['beginTime'] ?? null)) ?></td>
                <td><?= empty($i['endTime']) ? 'НВ' : e(fmtDate($i['endTime'])) ?></td>
            </tr>
        <?php endforeach; ?>
    </table>
    <div class="pagination">
        <?php if ($page > 1): ?><a href="?<?= e(http_build_query(array_merge($_GET, ['page' => $page - 1]))) ?>">← Назад</a><?php endif; ?>
        <span>Стр. <?= $page ?> из <?= max(1, (int) ceil($totalRows / $pageSize)) ?> · Всего: <?= $totalRows ?></span>
        <?php if ($page * $pageSize < $totalRows): ?><a href="?<?= e(http_build_query(array_merge($_GET, ['page' => $page + 1]))) ?>">Вперёд →</a><?php endif; ?>
    </div>
    <?php endif; ?>
</div>
<?php renderFooter(); ?>
