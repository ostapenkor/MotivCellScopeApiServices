<?php
require __DIR__ . '/../src/bootstrap.php';
requireLogin();
require __DIR__ . '/../src/layout.php';

$from = $_GET['from'] ?? date('Y-m-d\TH:i', strtotime('-24 hours'));
$to   = $_GET['to'] ?? date('Y-m-d\TH:i');
$fromIso = toIso($from);
$toIso   = toIso($to);

$token = authToken();
$summaryRes  = $api->request('GET', '/api/reports/summary?from=' . urlencode((string) $fromIso) . '&to=' . urlencode((string) $toIso), null, $token);
handleUnauthorized($summaryRes);
$incidentsRes = $api->request('GET', '/api/incidents?from=' . urlencode((string) $fromIso) . '&to=' . urlencode((string) $toIso), null, $token);
handleUnauthorized($incidentsRes);

// Дельта к предыдущему равному периоду
$duration = max(1, strtotime((string) $toIso) - strtotime((string) $fromIso));
$prevTo   = date('Y-m-d\TH:i:s\Z', strtotime((string) $fromIso));
$prevFrom = date('Y-m-d\TH:i:s\Z', strtotime((string) $fromIso) - $duration);
$prevRes  = $api->request('GET', '/api/reports/summary?from=' . urlencode($prevFrom) . '&to=' . urlencode($prevTo), null, $token);
handleUnauthorized($prevRes);

$summary  = $summaryRes['body'] ?? [];
$items    = $incidentsRes['body']['items'] ?? [];
$total    = (int) ($summary['total'] ?? count($items));
$prevTotal = (int) ($prevRes['body']['total'] ?? 0);
$delta    = $total - $prevTotal;
$openCount = count(array_filter($items, fn ($i) => empty($i['endTime'])));
$critical = 0;
foreach (($summary['bySeverity'] ?? []) as $row) {
    if (($row['severity'] ?? null) === 'critical') {
        $critical = (int) $row['count'];
    }
}
$loadError = ($summaryRes['status'] !== 200) ? apiErrorText($summaryRes) : null;

renderHeader('Дашборд', 'index');
?>
<div class="card">
    <form class="filter-form" method="get">
        <div class="form-group"><label>Период с</label><input type="datetime-local" name="from" value="<?= e($from) ?>"></div>
        <div class="form-group"><label>по</label><input type="datetime-local" name="to" value="<?= e($to) ?>"></div>
        <button class="btn btn-secondary" type="submit">Показать</button>
        <a class="btn btn-secondary" style="text-decoration:none;" href="index.php">Сутки</a>
    </form>
</div>

<?php if ($loadError): ?>
    <div class="alert alert-error"><?= e($loadError) ?></div>
<?php endif; ?>

<div class="dashboard-grid">
    <div class="kpi-card red"><div class="kpi-title">Всего аварий за период</div><div class="kpi-value"><?= $total ?></div><div class="kpi-sub"><?= $delta >= 0 ? '+' : '' ?><?= $delta ?> к предыдущему периоду</div></div>
    <div class="kpi-card orange"><div class="kpi-title">Открытые (НВ)</div><div class="kpi-value"><?= $openCount ?></div><div class="kpi-sub">Без времени окончания</div></div>
    <div class="kpi-card blue"><div class="kpi-title">Критичные</div><div class="kpi-value"><?= $critical ?></div><div class="kpi-sub">Severity = critical за период</div></div>
</div>

<div class="dashboard-grid">
    <div class="card">
        <div class="card-title">По категориям</div>
        <?php $maxCat = max(1, ...array_map(fn ($r) => (int) $r['count'], $summary['byCategory'] ?? [0])); ?>
        <?php foreach (($summary['byCategory'] ?? []) as $row): ?>
            <div class="bar-row"><span class="bar-label"><?= e($row['category']) ?></span><span class="bar-track"><span class="bar-fill" style="display:block;width:<?= round(100 * $row['count'] / $maxCat) ?>%"></span></span><b><?= (int) $row['count'] ?></b></div>
        <?php endforeach; ?>
        <?php if (empty($summary['byCategory'])): ?><div class="empty-state">Нет данных за период</div><?php endif; ?>
    </div>
    <div class="card">
        <div class="card-title">По критичности</div>
        <?php $maxSev = max(1, ...array_map(fn ($r) => (int) $r['count'], $summary['bySeverity'] ?? [0])); ?>
        <?php foreach (($summary['bySeverity'] ?? []) as $row): ?>
            <div class="bar-row"><span class="bar-label"><?= e($row['severity']) ?></span><span class="bar-track"><span class="bar-fill orange" style="display:block;width:<?= round(100 * $row['count'] / $maxSev) ?>%"></span></span><b><?= (int) $row['count'] ?></b></div>
        <?php endforeach; ?>
        <?php if (empty($summary['bySeverity'])): ?><div class="empty-state">Нет данных за период</div><?php endif; ?>
    </div>
</div>

<div class="card">
    <div class="card-title">Последние инциденты <a href="incidents.php" style="font-size:12px;color:#42a5f5;text-decoration:none;">Все аварии →</a></div>
    <?php if (!$items): ?>
        <div class="empty-state">За выбранный период инцидентов нет</div>
    <?php else: ?>
    <table>
        <tr><th>NTT/PW</th><th>Направление</th><th>Сетевой элемент</th><th>Статус</th><th>Начало</th></tr>
        <?php foreach (array_slice($items, 0, 8) as $i): ?>
            <tr onclick="window.location.href='incident.php?id=<?= (int) $i['incidentId'] ?>'">
                <td><?= e($i['pw'] ?? ('#' . $i['incidentId'])) ?></td>
                <td><?= e($i['directionCode'] ?? '—') ?></td>
                <td><?= e($i['servElem'] ?? $i['networkElementName'] ?? '—') ?></td>
                <td><?= empty($i['endTime']) ? '<span class="badge badge-red">Открыт (НВ)</span>' : '<span class="badge badge-green">Завершён</span>' ?></td>
                <td><?= e(fmtDate($i['beginTime'] ?? null)) ?></td>
            </tr>
        <?php endforeach; ?>
    </table>
    <?php endif; ?>
</div>
<?php renderFooter(); ?>
