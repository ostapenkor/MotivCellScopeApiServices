<?php
require __DIR__ . '/../src/bootstrap.php';
requireLogin();
require __DIR__ . '/../src/layout.php';

if (!canEdit()) {
    renderHeader('Аварии', 'incidents');
    echo '<div class="alert alert-error">Недостаточно прав: создание и редактирование доступно дежурным и администраторам.</div>';
    renderFooter();
    exit;
}

$token = authToken();
$id = isset($_GET['id']) ? (int) $_GET['id'] : null;
$errors = [];
$values = [];

$refsRes = $api->request('GET', '/api/reference/all', null, $token);
handleUnauthorized($refsRes);
$refs = $refsRes['body'] ?? [];

if ($id) {
    $cur = $api->request('GET', '/api/incidents/' . $id, null, $token);
    handleUnauthorized($cur);
    if ($cur['status'] !== 200) {
        renderHeader('Аварии', 'incidents');
        echo '<div class="alert alert-error">Инцидент не найден</div>';
        renderFooter();
        exit;
    }
    $c = $cur['body'];
    $values = [
        'pw' => $c['pw'], 'directionId' => $c['directionId'], 'techCenterId' => $c['techCenterId'],
        'priorityId' => $c['priorityId'], 'networkTypeId' => $c['networkTypeId'],
        'networkElementId' => $c['networkElementId'], 'servElem' => $c['servElem'],
        'numbRbs' => $c['numbRbs'], 'equipmentTypeId' => $c['equipmentTypeId'], 'alarmTypeId' => $c['alarmTypeId'],
        'descr' => $c['descr'], 'beginTime' => fmtDateTimeLocal($c['beginTime']), 'endTime' => fmtDateTimeLocal($c['endTime']),
        'akb' => $c['akb'], 'dgu' => $c['dgu'], 'prostoy' => $c['prostoy'], 'alarmWho' => $c['alarmWho'],
        'descNotice' => $c['descNotice'], 'adminDistrict' => $c['adminDistrict'], 'regionId' => $c['regionId'],
        'severity' => $c['severity'], 'category' => $c['category'],
    ];
}

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    csrfCheck();
    $intOrNull = fn ($key) => (isset($_POST[$key]) && $_POST[$key] !== '') ? (int) $_POST[$key] : null;
    $strOrNull = fn ($key) => (isset($_POST[$key]) && trim((string) $_POST[$key]) !== '') ? trim((string) $_POST[$key]) : null;
    $values = $_POST;
    $payload = [
        'pw' => $strOrNull('pw'), 'directionId' => $intOrNull('directionId'), 'techCenterId' => $intOrNull('techCenterId'),
        'priorityId' => $intOrNull('priorityId'), 'networkTypeId' => $intOrNull('networkTypeId'),
        'networkElementId' => $intOrNull('networkElementId'), 'servElem' => $strOrNull('servElem'),
        'numbRbs' => $strOrNull('numbRbs'), 'equipmentTypeId' => $intOrNull('equipmentTypeId'),
        'alarmTypeId' => $intOrNull('alarmTypeId'), 'descr' => $strOrNull('descr'),
        'beginTime' => toIso($_POST['beginTime'] ?? ''), 'endTime' => toIso($_POST['endTime'] ?? ''),
        'akb' => $strOrNull('akb'), 'dgu' => $strOrNull('dgu'), 'prostoy' => $strOrNull('prostoy'),
        'alarmWho' => $strOrNull('alarmWho'), 'descNotice' => $strOrNull('descNotice'),
        'adminDistrict' => $strOrNull('adminDistrict'), 'regionId' => $intOrNull('regionId'),
        'severity' => $strOrNull('severity') ?? 'medium', 'category' => $strOrNull('category') ?? 'other',
    ];
    if (empty($payload['beginTime'])) {
        $errors['beginTime'] = 'Укажите время начала аварии';
    } else {
        $res = $id
            ? $api->request('PUT', '/api/incidents/' . $id, $payload, $token)
            : $api->request('POST', '/api/incidents', $payload, $token);
        handleUnauthorized($res);
        if (in_array($res['status'], [200, 201], true)) {
            $savedId = $res['body']['incidentId'] ?? $id;
            flash('success', $id ? 'Авария обновлена' : 'Авария создана');
            header('Location: incident.php?id=' . (int) $savedId);
            exit;
        }
        $field = is_array($res['body']) ? ($res['body']['field'] ?? null) : null;
        if ($field) {
            $errors[$field] = apiErrorText($res);
        } else {
            $errors['_'] = apiErrorText($res);
        }
    }
}

$v = fn ($key) => $values[$key] ?? '';
$sel = function ($key, $optionValue) use ($values) {
    return isset($values[$key]) && (string) $values[$key] === (string) $optionValue ? ' selected' : '';
};
$err = function ($key) use ($errors) {
    return isset($errors[$key]) ? '<div class="field-error">' . e($errors[$key]) . '</div>' : '';
};
$refSelect = function (string $name, array $options, string $idKey, string $label) use ($v, $sel, $err) {
    $html = '<div class="form-group"><label>' . e($label) . '</label><select name="' . $name . '"><option value="">—</option>';
    foreach ($options as $opt) {
        $html .= '<option value="' . (int) $opt[$idKey] . '"' . $sel($name, $opt[$idKey]) . '>' . e($opt['code'] ?? $opt['name']) . '</option>';
    }
    return $html . '</select>' . $err($name) . '</div>';
};

renderHeader($id ? 'Редактирование аварии' : 'Новая авария', 'incidents');
?>
<div class="card">
    <div class="card-title"><?= $id ? 'Редактирование аварии #' . $id : 'Регистрация новой аварии' ?></div>
    <?php if ($id): ?><div class="alert alert-error" style="background:rgba(251,140,0,.1);border-color:rgba(251,140,0,.4);color:#fb8c00;">При сохранении незаполненные поля будут сброшены (полная замена).</div><?php endif; ?>
    <?php if (isset($errors['_'])): ?><div class="alert alert-error"><?= e($errors['_']) ?></div><?php endif; ?>

    <form method="post">
        <input type="hidden" name="csrf" value="<?= e(csrfToken()) ?>">
        <div class="card-title" style="font-size:14px;">Основное</div>
        <div class="form-grid">
            <div class="form-group"><label>Номер PW/NTT</label><input type="text" name="pw" maxlength="450" value="<?= e($v('pw')) ?>"><?= $err('pw') ?></div>
            <div class="form-group"><label>ID сетевого элемента</label><input type="number" name="networkElementId" value="<?= e($v('networkElementId')) ?>"><?= $err('networkElementId') ?></div>
            <div class="form-group"><label>Обслуживаемый элемент</label><input type="text" name="servElem" value="<?= e($v('servElem')) ?>"></div>
            <div class="form-group"><label>Кол-во БС</label><input type="text" name="numbRbs" value="<?= e($v('numbRbs')) ?>"></div>
        </div>

        <div class="card-title" style="font-size:14px;margin-top:20px;">Классификация</div>
        <div class="form-grid">
            <?= $refSelect('directionId', $refs['directions'] ?? [], 'directionId', 'Направление') ?>
            <?= $refSelect('techCenterId', $refs['technicalCenters'] ?? [], 'techCenterId', 'ТЦ') ?>
            <?= $refSelect('priorityId', $refs['priorities'] ?? [], 'priorityId', 'Приоритет') ?>
            <?= $refSelect('networkTypeId', $refs['networkTypes'] ?? [], 'networkTypeId', 'Тип сети') ?>
            <?= $refSelect('equipmentTypeId', $refs['equipmentTypes'] ?? [], 'equipmentTypeId', 'Тип оборудования') ?>
            <?= $refSelect('alarmTypeId', $refs['alarmTypes'] ?? [], 'alarmTypeId', 'Тип аварии') ?>
            <?= $refSelect('regionId', $refs['regions'] ?? [], 'regionId', 'Регион') ?>
            <div class="form-group"><label>Категория</label><select name="category">
                <?php foreach (($refs['categories'] ?? []) as $c): ?><option value="<?= e($c['code']) ?>"<?= $sel('category', $c['code']) ?>><?= e($c['label']) ?></option><?php endforeach; ?>
            </select><?= $err('category') ?></div>
            <div class="form-group"><label>Критичность</label><select name="severity">
                <?php foreach ([['code' => 'low', 'label' => 'Низкая'], ['code' => 'medium', 'label' => 'Средняя'], ['code' => 'high', 'label' => 'Высокая'], ['code' => 'critical', 'label' => 'Критическая']] as $s): ?><option value="<?= $s['code'] ?>"<?= $sel('severity', $s['code']) ?>><?= $s['label'] ?></option><?php endforeach; ?>
            </select><?= $err('severity') ?></div>
        </div>

        <div class="card-title" style="font-size:14px;margin-top:20px;">Время и простой</div>
        <div class="form-grid">
            <div class="form-group"><label>Начало аварии *</label><input type="datetime-local" name="beginTime" value="<?= e($v('beginTime')) ?>" required><?= $err('beginTime') ?></div>
            <div class="form-group"><label>Окончание (пусто = продолжается)</label><input type="datetime-local" name="endTime" value="<?= e($v('endTime')) ?>"><?= $err('endTime') ?></div>
            <div class="form-group"><label>АКБ (ЧЧ:ММ:СС)</label><input type="text" name="akb" placeholder="02:00:00" value="<?= e($v('akb')) ?>"><?= $err('akb') ?></div>
            <div class="form-group"><label>ДГУ (ЧЧ:ММ:СС)</label><input type="text" name="dgu" placeholder="01:30:00" value="<?= e($v('dgu')) ?>"><?= $err('dgu') ?></div>
            <div class="form-group"><label>Простой (ЧЧ:ММ:СС)</label><input type="text" name="prostoy" placeholder="03:30:00" value="<?= e($v('prostoy')) ?>"><?= $err('prostoy') ?></div>
        </div>

        <div class="card-title" style="font-size:14px;margin-top:20px;">Описание и уведомления</div>
        <div class="form-group"><label>Описание</label><textarea name="descr" rows="3"><?= e($v('descr')) ?></textarea></div>
        <div class="form-grid" style="margin-top:15px;">
            <div class="form-group"><label>Кто выявил</label><input type="text" name="alarmWho" value="<?= e($v('alarmWho')) ?>"></div>
            <div class="form-group"><label>Район</label><input type="text" name="adminDistrict" value="<?= e($v('adminDistrict')) ?>"></div>
        </div>
        <div class="form-group" style="margin-top:15px;"><label>Комментарий</label><textarea name="descNotice" rows="2"><?= e($v('descNotice')) ?></textarea></div>

        <div style="text-align:right;margin-top:20px;">
            <a class="btn btn-secondary" style="text-decoration:none;" href="<?= $id ? 'incident.php?id=' . $id : 'incidents.php' ?>">Отмена</a>
            <button class="btn btn-primary" type="submit"><?= $id ? 'Сохранить изменения' : 'Сохранить аварию' ?></button>
        </div>
    </form>
</div>
<?php renderFooter(); ?>
