<?php
require __DIR__ . '/../src/auth.php';
require __DIR__ . '/../src/layout.php';

renderHeader('Отчетность', 'reports');
?>
<div class="card">
    <div class="table-toolbar">
        <div class="table-filters">
            <input type="date"><span>—</span><input type="date">
            <select><option>Все направления</option><option>СВН</option></select>
            <select><option>Все критичности</option><option>Critical</option></select>
            <button class="btn btn-secondary">Найти</button>
        </div>
        <button class="btn btn-primary" onclick="alert('CSV выгружен!')">📥 Выгрузить в CSV</button>
    </div>
    <table>
        <tr>
            <th>NTT/PW</th><th>Направление</th><th>Severity</th><th>Сетевой элемент</th><th>Начало</th><th>Окончание</th>
            <?php if (!$isOperator): ?><th>Действия</th><?php endif; ?>
        </tr>
        <tr>
            <td>NTT-63587</td><td>ЮЗН</td>
            <td><span class="badge badge-red">Critical</span></td>
            <td>УЗ_044 — БС «Таёжный-044»</td><td>29.08 11:08</td><td>НВ</td>
            <?php if (!$isOperator): ?><td><button class="btn-edit" onclick="alert('Открыть форму редактирования')">✏️</button></td><?php endif; ?>
        </tr>
        <tr>
            <td>NTT-63339</td><td>ХМЯН</td>
            <td><span class="badge badge-green">Low</span></td>
            <td>УЗ_045 — БС «Восточный-045»</td><td>31.08 04:17</td><td>31.08 04:18</td>
            <?php if (!$isOperator): ?><td><button class="btn-edit" onclick="alert('Открыть форму редактирования')">✏️</button></td><?php endif; ?>
        </tr>
    </table>
    <div style="text-align:right; margin-top:15px; color:#8c8c99;">Стр. 1 2 3 ... 10 ></div>
</div>
<?php renderFooter();