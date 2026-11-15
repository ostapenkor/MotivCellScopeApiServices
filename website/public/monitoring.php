<?php
require __DIR__ . '/../src/bootstrap.php';
requireLogin();
require __DIR__ . '/../src/layout.php';

$token = authToken();

// Действия над аномалиями
if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    csrfCheck();
    if (!canEdit()) {
        flash('error', 'Недостаточно прав.');
        header('Location: monitoring.php');
        exit;
    }
    $action = $_POST['action'] ?? '';
    $anomalyId = (int) ($_POST['anomalyId'] ?? 0);
    if ($action === 'explain') {
        $res = $api->request('POST', '/api/anomalies/' . $anomalyId . '/explain', ['incidentId' => (int) $_POST['incidentId']], $token);
    } elseif ($action === 'status') {
        $res = $api->request('PATCH', '/api/anomalies/' . $anomalyId . '/status', ['status' => (string) $_POST['status']], $token);
    } else {
        $res = ['status' => 400, 'body' => ['message' => 'Неизвестное действие'], 'error' => null];
    }
    handleUnauthorized($res);
    if ($res['status'] === 200) {
        flash('success', $action === 'explain' ? 'Аномалия объяснена инцидентом' : 'Статус аномалии обновлён');
    } else {
        flash('error', apiErrorText($res));
    }
    header('Location: monitoring.php?selected=' . $anomalyId);
    exit;
}

$from = $_GET['from'] ?? date('Y-m-d\TH:i', strtotime('-7 days'));
$to   = $_GET['to'] ?? date('Y-m-d\TH:i');
$page = max(1, (int) ($_GET['page'] ?? 1));
$pageSize = 20;
$qs = http_build_query(array_filter([
    'from' => toIso($from), 'to' => toIso($to),
    'severity' => $_GET['severity'] ?? null, 'status' => $_GET['status'] ?? null,
    'type' => $_GET['type'] ?? null, 'kpiCode' => trim((string) ($_GET['kpiCode'] ?? '')) ?: null,
    'regionId' => $_GET['regionId'] ?? null, 'btsId' => $_GET['btsId'] ?? null,
    'page' => $page, 'pageSize' => $pageSize,
], fn ($x) => $x !== null && $x !== ''));

$res = $api->request('GET', '/api/anomalies?' . $qs, null, $token);
handleUnauthorized($res);
$list = $res['body'] ?? [];
$items = $list['items'] ?? [];
$total = (int) ($list['total'] ?? 0);
$loadError = ($res['status'] !== 200) ? apiErrorText($res) : null;

$refsRes = $api->request('GET', '/api/reference/all', null, $token);
handleUnauthorized($refsRes);
$refs = $refsRes['body'] ?? [];

// Выбранная аномалия
$selected = null;
$selectedId = isset($_GET['selected']) ? (int) $_GET['selected'] : (!empty($items) ? (int) $items[0]['anomalyId'] : null);
if ($selectedId) {
    $det = $api->request('GET', '/api/anomalies/' . $selectedId, null, $token);
    handleUnauthorized($det);
    if ($det['status'] === 200) {
        $selected = $det['body'];
    }
}
// Инциденты для формы «Объяснить» (тот же период)
$incidents = [];
if ($selected && canEdit()) {
    $incRes = $api->request('GET', '/api/incidents?from=' . urlencode((string) toIso($from)) . '&to=' . urlencode((string) toIso($to)), null, $token);
    handleUnauthorized($incRes);
    $incidents = $incRes['body']['items'] ?? [];
}

$keepQuery = $_GET;
$buildPage = function (int $p) use ($keepQuery) {
    $keepQuery['page'] = $p;
    return 'monitoring.php?' . http_build_query($keepQuery);
};

renderHeader('Мониторинг', 'monitoring');
?>
<div class="map-container" style="height:auto;align-items:flex-start;">
    <form class="map-sidebar" method="get">
        <div class="card-title" style="margin-bottom:10px;padding-bottom:10px;">Фильтры</div>
        <div class="form-group"><label>Период с</label><input type="datetime-local" name="from" value="<?= e($from) ?>"></div>
        <div class="form-group"><label>Период по</label><input type="datetime-local" name="to" value="<?= e($to) ?>"></div>
        <div class="form-group"><label>Регион</label><select name="regionId"><option value="">Все регионы</option>
            <?php foreach (($refs['regions'] ?? []) as $r): ?><option value="<?= (int) $r['regionId'] ?>" <?= (string) ($_GET['regionId'] ?? '') === (string) $r['regionId'] ? 'selected' : '' ?>><?= e($r['name']) ?></option><?php endforeach; ?>
        </select></div>
        <div class="form-group"><label>Статус</label><select name="status"><option value="">Все статусы</option>
            <?php foreach (['new', 'acknowledged', 'explained', 'resolved', 'false_positive'] as $s): ?><option value="<?= $s ?>" <?= (($_GET['status'] ?? '') === $s) ? 'selected' : '' ?>><?= e(anomalyStatusLabel($s)) ?></option><?php endforeach; ?>
        </select></div>
        <div class="form-group"><label>Severity</label><select name="severity"><option value="">Все</option>
            <?php foreach (['info', 'warning', 'critical'] as $s): ?><option value="<?= $s ?>" <?= (($_GET['severity'] ?? '') === $s) ? 'selected' : '' ?>><?= $s ?></option><?php endforeach; ?>
        </select></div>
        <div class="form-group"><label>Тип</label><select name="type"><option value="">Все типы</option>
            <?php foreach (['threshold', 'zscore', 'missing_data', 'logical', 'cross_source'] as $s): ?><option value="<?= $s ?>" <?= (($_GET['type'] ?? '') === $s) ? 'selected' : '' ?>><?= $s ?></option><?php endforeach; ?>
        </select></div>
        <div class="form-group"><label>KPI</label><input type="text" name="kpiCode" placeholder="cssr" value="<?= e($_GET['kpiCode'] ?? '') ?>"></div>
        <div class="form-group"><label>ID БС</label><input type="number" name="btsId" value="<?= e($_GET['btsId'] ?? '') ?>"></div>
        <button class="btn btn-primary" type="submit" style="width:100%;">Показать</button>
        <a class="btn btn-secondary" style="display:block;text-align:center;text-decoration:none;margin-top:10px;" href="monitoring.php">Сбросить</a>
        <div style="border-top:1px solid #38383f;margin:15px 0;"></div>
        <h4 style="color:#fff;font-size:14px;margin-bottom:10px;">Легенда</h4>
        <div style="font-size:13px;margin-top:8px;">🔴 critical &nbsp; 🟠 warning &nbsp; 🔵 info</div>
    </form>

    <div class="map-table-area">
        <div class="card-title">Список аномалий <span style="font-size:12px;color:#8c8c99;">Всего: <?= $total ?></span></div>
        <?php if ($loadError): ?>
            <div class="alert alert-error"><?= e($loadError) ?></div>
        <?php elseif (!$items): ?>
            <div class="empty-state">Аномалий по заданным условиям нет</div>
        <?php else: ?>
        <table>
            <tr><th>ID</th><th>Время</th><th>БС / Сектор</th><th>KPI</th><th>Severity</th><th>Статус</th></tr>
            <?php foreach ($items as $a): ?>
                <tr <?= $selectedId === (int) $a['anomalyId'] ? 'style="background:rgba(227,6,19,0.1);"' : '' ?> onclick="window.location.href='<?= e('monitoring.php?' . http_build_query(array_merge($_GET, ['selected' => $a['anomalyId']]))) ?>'">
                    <td>#<?= (int) $a['anomalyId'] ?></td>
                    <td><?= e(fmtDate($a['tsFrom'])) ?></td>
                    <td><?= e($a['btsCode'] ?? '—') ?> / <?= e($a['sector'] ?? '—') ?></td>
                    <td><?= e($a['kpiCode'] ?? '—') ?> (<?= e($a['observedValue'] ?? '—') ?>)</td>
                    <td><?= severityBadge($a['severity'] ?? null) ?></td>
                    <td><?= anomalyStatusBadge($a['status'] ?? null) ?></td>
                </tr>
            <?php endforeach; ?>
        </table>
        <div class="pagination">
            <?php if ($page > 1): ?><a href="<?= e($buildPage($page - 1)) ?>">← Назад</a><?php endif; ?>
            <span>Стр. <?= $page ?></span>
            <?php if ($page * $pageSize < $total): ?><a href="<?= e($buildPage($page + 1)) ?>">Вперёд →</a><?php endif; ?>
        </div>
        <?php endif; ?>
    </div>

    <div class="map-details">
        <?php if (!$selected): ?>
            <div class="card-title">Детали аномалии</div>
            <div class="empty-state">Выберите аномалию в списке</div>
        <?php else: ?>
            <div class="card-title">Детали аномалии #<?= (int) $selected['anomalyId'] ?></div>
            <p style="margin-bottom:5px;">БС: <?= e($selected['btsCode'] ?? '—') ?> (Сектор <?= e($selected['sector'] ?? '—') ?>)</p>
            <p style="margin-bottom:5px;">Регион: <?= e($selected['regionCode'] ?? '—') ?> · Соты ID: <?= e($selected['cellId'] ?? '—') ?></p>
            <p style="margin-bottom:15px;">Статус: <?= anomalyStatusBadge($selected['status'] ?? null) ?></p>
            <div style="background:#17171c;padding:10px;border-radius:4px;margin-bottom:10px;">
                <div style="font-size:11px;color:#8c8c99;">Факт (<?= e(mb_strtoupper((string) ($selected['kpiCode'] ?? ''))) ?>)</div>
                <div style="font-size:20px;color:#e30613;font-weight:bold;"><?= e($selected['observedValue'] ?? '—') ?></div>
                <div style="font-size:11px;color:#8c8c99;margin-top:5px;">Ожидание: <?= e($selected['expectedValue'] ?? '—') ?> · Score: <?= e($selected['score'] ?? '—') ?></div>
                <div style="font-size:11px;color:#8c8c99;margin-top:5px;">Период: <?= e(fmtDate($selected['tsFrom'])) ?> — <?= e(fmtDate($selected['tsTo'])) ?></div>
            </div>
            <div style="background:rgba(227,6,19,0.1);padding:10px;border-radius:4px;border:1px solid rgba(227,6,19,0.3);margin-top:15px;">
                <div style="font-size:11px;color:#8c8c99;">Связанный инцидент:</div>
                <?php if (!empty($selected['incident'])): ?>
                    <a href="incident.php?id=<?= (int) $selected['incident']['incidentId'] ?>" style="font-size:13px;color:#e30613;font-weight:bold;"><?= e($selected['incident']['pw'] ?? ('#' . $selected['incident']['incidentId'])) ?></a>
                <?php else: ?>
                    <div style="font-size:13px;color:#8c8c99;font-weight:bold;margin-top:3px;">Не объяснена</div>
                <?php endif; ?>
            </div>

            <?php if (canEdit()): ?>
                <form method="post" style="margin-top:20px;">
                    <input type="hidden" name="csrf" value="<?= e(csrfToken()) ?>">
                    <input type="hidden" name="action" value="explain">
                    <input type="hidden" name="anomalyId" value="<?= (int) $selected['anomalyId'] ?>">
                    <div class="form-group"><label>Объяснить инцидентом</label>
                        <select name="incidentId" required>
                            <option value="">— выберите инцидент периода —</option>
                            <?php foreach ($incidents as $inc): ?><option value="<?= (int) $inc['incidentId'] ?>"><?= e($inc['pw'] ?? ('#' . $inc['incidentId'])) ?> · <?= e($inc['descr'] ?? '') ?></option><?php endforeach; ?>
                        </select>
                    </div>
                    <button class="btn btn-primary" type="submit" style="width:100%;margin-bottom:10px;">Объяснить инцидентом</button>
                </form>
                <form method="post">
                    <input type="hidden" name="csrf" value="<?= e(csrfToken()) ?>">
                    <input type="hidden" name="action" value="status">
                    <input type="hidden" name="anomalyId" value="<?= (int) $selected['anomalyId'] ?>">
                    <div class="form-group"><label>Сменить статус</label>
                        <select name="status">
                            <?php foreach (['new', 'acknowledged', 'explained', 'resolved', 'false_positive'] as $s): ?><option value="<?= $s ?>" <?= ($selected['status'] ?? '') === $s ? 'selected' : '' ?>><?= e(anomalyStatusLabel($s)) ?></option><?php endforeach; ?>
                        </select>
                    </div>
                    <button class="btn btn-secondary" type="submit" style="width:100%;">Сохранить статус</button>
                    <div style="font-size:11px;color:#8c8c99;margin-top:6px;">Статусы «Новая» и «Ложная тревога» сбросят привязку к инциденту.</div>
                </form>
            <?php else: ?>
                <div style="font-size:12px;color:#8c8c99;margin-top:20px;">Действия доступны дежурным и администраторам.</div>
            <?php endif; ?>
        <?php endif; ?>
    </div>
</div>
<?php renderFooter(); ?>
