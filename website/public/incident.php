<?php
require __DIR__ . '/../src/bootstrap.php';
requireLogin();
require __DIR__ . '/../src/layout.php';

$token = authToken();
$id = (int) ($_GET['id'] ?? 0);

$res = $api->request('GET', '/api/incidents/' . $id, null, $token);
handleUnauthorized($res);
if ($res['status'] !== 200) {
    renderHeader('Инцидент', 'incidents');
    echo '<div class="alert alert-error">' . e($res['status'] === 404 ? 'Инцидент не найден' : apiErrorText($res)) . '</div>';
    renderFooter();
    exit;
}
$i = $res['body'];
$anRes = $api->request('GET', '/api/incidents/' . $id . '/anomalies', null, $token);
handleUnauthorized($anRes);
$anomalies = is_array($anRes['body']) ? $anRes['body'] : [];

renderHeader('Инцидент ' . ($i['pw'] ?? ('#' . $id)), 'incidents');
?>
<div class="card">
    <div class="card-title">
        <span><?= e($i['pw'] ?? ('Инцидент #' . $id)) ?> <?= severityBadge($i['severity'] ?? null) ?>
        <?= empty($i['endTime']) ? '<span class="badge badge-red">Открыт (НВ)</span>' : '<span class="badge badge-green">Завершён</span>' ?></span>
        <?php if (canEdit()): ?>
            <span>
                <a class="btn btn-secondary" style="text-decoration:none;" href="incident_form.php?id=<?= $id ?>">Изменить</a>
                <form method="post" action="incidents.php" style="display:inline;" onsubmit="return confirm('Удалить аварию? Связанные аномалии будут отвязаны и вернутся в статус «Новая».');">
                    <input type="hidden" name="csrf" value="<?= e(csrfToken()) ?>">
                    <input type="hidden" name="action" value="delete">
                    <input type="hidden" name="id" value="<?= $id ?>">
                    <button class="btn btn-danger" type="submit">Удалить</button>
                </form>
            </span>
        <?php endif; ?>
    </div>
    <dl class="detail-grid">
        <div><dt>Направление</dt><dd><?= e($i['directionCode'] ?? '—') ?></dd></div>
        <div><dt>ТЦ</dt><dd><?= e($i['techCenterCode'] ?? '—') ?></dd></div>
        <div><dt>Приоритет</dt><dd><?= e($i['priorityCode'] ?? '—') ?></dd></div>
        <div><dt>Тип сети</dt><dd><?= e($i['networkTypeCode'] ?? '—') ?></dd></div>
        <div><dt>Тип оборудования</dt><dd><?= e($i['equipmentTypeCode'] ?? '—') ?></dd></div>
        <div><dt>Тип аварии</dt><dd><?= e($i['alarmTypeCode'] ?? '—') ?></dd></div>
        <div><dt>Сетевой элемент</dt><dd><?= e($i['networkElementName'] ?? '—') ?></dd></div>
        <div><dt>Обслуживаемый элемент</dt><dd><?= e($i['servElem'] ?? '—') ?></dd></div>
        <div><dt>Кол-во БС</dt><dd><?= e($i['numbRbs'] ?? '—') ?></dd></div>
        <div><dt>Категория</dt><dd><?= e($i['category'] ?? '—') ?></dd></div>
        <div><dt>Начало</dt><dd><?= e(fmtDate($i['beginTime'] ?? null, 'd.m.Y H:i')) ?></dd></div>
        <div><dt>Окончание</dt><dd><?= e(empty($i['endTime']) ? 'НВ' : fmtDate($i['endTime'], 'd.m.Y H:i')) ?></dd></div>
        <div><dt>АКБ</dt><dd><?= e($i['akb'] ?? '—') ?></dd></div>
        <div><dt>ДГУ</dt><dd><?= e($i['dgu'] ?? '—') ?></dd></div>
        <div><dt>Простой</dt><dd><?= e($i['prostoy'] ?? '—') ?></dd></div>
        <div><dt>Длительность</dt><dd><?= e($i['lenght'] ?? '—') ?></dd></div>
        <div><dt>Общий простой</dt><dd><?= e($i['commonProstoy'] ?? '—') ?></dd></div>
        <div><dt>Кто выявил</dt><dd><?= e($i['alarmWho'] ?? '—') ?></dd></div>
        <div><dt>Район</dt><dd><?= e($i['adminDistrict'] ?? '—') ?></dd></div>
        <div><dt>Автор (ID)</dt><dd><?= e($i['createdByUserId'] ?? '—') ?></dd></div>
        <div><dt>Создано</dt><dd><?= e(fmtDate($i['createdAt'] ?? null, 'd.m.Y H:i')) ?></dd></div>
        <div><dt>Обновлено</dt><dd><?= e(fmtDate($i['updatedAt'] ?? null, 'd.m.Y H:i')) ?></dd></div>
    </dl>
    <div style="margin-top:10px;"><dt style="font-size:11px;color:#8c8c99;text-transform:uppercase;">Описание</dt><p><?= nl2br(e($i['descr'] ?? '—')) ?></p></div>
    <div style="margin-top:10px;"><dt style="font-size:11px;color:#8c8c99;text-transform:uppercase;">Комментарий</dt><p><?= nl2br(e($i['descNotice'] ?? '—')) ?></p></div>
</div>

<div class="card">
    <div class="card-title">Связанные аномалии</div>
    <?php if (!$anomalies): ?>
        <div class="empty-state">Этим инцидентом аномалии не объяснены</div>
    <?php else: ?>
    <table>
        <tr><th>ID</th><th>БС</th><th>Сектор</th><th>Тип</th><th>KPI</th><th>Severity</th><th>Период</th><th>Факт / ожидание</th><th>Статус</th></tr>
        <?php foreach ($anomalies as $a): ?>
            <tr onclick="window.location.href='monitoring.php?selected=<?= (int) $a['anomalyId'] ?>&from=<?= e(urlencode(date('Y-m-d\TH:i', strtotime($a['tsFrom']) - 86400))) ?>&to=<?= e(urlencode(date('Y-m-d\TH:i', strtotime($a['tsTo']) + 86400))) ?>'">
                <td>#<?= (int) $a['anomalyId'] ?></td><td><?= e($a['btsCode'] ?? '—') ?></td><td><?= e($a['sector'] ?? '—') ?></td>
                <td><?= e($a['anomalyType']) ?></td><td><?= e($a['kpiCode'] ?? '—') ?></td><td><?= severityBadge($a['severity'] ?? null) ?></td>
                <td><?= e(fmtDate($a['tsFrom'])) ?> — <?= e(fmtDate($a['tsTo'])) ?></td>
                <td><?= e($a['observedValue'] ?? '—') ?> / <?= e($a['expectedValue'] ?? '—') ?></td>
                <td><?= anomalyStatusBadge($a['status'] ?? null) ?></td>
            </tr>
        <?php endforeach; ?>
    </table>
    <?php endif; ?>
</div>
<?php renderFooter(); ?>
